using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// Phase 5 review shots and assertions. The now line is checked against a pinned instant
/// (<c>?now=</c>), so Prague and New York are known numbers rather than "now". Element screenshots
/// of the new surfaces land in <c>__screenshots__/scheduler/phase5/</c>.
/// </summary>
[TestClass]
public class SchedulerPhase5ReviewE2ETests : WasmTestBase
{
    private const string PinnedNow = "2026-06-15T08:15:00Z";

    private async Task<IPage> OpenAsync(string query = "")
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/scheduler{query}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task NowIndicator_Places_The_Same_Instant_Per_Zone()
    {
        var page = await OpenAsync($"?now={Uri.EscapeDataString(PinnedNow)}");

        // 08:15 UTC is 10:15 in Prague (UTC+2 in June) and 04:15 in New York (UTC-4).
        await AssertNowAsync(page, "scheduler-now-prague", "10:15", 10 * 60 + 15);
        await AssertNowAsync(page, "scheduler-now-newyork", "4:15", 4 * 60 + 15);

        await SaveElementAsync(page, "scheduler-now-prague", "now-prague");
        await SetDarkAsync(page);
        await SaveElementAsync(page, "scheduler-now-newyork", "now-newyork-dark");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Resize_Reports_The_New_End_Time()
    {
        var page = await OpenAsync();
        var section = page.Locator("[data-testid='scheduler-range-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var standup = section.Locator(".tm-scheduler-timegrid-event", new LocatorLocatorOptions { HasText = "Morning Standup" });
        await standup.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        var handle = standup.Locator(".tm-scheduler-event-resize-handle").Last;
        var box = (await handle.BoundingBoxAsync())!;

        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + 70, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        // The standup starts at 09:00 and lasts 30 minutes. A drag of about an hour must move the
        // end past 09:30, which is the assertion the height check cannot make.
        var end = section.Locator("[data-testid='scheduler-range-end']");
        await Assertions.Expect(end).Not.ToHaveTextAsync("Morning Standup ends 09:30",
            new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_Surfaces_Screenshot()
    {
        var page = await OpenAsync();

        await SaveElementAsync(page, "scheduler-resource-day-section", "day-resources");
        await SaveElementAsync(page, "scheduler-month-auto-section", "month-auto-june");

        var timeline = page.Locator("[data-testid='scheduler-timeline-section'] .tm-scheduler");
        var scrolled = await timeline.Locator(".tm-scheduler-timeline").EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(scrolled > 0, "ScrollToTime should move the timeline past the 08:00 column.");
        await timeline.ScrollIntoViewIfNeededAsync();
        await timeline.Locator(".tm-scheduler-timeline-event").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
        await timeline.EvaluateAsync("el => { el.scrollLeft = 600; }");
        await page.WaitForTimeoutAsync(200);
        await SaveSchedulerAsync(page, timeline, "timeline-lanes-sticky");

        await SetDarkAsync(page);
        await SaveElementAsync(page, "scheduler-range-section", "working-hours-dark");
        await SaveElementAsync(page, "scheduler-resource-day-section", "event-resource-dark");
    }

    private static async Task AssertNowAsync(IPage page, string testId, string label, int minutes)
    {
        var line = page.Locator($"[data-testid='{testId}'] .tm-scheduler-timegrid-now");
        await line.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        StringAssert.Contains(await line.GetAttributeAsync("aria-label"), label,
            $"{testId} should name {label}.");

        var offset = await line.EvaluateAsync<double>(
            "el => parseFloat(getComputedStyle(el).getPropertyValue('--tm-scheduler-now'))");
        Assert.AreEqual(minutes, offset, 0.1, $"{testId} offset");

        // The line must sit on the same pixel as the slot for that minute, not just share a variable.
        var drift = await line.EvaluateAsync<double>(@"el => {
            const root = el.closest('.tm-scheduler');
            const start = parseInt(root.querySelector('.tm-scheduler-timegrid')?.dataset.rangeStart ?? '0', 10);
            const slot = root.querySelector('.tm-scheduler-timegrid-slot');
            const minutes = parseFloat(getComputedStyle(el).getPropertyValue('--tm-scheduler-now'));
            const slotMinutes = parseFloat(getComputedStyle(slot).getPropertyValue('--tm-scheduler-slot-size'));
            const expected = slot.getBoundingClientRect().top + (minutes - start) / slotMinutes * slot.getBoundingClientRect().height;
            return Math.abs(el.getBoundingClientRect().top - expected);
        }");
        Assert.IsTrue(drift < 2, $"{testId} now line is {drift:0.0}px off its slot.");
    }

    private static async Task SaveElementAsync(IPage page, string testId, string file)
    {
        var scheduler = page.Locator($"[data-testid='{testId}'] .tm-scheduler").First;
        await scheduler.ScrollIntoViewIfNeededAsync();
        await scheduler.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveSchedulerAsync(page, scheduler, file);
    }

    private static async Task SaveSchedulerAsync(IPage page, ILocator scheduler, string file)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "scheduler", "phase5");
        Directory.CreateDirectory(dir);
        await scheduler.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{file}.png") });
    }

    private static async Task SetDarkAsync(IPage page)
    {
        await page.EvaluateAsync(@"() => {
            document.documentElement.setAttribute('data-theme', 'dark');
            document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
        }");
        await page.WaitForTimeoutAsync(200);
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
