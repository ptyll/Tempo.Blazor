using System.Text.Json;
using System.Threading;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Machine-readable record of demo-host resurrections (N209).
/// <para>
/// WHY IT EXISTS: <see cref="PlaywrightTestBase"/> resurrects a self-hosted demo app that dies or
/// wedges mid-run so the suite can still finish — a deliberate behaviour. What it must NOT be is
/// invisible: until this log existed, a restart was a <c>context.WriteLine</c> nobody read, so a
/// crashed server was classified as "load flake". Every resurrection now appends one JSONL line to
/// <c>TestResults/host-restarts.jsonl</c> and increments <see cref="TotalHostRestarts"/>, so the
/// release-evidence run can refuse to report a run with <c>TotalHostRestarts &gt; 0</c> as a clean
/// full-suite green.
/// </para>
/// </summary>
internal static class HostRestartLog
{
    private static readonly object Gate = new();
    private static int _totalHostRestarts;
    private static int _totalBrowserRelaunches;

    /// <summary>Total resurrections recorded this run, across all hosts (Interlocked).</summary>
    internal static int TotalHostRestarts => Interlocked.CompareExchange(ref _totalHostRestarts, 0, 0);

    /// <summary>
    /// H4: total shared-browser relaunches recorded this run (Interlocked). The summary of a full
    /// lane run shows it next to <see cref="TotalHostRestarts"/>, so a run that had to resurrect
    /// the wedge-prone Chromium is visible without grepping the shard logs.
    /// </summary>
    internal static int TotalBrowserRelaunches => Interlocked.CompareExchange(ref _totalBrowserRelaunches, 0, 0);

    /// <summary>
    /// The JSON line appended per restart — the pure half of the recorder so the shape can be
    /// asserted without standing up Playwright or a demo host. <paramref name="exitCode"/> rides
    /// along when the dead process reported one: a negative/Win32-fault code classifies a crash
    /// while 1 with an otherwise-silent output tail points at an external hard kill — the
    /// distinction the release evidence needs to separate an app bug from an environment kill
    /// (2026-11-18 "Demo WASM died-mid-run" shipped no exit evidence at all). H2:
    /// <paramref name="testName"/> names the test whose run tripped the restart, so the JSONL
    /// answers "which test was in flight" without cross-referencing the TRX.
    /// </summary>
    internal static string BuildRestartLine(string host, string reason, DateTimeOffset atUtc, string recentOutput, int? exitCode = null, string? testName = null)
    {
        var record = new Dictionary<string, object?>
        {
            ["host"] = host,
            ["reason"] = reason,
            ["atUtc"] = atUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["recentOutput"] = recentOutput.Length > 2000 ? recentOutput[^2000..] : recentOutput,
        };
        if (exitCode is { } code)
        {
            record["exitCode"] = code;
        }
        if (!string.IsNullOrEmpty(testName))
        {
            record["test"] = testName;
        }

        return JsonSerializer.Serialize(record);
    }

    /// <summary>
    /// Appends one restart record to <paramref name="jsonlPath"/> and increments the counter.
    /// Returns the line written so callers/tests can log it.
    /// </summary>
    internal static string AppendRestartRecord(
        string jsonlPath, string host, string reason, string recentOutput, int? exitCode = null, string? testName = null)
    {
        // CF19e — the increment comes FIRST: the release gate reads TotalHostRestarts, so a
        // JSONL write that throws must still leave the resurrection counted. Writing the file
        // first meant a failed append kept the counter at zero and the run reported clean.
        Interlocked.Increment(ref _totalHostRestarts);
        string line = BuildRestartLine(host, reason, DateTimeOffset.UtcNow, recentOutput, exitCode, testName);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonlPath)!);
            File.AppendAllText(jsonlPath, line + Environment.NewLine);
        }

        return line;
    }

    /// <summary>
    /// The JSON line appended per browser relaunch (H4): one record per resurrection of the
    /// shared Chromium, with the reason (dead process vs. the wedged-browser force relaunch) and,
    /// when known, the test that was in flight.
    /// </summary>
    internal static string BuildBrowserRelaunchLine(string reason, DateTimeOffset atUtc, string? testName = null)
    {
        var record = new Dictionary<string, object?>
        {
            ["kind"] = "browser-relaunch",
            ["reason"] = reason,
            ["atUtc"] = atUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(testName))
        {
            record["test"] = testName;
        }

        return JsonSerializer.Serialize(record);
    }

    /// <summary>
    /// Appends one browser-relaunch record to <paramref name="jsonlPath"/> and increments
    /// <see cref="TotalBrowserRelaunches"/>. Counter first, mirroring <see cref="AppendRestartRecord"/>
    /// (CF19e): a failed append must not hide the relaunch from the lane summary.
    /// </summary>
    internal static string AppendBrowserRelaunchRecord(string jsonlPath, string reason, string? testName = null)
    {
        Interlocked.Increment(ref _totalBrowserRelaunches);
        string line = BuildBrowserRelaunchLine(reason, DateTimeOffset.UtcNow, testName);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonlPath)!);
            File.AppendAllText(jsonlPath, line + Environment.NewLine);
        }

        return line;
    }
}
