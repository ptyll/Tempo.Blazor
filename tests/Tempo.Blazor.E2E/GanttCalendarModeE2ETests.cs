using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Manual-verification replacement for the Gantt calendar mode after the B14/B24 scheduler
/// range fixes: the embedded scheduler renders with the time grid hidden, and its own
/// view binding and date navigation still move it. The card screenshot is written to
/// <c>__screenshots__/scheduler/</c> only when <c>TM_E2E_UPDATE_BASELINE=1</c>; otherwise it lands
/// in the git-ignored TestResults folder.
/// </summary>
[TestClass]
public class GanttCalendarModeE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Gantt_CalendarMode_RendersScheduler_AndNavigates()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/gantt",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);

        await page.Locator("button", new PageLocatorOptions { HasText = "Calendar" }).First.ClickAsync();

        var card = page.Locator("[data-testid='gantt-calendar-view']");
        await card.Locator(".tm-scheduler").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        Assert.AreEqual(0, await card.Locator(".tm-scheduler-timegrid-body").CountAsync(),
            "the Gantt calendar mode hides the hourly time grid");

        var title = card.Locator(".tm-scheduler-toolbar-title");
        var before = await title.InnerTextAsync();
        await card.Locator("[data-testid='scheduler-next']").ClickAsync();
        await Assertions.Expect(title).Not.ToHaveTextAsync(before);

        await SaveScreenshotAsync(page, "gantt-calendar-mode");
    }

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            UpdateBaseline ? Path.Combine("__screenshots__", "scheduler") : Path.Combine("TestResults", "scheduler"));
        Directory.CreateDirectory(dir);
        var card = page.Locator("[data-testid='gantt-calendar-view']");
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png") });
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
