using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CodexQuota;

internal static class CoreTests
{
    private static int count;

    private static Dictionary<string, object> Window(object used, object duration, object resets)
    {
        return new Dictionary<string, object> { { "usedPercent", used }, { "windowDurationMins", duration }, { "resetsAt", resets } };
    }

    private static string Snapshot(object primary, object secondary)
    {
        return new JavaScriptSerializer().Serialize(new { result = new { rateLimits = new { primary = primary, secondary = secondary, planType = "plus" } } });
    }

    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }

    private static void Reject(string json, string message)
    {
        try { QuotaParser.Parse(json); }
        catch (FormatException) { Check(true, message); return; }
        throw new Exception("Accepted invalid data: " + message);
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "app-server") return RunFakeServer();
        try
        {
            var swapped_ = QuotaParser.Parse(Snapshot(Window(25, 10080, 1800000000), Window(0, 300, null)));
            Check(swapped_.Weekly.UsedPercent == 25 && swapped_.FiveHour.RemainingPercent == 100, "Windows identified by duration and zero remains valid.");
            Check(swapped_.Weekly.ResetsAt.Value.Year == 2027 && !swapped_.FiveHour.ResetsAt.HasValue, "Reset timestamp and null.");
            var weeklyOnly_ = QuotaParser.Parse(Snapshot(Window(100, 10080, null), null));
            Check(weeklyOnly_.FiveHour == null && weeklyOnly_.Weekly.RemainingPercent == 0, "Weekly-only primary.");
            var secondaryOnly_ = QuotaParser.Parse(Snapshot(null, Window(2.5, 300, null)));
            Check(secondaryOnly_.FiveHour.UsedPercent == 2.5 && secondaryOnly_.Weekly == null, "Null primary.");
            var empty_ = QuotaParser.Parse(Snapshot(null, null));
            Check(empty_.FiveHour == null && empty_.Weekly == null, "Null windows remain unknown.");
            var unknown_ = QuotaParser.Parse(Snapshot(Window(33, 60, null), Window(40, null, null)));
            Check(unknown_.FiveHour == null && unknown_.Weekly == null, "Unknown duration not guessed.");
            var buckets_ = new Dictionary<string, object> {
                { "codex", new { primary = Window(10, 300, null), secondary = (object)null } },
                { "codex_other", new { primary = Window(90, 10080, null), secondary = (object)null } }
            };
            var multiple_ = QuotaParser.Parse(new JavaScriptSerializer().Serialize(new { rateLimitsByLimitId = buckets_, rateLimits = new { primary = Window(99, 10080, null) } }));
            Check(multiple_.LimitId == "codex" && multiple_.FiveHour.UsedPercent == 10 && multiple_.Weekly == null, "No cross-bucket mixing.");
            buckets_.Remove("codex");
            var fallback_ = QuotaParser.Parse(new JavaScriptSerializer().Serialize(new { rateLimitsByLimitId = buckets_, rateLimits = new { primary = Window(20, 10080, null) } }));
            Check(fallback_.Weekly.UsedPercent == 20 && fallback_.FiveHour == null, "Fallback uses legacy bucket only.");
            Reject(new JavaScriptSerializer().Serialize(new { rateLimitsByLimitId = buckets_ }), "No arbitrary bucket fallback.");
            Reject(Snapshot(Window(10, 300, null), Window(20, 300, null)), "Duplicate window.");
            Reject(Snapshot(Window(-1, 300, null), null), "Negative percentage.");
            Reject(Snapshot(Window(101, 300, null), null), "Excess percentage.");
            Reject(Snapshot(Window(null, 300, null), null), "Missing percentage.");
            Reject(Snapshot(Window("0", 300, null), null), "String percentage.");
            Reject(Snapshot(Window(1, 300.5, null), null), "Fractional duration.");
            Reject(Snapshot(Window(1, 300, 1e50), null), "Reset overflow.");
            Reject(Snapshot(Window(1, 300, -1), null), "Negative reset time.");
            Reject(Snapshot(Window(1, 300, 3.5), null), "Fractional reset time.");
            Reject("{\"result\":null}", "Null result.");
            Reject("{\"error\":{\"message\":\"secret\"}}", "RPC error.");
            Reject("bad json", "Malformed response.");
            Reject("{\"rateLimitsByLimitId\":{\"codex\":null},\"rateLimits\":{\"primary\":null}}", "Explicit invalid codex bucket cannot silently fallback.");
            var fullRpc_ = QuotaParser.Parse("{\"id\":1,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300,\"resetsAt\":0}}}}");
            Check(fullRpc_.FiveHour.ResetsAt.Value.Year == 1970, "Full RPC and epoch zero.");
            TestRpcAsync().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + count + " quota parser and RPC checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_.Message);
            return 1;
        }
    }

    private static async Task TestRpcAsync()
    {
        using (var client_ = new CodexRpcClient())
        {
            client_.Start(Process.GetCurrentProcess().MainModule.FileName);
            var snapshot_ = await client_.ReadQuotaAsync();
            Check(snapshot_.FiveHour.RemainingPercent == 75 && snapshot_.Weekly == null, "RPC handshake and quota read.");
            Check(snapshot_.AccountLabel == "t***@example.invalid" && snapshot_.PlanType == "pro", "Account label masked and account plan fallback.");
            var first_ = client_.CallAsync("test/echo", new { value = "first" }, 3000);
            var second_ = client_.CallAsync("test/echo", new { value = "second" }, 3000);
            await Task.WhenAll(first_, second_);
            Check(first_.Result.Contains("first") && second_.Result.Contains("second"), "Out-of-order replies correlate by request ID.");
            try { await client_.CallAsync("test/error", new { }, 3000); throw new Exception("RPC error accepted."); }
            catch (InvalidOperationException exception_)
            {
                Check(!exception_.Message.Contains("private-secret") && exception_.Message.Contains("-32000"), "RPC error sanitized.");
            }
            try { await client_.CallAsync("test/never", new { }, 100); throw new Exception("Timeout accepted."); }
            catch (TimeoutException) { Check(true, "Request timeout."); }
            try { await client_.CallAsync("test/exit", new { }, 3000); throw new Exception("Exited process accepted."); }
            catch (System.IO.IOException) { Check(true, "Exited process fails pending request."); }
        }
        var disposedClient_ = new CodexRpcClient();
        disposedClient_.Start(Process.GetCurrentProcess().MainModule.FileName);
        await disposedClient_.InitializeAsync();
        var waiting_ = disposedClient_.CallAsync("test/never", new { }, 3000);
        disposedClient_.Dispose();
        try { await waiting_; throw new Exception("Disposed request accepted."); }
        catch (System.IO.IOException) { Check(true, "Dispose fails pending request."); }
    }

    private static int RunFakeServer()
    {
        // This isolated child process simulates JSONL transport and never runs Codex or reads credentials.
        Console.Error.WriteLine(new string('x', 100000));
        var serializer_ = new JavaScriptSerializer();
        IDictionary<string, object> held_ = null;
        string line_;
        while ((line_ = Console.ReadLine()) != null)
        {
            var request_ = serializer_.DeserializeObject(line_) as IDictionary<string, object>;
            var method_ = request_["method"] as string;
            if (method_ == "initialized") continue;
            var id_ = request_["id"];
            object result_ = new { };
            if (method_ == "initialize") result_ = new { userAgent = "fake" };
            else if (method_ == "account/read") result_ = new { account = new { type = "chatgpt", email = "test@example.invalid", planType = "pro" } };
            else if (method_ == "account/rateLimits/read") result_ = new { rateLimits = new { primary = Window(25, 300, null), secondary = (object)null } };
            else if (method_ == "test/echo")
            {
                if (held_ == null) { held_ = request_; continue; }
                Console.WriteLine(serializer_.Serialize(new { id = id_, result = request_["params"] }));
                Console.WriteLine(serializer_.Serialize(new { id = held_["id"], result = held_["params"] }));
                held_ = null;
                continue;
            }
            else if (method_ == "test/error")
            {
                Console.WriteLine(serializer_.Serialize(new { id = id_, error = new { code = -32000, message = "private-secret" } }));
                continue;
            }
            else if (method_ == "test/never") continue;
            else if (method_ == "test/exit") return 0;
            Console.WriteLine(serializer_.Serialize(new { id = id_, result = result_ }));
        }
        return 0;
    }
}
