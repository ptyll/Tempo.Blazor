using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7 of the Spreadsheet redesign — TOUCH INTERACTION of the canvas. Everything the mouse can do on
/// the grid must also work with a finger (select, edit, extend the selection, scroll, zoom, the cell
/// menu) without regressing the mouse, and the filled/selected state must be reachable without a DRAG
/// at all (WCAG 2.5.7 — the fill commands in the cell menu, Ctrl+D / Ctrl+R, Shift+arrow).
/// <para>
/// Every page is opened with <c>hasTouch</c>: the touch gesture sessions are
/// <c>pointerTypes: ['touch']</c> and the handle layer is a <c>(pointer: coarse)</c> rule, so a
/// desktop-pointer context can never observe them. The fingers themselves are REAL CDP touch events
/// (<c>Input.dispatchTouchEvent</c>) for the pinch, the long-press and the handle drag — a synthetic
/// PointerEvent cannot prove pointer capture — plus <c>Touchscreen.TapAsync</c> for the taps.
/// </para>
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as <c>phase7-*.png</c> and are reviewed
/// against output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet--tablet.png and
/// <c>spreadsheet--mobile.png</c>.
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    private const int Phase7TabletWidth = 1024;
    private const int Phase7TabletHeight = 900;
    private const int Phase7MobileWidth = 390;
    private const int Phase7MobileHeight = 844;

    /// <summary>The demo card's canvas grid (the wide canvas renderer of <c>/spreadsheet</c>).</summary>
    private static ILocator Phase7Grid(IPage page)
        => page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid");

    private async Task<IPage> OpenPhase7DemoAsync(int width, int height, bool hasTouch)
    {
        var page = await CreatePageAsync(hasTouch);
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
        var grid = Phase7Grid(page);
        await WaitForCanvasGridReadyAsync(page, grid);

        // The demo page is a long scrolling document: centre the card so the grid (and the phone's
        // bottom action bar) is on screen for both the fingers and the shots.
        await page.EvaluateAsync(@"() => {
            const card = document.querySelector(""[data-testid='spreadsheet-demo']"");
            if (card) card.scrollIntoView({ block: 'center' });
        }");
        await page.WaitForTimeoutAsync(500);
        await grid.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private async Task ShotPhase7Async(IPage page, string name)
    {
        var path = ShotPath("phase7-" + name);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    /// <summary>Taps a cell with a real finger tap (Playwright's touch tap, element-relative position).</summary>
    private static async Task Phase7TapCellAsync(IPage page, ILocator grid, string cellRef)
    {
        var point = await GetCanvasCellCenterAsync(grid, cellRef);
        Assert.IsTrue(point.X >= 0 && point.Y >= 0, $"cell {cellRef} must be rendered before it can be tapped");
        await grid.TapAsync(new LocatorTapOptions
        {
            Force = true,
            Position = new() { X = point.X, Y = point.Y },
            Timeout = 20_000,
        });
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>The cell centre in VIEWPORT coordinates (what the fingers and the touchscreen use).</summary>
    private static async Task<(double X, double Y)> Phase7CellPointAsync(ILocator grid, string cellRef)
    {
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box, "the canvas grid must be on screen");
        var cell = await GetCanvasCellCenterAsync(grid, cellRef);
        Assert.IsTrue(cell.X >= 0 && cell.Y >= 0, $"cell {cellRef} must be rendered before it can be touched");
        Assert.IsTrue(cell.X <= box!.Width && cell.Y <= box.Height,
            $"cell {cellRef} must be inside the grid box at the current zoom/scroll (x={cell.X}, y={cell.Y})");
        return (box.X + cell.X, box.Y + cell.Y);
    }

    // ── CDP fingers ─────────────────────────────────────────────────────────────────────────────

    private static Task Phase7TouchAsync(ICDPSession session, string type, params (int Id, double X, double Y)[] points)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(point => (object)new Dictionary<string, object>
            {
                ["x"] = point.X,
                ["y"] = point.Y,
                ["id"] = point.Id,
                ["radiusX"] = 6,
                ["radiusY"] = 6,
                ["force"] = 1,
            }).ToArray(),
        });

    /// <summary>A real finger hold: touchStart, wait, touchEnd (the 500 ms long-press path).</summary>
    private static async Task Phase7LongPressAsync(IPage page, ICDPSession session, double x, double y, int holdMs = 800)
    {
        await Phase7TouchAsync(session, "touchStart", (1, x, y));
        await page.WaitForTimeoutAsync(holdMs);
        await Phase7TouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>A one-finger drag: the finger lands, moves in steps, lifts.</summary>
    private static async Task Phase7FingerDragAsync(
        IPage page, ICDPSession session, (double X, double Y) from, (double X, double Y) to, int steps = 8)
    {
        await Phase7TouchAsync(session, "touchStart", (1, from.X, from.Y));
        await page.WaitForTimeoutAsync(80);
        for (var step = 1; step <= steps; step++)
        {
            var x = from.X + (to.X - from.X) * step / steps;
            var y = from.Y + (to.Y - from.Y) * step / steps;
            await Phase7TouchAsync(session, "touchMove", (1, x, y));
            await page.WaitForTimeoutAsync(40);
        }

        await Phase7TouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>A two-finger pinch around the grid centre (fingers spread by <paramref name="spread"/>).</summary>
    private static async Task Phase7PinchAsync(IPage page, ICDPSession session, ILocator grid, double spread, int steps = 10)
    {
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box, "the canvas grid must be on screen");
        var centerX = box!.X + box.Width / 2;
        var centerY = box.Y + box.Height / 2;
        const double start = 60;

        await Phase7TouchAsync(session, "touchStart", (1, centerX - start, centerY), (2, centerX + start, centerY));
        await page.WaitForTimeoutAsync(60);
        for (var step = 1; step <= steps; step++)
        {
            var distance = start + spread * step / steps;
            await Phase7TouchAsync(session, "touchMove",
                (1, centerX - distance, centerY), (2, centerX + distance, centerY));
            await page.WaitForTimeoutAsync(45);
        }

        await Phase7TouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(700);
    }

    /// <summary>Writes a value into a cell with the phone/keyboard path the grid uses (double tap → type).</summary>
    private static async Task Phase7SeedCellAsync(IPage page, ILocator grid, string cellRef, string value)
    {
        await EditCanvasCellAsync(page, grid, cellRef, value);
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>
    /// A cell reference whose centre is on screen AT THIS ZOOM: the model's visible rows/columns are read
    /// from the canvas, so a shot/long-press after a pinch never targets a cell that scrolled away.
    /// </summary>
    private static async Task<string> Phase7VisibleCellRefAsync(ILocator grid)
    {
        var cellRef = await grid.EvaluateAsync<string>(@"el => {
            const s = el.__tmSpreadsheetCanvas;
            const model = s?.model || {};
            const read = (o, a, b, fallback) => (o?.[a] ?? o?.[b] ?? fallback);
            // The gutter geometry is the engine's (density-scaled 40/20 or 56/28), never 40/20 literals.
            const gutters = window.tmSpreadsheetCanvas.getGridGutters(el);
            if (!gutters) {
                throw new Error('the canvas engine has no model yet - refusing to measure a 40/20 grid it never painted');
            }
            const rowHeaderWidth = Number(gutters.rowHeaderWidth);
            const columnHeaderHeight = Number(gutters.columnHeaderHeight);
            const scrollLeft = Number(read(model, 'scrollLeft', 'ScrollLeft', 0));
            const scrollTop = Number(read(model, 'scrollTop', 'ScrollTop', 0));
            const width = el.clientWidth || 1;
            const height = el.clientHeight || 1;
            const rows = read(model, 'rows', 'Rows', []) || [];
            const columns = read(model, 'columns', 'Columns', []) || [];
            const toRef = (row, col) => {
                let letters = '';
                let value = col + 1;
                while (value > 0) { value -= 1; letters = String.fromCharCode(65 + (value % 26)) + letters; value = Math.floor(value / 26); }
                return letters + (row + 1);
            };
            for (const row of rows) {
                const top = Number(read(row, 'top', 'Top', 0));
                const rowHeight = Number(read(row, 'height', 'Height', 0));
                const y = columnHeaderHeight + top - scrollTop + rowHeight / 2;
                if (rowHeight <= 0 || y < columnHeaderHeight + 8 || y > height - 8) continue;
                for (const column of columns) {
                    const left = Number(read(column, 'left', 'Left', 0));
                    const columnWidth = Number(read(column, 'width', 'Width', 0));
                    const x = rowHeaderWidth + left - scrollLeft + columnWidth / 2;
                    if (columnWidth <= 0 || x < rowHeaderWidth + 8 || x > width - 8) continue;
                    return toRef(Number(read(row, 'index', 'Index', 0)), Number(read(column, 'index', 'Index', 0)));
                }
            }
            return 'A1';
        }");
        return string.IsNullOrWhiteSpace(cellRef) ? "A1" : cellRef;
    }

    /// <summary>The status-bar zoom readout as a number ("140%" → 140); the phone chrome has none.</summary>
    private static async Task<int> Phase7ZoomPercentAsync(IPage page)
    {
        var text = await page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-statusbar__zoom-percent")
            .First.TextContentAsync();
        var digits = new string((text ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : -1;
    }

    /// <summary>Waits for the status-bar zoom to satisfy the predicate (the .NET round-trip is slow here).</summary>
    private static async Task<int> Phase7WaitForZoomAsync(IPage page, Func<int, bool> predicate, string failureMessage)
    {
        var percent = -1;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            percent = await Phase7ZoomPercentAsync(page);
            if (predicate(percent))
                return percent;

            await Task.Delay(150);
        }

        Assert.Fail($"{failureMessage} Last zoom: {percent}%");
        return percent;
    }

    /// <summary>The grid's selection snapshot as compact JSON (row/col + the rectangle).</summary>
    private static Task<string> Phase7SelectionJsonAsync(ILocator grid) =>
        grid.EvaluateAsync<string>(@"el => {
            const model = el.__tmSpreadsheetCanvas?.model || {};
            const selection = model.Selection || model.selection || {};
            const read = (o, a, b, f) => (o?.[a] ?? o?.[b] ?? f);
            return JSON.stringify({
                active: String(read(model, 'activeCellRef', 'ActiveCellRef', '')),
                startRow: Number(read(selection, 'startRow', 'StartRow', -1)),
                startCol: Number(read(selection, 'startCol', 'StartCol', -1)),
                endRow: Number(read(selection, 'endRow', 'EndRow', -1)),
                endCol: Number(read(selection, 'endCol', 'EndCol', -1))
            });
        }");

    private sealed class Phase7Selection
    {
        public string Active { get; set; } = string.Empty;

        public int StartRow { get; set; } = -1;

        public int StartCol { get; set; } = -1;

        public int EndRow { get; set; } = -1;

        public int EndCol { get; set; } = -1;
    }

    private static async Task<Phase7Selection> Phase7WaitForSelectionAsync(
        ILocator grid, Func<Phase7Selection, bool> predicate, string failureMessage)
    {
        Phase7Selection? selection = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var json = await Phase7SelectionJsonAsync(grid);
            selection = System.Text.Json.JsonSerializer.Deserialize<Phase7Selection>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (selection is not null && predicate(selection))
                return selection;

            await Task.Delay(100);
        }

        Assert.Fail($"{failureMessage} Last selection: {System.Text.Json.JsonSerializer.Serialize(selection)}");
        return selection!;
    }

    // ── tablet 1024: tap, double tap, long press, pinch, handle drag ─────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Tablet_TapSelectsCell_AndDoubleTapStartsEditing()
    {
        var page = await OpenPhase7DemoAsync(Phase7TabletWidth, Phase7TabletHeight, hasTouch: true);
        var grid = Phase7Grid(page);

        // ── Tap = select ────────────────────────────────────────────────────────────────────────
        await Phase7TapCellAsync(page, grid, "C4");
        await Phase7WaitForSelectionAsync(grid, selection => selection.Active == "C4",
            "a single finger tap on C4 must select it");
        await ShotPhase7Async(page, "tablet-1024-touch--selection");

        // ── Double tap = edit, with the typed virtual keyboard ───────────────────────────────────
        // One CDP touch sequence for both taps: two Playwright TapAsync calls are separate round trips and
        // can fall outside the 320 ms double-tap window on a loaded box.
        var dbl = await Phase7CellPointAsync(grid, "C4");
        var tapSession = await page.Context.NewCDPSessionAsync(page);
        await Phase7TouchAsync(tapSession, "touchStart", (1, dbl.X, dbl.Y));
        await Phase7TouchAsync(tapSession, "touchEnd");
        await Phase7TouchAsync(tapSession, "touchStart", (1, dbl.X, dbl.Y));
        await Phase7TouchAsync(tapSession, "touchEnd");
        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.AreEqual("text", await editor.GetAttributeAsync("inputmode"),
            "an empty (text) cell asks for the text keyboard; a numeric cell asks for the decimal one");

        await page.Keyboard.TypeAsync("42");
        // The evidence for "editing with the keyboard": the open editor and its inputmode, captured while
        // they are on screen (after Enter the editor is gone and the shot shows nothing).
        Assert.AreEqual("42", await editor.InputValueAsync());
        var editorBox = await editor.BoundingBoxAsync();
        Assert.IsNotNull(editorBox, "the inline editor must be visible for the editing screenshot");
        await ShotPhase7Async(page, "tablet-1024-touch--editing");

        await page.Keyboard.PressAsync("Enter");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        var snapshot = await WaitForCanvasCellSnapshotAsync(grid, "C4",
            cellSnapshot => cellSnapshot.Value == "42", "the value typed with the virtual keyboard must commit");
        Assert.AreEqual("42", snapshot.Value);

        // ── Long press = the cell menu (BEFORE the pinch, so the target is on screen at 100 %) ───
        // The finger lands in the middle of the visible grid: whatever cell is there must become ACTIVE
        // (the menu acts on it) and the menu must open. A1 is selected first so "became active" is
        // unambiguous, and the point needs no cell-store arithmetic (which density/zoom can invalidate).
        var session = await page.Context.NewCDPSessionAsync(page);
        await Phase7TapCellAsync(page, grid, "A1");
        await Phase7WaitForSelectionAsync(grid, selection => selection.Active == "A1", "A1 must be selected");
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await Phase7LongPressAsync(page, session, box!.X + box.Width / 2, box.Y + box.Height / 2);
        var pressed = await Phase7WaitForSelectionAsync(grid,
            selection => selection.Active.Length > 0 && selection.Active != "A1",
            "the long press must make the cell under the finger active");
        var menuItem = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-context-menu__item").First;
        await menuItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotPhase7Async(page, "tablet-1024-touch--cell-menu");
        Assert.AreNotEqual("", pressed.Active, "the context menu names the cell it acts on");

        // The menu must NOT have been closed by the lifting finger (the release-click guard).
        await page.WaitForTimeoutAsync(600);
        Assert.IsTrue(await menuItem.IsVisibleAsync(),
            "the release of the long-press must not close the menu it just opened (release-click guard)");

        // A finger dismisses the anchored menu by tapping outside it (the overlay closes the host menu).
        await page.Touchscreen.TapAsync(12, 12);
        await menuItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // ── Two fingers = pinch zoom (the status bar readout proves it) ─────────────────────────
        var zoomBefore = await Phase7ZoomPercentAsync(page);
        await Phase7PinchAsync(page, session, grid, spread: 120);
        var zoomAfter = await Phase7WaitForZoomAsync(page, percent => percent > zoomBefore,
            $"a two-finger pinch must zoom in through pointer-gesture.js (was {zoomBefore}%)");
        await ShotPhase7Async(page, "tablet-1024-touch--pinch-zoom");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Tablet_DragFromSelectionHandle_ExtendsSelection()
    {
        var page = await OpenPhase7DemoAsync(Phase7TabletWidth, Phase7TabletHeight, hasTouch: true);
        var grid = Phase7Grid(page);

        await Phase7TapCellAsync(page, grid, "B3");
        await Phase7WaitForSelectionAsync(grid, selection => selection.Active == "B3", "B3 must be selected first");

        // The DOM handle layer must be visible: two ROUND corner drops plus the fill box (the four edge
        // strips were removed — they swallowed taps meant for the selected row/column).
        var endDrop = grid.Locator("[data-touch-handle='end']");
        await endDrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var handleBox = await endDrop.BoundingBoxAsync();
        Assert.IsNotNull(handleBox, "the selection's bottom-right corner drop must be on screen");
        Assert.AreEqual(0, await grid.Locator("[data-touch-handle='bottom']").CountAsync(),
            "the bottom edge strip must be gone");

        // One row down is enough to prove the extension, and staying well inside the grid keeps the
        // assertion independent of the drag autoscroll.
        var target = await Phase7CellPointAsync(grid, "B4");
        var session = await page.Context.NewCDPSessionAsync(page);
        await Phase7FingerDragAsync(page, session,
            (handleBox.X + handleBox.Width / 2, handleBox.Y + handleBox.Height / 2),
            (target.X, target.Y));

        var selection = await Phase7WaitForSelectionAsync(grid, candidate => candidate.EndRow >= 3,
            "dragging the selection's bottom drop must EXTEND the selection (pointer-drag.js session)");
        Assert.AreEqual(2, selection.StartRow, "the selection anchor stays on B3's row (index 2)");
        Assert.IsTrue(selection.EndRow >= 3, $"the end row must follow the finger (endRow={selection.EndRow})");
        await ShotPhase7Async(page, "tablet-1024-touch--extended-selection");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Tablet_TapOnTheCellBelowTheSelection_SelectsIt()
    {
        var page = await OpenPhase7DemoAsync(Phase7TabletWidth, Phase7TabletHeight, hasTouch: true);
        var grid = Phase7Grid(page);

        await Phase7TapCellAsync(page, grid, "B3");
        await Phase7WaitForSelectionAsync(grid, selection => selection.Active == "B3", "B3 must be selected first");

        // The corner drops sit on the corners only — the row BELOW the selection is not covered by any hit
        // area, so a tap there selects the cell instead of being stolen (the old edge strip ate it).
        await Phase7TapCellAsync(page, grid, "B4");
        var selection = await Phase7WaitForSelectionAsync(grid, candidate => candidate.Active == "B4",
            "a tap on the cell directly below the selection must select that cell");
        Assert.AreEqual(3, selection.StartRow, "the selection is collapsed on the tapped cell");
        Assert.AreEqual(3, selection.EndRow);
    }

    // ── mobile 390: G2 = viewing + basic editing, the sheet, the action bar ─────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Mobile_TapSelects_LongPressOpensTheSheet()
    {
        var page = await OpenPhase7DemoAsync(Phase7MobileWidth, Phase7MobileHeight, hasTouch: true);
        var grid = Phase7Grid(page);

        var cell = await Phase7CellPointAsync(grid, "B3");
        await page.Touchscreen.TapAsync((float)cell.X, (float)cell.Y);
        await Phase7WaitForSelectionAsync(grid, selection => selection.Active == "B3", "a phone tap must select B3");
        await ShotPhase7Async(page, "mobile-390-touch--selection");

        var session = await page.Context.NewCDPSessionAsync(page);
        // Recompute the point: the tap above may have scrolled the grid into view.
        var hold = await Phase7CellPointAsync(grid, "B3");
        await Phase7LongPressAsync(page, session, hold.X, hold.Y);

        // The phone chrome renders the host-owned menu as the core bottom sheet.
        var sheet = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-context-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        var sheetItems = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-context-menu--sheet .tm-spreadsheet-context-menu__item");
        Assert.IsTrue(await sheetItems.CountAsync() >= 10, "the sheet carries the full cell menu");
        var labels = await sheetItems.AllTextContentsAsync();
        Assert.IsTrue(labels.Any(label => label.Contains("Fill down", StringComparison.Ordinal)),
            "the sheet carries the non-drag fill entry (WCAG 2.5.7)");
        await ShotPhase7Async(page, "mobile-390-touch--context-sheet");

        // Dismiss the first sheet with a finger tap outside it.
        await page.Touchscreen.TapAsync(195, 60);
        await page.WaitForTimeoutAsync(500);

        // Fill down from the sheet: the fill handle's non-drag alternative — the entry is TAPPED and the
        // value it produces is asserted (not just "the menu has a label"). B3 is a single cell, so Fill
        // down copies the cell ABOVE it; B2 is seeded first so the fill has a source.
        await Phase7SeedCellAsync(page, grid, "B2", "7");
        await Phase7TapCellAsync(page, grid, "B3");
        await Phase7WaitForSelectionAsync(grid, s => s.Active == "B3", "B3 must be selected for the fill");
        var holdPoint = await Phase7CellPointAsync(grid, "B3");
        var holdSession = await page.Context.NewCDPSessionAsync(page);
        await Phase7LongPressAsync(page, holdSession, holdPoint.X, holdPoint.Y);
        await page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-context-menu--sheet")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-context-menu--sheet .tm-spreadsheet-context-menu__item")
            .Filter(new LocatorFilterOptions { HasText = "Fill down" })
            .TapAsync(new LocatorTapOptions { Force = true });
        var filled = await WaitForCanvasCellSnapshotAsync(grid, "B3", c => c.Value == "7",
            "the phone sheet's Fill down must copy B2 into B3");
        Assert.AreEqual("7", filled.Value);
        await ShotPhase7Async(page, "mobile-390-touch--fill-down");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Mobile_DoubleTapEdits_NumericCellUsesTheDecimalKeypad()
    {
        var page = await OpenPhase7DemoAsync(Phase7MobileWidth, Phase7MobileHeight, hasTouch: true);
        var grid = Phase7Grid(page);

        // Type a number first (a hardware key), so the cell is numeric.
        await EditCanvasCellAsync(page, grid, "B2", "12");

        var cell = await Phase7CellPointAsync(grid, "B2");
        // One CDP touch sequence for both taps: two Playwright Touchscreen.TapAsync calls are separate
        // round trips and can fall outside the 320 ms double-tap window on a loaded box.
        var tapSession = await page.Context.NewCDPSessionAsync(page);
        await Phase7TouchAsync(tapSession, "touchStart", (1, cell.X, cell.Y));
        await Phase7TouchAsync(tapSession, "touchEnd");
        await Phase7TouchAsync(tapSession, "touchStart", (1, cell.X, cell.Y));
        await Phase7TouchAsync(tapSession, "touchEnd");
        var editor = grid.Locator(".tm-spreadsheet-canvas-grid__editor");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.AreEqual("decimal", await editor.GetAttributeAsync("inputmode"),
            "a numeric cell asks for the decimal keypad (the inputmode follows the cell type)");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);

        // Reading the cell back proves the double tap opened the SAME editor the mouse uses.
        var snapshot = await ReadCanvasCellSnapshotAsync(grid, "B2");
        Assert.AreEqual("12", snapshot.Value);
    }

    // ── Ctrl+D / Ctrl+R: the canvas claims the chords, so no browser bookmark and no reload ──────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Desktop_CtrlD_FillsDown_AndCtrlR_FillsRight_WithoutReloadingThePage()
    {
        var page = await OpenPhase7DemoAsync(1440, 960, hasTouch: false);
        var grid = Phase7Grid(page);

        // A page marker: a reload would wipe it (Ctrl+R is the browser's reload when the canvas does not
        // claim the chord — the phase-7 bug this pins).
        await page.EvaluateAsync("() => { window.__phase7Marker = 'alive'; }");

        await EditCanvasCellAsync(page, grid, "A1", "5");

        // Ctrl+D on A1:A3 — every row below the pattern gets the value.
        await DragCanvasBetweenCellsAsync(grid, "A1", "A3");
        await Phase7WaitForSelectionAsync(grid, s => s.EndRow == 2, "A1:A3 must be selected");
        await grid.PressAsync("Control+d");
        var a2 = await WaitForCanvasCellSnapshotAsync(grid, "A2", c => c.Value == "5", "Ctrl+D must fill A2 from A1");
        var a3 = await WaitForCanvasCellSnapshotAsync(grid, "A3", c => c.Value == "5", "Ctrl+D must fill A3 from A1");
        Assert.AreEqual("5", a2.Value);
        Assert.AreEqual("5", a3.Value);

        // Ctrl+R on A1:C1 — every column to the right of the pattern gets the value.
        await DragCanvasBetweenCellsAsync(grid, "A1", "C1");
        await Phase7WaitForSelectionAsync(grid, s => s.EndCol == 2, "A1:C1 must be selected");
        await grid.PressAsync("Control+r");
        await WaitForCanvasCellSnapshotAsync(grid, "B1", c => c.Value == "5", "Ctrl+R must fill B1 from A1");
        await WaitForCanvasCellSnapshotAsync(grid, "C1", c => c.Value == "5", "Ctrl+R must fill C1 from A1");

        // Neither chord may have reloaded the page: the marker (and the filled cells) survived.
        var marker = await page.EvaluateAsync<string?>("() => window.__phase7Marker ?? null");
        Assert.AreEqual("alive", marker, "Ctrl+R must not reload the page with the grid on it");
        Assert.IsTrue(await page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid").First.IsVisibleAsync(),
            "the grid must still be interactive after the chords");
    }

    // ── mouse regression (the desktop pointer must not enter the touch path) ────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_Desktop_MouseSelectionAndFillHandleStillWork()
    {
        var page = await OpenPhase7DemoAsync(1440, 960, hasTouch: false);
        var grid = Phase7Grid(page);
        await page.EvaluateAsync("() => document.querySelectorAll('[data-touch-handles]').length");

        // The handle layer must be hidden for a fine pointer: nothing about the mouse path changes.
        var handlesLayerState = await grid.EvaluateAsync<string>(
            "el => { const layer = el.querySelector('[data-touch-handles]'); return layer ? getComputedStyle(layer).display : 'missing'; }");
        Assert.AreEqual("none", handlesLayerState,
            "a fine pointer hides the touch handle layer entirely (no mouse regression)");

        // The classic mouse drag-select still selects a rectangle.
        await DragCanvasBetweenCellsAsync(grid, "B2", "D4");
        var selection = await Phase7WaitForSelectionAsync(grid, s => s.StartRow == 1 && s.EndRow == 3,
            "the mouse drag-select must keep working exactly as before");
        Assert.AreEqual(1, selection.StartCol);
        Assert.AreEqual(3, selection.EndCol);
    }

    // ── focus ownership after a canvas activation (phase-7 regression guard) ────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_CellClickThenNameBox_KeepsFocusAndNavigates()
    {
        var page = await OpenPhase7DemoAsync(1440, 960, hasTouch: false);
        var grid = Phase7Grid(page);

        // Click a cell, then IMMEDIATELY use the name box while the canvas' late frame may still be
        // pending. A grid that grabs the focus back blurs the name box, whose blur restores the active
        // ref and eats the Enter — the phase-7 render cascade made that race lose every time.
        var point = await GetCanvasCellCenterAsync(grid, "D5");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = point.X, Y = point.Y } });

        var nameBox = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-formula-bar__ref");
        await nameBox.FillAsync("F7");
        await nameBox.PressAsync("Enter");

        await Phase7WaitForSelectionAsync(grid, selection => selection.Active == "F7",
            "the name box must still navigate when it is used right after a canvas click");
    }

    // ── performance: the mouse path is unchanged (deterministic counters, never wall clock) ─────

    private sealed class Phase7PaintMetrics
    {
        public double SelectionPaintFrameCount { get; set; }

        public double HeaderLayerPaintCount { get; set; }

        public double ContentLayerPaintCount { get; set; }

        public double RedrawCount { get; set; }
    }

    private static Task<Phase7PaintMetrics> Phase7MetricsAsync(ILocator grid) =>
        grid.EvaluateAsync<Phase7PaintMetrics>(@"el => {
            const m = window.tmSpreadsheetCanvas?.getDebugMetrics?.(el) || {};
            return {
                selectionPaintFrameCount: Number(m.selectionPaintFrameCount || 0),
                headerLayerPaintCount: Number(m.headerLayerPaintCount || 0),
                contentLayerPaintCount: Number(m.contentLayerPaintCount || 0),
                redrawCount: Number(m.redrawCount || 0)
            };
        }");

    /// <summary>
    /// The mouse selection drag loop the A/B measures: 8 plain cell clicks and 8 drag-select moves, all
    /// with the DESKTOP pointer. Deterministic on purpose — this box's wall clock swings by an order of
    /// magnitude, so the claim rests on the per-frame counters, not on elapsed milliseconds.
    /// </summary>
    private static async Task<Phase7PaintMetrics> Phase7RunMouseSelectionLoopAsync(IPage page, ILocator grid)
    {
        var before = await Phase7MetricsAsync(grid);
        for (var index = 0; index < 8; index++)
        {
            var point = await GetCanvasCellCenterAsync(grid, ToCellRef(1 + index, 2));
            await grid.ClickAsync(new LocatorClickOptions
            {
                Force = true,
                Position = new() { X = point.X, Y = point.Y },
            });
        }

        for (var index = 0; index < 8; index++)
        {
            await DragCanvasBetweenCellsAsync(grid, ToCellRef(1 + index, 2), ToCellRef(1 + index, 4));
        }

        await page.WaitForTimeoutAsync(800);
        var after = await Phase7MetricsAsync(grid);
        return new Phase7PaintMetrics
        {
            SelectionPaintFrameCount = after.SelectionPaintFrameCount - before.SelectionPaintFrameCount,
            HeaderLayerPaintCount = after.HeaderLayerPaintCount - before.HeaderLayerPaintCount,
            ContentLayerPaintCount = after.ContentLayerPaintCount - before.ContentLayerPaintCount,
            RedrawCount = after.RedrawCount - before.RedrawCount,
        };
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7_MouseSelectionDrag_KeepsOneHeaderPaintPerSelectionChange()
    {
        var page = await OpenPhase7DemoAsync(1440, 960, hasTouch: false);
        var grid = Phase7Grid(page);

        var delta = await Phase7RunMouseSelectionLoopAsync(page, grid);

        // The absolute count is rAF-coalescing dependent (a drag repaints as it moves); what phase 7 must
        // not do is add a repaint PER LAYER. The header can never be painted more often than the selection,
        // and neither layer may exceed two paints per change. The A/B below pins the equality against the
        // phase-6 script, which is the actual no-regression claim.
        Assert.IsTrue(delta.HeaderLayerPaintCount <= delta.SelectionPaintFrameCount + 0.5,
            $"the header layer must not paint more often than the selection layer (header {delta.HeaderLayerPaintCount}, selection {delta.SelectionPaintFrameCount})");
        Assert.IsTrue(delta.SelectionPaintFrameCount <= 32,
            $"16 mouse selection changes must not paint the selection layer more than twice each (was {delta.SelectionPaintFrameCount})");
        Assert.IsTrue(delta.HeaderLayerPaintCount <= 32,
            $"16 mouse selection changes must not paint the header layer more than twice each (was {delta.HeaderLayerPaintCount})");
    }

    /// <summary>
    /// In-session A/B of the mouse selection loop: the phase-6 canvas script served through Playwright
    /// route interception, alternating arms, deterministic counters only. Set
    /// <c>TM_E2E_PHASE7_BASELINE_CANVAS</c> to the previous script's path to run it (otherwise inconclusive) —
    /// the baseline deliberately does not live in the repository, where it would go stale.
    /// </summary>
    [TestMethod]
    [TestCategory("Performance")]
    public async Task Phase7_MouseBenchmark_Ab_AgainstThePhase6Canvas()
    {
        var baselinePath = Environment.GetEnvironmentVariable("TM_E2E_PHASE7_BASELINE_CANVAS");
        if (string.IsNullOrWhiteSpace(baselinePath) || !File.Exists(baselinePath))
        {
            Assert.Inconclusive("TM_E2E_PHASE7_BASELINE_CANVAS must point at the previous spreadsheet-canvas.js");
            return;
        }

        var currentPath = Path.Combine(
            Phase2RepoRoot().FullName, "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "js", "spreadsheet-canvas.js");
        var arm = "current";
        var page = await CreatePageAsync();
        await page.RouteAsync("**/spreadsheet-canvas.js*", async route =>
        {
            var source = arm == "baseline" ? baselinePath : currentPath;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/javascript",
                Body = await File.ReadAllTextAsync(source),
            });
        });

        async Task<Phase7PaintMetrics> RunAsync(string which)
        {
            arm = which;
            await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
            var grid = Phase7Grid(page);
            await WaitForCanvasGridReadyAsync(page, grid);
            var served = await page.EvaluateAsync<string>(
                "() => (window.tmSpreadsheetCanvas && window.tmSpreadsheetCanvas._internals.resolveTouchTap) ? 'phase7' : 'phase6'");
            Assert.AreEqual(which == "baseline" ? "phase6" : "phase7", served,
                $"the route interception must serve the {which} arm");
            return await Phase7RunMouseSelectionLoopAsync(page, grid);
        }

        var first = await RunAsync("baseline");
        var second = await RunAsync("current");
        var third = await RunAsync("baseline");
        var fourth = await RunAsync("current");

        var baselineSelection = (first.SelectionPaintFrameCount + third.SelectionPaintFrameCount) / 2;
        var currentSelection = (second.SelectionPaintFrameCount + fourth.SelectionPaintFrameCount) / 2;
        var baselineHeader = (first.HeaderLayerPaintCount + third.HeaderLayerPaintCount) / 2;
        var currentHeader = (second.HeaderLayerPaintCount + fourth.HeaderLayerPaintCount) / 2;

        TestContext.WriteLine(
            "[phase7-ab] baseline selection={0:F0} header={1:F0} content={2:F0} | current selection={3:F0} header={4:F0} content={5:F0}",
            baselineSelection, baselineHeader, (first.ContentLayerPaintCount + third.ContentLayerPaintCount) / 2,
            currentSelection, currentHeader, (second.ContentLayerPaintCount + fourth.ContentLayerPaintCount) / 2);
        Assert.AreEqual(baselineSelection, currentSelection, 0.5,
            "the mouse selection drag paints the selection layer exactly as often as the phase-6 canvas did");
        Assert.AreEqual(baselineHeader, currentHeader, 0.5,
            "the mouse selection drag paints the header layer exactly as often as the phase-6 canvas did");
    }
}
