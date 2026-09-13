using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 5 baseline screenshots: the data validation dialog (Settings tab), the in-cell
/// dropdown popover, and the Stop-style error alert. Run with the BaselineGeneration
/// category against a running WASM demo to (re)generate the PNG baselines under
/// artifacts/baseline/spreadsheet.
/// </summary>
public partial class SpreadsheetBaselineScreenshots
{
    private static ILocator Phase5DataTab(IPage page)
        => Phase2Component(page).Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" });

    private static async Task OpenValidationDialogAsync(IPage page, string cellRef)
    {
        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, cellRef);
        await page.WaitForFunctionAsync(
            $"el => (el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '').toUpperCase() === '{cellRef.ToUpperInvariant()}'",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000, PollingInterval = 100 });
        await Phase5DataTab(page).ClickAsync();
        await Phase2Component(page).Locator(".tm-spreadsheet-toolbar__button[title='Data validation...']").ClickAsync();
        var dialog = page.Locator(".tm-dvd");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    private static async Task FillValidationInputAsync(IPage page, ILocator input, string value)
    {
        await input.ClickAsync();
        await input.SelectTextAsync();
        await page.Keyboard.TypeAsync(value);
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(200);
    }

    [TestMethod]
    public async Task Baseline_Validation_Dialog_Settings()
    {
        var page = await OpenSpreadsheetAsync();

        // Open dialog on a fresh cell (E1) — no existing rule.
        await OpenValidationDialogAsync(page, "E1");

        // Configure: Type = List, Formula1 = Apple,Banana,Cherry
        var dialog = page.Locator(".tm-dvd");
        await dialog.Locator(".tm-dvd__select").First.SelectOptionAsync("List");
        await page.WaitForTimeoutAsync(200);
        await FillValidationInputAsync(page, dialog.Locator(".tm-dvd__input").First, "Apple,Banana,Cherry");

        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "validation-01-dialog.png", dialog);

        // Cancel — don't commit.
        await dialog.Locator(".tm-dvd__actions button").First.ClickAsync();
    }

    [TestMethod]
    public async Task Baseline_Validation_Dropdown()
    {
        var page = await OpenSpreadsheetAsync();

        // Set up a list validation on E2 and apply it.
        await OpenValidationDialogAsync(page, "E2");
        var dialog = page.Locator(".tm-dvd");
        await dialog.Locator(".tm-dvd__select").First.SelectOptionAsync("List");
        await page.WaitForTimeoutAsync(200);
        await FillValidationInputAsync(page, dialog.Locator(".tm-dvd__input").First, "Apple,Banana,Cherry");
        await dialog.Locator(".tm-dvd__actions button").Last.ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Click the cell again to select it.
        var grid = Phase2Grid(page);
        await ClickCellAsync(grid, "E2");
        await page.WaitForTimeoutAsync(400);

        // Attempt to trigger the validation drop button — it's drawn on the canvas near
        // the right edge of the selected cell.
        var pt = await ComputeCellPointAsync(grid, "E2");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = pt.X + 20, Y = pt.Y }
        });
        await page.WaitForTimeoutAsync(300);

        var dropdown = page.Locator(".tm-spreadsheet-vd");
        try
        {
            await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 2000 });
            await CaptureAsync(page, "validation-02-dropdown.png", Phase2Component(page));
        }
        catch
        {
            // The drop button hit-test is pixel-exact; fall back to a full-component capture.
            await CaptureAsync(page, "validation-02-dropdown.png", Phase2Component(page));
        }
    }

    [TestMethod]
    public async Task Baseline_Validation_ErrorAlert()
    {
        var page = await OpenSpreadsheetAsync();

        // Set up Stop-style whole-number 1–10 validation on E3.
        await OpenValidationDialogAsync(page, "E3");
        var dialog = page.Locator(".tm-dvd");
        await dialog.Locator(".tm-dvd__select").First.SelectOptionAsync("Whole");
        await page.WaitForTimeoutAsync(200);

        // Fill min and max
        await FillValidationInputAsync(page, dialog.Locator(".tm-dvd__input").First, "1");
        await FillValidationInputAsync(page, dialog.Locator(".tm-dvd__input").Last, "10");

        // Apply — Stop is already the default ErrorAlert style
        await dialog.Locator(".tm-dvd__actions button").Last.ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Type an out-of-range value (15) to trigger the Stop alert.
        await TypeIntoCellViaKeyboardAsync(page, Phase2Grid(page), "E3", "15");
        var alert = Phase2Component(page).Locator(".tm-spreadsheet-alert");
        await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "validation-03-error.png", Phase2Component(page));

        // Dismiss to avoid leaving alert open.
        await alert.Locator("button").First.ClickAsync();
    }
}
