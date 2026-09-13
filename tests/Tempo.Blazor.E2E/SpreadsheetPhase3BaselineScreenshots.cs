using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3 baseline screenshots: the auto-filter dropdown, an active-filter state, and the multi-level
/// sort dialog. Run with the BaselineGeneration category against a running WASM demo to (re)generate
/// the PNG baselines under artifacts/baseline/spreadsheet.
/// </summary>
public partial class SpreadsheetBaselineScreenshots
{
    private async Task SeedFilterColumnAsync(IPage page)
    {
        await SeedColumnDAsync(page, ("D1", "Fruit"), ("D2", "Apple"), ("D3", "Banana"), ("D4", "Cherry"));
        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        // Enable the auto-filter from the Data tab.
        var component = Phase2Component(page);
        await component.Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" }).ClickAsync();
        await component.Locator(".tm-spreadsheet-toolbar__button[title='Filter']").ClickAsync();
        await page.WaitForFunctionAsync(
            "el => (el.__tmSpreadsheetCanvas?.filterButtons?.length || 0) > 0",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task ClickFilterButtonAsync(IPage page, ILocator grid, int col)
    {
        var pos = await grid.EvaluateAsync<CellPoint>(
            @"(el, c) => {
                const b = (el.__tmSpreadsheetCanvas?.filterButtons || []).find(x => x.col === Number(c));
                return b ? { x: Math.round(b.x + b.w / 2), y: Math.round(b.y + b.h / 2) } : { x: -1, y: -1 };
            }",
            col);
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = pos.X, Y = pos.Y } });
    }

    [TestMethod]
    public async Task Baseline_Filter_Dropdown()
    {
        var page = await OpenSpreadsheetAsync();
        await SeedFilterColumnAsync(page);
        await ClickFilterButtonAsync(page, Phase2Grid(page), col: 3);

        await page.Locator(".tm-spreadsheet-filter-dropdown")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "filter-01-dropdown.png", Phase2Component(page));
    }

    [TestMethod]
    public async Task Baseline_Filter_Active()
    {
        var page = await OpenSpreadsheetAsync();
        await SeedFilterColumnAsync(page);
        var grid = Phase2Grid(page);
        await ClickFilterButtonAsync(page, grid, col: 3);

        var dropdown = page.Locator(".tm-spreadsheet-filter-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await dropdown.Locator(".tm-spreadsheet-filter-dropdown__check", new() { HasTextString = "Banana" })
            .Locator("input[type=checkbox]").UncheckAsync();
        await dropdown.Locator(".tm-spreadsheet-filter-dropdown__btn--ok").ClickAsync();
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(500);

        await CaptureAsync(page, "filter-02-active.png", Phase2Component(page));
    }

    [TestMethod]
    public async Task Baseline_Sort_Dialog()
    {
        var page = await OpenSpreadsheetAsync();
        await SeedColumnDAsync(page, ("D1", "Beta"), ("D2", "Alpha"), ("D3", "Gamma"));
        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        var component = Phase2Component(page);
        await component.Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" }).ClickAsync();
        await component.Locator(".tm-spreadsheet-toolbar__button--text[title='Custom sort…']").ClickAsync();

        await page.Locator(".tm-spreadsheet-sort")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "sort-01-dialog.png");
    }
}
