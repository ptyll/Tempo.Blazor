using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 7 - the mobile layout (decision G2: viewing + light edits). The editor resolves its layout from its
/// OWN container, so on a phone viewport (390x844 and 320x640, <c>HasTouch=true</c>, the real CDP touch pipeline) the demo editor is
/// mobile: a compact header with the tool row, a full-width canvas with a floating zoom, a <c>TmMobileActionBar</c> and non-modal bottom
/// sheets. Covers the baseline finding of phase 1 (the canvas measured 0 px wide on a phone), the whole touch flow (view, tap-insert, select,
/// edit a property, delete from the sheet, switch page, layer visibility, pinch zoom, long-press menu, export through the sheet), the
/// "nothing wider than the viewport" rule at 320 and the tablet / desktop regression. Screenshots land in
/// <c>__screenshots__/wireframe/phase7/</c> only with <c>TM_E2E_UPDATE_BASELINE=1</c> (otherwise in the git-ignored TestResults folder);
/// review them against <c>output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/wireframe-editor--mobile.png</c>,
/// <c>wireframe-layers-panel--mobile.png</c> and <c>wireframe-export-dialog--mobile.png</c>.
/// </summary>
[TestClass]
public sealed class WireframePhase7MobileE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotDirectory => UpdateBaseline
        ? Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "phase7")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase7");

    // ── Layout: the canvas has a width, nothing is wider than the viewport ──────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Baseline finding fixed: the canvas is as wide as the editor, the editor as wide as the viewport; no element sticks out; the sheet is closed and the action bar is there")]
    public async Task Mobile_Layout_CanvasHasWidth_NothingWiderThanViewport(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);

        Assert.AreEqual("mobile", await editor.GetAttributeAsync("data-editor-layout"));
        var metrics = await editor.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            e => {
                const svg = e.querySelector('.tm-wd-canvas__svg').getBoundingClientRect();
                const box = e.getBoundingClientRect();
                const vw = document.documentElement.clientWidth;
                const wide = Array.from(e.querySelectorAll('*')).filter(n => {
                    const r = n.getBoundingClientRect();
                    return r.width > 0 && r.height > 0 && (r.right > vw + 1 || r.left < -1);
                }).filter(n => !n.closest('.tm-wd-canvas__svg') && !n.closest('[hidden]'))
                  .map(n => n.tagName.toLowerCase() + '.' + String(n.className && n.className.baseVal !== undefined ? n.className.baseVal : n.className).split(' ')[0]);
                return { svgWidth: svg.width, svgHeight: svg.height, editorWidth: box.width, viewport: vw,
                         docOverflow: document.documentElement.scrollWidth - vw, wide: wide.slice(0, 8),
                         sheet: e.querySelectorAll('.tm-editor-shell__sheet').length,
                         bar: e.querySelectorAll('.tm-mobile-action-bar__bar').length,
                         rulers: e.querySelectorAll('.tm-wd-ruler').length, minimap: e.querySelectorAll('.tm-wd-editor__minimap-wrap').length,
                         panels: e.querySelectorAll('.tm-editor-shell__panel').length };
            }
            """);
        var svgWidth = metrics.GetProperty("svgWidth").GetDouble();
        TestContext.WriteLine($"mobile {width}: svg {svgWidth:0}x{metrics.GetProperty("svgHeight").GetDouble():0}, editor {metrics.GetProperty("editorWidth").GetDouble():0}, doc overflow {metrics.GetProperty("docOverflow").GetInt32()}");
        // The demo page keeps 16 px of padding per side, so the editor itself is viewport - 32 px; the canvas must fill the editor (minus its 1 px border).
        var editorWidth = metrics.GetProperty("editorWidth").GetDouble();
        Assert.IsTrue(svgWidth >= editorWidth - 3 && svgWidth >= width - 40, $"the canvas is {svgWidth:0}px wide in a {editorWidth:0}px editor / {width}px viewport (it measured 0 px before phase 7)");
        Assert.IsTrue(metrics.GetProperty("svgHeight").GetDouble() > 120, "the canvas has a usable height");
        Assert.IsTrue(metrics.GetProperty("editorWidth").GetDouble() <= width + 0.5, "the editor fits the viewport");
        Assert.AreEqual(0, metrics.GetProperty("wide").GetArrayLength(), "elements wider than the viewport: " + metrics.GetProperty("wide"));
        Assert.AreEqual(0, metrics.GetProperty("sheet").GetInt32(), "the sheet starts closed");
        Assert.AreEqual(1, metrics.GetProperty("bar").GetInt32(), "the action bar renders");
        Assert.AreEqual(0, metrics.GetProperty("rulers").GetInt32());
        Assert.AreEqual(0, metrics.GetProperty("minimap").GetInt32());
        Assert.AreEqual(0, metrics.GetProperty("panels").GetInt32(), "no docked panel on a phone");

        await ShotAsync(page, $"mobile-{width}--editor");
    }

    // ── The touch flow ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Touch flow: tap-insert a component, it is selected (properties peek), rename it in the sheet, duplicate, delete from the sheet, undo brings it back")]
    public async Task Mobile_TouchFlow_Insert_Select_EditProperty_Delete(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);
        var elements = () => editor.Locator("g[data-el-id]").CountAsync();
        var before = await elements();

        // 1. Components tile -> the palette sheet; a tap inserts and selects.
        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']").TapAsync();
        var sheet = editor.Locator(".tm-editor-shell__sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);
        await ShotAsync(page, $"mobile-{width}--components-sheet");
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // 2. The insert selected it: the sheet peeks the properties with the quick actions.
        var quick = editor.Locator("[data-testid='wd-quick-actions']");
        await quick.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual("0", await sheet.GetAttributeAsync("data-snap-index"), "the selection peeks the sheet");
        await ShotAsync(page, $"mobile-{width}--selected-peek");
        foreach (var testId in new[] { "wd-quick-duplicate", "wd-quick-lock", "wd-quick-delete" })
        {
            var box = await editor.Locator($"[data-testid='{testId}']").BoundingBoxAsync();
            Assert.IsNotNull(box, testId);
            Assert.IsTrue(box!.Height >= 43.5, $"{testId} is finger-sized ({box.Height:0}px)");
            Assert.IsTrue(box.X >= 0 && box.X + box.Width <= width + 0.5, $"{testId} is inside the viewport");
        }

        // 3. Raise the sheet (swipe handle = the Properties tab at half) and change the Name.
        await editor.Locator(".tm-editor-shell__tabs [role='tab']", new() { HasTextString = "Properties" }).TapAsync();
        var name = editor.Locator("[data-testid='wd-element-name']");
        await name.ScrollIntoViewIfNeededAsync();
        await name.FillAsync("Book now");
        await name.PressAsync("Tab");
        await page.WaitForFunctionAsync(
            "() => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); return g && (g.getAttribute('aria-label') === 'Book now'); }",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ShotAsync(page, $"mobile-{width}--properties-name");

        // 4. Duplicate (one more element), then delete the selection from the sheet, then undo.
        await editor.Locator("[data-testid='wd-quick-duplicate']").ScrollIntoViewIfNeededAsync();
        await editor.Locator("[data-testid='wd-quick-duplicate']").TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length >= n + 2",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-quick-delete']").TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n + 1",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        // The delete closed the selection-opened sheet and unmounted the button that had the focus: the focus must not fall to <body> (2.4.3).
        await page.WaitForFunctionAsync("() => { const a = document.activeElement; return !!a && a !== document.body && !!a.closest('.tm-wd-editor'); }",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']").TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n + 2",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(before + 2, await elements(), "the delete was one undoable step");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Touch flow: Pages | Layers sheet - add and switch page, toggle a layer's visibility")]
    public async Task Mobile_TouchFlow_PagesAndLayers(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);

        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='pages-layers']").TapAsync();
        var extra = editor.Locator("[data-region='extra']");
        await extra.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);
        await ShotAsync(page, $"mobile-{width}--pages-layers-sheet");

        await extra.Locator("[data-testid='wd-page-add']").TapAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await extra.Locator(".tm-wd-pages__select").First.TapAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-pages__select[aria-current=\"page\"]') === document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-pages__select')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        await extra.Locator("[role='tab']", new() { HasTextString = "Layers" }).TapAsync();
        var visibility = editor.Locator(".tm-wd-layers__visibility").First;
        await visibility.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var pressed = await visibility.GetAttributeAsync("aria-pressed");
        await visibility.TapAsync();
        await page.WaitForFunctionAsync("v => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-layers__visibility').getAttribute('aria-pressed') !== v",
            pressed, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
        await ShotAsync(page, $"mobile-{width}--layers-sheet");
        await AssertNothingWiderThanViewportAsync(page, width);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Pinch zoom with two real touch points and the floating zoom buttons; a one-finger drag on the empty canvas pans")]
    public async Task Mobile_Gestures_PinchZoom_FloatingZoom_Pan(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);
        var svg = editor.Locator(".tm-wd-canvas__svg");
        var session = await page.Context.NewCDPSessionAsync(page);
        var box = (await svg.BoundingBoxAsync())!;
        var (x, y) = (box.X + box.Width / 2, box.Y + box.Height * 0.6);
        var start = await ViewBoxAsync(editor);

        await CdpTouchAsync(session, "touchStart", (1, x - 40, y), (2, x + 40, y));
        for (var i = 1; i <= 8; i++)
            await CdpTouchAsync(session, "touchMove", (1, x - 40 - i * 6, y), (2, x + 40 + i * 6, y));
        await CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(300);
        var pinched = await ViewBoxAsync(editor);
        Assert.IsTrue(pinched.W < start.W - 1, $"a two-finger spread zooms in (viewBox {start.W:0} -> {pinched.W:0})");

        // One finger on the empty canvas pans (no rubber band on a phone).
        await CdpTouchAsync(session, "touchStart", (1, x, y));
        for (var i = 1; i <= 8; i++)
            await CdpTouchAsync(session, "touchMove", (1, x - i * 5, y - i * 5));
        await CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(300);
        var panned = await ViewBoxAsync(editor);
        Assert.IsTrue(Math.Abs(panned.X - pinched.X) > 1 || Math.Abs(panned.Y - pinched.Y) > 1, "a one-finger drag pans the canvas");
        Assert.AreEqual(0, await editor.Locator(".tm-wd-rubber").CountAsync(), "no rubber band is left behind");

        var label = editor.Locator("[data-testid='wd-zoom-overlay'] .tm-wd-editor__zoom-overlay-label");
        var zoomBefore = await ZoomPercentAsync(label);
        await editor.Locator("[data-testid='wd-zoom-in']").TapAsync();
        await page.WaitForFunctionAsync("p => parseFloat(document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-editor__zoom-overlay-label').textContent) > p",
            zoomBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-zoom-fit']").TapAsync();
        await page.WaitForTimeoutAsync(400);
        var fitted = await ViewBoxAsync(editor);
        Assert.IsTrue(fitted.W > pinched.W, "fit shows the whole page again");
        await ShotAsync(page, $"mobile-{width}--zoom-overlay");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("A real 800 ms touch hold on an element opens the action sheet (no fixed menu), Delete from it removes the element")]
    public async Task Mobile_LongPress_OpensTheActionSheet(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var (cx, cy) = await StableElementCentreAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);

        await CdpTouchAsync(session, "touchStart", (1, cx, cy));
        await page.WaitForTimeoutAsync(800);
        await CdpTouchAsync(session, "touchEnd");

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator(".tm-wd-editor__context-menu").CountAsync(), "the fixed menu is not used on a phone");
        await page.WaitForTimeoutAsync(400);
        await ShotAsync(page, $"mobile-{width}--long-press-sheet");
        var items = await sheet.Locator("[role='menuitem']").AllInnerTextsAsync();
        CollectionAssert.IsSubsetOf(new[] { "Duplicate", "Delete" }, items.Select(i => i.Trim()).ToArray());
        Assert.IsFalse(items.Any(i => i.Contains("Align", StringComparison.OrdinalIgnoreCase)), "align is tablet+");

        var count = await editor.Locator("g[data-el-id]").CountAsync();
        await sheet.Locator("[role='menuitem']", new() { HasTextString = "Delete" }).TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length < n",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Export through a bottom sheet: the dialog is a sheet with the footer inline and fits the viewport; Export downloads a file")]
    public async Task Mobile_Export_ThroughTheSheet(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);

        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='export']").TapAsync();
        var dialog = page.Locator(".tm-modal--sheet .tm-wd-export-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        await page.WaitForTimeoutAsync(300);

        var metrics = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const sheet = document.querySelector('.tm-modal--sheet');
                const footer = sheet.querySelector('.tm-modal-footer');
                const buttons = Array.from(footer.querySelectorAll('button')).map(b => b.getBoundingClientRect());
                const r = sheet.getBoundingClientRect();
                return { left: r.left, right: r.right, bottom: r.bottom, vw: document.documentElement.clientWidth, vh: window.innerHeight,
                         buttons: buttons.length, sameRow: buttons.length > 1 && Math.abs(buttons[0].top - buttons[1].top) < 2,
                         footerBottom: footer.getBoundingClientRect().bottom };
            }
            """);
        Assert.IsTrue(metrics.GetProperty("left").GetDouble() >= -0.5 && metrics.GetProperty("right").GetDouble() <= metrics.GetProperty("vw").GetDouble() + 0.5, "the sheet fits the width");
        Assert.IsTrue(metrics.GetProperty("bottom").GetDouble() <= metrics.GetProperty("vh").GetDouble() + 0.5, "the sheet fits the height");
        Assert.AreEqual(2, metrics.GetProperty("buttons").GetInt32(), "Cancel and Export");
        Assert.IsTrue(metrics.GetProperty("sameRow").GetBoolean(), "the footer buttons sit side by side");
        Assert.IsTrue(metrics.GetProperty("footerBottom").GetDouble() <= metrics.GetProperty("vh").GetDouble() + 0.5, "the footer is not pushed below the screen");
        await ShotAsync(page, $"mobile-{width}--export-sheet");

        // Export posts the SVG to api/wireframe/export/png on the DEMO API (the editor names the "DemoApi" client; the WASM host itself serves
        // no such endpoint and answered 405 before): the request goes to the API origin, answers 200 and a real PNG is downloaded.
        var request = page.WaitForRequestAsync(r => r.Url.Contains("api/wireframe/export/png", StringComparison.OrdinalIgnoreCase)
            && r.Method == "POST", new PageWaitForRequestOptions { Timeout = 30_000 });
        var response = page.WaitForResponseAsync(r => r.Url.Contains("api/wireframe/export/png", StringComparison.OrdinalIgnoreCase)
            && r.Request.Method == "POST", new PageWaitForResponseOptions { Timeout = 30_000 });
        var download = await page.RunAndWaitForDownloadAsync(async () =>
            await page.Locator(".tm-modal--sheet .tm-modal-footer button", new() { HasTextString = "Export" }).TapAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 60_000 });
        var sent = await request;
        var body = sent.PostData ?? string.Empty;
        Assert.IsTrue(body.Contains("<svg", StringComparison.Ordinal) || body.Contains("\\u003Csvg", StringComparison.Ordinal), "the exported SVG is the body of the request");
        Assert.IsTrue(body.Contains("viewBox", StringComparison.Ordinal) && !body.Contains("width:100%", StringComparison.Ordinal),
            "the exported SVG is the standalone page, not the live canvas element");
        var answered = await response;
        Assert.IsTrue(answered.Url.Contains(":5100", StringComparison.Ordinal), $"the export went to the Demo API origin, not the WASM host ({answered.Url})");
        Assert.AreEqual(200, answered.Status, "the Demo API answers the export");
        Assert.IsTrue(download.SuggestedFilename.EndsWith(".png", StringComparison.OrdinalIgnoreCase), $"downloaded {download.SuggestedFilename}");
        var path = await download.PathAsync();
        Assert.IsNotNull(path);
        var bytes = await File.ReadAllBytesAsync(path!);
        Assert.IsTrue(bytes.Length > 200, "the PNG has content");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray(), "the file is a PNG (magic bytes)");
        await page.Locator(".tm-modal--sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844, "Export JSON", ".wireframe.json")]
    [DataRow(320, 640, "Export SVG", ".svg")]
    [Description("Download from the More sheet: the file really arrives (Blob URL on the hidden anchor - the iOS-Safari-safe path), with the right name")]
    public async Task Mobile_MoreMenu_ExportJsonAndSvg_DownloadAFile(int width, int height, string entry, string extension)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);

        await editor.Locator(".tm-mobile-action-bar__more").TapAsync();
        var menu = page.Locator(".tm-overlay-panel-sheet");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var download = await page.RunAndWaitForDownloadAsync(async () =>
            await menu.Locator("[role='menuitem']", new() { HasTextString = entry }).TapAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 30_000 });

        Assert.IsTrue(download.SuggestedFilename.EndsWith(extension, StringComparison.OrdinalIgnoreCase), $"downloaded {download.SuggestedFilename}");
        var path = await download.PathAsync();
        Assert.IsNotNull(path);
        Assert.IsTrue(new FileInfo(path!).Length > 20, "the downloaded file has content");
        var text = await File.ReadAllTextAsync(path!);
        Assert.IsTrue(text.Contains(extension == ".svg" ? "<svg" : "\"elements\"", StringComparison.Ordinal), "the file is a wireframe " + extension);
    }
    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("The More menu: device preset resizes the page (undoable), read-only/grid toggles; no Import, Align or Group")]
    public async Task Mobile_MoreMenu_DevicePreset_NoTabletCommands(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);

        await editor.Locator(".tm-mobile-action-bar__more").TapAsync();
        var menu = page.Locator(".tm-overlay-panel-sheet");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);
        var labels = (await menu.Locator("[role='menuitem'], [role='menuitemcheckbox']").AllInnerTextsAsync()).Select(l => l.Trim()).ToList();
        Assert.IsTrue(labels.Any(l => l.StartsWith("iPhone 14", StringComparison.Ordinal)), "device presets: " + string.Join(" | ", labels));
        Assert.IsFalse(labels.Any(l => l.Contains("Import", StringComparison.OrdinalIgnoreCase)), "import is tablet+");
        Assert.IsFalse(labels.Any(l => l.Contains("Align", StringComparison.OrdinalIgnoreCase)));
        await ShotAsync(page, $"mobile-{width}--more-sheet");

        var pageWidthBefore = await editor.Locator(".tm-wd-canvas__page").EvaluateAsync<double>("r => parseFloat(r.getAttribute('width'))");
        await menu.Locator("[role='menuitemcheckbox'], [role='menuitem']", new() { HasTextString = "Android" }).First.TapAsync();
        await page.WaitForFunctionAsync("w => parseFloat(document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__page').getAttribute('width')) !== w",
            pageWidthBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(360, await editor.Locator(".tm-wd-canvas__page").EvaluateAsync<double>("r => parseFloat(r.getAttribute('width'))"), "Android is 360 wide");
        await editor.Locator("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']").TapAsync();
        await page.WaitForFunctionAsync("w => parseFloat(document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__page').getAttribute('width')) === w",
            pageWidthBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: opening, switching and closing the sheet never repaints the canvas scene (a MutationObserver sees no scene mutation)")]
    public async Task Mobile_SheetInteractions_DoNotMutateTheCanvasScene()
    {
        var (page, editor) = await OpenWithElementAsync(390, 844);
        await page.EvaluateAsync(
            """
            () => {
                window.__wdMutations = 0;
                const svg = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg');
                new MutationObserver(list => { window.__wdMutations += list.filter(m => !(m.target.closest && m.target.closest('.tm-wd-selection'))).length; })
                    .observe(svg, { subtree: true, childList: true, attributes: true, characterData: true });
            }
            """);

        var components = editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']");
        await components.TapAsync();
        await editor.Locator(".tm-editor-shell__sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='pages-layers']").TapAsync();
        await page.WaitForTimeoutAsync(500);
        await editor.Locator(".tm-editor-shell__tabs [role='tab']", new() { HasTextString = "Add" }).TapAsync();
        await page.WaitForTimeoutAsync(300);
        await components.TapAsync();
        await editor.Locator(".tm-editor-shell__sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.WaitForTimeoutAsync(800);

        var mutations = await page.EvaluateAsync<int>("() => window.__wdMutations");
        Assert.IsTrue(mutations <= 6, $"the sheet interactions mutated the svg {mutations} times (only the script relayout viewBox change is allowed)");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a one-finger drag on the element moves it, no resize / rotate grips exist, and an Escape-less tap on the empty canvas deselects and closes the peeked sheet")]
    public async Task Mobile_LightEditing_NoGrips_TapEmptyDeselects()
    {
        var (page, editor) = await OpenWithElementAsync(390, 844);
        var session = await page.Context.NewCDPSessionAsync(page);
        var box = (await editor.Locator("g[data-el-id]").First.Locator("svg").First.BoundingBoxAsync())!;
        var (cx, cy) = (box.X + box.Width / 2, box.Y + box.Height / 2);

        await CdpTouchAsync(session, "touchStart", (1, cx, cy));
        await CdpTouchAsync(session, "touchEnd");
        await editor.Locator(".tm-wd-selection").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        Assert.AreEqual(0, await editor.Locator(".tm-wd-selection [data-handle]").CountAsync(), "no resize handle on a phone");
        Assert.AreEqual(0, await editor.Locator(".tm-wd-selection [data-rotate-handle]").CountAsync(), "no rotate handle on a phone");
        await editor.Locator(".tm-editor-shell__sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // The peeked sheet took room from the canvas and the page re-fitted: measure the element again before dragging it.
        await page.WaitForTimeoutAsync(900);
        box = (await editor.Locator("g[data-el-id]").First.Locator("svg").First.BoundingBoxAsync())!;
        (cx, cy) = (box.X + box.Width / 2, box.Y + box.Height / 2);
        var sheetTop = (await editor.Locator(".tm-editor-shell__sheet .tm-drawer__panel").BoundingBoxAsync())!.Y;
        Assert.IsTrue(cy < sheetTop, $"the selected element stays visible above the peeked sheet (element y {cy:0}, sheet top {sheetTop:0})");
        var before = await ElementPositionAsync(editor);
        await DragAsync(session, (cx, cy), (cx + 40, cy + 40));
        await page.WaitForFunctionAsync("x => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); " +
            "const m = /translate\\(([-\\d.]+),/.exec(g.getAttribute('transform')); return Math.abs(parseFloat(m[1]) - x) > 1; }",
            before.X, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // A tap on empty canvas, high above the sheet, deselects; the sheet a selection opened closes again.
        var (px, py) = await EmptyCanvasCornerAsync(page);
        await CdpTouchAsync(session, "touchStart", (1, px, py));
        await CdpTouchAsync(session, "touchEnd");
        await editor.Locator(".tm-editor-shell__sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: the host Open action in the header stays reachable and nothing built-in says Save; read-only (More toggle) disables the quick actions")]
    public async Task Mobile_HostSlot_NoBuiltInSave_ReadOnlyToggle()
    {
        var (page, editor) = await OpenWithElementAsync(390, 844);

        Assert.AreEqual(1, await editor.Locator("[data-testid='wf-open-btn']").CountAsync(), "the host's Open… button is in the header slot");
        var barText = await editor.Locator(".tm-mobile-action-bar").InnerTextAsync();
        Assert.IsFalse(barText.Contains("Save", StringComparison.OrdinalIgnoreCase), "no built-in Save: " + barText);

        await editor.Locator(".tm-mobile-action-bar__more").TapAsync();
        var menu = page.Locator(".tm-overlay-panel-sheet");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await menu.Locator("[role='menuitemcheckbox'], [role='menuitem']", new() { HasTextString = "Read-only" }).First.TapAsync();
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual("true", await editor.Locator("[data-testid='wd-title']").EvaluateAsync<string>("i => String(i.disabled)"), "the title is read-only now");
    }


    // ── Plan 643bba06 (G2 review debt): landscape phones and the keyboard-only sheet ────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(667, 375)]
    [DataRow(844, 390)]
    [Description("Edge: a phone in LANDSCAPE (667x375, 844x390) stays usable. The layout follows the editor's own container (< 768 = mobile): 667x375 is mobile, 844x390 has a >= 768 container and is the tablet layout - in both the 375/390 px high editor keeps a usable canvas, the panels stay inside it and nothing sticks out")]
    public async Task Mobile_Landscape_StaysUsable(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);

        var layout = await editor.GetAttributeAsync("data-editor-layout");
        var editorWidth = (await editor.BoundingBoxAsync())!.Width;
        Assert.AreEqual(editorWidth < 768 ? "mobile" : "tablet", layout,
            $"the layout follows the editor's own container ({editorWidth:0}px), never the viewport ({width}px)");
        await AssertNothingWiderThanViewportAsync(page, width);
        var canvas = (await editor.Locator(".tm-wd-canvas__svg").BoundingBoxAsync())!;
        Assert.IsTrue(canvas.Height >= 120, $"the canvas keeps a usable height in landscape ({canvas.Height:0}px)");
        await ShotAsync(page, $"mobile-landscape-{width}x{height}--editor");

        if (layout != "mobile")
        {
            // Tablet in landscape: the palette is the left drawer; it must fit the 390 px high editor and close again.
            await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
            var drawer = editor.Locator("[data-testid='wd-left-drawer']");
            await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);
            var editorBox = (await editor.BoundingBoxAsync())!;
            var drawerBox = (await drawer.BoundingBoxAsync())!;
            Assert.IsTrue(drawerBox.Y >= editorBox.Y - 1 && drawerBox.Y + drawerBox.Height <= editorBox.Y + editorBox.Height + 1,
                $"the drawer stays inside the {editorBox.Height:0}px editor");
            await ShotAsync(page, $"mobile-landscape-{width}x{height}--drawer");
            return;
        }

        var bar = await editor.Locator(".tm-mobile-action-bar__bar").BoundingBoxAsync();
        Assert.IsNotNull(bar, "the action bar renders");
        Assert.IsTrue(bar!.Width <= width + 0.5, "the bar is not wider than the viewport");

        // The Components sheet opens at half: it stays inside the editor, and the palette tiles are still finger-sized.
        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']").TapAsync();
        var sheet = editor.Locator(".tm-editor-shell__sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(600);
        var editorRect = (await editor.BoundingBoxAsync())!;
        var panel = (await sheet.Locator(".tm-drawer__panel").BoundingBoxAsync())!;
        Assert.IsTrue(panel.Y >= editorRect.Y - 1 && panel.Y + panel.Height <= editorRect.Y + editorRect.Height + 1,
            $"the sheet stays inside the {editorRect.Height:0}px editor (sheet {panel.Y:0}..{panel.Y + panel.Height:0}, editor {editorRect.Y:0}..{editorRect.Y + editorRect.Height:0})");
        var tile = (await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.BoundingBoxAsync())!;
        Assert.IsTrue(tile.Height >= 43.5, $"the palette tile is finger-sized in landscape ({tile.Height:0}px)");
        await AssertNothingWiderThanViewportAsync(page, width);
        await ShotAsync(page, $"mobile-landscape-{width}x{height}--components-sheet");

        await editor.Locator(".tm-editor-shell__sheet-close").TapAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }
    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [Description("Keyboard only: Enter on the Add tile opens the sheet, a palette item inserts on Enter (selection peeks the properties and is announced), Escape closes, and the focus is back on the Add tile - never on <body>")]
    public async Task Mobile_KeyboardOnly_Sheet_OpenInsertEscape_FocusReturnsToTheOpener(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height, touch: false);
        var add = editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']");
        var before = await editor.Locator("g[data-el-id]").CountAsync();

        // 1. Enter on the focused tile (a keyboard click: detail 0) opens the palette sheet.
        await add.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var sheet = editor.Locator(".tm-editor-shell__sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);

        // 2. A palette item takes the keyboard: Enter inserts (no pointer involved) and selects the element, which peeks the properties.
        var item = editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First;
        await item.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-quick-actions']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var status = editor.Locator("[data-testid='wd-canvas-status']");
        await page.WaitForFunctionAsync("() => /^Selected: .+Properties and actions are open\\./.test(document.querySelector('.tm-wd-editor [data-testid=\"wd-canvas-status\"]').textContent.trim())",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual("polite", await status.GetAttributeAsync("aria-live"), "the peek is announced politely, never assertively");

        // 3. A keyboard activation of a quick action is NOT swallowed by the hold release-click guard (detail 0): Lock toggles on Enter.
        var lock_ = editor.Locator("[data-testid='wd-quick-lock']");
        await lock_.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => /^Locked/.test(document.querySelector('.tm-wd-editor [data-testid=\"wd-canvas-status\"]').textContent.trim())",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // 4. Escape closes the non-modal sheet and the focus is handed back to a tile of the action bar (the sheet unmounted the focused control).
        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => { const a = document.activeElement; return !!a && a !== document.body && !!a.closest('.tm-wd-editor'); }",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow("components")]
    [DataRow("pages-layers")]
    [Description("Keyboard: closing the sheet that a tile opened puts the focus back on THAT tile (not the first one), through the sheet's own close button")]
    public async Task Mobile_Keyboard_CloseSheet_FocusReturnsToTheTileThatOpenedIt(string tile)
    {
        var (page, editor) = await OpenAsync(390, 844, touch: false);
        var opener = editor.Locator($".tm-mobile-action-bar__action[data-action-id='{tile}']");
        await opener.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var sheet = editor.Locator(".tm-editor-shell__sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);

        await editor.Locator(".tm-editor-shell__sheet-close").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        await page.WaitForFunctionAsync(
            "id => document.activeElement?.getAttribute('data-action-id') === id",
            tile, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }
    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844, "mobile")]
    [DataRow(1024, 768, "tablet")]
    [DataRow(1440, 900, "desktop")]
    [Description("Axe (wcag2a/aa): the editor INCLUDING the Properties panel (.tm-wd-props - no exclusion any more) has no critical or serious violation on mobile (selection sheet peeked), tablet (inspector sheet) and desktop (docked inspector), for an empty selection (Canvas / Page / Grid) and for a selected element")]
    public async Task Axe_PropertiesPanelIncluded_NoCriticalOrSeriousViolations(int width, int height, string layout)
    {
        var (page, editor) = await OpenAsync(width, height, touch: layout != "desktop");
        Assert.AreEqual(layout, await editor.GetAttributeAsync("data-editor-layout"));
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js" });

        // 1. Empty selection: tablet / desktop show the Canvas | Page | Grid inspector; a phone has no empty-selection sheet.
        if (layout == "tablet")
        {
            await editor.Locator("[data-testid='wd-toggle-inspector']").ClickAsync();
            await editor.Locator("[data-testid='wd-inspector-sheet']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }

        if (layout != "mobile")
        {
            await editor.Locator("[data-testid='wd-section-canvas']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(700);   // sheet slide-in must not be sampled mid-transform
            await AssertNoAxeViolationsAsync(page, $"{layout} / empty selection");
        }

        // 2. A selected element: insert one through the palette (selects it) - the Element / Layout / per-type prop sections.
        if (layout == "tablet")
        {
            await editor.Locator("[data-testid='wd-toggle-left']").ClickAsync();
            await editor.Locator("[data-testid='wd-left-drawer']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        else if (layout == "mobile")
        {
            await editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']").ClickAsync();
        }

        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        if (layout == "mobile")
        {
            await editor.Locator("[data-testid='wd-quick-actions']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        else
        {
            // On a tablet the insert replaces the palette drawer with the inspector sheet (at most one sheet is open) - do not toggle it back off.
            await editor.Locator("[data-testid='wd-element-name']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }

        await page.WaitForTimeoutAsync(700);
        Assert.IsTrue(await editor.Locator(".tm-wd-props input, .tm-wd-props select").CountAsync() > 0, "the Properties panel is in the DOM (it must be part of the scan)");
        await AssertNoAxeViolationsAsync(page, $"{layout} / element selected");
    }

    /// <summary>
    /// axe over the whole first editor, Properties panel included. Only the SVG scene is out of scope: it is a drawing whose element
    /// semantics are covered by the canvas specs.
    /// </summary>
    private static async Task AssertNoAxeViolationsAsync(IPage page, string state)
    {
        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelectorAll('.tm-wd-editor')[0];
                const result = await axe.run({ include: [host], exclude: [['.tm-wd-canvas__svg']] }, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
            }
            """);
        Assert.AreEqual(0, violations.Length, $"axe [{state}]: " + string.Join(" | ", violations));
    }
    // ── Regression: tablet and desktop are unchanged ────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768, "tablet")]
    [DataRow(1440, 900, "desktop")]
    [Description("Regression: 1024 stays the tablet layout and 1440 the desktop one - no mobile chrome, the resize handles and the minimap are back")]
    public async Task Regression_TabletAndDesktop_AreUnchanged(int width, int height, string layout)
    {
        var (page, editor) = await OpenAsync(width, height, touch: width < 1200, hideSidebar: true);

        Assert.AreEqual(layout, await editor.GetAttributeAsync("data-editor-layout"));
        Assert.AreEqual(0, await editor.Locator(".tm-mobile-action-bar__bar").CountAsync(), "no mobile action bar");
        Assert.AreEqual(0, await editor.Locator(".tm-wd-editor__zoom-overlay").CountAsync(), "no floating zoom");
        Assert.AreEqual(0, await editor.Locator("[data-light-editing]").CountAsync(), "the canvas is not in light editing");
        if (layout == "desktop") Assert.IsTrue(await editor.Locator(".tm-wd-editor__minimap-wrap").CountAsync() > 0, "the desktop minimap is there");
        else Assert.AreEqual(2, await editor.Locator("[data-testid^='wd-toolbar-row-']").CountAsync(), "the tablet two-row toolbar");

        // A selected element still has its eight resize handles (full editing from the tablet up).
        var element = editor.Locator("g[data-el-id]").First;
        if (await element.CountAsync() == 0)
        {
            if (layout == "tablet") await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
            await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
            await element.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        }

        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(8, await editor.Locator(".tm-wd-selection [data-handle]:not([data-handle-hit])").CountAsync(), "eight resize handles");
        Assert.AreEqual(1, await editor.Locator(".tm-wd-selection [data-rotate-handle]").CountAsync() > 0 ? 1 : 0, "a rotate handle");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(IPage Page, ILocator Editor)> OpenAsync(int width, int height, bool touch = true, bool hideSidebar = true)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = false,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type is "error") TestContext.WriteLine($"[console.{message.Type}] {message.Text}"); };
        page.PageError += (_, error) => TestContext.WriteLine($"[pageerror] {error}");
        // The layout resolves from the editor's CONTAINER. The demo sidebar is a desktop affordance (below 1024 px it already collapses),
        // and the demo page's 24 px padding is real; both are what a host would have. Only the desktop width needs the sidebar removed.
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');"
            + (hideSidebar && width >= 1000
                ? """
                  document.addEventListener('DOMContentLoaded', () => {
                      const style = document.createElement('style');
                      style.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }';
                      document.head.appendChild(style);
                  });
                  """
                : string.Empty));
        await GotoAppReadyAsync(page, $"{BaseUrl}{EditorUrl}");

        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(1500);   // container measurement + toolbar overflow settle
        return (page, editor);
    }

    /// <summary>Opens the editor and puts one element on the canvas through the Components sheet, then returns to the clean canvas.</summary>
    private async Task<(IPage Page, ILocator Editor)> OpenWithElementAsync(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height);
        if (await editor.Locator("g[data-el-id]").CountAsync() == 0)
        {
            await editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']").TapAsync();
            await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.TapAsync();
            await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length >= 1",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        // Close the peeked sheet (the Properties action-bar path closes it) so the canvas has its room.
        var close = editor.Locator(".tm-editor-shell__sheet-close");
        if (await close.CountAsync() > 0)
        {
            await close.First.TapAsync();
            await editor.Locator(".tm-editor-shell__sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        }

        // Closing the sheet leaves the selection as it is: a tap on the empty canvas deselects (light editing), so a later tap on the element selects it afresh.
        if (await editor.Locator(".tm-wd-selection").CountAsync() > 0)
        {
            var session = await page.Context.NewCDPSessionAsync(page);
            var (px, py) = await EmptyCanvasCornerAsync(page);
            await CdpTouchAsync(session, "touchStart", (1, px, py));
            await CdpTouchAsync(session, "touchEnd");
            await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-selection').length === 0",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        await page.WaitForTimeoutAsync(800);
        await editor.Locator(".tm-wd-canvas__svg").ScrollIntoViewIfNeededAsync();
        return (page, editor);
    }

    /// <summary>
    /// A point in the top-left of the VISIBLE part of the canvas that is empty page (not an element, not the selection, not the floating zoom). Computed
    /// at the moment of use, in viewport coordinates: the page can scroll by a few pixels while a sheet closes (the 600 px editor barely fits a 640 px
    /// viewport), so a box measured earlier would put the touch off screen.
    /// </summary>
    private static async Task<(double X, double Y)> EmptyCanvasCornerAsync(IPage page)
    {
        var text = await page.EvaluateAsync<string>(
            """
            () => {
                const svg = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg');
                const r = svg.getBoundingClientRect();
                const left = Math.max(r.left, 0), top = Math.max(r.top, 0), right = Math.min(r.right, window.innerWidth), bottom = Math.min(r.bottom, window.innerHeight);
                for (let dy = 8; dy < Math.max(bottom - top - 8, 9); dy += 12) {
                    for (let dx = 8; dx < Math.max(right - left - 8, 9); dx += 12) {
                        const x = left + dx, y = top + dy;
                        const el = document.elementFromPoint(x, y);
                        if (el && svg.contains(el) && !el.closest('[data-el-id], .tm-wd-selection, [data-connector-id]')) return x + ',' + y;
                    }
                }
                return '';
            }
            """);
        Assert.IsFalse(string.IsNullOrEmpty(text), "no empty point on the visible canvas");
        var parts = text.Split(',');
        return (double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The centre of the element once its box stopped moving (closing a sheet re-fits the page by script) and the element - not the
    /// floating zoom or anything else - is what a finger would hit there.
    /// </summary>
    private static async Task<(double X, double Y)> StableElementCentreAsync(IPage page, ILocator editor)
    {
        var svg = editor.Locator("g[data-el-id]").First.Locator("svg").First;
        (double X, double Y, double W, double H)? last = null;
        for (var i = 0; i < 40; i++)
        {
            var b = await svg.BoundingBoxAsync();
            Assert.IsNotNull(b, "the element has a box");
            var now = (b!.X, b.Y, b.Width, b.Height);
            if (last is { } l && Math.Abs(l.X - now.X) < 0.5 && Math.Abs(l.Y - now.Y) < 0.5 && Math.Abs(l.W - now.Width) < 0.5)
            {
                var (cx, cy) = (now.X + now.Width / 2, now.Y + now.Height / 2);
                var hit = await page.EvaluateAsync<bool>($"() => {{ const el = document.elementFromPoint({cx.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {cy.ToString(System.Globalization.CultureInfo.InvariantCulture)}); return !!el && !!el.closest('[data-el-id]'); }}");
                if (hit) return (cx, cy);
            }

            last = now;
            await page.WaitForTimeoutAsync(150);
        }

        Assert.Fail("the element never settled under a finger-reachable point");
        return default;
    }

    private static async Task AssertNothingWiderThanViewportAsync(IPage page, int width)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.IsTrue(overflow <= 0, $"the page scrolls horizontally by {overflow}px at {width}px");
        var wide = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('.tm-wd-editor, .tm-wd-editor *, .tm-editor-shell__sheet *, .tm-modal--sheet *')).filter(n => {
                const r = n.getBoundingClientRect();
                return r.width > 0 && r.height > 0 && (r.right > document.documentElement.clientWidth + 1) && !n.closest('.tm-wd-canvas__svg') && !n.closest('[hidden]');
            }).slice(0, 6).map(n => n.tagName.toLowerCase() + '.' + String(n.className && n.className.baseVal !== undefined ? n.className.baseVal : n.className).split(' ')[0])
            """);
        Assert.AreEqual(0, wide.Length, "wider than the viewport: " + string.Join(", ", wide));
    }

    private static async Task<(double X, double Y, double W, double H)> ViewBoxAsync(ILocator editor)
    {
        var v = await editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<double[]>("s => { const b = s.viewBox.baseVal; return [b.x, b.y, b.width, b.height]; }");
        return (v[0], v[1], v[2], v[3]);
    }

    private static async Task<double> ZoomPercentAsync(ILocator label) =>
        await label.EvaluateAsync<double>("e => parseFloat(e.textContent)");

    private static async Task<(double X, double Y)> ElementPositionAsync(ILocator editor)
    {
        var xy = await editor.Locator("g[data-el-id]").First.EvaluateAsync<double[]>(
            "g => { const m = /translate\\(([-\\d.]+),\\s*([-\\d.]+)\\)/.exec(g.getAttribute('transform')); return [parseFloat(m[1]), parseFloat(m[2])]; }");
        return (xy[0], xy[1]);
    }

    /// <summary>Real CDP touch: every listed point is down; an empty list lifts all fingers.</summary>
    private static Task CdpTouchAsync(ICDPSession session, string type, params (int id, double x, double y)[] points)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(p => (object)new Dictionary<string, object> { ["x"] = p.x, ["y"] = p.y, ["id"] = p.id }).ToArray(),
        });

    private static async Task DragAsync(ICDPSession session, (double X, double Y) from, (double X, double Y) to, int steps = 10)
    {
        await CdpTouchAsync(session, "touchStart", (1, from.X, from.Y));
        for (var i = 1; i <= steps; i++)
            await CdpTouchAsync(session, "touchMove", (1, from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps));
        await CdpTouchAsync(session, "touchEnd");
    }

    private async Task ShotAsync(IPage page, string name)
    {
        var path = Path.Combine(ShotDirectory, name + ".png");
        Directory.CreateDirectory(ShotDirectory);
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = false, Timeout = 60_000 });
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

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
