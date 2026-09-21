using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;

namespace Tempo.Blazor.E2E;

[TestClass]
public partial class SpreadsheetE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task ArrowNavigation_ScrollsGridVertically()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-grid").Nth(2);
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var before = await grid.EvaluateAsync<double>("el => el.scrollTop");

        for (var i = 0; i < 29; i++)
        {
            await grid.PressAsync("ArrowDown");
        }

        await page.WaitForFunctionAsync(
            "el => el.scrollTop > 0",
            await grid.ElementHandleAsync());

        var after = await grid.EvaluateAsync<double>("el => el.scrollTop");
        Assert.IsTrue(after > before, $"Expected spreadsheet grid to scroll down. Before: {before}, after: {after}.");
    }

    [TestMethod]
    public async Task ArrowNavigation_ScrollsGridHorizontally()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-grid").Nth(2);
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var before = await grid.EvaluateAsync<double>("el => el.scrollLeft");

        for (var i = 0; i < 45; i++)
        {
            await grid.PressAsync("ArrowRight");
        }

        await page.WaitForFunctionAsync(
            "el => el.scrollLeft > 0",
            await grid.ElementHandleAsync());

        var after = await grid.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(after > before, $"Expected spreadsheet grid to scroll right. Before: {before}, after: {after}.");

        await grid.Locator("[title='AT1']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });
    }

    [TestMethod]
    public async Task CanvasRenderer_RendersNonBlankCanvasAndScrollsHorizontally()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        await page.WaitForFunctionAsync(
            @"el => {
                const canvas = el?.querySelector('.tm-spreadsheet-canvas-grid__canvas--content');
                if (!canvas || canvas.width === 0 || canvas.height === 0) return false;
                const ctx = canvas.getContext('2d');
                const data = ctx.getImageData(0, 0, Math.min(canvas.width, 160), Math.min(canvas.height, 96)).data;
                for (let i = 0; i < data.length; i += 4) {
                    if (data[i + 3] !== 0 && (data[i] < 250 || data[i + 1] < 250 || data[i + 2] < 250)) return true;
                }
                return false;
            }",
            await grid.ElementHandleAsync());

        await grid.ClickAsync();
        for (var i = 0; i < 45; i++)
        {
            await grid.PressAsync("ArrowRight");
        }

        await page.WaitForFunctionAsync(
            "el => el.scrollLeft > 0",
            await grid.ElementHandleAsync());
    }

    [TestMethod]
    public async Task DomRenderer_RemainsFunctionalAsFallback()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-grid").Nth(2);
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var a1 = grid.Locator("[title='A1']").First;
        await a1.ClickAsync();
        await grid.Locator("[title='A1'].tm-spreadsheet-cell--active").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.Keyboard.PressAsync("ArrowRight");

        await grid.Locator("[title='B1'].tm-spreadsheet-cell--active").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    [TestMethod]
    public async Task CanvasRenderer_DoubleClickStartsCellEdit()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var canvas = grid.Locator("canvas").First;
        await page.WaitForFunctionAsync(
            "canvas => canvas && canvas.width > 0 && canvas.height > 0",
            await canvas.ElementHandleAsync());

        await grid.DblClickAsync(new LocatorDblClickOptions
        {
            Force = true,
            Position = new() { X = 120, Y = 56 }
        });

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        var isFocused = await editor.EvaluateAsync<bool>("el => document.activeElement === el");
        Assert.IsTrue(isFocused, "Expected double-clicked canvas cell editor to receive focus.");
    }

    [TestMethod]
    public async Task CanvasRenderer_TypingStartsEditAndKeepsAcceptingCharacters()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        await grid.PressAsync("a");
        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Keyboard.TypeAsync("bc");

        var value = await editor.InputValueAsync();
        Assert.AreEqual("abc", value);
    }

    [TestMethod]
    public async Task CanvasRenderer_JsEditorTypingTwentyCharactersKeepsAllCharacters()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsEditorProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: 'a',
                    bubbles: true,
                    cancelable: true
                }));
                requestAnimationFrame(() => {
                    const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                    input.value += 'bcdefghijklmnopqrst';
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    requestAnimationFrame(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        resolve({
                            editorValue: input.value,
                            editorOpenCount: after.editorOpenCount - before.editorOpenCount,
                            keyCommandCallbacks: after.keyCommandCallbackCount - before.keyCommandCallbackCount,
                            editBatchCallbacks: after.cellEditCommitBatchCallbackCount - before.cellEditCommitBatchCallbackCount,
                            editBatchItems: after.cellEditCommitBatchItemCount - before.cellEditCommitBatchItemCount
                        });
                    });
                });
            })");

        Assert.AreEqual("abcdefghijklmnopqrst", result.EditorValue);
        Assert.AreEqual(1, result.EditorOpenCount, "Expected typing the first character to open the JS editor once.");
        Assert.AreEqual(0, result.KeyCommandCallbacks, "Typing normal text should not go through the Blazor key-command path.");
        Assert.AreEqual(0, result.EditBatchCallbacks, "Typing without commit should not send a cell-edit batch.");
        Assert.AreEqual(0, result.EditBatchItems, "Typing without commit should not queue committed items.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsEditorFastEnterCommitKeepsNextActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsEditorProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const startRef = state.model.activeCellRef || state.model.ActiveCellRef || '';
                const start = before.sheetState.activeCell;
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: 'q',
                    bubbles: true,
                    cancelable: true
                }));
                requestAnimationFrame(() => {
                    const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                    input.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'Enter',
                        bubbles: true,
                        cancelable: true
                    }));
                    setTimeout(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        const endRef = state.model.activeCellRef || state.model.ActiveCellRef || '';
                        const key = `${start.row}:${start.col}`;
                        const stored = state.sheetState.cellStore.cells.get(key);
                        resolve({
                            startRef,
                            activeRef: endRef,
                            committedValue: stored?.value || stored?.Value || '',
                            editorLocalCommits: after.editorLocalCommitCount - before.editorLocalCommitCount,
                            editBatchCallbacks: after.cellEditCommitBatchCallbackCount - before.cellEditCommitBatchCallbackCount,
                            editBatchItems: after.cellEditCommitBatchItemCount - before.cellEditCommitBatchItemCount,
                            keyCommandCallbacks: after.keyCommandCallbackCount - before.keyCommandCallbackCount,
                            dotNetCallbackMethodCount: after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0
                        });
                    }, 180);
                });
            })");

        Assert.AreEqual(ParseRow(result.StartRef) + 1, ParseRow(result.ActiveRef), $"Expected fast Enter commit to keep the locally selected next row. Start: {result.StartRef}, end: {result.ActiveRef}.");
        Assert.AreEqual(ParseColumn(result.StartRef), ParseColumn(result.ActiveRef), $"Expected fast Enter commit to keep the column. Start: {result.StartRef}, end: {result.ActiveRef}.");
        Assert.AreEqual("q", result.CommittedValue, "Expected the JS cell store to contain the one-character local commit.");
        Assert.AreEqual(1, result.EditorLocalCommits, "Expected one local editor commit.");
        Assert.AreEqual(1, result.EditBatchCallbacks, "Expected one delayed batch callback for the committed cell.");
        Assert.AreEqual(1, result.EditBatchItems, "Expected one committed cell in the delayed batch.");
        Assert.AreEqual(0, result.KeyCommandCallbacks, "Typing and Enter inside the JS editor should not use the Blazor key-command path.");
        Assert.IsTrue(result.DotNetCallbackMethodCount > 0, "Expected the command log batch callback to be used for .NET synchronization.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsFormulaEditorClickInsertsReferenceWithinOneFrame()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsFormulaEditorProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: '=',
                    bubbles: true,
                    cancelable: true
                }));
                const dispatchPointer = (type, col, row) => {
                    const rect = el.getBoundingClientRect();
                    const x = rect.left + 40 + col * 64 + 32;
                    const y = rect.top + 20 + row * 20 + 10;
                    el.dispatchEvent(new PointerEvent(type, {
                        pointerId: 17,
                        pointerType: 'mouse',
                        clientX: x,
                        clientY: y,
                        button: 0,
                        buttons: type === 'pointerup' ? 0 : 1,
                        bubbles: true,
                        cancelable: true
                    }));
                };
                dispatchPointer('pointerdown', 1, 1);
                dispatchPointer('pointerup', 1, 1);
                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                    resolve({
                        editorValue: input?.value || '',
                        formulaActive: !!after.sheetState?.formulaEditor?.active,
                        formulaRefCount: after.sheetState?.formulaEditor?.refCount || 0,
                        formulaClickInserts: after.formulaEditorCellClickInsertCount - before.formulaEditorCellClickInsertCount,
                        keyCommandCallbacks: after.keyCommandCallbackCount - before.keyCommandCallbackCount,
                        cellPointerCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCellPointer || 0) - (before.dotNetCallbacksByMethod.OnCanvasCellPointer || 0)
                    });
                });
            })");

        Assert.AreEqual("=B2", result.EditorValue);
        Assert.IsTrue(result.FormulaActive, "Expected the JS formula editor to stay active after cell reference insertion.");
        Assert.AreEqual(1, result.FormulaRefCount, "Expected the JS formula parser to expose one reference token.");
        Assert.AreEqual(1, result.FormulaClickInserts, "Expected one local formula cell-click insertion.");
        Assert.AreEqual(0, result.KeyCommandCallbacks, "Typing '=' should open the JS formula editor without Blazor key command.");
        Assert.AreEqual(0, result.CellPointerCallbacks, "Formula reference click should not call the Blazor cell pointer path.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsFormulaEditorDragInsertsRangeLocally()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsFormulaEditorProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: '=',
                    bubbles: true,
                    cancelable: true
                }));
                const dispatchPointer = (type, col, row) => {
                    const rect = el.getBoundingClientRect();
                    const x = rect.left + 40 + col * 64 + 32;
                    const y = rect.top + 20 + row * 20 + 10;
                    el.dispatchEvent(new PointerEvent(type, {
                        pointerId: 19,
                        pointerType: 'mouse',
                        clientX: x,
                        clientY: y,
                        button: 0,
                        buttons: type === 'pointerup' ? 0 : 1,
                        bubbles: true,
                        cancelable: true
                    }));
                };
                dispatchPointer('pointerdown', 1, 1);
                dispatchPointer('pointermove', 3, 3);
                dispatchPointer('pointerup', 3, 3);
                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                    resolve({
                        editorValue: input?.value || '',
                        formulaActive: !!after.sheetState?.formulaEditor?.active,
                        formulaRefCount: after.sheetState?.formulaEditor?.refCount || 0,
                        formulaRangeDrags: after.formulaEditorRangeDragCount - before.formulaEditorRangeDragCount,
                        highlightedCells: after.formulaEditorHighlightCount,
                        cellPointerCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCellPointer || 0) - (before.dotNetCallbacksByMethod.OnCanvasCellPointer || 0)
                    });
                });
            })");

        Assert.AreEqual("=B2:D4", result.EditorValue);
        Assert.IsTrue(result.FormulaActive, "Expected the JS formula editor to stay active after range drag.");
        Assert.AreEqual(1, result.FormulaRefCount, "Expected the range to remain one formula reference token.");
        Assert.IsTrue(result.FormulaRangeDrags > 0, "Expected range drag to update the formula token locally.");
        Assert.AreEqual(9, result.HighlightedCells, "Expected a 3x3 range highlight from B2:D4.");
        Assert.AreEqual(0, result.CellPointerCallbacks, "Formula range drag should not call the Blazor cell pointer path.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsFormulaEditorEnterCommitsFormulaLocally()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsFormulaEditorProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: '=',
                    bubbles: true,
                    cancelable: true
                }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                const editorRow = state.editor?.row ?? before.sheetState.activeCell.row;
                const editorCol = state.editor?.col ?? before.sheetState.activeCell.col;
                input.value = '=B2';
                input.setSelectionRange(input.value.length, input.value.length);
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', {
                    key: 'Enter',
                    bubbles: true,
                    cancelable: true
                }));
                const storedImmediately = state.sheetState.cellStore.cells.get(`${editorRow}:${editorCol}`);
                const committedValue = storedImmediately?.value || storedImmediately?.Value || '';
                const committedFormula = storedImmediately?.formula || storedImmediately?.Formula || '';
                setTimeout(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        editorValue: input?.value || '',
                        formulaActive: !!after.sheetState?.formulaEditor?.active,
                        committedValue,
                        committedFormula,
                        editBatchCallbacks: after.cellEditCommitBatchCallbackCount - before.cellEditCommitBatchCallbackCount,
                        editBatchItems: after.cellEditCommitBatchItemCount - before.cellEditCommitBatchItemCount
                    });
                }, 180);
            })");

        Assert.IsFalse(result.FormulaActive, "Expected formula mode to end after Enter commit.");
        Assert.AreEqual("=B2", result.CommittedValue, "Expected the JS cell store to contain the committed formula text.");
        Assert.AreEqual("=B2", result.CommittedFormula, "Expected the JS cell store to mark the committed value as a formula.");
        Assert.AreEqual(1, result.EditBatchCallbacks, "Expected one delayed batch callback for formula commit.");
        Assert.AreEqual(1, result.EditBatchItems, "Expected one formula commit item in the delayed batch.");
    }

    [TestMethod]
    public async Task CanvasRenderer_ExistingFormulaOpensEditorWithFormulaText()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsFormulaEditorProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const active = state?.sheetState?.activeCell || { row: 0, col: 0 };
                window.tmSpreadsheetCanvas.setCells(el, [{
                    row: active.row,
                    col: active.col,
                    value: '2',
                    Value: '2',
                    formula: '=1+1',
                    Formula: '=1+1'
                }]);

                requestAnimationFrame(() => {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'F2',
                        bubbles: true,
                        cancelable: true
                    }));

                    requestAnimationFrame(() => {
                        const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        resolve({
                            editorValue: input?.value || '',
                            formulaActive: !!after.sheetState?.formulaEditor?.active,
                            formulaRefCount: after.sheetState?.formulaEditor?.refCount || 0
                        });
                    });
                });
            })");

        Assert.AreEqual("=1+1", result.EditorValue, "Expected existing formula edit to open with the formula text, not the evaluated result.");
        Assert.IsTrue(result.FormulaActive, "Expected formula mode to become active when editing an existing formula.");
        Assert.AreEqual(0, result.FormulaRefCount, "Expected no cell reference tokens in the simple =1+1 formula.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_EditingDependencyRecalculatesFormulaCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        await EditCanvasCellAsync(page, grid, "A5", "10");
        await EditCanvasCellAsync(page, grid, "B5", "10");
        await EditCanvasCellAsync(page, grid, "C5", "=A5+B5");

        var initialFormula = await WaitForCanvasCellSnapshotAsync(
            grid,
            "C5",
            snapshot => snapshot.Formula == "=A5+B5" && snapshot.Value == "20",
            "Expected C5 to recalculate to 20 after creating =A5+B5.");

        Assert.AreEqual("20", initialFormula.Value, "Expected C5 to render the initial formula result.");
        Assert.AreEqual("=A5+B5", initialFormula.Formula, "Expected C5 to keep the committed formula text.");

        await EditCanvasCellAsync(page, grid, "B5", "15");

        var updatedFormula = await WaitForCanvasCellSnapshotAsync(
            grid,
            "C5",
            snapshot => snapshot.Formula == "=A5+B5" && snapshot.Value == "25",
            "Expected C5 to refresh after editing dependent cell B5.");

        Assert.AreEqual("25", updatedFormula.Value, "Expected dependent formula cell C5 to recalculate after B5 changes.");
        Assert.AreEqual("=A5+B5", updatedFormula.Formula, "Expected dependent formula sync not to lose the formula text.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_F4CyclesAbsoluteReferencesInFormulaEditor()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var cell = await GetCanvasCellCenterAsync(grid, "C5");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = cell.X, Y = cell.Y }
        });

        await grid.PressAsync("=");
        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.TypeAsync("A1");

        CollectionAssert.AreEqual(
            new[] { "=A1", "=$A$1", "=A$1", "=$A1", "=A1" },
            await ReadFormulaEditorCycleAsync(page, editor),
            "Expected F4 to cycle the last formula reference through Excel-style absolute reference states.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_F4CyclesReferenceAtCaretForFirstFormulaToken()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=A1+B2';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.setSelectionRange(2, 2);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'F4', bubbles: true, cancelable: true }));
                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        editorValue: input.value || '',
                        selectionStart: input.selectionStart ?? -1,
                        selectionEnd: input.selectionEnd ?? -1,
                        activeTokenIndex: after.sheetState?.formulaEditor?.activeTokenIndex ?? -1,
                        tokenReplaceCount: after.formulaEditorTokenReplaceCount - before.formulaEditorTokenReplaceCount
                    });
                });
            })");

        Assert.AreEqual("=$A$1+B2", result.EditorValue, "Expected F4 to target the first formula reference under the caret.");
        Assert.AreEqual(2, result.SelectionStart, $"Expected caret to stay inside the cycled first token. Actual: {result.SelectionStart}");
        Assert.AreEqual(result.SelectionStart, result.SelectionEnd, "Expected F4 to leave a collapsed caret selection.");
        Assert.AreEqual(0, result.ActiveTokenIndex, $"Expected the first token to remain active after F4. Actual token index: {result.ActiveTokenIndex}");
        Assert.AreEqual(0, result.TokenReplaceCount, "F4 should cycle the active token in place, not go through click-based token replacement.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_F4CyclesReferenceAtCaretForLastFormulaToken()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=A1+B2';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.setSelectionRange(5, 5);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'F4', bubbles: true, cancelable: true }));
                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        editorValue: input.value || '',
                        activeTokenIndex: after.sheetState?.formulaEditor?.activeTokenIndex ?? -1
                    });
                });
            })");

        Assert.AreEqual("=A1+$B$2", result.EditorValue, "Expected F4 to target the last formula reference when the caret is inside it.");
        Assert.AreEqual(1, result.ActiveTokenIndex, $"Expected the second token to remain active after F4. Actual token index: {result.ActiveTokenIndex}");
    }

    [TestMethod]
    public async Task CanvasJsEngine_F4CyclesRangeTokenAtCaret()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                const formula = '=SUM(A1:B5)+C7';
                input.value = formula;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                const caret = formula.indexOf('A1:B5') + 3;
                input.setSelectionRange(caret, caret);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'F4', bubbles: true, cancelable: true }));
                requestAnimationFrame(() => resolve({
                    editorValue: input.value || ''
                }));
            })");

        Assert.AreEqual("=SUM($A$1:$B$5)+C7", result.EditorValue, "Expected F4 to cycle the active range token under the caret.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_F4OutsideReferenceLeavesFormulaUnchanged()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                const formula = '=SUM(A1)+1';
                input.value = formula;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.setSelectionRange(2, 2);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'F4', bubbles: true, cancelable: true }));
                requestAnimationFrame(() => resolve({
                    editorValue: input.value || '',
                    selectionStart: input.selectionStart ?? -1
                }));
            })");

        Assert.AreEqual("=SUM(A1)+1", result.EditorValue, "Expected F4 outside a reference token to keep the formula unchanged.");
        Assert.AreEqual(2, result.SelectionStart, $"Expected caret to remain at the non-reference position. Actual: {result.SelectionStart}");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaSelfClickDoesNotInsertSelfReference()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=B2+C3';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.setSelectionRange(input.value.length, input.value.length);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowRight', bubbles: true, cancelable: true }));

                const rect = el.getBoundingClientRect();
                const active = state.sheetState?.activeCell || { row: 0, col: 0, ref: 'A1' };
                const x = rect.left + 40 + active.col * 64 + 32;
                const y = rect.top + 20 + active.row * 20 + 10;
                const pointer = type => el.dispatchEvent(new PointerEvent(type, {
                    pointerId: 33,
                    pointerType: 'mouse',
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons: type === 'pointerup' ? 0 : 1,
                    bubbles: true,
                    cancelable: true
                }));

                pointer('pointerdown');
                pointer('pointerup');

                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        editorValue: input.value || '',
                        startRef: active.ref || '',
                        activeRef: state.model.activeCellRef || state.model.ActiveCellRef || '',
                        ignoredSelfClickCount: after.formulaEditorIgnoredSelfClickCount - before.formulaEditorIgnoredSelfClickCount,
                        formulaActive: !!after.sheetState?.formulaEditor?.active
                    });
                });
            })");

        Assert.AreEqual("=B2+C3", result.EditorValue, "Expected clicking the edited formula cell not to inject a self reference.");
        Assert.AreEqual(result.StartRef, result.ActiveRef, "Expected self-click to keep the edited cell active.");
        Assert.IsTrue(result.IgnoredSelfClickCount > 0, $"Expected self-click to be counted as ignored. Count: {result.IgnoredSelfClickCount}.");
        Assert.IsTrue(result.FormulaActive, "Expected the formula editor to remain active after self-click.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaClickReplacesReferenceAtCaret()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=B2+C3';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.setSelectionRange(2, 2);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));

                const rect = el.getBoundingClientRect();
                const x = rect.left + 40 + 3 * 64 + 32;
                const y = rect.top + 20 + 3 * 20 + 10;
                const pointer = type => el.dispatchEvent(new PointerEvent(type, {
                    pointerId: 35,
                    pointerType: 'mouse',
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons: type === 'pointerup' ? 0 : 1,
                    bubbles: true,
                    cancelable: true
                }));

                pointer('pointerdown');
                pointer('pointerup');

                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        editorValue: input.value || '',
                        activeTokenIndex: after.sheetState?.formulaEditor?.activeTokenIndex ?? -1,
                        tokenReplaceCount: after.formulaEditorTokenReplaceCount - before.formulaEditorTokenReplaceCount
                    });
                });
            })");

        Assert.AreEqual("=D4+C3", result.EditorValue, "Expected clicking another cell to replace the reference token under the caret.");
        Assert.AreEqual(0, result.ActiveTokenIndex, "Expected the replaced first token to stay active.");
        Assert.IsTrue(result.TokenReplaceCount > 0, $"Expected caret-targeted click replacement to update one formula token. Count: {result.TokenReplaceCount}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaArrowLeftRightMoveCaretWithoutChangingActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, "=A1+B2");
        await SetFormulaEditorSelectionAsync(editor, 6, 6);
        var startRef = await GetCanvasActiveRefAsync(grid);

        await page.Keyboard.PressAsync("ArrowLeft");
        var afterLeft = await editor.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => ({
                selectionStart: el.selectionStart ?? -1,
                selectionEnd: el.selectionEnd ?? -1,
                editorValue: el.value || ''
            })");

        await page.Keyboard.PressAsync("ArrowRight");
        var afterRight = await editor.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => ({
                selectionStart: el.selectionStart ?? -1,
                selectionEnd: el.selectionEnd ?? -1,
                editorValue: el.value || ''
            })");

        var metrics = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => {
                const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    arrowCaretCount: after.formulaEditorArrowCaretCount || 0
                };
            }");

        Assert.AreEqual("=A1+B2", afterLeft.EditorValue, "Expected caret arrows not to commit or change the formula text.");
        Assert.AreEqual(5, afterLeft.SelectionStart, $"Expected ArrowLeft in formula editor to move caret left. Actual: {afterLeft.SelectionStart}");
        Assert.AreEqual(5, afterLeft.SelectionEnd, "Expected ArrowLeft to keep a collapsed caret.");
        Assert.AreEqual(6, afterRight.SelectionStart, $"Expected ArrowRight in formula editor to move caret right back. Actual: {afterRight.SelectionStart}");
        Assert.AreEqual(startRef, metrics.ActiveRef, "Expected caret arrows in formula editor not to change the grid active cell.");
        Assert.IsTrue(metrics.ArrowCaretCount >= 2, $"Expected caret arrow metric to count left/right moves. Count: {metrics.ArrowCaretCount}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaHighlightFollowsCaretAcrossTokens()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=A1+B2:C3';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.setSelectionRange(2, 2);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));

                requestAnimationFrame(() => {
                    const mid = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    input.setSelectionRange(6, 6);
                    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowRight', bubbles: true, cancelable: true }));
                    requestAnimationFrame(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        resolve({
                            startActiveTokenIndex: mid.sheetState?.formulaEditor?.activeTokenIndex ?? -1,
                            activeTokenIndex: after.sheetState?.formulaEditor?.activeTokenIndex ?? -1,
                            selectionPaintFrames: after.selectionPaintFrameCount - before.selectionPaintFrameCount,
                            contentPaintFrames: after.contentPaintFrameCount - before.contentPaintFrameCount,
                            caretMoveCount: after.formulaEditorCaretMoveCount - before.formulaEditorCaretMoveCount
                        });
                    });
                });
            })");

        Assert.AreEqual(0, result.StartActiveTokenIndex, "Expected caret on the first reference to activate the first token highlight.");
        Assert.AreEqual(1, result.ActiveTokenIndex, "Expected moving caret to the second reference to switch the active token highlight.");
        Assert.IsTrue(result.SelectionPaintFrames > 0, $"Expected caret-driven highlight updates to repaint the selection layer. Frames: {result.SelectionPaintFrames}.");
        Assert.AreEqual(0, result.ContentPaintFrames, $"Expected caret-driven highlight updates not to repaint content. Frames: {result.ContentPaintFrames}.");
        Assert.IsTrue(result.CaretMoveCount > 0, $"Expected caret movement metric to increase while switching active token. Count: {result.CaretMoveCount}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaClickReplacesCorrectTokenInMixedRangeAndSingleReferenceFormula()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => new Promise(resolve => {
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', { key: '=', bubbles: true, cancelable: true }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                const formula = '=SUM(A1:B5)+C7';
                input.value = formula;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                const caret = formula.indexOf('C7') + 1;
                input.setSelectionRange(caret, caret);
                input.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft', bubbles: true, cancelable: true }));

                const rect = el.getBoundingClientRect();
                const x = rect.left + 40 + 4 * 64 + 32;
                const y = rect.top + 20 + 5 * 20 + 10;
                const pointer = type => el.dispatchEvent(new PointerEvent(type, {
                    pointerId: 41,
                    pointerType: 'mouse',
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons: type === 'pointerup' ? 0 : 1,
                    bubbles: true,
                    cancelable: true
                }));

                pointer('pointerdown');
                pointer('pointerup');

                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        editorValue: input.value || '',
                        activeTokenIndex: after.sheetState?.formulaEditor?.activeTokenIndex ?? -1
                    });
                });
            })");

        Assert.AreEqual("=SUM(A1:B5)+E6", result.EditorValue, "Expected clicking another cell to replace only the caret-selected single-reference token in a mixed formula.");
        Assert.AreEqual(1, result.ActiveTokenIndex, "Expected the second token to remain active after replacement in a mixed formula.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarF4MatchesInlineEditorSemantics()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=A1+B2");
        await SetFormulaBarSelectionAsync(input, 2, 2);

        await page.Keyboard.PressAsync("F4");

        Assert.AreEqual("=$A$1+B2", await input.InputValueAsync(), "Expected formula bar F4 to cycle the active reference token under the caret.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarAutocompleteAcceptsFunctionFromKeyboard()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FocusAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("=SU");

        var suggestions = spreadsheet.Locator("[data-testid='tm-spreadsheet-formula-bar-suggestions']");
        await suggestions.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        StringAssert.Contains((await suggestions.TextContentAsync()) ?? string.Empty, "SUM", "Expected function suggestions to offer SUM for '=SU'.");

        await page.Keyboard.PressAsync("Enter");

        Assert.AreEqual("=SUM(", await input.InputValueAsync(), "Expected Enter on function suggestions to accept the selected formula function.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarAutocompleteKeyboardSelectionAcceptsHighlightedSuggestion()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FocusAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("=RAN");

        var suggestions = spreadsheet.Locator("[data-testid='tm-spreadsheet-formula-bar-suggestions']");
        await suggestions.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");

        Assert.AreEqual("=RANDBETWEEN(", await input.InputValueAsync(), "Expected ArrowDown plus Enter to accept the highlighted autocomplete suggestion.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarClickReplacesReferenceAtCaretWithoutChangingActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);
        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=A1+B2");
        await SetFormulaBarSelectionAsync(input, 5, 5);

        var startRef = await GetCanvasActiveRefAsync(grid);
        var targetRef = "J8";
        var target = await GetCanvasCellCenterAsync(grid, targetRef);
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual($"=A1+{targetRef}", await input.InputValueAsync(), "Expected grid click during formula bar editing to replace the caret-targeted token.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected formula bar reference picking not to move the active cell away from the edit origin.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarDragRangeReplacesReferenceWithoutChangingActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=A1+B2");
        await SetFormulaBarSelectionAsync(input, 5, 5);

        var startRef = await GetCanvasActiveRefAsync(grid);
        await DragCanvasBetweenCellsAsync(grid, "J8", "L10", pointerId: 143);
        await page.WaitForFunctionAsync(
            "el => (el.value || '') === '=A1+J8:L10'",
            await input.ElementHandleAsync());

        Assert.AreEqual("=A1+J8:L10", await input.InputValueAsync(), "Expected drag reference picking from the formula bar to replace the active token with a range reference.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected formula bar drag reference picking not to change the edit-origin active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarSelfClickKeepsSessionAndValue()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=A1+B2");
        await SetFormulaBarSelectionAsync(input, 5, 5);

        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });

        Assert.AreEqual("=A1+B2", await input.InputValueAsync(), "Expected self-click during formula bar editing to keep the current formula untouched.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), "Expected self-click during formula bar editing not to move the active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarContextMenuAttemptKeepsSessionAndDoesNotOpenGridMenu()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=A1+B2");
        await SetFormulaBarSelectionAsync(input, 5, 5);
        Assert.AreEqual("true", await spreadsheet.GetAttributeAsync("data-formula-point-mode"), "Expected the spreadsheet host to advertise formula-point mode before the context-menu gesture.");

        var otherCell = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Button = MouseButton.Right,
            Position = new() { X = otherCell.X, Y = otherCell.Y }
        });

        await page.WaitForTimeoutAsync(250);

        var debug = await grid.EvaluateAsync<CanvasClickSyncProbeResult>(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const host = el.closest('.tm-spreadsheet');
                const ref = host?.querySelector('.tm-spreadsheet-formula-bar__ref');
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    formulaBarRef: (ref?.textContent || '').trim(),
                    commandLogCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCommandLogBatch || 0,
                    cellPointerCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCellPointer || 0
                };
            }");

        Assert.AreEqual("=A1+B2", await input.InputValueAsync(), "Expected a context-menu gesture during formula-bar reference picking to keep the current formula text untouched.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), $"Expected a context-menu gesture during formula-bar reference picking not to move the active cell. activeRef={debug.ActiveRef}, formulaBarRef={debug.FormulaBarRef}, commandLogCallbacks={debug.CommandLogCallbacks}, cellPointerCallbacks={debug.CellPointerCallbacks}.");
        Assert.IsTrue(await input.IsVisibleAsync(), "Expected the formula-bar session to stay open after a context-menu gesture.");
        Assert.AreEqual(0, await spreadsheet.Locator(".tm-spreadsheet-context-menu").CountAsync(), "Expected the grid context menu to stay closed during formula-bar reference picking.");
    }

    public async Task CanvasJsEngine_FormulaBarClickReferencePickingMatchesInlineEditorForSameFormula()
    {
        const string formula = "=SUM(A1:B5)+C7";
        const string expected = "=SUM(A1:B5)+E6";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var inlineEditor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        var inlineCaret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaEditorSelectionAsync(inlineEditor, inlineCaret, inlineCaret);
        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });
        var inlineValue = await inlineEditor.InputValueAsync();
        await page.Keyboard.PressAsync("Escape");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var barCaret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaBarSelectionAsync(input, barCaret, barCaret);
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });
        var barValue = await input.InputValueAsync();

        Assert.AreEqual(expected, inlineValue, "Expected inline formula editor click reference-picking to replace the caret-targeted token.");
        Assert.AreEqual(expected, barValue, "Expected formula bar click reference-picking to replace the caret-targeted token with the same result as inline editing.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorPartialTokenSelectionReplacesWholeReference()
    {
        const string formula = "=SUM(A1)+B2";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        await SetFormulaEditorSelectionAsync(editor, 6, 7);

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=SUM(E6)+B2", await editor.InputValueAsync(), "Expected partial selection inside a reference token to replace the whole token in the inline formula editor.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarPartialTokenSelectionReplacesWholeReference()
    {
        const string formula = "=SUM(A1)+B2";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        await SetFormulaBarSelectionAsync(input, 6, 7);

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=SUM(E6)+B2", await input.InputValueAsync(), "Expected partial selection inside a reference token to replace the whole token in the formula bar.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorDoubleClickSelectionRefreshesActiveToken()
    {
        const string formula = "=SUM(A1)+B2";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        await editor.EvaluateAsync(
            @"el => {
                el.focus();
                el.setSelectionRange(5, 7);
                el.dispatchEvent(new Event('select', { bubbles: true }));
                el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
            }");

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=SUM(E6)+B2", await editor.InputValueAsync(), "Expected double-click token selection in the inline editor to refresh the active token before reference replacement.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarDoubleClickSelectionRefreshesActiveToken()
    {
        const string formula = "=SUM(A1)+B2";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        await input.EvaluateAsync(
            @"el => {
                el.focus();
                el.setSelectionRange(5, 7);
                el.dispatchEvent(new Event('select', { bubbles: true }));
                el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
            }");

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=SUM(E6)+B2", await input.InputValueAsync(), "Expected double-click token selection in the formula bar to refresh the active token before reference replacement.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarMixedFormulaClickReplacesOnlyCaretTargetedToken()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var caret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaBarSelectionAsync(input, caret, caret);

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=SUM(A1:B5)+E6", await input.InputValueAsync(), "Expected clicking another cell to replace only the caret-targeted single reference inside a mixed formula.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarMixedFormulaDragRangeReplacesOnlyCaretTargetedToken()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var caret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaBarSelectionAsync(input, caret, caret);

        await DragCanvasBetweenCellsAsync(grid, "J8", "L10", pointerId: 147);

        Assert.AreEqual("=SUM(A1:B5)+J8:L10", await input.InputValueAsync(), "Expected drag range reference-picking to replace only the caret-targeted single reference inside a mixed formula.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarClickIntoEmptyFunctionArgumentInsertsReferenceWithoutBreakingSyntax()
    {
        const string formula = "=SUM()";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        await SetFormulaBarSelectionAsync(input, 5, 5);

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=SUM(E6)", await input.InputValueAsync(), "Expected reference-picking at an empty argument position to insert a valid reference without breaking the formula syntax.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorComplexFormulaReplacementIgnoresStringLiteralReferences()
    {
        const string formula = "=IF(A1>10,\"A1\",SUM(B2:B4)+C7)";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        var caret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaEditorSelectionAsync(editor, caret, caret);

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=IF(A1>10,\"A1\",SUM(B2:B4)+E6)", await editor.InputValueAsync(), "Expected inline formula replacement to target only the active reference token and ignore A1-like text inside string literals.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarComplexFormulaReplacementIgnoresStringLiteralReferences()
    {
        const string formula = "=IF(A1>10,\"A1\",SUM(B2:B4)+C7)";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var caret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaBarSelectionAsync(input, caret, caret);

        var target = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });

        Assert.AreEqual("=IF(A1>10,\"A1\",SUM(B2:B4)+E6)", await input.InputValueAsync(), "Expected formula-bar replacement to target only the active reference token and ignore A1-like text inside string literals.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarSessionKeepsCaretAndActiveCellDuringViewportScroll()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var caret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaBarSelectionAsync(input, caret, caret);
        var startRef = await GetCanvasActiveRefAsync(grid);
        var before = await ReadTextInputSelectionAsync(input);

        await grid.EvaluateAsync(
            @"el => {
                el.scrollTop += 260;
                el.dispatchEvent(new Event('scroll', { bubbles: true }));
            }");
        await page.WaitForTimeoutAsync(180);

        var after = await ReadTextInputSelectionAsync(input);

        Assert.AreEqual(formula, await input.InputValueAsync(), "Expected viewport scroll during formula-bar editing to keep the live formula text intact.");
        Assert.AreEqual(before.SelectionStart, after.SelectionStart, "Expected viewport scroll during formula-bar editing to preserve caret start.");
        Assert.AreEqual(before.SelectionEnd, after.SelectionEnd, "Expected viewport scroll during formula-bar editing to preserve caret end.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected viewport scroll during formula-bar editing not to move the active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarSessionKeepsCaretAndActiveCellDuringResizeCommit()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var caret = formula.IndexOf("C7", StringComparison.Ordinal) + 1;
        await SetFormulaBarSelectionAsync(input, caret, caret);
        var before = await ReadTextInputSelectionAsync(input);
        var startRef = await GetCanvasActiveRefAsync(grid);

        var result = await grid.EvaluateAsync<CanvasFormulaResizeSessionProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const columns = model.Columns || model.columns || [];
                const column = columns[1] || columns[0];
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const left = column.Left ?? column.left ?? 0;
                const initialSize = column.Width ?? column.width ?? 64;
                const rect = el.getBoundingClientRect();
                const startX = rect.left + rowHeaderWidth + left + initialSize - 1;
                const startY = rect.top + Math.max(6, columnHeaderHeight / 2);
                const endX = startX + 36;
                const pointerId = 175;
                const dispatch = (type, x, y, buttons) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                dispatch('pointerdown', startX, startY, 1);
                let move = 0;
                const moveCount = 6;
                const step = () => {
                    move += 1;
                    dispatch('pointermove', startX + (endX - startX) * move / moveCount, startY, 1);
                    if (move < moveCount) {
                        requestAnimationFrame(step);
                        return;
                    }

                    dispatch('pointerup', endX, startY, 0);
                    setTimeout(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        const host = window.tmSpreadsheetFormulaRuntime?.getHostFormulaSession?.(el.closest('.tm-spreadsheet')) || null;
                        const syncedColumns = state.model.Columns || state.model.columns || [];
                        const synced = syncedColumns[1] || syncedColumns[0];
                        resolve({
                            activeRef: state.model.activeCellRef || state.model.ActiveCellRef || '',
                            hostText: host?.text || host?.Text || '',
                            hostSelectionStart: host?.selectionStart ?? host?.SelectionStart ?? -1,
                            hostSelectionEnd: host?.selectionEnd ?? host?.SelectionEnd ?? -1,
                            finalSize: synced?.Width ?? synced?.width ?? 0,
                            dotNetCallbacks: (after.resizeDotNetCallbackCount || 0) - (before.resizeDotNetCallbackCount || 0),
                            blazorFrames: (after.resizeBlazorFrameCount || 0) - (before.resizeBlazorFrameCount || 0)
                        });
                    }, 420);
                };

                requestAnimationFrame(step);
            })");

        var after = await ReadTextInputSelectionAsync(input);

        Assert.AreEqual(formula, await input.InputValueAsync(), "Expected resize commit during formula-bar editing to keep the live formula text intact.");
        Assert.AreEqual(before.SelectionStart, after.SelectionStart, "Expected resize commit during formula-bar editing to preserve caret start.");
        Assert.AreEqual(before.SelectionEnd, after.SelectionEnd, "Expected resize commit during formula-bar editing to preserve caret end.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected resize commit during formula-bar editing not to move the active cell.");
        Assert.AreEqual(startRef, result.ActiveRef, "Expected canvas model active cell to stay at the formula edit origin after resize commit.");
        Assert.AreEqual(formula, result.HostText, "Expected host formula session to stay authoritative through resize commit.");
        Assert.AreEqual(before.SelectionStart, result.HostSelectionStart, "Expected host formula session caret start to survive resize commit.");
        Assert.AreEqual(before.SelectionEnd, result.HostSelectionEnd, "Expected host formula session caret end to survive resize commit.");
        Assert.IsTrue(result.FinalSize > 64, $"Expected resize commit to actually update the synced column width. Width: {result.FinalSize:N1}.");
        Assert.IsTrue(result.DotNetCallbacks <= 1, $"Expected resize commit to remain a bounded single callback path. Count: {result.DotNetCallbacks:N0}.");
        Assert.IsTrue(result.BlazorFrames <= 2, $"Expected resize commit to keep Blazor frames bounded while the formula session survives. Frames: {result.BlazorFrames:N0}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaCommitClearsFormulaHighlightsAndPointMode()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "E4");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "E4");

        await grid.PressAsync("=");
        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await editor.FillAsync("=E2+E3");
        await SetFormulaEditorSelectionAsync(editor, "=E2+E3".Length, "=E2+E3".Length);
        await editor.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);

        var result = await grid.EvaluateAsync<CanvasFormulaSessionCleanupProbeResult>(
            @"el => {
                const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const host = el.closest('.tm-spreadsheet');
                const session = host?.__tmSpreadsheetFormulaSession || null;
                const editor = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                const stateEditor = el.__tmSpreadsheetCanvas?.editor || null;
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    formulaActive: !!after.sheetState?.formulaEditor?.active,
                    highlightedCells: after.formulaEditorHighlightCount || 0,
                    hostFormulaPointMode: host?.dataset?.formulaPointMode === 'true',
                    hostSessionOwner: session?.owner || '',
                    hostSessionText: `${session?.text || ''}|editorVisible=${!!editor}|editorCount=${el.querySelectorAll('.tm-spreadsheet-canvas-grid__editor').length}|editorLabel=${editor?.getAttribute('aria-label') || ''}|editorMode=${editor?.getAttribute('data-editor-mode') || ''}|stateEditor=${!!stateEditor}|stateEditorCell=${stateEditor ? `${stateEditor.row},${stateEditor.col}` : ''}|removeAttempt=${after.editorRemoveAttemptCount || 0}|removeComplete=${after.editorRemoveCompleteCount || 0}|domAfterRemove=${after.editorLastDomCountAfterRemove || 0}`
                };
            }");

        var resultSummary = $"activeRef={result.ActiveRef}; formulaActive={result.FormulaActive}; highlightedCells={result.HighlightedCells}; hostFormulaPointMode={result.HostFormulaPointMode}; hostSessionOwner={result.HostSessionOwner}; hostSessionText={result.HostSessionText}";

        Assert.IsFalse(result.FormulaActive, $"Expected inline formula editor to close after Enter commit. {resultSummary}");
        Assert.AreEqual(0, result.HighlightedCells, $"Expected formula reference highlights to clear after inline commit. {resultSummary}");
        Assert.IsFalse(result.HostFormulaPointMode, $"Expected spreadsheet host formula-point mode to clear after inline commit. {resultSummary}");
        Assert.AreEqual(string.Empty, result.HostSessionOwner, $"Expected no lingering host formula session after inline commit. {resultSummary}");

        var target = await GetCanvasCellCenterAsync(grid, "H6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "H6");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarCommitClearsFormulaHighlightsAndRestoresMousePicking()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "F3");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "F3");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=E2+E3");
        await page.Keyboard.PressAsync("Enter");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        await page.WaitForTimeoutAsync(200);

        var result = await grid.EvaluateAsync<CanvasFormulaSessionCleanupProbeResult>(
            @"el => {
                const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const host = el.closest('.tm-spreadsheet');
                const session = host?.__tmSpreadsheetFormulaSession || null;
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    formulaActive: !!after.sheetState?.formulaEditor?.active,
                    highlightedCells: after.formulaEditorHighlightCount || 0,
                    hostFormulaPointMode: host?.dataset?.formulaPointMode === 'true',
                    hostSessionOwner: session?.owner || '',
                    hostSessionText: session?.text || ''
                };
            }");

        var resultSummary = $"activeRef={result.ActiveRef}; formulaActive={result.FormulaActive}; highlightedCells={result.HighlightedCells}; hostFormulaPointMode={result.HostFormulaPointMode}; hostSessionOwner={result.HostSessionOwner}; hostSessionText={result.HostSessionText}";

        Assert.IsFalse(result.FormulaActive, $"Expected no inline formula editor to remain active after formula-bar commit. {resultSummary}");
        Assert.AreEqual(0, result.HighlightedCells, $"Expected formula reference highlights to clear after formula-bar commit. {resultSummary}");
        Assert.IsFalse(result.HostFormulaPointMode, $"Expected spreadsheet host formula-point mode to clear after formula-bar commit. {resultSummary}");
        Assert.AreEqual(string.Empty, result.HostSessionOwner, $"Expected no lingering host formula session after formula-bar commit. {resultSummary}");

        var target = await GetCanvasCellCenterAsync(grid, "H6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = target.X, Y = target.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "H6");
    }

    [TestMethod]
    public async Task CanvasJsEngine_F2TransfersFormulaSessionFromFormulaBarToInlineEditorWithoutLosingCaret()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=SUM(A1, B1)");
        await SetFormulaBarSelectionAsync(input, 9, 11);

        await input.PressAsync("F2");

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var selection = await ReadTextInputSelectionAsync(editor);

        Assert.AreEqual("=SUM(A1, B1)", await editor.InputValueAsync(), "Expected formula-bar session text to transfer into inline editor.");
        Assert.AreEqual(9, selection.SelectionStart, "Expected transferred inline editor caret start to match formula bar.");
        Assert.AreEqual(11, selection.SelectionEnd, "Expected transferred inline editor caret end to match formula bar.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaSessionTransfersFromInlineEditorToFormulaBarWithoutLosingCaret()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeCell = await GetCanvasCellCenterAsync(grid, "A1");
        await grid.DblClickAsync(new LocatorDblClickOptions
        {
            Force = true,
            Position = new() { X = activeCell.X, Y = activeCell.Y }
        });

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await editor.FillAsync("=SUM(A1, B1)");
        await SetFormulaEditorSelectionAsync(editor, 6, 8);

        await spreadsheet.Locator(".tm-spreadsheet-formula-bar__display").ClickAsync();

        var input = page.Locator("[data-testid='tm-spreadsheet-formula-bar-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var selection = await ReadTextInputSelectionAsync(input);

        Assert.AreEqual("=SUM(A1, B1)", await input.InputValueAsync(), "Expected inline editor session text to transfer into formula bar.");
        Assert.AreEqual(6, selection.SelectionStart, "Expected transferred formula-bar caret start to match inline editor.");
        Assert.AreEqual(8, selection.SelectionEnd, "Expected transferred formula-bar caret end to match inline editor.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorAutocompleteAcceptsFunctionFromKeyboard()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeCell = await GetCanvasCellCenterAsync(grid, "A1");
        await grid.DblClickAsync(new LocatorDblClickOptions
        {
            Force = true,
            Position = new() { X = activeCell.X, Y = activeCell.Y }
        });

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await editor.FillAsync("=SU");

        var suggestions = grid.Locator(".tm-spreadsheet-canvas-grid__formula-suggestions");
        await suggestions.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await editor.PressAsync("Enter");

        Assert.AreEqual("=SUM(", await editor.InputValueAsync(), "Expected inline editor Enter to accept shared autocomplete suggestion before commit.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorShowsSharedFunctionHint()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeCell = await GetCanvasCellCenterAsync(grid, "A1");
        await grid.DblClickAsync(new LocatorDblClickOptions
        {
            Force = true,
            Position = new() { X = activeCell.X, Y = activeCell.Y }
        });

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await editor.FillAsync("=SUM(A1,");

        var hint = grid.Locator(".tm-spreadsheet-canvas-grid__formula-hint");
        await hint.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        StringAssert.Contains(await hint.InnerTextAsync(), "SUM");
        Assert.AreEqual(1, await hint.Locator(".tm-spreadsheet-canvas-grid__formula-hint-arg--active").CountAsync(), "Expected inline shared hint to highlight exactly one active argument.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarArrowUpDownKeepSessionAndDoNotChangeActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=SUM(A1:B5)+C7");
        await SetFormulaBarSelectionAsync(input, 7, 7);

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowUp");
        var afterArrows = await ReadTextInputSelectionAsync(input);

        Assert.AreEqual("=SUM(A1:B5)+C7", afterArrows.EditorValue, "Expected ArrowUp/ArrowDown in the formula bar to keep the current formula text untouched.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), "Expected ArrowUp/ArrowDown during formula-bar editing not to trigger grid navigation.");
        Assert.IsTrue(await input.IsVisibleAsync(), "Expected the formula-bar session to stay open after ArrowUp/ArrowDown.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorArrowUpDownKeepSessionAndDoNotChangeActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        var startRef = await GetCanvasActiveRefAsync(grid);
        await SetFormulaEditorSelectionAsync(editor, 7, 7);

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowUp");
        var afterArrows = await ReadTextInputSelectionAsync(editor);

        Assert.AreEqual(formula, afterArrows.EditorValue, "Expected ArrowUp/ArrowDown in the inline formula editor to keep the current formula text untouched.");
        Assert.AreEqual(7, afterArrows.SelectionStart, "Expected ArrowUp/ArrowDown in the inline formula editor to keep the caret collapsed at the same position.");
        Assert.AreEqual(7, afterArrows.SelectionEnd, "Expected ArrowUp/ArrowDown in the inline formula editor not to extend the selection.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected ArrowUp/ArrowDown during inline formula editing not to trigger grid navigation.");
        Assert.IsTrue(await editor.IsVisibleAsync(), "Expected the inline formula session to stay open after ArrowUp/ArrowDown.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarSelectionShortcutsDoNotChangeActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=SUM(A1:B5)+C7");
        await SetFormulaBarSelectionAsync(input, 7, 7);

        await page.Keyboard.PressAsync("Home");
        var afterHome = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("End");
        var afterEnd = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("Shift+ArrowLeft");
        var afterShiftLeft = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        var afterShiftRight = await ReadTextInputSelectionAsync(input);

        Assert.AreEqual(0, afterHome.SelectionStart, "Expected Home in formula bar to move caret to the beginning of the formula.");
        Assert.AreEqual(0, afterHome.SelectionEnd, "Expected Home in formula bar to keep a collapsed caret.");
        Assert.AreEqual("=SUM(A1:B5)+C7".Length, afterEnd.SelectionStart, "Expected End in formula bar to move caret to the end of the formula.");
        Assert.AreEqual(afterEnd.SelectionStart - 1, afterShiftLeft.SelectionStart, "Expected Shift+ArrowLeft to extend the selection one character to the left.");
        Assert.AreEqual(afterEnd.SelectionStart, afterShiftLeft.SelectionEnd, "Expected Shift+ArrowLeft to keep the anchor at the previous caret position.");
        Assert.AreEqual(afterEnd.SelectionStart, afterShiftRight.SelectionStart, "Expected Shift+ArrowRight to collapse the selection back to the original end position.");
        Assert.AreEqual(afterEnd.SelectionStart, afterShiftRight.SelectionEnd, "Expected Shift+ArrowRight to restore a collapsed caret.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), "Expected text caret shortcuts in the formula bar not to change the grid active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarAdvancedWordNavigationShortcutsKeepActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        await SetFormulaBarSelectionAsync(input, formula.Length, formula.Length);

        await page.Keyboard.PressAsync("Control+ArrowLeft");
        var afterCtrlLeft = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("Control+ArrowRight");
        var afterCtrlRight = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("Home");
        var afterHome = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("End");
        var afterEnd = await ReadTextInputSelectionAsync(input);

        Assert.IsTrue(afterCtrlLeft.SelectionStart < formula.Length, $"Expected Ctrl+ArrowLeft in formula bar to move the caret left by a word. Actual: {afterCtrlLeft.SelectionStart}");
        Assert.AreEqual(afterCtrlLeft.SelectionStart, afterCtrlLeft.SelectionEnd, "Expected Ctrl+ArrowLeft to keep a collapsed caret.");
        Assert.AreEqual(formula.Length, afterCtrlRight.SelectionStart, $"Expected Ctrl+ArrowRight in formula bar to move the caret back to the end. Actual: {afterCtrlRight.SelectionStart}");
        Assert.AreEqual(0, afterHome.SelectionStart, "Expected Home in formula bar to move caret to the beginning.");
        Assert.AreEqual(formula.Length, afterEnd.SelectionStart, "Expected End in formula bar to move caret to the end.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), "Expected advanced caret navigation shortcuts in the formula bar not to change the active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarExtendedRangeShortcutsKeepSessionAndDoNotChangeActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        await SetFormulaBarSelectionAsync(input, formula.Length, formula.Length);

        await page.Keyboard.PressAsync("Control+Shift+ArrowLeft");
        var afterCtrlShiftLeft = await ReadTextInputSelectionAsync(input);
        await page.Keyboard.PressAsync("Shift+ArrowUp");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("PageUp");
        await page.Keyboard.PressAsync("PageDown");
        var afterExtended = await ReadTextInputSelectionAsync(input);

        Assert.IsTrue(afterCtrlShiftLeft.SelectionStart < afterCtrlShiftLeft.SelectionEnd, $"Expected Ctrl+Shift+ArrowLeft in formula bar to extend the selection. Start: {afterCtrlShiftLeft.SelectionStart}, end: {afterCtrlShiftLeft.SelectionEnd}.");
        Assert.AreEqual(formula.Length, afterCtrlShiftLeft.SelectionEnd, "Expected Ctrl+Shift+ArrowLeft to keep the original end-of-formula anchor.");
        Assert.AreEqual(formula, afterExtended.EditorValue, "Expected extended range shortcuts in the formula bar to keep the live formula text intact.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), "Expected Shift+ArrowUp/Down, Ctrl+Shift+Arrow and PageUp/PageDown in the formula bar not to change the active cell.");
        Assert.IsTrue(await input.IsVisibleAsync(), "Expected the formula-bar session to stay open after extended range shortcuts.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarDeleteAndCtrlBackspaceEditTextWithoutChangingActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync(formula);
        var plusIndex = formula.IndexOf('+', StringComparison.Ordinal);
        await SetFormulaBarSelectionAsync(input, plusIndex, plusIndex);
        await page.Keyboard.PressAsync("Delete");
        var afterDelete = await input.InputValueAsync();
        await SetFormulaBarSelectionAsync(input, afterDelete.Length, afterDelete.Length);
        await page.Keyboard.PressAsync("Control+Backspace");
        var afterCtrlBackspace = await input.InputValueAsync();

        Assert.AreEqual("=SUM(A1:B5)C7", afterDelete, "Expected Delete in formula bar to remove the next character at the caret.");
        Assert.AreEqual("=SUM(A1:B5)", afterCtrlBackspace, "Expected Ctrl+Backspace in formula bar to remove the previous word-like token.");
        Assert.AreEqual("J8", await GetCanvasActiveRefAsync(grid), "Expected editing shortcuts in the formula bar not to change the active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorAdvancedWordNavigationShortcutsKeepActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        var startRef = await GetCanvasActiveRefAsync(grid);
        await SetFormulaEditorSelectionAsync(editor, formula.Length, formula.Length);

        await page.Keyboard.PressAsync("Control+ArrowLeft");
        var afterCtrlLeft = await ReadTextInputSelectionAsync(editor);
        await page.Keyboard.PressAsync("Control+ArrowRight");
        var afterCtrlRight = await ReadTextInputSelectionAsync(editor);
        await page.Keyboard.PressAsync("Home");
        var afterHome = await ReadTextInputSelectionAsync(editor);
        await page.Keyboard.PressAsync("End");
        var afterEnd = await ReadTextInputSelectionAsync(editor);

        Assert.IsTrue(afterCtrlLeft.SelectionStart < formula.Length, $"Expected Ctrl+ArrowLeft in inline formula editor to move the caret left by a word. Actual: {afterCtrlLeft.SelectionStart}");
        Assert.AreEqual(afterCtrlLeft.SelectionStart, afterCtrlLeft.SelectionEnd, "Expected Ctrl+ArrowLeft to keep a collapsed caret.");
        Assert.AreEqual(formula.Length, afterCtrlRight.SelectionStart, $"Expected Ctrl+ArrowRight in inline formula editor to move the caret back to the end. Actual: {afterCtrlRight.SelectionStart}");
        Assert.AreEqual(0, afterHome.SelectionStart, "Expected Home in inline formula editor to move caret to the beginning.");
        Assert.AreEqual(formula.Length, afterEnd.SelectionStart, "Expected End in inline formula editor to move caret to the end.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected advanced caret navigation shortcuts in the inline formula editor not to change the active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorExtendedRangeShortcutsKeepSessionAndDoNotChangeActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        var startRef = await GetCanvasActiveRefAsync(grid);
        await SetFormulaEditorSelectionAsync(editor, formula.Length, formula.Length);

        await page.Keyboard.PressAsync("Control+Shift+ArrowLeft");
        var afterCtrlShiftLeft = await ReadTextInputSelectionAsync(editor);
        await page.Keyboard.PressAsync("Shift+ArrowUp");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("PageUp");
        await page.Keyboard.PressAsync("PageDown");
        var afterExtended = await ReadTextInputSelectionAsync(editor);

        Assert.IsTrue(afterCtrlShiftLeft.SelectionStart < afterCtrlShiftLeft.SelectionEnd, $"Expected Ctrl+Shift+ArrowLeft in inline formula editor to extend the selection. Start: {afterCtrlShiftLeft.SelectionStart}, end: {afterCtrlShiftLeft.SelectionEnd}.");
        Assert.AreEqual(formula.Length, afterCtrlShiftLeft.SelectionEnd, "Expected Ctrl+Shift+ArrowLeft to keep the original end-of-formula anchor.");
        Assert.AreEqual(formula, afterExtended.EditorValue, "Expected extended range shortcuts in the inline formula editor to keep the live formula text intact.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected Shift+ArrowUp/Down, Ctrl+Shift+Arrow and PageUp/PageDown in the inline formula editor not to change the active cell.");
        Assert.IsTrue(await editor.IsVisibleAsync(), "Expected the inline formula session to stay open after extended range shortcuts.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorDeleteAndCtrlBackspaceEditTextWithoutChangingActiveCell()
    {
        const string formula = "=SUM(A1:B5)+C7";

        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, formula);
        var startRef = await GetCanvasActiveRefAsync(grid);
        var plusIndex = formula.IndexOf('+', StringComparison.Ordinal);
        await SetFormulaEditorSelectionAsync(editor, plusIndex, plusIndex);
        await page.Keyboard.PressAsync("Delete");
        var afterDelete = await editor.InputValueAsync();
        await SetFormulaEditorSelectionAsync(editor, afterDelete.Length, afterDelete.Length);
        await page.Keyboard.PressAsync("Control+Backspace");
        var afterCtrlBackspace = await editor.InputValueAsync();

        Assert.AreEqual("=SUM(A1:B5)C7", afterDelete, "Expected Delete in inline formula editor to remove the next character at the caret.");
        Assert.AreEqual("=SUM(A1:B5)", afterCtrlBackspace, "Expected Ctrl+Backspace in inline formula editor to remove the previous word-like token.");
        Assert.AreEqual(startRef, await GetCanvasActiveRefAsync(grid), "Expected editing shortcuts in the inline formula editor not to change the active cell.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarLongSessionCombinesAutocompleteReferencePickingScrollAndCommit()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);

        var activeTarget = await GetCanvasCellCenterAsync(grid, "J8");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = activeTarget.X, Y = activeTarget.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "J8");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FocusAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("=SU");

        var suggestions = spreadsheet.Locator("[data-testid='tm-spreadsheet-formula-bar-suggestions']");
        await suggestions.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");
        Assert.AreEqual("=SUM(", await input.InputValueAsync(), "Expected long formula-bar session to accept SUM from autocomplete.");

        var firstArg = await GetCanvasCellCenterAsync(grid, "E6");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = firstArg.X, Y = firstArg.Y }
        });
        Assert.AreEqual("=SUM(E6", await input.InputValueAsync(), "Expected first reference click to insert the first SUM argument.");

        await page.Keyboard.TypeAsync(",");
        await grid.EvaluateAsync(
            @"el => {
                el.scrollTop += 260;
                el.dispatchEvent(new Event('scroll', { bubbles: true }));
            }");
        await page.WaitForTimeoutAsync(180);

        await DragCanvasBetweenCellsAsync(grid, "J20", "L22");
        Assert.AreEqual("=SUM(E6,J20:L22", await input.InputValueAsync(), "Expected drag range after viewport scroll to replace the active argument token.");

        await page.Keyboard.TypeAsync(")");
        await page.Keyboard.PressAsync("Enter");

        await WaitForCanvasActiveRefAsync(grid, "J9");
        var committed = await WaitForCanvasCellSnapshotAsync(
            grid,
            "J8",
            snapshot => string.Equals(snapshot.Formula, "=SUM(E6,J20:L22)", StringComparison.Ordinal),
            "Expected long shared formula session to commit the final SUM formula into the original active cell.");

        Assert.AreEqual("=SUM(E6,J20:L22)", committed.Formula, "Expected committed formula to keep autocomplete, click reference and drag range edits in one shared session.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarShowsActiveFunctionArgumentHint()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=SUM(A1,");
        await SetFormulaBarSelectionAsync(input, 8, 8);

        var hint = page.Locator("[data-testid='tm-spreadsheet-formula-bar-hint']");
        await hint.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var activeArg = hint.Locator(".tm-spreadsheet-formula-bar__hint-arg--active");

        StringAssert.Contains((await hint.TextContentAsync()) ?? string.Empty, "SUM", "Expected function hint to describe the active SUM call.");
        Assert.AreEqual("number2", (await activeArg.TextContentAsync())?.Trim(), "Expected the active argument hint to advance after typing the first separator.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaBarCzechDecimalCommaDoesNotAdvanceArgumentHintPrematurely()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'cs')");
        await page.ReloadAsync();
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        Assert.AreEqual(";", await spreadsheet.GetAttributeAsync("data-formula-argument-separator"), "Expected Czech culture to advertise semicolon as the formula argument separator.");
        Assert.AreEqual(",", await spreadsheet.GetAttributeAsync("data-formula-decimal-separator"), "Expected Czech culture to advertise comma as the formula decimal separator.");

        var input = await OpenFormulaBarEditorAsync(page, spreadsheet);
        await input.FillAsync("=SUM(1,2;");
        await SetFormulaBarSelectionAsync(input, 9, 9);

        var hint = page.Locator("[data-testid='tm-spreadsheet-formula-bar-hint']");
        await hint.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var activeArg = hint.Locator(".tm-spreadsheet-formula-bar__hint-arg--active");

        Assert.AreEqual("number2", (await activeArg.TextContentAsync())?.Trim(), "Expected decimal comma in Czech formula input to stay inside the first SUM argument until the semicolon separator is typed.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_InlineFormulaEditorCzechDecimalCommaDoesNotAdvanceArgumentHintPrematurely()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'cs')");
        await page.ReloadAsync();
        await WaitForAppReadyAsync(page);

        var spreadsheet = page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);
        Assert.AreEqual(";", await spreadsheet.GetAttributeAsync("data-formula-argument-separator"), "Expected Czech culture to advertise semicolon as the formula argument separator.");
        Assert.AreEqual(",", await spreadsheet.GetAttributeAsync("data-formula-decimal-separator"), "Expected Czech culture to advertise comma as the formula decimal separator.");

        var editor = await OpenCanvasFormulaEditorAsync(page, grid, "=SUM(1,2;");
        await SetFormulaEditorSelectionAsync(editor, 9, 9);

        var hint = grid.Locator(".tm-spreadsheet-canvas-grid__formula-hint");
        await hint.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var activeArg = hint.Locator(".tm-spreadsheet-canvas-grid__formula-hint-arg--active");

        Assert.AreEqual(1, await activeArg.CountAsync(), "Expected inline formula hint to highlight exactly one active argument in Czech decimal-comma mode.");
        Assert.AreEqual("number2", (await activeArg.TextContentAsync())?.Trim(), "Expected decimal comma in Czech inline formula input to stay inside the first SUM argument until the semicolon separator is typed.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_ContextMenuClickKeepsOriginalActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var cell = await GetCanvasCellCenterAsync(grid, "C5");
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = cell.X, Y = cell.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, "C5");

        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Button = MouseButton.Right,
            Position = new() { X = cell.X, Y = cell.Y }
        });

        var menu = page.Locator(".tm-spreadsheet-context-menu").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await menu.Locator(".tm-spreadsheet-context-menu__item").First.ClickAsync();

        var dialog = page.Locator(".tm-fcd").First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var activeRef = await GetCanvasActiveRefAsync(grid);
        Assert.AreEqual("C5", activeRef, "Clicking a context-menu item must not select the spreadsheet cell underneath the menu.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_PublicApiCellUpdatesReachCanvasStore()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await page.WaitForFunctionAsync(
            @"el => {
                const cells = el?.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells;
                return cells?.get('0:0')?.value === 'Canvas JS engine'
                    && cells?.get('0:1')?.value === 'Public API sync'
                    && cells?.get('1:0')?.value === 'Arrow keys'
                    && cells?.get('1:1')?.value === 'Formula/editor hot path';
            }",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var result = await grid.EvaluateAsync<CanvasPublicApiProbeResult>(
            @"el => new Promise(resolve => {
                const readValue = key => {
                    const cell = el.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells?.get(key);
                    return cell?.value || cell?.Value || '';
                };

                requestAnimationFrame(() => {
                    requestAnimationFrame(() => {
                        resolve({
                            a1: readValue('0:0'),
                            b1: readValue('0:1'),
                            a2: readValue('1:0'),
                            b2: readValue('1:1')
                        });
                    });
                });
            })");

        Assert.AreEqual("Canvas JS engine", result.A1, "Expected the demo page public API initialization to populate A1 in the canvas JS engine store.");
        Assert.AreEqual("Public API sync", result.B1, "Expected the demo page public API initialization to populate B1 in the canvas JS engine store.");
        Assert.AreEqual("Arrow keys", result.A2, "Expected the demo page public API initialization to populate A2 in the canvas JS engine store.");
        Assert.AreEqual("Formula/editor hot path", result.B2, "Expected the demo page public API initialization to populate B2 in the canvas JS engine store.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_KeyboardNavigationKeepsGridFocusAndUpdatesAccessibilityState()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.FocusAsync();
        var beforeRef = await GetCanvasActiveRefAsync(grid);
        var expectedRowIndex = ParseRow(beforeRef) + 1;
        var expectedColIndex = ParseColumn(beforeRef);
        await grid.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(250);

        var result = await grid.EvaluateAsync<CanvasAccessibilityProbeResult>(
            @"el => {
                const activeId = el.getAttribute('aria-activedescendant') || '';
                const active = activeId ? document.getElementById(activeId) : null;
                const live = document.getElementById(el?.dataset?.a11yLiveRegionId || '');
                return {
                    rootFocused: document.activeElement === el,
                    activeDescendant: activeId,
                    activeText: active?.textContent || '',
                    activeRowIndex: active?.getAttribute('aria-rowindex') || '',
                    activeColIndex: active?.getAttribute('aria-colindex') || '',
                    liveText: live?.textContent || ''
                };
            }");

        Assert.IsTrue(result.RootFocused, "Expected the canvas grid root to keep keyboard focus after ArrowDown navigation.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ActiveDescendant), "Expected the grid to expose aria-activedescendant.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ActiveText), "Expected the active cell accessibility proxy to expose readable text.");
        Assert.AreEqual(expectedRowIndex.ToString(CultureInfo.InvariantCulture), result.ActiveRowIndex, $"Expected aria-rowindex to move one row down from {beforeRef}. Actual: {result.ActiveRowIndex}");
        Assert.AreEqual(expectedColIndex.ToString(CultureInfo.InvariantCulture), result.ActiveColIndex, $"Expected aria-colindex to stay on the same column as {beforeRef}. Actual: {result.ActiveColIndex}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.LiveText), "Expected the canvas grid to expose a non-empty live region after keyboard navigation.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_FormulaEditorExposesTextboxAccessibilityAndCaret()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();
        await grid.PressAsync("=");

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var result = await editor.EvaluateAsync<CanvasEditorAccessibilityProbeResult>(
            @"el => ({
                focused: document.activeElement === el,
                role: el.getAttribute('role') || '',
                ariaLabel: el.getAttribute('aria-label') || '',
                ariaDescribedBy: el.getAttribute('aria-describedby') || '',
                editorMode: el.dataset.editorMode || '',
                selectionStart: el.selectionStart ?? -1,
                selectionEnd: el.selectionEnd ?? -1,
                value: el.value || ''
            })");

        Assert.IsTrue(result.Focused, "Expected formula editor input to keep focus after opening from keyboard typing.");
        Assert.AreEqual("textbox", result.Role, $"Expected formula editor to expose textbox role. Actual: {result.Role}");
        Assert.IsTrue(result.AriaLabel.Contains("Formula editor", StringComparison.OrdinalIgnoreCase), $"Expected formula editor aria-label. Actual: {result.AriaLabel}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AriaDescribedBy), "Expected formula editor to reference the canvas accessibility description.");
        Assert.AreEqual("formula", result.EditorMode, $"Expected formula editor mode marker. Actual: {result.EditorMode}");
        Assert.AreEqual("=", result.Value, $"Expected keyboard-opened formula editor to start with '='. Actual: {result.Value}");
        Assert.AreEqual(1, result.SelectionStart, $"Expected caret to stay after the opening '='. Actual: {result.SelectionStart}");
        Assert.AreEqual(1, result.SelectionEnd, $"Expected selection end to stay after the opening '='. Actual: {result.SelectionEnd}");
    }

    [TestMethod]
    public async Task CanvasJsEngine_Paste100x20UsesSingleRangeBatch()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasPasteProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                state.sheetState.activeCell = { row: 0, col: 0, ref: 'A1' };
                state.sheetState.selection = { startRow: 0, startCol: 0, endRow: 0, endCol: 0 };
                state.model.ActiveCellRef = 'A1';
                state.model.activeCellRef = 'A1';
                state.model.Selection = { startRow: 0, startCol: 0, endRow: 0, endCol: 0 };
                state.model.selection = { startRow: 0, startCol: 0, endRow: 0, endCol: 0 };
                const text = Array.from({ length: 100 }, (_, row) =>
                    Array.from({ length: 20 }, (_, col) => `Paste ${row + 1}:${col + 1}`).join('\t')
                ).join('\n');
                const started = performance.now();
                window.tmSpreadsheetCanvas.applyClipboardText(el, text);

                setTimeout(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const readValue = key => {
                        const cell = el.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells?.get(key);
                        return String(cell?.value ?? cell?.Value ?? '');
                    };

                    resolve({
                        elapsedMs: performance.now() - started,
                        topLeft: readValue('0:0'),
                        bottomRight: readValue('99:19'),
                        rangeChangedCommands: after.rangeChangedCommandCount - before.rangeChangedCommandCount,
                        commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0),
                        commandLogBatchCallbacks: after.commandLogBatchCallbackCount - before.commandLogBatchCallbackCount,
                        commandLogBatchItems: after.commandLogBatchItemCount - before.commandLogBatchItemCount,
                        contentPaintFrames: after.contentPaintFrameCount - before.contentPaintFrameCount
                    });
                }, 320);
            })");

        Assert.AreEqual("Paste 1:1", result.TopLeft, "Expected JS paste to update the top-left pasted cell immediately in the canvas store.");
        Assert.AreEqual("Paste 100:20", result.BottomRight, "Expected JS paste to update the bottom-right pasted cell immediately in the canvas store.");
        Assert.AreEqual(1, result.RangeChangedCommands, $"Expected one rangeChanged command for the 100x20 paste. Count: {result.RangeChangedCommands}.");
        Assert.AreEqual(1, result.CommandLogCallbacks, $"Expected one .NET command-log callback for the paste batch. Count: {result.CommandLogCallbacks}.");
        Assert.AreEqual(1, result.CommandLogBatchCallbacks, $"Expected one command-log batch callback for the paste batch. Count: {result.CommandLogBatchCallbacks}.");
        Assert.AreEqual(1, result.CommandLogBatchItems, $"Expected one command payload in the paste batch. Items: {result.CommandLogBatchItems}.");
        Assert.IsTrue(result.ContentPaintFrames > 0, "Expected the paste hot path to repaint canvas content.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_AutoFillCommitsAsSingleRangeBatch()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasAutoFillProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                window.tmSpreadsheetCanvas.setCells(el, [
                    { row: 0, col: 0, value: '1', Value: '1' },
                    { row: 1, col: 0, value: '2', Value: '2' }
                ]);

                const selection = { startRow: 0, startCol: 0, endRow: 1, endCol: 0 };
                state.sheetState.activeCell = { row: 1, col: 0, ref: 'A2' };
                state.sheetState.selection = { ...selection };
                state.model.ActiveCellRef = 'A2';
                state.model.activeCellRef = 'A2';
                state.model.Selection = { ...selection };
                state.model.selection = { ...selection };

                requestAnimationFrame(() => {
                    window.tmSpreadsheetCanvas.applyAutoFill(el, 4, 0, {
                        row: 1,
                        col: 0,
                        startRow: 0,
                        startCol: 0,
                        endRow: 1,
                        endCol: 0
                    });

                    setTimeout(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        const readValue = key => {
                            const cell = el.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells?.get(key);
                            return String(cell?.value ?? cell?.Value ?? '');
                        };
                        resolve({
                            a3: readValue('2:0'),
                            a4: readValue('3:0'),
                            a5: readValue('4:0'),
                            rangeChangedCommands: after.rangeChangedCommandCount - before.rangeChangedCommandCount,
                            commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0),
                            commandLogBatchCallbacks: after.commandLogBatchCallbackCount - before.commandLogBatchCallbackCount,
                            commandLogBatchItems: after.commandLogBatchItemCount - before.commandLogBatchItemCount
                        });
                    }, 320);
                });
            })");

        Assert.AreEqual("3", result.A3, "Expected autofill to continue the numeric series into A3.");
        Assert.AreEqual("4", result.A4, "Expected autofill to continue the numeric series into A4.");
        Assert.AreEqual("5", result.A5, "Expected autofill to continue the numeric series into A5.");
        Assert.AreEqual(1, result.RangeChangedCommands, $"Expected one rangeChanged command for the autofill batch. Count: {result.RangeChangedCommands}.");
        Assert.IsTrue(result.CommandLogCallbacks >= 1, $"Expected the autofill batch to reach .NET through the command log. Count: {result.CommandLogCallbacks}.");
        Assert.IsTrue(result.CommandLogBatchCallbacks >= 1, $"Expected at least one command-log batch callback for the autofill batch. Count: {result.CommandLogBatchCallbacks}.");
        Assert.IsTrue(result.CommandLogBatchItems >= 1, $"Expected the autofill batch payload to contain at least one command. Items: {result.CommandLogBatchItems}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsFormulaReferenceHighlightsRemainVisibleAfterScroll()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsFormulaEditorProbeResult>(
            @"el => new Promise(resolve => {
                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: '=',
                    bubbles: true,
                    cancelable: true
                }));
                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=B20';
                input.setSelectionRange(input.value.length, input.value.length);
                input.dispatchEvent(new Event('input', { bubbles: true }));
                el.scrollTop = 340;
                el.dispatchEvent(new Event('scroll', { bubbles: true }));
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const canvas = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--selection');
                    const dpr = window.devicePixelRatio || 1;
                    const ctx = canvas.getContext('2d');
                    const sampleX = Math.round((40 + 64 + 2) * dpr);
                    const sampleY = Math.round((20 + 19 * 20 - 340 + 10) * dpr);
                    let bluePixels = 0;
                    const data = ctx.getImageData(sampleX, Math.max(0, sampleY - 8), Math.round(8 * dpr), Math.round(16 * dpr)).data;
                    for (let i = 0; i < data.length; i += 4) {
                        if (data[i + 3] > 0 && data[i + 2] > data[i] + 20 && data[i + 2] > data[i + 1]) bluePixels++;
                    }
                    resolve({
                        editorValue: input.value,
                        formulaActive: !!after.sheetState?.formulaEditor?.active,
                        formulaRefCount: after.sheetState?.formulaEditor?.refCount || 0,
                        highlightedCells: after.formulaEditorHighlightCount,
                        bluePixels,
                        logicalScrollTop: after.logicalScrollTop
                    });
                }));
            })");

        Assert.AreEqual("=B20", result.EditorValue);
        Assert.IsTrue(result.FormulaActive, "Expected the JS formula editor to stay active while scrolling.");
        Assert.AreEqual(1, result.FormulaRefCount, "Expected one parsed reference after editing the formula text.");
        Assert.AreEqual(1, result.HighlightedCells, "Expected one highlighted formula reference cell.");
        Assert.IsTrue(result.LogicalScrollTop > 0, $"Expected canvas to scroll while formula editing. ScrollTop: {result.LogicalScrollTop}.");
        Assert.IsTrue(result.BluePixels > 0, $"Expected formula reference highlight pixels after scroll. Count: {result.BluePixels}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsSheetStateRejectsStaleBlazorSelectionFrame()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        for (var i = 0; i < 8; i++)
        {
            await grid.PressAsync("ArrowDown");
        }

        var beforeRef = await GetCanvasActiveRefAsync(grid);
        var result = await grid.EvaluateAsync<CanvasJsFirstStateProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const clone = value => JSON.parse(JSON.stringify(value));
                const stale = clone(state.model);
                stale.activeCellRef = stale.ActiveCellRef = 'A1';
                stale.selection = stale.Selection = { startRow: 0, startCol: 0, endRow: 0, endCol: 0, StartRow: 0, StartCol: 0, EndRow: 0, EndCol: 0 };
                stale.scrollTop = stale.ScrollTop = 0;
                stale.scrollLeft = stale.ScrollLeft = 0;
                stale.interactionVersion = stale.InteractionVersion = 0;
                window.tmSpreadsheetCanvas.render(el, state.canvas, stale);
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const model = state.model;
                    resolve({
                        activeRef: model?.activeCellRef || model?.ActiveCellRef || '',
                        sheetActiveRef: after.sheetState?.activeCell?.ref || '',
                        localRevision: after.localRevision,
                        serverRevision: after.serverRevision,
                        staleFramesIgnored: after.staleFramesIgnored - before.staleFramesIgnored
                    });
                }));
            })");

        Assert.AreEqual(beforeRef, result.ActiveRef, "A stale Blazor frame must not move the canvas model selection back.");
        Assert.AreEqual(beforeRef, result.SheetActiveRef, "A stale Blazor frame must not move the JS sheet state selection back.");
        Assert.IsTrue(result.LocalRevision > result.ServerRevision, $"Expected JS local revision to remain ahead of stale server revision. Local: {result.LocalRevision}, server: {result.ServerRevision}.");
        Assert.IsTrue(result.StaleFramesIgnored > 0, $"Expected stale frame counter to increase. Count: {result.StaleFramesIgnored}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_JsEditorStateRejectsStaleBlazorFrame()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        await grid.PressAsync("a");
        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Keyboard.TypeAsync("bcdefghijklmnopqrst");
        var beforeValue = await editor.InputValueAsync();

        var result = await grid.EvaluateAsync<CanvasJsFirstStateProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const clone = value => JSON.parse(JSON.stringify(value));
                const stale = clone(state.model);
                stale.activeCellRef = stale.ActiveCellRef = 'A1';
                stale.selection = stale.Selection = { startRow: 0, startCol: 0, endRow: 0, endCol: 0, StartRow: 0, StartCol: 0, EndRow: 0, EndCol: 0 };
                stale.interactionVersion = stale.InteractionVersion = 0;
                const cells = stale.cells || stale.Cells || [];
                for (const cell of cells) {
                    if ((cell.active || cell.Active) || (cell.ref || cell.Ref) === (before.sheetState?.activeCell?.ref || 'A1')) {
                        cell.value = cell.Value = 'OLD';
                    }
                }
                window.tmSpreadsheetCanvas.render(el, state.canvas, stale);
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                    resolve({
                        editorValue: input?.value || '',
                        sheetEditorValue: after.sheetState?.editor?.value || '',
                        localRevision: after.localRevision,
                        serverRevision: after.serverRevision,
                        staleFramesIgnored: after.staleFramesIgnored - before.staleFramesIgnored
                    });
                }));
            })");

        Assert.AreEqual(beforeValue, result.EditorValue, "A stale Blazor frame must not overwrite the visible JS editor text.");
        Assert.AreEqual(beforeValue, result.SheetEditorValue, "A stale Blazor frame must not overwrite the JS sheet editor state.");
        Assert.IsTrue(result.LocalRevision > result.ServerRevision, $"Expected editor local revision to remain ahead of stale server revision. Local: {result.LocalRevision}, server: {result.ServerRevision}.");
        Assert.IsTrue(result.StaleFramesIgnored > 0, $"Expected stale frame counter to increase. Count: {result.StaleFramesIgnored}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_CellStoreBatchUpdateRepaintsVisibleCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        var result = await grid.EvaluateAsync<CanvasCellStoreProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const cells = model.cells || model.Cells || [];
                const source = cells.find(c => (c.row ?? c.Row) === 2 && (c.col ?? c.Col) === 2) || cells[0];
                const row = source.row ?? source.Row;
                const col = source.col ?? source.Col;
                const patch = {
                    ...source,
                    row,
                    col,
                    Row: row,
                    Col: col,
                    value: '',
                    Value: '',
                    style: { backgroundColor: '#ff0000', BackgroundColor: '#ff0000' },
                    Style: { backgroundColor: '#ff0000', BackgroundColor: '#ff0000' }
                };
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                window.tmSpreadsheetCanvas.setCells(el, [patch]);
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const stored = state.sheetState.cellStore.cells.get(`${row}:${col}`);
                    const content = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--content');
                    const dpr = window.devicePixelRatio || 1;
                    const ctx = content.getContext('2d');
                    const left = (stored.left ?? stored.Left) - (model.scrollLeft ?? model.ScrollLeft ?? 0) + (model.rowHeaderWidth ?? model.RowHeaderWidth ?? 40);
                    const top = (stored.top ?? stored.Top) - (model.scrollTop ?? model.ScrollTop ?? 0) + (model.columnHeaderHeight ?? model.ColumnHeaderHeight ?? 20);
                    const width = stored.width ?? stored.Width;
                    const height = stored.height ?? stored.Height;
                    const data = ctx.getImageData(
                        Math.round((left + width / 2) * dpr),
                        Math.round((top + height / 2) * dpr),
                        1,
                        1).data;
                    resolve({
                        red: data[0],
                        green: data[1],
                        blue: data[2],
                        setCellCount: after.cellStoreSetCellCount - before.cellStoreSetCellCount,
                        storeSize: after.cellStoreSize,
                        styledOrNonEmptyCount: after.cellStoreStyledOrNonEmptyCount
                    });
                }));
            })");

        Assert.IsTrue(result.Red > 220 && result.Green < 80 && result.Blue < 80, $"Expected patched cell background to be visible on canvas. RGB: {result.Red},{result.Green},{result.Blue}.");
        Assert.AreEqual(1, result.SetCellCount, $"Expected one JS cell-store set operation. Count: {result.SetCellCount}.");
        Assert.IsTrue(result.StoreSize > 0, $"Expected JS cell store to contain visible cells. Size: {result.StoreSize}.");
        Assert.IsTrue(result.StyledOrNonEmptyCount > 0, $"Expected styled/non-empty index to include patched cell. Count: {result.StyledOrNonEmptyCount}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_KeyboardSelectionUsesCellStoreWithoutFrameScans()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasCellStoreProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                for (let i = 0; i < 24; i++) {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: i % 2 === 0 ? 'ArrowDown' : 'ArrowRight',
                        bubbles: true,
                        cancelable: true
                    }));
                }
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const model = el.__tmSpreadsheetCanvas.model;
                    resolve({
                        activeRef: model.activeCellRef || model.ActiveCellRef || '',
                        lookupCount: after.cellStoreLookupCount - before.cellStoreLookupCount,
                        hitCount: after.cellStoreHitCount - before.cellStoreHitCount,
                        frameScanCount: after.cellStoreFrameScanCount - before.cellStoreFrameScanCount,
                        visibleCellCount: after.cellStoreVisibleCellCount,
                        storeSize: after.cellStoreSize
                    });
                }));
            })");

        Assert.IsTrue(ParseRow(result.ActiveRef) > 1, $"Expected keyboard navigation to move through cells. Ref: {result.ActiveRef}.");
        Assert.IsTrue(result.LookupCount > 0, $"Expected keyboard selection to query the JS cell store. Lookups: {result.LookupCount}.");
        Assert.IsTrue(result.HitCount > 0, $"Expected keyboard selection to hit the JS cell store. Hits: {result.HitCount}.");
        Assert.AreEqual(0, result.FrameScanCount, $"Expected keyboard selection to avoid scanning sampled frame cells. Scans: {result.FrameScanCount}.");
        Assert.IsTrue(result.VisibleCellCount > 0, $"Expected renderer to use visible cells from JS store. Count: {result.VisibleCellCount}.");
        Assert.IsTrue(result.StoreSize >= result.VisibleCellCount, $"Expected store size to cover visible cells. Store: {result.StoreSize}, visible: {result.VisibleCellCount}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_WheelScrollUsesJsLayoutWithoutBlazorFrame()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        var result = await grid.EvaluateAsync<CanvasJsLayoutProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.dispatchEvent(new WheelEvent('wheel', {
                    deltaY: 260,
                    bubbles: true,
                    cancelable: true
                }));
                requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        blazorFrames: after.blazorFrameCount - before.blazorFrameCount,
                        layoutComputes: after.visibleLayoutJsComputeCount - before.visibleLayoutJsComputeCount,
                        binarySearches: after.visibleLayoutBinarySearchCount - before.visibleLayoutBinarySearchCount,
                        logicalScrollTop: after.logicalScrollTop,
                        rowSizeCacheSize: after.layoutRowSizeCacheSize,
                        columnSizeCacheSize: after.layoutColumnSizeCacheSize
                    });
                });
            })");

        Assert.AreEqual(0, result.BlazorFrames, $"Wheel visible layout should be computed locally before any Blazor frame. Frames: {result.BlazorFrames}.");
        Assert.IsTrue(result.LayoutComputes > 0, $"Expected wheel scroll to compute visible layout in JS. Count: {result.LayoutComputes}.");
        Assert.IsTrue(result.BinarySearches > 0, $"Expected wheel scroll to use binary search for visible layout. Count: {result.BinarySearches}.");
        Assert.IsTrue(result.LogicalScrollTop > 0, $"Expected wheel scroll to advance logical scroll. Top: {result.LogicalScrollTop}.");
        Assert.IsTrue(result.RowSizeCacheSize > 0, $"Expected JS row size cache to be populated. Size: {result.RowSizeCacheSize}.");
        Assert.IsTrue(result.ColumnSizeCacheSize > 0, $"Expected JS column size cache to be populated. Size: {result.ColumnSizeCacheSize}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_KeyboardScrollUsesJsLayoutWithoutBlazorFrame()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasJsLayoutProbeResult>(
            @"el => new Promise(resolve => {
                requestAnimationFrame(() => {
                    requestAnimationFrame(() => {
                        const state = el.__tmSpreadsheetCanvas;
                        const model = state.model;
                        const layout = state.visibleLayoutCache?.layout;
                        const rows = layout?.rows || [];
                        const cols = layout?.columns || [];
                        const bodyRows = rows.filter(row => row.y >= (model.columnHeaderHeight ?? model.ColumnHeaderHeight ?? 20) && row.y + row.height <= el.clientHeight);
                        const row = bodyRows.length ? bodyRows[bodyRows.length - 1].index : 1;
                        const col = cols.length ? cols[0].index : 0;
                        const ref = String.fromCharCode(65 + col) + String(row + 1);
                        model.activeCellRef = model.ActiveCellRef = ref;
                        model.selection = model.Selection = {
                            startRow: row,
                            StartRow: row,
                            startCol: col,
                            StartCol: col,
                            endRow: row,
                            EndRow: row,
                            endCol: col,
                            EndCol: col
                        };
                        state.sheetState.activeCell = { row, col, ref };
                        state.sheetState.selection = { startRow: row, startCol: col, endRow: row, endCol: col };
                        state.visibleLayoutCache = null;
                        const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        el.dispatchEvent(new KeyboardEvent('keydown', {
                            key: 'ArrowDown',
                            bubbles: true,
                            cancelable: true
                        }));
                        requestAnimationFrame(() => {
                            const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                            const model = el.__tmSpreadsheetCanvas.model;
                            resolve({
                                activeRef: model.activeCellRef || model.ActiveCellRef || '',
                                blazorFrames: after.blazorFrameCount - before.blazorFrameCount,
                                layoutComputes: after.visibleLayoutJsComputeCount - before.visibleLayoutJsComputeCount,
                                binarySearches: after.visibleLayoutBinarySearchCount - before.visibleLayoutBinarySearchCount,
                                logicalScrollTop: after.logicalScrollTop,
                                keyboardLogicalScrolls: after.logicalKeyboardScrollCount - before.logicalKeyboardScrollCount
                            });
                        });
                    });
                });
            })");

        Assert.IsTrue(ParseRow(result.ActiveRef) > 1, $"Expected local keyboard navigation to move at the scroll edge. Ref: {result.ActiveRef}.");
        Assert.AreEqual(0, result.BlazorFrames, $"Keyboard visible layout should be computed locally before any Blazor frame. Frames: {result.BlazorFrames}.");
        Assert.IsTrue(result.LayoutComputes > 0, $"Expected keyboard scroll to compute visible layout in JS. Count: {result.LayoutComputes}.");
        Assert.IsTrue(result.BinarySearches > 0, $"Expected keyboard scroll to use binary search for visible layout. Count: {result.BinarySearches}.");
        Assert.IsTrue(result.LogicalScrollTop > 0, $"Expected keyboard scroll to advance logical scroll. Top: {result.LogicalScrollTop}.");
        Assert.IsTrue(result.KeyboardLogicalScrolls > 0, $"Expected keyboard path to use logical scroll. Count: {result.KeyboardLogicalScrolls}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_ArrowDownInViewportPaintsOnlySelectionLayer()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasRendererPipelineProbeResult>(
            @"el => new Promise(resolve => {
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        bubbles: true,
                        cancelable: true
                    }));
                    requestAnimationFrame(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        resolve({
                            paintFrames: after.paintFrameCount - before.paintFrameCount,
                            contentPaintFrames: after.contentPaintFrameCount - before.contentPaintFrameCount,
                            selectionPaintFrames: after.selectionPaintFrameCount - before.selectionPaintFrameCount,
                            contentLayerPaints: after.contentLayerPaintCount - before.contentLayerPaintCount,
                            headerLayerPaints: after.headerLayerPaintCount - before.headerLayerPaintCount,
                            selectionLayerPaints: after.selectionLayerPaintCount - before.selectionLayerPaintCount,
                            selectionDirtyRects: after.selectionDirtyRectCount - before.selectionDirtyRectCount,
                            contentDirtyRects: after.contentDirtyRectCount - before.contentDirtyRectCount
                        });
                    });
                }));
            })");

        Assert.AreEqual(1, result.PaintFrames, $"Expected one local paint frame. Frames: {result.PaintFrames}.");
        Assert.AreEqual(0, result.ContentPaintFrames, $"ArrowDown inside viewport should not paint content. Frames: {result.ContentPaintFrames}.");
        Assert.AreEqual(0, result.ContentLayerPaints, $"ArrowDown inside viewport should not touch the content layer. Paints: {result.ContentLayerPaints}.");
        Assert.AreEqual(0, result.HeaderLayerPaints, $"ArrowDown inside viewport should not touch the header layer. Paints: {result.HeaderLayerPaints}.");
        Assert.IsTrue(result.SelectionPaintFrames > 0, $"Expected selection paint frame. Frames: {result.SelectionPaintFrames}.");
        Assert.IsTrue(result.SelectionLayerPaints > 0, $"Expected selection layer paint. Paints: {result.SelectionLayerPaints}.");
        Assert.IsTrue(result.SelectionDirtyRects > 0, $"Expected selection dirty rects. Count: {result.SelectionDirtyRects}.");
        Assert.AreEqual(0, result.ContentDirtyRects, $"Selection movement should not create content dirty rects. Count: {result.ContentDirtyRects}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_ArrowDownAtScrollEdgePaintsContentAtMostOnce()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasRendererPipelineProbeResult>(
            @"el => new Promise(resolve => {
                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const state = el.__tmSpreadsheetCanvas;
                    const model = state.model;
                    const layout = state.visibleLayoutCache?.layout;
                    const rows = layout?.rows || [];
                    const cols = layout?.columns || [];
                    const bodyRows = rows.filter(row => row.y >= (model.columnHeaderHeight ?? model.ColumnHeaderHeight ?? 20) && row.y + row.height <= el.clientHeight);
                    const row = bodyRows.length ? bodyRows[bodyRows.length - 1].index : 1;
                    const col = cols.length ? cols[0].index : 0;
                    const ref = String.fromCharCode(65 + col) + String(row + 1);
                    model.activeCellRef = model.ActiveCellRef = ref;
                    model.selection = model.Selection = {
                        startRow: row,
                        StartRow: row,
                        startCol: col,
                        StartCol: col,
                        endRow: row,
                        EndRow: row,
                        endCol: col,
                        EndCol: col
                    };
                    state.sheetState.activeCell = { row, col, ref };
                    state.sheetState.selection = { startRow: row, startCol: col, endRow: row, endCol: col };
                    const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        bubbles: true,
                        cancelable: true
                    }));
                    requestAnimationFrame(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        resolve({
                            paintFrames: after.paintFrameCount - before.paintFrameCount,
                            contentPaintFrames: after.contentPaintFrameCount - before.contentPaintFrameCount,
                            selectionPaintFrames: after.selectionPaintFrameCount - before.selectionPaintFrameCount,
                            contentLayerPaints: after.contentLayerPaintCount - before.contentLayerPaintCount,
                            selectionLayerPaints: after.selectionLayerPaintCount - before.selectionLayerPaintCount,
                            logicalScrollTop: after.logicalScrollTop
                        });
                    });
                }));
            })");

        Assert.IsTrue(result.PaintFrames <= 1, $"Expected edge ArrowDown to coalesce into at most one scheduler frame. Frames: {result.PaintFrames}.");
        Assert.IsTrue(result.ContentPaintFrames <= 1, $"Expected edge ArrowDown to paint content at most once. Frames: {result.ContentPaintFrames}.");
        Assert.IsTrue(result.ContentLayerPaints <= 1, $"Expected content layer to paint at most once. Paints: {result.ContentLayerPaints}.");
        Assert.IsTrue(result.SelectionLayerPaints <= 1, $"Expected selection layer to paint at most once. Paints: {result.SelectionLayerPaints}.");
        Assert.IsTrue(result.LogicalScrollTop > 0, $"Expected edge ArrowDown to move logical scroll. Top: {result.LogicalScrollTop}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_RapidArrowDownKeepsSelectionMonotonicWhileScrolling()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var previousRow = 1;
        for (var i = 0; i < 80; i++)
        {
            await grid.PressAsync("ArrowDown");
            var activeRef = await grid.EvaluateAsync<string>(
                "el => el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || ''");
            var row = ParseRow(activeRef);
            Assert.IsTrue(row >= previousRow, $"Expected active row to stay monotonic. Previous: {previousRow}, current: {row}, ref: {activeRef}.");
            previousRow = row;
        }

        var logicalScrollTop = await grid.EvaluateAsync<double>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).logicalScrollTop");
        Assert.IsTrue(logicalScrollTop > 0, $"Expected ArrowDown navigation to advance the logical canvas scroll. logicalScrollTop: {logicalScrollTop}.");
        Assert.IsTrue(previousRow >= 70, $"Expected rapid ArrowDown navigation to reach a later row. Last row: {previousRow}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_RapidArrowRightKeepsSelectionMonotonicWhileScrolling()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var previousColumn = 1;
        for (var i = 0; i < 40; i++)
        {
            await grid.PressAsync("ArrowRight");
            var activeRef = await grid.EvaluateAsync<string>(
                "el => el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || ''");
            var column = ParseColumn(activeRef);
            Assert.IsTrue(column >= previousColumn, $"Expected active column to stay monotonic. Previous: {previousColumn}, current: {column}, ref: {activeRef}.");
            previousColumn = column;
        }

        var scrollLeft = await grid.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(scrollLeft > 0, $"Expected ArrowRight navigation to scroll canvas grid. scrollLeft: {scrollLeft}.");
        Assert.IsTrue(previousColumn >= 35, $"Expected rapid ArrowRight navigation to reach a later column. Last column: {previousColumn}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_RapidArrowUpKeepsSelectionMonotonicWhileScrolling()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        for (var i = 0; i < 80; i++)
        {
            await grid.PressAsync("ArrowDown");
        }

        var startRow = ParseRow(await GetCanvasActiveRefAsync(grid));
        var previousRow = startRow;
        for (var i = 0; i < 45; i++)
        {
            await grid.PressAsync("ArrowUp");
            var activeRef = await GetCanvasActiveRefAsync(grid);
            var row = ParseRow(activeRef);
            Assert.IsTrue(row <= previousRow, $"Expected active row to decrease monotonically. Previous: {previousRow}, current: {row}, ref: {activeRef}.");
            previousRow = row;
        }

        Assert.IsTrue(previousRow < startRow, $"Expected rapid ArrowUp navigation to reach an earlier row. Start row: {startRow}, last row: {previousRow}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_RapidArrowLeftKeepsSelectionMonotonicWhileScrolling()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        for (var i = 0; i < 40; i++)
        {
            await grid.PressAsync("ArrowRight");
        }

        var startColumn = ParseColumn(await GetCanvasActiveRefAsync(grid));
        var previousColumn = startColumn;
        for (var i = 0; i < 28; i++)
        {
            await grid.PressAsync("ArrowLeft");
            var activeRef = await GetCanvasActiveRefAsync(grid);
            var column = ParseColumn(activeRef);
            Assert.IsTrue(column <= previousColumn, $"Expected active column to decrease monotonically. Previous: {previousColumn}, current: {column}, ref: {activeRef}.");
            previousColumn = column;
        }

        Assert.IsTrue(previousColumn < startColumn, $"Expected rapid ArrowLeft navigation to reach an earlier column. Start column: {startColumn}, last column: {previousColumn}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_KeyboardNavigationUsesSelectionOverlay()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var canvasCount = await grid.Locator("canvas").CountAsync();
        Assert.IsTrue(canvasCount >= 3, $"Expected canvas renderer to use content, header, and selection canvases. Count: {canvasCount}.");

        await grid.ClickAsync();
        var before = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).selectionRedrawCount");

        await grid.PressAsync("ArrowDown");
        await grid.PressAsync("ArrowRight");

        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).selectionRedrawCount > {before}",
            await grid.ElementHandleAsync());
    }

    [TestMethod]
    public async Task CanvasRenderer_RapidArrowDownStaysOnLocalHotPath()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasArrowHotPathProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                for (let i = 0; i < 80; i++) {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        bubbles: true,
                        cancelable: true
                    }));
                }

                requestAnimationFrame(() => {
                    const first = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const model = el.__tmSpreadsheetCanvas?.model;
                    resolve({
                        activeRef: model?.activeCellRef || model?.ActiveCellRef || '',
                        keyboardInteractions: after.keyboardInteractions - before.keyboardInteractions,
                        selectionCallbacks: after.selectionCallbackCount - before.selectionCallbackCount,
                        keyCommandCallbacks: after.keyCommandCallbackCount - before.keyCommandCallbackCount,
                        paintRequests: after.paintRequestCount - before.paintRequestCount,
                        paintFrames: after.paintFrameCount - before.paintFrameCount,
                        firstFramePaints: first.paintFrameCount - before.paintFrameCount,
                        firstFrameSelectionPaints: first.selectionPaintFrameCount - before.selectionPaintFrameCount,
                        firstFrameContentPaints: first.contentPaintFrameCount - before.contentPaintFrameCount,
                        selectionPaintFrames: after.selectionPaintFrameCount - before.selectionPaintFrameCount,
                        contentPaintFrames: after.contentPaintFrameCount - before.contentPaintFrameCount,
                        mergedPaintRequests: after.mergedPaintRequestCount - before.mergedPaintRequestCount,
                        discardedIntermediatePaints: after.discardedIntermediatePaintCount - before.discardedIntermediatePaintCount,
                        maxMergedPaintRequestsPerFrame: after.maxMergedPaintRequestsPerFrame,
                        keyboardScrollToCount: after.keyboardScrollToCount - before.keyboardScrollToCount,
                        scrollToCount: after.scrollToCount - before.scrollToCount,
                        logicalKeyboardScrollCount: after.logicalKeyboardScrollCount - before.logicalKeyboardScrollCount,
                        logicalScrollTop: after.logicalScrollTop,
                        scrollTop: el.scrollTop
                    });
                    });
                });
            })");

        Assert.IsTrue(ParseRow(result.ActiveRef) >= 70, $"Expected rapid local ArrowDown navigation to advance far down the sheet. Ref: {result.ActiveRef}.");
        Assert.IsTrue(result.KeyboardInteractions >= 80, $"Expected every ArrowDown to stay on the canvas keyboard path. Count: {result.KeyboardInteractions}.");
        Assert.IsTrue(result.SelectionCallbacks <= 2, $"Expected selection sync to coalesce into at most two .NET callbacks. Count: {result.SelectionCallbacks}.");
        Assert.AreEqual(0, result.KeyCommandCallbacks, $"Arrow navigation should not use the .NET key command callback. Count: {result.KeyCommandCallbacks}.");
        Assert.IsTrue(result.PaintRequests >= result.KeyboardInteractions, $"Expected keyboard navigation to request paint locally. Paint requests: {result.PaintRequests}, keyboard interactions: {result.KeyboardInteractions}.");
        Assert.IsTrue(result.PaintFrames <= 2, $"Expected rapid keyboard navigation to coalesce paints into at most two animation frames. Frames: {result.PaintFrames}.");
        Assert.IsTrue(result.FirstFramePaints <= 1, $"Expected one scheduler paint at most in the first animation frame. Frames: {result.FirstFramePaints}.");
        Assert.IsTrue(result.FirstFrameSelectionPaints <= 1, $"Expected selection overlay to draw at most once in the first coalesced keyboard frame. Frames: {result.FirstFrameSelectionPaints}.");
        Assert.IsTrue(result.FirstFrameContentPaints <= 1, $"Expected content canvas to draw at most once in the first coalesced keyboard frame. Frames: {result.FirstFrameContentPaints}.");
        Assert.IsTrue(result.SelectionPaintFrames <= result.PaintFrames, $"Expected selection paints to be bounded by scheduler frames. Selection: {result.SelectionPaintFrames}, frames: {result.PaintFrames}.");
        Assert.IsTrue(result.ContentPaintFrames <= result.PaintFrames, $"Expected content paints to be bounded by scheduler frames. Content: {result.ContentPaintFrames}, frames: {result.PaintFrames}.");
        Assert.IsTrue(result.MergedPaintRequests > 0, $"Expected paint scheduler to merge repeated keyboard paint requests. Count: {result.MergedPaintRequests}.");
        Assert.IsTrue(result.DiscardedIntermediatePaints > 0, $"Expected paint scheduler to discard intermediate keyboard states. Count: {result.DiscardedIntermediatePaints}.");
        Assert.IsTrue(result.MaxMergedPaintRequestsPerFrame > 1, $"Expected more than one paint request in a coalesced frame. Max: {result.MaxMergedPaintRequestsPerFrame}.");
        Assert.AreEqual(0, result.KeyboardScrollToCount, $"Keyboard navigation should move logical scroll without calling root.scrollTo per key. Count: {result.KeyboardScrollToCount}.");
        Assert.IsTrue(result.ScrollToCount <= 1, $"Expected delayed native scroll sync to stay coalesced, not grow with key repeat. Count: {result.ScrollToCount}.");
        Assert.IsTrue(result.LogicalKeyboardScrollCount > 0, $"Expected keyboard navigation to advance logical scroll. Count: {result.LogicalKeyboardScrollCount}.");
        Assert.IsTrue(result.LogicalScrollTop > 0, $"Expected local ArrowDown navigation to move logical scroll. logicalScrollTop: {result.LogicalScrollTop}, native scrollTop: {result.ScrollTop}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_CommandLogBatchesArrowNavigationWithoutLegacyCallbacks()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasCommandLogProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                for (let i = 0; i < 80; i++) {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        bubbles: true,
                        cancelable: true
                    }));
                }

                setTimeout(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        commandLogBatchCallbacks: after.commandLogBatchCallbackCount - before.commandLogBatchCallbackCount,
                        commandLogBatchItems: after.commandLogBatchItemCount - before.commandLogBatchItemCount,
                        selectionSettledCommands: after.selectionSettledCommandCount - before.selectionSettledCommandCount,
                        viewportSettledCommands: after.viewportSettledCommandCount - before.viewportSettledCommandCount,
                        legacySelectionCallbacks: (after.dotNetCallbacksByMethod.OnCanvasSelectionChanged || 0) - (before.dotNetCallbacksByMethod.OnCanvasSelectionChanged || 0),
                        legacyViewportCallbacks: (after.dotNetCallbacksByMethod.OnCanvasViewportChanged || 0) - (before.dotNetCallbacksByMethod.OnCanvasViewportChanged || 0),
                        commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0),
                        ackRevision: after.commandLogAckRevision
                    });
                }, 240);
            })");

        Assert.AreEqual(0, result.LegacySelectionCallbacks, $"Expected ArrowDown hot path to stop using legacy selection callbacks. Count: {result.LegacySelectionCallbacks}.");
        Assert.AreEqual(0, result.LegacyViewportCallbacks, $"Expected ArrowDown hot path to stop using legacy viewport callbacks. Count: {result.LegacyViewportCallbacks}.");
        Assert.IsTrue(result.CommandLogCallbacks > 0, "Expected ArrowDown hot path to use the command log batch callback.");
        Assert.IsTrue(result.CommandLogBatchCallbacks <= 2, $"Expected command log batching to coalesce ArrowDown callbacks. Count: {result.CommandLogBatchCallbacks}.");
        Assert.IsTrue(result.CommandLogBatchItems > 0, $"Expected the command log batch to contain settled events. Items: {result.CommandLogBatchItems}.");
        Assert.IsTrue(result.SelectionSettledCommands > 0, $"Expected selectionSettled commands for keyboard navigation. Count: {result.SelectionSettledCommands}.");
        Assert.IsTrue(result.ViewportSettledCommands > 0, $"Expected viewportSettled commands after scrolling. Count: {result.ViewportSettledCommands}.");
        Assert.IsTrue(result.AckRevision > 0, $"Expected the command log ack revision to advance. Ack: {result.AckRevision}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_RapidLocalEditsShareOneCommandLogBatch()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasCommandLogProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const runEdit = key => {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key,
                        bubbles: true,
                        cancelable: true
                    }));
                    const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                    input.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'Enter',
                        bubbles: true,
                        cancelable: true
                    }));
                };

                runEdit('a');
                runEdit('b');

                setTimeout(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        commandLogBatchCallbacks: after.commandLogBatchCallbackCount - before.commandLogBatchCallbackCount,
                        commandLogBatchItems: after.commandLogBatchItemCount - before.commandLogBatchItemCount,
                        cellChangedCommands: after.cellChangedCommandCount - before.cellChangedCommandCount,
                        formulaCommittedCommands: after.formulaCommittedCommandCount - before.formulaCommittedCommandCount,
                        commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0),
                        legacyEditCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCellEditCommittedBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCellEditCommittedBatch || 0),
                        ackRevision: after.commandLogAckRevision
                    });
                }, 240);
            })");

        Assert.AreEqual(0, result.LegacyEditCallbacks, $"Expected local edits to stop using the legacy edit callback. Count: {result.LegacyEditCallbacks}.");
        Assert.AreEqual(1, result.CommandLogCallbacks, $"Expected two quick local edits to share one command-log callback. Count: {result.CommandLogCallbacks}.");
        Assert.AreEqual(1, result.CommandLogBatchCallbacks, $"Expected one command-log batch callback. Count: {result.CommandLogBatchCallbacks}.");
        Assert.AreEqual(2, result.CellChangedCommands, $"Expected two cellChanged commands for two local commits. Count: {result.CellChangedCommands}.");
        Assert.AreEqual(0, result.FormulaCommittedCommands, $"Expected plain text edits not to be tagged as formula commits. Count: {result.FormulaCommittedCommands}.");
        Assert.IsTrue(result.CommandLogBatchItems >= 2, $"Expected one batch payload carrying both local edits. Items: {result.CommandLogBatchItems}.");
        Assert.IsTrue(result.AckRevision > 0, $"Expected the command log ack revision to advance. Ack: {result.AckRevision}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_RangeChangeBatchUsesSingleCommandLogCallback()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var result = await grid.EvaluateAsync<CanvasCommandLogProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                window.tmSpreadsheetCanvas.setCells(el, {
                    queueRangeCommand: true,
                    cells: [
                        { row: 0, col: 0, value: 'left', Value: 'left' },
                        { row: 0, col: 1, value: 'right', Value: 'right' }
                    ]
                });

                setTimeout(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        commandLogBatchCallbacks: after.commandLogBatchCallbackCount - before.commandLogBatchCallbackCount,
                        commandLogBatchItems: after.commandLogBatchItemCount - before.commandLogBatchItemCount,
                        rangeChangedCommands: after.rangeChangedCommandCount - before.rangeChangedCommandCount,
                        commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0),
                        legacyEditCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCellEditCommittedBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCellEditCommittedBatch || 0),
                        ackRevision: after.commandLogAckRevision
                    });
                }, 180);
            })");

        Assert.AreEqual(0, result.LegacyEditCallbacks, $"Expected range change batching to avoid the legacy edit callback. Count: {result.LegacyEditCallbacks}.");
        Assert.AreEqual(1, result.CommandLogCallbacks, $"Expected one command-log callback for a two-cell range change. Count: {result.CommandLogCallbacks}.");
        Assert.AreEqual(1, result.CommandLogBatchCallbacks, $"Expected one command-log batch callback. Count: {result.CommandLogBatchCallbacks}.");
        Assert.AreEqual(1, result.RangeChangedCommands, $"Expected one rangeChanged command for the two-cell batch. Count: {result.RangeChangedCommands}.");
        Assert.AreEqual(1, result.CommandLogBatchItems, $"Expected one command payload in the batch. Items: {result.CommandLogBatchItems}.");
        Assert.IsTrue(result.AckRevision > 0, $"Expected the command log ack revision to advance. Ack: {result.AckRevision}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_KeyboardScrollSyncsNativeScrollbarAfterDebounce()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        await grid.EvaluateAsync(
            @"el => {
                for (let i = 0; i < 80; i++) {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        bubbles: true,
                        cancelable: true
                    }));
                }
            }");

        await page.WaitForFunctionAsync(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                return metrics.logicalScrollTop > 0
                    && Math.abs(metrics.nativeScrollTop - metrics.logicalScrollTop) <= 1;
            }",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var result = await grid.EvaluateAsync<CanvasScrollbarSyncProbeResult>(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                return {
                    nativeScrollTop: metrics.nativeScrollTop,
                    logicalScrollTop: metrics.logicalScrollTop,
                    keyboardScrollToCount: metrics.keyboardScrollToCount,
                    scrollToCount: metrics.scrollToCount,
                    ownNativeScrollEventCount: metrics.ownNativeScrollEventCount
                };
            }");

        Assert.AreEqual(0, result.KeyboardScrollToCount, $"Keyboard scroll should not call root.scrollTo directly. Count: {result.KeyboardScrollToCount}.");
        Assert.IsTrue(result.ScrollToCount <= 1, $"Expected native scrollbar sync to be debounced into at most one scrollTo. Count: {result.ScrollToCount}.");
        Assert.IsTrue(Math.Abs(result.NativeScrollTop - result.LogicalScrollTop) <= 1, $"Expected native scrollbar to match logical scroll. Native: {result.NativeScrollTop}, logical: {result.LogicalScrollTop}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_KeyboardRepeatAcceleratesButClampsToSheetEnd()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasKeyboardRepeatProbeResult>(
            @"el => new Promise(resolve => {
                window.tmSpreadsheetCanvas.keyboardRepeatAccelerationEnabled = true;
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rowCount = model.rowCount ?? model.RowCount;
                const startRow = rowCount - 6;
                model.activeCellRef = model.ActiveCellRef = 'A' + (startRow + 1);
                const selection = model.selection || model.Selection || {};
                selection.startRow = selection.StartRow = startRow;
                selection.startCol = selection.StartCol = 0;
                selection.endRow = selection.EndRow = startRow;
                selection.endCol = selection.EndCol = 0;
                model.selection = model.Selection = selection;

                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                el.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
                for (let i = 0; i < 80; i++) {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        repeat: true,
                        bubbles: true,
                        cancelable: true
                    }));
                }

                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        activeRef: model.activeCellRef || model.ActiveCellRef || '',
                        rowCount,
                        repeatEvents: after.keyboardRepeatEventCount - before.keyboardRepeatEventCount,
                        acceleratedEvents: after.keyboardRepeatAcceleratedEventCount - before.keyboardRepeatAcceleratedEventCount,
                        maxStep: after.keyboardRepeatMaxStep,
                        lastStep: after.keyboardRepeatLastStep,
                        sequenceCount: after.keyboardRepeatSequenceCount - before.keyboardRepeatSequenceCount
                    });
                }));
            })");

        Assert.AreEqual(result.RowCount, ParseRow(result.ActiveRef), $"Expected accelerated repeat to clamp at the last row. Ref: {result.ActiveRef}, row count: {result.RowCount}.");
        Assert.IsTrue(result.RepeatEvents >= 80, $"Expected repeat events to be counted. Count: {result.RepeatEvents}.");
        Assert.IsTrue(result.AcceleratedEvents > 0, $"Expected long ArrowDown repeat to accelerate. Count: {result.AcceleratedEvents}.");
        Assert.IsTrue(result.MaxStep > 1, $"Expected repeat acceleration to increase the navigation step. Max step: {result.MaxStep}.");
        Assert.AreEqual(1, result.SequenceCount, $"Expected one continuous repeat sequence. Count: {result.SequenceCount}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_ShortArrowPressesStaySingleStep()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var result = await grid.EvaluateAsync<CanvasKeyboardRepeatProbeResult>(
            @"el => new Promise(resolve => {
                window.tmSpreadsheetCanvas.keyboardRepeatAccelerationEnabled = true;
                const model = el.__tmSpreadsheetCanvas.model;
                const beforeRef = model.activeCellRef || model.ActiveCellRef || '';
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);

                for (let i = 0; i < 4; i++) {
                    el.dispatchEvent(new KeyboardEvent('keydown', {
                        key: 'ArrowDown',
                        bubbles: true,
                        cancelable: true
                    }));
                }

                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        activeRef: model.activeCellRef || model.ActiveCellRef || '',
                        startRef: beforeRef,
                        repeatEvents: after.keyboardRepeatEventCount - before.keyboardRepeatEventCount,
                        acceleratedEvents: after.keyboardRepeatAcceleratedEventCount - before.keyboardRepeatAcceleratedEventCount,
                        maxStep: after.keyboardRepeatMaxStep,
                        lastStep: after.keyboardRepeatLastStep
                    });
                }));
            })");

        var startRow = ParseRow(result.StartRef);
        var endRow = ParseRow(result.ActiveRef);
        Assert.AreEqual(startRow + 4, endRow, $"Expected four short ArrowDown presses to move exactly four rows. Start: {result.StartRef}, end: {result.ActiveRef}.");
        Assert.AreEqual(0, result.RepeatEvents, $"Expected non-repeat key presses not to count as repeat events. Count: {result.RepeatEvents}.");
        Assert.AreEqual(0, result.AcceleratedEvents, $"Expected non-repeat key presses not to accelerate. Count: {result.AcceleratedEvents}.");
        Assert.AreEqual(1, result.LastStep, $"Expected last keyboard step to remain one row. Step: {result.LastStep}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_LayersTrackDevicePixelRatioAndResize()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 960, Height = 540 },
            DeviceScaleFactor = 2,
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync($"{BaseUrl}/spreadsheet");
            await WaitForAppReadyAsync(page);

            var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
            await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await page.WaitForFunctionAsync(
                @"el => [...el.querySelectorAll('canvas')].length >= 3
                    && [...el.querySelectorAll('canvas')].every(canvas =>
                        canvas.width === Math.round(el.clientWidth * window.devicePixelRatio)
                        && canvas.height === Math.round(el.clientHeight * window.devicePixelRatio))",
                await grid.ElementHandleAsync());

            await page.SetViewportSizeAsync(720, 420);
            await page.WaitForFunctionAsync(
                @"el => [...el.querySelectorAll('canvas')].length >= 3
                    && [...el.querySelectorAll('canvas')].every(canvas =>
                        canvas.width === Math.round(el.clientWidth * window.devicePixelRatio)
                        && canvas.height === Math.round(el.clientHeight * window.devicePixelRatio))",
                await grid.ElementHandleAsync());
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task CanvasRenderer_UnchangedLocalEditDoesNotRedrawContent()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.DblClickAsync(new LocatorDblClickOptions
        {
            Force = true,
            Position = new() { X = 120, Y = 56 }
        });

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var redrawsBeforeCommit = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).redrawCount");
        await editor.EvaluateAsync("el => el.blur()");
        await page.WaitForTimeoutAsync(250);
        var redrawsAfterCommit = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).redrawCount");

        Assert.AreEqual(redrawsBeforeCommit, redrawsAfterCommit, "Expected an unchanged local edit to close without a content redraw.");
    }

    [TestMethod]
    public async Task CanvasRenderer_EnterAfterLocalEditMovesActiveCell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var startRef = await GetCanvasActiveRefAsync(grid);
        await grid.PressAsync("x");

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Keyboard.TypeAsync("yz");
        await editor.PressAsync("Enter");

        await page.WaitForFunctionAsync(
            @"el => !el.querySelector('.tm-spreadsheet-canvas-grid__editor')
                && (el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '') !== ''",
            await grid.ElementHandleAsync());

        var endRef = await GetCanvasActiveRefAsync(grid);
        Assert.AreEqual(ParseRow(startRef) + 1, ParseRow(endRef), $"Expected Enter after local edit to move one row down. Start: {startRef}, end: {endRef}.");
        Assert.AreEqual(ParseColumn(startRef), ParseColumn(endRef), $"Expected Enter after local edit to keep the same column. Start: {startRef}, end: {endRef}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_MouseClickSyncsBlazorActiveCellImmediately()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();
        var beforeClick = await grid.EvaluateAsync<CanvasClickSyncProbeResult>(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const host = el.closest('.tm-spreadsheet');
                const ref = host?.querySelector('.tm-spreadsheet-formula-bar__ref');
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    commandLogCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCommandLogBatch || 0,
                    cellPointerCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCellPointer || 0,
                    formulaBarRef: (ref?.textContent || '').trim()
                };
            }");

        var targetPoint = await GetCanvasCellCenterAsync(grid, "F5");
        Assert.IsTrue(targetPoint.X >= 0 && targetPoint.Y >= 0, $"Expected a visible canvas point for F5. Point: {targetPoint.X},{targetPoint.Y}.");

        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new Position { X = targetPoint.X, Y = targetPoint.Y }
        });

        await page.WaitForTimeoutAsync(350);

        var afterClick = await grid.EvaluateAsync<CanvasClickSyncProbeResult>(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const host = el.closest('.tm-spreadsheet');
                const ref = host?.querySelector('.tm-spreadsheet-formula-bar__ref');
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    formulaBarRef: (ref?.textContent || '').trim(),
                    commandLogCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCommandLogBatch || 0,
                    cellPointerCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCellPointer || 0
                };
            }");
        Assert.AreEqual("F5", afterClick.ActiveRef, $"Expected canvas active ref to stay aligned with the formula bar after click. Ref: {afterClick.ActiveRef}.");
        Assert.AreEqual("F5", afterClick.FormulaBarRef, $"Expected Blazor formula bar ref to update immediately after canvas click. Before click activeRef={beforeClick.ActiveRef}, formulaBarRef={beforeClick.FormulaBarRef}, commandLogCallbacks={beforeClick.CommandLogCallbacks}, cellPointerCallbacks={beforeClick.CellPointerCallbacks}. After click activeRef={afterClick.ActiveRef}, formulaBarRef={afterClick.FormulaBarRef}, commandLogCallbacks={afterClick.CommandLogCallbacks}, cellPointerCallbacks={afterClick.CellPointerCallbacks}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_ScrollDuringLocalEditKeepsEditorAlignedAndHidesWhenOutOfView()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();
        for (var i = 0; i < 10; i++)
        {
            await grid.PressAsync("ArrowDown");
        }
        await grid.PressAsync("F2");

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var beforeTop = await editor.EvaluateAsync<double>("el => el.getBoundingClientRect().top");

        await grid.EvaluateAsync(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                state.logicalScrollTop += 40;
                state.model.scrollTop = state.model.ScrollTop = state.logicalScrollTop;
                window.tmSpreadsheetCanvas.render(el, state.canvas, state.model);
                requestAnimationFrame(() => requestAnimationFrame(resolve));
            })");
        var movedTopThreshold = (beforeTop - 20).ToString(CultureInfo.InvariantCulture);
        await page.WaitForFunctionAsync(
            $"el => el.getBoundingClientRect().top < {movedTopThreshold}",
            await editor.ElementHandleAsync());

        var afterTop = await editor.EvaluateAsync<double>("el => el.getBoundingClientRect().top");
        Assert.IsTrue(afterTop < beforeTop, $"Expected editor to move upward with the edited cell during scroll. Before: {beforeTop}, after: {afterTop}.");

        await grid.EvaluateAsync(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                state.logicalScrollTop += 400;
                state.model.scrollTop = state.model.ScrollTop = state.logicalScrollTop;
                window.tmSpreadsheetCanvas.render(el, state.canvas, state.model);
                requestAnimationFrame(() => requestAnimationFrame(resolve));
            })");
        await page.WaitForFunctionAsync(
            @"el => {
                const editor = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                if (!editor) return true;
                if (getComputedStyle(editor).visibility === 'hidden') return true;
                const gridRect = el.getBoundingClientRect();
                const editorRect = editor.getBoundingClientRect();
                return editorRect.bottom < gridRect.top || editorRect.top > gridRect.bottom;
            }",
            await grid.ElementHandleAsync());
    }

    [TestMethod]
    public async Task CanvasRenderer_SmallScrollUsesBitmapShiftAndLargeScrollFallsBack()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        var shiftsBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).bitmapShiftCount");
        var userScrollEventsBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).userNativeScrollEventCount");
        await grid.EvaluateAsync("el => { el.scrollTop += 40; }");
        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).bitmapShiftCount > {shiftsBefore}",
            await grid.ElementHandleAsync());
        var userScrollEventsAfter = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).userNativeScrollEventCount");
        Assert.IsTrue(userScrollEventsAfter > userScrollEventsBefore, $"Expected direct native scroll to count as a user scroll event. Before: {userScrollEventsBefore}, after: {userScrollEventsAfter}.");

        var lastDy = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).lastBitmapShiftDy");
        Assert.AreNotEqual(0, lastDy, "Expected bitmap shift to record a vertical delta.");

        var exposedStripHasInk = await grid.EvaluateAsync<bool>(
            @"el => {
                const canvas = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--content');
                const ctx = canvas.getContext('2d');
                const dpr = window.devicePixelRatio || 1;
                const rowHeader = 40 * dpr;
                const stripHeight = Math.min(48 * dpr, canvas.height - 20 * dpr);
                const y = Math.max(20 * dpr, canvas.height - stripHeight);
                const data = ctx.getImageData(rowHeader, y, Math.min(180 * dpr, canvas.width - rowHeader), stripHeight).data;
                for (let i = 0; i < data.length; i += 4) {
                    if (data[i + 3] !== 0 && (data[i] < 245 || data[i + 1] < 245 || data[i + 2] < 245)) return true;
                }
                return false;
            }");
        Assert.IsTrue(exposedStripHasInk, "Expected the newly exposed bitmap-shift strip to be redrawn with grid/content pixels.");

        shiftsBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).bitmapShiftCount");
        await grid.EvaluateAsync("el => { el.scrollLeft += 40; }");
        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).bitmapShiftCount > {shiftsBefore}",
            await grid.ElementHandleAsync());

        var lastDx = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).lastBitmapShiftDx");
        Assert.AreNotEqual(0, lastDx, "Expected bitmap shift to record a horizontal delta.");

        var fallbacksBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).bitmapShiftFallbackCount");
        await grid.EvaluateAsync("el => { el.scrollTop += el.clientHeight; }");
        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).bitmapShiftFallbackCount > {fallbacksBefore}",
            await grid.ElementHandleAsync());
    }

    [TestMethod]
    public async Task CanvasRenderer_DragSelectionUpdatesRangeLocally()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);

        var startX = (int)Math.Round(box.X + 92);
        var startY = (int)Math.Round(box.Y + 36);
        var endX = (int)Math.Round(box.X + 230);
        var endY = (int)Math.Round(box.Y + 86);

        await grid.DispatchEventAsync("pointerdown", new
        {
            clientX = startX,
            clientY = startY,
            button = 0,
            buttons = 1,
            pointerId = 7,
            pointerType = "mouse"
        });

        for (var i = 1; i <= 8; i++)
        {
            await grid.DispatchEventAsync("pointermove", new
            {
                clientX = startX + (endX - startX) * i / 8,
                clientY = startY + (endY - startY) * i / 8,
                button = 0,
                buttons = 1,
                pointerId = 7,
                pointerType = "mouse"
            });
        }

        await grid.DispatchEventAsync("pointerup", new
        {
            clientX = endX,
            clientY = endY,
            button = 0,
            buttons = 0,
            pointerId = 7,
            pointerType = "mouse"
        });

        await page.WaitForFunctionAsync(
            @"el => {
                const selection = el.__tmSpreadsheetCanvas?.model?.selection || el.__tmSpreadsheetCanvas?.model?.Selection;
                if (!selection) return false;
                const startRow = selection.startRow ?? selection.StartRow;
                const startCol = selection.startCol ?? selection.StartCol;
                const endRow = selection.endRow ?? selection.EndRow;
                const endCol = selection.endCol ?? selection.EndCol;
                return endRow > startRow && endCol > startCol;
            }",
            await grid.ElementHandleAsync());

        var selectionRedraws = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).selectionRedrawCount");
        Assert.IsTrue(selectionRedraws > 0, $"Expected drag selection to redraw the selection overlay. Count: {selectionRedraws}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_DragSelectionAutoscrollsDown()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);

        var startX = (int)Math.Round(box.X + 92);
        var startY = (int)Math.Round(box.Y + 42);
        var edgeY = (int)Math.Round(box.Y + box.Height - 2);

        await grid.DispatchEventAsync("pointerdown", new
        {
            clientX = startX,
            clientY = startY,
            button = 0,
            buttons = 1,
            pointerId = 8,
            pointerType = "mouse"
        });

        await grid.DispatchEventAsync("pointermove", new
        {
            clientX = startX,
            clientY = edgeY,
            button = 0,
            buttons = 1,
            pointerId = 8,
            pointerType = "mouse"
        });

        await page.WaitForFunctionAsync(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const selection = el.__tmSpreadsheetCanvas?.model?.selection || el.__tmSpreadsheetCanvas?.model?.Selection;
                const startRow = selection?.startRow ?? selection?.StartRow ?? 0;
                const endRow = selection?.endRow ?? selection?.EndRow ?? 0;
                return metrics.logicalScrollTop > 0 && metrics.dragAutoscrollFrames > 0 && endRow > startRow;
            }",
            await grid.ElementHandleAsync());

        await grid.DispatchEventAsync("pointerup", new
        {
            clientX = startX,
            clientY = edgeY,
            button = 0,
            buttons = 0,
            pointerId = 8,
            pointerType = "mouse"
        });
    }

    [TestMethod]
    public async Task CanvasRenderer_DragSelectionAutoscrollsRight()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);

        var startX = (int)Math.Round(box.X + 92);
        var startY = (int)Math.Round(box.Y + 42);
        var edgeX = (int)Math.Round(box.X + box.Width - 2);

        await grid.DispatchEventAsync("pointerdown", new
        {
            clientX = startX,
            clientY = startY,
            button = 0,
            buttons = 1,
            pointerId = 9,
            pointerType = "mouse"
        });

        await grid.DispatchEventAsync("pointermove", new
        {
            clientX = edgeX,
            clientY = startY,
            button = 0,
            buttons = 1,
            pointerId = 9,
            pointerType = "mouse"
        });

        await page.WaitForFunctionAsync(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const selection = el.__tmSpreadsheetCanvas?.model?.selection || el.__tmSpreadsheetCanvas?.model?.Selection;
                const startCol = selection?.startCol ?? selection?.StartCol ?? 0;
                const endCol = selection?.endCol ?? selection?.EndCol ?? 0;
                return metrics.logicalScrollLeft > 0 && metrics.dragAutoscrollFrames > 0 && endCol > startCol;
            }",
            await grid.ElementHandleAsync());

        await grid.DispatchEventAsync("pointerup", new
        {
            clientX = edgeX,
            clientY = startY,
            button = 0,
            buttons = 0,
            pointerId = 9,
            pointerType = "mouse"
        });
    }

    [TestMethod]
    public async Task CanvasRenderer_DragSelectionAutoscrollStaysOnLogicalHotPath()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);

        var startX = (int)Math.Round(box.X + 92);
        var startY = (int)Math.Round(box.Y + 42);
        var edgeY = (int)Math.Round(box.Y + box.Height - 2);
        var before = await grid.EvaluateAsync<CanvasDragAutoscrollHotPathProbeResult>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el)");

        await grid.DispatchEventAsync("pointerdown", new
        {
            clientX = startX,
            clientY = startY,
            button = 0,
            buttons = 1,
            pointerId = 18,
            pointerType = "mouse"
        });

        await grid.DispatchEventAsync("pointermove", new
        {
            clientX = startX,
            clientY = edgeY,
            button = 0,
            buttons = 1,
            pointerId = 18,
            pointerType = "mouse"
        });

        await page.WaitForFunctionAsync(
            @"el => {
                const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                return metrics.logicalScrollTop > 0 && metrics.dragAutoscrollFrames >= 4;
            }",
            await grid.ElementHandleAsync());

        await grid.DispatchEventAsync("pointerup", new
        {
            clientX = startX,
            clientY = edgeY,
            button = 0,
            buttons = 0,
            pointerId = 18,
            pointerType = "mouse"
        });

        await page.WaitForTimeoutAsync(50);
        var after = await grid.EvaluateAsync<CanvasDragAutoscrollHotPathProbeResult>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el)");

        var dragFrames = after.DragAutoscrollFrames - before.DragAutoscrollFrames;
        var viewportCallbacks = after.ViewportCallbackCount - before.ViewportCallbackCount;
        var selectionCallbacks = after.SelectionCallbackCount - before.SelectionCallbackCount;
        var scrollToCount = after.ScrollToCount - before.ScrollToCount;
        var selectionRedraws = after.SelectionRedrawCount - before.SelectionRedrawCount;

        Assert.IsTrue(after.LogicalScrollTop > before.LogicalScrollTop, $"Expected drag autoscroll to advance logical scroll. Before: {before.LogicalScrollTop}, after: {after.LogicalScrollTop}.");
        Assert.IsTrue(after.LogicalPointerScrollCount > before.LogicalPointerScrollCount, "Expected drag autoscroll to use logical pointer scroll.");
        Assert.IsTrue(dragFrames >= 4, $"Expected several drag autoscroll frames. Count: {dragFrames}.");
        Assert.IsTrue(scrollToCount <= 1, $"Expected drag autoscroll to avoid native scrollTo per frame. scrollTo delta: {scrollToCount}, drag frames: {dragFrames}.");
        Assert.IsTrue(viewportCallbacks <= 2, $"Expected drag autoscroll viewport callbacks to be coalesced. Callbacks: {viewportCallbacks}, drag frames: {dragFrames}.");
        Assert.IsTrue(selectionCallbacks <= 2, $"Expected drag selection callbacks to be coalesced. Callbacks: {selectionCallbacks}, drag frames: {dragFrames}.");
        Assert.IsTrue(selectionRedraws >= dragFrames, $"Expected selection overlay to redraw during drag autoscroll frames. Selection redraws: {selectionRedraws}, drag frames: {dragFrames}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_WheelScrollStaysOnLogicalHotPath()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var result = await grid.EvaluateAsync<CanvasWheelHotPathProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                for (let i = 0; i < 40; i++) {
                    el.dispatchEvent(new WheelEvent('wheel', {
                        deltaY: 32,
                        deltaMode: WheelEvent.DOM_DELTA_PIXEL,
                        bubbles: true,
                        cancelable: true
                    }));
                }

                requestAnimationFrame(() => requestAnimationFrame(() => {
                    setTimeout(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        resolve({
                            logicalScrollTop: after.logicalScrollTop,
                            nativeScrollTop: after.nativeScrollTop,
                            wheelEvents: after.wheelEventCount - before.wheelEventCount,
                            wheelPrevented: after.wheelPreventedCount - before.wheelPreventedCount,
                            logicalWheelScrollCount: after.logicalWheelScrollCount - before.logicalWheelScrollCount,
                            viewportCallbacks: after.viewportCallbackCount - before.viewportCallbackCount,
                            scrollToCount: after.scrollToCount - before.scrollToCount,
                            paintRequests: after.paintRequestCount - before.paintRequestCount,
                            paintFrames: after.paintFrameCount - before.paintFrameCount,
                            contentPaintFrames: after.contentPaintFrameCount - before.contentPaintFrameCount,
                            maxMergedPaintRequestsPerFrame: after.maxMergedPaintRequestsPerFrame
                        });
                    }, 180);
                }));
            })");

        Assert.IsTrue(result.LogicalScrollTop > 0, $"Expected wheel to advance logical scroll. logicalScrollTop: {result.LogicalScrollTop}.");
        Assert.AreEqual(40, result.WheelEvents, $"Expected every wheel event to stay on the canvas wheel path. Count: {result.WheelEvents}.");
        Assert.AreEqual(40, result.WheelPrevented, $"Expected wheel events to be prevented before native scroll. Count: {result.WheelPrevented}.");
        Assert.IsTrue(result.LogicalWheelScrollCount > 0, $"Expected wheel to use logical scroll. Count: {result.LogicalWheelScrollCount}.");
        Assert.IsTrue(result.PaintFrames <= 2, $"Expected wheel paint to be coalesced into at most two frames. Paint frames: {result.PaintFrames}.");
        Assert.IsTrue(result.ContentPaintFrames <= 2, $"Expected wheel content paint to be coalesced. Content frames: {result.ContentPaintFrames}.");
        Assert.IsTrue(result.ScrollToCount <= 1, $"Expected wheel to sync the native scrollbar at most once after paint. scrollTo delta: {result.ScrollToCount}.");
        Assert.IsTrue(result.ViewportCallbacks <= 2, $"Expected wheel viewport callbacks to be coalesced. Count: {result.ViewportCallbacks}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_ColumnResizeDragStaysJsOnlyUntilCommitAndSyncsModel()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasResizeDragProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const columns = model.Columns || model.columns || [];
                const column = columns[1] || columns[0];
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const left = column.Left ?? column.left ?? 0;
                const initialSize = column.Width ?? column.width ?? 64;
                const rect = el.getBoundingClientRect();
                const startX = rect.left + rowHeaderWidth + left + initialSize - 1;
                const startY = rect.top + Math.max(6, columnHeaderHeight / 2);
                const endX = startX + 48;
                const pointerId = 71;
                const dispatch = (type, x, y, buttons) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                dispatch('pointerdown', startX, startY, 1);
                let move = 0;
                const moveCount = 8;
                const step = () => {
                    move += 1;
                    dispatch('pointermove', startX + (endX - startX) * move / moveCount, startY, 1);
                    if (move < moveCount) {
                        requestAnimationFrame(step);
                        return;
                    }

                    const mid = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const preview = mid.sheetState?.resize;
                    dispatch('pointerup', endX, startY, 0);

                    setTimeout(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        const syncedColumns = state.model.Columns || state.model.columns || [];
                        const synced = syncedColumns[1] || syncedColumns[0];
                        resolve({
                            initialSize,
                            previewSize: preview?.currentSize ?? 0,
                            finalSize: synced?.Width ?? synced?.width ?? 0,
                            pointerMoves: (mid.resizePointerMoveCount || 0) - (before.resizePointerMoveCount || 0),
                            paintFramesBeforeCommit: (mid.resizePaintFrameCount || 0) - (before.resizePaintFrameCount || 0),
                            contentPaintFramesBeforeCommit: (mid.contentPaintFrameCount || 0) - (before.contentPaintFrameCount || 0),
                            dotNetBeforeCommit: (mid.resizeDotNetCallbackCount || 0) - (before.resizeDotNetCallbackCount || 0),
                            blazorBeforeCommit: (mid.resizeBlazorFrameCount || 0) - (before.resizeBlazorFrameCount || 0),
                            dotNetAfterCommit: (after.resizeDotNetCallbackCount || 0) - (before.resizeDotNetCallbackCount || 0),
                            blazorAfterCommit: (after.resizeBlazorFrameCount || 0) - (before.resizeBlazorFrameCount || 0),
                            commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0)
                        });
                    }, 420);
                };

                requestAnimationFrame(step);
            })");

        Assert.IsTrue(result.PointerMoves >= 6, $"Expected column resize drag to process multiple pointer moves. Count: {result.PointerMoves:N0}.");
        Assert.IsTrue(result.PreviewSize > result.InitialSize, $"Expected local JS preview width to grow during drag. Initial: {result.InitialSize:N1}, preview: {result.PreviewSize:N1}.");
        Assert.IsTrue(result.PaintFramesBeforeCommit > 0, "Expected column resize preview to repaint during drag.");
        Assert.AreEqual(0, result.ContentPaintFramesBeforeCommit, $"Column resize preview should not repaint content per move. Content frames before commit: {result.ContentPaintFramesBeforeCommit:N0}.");
        Assert.AreEqual(0, result.DotNetBeforeCommit, $"Column resize should stay JS-only until commit. .NET callbacks before commit: {result.DotNetBeforeCommit:N0}.");
        Assert.AreEqual(0, result.BlazorBeforeCommit, $"Column resize should not trigger Blazor frames during drag. Frames before commit: {result.BlazorBeforeCommit:N0}.");
        Assert.IsTrue(result.DotNetAfterCommit <= 1, $"Expected at most one .NET resize callback after commit. Count: {result.DotNetAfterCommit:N0}.");
        Assert.IsTrue(result.BlazorAfterCommit <= 2, $"Expected resize commit to stay bounded to a tiny number of Blazor frames. Count: {result.BlazorAfterCommit:N0}.");
        Assert.IsTrue(result.CommandLogCallbacks >= 1, "Expected column resize commit to reach .NET through the command log.");
        Assert.IsTrue(result.FinalSize >= result.PreviewSize - 1, $"Expected synced model width to keep the committed size. Final: {result.FinalSize:N1}, preview: {result.PreviewSize:N1}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_RowResizeDragStaysJsOnlyUntilCommitAndSyncsModel()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasResizeDragProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rows = model.Rows || model.rows || [];
                const row = rows[2] || rows[1] || rows[0];
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const top = row.Top ?? row.top ?? 0;
                const initialSize = row.Height ?? row.height ?? 20;
                const rect = el.getBoundingClientRect();
                const startX = rect.left + Math.max(8, rowHeaderWidth / 2);
                const startY = rect.top + columnHeaderHeight + top + initialSize - 1;
                const endY = startY + 20;
                const pointerId = 72;
                const dispatch = (type, x, y, buttons) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                dispatch('pointerdown', startX, startY, 1);
                let move = 0;
                const moveCount = 8;
                const step = () => {
                    move += 1;
                    dispatch('pointermove', startX, startY + (endY - startY) * move / moveCount, 1);
                    if (move < moveCount) {
                        requestAnimationFrame(step);
                        return;
                    }

                    const mid = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const preview = mid.sheetState?.resize;
                    dispatch('pointerup', startX, endY, 0);

                    setTimeout(() => {
                        const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                        const syncedRows = state.model.Rows || state.model.rows || [];
                        const synced = syncedRows[2] || syncedRows[1] || syncedRows[0];
                        resolve({
                            initialSize,
                            previewSize: preview?.currentSize ?? 0,
                            finalSize: synced?.Height ?? synced?.height ?? 0,
                            pointerMoves: (mid.resizePointerMoveCount || 0) - (before.resizePointerMoveCount || 0),
                            paintFramesBeforeCommit: (mid.resizePaintFrameCount || 0) - (before.resizePaintFrameCount || 0),
                            contentPaintFramesBeforeCommit: (mid.contentPaintFrameCount || 0) - (before.contentPaintFrameCount || 0),
                            dotNetBeforeCommit: (mid.resizeDotNetCallbackCount || 0) - (before.resizeDotNetCallbackCount || 0),
                            blazorBeforeCommit: (mid.resizeBlazorFrameCount || 0) - (before.resizeBlazorFrameCount || 0),
                            dotNetAfterCommit: (after.resizeDotNetCallbackCount || 0) - (before.resizeDotNetCallbackCount || 0),
                            blazorAfterCommit: (after.resizeBlazorFrameCount || 0) - (before.resizeBlazorFrameCount || 0),
                            commandLogCallbacks: (after.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0) - (before.dotNetCallbacksByMethod.OnCanvasCommandLogBatch || 0)
                        });
                    }, 420);
                };

                requestAnimationFrame(step);
            })");

        Assert.IsTrue(result.PointerMoves >= 6, $"Expected row resize drag to process multiple pointer moves. Count: {result.PointerMoves:N0}.");
        Assert.IsTrue(result.PreviewSize > result.InitialSize, $"Expected local JS preview height to grow during drag. Initial: {result.InitialSize:N1}, preview: {result.PreviewSize:N1}.");
        Assert.IsTrue(result.PaintFramesBeforeCommit > 0, "Expected row resize preview to repaint during drag.");
        Assert.AreEqual(0, result.ContentPaintFramesBeforeCommit, $"Row resize preview should not repaint content per move. Content frames before commit: {result.ContentPaintFramesBeforeCommit:N0}.");
        Assert.AreEqual(0, result.DotNetBeforeCommit, $"Row resize should stay JS-only until commit. .NET callbacks before commit: {result.DotNetBeforeCommit:N0}.");
        Assert.AreEqual(0, result.BlazorBeforeCommit, $"Row resize should not trigger Blazor frames during drag. Frames before commit: {result.BlazorBeforeCommit:N0}.");
        Assert.IsTrue(result.DotNetAfterCommit <= 1, $"Expected at most one .NET resize callback after commit. Count: {result.DotNetAfterCommit:N0}.");
        Assert.IsTrue(result.BlazorAfterCommit <= 2, $"Expected row resize commit to stay bounded to a tiny number of Blazor frames. Count: {result.BlazorAfterCommit:N0}.");
        Assert.IsTrue(result.CommandLogCallbacks >= 1, "Expected row resize commit to reach .NET through the command log.");
        Assert.IsTrue(result.FinalSize >= result.PreviewSize - 1, $"Expected synced model height to keep the committed size. Final: {result.FinalSize:N1}, preview: {result.PreviewSize:N1}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_RowResizeSecondDragStartsFromCommittedHeight()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasResizeTwiceProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const rect = el.getBoundingClientRect();
                const startX = rect.left + Math.max(8, rowHeaderWidth / 2);
                const pointerId = 73;

                const dispatch = (type, x, y, buttons) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                const getRow = () => {
                    const rows = state.model.Rows || state.model.rows || [];
                    return rows[2] || rows[1] || rows[0];
                };

                const dragRow = delta => new Promise(done => {
                    const row = getRow();
                    const top = row.Top ?? row.top ?? 0;
                    const size = row.Height ?? row.height ?? 20;
                    const startY = rect.top + columnHeaderHeight + top + size - 1;
                    const endY = startY + delta;
                    let move = 0;
                    const moveCount = 8;

                    dispatch('pointerdown', startX, startY, 1);

                    const step = () => {
                        move += 1;
                        dispatch('pointermove', startX, startY + (endY - startY) * move / moveCount, 1);
                        if (move < moveCount) {
                            requestAnimationFrame(step);
                            return;
                        }

                        const preview = window.tmSpreadsheetCanvas.getDebugMetrics(el).sheetState?.resize?.currentSize ?? 0;
                        dispatch('pointerup', startX, endY, 0);
                        setTimeout(() => {
                            const synced = getRow();
                            done({
                                startSize: size,
                                previewSize: preview,
                                finalSize: synced?.Height ?? synced?.height ?? 0
                            });
                        }, 260);
                    };

                    requestAnimationFrame(step);
                });

                (async () => {
                    const first = await dragRow(18);
                    const second = await dragRow(16);
                    const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        firstStartSize: first.startSize,
                        firstPreviewSize: first.previewSize,
                        firstFinalSize: first.finalSize,
                        secondStartSize: second.startSize,
                        secondPreviewSize: second.previewSize,
                        secondFinalSize: second.finalSize,
                        commandLogCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCommandLogBatch || 0
                    });
                })();
            })");

        Assert.IsTrue(result.FirstPreviewSize > result.FirstStartSize, $"Expected first row resize preview to grow. Start: {result.FirstStartSize:N1}, preview: {result.FirstPreviewSize:N1}.");
        Assert.IsTrue(result.FirstFinalSize >= result.FirstPreviewSize - 1, $"Expected first committed row height to match preview. Final: {result.FirstFinalSize:N1}, preview: {result.FirstPreviewSize:N1}.");
        Assert.IsTrue(result.SecondStartSize >= result.FirstFinalSize - 1, $"Expected second resize to start from committed height, not original. First final: {result.FirstFinalSize:N1}, second start: {result.SecondStartSize:N1}.");
        Assert.IsTrue(result.SecondPreviewSize > result.SecondStartSize, $"Expected second row resize preview to grow from the already committed height. Start: {result.SecondStartSize:N1}, preview: {result.SecondPreviewSize:N1}.");
        Assert.IsTrue(result.SecondFinalSize >= result.SecondPreviewSize - 1, $"Expected second committed row height to match preview. Final: {result.SecondFinalSize:N1}, preview: {result.SecondPreviewSize:N1}.");
        Assert.IsTrue(result.CommandLogCallbacks >= 2, $"Expected both row resize commits to reach .NET through the command log. Callback count: {result.CommandLogCallbacks:N0}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_ColumnResizeSecondDragStartsFromCommittedWidth()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasResizeTwiceProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const rect = el.getBoundingClientRect();
                const startY = rect.top + Math.max(6, columnHeaderHeight / 2);
                const pointerId = 74;

                const dispatch = (type, x, y, buttons) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                const getColumn = () => {
                    const columns = state.model.Columns || state.model.columns || [];
                    return columns[1] || columns[0];
                };

                const dragColumn = delta => new Promise(done => {
                    const column = getColumn();
                    const left = column.Left ?? column.left ?? 0;
                    const size = column.Width ?? column.width ?? 64;
                    const startX = rect.left + rowHeaderWidth + left + size - 1;
                    const endX = startX + delta;
                    let move = 0;
                    const moveCount = 8;

                    dispatch('pointerdown', startX, startY, 1);

                    const step = () => {
                        move += 1;
                        dispatch('pointermove', startX + (endX - startX) * move / moveCount, startY, 1);
                        if (move < moveCount) {
                            requestAnimationFrame(step);
                            return;
                        }

                        const preview = window.tmSpreadsheetCanvas.getDebugMetrics(el).sheetState?.resize?.currentSize ?? 0;
                        dispatch('pointerup', endX, startY, 0);
                        setTimeout(() => {
                            const synced = getColumn();
                            done({
                                startSize: size,
                                previewSize: preview,
                                finalSize: synced?.Width ?? synced?.width ?? 0
                            });
                        }, 260);
                    };

                    requestAnimationFrame(step);
                });

                (async () => {
                    const first = await dragColumn(36);
                    const second = await dragColumn(28);
                    const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        firstStartSize: first.startSize,
                        firstPreviewSize: first.previewSize,
                        firstFinalSize: first.finalSize,
                        secondStartSize: second.startSize,
                        secondPreviewSize: second.previewSize,
                        secondFinalSize: second.finalSize,
                        commandLogCallbacks: metrics.dotNetCallbacksByMethod?.OnCanvasCommandLogBatch || 0
                    });
                })();
            })");

        Assert.IsTrue(result.FirstPreviewSize > result.FirstStartSize, $"Expected first column resize preview to grow. Start: {result.FirstStartSize:N1}, preview: {result.FirstPreviewSize:N1}.");
        Assert.IsTrue(result.FirstFinalSize >= result.FirstPreviewSize - 1, $"Expected first committed column width to match preview. Final: {result.FirstFinalSize:N1}, preview: {result.FirstPreviewSize:N1}.");
        Assert.IsTrue(result.SecondStartSize >= result.FirstFinalSize - 1, $"Expected second resize to start from committed width, not original. First final: {result.FirstFinalSize:N1}, second start: {result.SecondStartSize:N1}.");
        Assert.IsTrue(result.SecondPreviewSize > result.SecondStartSize, $"Expected second column resize preview to grow from the already committed width. Start: {result.SecondStartSize:N1}, preview: {result.SecondPreviewSize:N1}.");
        Assert.IsTrue(result.SecondFinalSize >= result.SecondPreviewSize - 1, $"Expected second committed column width to match preview. Final: {result.SecondFinalSize:N1}, preview: {result.SecondPreviewSize:N1}.");
        Assert.IsTrue(result.CommandLogCallbacks >= 2, $"Expected both column resize commits to reach .NET through the command log. Callback count: {result.CommandLogCallbacks:N0}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_ResizeCommitKeepsEditorSelectionAndFormulaHighlightAligned()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasResizeAlignmentProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const rows = model.Rows || model.rows || [];
                const columns = model.Columns || model.columns || [];
                const row = rows[0];
                const column = columns[0];

                el.focus();
                el.dispatchEvent(new KeyboardEvent('keydown', {
                    key: '=',
                    bubbles: true,
                    cancelable: true
                }));

                const input = el.querySelector('.tm-spreadsheet-canvas-grid__editor');
                input.value = '=B2';
                input.setSelectionRange(input.value.length, input.value.length);
                input.dispatchEvent(new Event('input', { bubbles: true }));

                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const beforeRect = input.getBoundingClientRect();
                const oldHeight = row.Height ?? row.height ?? 20;
                const oldWidth = column.Width ?? column.width ?? 64;
                const newHeight = oldHeight + 14;
                const newWidth = oldWidth + 26;
                const totalHeight = (model.TotalHeight ?? model.totalHeight ?? 0) + (newHeight - oldHeight);
                const totalWidth = (model.TotalWidth ?? model.totalWidth ?? 0) + (newWidth - oldWidth);

                window.tmSpreadsheetCanvas.applyCommand(el, {
                    Type: 'syncLayoutAxes',
                    RowCount: model.RowCount ?? model.rowCount ?? rows.length,
                    ColumnCount: model.ColumnCount ?? model.columnCount ?? columns.length,
                    TotalWidth: totalWidth,
                    TotalHeight: totalHeight,
                    FreezeRowCount: model.FreezeRowCount ?? model.freezeRowCount ?? 0,
                    FreezeColumnCount: model.FreezeColumnCount ?? model.freezeColumnCount ?? 0,
                    Rows: [{
                        Index: 0,
                        Top: 0,
                        Height: newHeight,
                        Frozen: false
                    }],
                    Columns: [{
                        Index: 0,
                        Left: 0,
                        Width: newWidth,
                        Label: 'A',
                        Frozen: false
                    }]
                });

                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    const afterRect = input.getBoundingClientRect();
                    const afterStyleWidth = parseFloat(input.style.width || '0');
                    const afterStyleHeight = parseFloat(input.style.height || '0');
                    const selectionCanvas = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--selection');
                    const dpr = window.devicePixelRatio || 1;
                    const ctx = selectionCanvas.getContext('2d');
                    const countBluePixels = (x, y, w, h) => {
                        const data = ctx.getImageData(Math.max(0, Math.round(x * dpr)), Math.max(0, Math.round(y * dpr)), Math.max(1, Math.round(w * dpr)), Math.max(1, Math.round(h * dpr))).data;
                        let count = 0;
                        for (let i = 0; i < data.length; i += 4) {
                            if (data[i + 3] > 0 && data[i + 2] > data[i] + 20 && data[i + 2] > data[i + 1] - 10) count++;
                        }
                        return count;
                    };

                    const selectionBluePixels = countBluePixels(
                        rowHeaderWidth + newWidth - 4,
                        columnHeaderHeight + 2,
                        8,
                        Math.max(8, newHeight - 4));

                    const formulaBluePixels = countBluePixels(
                        rowHeaderWidth + newWidth + 2,
                        columnHeaderHeight + newHeight + 2,
                        14,
                        14);

                    resolve({
                        beforeEditorWidth: beforeRect.width,
                        afterEditorWidth: afterRect.width,
                        beforeEditorHeight: beforeRect.height,
                        afterEditorHeight: afterRect.height,
                        afterEditorStyleWidth: afterStyleWidth,
                        afterEditorStyleHeight: afterStyleHeight,
                        editorRow: state.editor?.row ?? -1,
                        editorCol: state.editor?.col ?? -1,
                        layoutColumnWidth: state.sheetState?.layoutState?.columnSizes?.get(0) ?? 0,
                        layoutRowHeight: state.sheetState?.layoutState?.rowSizes?.get(0) ?? 0,
                        modelColumnWidth: (state.model.Columns || state.model.columns || [])[0]?.Width ?? (state.model.Columns || state.model.columns || [])[0]?.width ?? 0,
                        formulaActive: !!after.sheetState?.formulaEditor?.active,
                        formulaRefCount: after.sheetState?.formulaEditor?.refCount || 0,
                        selectionPaintDelta: (after.selectionLayerPaintCount || 0) - (before.selectionLayerPaintCount || 0),
                        selectionBluePixels,
                        formulaBluePixels
                    });
                }));
            })");

        Assert.IsTrue(result.FormulaActive, "Expected formula editor to stay active after resize commit patch.");
        Assert.AreEqual(1, result.FormulaRefCount, "Expected one parsed formula reference after resize commit patch.");
        Assert.IsTrue(result.AfterEditorStyleWidth > result.BeforeEditorWidth, $"Expected editor width to follow resized column. Before: {result.BeforeEditorWidth:N1}, styled after: {result.AfterEditorStyleWidth:N1}, rect after: {result.AfterEditorWidth:N1}, editor cell: {result.EditorRow}:{result.EditorCol}, layout width: {result.LayoutColumnWidth:N1}, model width: {result.ModelColumnWidth:N1}.");
        Assert.IsTrue(result.AfterEditorStyleHeight > result.BeforeEditorHeight, $"Expected editor height to follow resized row. Before: {result.BeforeEditorHeight:N1}, styled after: {result.AfterEditorStyleHeight:N1}, rect after: {result.AfterEditorHeight:N1}, editor cell: {result.EditorRow}:{result.EditorCol}, layout height: {result.LayoutRowHeight:N1}.");
        Assert.IsTrue(result.SelectionPaintDelta > 0, $"Expected selection overlay to repaint after resize commit patch. Delta: {result.SelectionPaintDelta:N0}.");
        Assert.IsTrue(result.SelectionBluePixels > 0, $"Expected active-cell selection pixels after resize commit patch. Count: {result.SelectionBluePixels:N0}.");
        Assert.IsTrue(result.FormulaBluePixels > 0, $"Expected formula reference highlight pixels after resize commit patch. Count: {result.FormulaBluePixels:N0}.");
    }

    [TestMethod]
    public async Task CanvasJsEngine_ResizeHotPathWorksWithFrozenAxes()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasFrozenResizeProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rows = model.Rows || model.rows || [];
                const columns = model.Columns || model.columns || [];
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const rect = el.getBoundingClientRect();
                const originalInvoke = state.dotNet.invokeMethodAsync.bind(state.dotNet);
                state.dotNet.invokeMethodAsync = () => Promise.resolve(0);

                window.tmSpreadsheetCanvas.applyCommand(el, {
                    Type: 'syncLayoutAxes',
                    RowCount: model.RowCount ?? model.rowCount ?? rows.length,
                    ColumnCount: model.ColumnCount ?? model.columnCount ?? columns.length,
                    TotalWidth: model.TotalWidth ?? model.totalWidth ?? 0,
                    TotalHeight: model.TotalHeight ?? model.totalHeight ?? 0,
                    FreezeRowCount: 1,
                    FreezeColumnCount: 1,
                    Rows: [{ Index: 0, Top: 0, Height: (rows[0]?.Height ?? rows[0]?.height ?? 20), Frozen: true }],
                    Columns: [{ Index: 0, Left: 0, Width: (columns[0]?.Width ?? columns[0]?.width ?? 64), Label: 'A', Frozen: true }]
                });
                window.tmSpreadsheetCanvas.render(el, state.canvas, state.model);

                const dispatch = (type, x, y, buttons, pointerId) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                const dragColumn = delta => new Promise(done => {
                    const column = (state.model.Columns || state.model.columns || [])[0];
                    const size = column.Width ?? column.width ?? 64;
                    const startX = rect.left + rowHeaderWidth + size - 1;
                    const startY = rect.top + Math.max(6, columnHeaderHeight / 2);
                    const endX = startX + delta;
                    let move = 0;
                    const moveCount = 6;
                    const pointerId = 75;
                    dispatch('pointerdown', startX, startY, 1, pointerId);
                    const step = () => {
                        move += 1;
                        dispatch('pointermove', startX + (endX - startX) * move / moveCount, startY, 1, pointerId);
                        if (move < moveCount) {
                            requestAnimationFrame(step);
                            return;
                        }

                        dispatch('pointerup', endX, startY, 0, pointerId);
                        requestAnimationFrame(() => {
                            const synced = (state.model.Columns || state.model.columns || [])[0];
                            done(synced?.Width ?? synced?.width ?? 0);
                        });
                    };

                    requestAnimationFrame(step);
                });

                const dragRow = delta => new Promise(done => {
                    const row = (state.model.Rows || state.model.rows || [])[0];
                    const size = row.Height ?? row.height ?? 20;
                    const startX = rect.left + Math.max(8, rowHeaderWidth / 2);
                    const startY = rect.top + columnHeaderHeight + size - 1;
                    const endY = startY + delta;
                    let move = 0;
                    const moveCount = 6;
                    const pointerId = 76;
                    dispatch('pointerdown', startX, startY, 1, pointerId);
                    const step = () => {
                        move += 1;
                        dispatch('pointermove', startX, startY + (endY - startY) * move / moveCount, 1, pointerId);
                        if (move < moveCount) {
                            requestAnimationFrame(step);
                            return;
                        }

                        dispatch('pointerup', startX, endY, 0, pointerId);
                        requestAnimationFrame(() => {
                            const synced = (state.model.Rows || state.model.rows || [])[0];
                            done(synced?.Height ?? synced?.height ?? 0);
                        });
                    };

                    requestAnimationFrame(step);
                });

                (async () => {
                    const initialColumn = columns[0]?.Width ?? columns[0]?.width ?? 64;
                    const initialRow = rows[0]?.Height ?? rows[0]?.height ?? 20;
                    const finalColumn = await dragColumn(24);
                    const finalRow = await dragRow(12);
                    const layout = state.sheetState?.layoutState;
                    state.dotNet.invokeMethodAsync = originalInvoke;
                    resolve({
                        freezeRowCount: state.model.FreezeRowCount ?? state.model.freezeRowCount ?? 0,
                        freezeColumnCount: state.model.FreezeColumnCount ?? state.model.freezeColumnCount ?? 0,
                        initialColumnSize: initialColumn,
                        finalColumnSize: layout?.columnSizes?.get(0) ?? finalColumn,
                        initialRowSize: initialRow,
                        finalRowSize: layout?.rowSizes?.get(0) ?? finalRow
                    });
                })();
            })");

        Assert.AreEqual(1, result.FreezeRowCount, $"Expected one frozen row during resize verification. Count: {result.FreezeRowCount:N0}.");
        Assert.AreEqual(1, result.FreezeColumnCount, $"Expected one frozen column during resize verification. Count: {result.FreezeColumnCount:N0}.");
        Assert.IsTrue(result.FinalColumnSize > result.InitialColumnSize, $"Expected frozen column resize to grow. Initial: {result.InitialColumnSize:N1}, final: {result.FinalColumnSize:N1}.");
        Assert.IsTrue(result.FinalRowSize > result.InitialRowSize, $"Expected frozen row resize to grow. Initial: {result.InitialRowSize:N1}, final: {result.FinalRowSize:N1}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_SelectedCellKeepsTextAndDrawsFormatting()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        var result = await grid.EvaluateAsync<CanvasFormattingProbeResult>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const cells = model.cells || model.Cells;
                const cell = cells.find(c => (c.row ?? c.Row) === 0 && (c.col ?? c.Col) === 0) || cells[0];
                cell.value = 'Visible';
                cell.Value = 'Visible';
                cell.active = true;
                cell.Active = true;
                cell.selected = true;
                cell.Selected = true;
                cell.selectionEnd = true;
                cell.SelectionEnd = true;
                cell.style = cell.Style = {
                    fontFamily: 'Arial',
                    FontFamily: 'Arial',
                    fontSize: 14,
                    FontSize: 14,
                    bold: true,
                    Bold: true,
                    italic: true,
                    Italic: true,
                    underline: true,
                    Underline: true,
                    foreColor: '#111827',
                    ForeColor: '#111827',
                    horizontalAlign: 'left',
                    HorizontalAlign: 'left',
                    verticalAlign: 'bottom',
                    VerticalAlign: 'bottom',
                    borderBottom: { style: 'thick', color: '#111827', Style: 'thick', Color: '#111827' },
                    BorderBottom: { style: 'thick', color: '#111827', Style: 'thick', Color: '#111827' }
                };
                window.tmSpreadsheetCanvas.render(el, state.canvas, model);

                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const content = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--content');
                    const selection = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--selection');
                    const dpr = window.devicePixelRatio || 1;
                    const composite = document.createElement('canvas');
                    composite.width = content.width;
                    composite.height = content.height;
                    const ctx = composite.getContext('2d');
                    ctx.drawImage(content, 0, 0);
                    ctx.drawImage(selection, 0, 0);
                    const darkPixels = (x, y, w, h) => {
                        const data = ctx.getImageData(Math.round(x * dpr), Math.round(y * dpr), Math.round(w * dpr), Math.round(h * dpr)).data;
                        let count = 0;
                        for (let i = 0; i < data.length; i += 4) {
                            if (data[i + 3] > 0 && data[i] < 180 && data[i + 1] < 180 && data[i + 2] < 180) count++;
                        }
                        return count;
                    };

                    resolve({
                        darkTextPixels: darkPixels(42, 24, 58, 16),
                        underlinePixels: darkPixels(42, 35, 58, 5),
                        borderPixels: darkPixels(40, 38, 64, 5),
                        fontCache: [...state.fontStringCache.values()].join('|')
                    });
                }));
            })");

        Assert.IsTrue(result.DarkTextPixels > 8, $"Expected selected cell text to stay visible. Dark pixels: {result.DarkTextPixels}.");
        Assert.IsTrue(result.UnderlinePixels > 8, $"Expected underline pixels to be drawn. Dark pixels: {result.UnderlinePixels}.");
        Assert.IsTrue(result.BorderPixels > 20, $"Expected a bottom border to be drawn. Dark pixels: {result.BorderPixels}.");
        Assert.IsTrue(result.FontCache.Contains("italic 700", StringComparison.Ordinal), $"Expected italic bold canvas font. Cache: {result.FontCache}");
    }

    [TestMethod]
    public async Task CanvasRenderer_DrawsFormulaReferenceHighlights()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        var bluePixels = await grid.EvaluateAsync<int>(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const cells = model.cells || model.Cells;
                const cell = cells.find(c => (c.row ?? c.Row) === 1 && (c.col ?? c.Col) === 1) || cells[0];
                cell.formulaRefColorIndex = 0;
                cell.FormulaRefColorIndex = 0;
                window.tmSpreadsheetCanvas.render(el, state.canvas, model);

                requestAnimationFrame(() => requestAnimationFrame(() => {
                    const selection = el.querySelector('.tm-spreadsheet-canvas-grid__canvas--selection');
                    const dpr = window.devicePixelRatio || 1;
                    const ctx = selection.getContext('2d');
                    const left = (cell.left ?? cell.Left) - (model.scrollLeft ?? model.ScrollLeft ?? 0) + (model.rowHeaderWidth ?? model.RowHeaderWidth ?? 40);
                    const top = (cell.top ?? cell.Top) - (model.scrollTop ?? model.ScrollTop ?? 0) + (model.columnHeaderHeight ?? model.ColumnHeaderHeight ?? 20);
                    const data = ctx.getImageData(Math.round(left * dpr), Math.round(top * dpr), Math.round((cell.width ?? cell.Width) * dpr), Math.round((cell.height ?? cell.Height) * dpr)).data;
                    let count = 0;
                    for (let i = 0; i < data.length; i += 4) {
                        if (data[i + 3] > 0 && data[i] < 80 && data[i + 1] > 90 && data[i + 2] > 120) count++;
                    }
                    resolve(count);
                }));
            })");

        Assert.IsTrue(bluePixels > 20, $"Expected formula reference highlight pixels. Count: {bluePixels}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_ExposesRedrawDebugMetrics()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();
        await grid.EvaluateAsync(
            @"el => {
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const cells = model.cells || model.Cells;
                const simple = cells.find(c => (c.row ?? c.Row) === 2 && (c.col ?? c.Col) === 1) || cells[0];
                simple.value = 'Fast';
                simple.Value = 'Fast';
                simple.style = simple.Style = {
                    horizontalAlign: 'left',
                    HorizontalAlign: 'left',
                    verticalAlign: 'bottom',
                    VerticalAlign: 'bottom'
                };

                const slow = cells.find(c => (c.row ?? c.Row) === 2 && (c.col ?? c.Col) === 2) || cells[1];
                slow.value = 'Slow';
                slow.Value = 'Slow';
                slow.style = slow.Style = {
                    bold: true,
                    Bold: true,
                    backgroundColor: '#e2e8f0',
                    BackgroundColor: '#e2e8f0',
                    horizontalAlign: 'left',
                    HorizontalAlign: 'left',
                    verticalAlign: 'bottom',
                    VerticalAlign: 'bottom'
                };

                // The demo seeds label text wider than its 64px columns, so every seeded cell would
                // take the clipping path and drown the unclipped counter the assert compares. Give
                // the remaining seeded cells short values too — the contract under test is that
                // fitting text draws without clipping.
                for (const c of cells) {
                    if (c === simple || c === slow) continue;
                    const v = c.value ?? c.Value;
                    if (v != null && String(v).length > 6) { c.value = 'ok'; c.Value = 'ok'; }
                }

                // Boot redraws already accumulated clipped counts for the original long labels
                // (metrics are cumulative on state, and the public render() entry does not bump
                // them), so reset the compared counters before the ArrowDown redraws below.
                if (state.metrics) {
                    state.metrics.clippedTextCount = 0;
                    state.metrics.unclippedTextCount = 0;
                }

                window.tmSpreadsheetCanvas.render(el, state.canvas, model);
            }");
        await grid.PressAsync("ArrowDown");
        await grid.PressAsync("ArrowDown");

        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        var keyboardInteractions = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).keyboardInteractions");
        var visibleCells = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).lastVisibleCellCount");
        var fastCells = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).fastCellPathCount");
        var slowCells = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).slowCellPathCount");
        var contextStateSkips = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).contextStateSkipCount");
        var clippedTexts = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).clippedTextCount");
        var unclippedTexts = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).unclippedTextCount");

        Assert.IsTrue(keyboardInteractions >= 2, $"Expected keyboard debug counter to increase. Count: {keyboardInteractions}.");
        Assert.IsTrue(visibleCells > 0, $"Expected debug metrics to report visible cells. Count: {visibleCells}.");
        Assert.IsTrue(fastCells > 0, $"Expected simple visible cells to use the canvas fast path. Count: {fastCells}.");
        Assert.IsTrue(slowCells > 0, $"Expected formatted/header cells to use the canvas slow path. Count: {slowCells}.");
        Assert.IsTrue(contextStateSkips > 0, $"Expected context state cache to skip redundant assignments. Count: {contextStateSkips}.");
        Assert.IsTrue(unclippedTexts > clippedTexts, $"Expected most regular cells to draw without clipping. Unclipped: {unclippedTexts}, clipped: {clippedTexts}.");
    }

    [TestMethod]
    public async Task CanvasRenderer_SelectionOnlyRedrawDoesNotMissContentSnapshots()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await grid.ClickAsync();

        var gridHandle = await grid.ElementHandleAsync();
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            gridHandle);

        await grid.EvaluateAsync(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                const cells = state.model.cells || state.model.Cells || [];
                for (let i = 0; i < Math.min(cells.length, 12); i++) {
                    const cell = cells[i];
                    cell.value = 'Snapshot ' + i;
                    cell.Value = 'Snapshot ' + i;
                    cell.style = cell.Style = {
                        horizontalAlign: 'left',
                        HorizontalAlign: 'left',
                        verticalAlign: 'bottom',
                        VerticalAlign: 'bottom'
                    };
                }
                window.tmSpreadsheetCanvas.render(el, state.canvas, state.model);
                requestAnimationFrame(() => requestAnimationFrame(resolve));
            })");
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).cellSnapshotMissCount > 0",
            gridHandle);

        var hitsBeforeSecondRender = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).cellSnapshotHitCount");
        await grid.EvaluateAsync(
            @"el => new Promise(resolve => {
                const state = el.__tmSpreadsheetCanvas;
                window.tmSpreadsheetCanvas.render(el, state.canvas, state.model);
                requestAnimationFrame(() => requestAnimationFrame(resolve));
            })");
        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).cellSnapshotHitCount > {hitsBeforeSecondRender}",
            gridHandle);

        var missesBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).cellSnapshotMissCount");
        var selectionFramesBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).selectionPaintFrameCount");

        await grid.PressAsync("ArrowRight");
        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).selectionPaintFrameCount > {selectionFramesBefore}",
            gridHandle);

        var missesAfter = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).cellSnapshotMissCount");

        Assert.AreEqual(missesBefore, missesAfter, "Expected selection-only keyboard movement to avoid content snapshot misses.");
    }

    [TestMethod]
    public async Task CanvasRenderer_UsesTextAndLayoutCaches()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator(".tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas?.getDebugMetrics?.(el)?.redrawCount > 0",
            await grid.ElementHandleAsync());

        await grid.EvaluateAsync(
            @"el => {
                const state = el.__tmSpreadsheetCanvas;
                const cells = state?.model?.cells || state?.model?.Cells || [];
                const cell = cells[0];
                if (!state || !cell) return;
                cell.value = 'Cache probe';
                cell.Value = 'Cache probe';
                const style = cell.style || cell.Style || {};
                style.fontSize = 11;
                style.FontSize = 11;
                style.fontFamily = 'Arial';
                style.FontFamily = 'Arial';
                style.foreColor = '#111827';
                style.ForeColor = '#111827';
                style.numberFormat = 'General';
                style.NumberFormat = 'General';
                cell.style = style;
                cell.Style = style;
                window.tmSpreadsheetCanvas.render(el, state.canvas, state.model);
            }");
        await page.WaitForFunctionAsync(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).lastTextCount > 0",
            await grid.ElementHandleAsync());

        var visibleRows = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).visibleRowCount");
        var visibleColumns = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).visibleColumnCount");
        var layoutMisses = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).visibleLayoutCacheMisses");
        var fontCacheSize = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).fontStringCacheSize");
        var paintCacheSize = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).paintStyleCacheSize");
        var displayCacheSize = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).displayValueCacheSize");
        var layoutHitsBefore = await grid.EvaluateAsync<int>(
            "el => window.tmSpreadsheetCanvas.getDebugMetrics(el).visibleLayoutCacheHits");

        await grid.ClickAsync();
        await grid.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            $"el => window.tmSpreadsheetCanvas.getDebugMetrics(el).visibleLayoutCacheHits > {layoutHitsBefore}",
            await grid.ElementHandleAsync());

        Assert.IsTrue(visibleRows > 0, $"Expected visible row layout cache to report rows. Count: {visibleRows}.");
        Assert.IsTrue(visibleColumns > 0, $"Expected visible column layout cache to report columns. Count: {visibleColumns}.");
        Assert.IsTrue(layoutMisses > 0, $"Expected visible layout cache to record at least one miss. Count: {layoutMisses}.");
        Assert.IsTrue(fontCacheSize > 0, $"Expected font string cache to fill. Size: {fontCacheSize}.");
        Assert.IsTrue(paintCacheSize > 0, $"Expected paint style cache to fill. Size: {paintCacheSize}.");
        Assert.IsTrue(displayCacheSize > 0, $"Expected display value cache to fill. Size: {displayCacheSize}.");
    }

    [TestMethod]
    public async Task BenchmarkPage_RunsCanvasBenchmark()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("spreadsheet-benchmark-run-canvas").ClickAsync();

        var result = page.GetByTestId("spreadsheet-benchmark-result-row").First;
        await result.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

        var text = await result.InnerTextAsync();
        Assert.IsTrue(text.Contains("Canvas"), $"Expected a canvas benchmark result row, got: {text}");
    }

    [TestMethod]
    public async Task BenchmarkPage_ExposesPasteLatencyForCanvasAndCanvasJsEngine()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("spreadsheet-benchmark-run-both").ClickAsync();
        await page.WaitForFunctionAsync(
            "document.querySelectorAll('[data-testid=\"spreadsheet-benchmark-result-row\"]').length >= 3",
            null,
            new PageWaitForFunctionOptions { Timeout = 90000 });

        var canvas = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"Canvas\"]").First;
        var canvasJsEngine = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"CanvasJsEngine\"]").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await canvasJsEngine.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var canvasPasteMs = await GetBenchmarkMetricAsync(canvas, "data-paste-ms");
        var canvasJsEnginePasteMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-paste-ms");

        Assert.IsTrue(canvasPasteMs > 0, $"Expected canvas benchmark to expose paste latency. Current: {canvasPasteMs:N1} ms.");
        Assert.IsTrue(canvasJsEnginePasteMs > 0, $"Expected canvas JS engine benchmark to expose paste latency. Current: {canvasJsEnginePasteMs:N1} ms.");
    }

    [TestMethod]
    public async Task BenchmarkPage_Phase11ReadinessMetricsPass()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("spreadsheet-benchmark-run-both").ClickAsync();
        await page.WaitForFunctionAsync(
            "document.querySelectorAll('[data-testid=\"spreadsheet-benchmark-result-row\"]').length >= 2",
            null,
            new PageWaitForFunctionOptions { Timeout = 60000 });

        var canvas = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"Canvas\"]").First;
        var dom = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"Dom\"]").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await dom.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var canvasDown = await GetBenchmarkMetricAsync(canvas, "data-keyboard-scroll-down-ms");
        var canvasUp = await GetBenchmarkMetricAsync(canvas, "data-keyboard-scroll-up-ms");
        var canvasRight = await GetBenchmarkMetricAsync(canvas, "data-keyboard-scroll-right-ms");
        var canvasLogicalKeyboard = await GetBenchmarkMetricAsync(canvas, "data-keyboard-logical-scroll-count");
        var canvasKeyboardScrollTo = await GetBenchmarkMetricAsync(canvas, "data-keyboard-scroll-to-count");
        var canvasWheelEvents = await GetBenchmarkMetricAsync(canvas, "data-wheel-events");
        var canvasWheelPrevented = await GetBenchmarkMetricAsync(canvas, "data-wheel-prevented");
        var canvasWheelLogical = await GetBenchmarkMetricAsync(canvas, "data-wheel-logical-scroll-count");
        var canvasWheelViewportCallbacks = await GetBenchmarkMetricAsync(canvas, "data-wheel-viewport-callback-count");
        var canvasWheelScrollTo = await GetBenchmarkMetricAsync(canvas, "data-wheel-scroll-to-count");
        var canvasWheelPaintFrames = await GetBenchmarkMetricAsync(canvas, "data-wheel-paint-frame-count");
        var canvasWheelContentFrames = await GetBenchmarkMetricAsync(canvas, "data-wheel-content-paint-frame-count");
        var canvasDragFrames = await GetBenchmarkMetricAsync(canvas, "data-drag-frames");
        var canvasDragLogical = await GetBenchmarkMetricAsync(canvas, "data-drag-logical-scroll-count");
        var canvasDragViewportCallbacks = await GetBenchmarkMetricAsync(canvas, "data-drag-viewport-callback-count");
        var canvasDragSelectionCallbacks = await GetBenchmarkMetricAsync(canvas, "data-drag-selection-callback-count");
        var canvasDragScrollTo = await GetBenchmarkMetricAsync(canvas, "data-drag-scroll-to-count");
        var domDown = await GetBenchmarkMetricAsync(dom, "data-keyboard-scroll-down-ms");

        const double phase10KeyboardEdgeBaselineMs = 612.2;

        Assert.IsTrue(canvasDown < phase10KeyboardEdgeBaselineMs, $"Expected ArrowDown edge navigation to stay below phase 10 baseline. Current: {canvasDown:N1} ms.");
        Assert.IsTrue(canvasUp < phase10KeyboardEdgeBaselineMs, $"Expected ArrowUp edge navigation to stay below phase 10 baseline. Current: {canvasUp:N1} ms.");
        Assert.IsTrue(canvasRight < phase10KeyboardEdgeBaselineMs, $"Expected ArrowRight edge navigation to stay below phase 10 baseline. Current: {canvasRight:N1} ms.");
        Assert.IsTrue(canvasDown <= domDown * 1.25, $"Expected canvas ArrowDown edge navigation to be comparable to DOM on 1,000 x 50. Canvas: {canvasDown:N1} ms, DOM: {domDown:N1} ms.");
        Assert.IsTrue(canvasLogicalKeyboard > 0, $"Expected keyboard edge navigation to use logical scroll. Count: {canvasLogicalKeyboard:N0}.");
        Assert.AreEqual(0d, canvasKeyboardScrollTo, $"Keyboard hot path should not call root.scrollTo per key. Count: {canvasKeyboardScrollTo:N0}.");
        Assert.IsTrue(canvasWheelEvents > 0, "Expected benchmark to exercise wheel events.");
        Assert.AreEqual(canvasWheelEvents, canvasWheelPrevented, $"Canvas wheel benchmark should prevent native wheel scroll. Events: {canvasWheelEvents:N0}, prevented: {canvasWheelPrevented:N0}.");
        Assert.IsTrue(canvasWheelLogical > 0, $"Expected wheel scroll to use logical scroll. Count: {canvasWheelLogical:N0}.");
        Assert.IsTrue(canvasWheelViewportCallbacks <= 2, $"Expected wheel viewport callbacks to stay coalesced. Count: {canvasWheelViewportCallbacks:N0}.");
        Assert.IsTrue(canvasWheelScrollTo <= 2, $"Expected wheel native scrollbar sync to stay coalesced. scrollTo count: {canvasWheelScrollTo:N0}.");
        Assert.IsTrue(canvasWheelPaintFrames <= canvasWheelEvents, $"Expected wheel paints not to exceed wheel events. Paint frames: {canvasWheelPaintFrames:N0}, wheel events: {canvasWheelEvents:N0}.");
        Assert.IsTrue(canvasWheelContentFrames <= canvasWheelPaintFrames, $"Expected wheel content paints to be bounded by paint frames. Content: {canvasWheelContentFrames:N0}, frames: {canvasWheelPaintFrames:N0}.");
        Assert.IsTrue(canvasDragFrames > 0, $"Expected drag autoscroll to produce frames. Count: {canvasDragFrames:N0}.");
        Assert.IsTrue(canvasDragLogical > 0, $"Expected drag autoscroll to use logical pointer scroll. Count: {canvasDragLogical:N0}.");
        Assert.IsTrue(canvasDragViewportCallbacks <= 2, $"Expected drag viewport callbacks to stay coalesced. Count: {canvasDragViewportCallbacks:N0}.");
        Assert.IsTrue(canvasDragSelectionCallbacks <= 2, $"Expected drag selection callbacks to stay coalesced. Count: {canvasDragSelectionCallbacks:N0}.");
        Assert.IsTrue(canvasDragScrollTo <= 2, $"Expected drag autoscroll native sync to stay coalesced. scrollTo count: {canvasDragScrollTo:N0}.");
    }

    [TestMethod]
    public async Task BenchmarkPage_CanvasKeyboardUsableOnLargeDataset()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.Locator("#benchmark-dataset").SelectOptionAsync("10000x100");
        await page.GetByTestId("spreadsheet-benchmark-run-canvas").ClickAsync();

        var canvasJsEngine = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"CanvasJsEngine\"]").First;
        await canvasJsEngine.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });

        var canvasDown = await GetBenchmarkMetricAsync(canvasJsEngine, "data-keyboard-scroll-down-ms");
        var canvasUp = await GetBenchmarkMetricAsync(canvasJsEngine, "data-keyboard-scroll-up-ms");
        var canvasRight = await GetBenchmarkMetricAsync(canvasJsEngine, "data-keyboard-scroll-right-ms");
        var canvasLogicalKeyboard = await GetBenchmarkMetricAsync(canvasJsEngine, "data-keyboard-logical-scroll-count");
        var canvasKeyboardScrollTo = await GetBenchmarkMetricAsync(canvasJsEngine, "data-keyboard-scroll-to-count");

        const double phase10KeyboardEdgeBaselineMs = 612.2;

        Assert.IsTrue(canvasDown < phase10KeyboardEdgeBaselineMs, $"Expected 10,000 x 100 ArrowDown edge navigation to stay usable. Current: {canvasDown:N1} ms.");
        Assert.IsTrue(canvasUp < phase10KeyboardEdgeBaselineMs, $"Expected 10,000 x 100 ArrowUp edge navigation to stay usable. Current: {canvasUp:N1} ms.");
        Assert.IsTrue(canvasRight < phase10KeyboardEdgeBaselineMs, $"Expected 10,000 x 100 ArrowRight edge navigation to stay usable. Current: {canvasRight:N1} ms.");
        Assert.IsTrue(canvasLogicalKeyboard > 0, $"Expected large dataset keyboard navigation to use logical scroll. Count: {canvasLogicalKeyboard:N0}.");
        Assert.AreEqual(0d, canvasKeyboardScrollTo, $"Large dataset keyboard hot path should not call root.scrollTo per key. Count: {canvasKeyboardScrollTo:N0}.");
    }

    [TestMethod]
    public async Task BenchmarkPage_CanvasResizeUsableOnLargeDataset()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.Locator("#benchmark-dataset").SelectOptionAsync("10000x100");
        await page.GetByTestId("spreadsheet-benchmark-load").ClickAsync();

        var grid = page.Locator("[data-spreadsheet-benchmark-surface] .tm-spreadsheet-canvas-grid").First;
        await WaitForCanvasGridReadyAsync(page, grid);

        var result = await grid.EvaluateAsync<CanvasLargeDatasetResizeProbeResult>(
            @"el => new Promise(resolve => {
                const before = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                const state = el.__tmSpreadsheetCanvas;
                const model = state.model;
                const rows = model.Rows || model.rows || [];
                const columns = model.Columns || model.columns || [];
                const row = rows[2] || rows[1] || rows[0];
                const column = columns[1] || columns[0];
                const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
                const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;
                const rect = el.getBoundingClientRect();

                const dispatch = (type, x, y, buttons, pointerId) => el.dispatchEvent(new PointerEvent(type, {
                    clientX: x,
                    clientY: y,
                    button: 0,
                    buttons,
                    pointerId,
                    pointerType: 'mouse',
                    bubbles: true,
                    cancelable: true
                }));

                const dragColumn = () => new Promise(done => {
                    const left = column.Left ?? column.left ?? 0;
                    const size = column.Width ?? column.width ?? 64;
                    const startX = rect.left + rowHeaderWidth + left + size - 1;
                    const startY = rect.top + Math.max(6, columnHeaderHeight / 2);
                    const endX = startX + 42;
                    const pointerId = 77;
                    let move = 0;
                    const moveCount = 8;
                    dispatch('pointerdown', startX, startY, 1, pointerId);
                    const step = () => {
                        move += 1;
                        dispatch('pointermove', startX + (endX - startX) * move / moveCount, startY, 1, pointerId);
                        if (move < moveCount) {
                            requestAnimationFrame(step);
                            return;
                        }

                        dispatch('pointerup', endX, startY, 0, pointerId);
                        setTimeout(() => {
                            const synced = (state.model.Columns || state.model.columns || [])[1] || (state.model.Columns || state.model.columns || [])[0];
                            done(synced?.Width ?? synced?.width ?? 0);
                        }, 260);
                    };

                    requestAnimationFrame(step);
                });

                const dragRow = () => new Promise(done => {
                    const top = row.Top ?? row.top ?? 0;
                    const size = row.Height ?? row.height ?? 20;
                    const startX = rect.left + Math.max(8, rowHeaderWidth / 2);
                    const startY = rect.top + columnHeaderHeight + top + size - 1;
                    const endY = startY + 18;
                    const pointerId = 78;
                    let move = 0;
                    const moveCount = 8;
                    dispatch('pointerdown', startX, startY, 1, pointerId);
                    const step = () => {
                        move += 1;
                        dispatch('pointermove', startX, startY + (endY - startY) * move / moveCount, 1, pointerId);
                        if (move < moveCount) {
                            requestAnimationFrame(step);
                            return;
                        }

                        dispatch('pointerup', startX, endY, 0, pointerId);
                        setTimeout(() => {
                            const synced = (state.model.Rows || state.model.rows || [])[2] || (state.model.Rows || state.model.rows || [])[1] || (state.model.Rows || state.model.rows || [])[0];
                            done(synced?.Height ?? synced?.height ?? 0);
                        }, 260);
                    };

                    requestAnimationFrame(step);
                });

                (async () => {
                    const initialColumnSize = column.Width ?? column.width ?? 64;
                    const initialRowSize = row.Height ?? row.height ?? 20;
                    const finalColumnSize = await dragColumn();
                    const finalRowSize = await dragRow();
                    const after = window.tmSpreadsheetCanvas.getDebugMetrics(el);
                    resolve({
                        initialColumnSize,
                        finalColumnSize,
                        initialRowSize,
                        finalRowSize,
                        dotNetBeforeCommit: (before.resizeDotNetCallbackCount || 0),
                        dotNetAfterCommit: (after.resizeDotNetCallbackCount || 0),
                        contentPaintFrames: (after.contentPaintFrameCount || 0) - (before.contentPaintFrameCount || 0)
                    });
                })();
            })");

        Assert.IsTrue(result.FinalColumnSize > result.InitialColumnSize, $"Expected 10,000 x 100 column resize to stay usable. Initial: {result.InitialColumnSize:N1}, final: {result.FinalColumnSize:N1}.");
        Assert.IsTrue(result.FinalRowSize > result.InitialRowSize, $"Expected 10,000 x 100 row resize to stay usable. Initial: {result.InitialRowSize:N1}, final: {result.FinalRowSize:N1}.");
        Assert.IsTrue(result.DotNetAfterCommit - result.DotNetBeforeCommit <= 2, $"Expected resize on 10,000 x 100 to avoid .NET callback spam. Delta: {result.DotNetAfterCommit - result.DotNetBeforeCommit:N0}.");
        Assert.IsTrue(result.ContentPaintFrames > 0, "Expected large-dataset resize commit to repaint the content layer at least once.");
    }

    [TestMethod]
    public async Task BenchmarkPage_Phase12LatencyProbeMatchesHotPathCriteria()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        var grid = page.Locator("[data-spreadsheet-benchmark-surface] .tm-spreadsheet-canvas-grid").First;
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "selector => !!window.tmSpreadsheetBenchmark?.runPhase12LatencyProbe && !!document.querySelector(selector)?.__tmSpreadsheetCanvas?.model",
            "[data-spreadsheet-benchmark-surface] .tm-spreadsheet-canvas-grid",
            new PageWaitForFunctionOptions { Timeout = 30000 });

        var probe = await page.EvaluateAsync<CanvasPhase12LatencyProbeResult>(
            "selector => window.tmSpreadsheetBenchmark.runPhase12LatencyProbe(selector)",
            "[data-spreadsheet-benchmark-surface] .tm-spreadsheet-canvas-grid");

        AssertPhase12Probe("ArrowDown viewport", probe.ArrowDownViewport);
        AssertPhase12Probe("ArrowDown scroll edge", probe.ArrowDownScrollEdge);
        AssertPhase12Probe("normal click", probe.NormalCellClick);
        AssertPhase12Probe("formula click", probe.FormulaCellClick);
        AssertPhase12Probe("typing character", probe.TypingCharacter);
        AssertPhase12Probe("formula commit", probe.FormulaCommit);

        Assert.AreEqual(0, probe.ArrowDownViewport.FirstFrameDebug.DotNetCallbackCount, "ArrowDown inside viewport should stay JS-only on the first frame.");
        Assert.AreEqual(0, probe.ArrowDownViewport.FirstFrameDebug.ContentPaintFrameCount, "ArrowDown inside viewport should not repaint content on the first frame.");
        Assert.IsTrue(probe.ArrowDownViewport.FirstFrameDebug.SelectionPaintFrameCount > 0, "ArrowDown inside viewport should repaint the selection layer.");
        Assert.AreEqual(0, probe.FormulaCellClick.FirstFrameDebug.DotNetCallbackCount, "Formula point click should stay JS-only before commit.");
        Assert.AreEqual(0, probe.TypingCharacter.FirstFrameDebug.DotNetCallbackCount, "Typing should stay JS-only on the hot path.");
        Assert.AreEqual(0, probe.TypingCharacter.FirstFrameDebug.BlazorFrameCount, "Typing should not trigger a Blazor frame per key.");
        Assert.IsTrue(probe.FormulaCommit.SettledDebug.DotNetCallbackCount > 0, "Formula commit should still synchronize back to .NET after the local hot path.");
    }

    [TestMethod]
    public async Task BenchmarkPage_Phase12BenchmarkRowExposesReadinessMetrics()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("spreadsheet-benchmark-run-both").ClickAsync();
        await page.WaitForFunctionAsync(
            "document.querySelectorAll('[data-testid=\"spreadsheet-benchmark-result-row\"]').length >= 3",
            null,
            new PageWaitForFunctionOptions { Timeout = 90000 });

        var canvas = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"Canvas\"]").First;
        var canvasJsEngine = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"CanvasJsEngine\"]").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await canvasJsEngine.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var legacyViewportArrowMs = await GetBenchmarkMetricAsync(canvas, "data-single-arrow-in-viewport-ms");
        var viewportArrowMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-single-arrow-in-viewport-ms");
        var viewportArrowDotNet = await GetBenchmarkMetricAsync(canvasJsEngine, "data-single-arrow-in-viewport-dotnet-callbacks");
        var viewportArrowContentFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-single-arrow-in-viewport-content-paint-frames");
        var viewportArrowSelectionFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-single-arrow-in-viewport-selection-paint-frames");
        var scrollEdgeMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-single-arrow-scroll-edge-ms");
        var formulaClickMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-formula-cell-click-latency-ms");
        var formulaClickDotNet = await GetBenchmarkMetricAsync(canvasJsEngine, "data-formula-cell-click-dotnet-callbacks");
        var typingMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-typing-latency-ms");
        var typingDotNet = await GetBenchmarkMetricAsync(canvasJsEngine, "data-typing-dotnet-callbacks");
        var typingBlazorFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-typing-blazor-frame-count");
        var callbacksPerInteraction = await GetBenchmarkMetricAsync(canvasJsEngine, "data-dotnet-callbacks-per-interaction");
        var wheelEvents = await GetBenchmarkMetricAsync(canvasJsEngine, "data-wheel-events");
        var wheelBlazorFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-wheel-blazor-frame-count");
        var dragFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-drag-frames");
        var dragBlazorFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-drag-blazor-frame-count");

        Assert.IsTrue(viewportArrowMs > 0, $"Expected benchmark row to expose viewport ArrowDown latency. Current: {viewportArrowMs:N1} ms.");
        Assert.AreEqual(0d, viewportArrowDotNet, $"Viewport ArrowDown should stay JS-only on the first frame. .NET callbacks: {viewportArrowDotNet:N0}.");
        Assert.AreEqual(0d, viewportArrowContentFrames, $"Viewport ArrowDown should not repaint content on the first frame. Content frames: {viewportArrowContentFrames:N0}.");
        Assert.IsTrue(viewportArrowSelectionFrames > 0, $"Viewport ArrowDown should repaint the selection layer. Selection frames: {viewportArrowSelectionFrames:N0}.");
        Assert.IsTrue(scrollEdgeMs > 0, $"Expected benchmark row to expose scroll-edge ArrowDown latency. Current: {scrollEdgeMs:N1} ms.");
        Assert.IsTrue(formulaClickMs > 0, $"Expected benchmark row to expose formula click latency. Current: {formulaClickMs:N1} ms.");
        Assert.AreEqual(0d, formulaClickDotNet, $"Formula click should stay JS-only before commit. .NET callbacks: {formulaClickDotNet:N0}.");
        Assert.IsTrue(typingMs > 0, $"Expected benchmark row to expose typing latency. Current: {typingMs:N1} ms.");
        Assert.AreEqual(0d, typingDotNet, $"Typing should stay JS-only on the first frame. .NET callbacks: {typingDotNet:N0}.");
        Assert.AreEqual(0d, typingBlazorFrames, $"Typing should not trigger a Blazor frame per key. Frames: {typingBlazorFrames:N0}.");
        Assert.IsTrue(wheelEvents > 0, "Expected benchmark row to expose wheel activity.");
        Assert.IsTrue(wheelBlazorFrames < wheelEvents, $"Wheel scroll should not trigger a Blazor frame per event. Frames: {wheelBlazorFrames:N0}, events: {wheelEvents:N0}.");
        Assert.IsTrue(dragFrames > 0, "Expected benchmark row to expose drag autoscroll frames.");
        Assert.IsTrue(dragBlazorFrames < dragFrames, $"Drag selection should not trigger a Blazor frame per move. Frames: {dragBlazorFrames:N0}, drag frames: {dragFrames:N0}.");
        Assert.IsTrue(callbacksPerInteraction <= 0.5, $"Expected average .NET callbacks per interaction to stay low. Current: {callbacksPerInteraction:N2}.");
        Assert.IsTrue(viewportArrowMs <= legacyViewportArrowMs * 1.1, $"Expected CanvasJsEngine viewport ArrowDown to stay comparable with legacy Canvas. JS engine: {viewportArrowMs:N1} ms, Canvas: {legacyViewportArrowMs:N1} ms.");
    }

    [TestMethod]
    public async Task BenchmarkPage_ResizeReadinessMetricsPass()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet-benchmark");
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("spreadsheet-benchmark-run-canvas").ClickAsync();

        var canvasJsEngine = page.Locator("[data-testid=\"spreadsheet-benchmark-result-row\"][data-renderer=\"CanvasJsEngine\"]").First;
        await canvasJsEngine.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 90000 });

        var columnResizeMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-column-resize-drag-ms");
        var columnResizeMoves = await GetBenchmarkMetricAsync(canvasJsEngine, "data-column-resize-pointer-moves");
        var columnResizeDotNet = await GetBenchmarkMetricAsync(canvasJsEngine, "data-column-resize-dotnet-callbacks");
        var columnResizeBlazorFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-column-resize-blazor-frame-count");
        var columnResizePaintFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-column-resize-paint-frame-count");
        var rowResizeMs = await GetBenchmarkMetricAsync(canvasJsEngine, "data-row-resize-drag-ms");
        var rowResizeMoves = await GetBenchmarkMetricAsync(canvasJsEngine, "data-row-resize-pointer-moves");
        var rowResizeDotNet = await GetBenchmarkMetricAsync(canvasJsEngine, "data-row-resize-dotnet-callbacks");
        var rowResizeBlazorFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-row-resize-blazor-frame-count");
        var rowResizePaintFrames = await GetBenchmarkMetricAsync(canvasJsEngine, "data-row-resize-paint-frame-count");

        Assert.IsTrue(columnResizeMs > 0, $"Expected benchmark row to expose column resize latency. Current: {columnResizeMs:N1} ms.");
        Assert.IsTrue(columnResizeMoves >= 6, $"Expected benchmark row to record several column resize pointer moves. Count: {columnResizeMoves:N0}.");
        Assert.IsTrue(columnResizeDotNet < columnResizeMoves, $"Column resize should not call .NET per move. Callbacks: {columnResizeDotNet:N0}, moves: {columnResizeMoves:N0}.");
        Assert.IsTrue(columnResizeBlazorFrames < columnResizeMoves, $"Column resize should not trigger a Blazor frame per move. Frames: {columnResizeBlazorFrames:N0}, moves: {columnResizeMoves:N0}.");
        Assert.IsTrue(rowResizeMs > 0, $"Expected benchmark row to expose row resize latency. Current: {rowResizeMs:N1} ms.");
        Assert.IsTrue(rowResizeMoves >= 6, $"Expected benchmark row to record several row resize pointer moves. Count: {rowResizeMoves:N0}.");
        Assert.IsTrue(rowResizeDotNet < rowResizeMoves, $"Row resize should not call .NET per move. Callbacks: {rowResizeDotNet:N0}, moves: {rowResizeMoves:N0}.");
        Assert.IsTrue(rowResizeBlazorFrames < rowResizeMoves, $"Row resize should not trigger a Blazor frame per move. Frames: {rowResizeBlazorFrames:N0}, moves: {rowResizeMoves:N0}.");
        Assert.IsTrue(columnResizePaintFrames >= 0, "Expected benchmark row to expose column resize paint frame metric.");
        Assert.IsTrue(rowResizePaintFrames >= 0, "Expected benchmark row to expose row resize paint frame metric.");
    }

    private static int ParseRow(string cellRef)
    {
        var digits = new string(cellRef.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var row) ? row : 0;
    }

    private static async Task WaitForCanvasGridReadyAsync(IPage page, ILocator grid)
    {
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            @"el => !!window.tmSpreadsheetCanvas
                && typeof window.tmSpreadsheetCanvas.setCells === 'function'
                && !!el
                && !!el.__tmSpreadsheetCanvas
                && !!el.__tmSpreadsheetCanvas.model
                && !!el.__tmSpreadsheetCanvas.sheetState?.activeCell
                && !!el.querySelector('.tm-spreadsheet-canvas-grid__canvas--content')
                && el.querySelector('.tm-spreadsheet-canvas-grid__canvas--content').width > 0
                && el.querySelector('.tm-spreadsheet-canvas-grid__canvas--content').height > 0",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 30000 });
    }

    private static async Task EditCanvasCellAsync(IPage page, ILocator grid, string cellRef, string value)
    {
        var point = await GetCanvasCellCenterAsync(grid, cellRef);
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = point.X, Y = point.Y }
        });
        await WaitForCanvasActiveRefAsync(grid, cellRef);

        var firstKey = value[..1];
        await grid.PressAsync(firstKey);

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        if (value.Length > 1)
            await page.Keyboard.TypeAsync(value[1..]);

        await page.Keyboard.PressAsync("Enter");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    private static async Task<CanvasCellPointResult> GetCanvasCellCenterAsync(ILocator grid, string cellRef)
    {
        var row = ParseRow(cellRef) - 1;
        var col = ParseColumn(cellRef) - 1;
        return await grid.EvaluateAsync<CanvasCellPointResult>(
            @"(el, args) => {
                const row = Number(args?.row ?? 0);
                const col = Number(args?.col ?? 0);
                const key = `${row}:${col}`;
                const state = el.__tmSpreadsheetCanvas;
                const model = state?.model || {};
                const cell = state?.sheetState?.cellStore?.cells?.get(key);
                if (!cell) {
                    return { x: -1, y: -1 };
                }

                const left = Number(cell.left ?? cell.Left ?? 0);
                const top = Number(cell.top ?? cell.Top ?? 0);
                const width = Number(cell.width ?? cell.Width ?? 0);
                const height = Number(cell.height ?? cell.Height ?? 0);
                const rowHeaderWidth = Number(model.rowHeaderWidth ?? model.RowHeaderWidth ?? 40);
                const columnHeaderHeight = Number(model.columnHeaderHeight ?? model.ColumnHeaderHeight ?? 20);
                const scrollLeft = Number(model.scrollLeft ?? model.ScrollLeft ?? 0);
                const scrollTop = Number(model.scrollTop ?? model.ScrollTop ?? 0);
                return {
                    x: Math.round(rowHeaderWidth + left - scrollLeft + width / 2),
                    y: Math.round(columnHeaderHeight + top - scrollTop + height / 2)
                };
            }",
            new { row, col });
    }

    private static Task<CanvasCellSnapshotResult> ReadCanvasCellSnapshotAsync(ILocator grid, string cellRef)
    {
        var row = ParseRow(cellRef) - 1;
        var col = ParseColumn(cellRef) - 1;
        return grid.EvaluateAsync<CanvasCellSnapshotResult>(
            @"(el, args) => {
                const key = `${Number(args?.row ?? 0)}:${Number(args?.col ?? 0)}`;
                const cell = el.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells?.get(key);
                return {
                    activeRef: el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || '',
                    value: String(cell?.value ?? cell?.Value ?? ''),
                    formula: String(cell?.formula ?? cell?.Formula ?? '')
                };
            }",
            new { row, col });
    }

    private static async Task<CanvasCellSnapshotResult> WaitForCanvasCellSnapshotAsync(
        ILocator grid,
        string cellRef,
        Func<CanvasCellSnapshotResult, bool> predicate,
        string failureMessage)
    {
        CanvasCellSnapshotResult? snapshot = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            snapshot = await ReadCanvasCellSnapshotAsync(grid, cellRef);
            if (predicate(snapshot))
                return snapshot;

            await Task.Delay(100);
        }

        Assert.Fail($"{failureMessage} Last snapshot for {cellRef}: value '{snapshot?.Value}', formula '{snapshot?.Formula}', active '{snapshot?.ActiveRef}'.");
        return snapshot!;
    }

    private static async Task WaitForCanvasActiveRefAsync(ILocator grid, string expectedRef)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (string.Equals(await GetCanvasActiveRefAsync(grid), expectedRef, StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(50);
        }

        var actualRef = await GetCanvasActiveRefAsync(grid);
        Assert.Fail($"Expected active cell {expectedRef}, but got {actualRef}.");
    }

    private static async Task<ILocator> OpenCanvasFormulaEditorAsync(IPage page, ILocator grid, string formula)
    {
        await grid.ClickAsync();
        await grid.PressAsync("=");

        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await editor.FillAsync(formula);
        await SetFormulaEditorSelectionAsync(editor, formula.Length, formula.Length);
        return editor;
    }

    private static async Task<ILocator> OpenFormulaBarEditorAsync(IPage page, ILocator? spreadsheet = null)
    {
        var root = spreadsheet ?? page.Locator(".tm-spreadsheet").Filter(new() { Has = page.Locator(".tm-spreadsheet-canvas-grid") }).First;
        var display = root.Locator(".tm-spreadsheet-formula-bar__display");
        await display.ClickAsync();
        var input = root.Locator("[data-testid='tm-spreadsheet-formula-bar-input']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        return input;
    }

    private static async Task SetFormulaBarSelectionAsync(ILocator input, int start, int end)
    {
        await input.EvaluateAsync(
            @"(el, args) => {
                el.focus();
                el.setSelectionRange(Number(args.start), Number(args.end));
                el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
                el.dispatchEvent(new KeyboardEvent('keyup', {
                    key: 'ArrowRight',
                    bubbles: true,
                    cancelable: true
                }));
            }",
            new { start, end });
    }

    private static async Task SetFormulaEditorSelectionAsync(ILocator editor, int start, int end)
    {
        await editor.EvaluateAsync(
            @"(el, args) => {
                el.focus();
                el.setSelectionRange(Number(args.start), Number(args.end));
                el.dispatchEvent(new KeyboardEvent('keyup', {
                    key: 'ArrowRight',
                    bubbles: true,
                    cancelable: true
                }));
            }",
            new { start, end });
    }

    private static Task<CanvasFormulaCaretProbeResult> ReadTextInputSelectionAsync(ILocator input) =>
        input.EvaluateAsync<CanvasFormulaCaretProbeResult>(
            @"el => ({
                selectionStart: el.selectionStart ?? -1,
                selectionEnd: el.selectionEnd ?? -1,
                editorValue: el.value || ''
            })");

    private static async Task DragCanvasBetweenCellsAsync(ILocator grid, string startCellRef, string endCellRef, int pointerId = 121)
    {
        var start = await GetCanvasCellCenterAsync(grid, startCellRef);
        var end = await GetCanvasCellCenterAsync(grid, endCellRef);

        await grid.EvaluateAsync(
            @"(el, args) => {
                const rect = el.getBoundingClientRect();
                const dispatch = (type, x, y, buttons) => el.dispatchEvent(new PointerEvent(type, {
                    pointerId: Number(args.pointerId),
                    pointerType: 'mouse',
                    clientX: rect.left + Number(x),
                    clientY: rect.top + Number(y),
                    button: 0,
                    buttons,
                    bubbles: true,
                    cancelable: true
                }));

                dispatch('pointerdown', args.startX, args.startY, 1);
                dispatch('pointermove', args.midX, args.midY, 1);
                dispatch('pointermove', args.endX, args.endY, 1);
                dispatch('pointerup', args.endX, args.endY, 0);
            }",
            new
            {
                pointerId,
                startX = start.X,
                startY = start.Y,
                midX = Math.Round((start.X + end.X) / 2.0),
                midY = Math.Round((start.Y + end.Y) / 2.0),
                endX = end.X,
                endY = end.Y
            });
    }

    private static async Task<string[]> ReadFormulaEditorCycleAsync(IPage page, ILocator editor)
    {
        var states = new List<string>
        {
            await editor.InputValueAsync()
        };

        for (var i = 0; i < 4; i++)
        {
            await page.Keyboard.PressAsync("F4");
            states.Add(await editor.InputValueAsync());
        }

        return states.ToArray();
    }

    private static void AssertPhase12Probe(string name, CanvasPhase12InteractionProbe probe)
    {
        Assert.IsTrue(probe.FirstFrameMs > 0, $"{name} should report first-frame latency.");
        Assert.IsTrue(probe.SettledMs >= probe.FirstFrameMs, $"{name} settled latency should include first-frame latency.");
        Assert.IsNotNull(probe.FirstFrameDebug, $"{name} should expose first-frame debug counters.");
        Assert.IsNotNull(probe.SettledDebug, $"{name} should expose settled debug counters.");
        Assert.IsNotNull(probe.FirstFrameDebug.DotNetCallbacksByMethod, $"{name} should expose first-frame .NET callback methods.");
        Assert.IsNotNull(probe.SettledDebug.DotNetCallbacksByMethod, $"{name} should expose settled .NET callback methods.");
    }

    private static async Task<double> GetBenchmarkMetricAsync(ILocator row, string attributeName)
    {
        var value = await row.GetAttributeAsync(attributeName);
        Assert.IsNotNull(value, $"Expected benchmark row to expose {attributeName}.");
        return double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static Task<string> GetCanvasActiveRefAsync(ILocator grid) =>
        grid.EvaluateAsync<string>(
            "el => el.__tmSpreadsheetCanvas?.model?.activeCellRef || el.__tmSpreadsheetCanvas?.model?.ActiveCellRef || ''");

    private static int ParseColumn(string cellRef)
    {
        var letters = new string(cellRef.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        var column = 0;
        foreach (var letter in letters)
        {
            column = column * 26 + letter - 'A' + 1;
        }

        return column;
    }

    private static string ToCellRef(int rowNumber, int columnNumber)
    {
        var column = Math.Max(1, columnNumber);
        var letters = string.Empty;
        while (column > 0)
        {
            column -= 1;
            letters = (char)('A' + (column % 26)) + letters;
            column /= 26;
        }

        return $"{letters}{Math.Max(1, rowNumber)}";
    }

    private sealed class CanvasFormattingProbeResult
    {
        public int DarkTextPixels { get; set; }
        public int UnderlinePixels { get; set; }
        public int BorderPixels { get; set; }
        public string FontCache { get; set; } = string.Empty;
    }

    private sealed class CanvasCellPointResult
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class CanvasClickSyncProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public string FormulaBarRef { get; set; } = string.Empty;
        public int CommandLogCallbacks { get; set; }
        public int CellPointerCallbacks { get; set; }
    }

    private sealed class CanvasCellSnapshotResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;
    }

    private sealed class CanvasFormulaCaretProbeResult
    {
        public string EditorValue { get; set; } = string.Empty;
        public string ActiveRef { get; set; } = string.Empty;
        public string StartRef { get; set; } = string.Empty;
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public int ActiveTokenIndex { get; set; }
        public int StartActiveTokenIndex { get; set; }
        public int TokenReplaceCount { get; set; }
        public int IgnoredSelfClickCount { get; set; }
        public int ArrowCaretCount { get; set; }
        public int SelectionPaintFrames { get; set; }
        public int ContentPaintFrames { get; set; }
        public int CaretMoveCount { get; set; }
        public bool FormulaActive { get; set; }
    }

    private sealed class CanvasFormulaResizeSessionProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public string HostText { get; set; } = string.Empty;
        public int HostSelectionStart { get; set; }
        public int HostSelectionEnd { get; set; }
        public double FinalSize { get; set; }
        public int DotNetCallbacks { get; set; }
        public int BlazorFrames { get; set; }
    }

    private sealed class CanvasFormulaSessionCleanupProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public bool FormulaActive { get; set; }
        public int HighlightedCells { get; set; }
        public bool HostFormulaPointMode { get; set; }
        public string HostSessionOwner { get; set; } = string.Empty;
        public string HostSessionText { get; set; } = string.Empty;
    }

    private sealed class CanvasJsFirstStateProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public string SheetActiveRef { get; set; } = string.Empty;
        public string EditorValue { get; set; } = string.Empty;
        public string SheetEditorValue { get; set; } = string.Empty;
        public int LocalRevision { get; set; }
        public int ServerRevision { get; set; }
        public int StaleFramesIgnored { get; set; }
    }

    private sealed class CanvasJsEditorProbeResult
    {
        public string StartRef { get; set; } = string.Empty;
        public string ActiveRef { get; set; } = string.Empty;
        public string EditorValue { get; set; } = string.Empty;
        public string CommittedValue { get; set; } = string.Empty;
        public int EditorOpenCount { get; set; }
        public int EditorLocalCommits { get; set; }
        public int KeyCommandCallbacks { get; set; }
        public int EditBatchCallbacks { get; set; }
        public int EditBatchItems { get; set; }
        public int DotNetCallbackMethodCount { get; set; }
    }

    private sealed class CanvasJsFormulaEditorProbeResult
    {
        public string EditorValue { get; set; } = string.Empty;
        public bool FormulaActive { get; set; }
        public int FormulaRefCount { get; set; }
        public int FormulaClickInserts { get; set; }
        public int FormulaRangeDrags { get; set; }
        public int HighlightedCells { get; set; }
        public int KeyCommandCallbacks { get; set; }
        public int CellPointerCallbacks { get; set; }
        public string CommittedValue { get; set; } = string.Empty;
        public string CommittedFormula { get; set; } = string.Empty;
        public int EditBatchCallbacks { get; set; }
        public int EditBatchItems { get; set; }
        public int BluePixels { get; set; }
        public double LogicalScrollTop { get; set; }
    }

    private sealed class CanvasPublicApiProbeResult
    {
        public string A1 { get; set; } = string.Empty;
        public string B1 { get; set; } = string.Empty;
        public string A2 { get; set; } = string.Empty;
        public string B2 { get; set; } = string.Empty;
    }

    private sealed class CanvasAccessibilityProbeResult
    {
        public bool RootFocused { get; set; }
        public string ActiveDescendant { get; set; } = string.Empty;
        public string ActiveText { get; set; } = string.Empty;
        public string ActiveRowIndex { get; set; } = string.Empty;
        public string ActiveColIndex { get; set; } = string.Empty;
        public string LiveText { get; set; } = string.Empty;
    }

    private sealed class CanvasEditorAccessibilityProbeResult
    {
        public bool Focused { get; set; }
        public string Role { get; set; } = string.Empty;
        public string AriaLabel { get; set; } = string.Empty;
        public string AriaDescribedBy { get; set; } = string.Empty;
        public string EditorMode { get; set; } = string.Empty;
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class CanvasPasteProbeResult
    {
        public double ElapsedMs { get; set; }
        public string TopLeft { get; set; } = string.Empty;
        public string BottomRight { get; set; } = string.Empty;
        public int RangeChangedCommands { get; set; }
        public int CommandLogCallbacks { get; set; }
        public int CommandLogBatchCallbacks { get; set; }
        public int CommandLogBatchItems { get; set; }
        public int ContentPaintFrames { get; set; }
    }

    private sealed class CanvasAutoFillProbeResult
    {
        public string A3 { get; set; } = string.Empty;
        public string A4 { get; set; } = string.Empty;
        public string A5 { get; set; } = string.Empty;
        public int RangeChangedCommands { get; set; }
        public int CommandLogCallbacks { get; set; }
        public int CommandLogBatchCallbacks { get; set; }
        public int CommandLogBatchItems { get; set; }
    }

    private sealed class CanvasCellStoreProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public int Red { get; set; }
        public int Green { get; set; }
        public int Blue { get; set; }
        public int SetCellCount { get; set; }
        public int StoreSize { get; set; }
        public int StyledOrNonEmptyCount { get; set; }
        public int LookupCount { get; set; }
        public int HitCount { get; set; }
        public int FrameScanCount { get; set; }
        public int VisibleCellCount { get; set; }
    }

    private sealed class CanvasJsLayoutProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public int BlazorFrames { get; set; }
        public int LayoutComputes { get; set; }
        public int BinarySearches { get; set; }
        public int RowSizeCacheSize { get; set; }
        public int ColumnSizeCacheSize { get; set; }
        public int KeyboardLogicalScrolls { get; set; }
        public double LogicalScrollTop { get; set; }
    }

    private sealed class CanvasRendererPipelineProbeResult
    {
        public int PaintFrames { get; set; }
        public int ContentPaintFrames { get; set; }
        public int SelectionPaintFrames { get; set; }
        public int ContentLayerPaints { get; set; }
        public int HeaderLayerPaints { get; set; }
        public int SelectionLayerPaints { get; set; }
        public int SelectionDirtyRects { get; set; }
        public int ContentDirtyRects { get; set; }
        public double LogicalScrollTop { get; set; }
    }

    private sealed class CanvasArrowHotPathProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public int KeyboardInteractions { get; set; }
        public int SelectionCallbacks { get; set; }
        public int KeyCommandCallbacks { get; set; }
        public int PaintRequests { get; set; }
        public int PaintFrames { get; set; }
        public int FirstFramePaints { get; set; }
        public int FirstFrameSelectionPaints { get; set; }
        public int FirstFrameContentPaints { get; set; }
        public int SelectionPaintFrames { get; set; }
        public int ContentPaintFrames { get; set; }
        public int MergedPaintRequests { get; set; }
        public int DiscardedIntermediatePaints { get; set; }
        public int MaxMergedPaintRequestsPerFrame { get; set; }
        public int KeyboardScrollToCount { get; set; }
        public int ScrollToCount { get; set; }
        public int LogicalKeyboardScrollCount { get; set; }
        public double LogicalScrollTop { get; set; }
        public double ScrollTop { get; set; }
    }

    private sealed class CanvasCommandLogProbeResult
    {
        public int CommandLogBatchCallbacks { get; set; }
        public int CommandLogBatchItems { get; set; }
        public int SelectionSettledCommands { get; set; }
        public int ViewportSettledCommands { get; set; }
        public int CellChangedCommands { get; set; }
        public int RangeChangedCommands { get; set; }
        public int FormulaCommittedCommands { get; set; }
        public int LegacySelectionCallbacks { get; set; }
        public int LegacyViewportCallbacks { get; set; }
        public int LegacyEditCallbacks { get; set; }
        public int CommandLogCallbacks { get; set; }
        public int AckRevision { get; set; }
    }

    private sealed class CanvasScrollbarSyncProbeResult
    {
        public double NativeScrollTop { get; set; }
        public double LogicalScrollTop { get; set; }
        public int KeyboardScrollToCount { get; set; }
        public int ScrollToCount { get; set; }
        public int OwnNativeScrollEventCount { get; set; }
    }

    private sealed class CanvasDragAutoscrollHotPathProbeResult
    {
        public double LogicalScrollTop { get; set; }
        public int LogicalPointerScrollCount { get; set; }
        public int DragAutoscrollFrames { get; set; }
        public int ViewportCallbackCount { get; set; }
        public int SelectionCallbackCount { get; set; }
        public int SelectionRedrawCount { get; set; }
        public int ScrollToCount { get; set; }
    }

    private sealed class CanvasWheelHotPathProbeResult
    {
        public double LogicalScrollTop { get; set; }
        public double NativeScrollTop { get; set; }
        public int WheelEvents { get; set; }
        public int WheelPrevented { get; set; }
        public int LogicalWheelScrollCount { get; set; }
        public int ViewportCallbacks { get; set; }
        public int ScrollToCount { get; set; }
        public int PaintRequests { get; set; }
        public int PaintFrames { get; set; }
        public int ContentPaintFrames { get; set; }
        public int MaxMergedPaintRequestsPerFrame { get; set; }
    }

    private sealed class CanvasPhase12LatencyProbeResult
    {
        public CanvasPhase12InteractionProbe ArrowDownViewport { get; set; } = new();
        public CanvasPhase12InteractionProbe ArrowDownScrollEdge { get; set; } = new();
        public CanvasPhase12InteractionProbe NormalCellClick { get; set; } = new();
        public CanvasPhase12InteractionProbe FormulaCellClick { get; set; } = new();
        public CanvasPhase12InteractionProbe TypingCharacter { get; set; } = new();
        public CanvasPhase12InteractionProbe FormulaCommit { get; set; } = new();
    }

    private sealed class CanvasPhase12InteractionProbe
    {
        public double FirstFrameMs { get; set; }
        public double SettledMs { get; set; }
        public CanvasPhase12DebugDelta FirstFrameDebug { get; set; } = new();
        public CanvasPhase12DebugDelta SettledDebug { get; set; } = new();
    }

    private sealed class CanvasPhase12DebugDelta
    {
        public int DotNetCallbackCount { get; set; }
        public int HotPathDotNetCallbackCount { get; set; }
        public Dictionary<string, int> DotNetCallbacksByMethod { get; set; } = new();
        public Dictionary<string, int> HotPathDotNetCallbacksByMethod { get; set; } = new();
        public string LastDotNetCallbackMethod { get; set; } = string.Empty;
        public int BlazorFrameCount { get; set; }
        public int HotPathBlazorFrameCount { get; set; }
        public int ViewportCallbackCount { get; set; }
        public int SelectionCallbackCount { get; set; }
        public int PaintFrameCount { get; set; }
        public int ContentPaintFrameCount { get; set; }
        public int SelectionPaintFrameCount { get; set; }
    }

    private sealed class CanvasResizeDragProbeResult
    {
        public double InitialSize { get; set; }
        public double PreviewSize { get; set; }
        public double FinalSize { get; set; }
        public int PointerMoves { get; set; }
        public int PaintFramesBeforeCommit { get; set; }
        public int ContentPaintFramesBeforeCommit { get; set; }
        public int DotNetBeforeCommit { get; set; }
        public int BlazorBeforeCommit { get; set; }
        public int DotNetAfterCommit { get; set; }
        public int BlazorAfterCommit { get; set; }
        public int CommandLogCallbacks { get; set; }
    }

    private sealed class CanvasResizeTwiceProbeResult
    {
        public double FirstStartSize { get; set; }
        public double FirstPreviewSize { get; set; }
        public double FirstFinalSize { get; set; }
        public double SecondStartSize { get; set; }
        public double SecondPreviewSize { get; set; }
        public double SecondFinalSize { get; set; }
        public int CommandLogCallbacks { get; set; }
    }

    private sealed class CanvasResizeAlignmentProbeResult
    {
        public double BeforeEditorWidth { get; set; }
        public double AfterEditorWidth { get; set; }
        public double BeforeEditorHeight { get; set; }
        public double AfterEditorHeight { get; set; }
        public double AfterEditorStyleWidth { get; set; }
        public double AfterEditorStyleHeight { get; set; }
        public int EditorRow { get; set; }
        public int EditorCol { get; set; }
        public double LayoutColumnWidth { get; set; }
        public double LayoutRowHeight { get; set; }
        public double ModelColumnWidth { get; set; }
        public bool FormulaActive { get; set; }
        public int FormulaRefCount { get; set; }
        public int SelectionPaintDelta { get; set; }
        public int SelectionBluePixels { get; set; }
        public int FormulaBluePixels { get; set; }
    }

    private sealed class CanvasFrozenResizeProbeResult
    {
        public int FreezeRowCount { get; set; }
        public int FreezeColumnCount { get; set; }
        public double InitialColumnSize { get; set; }
        public double FinalColumnSize { get; set; }
        public double InitialRowSize { get; set; }
        public double FinalRowSize { get; set; }
    }

    private sealed class CanvasLargeDatasetResizeProbeResult
    {
        public double InitialColumnSize { get; set; }
        public double FinalColumnSize { get; set; }
        public double InitialRowSize { get; set; }
        public double FinalRowSize { get; set; }
        public int DotNetBeforeCommit { get; set; }
        public int DotNetAfterCommit { get; set; }
        public int ContentPaintFrames { get; set; }
    }

    private sealed class CanvasKeyboardRepeatProbeResult
    {
        public string ActiveRef { get; set; } = string.Empty;
        public string StartRef { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public int RepeatEvents { get; set; }
        public int AcceleratedEvents { get; set; }
        public int MaxStep { get; set; }
        public int LastStep { get; set; }
        public int SequenceCount { get; set; }
    }
}
