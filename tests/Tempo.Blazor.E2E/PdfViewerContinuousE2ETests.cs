using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

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
    wrap?.querySelector('.tm-pdf-viewer__page[data-page=""{target}""]')?.scrollIntoView({{ block: 'start' }});
}}");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_ScrollUpdatesPageIndicator()
    {
        var page = await OpenContinuousAsync();

        // One placeholder wrap per page exists up front (layout is stable before any paint).
        await page.WaitForFunctionAsync(
            $@"() => document.querySelectorAll("".tm-pdf-viewer__continuous-wrap .tm-pdf-viewer__page"").length === {TotalPages}",
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
    const page42 = wrap?.querySelector('.tm-pdf-viewer__page[data-page=""42""]');
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
        total: wrap.querySelectorAll('.tm-pdf-viewer__page').length,
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
        var textSpans = await section.Locator(".tm-pdf-viewer__canvas-wrap .tm-pdf-viewer__text-layer span").CountAsync();
        Assert.IsTrue(textSpans > 0,
            $"the selection text layer must be populated after the switch (found {textSpans} spans)");
    }

    /// <summary>
    /// Opens the "All Features Combined" viewer (annotations + view-mode toggle) and
    /// switches it to Continuous — the shared setup of the FZE-13 annotation lanes.
    /// Returns (page, section) with the continuous wrap already built.
    /// </summary>
    private async Task<(IPage Page, ILocator Section)> OpenAllFeaturesContinuousAsync()
    {
        var page = await OpenContinuousAsync();
        var section = page.Locator(AllFeaturesSelector);
        await section.Locator(".tm-pdf-viewer__page-input").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        await section.Locator(".tm-pdf-viewer__view-mode .tm-radio-segment").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            $@"() => {{ const v = {JsAllFeaturesRootExpr}; return !!v?.querySelector('.tm-pdf-viewer__continuous-wrap .tm-pdf-viewer__page'); }}",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        return (page, section);
    }

    /// <summary>The continuous wrap of the All-Features viewer.</summary>
    private const string JsAllFeaturesWrapExpr = JsAllFeaturesRootExpr + "?.querySelector('.tm-pdf-viewer__continuous-wrap')";

    /// <summary>
    /// FZE-13 alignment probe: the Blazor overlay slot of a page must sit exactly over
    /// the page's INNER box — the actual canvas box (B1: the outer wrap is a full-width
    /// centered flex row, so only its vertical extent defines the band). Zoom and
    /// rotation included. Returns "ok" or a detail string.
    /// </summary>
    private static string AlignmentProbe(int page) => $@"
() => {{
    const wrap = {JsAllFeaturesWrapExpr};
    if (!wrap) return 'missing-wrap';
    const pageInner = wrap.querySelector('.tm-pdf-viewer__page[data-page=""{page}""] > .tm-pdf-viewer__page-inner');
    const slot = wrap.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""{page}""]');
    if (!pageInner) return 'missing-page';
    if (!slot) return 'missing-slot';
    const a = pageInner.getBoundingClientRect();
    const b = slot.getBoundingClientRect();
    const drift = Math.max(Math.abs(a.left - b.left), Math.abs(a.top - b.top),
        Math.abs(a.width - b.width), Math.abs(a.height - b.height));
    return drift <= 1.5 ? 'ok' : 'drift=' + drift.toFixed(1);
}}";

    /// <summary>
    /// B3 rotation probe: the annotation geometry is normalized to the UNROTATED page,
    /// so after a quarter turn the highlight must sit at the transformed anchor position
    /// — over the text it anchors. The seeded full-band anchor is
    /// <c>DocumentCommentRect.Create(0.20, 0.35, 0.25, 0.015)</c>; rotation 90 maps the
    /// unrotated (u, v) to (1 − v, u) inside the rotated inner box.
    /// </summary>
    private const string RotationHighlightProbe = @"
() => {
    const wrap = " + JsAllFeaturesWrapExpr + @";
    const inner = wrap?.querySelector('.tm-pdf-viewer__page[data-page=""1""] > .tm-pdf-viewer__page-inner');
    const highlight = wrap?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""1""] .tm-pdf-annotation-layer__highlight');
    if (!inner || !highlight) return 'missing';
    const a = inner.getBoundingClientRect();
    const h = highlight.getBoundingClientRect();
    const ax0 = (h.left - a.left) / a.width, ax1 = (h.right - a.left) / a.width;
    const ay0 = (h.top - a.top) / a.height, ay1 = (h.bottom - a.top) / a.height;
    const ex0 = 1 - 0.365, ex1 = 1 - 0.35, ey0 = 0.20, ey1 = 0.45;
    const drift = Math.max(Math.abs(ex0 - ax0), Math.abs(ex1 - ax1), Math.abs(ey0 - ay0), Math.abs(ey1 - ay1));
    const inside = ax0 >= -0.01 && ax1 <= 1.01 && ay0 >= -0.01 && ay1 <= 1.01;
    return inside && drift <= 0.02 ? 'ok' : 'drift=' + drift.toFixed(3);
}";

    /// <summary>
    /// B4 containment probe: EVERY mounted slot's marker/highlight box must lie inside
    /// its page-inner box (the actual canvas box) — nothing paints over the workspace
    /// or a neighbour page. Returns "ok" or the first offending page.
    /// </summary>
    private const string MarkerContainmentProbe = @"
() => {
    const wrap = " + JsAllFeaturesWrapExpr + @";
    if (!wrap) return 'missing-wrap';
    for (const slot of wrap.querySelectorAll('.tm-pdf-viewer__page-overlay-slot')) {
        const inner = wrap.querySelector('.tm-pdf-viewer__page[data-page=""' + slot.dataset.page + '""] > .tm-pdf-viewer__page-inner');
        if (!inner) return 'missing-inner:' + slot.dataset.page;
        const a = inner.getBoundingClientRect();
        const boxes = [...slot.querySelectorAll('[data-testid=""pdf-annotation-marker""], .tm-pdf-annotation-layer__highlight')];
        for (const box of boxes) {
            const b = box.getBoundingClientRect();
            if (b.width === 0 && b.height === 0) continue;
            if (b.left < a.left - 1 || b.right > a.right + 1 || b.top < a.top - 1 || b.bottom > a.bottom + 1) {
                return 'outside-page-' + slot.dataset.page;
            }
        }
    }
    return 'ok';
}";

    /// <summary>
    /// FZE-13: in continuous mode the annotation markers render on every mounted page —
    /// the demo seeds one thread per page of the 120-page document, so scrolling anywhere
    /// shows markers inside the mounted band's slots (and never outside them).
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_MarkersOnEveryVisiblePage()
    {
        var (page, section) = await OpenAllFeaturesContinuousAsync();
        await section.Locator(".tm-pdf-viewer").First.ScrollIntoViewIfNeededAsync();

        // The mounted band's slots carry per-page annotation layers; a thread exists on
        // every page of the fixture, so every mounted slot holds at least one marker.
        await page.WaitForFunctionAsync(@"
() => {
    const wrap = " + JsAllFeaturesWrapExpr + @";
    if (!wrap) return false;
    const slots = [...wrap.querySelectorAll('.tm-pdf-viewer__page-overlay-slot')];
    return slots.length > 0 && slots.every(s => s.querySelector('[data-testid=""pdf-annotation-marker""]'));
}", null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // The marker sits over its page's box (the seed rect is at 20% / 35%).
        var inside = await page.EvaluateAsync<string>(@"
() => {
    const wrap = " + JsAllFeaturesWrapExpr + @";
    const pageInner = wrap.querySelector('.tm-pdf-viewer__page[data-page=""1""] > .tm-pdf-viewer__page-inner');
    const slot = wrap.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""1""]');
    const pageBox = pageInner.getBoundingClientRect();
    const marker = slot.querySelector('[data-testid=""pdf-annotation-marker""]').getBoundingClientRect();
    return marker.left >= pageBox.left - 1 && marker.right <= pageBox.right + 1
        && marker.top >= pageBox.top - 1 && marker.bottom <= pageBox.bottom + 1 ? 'ok' : 'outside';
}");
        Assert.AreEqual("ok", inside, "the marker must paint inside its page's inner (canvas) box");

        // B4: every mounted slot's markers/highlights lie inside their page-inner box.
        var containment = await page.EvaluateAsync<string>(MarkerContainmentProbe);
        Assert.AreEqual("ok", containment, "every marker/highlight box must lie inside its page-inner box");

        // Unmounted pages (far from the viewport) render no slots at all.
        var slotCount = await page.EvaluateAsync<int>(
            $@"() => {JsAllFeaturesWrapExpr}?.querySelectorAll('.tm-pdf-viewer__page-overlay-slot').length ?? 0");
        Assert.IsTrue(slotCount < 20,
            $"the mount band keeps the slot set bounded (was {slotCount})");

        await SaveAllFeaturesScreenshotAsync(page, "continuous-annotations-desktop");
    }

    /// <summary>
    /// FZE-13: selecting a thread in Continuous scrolls to its page and centers the
    /// anchor — the view mode STAYS Continuous and the lazy renderer paints the page.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_SelectThread_ScrollsWithoutModeSwitch()
    {
        var (page, section) = await OpenAllFeaturesContinuousAsync();

        // Select the seeded page-3 thread from the docked panel (the demo seeds one
        // thread per page; the id pins the page this lane asserts).
        var thread = section.Locator(".tm-pdf-annotation-panel [data-thread-id='full-band-thread-3']").First;
        await thread.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await thread.ScrollIntoViewIfNeededAsync();
        await thread.ClickAsync();

        // The page indicator lands on the thread's page…
        await page.WaitForFunctionAsync(
            $@"() => {{ const v = {JsAllFeaturesRootExpr}; return v?.querySelector('.tm-pdf-viewer__page-input')?.value === '3'; }}",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // …the mode stays Continuous (no canvas wrap back, the radio stays checked)…
        var stillContinuous = await page.EvaluateAsync<bool>($@"
() => {{
    const v = {JsAllFeaturesRootExpr};
    return !v?.querySelector('.tm-pdf-viewer__canvas-wrap')
        && !!v?.querySelector('.tm-pdf-viewer__continuous-wrap')
        && !!v?.querySelector('.tm-pdf-viewer__view-mode input[type=""radio""]:checked')?.closest('.tm-radio-segment')
            ?.textContent?.includes('Continuous');
}}");
        Assert.IsTrue(stillContinuous, "the thread select must not switch the view mode");

        // …and the page is painted by the lazy renderer, with the highlighted marker
        // inside the page-3 slot.
        await page.WaitForFunctionAsync($@"
() => {{
    const wrap = {JsAllFeaturesWrapExpr};
    const pageWrap = wrap?.querySelector('.tm-pdf-viewer__page[data-page=""3""]');
    const slot = wrap?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""3""]');
    return !!pageWrap?.querySelector('canvas[data-rendered]') && !!slot?.querySelector('[data-testid=""pdf-annotation-marker""]');
}}", null, new PageWaitForFunctionOptions { Timeout = 30000 });

        var alignment = await page.EvaluateAsync<string>(AlignmentProbe(3));
        Assert.AreEqual("ok", alignment, "the page-3 slot must sit over the page after the anchor scroll");

        // M5: the selected state is reviewable — the page-3 marker carries the selected
        // class and sits inside the viewport (the anchor scroll centers it).
        await page.WaitForFunctionAsync($@"
() => {{
    const wrap = {JsAllFeaturesWrapExpr};
    const marker = wrap?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""3""] [data-testid=""pdf-annotation-marker""]');
    if (!marker?.classList.contains('tm-pdf-annotation-layer__marker--selected')) return false;
    const box = marker.getBoundingClientRect();
    return box.top >= 0 && box.left >= 0 && box.bottom <= window.innerHeight && box.right <= window.innerWidth;
}}", null, new PageWaitForFunctionOptions { Timeout = 30000 });

        await SaveAllFeaturesScreenshotAsync(page, "continuous-thread-selected");
    }

    /// <summary>
    /// FZE-13: text selection inside the continuous column reports the anchor page —
    /// a selection made on page 7 drafts and creates its thread on page 7.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_SelectTextOnPage7_CreatesThreadOnPage7()
    {
        var (page, section) = await OpenAllFeaturesContinuousAsync();

        // Page 7 must be painted before its text layer exists (the paint renders it).
        // The scroll targets THIS viewer's wrap — the page has several continuous
        // viewers and the first wrap belongs to the Continuous Scroll section.
        await page.EvaluateAsync($@"
() => {JsAllFeaturesWrapExpr}?.querySelector('.tm-pdf-viewer__page[data-page=""7""]')?.scrollIntoView({{ block: 'start' }});");
        await page.WaitForFunctionAsync($@"
() => {{
    const wrap = {JsAllFeaturesWrapExpr};
    const pageWrap = wrap?.querySelector('.tm-pdf-viewer__page[data-page=""7""]');
    return !!pageWrap?.querySelector('canvas[data-rendered]')
        && (pageWrap.querySelector('.tm-pdf-viewer__text-layer span')?.textContent?.length ?? 0) > 0;
}}", null, new PageWaitForFunctionOptions { Timeout = 60000 });

        // Drag a selection across ONE visible text span of the page-7 layer: the page
        // box is taller than the viewport and much of it is blank, so a fixed midpoint
        // drag could fall between lines. Picking the span guarantees the native drag
        // selects text (and the viewport-absolute coordinates stay on-screen).
        await section.Locator(".tm-pdf-viewer").First.ScrollIntoViewIfNeededAsync();
        var spanBox = await page.EvaluateAsync<string>(@"
() => {
    const wrap = " + JsAllFeaturesWrapExpr + @";
    const spans = [...wrap.querySelectorAll('.tm-pdf-viewer__page[data-page=""7""] .tm-pdf-viewer__text-layer span')]
        .map(s => { const b = s.getBoundingClientRect(); return { x: b.x, y: b.y, w: b.width, h: b.height, text: s.textContent ?? '' }; })
        .filter(b => b.w > 30 && b.h > 5 && b.y > 120 && b.y + b.h < 950);
    return spans.length ? JSON.stringify(spans[0]) : 'none';
}");
        Assert.AreNotEqual("none", spanBox, "the page-7 layer must have a visible text span");
        var span = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(spanBox!);
        var sx = span.GetProperty("x").GetSingle();
        var sy = span.GetProperty("y").GetSingle();
        var sw = span.GetProperty("w").GetSingle();
        var sh = span.GetProperty("h").GetSingle();
        var dragY = sy + sh / 2;
        await page.Mouse.MoveAsync(sx + 4, dragY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(sx + sw * 0.8f, dragY, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        // The draft paints in the page-7 slot (selection draft screenshot first).
        await page.WaitForFunctionAsync($@"
() => !!{JsAllFeaturesWrapExpr}?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""7""] [data-testid=""pdf-annotation-draft""]')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        var draftOutside7 = await page.EvaluateAsync<int>(
            $@"() => {JsAllFeaturesWrapExpr}?.querySelectorAll('.tm-pdf-viewer__page-overlay-slot:not([data-page=""7""]) [data-testid=""pdf-annotation-draft""]').length ?? 0");
        Assert.AreEqual(0, draftOutside7, "the draft never leaks into another page's slot");
        await SaveAllFeaturesScreenshotAsync(page, "continuous-selection-draft");

        // The docked panel hosts the prompt + composer: create the thread.
        var input = section.Locator("[data-testid='pdf-annotation-new-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await input.FillAsync("Page 7 thread from the continuous column");
        await section.Locator("[data-testid='pdf-annotation-new-submit']").First.ClickAsync();

        await page.WaitForFunctionAsync($@"
() => {{
    const slot = {JsAllFeaturesWrapExpr}?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""7""]');
    return !!slot?.querySelector('[data-testid=""pdf-annotation-marker""]');
}}", null, new PageWaitForFunctionOptions { Timeout = 30000 });
    }

    /// <summary>
    /// FZE-13: an in-place zoom (scale-only update) re-reports the geometry — the
    /// overlay slots re-scale with the pages and the markers stay aligned.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_ZoomKeepsMarkersAligned()
    {
        var (page, section) = await OpenAllFeaturesContinuousAsync();
        await section.Locator(".tm-pdf-viewer").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync($@"
() => !!{JsAllFeaturesWrapExpr}?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""1""] [data-testid=""pdf-annotation-marker""]')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        await ClickAllFeaturesCommandAsync(page, "Zoom out");
        await ClickAllFeaturesCommandAsync(page, "Zoom out");

        // B4: the zoom hides the slots until the fresh geometry report arrives — wait
        // for the re-scaled marker, then assert every box is inside its page-inner.
        await page.WaitForFunctionAsync($@"
() => {{
    const slot = {JsAllFeaturesWrapExpr}?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""1""]');
    if (!slot) return false;
    const container = {JsAllFeaturesWrapExpr};
    return !container.querySelector('.tm-pdf-viewer__continuous-overlays--stale')
        && !!slot.querySelector('[data-testid=""pdf-annotation-marker""]');
}}", null, new PageWaitForFunctionOptions { Timeout = 30000 });

        var alignment = await page.EvaluateAsync<string>(AlignmentProbe(1));
        Assert.AreEqual("ok", alignment, "the slots must re-scale with the in-place zoom");

        // B4: before the screenshot, every marker/highlight box must lie inside its
        // page-inner box (no stale geometry painting over the moved pages).
        var containment = await page.EvaluateAsync<string>(MarkerContainmentProbe);
        Assert.AreEqual("ok", containment, "after the zoom every marker/highlight stays inside its page-inner box");
        await SaveAllFeaturesScreenshotAsync(page, "continuous-annotations-zoomed");
    }

    /// <summary>
    /// FZE-13: a rotation rebuilds the placeholders — the fresh geometry report
    /// re-positions the slots for the flipped page boxes.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_RotateKeepsMarkersAligned()
    {
        var (page, section) = await OpenAllFeaturesContinuousAsync();
        await section.Locator(".tm-pdf-viewer").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync($@"
() => !!{JsAllFeaturesWrapExpr}?.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""1""] [data-testid=""pdf-annotation-marker""]')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });

        // ToolbarLabels=Auto may collapse the compact rotate pair into the "More" menu
        // (the toolbar's priority system) — click the visible button or the menu entry.
        await ClickAllFeaturesCommandAsync(page, "Rotate clockwise");
        await page.WaitForFunctionAsync($@"
() => {{
    const v = {JsAllFeaturesRootExpr};
    return !!v?.querySelector('.tm-pdf-viewer__continuous-wrap .tm-pdf-viewer__page[data-page=""1""] canvas[data-rendered]');
}}", null, new PageWaitForFunctionOptions { Timeout = 60000 });

        var alignment = await page.EvaluateAsync<string>(AlignmentProbe(1));
        Assert.AreEqual("ok", alignment, "the slots must follow the rotated page boxes");

        // B3: the anchor geometry is normalized to the unrotated page — after the
        // quarter turn the highlight must still sit over the text it anchors.
        await page.WaitForFunctionAsync($@"
() => ({RotationHighlightProbe})() === 'ok'",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
        await SaveAllFeaturesScreenshotAsync(page, "continuous-annotations-rotated");
    }

    /// <summary>
    /// FZE-13 edge: the 120-page document with a thread on EVERY page — jumping through
    /// the column must keep the main thread free (the Blazor diff only sees the mounted
    /// band) and the painted set bounded.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Continuous_120Pages_ScrollStaysResponsive_Edge()
    {
        var (page, section) = await OpenAllFeaturesContinuousAsync();

        var result = await page.EvaluateAsync<string>(@"
async () => {
    const wrap = " + JsAllFeaturesWrapExpr + @";
    if (!wrap) return JSON.stringify({ total: 0, painted: 0, slots: 0, maxRafDelay: 0 });
    const steps = [1, 30, 60, 90, 120];
    let maxRafDelay = 0;
    for (const target of steps) {
        wrap.querySelector(`.tm-pdf-viewer__page[data-page=""${target}""]`)?.scrollIntoView({ block: 'start' });
        const t0 = performance.now();
        await new Promise(resolve => requestAnimationFrame(resolve));
        maxRafDelay = Math.max(maxRafDelay, performance.now() - t0);
        await new Promise(resolve => setTimeout(resolve, 120));
    }
    return JSON.stringify({
        total: wrap.querySelectorAll('.tm-pdf-viewer__page').length,
        painted: wrap.querySelectorAll('canvas[data-rendered]').length,
        slots: wrap.querySelectorAll('.tm-pdf-viewer__page-overlay-slot').length,
        maxRafDelay
    });
}");
        var probe = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(result!);
        Assert.IsNotNull(probe, "the continuous wrap must exist");
        Assert.AreEqual(TotalPages, (int)probe["total"], "all placeholders exist without painting everything");
        Assert.IsTrue(probe["painted"] <= 20,
            "the painted set must stay bounded while jumping through the document");
        Assert.IsTrue(probe["slots"] <= 12,
            $"the mounted slot set must stay bounded (was {probe["slots"]}) — annotations on every page must not blow up the diff");
        Assert.IsTrue(probe["maxRafDelay"] < 1000,
            "the main thread must stay responsive while the lazy renderer works");
    }

    /// <summary>
    /// Clicks a toolbar command on the All-Features viewer: the visible button when it
    /// fits (ToolbarLabels=Auto drops the tm-pdf-viewer__btn class, so key off the
    /// aria-label inside THIS viewer), otherwise the "More" menu entry (a pressed
    /// toggle renders as a menuitemcheckbox, hence the item-class lookup).
    /// </summary>
    private async Task ClickAllFeaturesCommandAsync(IPage page, string label)
    {
        var button = page.Locator($"{AllFeaturesSelector} .tm-pdf-viewer [aria-label='{label}']").First;
        if (await button.IsVisibleAsync() && !await button.IsDisabledAsync())
        {
            await button.ClickAsync();
            return;
        }

        await page.Locator($"{AllFeaturesSelector} button.tm-toolbar-more").First.ClickAsync();
        await page.Locator($"{AllFeaturesSelector} .tm-toolbar-more__item:has-text('{label}')").First.ClickAsync();
    }

    /// <summary>Screenshots the All-Features viewer (the continuous annotation evidence).</summary>
    private async Task SaveAllFeaturesScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "pdf-viewer");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");

        await page.Locator(AllFeaturesSelector).ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400); // let the band reports settle
        await page.Locator($"{AllFeaturesSelector} .tm-pdf-viewer").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
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
        await ClickZoomOutAsync(page);
        await ClickZoomOutAsync(page);
        await page.WaitForTimeoutAsync(1500); // let the in-place repaint settle

        await page.Locator($"{SectionSelector} .tm-pdf-viewer").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    /// <summary>
    /// Clicks the zoom-out command. With the demo's <c>ToolbarLabels=Auto</c> the compact zoom
    /// buttons may not fit the row and live in the "More" menu instead — the same command is
    /// offered there, so this falls back to the menu entry.
    /// </summary>
    private async Task ClickZoomOutAsync(IPage page)
    {
        var zoomOut = page.Locator($"{SectionSelector} .tm-pdf-viewer__btn[aria-label='Zoom out']").First;
        if (await zoomOut.IsVisibleAsync())
        {
            await zoomOut.ClickAsync();
            return;
        }

        var more = page.Locator($"{SectionSelector} button.tm-toolbar-more").First;
        await more.ClickAsync();
        await page.Locator($"{SectionSelector} [role='menuitem']:has-text('Zoom out')").First.ClickAsync();
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
