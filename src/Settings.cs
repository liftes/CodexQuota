using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace CodexQuota
{
    public sealed class UserSettings
    {
        public string FiveHourMode { get; set; }
        public string PositionMode { get; set; }
        public string CodexPath { get; set; }
        public int RefreshSeconds { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public int SettingsVersion { get; set; }
        public double RingSize { get; set; }
        public double RingGap { get; set; }
        public string FiveHourColor { get; set; }
        public string WeeklyColor { get; set; }
        public string TrackColor { get; set; }
        public double TrackOpacity { get; set; }
        public string CenterDisplay { get; set; }
        public string ThemeMode { get; set; }
        public bool ShowResetRing { get; set; }
        public string ResetRingPlacement { get; set; }
        public string ResetColor { get; set; }
        public bool AdaptivePolling { get; set; }
        public bool HideFullscreen { get; set; }
        public string DisplayMode { get; set; }
        public double HoverTitleFontSize { get; set; }
        public double HoverValueFontSize { get; set; }
        public double HoverDetailFontSize { get; set; }
        public double HoverContentWidth { get; set; }
        public UserSettings()
        {
            FiveHourMode = "Auto"; PositionMode = "StartLeft";
            CodexPath = ""; RefreshSeconds = 60;
            SettingsVersion = 7; RingSize = 20; RingGap = 6; FiveHourColor = "#FFB9BDC5"; WeeklyColor = "#FFB9BDC5";
            TrackColor = "#FF9398A1"; TrackOpacity = 0.25; CenterDisplay = "Label";
            ThemeMode = "System"; ShowResetRing = true; ResetRingPlacement = "Outer";
            ResetColor = "#FF78A6CD"; AdaptivePolling = true; HideFullscreen = true; DisplayMode = "Taskbar";
            HoverTitleFontSize = 12; HoverValueFontSize = 22; HoverDetailFontSize = 13; HoverContentWidth = 300;
        }
        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuota"); }
        }
        public static UserSettings Load()
        {
            try
            {
                string path_ = Path.Combine(DataDirectory, "settings.json");
                if (!File.Exists(path_)) return new UserSettings();
                return Parse(File.ReadAllText(path_, Encoding.UTF8));
            }
            catch { return new UserSettings(); }
        }
        public static UserSettings Parse(string json)
        {
            UserSettings settings_ = new JavaScriptSerializer().Deserialize<UserSettings>(json);
            if (settings_ == null) return new UserSettings();
            settings_.Normalize();
            return settings_;
        }
        public void Normalize()
        {
            if (!new[] { "Auto", "Show", "Hide" }.Contains(FiveHourMode)) FiveHourMode = "Auto";
            if (!new[] { "StartLeft", "Above", "Tray" }.Contains(PositionMode)) PositionMode = "StartLeft";
            RefreshSeconds = Math.Max(30, Math.Min(300, RefreshSeconds));
            SettingsVersion = 7;
            if (!new[] { "Taskbar", "Floating" }.Contains(DisplayMode)) DisplayMode = "Taskbar";
            if (!new[] { "Label", "Percent", "None", "ResetPercent" }.Contains(CenterDisplay)) CenterDisplay = "Label";
            if (!new[] { "System", "Light", "Dark" }.Contains(ThemeMode)) ThemeMode = "System";
            if (!new[] { "Outer", "Inner" }.Contains(ResetRingPlacement)) ResetRingPlacement = "Outer";
            if (double.IsNaN(OffsetX) || double.IsInfinity(OffsetX)) OffsetX = 0;
            if (double.IsNaN(OffsetY) || double.IsInfinity(OffsetY)) OffsetY = 0;
            OffsetX = Math.Max(-500, Math.Min(500, OffsetX)); OffsetY = Math.Max(-300, Math.Min(300, OffsetY));
            if (double.IsNaN(RingSize) || double.IsInfinity(RingSize)) RingSize = 20;
            RingSize = Math.Max(16, Math.Min(28, RingSize));
            if (double.IsNaN(RingGap) || double.IsInfinity(RingGap)) RingGap = 6;
            RingGap = Math.Max(0,Math.Min(40,RingGap));
            if (double.IsNaN(TrackOpacity) || double.IsInfinity(TrackOpacity)) TrackOpacity = 0.25;
            TrackOpacity = Math.Max(0, Math.Min(1, TrackOpacity));
            FiveHourColor = NormalizeColor(FiveHourColor, "#FFB9BDC5");
            WeeklyColor = NormalizeColor(WeeklyColor, "#FFB9BDC5");
            TrackColor = NormalizeColor(TrackColor, "#FF9398A1");
            ResetColor = NormalizeColor(ResetColor, "#FF78A6CD");
            HoverAppearance hover_ = GetHoverAppearance();
            HoverTitleFontSize = hover_.TitleFontSize; HoverValueFontSize = hover_.ValueFontSize;
            HoverDetailFontSize = hover_.DetailFontSize; HoverContentWidth = hover_.ContentWidth;
        }
        public HoverAppearance GetHoverAppearance()
        {
            HoverAppearance appearance_ = new HoverAppearance { TitleFontSize = HoverTitleFontSize, ValueFontSize = HoverValueFontSize, DetailFontSize = HoverDetailFontSize, ContentWidth = HoverContentWidth };
            appearance_.Normalize(); return appearance_;
        }
        public static string NormalizeColor(string value, string fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || (value.Length != 7 && value.Length != 9)) return fallback;
                Color color_ = (Color)ColorConverter.ConvertFromString(value);
                return color_.ToString(CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }
        public void Save()
        {
            Normalize();
            Directory.CreateDirectory(DataDirectory);
            string path_ = Path.Combine(DataDirectory, "settings.json");
            string temporaryPath_ = path_ + ".tmp";
            File.WriteAllText(temporaryPath_, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path_)) File.Replace(temporaryPath_, path_, null);
            else File.Move(temporaryPath_, path_);
        }
    }

    public static class CodexLocator
    {
        public static string Find(string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (File.Exists(explicitPath) && string.Equals(Path.GetExtension(explicitPath), ".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(explicitPath);
                throw new InvalidOperationException("指定的 Codex 可执行文件不存在，请在设置中重新选择。");
            }
            string localPath_ = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profileLocalPath_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            foreach (string officialRoot_ in new[] { Path.Combine(localPath_, "OpenAI", "Codex", "bin"), Path.Combine(profileLocalPath_, "OpenAI", "Codex", "bin") }.Distinct())
            {
                if (!Directory.Exists(officialRoot_)) continue;
                string found_ = Directory.GetDirectories(officialRoot_).Select(directory_ => Path.Combine(directory_, "codex.exe"))
                    .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (found_ != null) return found_;
            }
            foreach (string directory_ in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { string path_ = Path.Combine(directory_.Trim('"'), "codex.exe"); if (File.Exists(path_)) return path_; }
                catch (ArgumentException) { }
            }
            string vscodeRoot_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
            if (Directory.Exists(vscodeRoot_))
            {
                foreach (string directory_ in Directory.GetDirectories(vscodeRoot_, "openai.chatgpt-*").OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    string path_ = Path.Combine(directory_, "bin", "windows-x86_64", "codex.exe");
                    if (File.Exists(path_)) return path_;
                }
            }
            throw new InvalidOperationException("未找到 Codex CLI。请先安装并登录官方 Codex，或在设置中选择 codex.exe。");
        }
    }
}
