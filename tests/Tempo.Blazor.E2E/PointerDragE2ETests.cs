using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F7 lane: the shared pointer-drag helper on the REAL components. The scheduler's event resize runs
/// through scheduler.js -> pointer-drag.js with a mouse, a finger (real touch emulation, CDP touch
/// events - the B13 bug was that none of this worked) and a pen-typed pointer; Escape and a
/// pointercancel roll the resize back without calling .NET. The editor-shell panel resizer (migrated
/// onto the same helper) is driven with a real mouse drag and a SECOND shell on the page proves
/// two instances do not share a gesture. Screenshots are viewport-only at the real viewport height.
/// </summary>
[TestClass]
public class PointerDragE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "pointer-drag");

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
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}.png"), FullPage = false });
    }

    private async Task<IPage> OpenSchedulerDayAsync(bool touch, int width, int height)
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
        await page.GotoAsync($"{BaseUrl}/scheduler", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var host = page.Locator("[data-testid='scheduler-interactive']");
        await host.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await host.ScrollIntoViewIfNeededAsync();
        // The Day view has the "Resize Test Event" today at 14:00-15:00 with both handles.
        await host.Locator("[data-view='Day']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='scheduler-interactive'] .tm-scheduler-timegrid-event[data-event-id='i4']");
        await page.EvaluateAsync("() => document.querySelector(\"[data-event-id='i4']\").scrollIntoView({ block: 'center', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static ILocator Event(IPage page) => page.Locator("[data-testid='scheduler-interactive'] [data-event-id='i4']");

    private static async Task<double> EventHeightAsync(IPage page)
        => await page.EvaluateAsync<double>("() => document.querySelector(\"[data-event-id='i4']\").getBoundingClientRect().height");

    private static async Task<string> LastActionAsync(IPage page)
        => (await page.Locator("[data-testid='scheduler-last-action']").InnerTextAsync()).Trim();

    private static async Task<(float X, float Y)> BottomHandleCenterAsync(IPage page)
    {
        var box = (await Event(page).Locator(".tm-scheduler-event-resize-handle--bottom").BoundingBoxAsync())!;
        return (box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }

    private async Task<int> SlotPixelsAsync(IPage page)
    {
        // One hour is 60 minutes; the demo's default slot is 30 minutes: derive px per slot from the grid.
        var hourPx = await page.EvaluateAsync<double>("() => { const g = document.querySelector('.tm-scheduler-timegrid-body'); return g.getBoundingClientRect().height / 24; }");
        return (int)Math.Round(hourPx / 2);
    }

    // ── mouse ───────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Mouse_ResizeBottomHandle_FollowsThePointer_AndNotifiesOnce()
    {
        var page = await OpenSchedulerDayAsync(touch: false, 1440, 900);
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var (x, y) = await BottomHandleCenterAsync(page);

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + slot * 2f, new MouseMoveOptions { Steps = 8 });
        var during = await EventHeightAsync(page);
        Assert.IsTrue(during > before + slot, $"the event follows the pointer live ({before} -> {during})");
        Assert.AreEqual("true", await Event(page).GetAttributeAsync("data-resizing"));
        await ShotAsync(page, "scheduler-mouse-resizing");
        await page.Mouse.UpAsync();

        await Assertions.Expect(Event(page)).Not.ToHaveAttributeAsync("data-resizing", "true");
        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']")).ToContainTextAsync("Resize Test Event");
        var after = await EventHeightAsync(page);
        Assert.IsTrue(after > before + slot, $"Blazor re-rendered the committed size ({before} -> {after})");
        await ShotAsync(page, "scheduler-mouse-resized");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Mouse_Escape_RollsTheResizeBack_WithoutNotifying()
    {
        var page = await OpenSchedulerDayAsync(touch: false, 1440, 900);
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var action = await LastActionAsync(page);
        var (x, y) = await BottomHandleCenterAsync(page);

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + slot * 3f, new MouseMoveOptions { Steps = 8 });
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, "precondition: the drag changed the live size");
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();

        Assert.AreEqual(before, await EventHeightAsync(page), 1.0, "Escape restores the original size");
        Assert.AreEqual(action, await LastActionAsync(page), ".NET was not notified");
        await Assertions.Expect(Event(page)).Not.ToHaveAttributeAsync("data-resizing", "true");
        Assert.AreEqual("true", await Event(page).GetAttributeAsync("draggable"), "HTML5 drag is back on after the cancel");
    }

    // ── touch (the B13 bug) ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Touch_ResizeBottomHandle_Works_OnATablet()
    {
        var page = await OpenSchedulerDayAsync(touch: true, 1024, 800);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "precondition: coarse pointer emulation");
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var handleBox = (await Event(page).Locator(".tm-scheduler-event-resize-handle--bottom").BoundingBoxAsync())!;
        Assert.IsTrue(handleBox.Height >= 10, $"the handle grows on a coarse pointer ({handleBox.Height}px)");
        Assert.AreEqual("pan-y", await Event(page).Locator(".tm-scheduler-event-resize-handle--bottom").EvaluateAsync<string>("e => getComputedStyle(e).touchAction"),
            "a coarse handle pans vertically; the long-press, not touch-action, starts the resize");

        var session = await page.Context.NewCDPSessionAsync(page);
        var x = handleBox.X + handleBox.Width / 2;
        var y = handleBox.Y + handleBox.Height / 2;
        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450); // touch resize waits out the 400ms long-press
        for (var i = 1; i <= 8; i++)
        {
            await TouchAsync(session, "touchMove", x, y + slot * 2 * i / 8.0);
        }
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, "the event follows the finger live");
        await ShotAsync(page, "scheduler-touch-resizing-1024");
        await TouchAsync(session, "touchEnd", x, y + slot * 2);

        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']")).ToContainTextAsync("Resize Test Event");
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, "the committed size is rendered");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "touch emulation survived the shot");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Touch_PointerCancel_RestoresTheEvent_WithoutNotifying()
    {
        var page = await OpenSchedulerDayAsync(touch: true, 1024, 800);
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var action = await LastActionAsync(page);
        var box = (await Event(page).Locator(".tm-scheduler-event-resize-handle--bottom").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450); // the long-press must fire before the drag, or the swipe is a scroll
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x, y + slot * 2 * i / 6.0);
        }
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, "precondition: resizing");
        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchCancel", ["touchPoints"] = Array.Empty<object>() });

        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(before, await EventHeightAsync(page), 1.0, "pointercancel restores the original size");
        Assert.AreEqual(action, await LastActionAsync(page));
        await Assertions.Expect(Event(page)).Not.ToHaveAttributeAsync("data-resizing", "true");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Touch_ResizeAt390_StillResizes_AndThePageStillScrolls()
    {
        var page = await OpenSchedulerDayAsync(touch: true, 390, 800);
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var box = (await Event(page).Locator(".tm-scheduler-event-resize-handle--bottom").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x, y + slot * 2 * i / 6.0);
        }
        await ShotAsync(page, "scheduler-touch-resizing-390");
        await TouchAsync(session, "touchEnd", x, y + slot * 2);
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, "the event resized at 390 wide");

        // A swipe that starts on the event BODY (not the handle) must still scroll the page: the helper
        // sets touch-action only on the handle.
        var bodyAction = await Event(page).EvaluateAsync<string>("e => getComputedStyle(e).touchAction");
        Assert.AreNotEqual("none", bodyAction, "the event body keeps native scrolling");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"));
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390)]
    [DataRow(768)]
    public async Task Scheduler_Touch_SwipeFromEventCentre_Scrolls_AndDoesNotResize(int width)
    {
        // J3: a swipe that starts in the middle of a 1h event, and of a 30-min event, must scroll.
        // Chromium snaps that finger onto a handle; the long-press is what keeps it a scroll.
        var page = await OpenSchedulerDayAsync(touch: true, width, 800);
        await page.Locator("[data-event-id='i5']").WaitForAsync();
        var action = await LastActionAsync(page);
        var session = await page.Context.NewCDPSessionAsync(page);

        foreach (var id in new[] { "i4", "i5" })
        {
            var before = await page.EvaluateAsync<double>($"() => document.querySelector(\"[data-event-id='{id}']\").getBoundingClientRect().height");
            var box = (await page.Locator($"[data-event-id='{id}']").BoundingBoxAsync())!;
            var scrollBefore = await page.EvaluateAsync<double>("() => window.scrollY");
            await TouchAsync(session, "touchStart", box.X + box.Width / 2, box.Y + box.Height / 2);
            for (var i = 1; i <= 8; i++)
            {
                await TouchAsync(session, "touchMove", box.X + box.Width / 2, box.Y + box.Height / 2 - 40.0 * i);
            }
            await TouchAsync(session, "touchEnd", box.X + box.Width / 2, box.Y + box.Height / 2 - 320);
            var scrollAfter = await page.EvaluateAsync<double>("() => window.scrollY");
            Assert.AreNotEqual(scrollBefore, scrollAfter, $"{width}px: a centre swipe on {id} must scroll");
            var after = await page.EvaluateAsync<double>($"() => document.querySelector(\"[data-event-id='{id}']\").getBoundingClientRect().height");
            Assert.AreEqual(before, after, 1.0, $"{width}px: {id} was resized by a swipe");
        }

        Assert.AreEqual(action, await LastActionAsync(page), "scheduler-last-action is unchanged");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390)]
    [DataRow(768)]
    public async Task Scheduler_Touch_HoldThenDrag_ResizesAndCommits(int width)
    {
        var page = await OpenSchedulerDayAsync(touch: true, width, 800);
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var box = (await Event(page).Locator(".tm-scheduler-event-resize-handle--bottom").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x, y + slot * 2 * i / 6.0);
        }
        await TouchAsync(session, "touchEnd", x, y + slot * 2);

        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']")).ToContainTextAsync("Resize Test Event");
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, $"hold-then-drag resizes at {width}");
    }

    // ── pen ─────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Pen_ResizeBottomHandle_Works()
    {
        var page = await OpenSchedulerDayAsync(touch: false, 1440, 900);
        var before = await EventHeightAsync(page);
        var slot = await SlotPixelsAsync(page);
        var (x, y) = await BottomHandleCenterAsync(page);

        // Chromium has no pen emulation in Playwright: dispatch pen-typed pointer events at the handle
        // exactly as the browser would (the helper keys off pointerType/pointerId only).
        await page.EvaluateAsync(@"({ x, y, dy }) => {
            const handle = document.querySelector(""[data-event-id='i4'] .tm-scheduler-event-resize-handle--bottom"");
            const fire = (type, py, buttons) => handle.dispatchEvent(new PointerEvent(type, {
                pointerId: 77, pointerType: 'pen', isPrimary: true, button: 0, buttons,
                clientX: x, clientY: py, bubbles: true, cancelable: true, composed: true }));
            fire('pointerdown', y, 1);
            for (let i = 1; i <= 8; i++) fire('pointermove', y + dy * i / 8, 1);
            fire('pointerup', y + dy, 0);
        }", new { x = (double)x, y = (double)y, dy = (double)slot * 2 });

        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']")).ToContainTextAsync("Resize Test Event");
        Assert.IsTrue(await EventHeightAsync(page) > before + slot, "a pen resize is committed");
    }

    // ── HTML5 DnD stays for the mouse (regression) ──────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Mouse_PressingTheEventBody_DoesNotStartAResize_AndKeepsHtml5Drag()
    {
        var page = await OpenSchedulerDayAsync(touch: false, 1440, 900);
        Assert.AreEqual("true", await Event(page).GetAttributeAsync("draggable"));
        var box = (await Event(page).BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 4, box.Y + box.Height / 2 + 4, new MouseMoveOptions { Steps = 3 });
        Assert.IsNull(await Event(page).GetAttributeAsync("data-resizing"), "a body press is not a resize");
        await page.Mouse.UpAsync();
        Assert.AreEqual("true", await Event(page).GetAttributeAsync("draggable"));
    }

    // ── editor shell resizer on the shared helper ───────────────────────────────────────────

    private const string ShellStorageKey = "tempo.tm-editor-shell.editor-shell-demo";

    [TestMethod]
    [TestCategory("WASM")]
    public async Task EditorShell_Resizer_MouseDrag_Works_OnTheSharedHelper_EscapeRollsBack_AndOnlyThatShellMoves()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/editor-shell", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var shell = "[data-testid='editor-shell']";
        var separator = page.Locator($"{shell} [role='separator'][data-side='left']");
        await Assertions.Expect(separator).ToBeVisibleAsync();
        var box = (await separator.BoundingBoxAsync())!;
        var y = box.Y + 100;
        var startWidth = int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!);

        // Escape mid-drag: the width goes back and nothing is persisted.
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 40, y, new MouseMoveOptions { Steps = 5 });
        Assert.AreEqual(startWidth - 40, int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!), "live width follows the pointer");
        await ShotAsync(page, "shell-resizer-dragging");
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();
        Assert.AreEqual(startWidth, int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!), "Escape restores the start width");
        Assert.IsNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{ShellStorageKey}')"), "a cancelled drag persists nothing");

        // A real drag commits once and persists.
        box = (await separator.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 50, y, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", (startWidth - 50).ToString());
        Assert.IsNotNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{ShellStorageKey}')"));

        // The capture was released: a plain mouse move afterwards must not keep resizing.
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 200, y, new MouseMoveOptions { Steps = 5 });
        Assert.AreEqual(startWidth - 50, int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!), "no drag after release");
        Assert.AreEqual("false", await page.EvaluateAsync<string>("() => String(document.querySelector('[role=separator][data-side=left]').classList.contains('tm-editor-shell__resizer--dragging'))"));

        // The thresholds shell on the same page has its own registration and is not affected.
        var other = page.Locator("[data-testid='editor-shell-thresholds'] [role='separator'][data-side='left']");
        if (await other.CountAsync() > 0 && await other.First.IsVisibleAsync())
        {
            var otherBefore = await other.First.GetAttributeAsync("aria-valuenow");
            Assert.IsNotNull(otherBefore);
        }
        await ShotAsync(page, "shell-resizer-after");
    }

    // ── touch plumbing ──────────────────────────────────────────────────────────────────────

    private static Task TouchAsync(ICDPSession session, string type, double x, double y)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = 1 } },
        });
}
