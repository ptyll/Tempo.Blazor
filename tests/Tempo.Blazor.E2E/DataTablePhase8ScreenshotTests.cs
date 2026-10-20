using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class DataTablePhase8ScreenshotTests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase8_CapturesDesktopGroupedAndMultiViewCards()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1440, 900);

        await page.GotoAsync(BaseUrl + "/data-table");
        await page.WaitForSelectorAsync(".tm-data-table", new PageWaitForSelectorOptions { Timeout = 30000 });
        await Shot(page, "[data-testid='dt-ergonomics-section'] .tm-data-table-wrapper", "desktop-table-1440.png");
        await Shot(page, "[data-testid='datatable-rowattrs-grouped']", "grouped-table-1440.png");

        await page.GotoAsync(BaseUrl + "/multi-view-list");
        await page.WaitForSelectorAsync(".tm-multi-view-list", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.Locator(".tm-multi-view-list").Nth(1).ScrollIntoViewIfNeededAsync();
        var projects = page.Locator(".tm-multi-view-list").Nth(1);
        var cardsButton = projects.Locator("button").Filter(new LocatorFilterOptions { HasText = "Cards" });
        if (await cardsButton.CountAsync() > 0)
            await cardsButton.First.ClickAsync();
        await Shot(page, ".tm-multi-view-list", "multi-view-list-cards-1440.png");
    }

    private static async Task Shot(IPage page, string selector, string name)
    {
        var directory = Path.Combine(FindRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table-redesign");
        Directory.CreateDirectory(directory);
        await page.Locator(selector).First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, name),
            Timeout = 30000
        });
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
