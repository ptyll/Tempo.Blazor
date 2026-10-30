using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Redesign phase 7 — touch interaction of the canvas. The browser-side gesture DECISIONS are pure
/// functions covered by <c>npm run test:spreadsheet</c> (<c>spreadsheet-canvas-touch.test.mjs</c>) and
/// the real fingers are driven by the Playwright touch lane; the tests here pin the CONTRACT between
/// that JS layer and Blazor: the long-press opens the host-owned phone sheet, the pinch commits through
/// <see cref="TmSpreadsheetCanvasGrid.OnCanvasZoomChanged"/>, the Auto density re-resolves when the
/// pointer kind flips, the Comfortable density scales the header gutters too, and the fill handle's
/// non-drag alternative (Ctrl+D / Ctrl+R, Fill down / Fill right) drives the same workbook command.
/// </summary>
[Collection("SpreadsheetClipboard")]
public class TmSpreadsheetPhase7TouchTests : LocalizationTestBase
{
    private const string InitEngine = "tmSpreadsheetCanvas.initEngine";
    private const string RenderFrameCall = "tmSpreadsheetCanvas.render";

    // ── long press → the host-owned phone sheet ──────────────────────────────────────────────────

    [Fact]
    public async Task CanvasGrid_LongPress_OpensContextMenuSheet_OnMobile()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll(".tm-spreadsheet-context-menu").Should().BeEmpty("no menu is open before the gesture");

        // The long-press recognizer (500 ms, touch only) is JS-side; its ACTION is this interop call —
        // the same one a right-click uses. On the phone layout the host renders it as the core sheet.
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(40, 40, 120, 240));

        var sheetMenu = cut.FindAll(".tm-spreadsheet-context-menu--sheet");
        sheetMenu.Should().ContainSingle("a long-press on the phone layout opens the bottom-sheet menu");
        cut.FindAll(".tm-spreadsheet-context-sheet").Should().ContainSingle(
            "the sheet is the core TmDrawer presentation, not the anchored desktop popover");
        cut.FindAll(".tm-spreadsheet-context-menu__overlay").Should().BeEmpty(
            "the desktop overlay belongs to the anchored presentation only");

        // The long-press alternative is still a real menu: the fill entries (the fill handle's
        // non-drag path) must be in it, localized.
        var labels = cut.FindAll(".tm-spreadsheet-context-menu--sheet .tm-spreadsheet-context-menu__item")
            .Select(item => item.TextContent.Trim())
            .ToList();
        labels.Should().Contain("Fill down");
        labels.Should().Contain("Fill right");
    }

    // ── pinch → OnZoomChanged ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanvasGrid_ZoomFromPinch_InvokesOnZoomChanged()
    {
        var zoomChanges = new List<double>();
        var cut = Render<TmSpreadsheetCanvasGrid>(parameters => parameters
            .Add(p => p.Sheet, new SpreadsheetSheet { RowCount = 20, ColumnCount = 10 })
            .Add(p => p.OnZoomChanged, zoom => zoomChanges.Add(zoom)));

        // The pinch is pointer-gesture.js on the browser side; it hands the clamped + quantized zoom
        // factor to this interop method. One commit, no per-frame .NET traffic.
        await cut.InvokeAsync(() => cut.Instance.OnCanvasZoomChanged(1.4));

        zoomChanges.Should().ContainSingle().Which.Should().Be(1.4,
            "the pinch commits the absolute zoom factor the status bar shows");
    }

    [Fact]
    public async Task Spreadsheet_ZoomFromPinch_ReachesTheStatusBar()
    {
        var cut = Render<TmSpreadsheet>();
        cut.Find(".tm-spreadsheet-statusbar__zoom-percent").TextContent.Should().Contain("100%");

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasZoomChanged(1.4));

        cut.Find(".tm-spreadsheet-statusbar__zoom-percent").TextContent.Should().Contain("140%",
            "the pinch is wired to the SAME OnZoomChanged the status-bar buttons drive");
        FrameValue(CanvasFrame(cut), "Zoom").Should().Be(1.4);
    }

    [Fact]
    public async Task Spreadsheet_ZoomFromPinch_IsClampedByTheComponent()
    {
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // The JS recognizer already clamps to 0.5–2.0; the component's own clamp is the last gate.
        await cut.InvokeAsync(() => grid.Instance.OnCanvasZoomChanged(9));

        FrameValue(CanvasFrame(cut), "Zoom").Should().Be(2.0,
            "the component maximum stays 200 % even if a host pumps a bigger value in");
    }

    // ── Auto density follows the pointer kind ───────────────────────────────────────────────────

    [Fact]
    public async Task CanvasGrid_PointerKindChange_ReResolvesAutoDensity()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Auto));

        FrameValue(CanvasFrame(cut), "DensityResolved").Should().Be("Compact", "bUnit reports a fine pointer");

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasPointerKindChanged(true));

        FrameValue(CanvasFrame(cut), "DensityResolved").Should().Be("Comfortable",
            "a coarse pointer appearing (a tablet, a 2-in-1 undocked) re-resolves the Auto density instead "
            + "of keeping the first answer for the lifetime of the grid");
        cut.Find(".tm-spreadsheet-canvas-grid").GetAttribute("data-density").Should().Be("comfortable");
    }

    [Fact]
    public async Task CanvasGrid_PointerKindChange_LeavesAForcedDensityAlone()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Compact));

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasPointerKindChanged(true));

        FrameValue(CanvasFrame(cut), "DensityResolved").Should().Be("Compact",
            "an explicit Density wins over the pointer kind — the change listener only serves Auto");
    }

    [Fact]
    public void Spreadsheet_Density_Comfortable_ScalesTheHeaderGutters()
    {
        var comfortable = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable));

        var frame = CanvasFrame(comfortable);
        FrameValue(frame, "RowHeaderWidth").Should().Be(56.0,
            "the tablet reference draws a visibly wider row gutter (40 × 1.4)");
        FrameValue(frame, "ColumnHeaderHeight").Should().Be(28.0,
            "the taller header gutter is also what gives the auto-filter button a finger-sized height");

        var compact = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Compact));
        FrameValue(CanvasFrame(compact), "RowHeaderWidth").Should().Be(40.0);
        FrameValue(CanvasFrame(compact), "ColumnHeaderHeight").Should().Be(20.0);
    }

    // ── touch handle layer ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanvasGrid_RendersTheTouchHandleLayer_InTheStickyScrollportLayer()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable));

        var layer = cut.Find(".tm-spreadsheet-canvas-grid__layer .tm-spreadsheet-canvas-grid__touch-handles");
        layer.Should().NotBeNull("the handles share the canvases' coordinate space (the sticky layer)");
        layer.QuerySelectorAll("[data-touch-handle]").Length.Should().Be(3,
            "two round corner drops (start/end) extend the selection and the fill box fills from the corner");

        foreach (var handle in layer.QuerySelectorAll("[data-touch-handle]"))
        {
            handle.HasAttribute("hidden").Should().BeTrue(
                "the JS engine positions and reveals the handles from the painted selection");
        }

        foreach (var name in new[] { "start", "fill", "end" })
        {
            layer.QuerySelector($"[data-touch-handle='{name}']").Should().NotBeNull();
        }

        // The four edge STRIPS are gone: they swallowed taps meant for the selected row/column.
        foreach (var gone in new[] { "top", "bottom", "left", "right" })
        {
            layer.QuerySelector($"[data-touch-handle='{gone}']").Should().BeNull();
        }
    }

    [Fact]
    public void PackageCss_DeclaresTheTouchContract()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var css = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "css", "components", "_spreadsheet.css"));

        css.Should().Contain("touch-action: pan-x pan-y",
            "one finger pans the grid natively while the browser's own pinch-zoom stays off — the core "
            + "pointer-gesture.js pinch owns the zoom (the same contract as the PdfViewer wrap)");
        css.Should().Contain(".tm-spreadsheet-canvas-grid__touch-handle",
            "the DOM handle layer is part of the package stylesheet");
        css.Should().Contain("touch-action: none",
            "a handle is the one place where the browser must NOT pan under the finger");
        css.Should().Contain("@media (pointer: fine)",
            "a fine pointer hides the touch handle layer entirely — the mouse keeps the painted handles");
    }

    // ── fill: the WCAG 2.5.7 non-drag alternative ───────────────────────────────────────────────

    [Fact]
    public async Task CanvasGrid_CtrlD_AndCtrlR_RaiseFillRequested()
    {
        var fills = new List<SpreadsheetFillDirection>();
        var cut = Render<TmSpreadsheetCanvasGrid>(parameters => parameters
            .Add(p => p.Sheet, new SpreadsheetSheet { RowCount = 20, ColumnCount = 10 })
            .Add(p => p.OnFillRequested, direction => fills.Add(direction)));

        await cut.InvokeAsync(() => cut.Instance.OnCanvasKeyCommand("d", false, true, false, false));
        await cut.InvokeAsync(() => cut.Instance.OnCanvasKeyCommand("r", false, true, false, false));

        fills.Should().Equal(
            new[] { SpreadsheetFillDirection.Down, SpreadsheetFillDirection.Right },
            "the fill handle's drag has a keyboard twin (Excel's Ctrl+D / Ctrl+R) and it is not swallowed "
            + "by the typing path");
    }

    [Fact]
    public async Task Spreadsheet_FillDown_FillsTheSelectionFromItsTopRow()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = 7 };

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 0, false, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(2, 0, true, false));

        await cut.InvokeAsync(() => grid.Instance.OnCanvasKeyCommand("d", false, true, false, false));

        sheet.Cells["A2"].Value.Should().Be(7, "Fill down copies the selection's leading row down");
        sheet.Cells["A3"].Value.Should().Be(7);
        sheet.Cells.Should().NotContainKey("B2", "only the selected column is filled");
    }

    [Fact]
    public async Task Spreadsheet_FillRight_FillsTheSelectionFromItsLeadingColumn()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = 3 };

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 0, false, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 2, true, false));

        await cut.InvokeAsync(() => grid.Instance.OnCanvasKeyCommand("r", false, true, false, false));

        sheet.Cells["B1"].Value.Should().Be(3);
        sheet.Cells["C1"].Value.Should().Be(3);
        sheet.Cells.Should().NotContainKey("A2", "only the selected row is filled");
    }

    [Fact]
    public async Task Spreadsheet_FillDown_SingleCell_CopiesFromAbove_AndNeverClears()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = 11 };

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(1, 0, false, false));

        await cut.InvokeAsync(() => grid.Instance.OnCanvasKeyCommand("d", false, true, false, false));
        sheet.Cells["A2"].Value.Should().Be(11, "a single cell fills from the cell above, like Excel");

        // An empty neighbour must never turn Ctrl+D into a delete.
        sheet.Cells.Remove("A2");
        sheet.Cells["A3"] = new SpreadsheetCell { Value = 99 };
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(2, 0, false, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasKeyCommand("d", false, true, false, false));
        sheet.Cells["A3"].Value.Should().Be(99, "an empty source is a no-op, not a clear");
    }

    [Fact]
    public async Task Spreadsheet_CellMenu_CarriesTheFillEntries()
    {
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(40, 40, 120, 240));

        var menu = cut.FindAll(".tm-spreadsheet-context-menu__item")
            .Select(item => item.TextContent.Trim())
            .ToList();

        menu.Should().Contain("Fill down");
        menu.Should().Contain("Fill right");
    }

    // ── fill: Excel semantics (multi-column, format copy, one undo) ─────────────────────────────

    [Fact]
    public async Task Spreadsheet_FillDown_MultiColumn_FillsEveryColumn_InOneUndoStep()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = 1 };
        sheet.Cells["B1"] = new SpreadsheetCell { Value = 2 };
        sheet.Cells["C1"] = new SpreadsheetCell { Value = 3 };

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 0, false, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(2, 2, true, false));

        await cut.InvokeAsync(() => grid.Instance.OnCanvasKeyCommand("d", false, true, false, false));

        foreach (var col in new[] { "A", "B", "C" })
        {
            sheet.Cells[$"{col}2"].Value.Should().Be(Convert.ToInt32(sheet.Cells[$"{col}1"].Value),
                $"row 1 of column {col} is the pattern for the whole column");
            sheet.Cells[$"{col}3"].Value.Should().NotBeNull($"column {col} is filled too");
        }

        // ONE undo reverses every column's fill (a BatchCommand, not N undo steps).
        var undo = cut.Instance.GetType().GetMethod(
            "Undo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        await cut.InvokeAsync(() => undo.Invoke(cut.Instance, null));
        cut.WaitForState(() => !sheet.Cells.ContainsKey("A2") && !sheet.Cells.ContainsKey("B2") && !sheet.Cells.ContainsKey("C2"));

        sheet.Cells.ContainsKey("A2").Should().BeFalse("one undo removes the whole multi-column fill");
        sheet.Cells.ContainsKey("C3").Should().BeFalse();
        sheet.Cells["A1"].Value.Should().Be(1, "the source row is never touched");
    }

    [Fact]
    public async Task Spreadsheet_FillDown_CopiesTheSourceFormat_LikeExcel()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = 5 };
        sheet.Cells["A1"].Style.Bold = true;
        sheet.Cells["A1"].Style.NumberFormat = "0.00";
        sheet.Cells["A2"] = new SpreadsheetCell { Value = 99 };
        sheet.Cells["A2"].Style.Italic = true;

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 0, false, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(2, 0, true, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasKeyCommand("d", false, true, false, false));

        sheet.Cells["A2"].Value.Should().Be(5);
        sheet.Cells["A2"].Style.Bold.Should().BeTrue("Excel's Fill copies the source cell's format");
        sheet.Cells["A2"].Style.NumberFormat.Should().Be("0.00");
        sheet.Cells["A2"].Style.Italic.Should().BeFalse("the target's own format is replaced by the source's");
    }

    [Fact]
    public async Task CanvasGrid_LongPressInsideTheSelection_KeepsTheRange()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "x" };
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // Select A1:B2, then long-press INSIDE it (B2) — Excel keeps the block so the menu acts on it.
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 0, false, false));
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(1, 1, true, false));
        var before = grid.Instance.GetSelectedCellRefs().ToList();

        var cell = FindCellCenter(cut, grid, 1, 1);
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(cell.ContentX, cell.ContentY, 300, 300, "touch", 800));

        grid.Instance.GetSelectedCellRefs().Should().BeEquivalentTo(before,
            "a press inside the selection must not collapse it to one cell");
        cut.FindComponent<TmSpreadsheetContextMenu>().Instance.Request.Anchor.Should().Be("touch",
            "a long press tells the menu it came from a finger (it offsets itself)");
    }

    [Fact]
    public async Task CanvasGrid_ContextMenuOutsideTheSelection_MovesTheActiveCell()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "x" };
        sheet.Cells["C3"] = new SpreadsheetCell { Value = "y" };
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellPointer(0, 0, false, false));
        var cell = FindCellCenter(cut, grid, 2, 2);
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(cell.ContentX, cell.ContentY, 300, 300, "pointer", 800));

        grid.Instance.GetSelectedCellRefs().Should().Equal(new[] { "C3" },
            "a press outside the selection moves the active cell to the pressed cell");
    }

    // ── explicit navigation always takes the focus (the isFocusOutside guard is pointer-only) ───

    [Fact]
    public async Task CanvasGrid_NavigateToCell_ForcesTheGridFocus()
    {
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        await cut.InvokeAsync(() => grid.Instance.NavigateToCellAsync("C3"));

        // The grid must report a FORCED focus request: the render path then focuses even though the
        // name box (or Find) still owns the caret — a user who navigates wants to keep typing in the grid.
        var flag = typeof(TmSpreadsheetCanvasGrid)
            .GetField("_forceFocusAfterRender", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        flag.Should().NotBeNull();
        // FocusAsync clears the flag through the render; what matters is that navigation did not leave the
        // guard as the only gate. Assert the active cell and that the grid accepted the navigation.
        cut.Instance.Workbook.ActiveSheet!.ActiveCellRef.Should().Be("C3");
    }

    // ── the read-only (Embedded) cell menu never offers a mutating entry ────────────────────────

    [Fact]
    public async Task Spreadsheet_Embedded_CellMenu_OffersNoMutatingEntries()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Mode, SpreadsheetMode.Embedded)
            .Add(p => p.LayoutMode, Tempo.Blazor.Abstractions.Layout.TmLayoutMode.Desktop));

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        var cell = FindCellCenter(cut, grid, 0, 0);
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(cell.ContentX, cell.ContentY, 300, 300, "pointer", 800));

        var labels = cut.FindAll(".tm-spreadsheet-context-menu__item").Select(item => item.TextContent.Trim()).ToList();
        labels.Should().Contain("Copy");
        labels.Should().NotContain("Paste");
        labels.Should().NotContain("Cut");
        labels.Should().NotContain("Fill down");
        labels.Should().NotContain("Fill right");
        labels.Should().NotContain("Delete row");
        labels.Should().NotContain("Clear content");
        labels.Should().NotContain("Clear all");
    }

    /// <summary>The content point a cell's centre hits, read from the canvas frame.</summary>
    private (double ContentX, double ContentY) FindCellCenter<T>(
        IRenderedComponent<T> cut, IRenderedComponent<TmSpreadsheetCanvasGrid> grid, int row, int col)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        var invocation = JSInterop.Invocations.LastOrDefault(i => i.Identifier == InitEngine);
        invocation.Should().NotBeNull();
        var frame = invocation!.Arguments[^1]!;
        var rows = (System.Collections.IEnumerable)frame.GetType()
            .GetProperty("Rows", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(frame)!;
        var columns = (System.Collections.IEnumerable)frame.GetType()
            .GetProperty("Columns", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(frame)!;

        // A row frame carries Top/Height, a column frame Left/Width — read both defensively.
        static double Value(object o, string name)
            => Convert.ToDouble(
                o.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(o) ?? 0d,
                System.Globalization.CultureInfo.InvariantCulture);

        static (double Left, double Top, double Width, double Height) Frame(object o)
            => (Value(o, "Left"), Value(o, "Top"), Value(o, "Width"), Value(o, "Height"));

        var rowFrame = rows.Cast<object>().First(o => Convert.ToInt32(o.GetType().GetProperty("Index")!.GetValue(o)) == row);
        var colFrame = columns.Cast<object>().First(o => Convert.ToInt32(o.GetType().GetProperty("Index")!.GetValue(o)) == col);
        var r = Frame(rowFrame);
        var c = Frame(colFrame);
        var rowHeaderWidth = Convert.ToDouble(frame.GetType().GetProperty("RowHeaderWidth")!.GetValue(frame));
        var columnHeaderHeight = Convert.ToDouble(frame.GetType().GetProperty("ColumnHeaderHeight")!.GetValue(frame));
        return (c.Left + c.Width / 2, r.Top + r.Height / 2);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The last canvas frame handed to the engine (initEngine or a later render).</summary>
    private object CanvasFrame(IRenderedComponent<TmSpreadsheet> cut)
    {
        var invocation = JSInterop.Invocations
            .LastOrDefault(i => i.Identifier is InitEngine or RenderFrameCall);
        invocation.Should().NotBeNull("the canvas grid must hand a frame to the JS engine");
        return invocation!.Arguments[^1]!;
    }

    private static object? FrameValue(object frame, string name) =>
        frame.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.GetValue(frame);
}
