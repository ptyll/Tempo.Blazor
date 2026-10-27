using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// Phase 6 responsiveness. Each view is screenshotted at desktop, tablet and phone widths, in light
/// and dark, and the phone checks the touch targets, the sticky gutter and the page width.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SchedulerResponsiveE2ETests : WasmTestBase
{
    private static readonly (int Width, int Height, bool Touch, string Name)[] Viewports =
    [
        (1440, 1100, false, "desktop"),
        (1024, 900, true, "tablet"),
        (390, 844, true, "phone"),
        (320, 700, true, "narrow"),
    ];

    private static readonly string[] Sections =
    [
        "scheduler-month-section", "scheduler-week-section", "scheduler-day-section",
        "scheduler-agenda-section", "scheduler-timeline-section",
    ];

    [TestMethod]
    public async Task Scheduler_Renders_At_Every_Viewport_Without_Page_Overflow()
    {
        foreach (var viewport in Viewports)
        {
            foreach (var dark in new[] { false, true })
            {
                var page = await OpenAsync(viewport.Width, viewport.Height, viewport.Touch, dark);
                foreach (var section in Sections)
                {
                    var root = page.Locator($"[data-testid='{section}'] .tm-scheduler");
                    await root.ScrollIntoViewIfNeededAsync();
                    await Assertions.Expect(root).ToBeVisibleAsync();
                    await root.ScreenshotAsync(new LocatorScreenshotOptions
                    {
                        Path = Shot(section, viewport.Name, dark),
                    });
                }

                if (viewport.Width == 320)
                {
                    var overflow = await page.EvaluateAsync<bool>(
                        "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
                    Assert.IsFalse(overflow, "a 320px page must not scroll sideways");
                }

                await page.Context.CloseAsync();
            }
        }
    }

    [TestMethod]
    public async Task Phone_Controls_Meet_The_Touch_Target_And_The_Gutter_Stays_Sticky()
    {
        var page = await OpenAsync(390, 844, touch: true, dark: false);
        var week = page.Locator("[data-testid='scheduler-mobile-plain'] .tm-scheduler");
        await week.ScrollIntoViewIfNeededAsync();

        var prev = (await week.Locator("[data-testid='scheduler-prev']").BoundingBoxAsync())!;
        Assert.IsTrue(prev.Width >= 44 && prev.Height >= 44, "the previous control is at least 44px");
        await AssertCompactRowFitsAsync(page, week);

        var narrow = await OpenAsync(320, 700, touch: true, dark: false);
        var narrowWeek = narrow.Locator("[data-testid='scheduler-mobile-plain'] .tm-scheduler");
        await narrowWeek.ScrollIntoViewIfNeededAsync();
        await AssertCompactRowFitsAsync(narrow, narrowWeek);
        await narrow.Context.CloseAsync();

        // The compact row is the header, not a footer. A sticky grid head must not paint over it.
        var headerTop = prev.Y;
        var grid = week.Locator(".tm-scheduler-timegrid");
        await grid.ScrollIntoViewIfNeededAsync();
        var gridTop = (await grid.BoundingBoxAsync())!.Y;
        Assert.IsTrue(headerTop < gridTop, $"the compact header sits above the grid ({headerTop} < {gridTop})");

        // The all-day row has its own gutter, and it is not the one that scrolls. The body gutter is.
        var gutter = week.Locator(".tm-scheduler-timegrid-body .tm-scheduler-timegrid-time-gutter").First;
        await gutter.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var before = (await gutter.BoundingBoxAsync())!;
        var column = week.Locator(".tm-scheduler-timegrid-col").First;
        var columnBefore = (await column.BoundingBoxAsync())!;
        var scroll = week.Locator(".tm-scheduler-timegrid-scroll");
        var wider = await scroll.EvaluateAsync<bool>("node => node.scrollWidth > node.clientWidth");
        Assert.IsTrue(wider, "a phone week scrolls sideways instead of crushing its columns");
        await scroll.EvaluateAsync("node => node.scrollLeft = 200");
        var after = (await gutter.BoundingBoxAsync())!;
        var columnAfter = (await column.BoundingBoxAsync())!;
        Assert.AreEqual(before.X, after.X, 1, "the time gutter stays put while the grid scrolls");
        Assert.IsTrue(columnAfter.X < columnBefore.X - 1, "the first column moves with the scroll");

        var title = (await week.Locator(".tm-scheduler-toolbar-title [role='button']").BoundingBoxAsync())!;
        Assert.IsTrue(title.Height >= 44, "the title trigger is at least 44px");
        var view = (await week.Locator("[data-testid='scheduler-view']").BoundingBoxAsync())!;
        Assert.IsTrue(view.Height >= 44, "the view trigger is at least 44px");
        var stripDay = (await week.Locator(".tm-scheduler-date-strip-day").First.BoundingBoxAsync())!;
        Assert.IsTrue(stripDay.Width >= 44 && stripDay.Height >= 44, "a strip day is at least 44px");

        var agenda = page.Locator("[data-testid='scheduler-mobile-agenda'] .tm-scheduler");
        await agenda.ScrollIntoViewIfNeededAsync();
        var agendaTitle = (await agenda.Locator(".tm-scheduler-agenda-event-title").First.BoundingBoxAsync())!;
        Assert.IsTrue(agendaTitle.Width > 0, "an agenda card title has a width");
    }

    [TestMethod]
    public async Task Desktop_Toolbar_Shows_The_Tablist_And_Does_Not_Overflow()
    {
        var page = await OpenAsync(1440, 1100, touch: false, dark: false);
        var toolbar = page.Locator("[data-testid='scheduler-toolbar-section'] .tm-scheduler").First;
        await toolbar.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(toolbar.Locator("[role='tablist']")).ToBeVisibleAsync();
        await Assertions.Expect(toolbar.Locator("button.tm-toolbar-more")).ToBeVisibleAsync();
        var fits = await toolbar.Locator(".tm-toolbar").EvaluateAsync<bool>(
            "node => node.scrollWidth <= node.clientWidth + 1");
        Assert.IsTrue(fits, "the desktop toolbar fits its row");
    }

    [TestMethod]
    public async Task Tablet_Card_Is_One_Toolbar_Row()
    {
        var page = await OpenAsync(1280, 900, touch: true, dark: false);
        var card = page.Locator("[data-testid='scheduler-tablet-section'] .showcase-card").First.Locator(".tm-scheduler");
        await card.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("data-layout", "tablet");
        await card.Locator("[data-tm-group='navigation']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // The Print group collapses into More and leaves an empty, zero-height wrapper; only a
        // group that paints counts as a row.
        var rows = await card.Locator(".tm-scheduler-toolbar").EvaluateAsync<int>(
            """
            node => {
                const boxes = [...node.querySelectorAll('[data-tm-group]')]
                    .map(el => el.getBoundingClientRect())
                    .filter(box => box.height > 0);
                if (boxes.length === 0) return 0;
                const origin = Math.min(...boxes.map(box => box.top));
                const placed = boxes
                    .map(box => ({ top: box.top - origin, bottom: box.bottom - origin }))
                    .sort((a, b) => a.top - b.top);
                let rows = 0;
                let rowBottom = -1;
                for (const box of placed) {
                    if (box.top >= rowBottom - 1) { rows++; rowBottom = box.bottom; }
                }
                return rows;
            }
            """);
        Assert.AreEqual(1, rows, "a tablet toolbar is one row");
    }

    [TestMethod]
    public async Task Unbounded_Mobile_Host_Keeps_The_Action_Bar_In_View()
    {
        var page = await OpenAsync(390, 844, touch: true, dark: false);
        // The host gives the card no height. The 70dvh cap on the grid is what keeps the bar inside
        // the viewport once the card is at the top. The scroll guard pins the page after its roving
        // focus, so the scroll waits until the guard has let go.
        var card = page.Locator("[data-testid='scheduler-mobile-unbounded']");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('[data-testid=\"scheduler-mobile-unbounded\"] .tm-scheduler')?.__tmSchedulerScrollHold");
        await card.ScrollIntoViewIfNeededAsync();
        var bar = (await card.Locator(".tm-mobile-action-bar").BoundingBoxAsync())!;
        var viewport = page.ViewportSize!;
        Assert.IsTrue(bar.Y >= 0 && bar.Y + bar.Height <= viewport.Height,
            $"the action bar is in the viewport ({bar.Y}+{bar.Height} vs {viewport.Height})");
    }

    [TestMethod]
    public async Task Tablet_Boundary_At_800_Is_One_Row()
    {
        var page = await OpenAsync(800, 900, touch: false, dark: false);
        var card = page.Locator("[data-testid='scheduler-tablet-auto'] .tm-scheduler");
        await card.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("data-layout", "tablet");
        var rows = await card.Locator(".tm-scheduler-toolbar").EvaluateAsync<int>("""
            node => {
                const boxes = [...node.querySelectorAll('[data-tm-group]')].map(el => el.getBoundingClientRect()).filter(b => b.height > 0);
                if (boxes.length === 0) return 0;
                const origin = Math.min(...boxes.map(b => b.top));
                const placed = boxes.map(b => ({ top: b.top - origin, bottom: b.bottom - origin })).sort((a, b) => a.top - b.top);
                let rows = 0;
                let rowBottom = -1;
                for (const box of placed) {
                    if (box.top >= rowBottom - 1) { rows++; rowBottom = box.bottom; }
                }
                return rows;
            }
            """);
        Assert.AreEqual(1, rows, "the 640–1023 boundary renders one toolbar row");
    }

    [TestMethod]
    public async Task Print_From_A_Phone_Lays_Out_Seven_Week_Columns()
    {
        var page = await OpenAsync(390, 844, touch: true, dark: false);
        // The week showcase is pinned desktop, so it proves nothing. The forced-mobile card is the
        // markup a phone prints.
        var week = page.Locator("[data-testid='scheduler-mobile-plain'] .tm-scheduler");
        await week.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(week).ToHaveAttributeAsync("data-layout", "mobile");
        Assert.AreEqual(3, await week.Locator(".tm-scheduler-timegrid-col").CountAsync(),
            "a phone week shows the focus day and its neighbours");

        // emulateMedia switches the CSS media; the column count is Blazor markup, so the browser's
        // beforeprint event (what Ctrl+P raises) is what pins the desktop layout. The toolbar Print
        // action calls PrintAsync, which awaits the re-render; a raw Ctrl+P does not.
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print });
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('beforeprint'))");
        await Assertions.Expect(week).ToHaveAttributeAsync("data-layout", "desktop");
        Assert.AreEqual(7, await week.Locator(".tm-scheduler-timegrid-col").CountAsync());

        await page.EvaluateAsync("() => window.dispatchEvent(new Event('afterprint'))");
        await Assertions.Expect(week).ToHaveAttributeAsync("data-layout", "mobile");
    }

    [TestMethod]
    public async Task Mobile_Card_Action_Bar_Opens_View_And_More()
    {
        var page = await OpenAsync(1440, 1100, touch: false, dark: false);
        var card = page.Locator("[data-testid='scheduler-mobile-section'] .showcase-card").Nth(2);
        await card.ScrollIntoViewIfNeededAsync();
        await card.Locator("[data-action-id='scheduler-add']").ClickAsync();
        await card.Locator("[data-action-id='scheduler-today']").ClickAsync();
        await card.Locator("[data-action-id='scheduler-view']").ClickAsync();
        var viewMenu = page.Locator(".tm-dropdown__menu[data-testid='scheduler-view-menu'], [data-testid='scheduler-view-menu']").Last;
        await Assertions.Expect(viewMenu).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await card.Locator(".tm-mobile-action-bar__more").ClickAsync();
        var more = page.Locator(".tm-mobile-action-bar__menu").Last;
        await Assertions.Expect(more).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-action-id='scheduler-print']")).ToBeVisibleAsync();
        await page.Locator("[data-action-id='scheduler-go-to-date']").ClickAsync();
        await Assertions.Expect(more).ToBeHiddenAsync();
        var calendar = page.Locator(".tm-scheduler-date-calendar").Last;
        await Assertions.Expect(calendar).ToBeVisibleAsync();
        var focused = await page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.closest('.tm-scheduler-date-calendar') ? 'in' : 'out'");
        Assert.AreEqual("in", focused, "Go to date moves focus into the calendar");
        await page.Keyboard.PressAsync("Escape");
        await PhaseShotAsync(page, "mobile-card-action-bar");
    }

    [TestMethod]
    public async Task Phone_Week_Has_No_Serious_Axe_Findings()
    {
        var page = await OpenAsync(390, 844, touch: true, dark: false);
        // The week showcase section is pinned desktop so the visual lane stays stable. The scan
        // belongs on the forced-mobile card, which is the markup a 390px container renders.
        var week = page.Locator("[data-testid='scheduler-mobile-plain'] .tm-scheduler");
        await week.ScrollIntoViewIfNeededAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js",
        });
        var findings = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector("[data-testid='scheduler-mobile-plain'] .tm-scheduler");
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'serious' || v.impact === 'critical')
                    .map(v => `${v.impact}: ${v.id}`);
            }
            """);
        Assert.AreEqual(0, findings.Length, string.Join("; ", findings));
    }

    [TestMethod]
    public async Task Drag_Label_Stays_Within_Four_Pixels_Of_The_Event()
    {
        foreach (var (testId, width, height) in new[] { ("scheduler-mobile-plain", 390, 844), ("scheduler-timeline-section", 1024, 900) })
        {
            var page = await OpenAsync(width, height, touch: true, dark: false);
            var host = page.Locator($"[data-testid='{testId}'] .tm-scheduler");
            await host.ScrollIntoViewIfNeededAsync();
            await host.Locator("[data-event-id]").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            // The first event can sit under the slot grid of a scrolled week. Pick one the hit test
            // resolves to the event itself, so the touch lands on it.
            var point = await host.EvaluateAsync<double[]>("""
                node => {
                    const events = [...node.querySelectorAll('[data-event-id]')];
                    for (const event of events) {
                        event.scrollIntoView({ block: 'center', inline: 'center' });
                        const r = event.getBoundingClientRect();
                        const x = r.left + r.width / 2;
                        const y = r.top + Math.min(r.height / 2, 20);
                        const hit = document.elementFromPoint(x, y);
                        if (hit && event.contains(hit)) return [x, y];
                    }
                    return [];
                }
                """);
            var x = point.Length == 2 ? point[0] : 0;
            var y = point.Length == 2 ? point[1] : 0;
            var session = await page.Context.NewCDPSessionAsync(page);
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchStart",
                ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = 1 } }
            });
            await page.WaitForTimeoutAsync(500);
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y + 40, ["id"] = 1 } }
            });
            // The label is placed on the frame after the snap, once the translate has landed.
            await page.WaitForTimeoutAsync(50);
            var gap = await host.EvaluateAsync<double>(
                """
                node => {
                    const label = node.querySelector(':scope > .tm-scheduler-event-drag-time');
                    const event = node.querySelector('.tm-scheduler-event--moving');
                    if (!label || !event) return 999;
                    const painted = label.getBoundingClientRect();
                    const box = event.getBoundingClientRect();
                    const above = label.classList.contains('tm-scheduler-event-drag-time--below')
                        ? Math.abs(painted.top - box.bottom)
                        : Math.abs(painted.bottom - box.top);
                    return above;
                }
                """);
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchEnd",
                ["touchPoints"] = Array.Empty<object>()
            });
            Assert.IsTrue(gap <= 4, $"the scheduler's own drag label sits on the event at {testId} ({gap}px)");
            await page.Context.CloseAsync();
        }
    }

    private static async Task AssertCompactRowFitsAsync(IPage page, ILocator scheduler)
    {
        var host = (await scheduler.BoundingBoxAsync())!;
        var next = (await scheduler.Locator("[data-testid='scheduler-next']").BoundingBoxAsync())!;
        var view = (await scheduler.Locator("[data-testid='scheduler-view']").BoundingBoxAsync())!;
        Assert.IsTrue(next.X >= host.X - 1 && next.X + next.Width <= host.X + host.Width + 1,
            $"scheduler-next stays inside the scheduler ({next.X},{next.Width} vs {host.X},{host.Width})");
        Assert.IsTrue(view.X >= host.X - 1 && view.X + view.Width <= host.X + host.Width + 1,
            $"the view trigger stays inside the scheduler ({view.X},{view.Width} vs {host.X},{host.Width})");
        var scrolled = await scheduler.EvaluateAsync<double>("node => node.scrollLeft");
        Assert.AreEqual(0, scrolled, 1, "the scheduler itself does not scroll sideways");

        var bar = page.Locator("[data-testid='scheduler-mobile-section'] .showcase-card").Nth(2).Locator(".tm-mobile-action-bar");
        await bar.ScrollIntoViewIfNeededAsync();
        Assert.IsTrue(await bar.IsVisibleAsync(), "the action bar is visible in the action-bar card");
    }

    private static async Task PhaseShotAsync(IPage page, string file)
    {
        var update = string.Equals(
            Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            update
                ? Path.Combine("__screenshots__", "scheduler", "phase6")
                : Path.Combine("TestResults", "scheduler", "phase6"));
        Directory.CreateDirectory(dir);
        var card = page.Locator("[data-testid='scheduler-mobile-section'] .showcase-card").Nth(2);
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{file}.png") });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static string Shot(string section, string viewport, bool dark)
        => Path.Combine(AppContext.BaseDirectory, "screenshots",
            $"{section}-{viewport}-{(dark ? "dark" : "light")}.png");

    private async Task<IPage> OpenAsync(int width, int height, bool touch, bool dark)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/scheduler", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        if (dark)
        {
            await page.EvaluateAsync(
                "() => { document.documentElement.setAttribute('data-theme', 'dark'); document.documentElement.classList.add('dark', 'tm-dark'); document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark')); }");
        }
        return page;
    }
}
