using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuota;
using Forms = System.Windows.Forms;

internal static class TaskbarHostTests
{
    private sealed class ParentWindow : Forms.NativeWindow, IDisposable
    {
        internal ParentWindow(int x, int y) { CreateHandle(new Forms.CreateParams { Caption = "Quota host test parent", X = x, Y = y, Width = 500, Height = 200, Style = unchecked((int)0x80000000) }); }
        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    private static int count;
    private static void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
    private static int RunParentFixture()
    {
        Forms.Application.EnableVisualStyles();
        using (ParentWindow parent_ = new ParentWindow(310, 270))
        using (Forms.Control dispatcher_ = new Forms.Control())
        using (Forms.Timer timeout_ = new Forms.Timer { Interval = 15000 })
        {
            IntPtr dispatcherHandle_ = dispatcher_.Handle;
            Action close_ = delegate { parent_.Dispose(); Forms.Application.ExitThread(); };
            timeout_.Tick += delegate { timeout_.Stop(); close_(); };
            Task.Run(delegate
            {
                try
                {
                    Console.ReadLine();
                    if (!dispatcher_.IsDisposed) dispatcher_.BeginInvoke(close_);
                }
                catch (InvalidOperationException) { }
            });
            timeout_.Start();
            Console.WriteLine(parent_.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
            Forms.Application.Run();
        }
        return 0;
    }
    private static void CheckCrossProcessParent()
    {
        ProcessStartInfo start_ = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--parent-fixture")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using (Process fixture_ = Process.Start(start_))
        using (TaskbarHost host_ = new TaskbarHost())
        {
            try
            {
                Task<string> handleLine_ = fixture_.StandardOutput.ReadLineAsync();
                Check(handleLine_.Wait(5000), "Foreign parent fixture reports its HWND within the timeout.");
                long handleValue_;
                Check(long.TryParse(handleLine_.Result, NumberStyles.Integer, CultureInfo.InvariantCulture, out handleValue_), "Foreign parent fixture returns a valid HWND.");
                IntPtr parent_ = new IntPtr(handleValue_);
                Check(IsWindow(parent_), "Hidden foreign parent is alive.");
                string error_;
                Check(host_.EnsureParent(parent_, out error_), "Child creates under a foreign-process parent: " + error_);
                Check(host_.IsAlive && GetParent(host_.Handle) == parent_, "Foreign parent relationship survives CreateHandle and DPI context checks.");
                Point origin_ = new Point();
                Check(ClientToScreen(parent_, ref origin_), "Foreign parent's client origin is available.");
                Check(host_.SetBounds(origin_.X + 38, origin_.Y + 5, 78, 36, out error_), "Foreign child positioning succeeds: " + error_);
                byte[] pixels_ = new byte[78 * 36 * 4];
                for (int index_ = 0; index_ < pixels_.Length; index_ += 4) { pixels_[index_] = 80; pixels_[index_ + 3] = 160; }
                BitmapSource frame_ = BitmapSource.Create(78, 36, 96, 96, PixelFormats.Pbgra32, null, pixels_, 78 * 4);
                Check(host_.Render(frame_, out error_), "Foreign child presents a per-pixel alpha frame: " + error_);
                Rect actual_;
                Check(GetWindowRect(host_.Handle, out actual_) && actual_.Left == origin_.X + 38 && actual_.Top == origin_.Y + 5, "Foreign child's submitted frame preserves screen coordinates.");
                foreach (int y_ in new[] { 0, 3, -4, 12 })
                {
                    Check(host_.SetBounds(origin_.X + 38, origin_.Y + y_, 78, 36, out error_), "Foreign rendered child's Y update succeeds: " + error_);
                    Check(GetWindowRect(host_.Handle, out actual_) && actual_.Top == origin_.Y + y_, "Foreign rendered child's HWND follows Y " + y_ + ".");
                    Check(host_.Render(frame_, out error_), "Foreign child re-presents after Y move: " + error_);
                    Check(GetWindowRect(host_.Handle, out actual_) && actual_.Top == origin_.Y + y_, "Foreign child's new frame retains moved Y " + y_ + ".");
                }
                host_.SetTooltips("Foreign 5h", "Foreign weekly", 8, 40, 8, 20, true);
                Check(SendMessage(host_.TooltipHandle, 0x040D, IntPtr.Zero, IntPtr.Zero).ToInt32() == 2, "Foreign-parent child retains both native tooltip regions.");
                int clicks_ = 0;
                host_.LeftClick += delegate { clicks_++; };
                SendMessage(host_.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                Check(clicks_ == 1, "Foreign-parent child still delivers its own mouse messages.");
                fixture_.StandardInput.WriteLine("close");
                fixture_.StandardInput.Flush();
                Stopwatch shutdown_ = Stopwatch.StartNew();
                // Destroying a foreign parent can send messages to this thread's
                // child. Pump them instead of blocking the UI thread in WaitForExit.
                while (!fixture_.WaitForExit(10) && shutdown_.ElapsedMilliseconds < 5000) Forms.Application.DoEvents();
                Forms.Application.DoEvents();
                Check(fixture_.HasExited, "Foreign parent shuts down within the timeout.");
                Check(fixture_.ExitCode == 0, "Foreign parent fixture exits cleanly.");
                Check(!IsWindow(parent_) && !host_.IsAlive, "Foreign parent teardown also destroys the owned child.");
            }
            finally
            {
                host_.Dispose();
                if (!fixture_.HasExited)
                {
                    try { fixture_.StandardInput.WriteLine("close"); fixture_.StandardInput.Flush(); } catch (Exception) { }
                    Stopwatch cleanup_ = Stopwatch.StartNew();
                    while (!fixture_.WaitForExit(10) && cleanup_.ElapsedMilliseconds < 1500) Forms.Application.DoEvents();
                    if (!fixture_.HasExited) { fixture_.Kill(); fixture_.WaitForExit(1500); }
                }
            }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--parent-fixture") return RunParentFixture();
            Forms.Application.EnableVisualStyles();
            using (ParentWindow parent_ = new ParentWindow(230, 310))
            using (TaskbarHost host_ = new TaskbarHost())
            {
                string error_;
                Check(!host_.EnsureParent(IntPtr.Zero, out error_) && error_ != null, "Missing parent has an actionable error.");
                Check(host_.EnsureParent(parent_.Handle, out error_), "Child creates under a test parent: " + error_);
                Check(host_.IsAlive && GetParent(host_.Handle) == parent_.Handle, "Child is owned by the requested parent.");
                Check((GetWindowLong(host_.Handle, -16) & 0x40000000) != 0, "Host uses WS_CHILD.");
                Check((GetWindowLong(host_.Handle, -20) & 0x00080000) != 0, "Host uses per-pixel layering.");
                Check((GetWindowLong(host_.Handle, -20) & 0x08000000) != 0, "Host cannot activate its parent.");
                IntPtr original_ = host_.Handle;
                Check(host_.EnsureParent(parent_.Handle, out error_) && original_ == host_.Handle, "Ensuring unchanged parent preserves the HWND.");
                Point origin_ = new Point();
                ClientToScreen(parent_.Handle, ref origin_);
                Check(host_.SetBounds(origin_.X + 40, origin_.Y - 7, 78, 36, out error_), "Negative relative Y is accepted: " + error_);
                Rect actual_;
                GetWindowRect(host_.Handle, out actual_);
                Check(actual_.Left == origin_.X + 40 && actual_.Top == origin_.Y - 7, "Screen positioning maps to the parent client origin.");
                int updates_ = host_.BoundsUpdateCount;
                Check(host_.SetBounds(origin_.X + 40, origin_.Y - 7, 78, 36, out error_) && updates_ == host_.BoundsUpdateCount, "Unchanged bounds do not perform another SetWindowPos.");
                byte[] pixels_ = new byte[78 * 36 * 4];
                for (int index_ = 0; index_ < pixels_.Length; index_ += 4) { pixels_[index_] = 64; pixels_[index_ + 1] = 32; pixels_[index_ + 2] = 16; pixels_[index_ + 3] = 128; }
                BitmapSource frame_ = BitmapSource.Create(78, 36, 96, 96, PixelFormats.Pbgra32, null, pixels_, 78 * 4);
                Check(host_.Render(frame_, out error_), "Premultiplied alpha frame submits: " + error_);
                Check(host_.RenderCount == 1, "Successful frame is recorded once.");
                GetWindowRect(host_.Handle, out actual_);
                Check(actual_.Left == origin_.X + 40 && actual_.Top == origin_.Y - 7, "Frame presentation preserves parent-relative position.");
                foreach (int y_ in new[] { -6, -2, 0, 3, 9, -7 })
                {
                    Check(host_.SetBounds(origin_.X + 40, origin_.Y + y_, 78, 36, out error_), "Rendered child moves to Y " + y_ + ": " + error_);
                    Check(GetWindowRect(host_.Handle, out actual_) && actual_.Top == origin_.Y + y_, "Rendered child's actual HWND follows Y " + y_ + ".");
                    Check(host_.Render(frame_, out error_), "Moved child re-presents frame at Y " + y_ + ": " + error_);
                    Check(GetWindowRect(host_.Handle, out actual_) && actual_.Top == origin_.Y + y_, "Re-presenting retains requested Y " + y_ + ".");
                }
                Check(!host_.Render(null, out error_) && error_ != null, "Null frame fails without crashing.");
                host_.SetTooltips("5h reset\nTomorrow", "Weekly reset\nNext week", 10, 42, 8, 20, true);
                IntPtr tooltip_ = host_.TooltipHandle;
                Check(tooltip_ != IntPtr.Zero && IsWindow(tooltip_), "Native hover tooltip is created.");
                Check(SendMessage(tooltip_, 0x040D, IntPtr.Zero, IntPtr.Zero).ToInt32() == 2, "Both native tooltip regions are registered.");
                host_.SetTooltips("5h updated", "Weekly updated", 10, 42, 8, 20, true);
                Check(tooltip_ == host_.TooltipHandle, "Tooltip updates preserve its native window.");
                host_.SetTooltips(null, "Weekly only", 10, 20, 8, 20, false);
                Check(tooltip_ == host_.TooltipHandle, "Hiding the 5h region preserves tooltip window.");
                Check(SendMessage(tooltip_, 0x040D, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1, "Hidden 5h region is removed without losing weekly hover.");
                int left_ = 0, right_ = 0, destroyed_ = 0;
                host_.LeftClick += delegate { left_++; };
                host_.RightClick += delegate { right_++; };
                host_.Destroyed += delegate { destroyed_++; };
                SendMessage(host_.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                SendMessage(host_.Handle, 0x0205, IntPtr.Zero, IntPtr.Zero);
                Check(left_ == 1 && right_ == 1, "Native mouse clicks reach managed handlers.");
                host_.UseNativeTooltips = false;
                Check(host_.TooltipHandle == IntPtr.Zero && !IsWindow(tooltip_), "Custom hover mode releases the native yellow tooltip window.");
                host_.SetTooltips("5h custom", "Weekly custom", 10, 42, 8, 20, true);
                Check(host_.TooltipHandle == IntPtr.Zero, "Custom hover mode does not recreate native tooltips.");
                int hoverEvents_ = 0, lastHover_ = -9;
                host_.HoverChanged += delegate(int index_) { hoverEvents_++; lastHover_ = index_; };
                host_.HoverChanged += delegate(int index_) { throw new InvalidOperationException("Popup failure fixture"); };
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((12 << 16) | 15));
                Check(host_.CurrentHoverIndex == 0 && lastHover_ == 0 && hoverEvents_ == 1, "Custom hover reports the first ring and contains callback failures.");
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((13 << 16) | 16));
                Check(hoverEvents_ == 1, "Movement inside one ring does not restart custom hover.");
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((12 << 16) | 45));
                Check(host_.CurrentHoverIndex == 1 && lastHover_ == 1 && hoverEvents_ == 2, "Custom hover switches to the weekly ring.");
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((12 << 16) | 0xFFFF));
                Check(host_.CurrentHoverIndex == -1 && lastHover_ == -1, "Signed negative client coordinates leave both hover regions.");
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((12 << 16) | 45));
                SendMessage(host_.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
                Check(host_.CurrentHoverIndex == -1 && lastHover_ == -1, "WM_MOUSELEAVE closes custom hover.");
                host_.SetTooltips(null, "Weekly only custom", 10, 42, 8, 20, false);
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((12 << 16) | 15));
                Check(host_.CurrentHoverIndex == -1, "Hidden five-hour ring cannot trigger hover.");
                SendMessage(host_.Handle, 0x0200, IntPtr.Zero, new IntPtr((12 << 16) | 45));
                SendMessage(host_.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                Check(host_.CurrentHoverIndex == -1, "Click closes custom hover before opening settings.");
                host_.SetVisible(false);
                Check((GetWindowLong(host_.Handle, -16) & 0x10000000) == 0, "Host can hide for fullscreen.");
                host_.SetVisible(true);
                Check((GetWindowLong(host_.Handle, -16) & 0x10000000) != 0, "Host can show without activating.");
                parent_.Dispose();
                Check(!host_.IsAlive && destroyed_ == 1, "Parent teardown notifies host destruction exactly once.");
                Check(!IsWindow(tooltip_), "Owned tooltip is destroyed with the child.");
                using (ParentWindow replacement_ = new ParentWindow(100, 100))
                {
                    Check(host_.EnsureParent(replacement_.Handle, out error_), "Host recreates after parent replacement: " + error_);
                    Check(GetParent(host_.Handle) == replacement_.Handle, "Recreated host attaches to the replacement parent.");
                    Check(host_.Render(frame_, out error_), "Recreated host accepts alpha frames: " + error_);
                    host_.Dispose();
                    Check(!host_.IsAlive && destroyed_ == 1, "Explicit disposal suppresses recursive destruction callbacks.");
                    Check(!host_.EnsureParent(replacement_.Handle, out error_), "Disposed host cannot be recreated.");
                }
            }
            CheckCrossProcessParent();
            Console.WriteLine("PASS: " + count + " native taskbar host checks.");
            return 0;
        }
        catch (Exception exception_) { Console.Error.WriteLine(exception_.ToString()); return 1; }
    }
}
