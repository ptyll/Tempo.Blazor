using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class DataTablePhase4ScreenshotTests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_CapturesGroupedExportAndTablet()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(BaseUrl + "/data-table");
        await page.WaitForSelectorAsync(".tm-data-table", new PageWaitForSelectorOptions { Timeout = 30000 });

        await Shot(page, "[data-testid='datatable-rowattrs-grouped']", "grouped-desktop-1440.png");
        await page.Locator("[data-testid='export-table'] .tm-data-table__export button").First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-data-table__export-item");
        await Shot(page, "[data-testid='export-table']", "export-menu-desktop-1440.png");

        await page.SetViewportSizeAsync(1024, 800);
        await Shot(page, "[data-testid='dt-ergonomics-section'] .tm-data-table-wrapper", "tablet-1024.png");
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
