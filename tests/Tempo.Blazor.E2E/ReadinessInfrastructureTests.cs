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
    public void NotionDatabasePhase8And9_SwitchAsync_DoesNotSleepAfterViewChange()
    {
        var root = FindE2EDirectory();
        foreach (var name in new[] { "NotionDatabasePhase8E2ETests.cs", "NotionDatabasePhase9E2ETests.cs" })
        {
            var text = File.ReadAllText(Path.Combine(root, name));
            Assert.IsFalse(
                text.Contains("WaitForTimeoutAsync(600)", StringComparison.Ordinal),
                $"{name} SwitchAsync must wait for the switched view surface, not sleep 600 ms.");
        }
    }

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

    // ── a spec must open its contexts and boot its pages through the central helpers ───────
    //
    // 2026-11-20 (targeted final run): Scheduler_Touch_SwipeFromEventCentre_Scrolls_AndDoesNotResize (768) sat 120 s in
    // WaitForAppReadyAsync because the WASM app never rendered `main` (a stalled/aborted boot fetch leaves the Blazor
    // loader on "Loading components..." for good; ONE reload boots in ~5 s). PointerDragE2ETests built its context with
    // Browser.NewContextAsync and its page with GotoAsync + WaitForAppReadyAsync, so it had neither the controlled
    // reload of GotoAppReadyAsync/ReloadAppReadyAsync nor the failure trace of CreateContextAsync. The same shape is in
    // ~370 other files, so the guard is general and ratcheted: a context comes from PlaywrightTestBase.CreateContextAsync,
    // a navigation from GotoAppReadyAsync / ReloadAppReadyAsync. Existing offenders are listed below with their count;
    // converting one LOWERS its count (update the entry, delete it at zero), a new offender - or a growing count - fails.

    private const string FirefoxLeg = "permanent: the shared browser is Chromium, this Firefox leg launches its own Playwright + browser";
    private const string OwnHost = "permanent: starts its own dotnet host (not the shared demo hosts), so it has no shared browser or boot recovery";

    private static readonly (string Id, Regex Pattern, string Hint)[] BootRules =
    [
        ("R1 NewContextAsync", new Regex(@"\.NewContextAsync\(", RegexOptions.Compiled),
            "create the context with PlaywrightTestBase.CreateContextAsync (trace, bounded relaunch, cleanup registration)"),
        ("R2 Goto/Reload + WaitForAppReadyAsync", new Regex(@"\.(?:Goto|Reload)Async\([^;]*\);\s*await\s+WaitForAppReadyAsync\(", RegexOptions.Compiled | RegexOptions.Singleline),
            "navigate with GotoAppReadyAsync / ReloadAppReadyAsync (the ONE controlled cold-boot reload)"),
        ("R3 WaitForFunction(data-blazor-ready)", new Regex(@"WaitForFunctionAsync\(\s*""[^""]*data-blazor-ready", RegexOptions.Compiled | RegexOptions.Singleline),
            "a hand-written data-blazor-ready wait skips the recovery; use GotoAppReadyAsync / ReloadAppReadyAsync"),
        ("R4 RegisterContext", new Regex(@"\bRegisterContext\(", RegexOptions.Compiled),
            "a context that needs RegisterContext was not made by CreateContextAsync"),
    ];

    /// <summary>
    /// Rules whose target is 0 and that are NOT part of the ratchet (never summed into <see cref="KnownOffenders"/>).
    /// Playwright .NET takes the init script as a SOURCE string and evaluates it: an arrow function is only an
    /// expression statement that is never called, so the script silently does nothing (the Signing/Diagram specs
    /// lost their culture pin this way).
    /// </summary>
    private static readonly (string Id, Regex Pattern, string Hint)[] ZeroRules =
    [
        ("R6 AddInitScriptAsync arrow function",
            new Regex(@"AddInitScriptAsync\(\s*(?:@\$|\$@|@|\$)?""\s*(?:async\s*)?\(\s*\)\s*=>", RegexOptions.Compiled),
            "an init script is a statement list, not a function: write \"localStorage.setItem(...);\", never \"() => ...\""),
    ];

    /// <summary>
    /// The ratchet: per file (relative to the E2E project, forward slashes) the number of rule hits tolerated today and
    /// why. Computed by running <see cref="BootRules"/> over every comment-stripped <c>*.cs</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Count, string Reason)> KnownOffenders =
        new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
        {
        ["FilterableDropdownE2ETests.cs"] = (1, FirefoxLeg),
        ["NotionOverlayInModalE2ETests.cs"] = (1, FirefoxLeg),
        ["OverlayPanelE2ETests.cs"] = (1, FirefoxLeg),
        ["ReportingF0FidelityE2ETests.cs"] = (1, OwnHost),
        ["ReportingF12ReportServerWebE2ETests.cs"] = (1, OwnHost),
        ["ReportingF13ReportDesignerE2ETests.cs"] = (1, OwnHost),
        ["ReportingF14ChartsE2ETests.cs"] = (1, OwnHost),
        ["ReportingF15ExportE2ETests.cs"] = (1, OwnHost),
        ["ReportingF16SchedulingE2ETests.cs"] = (1, OwnHost),
        ["ReportingF17EmbeddingE2ETests.cs"] = (1, OwnHost),
        ["ReportingF5TextLayoutE2ETests.cs"] = (1, OwnHost),
        ["ReportingF6InvoicePaginationE2ETests.cs"] = (1, OwnHost),
        ["ReportingF7TablixE2ETests.cs"] = (1, OwnHost),
        ["ReportingF8PdfRendererE2ETests.cs"] = (1, OwnHost),
        ["ReportingF9ReportViewerE2ETests.cs"] = (1, OwnHost),
        ["TmDataTableSortButtonE2ETests.cs"] = (1, FirefoxLeg),
        ["TmDataTableSortButtonVisualE2ETests.cs"] = (1, FirefoxLeg),
        };

    /// <summary>
    /// Every scanned <c>*.cs</c> of the E2E project (relative path with forward slashes + comment-stripped text):
    /// bin/obj, this guard and the central <c>PlaywrightTestBase</c> partials are excluded.
    /// </summary>
    private static IEnumerable<(string Relative, string Text)> EnumerateScannedSources(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative is "ReadinessInfrastructureTests.cs"
                // PlaywrightTestBase is a partial class split across PlaywrightTestBase.cs and
                // PlaywrightTestBase.<Part>.cs (source-file ceiling); every part is the central harness.
                || Regex.IsMatch(relative, @"^PlaywrightTestBase(\.[A-Za-z]+)?\.cs$"))
            {
                continue;
            }

            yield return (relative, StripComments(File.ReadAllText(file)));
        }
    }

    [TestMethod]
    public void NoInitScriptIsAnArrowFunction()
    {
        var root = FindE2EDirectory();
        var offenders = new List<string>();
        foreach (var (relative, text) in EnumerateScannedSources(root))
        {
            foreach (var (id, pattern, _) in ZeroRules)
            {
                var hits = pattern.Matches(text).Count;
                if (hits > 0)
                {
                    offenders.Add($"{relative}: {id} x{hits}");
                }
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "an init script is a SOURCE string, not a function (the arrow is never called); this rule is not ratcheted (target 0, KnownOffenders does not apply):\n  "
            + string.Join("\n  ", offenders) + "\n  " + string.Join(" | ", ZeroRules.Select(r => $"{r.Id}: {r.Hint}")));
    }

    /// <summary>
    /// R8 (remaining 7f95b6e6): a direct <c>WaitForFunctionAsync</c> must not take an <c>async</c> predicate
    /// (a Promise is truthy, so the wait never observes the resolved boolean) and must not pass
    /// <see cref="Microsoft.Playwright.PageWaitForFunctionOptions"/> as the second argument (Playwright .NET
    /// binds that object as <c>arg</c> and then uses the default 30 s timeout). Target 0; use
    /// <c>WaitForPredicateAsync</c>.
    /// </summary>
    [TestMethod]
    public void WaitForFunctionAsync_NeverTakesAnAsyncPredicateOrOptionsAsArg()
    {
        var root = FindE2EDirectory();
        var offenders = new List<string>();
        foreach (var (relative, text) in EnumerateScannedSources(root))
        {
            var asyncHits = 0;
            var optionsAsArgHits = 0;
            foreach (var args in EnumerateWaitForFunctionArgs(text))
            {
                if (args.Count >= 1 && FirstArgumentLooksLikeAsyncPredicate(args[0]))
                {
                    asyncHits++;
                }

                if (args.Count == 2 && args[1].Contains("new PageWaitForFunctionOptions", StringComparison.Ordinal))
                {
                    optionsAsArgHits++;
                }
            }

            if (asyncHits > 0)
            {
                offenders.Add($"{relative}: R8 async WaitForFunction predicate x{asyncHits}");
            }

            if (optionsAsArgHits > 0)
            {
                offenders.Add($"{relative}: R8 WaitForFunction options bound as arg x{optionsAsArgHits}");
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "R8: WaitForFunctionAsync must not take an async predicate or bind PageWaitForFunctionOptions as arg; convert to WaitForPredicateAsync:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateWaitForFunctionArgs(string text)
    {
        const string needle = "WaitForFunctionAsync(";
        var i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            var open = i + needle.Length - 1;
            if (TryReadInvocationArgs(text, open, out var args, out var end))
            {
                yield return args;
                i = end;
            }
            else
            {
                i += needle.Length;
            }
        }
    }

    private static bool FirstArgumentLooksLikeAsyncPredicate(string argument)
    {
        var trimmed = argument.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // Strip C# string delimiters so `async` inside the page function is visible.
        trimmed = trimmed.TrimStart('@', '$');
        if (trimmed.StartsWith("\"\"\"", StringComparison.Ordinal) && trimmed.EndsWith("\"\"\"", StringComparison.Ordinal) && trimmed.Length >= 6)
        {
            trimmed = trimmed[3..^3];
        }
        else if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal) && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1];
        }

        trimmed = trimmed.TrimStart();
        return trimmed.StartsWith("async ", StringComparison.Ordinal)
            || trimmed.StartsWith("async(", StringComparison.Ordinal);
    }

    private static bool TryReadInvocationArgs(string text, int openParen, out List<string> args, out int end)
    {
        args = [];
        end = openParen;
        if (openParen < 0 || openParen >= text.Length || text[openParen] != '(')
        {
            return false;
        }

        var paren = 0;
        var brace = 0;
        var bracket = 0;
        var start = openParen + 1;
        var i = openParen;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                if (!TrySkipString(text, ref i))
                {
                    return false;
                }

                continue;
            }

            switch (c)
            {
                case '(':
                    paren++;
                    break;
                case ')':
                    if (paren == 1 && brace == 0 && bracket == 0)
                    {
                        args.Add(text[start..i].Trim());
                        end = i + 1;
                        args.RemoveAll(static a => a.Length == 0);
                        return true;
                    }

                    paren--;
                    break;
                case '{':
                    brace++;
                    break;
                case '}':
                    brace--;
                    break;
                case '[':
                    bracket++;
                    break;
                case ']':
                    bracket--;
                    break;
                case ',' when paren == 1 && brace == 0 && bracket == 0:
                    args.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
            }

            i++;
        }

        return false;
    }

    private static bool TrySkipString(string text, ref int i)
    {
        if (i + 2 < text.Length && text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')
        {
            i += 3;
            while (i + 2 < text.Length)
            {
                if (text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')
                {
                    i += 3;
                    return true;
                }

                i++;
            }

            return false;
        }

        var quote = text[i++];
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == quote)
            {
                i++;
                return true;
            }

            i++;
        }

        return false;
    }

    [TestMethod]
    public void NoSpecOpensItsOwnContextOrBootsPastTheCentralRecovery_RatchetedByKnownOffenders()
    {
        var root = FindE2EDirectory();
        var actual = new SortedDictionary<string, (int Count, string Detail)>(StringComparer.Ordinal);
        foreach (var (relative, text) in EnumerateScannedSources(root))
        {
            var total = 0;
            var rules = new List<string>();
            foreach (var (id, pattern, _) in BootRules)
            {
                var hits = pattern.Matches(text).Count;
                if (hits > 0)
                {
                    total += hits;
                    rules.Add($"{id} x{hits}");
                }
            }

            if (total > 0)
            {
                actual[relative] = (total, string.Join(", ", rules));
            }
        }

        var problems = new List<string>();
        foreach (var (file, (count, detail)) in actual)
        {
            if (!KnownOffenders.TryGetValue(file, out var known))
            {
                problems.Add($"NEW offender {file}: {count} hit(s) [{detail}]. " + string.Join(" | ", BootRules.Select(r => $"{r.Id}: {r.Hint}")));
            }
            else if (count > known.Count)
            {
                problems.Add($"GREW {file}: {known.Count} -> {count} [{detail}]. New code must use the central helpers.");
            }
            else if (count < known.Count)
            {
                problems.Add($"SHRANK {file}: {known.Count} -> {count} [{detail}]. Lower its KnownOffenders entry to {count} (delete it at 0) so the gain cannot regress.");
            }
        }

        foreach (var (file, known) in KnownOffenders)
        {
            if (!actual.ContainsKey(file))
            {
                problems.Add(File.Exists(Path.Combine(root, file))
                    ? $"CONVERTED {file}: no hit left (was {known.Count}). Delete its KnownOffenders entry."
                    : $"GONE {file}: the file no longer exists. Delete its KnownOffenders entry.");
            }
        }

        Assert.AreEqual(0, problems.Count,
            "context creation / boot readiness must go through PlaywrightTestBase (docs/e2e-test-lanes.md \"Boot readiness and cold-boot recovery\"):\n  "
            + string.Join("\n  ", problems));
    }

    /// <summary>
    /// Removes <c>//</c> and <c>/* */</c> comments but leaves string, char, verbatim and raw-string literals intact
    /// (a <c>//</c> inside a URL or a JS snippet is not a comment), so a rule never matches prose in a comment.
    /// </summary>
    internal static string StripComments(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        int i = 0, n = source.Length;
        while (i < n)
        {
            var c = source[i];
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        sb.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(n, i + 2);
                sb.Append(' ');
                continue;
            }

            if (c == '"' && i + 2 < n && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var quotes = 0;
                while (i + quotes < n && source[i + quotes] == '"')
                {
                    quotes++;
                }

                var start = i;
                i += quotes;
                var end = source.IndexOf(new string('"', quotes), i, StringComparison.Ordinal);
                end = end < 0 ? n : end + quotes;
                sb.Append(source, start, end - start);
                i = end;
                continue;
            }

            if (c == '"')
            {
                var verbatim = (i > 0 && source[i - 1] == '@') || (i > 1 && source[i - 1] == '$' && source[i - 2] == '@');
                var start = i++;
                while (i < n)
                {
                    if (verbatim)
                    {
                        if (source[i] == '"' && i + 1 < n && source[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }

                        if (source[i] == '"')
                        {
                            i++;
                            break;
                        }
                    }
                    else
                    {
                        if (source[i] == '\\')
                        {
                            i += 2;
                            continue;
                        }

                        if (source[i] == '"' || source[i] == '\n')
                        {
                            i++;
                            break;
                        }
                    }

                    i++;
                }

                i = Math.Min(i, n);
                sb.Append(source, start, i - start);
                continue;
            }

            if (c == '\'')
            {
                var start = i++;
                while (i < n)
                {
                    if (source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == '\'' || source[i] == '\n')
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                i = Math.Min(i, n);
                sb.Append(source, start, i - start);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
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
