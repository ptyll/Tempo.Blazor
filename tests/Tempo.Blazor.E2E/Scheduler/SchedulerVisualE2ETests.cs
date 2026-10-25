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
