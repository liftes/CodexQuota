using System;
using System.IO;
using Microsoft.Win32;

namespace CodexQuota
{
    // Only this application's named value is ever read or changed by this store.
    public interface IStartupStore
    {
        string ReadCommand();
        void WriteCommand(string command);
        void DeleteCommand();
        bool HasSystemApprovalRecord();
    }

    public sealed class RegistryStartupStore : IStartupStore
    {
        public const string ValueName = "CodexQuota";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public string ReadCommand()
        {
            using (RegistryKey key_ = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return key_ == null ? null : key_.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }

        public void WriteCommand(string command)
        {
            using (RegistryKey key_ = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key_ == null) throw new IOException("Unable to open the current user's startup registration.");
                key_.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }

        public void DeleteCommand()
        {
            using (RegistryKey key_ = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key_ != null) key_.DeleteValue(ValueName, false);
            }
        }

        public bool HasSystemApprovalRecord()
        {
            // StartupApproved's binary state has no documented contract. Detect its
            // existence without interpreting or changing that private Windows data.
            foreach (string suffix_ in new[] { "Run", "Run32" })
            {
                using (RegistryKey key_ = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + suffix_, false))
                {
                    if (key_ != null && key_.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null) return true;
                }
            }
            return false;
        }
    }

    public sealed class StartupStatus
    {
        public bool IsRegistered { get; internal set; }
        public bool IsCurrentExecutable { get; internal set; }
        public bool HasSystemApprovalRecord { get; internal set; }
        public string StatusLabel { get; internal set; }
    }

    public sealed class StartupRegistrationService
    {
        private readonly IStartupStore store;

        public StartupRegistrationService(IStartupStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
        }

        public static string BuildCommand(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || exePath.IndexOf('"') >= 0 || exePath.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Startup executable must be an absolute executable path.", "exePath");
            if (exePath.Length > 258) throw new ArgumentException("Startup executable path is too long for Windows Run registration.", "exePath");
            string root_ = Path.GetPathRoot(exePath);
            if (!Path.IsPathRooted(exePath) || string.IsNullOrEmpty(root_) || (root_.Length < 3 && !root_.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException("Startup executable must be an absolute executable path.", "exePath");
            string fullPath_ = Path.GetFullPath(exePath);
            if (!string.Equals(Path.GetExtension(fullPath_), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Startup executable must be an .exe file.", "exePath");
            string command_ = "\"" + fullPath_ + "\"";
            // Windows documents a 260-character limit for Run command lines.
            if (command_.Length > 260) throw new ArgumentException("Startup executable path is too long for Windows Run registration.", "exePath");
            return command_;
        }

        public StartupStatus GetStatus(string exePath)
        {
            string expected_ = BuildCommand(exePath);
            string command_ = store.ReadCommand();
            bool registered_ = !string.IsNullOrWhiteSpace(command_);
            bool matches_ = registered_ && string.Equals(command_.Trim(), expected_, StringComparison.OrdinalIgnoreCase);
            bool managed_ = registered_ && store.HasSystemApprovalRecord();
            string label_ = !registered_ ? "未登记开机启动" : !matches_ ? "另一位置的版本已登记；开启可切换至当前版本" : managed_ ? "已登记；请在 Windows 启动应用中确认是否被禁用" : "已登记，将在登录 Windows 后启动";
            return new StartupStatus { IsRegistered = registered_, IsCurrentExecutable = matches_, HasSystemApprovalRecord = managed_, StatusLabel = label_ };
        }

        public bool IsEnabled(string exePath) { return GetStatus(exePath).IsCurrentExecutable; }

        public void SetEnabled(bool enabled, string exePath)
        {
            string command_ = BuildCommand(exePath);
            if (enabled) store.WriteCommand(command_);
            else store.DeleteCommand();
        }
    }

    public static class StartupRegistration
    {
        private static readonly StartupRegistrationService service = new StartupRegistrationService(new RegistryStartupStore());
        public static bool IsEnabled(string exePath) { return service.IsEnabled(exePath); }
        public static void SetEnabled(bool enabled, string exePath) { service.SetEnabled(enabled, exePath); }
        public static StartupStatus GetStatus(string exePath) { return service.GetStatus(exePath); }
        public static string GetStatusLabel(string exePath) { return service.GetStatus(exePath).StatusLabel; }
    }
}
