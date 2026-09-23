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

    /// <summary>Total resurrections recorded this run, across all hosts (Interlocked).</summary>
    internal static int TotalHostRestarts => Interlocked.CompareExchange(ref _totalHostRestarts, 0, 0);

    /// <summary>
    /// The JSON line appended per restart — the pure half of the recorder so the shape can be
    /// asserted without standing up Playwright or a demo host.
    /// </summary>
    internal static string BuildRestartLine(string host, string reason, DateTimeOffset atUtc, string recentOutput)
    {
        var record = new Dictionary<string, string>
        {
            ["host"] = host,
            ["reason"] = reason,
            ["atUtc"] = atUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["recentOutput"] = recentOutput.Length > 2000 ? recentOutput[^2000..] : recentOutput,
        };
        return JsonSerializer.Serialize(record);
    }

    /// <summary>
    /// Appends one restart record to <paramref name="jsonlPath"/> and increments the counter.
    /// Returns the line written so callers/tests can log it.
    /// </summary>
    internal static string AppendRestartRecord(
        string jsonlPath, string host, string reason, string recentOutput)
    {
        string line = BuildRestartLine(host, reason, DateTimeOffset.UtcNow, recentOutput);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonlPath)!);
            File.AppendAllText(jsonlPath, line + Environment.NewLine);
        }

        Interlocked.Increment(ref _totalHostRestarts);
        return line;
    }
}
