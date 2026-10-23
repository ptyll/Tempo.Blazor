using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Browser-free coverage of the E2E boot-readiness infrastructure (Fáze 1 of the follow-up plan):
/// the configurable readiness budget (<c>TM_E2E_READY_TIMEOUT_MS</c>), the ONE controlled reload on a
/// cold boot, and the guard that keeps that recovery in <see cref="PlaywrightTestBase"/> instead of
/// being copy-pasted into individual specs again. No Playwright browser and no demo host is needed, so
/// these run in the unit lane of the E2E assembly in milliseconds.
/// </summary>
[TestClass]
public class ReadinessInfrastructureTests
{
    // ── configurable readiness budget ───────────────────────────────────────────────────────

    [TestMethod]
    public void ReadyTimeout_DefaultsToMoreThanTheFormerSixtySeconds()
    {
        var timeouts = ReadinessTimeouts.Resolve(null);

        Assert.IsTrue(timeouts.ReadyMs > 60_000,
            $"the authoritative data-blazor-ready gate must be longer than the former 60 s, was {timeouts.ReadyMs} ms");
        Assert.IsTrue(timeouts.DomContentLoadedMs > 60_000,
            $"the DOMContentLoaded wait must be longer than the former 60 s, was {timeouts.DomContentLoadedMs} ms");
        Assert.IsTrue(timeouts.SelectorMs > 15_000,
            $"the app-shell selector wait must be longer than the former 15 s, was {timeouts.SelectorMs} ms");
        Assert.IsTrue(timeouts.FallbackMs > 45_000,
            $"the text-content fallback wait must be longer than the former 45 s, was {timeouts.FallbackMs} ms");
    }

    [TestMethod]
    public void ReadyTimeout_EnvironmentOverride_ScalesEveryPhase()
    {
        var timeouts = ReadinessTimeouts.Resolve("200000");

        Assert.AreEqual(200_000, timeouts.ReadyMs, "TM_E2E_READY_TIMEOUT_MS sets the data-blazor-ready cap");
        Assert.IsTrue(timeouts.DomContentLoadedMs >= 200_000, "the DOMContentLoaded wait follows the override");
        Assert.IsTrue(timeouts.SelectorMs > ReadinessTimeouts.Resolve(null).SelectorMs,
            "the shell selector wait scales with the override");
        Assert.IsTrue(timeouts.SelectorMs < timeouts.ReadyMs && timeouts.FallbackMs < timeouts.ReadyMs,
            "the pre-gates stay inside the authoritative gate budget");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("999999999999")]
    public void ReadyTimeout_InvalidOverride_FallsBackToTheDefault(string raw)
    {
        Assert.AreEqual(ReadinessTimeouts.Resolve(null), ReadinessTimeouts.Resolve(raw),
            $"an unusable TM_E2E_READY_TIMEOUT_MS ('{raw}') must not disable or zero the readiness gate");
    }

    // ── one controlled reload on a cold boot ────────────────────────────────────────────────

    [TestMethod]
    public async Task ColdBoot_ReadyOnTheFirstAttempt_NeverReloads()
    {
        int ready = 0, reloads = 0;

        await ColdBootRecovery.RunAsync(
            () => { ready++; return Task.CompletedTask; },
            () => { reloads++; return Task.CompletedTask; });

        Assert.AreEqual(1, ready);
        Assert.AreEqual(0, reloads, "a healthy boot must not pay a reload");
    }

    [TestMethod]
    public async Task ColdBoot_FirstAttemptTimesOut_ReloadsExactlyOnceAndSucceeds()
    {
        int ready = 0, reloads = 0;

        await ColdBootRecovery.RunAsync(
            () =>
            {
                ready++;
                return ready == 1 ? Task.FromException(new TimeoutException("cold boot")) : Task.CompletedTask;
            },
            () => { reloads++; return Task.CompletedTask; });

        Assert.AreEqual(2, ready, "the readiness gate runs again after the reload");
        Assert.AreEqual(1, reloads, "exactly one controlled reload");
    }

    [TestMethod]
    public async Task ColdBoot_TimesOutTwice_PropagatesAndDoesNotReloadAgain()
    {
        int ready = 0, reloads = 0;

        await Assert.ThrowsExceptionAsync<TimeoutException>(() => ColdBootRecovery.RunAsync(
            () => { ready++; return Task.FromException(new TimeoutException("still booting")); },
            () => { reloads++; return Task.CompletedTask; }));

        Assert.AreEqual(2, ready);
        Assert.AreEqual(1, reloads, "a second timeout is a real failure, not another reload");
    }

    [TestMethod]
    public async Task ColdBoot_NonTimeoutFailure_IsNotRetried()
    {
        int ready = 0, reloads = 0;

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => ColdBootRecovery.RunAsync(
            () => { ready++; return Task.FromException(new InvalidOperationException("assertion-like failure")); },
            () => { reloads++; return Task.CompletedTask; }));

        Assert.AreEqual(1, ready);
        Assert.AreEqual(0, reloads, "only a readiness TIMEOUT is a cold boot; anything else must surface immediately");
    }

    [TestMethod]
    public async Task ColdBoot_PlaywrightTimeout_CountsAsColdBoot()
    {
        int reloads = 0;
        int ready = 0;

        await ColdBootRecovery.RunAsync(
            () =>
            {
                ready++;
                return ready == 1
                    ? Task.FromException(new Microsoft.Playwright.PlaywrightException("Timeout 60000ms exceeded."))
                    : Task.CompletedTask;
            },
            () => { reloads++; return Task.CompletedTask; });

        Assert.AreEqual(1, reloads, "Playwright reports wait timeouts as PlaywrightException('Timeout …ms exceeded')");
    }

    // ── recovery lives in the base, not in the specs ────────────────────────────────────────

    // Matches the hand-rolled cold-boot recoveries that used to be copy-pasted into individual specs (Email F8/F9/F10,
    // Wireframe, Audit log, charts …). The retry belongs to PlaywrightTestBase.GotoAppReadyAsync. Two shapes, checked
    // only in files that call WaitForAppReadyAsync:
    //   1. catch (TimeoutException | PlaywrightException | Exception …) { … ReloadAsync }   (reload-in-catch)
    //   2. catch (TimeoutException | PlaywrightException | Exception …) when (… attempt …)  (attempt-loop retry)
    private const string CaughtType = @"(?:System\.|Microsoft\.Playwright\.)?(?:Timeout|Playwright)?Exception";

    private static readonly Regex LocalRecovery = new(
        @"catch\s*\(\s*" + CaughtType + @"\b[^)]*\)[^{]*\{[^}]*?ReloadAsync"
        + @"|catch\s*\(\s*" + CaughtType + @"\b[^)]*\)\s*when\s*\([^)]*\battempt\b",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [TestMethod]
    public void NoSpecKeepsALocalColdBootRecoveryCopy()
    {
        var root = FindE2EDirectory();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(Path.GetFileName(file), "ReadinessInfrastructureTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (!text.Contains("WaitForAppReadyAsync", StringComparison.Ordinal)) continue;
            if (LocalRecovery.IsMatch(text)) offenders.Add(relative);
        }

        Assert.AreEqual(0, offenders.Count,
            "cold-boot recovery must be centralized in PlaywrightTestBase.GotoAppReadyAsync; local copies in: "
            + string.Join(", ", offenders));
    }

    private static string FindE2EDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return Path.Combine(current.FullName, "tests", "Tempo.Blazor.E2E");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from the test output directory.");
    }
}
