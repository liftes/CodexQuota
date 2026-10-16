using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexQuota;
using Forms = System.Windows.Forms;

internal static class ApplicationTests
{
    private static int count;
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
    private static readonly BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }
    private static object Get(object target, string name)
    {
        return target.GetType().GetField(name, privateInstance).GetValue(target);
    }
    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, privateInstance).SetValue(target, value);
    }
    private static object Invoke(object target, string name, params object[] arguments)
    {
        try { return target.GetType().GetMethod(name, privateInstance).Invoke(target, arguments); }
        catch (TargetInvocationException exception_) { throw exception_.InnerException ?? exception_; }
    }
    private static T Find<T>(DependencyObject root, Predicate<T> predicate) where T : DependencyObject
    {
        HashSet<DependencyObject> seen_ = new HashSet<DependencyObject>();
        Queue<DependencyObject> pending_ = new Queue<DependencyObject>();
        pending_.Enqueue(root);
        while (pending_.Count > 0)
        {
            DependencyObject current_ = pending_.Dequeue();
            if (!seen_.Add(current_)) continue;
            T match_ = current_ as T;
            if (match_ != null && predicate(match_)) return match_;
            foreach (object child_ in LogicalTreeHelper.GetChildren(current_))
                if (child_ is DependencyObject) pending_.Enqueue((DependencyObject)child_);
            if (current_ is Visual || current_ is System.Windows.Media.Media3D.Visual3D)
                for (int index_ = 0; index_ < VisualTreeHelper.GetChildrenCount(current_); index_++)
                    pending_.Enqueue(VisualTreeHelper.GetChild(current_, index_));
        }
        throw new Exception("Expected UI control was not found: " + typeof(T).Name);
    }
    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (DependencyObject current_ = child; current_ != null; current_ = LogicalTreeHelper.GetParent(current_))
            if (object.ReferenceEquals(current_, ancestor)) return true;
        return false;
    }
    private static int RenderCount(TaskbarHost host)
    {
        return (int)typeof(TaskbarHost).GetProperty("RenderCount", privateInstance).GetValue(host, null);
    }
    private static bool TooltipContains(ToolTip tooltip, string text)
    {
        string content_ = tooltip.Content as string;
        if (content_ != null) return content_.Contains(text);
        DependencyObject root_ = tooltip.Content as DependencyObject;
        return root_ != null && Find<TextBlock>(root_, delegate(TextBlock block_) { return block_.Text.Contains(text); }) != null;
    }
    private static TextBlock NamedText(DependencyObject root, string name)
    {
        return Find<TextBlock>(root, delegate(TextBlock candidate_) { return candidate_.Name == name; });
    }
    private static void CheckHoverDesign(QuotaApplication application, UserSettings settings, Window details)
    {
        double savedX_ = settings.OffsetX, savedY_ = settings.OffsetY, savedGap_ = settings.RingGap;
        Expander design_ = Find<Expander>(details, delegate(Expander candidate_) { return candidate_.Name == "HoverDesignExpander"; });
        Expander rings_ = Find<Expander>(details, delegate(Expander candidate_) { return object.Equals(candidate_.Header,"圆环外观"); });
        Check(object.Equals(design_.Header,"悬浮窗设计") && !IsDescendantOf(design_,rings_), "Hover design has its own expander independently of ring appearance.");
        Check(!design_.IsExpanded, "Hover design initially remains collapsed to keep the settings panel compact.");
        TextBox title_ = Find<TextBox>(design_, delegate(TextBox candidate_) { return candidate_.Name == "HoverTitleInput"; });
        TextBox value_ = Find<TextBox>(design_, delegate(TextBox candidate_) { return candidate_.Name == "HoverValueInput"; });
        TextBox detail_ = Find<TextBox>(design_, delegate(TextBox candidate_) { return candidate_.Name == "HoverDetailInput"; });
        TextBox width_ = Find<TextBox>(design_, delegate(TextBox candidate_) { return candidate_.Name == "HoverWidthInput"; });
        Check(title_.Text == "12" && value_.Text == "22" && detail_.Text == "13" && width_.Text == "300", "Actual hover design inputs display the compact defaults.");
        design_.IsExpanded = true;
        details.UpdateLayout();
        ContentControl preview_ = Find<ContentControl>(design_, delegate(ContentControl candidate_) { return candidate_.Name == "HoverDesignPreview"; });
        Check(preview_.Content is FrameworkElement && preview_.IsVisible, "Expanding hover design exposes a populated inline preview.");
        title_.Text = "16"; value_.Text = "32"; detail_.Text = "17"; width_.Text = "420";
        Check(settings.HoverTitleFontSize == 16 && settings.HoverValueFontSize == 32 && settings.HoverDetailFontSize == 17 && settings.HoverContentWidth == 420,
            "Editing all four hover controls immediately updates the actual settings model.");
        DependencyObject previewContent_ = (DependencyObject)preview_.Content;
        Check(NamedText(previewContent_,"QuotaHoverHeading").FontSize == 16 && NamedText(previewContent_,"QuotaHoverRemaining").FontSize == 32,
            "Actual shared preview renders the selected heading and value font sizes.");
        Check(Find<TextBlock>(previewContent_,delegate(TextBlock candidate_) { return candidate_.Text == "重置时间"; }).FontSize == 17 && ((FrameworkElement)previewContent_).Width == 420,
            "Actual preview applies the selected detail font and content width.");
        QuotaHoverPresenter presenter_ = (QuotaHoverPresenter)Get(application,"hoverPresenter");
        HoverAppearance applied_ = (HoverAppearance)Get(presenter_,"appearance");
        Check(applied_.TitleFontSize == 16 && applied_.ValueFontSize == 32 && applied_.DetailFontSize == 17 && applied_.ContentWidth == 420,
            "Control change callbacks update the real taskbar hover presenter appearance.");
        DependencyObject actualTooltip_ = (DependencyObject)((ToolTip)((RingControl)Get(application,"fiveHourRing")).ToolTip).Content;
        Check(NamedText(actualTooltip_,"QuotaHoverHeading").FontSize == 16 && NamedText(actualTooltip_,"QuotaHoverRemaining").FontSize == 32,
            "Control changes refresh the actual floating ring tooltip content as well as the design preview.");
        foreach (string invalid_ in new[] { "-", "", "9", "21", "NaN" })
        {
            title_.Text = invalid_;
            Check(settings.HoverTitleFontSize == 16, "Invalid or incomplete heading input preserves the last valid design: " + invalid_);
        }
        title_.Text = "16";
        value_.Text = "41"; detail_.Text = "25"; width_.Text = "481";
        Check(settings.HoverValueFontSize == 32 && settings.HoverDetailFontSize == 17 && settings.HoverContentWidth == 420,
            "Out-of-range value, detail and width edits preserve their last valid design.");
        value_.Text = "32"; detail_.Text = "17"; width_.Text = "480";
        details.UpdateLayout();
        ScrollViewer previewScroll_ = Find<ScrollViewer>(design_,delegate(ScrollViewer candidate_) { return candidate_.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto; });
        Check(settings.HoverContentWidth == 480 && previewScroll_.ScrollableWidth > 0,
            "Maximum-width hover preview can scroll horizontally instead of clipping content.");
        Button compact_ = Find<Button>(design_,delegate(Button candidate_) { return candidate_.Content is string && ((string)candidate_.Content).StartsWith("紧凑",StringComparison.Ordinal); });
        Button loose_ = Find<Button>(design_,delegate(Button candidate_) { return candidate_.Content is string && ((string)candidate_.Content).StartsWith("宽松",StringComparison.Ordinal); });
        Click(loose_);
        Check(settings.HoverTitleFontSize == 14 && settings.HoverValueFontSize == 28 && settings.HoverDetailFontSize == 18 && settings.HoverContentWidth == 360,
            "Loose preset applies its documented font sizes and width through the actual UI.");
        Check(title_.Text == "14" && value_.Text == "28" && detail_.Text == "18" && width_.Text == "360",
            "Loose preset updates all editable input values.");
        Click(compact_);
        Check(settings.HoverTitleFontSize == 12 && settings.HoverValueFontSize == 22 && settings.HoverDetailFontSize == 13 && settings.HoverContentWidth == 300,
            "Compact preset restores the documented defaults through the actual UI.");
        ComboBox theme_ = Find<ComboBox>(details,delegate(ComboBox candidate_) { return candidate_.Items.Count == 3 && object.Equals(candidate_.Items[1],"浅色") && object.Equals(candidate_.Items[2],"深色"); });
        Color darkSurface_ = ((SolidColorBrush)preview_.Background).Color;
        theme_.SelectedIndex = 1;
        details.UpdateLayout();
        Color lightSurface_ = ((SolidColorBrush)preview_.Background).Color;
        Check(darkSurface_ != lightSurface_, "Existing hover preview follows a switch from dark to light theme.");
        theme_.SelectedIndex = 2;
        details.UpdateLayout();
        Check(((SolidColorBrush)preview_.Background).Color == darkSurface_, "Existing hover preview follows the switch back to dark theme.");
        Check(settings.OffsetX == savedX_ && settings.OffsetY == savedY_ && settings.RingGap == savedGap_,
            "Hover design, presets and theme changes preserve taskbar calibration and ring spacing.");
        design_.IsExpanded = false;
    }
    [STAThread]
    private static int Main()
    {
        // Keep the bootstrap free of CodexQuota types: the CLR resolves the referenced
        // release executable only after this handler is installed for a test in /tests.
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender_, ResolveEventArgs event_)
        {
            if (new AssemblyName(event_.Name).Name != "CodexQuota") return null;
            string path_ = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Release", "CodexQuota.exe"));
            return Assembly.LoadFrom(path_);
        };
        try
        {
            RunChecks();
            Console.WriteLine("PASS: " + count + " release application integration checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_);
            return 1;
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunChecks()
    {
        QuotaApplication application_ = new QuotaApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Forms.NativeWindow parent_ = new Forms.NativeWindow();
        TaskbarHost host_ = new TaskbarHost();
        try
        {
            UserSettings settings_ = new UserSettings { OffsetX = 13, OffsetY = -10, RingSize = 20,
                FiveHourMode = "Show", ThemeMode = "Dark", CenterDisplay = "Percent", DisplayMode = "Taskbar" };
            Set(application_, "settings", settings_);
            Set(application_, "isRendering", true); // Suppresses tray visibility and all setting writes.
            QuotaSnapshot snapshot_ = new QuotaSnapshot { AccountLabel = "Integration fixture", PlanType = "plus", UpdatedAt = DateTimeOffset.UtcNow,
                FiveHour = new QuotaWindow { UsedPercent = 27, WindowDurationMins = 300, ResetsAt = DateTimeOffset.UtcNow.AddHours(2) },
                Weekly = new QuotaWindow { UsedPercent = 56, WindowDurationMins = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(3) } };
            Set(application_, "snapshot", snapshot_);
            Invoke(application_, "CreateOverlay");
            Invoke(application_, "CreateTray");
            Invoke(application_, "PaintRings");
            Window overlay_ = (Window)Get(application_, "overlay");
            Forms.NotifyIcon tray_ = (Forms.NotifyIcon)Get(application_, "tray");
            RingControl five_ = (RingControl)Get(application_, "fiveHourRing");
            RingControl weekly_ = (RingControl)Get(application_, "weeklyRing");
            Check(overlay_.Opacity == 0 && !overlay_.IsVisible, "Fixture overlay must remain invisible.");
            Check(!tray_.Visible && tray_.Icon != null, "Release must load the embedded application icon without showing a tray item.");
            Check(object.ReferenceEquals(five_.Quota, snapshot_.FiveHour) && object.ReferenceEquals(weekly_.Quota, snapshot_.Weekly), "Actual overlay receives both quota windows.");
            Check(five_.Width == 20 && weekly_.Width == 20 && five_.CenterDisplay == "Percent", "Actual ring controls receive saved appearance settings.");
            Check(TooltipContains((ToolTip)five_.ToolTip, "73%"), "Actual ring tooltip contains remaining quota.");

            Forms.CreateParams parameters_ = new Forms.CreateParams { Caption = "CodexQuota integration fixture", X = 100, Y = 100, Width = 400, Height = 24,
                Style = unchecked((int)0x80000000) }; // Hidden synthetic popup; never Explorer/taskbar.
            parent_.CreateHandle(parameters_);
            string error_;
            Check(host_.EnsureParent(parent_.Handle, out error_), "Synthetic child host creation: " + error_);
            Check(host_.SetBounds(110, 110, 78, 36, out error_), "Synthetic child host positioning: " + error_);
            Set(application_, "taskbarHost", host_);
            Set(application_, "hostWidth", 78);
            Set(application_, "hostHeight", 36);
            Set(application_, "hostScale", 1.0);
            int beforeRender_ = RenderCount(host_);
            Check((bool)Invoke(application_, "PaintTaskbarFrame"), "Actual application must present a WPF quota frame to its native child host.");
            Check(RenderCount(host_) == beforeRender_ + 1, "Actual native host receives the rendered frame.");
            Check(Get(application_, "hoverPresenter") != null, "Application creates its custom hover presenter.");
            Invoke(application_, "PaintRings");
            Check(RenderCount(host_) == beforeRender_ + 2, "Appearance/quota repaint immediately updates the attached host.");

            // Drive the production geometry-to-HWND path under our own hidden parent.
            // The 36-DIP host's transparent padding may extend beyond this 24px taskbar;
            // its 20px painted rings still fit and must move for each Y adjustment.
            Invoke(application_, "ReleaseTaskbarHost");
            NativeRect bar_;
            Check(GetWindowRect(parent_.Handle, out bar_) && bar_.Height == 24, "Fixture provides an actual 24px taskbar client area.");
            int previousTop_ = 0;
            IntPtr firstHandle_ = IntPtr.Zero;
            for (int offset_ = 0; offset_ >= -2; offset_--)
            {
                settings_.OffsetY = offset_;
                AnchorResult anchor_ = TaskbarAnchor.ComputeGeometry(new System.Drawing.Rectangle(0,0,1000,1000),bar_,
                    new Rect(bar_.Left + 250,bar_.Top,24,24),new List<Rect>(),overlay_.Width,36,settings_.RingSize,1,
                    "StartLeft",settings_.OffsetX,settings_.OffsetY,settings_.RingGap,2);
                Set(application_, "isRendering", false);
                try { Invoke(application_, "ApplyAnchor", anchor_, parent_.Handle); }
                finally { Set(application_, "isRendering", true); }
                TaskbarHost attached_ = (TaskbarHost)Get(application_, "taskbarHost");
                Check(attached_ != null && attached_.IsAlive && !overlay_.IsVisible, "Production placement keeps fitting rings attached for Y=" + offset_ + ".");
                NativeRect actual_;
                Check(GetWindowRect(attached_.Handle, out actual_) && actual_.Top == (int)Math.Round(anchor_.Top), "Production placement honors the requested native Y=" + offset_ + ".");
                Check(offset_ == 0 || actual_.Top == previousTop_ - 1, "Every one-DIP Y change reaches the actual child HWND.");
                if (offset_ == 0) firstHandle_ = attached_.Handle;
                Check(attached_.Handle == firstHandle_, "Y movement preserves the attached host instead of recreating it.");
                previousTop_ = actual_.Top;
            }
            TaskbarHost actualHost_ = (TaskbarHost)Get(application_, "taskbarHost");
            Check(!actualHost_.UseNativeTooltips, "Production attached host selects the custom hover UI.");
            QuotaHoverPresenter presenter_ = (QuotaHoverPresenter)Get(application_, "hoverPresenter");
            int hoverX_ = (int)RingLayout.RingLeft(overlay_.Width,settings_.RingSize,settings_.RingGap,2,0) + 10;
            SendMessage(actualHost_.Handle,0x0200,IntPtr.Zero,new IntPtr(hoverX_ | (18 << 16)));
            Check(presenter_.IsPending && (int)Get(application_, "nativeHoverIndex") == 0, "Native ring movement reaches the application's custom hover presenter.");
            SendMessage(actualHost_.Handle,0x02A3,IntPtr.Zero,IntPtr.Zero);
            Check(!presenter_.IsPending && !presenter_.Popup.IsOpen && (int)Get(application_, "nativeHoverIndex") == -1, "Native mouse leave cancels the popup without displaying UI during tests.");
            settings_.OffsetY = -10;

            Invoke(application_, "ShowDetails");
            Window details_ = (Window)Get(application_, "details");
            Check(details_.Opacity == 0 && !details_.ShowActivated && details_.Icon != null, "Settings fixture remains transparent/inactive and uses the embedded icon.");
            details_.UpdateLayout();
            Grid detailsLayout_ = Find<Grid>(details_, delegate(Grid candidate_) { return candidate_.Name == "DetailsLayout"; });
            Grid columns_ = Find<Grid>(details_, delegate(Grid candidate_) { return candidate_.Name == "DetailsColumns"; });
            ScrollViewer quotaScroll_ = Find<ScrollViewer>(details_, delegate(ScrollViewer candidate_) { return candidate_.Name == "QuotaScroll"; });
            ScrollViewer settingsScroll_ = Find<ScrollViewer>(details_, delegate(ScrollViewer candidate_) { return candidate_.Name == "SettingsScroll"; });
            StackPanel actions_ = Find<StackPanel>(details_, delegate(StackPanel candidate_) { return candidate_.Name == "DetailsActions"; });
            Check(details_.Width <= details_.MaxWidth && details_.Height <= details_.MaxHeight && details_.SizeToContent == SizeToContent.Manual,
                "Settings window stays within explicit working-area limits instead of growing with content.");
            Check(quotaScroll_.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && settingsScroll_.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                "Quota and settings panes support independent vertical scrolling.");
            Check(object.ReferenceEquals(LogicalTreeHelper.GetParent(actions_),detailsLayout_) && Grid.GetRow(actions_) == 1 && detailsLayout_.RowDefinitions[1].Height.IsAuto,
                "Refresh and close actions remain outside scrolling content in the fixed footer.");
            Check(columns_.ColumnDefinitions.Count == (details_.ActualWidth < 700 ? 1 : 2), "Actual settings window selects columns appropriate for its width.");
            Check(settingsScroll_.ViewportHeight > 0 && settingsScroll_.ScrollableHeight > 0, "Settings pane has a finite viewport and can reach content below the fold.");
            TextBox y_ = Find<TextBox>(details_, delegate(TextBox candidate_) { return candidate_.Name == "VerticalOffsetInput"; });
            y_.Text = "-17.5";
            Check(settings_.OffsetY == -17.5 && settings_.OffsetX == 13, "Typing Y updates the actual saved model immediately without changing X.");
            y_.Text = "-";
            Check(settings_.OffsetY == -17.5, "Incomplete negative input does not reset the selected baseline.");
            y_.Text = "301";
            Check(settings_.OffsetY == -17.5, "Out-of-range Y input cannot corrupt the selected baseline.");
            y_.Text = "-17.5";
            Button up_ = Find<Button>(details_, delegate(Button candidate_) { return object.Equals(candidate_.Content, "上移 1"); });
            Button down_ = Find<Button>(details_, delegate(Button candidate_) { return object.Equals(candidate_.Content, "下移 1"); });
            Button align_ = Find<Button>(details_, delegate(Button candidate_) { return object.Equals(candidate_.Content, "对齐开始"); });
            Click(up_);
            Check(settings_.OffsetY == -18.5 && y_.Text == "-18.5", "Up button raises Y by one DIP and updates the input.");
            Click(down_);
            Check(settings_.OffsetY == -17.5 && y_.Text == "-17.5", "Down button lowers Y by one DIP and updates the input.");
            Click(align_);
            Check(settings_.OffsetY == 0 && y_.Text == "0" && settings_.OffsetX == 13, "Align button resets only the Start-relative Y baseline.");
            TextBox x_ = Find<TextBox>(details_, delegate(TextBox candidate_) { return candidate_.Name == "HorizontalOffsetInput"; });
            Check(IsDescendantOf(x_,quotaScroll_), "Horizontal adjustment stays beside Y in the quota pane.");
            double savedX_ = settings_.OffsetX;
            double savedGap_ = settings_.RingGap;
            x_.Text = "-25.5";
            Check(settings_.OffsetX == -25.5 && settings_.OffsetY == 0 && settings_.RingGap == savedGap_, "Typing fractional X updates the real model immediately while preserving Y and spacing.");
            x_.Text = "-";
            Check(settings_.OffsetX == -25.5, "Incomplete negative X input preserves the current horizontal calibration.");
            x_.Text = "501";
            Check(settings_.OffsetX == -25.5, "Out-of-range X input cannot corrupt the selected horizontal calibration.");
            x_.Text = "-25.5";
            Button left_ = Find<Button>(details_, delegate(Button candidate_) { return object.Equals(candidate_.Content, "左移 1"); });
            Button right_ = Find<Button>(details_, delegate(Button candidate_) { return object.Equals(candidate_.Content, "右移 1"); });
            Button resetX_ = Find<Button>(details_, delegate(Button candidate_) { return object.Equals(candidate_.Content, "X 归零"); });
            Click(left_);
            Check(settings_.OffsetX == -26.5 && x_.Text == "-26.5", "Left button moves X by one DIP and updates its input.");
            Click(right_);
            Check(settings_.OffsetX == -25.5 && x_.Text == "-25.5", "Right button moves X by one DIP and updates its input.");
            Click(resetX_);
            Check(settings_.OffsetX == 0 && x_.Text == "0" && settings_.OffsetY == 0 && settings_.RingGap == savedGap_, "X reset changes only the horizontal calibration.");
            int baselineLeft_ = 0, baselineTop_ = 0;
            foreach (int offset_ in new[] { 0,-10,10 })
            {
                x_.Text = offset_.ToString(System.Globalization.CultureInfo.InvariantCulture);
                AnchorResult anchor_ = TaskbarAnchor.ComputeGeometry(new System.Drawing.Rectangle(0,0,1000,1000),bar_,
                    new Rect(bar_.Left + 250,bar_.Top,24,24),new List<Rect>(),overlay_.Width,36,settings_.RingSize,1,
                    "StartLeft",settings_.OffsetX,settings_.OffsetY,settings_.RingGap,2);
                Check(!anchor_.IsFallback, "Manual X preserves the independently selected taskbar anchor.");
                Set(application_, "isRendering", false);
                try { Invoke(application_, "ApplyAnchor", anchor_, parent_.Handle); }
                finally { Set(application_, "isRendering", true); }
                NativeRect actual_;
                TaskbarHost attached_ = (TaskbarHost)Get(application_, "taskbarHost");
                Check(attached_ != null && attached_.Handle == actualHost_.Handle, "X edits use the existing real attached HWND.");
                Check(GetWindowRect(attached_.Handle,out actual_), "Actual X target has readable native bounds.");
                if (offset_ == 0) { baselineLeft_ = actual_.Left; baselineTop_ = actual_.Top; }
                Check(actual_.Left == baselineLeft_ + offset_, "Input-to-model-to-anchor-to-HWND path applies X=" + offset_ + " without reversal or drift.");
                Check(actual_.Top == baselineTop_ && settings_.OffsetY == 0 && settings_.RingGap == savedGap_, "Actual horizontal positioning preserves native Y and ring spacing.");
            }
            x_.Text = savedX_.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Check(settings_.OffsetX == savedX_, "X fixture restores the previous user calibration.");
            CheckBox startup_ = Find<CheckBox>(details_, delegate(CheckBox candidate_) { return candidate_.Name == "StartupToggle"; });
            Button systemStartup_ = Find<Button>(details_, delegate(Button candidate_) { return candidate_.Name == "StartupSystemSettings"; });
            Check(startup_.Content != null && systemStartup_.Content != null, "Settings expose startup registration and the Windows startup settings shortcut.");
            Check((bool)Get(application_, "isRendering"), "Startup UI inspection retains rendering mode and never writes a real Run registration.");
            StartupRegistrationService startupService_ = (StartupRegistrationService)Get(application_, "startupRegistration");
            Check(Get(startupService_, "store").GetType().Name == "PreviewStartupStore", "Startup UI fixture uses the dedicated memory store before exercising registration controls.");
            string executable_ = typeof(QuotaApplication).Assembly.Location;
            Check(startup_.IsChecked == false && !startupService_.IsEnabled(executable_), "Startup control reads the initial unregistered memory state.");
            startup_.IsChecked = true;
            startup_.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent,startup_));
            Check(startup_.IsChecked == true && startupService_.GetStatus(executable_).IsCurrentExecutable,
                "Actual startup UI click registers the current Release executable in its isolated store.");
            startup_.IsChecked = false;
            startup_.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent,startup_));
            Check(startup_.IsChecked == false && !startupService_.GetStatus(executable_).IsRegistered,
                "Actual startup UI click removes the isolated registration and reflects the new state.");
            CheckHoverDesign(application_,settings_,details_);
            TextBox gap_ = Find<TextBox>(details_, delegate(TextBox candidate_) { return candidate_.Name == "RingGapInput"; });
            int beforeGapRender_ = RenderCount(actualHost_);
            gap_.Text = "0";
            Check(settings_.RingGap == 0 && five_.Margin.Right == 0 && weekly_.Margin.Left == 0 && overlay_.Width == 72,
                "Zero spacing removes the actual gap and shrinks the overlay footprint immediately.");
            gap_.Text = "20";
            Check(settings_.RingGap == 20 && five_.Margin.Right == 20 && overlay_.Width == 92,
                "Editing spacing updates actual ring margins and overlay width immediately.");
            Check(((RingControl)Get(application_, "fiveHourPreview")).Margin.Right == 20 && RenderCount(actualHost_) >= beforeGapRender_ + 2,
                "Spacing changes update both the settings preview and the attached native frame.");
            Check(settings_.OffsetX == 13 && settings_.OffsetY == 0, "Changing ring spacing preserves the calibrated X and Y baseline.");
            gap_.Text = "41";
            Check(settings_.RingGap == 20 && overlay_.Width == 92, "Out-of-range gap input does not alter the current layout.");
            details_.Width = Math.Min(640,details_.MaxWidth);
            details_.UpdateLayout();
            Check(columns_.ColumnDefinitions.Count == 1 && Grid.GetRow(settingsScroll_) == 1,
                "Narrowing the actual settings window stacks its independently scrollable panes.");
            Check(object.ReferenceEquals(LogicalTreeHelper.GetParent(actions_),detailsLayout_) && Grid.GetRow(actions_) == 1,
                "Responsive layout preserves the fixed action footer.");
            ComboBox integration_ = Find<ComboBox>(details_, delegate(ComboBox candidate_) { return candidate_.Items.Count == 2 && object.Equals(candidate_.Items[0], "任务栏附着（推荐）"); });
            integration_.SelectedIndex = 1;
            Check(settings_.DisplayMode == "Floating", "Actual integration choice stores floating mode.");
            integration_.SelectedIndex = 0;
            Check(settings_.DisplayMode == "Taskbar", "Actual integration choice restores taskbar mode.");
            Check(Get(application_, "rpcClient") == null && Get(application_, "shellWatcher") == null && Get(application_, "refreshTimer") == null,
                "UI integration checks never start Codex RPC, shell hooks, or polling.");
        }
        finally
        {
            Set(application_, "isClosing", true);
            Invoke(application_, "Stop", null, null);
            host_.Dispose();
            if (parent_.Handle != IntPtr.Zero) parent_.DestroyHandle();
            application_.Shutdown();
        }
    }
}
