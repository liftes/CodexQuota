using System;
using System.Collections.Generic;
using System.IO;
using CodexQuota;

internal static class StartupTests
{
    private static int count;
    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }

    private static void RejectPath(string path)
    {
        try { StartupRegistrationService.BuildCommand(path); }
        catch (ArgumentException) { Check(true, "Rejected invalid startup path"); return; }
        throw new Exception("Accepted invalid startup path: " + path);
    }

    private sealed class MemoryStartupStore : IStartupStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public bool ApprovalRecord;
        public bool FailReads;
        public bool FailWrites;
        public int WriteCount;
        public int DeleteCount;
        public string ReadCommand()
        {
            if (FailReads) throw new IOException("Read failure");
            string command_;
            return Values.TryGetValue("CodexQuota", out command_) ? command_ : null;
        }
        public void WriteCommand(string command)
        {
            if (FailWrites) throw new UnauthorizedAccessException("Write failure");
            WriteCount++;
            Values["CodexQuota"] = command;
        }
        public void DeleteCommand()
        {
            if (FailWrites) throw new UnauthorizedAccessException("Delete failure");
            DeleteCount++;
            Values.Remove("CodexQuota");
        }
        public bool HasSystemApprovalRecord() { return ApprovalRecord; }
    }

    private static int Main()
    {
        try
        {
            const string exePath_ = @"C:\Users\Test User\Codex Quota\CodexQuota.exe";
            const string expected_ = "\"C:\\Users\\Test User\\Codex Quota\\CodexQuota.exe\"";
            Check(StartupRegistrationService.BuildCommand(exePath_) == expected_, "Spaces are protected by double quotes with no shell or intermediary.");
            Check(StartupRegistrationService.BuildCommand(@"C:\App\..\CodexQuota.exe") == "\"C:\\CodexQuota.exe\"", "Dot segments normalize to an absolute path.");
            Check(StartupRegistrationService.BuildCommand(@"\\server\share\CodexQuota.exe") == "\"\\\\server\\share\\CodexQuota.exe\"", "Absolute UNC executable paths remain fully quoted.");
            RejectPath(null); RejectPath(""); RejectPath("CodexQuota.exe"); RejectPath(@"C:CodexQuota.exe"); RejectPath(@"\CodexQuota.exe");
            RejectPath(@"C:\Quota\CodexQuota.exe" + "\" --extra"); RejectPath("C:\\Quota\\CodexQuota.exe\n"); RejectPath(@"C:\Quota\launch.ps1");
            RejectPath("C:\\" + new string('x', 255) + ".exe");

            MemoryStartupStore store_ = new MemoryStartupStore();
            store_.Values["OtherApp"] = "untouched";
            StartupRegistrationService service_ = new StartupRegistrationService(store_);
            StartupStatus status_ = service_.GetStatus(exePath_);
            Check(!status_.IsRegistered && !status_.IsCurrentExecutable, "Missing actual Run value is disabled.");
            Check(store_.WriteCount == 0 && store_.DeleteCount == 0, "Reading status never writes registration.");
            service_.SetEnabled(true, exePath_);
            Check(store_.Values["CodexQuota"] == expected_, "Enable writes the current executable command.");
            Check(service_.IsEnabled(exePath_), "Actual registry command determines enabled state.");
            Check(store_.Values["OtherApp"] == "untouched", "Enable preserves every other named startup value.");
            store_.Values["CodexQuota"] = expected_.ToUpperInvariant();
            Check(service_.IsEnabled(exePath_), "Windows path comparison ignores case.");
            store_.Values["CodexQuota"] = "  " + expected_ + "  ";
            Check(service_.IsEnabled(exePath_), "Command boundary whitespace is harmless.");
            store_.Values["CodexQuota"] = expected_ + " --unexpected";
            Check(!service_.IsEnabled(exePath_), "Unexpected arguments are not treated as this app's expected registration.");
            store_.Values["CodexQuota"] = "\"C:\\Old App\\CodexQuota.exe\"";
            status_ = service_.GetStatus(exePath_);
            Check(status_.IsRegistered && !status_.IsCurrentExecutable && status_.StatusLabel.Contains("另一位置"), "Moved installations expose stale registrations rather than reporting enabled.");
            service_.SetEnabled(true, exePath_);
            Check(service_.IsEnabled(exePath_), "Explicit enable repairs an older app location.");
            store_.ApprovalRecord = true;
            status_ = service_.GetStatus(exePath_);
            Check(status_.HasSystemApprovalRecord && status_.StatusLabel.Contains("确认是否被禁用"), "Windows-managed startup state is honestly qualified without decoding private binary data.");
            service_.SetEnabled(false, exePath_);
            Check(!service_.IsEnabled(exePath_) && !store_.Values.ContainsKey("CodexQuota"), "Disable removes this app's named registration.");
            Check(store_.Values["OtherApp"] == "untouched" && store_.ApprovalRecord, "Disable preserves other apps and Windows approval records.");
            service_.SetEnabled(false, exePath_);
            Check(!service_.IsEnabled(exePath_), "Disable remains safe when the value is missing.");
            store_.FailReads = true;
            bool readFailed_ = false;
            try { service_.IsEnabled(exePath_); } catch (IOException) { readFailed_ = true; }
            Check(readFailed_, "Read failures propagate for UI error reporting rather than claiming disabled.");
            store_.FailReads = false; store_.FailWrites = true;
            bool enableFailed_ = false;
            try { service_.SetEnabled(true, exePath_); } catch (UnauthorizedAccessException) { enableFailed_ = true; }
            Check(enableFailed_, "Enable failures propagate rather than pretending startup was registered.");
            bool disableFailed_ = false;
            try { service_.SetEnabled(false, exePath_); } catch (UnauthorizedAccessException) { disableFailed_ = true; }
            Check(disableFailed_, "Disable failures propagate for UI recovery.");
            Check(store_.Values["OtherApp"] == "untouched", "Failure paths leave other apps untouched.");
            Console.WriteLine("StartupTests: " + count + " checks passed (memory store only; no startup registry writes).");
            return 0;
        }
        catch (Exception error_) { Console.Error.WriteLine(error_); return 1; }
    }
}
