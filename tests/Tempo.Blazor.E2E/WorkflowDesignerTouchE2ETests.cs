using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F23 lane: TmWorkflowDesignerCanvas on the shared pointer helpers. A mouse (1440) and a real
/// finger (CDP touch, 768 and 390) drag states, pan the empty canvas and pinch-zoom with two
/// touchPoints; Escape rolls a drag back; a port drag creates a transition; the keyboard reaches
/// a state by Tab and moves it with Alt+ArrowRight (no Back navigation — the chord is cancelled).
/// Diagnostic shots go to gitignored TestResults, never a committed baseline.
/// </summary>
[TestClass]
public class WorkflowDesignerTouchE2ETests : WasmTestBase
{
    /// <summary>
    /// The canvas svg is a DIRECT child of the canvas div. A bare descendant selector
    /// (<c>.tm-wf-canvas svg</c>) also catches the zoom buttons' icon svgs AND the second
    /// (read-only) designer further down the demo page — a strict-mode violation.
    /// </summary>
    private const string Canvas = ".tm-wf-canvas > svg";

    /// <summary>The demo page renders two designers (interactive + read-only); target the first.</summary>
    private static ILocator CanvasSvg(IPage page) => page.Locator(Canvas).First;

    private static ILocator FirstCanvas(IPage page) => page.Locator(".tm-wf-canvas").First;

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "workflow-designer");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private async Task ShotAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}.png"), FullPage = false });
    }

    private async Task<IPage> OpenDesignerAsync(bool touch, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/workflow-designer", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var canvas = CanvasSvg(page);
        await canvas.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.Locator("[data-state-id='draft']").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await canvas.ScrollIntoViewIfNeededAsync();
        // A realistic pointer start: the mouse parks at a neutral spot before any gesture begins.
        await page.Mouse.MoveAsync(10, 10);
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static async Task<string> TransformAsync(IPage page, string stateId)
        => (await page.Locator($"[data-state-id='{stateId}']").GetAttributeAsync("transform")) ?? string.Empty;

    private static async Task<string> ViewBoxAsync(IPage page)
        => (await CanvasSvg(page).GetAttributeAsync("viewBox")) ?? string.Empty;

    private static async Task<(float X, float Y)> StateCentreAsync(IPage page, string stateId)
    {
        var box = (await page.Locator($"[data-state-id='{stateId}']").BoundingBoxAsync())!;
        return (box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }

    // ── mouse at 1440 ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mouse_NodeDrag_FollowsThePointer_AndCommitsSnapped()
    {
        var page = await OpenDesignerAsync(touch: false, 1440, 900);
        var before = await TransformAsync(page, "draft");
        var (x, y) = await StateCentreAsync(page, "draft");

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + 30, y + 36, new MouseMoveOptions { Steps = 8 });
        var live = await TransformAsync(page, "draft");
        Assert.AreNotEqual(before, live, "the node follows the pointer live");

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(300);
        // The diagnostic shot runs AFTER the commit: a mid-drag shot parks the mouse at (0, 0),
        // and the pointer-up there would commit the drag at the viewport origin.
        await ShotAsync(page, "wf-mouse-dragging-1440");

        var committed = await TransformAsync(page, "draft");
        Assert.AreNotEqual(before, committed, "Blazor re-rendered the committed position");
        var coords = committed.Replace("translate(", string.Empty).Replace(")", string.Empty).Split(',');
        Assert.AreEqual(60, int.Parse(coords[0].Trim()), "the committed x snaps to the 20px grid");
        Assert.AreEqual(240, int.Parse(coords[1].Trim()), "the committed y snaps to the 20px grid");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mouse_Escape_RollsTheDragBack_WithoutCommitting()
    {
        var page = await OpenDesignerAsync(touch: false, 1440, 900);
        var before = await TransformAsync(page, "draft");
        var (x, y) = await StateCentreAsync(page, "draft");

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + 60, y + 40, new MouseMoveOptions { Steps = 8 });
        Assert.AreNotEqual(before, await TransformAsync(page, "draft"), "precondition: dragging");
        await page.Keyboard.PressAsync("Escape");
        await ShotAsync(page, "wf-mouse-escape-rollback");
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(before, await TransformAsync(page, "draft"), "Escape restores the exact start transform");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mouse_PortDrag_ToAnotherState_CreatesATransition()
    {
        var page = await OpenDesignerAsync(touch: false, 1440, 900);
        var transitionsBefore = await FirstCanvas(page).Locator(".tm-wf-transition").CountAsync();

        var port = page.Locator("[data-port='draft']");
        var portBox = (await port.BoundingBoxAsync())!;
        var (tx, ty) = await StateCentreAsync(page, "review");

        await page.Mouse.MoveAsync(portBox.X + portBox.Width / 2f, portBox.Y + portBox.Height / 2f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(tx, ty, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);
        // Shot after the drop: a mid-drag shot would park the mouse (and the drop point) at (0, 0).
        await ShotAsync(page, "wf-mouse-port-drag");

        Assert.IsTrue(await FirstCanvas(page).Locator(".tm-wf-transition").CountAsync() > transitionsBefore,
            "dropping the port on another state creates a transition");
    }

    // ── touch (CDP) at 768 and 390 ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(768)]
    [DataRow(390)]
    public async Task Touch_NodeDrag_And_EmptyCanvasPan_Work(int width)
    {
        var page = await OpenDesignerAsync(touch: true, width, 900);
        var before = await TransformAsync(page, "draft");
        var (x, y) = await StateCentreAsync(page, "draft");
        var session = await page.Context.NewCDPSessionAsync(page);

        // A finger drags the state (no long-press: the canvas owns its surface).
        await TouchAsync(session, "touchStart", x, y);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x + 40 * i / 6.0, y + 20 * i / 6.0);
        }
        Assert.AreNotEqual(before, await TransformAsync(page, "draft"), "the node follows the finger live");
        await ShotAsync(page, $"wf-touch-dragging-{width}");
        await TouchAsync(session, "touchEnd", x + 40, y + 20);
        await page.WaitForTimeoutAsync(300);
        Assert.AreNotEqual(before, await TransformAsync(page, "draft"), "the drag commits at this width");

        // A one-finger drag on the EMPTY canvas pans the viewBox.
        var viewBoxBefore = await ViewBoxAsync(page);
        var box = (await CanvasSvg(page).BoundingBoxAsync())!;
        var px = box.X + box.Width * 0.5;
        var py = box.Y + box.Height * 0.8;
        await TouchAsync(session, "touchStart", px, py);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", px - 30 * i / 6.0, py - 20 * i / 6.0);
        }
        await TouchAsync(session, "touchEnd", px - 30, py - 20);
        await page.WaitForTimeoutAsync(300);

        Assert.AreNotEqual(viewBoxBefore, await ViewBoxAsync(page), "the one-finger pan changed the viewBox");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Touch_PinchZoom_WithTwoTouchPoints_ShinksTheViewBox_AndHasZoomButtons()
    {
        var page = await OpenDesignerAsync(touch: true, 768, 900);
        var viewBoxBefore = await ViewBoxAsync(page);

        // The single-pointer alternative ships next to the gesture (WCAG 2.5.7).
        Assert.AreEqual(2, await FirstCanvas(page).Locator(".tm-wf-zoom-overlay button").CountAsync(), "zoom buttons exist");

        var box = (await CanvasSvg(page).BoundingBoxAsync())!;
        var cx = box.X + box.Width / 2f;
        var cy = box.Y + box.Height / 2f;
        var session = await page.Context.NewCDPSessionAsync(page);

        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new object[]
            {
                new Dictionary<string, object> { ["x"] = cx - 40, ["y"] = cy, ["id"] = 1 },
                new Dictionary<string, object> { ["x"] = cx + 40, ["y"] = cy, ["id"] = 2 },
            },
        });
        for (var i = 1; i <= 6; i++)
        {
            var spread = 40 + 30 * i; // 40 -> 220 px apart: scale 2.75
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new object[]
                {
                    new Dictionary<string, object> { ["x"] = cx - spread, ["y"] = cy, ["id"] = 1 },
                    new Dictionary<string, object> { ["x"] = cx + spread, ["y"] = cy, ["id"] = 2 },
                },
            });
        }
        Assert.IsTrue((await ViewBoxWidthAsync(page)) < 800, "the spread zooms in live");

        await ShotAsync(page, "wf-touch-pinch-768");
        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>(),
        });
        await page.WaitForTimeoutAsync(300);

        var after = await ViewBoxAsync(page);
        Assert.AreNotEqual(viewBoxBefore, after, "the pinch committed the new viewBox");
        Assert.IsTrue(await ViewBoxWidthAsync(page) < 800, "the committed viewBox is zoomed in");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("J1: a pinch whose first finger moved before the second landed still zooms (the pan hand-over must not roll the pinch back)")]
    public async Task Touch_PinchZoom_AfterFirstFingerMoved_StillZooms()
    {
        var page = await OpenDesignerAsync(touch: true, 768, 900);
        var viewBoxBefore = await ViewBoxAsync(page);

        Assert.AreEqual(2, await FirstCanvas(page).Locator(".tm-wf-zoom-overlay button").CountAsync(), "zoom buttons exist");

        var box = (await CanvasSvg(page).BoundingBoxAsync())!;
        var cx = box.X + box.Width / 2f;
        var cy = box.Y + box.Height / 2f;
        var session = await page.Context.NewCDPSessionAsync(page);

        // Finger one lands alone and moves 2 px: its pointer capture goes live on the canvas,
        // the element the pinch is attached to as well.
        await TouchAsync(session, "touchStart", cx - 40, cy, id: 1);
        await TouchAsync(session, "touchMove", cx - 38, cy, id: 1);

        // The second finger joins with BOTH touchPoints listed.
        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new object[]
            {
                new Dictionary<string, object> { ["x"] = cx - 38, ["y"] = cy, ["id"] = 1 },
                new Dictionary<string, object> { ["x"] = cx + 40, ["y"] = cy, ["id"] = 2 },
            },
        });
        for (var i = 1; i <= 6; i++)
        {
            var spread = 40 + 30 * i; // 40 -> 220 px apart: scale 2.75
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new object[]
                {
                    new Dictionary<string, object> { ["x"] = cx - spread, ["y"] = cy, ["id"] = 1 },
                    new Dictionary<string, object> { ["x"] = cx + spread, ["y"] = cy, ["id"] = 2 },
                },
            });
        }
        Assert.IsTrue((await ViewBoxWidthAsync(page)) < 800, "the spread zooms in live despite the moving first finger");

        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>(),
        });
        await page.WaitForTimeoutAsync(300);

        var after = await ViewBoxAsync(page);
        Assert.AreNotEqual(viewBoxBefore, after, "the pinch committed the new viewBox");
        Assert.IsTrue(await ViewBoxWidthAsync(page) < 800, "the committed viewBox is zoomed in");
    }

    private static async Task<double> ViewBoxWidthAsync(IPage page)
    {
        var vb = await ViewBoxAsync(page);
        var parts = vb.Split(' ');
        return double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Real CDP touch with a distinct pointer id (the pinch uses two touchPoints).</summary>
    private static Task TouchAsync(ICDPSession session, string type, double x, double y, double id = 1)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = id } },
        });

    // ── keyboard ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Keyboard_TabToState_AltArrowMoves_WithAnnouncement_AndNoBackNavigation()
    {
        var page = await OpenDesignerAsync(touch: false, 1440, 900);
        var urlBefore = page.Url;
        var before = await TransformAsync(page, "draft");

        // Tab reaches the states (roving tabindex); Alt+ArrowRight moves the node one grid step.
        await page.Locator("[data-state-id='draft']").FocusAsync();
        var role = await page.Locator("[data-state-id='draft']").GetAttributeAsync("role");
        Assert.AreEqual("button", role, "the state is a named, focusable button");
        Assert.AreEqual("0", await page.Locator("[data-state-id='draft']").GetAttributeAsync("tabindex"),
            "the first state is the roving tab stop");

        await page.Keyboard.PressAsync("Alt+ArrowRight");
        await page.WaitForTimeoutAsync(300);

        // The keyboard commit shares the drag's snap path: the demo's Draft starts OFF-grid at
        // x = 30, one Alt+ArrowRight step lands on 50, and the commit snaps it to the 20 px grid.
        Assert.AreEqual("translate(40, 200)", await TransformAsync(page, "draft"),
            $"Alt+ArrowRight moved the node one 20px grid step, snapped on commit (before: {before})");
        var announcement = await FirstCanvas(page).Locator("[role='status']").InnerTextAsync();
        Assert.IsTrue(announcement.Contains("40, 200"),
            $"the live region announces the committed position ({announcement})");
        Assert.AreEqual(urlBefore, page.Url, "Alt+Arrow never navigated back");

        // The Shift+F10 menu is the keyboard route to every pointer-only command: the first item
        // takes the focus, and Escape closes the menu and returns the focus to the state.
        await page.Keyboard.PressAsync("Shift+F10");
        await page.WaitForTimeoutAsync(300);
        var menu = page.Locator(".tm-wf-context-menu");
        Assert.IsTrue(await menu.IsVisibleAsync(), "the context menu opens from the keyboard");
        var focusedClass = await page.EvaluateAsync<string>(
            "() => document.activeElement ? document.activeElement.className : ''");
        Assert.IsTrue(focusedClass.Contains("tm-wf-context-menu__item"),
            $"the first menu item owns the focus (active element: '{focusedClass}')");

        await ShotAsync(page, "wf-keyboard-menu-1440");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(0, await menu.CountAsync(), "Escape closes the menu");
        var focusedState = await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.getAttribute"
            + " ? (document.activeElement.getAttribute('data-state-id') || '') : ''");
        Assert.AreEqual("draft", focusedState, "Escape returns the focus to the originating state");
    }
}
