using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for <c>ColumnMenuMode.Menu</c>: the kebab lives in the data column header, its menu offers
/// only what the column can do, and M / Escape drive it. bUnit covers the render contract; this
/// proves the overlay actually anchors and opens in a browser.
/// </summary>
[TestClass]
public class TmDataTableColumnMenuE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColumnMenu_OpensFromHeader_Sorts_And_ClosesOnEscape()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/data-table",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator("[data-testid='dt-column-menu-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var kebab = section.Locator(".tm-data-table-col-menu[data-column-key='Name']");
        await kebab.WaitForAsync();
        Assert.AreEqual("Column options: Name", await kebab.GetAttributeAsync("aria-label"));
        Assert.AreEqual(0, await section.Locator(".tm-col-pin-btn").CountAsync(),
            "Menu mode replaces the standalone pin button");

        await kebab.ClickAsync();

        var menu = section.Locator("[role='menu']");
        await menu.WaitForAsync();
        var actions = await menu.Locator("[role^='menuitem']").EvaluateAllAsync<string[]>(
            "items => items.map(i => i.getAttribute('data-action'))");
        CollectionAssert.AreEquivalent(
            new[] { "sort-asc", "sort-desc", "pin-left", "pin-right", "group", "hide", "filter" },
            actions);
        Assert.AreEqual("menuitemradio", await menu.Locator("[data-action='sort-asc']").GetAttributeAsync("role"));

        // ArrowDown walks the menu (WAI-ARIA APG): focus leaves the first item for the second.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-action') === 'sort-asc'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-action') === 'sort-desc'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        await menu.Locator("[data-action='sort-asc']").ClickAsync();
        await section.Locator("th[aria-sort='ascending']").WaitForAsync();

        await kebab.ClickAsync();
        await menu.WaitForAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.AreEqual(0, await section.Locator("[role='menu']").CountAsync());

        // Escape returns focus to the column header (its sort button), not to <body>.
        var header = section.Locator("th:has(.tm-data-table-col-menu[data-column-key='Name']) .tm-th-sort");
        await Assertions.Expect(header).ToBeFocusedAsync(new() { Timeout = 5000 });
    }
}
