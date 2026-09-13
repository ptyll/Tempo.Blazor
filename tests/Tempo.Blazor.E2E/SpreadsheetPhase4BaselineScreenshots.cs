using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 baseline screenshots: the Remove Duplicates dialog, the Text to Columns step-2 live
/// preview, and the Paste Special dialog. Run with the BaselineGeneration category against a running
/// WASM demo to (re)generate the PNG baselines under artifacts/baseline/spreadsheet.
/// </summary>
public partial class SpreadsheetBaselineScreenshots
{
    [TestMethod]
    public async Task Baseline_Dedup_Dialog()
    {
        var page = await OpenSpreadsheetAsync();
        await SeedColumnDAsync(page, ("D1", "Apple"), ("D2", "Banana"), ("D3", "Apple"), ("D4", "Banana"));

        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        await Phase2Component(page).Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" }).ClickAsync();
        await Phase2Component(page).Locator(".tm-spreadsheet-toolbar__button[title='Remove duplicates']").ClickAsync();

        var dialog = page.Locator(".tm-spreadsheet-dedup");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "dedup-01-dialog.png", dialog);
    }

    [TestMethod]
    public async Task Baseline_TextToColumns_Step2Preview()
    {
        var page = await OpenSpreadsheetAsync();
        await SeedColumnDAsync(page, ("D1", "Jan;Novak;Praha"), ("D2", "Petr;Svoboda;Brno"));

        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        await Phase2Component(page).Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" }).ClickAsync();
        await Phase2Component(page).Locator(".tm-spreadsheet-toolbar__button[title='Text to columns']").ClickAsync();

        var dialog = page.Locator(".tm-spreadsheet-t2c");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await dialog.Locator(".tm-spreadsheet-t2c__btn--ok").ClickAsync(); // → step 2
        await dialog.Locator(".tm-spreadsheet-t2c__delims input[type=checkbox]").Nth(1).CheckAsync(); // semicolon
        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "t2c-01-step2-preview.png", dialog);
    }

    [TestMethod]
    public async Task Baseline_PasteSpecial_Dialog()
    {
        var page = await OpenSpreadsheetAsync();
        await SeedColumnDAsync(page, ("D1", "10"));

        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, "D1");
        await page.Keyboard.PressAsync("Control+c");
        await ClickCellAsync(grid, "F1");
        await page.Keyboard.PressAsync("Control+Shift+V");

        var dialog = page.Locator(".tm-spreadsheet-pastespecial");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "pastespecial-01-dialog.png", dialog);
    }
}
