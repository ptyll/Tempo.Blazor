using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 E2E coverage: data tools (Remove Duplicates, Text to Columns) and Paste Special
/// (values-only and transpose, via the Ctrl+Shift+V dialog) against the live canvas engine on the
/// WASM demo's <c>/spreadsheet</c> page.
/// </summary>
public partial class SpreadsheetE2ETests
{
    private async Task<IPage> OpenPhase4DemoAsync()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);
        await WaitForCanvasGridReadyAsync(page, DemoGrid(page));
        return page;
    }

    private async Task SelectColumnRangeAsync(IPage page, ILocator grid, string topCell, int extendDown)
    {
        var pt = await GetCanvasCellCenterAsync(grid, topCell);
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = pt.X, Y = pt.Y } });
        await WaitForCanvasActiveRefAsync(grid, topCell);
        for (var i = 0; i < extendDown; i++)
            await page.Keyboard.PressAsync("Shift+ArrowDown");
    }

    private static ILocator DataToolButton(IPage page, string title)
        => DemoComponent(page).Locator($".tm-spreadsheet-toolbar__button[title='{title}']");

    [TestMethod]
    public async Task RemoveDuplicates_RemovesDuplicateRows()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "Apple");
        await EditCanvasCellAsync(page, grid, "D2", "Banana");
        await EditCanvasCellAsync(page, grid, "D3", "Apple");
        await EditCanvasCellAsync(page, grid, "D4", "Banana");

        await SelectColumnRangeAsync(page, grid, "D1", extendDown: 3);

        await DataTab(page).ClickAsync();
        await DataToolButton(page, "Remove duplicates").ClickAsync();

        var dialog = page.Locator(".tm-spreadsheet-dedup");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Uncheck "my data has headers" so all four rows are treated as data.
        await dialog.Locator(".tm-spreadsheet-dedup__toggle input[type=checkbox]").First.UncheckAsync();
        await dialog.Locator(".tm-spreadsheet-dedup__btn--ok").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Apple, Banana kept; the second Apple/Banana removed and the tail cleared.
        await WaitForCanvasCellSnapshotAsync(grid, "D1", s => s.Value == "Apple", "D1 should remain Apple.");
        await WaitForCanvasCellSnapshotAsync(grid, "D2", s => s.Value == "Banana", "D2 should remain Banana.");
        await WaitForCanvasCellSnapshotAsync(grid, "D3", s => string.IsNullOrEmpty(s.Value), "D3 should be cleared.");

        // A localized result banner is shown.
        var toast = DemoComponent(page).Locator(".tm-spreadsheet__datatool-toast");
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        StringAssert.Contains(await toast.InnerTextAsync(), "removed");
    }

    [TestMethod]
    public async Task TextToColumns_SplitsSingleColumnIntoThree()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "Jan;Novak;Praha");

        var pt = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = pt.X, Y = pt.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");

        await DataTab(page).ClickAsync();
        await DataToolButton(page, "Text to columns").ClickAsync();

        var dialog = page.Locator(".tm-spreadsheet-t2c");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Step 1 → Step 2.
        await dialog.Locator(".tm-spreadsheet-t2c__btn--ok").ClickAsync();
        // Enable the semicolon delimiter (2nd delimiter checkbox).
        await dialog.Locator(".tm-spreadsheet-t2c__delims input[type=checkbox]").Nth(1).CheckAsync();
        // Step 2 → Step 3.
        await dialog.Locator(".tm-spreadsheet-t2c__btn--ok").ClickAsync();
        // Finish.
        await dialog.Locator(".tm-spreadsheet-t2c__btn--ok").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        await WaitForCanvasCellSnapshotAsync(grid, "D1", s => s.Value == "Jan", "D1 should be Jan.");
        await WaitForCanvasCellSnapshotAsync(grid, "E1", s => s.Value == "Novak", "E1 should be Novak.");
        await WaitForCanvasCellSnapshotAsync(grid, "F1", s => s.Value == "Praha", "F1 should be Praha.");
    }

    [TestMethod]
    public async Task PasteSpecial_ValuesOnly_DropsFormula()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "=5+5");

        // Copy D1.
        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await page.Keyboard.PressAsync("Control+c");

        // Move to F1 and open Paste Special with Ctrl+Shift+V.
        var f1 = await GetCanvasCellCenterAsync(grid, "F1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = f1.X, Y = f1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "F1");
        await page.Keyboard.PressAsync("Control+Shift+V");

        var dialog = page.Locator(".tm-spreadsheet-pastespecial");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Choose "Values" (2nd content radio) and apply.
        await dialog.Locator("input[name=ps-content]").Nth(1).CheckAsync();
        await dialog.Locator(".tm-spreadsheet-pastespecial__btn--ok").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        await WaitForCanvasCellSnapshotAsync(grid, "F1",
            s => s.Value == "10" && string.IsNullOrEmpty(s.Formula),
            "F1 should hold the value 10 with no formula.");
    }

    [TestMethod]
    public async Task PasteSpecial_Transpose_SwapsRowToColumn()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "1");
        await EditCanvasCellAsync(page, grid, "E1", "2");
        await EditCanvasCellAsync(page, grid, "F1", "3");

        // Select D1:F1 and copy. Extend one column at a time, waiting for the active cell to move so
        // the selection reliably reaches F1 before copying.
        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await WaitForCanvasActiveRefAsync(grid, "E1");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await WaitForCanvasActiveRefAsync(grid, "F1");
        await page.Keyboard.PressAsync("Control+c");

        // Paste-special transpose into D3.
        var d3 = await GetCanvasCellCenterAsync(grid, "D3");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d3.X, Y = d3.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D3");
        await page.Keyboard.PressAsync("Control+Shift+V");

        var dialog = page.Locator(".tm-spreadsheet-pastespecial");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        // Tick transpose (2nd toggle checkbox) and apply (content defaults to All).
        await dialog.Locator(".tm-spreadsheet-pastespecial__toggles input[type=checkbox]").Nth(1).CheckAsync();
        await dialog.Locator(".tm-spreadsheet-pastespecial__btn--ok").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        await WaitForCanvasCellSnapshotAsync(grid, "D3", s => s.Value == "1", "D3 should be 1.");
        await WaitForCanvasCellSnapshotAsync(grid, "D4", s => s.Value == "2", "D4 should be 2.");
        await WaitForCanvasCellSnapshotAsync(grid, "D5", s => s.Value == "3", "D5 should be 3.");
    }

    /// <summary>
    /// Regression tooth for the copy/selection race behind the flaky
    /// <see cref="PasteSpecial_Transpose_SwapsRowToColumn"/>: the canvas applies Shift+Arrow locally and only
    /// QUEUES the new selection for .NET (a frame, then a debounced command-log batch), while the copy
    /// handler used to notify .NET at once — so Ctrl+C pressed before that batch left filled the internal
    /// clipboard (the one Paste Special reads) from the PREVIOUS selection. Measured on the unfixed build:
    /// the key command "c" reached .NET 47 ms before the batch carrying D1:F1, and the transposed paste
    /// wrote only D3:D4. Here the extension and the copy are dispatched in ONE JS task, which makes that
    /// ordering certain instead of timing-dependent; the native clipboard text is captured to prove the
    /// canvas itself already held D1:F1, so a red here can only be the .NET side lagging behind it.
    /// </summary>
    [TestMethod]
    public async Task PasteSpecial_CopyInTheSameTaskAsTheSelectionChange_CopiesTheExtendedRange()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "1");
        await EditCanvasCellAsync(page, grid, "E1", "2");
        await EditCanvasCellAsync(page, grid, "F1", "3");

        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await WaitForCanvasCellSnapshotAsync(grid, "F1", s => s.Value == "3", "F1 should hold 3 before the copy.");

        var copiedText = await grid.EvaluateAsync<string>(
            @"el => {
                const arrowRight = () => el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: 'ArrowRight', code: 'ArrowRight', shiftKey: true, bubbles: true, cancelable: true
                }));
                arrowRight();
                arrowRight();
                const clipboardData = new DataTransfer();
                el.dispatchEvent(new ClipboardEvent('copy', { clipboardData, bubbles: true, cancelable: true }));
                return clipboardData.getData('text/plain');
            }");
        Assert.AreEqual(
            "1\t2\t3",
            copiedText.TrimEnd('\r', '\n'),
            "precondition: the canvas must already hold D1:F1 when the copy handler runs, otherwise this test "
            + "measures the synthetic key events rather than the .NET-side copy");
        await WaitForCanvasActiveRefAsync(grid, "F1");

        var d3 = await GetCanvasCellCenterAsync(grid, "D3");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d3.X, Y = d3.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D3");
        await page.Keyboard.PressAsync("Control+Shift+V");

        var dialog = page.Locator(".tm-spreadsheet-pastespecial");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await dialog.Locator(".tm-spreadsheet-pastespecial__toggles input[type=checkbox]").Nth(1).CheckAsync();
        await dialog.Locator(".tm-spreadsheet-pastespecial__btn--ok").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        await WaitForCanvasCellSnapshotAsync(grid, "D3", s => s.Value == "1", "D3 should be 1.");
        await WaitForCanvasCellSnapshotAsync(grid, "D4", s => s.Value == "2", "D4 should be 2.");
        await WaitForCanvasCellSnapshotAsync(
            grid,
            "D5",
            s => s.Value == "3",
            "D5 should be 3 — the internal clipboard must hold the range the canvas showed selected at Ctrl+C (D1:F1), not the one .NET had last been told about (D1:E1).");
    }
}
