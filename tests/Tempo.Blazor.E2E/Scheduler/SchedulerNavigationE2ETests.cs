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

        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-title-text")).ToHaveTextAsync("June 2026");

        var more = card.Locator(".tm-scheduler-month-more");
        await more.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveCardAsync(card, "ranges-month-more");

        await more.ClickAsync();

        await Assertions.Expect(card.Locator("[role='tab'][aria-selected='true']"))
            .ToHaveAttributeAsync("data-view", "Day");
        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-title-text"))
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
        var title = card.Locator(".tm-scheduler-toolbar-title-text");
        var anchor = card.Locator("[data-testid='scheduler-ranges-event']",
            new LocatorLocatorOptions { HasText = "Range Anchor" });

        await card.Locator("[role='tab'][data-view='Week']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync("June 15–21, 2026");
        await Assertions.Expect(anchor).ToBeVisibleAsync();
        await SaveCardAsync(card, "ranges-week-template");

        await card.Locator("[role='tab'][data-view='Timeline']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Monday);
        await Assertions.Expect(card.Locator(".tm-scheduler-timeline-event")).ToHaveCountAsync(1);
        await Assertions.Expect(anchor).ToBeVisibleAsync();
        await Assertions.Expect(anchor).ToHaveTextAsync("Range Anchor");

        await card.Locator("[data-testid='scheduler-next']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Tuesday);
        await card.Locator("[data-testid='scheduler-prev']").ClickAsync();
        await Assertions.Expect(title).ToHaveTextAsync(Monday);
        await SaveCardAsync(card, "ranges-timeline-day");

        await card.Locator("[role='tab'][data-view='Agenda']").ClickAsync();
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

        await card.Locator("[role='tab'][data-view='Week']").ClickAsync();
        await Assertions.Expect(card.Locator(".tm-scheduler-toolbar-title-text")).ToHaveTextAsync("June 14–20, 2026");
        await SaveCardAsync(card, "ranges-sunday-first");
    }

    /// <summary>
    /// The toolbar section: a date pick from the title popover, a restricted view set, More with and
    /// without Print, a hidden header, and a day plus an event selection.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Toolbar_DatePopover_Views_More_And_Selection()
    {
        var page = await OpenAsync();
        var card = page.Locator("[data-testid='scheduler-toolbar-section']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await card.ScrollIntoViewIfNeededAsync();

        var tabs = card.Locator(".tm-scheduler").First.Locator("[role='tab']");
        await Assertions.Expect(tabs).ToHaveCountAsync(2);
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("data-view", "Month");
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("data-view", "Day");

        var scheduler = card.Locator(".tm-scheduler").First;
        await scheduler.Locator(".tm-scheduler-toolbar-title [role='button']").ClickAsync();
        var calendar = scheduler.Locator(".tm-calendar-view");
        await calendar.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await calendar.Locator("[data-date='2026-06-20']").ClickAsync(
            new LocatorClickOptions { Force = true });
        await Assertions.Expect(calendar).ToHaveCountAsync(0);
        await Assertions.Expect(scheduler.Locator(".tm-scheduler-toolbar-title-text")).ToContainTextAsync("June 2026");

        await Assertions.Expect(scheduler.Locator("button.tm-toolbar-more")).ToBeVisibleAsync();

        await card.Locator(".tm-toggle-track").Nth(0).ClickAsync();
        await Assertions.Expect(scheduler.Locator("button.tm-toolbar-more")).ToHaveCountAsync(0);

        await card.Locator(".tm-toggle-track").Nth(1).ClickAsync();
        await Assertions.Expect(card.Locator(".tm-scheduler").First.Locator(".tm-scheduler-toolbar")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The title popover from the keyboard: Tab reaches the trigger, Enter opens it and moves focus
    /// into the calendar, Escape returns focus to the trigger, and picking a day with Enter returns
    /// focus and changes the date. axe must stay clean while the popover is open.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Toolbar_DatePopover_Keyboard_And_Axe()
    {
        var page = await OpenAsync();
        var card = page.Locator("[data-testid='scheduler-toolbar-section']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        var scheduler = card.Locator(".tm-scheduler").First;
        await scheduler.ScrollIntoViewIfNeededAsync();

        var trigger = scheduler.Locator(".tm-scheduler-toolbar-title [role='button']");
        await trigger.ScrollIntoViewIfNeededAsync();
        await scheduler.Locator("[data-testid='scheduler-next']").FocusAsync();
        // The title sits inside a toolbar item, so it joins the toolbar's roving set: Tab leaves
        // the toolbar, ArrowRight moves onto the date trigger.
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(trigger).ToBeFocusedAsync();

        var titleBefore = await scheduler.Locator(".tm-scheduler-toolbar-title-text").InnerTextAsync();
        await page.Keyboard.PressAsync("Enter");
        var calendar = scheduler.Locator(".tm-calendar-view");
        await calendar.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await Assertions.Expect(calendar.Locator(".tm-cal-day--selected, .tm-cal-day--today").First).ToBeFocusedAsync();

        var violations = await AxeAsync(page);
        Assert.AreEqual(0, violations.Length,
            "open date popover:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(calendar).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Enter");
        await calendar.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await calendar.Locator(".tm-cal-next").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var nextMonth = calendar.Locator(".tm-cal-day:not(.tm-cal-day--other-month)").First;
        await nextMonth.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(calendar).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await Assertions.Expect(scheduler.Locator(".tm-scheduler-toolbar-title-text")).Not.ToHaveTextAsync(titleBefore);
    }

    /// <summary>A one-item view set and a view missing from the set each keep a single tab stop.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Toolbar_OneView_And_InsertedView_Are_One_Tab_Stop()
    {
        var page = await OpenAsync();

        var one = page.Locator("[data-testid='scheduler-toolbar-one-view']");
        await one.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await Assertions.Expect(one.Locator("[role='tab']")).ToHaveCountAsync(1);
        await Assertions.Expect(one.Locator("[role='tab'][tabindex='0']")).ToHaveCountAsync(1);
        await Assertions.Expect(one.Locator("[role='tab']")).ToHaveAttributeAsync("data-view", "Week");

        var inserted = page.Locator("[data-testid='scheduler-toolbar-inserted-view']");
        var tabs = inserted.Locator("[role='tab']");
        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("data-view", "Week");
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(inserted.Locator("[role='tab'][tabindex='0']")).ToHaveCountAsync(1);
    }

    private static async Task<string[]> AxeAsync(IPage page)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });
        return await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('.tm-overlay-panel:popover-open') || document.body;
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.nodes.length > 0 && (v.impact === 'critical' || v.impact === 'serious'))
                    .map(v => `${v.impact}: ${v.id} - ${v.help} [${(v.nodes[0] && v.nodes[0].target) || ''}]`);
            }
            """);
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
