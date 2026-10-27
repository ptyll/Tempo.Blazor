using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// E2E for the scheduler time-grid phase on the Scheduler demo page (WASM demo at 7106):
/// a work week clipped to 08:00-17:00 scrolls to the requested time, and a resize inside that
/// clipped range uses the visible minutes, not a hard-coded 24 hour day.
/// Shots are rewritten into <c>__screenshots__/scheduler/</c> only with
/// <c>TM_E2E_UPDATE_BASELINE=1</c>; otherwise they land in TestResults.
/// </summary>
[TestClass]
public class SchedulerRangeE2ETests : WasmTestBase
{
    private const string SchedulerPage = "/scheduler";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private async Task<IPage> OpenAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{SchedulerPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_ScrollToTime_Shows_The_Requested_Hour()
    {
        var page = await OpenAsync();
        var section = page.Locator("[data-testid='scheduler-range-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var grid = section.Locator(".tm-scheduler-timegrid");
        await grid.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);

        var range = await grid.GetAttributeAsync("data-range-minutes");
        Assert.AreEqual("540", range, "A work day clipped to 08:00-17:00 is 540 minutes.");
        Assert.AreEqual("480", await grid.GetAttributeAsync("data-range-start"));

        // ScrollToTime=08:00 puts the first visible hour at the top of the scroller.
        var scrollTop = await section.Locator("[data-tm-scroll='timegrid']").EvaluateAsync<double>("el => el.scrollTop");
        Assert.IsTrue(scrollTop < 40, $"The scroller should start at 08:00, but scrollTop was {scrollTop}.");

        var hours = await section.Locator(".tm-scheduler-timegrid-body .tm-scheduler-timegrid-time-label").AllInnerTextsAsync();
        // The gutter uses the culture short time, the same format the events use.
        CollectionAssert.DoesNotContain(hours.Select(h => h.Trim()).ToList(), "7:00 AM");
        CollectionAssert.Contains(hours.Select(h => h.Trim()).ToList(), "8:00 AM");

        await SaveScreenshotAsync(page, "range-scroll-to-0800");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_Resize_In_Limited_Range_Uses_Visible_Minutes()
    {
        var page = await OpenAsync();
        var section = page.Locator("[data-testid='scheduler-range-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var grid = section.Locator(".tm-scheduler-timegrid");
        await grid.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var standup = section.Locator(".tm-scheduler-timegrid-event", new LocatorLocatorOptions { HasText = "Morning Standup" });
        await standup.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        var before = await standup.BoundingBoxAsync();
        Assert.IsNotNull(before, "The 09:00 standup must render inside the clipped range.");

        var handle = standup.Locator(".tm-scheduler-event-resize-handle").Last;
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box, "The resize handle must be reachable.");

        // Drag the bottom edge down by about an hour. The clipped range is 540 minutes, so the
        // event grows by roughly an hour, not by the 1440/540 ratio a hard-coded day would give.
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + 70, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var after = await standup.BoundingBoxAsync();
        Assert.IsNotNull(after);
        Assert.IsTrue(after.Height > before.Height + 20,
            $"The resize should grow the event, before {before.Height:F0}px after {after.Height:F0}px.");

        await SaveScreenshotAsync(page, "range-resize");
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            UpdateBaseline
                ? Path.Combine("__screenshots__", "scheduler")
                : Path.Combine("TestResults", "scheduler"));
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
