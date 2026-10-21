using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 13 (13.1): this class used to write full-page PNGs picked with <c>.First</c>, asserting nothing.
/// It now asserts the STATE each of those pictures claimed to show (grouped rows present, export menu
/// open with its scope switch, the tablet ergonomics table laid out without overflow). The PNG baselines
/// moved to <see cref="DataTableRedesignBaselineScreenshots"/>, which is gated by TM_WRITE_BASELINES.
/// </summary>
[TestClass]
public sealed class DataTablePhase4ScreenshotTests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_GroupedRowsExportMenuAndTabletLayout_AreInTheExpectedState()
    {
        var page = await OpenAsync(Browser, BaseUrl, "/data-table", new Options(1440, 900), RegisterContext);

        var grouped = page.Locator("[data-testid='datatable-rowattrs-grouped']");
        await grouped.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(grouped.Locator("tr.tm-data-table-group-row").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var export = page.Locator("[data-testid='export-table']");
        await export.ScrollIntoViewIfNeededAsync();
        await export.Locator(".tm-data-table__export .tm-dropdown-trigger").ClickAsync();
        var menu = page.Locator(".tm-dropdown-menu");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await Assertions.Expect(menu.Locator("[data-export-scope='all']")).ToBeVisibleAsync();
        await Assertions.Expect(menu.Locator("[data-export-format='csv']")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");

        await SetViewportAsync(page, 1024, 800);
        var section = page.Locator("[data-testid='dt-ergonomics-section'] .tm-data-table-wrapper");
        await section.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(section.Locator("thead th.tm-col-pinned-left").First).ToBeVisibleAsync();
        var box = await BoxAsync(section);
        Assert.IsTrue(box.Width <= 1024, "the table fits the tablet viewport");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"), "no page-level horizontal overflow");
    }
}
