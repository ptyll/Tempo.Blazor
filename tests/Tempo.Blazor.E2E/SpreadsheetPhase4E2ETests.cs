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
            "D5 should be 3 — the internal clipboard must hold the range the canvas showed selected at Ctrl+C (D1:F1), not the one .NET had last been told about (D1 or D1:E1).");
    }

    /// <summary>
    /// The other half of the copy/selection ordering contract: a key command must run against the state
    /// at ITS key press — not against selection changes made after it. Shift+Arrow ×2, copy, one more
    /// Shift+Arrow and Ctrl+B are dispatched in ONE JS task, so the later selection (D1:G1) is already
    /// queued while the copy still waits for .NET to acknowledge D1:F1. A flush that waits for an EMPTY
    /// command log (the first version of the fix) delivers D1:G1 before the copy and the internal
    /// clipboard gets four cells; the transposed paste then writes G1's 4 into D6. The copy must see
    /// exactly D1:F1, so D6 stays empty.
    /// </summary>
    [TestMethod]
    public async Task PasteSpecial_SelectionChangedAfterTheCopy_DoesNotLeakIntoTheCopiedRange()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "1");
        await EditCanvasCellAsync(page, grid, "E1", "2");
        await EditCanvasCellAsync(page, grid, "F1", "3");
        await EditCanvasCellAsync(page, grid, "G1", "4");

        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await WaitForCanvasCellSnapshotAsync(grid, "G1", s => s.Value == "4", "G1 should hold 4 before the copy.");

        var copiedText = await grid.EvaluateAsync<string>(
            @"el => {
                const key = (key, extra) => el.dispatchEvent(new KeyboardEvent('keydown', {
                    key, code: key.length === 1 ? 'Key' + key.toUpperCase() : key, bubbles: true, cancelable: true, ...extra
                }));
                key('ArrowRight', { shiftKey: true });
                key('ArrowRight', { shiftKey: true });
                const clipboardData = new DataTransfer();
                el.dispatchEvent(new ClipboardEvent('copy', { clipboardData, bubbles: true, cancelable: true }));
                key('ArrowRight', { shiftKey: true });
                key('b', { ctrlKey: true });
                return clipboardData.getData('text/plain');
            }");
        Assert.AreEqual(
            "1\t2\t3",
            copiedText.TrimEnd('\r', '\n'),
            "precondition: the canvas must hold D1:F1 when the copy handler runs");
        await WaitForCanvasActiveRefAsync(grid, "G1");

        var d3 = await GetCanvasCellCenterAsync(grid, "D3");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d3.X, Y = d3.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D3");
        await page.Keyboard.PressAsync("Control+Shift+V");

        var dialog = page.Locator(".tm-spreadsheet-pastespecial");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await dialog.Locator(".tm-spreadsheet-pastespecial__toggles input[type=checkbox]").Nth(1).CheckAsync();
        await dialog.Locator(".tm-spreadsheet-pastespecial__btn--ok").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // One PasteSpecialCommand writes and syncs every target cell together, so once D5 holds its
        // value D6 is final too.
        await WaitForCanvasCellSnapshotAsync(grid, "D3", s => s.Value == "1", "D3 should be 1.");
        await WaitForCanvasCellSnapshotAsync(grid, "D5", s => s.Value == "3", "D5 should be 3.");
        var d6 = await ReadCanvasCellSnapshotAsync(grid, "D6");
        Assert.AreEqual(
            "",
            d6.Value,
            "D6 must stay empty — the copy ran against D1:F1 (its key press), so a selection extended to G1 AFTER the copy must not reach the internal clipboard.");
    }

    /// <summary>
    /// Deterministic tooth for the key-command path (handleCommandKey), which the copy tests above do not
    /// reach: Shift+Arrow ×2 and Delete in ONE JS task. Before the fix Delete reached .NET at once, while
    /// the extended selection was still only queued, so .NET cleared just D1 and E1/F1 kept their values.
    /// </summary>
    [TestMethod]
    public async Task Delete_InTheSameTaskAsTheSelectionChange_ClearsTheExtendedRange()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "1");
        await EditCanvasCellAsync(page, grid, "E1", "2");
        await EditCanvasCellAsync(page, grid, "F1", "3");

        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await WaitForCanvasCellSnapshotAsync(grid, "F1", s => s.Value == "3", "F1 should hold 3 before the delete.");

        await grid.EvaluateAsync(
            @"el => {
                const key = (key, extra) => el.dispatchEvent(new KeyboardEvent('keydown', { key, code: key, bubbles: true, cancelable: true, ...extra }));
                key('ArrowRight', { shiftKey: true });
                key('ArrowRight', { shiftKey: true });
                key('Delete', {});
            }");

        await WaitForCanvasCellSnapshotAsync(grid, "D1", s => s.Value == "", "D1 should be cleared.");
        await WaitForCanvasCellSnapshotAsync(
            grid,
            "F1",
            s => s.Value == "",
            "F1 should be cleared — Delete must act on the selection the canvas showed at the key press (D1:F1), not on the one .NET had last been told about.");
        await WaitForCanvasCellSnapshotAsync(grid, "E1", s => s.Value == "", "E1 should be cleared.");
    }

    /// <summary>
    /// Two key commands in a row must each run against the state at THEIR key press, even while the first
    /// one's .NET call is still running. The stub makes every <c>OnCanvasKeyCommand</c> call take 300 ms
    /// to complete (the call itself is forwarded to .NET at once). Shift+Arrow ×2, Ctrl+B, copy and one
    /// more Shift+Arrow run in ONE task. When key commands were chained on the COMPLETION of the previous
    /// call and a barrier lapsed once its commands were acknowledged, the later Shift+Arrow (D1:G1)
    /// reached .NET during Ctrl+B's 300 ms and the copy then took four cells, writing G1's 4 into D6.
    /// </summary>
    [TestMethod]
    public async Task PasteSpecial_CopyQueuedBehindASlowKeyCommand_KeepsItsOwnSelection()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "1");
        await EditCanvasCellAsync(page, grid, "E1", "2");
        await EditCanvasCellAsync(page, grid, "F1", "3");
        await EditCanvasCellAsync(page, grid, "G1", "4");

        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await WaitForCanvasCellSnapshotAsync(grid, "G1", s => s.Value == "4", "G1 should hold 4 before the copy.");

        var copiedText = await grid.EvaluateAsync<string>(
            @"el => {
                const dotNet = el.__tmSpreadsheetCanvas.dotNet;
                const original = dotNet.invokeMethodAsync.bind(dotNet);
                dotNet.invokeMethodAsync = (method, ...args) => {
                    const call = original(method, ...args);
                    return method === 'OnCanvasKeyCommand'
                        ? call.then(result => new Promise(resolve => setTimeout(() => resolve(result), 300)))
                        : call;
                };
                const key = (key, extra) => el.dispatchEvent(new KeyboardEvent('keydown', {
                    key, code: key.length === 1 ? 'Key' + key.toUpperCase() : key, bubbles: true, cancelable: true, ...extra
                }));
                key('ArrowRight', { shiftKey: true });
                key('ArrowRight', { shiftKey: true });
                key('b', { ctrlKey: true });
                const clipboardData = new DataTransfer();
                el.dispatchEvent(new ClipboardEvent('copy', { clipboardData, bubbles: true, cancelable: true }));
                key('ArrowRight', { shiftKey: true });
                return clipboardData.getData('text/plain');
            }");
        Assert.AreEqual("1\t2\t3", copiedText.TrimEnd('\r', '\n'), "precondition: the canvas must hold D1:F1 when the copy handler runs");
        await page.WaitForTimeoutAsync(400); // outlast the stub's 300 ms so both key commands have completed
        await WaitForCanvasActiveRefAsync(grid, "G1");

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
        await WaitForCanvasCellSnapshotAsync(grid, "D5", s => s.Value == "3", "D5 should be 3.");
        var d6 = await ReadCanvasCellSnapshotAsync(grid, "D6");
        Assert.AreEqual(
            "",
            d6.Value,
            "D6 must stay empty — the copy's selection is D1:F1 (its key press); the Shift+Arrow made after it must not overtake it while Ctrl+B's .NET call is still running.");
    }

    /// <summary>
    /// A key command whose .NET call never returns (e.g. a circuit reconnect) must not block the keys after
    /// it. The stub forwards every <c>OnCanvasKeyCommand</c> to .NET but never resolves the returned
    /// promise; Delete pressed after Ctrl+B must still clear the cell within 3 s.
    /// </summary>
    [TestMethod]
    public async Task KeyCommand_AfterAKeyCommandThatNeverCompletes_StillReachesDotNet()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "1");
        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await WaitForCanvasCellSnapshotAsync(grid, "D1", s => s.Value == "1", "D1 should hold 1 before the delete.");

        await grid.EvaluateAsync(
            @"el => {
                const dotNet = el.__tmSpreadsheetCanvas.dotNet;
                const original = dotNet.invokeMethodAsync.bind(dotNet);
                dotNet.invokeMethodAsync = (method, ...args) => {
                    const call = original(method, ...args);
                    return method === 'OnCanvasKeyCommand' ? new Promise(() => { }) : call;
                };
                const key = (key, extra) => el.dispatchEvent(new KeyboardEvent('keydown', { key, code: key, bubbles: true, cancelable: true, ...extra }));
                key('b', { ctrlKey: true });
                key('Delete', {});
            }");

        var deadline = DateTime.UtcNow.AddSeconds(3);
        CanvasCellSnapshotResult snapshot;
        do
        {
            snapshot = await ReadCanvasCellSnapshotAsync(grid, "D1");
            if (snapshot.Value == "")
                return;
            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Delete pressed after a Ctrl+B whose .NET call never completes must still clear D1 within 3 s; D1 is still '{snapshot.Value}'.");
    }

    /// <summary>
    /// The flush before a key command must only send a selection that has not reached .NET yet. Re-sending
    /// a settled one costs a round-trip per key and raises a spurious ActiveCellChanged on the public
    /// component (which also ends a formula-bar editing session). Measured with the engine's own
    /// selectionCallbackCount: Ctrl+B on a settled single-cell selection must not send the selection again.
    /// </summary>
    [TestMethod]
    public async Task KeyCommand_WithASettledSelection_DoesNotResendTheSelection()
    {
        var page = await OpenPhase4DemoAsync();
        var grid = DemoGrid(page);

        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await page.WaitForFunctionAsync(
            @"el => {
                const s = el.__tmSpreadsheetCanvas;
                return !!s && !s.commandLogInFlight && !s.commandLogTimer && (s.commandLog || []).length === 0
                    && !s.selectionSyncFrame && !s.selectionSyncTimer;
            }",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var counts = await grid.EvaluateAsync<int[]>(
            @"async el => {
                const s = el.__tmSpreadsheetCanvas;
                const before = s.metrics.selectionCallbackCount;
                const keysBefore = s.metrics.keyCommandCallbackCount;
                el.dispatchEvent(new KeyboardEvent('keydown', { key: 'b', code: 'KeyB', ctrlKey: true, bubbles: true, cancelable: true }));
                await s.pendingStateCommandChain;
                return [before, s.metrics.selectionCallbackCount, keysBefore, s.metrics.keyCommandCallbackCount];
            }");
        Assert.AreEqual(counts[2] + 1, counts[3], "precondition: Ctrl+B must have gone through the key-command path exactly once");
        Assert.AreEqual(
            counts[0],
            counts[1],
            "Ctrl+B on a settled selection must not re-send it to .NET (one extra round-trip and a spurious ActiveCellChanged per key)");
    }
}
