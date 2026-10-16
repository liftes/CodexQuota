using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuota;

internal static class HoverTests
{
    private static int count;
    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }
    private static void PumpFor(int milliseconds)
    {
        DispatcherFrame frame_ = new DispatcherFrame();
        DispatcherTimer timer_ = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer_.Tick += delegate { timer_.Stop(); frame_.Continue = false; };
        timer_.Start(); Dispatcher.PushFrame(frame_);
    }
    private static List<TextBlock> Blocks(FrameworkElement element)
    {
        List<TextBlock> result_ = new List<TextBlock>();
        TextBlock block_ = element as TextBlock;
        if (block_ != null) result_.Add(block_);
        Panel panel_ = element as Panel;
        if (panel_ != null) foreach (FrameworkElement child_ in panel_.Children) result_.AddRange(Blocks(child_));
        ContentControl content_ = element as ContentControl;
        if (content_ != null && content_.Content is FrameworkElement) result_.AddRange(Blocks((FrameworkElement)content_.Content));
        return result_;
    }
    private static string Text(FrameworkElement element)
    {
        List<string> text_ = new List<string>();
        foreach (TextBlock block_ in Blocks(element)) text_.Add(block_.Text);
        return String.Join("\n", text_);
    }
    private static double Linear(byte channel)
    {
        double component_ = channel / 255.0;
        return component_ <= .04045 ? component_ / 12.92 : Math.Pow((component_ + .055) / 1.055, 2.4);
    }
    private static double Contrast(Brush first, Brush second)
    {
        Color first_ = ((SolidColorBrush)first).Color, second_ = ((SolidColorBrush)second).Color;
        double a_ = .2126 * Linear(first_.R) + .7152 * Linear(first_.G) + .0722 * Linear(first_.B);
        double b_ = .2126 * Linear(second_.R) + .7152 * Linear(second_.G) + .0722 * Linear(second_.B);
        return (Math.Max(a_, b_) + .05) / (Math.Min(a_, b_) + .05);
    }
    [STAThread]
    private static int Main()
    {
        Application application_ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        QuotaHoverPresenter presenter_ = null;
        try
        {
            UiTheme.Apply("Light");
            DateTimeOffset now_ = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
            QuotaWindow quota_ = new QuotaWindow { UsedPercent = 27.5, WindowDurationMins = 300, ResetsAt = now_.AddMinutes(150) };
            FrameworkElement content_ = QuotaHoverPresenter.BuildContent("五小时额度", quota_, false, now_);
            content_.Measure(new Size(400, Double.PositiveInfinity));
            content_.Arrange(new Rect(new Point(), content_.DesiredSize));
            string contentText_ = Text(content_);
            Check(contentText_.Contains("72.5%") && contentText_.Contains("剩余"), "Large value shows the authoritative remaining quota.");
            Check(contentText_.Contains("2 小时 30 分钟"), "Countdown uses the supplied clock.");
            Check(contentText_.Contains("50%"), "Time remaining percent uses the server window duration.");
            Check(contentText_.Contains(quota_.ResetsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), "Full local reset date includes seconds.");
            Check(content_.DesiredSize.Width == 300 && content_.DesiredSize.Height < 190, "Default tooltip content returns to the compact 300 DIP layout.");
            double headingSize_ = 0, remainingSize_ = 0;
            foreach (TextBlock block_ in Blocks(content_))
            {
                Check(block_.TextWrapping == TextWrapping.Wrap, "Every content label can wrap rather than clip.");
                if (block_.Name == "QuotaHoverHeading") headingSize_ = block_.FontSize;
                if (block_.Name == "QuotaHoverRemaining")
                {
                    remainingSize_ = block_.FontSize;
                    foreach (System.Windows.Documents.Inline inline_ in block_.Inlines)
                        if (inline_ is System.Windows.Documents.Run && ((System.Windows.Documents.Run)inline_).Text.Contains("剩余"))
                            Check(inline_.FontSize == 13, "Remaining suffix uses configured detail typography.");
                }
                if (block_.Name != "QuotaHoverHeading" && block_.Name != "QuotaHoverRemaining")
                    Check(block_.FontSize == 13, "Default detail labels and values use readable 13 DIP text.");
            }
            Check(remainingSize_ == 22 && headingSize_ == 12, "Default quota and heading use compact 22/12 DIP hierarchy.");
            Grid firstDetail_ = ((StackPanel)content_).Children[3] as Grid;
            Check(firstDetail_ != null && Math.Abs(firstDetail_.ColumnDefinitions[0].Width.Value - 68.9) < .01, "Detail label width adapts to the configured font size.");
            Check(((TextBlock)firstDetail_.Children[1]).DesiredSize.Height < 25, "Full date fits one line at the compact default size.");
            HoverAppearance invalid_ = new HoverAppearance { TitleFontSize = Double.NaN, ValueFontSize = Double.PositiveInfinity, DetailFontSize = Double.NegativeInfinity, ContentWidth = Double.NaN };
            invalid_.Normalize();
            Check(invalid_.TitleFontSize == 12 && invalid_.ValueFontSize == 22 && invalid_.DetailFontSize == 13 && invalid_.ContentWidth == 300, "Non-finite appearance values recover safe defaults.");
            HoverAppearance clamped_ = new HoverAppearance { TitleFontSize = 1, ValueFontSize = 99, DetailFontSize = 0, ContentWidth = 999 }.Normalize();
            Check(clamped_.TitleFontSize == 10 && clamped_.ValueFontSize == 40 && clamped_.DetailFontSize == 10 && clamped_.ContentWidth == 480, "Appearance values clamp to supported bounds.");
            HoverAppearance copied_ = clamped_.Clone();
            copied_.ContentWidth = 240;
            Check(clamped_.ContentWidth == 480, "Clone creates an independent appearance object.");
            foreach (HoverAppearance appearance_ in new[] {
                new HoverAppearance { TitleFontSize = 10, ValueFontSize = 16, DetailFontSize = 10, ContentWidth = 240 },
                new HoverAppearance { TitleFontSize = 20, ValueFontSize = 40, DetailFontSize = 24, ContentWidth = 480 },
                new HoverAppearance { TitleFontSize = 12, ValueFontSize = 30, DetailFontSize = 24, ContentWidth = 240 } })
            {
                FrameworkElement custom_ = QuotaHoverPresenter.BuildContent("五小时额度", quota_, true, now_, appearance_);
                custom_.Measure(new Size(appearance_.ContentWidth, Double.PositiveInfinity));
                custom_.Arrange(new Rect(new Point(), custom_.DesiredSize));
                Check(custom_.DesiredSize.Width == appearance_.ContentWidth, "Preview content width matches the selected width one-to-one.");
                foreach (TextBlock block_ in Blocks(custom_))
                    Check(block_.FontSize == (block_.Name == "QuotaHoverHeading" ? appearance_.TitleFontSize : block_.Name == "QuotaHoverRemaining" ? appearance_.ValueFontSize : appearance_.DetailFontSize), "Every content text level follows its selected size.");
                Grid customDetail_ = ((StackPanel)custom_).Children[3] as Grid;
                Check(customDetail_.ColumnDefinitions[0].Width.Value >= 65 && ((TextBlock)customDetail_.Children[1]).TextWrapping == TextWrapping.Wrap, "Detail labels stay legible and long values may wrap at custom widths.");
                if (appearance_.ContentWidth == 240 && appearance_.DetailFontSize == 24)
                    Check(((TextBlock)customDetail_.Children[1]).DesiredSize.Height > 40, "Large detail text at narrow width wraps full dates instead of clipping.");
            }
            HoverAppearance unchanged_ = new HoverAppearance { TitleFontSize = 2 };
            QuotaHoverPresenter.BuildContent("周额度", null, true, now_, unchanged_);
            Check(unchanged_.TitleFontSize == 2, "Content construction normalizes its copy without mutating the supplied appearance.");
            string unknown_ = Text(QuotaHoverPresenter.BuildContent("周额度", null, true, now_));
            Check(unknown_.Contains("额度未知") && unknown_.Contains("重置时间未知"), "Missing quota and reset time are explicit.");
            Check(unknown_.Contains("数据已过期"), "Stale state is described in neutral text.");
            QuotaWindow expired_ = new QuotaWindow { UsedPercent = 91, WindowDurationMins = 10080, ResetsAt = now_.AddSeconds(-1) };
            string expiredText_ = Text(QuotaHoverPresenter.BuildContent("周额度", expired_, false, now_));
            Check(expiredText_.Contains("9%") && !expiredText_.Contains("100%"), "Local reset expiry never fills quota.");
            Check(expiredText_.Contains("等待服务器确认") && expiredText_.Contains("0%"), "Expired reset retains a zero time fraction and pending-server message.");
            QuotaWindow noReset_ = new QuotaWindow { UsedPercent = 50 };
            Check(Text(QuotaHoverPresenter.BuildContent("周额度", noReset_, false, now_)).Contains("未提供重置时间"), "Absent reset timestamp is distinct from zero countdown.");

            Rect screen_ = new Rect(0, 0, 1920, 1080);
            Size popupSize_ = new Size(390, 250);
            Rect topRing_ = new Rect(600, 10, 24, 24), topBar_ = new Rect(0, 0, 1920, 48);
            Rect top_ = QuotaHoverPresenter.ComputePopupBounds(topRing_, topBar_, screen_, popupSize_, 1);
            Check(top_.Top == 56 && !top_.IntersectsWith(topRing_), "Top taskbar opens the card below the bar with an 8 DIP gap.");
            Check(Math.Abs(top_.Left + top_.Width / 2 - 612) < .01, "Horizontal cards center on the quota ring.");
            Rect bottomRing_ = new Rect(600, 1044, 24, 24);
            Rect bottom_ = QuotaHoverPresenter.ComputePopupBounds(bottomRing_, new Rect(0, 1032, 1920, 48), screen_, popupSize_, 1);
            Check(bottom_.Bottom == 1024 && !bottom_.IntersectsWith(bottomRing_), "Bottom taskbar opens above without covering the ring.");
            Rect leftRing_ = new Rect(12, 500, 24, 24);
            Rect left_ = QuotaHoverPresenter.ComputePopupBounds(leftRing_, new Rect(0, 0, 48, 1080), screen_, popupSize_, 1);
            Check(left_.Left == 56 && !left_.IntersectsWith(leftRing_), "Left taskbar opens toward the screen interior.");
            Rect rightRing_ = new Rect(1884, 500, 24, 24);
            Rect right_ = QuotaHoverPresenter.ComputePopupBounds(rightRing_, new Rect(1872, 0, 48, 1080), screen_, popupSize_, 1);
            Check(right_.Right == 1864 && !right_.IntersectsWith(rightRing_), "Right taskbar opens toward the screen interior.");
            Rect edge_ = QuotaHoverPresenter.ComputePopupBounds(new Rect(1, 10, 24, 24), topBar_, screen_, popupSize_, 1);
            Check(screen_.Contains(edge_) && edge_.Left == 0, "A ring near the screen edge keeps the full card visible.");
            Rect scaled_ = QuotaHoverPresenter.ComputePopupBounds(new Rect(900, 15, 36, 36), new Rect(0, 0, 2880, 72), new Rect(0, 0, 2880, 1620), new Size(585, 375), 1.5);
            Check(scaled_.Top == 84 && scaled_.Width == top_.Width * 1.5 && scaled_.Left == top_.Left * 1.5, "150 percent DPI scales the gap, size and aligned position exactly.");
            Rect tinyScreen_ = new Rect(0, 0, 300, 220);
            Rect tiny_ = QuotaHoverPresenter.ComputePopupBounds(new Rect(100, 10, 24, 24), new Rect(0, 0, 300, 48), tinyScreen_, popupSize_, 1);
            Check(tinyScreen_.Contains(tiny_) && tiny_.Top == 56 && tiny_.Height == 164 && tiny_.Width == 300, "Small screens constrain the card to the inward space.");
            Rect secondaryScreen_ = new Rect(-1920, -1080, 1920, 1080);
            Rect secondaryBounds_ = QuotaHoverPresenter.ComputePopupBounds(new Rect(-1600, -1070, 24, 24), new Rect(-1920, -1080, 1920, 48), secondaryScreen_, popupSize_, 1);
            Check(secondaryScreen_.Contains(secondaryBounds_) && secondaryBounds_.Top == -1024, "Negative monitor coordinates preserve inward taskbar placement.");

            presenter_ = new QuotaHoverPresenter();
            presenter_.Popup.Opacity = 0;
            presenter_.ShowAfterDelay("五小时额度", quota_, false, 200, 300, 1.5);
            Check(presenter_.IsPending && !presenter_.Popup.IsOpen, "Initial hover waits 300 ms.");
            presenter_.Close();
            PumpFor(340);
            Check(!presenter_.IsPending && !presenter_.Popup.IsOpen, "Mouse leave cancels delayed opening.");
            presenter_.ShowAfterDelay("五小时额度", quota_, false, 200, 300, 1.5);
            PumpFor(180);
            presenter_.SetAppearance(new HoverAppearance());
            presenter_.Update("五小时额度", quota_, true);
            presenter_.ShowAfterDelay("五小时额度", quota_, true, 200, 300, 1.5);
            PumpFor(165);
            Check(presenter_.Popup.IsOpen && !presenter_.IsPending, "Same-ring updates do not postpone the initial hover delay.");
            Check(Math.Abs(presenter_.Popup.HorizontalOffset - (200 / 1.5 - 18)) < .01 && presenter_.Popup.VerticalOffset < 300 / 1.5, "Physical hover position converts to DIPs and positions above the cursor.");
            Check(Text((FrameworkElement)presenter_.Popup.Content).Contains("数据已过期"), "Pending data updates reach opened content.");
            presenter_.SetAnchor(topRing_, topBar_, screen_);
            Check(presenter_.Popup.VerticalOffset == 56, "An open card immediately follows its new top-taskbar anchor.");
            Check(presenter_.Popup.DesiredSize.Width >= 320 && presenter_.Popup.DesiredSize.Width <= 340 && presenter_.Popup.DesiredSize.Height > 100, "Placement measures the compact popup including template padding and border.");
            HoverAppearance liveAppearance_ = new HoverAppearance { TitleFontSize = 15, ValueFontSize = 31, DetailFontSize = 17, ContentWidth = 420 };
            presenter_.SetAppearance(liveAppearance_);
            Check(presenter_.Popup.IsOpen && !presenter_.IsPending && presenter_.Popup.VerticalOffset == 56, "Live appearance changes remeasure the open popup without restarting delay or changing its anchored side.");
            double chromeWidth_ = presenter_.Popup.Padding.Left + presenter_.Popup.Padding.Right + presenter_.Popup.BorderThickness.Left + presenter_.Popup.BorderThickness.Right;
            Check(Math.Abs(presenter_.Popup.DesiredSize.Width - (420 + chromeWidth_)) < .01, "Live custom width includes the actual template padding exactly once.");
            liveAppearance_.ValueFontSize = 16;
            liveAppearance_.ContentWidth = 240;
            presenter_.Update("五小时额度", quota_, true);
            Check(Math.Abs(presenter_.Popup.DesiredSize.Width - (420 + chromeWidth_)) < .01, "Presenter retains an independent appearance copy after callers mutate their settings object.");
            foreach (TextBlock block_ in Blocks((FrameworkElement)presenter_.Popup.Content))
                if (block_.Name == "QuotaHoverRemaining") Check(block_.FontSize == 31, "Later data refresh retains the chosen quota text size.");
            presenter_.SetAppearance(null);
            Check(Math.Abs(presenter_.Popup.DesiredSize.Width - (300 + chromeWidth_)) < .01, "Null appearance restores compact defaults immediately.");
            presenter_.ShowAfterDelay("五小时额度", quota_, true, 1500, 900);
            Check(presenter_.Popup.VerticalOffset == 56, "Anchored placement ignores cursor movement over the same ring.");
            presenter_.SetAnchor(new Rect(100, 10, 24, 24), new Rect(0, 0, 300, 48), tinyScreen_);
            ScrollViewer scroll_ = presenter_.Popup.Content as ScrollViewer;
            Check(scroll_ != null && scroll_.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && presenter_.Popup.MaxHeight <= 164 && presenter_.Popup.MaxWidth <= 300, "Small-screen cards provide scrolling within the available inward area.");
            presenter_.SetAnchor(topRing_, topBar_, screen_);
            foreach (string mode_ in new[] { "Light", "Dark", "Light" })
            {
                UiTheme.Apply(mode_); PumpFor(20);
                presenter_.Popup.ApplyTemplate();
                Border surface_ = presenter_.Popup.Template.FindName("TooltipSurface", presenter_.Popup) as Border;
                Check(surface_ != null && surface_.CornerRadius.TopLeft == 8, mode_ + " replaces native yellow tooltip chrome with the rounded template.");
                Color background_ = ((SolidColorBrush)presenter_.Popup.Background).Color;
                Color foreground_ = ((SolidColorBrush)presenter_.Popup.Foreground).Color;
                Check(background_.R == background_.G && background_.G == background_.B && foreground_.R == foreground_.G && foreground_.G == foreground_.B, mode_ + " popup uses only neutral gray, black and white.");
                Check(mode_ == "Light" ? background_.R > 200 && foreground_.R < 50 : background_.R < 60 && foreground_.R > 240, mode_ + " popup has the requested light/dark polarity: " + background_ + " / " + foreground_);
                Brush secondary_ = (Brush)application_.Resources["QuotaTooltipSecondary"];
                Check(Contrast(presenter_.Popup.Foreground, presenter_.Popup.Background) >= 7 && Contrast(secondary_, presenter_.Popup.Background) >= 4.5, mode_ + " primary and secondary text remain readable.");
                foreach (TextBlock block_ in Blocks((FrameworkElement)presenter_.Popup.Content))
                    Check(Contrast(block_.Foreground, presenter_.Popup.Background) >= 4.5, mode_ + " existing open popup text follows theme changes.");
            }
            presenter_.Dispose();
            Check(!presenter_.Popup.IsOpen && !presenter_.IsPending && presenter_.Popup.Content == null, "Dispose closes and releases popup content.");
            presenter_.ShowAfterDelay("周额度", quota_, false, 0, 0);
            Check(!presenter_.IsPending, "Disposed presenter cannot reschedule hover work.");
            Console.WriteLine("PASS: " + count + " themed hover content, placement and lifecycle checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_);
            return 1;
        }
        finally
        {
            if (presenter_ != null) presenter_.Dispose();
            application_.Shutdown();
        }
    }
}
