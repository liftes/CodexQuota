using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace CodexQuota
{
    // An owned, native child window avoids reparenting a WPF Window (and its
    // top-level lifecycle). Explorer owns the parent, never this host's lifetime.
    public sealed class TaskbarHost : IDisposable
    {
        private sealed class HostWindow : Forms.NativeWindow
        {
            private readonly TaskbarHost owner;
            internal HostWindow(TaskbarHost owner) { this.owner = owner; }
            protected override void WndProc(ref Forms.Message message)
            {
                if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
                if (message.Msg == 0x0200) owner.HandleMouseMove(message.LParam);
                if (message.Msg == 0x02A3) owner.HandleMouseLeave();
                if (message.Msg == 0x0202) owner.NotifyClick(false);
                if (message.Msg == 0x0205) owner.NotifyClick(true);
                if (message.Msg == 0x0014) { message.Result = new IntPtr(1); return; }
                if (message.Msg == 0x0002) owner.NotifyDestroyed();
                base.WndProc(ref message);
            }
        }
        private sealed class TooltipSlot
        {
            internal bool Added;
            internal string Text;
            internal IntPtr TextPointer;
            internal Rect Bounds;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Size { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, SizeImage;
            internal int XPelsPerMeter, YPelsPerMeter;
            internal uint ClrUsed, ClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal BitmapHeader Header; internal uint Color; }
        [StructLayout(LayoutKind.Sequential)] private struct CommonControls { internal uint Size, Classes; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseTracking { internal uint Size, Flags; internal IntPtr Window; internal uint HoverTime; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { internal byte Operation, Flags, ConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ToolInfo
        {
            internal uint Size, Flags;
            internal IntPtr Window, Id;
            internal Rect Bounds;
            internal IntPtr Instance, Text, Parameter, Reserved;
        }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, IntPtr destination, ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendToolMessage(IntPtr window, uint message, IntPtr wparam, ref ToolInfo info);
        [DllImport("comctl32.dll")] private static extern bool InitCommonControlsEx(ref CommonControls controls);
        [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref MouseTracking tracking);

        private readonly HostWindow window;
        private readonly TooltipSlot[] tooltips = { new TooltipSlot(), new TooltipSlot() };
        private readonly Rect[] hoverBounds = new Rect[2];
        private bool firstHoverVisible, isTrackingMouse, useNativeTooltips = true;
        private int currentHoverIndex = -1;
        private IntPtr tooltipWindow;
        private bool disposed, suppressDestroyed;
        public event Action LeftClick;
        public event Action RightClick;
        public event Action Destroyed;
        public event Action<int> HoverChanged;
        public TaskbarHost() { window = new HostWindow(this); }
        public IntPtr Handle { get { return window.Handle; } }
        public bool IsAlive { get { return !disposed && window.Handle != IntPtr.Zero && IsWindow(window.Handle); } }
        public int CurrentHoverIndex { get { return currentHoverIndex; } }
        public bool UseNativeTooltips
        {
            get { return useNativeTooltips; }
            set { useNativeTooltips = value; if (!value) CloseTooltip(); }
        }
        internal int BoundsUpdateCount { get; private set; }
        internal int RenderCount { get; private set; }
        internal IntPtr TooltipHandle { get { return tooltipWindow; } }

        public bool EnsureParent(IntPtr parent, out string error)
        {
            error = null;
            if (disposed) { error = "Taskbar host is disposed."; return false; }
            if (parent == IntPtr.Zero || !IsWindow(parent)) { error = "Taskbar parent is unavailable."; return false; }
            if (IsAlive && GetParent(Handle) == parent) return true;
            CloseWindow();
            try
            {
                Forms.CreateParams parameters_ = new Forms.CreateParams();
                parameters_.Caption = "CodexQuota taskbar host";
                parameters_.Parent = parent;
                parameters_.Style = unchecked((int)0x50000000) | 0x04000000; // CHILD | VISIBLE | CLIPSIBLINGS
                parameters_.ExStyle = 0x00080000 | 0x08000000 | 0x00000080; // LAYERED | NOACTIVATE | TOOLWINDOW
                parameters_.Width = 1;
                parameters_.Height = 1;
                window.CreateHandle(parameters_);
                if (!IsAlive || GetParent(Handle) != parent) { error = "Taskbar child creation failed."; CloseWindow(); return false; }
                return true;
            }
            catch (Exception exception_) { error = exception_.Message; CloseWindow(); return false; }
        }

        public bool SetBounds(int screenX, int screenY, int width, int height, out string error)
        {
            error = null;
            if (!IsAlive) { error = "Taskbar host is unavailable."; return false; }
            if (width < 1 || height < 1) { error = "Taskbar host dimensions must be positive."; return false; }
            Point desired_ = new Point { X = screenX, Y = screenY };
            if (!ScreenToClient(GetParent(Handle), ref desired_)) return Fail("Convert taskbar position", out error);
            Rect current_;
            bool same_ = false;
            if (GetWindowRect(Handle, out current_))
            {
                Point currentPoint_ = new Point { X = current_.Left, Y = current_.Top };
                if (ScreenToClient(GetParent(Handle), ref currentPoint_))
                    same_ = currentPoint_.X == desired_.X && currentPoint_.Y == desired_.Y && current_.Right - current_.Left == width && current_.Bottom - current_.Top == height;
            }
            if (same_) return true;
            if (!SetWindowPos(Handle, IntPtr.Zero, desired_.X, desired_.Y, width, height, 0x0010)) return Fail("Position taskbar child", out error);
            BoundsUpdateCount++;
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (!visible) UpdateHover(-1);
            if (IsAlive && ((GetWindowLong(Handle,-16) & 0x10000000) != 0) != visible) ShowWindow(Handle, visible ? 8 : 0); // SW_SHOWNA or SW_HIDE
            if (!visible && tooltipWindow != IntPtr.Zero) SendMessage(tooltipWindow, 0x041C, IntPtr.Zero, IntPtr.Zero); // TTM_POP
        }

        public void RaiseWithinTaskbar()
        {
            if (IsAlive) SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0010 | 0x0001 | 0x0002);
        }

        public bool Render(BitmapSource bitmap, out string error)
        {
            error = null;
            if (!IsAlive) { error = "Taskbar host is unavailable."; return false; }
            if (bitmap == null || bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1 || bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096)
            { error = "Taskbar frame dimensions are invalid."; return false; }
            IntPtr dc_ = IntPtr.Zero, dib_ = IntPtr.Zero, previous_ = IntPtr.Zero;
            try
            {
                BitmapSource frame_ = bitmap;
                if (frame_.Format != PixelFormats.Pbgra32) frame_ = new FormatConvertedBitmap(frame_, PixelFormats.Pbgra32, null, 0);
                int stride_ = checked(frame_.PixelWidth * 4);
                byte[] pixels_ = new byte[checked(stride_ * frame_.PixelHeight)];
                frame_.CopyPixels(pixels_, stride_, 0);
                dc_ = CreateCompatibleDC(IntPtr.Zero);
                if (dc_ == IntPtr.Zero) return Fail("Create taskbar frame DC", out error);
                BitmapInfo info_ = new BitmapInfo { Header = new BitmapHeader { Size = (uint)Marshal.SizeOf(typeof(BitmapHeader)), Width = frame_.PixelWidth, Height = -frame_.PixelHeight, Planes = 1, BitCount = 32, Compression = 0, SizeImage = (uint)pixels_.Length } };
                IntPtr bits_;
                dib_ = CreateDIBSection(dc_, ref info_, 0, out bits_, IntPtr.Zero, 0);
                if (dib_ == IntPtr.Zero || bits_ == IntPtr.Zero) return Fail("Create taskbar frame bitmap", out error);
                Marshal.Copy(pixels_, 0, bits_, pixels_.Length);
                previous_ = SelectObject(dc_, dib_);
                Size size_ = new Size { Width = frame_.PixelWidth, Height = frame_.PixelHeight };
                Point source_ = new Point();
                BlendFunction blend_ = new BlendFunction { Operation = 0, Flags = 0, ConstantAlpha = 255, AlphaFormat = 1 };
                // NULL destination preserves the parent's client coordinates already
                // selected by SetBounds. Never mix SetLayeredWindowAttributes here.
                if (!UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size_, dc_, ref source_, 0, ref blend_, 2)) return Fail("Present taskbar frame", out error);
                RenderCount++;
                return true;
            }
            catch (Exception exception_) { error = exception_.Message; return false; }
            finally
            {
                if (previous_ != IntPtr.Zero && previous_ != new IntPtr(-1)) SelectObject(dc_, previous_);
                if (dib_ != IntPtr.Zero) DeleteObject(dib_);
                if (dc_ != IntPtr.Zero) DeleteDC(dc_);
            }
        }

        public void SetTooltips(string firstText, string secondText, int firstLeft, int secondLeft, int top, int ringSize, bool showFirst)
        {
            if (!IsAlive || ringSize < 1) return;
            hoverBounds[0] = new Rect { Left = firstLeft, Top = top, Right = firstLeft + ringSize, Bottom = top + ringSize };
            hoverBounds[1] = new Rect { Left = secondLeft, Top = top, Right = secondLeft + ringSize, Bottom = top + ringSize };
            firstHoverVisible = showFirst;
            if (!useNativeTooltips) return;
            if (!EnsureTooltip()) return;
            UpdateTooltip(0, firstText, hoverBounds[0], showFirst);
            UpdateTooltip(1, secondText, hoverBounds[1], true);
        }
        private void HandleMouseMove(IntPtr coordinates)
        {
            long raw_ = coordinates.ToInt64();
            int x_ = unchecked((short)(raw_ & 0xFFFF)), y_ = unchecked((short)((raw_ >> 16) & 0xFFFF));
            int index_ = firstHoverVisible && Contains(hoverBounds[0], x_, y_) ? 0 : Contains(hoverBounds[1], x_, y_) ? 1 : -1;
            UpdateHover(index_);
            if (!isTrackingMouse)
            {
                MouseTracking tracking_ = new MouseTracking { Size = (uint)Marshal.SizeOf(typeof(MouseTracking)), Flags = 2, Window = Handle };
                isTrackingMouse = TrackMouseEvent(ref tracking_);
            }
        }
        private static bool Contains(Rect bounds, int x, int y)
        {
            return x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
        }
        private void HandleMouseLeave() { isTrackingMouse = false; UpdateHover(-1); }
        private void UpdateHover(int index)
        {
            if (index == currentHoverIndex) return;
            currentHoverIndex = index;
            Action<int> handler_ = HoverChanged;
            if (handler_ == null) return;
            // Never allow an application popup handler to unwind a native WndProc.
            foreach (Delegate callback_ in handler_.GetInvocationList())
                try { ((Action<int>)callback_)(index); } catch (Exception) { }
        }
        private bool EnsureTooltip()
        {
            if (tooltipWindow != IntPtr.Zero && IsWindow(tooltipWindow)) return true;
            CommonControls controls_ = new CommonControls { Size = (uint)Marshal.SizeOf(typeof(CommonControls)), Classes = 0x000000FF };
            InitCommonControlsEx(ref controls_); // ICC_WIN95_CLASSES includes the tooltip class.
            tooltipWindow = CreateWindowEx(0x00000008, "tooltips_class32", null, 0x80000000 | 0x0001 | 0x0002, 0, 0, 0, 0, Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (tooltipWindow == IntPtr.Zero) return false;
            SendMessage(tooltipWindow, 0x0403, new IntPtr(3), new IntPtr(300));
            SendMessage(tooltipWindow, 0x0403, new IntPtr(1), new IntPtr(100));
            SendMessage(tooltipWindow, 0x0403, new IntPtr(2), new IntPtr(60000));
            SendMessage(tooltipWindow, 0x0418, IntPtr.Zero, new IntPtr(420));
            return true;
        }
        private ToolInfo GetToolInfo(int index, IntPtr text, Rect bounds)
        {
            // TOOLINFO_V2_SIZE works with both system comctl32 v5 and themed v6.
            return new ToolInfo { Size = (uint)Marshal.OffsetOf(typeof(ToolInfo), "Reserved").ToInt32(), Flags = 0x0010, Window = Handle, Id = new IntPtr(index + 1), Bounds = bounds, Text = text };
        }
        private void UpdateTooltip(int index, string text, Rect bounds, bool visible)
        {
            TooltipSlot slot_ = tooltips[index];
            if (!visible)
            {
                if (slot_.Added)
                {
                    ToolInfo removed_ = GetToolInfo(index, slot_.TextPointer, slot_.Bounds);
                    SendToolMessage(tooltipWindow, 0x0433, IntPtr.Zero, ref removed_); // TTM_DELTOOLW
                    slot_.Added = false;
                }
                FreeText(slot_);
                return;
            }
            string text_ = text ?? "";
            bool textChanged_ = slot_.Text != text_ || slot_.TextPointer == IntPtr.Zero;
            bool rectChanged_ = !slot_.Bounds.Equals(bounds);
            IntPtr oldText_ = slot_.TextPointer;
            if (textChanged_) { slot_.TextPointer = Marshal.StringToHGlobalUni(text_); slot_.Text = text_; }
            ToolInfo info_ = GetToolInfo(index, slot_.TextPointer, bounds);
            if (!slot_.Added) slot_.Added = SendToolMessage(tooltipWindow, 0x0432, IntPtr.Zero, ref info_) != IntPtr.Zero; // TTM_ADDTOOLW
            else
            {
                if (textChanged_) SendToolMessage(tooltipWindow, 0x0439, IntPtr.Zero, ref info_); // TTM_UPDATETIPTEXTW
                if (rectChanged_) SendToolMessage(tooltipWindow, 0x0434, IntPtr.Zero, ref info_); // TTM_NEWTOOLRECTW
            }
            slot_.Bounds = bounds;
            if (textChanged_ && oldText_ != IntPtr.Zero) Marshal.FreeHGlobal(oldText_);
        }
        private void FreeText(TooltipSlot slot)
        {
            if (slot.TextPointer != IntPtr.Zero) Marshal.FreeHGlobal(slot.TextPointer);
            slot.TextPointer = IntPtr.Zero;
            slot.Text = null;
        }
        private void CloseTooltip()
        {
            if (tooltipWindow != IntPtr.Zero && IsWindow(tooltipWindow)) DestroyWindow(tooltipWindow);
            tooltipWindow = IntPtr.Zero;
            foreach (TooltipSlot slot_ in tooltips) { slot_.Added = false; FreeText(slot_); }
        }
        private void NotifyClick(bool right)
        {
            UpdateHover(-1);
            Action handler_ = right ? RightClick : LeftClick;
            if (!disposed && handler_ != null) handler_();
        }
        private void NotifyDestroyed()
        {
            HandleMouseLeave();
            CloseTooltip();
            Action handler_ = Destroyed;
            if (!disposed && !suppressDestroyed && handler_ != null) handler_();
        }
        private void CloseWindow()
        {
            HandleMouseLeave();
            suppressDestroyed = true;
            try { CloseTooltip(); if (window.Handle != IntPtr.Zero) window.DestroyHandle(); }
            finally { suppressDestroyed = false; }
        }
        private static bool Fail(string action, out string error)
        {
            int code_ = Marshal.GetLastWin32Error();
            error = action + ": " + new Win32Exception(code_).Message + " (" + code_ + ").";
            return false;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseWindow();
        }
    }
}
