using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 13 (13.1): the old class chose the multi-view list's card mode by button TEXT and fell through
/// silently when the button was missing, so "multi-view-list-cards-1440.png" showed a table. It now
/// switches with <c>.tm-mvl-switch-card</c> and asserts the card grid is there. PNGs are produced by
/// <see cref="DataTableRedesignBaselineScreenshots"/>.
/// </summary>
[TestClass]
public sealed class DataTablePhase8ScreenshotTests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase8_GroupedTableAndMultiViewCards_AreInTheExpectedState()
    {
        var page = await OpenAsync(Browser, BaseUrl, "/data-table", new Options(1440, 900), RegisterContext);
        await Assertions.Expect(page.Locator("[data-testid='dt-ergonomics-section'] .tm-data-table-wrapper")).ToBeVisibleAsync();
        var grouped = page.Locator("[data-testid='datatable-rowattrs-grouped']");
        await grouped.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(grouped.Locator("tr.tm-data-table-group-row").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var list = await OpenAsync(Browser, BaseUrl, "/multi-view-list", new Options(1440, 900), RegisterContext);
        var cards = list.Locator("[data-testid='mvl-redesign-list']");
        await cards.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(cards.Locator(".tm-mvl-card-grid")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await cards.Locator(".tm-mvl-switch-card").ClickAsync();
        await Assertions.Expect(cards.Locator(".tm-mvl-card-grid")).ToBeVisibleAsync();
        Assert.AreEqual(3, await cards.Locator(".tm-mvl-card").CountAsync(), "three people render as three cards");
        Assert.AreEqual(0, await cards.Locator("table").CountAsync(), "card mode shows no table");
    }
}
