using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// The day-granularity timeline on the scheduler demo page: resource rows × the days of the week
/// (the "Channels × days" card), the horizontal scroll that reveals the first event's day, and the
/// regression guard for the event time in a narrow column (a week cell or a week of resources used
/// to cut "9:00 AM – 10:00 AM" mid-token at the card's edge).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SchedulerTimelineDaysE2ETests : WasmTestBase
{
    private const string BoardSection = "[data-testid='scheduler-timeline-days-section']";
    private const string Board = BoardSection + " [data-tm-scroll='timeline-days']";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    [TestMethod]
    public async Task Day_Board_Renders_One_Row_Per_Channel_And_One_Column_Per_Day()
    {
        var (page, errors) = await OpenAsync(1440, 1100);
        var board = page.Locator(Board);
        await board.ScrollIntoViewIfNeededAsync();

        Assert.AreEqual("days", await board.GetAttributeAsync("data-scale"));
        Assert.AreEqual(4, await board.Locator(".tm-scheduler-timeline-row").CountAsync(),
            "one row per channel");
        Assert.AreEqual(7, await board.Locator(".tm-scheduler-timeline-day-header").CountAsync(),
            "one column per day of the week");

        var headers = board.Locator(".tm-scheduler-timeline-day-header");
        StringAssert.Contains(await headers.First.InnerTextAsync(), "Mon");
        StringAssert.Contains(await headers.Last.InnerTextAsync(), "Sun");

        // A chip carries the time and the title; the topic row's overlap takes two lanes.
        var chips = board.Locator(".tm-scheduler-timeline-day-event");
        Assert.IsTrue(await chips.CountAsync() >= 8, "the board paints every event of the week");
        var lanes = board.Locator(".tm-scheduler-timeline-day-event[data-lane='1']");
        Assert.IsTrue(await lanes.CountAsync() >= 1,
            "an overlap paints in the second lane (the paid row on Tuesday)");
        Assert.AreEqual(1, await board.Locator(
                ".tm-scheduler-timeline-day-cell[data-resource='paid'] .tm-scheduler-timeline-day-event[data-lane='1']")
            .CountAsync(), "the paid row's own overlap is the one lane-two chip on that row");

        // Four e-mail events on Thursday and the per-cell cap of three: "+1 more".
        var more = board.Locator(".tm-scheduler-timeline-day-more");
        Assert.AreEqual(1, await more.CountAsync());
        Assert.AreEqual("+1 more", (await more.First.InnerTextAsync()).Trim());
        StringAssert.Contains(await more.First.GetAttributeAsync("aria-label") ?? "", "1 more event");

        // The four-day campaign paints on each day it covers: three of them are continuations.
        Assert.AreEqual(4, await board.Locator(".tm-scheduler-timeline-day-event[data-event-id='c4']").CountAsync(),
            "the campaign paints on four days");
        Assert.AreEqual(3, await board.Locator(
                ".tm-scheduler-timeline-day-event[data-event-id='c4'].tm-scheduler-timeline-day-event--continuation").CountAsync(),
            "the days after its start carry the continuation edge");

        // A chip's time never breaks between the clock and its meridiem: U+00A0, not a plain space
        // (the full reading is the span the stylesheet shows at this width).
        var full = (await board.Locator(".tm-scheduler-event-time-full").First.InnerTextAsync()).Trim();
        StringAssert.Contains(full, "\u00A0", "the meridiem stays joined to its time");

        // The scroll-edge shadow only exists while the board actually overflows.
        var overflows = await board.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
        var shadow = await board.EvaluateAsync<string>(
            "el => el.parentElement && el.parentElement.getAttribute('data-scroll-shadow')");
        if (overflows)
            Assert.IsTrue(shadow is "start" or "end" or "both",
                $"an overflowing board names its shadowed edges (got '{shadow}')");
        else
            Assert.IsTrue(shadow is null or "none", "a board that fits paints no edge");

        await AssertNoBlazorErrorsAsync(errors);
    }

    [TestMethod]
    public async Task Tablet_Toolbar_Keeps_The_Date_Title_And_Collapses_The_View_Buttons()
    {
        // 834 px is the tablet band: the board is ~750 px of toolbar, too narrow for the date title
        // AND five view pills. The title has priority; the pills become the compact dropdown.
        var (page, errors) = await OpenAsync(834, 1112);
        var section = page.Locator(BoardSection);
        await section.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        var toolbar = section.Locator(".tm-scheduler-toolbar");
        var barBox = (await toolbar.BoundingBoxAsync())!;

        var title = toolbar.Locator(".tm-scheduler-toolbar-title-text").First;
        await Assertions.Expect(title).ToBeVisibleAsync();
        var titleText = (await title.InnerTextAsync()).Trim();
        Assert.IsFalse(string.IsNullOrEmpty(titleText), "the date title keeps its text");
        var titleBox = (await title.BoundingBoxAsync())!;
        Assert.IsTrue(titleBox.Width > 40, $"the date title is not squeezed to nothing (got {titleBox.Width}px)");
        Assert.IsTrue(titleBox.X >= barBox.X - 1 && titleBox.X + titleBox.Width <= barBox.X + barBox.Width + 1,
            "the date title sits inside the toolbar");

        // The pill tablist is gone; the compact view dropdown is the switcher and its label is whole.
        await Assertions.Expect(toolbar.Locator("[role='tablist']")).ToBeHiddenAsync();
        var view = toolbar.Locator("[data-testid='scheduler-view']").First;
        await Assertions.Expect(view).ToBeVisibleAsync();
        var viewBox = (await view.BoundingBoxAsync())!;
        Assert.IsTrue(viewBox.X >= barBox.X - 1 && viewBox.X + viewBox.Width <= barBox.X + barBox.Width + 1,
            $"the view trigger stays inside the toolbar ({viewBox.X}+{viewBox.Width} vs {barBox.X}+{barBox.Width})");
        Assert.IsFalse(await view.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1"),
            "the active view label is not clipped");
        StringAssert.Contains(await view.InnerTextAsync(), "Timeline");

        await AssertNoBlazorErrorsAsync(errors);
    }

    [TestMethod]
    public async Task ScrollToTime_Reveals_The_First_Event_Day()
    {
        var (page, errors) = await OpenAsync(390, 844);
        var board = page.Locator(Board);
        await board.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);

        // Monday is empty on the board, so the first event is Tuesday's and the scroll has to
        // move: the pinned channel column stays put and the Tuesday cell comes into view.
        var scrolled = await board.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(scrolled > 0, $"the board scrolls to the first event's day (scrollLeft {scrolled})");

        var chip = board.Locator(".tm-scheduler-timeline-day-event").First;
        var chipBox = (await chip.BoundingBoxAsync())!;
        var labelBox = (await board.Locator(".tm-scheduler-timeline-resource-header").BoundingBoxAsync())!;
        var boardBox = (await board.BoundingBoxAsync())!;
        Assert.IsTrue(chipBox.X >= labelBox.X + labelBox.Width - 1,
            "the revealed chip clears the pinned channel column");
        Assert.IsTrue(chipBox.X + chipBox.Width <= boardBox.X + boardBox.Width + 1,
            "and it is inside the board's viewport");

        await AssertNoBlazorErrorsAsync(errors);
    }

    [TestMethod]
    public async Task ScrollToTime_Still_Scrolls_The_Week_To_The_Hour()
    {
        var (page, errors) = await OpenAsync(1440, 1100);
        var week = page.Locator("[data-testid='scheduler-week-section'] .tm-scheduler");
        await week.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);

        var scrollTop = await week.Locator(".tm-scheduler-timegrid-scroll").EvaluateAsync<double>("el => el.scrollTop");
        Assert.IsTrue(scrollTop > 0, $"the week scrolled 08:00 into view (scrollTop {scrollTop})");

        await AssertNoBlazorErrorsAsync(errors);
    }

    [TestMethod]
    public async Task Week_And_Resource_Column_Times_Are_Not_Clipped()
    {
        foreach (var (width, height) in new[] { (1440, 1100), (834, 1112), (390, 844) })
        {
            var (page, errors) = await OpenAsync(width, height);

            var week = page.Locator("[data-testid='scheduler-week-section'] .tm-scheduler");
            await week.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(300);
            await SaveNamedAsync(week, $"times-week-{width}");
            await AssertTimesFitAsync(week, width, requireVisibleTime: width >= 1440);

            // Four resource columns in one day: the narrow-column case with the room named.
            var day = page.Locator("[data-testid='scheduler-resource-day-section'] .tm-scheduler");
            await day.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(300);
            await SaveNamedAsync(day, $"times-day-{width}");
            await AssertTimesFitAsync(day, width, requireVisibleTime: true);

            await AssertNoBlazorErrorsAsync(errors);
        }
    }

    [TestMethod]
    public async Task Day_Board_Desktop_Tablet_Mobile_LightAndDark()
    {
        foreach (var (width, height) in new[] { (1440, 1100), (834, 1112), (390, 844) })
        {
            var (page, errors) = await OpenAsync(width, height);
            var section = page.Locator(BoardSection);
            await section.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(400);
            await SaveAsync(section, width, "light");

            await SetDarkAsync(page);
            await SaveAsync(section, width, "dark");

            await AssertNoBlazorErrorsAsync(errors);
        }
    }

    /// <summary>
    /// Every visible event time has to fit inside its card: a card that is too narrow drops the
    /// time instead of cutting it, and a card that keeps it wraps the range.
    /// </summary>
    private static async Task AssertTimesFitAsync(ILocator scheduler, int width, bool requireVisibleTime)
    {
        var cards = scheduler.Locator(".tm-scheduler-timegrid-event");
        var count = await cards.CountAsync();
        Assert.IsTrue(count > 0, "the section renders events");

        var checkedTimes = 0;
        for (var i = 0; i < count; i++)
        {
            var card = cards.Nth(i);
            if (!await card.IsVisibleAsync()) continue;
            var cardBox = await card.BoundingBoxAsync();
            if (cardBox is null) continue;

            var time = card.Locator(".tm-scheduler-timegrid-event-time");
            if (await time.CountAsync() == 0) continue;
            var box = await time.First.BoundingBoxAsync();
            if (box is null || box.Width < 1) continue;   // dropped by the narrow-card rule

            checkedTimes++;
            var overflows = await time.First.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
            Assert.IsFalse(overflows, $"card {i} cuts its time horizontally");
            Assert.IsTrue(box.X >= cardBox.X - 1 && box.X + box.Width <= cardBox.X + cardBox.Width + 1,
                $"card {i} paints its time outside the card");

            var text = (await time.First.InnerTextAsync()).Trim();
            Assert.IsFalse(string.IsNullOrEmpty(text), $"card {i} renders no time at all");
            var title = await time.First.GetAttributeAsync("title");
            Assert.IsFalse(string.IsNullOrEmpty(title), $"card {i} keeps the full range in its title");
            StringAssert.Contains(title!, text.Split('–')[0].Trim(),
                $"the title carries the range the card shows (card {i})");
        }

        if (requireVisibleTime)
            Assert.IsTrue(checkedTimes > 0,
                $"at least one card is wide enough to show its time (viewport {width}, {count} cards, {checkedTimes} checked)");
    }

    private async Task<(IPage Page, List<string> Errors)> OpenAsync(int width, int height)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception", StringComparison.Ordinal))
                errors.Add(msg.Text);
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}/scheduler");
        await page.Locator(BoardSection).WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        return (page, errors);
    }

    private static async Task AssertNoBlazorErrorsAsync(List<string> errors)
        => Assert.AreEqual(0, errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", errors));

    /// <summary>
    /// Dark through the app's OWN ThemeService toggle (the header's theme button), not by writing
    /// data-theme by hand: only the service flips the demo shell to its dark surface classes, and a
    /// hand-written attribute left the section on the light background — a "dark" screenshot whose
    /// top half was white. The assertion below is that the resolved surface is actually dark.
    /// </summary>
    private static async Task SetDarkAsync(IPage page)
    {
        var toggle = page.Locator("[data-testid='theme-toggle']:visible").First;
        await toggle.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.demo-shell')?.getAttribute('data-theme') === 'dark'");
        await page.WaitForTimeoutAsync(400);

        var dark = await page.EvaluateAsync<bool>(
            """
            () => {
                const card = document.querySelector("[data-testid='scheduler-timeline-days-section'] .showcase-card");
                if (!card) return false;
                const parts = getComputedStyle(card).backgroundColor.match(/[\d.]+/g) || [];
                if (parts.length < 3) return false;
                const [r, g, b] = parts.map(Number);
                return (r + g + b) / 3 < 128;
            }
            """);
        Assert.IsTrue(dark, "the board's surface resolves to the dark tokens");
    }

    /// <summary>
    /// Review evidence for the narrow-column guard. Always a run artefact (never a committed
    /// reference): the three widths are the point, not the pixels.
    /// </summary>
    private static async Task SaveNamedAsync(ILocator element, string file)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            Path.Combine("TestResults", "scheduler", "timeline-days"));
        Directory.CreateDirectory(dir);
        await element.ScreenshotAsync(
            new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{file}.png") });
    }

    private static async Task SaveAsync(ILocator section, int width, string theme)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            UpdateBaseline
                ? Path.Combine("__screenshots__", "scheduler", "timeline-days")
                : Path.Combine("TestResults", "scheduler", "timeline-days"));
        Directory.CreateDirectory(dir);
        await section.ScreenshotAsync(
            new LocatorScreenshotOptions { Path = Path.Combine(dir, $"board-{width}-{theme}.png") });
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

/// <summary>The same board on the Blazor Server demo (:7107): a circuit round trip per event.</summary>
[TestClass]
[TestCategory("Server")]
public sealed class SchedulerTimelineDaysServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task Day_Board_Renders_On_The_Server_Host()
    {
        await OpenBoardAsync();
    }

    private async Task OpenBoardAsync() => await SchedulerTimelineDaysSmoke.RunAsync(
        BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The same board on the InteractiveAuto demo (:7108, WASM after the first render).</summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class SchedulerTimelineDaysInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    public async Task Day_Board_Renders_On_The_InteractiveAuto_Host()
    {
        await OpenBoardAsync();
    }

    private async Task OpenBoardAsync() => await SchedulerTimelineDaysSmoke.RunAsync(
        BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The host-independent smoke assertions both non-WASM lanes run.</summary>
internal static class SchedulerTimelineDaysSmoke
{
    private const string BoardSection = "[data-testid='scheduler-timeline-days-section']";
    private const string Board = BoardSection + " [data-tm-scroll='timeline-days']";

    public static async Task RunAsync(
        string baseUrl,
        Func<Task<IBrowserContext>> createContext,
        Func<IPage, string, Task> gotoReady)
    {
        var context = await createContext();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);

        await gotoReady(page, $"{baseUrl}/scheduler");
        var board = page.Locator(Board);
        await board.WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        await board.ScrollIntoViewIfNeededAsync();

        Assert.AreEqual(4, await board.Locator(".tm-scheduler-timeline-row").CountAsync());
        Assert.AreEqual(7, await board.Locator(".tm-scheduler-timeline-day-header").CountAsync());
        Assert.IsTrue(await board.Locator(".tm-scheduler-timeline-day-event").CountAsync() >= 8);

        await board.Locator(".tm-scheduler-timeline-day-event").First.ClickAsync();
        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
    }
}
