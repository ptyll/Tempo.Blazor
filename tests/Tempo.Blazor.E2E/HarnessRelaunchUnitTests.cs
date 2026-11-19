using System.Diagnostics;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Unit-level coverage of the shared-browser relaunch path (B1) and the demo-host exit-code
/// capture (B2) — deliberately NOT Playwright tests: the fake launcher and a real short-lived
/// process exercise the harness seams so this class needs no browser and no demo host.
/// </summary>
[TestClass]
[DoNotParallelize] // The seams mutate shared statics (BrowserLaunchOverride, the browser statics).
public class HarnessRelaunchUnitTests
{
    /// <summary>
    /// B1: ForceRelaunchBrowserAsync used to call EnsureBrowserAsync while ALREADY holding
    /// BrowserLock — SemaphoreSlim is not reentrant, so the first wedged-browser recovery
    /// deadlocked the shard forever. With the launch extracted into the lock-free core, a relaunch
    /// with a FAKE launcher must complete well inside 5 s.
    /// </summary>
    [TestMethod]
    public async Task ForceRelaunchBrowserAsync_CompletesWithAFakeLauncher_InUnderFiveSeconds()
    {
        var savedBrowserStatics = PlaywrightTestBase.SnapshotBrowserForTest();
        var savedOverride = PlaywrightTestBase.BrowserLaunchOverride;
        PlaywrightTestBase.BrowserLaunchOverride = () => Task.FromResult<IBrowser?>(null);
        PlaywrightTestBase.RestoreBrowserForTest(null, null);

        try
        {
            var watch = Stopwatch.StartNew();
            await PlaywrightTestBase.ForceRelaunchBrowserAsync(testName: "ForceRelaunchBrowserAsync_CompletesWithAFakeLauncher_InUnderFiveSeconds");
            watch.Stop();

            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5),
                $"the relaunch took {watch.Elapsed.TotalSeconds:0.0}s — it deadlocked on the re-entered "
                + "BrowserLock (SemaphoreSlim is not reentrant) instead of launching through the lock-free core");
        }
        finally
        {
            PlaywrightTestBase.BrowserLaunchOverride = savedOverride;
            PlaywrightTestBase.RestoreBrowserForTest(savedBrowserStatics.Browser, savedBrowserStatics.Playwright);
        }
    }

    /// <summary>
    /// B2: the died-mid-run restart record reads <c>existing.ExitCode</c> AFTER
    /// <c>existing.Dispose()</c>. A disposed Process throws InvalidOperationException from
    /// HasExited/ExitCode, so the record used to crash instead of capturing the evidence. The
    /// exit code must now survive Dispose: a real short-lived process's code is readable after
    /// the host process object is disposed.
    /// </summary>
    [TestMethod]
    public void DemoHostProcess_ExitCodeSurvivesDispose()
    {
        const int expectedExitCode = 42;
        using var process = StartProcessThatExitsWithCode(expectedExitCode);
        var host = new PlaywrightTestBase.DemoHostProcess("unit-test host", process);

        // The test launch profile throttles restarts inside 30 s, so the real path reads the exit
        // code only after Dispose() — exactly the sequence that used to throw.
        host.Dispose();

        Assert.AreEqual(expectedExitCode, host.ExitCode,
            "the exit code captured inside Dispose() must survive the disposed Process object");
    }

    [TestMethod]
    public void DemoHostProcess_ExitCodeIsNullWhileTheProcessIsAliveOrUnknown()
    {
        var host = new PlaywrightTestBase.DemoHostProcess("unit-test host", StartProcessThatExitsWithCode(7));

        // Immediately after Start the child may not have exited yet — either way, a live or
        // un-reaped process must answer null, not a code and not an exception.
        if (!host.HasExited)
        {
            Assert.IsNull(host.ExitCode, "a live process has no exit code yet");
        }

        host.Dispose();
        Assert.IsNotNull(host.ExitCode, "after Dispose the captured exit code is known");
    }

    /// <summary>
    /// H2: the restart record names the test whose run tripped the restart ("test" field), so the
    /// JSONL answers "which test was in flight" without cross-referencing the TRX.
    /// </summary>
    [TestMethod]
    public void BuildRestartLine_CarriesTheTestName_WhenOneIsKnown()
    {
        string withTest = HostRestartLog.BuildRestartLine(
            "Demo WASM", "died-mid-run", new DateTimeOffset(2026, 11, 19, 4, 0, 0, TimeSpan.Zero),
            "tail", testName: "SomeSurface_OpensAtEveryBand");
        using (JsonDocument document = JsonDocument.Parse(withTest))
        {
            Assert.AreEqual("SomeSurface_OpensAtEveryBand", document.RootElement.GetProperty("test").GetString());
        }

        string withoutTest = HostRestartLog.BuildRestartLine(
            "Demo API", "externally-died", new DateTimeOffset(2026, 11, 19, 4, 0, 0, TimeSpan.Zero), "");
        using JsonDocument absent = JsonDocument.Parse(withoutTest);
        Assert.IsFalse(absent.RootElement.TryGetProperty("test", out _),
            "no test context means no test property — an absent field is easier to query than a null");
    }

    /// <summary>
    /// H4: a browser relaunch appends one record and increments
    /// <see cref="HostRestartLog.TotalBrowserRelaunches"/> — the counter the lane summary reports.
    /// </summary>
    [TestMethod]
    public void BrowserRelaunchRecord_AppendsJsonl_AndIncrementsTheCounter()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tm-browserrelaunch-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "browser-relaunches.jsonl");
        int before = HostRestartLog.TotalBrowserRelaunches;

        try
        {
            string line = HostRestartLog.AppendBrowserRelaunchRecord(path, "wedged-browser-force-relaunch", "Some_Test");

            Assert.IsTrue(File.Exists(path), "the record must create the JSONL file");
            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(1, lines.Length, "exactly one JSONL line per relaunch");

            using JsonDocument document = JsonDocument.Parse(lines[0]);
            var root = document.RootElement;
            Assert.AreEqual("browser-relaunch", root.GetProperty("kind").GetString());
            Assert.AreEqual("wedged-browser-force-relaunch", root.GetProperty("reason").GetString());
            Assert.AreEqual("Some_Test", root.GetProperty("test").GetString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("atUtc").GetString()),
                "the record must carry a timestamp");

            Assert.AreEqual(before + 1, HostRestartLog.TotalBrowserRelaunches,
                "each recorded relaunch must be visible to the lane summary");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* temp cleanup */ }
        }
    }

    private static Process StartProcessThatExitsWithCode(int expectedExitCode)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add($"exit {expectedExitCode}");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"exit {expectedExitCode}");
        }

        var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start(), "the fixture process must start");
        process.WaitForExit(10000);
        Assert.IsTrue(process.HasExited, "the fixture process must exit on its own");
        Assert.AreEqual(expectedExitCode, process.ExitCode, "fixture sanity: the process reports its own code while alive");
        return process;
    }
}
