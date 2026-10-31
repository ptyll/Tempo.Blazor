using Tempo.Blazor.Components.Spreadsheet.Commands;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Spreadsheet.Rendering;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Phase 7 (touch interaction): the non-drag half of the fill-handle gesture. WCAG 2.5.7 requires a
/// single-pointer path for anything a drag can do, so a fill is also reachable from the cell menu
/// (Fill down / Fill right) and from the Excel chords Ctrl+D / Ctrl+R.
/// </summary>
/// <remarks>
/// Excel semantics, deliberately:
/// <list type="bullet">
/// <item>Fill down copies the selection's TOP row into EVERY column of the selection (fill right copies
/// its LEFT column into every row) — one <see cref="AutoFillCommand"/> per column/row, wrapped in a single
/// <see cref="BatchCommand"/>, so one undo reverses the whole fill.</item>
/// <item>Only the source ROW/COLUMN of each strip is the pattern: the source cells themselves are never
/// written (Excel does not re-write the row the pattern came from).</item>
/// <item>The SOURCE cell's format is copied into the filled cells (<see cref="SpreadsheetCellStyle.CopyFrom"/>)
/// — this is Excel's Fill behaviour — as a <see cref="SetCellStyleCommand"/> inside the same batch, so it
/// is part of the same undo step.</item>
/// <item>A single-cell selection copies the cell above/left into it and stays a no-op when that neighbour
/// is empty — a stray Ctrl+D never wipes data.</item>
/// </list>
/// </remarks>
public partial class TmSpreadsheet
{
    /// <summary>Fills the selection (see the type remarks for the exact semantics).</summary>
    private async Task OnGridFillRequested(SpreadsheetFillDirection direction)
    {
        if (_workbook.ActiveSheet is not { } sheet || _commandManager is null || _grid is null)
            return;

        var startRef = _grid.SelectionStartRef ?? sheet.ActiveCellRef ?? "A1";
        var endRef = _grid.SelectionEndRef ?? startRef;
        var (startRow, startCol) = SpreadsheetSelectionState.ParseCellRef(startRef);
        var (endRow, endCol) = SpreadsheetSelectionState.ParseCellRef(endRef);
        var minRow = Math.Min(startRow, endRow);
        var maxRow = Math.Max(startRow, endRow);
        var minCol = Math.Min(startCol, endCol);
        var maxCol = Math.Max(startCol, endCol);

        var batch = new BatchCommand();
        var strips = 0;
        if (direction == SpreadsheetFillDirection.Down)
        {
            // Multi-row: the top row is the pattern and the rows below it are the target. Single row: the
            // row ABOVE is the pattern (Excel's Ctrl+D on one cell).
            var sourceRow = maxRow > minRow ? minRow : minRow - 1;
            if (sourceRow < 0)
                return;

            var targetTop = maxRow > minRow ? minRow + 1 : minRow;
            for (var col = minCol; col <= maxCol; col++)
            {
                var sourceRef = SpreadsheetSelectionState.ToCellRef(sourceRow, col);
                if (!HasFillSource(sheet, sourceRef))
                    continue;

                batch.Add(new AutoFillCommand(
                    sheet,
                    $"{sourceRef}:{sourceRef}",
                    $"{SpreadsheetSelectionState.ToCellRef(targetTop, col)}:{SpreadsheetSelectionState.ToCellRef(maxRow, col)}"));
                AddFillStyleCommand(batch, sheet, sourceRef, targetTop, maxRow, col, col);
                strips++;
            }
        }
        else
        {
            var sourceCol = maxCol > minCol ? minCol : minCol - 1;
            if (sourceCol < 0)
                return;

            var targetLeft = maxCol > minCol ? minCol + 1 : minCol;
            for (var row = minRow; row <= maxRow; row++)
            {
                var sourceRef = SpreadsheetSelectionState.ToCellRef(row, sourceCol);
                if (!HasFillSource(sheet, sourceRef))
                    continue;

                batch.Add(new AutoFillCommand(
                    sheet,
                    $"{sourceRef}:{sourceRef}",
                    $"{SpreadsheetSelectionState.ToCellRef(row, targetLeft)}:{SpreadsheetSelectionState.ToCellRef(row, maxCol)}"));
                AddFillStyleCommand(batch, sheet, sourceRef, row, row, targetLeft, maxCol);
                strips++;
            }
        }

        if (strips == 0)
            return;

        _commandManager.Execute(batch);

        var refs = _grid.GetSelectedCellRefs().ToList();
        InvalidateRenderedCells(refs);
        await SyncCanvasJsEngineCellsAsync(refs);
        StateHasChanged();
    }

    /// <summary>Whether a cell can act as the fill's pattern (a value or a formula).</summary>
    private static bool HasFillSource(SpreadsheetSheet sheet, string cellRef)
        => sheet.Cells.TryGetValue(cellRef, out var cell)
            && (cell.Value is not null || !string.IsNullOrEmpty(cell.Formula));

    /// <summary>Appends the Excel format copy for one filled strip to the same undo step.</summary>
    private static void AddFillStyleCommand(
        BatchCommand batch, SpreadsheetSheet sheet, string sourceRef, int fromRow, int toRow, int fromCol, int toCol)
    {
        if (!sheet.Cells.TryGetValue(sourceRef, out var sourceCell))
            return;

        var style = sourceCell.Style.Clone();
        var targetRefs = new List<string>();
        for (var row = fromRow; row <= toRow; row++)
        {
            for (var col = fromCol; col <= toCol; col++)
            {
                var cellRef = SpreadsheetSelectionState.ToCellRef(row, col);
                if (!string.Equals(cellRef, sourceRef, StringComparison.OrdinalIgnoreCase))
                    targetRefs.Add(cellRef);
            }
        }

        if (targetRefs.Count == 0)
            return;

        batch.Add(new SetCellStyleCommand(sheet, targetRefs, target => target.CopyFrom(style)));
    }
}
