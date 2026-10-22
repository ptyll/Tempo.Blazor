using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 5 — touch and pen on the canvas. Real CDP touch fingers
/// (<c>Input.dispatchTouchEvent</c>, so the browser's own pointer pipeline, capture and <c>touch-action</c> are exercised, not
/// synthetic events) at 1024×768 and 1366×1024 with <c>HasTouch=true</c>: tap selects, finger drag moves, a handle resizes,
/// two fingers pinch-zoom and pan, a 500 ms hold opens the context menu, a toolbox tap adds an element. A mouse regression
/// pass (drag timing, handles, wheel) and the edge cases (Escape/second finger roll-back, read-only, locked element, the
/// page still scrolls from outside the canvas) sit beside them. Captures the 1024 touch editor with a selected element and
/// the larger handles into <c>__screenshots__/wireframe/phase5/</c> only with <c>TM_E2E_UPDATE_BASELINE=1</c> (otherwise into
/// the git-ignored per-run TestResults folder), to be reviewed against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/wireframe-editor--tablet.png</c>.
/// </summary>
[TestClass]
public sealed class WireframePhase5TouchE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotDirectory => UpdateBaseline
        ? Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "phase5")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase5");

    // ── Touch ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("A finger tap on an element selects it (handles + inspector name field appear)")]
    public async Task Touch_TapSelectsElement(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);

        await TapAsync(session, cx, cy);

        await editor.Locator(".tm-wd-selection").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-element-name']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(8, await editor.Locator(".tm-wd-selection [data-handle]:not([data-handle-hit])").CountAsync(), "eight resize handles");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("A finger drag moves the element by the finger travel (grid snapped) and the move is one undo step")]
    public async Task Touch_DragMovesElement(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var before = await ElementPositionAsync(editor);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        var scale = await CanvasScaleAsync(editor);
        var session = await page.Context.NewCDPSessionAsync(page);

        await DragAsync(session, (cx, cy), (cx + 120, cy + 64));

        await page.WaitForFunctionAsync("p => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); " +
            "const m = /translate\\(([-\\d.]+),\\s*([-\\d.]+)\\)/.exec(g.getAttribute('transform')); return Math.abs(parseFloat(m[1]) - p[0]) > 4; }",
            new[] { before.X }, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var after = await ElementPositionAsync(editor);
        Assert.AreEqual(120 / scale, after.X - before.X, 10 / scale + 8, $"moved right by the finger travel (scale {scale:0.###})");
        Assert.AreEqual(64 / scale, after.Y - before.Y, 10 / scale + 8, "moved down by the finger travel");

        // One undo step puts it back.
        await page.Keyboard.PressAsync("Control+z");
        await page.WaitForFunctionAsync("x => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); " +
            "const m = /translate\\(([-\\d.]+),/.exec(g.getAttribute('transform')); return Math.abs(parseFloat(m[1]) - x) < 1; }",
            before.X, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("A finger drag on a resize handle (the larger coarse-pointer hit rect) resizes the element")]
    public async Task Touch_ResizeHandle(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        await TapAsync(session, cx, cy);
        await editor.Locator(".tm-wd-selection").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });

        // The fit zoom of a 1440 px page shows a 36 px button only ~13 px tall: a hit rect is capped at half the element (it must
        // never cover the body a finger drags), so make the element big first - through the inspector fields, the non-drag alternative.
        var fields = editor.Locator(".tm-wd-props__section-body--grid2 input");
        await fields.Nth(2).FillAsync("360");
        await fields.Nth(3).FillAsync("240");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').getAttribute('data-w') === '360'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').getAttribute('data-h') === '240'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);
        var sizeBefore = await ElementSizeAsync(editor);
        var coarse = await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches");
        if (!coarse) Assert.Inconclusive("Chromium did not report (pointer: coarse) for the touch context; the larger hit rects cannot be asserted here.");

        var hit = editor.Locator(".tm-wd-selection [data-handle-hit='se']");
        Assert.AreEqual(1, await hit.CountAsync(), "the south-east handle has an invisible finger-sized hit rect on a coarse pointer");
        var box = (await hit.BoundingBoxAsync())!;
        Assert.IsTrue(box.Width >= 23 && box.Height >= 23, $"hit area >= 24 screen px, was {box.Width:0.#}x{box.Height:0.#}");

        var (hx, hy) = (box.X + box.Width / 2, box.Y + box.Height / 2);
        await DragAsync(session, (hx, hy), (hx + 100, hy + 50));

        await page.WaitForFunctionAsync("w => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); return parseFloat(g.getAttribute('data-w')) > w + 10; }",
            sizeBefore.W, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var after = await ElementSizeAsync(editor);
        Assert.IsTrue(after.W > sizeBefore.W + 10 && after.H > sizeBefore.H, $"grew from {sizeBefore.W}x{sizeBefore.H} to {after.W}x{after.H}");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("Two fingers moving apart pinch-zoom the canvas around the fingers; the toolbar zoom read-out follows")]
    public async Task Touch_PinchZooms(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var (x, y) = await EmptyCanvasPointAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        var widthBefore = await ViewBoxWidthAsync(editor);
        var zoomBefore = await ZoomLabelAsync(editor);

        await CdpTouchAsync(session, "touchStart", (1, x - 40, y), (2, x + 40, y));
        for (var i = 1; i <= 8; i++)
            await CdpTouchAsync(session, "touchMove", (1, x - 40 - 12 * i, y), (2, x + 40 + 12 * i, y));
        await CdpTouchAsync(session, "touchEnd");

        await page.WaitForFunctionAsync("w => { const s = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg'); return s.viewBox.baseVal.width < w - 1; }",
            widthBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.IsTrue(await ViewBoxWidthAsync(editor) < widthBefore, "the viewBox shrank = zoomed in");
        await page.WaitForFunctionAsync("z => parseFloat(document.querySelector('.tm-wd-editor__zoom-label').textContent) > z + 1",
            zoomBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("Two fingers moving together pan the canvas without changing the zoom")]
    public async Task Touch_TwoFingerPan(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var (x, y) = await EmptyCanvasPointAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        var before = await ViewBoxAsync(editor);
        var zoomBefore = await CanvasScaleAsync(editor);

        await CdpTouchAsync(session, "touchStart", (1, x - 30, y), (2, x + 30, y));
        for (var i = 1; i <= 8; i++)
            await CdpTouchAsync(session, "touchMove", (1, x - 30 + 10 * i, y + 5 * i), (2, x + 30 + 10 * i, y + 5 * i));
        await CdpTouchAsync(session, "touchEnd");

        await page.WaitForFunctionAsync("x => { const s = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg'); return Math.abs(s.viewBox.baseVal.x - x) > 1; }",
            before.X, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var after = await ViewBoxAsync(editor);
        // The zoom, not the raw view box width: the pinch first makes the view box match the svg's aspect (preserveAspectRatio=meet letterboxes it),
        // which widens W without changing what the user sees - and the wider tablet canvas has another aspect than the old docked one.
        Assert.AreEqual(zoomBefore, await CanvasScaleAsync(editor), 0.01, "a pan keeps the zoom");
        Assert.IsTrue(after.X < before.X, "fingers moved right, so the content followed and the viewBox origin moved left");
        Assert.IsTrue(after.Y < before.Y, "fingers moved down, so the viewBox origin moved up");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("A 500 ms hold on an element opens the context menu beside the finger; the lifting finger does not activate an item")]
    public async Task Touch_LongPressOpensContextMenu(int width, int height)
    {
        var (page, editor) = await OpenWithElementAsync(width, height);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        var elementsBefore = await editor.Locator("g[data-el-id]").CountAsync();

        await CdpTouchAsync(session, "touchStart", (1, cx, cy));
        await page.WaitForTimeoutAsync(800);
        await CdpTouchAsync(session, "touchEnd");

        var menu = page.Locator(".tm-wd-editor__context-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(1, await page.Locator(".tm-wd-editor__context-menu").CountAsync(), "exactly one menu (the native touch contextmenu must not open a second)");
        var box = (await menu.BoundingBoxAsync())!;
        Assert.IsTrue(box.X >= cx || box.Y >= cy, "the menu is parked beside the finger, not under it");
        Assert.AreEqual(elementsBefore, await editor.Locator("g[data-el-id]").CountAsync(), "the release click did not run a menu item (e.g. Delete)");
        var items = await menu.Locator("[role='menuitem']").AllInnerTextsAsync();
        TestContext.WriteLine("menu items: " + string.Join(" | ", items));
        Assert.IsTrue(items.Any(i => i.Contains("Delete", StringComparison.OrdinalIgnoreCase)), "the element menu offers Delete; items: " + string.Join(" | ", items));

        // Tapping elsewhere (on the canvas, away from the menu and the element) closes it.
        var svgBox = (await editor.Locator(".tm-wd-canvas__svg").BoundingBoxAsync())!;
        await TapAsync(session, svgBox.X + svgBox.Width * 0.12, svgBox.Y + svgBox.Height * 0.85);
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768)]
    [DataRow(1366, 1024)]
    [Description("Tapping a toolbox tile adds an element (HTML5 drag-and-drop does not run on touch)")]
    public async Task Touch_TapToolboxAddsElement(int width, int height)
    {
        var (page, editor) = await OpenAsync(width, height, touch: true);
        var before = await editor.Locator("g[data-el-id]").CountAsync();
        await EnsureToolboxAsync(editor);
        var tile = editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First;
        await tile.ScrollIntoViewIfNeededAsync();

        await tile.TapAsync();

        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a second finger landing in the middle of a drag rolls the drag back (the pinch takes over); nothing is committed")]
    public async Task Touch_SecondFingerMidDrag_RollsTheDragBack()
    {
        var (page, editor) = await OpenWithElementAsync(1024, 768);
        var before = await ElementPositionAsync(editor);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        var (ex, ey) = await EmptyCanvasPointAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        var elementsBeforeHandOver = await editor.Locator("g[data-el-id]").CountAsync();

        await CdpTouchAsync(session, "touchStart", (1, cx, cy));
        await CdpTouchAsync(session, "touchMove", (1, cx + 20, cy));
        await CdpTouchAsync(session, "touchMove", (1, cx + 60, cy));
        await CdpTouchAsync(session, "touchStart", (1, cx + 60, cy), (2, ex, ey));
        await CdpTouchAsync(session, "touchMove", (1, cx + 60, cy), (2, ex + 30, ey));
        await CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(500);
        TestContext.WriteLine($"elements after the hand-over: {await editor.Locator("g[data-el-id]").CountAsync()} (was {elementsBeforeHandOver}); url {page.Url}");

        var after = await ElementPositionAsync(editor);
        Assert.AreEqual(before.X, after.X, 0.5, "the dragged element went back to where it started");
        Assert.AreEqual(before.Y, after.Y, 0.5);
        // The undo stack top must be the toolbox INSERT, not a move: one Ctrl+Z removes the element. (Had the rolled-back drag
        // been committed, that Ctrl+Z would only have reverted the move and the element would still be there.)
        await page.Keyboard.PressAsync("Control+z");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: Escape during a mouse drag restores the element and commits nothing")]
    public async Task Mouse_EscapeDuringDrag_RestoresTheElement()
    {
        var (page, editor) = await OpenWithElementAsync(1366, 1024, touch: false);
        var before = await ElementPositionAsync(editor);
        var (cx, cy) = await ElementCentreAsync(page, editor);

        await page.Mouse.MoveAsync((float)cx, (float)cy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(cx + 80), (float)(cy + 40), new MouseMoveOptions { Steps = 6 });
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var after = await ElementPositionAsync(editor);
        Assert.AreEqual(before.X, after.X, 0.5);
        Assert.AreEqual(before.Y, after.Y, 0.5);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a finger on a locked element selects and drags nothing")]
    public async Task Touch_LockedElement_IsNotMoved()
    {
        var (page, editor) = await OpenWithElementAsync(1024, 768);
        var before = await ElementPositionAsync(editor);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        await TapAsync(session, cx, cy);
        await editor.Locator(".tm-wd-selection").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        await page.Keyboard.PressAsync("Control+Shift+L");   // lock
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').getAttribute('data-locked') === 'true'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        await DragAsync(session, (cx, cy), (cx + 120, cy + 60));
        await page.WaitForTimeoutAsync(400);

        var after = await ElementPositionAsync(editor);
        Assert.AreEqual(before.X, after.X, 0.5, "a locked element does not move");
        Assert.AreEqual(before.Y, after.Y, 0.5);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: the arrow keys nudge the selection (Shift = one grid cell) - the non-drag alternative to dragging")]
    public async Task Keyboard_ArrowsNudgeTheSelection()
    {
        var (page, editor) = await OpenWithElementAsync(1366, 1024, touch: false);
        var (cx, cy) = await ElementCentreAsync(page, editor);
        await page.Mouse.ClickAsync((float)cx, (float)cy);
        await editor.Locator(".tm-wd-selection").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        var before = await ElementPositionAsync(editor);

        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        await page.WaitForFunctionAsync("x => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); " +
            "const m = /translate\\(([-\\d.]+),/.exec(g.getAttribute('transform')); return Math.abs(parseFloat(m[1]) - x) > 0.5; }",
            before.X, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var after = await ElementPositionAsync(editor);
        Assert.AreEqual(before.X + 1, after.X, 0.5, "plain arrow = 1 unit");
        Assert.AreEqual(before.Y + 8, after.Y, 0.5, "Shift+arrow = one 8px grid cell");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: the canvas claims the touch (touch-action: none) while the page outside it still scrolls")]
    public async Task Touch_CanvasClaimsTheTouch_ButThePageStillScrolls()
    {
        var (page, editor) = await OpenWithElementAsync(1024, 768);

        Assert.AreEqual("none", await editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("e => getComputedStyle(e).touchAction"));
        var outside = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('main') ?? document.body).touchAction");
        Assert.AreNotEqual("none", outside, "only the canvas claims the touch");
    }

    // ── Mouse regression ──────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse regression: a drag still moves the element and its duration does not regress (Pointer Events + the shared helper)")]
    public async Task Mouse_DragTiming_DoesNotRegress()
    {
        var (page, editor) = await OpenWithElementAsync(1366, 1024, touch: false);
        var before = await ElementPositionAsync(editor);
        var (cx, cy) = await ElementCentreAsync(page, editor);

        var elapsed = await page.EvaluateAsync<double>("() => performance.now()");
        await page.Mouse.MoveAsync((float)cx, (float)cy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(cx + 160), (float)(cy + 80), new MouseMoveOptions { Steps = 40 });
        await page.Mouse.UpAsync();
        var spent = await page.EvaluateAsync<double>("t => performance.now() - t", elapsed);

        await page.WaitForFunctionAsync("x => { const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]'); " +
            "const m = /translate\\(([-\\d.]+),/.exec(g.getAttribute('transform')); return Math.abs(parseFloat(m[1]) - x) > 4; }",
            before.X, new PageWaitForFunctionOptions { Timeout = 10_000 });
        // 40 pointer moves + the commit round trip. Measured against the pre-phase-5 mouse code (git worktree at HEAD): 3.5-3.8 s
        // for the same drag on this machine (Playwright's own 40 synthetic moves dominate), 3.6-3.9 s with the pointer helper. The bound
        // is 2x that: a regression to a per-move Blazor round trip or a missed capture would blow it, load noise does not.
        Assert.IsTrue(spent < 8_000, $"a 40-step drag took {spent:0} ms (HEAD baseline 3.5-3.8 s)");
        TestContext.WriteLine($"mouse drag of 40 moves: {spent:0} ms");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse regression: the wheel zooms the canvas and a ctrl+wheel (trackpad pinch) zooms smoothly")]
    public async Task Mouse_WheelAndCtrlWheelZoom()
    {
        var (page, editor) = await OpenWithElementAsync(1366, 1024, touch: false);
        var (x, y) = await EmptyCanvasPointAsync(page, editor);
        var before = await ViewBoxWidthAsync(editor);
        await page.Mouse.MoveAsync((float)x, (float)y);

        await page.Mouse.WheelAsync(0, -300);
        await page.WaitForTimeoutAsync(250);
        var afterWheel = await ViewBoxWidthAsync(editor);
        Assert.IsTrue(afterWheel < before - 1, "wheel up zooms in");

        await page.Keyboard.DownAsync("Control");
        await page.Mouse.WheelAsync(0, 60);
        await page.Keyboard.UpAsync("Control");
        await page.WaitForTimeoutAsync(250);
        Assert.IsTrue(await ViewBoxWidthAsync(editor) > afterWheel + 1, "ctrl+wheel down zooms out");
    }

    // ── Screenshots / UX review ───────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Screenshots at 1024 (touch) with a selected element and the larger handles, plus the open context menu")]
    public async Task Screenshots_Tablet_1024Touch_SelectedElementAndMenu()
    {
        Directory.CreateDirectory(ShotDirectory);
        var (page, editor) = await OpenWithElementAsync(1024, 768);
        await editor.ScrollIntoViewIfNeededAsync();
        await page.Mouse.MoveAsync(0, 0);
        await ShotAsync(editor, "tablet-1024-touch--no-selection");

        var (cx, cy) = await ElementCentreAsync(page, editor);
        var session = await page.Context.NewCDPSessionAsync(page);
        await TapAsync(session, cx, cy);
        await editor.Locator(".tm-wd-selection").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);
        await ShotAsync(editor, "tablet-1024-touch--selected-fit-zoom");

        // The same selection with a comfortably sized element: the larger (1.5x) handles and the invisible 24 px hit areas are only
        // visible once the element is big enough on screen (the fit zoom of the demo page is ~30 %, so the default button is a speck).
        var fields = editor.Locator(".tm-wd-props__section-body--grid2 input");
        await fields.Nth(2).FillAsync("360");
        await fields.Nth(3).FillAsync("240");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').getAttribute('data-w') === '360'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(600);
        await ShotAsync(editor, "tablet-1024-touch--selected-large-handles");
        (cx, cy) = await ElementCentreAsync(page, editor);

        await CdpTouchAsync(session, "touchStart", (1, cx, cy));
        await page.WaitForTimeoutAsync(800);
        await CdpTouchAsync(session, "touchEnd");
        await page.Locator(".tm-wd-editor__context-menu").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
        await ShotPageAsync(page, "tablet-1024-touch--long-press-menu");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(IPage Page, ILocator Editor)> OpenAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = false,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type is "error" or "warning") TestContext.WriteLine($"[console.{message.Type}] {message.Text}"); };
        page.PageError += (_, error) => TestContext.WriteLine($"[pageerror] {error}");
        // The layout resolves from the editor's CONTAINER and a panel railed at a narrow first measurement stays a rail,
        // so the demo sidebar / page margin is removed BEFORE the app boots (not after).
        await page.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            document.addEventListener('DOMContentLoaded', () => {
                const style = document.createElement('style');
                style.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; } .tm-wd-editor__minimap-wrap { display: none !important; }';
                document.head.appendChild(style);
            });
            """);
        try
        {
            await page.GotoAsync($"{BaseUrl}{EditorUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
            await WaitForAppReadyAsync(page);
        }
        catch (TimeoutException)
        {
            // Cold boot of the self-hosted apps: one reload of the same page is enough once the app is up.
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
            await WaitForAppReadyAsync(page);
        }

        // The toolbox is docked on a desktop container but lives in an overlay drawer on a tablet one: wait for the canvas instead.
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(800);
        return (page, editor);
    }

    /// <summary>Opens the interactive editor and puts one button on the canvas (inserted through the toolbox: tap or click).</summary>
    private async Task<(IPage Page, ILocator Editor)> OpenWithElementAsync(int width, int height, bool touch = true)
    {
        var (page, editor) = await OpenAsync(width, height, touch);
        var existing = await editor.Locator("g[data-el-id]").CountAsync();
        if (existing == 0)
        {
            await EnsureToolboxAsync(editor);
            await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length >= 1",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        // Deselect and scroll the canvas into view so the first gesture starts from a known state. On a tablet container the selection
        // made by the insertion opened the inspector sheet: close it so the canvas has its full width, like the desktop layout.
        var sheetClose = editor.Locator("[data-testid='wd-sheet-close']");
        if (await sheetClose.CountAsync() > 0)
        {
            await sheetClose.First.ClickAsync();
            await editor.Locator(".tm-wd-sheet").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(800);   // the canvas re-measures (relayout) once the sheet is gone; read the view box only after that
        }

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        await editor.Locator(".tm-wd-canvas__svg").ScrollIntoViewIfNeededAsync();
        return (page, editor);
    }

    /// <summary>
    /// On a tablet editor container (768-1199 px, phase 6) the toolbox lives in an overlay drawer opened from the toolbar: open it when no
    /// toolbox tile is visible. On the desktop layout the docked toolbox is already there and nothing happens.
    /// </summary>
    private static async Task EnsureToolboxAsync(ILocator editor)
    {
        var tile = editor.Locator(".tm-wd-toolbox__item").First;
        if (await tile.CountAsync() > 0 && await tile.IsVisibleAsync()) return;
        await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
        await tile.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    private static async Task<(double X, double Y)> ElementCentreAsync(IPage page, ILocator editor)
    {
        var box = await editor.Locator("g[data-el-id]").First.Locator("svg").First.BoundingBoxAsync();
        Assert.IsNotNull(box, "the element has a box");
        return (box!.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>A canvas point whose horizontal neighbourhood (+-100 px) hits no element, inside the visible svg.</summary>
    private static async Task<(double X, double Y)> EmptyCanvasPointAsync(IPage page, ILocator editor)
    {
        var point = await editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<double[]?>(
            """
            svg => {
                const r = svg.getBoundingClientRect();
                const busy = (x, y) => { const el = document.elementFromPoint(x, y); return !el || !svg.contains(el) || !!el.closest('[data-el-id], [data-connector-id], .tm-wd-selection'); };
                for (let fy = 0.9; fy > 0.2; fy -= 0.05)
                    for (let fx = 0.5; fx < 0.9; fx += 0.05) {
                        const x = r.left + r.width * fx, y = r.top + r.height * fy;
                        if (![-100, -50, 0, 50, 100].some(d => busy(x + d, y))) return [x, y];
                    }
                return null;
            }
            """);
        Assert.IsNotNull(point, "no empty canvas strip to put two fingers on");
        return (point![0], point[1]);
    }

    private static async Task<(double X, double Y)> ElementPositionAsync(ILocator editor)
    {
        var xy = await editor.Locator("g[data-el-id]").First.EvaluateAsync<double[]>(
            "g => { const m = /translate\\(([-\\d.]+),\\s*([-\\d.]+)\\)/.exec(g.getAttribute('transform')); return [parseFloat(m[1]), parseFloat(m[2])]; }");
        return (xy[0], xy[1]);
    }

    private static async Task<(double W, double H)> ElementSizeAsync(ILocator editor)
    {
        var wh = await editor.Locator("g[data-el-id]").First.EvaluateAsync<double[]>(
            "g => [parseFloat(g.getAttribute('data-w')), parseFloat(g.getAttribute('data-h'))]");
        return (wh[0], wh[1]);
    }

    private static Task<double> ViewBoxWidthAsync(ILocator editor) =>
        editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<double>("s => s.viewBox.baseVal.width");

    private static async Task<(double X, double Y, double W, double H)> ViewBoxAsync(ILocator editor)
    {
        var v = await editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<double[]>(
            "s => [s.viewBox.baseVal.x, s.viewBox.baseVal.y, s.viewBox.baseVal.width, s.viewBox.baseVal.height]");
        return (v[0], v[1], v[2], v[3]);
    }

    /// <summary>Screen pixels per svg unit (the canvas fits the page into the viewport, so it is not 1).</summary>
    private static Task<double> CanvasScaleAsync(ILocator editor) =>
        editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<double>(
            "s => Math.min(s.getBoundingClientRect().width / s.viewBox.baseVal.width, s.getBoundingClientRect().height / s.viewBox.baseVal.height)");

    private static async Task<double> ZoomLabelAsync(ILocator editor) =>
        await editor.Locator(".tm-wd-editor__zoom-label").EvaluateAsync<double>("e => parseFloat(e.textContent)");

    /// <summary>Real CDP touch: every listed point is down; an empty list lifts all fingers.</summary>
    private static Task CdpTouchAsync(ICDPSession session, string type, params (int id, double x, double y)[] points)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(p => (object)new Dictionary<string, object>
                { ["x"] = p.x, ["y"] = p.y, ["id"] = p.id }).ToArray(),
        });

    private static async Task TapAsync(ICDPSession session, double x, double y)
    {
        await CdpTouchAsync(session, "touchStart", (1, x, y));
        await CdpTouchAsync(session, "touchEnd");
    }

    private static async Task DragAsync(ICDPSession session, (double X, double Y) from, (double X, double Y) to, int steps = 10)
    {
        await CdpTouchAsync(session, "touchStart", (1, from.X, from.Y));
        for (var i = 1; i <= steps; i++)
            await CdpTouchAsync(session, "touchMove", (1, from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps));
        await CdpTouchAsync(session, "touchEnd");
    }

    private async Task ShotAsync(ILocator locator, string name)
    {
        var path = Path.Combine(ShotDirectory, name + ".png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private async Task ShotPageAsync(IPage page, string name)
    {
        var path = Path.Combine(ShotDirectory, name + ".png");
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
