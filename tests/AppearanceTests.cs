using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuota;

internal static class AppearanceTests
{
    private static int count;

    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            UserSettings defaults_ = new UserSettings();
            Check(defaults_.RingSize == 20 && defaults_.CenterDisplay == "Label", "Compact default size and center mode.");
            UserSettings migrated_ = UserSettings.Parse("{\"FiveHourMode\":\"Hide\",\"PositionMode\":\"StartLeft\",\"OffsetX\":-70,\"OffsetY\":2,\"RefreshSeconds\":120,\"CodexPath\":\"C:\\\\Codex\\\\codex.exe\"}");
            Check(migrated_.OffsetX == -70 && migrated_.OffsetY == 2, "Existing working offsets survive v0.1 migration.");
            Check(migrated_.FiveHourMode == "Hide" && migrated_.PositionMode == "StartLeft" && migrated_.RefreshSeconds == 120, "Existing behavior survives migration.");
            Check(migrated_.RingSize == 20 && migrated_.TrackOpacity == 0.25 && migrated_.SettingsVersion == 7, "Missing appearance fields receive current defaults.");
            Check(migrated_.HoverTitleFontSize == 12 && migrated_.HoverValueFontSize == 22 && migrated_.HoverDetailFontSize == 13 && migrated_.HoverContentWidth == 300,"Older settings receive a compact hover appearance.");
            UserSettings hoverCustom_ = UserSettings.Parse("{\"OffsetX\":-40,\"OffsetY\":1,\"RingGap\":13,\"HoverTitleFontSize\":16,\"HoverValueFontSize\":32,\"HoverDetailFontSize\":17,\"HoverContentWidth\":420}");
            Check(hoverCustom_.HoverTitleFontSize == 16 && hoverCustom_.HoverValueFontSize == 32 && hoverCustom_.HoverDetailFontSize == 17 && hoverCustom_.HoverContentWidth == 420 && hoverCustom_.OffsetX == -40 && hoverCustom_.OffsetY == 1 && hoverCustom_.RingGap == 13,"Hover customization and existing calibrated placement survive loading.");
            UserSettings hoverInvalid_ = UserSettings.Parse("{\"HoverTitleFontSize\":100,\"HoverValueFontSize\":-1,\"HoverDetailFontSize\":50,\"HoverContentWidth\":10}");
            Check(hoverInvalid_.HoverTitleFontSize == 20 && hoverInvalid_.HoverValueFontSize == 16 && hoverInvalid_.HoverDetailFontSize == 24 && hoverInvalid_.HoverContentWidth == 240,"Invalid hover values use supported size limits.");
            hoverCustom_.HoverValueFontSize = double.NaN; hoverCustom_.HoverContentWidth = double.PositiveInfinity; hoverCustom_.Normalize();
            Check(hoverCustom_.HoverValueFontSize == 22 && hoverCustom_.HoverContentWidth == 300,"Nonfinite hover appearance cannot corrupt popup geometry.");
            Check(migrated_.RingGap == 6, "Older settings retain the previous six-DIP ring gap.");
            Check(UserSettings.Parse("{\"RingGap\":0}").RingGap == 0 && UserSettings.Parse("{\"RingGap\":40}").RingGap == 40,"Gap endpoints are configurable.");
            Check(UserSettings.Parse("{\"RingGap\":500}").RingGap == 40 && UserSettings.Parse("{\"RingGap\":-5}").RingGap == 0,"Invalid gap values stay within safe bounds.");
            Check(RingLayout.WindowWidth(20,6,2) == 78 && RingLayout.WindowWidth(20,6,1) == 42,"Default layout preserves calibrated window footprint.");
            Check(RingLayout.RingLeft(98,20,26,2,1) - RingLayout.RingLeft(98,20,26,2,0) == 46,"Circle centers reflect the requested gap.");
            Check(RingLayout.GroupWidth(16,0,2) == 32 && RingLayout.GroupWidth(28,40,1) == 28,"Zero gap and hidden five-hour layout use only visible circles.");
            Check(migrated_.DisplayMode == "Taskbar", "Older settings opt into taskbar attachment.");
            UserSettings v03_ = UserSettings.Parse("{\"SettingsVersion\":3,\"OffsetX\":13,\"OffsetY\":-10,\"CenterDisplay\":\"Percent\",\"ThemeMode\":\"Dark\",\"TrackOpacity\":0.15}");
            Check(v03_.OffsetX == 13 && v03_.OffsetY == -10 && v03_.CenterDisplay == "Percent" && v03_.ThemeMode == "Dark" && v03_.TrackOpacity == 0.15 && v03_.DisplayMode == "Taskbar", "v0.3 settings survive attachment migration.");
            Check(UserSettings.Parse("{\"DisplayMode\":\"Floating\"}").DisplayMode == "Floating", "Explicit floating compatibility mode is preserved.");
            Check(UserSettings.Parse("{\"DisplayMode\":\"invalid\"}").DisplayMode == "Taskbar", "Invalid integration setting receives a valid default.");
            Check(UserSettings.Parse("null").RingSize == 20, "Null settings receive defaults.");
            UserSettings v02_ = UserSettings.Parse("{\"SettingsVersion\":2,\"OffsetX\":-70,\"OffsetY\":-20,\"RingSize\":22,\"CenterDisplay\":\"Percent\"}");
            Check(v02_.OffsetX == -70 && v02_.OffsetY == -20 && v02_.RingSize == 22 && v02_.CenterDisplay == "Percent","v0.2 calibrated position and appearance survive v0.3 migration.");
            Check(v02_.ShowResetRing && v02_.AdaptivePolling && v02_.ThemeMode == "System","New v0.3 features have safe migration defaults.");
            UserSettings explicit_ = UserSettings.Parse("{\"ShowResetRing\":false,\"AdaptivePolling\":false,\"HideFullscreen\":false,\"ThemeMode\":\"Dark\",\"ResetRingPlacement\":\"Inner\",\"CenterDisplay\":\"ResetPercent\"}");
            Check(!explicit_.ShowResetRing && !explicit_.AdaptivePolling && !explicit_.HideFullscreen && explicit_.ThemeMode == "Dark" && explicit_.ResetRingPlacement == "Inner" && explicit_.CenterDisplay == "ResetPercent","Explicit v0.3 display and polling choices are preserved.");
            UserSettings invalid_ = UserSettings.Parse("{\"RingSize\":100,\"TrackOpacity\":-0.5,\"CenterDisplay\":\"Invalid\",\"WeeklyColor\":\"red\",\"FiveHourColor\":\"#zz0000\"}");
            Check(invalid_.RingSize == 28 && invalid_.TrackOpacity == 0 && invalid_.CenterDisplay == "Label", "Out-of-range settings are normalized.");
            Check(invalid_.WeeklyColor == "#FFB9BDC5" && invalid_.FiveHourColor == "#FFB9BDC5", "Invalid colors use documented defaults.");
            Check(UserSettings.NormalizeColor("#12aBcD", "bad") == "#FF12ABCD", "RGB hex becomes canonical opaque ARGB.");
            Check(UserSettings.NormalizeColor("#4012abcd", "bad") == "#4012ABCD", "ARGB hex preserves alpha.");
            Check(UserSettings.NormalizeColor("#fff", "fallback") == "fallback" && UserSettings.NormalizeColor(null, "fallback") == "fallback", "Unsupported short hex and null rejected.");
            defaults_.RingSize = double.NaN; defaults_.TrackOpacity = double.PositiveInfinity; defaults_.OffsetX = double.NaN; defaults_.Normalize();
            Check(defaults_.RingSize == 20 && defaults_.TrackOpacity == 0.25 && defaults_.OffsetX == 0, "Nonfinite numeric settings cannot corrupt rendering.");

            QuotaWindow quota_ = new QuotaWindow { UsedPercent = 28.5, ResetsAt = DateTimeOffset.UtcNow.AddHours(3), WindowDurationMins = 300 };
            string hover_ = QuotaDisplayFormatter.FormatHover("5h", quota_, false);
            Check(hover_.Contains("71.5%") && hover_.Contains(quota_.ResetsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")), "Hover includes accurate remaining amount and local reset timestamp.");
            Check(hover_.Contains("3") || hover_.Contains("2"), "Future reset includes countdown.");
            Check(QuotaDisplayFormatter.FormatHover("5h", quota_, true).Contains("\u6570\u636e\u5df2\u8fc7\u671f"), "Stale hover carries explicit stale state.");
            quota_.ResetsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            Check(QuotaDisplayFormatter.FormatHover("5h", quota_, false).Contains("\u7b49\u5f85\u670d\u52a1\u7aef\u786e\u8ba4"), "Past reset is not falsely treated as renewed quota.");
            Check(QuotaDisplayFormatter.FormatHover("weekly", null, false).Contains("\u672a\u77e5"), "Missing quota is explicitly unknown.");
            quota_.ResetsAt = null;
            Check(QuotaDisplayFormatter.FormatHover("5h", quota_, false).Contains("\u672a\u8fd4\u56de\u91cd\u7f6e\u65f6\u95f4"), "Missing reset is explicitly reported.");

            Check(CenterHasInk(Render("Label", quota_, 0)), "Label center is drawn.");
            Check(CenterHasInk(Render("Percent", quota_, 0)), "Percent center is drawn.");
            Check(!CenterHasInk(Render("None", quota_, 0)), "None center has no text.");
            Check(!CenterHasInk(Render("None", null, 0)), "None also suppresses unknown-quota question mark.");
            byte[] transparent_ = Render("None", new QuotaWindow { UsedPercent = 100 }, 0);
            Check(MaxAlpha(transparent_) <= 1, "Zero-opacity empty track is visually transparent and preserves hover surface.");
            byte[] visibleTrack_ = Render("None", new QuotaWindow { UsedPercent = 100 }, 0.5);
            Check(MaxAlpha(visibleTrack_) > 50 && MaxAlpha(visibleTrack_) < 180, "Track opacity controls empty ring alpha.");
            RingControl hitTarget_ = new RingControl { CenterDisplay = "None", Quota = quota_ };
            hitTarget_.Measure(new Size(20,20)); hitTarget_.Arrange(new Rect(0,0,20,20)); hitTarget_.UpdateLayout();
            RenderTargetBitmap hitBitmap_ = new RenderTargetBitmap(20,20,96,96,PixelFormats.Pbgra32); hitBitmap_.Render(hitTarget_);
            Check(VisualTreeHelper.HitTest(hitTarget_, new Point(10,10)) != null, "Hollow center remains hit-testable for hover tooltip.");
            Console.WriteLine("PASS: " + count + " appearance, migration, hover and rendering checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_.Message);
            return 1;
        }
    }

    private static byte[] Render(string centerDisplay, QuotaWindow quota, double trackOpacity)
    {
        RingControl ring_ = new RingControl { Label = "5h", CenterDisplay = centerDisplay, Quota = quota, TrackOpacity = trackOpacity, RemainingColor = Colors.White };
        ring_.Measure(new Size(20,20)); ring_.Arrange(new Rect(0,0,20,20)); ring_.UpdateLayout();
        RenderTargetBitmap bitmap_ = new RenderTargetBitmap(20,20,96,96,PixelFormats.Pbgra32);
        bitmap_.Render(ring_);
        byte[] pixels_ = new byte[20 * 20 * 4]; bitmap_.CopyPixels(pixels_,80,0); return pixels_;
    }

    private static bool CenterHasInk(byte[] pixels)
    {
        for (int y_ = 7; y_ <= 12; y_++)
            for (int x_ = 7; x_ <= 12; x_++)
                if (pixels[(y_ * 20 + x_) * 4 + 3] > 8) return true;
        return false;
    }

    private static int MaxAlpha(byte[] pixels)
    {
        int maximum_ = 0;
        for (int index_ = 3; index_ < pixels.Length; index_ += 4) maximum_ = Math.Max(maximum_, pixels[index_]);
        return maximum_;
    }
}
