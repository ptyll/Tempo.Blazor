using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// Phase 7: a finger moves an event (400ms hold), a mouse keeps HTML5 drag-and-drop, and
/// Alt+Arrow is the keyboard alternative. The resize path is covered by PointerDragE2ETests.
/// </summary>
[TestClass]
public class SchedulerTouchE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_TouchHold_MovesEvent_AndReportsTheNewTime()
    {
        var page = await OpenAsync(1024, 800, touch: true);
        await SwitchViewAsync(page, "Week");
        await ScrollGridToAsync(page, 8.0 / 24);

        var action = await LastActionAsync(page);
        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
            await TouchAsync(session, "touchMove", x, y + 60.0 * i / 6);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("tablet-week-moving") });
        await TouchAsync(session, "touchEnd", x, y + 60);

        var after = await LastActionAsync(page);
        Assert.AreNotEqual(action, after, "the drop commits a new time");
        Assert.IsTrue(after.Contains(':'), $"the action names the new time ({after})");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_TouchHold_MovesEvent_InTimeline()
    {
        var page = await OpenAsync(1024, 800, touch: true);
        await SwitchViewAsync(page, "Timeline");
        await page.EvaluateAsync(@"() => {
            const event = document.querySelector(""[data-testid='scheduler-interactive'] [data-event-id='i2']"");
            if (event) event.scrollIntoView({ inline: 'center', block: 'nearest' });
        }");

        var action = await LastActionAsync(page);
        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
            await TouchAsync(session, "touchMove", x + 80.0 * i / 6, y);
        await page.Locator("[data-testid='scheduler-interactive']").ScreenshotAsync(
            new LocatorScreenshotOptions { Path = Shot("tablet-timeline-moving") });
        await TouchAsync(session, "touchEnd", x + 80, y);

        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']"))
            .Not.ToHaveTextAsync(action, new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_TouchHold_OnTheHandle_Resizes_NotMoves()
    {
        var page = await OpenAsync(1024, 800, touch: true);
        await SwitchViewAsync(page, "Week");
        await ScrollGridToAsync(page, 8.0 / 24);

        var evt = EventAsync(page, "i4");
        await evt.ClickAsync();
        var before = await evt.EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        var handle = (await evt.Locator(".tm-scheduler-event-resize-handle--bottom").BoundingBoxAsync())!;
        Assert.IsTrue(handle.Height >= 44, $"the selected coarse handle is a 44px target ({handle.Height}px)");
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = handle.X + handle.Width / 2;
        var y = handle.Y + handle.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
            await TouchAsync(session, "touchMove", x, y + 50.0 * i / 6);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("tablet-week-resizing") });
        await TouchAsync(session, "touchEnd", x, y + 50);

        var after = await evt.EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        Assert.IsTrue(after > before + 10, $"the handle grew the event ({before} -> {after})");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_TouchHold_MovesEvent_InTimeline()
    {
        var page = await OpenAsync(390, 844, touch: true);
        await SwitchViewAsync(page, "Timeline");
        await page.EvaluateAsync(@"() => {
            const event = document.querySelector(""[data-testid='scheduler-interactive'] [data-event-id='i2']"");
            if (event) event.scrollIntoView({ inline: 'center', block: 'nearest' });
        }");

        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
            await TouchAsync(session, "touchMove", x + 60.0 * i / 6, y);
        await page.Locator("[data-testid='scheduler-interactive']").ScreenshotAsync(
            new LocatorScreenshotOptions { Path = Shot("mobile-timeline-moving") });
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("mobile-live-label") });
        await TouchAsync(session, "touchEnd", x + 60, y);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_LongPress_MovesEvent_InDayView()
    {
        var page = await OpenAsync(390, 844, touch: true);
        await SwitchViewAsync(page, "Day");
        await ScrollGridToAsync(page, 8.0 / 24);

        var action = await LastActionAsync(page);
        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        for (var i = 1; i <= 6; i++)
            await TouchAsync(session, "touchMove", x, y + 40.0 * i / 6);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("mobile-day-moving") });
        await TouchAsync(session, "touchEnd", x, y + 40);

        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']"))
            .Not.ToHaveTextAsync(action, new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Drag_Shows_A_Live_Time_Label()
    {
        var page = await OpenAsync(1024, 800, touch: true);
        await SwitchViewAsync(page, "Week");
        await ScrollGridToAsync(page, 8.0 / 24);

        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        await TouchAsync(session, "touchMove", x, y + 30);

        var label = page.Locator(".tm-scheduler-event-drag-time");
        await Assertions.Expect(label).ToBeVisibleAsync();
        Assert.IsTrue((await label.InnerTextAsync()).Contains(':'), "the label names a time");
        // The event clips overflow, so a label that merely exists can still be invisible. The point
        // above its box must hit the label itself.
        var hit = await label.EvaluateAsync<bool>(@"el => {
            // pointer-events: none drops the label from elementsFromPoint even when it is painted.
            // Forcing it back in is what distinguishes 'painted' from 'clipped by an ancestor'.
            el.style.pointerEvents = 'auto';
            const r = el.getBoundingClientRect();
            const painted = document.elementsFromPoint(r.x + r.width / 2, r.y + r.height / 2).includes(el);
            el.style.pointerEvents = '';
            return painted;
        }");
        Assert.IsTrue(hit, "the live time label is painted above the event, not clipped by it");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("tablet-live-label") });
        await TouchAsync(session, "touchEnd", x, y + 30);

        await SwitchViewAsync(page, "Timeline");
        await page.EvaluateAsync(@"() => {
            const event = document.querySelector(""[data-testid='scheduler-interactive'] [data-event-id='i2']"");
            if (event) event.scrollIntoView({ inline: 'center', block: 'nearest' });
        }");
        var timeline = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        await TouchAsync(session, "touchStart", timeline.X + timeline.Width / 2, timeline.Y + 4);
        await page.WaitForTimeoutAsync(450);
        var timelineLabel = page.Locator(".tm-scheduler-event-drag-time");
        await Assertions.Expect(timelineLabel).ToBeVisibleAsync();
        var timelineHit = await timelineLabel.EvaluateAsync<bool>(@"el => {
            el.style.pointerEvents = 'auto';
            const r = el.getBoundingClientRect();
            const painted = document.elementsFromPoint(r.x + r.width / 2, r.y + r.height / 2).includes(el);
            el.style.pointerEvents = '';
            return painted;
        }");
        Assert.IsTrue(timelineHit, "the first-row timeline label is painted, not clipped by the sticky header");
        await TouchAsync(session, "touchEnd", timeline.X + timeline.Width / 2, timeline.Y + 4);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dark_Week_Move_Shows_The_Live_Label()
    {
        var page = await OpenAsync(1024, 800, touch: true);
        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'dark')");
        await SwitchViewAsync(page, "Week");
        await ScrollGridToAsync(page, 8.0 / 24);

        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await TouchAsync(session, "touchStart", x, y);
        await page.WaitForTimeoutAsync(450);
        await TouchAsync(session, "touchMove", x, y + 30);
        await Assertions.Expect(page.Locator(".tm-scheduler-event-drag-time")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("dark-week-moving") });
        await TouchAsync(session, "touchEnd", x, y + 30);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Keyboard_AltArrow_Moves_The_Focused_Event()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await SwitchViewAsync(page, "Day");
        await ScrollGridToAsync(page, 8.0 / 24);

        var action = await LastActionAsync(page);
        await EventAsync(page, "i2").FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowDown");

        var after = await LastActionAsync(page);
        Assert.AreNotEqual(action, after, "Alt+ArrowDown commits a new slot");
        Assert.AreEqual("i2", await page.EvaluateAsync<string>("() => document.activeElement?.dataset.eventId ?? ''"),
            "the moved event keeps the focus");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Shot("focused-after-alt-down") });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Desktop_Mouse_Drag_Still_Moves_The_Event()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await SwitchViewAsync(page, "Day");
        await ScrollGridToAsync(page, 8.0 / 24);

        var action = await LastActionAsync(page);
        var box = (await EventAsync(page, "i2").BoundingBoxAsync())!;
        var x = box.X + box.Width / 2;
        var y = box.Y + 8; // clear of the top resize handle
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + 80, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        await Assertions.Expect(page.Locator("[data-testid='scheduler-last-action']"))
            .Not.ToHaveTextAsync(action, new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/scheduler", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='scheduler-interactive']").ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator EventAsync(IPage page, string id)
        => page.Locator($"[data-testid='scheduler-interactive'] [data-event-id='{id}']");

    private static async Task SwitchViewAsync(IPage page, string view)
    {
        await page.Locator($"[data-testid='scheduler-interactive'] [role='tab'][data-view='{view}']").ClickAsync();
        await page.Locator($"[data-testid='scheduler-interactive'] [data-event-id='i2']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10000 });
    }

    private static Task ScrollGridToAsync(IPage page, double fraction)
        => page.EvaluateAsync(@"fraction => {
            const event = document.querySelector(""[data-testid='scheduler-interactive'] [data-event-id='i2']"");
            if (event) event.scrollIntoView({ block: 'center' });
            const root = document.querySelector(""[data-testid='scheduler-interactive'] .tm-scheduler-timegrid-scroll"");
            if (root && root.scrollHeight > root.clientHeight + 1) root.scrollTop = root.scrollHeight * fraction;
        }", fraction);

    private static Task<string> LastActionAsync(IPage page)
        => page.Locator("[data-testid='scheduler-last-action']").InnerTextAsync();

    private static string Shot(string name)
    {
        var dir = Path.Combine(FindRepoRoot(),
            UpdateBaseline
                ? Path.Combine("tests", "Tempo.Blazor.E2E", "__screenshots__", "scheduler", "phase7")
                : Path.Combine("TestResults", "scheduler-phase7"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}.png");
    }

    private static bool UpdateBaseline
        => string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    private static Task TouchAsync(ICDPSession session, string type, double x, double y)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = 1 } },
        });
}
