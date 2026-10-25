using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Source guard (browser-free) for the document-editor canvas specs. Chromium answers an async <c>Runtime.callFunctionOn</c> that awaits a
/// page-side promise / timer / dynamic <c>import()</c> with <c>{"code":-32000,"message":"Promise was collected"}</c> while the WASM demo host is
/// busy, and Playwright 1.51 rewrites that into "Execution context was destroyed, most likely because of a navigation" (see
/// docs/e2e-test-lanes.md). Specs used to retry around the message, which hid a defect class instead of a navigation; they now read the
/// engine through <see cref="CanvasInteropBridge"/> (synchronous) and poll with synchronous predicates. This guard keeps both from coming back:
/// <list type="bullet">
/// <item>no <c>DocumentEditorCanvas*E2ETests</c> / <c>CanvasInteropBridge</c> file may catch or match the "Execution context was destroyed" /
/// "because of a navigation" text (a genuine navigation is asserted through <c>FrameNavigated</c> counting, not by retrying it away);</item>
/// <item>no page script of those files may contain <c>await new Promise(…)</c>, <c>setTimeout</c> inside an awaited promise, or an
/// <c>await import(</c> of the canvas interop module.</item>
/// </list>
/// </summary>
[TestClass]
public class CanvasEvaluateGuardTests
{
    private static readonly Regex RetryOnContextLoss = new(
        @"Execution context was destroyed|because of a navigation|Cannot find context with specified id",
        RegexOptions.Compiled);

    private static readonly Regex AwaitedPageTimer = new(
        @"await\s+new\s+Promise|await\s+import\(\s*'/_content/Tempo\.Blazor\.DocumentEditor/js/document-editor-canvas/",
        RegexOptions.Compiled);

    /// <summary>
    /// Canvas specs that still run wait import(...) / wait new Promise(...) inside one evaluate. They carry NO retry wrapper (a failure there
    /// surfaces, if misleadingly), so they are outside the retry-removal, but they are the same defect class: convert one to
    /// <see cref="CanvasInteropBridge"/> and delete it from this frozen list. The list may only shrink.
    /// </summary>
    private static readonly HashSet<string> NotYetConverted = new(StringComparer.Ordinal)
    {
        "DocumentEditorCanvasAccessibilityE2ETests.cs",
        "DocumentEditorCanvasCommentsRevisionsE2ETests.cs",
        "DocumentEditorCanvasContentControlsE2ETests.cs",
        "DocumentEditorCanvasHeadersFootersNotesE2ETests.cs",
        "DocumentEditorCanvasHyphenationAdvancedTablesE2ETests.cs",
        "DocumentEditorCanvasImageE2ETests.cs",
        "DocumentEditorCanvasImageFormattingFixE2ETests.cs",
        "DocumentEditorCanvasTableE2ETests.cs",
        "DocumentEditorCanvasTabStopsE2ETests.cs",
        "DocumentEditorCanvasViewModesPrintE2ETests.cs",
    };

    private static IEnumerable<string> CanvasSpecFiles()
    {
        var root = FindE2EDirectory();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("DocumentEditorCanvas", StringComparison.Ordinal) || name == "CanvasInteropBridge.cs";
            });
    }

    [TestMethod]
    public void NoCanvasSpecRetriesAroundExecutionContextLoss()
    {
        var offenders = new List<string>();
        foreach (var file in CanvasSpecFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (RetryOnContextLoss.IsMatch(lines[i])) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "a spec matches the 'Execution context was destroyed' text: that message is how Playwright 1.51 reports 'Promise was collected' (an async evaluate awaiting a page promise), "
            + "so a retry around it masks that defect. Use CanvasInteropBridge + synchronous evaluates / predicates; assert real navigations with FrameNavigated. Offenders: "
            + string.Join(", ", offenders));
    }

    [TestMethod]
    public void NoCanvasSpecAwaitsAPagePromiseInsideAnEvaluate()
    {
        var offenders = new List<string>();
        foreach (var file in CanvasSpecFiles())
        {
            if (NotYetConverted.Contains(Path.GetFileName(file))) continue;
            var text = File.ReadAllText(file);
            foreach (Match raw in Regex.Matches(text, "\"\"\"(?<b>.*?)\"\"\"", RegexOptions.Singleline))
            {
                if (!AwaitedPageTimer.IsMatch(raw.Groups["b"].Value)) continue;
                var line = text.AsSpan(0, raw.Index).Count('\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "a page script awaits a promise / dynamic import inside one evaluate (Chromium may answer 'Promise was collected'). Load modules once through "
            + "CanvasInteropBridge.EnsureLoadedAsync and poll from C# with synchronous predicates. Offenders: " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void TheNotYetConvertedListOnlyNamesSpecsThatStillNeedIt()
    {
        var stale = new List<string>();
        foreach (var name in NotYetConverted)
        {
            var path = Path.Combine(FindE2EDirectory(), name);
            if (!File.Exists(path) || !AwaitedPageTimer.IsMatch(File.ReadAllText(path))) stale.Add(name);
        }

        Assert.AreEqual(0, stale.Count, "these specs no longer await a page promise inside an evaluate - remove them from NotYetConverted: " + string.Join(", ", stale));
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
