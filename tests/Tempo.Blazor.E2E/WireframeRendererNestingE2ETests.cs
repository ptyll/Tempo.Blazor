using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-1 browser DOM guard (WASM lane): eight repeat-based stencils inserted into the REAL
/// running wireframe editor must nest their transforms. The pre-existing renderer defect emitted
/// every <c>&lt;g transform=…&gt;</c> as its own Blazor markup frame; the browser parses each
/// <c>AddMarkupContent</c> chunk into its own template, auto-closes the unclosed <c>&lt;g&gt;</c>
/// and turns the children into siblings — so every repeat item painted collapsed on top of the
/// first one. The server-side string renderer and every string-asserting bUnit suite are blind to
/// this; only the DOM can see it.
///
/// For each stencil the guard asserts:
/// (a) inside <c>g[data-el-id] &gt; svg</c> no <c>g[transform]</c> is empty — a split open/close
///     pair always leaves an auto-closed empty <c>&lt;g&gt;</c> behind;
/// (b) labels of repeated items do not share one position — Day/Week/Month and the duplicated
///     Meeting labels must have distinct screen positions once their transforms nest. The check
///     uses <c>getBoundingClientRect()</c> (viewport coordinates, so parent &lt;g&gt; transforms
///     apply) and flags ANY two non-empty labels within 0.5 px of each other, regardless of text;
/// (c) the toolbox preview renders through the same <c>RenderSvg</c> and must not contain an empty
///     <c>g[transform]</c> either — built-in stencils carry resolvable icons so the mini-SVG preview
///     only renders for an unknown icon name (<c>edit-3</c> on TmRichEditorFull, pinned explicitly so
///     the guard never goes vacuous).
/// </summary>
[TestClass]
[TestCategory("Wireframe")]
public sealed class WireframeRendererNestingE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task RepeatBasedStencils_NestGroups_InElementSvg_AndToolboxPreview()
        => await WireframeRendererNestingGuard.RunAsync(
            BaseUrl,
            () => CreateContextAsync(),
            (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The same DOM guard on the Blazor Server demo (:7107).</summary>
[TestClass]
[TestCategory("Server")]
[TestCategory("Wireframe")]
public sealed class WireframeRendererNestingServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task RepeatBasedStencils_NestGroups_On_The_Server_Host()
        => await WireframeRendererNestingGuard.RunAsync(
            BaseUrl,
            () => CreateContextAsync(),
            (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The same DOM guard on the InteractiveAuto demo (:7108).</summary>
[TestClass]
[TestCategory("InteractiveAuto")]
[TestCategory("Wireframe")]
public sealed class WireframeRendererNestingInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    public async Task RepeatBasedStencils_NestGroups_On_The_InteractiveAuto_Host()
        => await WireframeRendererNestingGuard.RunAsync(
            BaseUrl,
            () => CreateContextAsync(),
            (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>Host-independent guard logic all three lanes run.</summary>
internal static class WireframeRendererNestingGuard
{
    private const string WireframeEditorUrl = "/wireframe-editor";

    /// <summary>The eight repeat-based stencils the FZE-1 evidence named, with the palette search
    /// term a designer would type. TmSegmentedControl additionally gets sample options so the
    /// repeat branch renders the Day/Week/Month labels the collapse piles onto segment 1.</summary>
    private static readonly (string Type, string SearchTerm)[] Stencils =
    [
        ("TmSegmentedControl", "Segmented Control"),
        ("TmMultiSelect", "Multi Select"),
        ("TmStepper", "Stepper"),
        ("TmScheduler", "Scheduler"),
        ("TmImageGallery", "Image Gallery"),
        ("TmActivityLog", "Activity Log"),
        ("TmCalendarHeatmap", "Calendar Heatmap"),
        ("TmRichEditorFull", "Rich Editor"),
    ];

    public static async Task RunAsync(
        string baseUrl,
        Func<Task<IBrowserContext>> createContext,
        Func<IPage, string, Task> gotoAppReady)
    {
        await using var context = await createContext();
        var page = await context.NewPageAsync();

        // Same boot sequence as the wireframe gallery specs: hide the sidebar so the first editor
        // resolves its DESKTOP layout (a tablet/mobile layout docks the palette differently and
        // renders no .tm-wd-toolbox__search-input), then pin the en culture the palette search
        // terms are written in.
        await page.AddInitScriptAsync(
            "document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); " +
            "s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } " +
            "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; " +
            "document.head.appendChild(s); });");
        await gotoAppReady(page, $"{baseUrl}{WireframeEditorUrl}");
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WireframeStencilHarness.PrepareAsync(page);

        foreach (var (type, searchTerm) in Stencils)
        {
            await WireframeStencilHarness.FilterPaletteAsync(page, searchTerm);

            var preview = WireframeStencilHarness.PaletteItem(page, type);
            await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            // (c) the toolbox preview renders through the same RenderSvg — it must not collapse either.
            // Built-in stencils carry resolvable icons, so the mini-SVG preview renders only when the
            // icon is unknown ("edit-3" for TmRichEditorFull) — pin that so the guard never goes
            // vacuous, and apply the empty-group assertion wherever a preview svg IS rendered.
            var previewSvgCount = await preview.Locator("svg").CountAsync();
            if (type == "TmRichEditorFull")
            {
                Assert.AreEqual(1, previewSvgCount,
                    "TmRichEditorFull names an unknown icon ('edit-3'), so its toolbox entry must render the mini-SVG preview");
            }

            if (previewSvgCount > 0)
            {
                var previewEmptyGroups = await preview.Locator("svg g[transform]:empty").CountAsync();
                Assert.AreEqual(0, previewEmptyGroups,
                    $"{type}: the toolbox preview must not contain an empty g[transform] " +
                    "(a split <g> markup pair auto-closes in the browser)");
            }

            var elementId = await WireframeStencilHarness.InsertAsync(page, type);
            await WireframeStencilHarness.SelectAsync(page, elementId);

            if (type == "TmSegmentedControl")
            {
                // The schema default options list is empty; give the repeat branch the labels the
                // collapse piles up (FZE-16 evidence: Day/Week/Month on segment 1). The properties
                // panel normalizes a StringList to the ", " separator, so type the normalized form.
                await WireframeStencilHarness.SetTextPropAsync(page, "options", "Day, Week, Month");
            }

            var elementSvg = WireframeStencilHarness.FirstEditor(page)
                .Locator($"g[data-el-id='{elementId}'] > svg");

            // (a) no auto-closed empty transform group inside the element sketch.
            var emptyGroups = await elementSvg.Locator("g[transform]:empty").CountAsync();
            Assert.AreEqual(0, emptyGroups,
                $"{type}: the inserted element must not contain an empty g[transform] — " +
                "the browser auto-closes each split markup <g> and orphans its children");

            // (b) repeated item labels must not share one position (Day/Week/Month, Meeting × 2).
            // getBoundingClientRect() is in viewport coordinates, so parent <g> transforms apply
            // (getBBox() is local and would ignore them). ANY two non-empty labels within 0.5 px
            // of each other are flagged, regardless of their text — a collapsed repeat piles
            // different labels on top of each other too.
            var stacked = await elementSvg.EvaluateAsync<string>(
                """
                svg => {
                    const labels = [...svg.querySelectorAll('text')]
                        .map(text => ({ text: text.textContent.trim(), rect: text.getBoundingClientRect() }))
                        .filter(label => label.text);
                    const stackedLabels = [];
                    for (let i = 0; i < labels.length; i++) {
                        for (let j = i + 1; j < labels.length; j++) {
                            const a = labels[i], b = labels[j];
                            if (Math.abs(a.rect.x - b.rect.x) < 0.5 && Math.abs(a.rect.y - b.rect.y) < 0.5) {
                                stackedLabels.push(`${a.text}≈${b.text}`);
                            }
                        }
                    }
                    return stackedLabels.join(', ');
                }
                """);
            Assert.AreEqual(string.Empty, stacked,
                $"{type}: repeated item labels must not stack at one position — " +
                $"the transforms do not nest (stacked: {stacked})");
        }
    }
}
