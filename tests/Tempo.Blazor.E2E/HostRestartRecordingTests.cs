using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Unit-level coverage of the machine-readable restart record (N209) and the /health readiness
/// verdict (N193) — deliberately NOT a Playwright test: the pure recorder and the status-code
/// predicate are exercised directly so this class needs no browser and no demo host.
/// </summary>
[TestClass]
[DoNotParallelize] // TotalHostRestarts is a process-wide counter; parallel members would race it.
public class HostRestartRecordingTests
{
    [TestMethod]
    public void RestartRecord_AppendsJsonl_AndIncrementsCounter()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tm-hostrestart-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "host-restarts.jsonl");
        int before = HostRestartLog.TotalHostRestarts;

        try
        {
            string line = HostRestartLog.AppendRestartRecord(
                path, "Demo API", "died-mid-run", "Now listening on: https://localhost:5100\nfail: \"boom\"");

            Assert.IsTrue(File.Exists(path), "the record must create the JSONL file");
            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(1, lines.Length, "exactly one JSONL line per restart");
            Assert.AreEqual(line, lines[0], "the returned line is the persisted line");

            using JsonDocument document = JsonDocument.Parse(lines[0]);
            JsonElement root = document.RootElement;
            Assert.AreEqual("Demo API", root.GetProperty("host").GetString());
            Assert.AreEqual("died-mid-run", root.GetProperty("reason").GetString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("atUtc").GetString()),
                "the record must carry a timestamp, not just prose");
            StringAssert.Contains(root.GetProperty("recentOutput").GetString()!, "boom");

            Assert.AreEqual(before + 1, HostRestartLog.TotalHostRestarts,
                "each recorded restart must be visible to the release-evidence verdict");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* temp cleanup */ }
        }
    }

    [TestMethod]
    public void BuildRestartLine_EscapesJson_AndCapsRecentOutput()
    {
        string longOutput = new string('x', 5000) + "tail-\"quoted\"";
        string line = HostRestartLog.BuildRestartLine(
            "Demo WASM", "unreachable-past-window", new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), longOutput);

        using JsonDocument document = JsonDocument.Parse(line);
        string recent = document.RootElement.GetProperty("recentOutput").GetString()!;
        Assert.IsTrue(recent.Length <= 2000,
            $"recentOutput must be capped so a chatty crash cannot bloat the log (got {recent.Length})");
        StringAssert.EndsWith(recent, "tail-\"quoted\"",
            "the cap keeps the END of the output — the crash line is what matters");
        Assert.AreEqual("unreachable-past-window", document.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>
    /// The readiness verdict arms — including the contrast that is the whole point of N193: under
    /// the old <c>status != ServiceUnavailable</c> predicate a crashed app answering 500 read as
    /// reachable; under this one it does not.
    /// </summary>
    [TestMethod]
    public void HealthProbe_OnlyTwoHundredCountsAsReady()
    {
        Assert.IsTrue(PlaywrightTestBase.IsReadyStatusCode(HttpStatusCode.OK));
        Assert.IsTrue(PlaywrightTestBase.IsReadyStatusCode(HttpStatusCode.NoContent));

        Assert.IsFalse(PlaywrightTestBase.IsReadyStatusCode(HttpStatusCode.InternalServerError),
            "a crashed app answering 500 must NOT read as ready — the old !=503 predicate passed it");
        Assert.IsFalse(PlaywrightTestBase.IsReadyStatusCode(HttpStatusCode.ServiceUnavailable));
        Assert.IsFalse(PlaywrightTestBase.IsReadyStatusCode(HttpStatusCode.NotFound),
            "a misrouted 404 is not a ready app");
        Assert.IsFalse(PlaywrightTestBase.IsReadyStatusCode(HttpStatusCode.Redirect));
    }
}
