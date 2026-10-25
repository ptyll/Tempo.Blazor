using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// E2E for the scheduler range contract on the WASM demo: exact toolbar titles, the overflow button
/// opening that day, the event template reaching every view, and a Sunday-first week. The demo
/// culture is pinned to English so the titles are exact strings. Element screenshots of the card go
/// to <c>__screenshots__/scheduler/</c> only when <c>TM_E2E_UPDATE_BASELINE=1</c>; otherwise they land
/// in the git-ignored TestResults folder.
/// </summary>
[TestClass]
public class SchedulerNavigationE2ETests : WasmTestBase
{
    private const string Monday = "Monday, June 15, 2026";
    private const string Tuesday = "Tuesday, June 16, 2026";
    private const string Agenda = "June 15 – July 14, 2026";
    private const string AgendaNext = "July 15 – August 13, 2026";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private async Task<IPage> OpenAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/scheduler",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<ILocator> CardAsync(IPage page)
    {
        var card = page.Locator("[data-testid='scheduler-ranges']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await card.ScrollIntoViewIfNeededAsync();
        await card.Locator(".tm-scheduler").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return card;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_MoreButton_OpensThatDay_AndTemplateRenders()
    {
        var page = await OpenAsync();
        var card = await CardAsync(page);

        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-title")).ToHaveTextAsync("June 2026");

        var more = card.Locator(".tm-scheduler-month-more");
        await more.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveCardAsync(card, "ranges-month-more");

        await more.ClickAsync();

        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-view-btn--active"))
            .ToHaveAttributeAsync("data-view", "Day");
        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-title"))
            .ToHaveTextAsync("Wednesday, June 10, 2026");
        await Assertions.Expect(card.Locator("[data-testid='scheduler-ranges-event']").First)
            .ToBeVisibleAsync();
        await SaveCardAsync(card, "ranges-day-after-more");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Titles_AreExact_AndTemplateRenders_InEveryView()
    {
        var page = await OpenAsync();
        var card = await CardAsync(page);
        var title = card.Locator(".tm-scheduler-toolbar-title");
        var anchor = card.Locator("[data-testid='scheduler-ranges-event']",
            new LocatorLocatorOptions { HasText = "Range Anchor" });

        await card.Locator(".tm-scheduler-toolbar-view-btn[data-view='Week']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync("June 15–21, 2026");
        await Assertions.Expect(anchor).ToBeVisibleAsync();
        await SaveCardAsync(card, "ranges-week-template");

        await card.Locator(".tm-scheduler-toolbar-view-btn[data-view='Timeline']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Monday);
        await Assertions.Expect(card.Locator(".tm-scheduler-timeline-event")).ToHaveCountAsync(1);
        await Assertions.Expect(anchor).ToBeVisibleAsync();
        await Assertions.Expect(anchor).ToHaveTextAsync("Range Anchor");

        await card.Locator("[data-testid='scheduler-next']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Tuesday);
        await card.Locator("[data-testid='scheduler-prev']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Monday);
        await SaveCardAsync(card, "ranges-timeline-day");

        await card.Locator(".tm-scheduler-toolbar-view-btn[data-view='Agenda']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Agenda);
        await Assertions.Expect(anchor).ToBeVisibleAsync();
        await Assertions.Expect(anchor).ToHaveTextAsync("Range Anchor");

        await card.Locator("[data-testid='scheduler-next']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(AgendaNext);
        await card.Locator("[data-testid='scheduler-prev']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Agenda);
        await SaveCardAsync(card, "ranges-agenda-range");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_SundayToggle_MovesTheWeekStart()
    {
        var page = await OpenAsync();
        var card = await CardAsync(page);

        await page.Locator("[data-testid='scheduler-ranges-sunday']").CheckAsync();

        await Assertions.Expect(card.Locator(".tm-scheduler-month-day-header").First).ToHaveTextAsync("Sun");

        await card.Locator(".tm-scheduler-toolbar-view-btn[data-view='Week']").ClickAsync();
        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-title")).ToHaveTextAsync("June 14–20, 2026");
        await SaveCardAsync(card, "ranges-sunday-first");
    }

    private static async Task SaveCardAsync(ILocator card, string fileName)
    {
        var dir = Path.Combine(RepoRoot(),
            UpdateBaseline
                ? Path.Combine("tests", "Tempo.Blazor.E2E", "__screenshots__", "scheduler")
                : Path.Combine("tests", "Tempo.Blazor.E2E", "TestResults", "scheduler"));
        Directory.CreateDirectory(dir);
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png") });
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
