using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PdfViewer redesign phase 6 (desktop 1440): the full continuous mode — placeholders with
/// the correct aspect ratio, lazy per-page painting (IntersectionObserver + a serial queue),
/// canvas release outside the viewport, scroll-driven page tracking and per-page search
/// (PD1: annotations and OverlayContent stay single-page). Screenshots land in
/// <c>__screenshots__/pdf-viewer/continuous-desktop.png</c> for UX review against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.PdfViewer/pdf-viewer-continuous--desktop.png</c>.
/// </summary>
[TestClass]
public class PdfViewerContinuousE2ETests : WasmTestBase
{
    private const string PdfPage = "/pdf-viewer";
    private const int DesktopWidth = 1440;
    private const int TotalPages = 120;
    private const int SearchPage = 42;

    private static readonly string SectionSelector = "section:has(h2:has-text('Continuous Scroll'))";
    private static readonly string AllFeaturesSelector = "section:has(h2:has-text('All Features Combined'))";

    private async Task<IPage> OpenContinuousAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, 1000);
        await page.GotoAsync($"{BaseUrl}{PdfPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator(SectionSelector);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        // The toolbar (and the page input) only renders once the document has loaded.
        var input = section.Locator(".tm-pdf-viewer__page-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        return page;
    }

    /// <summary>Viewer root of the continuous fixture: the demo page has exactly one
    /// continuous-mode viewer, so its wrap identifies the instance (standard CSS only —
    /// <c>:has-text()</c> is a Playwright engine and not valid for querySelector).</summary>
    private const string JsViewerRootExpr =
        "document.querySelector('.tm-pdf-viewer__continuous-wrap')?.closest('.tm-pdf-viewer')";

    /// <summary>Expression evaluating to the toolbar page-input value of the continuous viewer.</summary>
    private const string JsInputValueExpr =
        JsViewerRootExpr + "?.querySelector('.tm-pdf-viewer__page-input')?.value ?? ''";

    private static string InputValueScript => $"() => ({JsInputValueExpr})";

    private static async Task ScrollContinuousToAsync(IPage page, int target)
        => await page.EvaluateAsync($@"
() => {{
    const wrap = {JsViewerRootExpr}?.querySelector('.tm-pdf-viewer__continuous-wrap');
    wrap?.querySelector('[data-page=""{target}""]')?.scrollIntoView({{ block: 'start' }});
}}");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_ScrollUpdatesPageIndicator()
    {
        var page = await OpenContinuousAsync();

        // One placeholder wrap per page exists up front (layout is stable before any paint).
        await page.WaitForFunctionAsync(
            $@"() => document.querySelectorAll("".tm-pdf-viewer__continuous-wrap > .tm-pdf-viewer__page"").length === {TotalPages}",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // Scroll to page 10: the visible-page tracker updates the toolbar input.
        await ScrollContinuousToAsync(page, 10);
        await page.WaitForFunctionAsync(
            $"() => ({JsInputValueExpr}) === '10'",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // Lazy rendering: only pages near the viewport are painted, never the whole document.
        var rendered = await page.EvaluateAsync<int>(@"
() => document.querySelectorAll("".tm-pdf-viewer__continuous-wrap canvas[data-rendered]"").length");
        Assert.IsTrue(rendered <= 20,
            $"lazy rendering must keep the painted set small (was {rendered})");

        // Scrolling back updates the indicator both ways.
        await ScrollContinuousToAsync(page, 2);
        await page.WaitForFunctionAsync(
            $"() => ({JsInputValueExpr}) === '2'",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        await SaveContinuousScreenshotAsync(page, "continuous-desktop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_SearchScrollsToMatch()
    {
        var page = await OpenContinuousAsync();

        var section = page.Locator(SectionSelector);
        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.FillAsync("needle-in-the-middle");
        await input.PressAsync("Enter");

        // The counter appears once the search ran; the match lives on a deep page.
        var count = section.Locator("[data-testid='pdf-search-count']").First;
        await count.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // The active match scrolled the wrap to its page (42 of 120) and the
        // visible-page tracker mirrored it into the toolbar input.
        await page.WaitForFunctionAsync(
            $"() => ({JsInputValueExpr}) === '{SearchPage}'",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // The match's page gets painted lazily and its highlight layer carries the
        // active rect (polled: the paint lands a moment after the scroll).
        await page.WaitForFunctionAsync(@"
() => {
    const wrap = document.querySelector("".tm-pdf-viewer__continuous-wrap"");
    const page42 = wrap?.querySelector('[data-page=""42""]');
    return !!page42?.querySelector('canvas[data-rendered]')
        && !!page42?.querySelector('.tm-pdf-search-highlight--active');
}",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // A multi-match query starts on its first page and stepping forward scrolls
        // the wrap match by match ("highlights" occurs once per page of the fixture).
        await input.FillAsync("highlights");
        await input.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            $"() => ({JsInputValueExpr}) === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        await section.Locator("[data-testid='pdf-search-next']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => ({JsInputValueExpr}) === '2'",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_LargeDocument_StaysResponsive()
    {        var page = await OpenContinuousAsync();

        // Jump through the document in a few steps; the main thread must stay free —
        // a rAF lands quickly between the jumps and the painted set stays bounded.
        var result = await page.EvaluateAsync<string>(@"
async () => {
    const wrap = document.querySelector("".tm-pdf-viewer__continuous-wrap"");
    if (!wrap) return JSON.stringify({ total: 0, painted: 0, maxRafDelay: 0 });
    const steps = [1, 30, 60, 90, 120];
    let maxRafDelay = 0;
    for (const target of steps) {
        wrap.querySelector(`[data-page=""${target}""]`)?.scrollIntoView({ block: 'start' });
        const t0 = performance.now();
        await new Promise(resolve => requestAnimationFrame(resolve));
        maxRafDelay = Math.max(maxRafDelay, performance.now() - t0);
        await new Promise(resolve => setTimeout(resolve, 120));
    }
    return JSON.stringify({
        total: wrap.querySelectorAll(':scope > .tm-pdf-viewer__page').length,
        painted: wrap.querySelectorAll('canvas[data-rendered]').length,
        maxRafDelay
    });
}");
        var probe = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(result!);
        Assert.IsNotNull(probe, "the continuous wrap must exist");
        Assert.AreEqual(TotalPages, (int)probe["total"], "all placeholders exist without painting everything");
        Assert.IsTrue(probe["painted"] <= 20,
            "the painted set must stay bounded while jumping through the document");
        Assert.IsTrue(probe["maxRafDelay"] < 1000,
            "the main thread must stay responsive while the lazy renderer works");

        // The last jump left the reader on page 120.
        await page.WaitForFunctionAsync(
            $"() => ({JsInputValueExpr}) === '{TotalPages}'",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// JS lookup of the "All Features Combined" viewer root (pure CSS only — the only
    /// viewer on the page that has both the annotations toggle and the view-mode radio
    /// group; <c>:has-text()</c> is not valid inside evaluate).
    /// </summary>
    private const string JsAllFeaturesRootExpr =
        "[...document.querySelectorAll('.tm-pdf-viewer')].find(v => "
        + "v.querySelector(\"[data-testid='pdf-annotations-toggle']\") && v.querySelector('.tm-pdf-viewer__view-mode'))";

    private const string JsCanvasPaintedProbe = @"
el => {
    const ctx = el.getContext('2d');
    if (!ctx || el.width === 0 || el.height === 0) return false;
    const data = ctx.getImageData(0, 0, el.width, el.height).data;
    for (let i = 3; i < data.length; i += 4) { if (data[i] !== 0) return true; }
    return false;
}";

    /// <summary>
    /// Review fix 1: switching Continuous → SinglePage (toolbar toggle or the PD1 thread
    /// select) used to run its JS against the stale continuous DOM. After the fix the
    /// render + selection setup are deferred to the post-render pass — the canvas is
    /// really painted (non-blank pixel probe) and the selection text layer is populated.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_ContinuousToSinglePage_CanvasPaintedAndSelectionEnabled()
    {
        var page = await OpenContinuousAsync();
        var section = page.Locator(AllFeaturesSelector);
        var viewer = section.Locator(".tm-pdf-viewer").First;
        await viewer.ScrollIntoViewIfNeededAsync();

        // Load the "All Features Combined" viewer (annotations + view mode toggle).
        await section.Locator(".tm-pdf-viewer__page-input").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // Switch to Continuous, let the lazy renderer paint, then back to SinglePage.
        await section.Locator(".tm-pdf-viewer__view-mode .tm-radio-segment").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            $@"() => {{ const v = {JsAllFeaturesRootExpr}; return !!v?.querySelector('.tm-pdf-viewer__continuous-wrap canvas[data-rendered]'); }}",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        await section.Locator(".tm-pdf-viewer__view-mode .tm-radio-segment").First.ClickAsync();

        var canvas = section.Locator(".tm-pdf-viewer__canvas-wrap canvas").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Non-blank pixel probe: the fresh canvas must hold painted content.
        Assert.IsTrue(await canvas.EvaluateAsync<bool>(JsCanvasPaintedProbe),
            "the single-page canvas must be painted after the switch (not blank)");

        // Selection enabled: the deferred setup rendered the text layer into the fresh
        // DOM (renderTextLayer + enableSelection run inside the same post-render pass).
        var textSpans = await section.Locator(".tm-pdf-viewer__canvas-wrap .tm-pdf-viewer__text-layer div").CountAsync();
        Assert.IsTrue(textSpans > 0,
            $"the selection text layer must be populated after the switch (found {textSpans} spans)");
    }

    /// <summary>
    /// Review fix 1, PD1 thread select: selecting a comment thread while in Continuous
    /// returns the viewer to SinglePage on the thread's page — with a painted canvas and
    /// a populated selection layer there (the stale-DOM regression made this blank).
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_ThreadSelect_ReturnsToSinglePagePainted()
    {
        var page = await OpenContinuousAsync();
        var section = page.Locator(AllFeaturesSelector);
        var viewer = section.Locator(".tm-pdf-viewer").First;
        await viewer.ScrollIntoViewIfNeededAsync();
        await section.Locator(".tm-pdf-viewer__page-input").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // Into Continuous and away from the thread's page.
        await section.Locator(".tm-pdf-viewer__view-mode .tm-radio-segment").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            $@"() => {{ const v = {JsAllFeaturesRootExpr}; return !!v?.querySelector('.tm-pdf-viewer__continuous-wrap canvas[data-rendered]'); }}",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // The thread is anchored on page 3; selecting it switches back to SinglePage.
        var thread = section.Locator("[data-testid='pdf-annotation-thread']").First;
        await thread.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await thread.ClickAsync();

        var canvas = section.Locator(".tm-pdf-viewer__canvas-wrap canvas").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        await page.WaitForFunctionAsync(
            $@"() => {{ const v = {JsAllFeaturesRootExpr}; return v?.querySelector('.tm-pdf-viewer__page-input')?.value === '3'; }}",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        Assert.IsTrue(await canvas.EvaluateAsync<bool>(JsCanvasPaintedProbe),
            "the thread select must leave a painted single-page canvas on page 3");

        var textSpans = await section.Locator(".tm-pdf-viewer__canvas-wrap .tm-pdf-viewer__text-layer div").CountAsync();
        Assert.IsTrue(textSpans > 0, "the selection layer must be re-armed for the thread's page");
    }

    /// <summary>
    /// UX review screenshot: a lower zoom (two steps out) plus a scroll to a page
    /// boundary frame two pages AND the gap between them, and the clip is the viewer
    /// element itself (toolbar included, page chrome visible end to end).
    /// </summary>
    private async Task SaveContinuousScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "pdf-viewer");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");

        await page.Locator(SectionSelector).ScrollIntoViewIfNeededAsync();
        // Page boundary: page 2 starts at the viewport top — page 2 and the sliver of
        // page 3 (plus the gap) are in view.
        await ScrollContinuousToAsync(page, 2);
        // Lower zoom (1.0 → 0.5): both visible page boxes shrink enough to show the
        // inter-page gap clearly. The in-place scale path repaints the visible pages.
        var zoomOut = page.Locator($"{SectionSelector} .tm-pdf-viewer__btn[aria-label='Zoom out']").First;
        await zoomOut.ClickAsync();
        await zoomOut.ClickAsync();
        await page.WaitForTimeoutAsync(1500); // let the in-place repaint settle

        await page.Locator($"{SectionSelector} .tm-pdf-viewer").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new InvalidOperationException("Could not locate TempoBlazor.slnx.");
    }
}
