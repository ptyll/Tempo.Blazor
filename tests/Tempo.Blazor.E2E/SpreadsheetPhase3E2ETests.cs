using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3 E2E coverage: AutoFilter (toolbar toggle, header dropdown buttons, value filtering,
/// active badge, clear) and sorting (ascending/descending from the Data tab) against the live canvas
/// engine on the WASM demo's <c>/spreadsheet</c> page.
/// </summary>
public partial class SpreadsheetE2ETests
{
    private async Task<IPage> OpenPhase3DemoAsync()
    {
        var page = await CreatePageAsync();
        // Redesign phase 6: the spreadsheet chrome follows its CONTAINER, so a 1280-wide page
        // is the TABLET chrome (no ribbon tabs). These specs assert the DESKTOP ribbon.
        await page.SetViewportSizeAsync(1440, 960);
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);
        var grid = DemoGrid(page);
        await WaitForCanvasGridReadyAsync(page, grid);
        return page;
    }

    private static ILocator DataTab(IPage page)
        => DemoComponent(page).Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" });

    private static Task<int> FilterButtonCountAsync(ILocator grid)
        => grid.EvaluateAsync<int>("el => el.__tmSpreadsheetCanvas?.filterButtons?.length || 0");

    private static Task<bool> FilterButtonActiveAsync(ILocator grid, int col)
        => grid.EvaluateAsync<bool>(
            "(el, c) => (el.__tmSpreadsheetCanvas?.filterButtons || []).some(b => b.col === Number(c) && b.active)",
            col);

    private async Task SeedFilterRegionAsync(IPage page, ILocator grid)
    {
        // Use empty D column so the demo sample data does not interfere.
        await EditCanvasCellAsync(page, grid, "D1", "Fruit");
        await EditCanvasCellAsync(page, grid, "D2", "Apple");
        await EditCanvasCellAsync(page, grid, "D3", "Banana");
        await EditCanvasCellAsync(page, grid, "D4", "Apple");

        // Select D1:D4.
        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
    }

    private async Task EnableFilterAsync(IPage page, ILocator grid)
    {
        await DataTab(page).ClickAsync();
        await DemoComponent(page).Locator(".tm-spreadsheet-toolbar__button[title='Filter']").ClickAsync();
        await page.WaitForFunctionAsync(
            "el => (el.__tmSpreadsheetCanvas?.filterButtons?.length || 0) > 0",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    [TestMethod]
    public async Task AutoFilter_Toolbar_EnablesHeaderFilterButtons()
    {
        var page = await OpenPhase3DemoAsync();
        var grid = DemoGrid(page);

        await SeedFilterRegionAsync(page, grid);
        await EnableFilterAsync(page, grid);

        (await FilterButtonCountAsync(grid)).Should().BeGreaterThanOrEqualTo(1);
    }

    [TestMethod]
    public async Task AutoFilter_ClickHeaderButton_OpensDropdownWithValues()
    {
        var page = await OpenPhase3DemoAsync();
        var grid = DemoGrid(page);

        await SeedFilterRegionAsync(page, grid);
        await EnableFilterAsync(page, grid);

        await ClickFilterButtonAsync(page, grid, col: 3);

        var dropdown = page.Locator(".tm-spreadsheet-filter-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var markup = await dropdown.InnerTextAsync();
        StringAssert.Contains(markup, "Apple");
        StringAssert.Contains(markup, "Banana");
    }

    [TestMethod]
    public async Task AutoFilter_UncheckValue_AppliesAndPersistsFilter()
    {
        var page = await OpenPhase3DemoAsync();
        var grid = DemoGrid(page);

        await SeedFilterRegionAsync(page, grid);
        await EnableFilterAsync(page, grid);
        await ClickFilterButtonAsync(page, grid, col: 3);

        var dropdown = page.Locator(".tm-spreadsheet-filter-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Uncheck "Banana" and apply.
        var bananaRow = dropdown.Locator(".tm-spreadsheet-filter-dropdown__check", new() { HasTextString = "Banana" });
        // Core TmCheckbox paints its input 0x0 (visually hidden) and the fixed dropdown can sit over a
        // scrollable list, so drive the checkbox through the DOM: the change event is the real contract.
        await bananaRow.Locator("input[type=checkbox]").EvaluateAsync("el => el.click()");
        await dropdown.Locator(".tm-spreadsheet-filter-dropdown__btn--ok").ClickAsync();
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Re-open the dropdown: the filter must have persisted (Banana unchecked, Apple checked).
        await ClickFilterButtonAsync(page, grid, col: 3);
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var bananaChecked = await dropdown
            .Locator(".tm-spreadsheet-filter-dropdown__check", new() { HasTextString = "Banana" })
            .Locator("input[type=checkbox]")
            .IsCheckedAsync();
        var appleChecked = await dropdown
            .Locator(".tm-spreadsheet-filter-dropdown__check", new() { HasTextString = "Apple" })
            .Locator("input[type=checkbox]")
            .IsCheckedAsync();

        bananaChecked.Should().BeFalse("Banana was filtered out");
        appleChecked.Should().BeTrue("Apple remains selected");
    }

    [TestMethod]
    public async Task Sort_Descending_ReordersNumbers()
    {
        var page = await OpenPhase3DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "E1", "1");
        await EditCanvasCellAsync(page, grid, "E2", "3");
        await EditCanvasCellAsync(page, grid, "E3", "2");

        // Select E1:E3.
        var e1 = await GetCanvasCellCenterAsync(grid, "E1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = e1.X, Y = e1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "E1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        await DataTab(page).ClickAsync();
        await DemoComponent(page).Locator(".tm-spreadsheet-toolbar__button[title='Sort descending']").ClickAsync();

        await WaitForCanvasCellSnapshotAsync(grid, "E1", s => s.Value == "3", "E1 should be 3 after descending sort.");
        await WaitForCanvasCellSnapshotAsync(grid, "E3", s => s.Value == "1", "E3 should be 1 after descending sort.");
    }

    [TestMethod]
    public async Task AutoFilter_ClearFromToolbar_RemovesFilterButtons()
    {
        var page = await OpenPhase3DemoAsync();
        var grid = DemoGrid(page);

        await SeedFilterRegionAsync(page, grid);
        await EnableFilterAsync(page, grid);
        (await FilterButtonCountAsync(grid)).Should().BeGreaterThanOrEqualTo(1);

        // Toggling Filter again clears it.
        await DemoComponent(page).Locator(".tm-spreadsheet-toolbar__button[title='Filter']").ClickAsync();
        await page.WaitForFunctionAsync(
            "el => (el.__tmSpreadsheetCanvas?.filterButtons?.length || 0) === 0",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        (await FilterButtonCountAsync(grid)).Should().Be(0);
    }

    private async Task ClickFilterButtonAsync(IPage page, ILocator grid, int col)
    {
        var pos = await grid.EvaluateAsync<CanvasCellPointResult>(
            @"(el, c) => {
                const b = (el.__tmSpreadsheetCanvas?.filterButtons || []).find(x => x.col === Number(c));
                return b ? { x: Math.round(b.x + b.w / 2), y: Math.round(b.y + b.h / 2) } : { x: -1, y: -1 };
            }",
            col);

        pos.X.Should().BeGreaterThan(0);
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = pos.X, Y = pos.Y } });
    }
}
