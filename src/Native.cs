using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

namespace CodexQuota
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }
    public sealed class AnchorResult
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Scale { get; set; }
        public double RequestedLeft { get; set; }
        public bool IsXClamped { get; set; }
        public double RequestedTop { get; set; }
        public bool IsYClamped { get; set; }
        public bool IsHidden { get; set; }
        public bool IsFallback { get; set; }
        public bool IsReliable { get; set; }
        public string Description { get; set; }
    }
    public static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        public static void ConfigureOverlay(IntPtr window)
        {
            SetWindowLong(window, -20, GetWindowLong(window, -20) | 0x08000000 | 0x00000080);
        }
        public static void SetPhysicalPosition(IntPtr window, int x, int y)
        {
            NativeRect current_;
            if (GetWindowRect(window, out current_)) SetPhysicalBounds(window, x, y, current_.Width, current_.Height, true);
        }
        public static void SetPhysicalBounds(IntPtr window, int x, int y, int width, int height, bool raise)
        {
            NativeRect current_;
            bool hasBounds_ = GetWindowRect(window, out current_);
            bool samePosition_ = hasBounds_ && current_.Left == x && current_.Top == y;
            bool sameSize_ = hasBounds_ && current_.Width == width && current_.Height == height;
            if (samePosition_ && sameSize_ && !raise) return;
            uint flags_ = 0x0010; // SWP_NOACTIVATE; never hide/show to repair z order.
            if (!raise) flags_ |= 0x0004; // SWP_NOZORDER
            if (samePosition_) flags_ |= 0x0002;
            if (sameSize_) flags_ |= 0x0001;
            SetWindowPos(window, new IntPtr(-1), x, y, width, height, flags_);
        }
        public static void EnableDpiAwareness()
        {
            try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
        }
        public static string WindowClass(IntPtr window)
        {
            StringBuilder name_ = new StringBuilder(256);
            GetClassName(window, name_, name_.Capacity);
            return name_.ToString();
        }
        public static bool IsDesktopWindow(IntPtr window) { return IsShellWindow(window); }
        public static bool IsShellIdentity(string className, string processName)
        {
            string[] classes_ = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "DV2ControlHost", "XamlExplorerHostIslandWindow", "NotifyIconOverflowWindow", "#32768" };
            foreach (string name_ in classes_) if (string.Equals(name_, className, StringComparison.OrdinalIgnoreCase)) return true;
            string[] processes_ = { "explorer", "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellExperienceHost", "InputApp", "TextInputHost", "LockApp", "ShellHost" };
            foreach (string name_ in processes_) if (string.Equals(name_, processName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public static bool IsShellWindow(IntPtr window)
        {
            string class_ = WindowClass(window);
            if (IsShellIdentity(class_, "")) return true;
            uint processId_;
            GetWindowThreadProcessId(window, out processId_);
            try
            {
                using (Process process_ = Process.GetProcessById((int)processId_)) return IsShellIdentity(class_, process_.ProcessName);
            }
            catch (Exception) { return false; }
        }
        public static bool CoversScreen(NativeRect foreground, System.Drawing.Rectangle bounds)
        {
            return foreground.Left <= bounds.Left && foreground.Top <= bounds.Top && foreground.Right >= bounds.Right && foreground.Bottom >= bounds.Bottom;
        }
    }
    public static class TaskbarAnchor
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
        private sealed class StartLayout
        {
            public System.Windows.Rect Start = System.Windows.Rect.Empty;
            public List<System.Windows.Rect> Occupied = new List<System.Windows.Rect>();
        }
        private static readonly object layoutGate = new object();
        private static Task pendingLayout;
        private static StartLayout cachedLayout;
        private static IntPtr cachedTaskbar;
        private static DateTime queriedAt = DateTime.MinValue;
        private static bool invalidated = true;
        public static event Action LayoutChanged;
        public static void InvalidateLayout() { lock (layoutGate) invalidated = true; }
        private static StartLayout ReadLayout(IntPtr taskbar)
        {
            lock (layoutGate)
            {
                if (taskbar != cachedTaskbar) { cachedLayout = null; cachedTaskbar = taskbar; invalidated = true; }
                if ((pendingLayout == null || pendingLayout.IsCompleted) && (invalidated || DateTime.UtcNow - queriedAt > TimeSpan.FromSeconds(2)))
                {
                    invalidated = false;
                    queriedAt = DateTime.UtcNow;
                    pendingLayout = Task.Run(delegate
                    {
                        StartLayout result_ = QueryLayout(taskbar);
                        bool changed_ = false;
                        lock (layoutGate)
                        {
                            if (cachedTaskbar == taskbar && result_ != null && !result_.Start.IsEmpty)
                            {
                                changed_ = cachedLayout == null || !LayoutsEqual(cachedLayout, result_);
                                cachedLayout = result_;
                            }
                        }
                        Action callback_ = LayoutChanged;
                        if (changed_ && callback_ != null) callback_();
                    });
                }
                return cachedLayout;
            }
        }
        private static bool LayoutsEqual(StartLayout first, StartLayout second)
        {
            if (first.Start != second.Start || first.Occupied.Count != second.Occupied.Count) return false;
            for (int index_ = 0; index_ < first.Occupied.Count; index_++) if (first.Occupied[index_] != second.Occupied[index_]) return false;
            return true;
        }
        internal static int GetStartCandidateScore(string automationId, string name)
        {
            if (string.Equals(automationId, "StartButton", StringComparison.OrdinalIgnoreCase)) return 2;
            return name == "开始" || name == "Start" || name == "开始按钮" ? 1 : 0;
        }
        private static System.Windows.Rect ReadNativeStart(IntPtr taskbar)
        {
            // Some shell versions expose a real Start child HWND. Read it on every positioning
            // event so centered taskbar movement does not wait for the asynchronous UIA cache.
            IntPtr start_ = FindWindowEx(taskbar, IntPtr.Zero, "Start", null);
            NativeRect rect_;
            NativeRect bar_;
            if (start_ != IntPtr.Zero && NativeMethods.GetWindowRect(start_, out rect_) && NativeMethods.GetWindowRect(taskbar, out bar_)
                && rect_.Width > 0 && rect_.Height > 0 && rect_.Left >= bar_.Left && rect_.Right <= bar_.Right
                && rect_.Top >= bar_.Top && rect_.Bottom <= bar_.Bottom)
                return new System.Windows.Rect(rect_.Left, rect_.Top, rect_.Width, rect_.Height);
            return System.Windows.Rect.Empty;
        }
        private static StartLayout QueryLayout(IntPtr taskbar)
        {
            StartLayout layout_ = new StartLayout();
            int startScore_ = 0;
            try
            {
                AutomationElement root_ = AutomationElement.FromHandle(taskbar);
                // Win11 Start is not consistently exposed as ControlType.Button across shell versions.
                AutomationElementCollection controls_ = root_.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement control_ in controls_)
                {
                    AutomationElement.AutomationElementInformation information_ = control_.Current;
                    string id_ = information_.AutomationId;
                    string name_ = information_.Name;
                    System.Windows.Rect rect_ = information_.BoundingRectangle;
                    if (information_.IsOffscreen || rect_.IsEmpty || rect_.Width <= 0 || rect_.Height <= 0) continue;
                    int candidateScore_ = GetStartCandidateScore(id_, name_);
                    if (candidateScore_ > 0)
                    {
                        // A localized text child must not replace the actual StartButton rectangle.
                        if (candidateScore_ > startScore_) { layout_.Start = rect_; startScore_ = candidateScore_; }
                    }
                    else if (information_.ControlType == ControlType.Button || information_.ControlType == ControlType.CheckBox || information_.ControlType == ControlType.SplitButton || information_.ControlType == ControlType.ListItem)
                        layout_.Occupied.Add(rect_);
                }
            }
            catch (Exception) { return null; }
            System.Windows.Rect nativeStart_ = ReadNativeStart(taskbar);
            if (!nativeStart_.IsEmpty) layout_.Start = nativeStart_;
            return layout_;
        }
        public static AnchorResult Locate(double widthDip, string mode, double offsetX, double offsetY)
        {
            return Locate(widthDip, 36, 20, mode, offsetX, offsetY, true);
        }
        public static AnchorResult Locate(double widthDip, double heightDip, double ringSizeDip, string mode, double offsetX, double offsetY, bool hideFullscreen, double ringGapDip = 6, int ringCount = 0)
        {
            NativeMethods.EnableDpiAwareness();
            Forms.Screen screen_ = Forms.Screen.PrimaryScreen;
            if (screen_ == null) return new AnchorResult { IsReliable = false, Description = "当前没有可用显示器，保留位置" };
            IntPtr taskbar_ = NativeMethods.FindWindow("Shell_TrayWnd", null);
            NativeRect bar_;
            if (taskbar_ == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar_, out bar_))
                return new AnchorResult { IsReliable = false, Description = "等待任务栏恢复，保留位置" };
            double scale_ = 1;
            try { uint dpi_ = NativeMethods.GetDpiForWindow(taskbar_); if (dpi_ > 0) scale_ = dpi_ / 96.0; } catch (EntryPointNotFoundException) { }
            System.Drawing.Rectangle bounds_ = screen_.Bounds;
            bool barHidden_ = bar_.Top >= bounds_.Bottom - 2 || bar_.Bottom <= bounds_.Top + 2 || bar_.Height <= 2;
            if (barHidden_) return new AnchorResult { IsReliable = true, IsHidden = true, Description = "任务栏自动隐藏中" };
            IntPtr foregroundWindow_ = NativeMethods.GetForegroundWindow();
            NativeRect foreground_;
            if (hideFullscreen && foregroundWindow_ != IntPtr.Zero && !NativeMethods.IsShellWindow(foregroundWindow_) && NativeMethods.GetWindowRect(foregroundWindow_, out foreground_) && NativeMethods.CoversScreen(foreground_, bounds_))
                return new AnchorResult { IsReliable = true, IsHidden = true, Description = "全屏应用中隐藏" };
            StartLayout layout_ = ReadLayout(taskbar_);
            System.Windows.Rect start_ = ReadNativeStart(taskbar_);
            if (start_.IsEmpty && layout_ != null) start_ = layout_.Start;
            if (start_.IsEmpty)
                return new AnchorResult { IsReliable = false, Description = "正在读取开始按钮位置，保留位置" };
            return ComputeGeometry(bounds_, bar_, start_, layout_ == null ? null : layout_.Occupied, widthDip, heightDip, ringSizeDip, scale_, mode, offsetX, offsetY, ringGapDip, ringCount);
        }
        public static AnchorResult ComputeGeometry(System.Drawing.Rectangle bounds, NativeRect bar, System.Windows.Rect start, IList<System.Windows.Rect> occupied, double widthDip, double heightDip, double ringSizeDip, double scale, string mode, double offsetX, double offsetY, double ringGapDip = 6, int ringCount = 0)
        {
            double width_ = widthDip * scale;
            double height_ = heightDip * scale;
            int ringCount_ = ringCount <= 0 ? (widthDip > 50 ? 2 : 1) : Math.Max(1, Math.Min(2, ringCount));
            double visibleWidth_ = Math.Min(width_, (ringCount_ * ringSizeDip + (ringCount_ - 1) * Math.Max(0, ringGapDip)) * scale);
            double visibleHeight_ = Math.Min(height_, ringSizeDip * scale);
            double baseX_ = start.Left - width_ - 7 * scale;
            // Align the visible rings with Start itself. The outer taskbar rectangle can include
            // bottom padding, an auto-hide strip or a differently sized XAML content area.
            double baseY_ = start.Top + (start.Height - height_) / 2;
            // Automatic collision handling selects one baseline before either manual offset is
            // applied. Otherwise crossing a button while dragging X switches to a different
            // baseline, making numeric offsets appear to jump or move in the wrong direction.
            System.Windows.Rect visible_ = new System.Windows.Rect(baseX_ + (width_ - visibleWidth_) / 2, baseY_ + (height_ - visibleHeight_) / 2, visibleWidth_, visibleHeight_);
            bool fallback_ = mode == "Above" || bar.Width < bar.Height || visible_.Left < bounds.Left || visible_.Right > bounds.Right || visible_.IntersectsWith(start);
            if (!fallback_ && occupied != null)
                foreach (System.Windows.Rect occupied_ in occupied) if (visible_.IntersectsWith(occupied_)) { fallback_ = true; break; }
            if (fallback_)
            {
                baseX_ = start.Left;
                if (bar.Width < bar.Height)
                {
                    bool leftBar_ = bar.Left + bar.Width / 2.0 < bounds.Left + bounds.Width / 2.0;
                    baseX_ = leftBar_ ? bar.Right + 6 * scale : bar.Left - width_ - 6 * scale;
                }
                else
                {
                    bool topBar_ = bar.Top + bar.Height / 2.0 < bounds.Top + bounds.Height / 2.0;
                    baseY_ = topBar_ ? bar.Bottom + 6 * scale : bar.Top - height_ - 6 * scale;
                }
            }
            double transparentLeft_ = (width_ - visibleWidth_) / 2;
            double requestedLeft_ = baseX_ + offsetX * scale;
            double x_ = Math.Max(bounds.Left - transparentLeft_, Math.Min(bounds.Right - transparentLeft_ - visibleWidth_, requestedLeft_));
            // Clamp the painted rings, not the 36-DIP transparent HWND. Otherwise
            // bottom-taskbar offsets reach the screen edge while the rings still
            // have room to move, making several different Y settings look identical.
            double transparentTop_ = (height_ - visibleHeight_) / 2;
            double requestedTop_ = baseY_ + offsetY * scale;
            double y_ = Math.Max(bounds.Top - transparentTop_, Math.Min(bounds.Bottom - transparentTop_ - visibleHeight_, requestedTop_));
            return new AnchorResult { Left = x_, Top = y_, RequestedLeft = requestedLeft_, IsXClamped = Math.Abs(x_ - requestedLeft_) > 0.5, RequestedTop = requestedTop_, IsYClamped = Math.Abs(y_ - requestedTop_) > 0.5, Width = width_, Height = height_, Scale = scale, IsReliable = true, IsFallback = fallback_, Description = fallback_ ? "任务栏外侧（空间不足或手动选择）" : "开始按钮左侧" };
        }
    }
}
