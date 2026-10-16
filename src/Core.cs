using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuota
{
    public sealed class QuotaWindow
    {
        public double UsedPercent { get; set; }
        public double RemainingPercent { get { return 100.0 - UsedPercent; } }
        public int WindowDurationMins { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
    }

    public sealed class QuotaSnapshot
    {
        public QuotaWindow FiveHour { get; set; }
        public QuotaWindow Weekly { get; set; }
        public string AccountLabel { get; set; }
        public string PlanType { get; set; }
        public string LimitId { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public static class QuotaParser
    {
        internal static IDictionary<string, object> Deserialize(string json)
        {
            try
            {
                var serializer_ = new JavaScriptSerializer { MaxJsonLength = 2097152, RecursionLimit = 64 };
                var root_ = serializer_.DeserializeObject(json) as IDictionary<string, object>;
                if (root_ == null) throw new FormatException();
                if (root_.ContainsKey("error")) throw new FormatException();
                object result_;
                if (root_.TryGetValue("result", out result_))
                {
                    root_ = result_ as IDictionary<string, object>;
                    if (root_ == null) throw new FormatException();
                }
                return root_;
            }
            catch (Exception exception_)
            {
                if (exception_ is OutOfMemoryException || exception_ is StackOverflowException) throw;
                throw new FormatException("Codex returned an invalid response.");
            }
        }

        internal static object Get(IDictionary<string, object> dictionary, string key)
        {
            object value_;
            return dictionary != null && dictionary.TryGetValue(key, out value_) ? value_ : null;
        }

        internal static string Text(IDictionary<string, object> dictionary, string key)
        {
            return Get(dictionary, key) as string;
        }

        public static QuotaSnapshot Parse(string json)
        {
            var root_ = Deserialize(json);
            var buckets_ = Get(root_, "rateLimitsByLimitId") as IDictionary<string, object>;
            IDictionary<string, object> selected_ = null;
            string limitId_ = null;
            if (buckets_ != null && buckets_.ContainsKey("codex"))
            {
                selected_ = Get(buckets_, "codex") as IDictionary<string, object>;
                if (selected_ == null) throw new FormatException("Codex quota bucket is unavailable.");
                limitId_ = "codex";
            }
            else
            {
                selected_ = Get(root_, "rateLimits") as IDictionary<string, object>;
                if (selected_ == null) throw new FormatException("Codex quota data is unavailable.");
                limitId_ = Text(selected_, "limitId");
            }
            var snapshot_ = new QuotaSnapshot
            {
                LimitId = limitId_, PlanType = Text(selected_, "planType"), UpdatedAt = DateTimeOffset.UtcNow
            };
            AddWindow(snapshot_, Get(selected_, "primary"));
            AddWindow(snapshot_, Get(selected_, "secondary"));
            return snapshot_;
        }

        private static double ReadNumber(object value)
        {
            if (!(value is int || value is long || value is decimal || value is double || value is float || value is short || value is byte))
                throw new FormatException("Codex quota contains an invalid numeric field.");
            double number_;
            try { number_ = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { throw new FormatException("Codex quota contains an invalid numeric field."); }
            if (Double.IsNaN(number_) || Double.IsInfinity(number_)) throw new FormatException("Codex quota contains an invalid numeric field.");
            return number_;
        }

        private static void AddWindow(QuotaSnapshot snapshot, object value)
        {
            if (value == null) return;
            var windowData_ = value as IDictionary<string, object>;
            if (windowData_ == null) throw new FormatException("Codex quota window is invalid.");
            var durationValue_ = Get(windowData_, "windowDurationMins");
            // A missing duration cannot safely identify a five-hour or weekly window.
            if (durationValue_ == null) return;
            var duration_ = ReadNumber(durationValue_);
            if (duration_ <= 0 || duration_ > Int32.MaxValue || duration_ != Math.Truncate(duration_))
                throw new FormatException("Codex quota window duration is invalid.");
            if (duration_ != 300 && duration_ != 10080) return;
            var used_ = ReadNumber(Get(windowData_, "usedPercent"));
            if (used_ < 0 || used_ > 100) throw new FormatException("Codex quota percentage is out of range.");
            DateTimeOffset? resetsAt_ = null;
            var resetsValue_ = Get(windowData_, "resetsAt");
            if (resetsValue_ != null)
            {
                var seconds_ = ReadNumber(resetsValue_);
                if (seconds_ < 0 || seconds_ > 253402300799 || seconds_ != Math.Truncate(seconds_))
                    throw new FormatException("Codex quota reset time is invalid.");
                try { resetsAt_ = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds_); }
                catch (ArgumentOutOfRangeException) { throw new FormatException("Codex quota reset time is invalid."); }
            }
            var window_ = new QuotaWindow { UsedPercent = used_, WindowDurationMins = (int)duration_, ResetsAt = resetsAt_ };
            if (duration_ == 300)
            {
                if (snapshot.FiveHour != null) throw new FormatException("Codex returned duplicate five-hour quota windows.");
                snapshot.FiveHour = window_;
            }
            else
            {
                if (snapshot.Weekly != null) throw new FormatException("Codex returned duplicate weekly quota windows.");
                snapshot.Weekly = window_;
            }
        }
    }

    public sealed class CodexRpcClient : IDisposable
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> pending = new ConcurrentDictionary<int, TaskCompletionSource<string>>();
        private readonly object writeLock = new object();
        private readonly SemaphoreSlim initializeLock = new SemaphoreSlim(1, 1);
        private Process process;
        private int nextId;
        private bool initialized;
        private volatile bool disposed;
        private volatile bool connectionClosed;

        public void Start(string executablePath)
        {
            if (disposed) throw new ObjectDisposedException("CodexRpcClient");
            if (process != null) throw new InvalidOperationException("Codex connection has already started.");
            if (String.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new FileNotFoundException("Codex executable was not found.");
            var startInfo_ = new ProcessStartInfo
            {
                FileName = executablePath, Arguments = "app-server", UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            };
            try
            {
                process = new Process { StartInfo = startInfo_ };
                if (!process.Start()) throw new InvalidOperationException();
                process.StandardInput.AutoFlush = true;
                Task.Run((Func<Task>)ReadLoopAsync);
                Task.Run((Func<Task>)DrainErrorsAsync);
            }
            catch
            {
                if (process != null) { process.Dispose(); process = null; }
                throw new InvalidOperationException("Could not start the Codex connection.");
            }
        }

        public async Task InitializeAsync()
        {
            await initializeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (initialized) return;
                await SendAsync("initialize", new
                {
                    clientInfo = new { name = "codex_quota", title = "Codex Quota", version = "0.1.0" },
                    capabilities = new { experimentalApi = false }
                }, 15000).ConfigureAwait(false);
                WriteMessage(new { method = "initialized" });
                initialized = true;
            }
            finally { initializeLock.Release(); }
        }

        public async Task<string> CallAsync(string method, object parameters, int timeoutMs)
        {
            await InitializeAsync().ConfigureAwait(false);
            return await SendAsync(method, parameters, timeoutMs).ConfigureAwait(false);
        }

        public async Task<QuotaSnapshot> ReadQuotaAsync()
        {
            var accountJson_ = await CallAsync("account/read", new { refreshToken = false }, 15000).ConfigureAwait(false);
            var accountResult_ = QuotaParser.Deserialize(accountJson_);
            var account_ = QuotaParser.Get(accountResult_, "account") as IDictionary<string, object>;
            if (account_ == null) throw new InvalidOperationException("Sign in to Codex first.");
            var accountType_ = QuotaParser.Text(account_, "type");
            if (accountType_ == "apiKey") throw new InvalidOperationException("ChatGPT subscription quota requires a ChatGPT login.");
            var quotaJson_ = await CallAsync("account/rateLimits/read", new { }, 15000).ConfigureAwait(false);
            var snapshot_ = QuotaParser.Parse(quotaJson_);
            snapshot_.AccountLabel = MaskEmail(QuotaParser.Text(account_, "email"));
            if (String.IsNullOrWhiteSpace(snapshot_.PlanType)) snapshot_.PlanType = QuotaParser.Text(account_, "planType");
            return snapshot_;
        }

        private static string MaskEmail(string email)
        {
            if (String.IsNullOrWhiteSpace(email)) return "ChatGPT";
            var at_ = email.IndexOf('@');
            if (at_ <= 0) return "ChatGPT";
            return email.Substring(0, 1) + "***" + email.Substring(at_);
        }

        private async Task<string> SendAsync(string method, object parameters, int timeoutMs)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException("timeoutMs");
            var id_ = Interlocked.Increment(ref nextId);
            var completion_ = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(id_, completion_)) throw new InvalidOperationException("Codex request could not be queued.");
            try
            {
                WriteMessage(new { id = id_, method = method, @params = parameters });
                var finished_ = await Task.WhenAny(completion_.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (finished_ != completion_.Task) throw new TimeoutException("The Codex connection timed out.");
                return await completion_.Task.ConfigureAwait(false);
            }
            finally
            {
                TaskCompletionSource<string> removed_;
                pending.TryRemove(id_, out removed_);
            }
        }

        private void WriteMessage(object message)
        {
            lock (writeLock)
            {
                if (disposed) throw new ObjectDisposedException("CodexRpcClient");
                if (process == null) throw new InvalidOperationException("Codex connection has not started.");
                if (connectionClosed) throw new IOException("The Codex connection closed. Retry the connection.");
                try
                {
                    if (process.HasExited) throw new IOException();
                    process.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(message));
                }
                catch { throw new IOException("The Codex connection is unavailable."); }
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                string line_;
                while (!disposed && (line_ = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    var message_ = new JavaScriptSerializer { MaxJsonLength = 2097152 }.DeserializeObject(line_) as IDictionary<string, object>;
                    if (message_ == null) continue;
                    var idValue_ = QuotaParser.Get(message_, "id");
                    if (!(idValue_ is int)) continue;
                    TaskCompletionSource<string> completion_;
                    if (!pending.TryRemove((int)idValue_, out completion_)) continue;
                    if (message_.ContainsKey("error"))
                    {
                        // Never forward server-provided messages: they can contain account details.
                        var error_ = QuotaParser.Get(message_, "error") as IDictionary<string, object>;
                        var code_ = QuotaParser.Get(error_, "code");
                        var suffix_ = code_ is int ? " (" + ((int)code_).ToString(CultureInfo.InvariantCulture) + ")" : "";
                        completion_.TrySetException(new InvalidOperationException("Codex request failed" + suffix_ + "."));
                    }
                    else completion_.TrySetResult(line_);
                }
            }
            catch { }
            finally
            {
                connectionClosed = true;
                FailPending("The Codex connection closed. Retry the connection.");
            }
        }

        private async Task DrainErrorsAsync()
        {
            try
            {
                // Drain to prevent child-process deadlock; deliberately do not retain or log output.
                while (!disposed && await process.StandardError.ReadLineAsync().ConfigureAwait(false) != null) { }
            }
            catch { }
        }

        private void FailPending(string message)
        {
            foreach (var item_ in pending)
            {
                TaskCompletionSource<string> completion_;
                if (pending.TryRemove(item_.Key, out completion_)) completion_.TrySetException(new IOException(message));
            }
        }

        public void Dispose()
        {
            lock (writeLock)
            {
                if (disposed) return;
                disposed = true;
                FailPending("The Codex connection was closed.");
                if (process != null)
                {
                    try { process.StandardInput.Close(); } catch { }
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    process.Dispose();
                }
            }
        }
    }
}
