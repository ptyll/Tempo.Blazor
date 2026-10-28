using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// E2E for TmScheduler K3 on the Scheduler demo page (WASM demo at 7106): timezone display
/// (Europe/Prague vs America/New_York), a valid ICS download, and the print-media layout.
/// Committed shots in <c>__screenshots__/scheduler/</c> are rewritten only with
/// <c>TM_E2E_UPDATE_BASELINE=1</c>; otherwise they land in the git-ignored TestResults folder.
/// </summary>
[TestClass]
public class SchedulerTimezoneE2ETests : WasmTestBase
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
    public async Task Scheduler_Timezone_RendersAndExportsValidIcs()
    {
        var page = await OpenAsync();

        var section = page.Locator("[data-testid='scheduler-tz-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await section.Locator(".tm-scheduler").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "timezone-prague");

        // The same events shown in a UTC-4 context (New York) — offsets/positions shift.
        await section.Locator("[data-testid='scheduler-tz-select']").SelectOptionAsync("America/New_York");
        await page.WaitForTimeoutAsync(600);
        await SaveScreenshotAsync(page, "timezone-newyork");

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await section.Locator("[data-testid='scheduler-export-ics']").ClickAsync();
        });

        StringAssert.EndsWith(download.SuggestedFilename, ".ics");
        var path = await download.PathAsync();
        Assert.IsNotNull(path);
        var content = await File.ReadAllTextAsync(path);
        StringAssert.Contains(content, "BEGIN:VCALENDAR");
        StringAssert.Contains(content, "BEGIN:VEVENT");
        StringAssert.Contains(content, "RRULE:");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Scheduler_PrintMedia_HidesNavChrome()
    {
        var page = await OpenAsync();

        var section = page.Locator("[data-testid='scheduler-tz-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print });
        await page.WaitForTimeoutAsync(400);
        await SaveScreenshotAsync(page, "print-preview");

        var nav = section.Locator(".tm-scheduler-toolbar-nav").First;
        Assert.IsFalse(await nav.IsVisibleAsync(), "Print CSS should hide the scheduler toolbar navigation.");
        Assert.IsFalse(await section.Locator(".tm-scheduler-toolbar-views").First.IsVisibleAsync(),
            "Print CSS should hide the view switcher.");
        Assert.IsFalse(await section.Locator(".tm-scheduler-toolbar-overflow").First.IsVisibleAsync(),
            "Print CSS should hide the More menu.");
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
