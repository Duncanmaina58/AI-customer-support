using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;

namespace LoadTest;

/// <summary>
/// Sprint 8 checklist: "Load test with 50 concurrent conversations — p95
/// latency &lt; 5 seconds". This tool drives that test directly against a real
/// (or locally running) deployment's public chat pipeline — the exact same
/// SignalR hub (/hubs/chat) the embeddable widget itself uses — rather than
/// simulating it, so the numbers it reports are the real end-to-end latency a
/// customer's website visitor would experience, AI generation included.
///
/// Usage:
///   dotnet run --project tools/LoadTest -- --url https://your-api.onrender.com --key pub_xxx
///   dotnet run --project tools/LoadTest -- --url http://localhost:5000 --key pub_xxx --conversations 50 --message "What are your opening hours?"
///
/// Notes:
///   - --key must be a real company's PublicApiKey or SandboxToken (Settings >
///     Web Chat, or the Sandbox page) — a fresh test/pilot company works well
///     so results aren't skewed by real customer traffic sharing the same
///     token budget.
///   - Every simulated conversation asks the same --message by default so RAG
///     retrieval cost is comparable across the run; pass different text per
///     run to test different intents.
///   - This exercises the full pipeline: SignalR connect → RAG retrieval →
///     Groq generation → escalation check → DB writes — i.e. exactly what
///     Sprint 8 asks to be load tested, not just the HTTP layer in isolation.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = LoadTestOptions.Parse(args);
        if (options is null) return 1;

        Console.WriteLine($"Load test: {options.ConcurrentConversations} concurrent conversations -> {options.HubUrl}");
        Console.WriteLine($"Message: \"{options.Message}\"");
        Console.WriteLine();

        var tasks = new List<Task<ConversationResult>>();
        for (var i = 0; i < options.ConcurrentConversations; i++)
        {
            var index = i;
            // Optional ramp-up: stagger starts slightly so this models "50 people
            // arrive over a few seconds", not "50 requests hit in the exact same
            // millisecond" — either is a legitimate test, ramp-up is just closer
            // to real traffic. Default is 0 (all at once, the harsher test).
            var startDelay = options.RampUpSeconds > 0
                ? TimeSpan.FromSeconds(options.RampUpSeconds * index / (double)options.ConcurrentConversations)
                : TimeSpan.Zero;

            tasks.Add(RunOneConversationAsync(options, index, startDelay));
        }

        var results = await Task.WhenAll(tasks);

        PrintReport(results, options);
        return results.Count(r => r.Success) == results.Length ? 0 : 1;
    }

    private static async Task<ConversationResult> RunOneConversationAsync(
        LoadTestOptions options, int index, TimeSpan startDelay)
    {
        if (startDelay > TimeSpan.Zero)
            await Task.Delay(startDelay);

        var connection = new HubConnectionBuilder()
            .WithUrl(options.HubUrl)
            .WithAutomaticReconnect()
            .Build();

        var replyComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorMessage = (string?)null;

        connection.On<object>("ReplyComplete", _ => replyComplete.TrySetResult(true));
        connection.On<string>("Error", msg => { errorMessage = msg; replyComplete.TrySetResult(false); });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await connection.StartAsync();
            await connection.InvokeAsync("JoinCompanyGroup", options.CompanyKey);

            var sessionId = $"loadtest-{Guid.NewGuid():N}";
            await connection.InvokeAsync("SendMessage", options.CompanyKey, sessionId, options.Message);

            var timeoutTask = Task.Delay(options.TimeoutSeconds * 1000);
            var completed = await Task.WhenAny(replyComplete.Task, timeoutTask);

            stopwatch.Stop();

            if (completed != replyComplete.Task)
                return ConversationResult.Failure(index, stopwatch.Elapsed, $"Timed out after {options.TimeoutSeconds}s");

            var success = await replyComplete.Task;
            return success
                ? ConversationResult.SuccessResult(index, stopwatch.Elapsed)
                : ConversationResult.Failure(index, stopwatch.Elapsed, errorMessage ?? "Hub returned an Error event");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ConversationResult.Failure(index, stopwatch.Elapsed, ex.Message);
        }
        finally
        {
            try { await connection.DisposeAsync(); } catch { /* best-effort cleanup */ }
        }
    }

    private static void PrintReport(ConversationResult[] results, LoadTestOptions options)
    {
        var successes = results.Where(r => r.Success).ToList();
        var failures  = results.Where(r => !r.Success).ToList();

        Console.WriteLine("---------------------------------------------------------");
        Console.WriteLine($"Total:      {results.Length}");
        Console.WriteLine($"Succeeded:  {successes.Count}");
        Console.WriteLine($"Failed:     {failures.Count}");

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Failures:");
            foreach (var f in failures.Take(10))
                Console.WriteLine($"  #{f.Index}: {f.Elapsed.TotalMilliseconds:F0}ms — {f.Error}");
            if (failures.Count > 10)
                Console.WriteLine($"  ...and {failures.Count - 10} more.");
        }

        if (successes.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No successful conversations — cannot compute latency percentiles.");
            return;
        }

        var sortedMs = successes.Select(r => r.Elapsed.TotalMilliseconds).OrderBy(x => x).ToList();
        double Percentile(double p)
        {
            var idx = (int)Math.Ceiling(p / 100.0 * sortedMs.Count) - 1;
            return sortedMs[Math.Clamp(idx, 0, sortedMs.Count - 1)];
        }

        var p50 = Percentile(50);
        var p95 = Percentile(95);
        var p99 = Percentile(99);
        var max = sortedMs[^1];
        var min = sortedMs[0];
        var mean = sortedMs.Average();

        Console.WriteLine();
        Console.WriteLine($"Latency (successful conversations only, ms):");
        Console.WriteLine($"  min:  {min,8:F0}");
        Console.WriteLine($"  mean: {mean,8:F0}");
        Console.WriteLine($"  p50:  {p50,8:F0}");
        Console.WriteLine($"  p95:  {p95,8:F0}");
        Console.WriteLine($"  p99:  {p99,8:F0}");
        Console.WriteLine($"  max:  {max,8:F0}");
        Console.WriteLine();

        var thresholdMs = options.P95ThresholdSeconds * 1000;
        var pass = p95 < thresholdMs && failures.Count == 0;
        Console.WriteLine(pass
            ? $"PASS — p95 ({p95:F0}ms) is under the {thresholdMs:F0}ms threshold and every conversation succeeded."
            : $"FAIL — {(p95 >= thresholdMs ? $"p95 ({p95:F0}ms) exceeds the {thresholdMs:F0}ms threshold" : "some conversations failed")}.");
        Console.WriteLine("---------------------------------------------------------");
    }
}

internal sealed record ConversationResult(int Index, TimeSpan Elapsed, bool Success, string? Error)
{
    public static ConversationResult SuccessResult(int index, TimeSpan elapsed) => new(index, elapsed, true, null);
    public static ConversationResult Failure(int index, TimeSpan elapsed, string error) => new(index, elapsed, false, error);
}

internal sealed class LoadTestOptions
{
    public required string HubUrl { get; init; }
    public required string CompanyKey { get; init; }
    public string Message { get; init; } = "What are your opening hours?";
    public int ConcurrentConversations { get; init; } = 50;
    public int RampUpSeconds { get; init; } = 0;
    public int TimeoutSeconds { get; init; } = 30;
    public int P95ThresholdSeconds { get; init; } = 5;

    public static LoadTestOptions? Parse(string[] args)
    {
        string? url = null, key = null, message = null;
        var conversations = 50;
        var rampUp = 0;
        var timeout = 30;
        var threshold = 5;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when i + 1 < args.Length: url = args[++i]; break;
                case "--key" when i + 1 < args.Length: key = args[++i]; break;
                case "--message" when i + 1 < args.Length: message = args[++i]; break;
                case "--conversations" when i + 1 < args.Length: conversations = int.Parse(args[++i]); break;
                case "--ramp-up-seconds" when i + 1 < args.Length: rampUp = int.Parse(args[++i]); break;
                case "--timeout-seconds" when i + 1 < args.Length: timeout = int.Parse(args[++i]); break;
                case "--p95-threshold-seconds" when i + 1 < args.Length: threshold = int.Parse(args[++i]); break;
                case "--help": PrintUsage(); return null;
            }
        }

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Missing required --url and/or --key.");
            PrintUsage();
            return null;
        }

        var hubUrl = url.TrimEnd('/') + "/hubs/chat";

        return new LoadTestOptions
        {
            HubUrl = hubUrl,
            CompanyKey = key,
            Message = message ?? "What are your opening hours?",
            ConcurrentConversations = conversations,
            RampUpSeconds = rampUp,
            TimeoutSeconds = timeout,
            P95ThresholdSeconds = threshold,
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage:
              dotnet run --project tools/LoadTest -- --url <api base url> --key <public or sandbox key> [options]

            Options:
              --conversations <n>            Concurrent simulated conversations (default 50)
              --message <text>               Message every simulated customer sends (default: "What are your opening hours?")
              --ramp-up-seconds <n>          Spread conversation starts over n seconds instead of all at once (default 0)
              --timeout-seconds <n>          Per-conversation timeout waiting for ReplyComplete (default 30)
              --p95-threshold-seconds <n>    SLA threshold for the pass/fail verdict (default 5, per Sprint 8)
            """);
    }
}
