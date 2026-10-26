using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// Desktop visual review of the scheduler after the unification (phase 3, layer A). Each of the
/// five views is an element screenshot of the component, not the page, in light and dark and in
/// the default and indigo themes, so the review compares the component against the reference
/// rather than the demo shell. Committed shots are rewritten only with
/// <c>TM_E2E_UPDATE_BASELINE=1</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SchedulerVisualE2ETests : WasmTestBase
{
    private static readonly (string Section, string File)[] Views =
    [
        ("scheduler-month-section", "month"),
        ("scheduler-week-section", "week"),
        ("scheduler-day-section", "day"),
        ("scheduler-agenda-section", "agenda"),
        ("scheduler-timeline-section", "timeline"),
    ];

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    [TestMethod]
    public async Task Scheduler_Columns_Line_Up_With_Their_Header()
    {
        var page = await OpenAsync(false);

        var month = page.Locator("[data-testid='scheduler-month-section'] .tm-scheduler");
        await month.ScrollIntoViewIfNeededAsync();
        var headerCells = month.Locator(".tm-scheduler-month-day-header");
        var firstRow = month.Locator(".tm-scheduler-month-row").First.Locator(".tm-scheduler-month-day");
        Assert.AreEqual(7, await headerCells.CountAsync());
        for (var i = 0; i < 7; i++)
        {
            var head = (await headerCells.Nth(i).BoundingBoxAsync())!;
            var cell = (await firstRow.Nth(i).BoundingBoxAsync())!;
            Assert.AreEqual(head.X, cell.X, 1, $"month column {i} left");
            Assert.AreEqual(head.Width, cell.Width, 1, $"month column {i} width");
        }

        var timeline = page.Locator("[data-testid='scheduler-timeline-section'] .tm-scheduler");
        await timeline.ScrollIntoViewIfNeededAsync();
        var eight = timeline.Locator(".tm-scheduler-timeline-time-header", new() { HasText = "09:00" }).First;
        var bar = timeline.Locator(".tm-scheduler-timeline-event").First;
        var row = timeline.Locator(".tm-scheduler-timeline-row").First;
        var eightBox = (await eight.BoundingBoxAsync())!;
        var barBox = (await bar.BoundingBoxAsync())!;
        var rowBox = (await row.BoundingBoxAsync())!;
        Assert.AreEqual(eightBox.X, barBox.X, 2, "the 09:00 bar starts under the 09:00 header");
        Assert.IsTrue(barBox.X >= rowBox.X - 1 && barBox.X + barBox.Width <= rowBox.X + rowBox.Width + 1,
            "the bar stays inside its row");

        var agenda = page.Locator("[data-testid='scheduler-agenda-section'] .tm-scheduler");
        await agenda.ScrollIntoViewIfNeededAsync();
        var dates = agenda.Locator(".tm-scheduler-agenda-date");
        var events = agenda.Locator(".tm-scheduler-agenda-event");
        var firstDate = (await dates.First.BoundingBoxAsync())!;
        var firstEvent = (await events.First.BoundingBoxAsync())!;
        Assert.IsTrue(firstDate.X + firstDate.Width < firstEvent.X, "the date column ends before the events");
        Assert.AreEqual(firstDate.Y, firstEvent.Y, 4, "the date and its first event share a row");
        var secondEvent = (await events.Nth(1).BoundingBoxAsync())!;
        Assert.AreEqual(firstEvent.X, secondEvent.X, 1, "events share one column");
    }

    [TestMethod]
    public async Task Scheduler_Desktop_Views_LightAndDark_DefaultAndIndigo()
    {
        foreach (var indigo in new[] { false, true })
        {
            var themeName = indigo ? "indigo" : "default";
            var page = await OpenAsync(indigo);

            foreach (var (section, file) in Views)
                await SaveAsync(page, section, $"{file}--light-{themeName}");

            await SetDarkAsync(page);

            foreach (var (section, file) in Views)
                await SaveAsync(page, section, $"{file}--dark-{themeName}");

            await SaveAsync(page, "scheduler-highlight-section", $"highlight--dark-{themeName}");
            await SaveAsync(page, "scheduler-empty-section", $"agenda-empty--dark-{themeName}");
        }
    }

    /// <summary>
    /// Toolbar 2.0 review shots. The date popover and the More menu paint in the top layer, so those
    /// two are page clips; the rest are the component. Light and dark.
    /// </summary>
    [TestMethod]
    public async Task Scheduler_Toolbar_And_Selection_LightAndDark()
    {
        var page = await OpenAsync(false);
        foreach (var dark in new[] { false, true })
        {
            if (dark) await SetDarkAsync(page);
            var theme = dark ? "dark" : "light";

            var section = page.Locator("[data-testid='scheduler-toolbar-section']");
            var toolbar = section.Locator(".tm-scheduler").First;
            // Park the card just under the sticky app bar. A toolbar left at the viewport top is
            // covered by that bar, and one left low makes the date popover flip above it.
            await ParkBelowHeaderAsync(page, toolbar.Locator(".tm-scheduler-toolbar"));
            await toolbar.Locator(".tm-scheduler-toolbar-title [role='button']").ClickAsync();
            await toolbar.Locator(".tm-calendar-view").WaitForAsync(
                new LocatorWaitForOptions { Timeout = 15000 });
            await page.WaitForTimeoutAsync(300);
            await FrameFromRowAsync(page, toolbar.Locator(".tm-scheduler-toolbar"), $"toolbar-popover-open--{theme}");
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(toolbar.Locator(".tm-calendar-view")).ToBeHiddenAsync();

            var more = toolbar.Locator("[data-testid='scheduler-more'] button");
            await ParkBelowHeaderAsync(page, toolbar.Locator(".tm-scheduler-toolbar"));
            await more.ClickAsync();
            await page.WaitForTimeoutAsync(400);
            await FrameFromRowAsync(page, toolbar.Locator(".tm-scheduler-toolbar"), $"toolbar-more-open--{theme}");
            await page.Keyboard.PressAsync("Escape");

            foreach (var (sectionId, file) in new[]
            {
                ("scheduler-month-section", "selected-month"),
                ("scheduler-week-section", "selected-week"),
                ("scheduler-agenda-section", "selected-agenda"),
            })
                await SaveAsync(page, sectionId, $"{file}--{theme}");
        }

        await page.GotoAsync($"{BaseUrl}/gantt",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("button", new PageLocatorOptions { HasText = "Calendar" }).First.ClickAsync();
        var gantt = page.Locator("[data-testid='gantt-calendar-view'] .tm-scheduler");
        await gantt.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveSchedulerAsync(page, gantt, "gantt-calendar-toolbar--light");
        await SetDarkAsync(page);
        await SaveSchedulerAsync(page, gantt, "gantt-calendar-toolbar--dark");
    }

    /// <summary>
    /// Scrolls so the toolbar sits just under the sticky demo bar. The page scrolls the window,
    /// and the window is what the clip is measured against.
    /// </summary>
    private static async Task ParkBelowHeaderAsync(IPage page, ILocator toolbar)
    {
        // Align the toolbar to the top, then move it just under the sticky bar. High enough that
        // the date popover opens downward, low enough that the bar does not cover the row.
        await toolbar.EvaluateAsync("el => el.scrollIntoView({ block: 'start' })");
        await page.EvaluateAsync("() => window.scrollBy(0, -140)");
    }

    /// <summary>
    /// Puts the toolbar row just under the sticky bar, then frames that row plus the open panel.
    /// The row is the one that owns the panel, so a second scheduler in the same section is not
    /// what gets clipped.
    /// </summary>
    private static async Task FrameFromRowAsync(IPage page, ILocator row, string file)
    {
        await row.EvaluateAsync("el => window.scrollBy(0, el.getBoundingClientRect().top - 150)");
        await page.WaitForTimeoutAsync(200);
        var box = (await row.BoundingBoxAsync())!;
        var panel = page.Locator(".tm-overlay-panel:popover-open").Last;
        var panelBox = await panel.CountAsync() > 0 ? await panel.BoundingBoxAsync() : null;
        var viewport = page.ViewportSize!;
        var top = panelBox is null ? box.Y : Math.Min(box.Y, panelBox.Y);
        // A menu that has just opened can report a box shorter than its items. Keep the Print row.
        var panelBottom = panelBox is null ? 0 : panelBox.Y + Math.Max(panelBox.Height, 72);
        var bottom = Math.Max(box.Y + box.Height, panelBottom);
        var left = panelBox is null ? box.X : Math.Min(box.X, panelBox.X);
        var right = panelBox is null ? box.X + box.Width : Math.Max(box.X + box.Width, panelBox.X + panelBox.Width);
        var x = Math.Max(0, left - 8);
        var y = Math.Max(0, top - 8);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = ShotPath(file),
            Clip = new Clip
            {
                X = x,
                Y = y,
                Width = Math.Max(1, Math.Min(right - left + 16, viewport.Width - x)),
                Height = Math.Max(1, Math.Min(bottom - top + 16, viewport.Height - y))
            }
        });
    }

    /// <summary>
    /// Frames the section plus the open panel. A toolbar clip misses a panel that flipped above
    /// it, and a full page frame includes the sticky bar.
    /// </summary>
    private static async Task FrameAsync(IPage page, ILocator section, string file)
    {
        var row = section.Locator(".tm-scheduler-toolbar").First;
        var rowBox = (await row.BoundingBoxAsync())!;
        var box = (await section.BoundingBoxAsync())!;
        box = new Microsoft.Playwright.LocatorBoundingBoxResult
        {
            X = box.X,
            Y = rowBox.Y,
            Width = box.Width,
            Height = box.Y + box.Height - rowBox.Y
        };
        var panel = page.Locator(".tm-overlay-panel:popover-open").Last;
        var panelBox = await panel.CountAsync() > 0 ? await panel.BoundingBoxAsync() : null;
        var viewport = page.ViewportSize!;
        var top = panelBox is null ? box.Y : Math.Min(box.Y, panelBox.Y);
        var bottom = panelBox is null ? box.Y + box.Height : Math.Max(box.Y + box.Height, panelBox.Y + panelBox.Height);
        var left = panelBox is null ? box.X : Math.Min(box.X, panelBox.X);
        var right = panelBox is null ? box.X + box.Width : Math.Max(box.X + box.Width, panelBox.X + panelBox.Width);
        var x = Math.Max(0, left - 8);
        var y = Math.Max(0, top - 8);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = ShotPath(file),
            Clip = new Clip
            {
                X = x,
                Y = y,
                Width = Math.Max(1, Math.Min(right - left + 16, viewport.Width - x)),
                Height = Math.Max(1, Math.Min(bottom - top + 16, viewport.Height - y))
            }
        });
    }

    private static async Task ClipAsync(IPage page, ILocator anchor, string file)
    {
        var toolbar = anchor.Locator(".tm-scheduler-toolbar").First;
        var box = (await toolbar.BoundingBoxAsync())!;
        var panel = anchor.Locator(".tm-overlay-panel:popover-open").First;
        if (await panel.CountAsync() > 0)
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var panelBox = await panel.CountAsync() > 0 ? await panel.BoundingBoxAsync() : null;
        var viewport = page.ViewportSize!;
        var left = panelBox is null ? box.X : Math.Min(box.X, panelBox.X);
        var right = panelBox is null ? box.X + box.Width : Math.Max(box.X + box.Width, panelBox.X + panelBox.Width);
        // Keep the toolbar at the top of the clip and extend downward over the open panel. A panel
        // that flipped above the toolbar is included only while that still leaves the toolbar in frame.
        var top = box.Y;
        var bottom = box.Y + box.Height;
        if (panelBox is not null)
        {
            bottom = Math.Max(bottom, panelBox.Y + panelBox.Height);
            if (panelBox.Y < top && top - panelBox.Y < 80) top = panelBox.Y;
        }
        var x = Math.Max(0, left - 16);
        var y = Math.Max(0, top - 8);
        var width = Math.Max(1, Math.Min(right - left + 32, viewport.Width - x));
        var height = Math.Max(1, Math.Min(bottom - top + 24, viewport.Height - y));

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = ShotPath(file),
            Clip = new Clip { X = x, Y = y, Width = width, Height = height }
        });
    }

    private static async Task SaveSchedulerAsync(IPage page, ILocator scheduler, string file)
    {
        await scheduler.ScrollIntoViewIfNeededAsync();
        await scheduler.Locator(".tm-scheduler-toolbar").ScreenshotAsync(
            new LocatorScreenshotOptions { Path = ShotPath(file) });
    }

    private static string ShotPath(string file)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            UpdateBaseline
                ? Path.Combine("__screenshots__", "scheduler", "toolbar")
                : Path.Combine("TestResults", "scheduler", "toolbar"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{file}.png");
    }

    private async Task<IPage> OpenAsync(bool indigo)
    {
        var context = await CreateContextAsync();
        var init = "localStorage.setItem('tm-demo-culture', 'en');";
        if (indigo)
            init += " localStorage.setItem('tm-demo-color-theme', 'indigo');";
        await context.AddInitScriptAsync(init);

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["Cache-Control"] = "no-cache" });
        await page.GotoAsync($"{BaseUrl}/scheduler",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        if (indigo)
        {
            await page.EvaluateAsync(
                "() => document.documentElement.setAttribute('data-tm-theme', 'indigo')");
        }

        return page;
    }

    /// <summary>
    /// Dark has to land on the html root and on every themed region. data-theme on
    /// documentElement alone leaves the scheduler on the light tokens.
    /// </summary>
    private static async Task SetDarkAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('dark', 'tm-dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task SaveAsync(IPage page, string section, string file)
    {
        var scheduler = page.Locator($"[data-testid='{section}'] .tm-scheduler");
        await scheduler.ScrollIntoViewIfNeededAsync();
        await scheduler.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        // The grid opens at midnight and is taller than the demo card, so an element
        // shot would otherwise show only the empty night. The reference frames show
        // the working day, which is where this demo's events sit.
        var scroller = scheduler.Locator(".tm-scheduler-timegrid-scroll");
        if (await scroller.CountAsync() > 0)
            await scroller.EvaluateAsync("el => { el.scrollTop = el.scrollHeight * (8 / 24); }");
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            UpdateBaseline
                ? Path.Combine("__screenshots__", "scheduler", "visual")
                : Path.Combine("TestResults", "scheduler", "visual"));
        Directory.CreateDirectory(dir);
        await scheduler.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{file}.png") });
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
}
