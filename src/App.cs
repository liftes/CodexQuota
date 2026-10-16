using System;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("CodexQuota")]
[assembly: AssemblyDescription("Local Codex quota rings for Windows 11")]
[assembly: AssemblyVersion("0.7.0.0")]
[assembly: AssemblyFileVersion("0.7.0.0")]

namespace CodexQuota
{
    public sealed class QuotaApplication : Application
    {
        private UserSettings settings;
        private QuotaSnapshot snapshot;
        private CodexRpcClient rpcClient;
        private Window overlay;
        private Window details;
        private RingControl fiveHourRing;
        private RingControl weeklyRing;
        private ToolTip fiveHourTooltip;
        private ToolTip weeklyTooltip;
        private RingControl fiveHourPreview;
        private RingControl weeklyPreview;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon appIcon;
        private TaskbarHost taskbarHost;
        private QuotaHoverPresenter hoverPresenter;
        private HoverDesignControls hoverDesign;
        private int nativeHoverIndex = -1;
        private Rect lastTaskbarBounds = Rect.Empty;
        private StartupRegistrationService startupRegistration;
        private double hostScale;
        private int hostWidth, hostHeight;
        private DateTime retryAttachmentAt;
        private string attachmentError;
        private DispatcherTimer refreshTimer;
        private DispatcherTimer clockTimer;
        private DispatcherTimer positionDebounce;
        private DispatcherTimer recoveryTimer;
        private ShellWatcher shellWatcher;
        private QuotaPollScheduler pollScheduler;
        private bool pendingPosition;
        private bool needsZOrderRefresh;
        private uint taskbarCreatedMessage;
        private DateTimeOffset lastActivityCheck;
        private bool isRefreshing;
        private bool isLocating;
        private bool isClosing;
        private bool lastRefreshFailed;
        private string status = "正在连接 Codex…";
        private string anchorStatus = "正在定位任务栏…";
        private string resolvedCodexPath;
        private TextBlock accountText, fiveHourText, weeklyText, updateText, stateText, anchorText;
        private bool isLight;
        private bool isRendering;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);

        [STAThread]
        public static int Main(string[] args)
        {
            NativeMethods.EnableDpiAwareness();
            if (args.Length == 2 && args[0] == "--probe") return Probe(args[1]);
            if (args.Length == 2 && args[0] == "--render-preview") { RenderPreview(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--render-settings") { RenderSettings(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--render-settings-dark") { RenderSettings(args[1],"Dark"); return 0; }
            if (args.Length == 2 && args[0] == "--render-hover-design") { RenderSettings(args[1],"Light",true); return 0; }
            if (args.Length == 2 && args[0] == "--render-hover-design-dark") { RenderSettings(args[1],"Dark",true); return 0; }
            bool isSmoke_ = args.Length == 2 && args[0] == "--smoke-ui";
            bool isNew_;
            using (Mutex mutex_ = new Mutex(true, isSmoke_ ? "Local\\CodexQuotaSmoke" : "Local\\CodexQuotaV01", out isNew_))
            {
                if (!isNew_) { MessageBox.Show("CodexQuota 已在运行，请从任务栏圆环或系统托盘打开。", "CodexQuota"); return 0; }
                QuotaApplication application_ = new QuotaApplication();
                application_.isRendering = isSmoke_;
                application_.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                application_.Startup += application_.Start;
                application_.Exit += application_.Stop;
                if (args.Length == 2 && args[0] == "--smoke-ui")
                {
                    DispatcherTimer smokeTimer_ = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    smokeTimer_.Tick += delegate
                    {
                        smokeTimer_.Stop();
                        File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new {
                            ApplicationStarted = true, OverlayCreated = application_.overlay != null,
                            TrayCreated = application_.tray != null, AnchorStatus = application_.anchorStatus,
                            QuotaAvailable = application_.snapshot != null }), new UTF8Encoding(false));
                        application_.Shutdown();
                    };
                    application_.Startup += delegate { smokeTimer_.Start(); };
                }
                application_.DispatcherUnhandledException += delegate(object sender_, DispatcherUnhandledExceptionEventArgs event_)
                {
                    event_.Handled = true;
                    MessageBox.Show("遇到界面错误。请退出后重新运行 CodexQuota。", "CodexQuota");
                    application_.Shutdown(1);
                };
                application_.Run();
                return 0;
            }
        }
        private async void Start(object sender, StartupEventArgs eventArgs)
        {
            settings = UserSettings.Load();
            UiTheme.Apply(settings.ThemeMode); isLight = UiTheme.IsLight;
            taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            pollScheduler = new QuotaPollScheduler(settings.RefreshSeconds,settings.AdaptivePolling,DateTimeOffset.UtcNow);
            positionDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            positionDebounce.Tick += async delegate { positionDebounce.Stop(); await RepositionAsync(); };
            CreateOverlay(); CreateTray();
            shellWatcher = new ShellWatcher();
            shellWatcher.Changed += OnShellChanged;
            TaskbarAnchor.LayoutChanged += OnAnchorLayoutChanged;
            shellWatcher.Start();
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            refreshTimer.Tick += async delegate { ObserveCodexActivity(); if (pollScheduler.IsDue(DateTimeOffset.UtcNow)) await RefreshAsync(); };
            refreshTimer.Start();
            clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clockTimer.Tick += delegate { TickLocalUi(); };
            clockTimer.Start();
            // Placement checks never query Codex or wait for quota updates.
            recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            recoveryTimer.Tick += delegate { shellWatcher.RefreshTaskbar(); RequestPositionUpdate(false); };
            recoveryTimer.Start();
            await RepositionAsync(); await RefreshAsync();
        }
        private void CreateOverlay()
        {
            hoverPresenter = new QuotaHoverPresenter();
            hoverPresenter.SetAppearance(settings.GetHoverAppearance());
            overlay = new Window { Title = "CodexQuota", Width = 78, Height = 36, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, Topmost = true, Opacity = isRendering ? 0 : 1 };
            StackPanel rings_ = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            fiveHourRing = new RingControl { Label = "5h", Margin = new Thickness(3, 0, 3, 0) };
            weeklyRing = new RingControl { Label = "周", Margin = new Thickness(3, 0, 3, 0) };
            fiveHourTooltip = CreateRingTooltip(fiveHourRing);
            weeklyTooltip = CreateRingTooltip(weeklyRing);
            rings_.Children.Add(fiveHourRing); rings_.Children.Add(weeklyRing);
            overlay.Content = rings_;
            overlay.SourceInitialized += delegate
            {
                IntPtr handle_ = new WindowInteropHelper(overlay).Handle;
                NativeMethods.ConfigureOverlay(handle_);
                HwndSource.FromHwnd(handle_).AddHook(OverlayMessage);
            };
            overlay.MouseLeftButtonUp += delegate { ShowDetails(); };
            overlay.MouseRightButtonUp += delegate { tray.ContextMenuStrip.Show(Forms.Cursor.Position); };
            overlay.Closing += delegate(object sender_, System.ComponentModel.CancelEventArgs event_) { if (!isClosing) { event_.Cancel = true; overlay.Hide(); } };
            new WindowInteropHelper(overlay).EnsureHandle();
            PaintRings();
        }
        private void CreateTray()
        {
            using (Stream iconStream_ = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexQuota.AppIcon"))
                appIcon = new System.Drawing.Icon(iconStream_, 32, 32);
            tray = new Forms.NotifyIcon { Icon = appIcon, Text = "CodexQuota · 正在读取额度", Visible = !isRendering };
            Forms.ContextMenuStrip menu_ = new Forms.ContextMenuStrip();
            menu_.Items.Add("查看额度 / 设置", null, delegate { Dispatcher.BeginInvoke(new Action(ShowDetails)); });
            menu_.Items.Add("立即刷新", null, delegate { Dispatcher.BeginInvoke(new Action(async delegate { await RefreshAsync(); })); });
            menu_.Items.Add(new Forms.ToolStripSeparator());
            menu_.Items.Add("恢复开始按钮左侧显示", null, delegate { Dispatcher.BeginInvoke(new Action(async delegate { settings.PositionMode = "StartLeft"; settings.OffsetX = settings.OffsetY = 0; SaveSettings(); await RepositionAsync(); })); });
            menu_.Items.Add("退出", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { Shutdown(); })); });
            tray.ContextMenuStrip = menu_;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowDetails)); };
        }
        private bool ShowFiveHour()
        {
            return settings.FiveHourMode == "Show" || (settings.FiveHourMode == "Auto" && (snapshot == null || snapshot.FiveHour != null));
        }
        private void OnAnchorLayoutChanged() { RequestPositionUpdate(false); }
        private void OnShellChanged()
        {
            RequestPositionUpdate(true);
        }
        private void ObserveCodexActivity()
        {
            if (isClosing || pollScheduler == null || (DateTimeOffset.UtcNow - lastActivityCheck).TotalSeconds < 5) return;
            lastActivityCheck = DateTimeOffset.UtcNow;
            try
            {
                uint processId_;
                NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(),out processId_);
                using (Process process_ = Process.GetProcessById((int)processId_))
                {
                    string name_ = process_.ProcessName.ToLowerInvariant();
                    if (name_ == "codex" || name_ == "code" || name_ == "windowsterminal") pollScheduler.MarkActivity(DateTimeOffset.UtcNow);
                }
            }
            catch { }
        }
        private void RequestPositionUpdate(bool raise)
        {
            if (isClosing || positionDebounce == null) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(delegate { RequestPositionUpdate(raise); })); return; }
            needsZOrderRefresh = needsZOrderRefresh || raise;
            if (!positionDebounce.IsEnabled) positionDebounce.Start();
        }
        private IntPtr OverlayMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (settings == null || isClosing) return IntPtr.Zero;
            if (message == 0x031A || message == 0x001A || message == 0x0015)
            {
                UiTheme.Apply(settings.ThemeMode); isLight = UiTheme.IsLight;
                if (hoverDesign != null) hoverDesign.RefreshPreview();
            }
            if (message == 0x007E || message == 0x02E0 || message == 0x031A || message == 0x001A || (uint)message == taskbarCreatedMessage)
            {
                if (shellWatcher != null) shellWatcher.RefreshTaskbar();
                TaskbarAnchor.InvalidateLayout(); RequestPositionUpdate(true);
            }
            return IntPtr.Zero;
        }
        private void TickLocalUi()
        {
            if (isClosing) return;
            DateTimeOffset now_ = DateTimeOffset.UtcNow;
            bool stale_ = IsStale();
            foreach (RingControl ring_ in new[] { fiveHourRing,weeklyRing,fiveHourPreview,weeklyPreview })
            {
                if (ring_ == null) continue;
                bool staleChanged_ = ring_.IsStale != stale_;
                ring_.NowUtc = now_; ring_.IsStale = stale_;
                if (ring_.IsVisible && (staleChanged_ || settings.ShowResetRing || settings.CenterDisplay == "ResetPercent")) ring_.InvalidateVisual();
            }
            if (fiveHourTooltip != null && fiveHourTooltip.IsOpen) fiveHourTooltip.Content = QuotaHoverPresenter.BuildContent("五小时额度",fiveHourRing.Quota,stale_,now_,settings.GetHoverAppearance());
            if (weeklyTooltip != null && weeklyTooltip.IsOpen) weeklyTooltip.Content = QuotaHoverPresenter.BuildContent("周额度",weeklyRing.Quota,stale_,now_,settings.GetHoverAppearance());
            UpdateNativeHover();
            if (details != null && details.IsVisible) UpdateDetailText();
            PaintTaskbarFrame();
        }
        private bool IsStale()
        {
            return snapshot != null && (lastRefreshFailed || (pollScheduler == null ? (DateTimeOffset.UtcNow - snapshot.UpdatedAt).TotalSeconds > settings.RefreshSeconds * 2 + 15 : DateTimeOffset.UtcNow > pollScheduler.NextDueAt.AddSeconds(Math.Max(15,settings.RefreshSeconds))));
        }
        private void PaintRings()
        {
            bool showFive_ = ShowFiveHour();
            fiveHourRing.Visibility = showFive_ ? Visibility.Visible : Visibility.Collapsed;
            double width_ = RingLayout.WindowWidth(settings.RingSize,settings.RingGap,showFive_ ? 2 : 1);
            fiveHourRing.Margin = new Thickness(0,0,settings.RingGap,0);
            weeklyRing.Margin = new Thickness(0);
            if (overlay.Width != width_) { overlay.Width = width_; RequestPositionUpdate(false); }
            fiveHourRing.Quota = snapshot == null ? null : snapshot.FiveHour;
            weeklyRing.Quota = snapshot == null ? null : snapshot.Weekly;
            ApplyRingAppearance(fiveHourRing, settings.FiveHourColor);
            ApplyRingAppearance(weeklyRing, settings.WeeklyColor);
            fiveHourRing.IsStale = weeklyRing.IsStale = IsStale();
            fiveHourRing.InvalidateVisual(); weeklyRing.InvalidateVisual();
            if (fiveHourTooltip != null) fiveHourTooltip.Content = QuotaHoverPresenter.BuildContent("五小时额度", fiveHourRing.Quota, IsStale(),DateTimeOffset.UtcNow,settings.GetHoverAppearance());
            if (weeklyTooltip != null) weeklyTooltip.Content = QuotaHoverPresenter.BuildContent("周额度", weeklyRing.Quota, IsStale(),DateTimeOffset.UtcNow,settings.GetHoverAppearance());
            UpdateNativeHover();
            PaintAppearancePreview();
            PaintTaskbarFrame();
            if (tray != null)
            {
                string text_ = "CodexQuota · 周 " + ShortQuota(weeklyRing.Quota);
                if (showFive_) text_ += " · 5h " + ShortQuota(fiveHourRing.Quota);
                if (IsStale()) text_ += " · 已过期";
                tray.Text = text_.Length > 63 ? text_.Substring(0, 63) : text_;
            }
        }
        private ToolTip CreateRingTooltip(RingControl ring)
        {
            ToolTip tooltip_ = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Top, PlacementTarget = ring, StaysOpen = true };
            ring.ToolTip = tooltip_;
            ring.ToolTipOpening += delegate { tooltip_.Content = QuotaHoverPresenter.BuildContent(ring.Label == "5h" ? "五小时额度" : "周额度",ring.Quota,ring.IsStale,DateTimeOffset.UtcNow,settings.GetHoverAppearance()); };
            ToolTipService.SetInitialShowDelay(ring, 300);
            ToolTipService.SetBetweenShowDelay(ring, 0);
            ToolTipService.SetShowDuration(ring, 60000);
            ToolTipService.SetShowOnDisabled(ring, true);
            // Use the same edge-aware popup in floating and attached display modes.
            ToolTipService.SetIsEnabled(ring,false);
            ring.MouseEnter += delegate { BeginRingHover(ring.Label == "5h" ? 0 : 1,ring); };
            ring.MouseLeave += delegate { OnNativeHoverChanged(-1); };
            return tooltip_;
        }
        private void ApplyRingAppearance(RingControl ring, string color)
        {
            ring.Width = ring.Height = settings.RingSize;
            ring.RemainingColor = (Color)ColorConverter.ConvertFromString(color);
            ring.TrackColor = (Color)ColorConverter.ConvertFromString(settings.TrackColor);
            ring.TrackOpacity = settings.TrackOpacity;
            ring.CenterDisplay = settings.CenterDisplay;
            ring.ShowResetRing = settings.ShowResetRing;
            ring.ResetRingPlacement = settings.ResetRingPlacement;
            ring.ResetColor = (Color)ColorConverter.ConvertFromString(settings.ResetColor);
            ring.NowUtc = DateTimeOffset.UtcNow;
            ring.InvalidateVisual();
        }
        private void PaintAppearancePreview()
        {
            if (fiveHourPreview == null || weeklyPreview == null) return;
            ApplyRingAppearance(fiveHourPreview, settings.FiveHourColor);
            ApplyRingAppearance(weeklyPreview, settings.WeeklyColor);
            fiveHourPreview.Quota = snapshot == null ? null : snapshot.FiveHour;
            weeklyPreview.Quota = snapshot == null ? null : snapshot.Weekly;
            fiveHourPreview.Visibility = ShowFiveHour() ? Visibility.Visible : Visibility.Collapsed;
            fiveHourPreview.Margin = new Thickness(0,0,settings.RingGap,0);
            weeklyPreview.Margin = new Thickness(0);
        }
        private sealed class DialogOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public DialogOwner(IntPtr handle) { Handle = handle; }
        }
        private void AddColorPicker(Panel panel, string label, Func<string> readColor, Action<string> writeColor)
        {
            DockPanel row_ = new DockPanel { Margin = new Thickness(0,5,0,3) };
            TextBlock name_ = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 125 };
            Button colorButton_ = MakeButton(readColor().Substring(3));
            colorButton_.MinWidth = 110;
            Border swatch_ = new Border { Width = 16, Height = 16, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(readColor())), Margin = new Thickness(0,0,6,0) };
            StackPanel buttonBody_ = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock colorText_ = new TextBlock { Text = "#" + readColor().Substring(3), VerticalAlignment = VerticalAlignment.Center };
            buttonBody_.Children.Add(swatch_); buttonBody_.Children.Add(colorText_); colorButton_.Content = buttonBody_;
            colorButton_.Click += delegate
            {
                using (Forms.ColorDialog picker_ = new Forms.ColorDialog { FullOpen = true, AnyColor = true })
                {
                    Color current_ = (Color)ColorConverter.ConvertFromString(readColor());
                    picker_.Color = System.Drawing.Color.FromArgb(current_.R,current_.G,current_.B);
                    if (picker_.ShowDialog(new DialogOwner(new WindowInteropHelper(details).Handle)) != Forms.DialogResult.OK) return;
                    string chosen_ = Color.FromRgb(picker_.Color.R,picker_.Color.G,picker_.Color.B).ToString(CultureInfo.InvariantCulture);
                    writeColor(chosen_); swatch_.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(chosen_));
                    colorText_.Text = "#" + chosen_.Substring(3); SaveSettings(); PaintRings();
                }
            };
            row_.Children.Add(name_); row_.Children.Add(colorButton_); panel.Children.Add(row_);
        }
        private async Task RefreshAsync()
        {
            if (isRefreshing || isClosing) return;
            isRefreshing = true; status = "正在刷新…"; UpdateDetailText();
            try
            {
                if (rpcClient == null)
                {
                    resolvedCodexPath = CodexLocator.Find(settings.CodexPath);
                    rpcClient = new CodexRpcClient();
                    rpcClient.Start(resolvedCodexPath);
                }
                QuotaSnapshot result_ = await rpcClient.ReadQuotaAsync();
                if (isClosing) return;
                snapshot = result_; lastRefreshFailed = false;
                if (pollScheduler != null) pollScheduler.RecordSuccess(result_,DateTimeOffset.UtcNow);
                status = result_.FiveHour == null && result_.Weekly == null ? "账户未返回可识别的五小时 / 周额度。请检查登录账户及认证方式。" : "已连接 · 显示剩余额度";
            }
            catch (Exception)
            {
                lastRefreshFailed = true;
                if (pollScheduler != null) pollScheduler.RecordFailure(DateTimeOffset.UtcNow);
                status = resolvedCodexPath == null ? "未找到可用的 Codex，请在下方选择官方 codex.exe。" : "读取失败。请检查官方 Codex 是否已登录、网络是否可用。保留上次成功数据。";
                if (rpcClient != null) { rpcClient.Dispose(); rpcClient = null; }
            }
            finally
            {
                isRefreshing = false;
                if (!isClosing) { PaintRings(); UpdateDetailText(); }
            }
        }
        private void ReleaseTaskbarHost()
        {
            if (hoverPresenter != null) hoverPresenter.Close();
            nativeHoverIndex = -1;
            if (taskbarHost == null) return;
            taskbarHost.Dispose(); taskbarHost = null;
            hostWidth = hostHeight = 0;
        }
        private void OnNativeHoverChanged(int index)
        {
            BeginRingHover(index,null);
        }
        private void BeginRingHover(int index, RingControl source)
        {
            nativeHoverIndex = index;
            if (hoverPresenter == null) return;
            if (index < 0) { hoverPresenter.Close(); return; }
            double scale_ = ConfigureHoverAnchor(index,source);
            System.Drawing.Point cursor_ = Forms.Cursor.Position;
            hoverPresenter.ShowAfterDelay(index == 0 ? "五小时额度" : "周额度",index == 0 ? fiveHourRing.Quota : weeklyRing.Quota,IsStale(),cursor_.X,cursor_.Y,scale_);
        }
        private double ConfigureHoverAnchor(int index, RingControl source)
        {
            double scale_ = hostScale > 0 ? hostScale : 1;
            Rect ringBounds_ = Rect.Empty;
            if (source != null && source.ActualWidth > 0)
            {
                Point first_ = source.PointToScreen(new Point(0,0));
                Point last_ = source.PointToScreen(new Point(source.ActualWidth,source.ActualHeight));
                ringBounds_ = new Rect(first_,last_);
                scale_ = ringBounds_.Width / source.ActualWidth;
            }
            else if (taskbarHost != null && taskbarHost.IsAlive)
            {
                NativeRect window_;
                if (NativeMethods.GetWindowRect(taskbarHost.Handle,out window_))
                {
                    int count_ = ShowFiveHour() ? 2 : 1;
                    int slot_ = count_ == 1 ? 0 : index;
                    double x_ = RingLayout.RingLeft(window_.Width / scale_,settings.RingSize,settings.RingGap,count_,slot_) * scale_;
                    ringBounds_ = new Rect(window_.Left + x_,window_.Top + (window_.Height - settings.RingSize * scale_) / 2,settings.RingSize * scale_,settings.RingSize * scale_);
                }
            }
            if (ringBounds_.IsEmpty) return scale_;
            Rect barBounds_ = lastTaskbarBounds;
            NativeRect bar_;
            if (barBounds_.IsEmpty && NativeMethods.GetWindowRect(NativeMethods.FindWindow("Shell_TrayWnd",null),out bar_))
                barBounds_ = new Rect(bar_.Left,bar_.Top,bar_.Width,bar_.Height);
            Forms.Screen screen_ = Forms.Screen.FromPoint(new System.Drawing.Point((int)ringBounds_.Left,(int)ringBounds_.Top));
            System.Drawing.Rectangle bounds_ = screen_.Bounds;
            hoverPresenter.SetAnchor(ringBounds_,barBounds_,new Rect(bounds_.Left,bounds_.Top,bounds_.Width,bounds_.Height),scale_);
            return scale_;
        }
        private void UpdateNativeHover()
        {
            if (hoverPresenter == null || nativeHoverIndex < 0) return;
            ConfigureHoverAnchor(nativeHoverIndex,taskbarHost != null && taskbarHost.IsAlive ? null : nativeHoverIndex == 0 ? fiveHourRing : weeklyRing);
            hoverPresenter.Update(nativeHoverIndex == 0 ? "五小时额度" : "周额度",nativeHoverIndex == 0 ? fiveHourRing.Quota : weeklyRing.Quota,IsStale());
        }
        private bool TryAttachToTaskbar(AnchorResult anchor, IntPtr parent)
        {
            if (isRendering || settings.DisplayMode != "Taskbar" || anchor.IsFallback) return false;
            IntPtr parent_ = parent;
            NativeRect bar_;
            if (parent_ == IntPtr.Zero || !NativeMethods.GetWindowRect(parent_, out bar_)) return false;
            double visibleWidth_ = RingLayout.GroupWidth(settings.RingSize,settings.RingGap,ShowFiveHour() ? 2 : 1) * anchor.Scale;
            double visibleLeft_ = anchor.Left + (anchor.Width - visibleWidth_) / 2;
            double visibleTop_ = anchor.Top + (anchor.Height - settings.RingSize * anchor.Scale) / 2;
            // Native child windows clip to their parent; only painted rings must fit.
            if (visibleLeft_ < bar_.Left || visibleLeft_ + visibleWidth_ > bar_.Right ||
                visibleTop_ < bar_.Top || visibleTop_ + settings.RingSize * anchor.Scale > bar_.Bottom)
            { attachmentError = "偏移超出任务栏，使用悬浮"; return false; }
            if (DateTime.UtcNow < retryAttachmentAt) return false;
            if (taskbarHost == null)
            {
                taskbarHost = new TaskbarHost { UseNativeTooltips = false };
                taskbarHost.HoverChanged += OnNativeHoverChanged;
                taskbarHost.LeftClick += ShowDetails;
                taskbarHost.RightClick += delegate { tray.ContextMenuStrip.Show(Forms.Cursor.Position); };
                TaskbarHost createdHost_ = taskbarHost;
                taskbarHost.Destroyed += delegate
                {
                    if (isClosing || Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (isClosing || !object.ReferenceEquals(taskbarHost,createdHost_)) return;
                        hostWidth = hostHeight = 0; RequestPositionUpdate(true);
                    }));
                };
            }
            string error_;
            IntPtr oldHandle_ = taskbarHost.Handle;
            int width_ = (int)Math.Round(anchor.Width), height_ = (int)Math.Round(anchor.Height);
            if (!taskbarHost.EnsureParent(parent_, out error_) ||
                !taskbarHost.SetBounds((int)Math.Round(anchor.Left), (int)Math.Round(anchor.Top), width_, height_, out error_))
            {
                attachmentError = error_; retryAttachmentAt = DateTime.UtcNow.AddSeconds(5);
                ReleaseTaskbarHost(); return false;
            }
            bool redraw_ = oldHandle_ != taskbarHost.Handle || hostWidth != width_ || hostHeight != height_ || hostScale != anchor.Scale;
            hostWidth = width_; hostHeight = height_; hostScale = anchor.Scale;
            if (redraw_ && !PaintTaskbarFrame())
            {
                retryAttachmentAt = DateTime.UtcNow.AddSeconds(5); ReleaseTaskbarHost(); return false;
            }
            taskbarHost.SetVisible(true);
            if (needsZOrderRefresh) taskbarHost.RaiseWithinTaskbar();
            attachmentError = null;
            return true;
        }
        private bool PaintTaskbarFrame()
        {
            if (taskbarHost == null || !taskbarHost.IsAlive || hostWidth <= 0 || hostHeight <= 0) return false;
            try
            {
                double widthDip_ = hostWidth / hostScale, heightDip_ = hostHeight / hostScale;
                StackPanel row_ = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (ShowFiveHour())
                {
                    RingControl ring_ = new RingControl { Label = "5h", Quota = fiveHourRing.Quota, IsStale = IsStale(), Margin = new Thickness(0,0,settings.RingGap,0) };
                    ApplyRingAppearance(ring_,settings.FiveHourColor); row_.Children.Add(ring_);
                }
                RingControl weekly_ = new RingControl { Label = "周", Quota = weeklyRing.Quota, IsStale = IsStale(), Margin = new Thickness(0) };
                ApplyRingAppearance(weekly_,settings.WeeklyColor); row_.Children.Add(weekly_);
                Border surface_ = new Border { Width = widthDip_, Height = heightDip_, Child = row_ };
                surface_.Measure(new Size(widthDip_,heightDip_)); surface_.Arrange(new Rect(0,0,widthDip_,heightDip_)); surface_.UpdateLayout();
                RenderTargetBitmap frame_ = new RenderTargetBitmap(hostWidth,hostHeight,96 * hostScale,96 * hostScale,PixelFormats.Pbgra32);
                frame_.Render(surface_);
                string error_;
                if (!taskbarHost.Render(frame_,out error_)) { attachmentError = error_; hostWidth = 0; RequestPositionUpdate(false); return false; }
                int ringSize_ = (int)Math.Round(settings.RingSize * hostScale);
                int firstLeft_ = (int)Math.Round(RingLayout.RingLeft(widthDip_,settings.RingSize,settings.RingGap,ShowFiveHour() ? 2 : 1,0) * hostScale);
                int secondLeft_ = ShowFiveHour() ? firstLeft_ + (int)Math.Round((settings.RingSize + settings.RingGap) * hostScale) : firstLeft_;
                taskbarHost.SetTooltips(QuotaDisplayFormatter.FormatHover("五小时额度",fiveHourRing.Quota,IsStale()),
                    QuotaDisplayFormatter.FormatHover("周额度",weeklyRing.Quota,IsStale()),firstLeft_,secondLeft_,(hostHeight - ringSize_) / 2,ringSize_,ShowFiveHour());
                return true;
            }
            catch (Exception exception_) { attachmentError = exception_.Message; hostWidth = 0; RequestPositionUpdate(false); return false; }
        }
        private async Task RepositionAsync()
        {
            if (isClosing) return;
            if (isLocating) { pendingPosition = true; return; }
            if (settings.PositionMode == "Tray") { overlay.Hide(); ReleaseTaskbarHost(); anchorStatus = "仅托盘显示"; return; }
            isLocating = true;
            double width_ = overlay.Width;
            string mode_ = settings.PositionMode;
            double offsetX_ = settings.OffsetX, offsetY_ = settings.OffsetY;
            double ringSize_ = settings.RingSize;
            double ringGap_ = settings.RingGap;
            int ringCount_ = ShowFiveHour() ? 2 : 1;
            bool hideFullscreen_ = settings.HideFullscreen;
            try
            {
                AnchorResult anchor_ = await Task.Run(() => TaskbarAnchor.Locate(width_,36,ringSize_,mode_,offsetX_,offsetY_,hideFullscreen_,ringGap_,ringCount_));
                if (isClosing || settings.PositionMode == "Tray") return;
                ApplyAnchor(anchor_,NativeMethods.FindWindow("Shell_TrayWnd",null));
            }
            catch { anchorStatus = "正在重试定位，保留原位置"; }
            finally { isLocating = false; if (pendingPosition) { pendingPosition = false; RequestPositionUpdate(false); } }
        }
        private void ApplyAnchor(AnchorResult anchor, IntPtr taskbarParent)
        {
            anchorStatus = anchor.Description;
            if (!anchor.IsReliable) return;
            NativeRect barBounds_;
            if (taskbarParent != IntPtr.Zero && NativeMethods.GetWindowRect(taskbarParent,out barBounds_))
                lastTaskbarBounds = new Rect(barBounds_.Left,barBounds_.Top,barBounds_.Width,barBounds_.Height);
            if (anchor.IsHidden)
            {
                if (overlay.IsVisible) overlay.Hide();
                if (taskbarHost != null) taskbarHost.SetVisible(false);
                return;
            }
            if (TryAttachToTaskbar(anchor,taskbarParent))
            {
                if (overlay.IsVisible) overlay.Hide();
                anchorStatus += " · 已附着任务栏";
            }
            else
            {
                ReleaseTaskbarHost();
                bool wasHidden_ = !overlay.IsVisible;
                if (wasHidden_) overlay.Show();
                NativeMethods.SetPhysicalBounds(new WindowInteropHelper(overlay).Handle,(int)Math.Round(anchor.Left),(int)Math.Round(anchor.Top),(int)Math.Round(anchor.Width),(int)Math.Round(anchor.Height),wasHidden_ || needsZOrderRefresh);
                anchorStatus += " · 悬浮" + (settings.DisplayMode == "Taskbar" && !anchor.IsFallback && !string.IsNullOrEmpty(attachmentError) ? "（" + attachmentError + "）" : "");
            }
            anchorStatus += "\nX / Y 偏移 " + settings.OffsetX.ToString("0.#",CultureInfo.InvariantCulture) + " / " + settings.OffsetY.ToString("0.#",CultureInfo.InvariantCulture) + " DIP" + (anchor.IsXClamped || anchor.IsYClamped ? "（已到可见屏幕边界）" : "");
            needsZOrderRefresh = false;
        }
        private void ShowDetails()
        {
            OnNativeHoverChanged(-1);
            if (details != null) { details.Show(); details.Activate(); UpdateDetailText(); return; }
            UiTheme.Apply(settings.ThemeMode); isLight = UiTheme.IsLight;
            double availableWidth_ = Math.Max(280,SystemParameters.WorkArea.Width - 32);
            double availableHeight_ = Math.Max(160,SystemParameters.WorkArea.Height - 60);
            details = new Window { Title = "CodexQuota v0.7 · 额度与设置", Width = Math.Min(800,availableWidth_), Height = Math.Min(720,availableHeight_),
                MinWidth = Math.Min(390,availableWidth_), MaxWidth = availableWidth_, MinHeight = Math.Min(420,availableHeight_), MaxHeight = availableHeight_,
                ResizeMode = ResizeMode.CanResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false, ShowActivated = !isRendering, Opacity = isRendering ? 0 : 1, Topmost = true, Background = new SolidColorBrush(isLight ? Color.FromRgb(246,246,246) : Color.FromRgb(35,35,35)),
                Foreground = isLight ? Brushes.Black : Brushes.Gainsboro, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"), FontSize = 13 };
            using (Stream iconStream_ = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexQuota.AppIcon"))
                details.Icon = BitmapFrame.Create(iconStream_, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            UiTheme.ApplyWindow(details);
            Grid root_ = new Grid { Name = "DetailsLayout", Margin = new Thickness(20) };
            root_.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });
            root_.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid columns_ = new Grid { Name = "DetailsColumns" };
            root_.Children.Add(columns_);
            StackPanel quotaPanel_ = new StackPanel { Margin = new Thickness(0,0,12,12) };
            quotaPanel_.Children.Add(new TextBlock { Text = "Codex 额度", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,4) });
            accountText = AddText(quotaPanel_, "", 12);
            fiveHourText = AddText(quotaPanel_, "", 14);
            weeklyText = AddText(quotaPanel_, "", 14);
            updateText = AddText(quotaPanel_, "", 12);
            stateText = AddText(quotaPanel_, "", 12);
            anchorText = AddText(quotaPanel_, "", 12);
            quotaPanel_.Children.Add(new Separator { Margin = new Thickness(0,16,0,10) });
            AddVerticalAdjustment(quotaPanel_);
            AddHorizontalAdjustment(quotaPanel_);
            ScrollViewer quotaScroll_ = new ScrollViewer { Name = "QuotaScroll", Content = quotaPanel_, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            StackPanel panel_ = new StackPanel { Margin = new Thickness(0,0,12,12) };
            panel_.Children.Add(new TextBlock { Text = "显示与设置", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,4) });
            ScrollViewer settingsScroll_ = new ScrollViewer { Name = "SettingsScroll", Content = panel_, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            columns_.Children.Add(quotaScroll_); columns_.Children.Add(settingsScroll_);
            Action<bool> arrangeColumns_ = delegate(bool narrow_)
            {
                columns_.ColumnDefinitions.Clear(); columns_.RowDefinitions.Clear();
                if (narrow_)
                {
                    columns_.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
                    columns_.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });
                    columns_.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3,GridUnitType.Star) });
                    Grid.SetColumn(quotaScroll_,0); Grid.SetRow(quotaScroll_,0);
                    Grid.SetColumn(settingsScroll_,0); Grid.SetRow(settingsScroll_,1);
                    quotaScroll_.Margin = new Thickness(0,0,0,12); settingsScroll_.Margin = new Thickness(0);
                }
                else
                {
                    columns_.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
                    columns_.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
                    columns_.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });
                    Grid.SetColumn(quotaScroll_,0); Grid.SetRow(quotaScroll_,0);
                    Grid.SetColumn(settingsScroll_,1); Grid.SetRow(settingsScroll_,0);
                    quotaScroll_.Margin = new Thickness(0,0,16,0); settingsScroll_.Margin = new Thickness(0);
                }
            };
            bool narrowLayout_ = details.Width < 700;
            arrangeColumns_(narrowLayout_);
            details.SizeChanged += delegate
            {
                bool nextNarrow_ = details.ActualWidth < 700;
                if (nextNarrow_ != narrowLayout_) { narrowLayout_ = nextNarrow_; arrangeColumns_(narrowLayout_); }
            };
            ComboBox fiveMode_ = AddChoice(panel_, "五小时圆环", new[] { "自动（按接口返回）", "始终显示", "隐藏" }, settings.FiveHourMode == "Show" ? 1 : settings.FiveHourMode == "Hide" ? 2 : 0);
            fiveMode_.SelectionChanged += async delegate { settings.FiveHourMode = new[] { "Auto", "Show", "Hide" }[fiveMode_.SelectedIndex]; SaveSettings(); PaintRings(); await RepositionAsync(); };
            ComboBox theme_ = AddChoice(panel_,"设置界面主题",new[] { "跟随系统应用主题", "浅色", "深色" },settings.ThemeMode == "Light" ? 1 : settings.ThemeMode == "Dark" ? 2 : 0);
            theme_.SelectionChanged += delegate { settings.ThemeMode = new[] { "System","Light","Dark" }[theme_.SelectedIndex]; UiTheme.Apply(settings.ThemeMode); isLight = UiTheme.IsLight; SaveSettings(); if (hoverDesign != null) hoverDesign.RefreshPreview(); };
            AddStartupControl(panel_);
            hoverDesign = new HoverDesignControls(settings,delegate { SaveSettings(); ApplyHoverDesign(); });
            panel_.Children.Add(hoverDesign);
            StackPanel appearance_ = new StackPanel { Margin = new Thickness(0,6,0,0) };
            Expander appearanceExpander_ = new Expander { Header = "圆环外观", IsExpanded = true, Content = appearance_, Margin = new Thickness(0,10,0,4) };
            panel_.Children.Add(appearanceExpander_);
            double[] sizes_ = {16,18,20,22,24,28};
            int selectedSize_ = Array.IndexOf(sizes_, settings.RingSize);
            ComboBox size_ = AddChoice(appearance_, "圆环直径", new[] { "16 DIP", "18 DIP", "20 DIP（默认）", "22 DIP", "24 DIP", "28 DIP（旧版大小）" }, selectedSize_ < 0 ? 2 : selectedSize_);
            size_.SelectionChanged += delegate { settings.RingSize = sizes_[size_.SelectedIndex]; SaveSettings(); PaintRings(); RequestPositionUpdate(false); };
            AddText(appearance_, "两个圆环的间距（DIP，0–40）",12);
            DockPanel gapRow_ = new DockPanel { Margin = new Thickness(0,4,0,6) };
            TextBox gapInput_ = new TextBox { Name = "RingGapInput", Text = settings.RingGap.ToString("0.#",CultureInfo.InvariantCulture), Width = 54, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) };
            DockPanel.SetDock(gapInput_,Dock.Left); gapRow_.Children.Add(gapInput_);
            Slider gap_ = new Slider { Name = "RingGapSlider", Minimum = 0, Maximum = 40, Value = settings.RingGap, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            gapRow_.Children.Add(gap_); appearance_.Children.Add(gapRow_);
            bool changingGap_ = false;
            Action<double> changeGap_ = delegate(double value_)
            {
                if (changingGap_) return;
                changingGap_ = true;
                settings.RingGap = Math.Max(0,Math.Min(40,value_));
                gap_.Value = settings.RingGap;
                changingGap_ = false;
                SaveSettings(); PaintRings(); RequestPositionUpdate(false);
            };
            gap_.ValueChanged += delegate
            {
                if (changingGap_) return;
                changeGap_(gap_.Value);
                gapInput_.Text = settings.RingGap.ToString("0.#",CultureInfo.InvariantCulture);
            };
            gapInput_.TextChanged += delegate
            {
                double value_;
                if (!changingGap_ && double.TryParse(gapInput_.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value_) && !double.IsNaN(value_) && !double.IsInfinity(value_) && value_ >= 0 && value_ <= 40) changeGap_(value_);
            };
            gapInput_.LostKeyboardFocus += delegate { gapInput_.Text = settings.RingGap.ToString("0.#",CultureInfo.InvariantCulture); };
            ComboBox center_ = AddChoice(appearance_, "圆环中心", new[] { "时间窗口标签：5h / 周", "额度剩余百分比数字", "重置时间剩余百分比数字", "不显示文字" }, settings.CenterDisplay == "Percent" ? 1 : settings.CenterDisplay == "ResetPercent" ? 2 : settings.CenterDisplay == "None" ? 3 : 0);
            center_.SelectionChanged += delegate { settings.CenterDisplay = new[] { "Label", "Percent", "ResetPercent", "None" }[center_.SelectedIndex]; SaveSettings(); PaintRings(); };
            CheckBox resetRing_ = new CheckBox { Content = "显示重置时间细环", IsChecked = settings.ShowResetRing, Margin = new Thickness(0,6,0,4) };
            resetRing_.Click += delegate { settings.ShowResetRing = resetRing_.IsChecked == true; SaveSettings(); PaintRings(); };
            appearance_.Children.Add(resetRing_);
            ComboBox resetPlacement_ = AddChoice(appearance_, "重置时间细环位置", new[] { "外侧", "内侧" }, settings.ResetRingPlacement == "Inner" ? 1 : 0);
            resetPlacement_.SelectionChanged += delegate { settings.ResetRingPlacement = resetPlacement_.SelectedIndex == 1 ? "Inner" : "Outer"; SaveSettings(); PaintRings(); };
            AddColorPicker(appearance_, "五小时剩余弧段", () => settings.FiveHourColor, value_ => settings.FiveHourColor = value_);
            AddColorPicker(appearance_, "周额度剩余弧段", () => settings.WeeklyColor, value_ => settings.WeeklyColor = value_);
            AddColorPicker(appearance_, "重置时间细环", () => settings.ResetColor, value_ => settings.ResetColor = value_);
            AddColorPicker(appearance_, "空环轨道", () => settings.TrackColor, value_ => settings.TrackColor = value_);
            TextBlock opacityText_ = AddText(appearance_, "空环不透明度：" + Math.Round(settings.TrackOpacity * 100) + "%", 12);
            Slider opacity_ = new Slider { Minimum = 0, Maximum = 100, Value = settings.TrackOpacity * 100, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0,6,0,6) };
            opacity_.ValueChanged += delegate { settings.TrackOpacity = opacity_.Value / 100; opacityText_.Text = "空环不透明度：" + Math.Round(opacity_.Value) + "%"; PaintRings(); };
            opacity_.LostMouseCapture += delegate { SaveSettings(); };
            opacity_.PreviewKeyUp += delegate { SaveSettings(); };
            appearance_.Children.Add(opacity_);
            Border previewSurface_ = new Border { Background = new SolidColorBrush(Color.FromRgb(36,36,36)), Padding = new Thickness(10), CornerRadius = new CornerRadius(4), Margin = new Thickness(0,2,0,4) };
            previewSurface_.SetResourceReference(Border.BackgroundProperty,"QuotaPreview");
            StackPanel previewRow_ = new StackPanel { Orientation = Orientation.Horizontal };
            fiveHourPreview = new RingControl { Label = "5h" };
            weeklyPreview = new RingControl { Label = "周" };
            previewRow_.Children.Add(fiveHourPreview); previewRow_.Children.Add(weeklyPreview); previewSurface_.Child = previewRow_; appearance_.Children.Add(previewSurface_);
            AddText(appearance_, "粗环为额度剩余，细环为时间剩余；悬停查看详情。", 11);
            StackPanel connection_ = new StackPanel { Margin = new Thickness(0,6,0,0) };
            panel_.Children.Add(new Expander { Header = "位置、任务栏附着与连接", IsExpanded = false, Content = connection_, Margin = new Thickness(0,10,0,4) });
            ComboBox integration_ = AddChoice(connection_, "任务栏显示方式", new[] { "任务栏附着（推荐）", "独立悬浮（兼容）" }, settings.DisplayMode == "Floating" ? 1 : 0);
            integration_.SelectionChanged += delegate { settings.DisplayMode = integration_.SelectedIndex == 1 ? "Floating" : "Taskbar"; retryAttachmentAt = DateTime.MinValue; SaveSettings(); RequestPositionUpdate(true); };
            AddText(connection_,"附着模式随任务栏显示。偏移超出任务栏或附着失败时自动使用悬浮。",11);
            ComboBox position_ = AddChoice(connection_, "显示位置", new[] { "开始按钮左侧（不足时移至上方）", "任务栏上方", "仅系统托盘" }, settings.PositionMode == "Above" ? 1 : settings.PositionMode == "Tray" ? 2 : 0);
            position_.SelectionChanged += async delegate { settings.PositionMode = new[] { "StartLeft", "Above", "Tray" }[position_.SelectedIndex]; SaveSettings(); await RepositionAsync(); };
            int[] intervals_ = { 30, 60, 120, 300 };
            int selected_ = Array.IndexOf(intervals_, settings.RefreshSeconds);
            ComboBox interval_ = AddChoice(connection_, "刷新间隔", new[] { "30 秒", "60 秒", "120 秒", "300 秒" }, selected_ < 0 ? 1 : selected_);
            interval_.SelectionChanged += delegate { settings.RefreshSeconds = intervals_[interval_.SelectedIndex]; ConfigureQuotaPolling(); SaveSettings(); };
            CheckBox adaptive_ = new CheckBox { Content = "自适应额度查询（无变化时降低频率）", IsChecked = settings.AdaptivePolling, Margin = new Thickness(0,6,0,4) };
            adaptive_.Click += delegate { settings.AdaptivePolling = adaptive_.IsChecked == true; ConfigureQuotaPolling(); SaveSettings(); };
            connection_.Children.Add(adaptive_);
            CheckBox fullscreen_ = new CheckBox { Content = "真正全屏应用时隐藏圆环", IsChecked = settings.HideFullscreen, Margin = new Thickness(0,4,0,4) };
            fullscreen_.Click += delegate { settings.HideFullscreen = fullscreen_.IsChecked == true; SaveSettings(); RequestPositionUpdate(true); };
            connection_.Children.Add(fullscreen_);
            AddText(connection_,"X / Y 在左侧额度栏即时微调。X 向右为正，Y 向下为正。",11);
            Button path_ = MakeButton("选择官方 codex.exe…"); path_.Margin = new Thickness(0,12,0,0);
            path_.Click += async delegate
            {
                OpenFileDialog picker_ = new OpenFileDialog { Filter = "Codex 可执行文件 (*.exe)|*.exe", Title = "选择官方 Codex CLI", CheckFileExists = true };
                if (picker_.ShowDialog(details) != true) return;
                if (isRefreshing) { MessageBox.Show(details, "正在刷新，请稍后再更改路径。", "CodexQuota"); return; }
                settings.CodexPath = picker_.FileName; SaveSettings();
                if (rpcClient != null) { rpcClient.Dispose(); rpcClient = null; }
                snapshot = null; resolvedCodexPath = null; await RefreshAsync();
            };
            connection_.Children.Add(path_);
            StackPanel actions_ = new StackPanel { Name = "DetailsActions", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            Button refresh_ = MakeButton("立即刷新"); refresh_.Click += async delegate { await RefreshAsync(); };
            Button close_ = MakeButton("关闭"); close_.Margin = new Thickness(8,0,0,0); close_.Click += delegate { details.Close(); };
            actions_.Children.Add(refresh_); actions_.Children.Add(close_); Grid.SetRow(actions_,1); root_.Children.Add(actions_);
            details.Content = root_;
            details.Closed += delegate { if (hoverDesign != null) { hoverDesign.Dispose(); hoverDesign = null; } details = null; accountText = fiveHourText = weeklyText = updateText = stateText = anchorText = null; fiveHourPreview = weeklyPreview = null; };
            PaintAppearancePreview(); UpdateDetailText(); details.Show();
        }
        private void ApplyHoverDesign()
        {
            HoverAppearance appearance_ = settings.GetHoverAppearance();
            if (hoverPresenter != null) hoverPresenter.SetAppearance(appearance_);
            if (fiveHourTooltip != null) fiveHourTooltip.Content = QuotaHoverPresenter.BuildContent("五小时额度",fiveHourRing.Quota,IsStale(),DateTimeOffset.UtcNow,appearance_);
            if (weeklyTooltip != null) weeklyTooltip.Content = QuotaHoverPresenter.BuildContent("周额度",weeklyRing.Quota,IsStale(),DateTimeOffset.UtcNow,appearance_);
            if (hoverDesign != null) hoverDesign.RefreshPreview();
        }
        private sealed class PreviewStartupStore : IStartupStore
        {
            private string command;
            public string ReadCommand() { return command; }
            public void WriteCommand(string value) { command = value; }
            public void DeleteCommand() { command = null; }
            public bool HasSystemApprovalRecord() { return false; }
        }
        private void AddStartupControl(Panel panel)
        {
            if (startupRegistration == null)
                startupRegistration = new StartupRegistrationService(isRendering ? (IStartupStore)new PreviewStartupStore() : new RegistryStartupStore());
            string executable_ = Assembly.GetExecutingAssembly().Location;
            CheckBox toggle_ = new CheckBox { Name = "StartupToggle", Content = "开机自启（当前用户）", Margin = new Thickness(0,8,0,2) };
            TextBlock state_ = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,2,0,4) };
            Action readState_ = delegate
            {
                try
                {
                    StartupStatus value_ = startupRegistration.GetStatus(executable_);
                    toggle_.IsChecked = value_.IsCurrentExecutable;
                    state_.Text = value_.IsCurrentExecutable ? "已登记开机启动" : value_.IsRegistered ? "自启仍指向旧位置，可重新开启" : "未登记开机启动";
                }
                catch { toggle_.IsChecked = false; state_.Text = "无法读取开机启动状态"; }
            };
            toggle_.Click += delegate
            {
                try { startupRegistration.SetEnabled(toggle_.IsChecked == true,executable_); readState_(); }
                catch (Exception exception_) { readState_(); state_.Text = "开机启动更改失败：" + exception_.Message; }
            };
            Button system_ = MakeButton("系统启动应用设置");
            system_.Name = "StartupSystemSettings"; system_.HorizontalAlignment = HorizontalAlignment.Left;
            system_.Click += delegate
            {
                if (isRendering) return;
                try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
                catch { state_.Text = "请在 Windows 设置 → 应用 → 启动中查看。"; }
            };
            readState_(); panel.Children.Add(toggle_); panel.Children.Add(state_); panel.Children.Add(system_);
        }
        private void AddHorizontalAdjustment(Panel panel)
        {
            AddText(panel,"水平偏移 X（DIP）",12);
            WrapPanel row_ = new WrapPanel { Margin = new Thickness(0,5,0,3) };
            TextBox value_ = new TextBox { Name = "HorizontalOffsetInput", Text = settings.OffsetX.ToString(CultureInfo.InvariantCulture), Width = 54, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,4) };
            value_.TextChanged += delegate
            {
                double x_;
                if (!double.TryParse(value_.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out x_) || double.IsNaN(x_) || double.IsInfinity(x_) || x_ < -500 || x_ > 500) return;
                settings.OffsetX = x_; RequestPositionUpdate(false);
            };
            value_.LostKeyboardFocus += delegate { value_.Text = settings.OffsetX.ToString(CultureInfo.InvariantCulture); SaveSettings(); };
            row_.Children.Add(value_);
            foreach (int step_ in new[] { -1,1,0 })
            {
                int delta_ = step_;
                Button button_ = MakeButton(step_ < 0 ? "左移 1" : step_ > 0 ? "右移 1" : "X 归零");
                button_.Margin = new Thickness(0,0,4,4);
                button_.Click += delegate
                {
                    settings.OffsetX = delta_ == 0 ? 0 : Math.Max(-500,Math.Min(500,settings.OffsetX + delta_));
                    value_.Text = settings.OffsetX.ToString(CultureInfo.InvariantCulture); SaveSettings(); RequestPositionUpdate(false);
                };
                row_.Children.Add(button_);
            }
            panel.Children.Add(row_);
            AddText(panel,"负数向左，正数向右；输入数值即预览。",11);
        }
        private void AddVerticalAdjustment(Panel panel)
        {
            AddText(panel,"垂直偏移 Y（DIP；0 = 对齐开始按钮中心）",12);
            WrapPanel row_ = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,5,0,3) };
            TextBox value_ = new TextBox { Name = "VerticalOffsetInput", Text = settings.OffsetY.ToString(CultureInfo.InvariantCulture), Width = 54, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,4) };
            value_.TextChanged += delegate
            {
                double y_;
                if (!double.TryParse(value_.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out y_) || double.IsNaN(y_) || double.IsInfinity(y_) || y_ < -300 || y_ > 300) return;
                settings.OffsetY = y_; RequestPositionUpdate(false);
            };
            value_.LostKeyboardFocus += delegate { value_.Text = settings.OffsetY.ToString(CultureInfo.InvariantCulture); SaveSettings(); };
            row_.Children.Add(value_);
            foreach (int step_ in new[] { -1, 1, 0 })
            {
                int delta_ = step_;
                Button button_ = MakeButton(step_ < 0 ? "上移 1" : step_ > 0 ? "下移 1" : "对齐开始");
                button_.Margin = new Thickness(0,0,4,4);
                button_.Click += delegate
                {
                    settings.OffsetY = delta_ == 0 ? 0 : Math.Max(-300,Math.Min(300,settings.OffsetY + delta_));
                    value_.Text = settings.OffsetY.ToString(CultureInfo.InvariantCulture); SaveSettings(); RequestPositionUpdate(false);
                };
                row_.Children.Add(button_);
            }
            panel.Children.Add(row_);
            AddText(panel,"负数向上，正数向下；输入数值即预览。",11);
        }
        private static TextBlock AddText(Panel panel, string text, double size)
        {
            TextBlock block_ = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) };
            panel.Children.Add(block_); return block_;
        }
        private static ComboBox AddChoice(Panel panel, string label, string[] values, int selected)
        {
            AddText(panel, label, 12);
            ComboBox combo_ = new ComboBox { ItemsSource = values, SelectedIndex = selected, Margin = new Thickness(0,4,0,4), Padding = new Thickness(4) };
            panel.Children.Add(combo_); return combo_;
        }
        private static Button MakeButton(string text)
        {
            return new Button { Content = text, Padding = new Thickness(10,5,10,5), MinHeight = 28 };
        }
        private void UpdateDetailText()
        {
            if (details == null) return;
            accountText.Text = snapshot == null ? "等待账户信息" : (string.IsNullOrWhiteSpace(snapshot.AccountLabel) ? "Codex 已登录账户" : snapshot.AccountLabel) + (string.IsNullOrWhiteSpace(snapshot.PlanType) ? "" : " · " + snapshot.PlanType);
            fiveHourText.Text = "五小时额度：" + FormatQuota(snapshot == null ? null : snapshot.FiveHour);
            weeklyText.Text = "周额度：" + FormatQuota(snapshot == null ? null : snapshot.Weekly);
            updateText.Text = snapshot == null ? "尚无成功数据" : "最近成功更新：" + snapshot.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss") + (IsStale() ? "（已过期）" : "");
            if (pollScheduler != null) updateText.Text += "\n额度查询：" + (settings.AdaptivePolling ? "自适应，当前 " : "固定 ") + pollScheduler.CurrentDelaySeconds + " 秒";
            if (pollScheduler != null) updateText.Text += " · 下次 " + pollScheduler.NextDueAt.ToLocalTime().ToString("HH:mm:ss");
            stateText.Text = status; anchorText.Text = "位置：" + anchorStatus;
        }
        private void ConfigureQuotaPolling()
        {
            if (pollScheduler == null) pollScheduler = new QuotaPollScheduler(settings.RefreshSeconds,settings.AdaptivePolling,DateTimeOffset.UtcNow);
            else pollScheduler.Configure(settings.RefreshSeconds,settings.AdaptivePolling,DateTimeOffset.UtcNow);
            UpdateDetailText();
        }
        private void SaveSettings()
        {
            if (isRendering) return;
            try { settings.Save(); } catch { status = "设置保存失败，本次运行仍生效。"; UpdateDetailText(); }
        }
        private static string ShortQuota(QuotaWindow quota)
        {
            return quota == null ? "未知" : quota.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }
        private static string FormatQuota(QuotaWindow quota)
        {
            if (quota == null) return "未返回 / 未知";
            string text_ = "剩余 " + ShortQuota(quota);
            if (quota.ResetsAt.HasValue)
            {
                DateTimeOffset reset_ = quota.ResetsAt.Value;
                text_ += "\n重置：" + reset_.ToLocalTime().ToString("MM-dd HH:mm");
                if (reset_ <= DateTimeOffset.UtcNow) text_ += "（等待服务端确认）";
                double? timeFraction_ = QuotaDisplayFormatter.TimeRemainingFraction(quota,DateTimeOffset.UtcNow);
                if (timeFraction_.HasValue) text_ += " · 时间剩余 " + (timeFraction_.Value * 100).ToString("0.#",CultureInfo.InvariantCulture) + "%";
            }
            return text_;
        }
        private void Stop(object sender, ExitEventArgs eventArgs)
        {
            isClosing = true;
            if (refreshTimer != null) refreshTimer.Stop();
            if (clockTimer != null) clockTimer.Stop();
            if (recoveryTimer != null) recoveryTimer.Stop();
            if (positionDebounce != null) positionDebounce.Stop();
            TaskbarAnchor.LayoutChanged -= OnAnchorLayoutChanged;
            if (shellWatcher != null) { shellWatcher.Changed -= OnShellChanged; shellWatcher.Dispose(); }
            if (rpcClient != null) rpcClient.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (appIcon != null) appIcon.Dispose();
            ReleaseTaskbarHost();
            if (hoverPresenter != null) hoverPresenter.Dispose();
            if (details != null) details.Close();
            if (overlay != null) overlay.Close();
        }
        private static int Probe(string outputPath)
        {
            object report_;
            int exitCode_ = 0;
            try
            {
                string path_ = CodexLocator.Find("");
                using (CodexRpcClient client_ = new CodexRpcClient())
                {
                    client_.Start(path_);
                    QuotaSnapshot result_ = client_.ReadQuotaAsync().GetAwaiter().GetResult();
                    report_ = new { Success = true, CodexPath = path_, AccountAvailable = !string.IsNullOrWhiteSpace(result_.AccountLabel), Plan = result_.PlanType,
                        FiveHourRemaining = result_.FiveHour == null ? (double?)null : result_.FiveHour.RemainingPercent,
                        WeeklyRemaining = result_.Weekly == null ? (double?)null : result_.Weekly.RemainingPercent,
                        UpdatedAt = result_.UpdatedAt.ToString("o") };
                }
            }
            catch (Exception exception_) { report_ = new { Success = false, ErrorType = exception_.GetType().Name, Message = exception_.Message }; exitCode_ = 1; }
            File.WriteAllText(outputPath, new JavaScriptSerializer().Serialize(report_), new UTF8Encoding(false));
            return exitCode_;
        }
        private static void RenderPreview(string outputPath)
        {
            StackPanel panel_ = new StackPanel { Width = 340, Background = new SolidColorBrush(Color.FromRgb(35,35,35)), Margin = new Thickness(0) };
            TextBlock title_ = new TextBlock { Text = "CodexQuota · v0.4", Foreground = Brushes.Gainsboro, FontSize = 20, Margin = new Thickness(20,18,20,8) };
            panel_.Children.Add(title_);
            panel_.Children.Add(new TextBlock { Text = "界面验证 · 以下为示例数据", Foreground = Brushes.DarkGray, FontSize = 12, Margin = new Thickness(20,0,20,16) });
            foreach (string mode_ in new[] { "Label", "Percent", "ResetPercent", "None" })
            {
                StackPanel rings_ = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20,0,20,16) };
                rings_.Children.Add(new RingControl { Label = "5h", CenterDisplay = mode_, ShowResetRing = true, ResetColor = Color.FromRgb(120,166,205), Quota = new QuotaWindow { UsedPercent = 28, WindowDurationMins = 300, ResetsAt = DateTimeOffset.UtcNow.AddHours(2) }, Margin = new Thickness(0,0,8,0) });
                rings_.Children.Add(new RingControl { Label = "周", CenterDisplay = mode_, ShowResetRing = true, ResetColor = Color.FromRgb(120,166,205), Quota = new QuotaWindow { UsedPercent = 62, WindowDurationMins = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(3) } });
                rings_.Children.Add(new TextBlock { Text = mode_ == "Label" ? "标签" : mode_ == "Percent" ? "额度剩余百分比" : mode_ == "ResetPercent" ? "时间剩余百分比" : "不显示文字", Foreground = Brushes.Gainsboro, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18,0,0,0) });
                panel_.Children.Add(rings_);
            }
            foreach (string text_ in new[] { "粗环：额度剩余 · 细环：重置时间剩余", "时间细环可放外侧或内侧", "悬停查看额度、重置时间和倒计时" })
                panel_.Children.Add(new TextBlock { Text = text_, Foreground = Brushes.Gainsboro, FontSize = 13, Margin = new Thickness(20,0,20,12) });
            panel_.Measure(new Size(340, double.PositiveInfinity)); panel_.Arrange(new Rect(panel_.DesiredSize)); panel_.UpdateLayout();
            RenderTargetBitmap bitmap_ = new RenderTargetBitmap((int)Math.Ceiling(panel_.ActualWidth), (int)Math.Ceiling(panel_.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap_.Render(panel_);
            PngBitmapEncoder encoder_ = new PngBitmapEncoder(); encoder_.Frames.Add(BitmapFrame.Create(bitmap_));
            using (FileStream stream_ = File.Create(outputPath)) encoder_.Save(stream_);
        }
        private static void RenderSettings(string outputPath, string themeMode = "Light", bool showHoverDesign = false)
        {
            QuotaApplication application_ = new QuotaApplication { settings = new UserSettings { ThemeMode = themeMode, RingSize = 22 }, isLight = themeMode != "Dark", isRendering = true,
                status = "界面验证 · 示例数据", anchorStatus = "开始按钮左侧",
                snapshot = new QuotaSnapshot { AccountLabel = "示例账户", PlanType = "plus", UpdatedAt = DateTimeOffset.UtcNow,
                    FiveHour = new QuotaWindow { UsedPercent = 28, WindowDurationMins = 300, ResetsAt = DateTimeOffset.UtcNow.AddHours(2) }, Weekly = new QuotaWindow { UsedPercent = 62, WindowDurationMins = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(3) } } };
            application_.ShowDetails();
            if (showHoverDesign) application_.hoverDesign.IsExpanded = true;
            application_.details.UpdateLayout();
            if (showHoverDesign) { application_.hoverDesign.BringIntoView(); application_.details.UpdateLayout(); }
            FrameworkElement content_ = (FrameworkElement)application_.details.Content;
            Border surface_ = new Border { Background = application_.details.Background, Child = null, Width = application_.details.ActualWidth, Height = content_.ActualHeight + 40 };
            application_.details.Content = null;
            surface_.Child = content_;
            surface_.Measure(new Size(surface_.Width, surface_.Height)); surface_.Arrange(new Rect(0,0,surface_.Width,surface_.Height)); surface_.UpdateLayout();
            RenderTargetBitmap bitmap_ = new RenderTargetBitmap((int)Math.Ceiling(surface_.ActualWidth), (int)Math.Ceiling(surface_.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap_.Render(surface_);
            PngBitmapEncoder encoder_ = new PngBitmapEncoder(); encoder_.Frames.Add(BitmapFrame.Create(bitmap_));
            using (FileStream stream_ = File.Create(outputPath)) encoder_.Save(stream_);
            application_.details.Close(); application_.Shutdown();
        }
    }
}
