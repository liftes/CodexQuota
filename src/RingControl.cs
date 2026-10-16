using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CodexQuota
{
    public static class QuotaDisplayFormatter
    {
        // Server timestamps are authoritative; local countdowns never renew quota.
        public static double? TimeRemainingFraction(QuotaWindow quota, DateTimeOffset nowUtc)
        {
            if (quota == null || !quota.ResetsAt.HasValue || quota.WindowDurationMins <= 0) return null;
            double fraction_ = (quota.ResetsAt.Value - nowUtc).TotalSeconds / (quota.WindowDurationMins * 60.0);
            return Math.Max(0, Math.Min(1, fraction_));
        }

        public static string FormatCenter(string label, QuotaWindow quota, string centerDisplay, DateTimeOffset nowUtc)
        {
            if (centerDisplay == "None") return "";
            if (centerDisplay == "ResetPercent")
            {
                double? fraction_ = TimeRemainingFraction(quota, nowUtc);
                return fraction_.HasValue ? (fraction_.Value * 100).ToString("0", CultureInfo.InvariantCulture) : "?";
            }
            if (quota == null) return "?";
            return centerDisplay == "Percent" ? quota.RemainingPercent.ToString("0", CultureInfo.InvariantCulture) : label;
        }

        public static string FormatHover(string label, QuotaWindow quota, bool isStale)
        {
            return FormatHover(label, quota, isStale, DateTimeOffset.UtcNow);
        }

        public static string FormatHover(string label, QuotaWindow quota, bool isStale, DateTimeOffset nowUtc)
        {
            if (quota == null) return label + "\n未返回该额度窗口 / 额度未知" + (isStale ? "\n数据已过期，等待刷新" : "");
            string text_ = label + "\n额度剩余 " + quota.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
            if (quota.ResetsAt.HasValue)
            {
                text_ += "\n重置：" + quota.ResetsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                double? fraction_ = TimeRemainingFraction(quota, nowUtc);
                if (fraction_.HasValue) text_ += "\n时间剩余 " + (fraction_.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%（按完整窗口计算）";
                TimeSpan remaining_ = quota.ResetsAt.Value - nowUtc;
                if (remaining_.TotalSeconds <= 0) text_ += "\n已到预计重置时间，等待服务端确认";
                else if (remaining_.TotalDays >= 1) text_ += "\n距离重置：" + (int)remaining_.TotalDays + " 天 " + remaining_.Hours + " 小时";
                else if (remaining_.TotalHours >= 1) text_ += "\n距离重置：" + (int)remaining_.TotalHours + " 小时 " + remaining_.Minutes + " 分钟";
                else text_ += "\n距离重置：" + Math.Max(1, (int)Math.Ceiling(remaining_.TotalMinutes)) + " 分钟";
            }
            else text_ += "\n服务端未返回重置时间";
            if (isStale) text_ += "\n数据已过期，等待刷新";
            return text_;
        }
    }

    public sealed class RingControl : FrameworkElement
    {
        public string Label { get; set; }
        public QuotaWindow Quota { get; set; }
        public bool IsStale { get; set; }
        public string CenterDisplay { get; set; }
        public Color RemainingColor { get; set; }
        public Color TrackColor { get; set; }
        public double TrackOpacity { get; set; }
        public bool ShowResetRing { get; set; }
        public string ResetRingPlacement { get; set; }
        public Color ResetColor { get; set; }
        public DateTimeOffset? NowUtc { get; set; }

        public RingControl()
        {
            Width = Height = 20;
            CenterDisplay = "Label";
            RemainingColor = Color.FromRgb(185, 189, 197);
            TrackColor = Color.FromRgb(147, 152, 161);
            TrackOpacity = 0.25;
            ResetRingPlacement = "Outer";
            ResetColor = Color.FromRgb(146, 153, 165);
            // Settings choose app defaults independently; preserve existing consumers.
            ShowResetRing = false;
            SnapsToDevicePixels = true;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            double size_ = Math.Min(ActualWidth, ActualHeight);
            if (size_ < 1) return;
            // A one-alpha surface keeps the hollow center hoverable in a layered window.
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            double lineWidth_ = Math.Max(1.6, size_ / 10);
            double center_ = size_ / 2;
            double radius_ = center_ - lineWidth_ / 2 - 0.5;
            double resetWidth_ = Math.Max(0.9, size_ / 22);
            double gap_ = Math.Max(0.7, size_ / 28);
            double resetRadius_ = 0;
            double textRadius_ = radius_ - lineWidth_ / 2;
            if (ShowResetRing)
            {
                if (ResetRingPlacement == "Inner")
                {
                    resetRadius_ = radius_ - lineWidth_ / 2 - gap_ - resetWidth_ / 2;
                    textRadius_ = resetRadius_ - resetWidth_ / 2;
                }
                else
                {
                    resetRadius_ = center_ - 0.5 - resetWidth_ / 2;
                    radius_ = resetRadius_ - resetWidth_ / 2 - gap_ - lineWidth_ / 2;
                    textRadius_ = radius_ - lineWidth_ / 2;
                }
            }
            Point centerPoint_ = new Point(center_, center_);
            Color track_ = TrackColor;
            track_.A = (byte)Math.Round(track_.A * Math.Max(0, Math.Min(1, TrackOpacity)));
            DateTimeOffset nowUtc_ = NowUtc ?? DateTimeOffset.UtcNow;
            DrawRing(drawingContext, centerPoint_, Math.Max(0.1, radius_), lineWidth_, track_, RemainingColor,
                Quota == null ? (double?)null : Quota.RemainingPercent / 100);
            if (ShowResetRing && resetRadius_ > 0)
                DrawRing(drawingContext, centerPoint_, resetRadius_, resetWidth_, track_, ResetColor,
                    QuotaDisplayFormatter.TimeRemainingFraction(Quota, nowUtc_));
            string label_ = QuotaDisplayFormatter.FormatCenter(Label, Quota, CenterDisplay, nowUtc_);
            if (!string.IsNullOrEmpty(label_))
            {
                double fontSize_ = size_ * (CenterDisplay == "Percent" || CenterDisplay == "ResetPercent" ? 0.35 : 0.4);
                FormattedText text_ = MakeText(label_, fontSize_);
                double availableWidth_ = Math.Max(1, 2 * textRadius_ - 1);
                if (text_.Width > availableWidth_) text_ = MakeText(label_, fontSize_ * availableWidth_ / text_.Width);
                drawingContext.DrawText(text_, new Point(center_ - text_.Width / 2, center_ - text_.Height / 2));
            }
            if (IsStale) drawingContext.DrawEllipse(new SolidColorBrush(RemainingColor), null, new Point(size_ - 1.5, 1.5), 1.3, 1.3);
        }

        private static void DrawRing(DrawingContext drawingContext, Point center, double radius, double lineWidth, Color track, Color remaining, double? fraction)
        {
            Pen trackPen_ = new Pen(new SolidColorBrush(track), lineWidth);
            drawingContext.DrawEllipse(null, trackPen_, center, radius, radius);
            if (!fraction.HasValue || Double.IsNaN(fraction.Value) || fraction.Value <= 0) return;
            Pen arcPen_ = new Pen(new SolidColorBrush(remaining), lineWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            double angle_ = Math.Min(1, fraction.Value) * 360;
            if (angle_ >= 359.99)
            {
                drawingContext.DrawEllipse(null, arcPen_, center, radius, radius);
                return;
            }
            double radians_ = (angle_ - 90) * Math.PI / 180;
            Point end_ = new Point(center.X + radius * Math.Cos(radians_), center.Y + radius * Math.Sin(radians_));
            StreamGeometry geometry_ = new StreamGeometry();
            using (StreamGeometryContext context_ = geometry_.Open())
            {
                context_.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                context_.ArcTo(end_, new Size(radius, radius), 0, angle_ > 180, SweepDirection.Clockwise, true, false);
            }
            drawingContext.DrawGeometry(null, arcPen_, geometry_);
        }

        private FormattedText MakeText(string text, double size)
        {
            Color color_ = CenterDisplay == "ResetPercent" ? ResetColor : RemainingColor;
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size, new SolidColorBrush(color_), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }
}
