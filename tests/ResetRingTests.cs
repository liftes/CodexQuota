using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuota;

internal static class ResetRingTests
{
    private static int count;
    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }
    private static void CheckFraction(QuotaWindow quota, DateTimeOffset nowUtc, double expected, string message)
    {
        double? fraction_ = QuotaDisplayFormatter.TimeRemainingFraction(quota, nowUtc);
        Check(fraction_.HasValue && Math.Abs(fraction_.Value - expected) < 0.000001, message);
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            DateTimeOffset nowUtc_ = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
            QuotaWindow quota_ = new QuotaWindow { UsedPercent = 72, WindowDurationMins = 300, ResetsAt = nowUtc_.AddMinutes(150) };
            CheckFraction(quota_, nowUtc_, 0.5, "Five-hour window uses the complete server duration.");
            Check(QuotaDisplayFormatter.FormatHover("5h", quota_, false, nowUtc_).Contains("时间剩余 50%"), "Hover distinguishes reset-time percentage from quota percentage.");
            Check(QuotaDisplayFormatter.FormatCenter("5h", quota_, "ResetPercent", nowUtc_) == "50", "Reset center shows time percentage.");
            Check(QuotaDisplayFormatter.FormatCenter("5h", quota_, "Percent", nowUtc_) == "28", "Quota center remains quota percentage.");
            Check(QuotaDisplayFormatter.FormatCenter("5h", quota_, "Label", nowUtc_) == "5h", "Label mode remains unchanged.");
            Check(QuotaDisplayFormatter.FormatCenter("5h", quota_, "None", nowUtc_) == "", "Empty center mode remains unchanged.");
            quota_.WindowDurationMins = 10080;
            quota_.ResetsAt = nowUtc_.AddDays(3.5);
            CheckFraction(quota_, nowUtc_, 0.5, "Weekly window uses actual reset, not a fixed weekday.");
            quota_.ResetsAt = nowUtc_.AddDays(7);
            CheckFraction(quota_, nowUtc_, 1, "Exactly one complete weekly window is 100 percent.");
            quota_.ResetsAt = nowUtc_.AddDays(9);
            CheckFraction(quota_, nowUtc_, 1, "Future timestamps cannot exceed 100 percent.");
            quota_.ResetsAt = nowUtc_;
            CheckFraction(quota_, nowUtc_, 0, "Exactly reset time is zero percent.");
            quota_.ResetsAt = nowUtc_.AddMinutes(-5);
            CheckFraction(quota_, nowUtc_, 0, "Past reset remains zero percent.");
            Check(QuotaDisplayFormatter.FormatHover("周", quota_, false, nowUtc_).Contains("等待服务端确认"), "Expired reset awaits authoritative refresh.");
            Check(quota_.RemainingPercent == 28, "Local countdown never renews quota.");
            quota_.ResetsAt = nowUtc_.AddDays(3.5).ToOffset(TimeSpan.FromHours(8));
            CheckFraction(quota_, nowUtc_.ToOffset(TimeSpan.FromHours(-7)), 0.5, "Offsets do not change the remaining fraction.");
            Check(!QuotaDisplayFormatter.TimeRemainingFraction(null, nowUtc_).HasValue, "Missing quota has unknown reset percentage.");
            quota_.ResetsAt = null;
            Check(!QuotaDisplayFormatter.TimeRemainingFraction(quota_, nowUtc_).HasValue, "Missing reset has unknown percentage.");
            Check(QuotaDisplayFormatter.FormatCenter("周", quota_, "ResetPercent", nowUtc_) == "?", "Missing reset center is unknown.");
            quota_.ResetsAt = nowUtc_.AddHours(1);
            quota_.WindowDurationMins = 0;
            Check(!QuotaDisplayFormatter.TimeRemainingFraction(quota_, nowUtc_).HasValue, "Zero duration cannot form a percentage.");
            quota_.WindowDurationMins = -1;
            Check(!QuotaDisplayFormatter.TimeRemainingFraction(quota_, nowUtc_).HasValue, "Negative duration cannot form a percentage.");
            Check(new RingControl().ShowResetRing == false, "Legacy consumers retain single-ring rendering by default.");
            quota_.WindowDurationMins = 300;
            quota_.ResetsAt = nowUtc_.AddMinutes(150);
            foreach (string placement_ in new[] { "Outer", "Inner" })
            {
                byte[] pixels_ = Render(quota_, nowUtc_, placement_, "None");
                Check(CountColor(pixels_, true) > 5, placement_ + " time ring renders in an independent color.");
                Check(CountColor(pixels_, false) > 5, placement_ + " quota ring remains visible.");
                Check(pixels_[(10 * 20 + 10) * 4 + 3] <= 1, placement_ + " center stays hollow.");
                Render(quota_, nowUtc_, placement_, "ResetPercent");
                Check(true, placement_ + " time center renders without layout errors.");
            }
            quota_.ResetsAt = nowUtc_.AddMinutes(-1);
            byte[] expired_ = Render(quota_, nowUtc_, "Outer", "None");
            Check(CountColor(expired_, true) == 0 && CountColor(expired_, false) > 5, "Expired reset removes time arc without touching quota arc.");
            Console.WriteLine("PASS: " + count + " reset-time fraction, hover, center and dual-ring rendering checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_.Message);
            return 1;
        }
    }
    private static byte[] Render(QuotaWindow quota, DateTimeOffset nowUtc, string placement, string centerDisplay)
    {
        RingControl ring_ = new RingControl
        {
            Width = 20, Height = 20, Quota = quota, NowUtc = nowUtc,
            ShowResetRing = true, ResetRingPlacement = placement, CenterDisplay = centerDisplay,
            RemainingColor = Colors.Red, ResetColor = Colors.Blue, TrackOpacity = 0
        };
        ring_.Measure(new Size(20, 20)); ring_.Arrange(new Rect(0, 0, 20, 20)); ring_.UpdateLayout();
        RenderTargetBitmap bitmap_ = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
        bitmap_.Render(ring_);
        byte[] pixels_ = new byte[20 * 20 * 4];
        bitmap_.CopyPixels(pixels_, 80, 0);
        return pixels_;
    }
    private static int CountColor(byte[] pixels, bool blue)
    {
        int total_ = 0;
        for (int index_ = 0; index_ < pixels.Length; index_ += 4)
            if (pixels[index_ + (blue ? 0 : 2)] > 10 && pixels[index_ + (blue ? 2 : 0)] < 5) total_++;
        return total_;
    }
}
