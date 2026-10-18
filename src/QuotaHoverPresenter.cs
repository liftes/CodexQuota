using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexQuota
{
    // Both the native taskbar host and the WPF overlay use the same themed content.
    // This presenter owns only a local popup and timer; it never reads the network.
    public sealed class QuotaHoverPresenter : IDisposable
    {
        private readonly DispatcherTimer showTimer;
        private string pendingLabel;
        private QuotaWindow pendingQuota;
        private bool pendingStale;
        private double screenX, screenY, screenScale = 1;
        private Rect ringBounds, taskbarBounds, screenBounds;
        private bool hasAnchor;
        private bool disposed;
        private bool layoutRefreshPending;
        private HoverAppearance appearance = new HoverAppearance();
        public ToolTip Popup { get; private set; }
        public bool IsPending { get { return pendingLabel != null && showTimer.IsEnabled; } }

        public QuotaHoverPresenter()
        {
            // AbsolutePoint also considers MenuDropAlignment and may align the
            // popup's RIGHT edge to our left edge on tablet/right-aligned systems.
            Popup = new ToolTip { Placement = PlacementMode.Absolute, StaysOpen = true, MaxWidth = 330, Focusable = false };
            Popup.Opened += OnPopupOpened;
            Popup.SizeChanged += OnPopupSizeChanged;
            Popup.SetResourceReference(Control.BackgroundProperty, "QuotaTooltipSurface");
            Popup.SetResourceReference(Control.ForegroundProperty, "QuotaTooltipText");
            UiTheme.ThemeChanged += OnThemeChanged;
            showTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(300) };
            showTimer.Tick += OnShowTimer;
        }

        public void SetAppearance(HoverAppearance hoverAppearance)
        {
            if (disposed) return;
            appearance = (hoverAppearance ?? new HoverAppearance()).Clone().Normalize();
            if (Popup.IsOpen) RefreshContent();
        }

        public void SetAnchor(Rect ringBoundsPhysical, Rect taskbarBoundsPhysical, Rect screenBoundsPhysical, double scale = 1)
        {
            ringBounds = ringBoundsPhysical;
            taskbarBounds = taskbarBoundsPhysical;
            screenBounds = screenBoundsPhysical;
            screenScale = ValidScale(scale);
            hasAnchor = IsValidRect(ringBounds) && IsValidRect(taskbarBounds) && IsValidRect(screenBounds);
            if (!disposed && Popup.IsOpen) RefreshContent();
        }

        public void ShowAfterDelay(string label, QuotaWindow quota, bool stale, double physicalX, double physicalY, double scale = 1)
        {
            if (disposed) return;
            bool sameCandidate_ = pendingLabel == label;
            if (!sameCandidate_) Close();
            pendingLabel = label;
            pendingQuota = quota;
            pendingStale = stale;
            screenX = physicalX;
            screenY = physicalY;
            if (!hasAnchor) screenScale = ValidScale(scale);
            if (Popup.IsOpen) RefreshContent();
            else if (!sameCandidate_) showTimer.Start();
        }

        public void Update(string label, QuotaWindow quota, bool stale)
        {
            if (disposed || pendingLabel != label) return;
            pendingQuota = quota;
            pendingStale = stale;
            if (Popup.IsOpen) RefreshContent();
        }

        private void OnShowTimer(object sender, EventArgs eventArgs)
        {
            showTimer.Stop();
            if (disposed || pendingLabel == null) return;
            RefreshContent();
            Popup.IsOpen = true;
        }

        private void RefreshContent()
        {
            Vector popupScale_ = GetPopupScale();
            FrameworkElement content_ = BuildContent(pendingLabel, pendingQuota, pendingStale, DateTimeOffset.UtcNow, appearance);
            // Measure the real tooltip template, including its padding and border.
            // A ScrollViewer keeps enlarged text accessible on unusually small screens.
            ScrollViewer scroll_ = new ScrollViewer { Content = content_, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Popup.Content = scroll_;
            Popup.ApplyTemplate();
            double horizontalChrome_ = Popup.Padding.Left + Popup.Padding.Right + Popup.BorderThickness.Left + Popup.BorderThickness.Right;
            double desiredWidth_ = appearance.ContentWidth + horizontalChrome_;
            Popup.MaxWidth = hasAnchor ? Math.Max(1, Math.Min(desiredWidth_, screenBounds.Width / popupScale_.X)) : desiredWidth_;
            Popup.MaxHeight = hasAnchor ? Math.Max(1, screenBounds.Height / popupScale_.Y) : Double.PositiveInfinity;
            content_.Width = Math.Max(1, Math.Min(appearance.ContentWidth, Popup.MaxWidth - horizontalChrome_));
            content_.MaxWidth = content_.Width;
            Popup.Measure(new Size(Popup.MaxWidth, Popup.MaxHeight));
            Size measured_ = Popup.DesiredSize;
            if (hasAnchor)
            {
                Rect bounds_ = ComputePopupBounds(ringBounds, taskbarBounds, screenBounds,
                    new Size(measured_.Width * popupScale_.X, measured_.Height * popupScale_.Y), screenScale);
                Popup.MaxWidth = bounds_.Width / popupScale_.X;
                Popup.MaxHeight = bounds_.Height / popupScale_.Y;
                // A vertical scrollbar takes a small part of the available text width.
                content_.Width = Math.Max(1, Math.Min(appearance.ContentWidth, Popup.MaxWidth - horizontalChrome_ - (measured_.Height > Popup.MaxHeight ? SystemParameters.VerticalScrollBarWidth : 0)));
                content_.MaxWidth = content_.Width;
                Popup.Measure(new Size(Popup.MaxWidth, Popup.MaxHeight));
                bounds_ = ComputePopupBounds(ringBounds, taskbarBounds, screenBounds,
                    new Size(Popup.DesiredSize.Width * popupScale_.X, Popup.DesiredSize.Height * popupScale_.Y), screenScale);
                SetPhysicalOrigin(bounds_.Left, bounds_.Top);
            }
            else
            {
                SetPhysicalOrigin(screenX - 18 * popupScale_.X, screenY - (measured_.Height + 8) * popupScale_.Y);
            }
        }

        private Vector GetPopupScale()
        {
            // The native taskbar and WPF popup need not have the same DPI.
            // Before opening there is no HWND; remeasure against its real scale
            // once WPF has connected it, and after a size/DPI transition.
            PresentationSource source_ = PresentationSource.FromVisual(Popup);
            if (source_ != null && source_.CompositionTarget != null)
            {
                Matrix transform_ = source_.CompositionTarget.TransformToDevice;
                return new Vector(ValidScale(transform_.M11), ValidScale(transform_.M22));
            }
            return new Vector(screenScale, screenScale);
        }

        private void SetPhysicalOrigin(double x, double y)
        {
            // WPF's Absolute PlacementRectangle is already in physical screen
            // coordinates. Offsets, in contrast, are transformed by the popup's
            // own DPI. Keep them zero rather than dividing by the taskbar DPI.
            Popup.HorizontalOffset = 0;
            Popup.VerticalOffset = 0;
            Popup.PlacementRectangle = new Rect(x, y, 0, 0);
        }

        private void OnPopupOpened(object sender, RoutedEventArgs eventArgs)
        {
            if (!disposed && pendingLabel != null) RefreshContent();
        }

        private void OnPopupSizeChanged(object sender, SizeChangedEventArgs eventArgs)
        {
            if (disposed || !Popup.IsOpen || layoutRefreshPending) return;
            layoutRefreshPending = true;
            Popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                layoutRefreshPending = false;
                if (!disposed && Popup.IsOpen && pendingLabel != null) RefreshContent();
            }));
        }

        public static Rect ComputePopupBounds(Rect ring, Rect bar, Rect screen, Size popupSizePhysical, double scale)
        {
            if (!IsValidRect(screen)) return Rect.Empty;
            double gap_ = 8 * ValidScale(scale);
            double width_ = Math.Min(screen.Width, Math.Max(1, popupSizePhysical.Width));
            double height_ = Math.Min(screen.Height, Math.Max(1, popupSizePhysical.Height));
            double x_ = ring.Left + ring.Width / 2 - width_ / 2;
            double y_ = ring.Top + ring.Height / 2 - height_ / 2;
            if (bar.Width >= bar.Height)
            {
                if (Math.Abs(bar.Top - screen.Top) <= Math.Abs(screen.Bottom - bar.Bottom))
                {
                    y_ = Math.Max(bar.Bottom, ring.Bottom) + gap_;
                    height_ = Math.Min(height_, Math.Max(1, screen.Bottom - y_));
                }
                else
                {
                    double edge_ = Math.Min(bar.Top, ring.Top) - gap_;
                    height_ = Math.Min(height_, Math.Max(1, edge_ - screen.Top));
                    y_ = edge_ - height_;
                }
            }
            else
            {
                if (Math.Abs(bar.Left - screen.Left) <= Math.Abs(screen.Right - bar.Right))
                {
                    x_ = Math.Max(bar.Right, ring.Right) + gap_;
                    width_ = Math.Min(width_, Math.Max(1, screen.Right - x_));
                }
                else
                {
                    double edge_ = Math.Min(bar.Left, ring.Left) - gap_;
                    width_ = Math.Min(width_, Math.Max(1, edge_ - screen.Left));
                    x_ = edge_ - width_;
                }
            }
            x_ = Math.Max(screen.Left, Math.Min(screen.Right - width_, x_));
            y_ = Math.Max(screen.Top, Math.Min(screen.Bottom - height_, y_));
            return new Rect(x_, y_, width_, height_);
        }

        private static double ValidScale(double scale)
        {
            return Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0 ? 1 : scale;
        }

        private static bool IsValidRect(Rect bounds)
        {
            return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 && !Double.IsInfinity(bounds.Width) && !Double.IsInfinity(bounds.Height) && !Double.IsNaN(bounds.X) && !Double.IsNaN(bounds.Y);
        }

        private void OnThemeChanged()
        {
            if (disposed) return;
            // A native-host tooltip has no WPF placement target/logical ancestor.
            // Explicitly refresh its values because resource invalidation does not
            // reach a detached popup when Application.Resources entries change.
            Popup.Background = (System.Windows.Media.Brush)Application.Current.Resources["QuotaTooltipSurface"];
            Popup.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["QuotaTooltipText"];
            Popup.BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["QuotaTooltipBorder"];
            if (Popup.IsOpen) RefreshContent();
        }

        public void Close()
        {
            showTimer.Stop();
            Popup.IsOpen = false;
            pendingLabel = null;
            pendingQuota = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Close();
            showTimer.Tick -= OnShowTimer;
            UiTheme.ThemeChanged -= OnThemeChanged;
            Popup.Opened -= OnPopupOpened;
            Popup.SizeChanged -= OnPopupSizeChanged;
            Popup.Content = null;
            disposed = true;
        }

        public static FrameworkElement BuildContent(string label, QuotaWindow quota, bool stale, DateTimeOffset now, HoverAppearance hoverAppearance = null)
        {
            HoverAppearance appearance_ = (hoverAppearance ?? new HoverAppearance()).Clone().Normalize();
            StackPanel panel_ = new StackPanel { Width = appearance_.ContentWidth, MaxWidth = appearance_.ContentWidth };
            TextBlock heading_ = MakeText(label, appearance_.TitleFontSize, false);
            heading_.Name = "QuotaHoverHeading";
            heading_.FontWeight = FontWeights.SemiBold;
            panel_.Children.Add(heading_);
            TextBlock remaining_ = MakeText(quota == null ? "额度未知" : quota.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%", appearance_.ValueFontSize, false);
            remaining_.Name = "QuotaHoverRemaining";
            remaining_.FontWeight = FontWeights.SemiBold;
            remaining_.Margin = new Thickness(0, 4, 0, 0);
            if (quota != null)
            {
                Run suffix_ = new Run("  剩余") { FontSize = appearance_.DetailFontSize, FontWeight = FontWeights.Normal };
                suffix_.SetResourceReference(TextElement.ForegroundProperty, "QuotaTooltipSecondary");
                remaining_.Inlines.Add(suffix_);
            }
            panel_.Children.Add(remaining_);
            Border divider_ = new Border { Height = 1, Margin = new Thickness(0, 9, 0, 8) };
            divider_.SetResourceReference(Border.BackgroundProperty, "QuotaTooltipBorder");
            panel_.Children.Add(divider_);

            if (quota == null)
                panel_.Children.Add(MakeText("服务器尚未返回此额度，重置时间未知。", appearance_.DetailFontSize, true));
            else if (!quota.ResetsAt.HasValue)
                panel_.Children.Add(MakeText("服务器未提供重置时间。", appearance_.DetailFontSize, true));
            else
            {
                AddDetail(panel_, "重置时间", quota.ResetsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), appearance_);
                TimeSpan remainingTime_ = quota.ResetsAt.Value - now;
                if (remainingTime_.TotalSeconds > 0)
                    AddDetail(panel_, "距重置", FormatCountdown(remainingTime_), appearance_);
                double? timeFraction_ = QuotaDisplayFormatter.TimeRemainingFraction(quota, now);
                if (timeFraction_.HasValue)
                    AddDetail(panel_, "时间剩余", (timeFraction_.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%", appearance_);
                if (remainingTime_.TotalSeconds <= 0)
                {
                    TextBlock expired_ = MakeText("已到预计重置时间，等待服务器确认。", appearance_.DetailFontSize, true);
                    expired_.Margin = new Thickness(0, 5, 0, 0);
                    panel_.Children.Add(expired_);
                }
            }
            if (stale)
            {
                TextBlock stale_ = MakeText("数据已过期 · 等待刷新", appearance_.DetailFontSize, true);
                stale_.Margin = new Thickness(0, 7, 0, 0);
                panel_.Children.Add(stale_);
            }
            return panel_;
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining.TotalDays >= 1) return ((int)remaining.TotalDays).ToString(CultureInfo.InvariantCulture) + " 天 " + remaining.Hours + " 小时 " + remaining.Minutes + " 分钟";
            if (remaining.TotalHours >= 1) return ((int)remaining.TotalHours).ToString(CultureInfo.InvariantCulture) + " 小时 " + remaining.Minutes + " 分钟";
            return remaining.Minutes + " 分 " + Math.Max(1, remaining.Seconds) + " 秒";
        }

        private static TextBlock MakeText(string text, double fontSize, bool secondary)
        {
            TextBlock block_ = new TextBlock { Text = text, FontSize = fontSize, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Segoe UI") };
            block_.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "QuotaTooltipSecondary" : "QuotaTooltipText");
            return block_;
        }

        private static void AddDetail(Panel panel, string label, string value, HoverAppearance hoverAppearance)
        {
            Grid row_ = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row_.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(65, hoverAppearance.DetailFontSize * 5.3)) });
            row_.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row_.Children.Add(MakeText(label, hoverAppearance.DetailFontSize, true));
            TextBlock value_ = MakeText(value, hoverAppearance.DetailFontSize, false);
            Grid.SetColumn(value_, 1);
            row_.Children.Add(value_);
            panel.Children.Add(row_);
        }
    }
}
