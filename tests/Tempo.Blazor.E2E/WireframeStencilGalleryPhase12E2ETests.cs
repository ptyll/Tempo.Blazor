using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6 phase 12 — the FOUR stencils this plan series added or changed, looked at IN THE RUNNING
/// WIREFRAME EDITOR: <c>TmFacetFilter</c> (240×220), <c>TmTraceWaterfall</c> (420×260), <c>TmUsageMeter</c>
/// (280×200) and <c>TmSegmentedControl</c> (240×52 — phase 16 raised it from 36 so the label line above
/// the tray and the badge inside it stop painting outside the element's own selection box).
/// <para>
/// Their schema, role vocabulary, pack parity and conditional branches are already pinned by bUnit
/// (<c>FacetFilterStencilTests</c>, <c>TraceWaterfallStencilTests</c>, <c>UsageMeterStencilTests</c>,
/// <c>SegmentedControlStencilTests</c>), but every one of those only parses the drawn SVG as a string. What
/// that layer cannot answer is whether a designer can FIND the entry in the palette and whether the sketch
/// READS as the component it stands in for — so this spec drives the real editor: it filters the toolbox,
/// inserts each stencil through the canvas drop handler, flips the opt-in props through the properties
/// panel, and captures the palette entry, the default render and every conditional state into
/// <c>TestResults/wireframe-stencils-phase12</c>.
/// </para>
/// <para>
/// Every assertion is about reachability and state, never about pixels: the palette entry must exist and be
/// clickable, the inserted element must land on the canvas at its declared default size, and the conditional
/// drawing must change when its prop is set. The captures are the artifact the UX review reads.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Wireframe")]
public class WireframeStencilGalleryPhase12E2ETests : WasmTestBase
{
    private const string WireframeEditorUrl = "/wireframe-editor";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-stencils-phase12");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>Boots the demo page with the sidebar margin removed, so the editor resolves its DESKTOP layout.</summary>
    private async Task<IPage> PrepareAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync(
            "document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); " +
            "s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } " +
            "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; " +
            "document.head.appendChild(s); });");
        await GotoAppReadyAsync(page, $"{BaseUrl}{WireframeEditorUrl}");
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await WireframeStencilHarness.PrepareAsync(page);
        return page;
    }

    /// <summary>The four stencils, with the palette search term a designer would type.</summary>
    private static readonly (string Type, string SearchTerm, int Width, int Height)[] Stencils =
    [
        ("TmFacetFilter", "Facet Filter", 240, 220),
        ("TmTraceWaterfall", "Trace Waterfall", 420, 260),
        ("TmUsageMeter", "Usage Meter", 280, 200),
        ("TmSegmentedControl", "Segmented Control", 240, 52),
    ];

    /// <summary>
    /// Frames ONE element and captures the EDITOR through a LOCATOR screenshot. The element box is the wrong
    /// clip for a sketch — the editor paints the group through its own viewBox transform, so the group's
    /// measured box and its painted extent disagree (a 280×200 usage meter measured 570×213 while painting a
    /// 530×380 card) and a page-clipped capture cut the category table off. A locator screenshot scrolls the
    /// editor into view and captures BEYOND the viewport, so the whole sketch lands in the artifact.
    /// </summary>
    private async Task ShootElementAsync(IPage page, string elementId, string name, int extraBottom = 0)
    {
        await WireframeStencilHarness.FrameAsync(page, elementId, padding: 24, reserveBelow: extraBottom);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(300);

        var path = Path.Combine(ShotDir, $"{name}.png");
        await WireframeStencilHarness.FirstEditor(page).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
        Assert.IsTrue(File.Exists(path), $"{name} lands on disk");
    }

    /// <summary>
    /// The element's MODEL box, read through the editor's own <c>getElementBounds</c> — never
    /// <c>getBBox()</c>, which measures the drawn sketch (a label above the tray, a badge past the edge) and
    /// would report a box the designer never sized.
    /// </summary>
    private static async Task<(double Width, double Height)> ElementSizeAsync(IPage page, string elementId)
    {
        var canvasId = await WireframeStencilHarness.FirstEditor(page).Locator(".tm-wd-canvas__svg")
            .EvaluateAsync<string>("el => el.id");
        var size = await page.EvaluateAsync<double[]>(
            """
            ([canvasId, id]) => {
                const inst = window.tmWireframeDesigner.instances.get(canvasId);
                if (!inst) return [0, 0];
                const bounds = window.tmWireframeDesigner.getElementBounds(inst.svg, id);
                return bounds ? [bounds.w, bounds.h] : [0, 0];
            }
            """,
            new object[] { canvasId, elementId });
        return (size[0], size[1]);
    }

    [TestMethod]
    public async Task TheFourStencils_AreFindableInThePalette_AndDrawAtTheirDeclaredSize()
    {
        var page = await PrepareAsync();

        foreach (var (type, searchTerm, _, _) in Stencils)
        {
            await WireframeStencilHarness.FilterPaletteAsync(page, searchTerm);
            Assert.AreEqual(1, await WireframeStencilHarness.PaletteItem(page, type).CountAsync(),
                $"the palette must offer {type} when the designer searches '{searchTerm}'");
            await WireframeStencilHarness.ShootAsync(
                page, WireframeStencilHarness.Toolbox(page), ShotDir, $"palette-{type}", pad: 4);
        }

        await WireframeStencilHarness.FilterPaletteAsync(page, "");
        foreach (var (type, _, width, height) in Stencils)
        {
            var elementId = await WireframeStencilHarness.InsertAsync(page, type);
            var svg = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(svg), $"{type} must draw something on the canvas");
            Assert.IsFalse(svg.Contains("Missing component", StringComparison.OrdinalIgnoreCase),
                $"{type} must resolve through the Tempo pack, not render a fallback");

            var size = await ElementSizeAsync(page, elementId);
            Assert.AreEqual(width, size.Width, 2, $"{type} draws at its declared {width} px width");
            Assert.AreEqual(height, size.Height, 2, $"{type} draws at its declared {height} px height");

            await ShootElementAsync(page, elementId, $"default-{type}");
        }
    }

    [TestMethod]
    public async Task FacetFilter_ConditionalStates_SketchTheTriggerTheSearchTheCountsTheChipsAndACollapsedGroup()
    {
        var page = await PrepareAsync();
        await WireframeStencilHarness.FilterPaletteAsync(page, "");
        var elementId = await WireframeStencilHarness.InsertAsync(page, "TmFacetFilter", x: 260, y: 180);
        await WireframeStencilHarness.SelectAsync(page, elementId);

        var initial = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.AreEqual(2, WireframeStencilHarness.Occurrences(initial, "Filters"),
            "the popover presentation draws the panel title AND the trigger chip that opens it");
        Assert.IsTrue(initial.Contains("Search Category"), "the group search is on by default");
        Assert.IsTrue(initial.Contains(">480<"), "the option counts are on by default");
        StringAssert.Contains(initial, "Show 480 results", "the confirmation footer names the result count");
        var chipRects = WireframeStencilHarness.Occurrences(initial, "rx=\"9\"");
        Assert.IsTrue(chipRects >= 2,
            $"the two active-filter chips are part of the default sketch (found {chipRects} rx-9 pills)");
        // Phase 16 of plan 7ba95ed6: the row reads as TWO filters, not the same one twice — the old sample
        // painted "Category: Boo…" (74 px, ellipsised) next to "Books". Both labels are whole now.
        Assert.AreEqual(2, WireframeStencilHarness.Occurrences(initial, ">Books<"),
            "the Books option row AND the Books active-filter chip, each drawn once");
        Assert.IsTrue(initial.Contains(">In stock<"), "the second chip is a DIFFERENT filter, not a duplicate");
        Assert.IsFalse(initial.Contains("Category:"),
            "a chip names the value; the facet name is the group header above the list");
        Assert.IsFalse(initial.Contains("Boo\u2026"), "nothing is ellipsised in the chip row at the default 240 px");
        Assert.IsFalse(initial.Contains("In sto\u2026"), "the longest chip label fits its pill too");
        Assert.IsFalse(initial.Contains("Status"), "the collapsed second group is opt-in");
        await ShootElementAsync(page, elementId, "state-TmFacetFilter-default");

        // Inline presentation: the trigger chip goes away, the groups stay.
        await WireframeStencilHarness.SelectEnumPropAsync(page, "presentation", "inline");
        var inline = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.AreEqual(1, WireframeStencilHarness.Occurrences(inline, "Filters"),
            "an inline filter has no chip, so only the panel title names it");
        Assert.IsTrue(inline.Contains("Search Category"), "the inline copy still lists the groups");
        await ShootElementAsync(page, elementId, "state-TmFacetFilter-inline");

        // Each opt-in prop removes exactly the piece it owns. The defaults are TRUE, so the value has to be
        // set explicitly — toggling would read the unchanged sketch back.
        await WireframeStencilHarness.SetBoolPropAsync(page, "showSearch", false);
        var withoutSearch = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsFalse(withoutSearch.Contains("Search Category"), "showSearch=false drops the search row");

        await WireframeStencilHarness.SetBoolPropAsync(page, "showCounts", false);
        var withoutCounts = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsFalse(withoutCounts.Contains(">480<"), "showCounts=false drops the option counts");
        StringAssert.Contains(withoutCounts, "Show 480 results",
            "the footer count is the FILTER's result count, not an option count — it stays");

        await WireframeStencilHarness.SetBoolPropAsync(page, "showChips", false);
        var withoutChips = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        var chipRectsAfter = WireframeStencilHarness.Occurrences(withoutChips, "rx=\"9\"");
        Assert.IsTrue(chipRectsAfter <= chipRects - 2,
            $"showChips=false drops the active-filter chip pills ({chipRects} -> {chipRectsAfter})");

        await WireframeStencilHarness.SetBoolPropAsync(page, "collapsed", true);
        var collapsed = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(collapsed.Contains("Status"), "collapsed=true draws the folded second group");
        await ShootElementAsync(page, elementId, "state-TmFacetFilter-collapsed");
    }

    [TestMethod]
    public async Task TraceWaterfall_ConditionalStates_SketchLiveZoomMetricsAndTheFoldedSubtree()
    {
        var page = await PrepareAsync();
        await WireframeStencilHarness.FilterPaletteAsync(page, "");
        var elementId = await WireframeStencilHarness.InsertAsync(page, "TmTraceWaterfall", x: 220, y: 160);
        await WireframeStencilHarness.SelectAsync(page, elementId);

        var initial = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(initial.Contains("Trace"), "the sketch is titled by the schema's own default");
        Assert.IsFalse(initial.Contains("Live"), "the live badge is opt-in");
        Assert.IsTrue(initial.Contains("420 ms"), "the expanded tree lists its child spans");
        Assert.IsTrue(initial.Contains("1.2 s"), "the metric column carries the root duration");
        await ShootElementAsync(page, elementId, "state-TmTraceWaterfall-default");

        await WireframeStencilHarness.SetBoolPropAsync(page, "live", true);
        var live = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(live.Contains("Live"), "live=true adds the running badge");
        await ShootElementAsync(page, elementId, "state-TmTraceWaterfall-live");

        // The metric column is switched off while the tree is still EXPANDED, so the assertion cannot be
        // satisfied by the folded summary (which carries its own "· 1.2 s" tail).
        await WireframeStencilHarness.SetBoolPropAsync(page, "showMetrics", false);
        var withoutMetrics = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsFalse(withoutMetrics.Contains("420 ms"), "showMetrics=false drops the per-span metric text");
        await ShootElementAsync(page, elementId, "state-TmTraceWaterfall-metrics-off");

        await WireframeStencilHarness.SetBoolPropAsync(page, "expanded", false);
        var folded = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsFalse(folded.Contains("420 ms"), "expanded=false hides the child rows");
        StringAssert.Contains(folded, "4 spans", "the folded subtree is summarised in one line");
        await ShootElementAsync(page, elementId, "state-TmTraceWaterfall-collapsed");

        await WireframeStencilHarness.SetBoolPropAsync(page, "showZoom", false);
        var withoutZoom = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.AreNotEqual(folded, withoutZoom, "showZoom=false drops the zoom buttons");
        Assert.IsFalse(withoutZoom.Contains("\u2212"), "the zoom-out button is gone with the zoom group");
    }

    [TestMethod]
    public async Task UsageMeter_ConditionalStates_SketchTheWarningTheCriticalTheResetTheDailyAndTheCompactCopy()
    {
        var page = await PrepareAsync();
        await WireframeStencilHarness.FilterPaletteAsync(page, "");
        var elementId = await WireframeStencilHarness.InsertAsync(page, "TmUsageMeter", x: 280, y: 200);
        await WireframeStencilHarness.SelectAsync(page, elementId);

        var initial = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(initial.Contains("Monthly credits"), "the title comes from the schema default");
        Assert.IsFalse(initial.Contains("Near limit"), "7 500 of 10 000 is below the warning threshold");
        Assert.IsFalse(initial.Contains("Resets on"), "the reset date is opt-in");
        Assert.IsTrue(initial.Contains("Category"), "the category table is on by default");
        await ShootElementAsync(page, elementId, "state-TmUsageMeter-default");

        // The DERIVED states: the same sketch turns warning, then critical, from `used` alone.
        await WireframeStencilHarness.SetNumberPropAsync(page, "used", "9500");
        var warning = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(warning.Contains("Near limit"), "used >= 80 % of the limit derives the warning sketch");
        Assert.IsFalse(warning.Contains("Limit reached"), "still under the limit");
        await ShootElementAsync(page, elementId, "state-TmUsageMeter-warning");

        await WireframeStencilHarness.SetNumberPropAsync(page, "used", "12000");
        var critical = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(critical.Contains("Limit reached"), "used >= limit derives the critical sketch");
        await ShootElementAsync(page, elementId, "state-TmUsageMeter-critical");

        await WireframeStencilHarness.TogglePropAsync(page, "showReset");
        await WireframeStencilHarness.TogglePropAsync(page, "showDaily");
        var withExtras = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(withExtras.Contains("Resets on"), "showReset draws the reset line");
        var resetRects = WireframeStencilHarness.Occurrences(withExtras, "<rect");
        Assert.IsTrue(resetRects > WireframeStencilHarness.Occurrences(critical, "<rect"),
            "showDaily adds the daily sparkline bars");
        await ShootElementAsync(page, elementId, "state-TmUsageMeter-reset-daily");

        // Compact drops the reset line, the sparkline and the category table (they are `!compact`).
        await WireframeStencilHarness.TogglePropAsync(page, "compact");
        var compact = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsFalse(compact.Contains("Resets on"), "the compact copy has no room for the reset line");
        Assert.IsFalse(compact.Contains("Category"), "the compact copy drops the category table");
        await ShootElementAsync(page, elementId, "state-TmUsageMeter-compact");

        // …and the bar-only presentation is what the categories-off branch draws. The compact copy already
        // hides the table, so compact is switched back OFF first — otherwise the assertion below would pass
        // for the wrong reason.
        await WireframeStencilHarness.SetBoolPropAsync(page, "compact", false);
        Assert.IsTrue((await WireframeStencilHarness.ElementSvgAsync(page, elementId)).Contains("Category"),
            "the full copy is back before the categories are switched off");
        await WireframeStencilHarness.SetBoolPropAsync(page, "showCategories", false);
        var barOnly = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsFalse(barOnly.Contains("Category"), "showCategories=false leaves the plain meter bar");
        await ShootElementAsync(page, elementId, "state-TmUsageMeter-bar-only");
    }

    [TestMethod]
    public async Task SegmentedControl_ConditionalStates_SketchTheLabelTheCountTheBadgeAndTheDisabledTray()
    {
        var page = await PrepareAsync();
        await WireframeStencilHarness.FilterPaletteAsync(page, "");
        var elementId = await WireframeStencilHarness.InsertAsync(page, "TmSegmentedControl", x: 320, y: 240);
        await WireframeStencilHarness.SelectAsync(page, elementId);

        // A dropped control carries no options, so the designer types them: the tray sketches one segment
        // per option, which is the whole point of the stencil.
        await WireframeStencilHarness.SetTextPropAsync(page, "options", "Day, Week, Month");
        var withOptions = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(withOptions.Contains("Day"), "the first option is drawn");
        Assert.IsTrue(withOptions.Contains("Week"), "the second option is drawn");
        Assert.IsTrue(withOptions.Contains("Month"), "the third option is drawn");
        Assert.IsTrue(withOptions.Contains("Period"), "the label default names the tray");
        await ShootElementAsync(page, elementId, "state-TmSegmentedControl-options");

        await WireframeStencilHarness.TogglePropAsync(page, "showCount");
        var withCount = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.AreNotEqual(withOptions, withCount, "showCount adds the count pill on the first segment");

        await WireframeStencilHarness.SetTextPropAsync(page, "badge", "New");
        var withBadge = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        Assert.IsTrue(withBadge.Contains("New"), "the badge text is sketched beside the label");
        await ShootElementAsync(page, elementId, "state-TmSegmentedControl-count-badge");

        await WireframeStencilHarness.TogglePropAsync(page, "disabled");
        var disabled = await WireframeStencilHarness.ElementSvgAsync(page, elementId);
        StringAssert.Contains(disabled, "opacity", "disabled dims the whole tray instead of hiding it");
        Assert.IsTrue(disabled.Contains("Day"), "a disabled tray still shows its options");
        await ShootElementAsync(page, elementId, "state-TmSegmentedControl-disabled");
    }
}
