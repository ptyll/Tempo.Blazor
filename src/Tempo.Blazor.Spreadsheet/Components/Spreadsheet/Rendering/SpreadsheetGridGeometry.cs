using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet.Rendering;

/// <summary>
/// Computes row/column dimensions, offsets, and hit testing without depending on a concrete renderer.
/// </summary>
internal sealed class SpreadsheetGridGeometry
{
    private SpreadsheetSheet? _sheet;
    private double _defaultRowHeight;
    private double _defaultColumnWidth;
    private double _zoom = 1.0;
    private double _rowScale = 1.0;
    private int _rowsHash;
    private int _columnsHash;
    private double[] _rowHeights = [];
    private double[] _columnWidths = [];
    private double[] _rowOffsets = [];
    private double[] _columnOffsets = [];

    /// <summary>Row height lookup table.</summary>
    public IReadOnlyList<double> RowHeights => _rowHeights;

    /// <summary>Column width lookup table.</summary>
    public IReadOnlyList<double> ColumnWidths => _columnWidths;

    /// <summary>Prefix sums of row heights.</summary>
    public IReadOnlyList<double> RowOffsets => _rowOffsets;

    /// <summary>Prefix sums of column widths.</summary>
    public IReadOnlyList<double> ColumnOffsets => _columnOffsets;

    /// <summary>Total sheet content width without the row header gutter.</summary>
    public double ContentWidth => _columnOffsets.Length == 0 ? 0 : _columnOffsets[^1];

    /// <summary>Total sheet content height without the column header gutter.</summary>
    public double ContentHeight => _rowOffsets.Length == 0 ? 0 : _rowOffsets[^1];

    /// <summary>The current zoom factor applied to all dimensions (1.0 = 100%).</summary>
    public double Zoom => _zoom;

    /// <summary>
    /// Ensures geometry arrays are up to date for the given sheet, defaults, zoom and density row scale.
    /// <paramref name="rowScale"/> is a RENDER multiplier (SP1 grid density): it scales the drawn row
    /// heights only, never the sheet's own <c>DefaultRowHeight</c> or a row's stored height.
    /// </summary>
    public void Update(
        SpreadsheetSheet? sheet,
        double defaultRowHeight,
        double defaultColumnWidth,
        double zoom = 1.0,
        double rowScale = 1.0)
    {
        if (sheet is null)
        {
            Clear();
            return;
        }

        zoom = zoom <= 0 ? 1.0 : zoom;
        rowScale = rowScale <= 0 ? 1.0 : rowScale;
        var rowsHash = ComputeRowsHash(sheet);
        var columnsHash = ComputeColumnsHash(sheet);
        if (ReferenceEquals(_sheet, sheet)
            && Math.Abs(_defaultRowHeight - defaultRowHeight) < double.Epsilon
            && Math.Abs(_defaultColumnWidth - defaultColumnWidth) < double.Epsilon
            && Math.Abs(_zoom - zoom) < double.Epsilon
            && Math.Abs(_rowScale - rowScale) < double.Epsilon
            && _rowHeights.Length == sheet.RowCount
            && _columnWidths.Length == sheet.ColumnCount
            && _rowsHash == rowsHash
            && _columnsHash == columnsHash)
        {
            return;
        }

        _sheet = sheet;
        _defaultRowHeight = defaultRowHeight;
        _defaultColumnWidth = defaultColumnWidth;
        _zoom = zoom;
        _rowScale = rowScale;
        _rowsHash = rowsHash;
        _columnsHash = columnsHash;

        _rowHeights = new double[sheet.RowCount];
        _rowOffsets = new double[sheet.RowCount + 1];
        for (var row = 0; row < sheet.RowCount; row++)
        {
            _rowHeights[row] = GetConfiguredRowHeight(sheet, row, defaultRowHeight) * zoom * rowScale;
            _rowOffsets[row + 1] = _rowOffsets[row] + _rowHeights[row];
        }

        _columnWidths = new double[sheet.ColumnCount];
        _columnOffsets = new double[sheet.ColumnCount + 1];
        for (var col = 0; col < sheet.ColumnCount; col++)
        {
            _columnWidths[col] = GetConfiguredColumnWidth(sheet, col, defaultColumnWidth) * zoom;
            _columnOffsets[col + 1] = _columnOffsets[col] + _columnWidths[col];
        }
    }

    /// <summary>Invalidates all cached arrays.</summary>
    public void Clear()
    {
        _sheet = null;
        _zoom = 1.0;
        _rowScale = 1.0;
        _rowsHash = 0;
        _columnsHash = 0;
        _rowHeights = [];
        _columnWidths = [];
        _rowOffsets = [];
        _columnOffsets = [];
    }

    /// <summary>Gets the effective (zoomed) row height.</summary>
    public double GetRowHeight(int row) =>
        (uint)row < (uint)_rowHeights.Length ? _rowHeights[row] : _defaultRowHeight * _zoom * _rowScale;

    /// <summary>Gets the effective (zoomed) column width.</summary>
    public double GetColumnWidth(int col) =>
        (uint)col < (uint)_columnWidths.Length ? _columnWidths[col] : _defaultColumnWidth * _zoom;

    /// <summary>Gets cumulative row height before the given row.</summary>
    public double GetCumulativeRowHeight(int upToRow) => GetOffset(_rowOffsets, upToRow);

    /// <summary>Gets cumulative column width before the given column.</summary>
    public double GetCumulativeColumnWidth(int upToCol) => GetOffset(_columnOffsets, upToCol);

    /// <summary>Finds the row at a content-space Y offset.</summary>
    public int FindRowAtOffset(double offset) => FindIndexAtOffset(_rowOffsets, offset);

    /// <summary>Finds the column at a content-space X offset.</summary>
    public int FindColumnAtOffset(double offset) => FindIndexAtOffset(_columnOffsets, offset);

    /// <summary>Hit-tests a content-space point and returns zero-based row/column indices.</summary>
    public (int Row, int Col) HitTest(double contentX, double contentY)
    {
        if (_sheet is null || contentX < 0 || contentY < 0)
            return (-1, -1);

        var row = Math.Clamp(FindRowAtOffset(contentY), 0, Math.Max(0, _sheet.RowCount - 1));
        var col = Math.Clamp(FindColumnAtOffset(contentX), 0, Math.Max(0, _sheet.ColumnCount - 1));
        return (row, col);
    }

    /// <summary>Gets the content-space rectangle for a cell.</summary>
    public (double Left, double Top, double Width, double Height) GetCellRect(int row, int col)
    {
        var left = GetCumulativeColumnWidth(col);
        var top = GetCumulativeRowHeight(row);
        return (left, top, GetColumnWidth(col), GetRowHeight(row));
    }

    /// <summary>Gets a visible row range with overscan.</summary>
    public (int Start, int End) GetVisibleRows(SpreadsheetSheet sheet, SpreadsheetViewportState viewport, int overscan)
    {
        if (sheet.RowCount == 0)
            return (0, -1);

        var start = Math.Max(0, FindRowAtOffset(Math.Max(0, viewport.ScrollTop)) - overscan);
        var end = Math.Min(sheet.RowCount - 1, FindRowAtOffset(Math.Max(0, viewport.ScrollTop + viewport.Height)) + overscan);
        return (start, Math.Max(start, end));
    }

    /// <summary>Gets a visible column range with overscan.</summary>
    public (int Start, int End) GetVisibleColumns(SpreadsheetSheet sheet, SpreadsheetViewportState viewport, int overscan)
    {
        if (sheet.ColumnCount == 0)
            return (0, -1);

        var start = Math.Max(0, FindColumnAtOffset(Math.Max(0, viewport.ScrollLeft)) - overscan);
        var end = Math.Min(sheet.ColumnCount - 1, FindColumnAtOffset(Math.Max(0, viewport.ScrollLeft + viewport.Width)) + overscan);
        return (start, Math.Max(start, end));
    }

    private static double GetConfiguredRowHeight(SpreadsheetSheet sheet, int rowIndex, double defaultRowHeight)
    {
        if (sheet.Rows.TryGetValue(rowIndex, out var row) && row.IsHidden)
            return 0;
        return sheet.Rows.TryGetValue(rowIndex, out row) && row.Height.HasValue
            ? row.Height.Value
            : defaultRowHeight;
    }

    private static double GetConfiguredColumnWidth(SpreadsheetSheet sheet, int colIndex, double defaultColumnWidth)
    {
        if (sheet.Columns.TryGetValue(colIndex, out var col) && col.IsHidden)
            return 0;
        return sheet.Columns.TryGetValue(colIndex, out col) && col.Width.HasValue
            ? col.Width.Value
            : defaultColumnWidth;
    }

    private static double GetOffset(double[] offsets, int index)
    {
        if (offsets.Length == 0)
            return 0;
        return offsets[Math.Clamp(index, 0, offsets.Length - 1)];
    }

    private static int FindIndexAtOffset(double[] offsets, double offset)
    {
        if (offsets.Length <= 1)
            return 0;

        var index = Array.BinarySearch(offsets, offset);
        if (index >= 0)
            return Math.Clamp(index, 0, offsets.Length - 2);

        index = ~index - 1;
        return Math.Clamp(index, 0, offsets.Length - 2);
    }

    private static int ComputeRowsHash(SpreadsheetSheet sheet)
    {
        var hash = new HashCode();
        hash.Add(sheet.Rows.Count);
        foreach (var (index, row) in sheet.Rows.OrderBy(kv => kv.Key))
        {
            hash.Add(index);
            hash.Add(row.Height);
            hash.Add(row.IsHidden);
        }
        return hash.ToHashCode();
    }

    private static int ComputeColumnsHash(SpreadsheetSheet sheet)
    {
        var hash = new HashCode();
        hash.Add(sheet.Columns.Count);
        foreach (var (index, column) in sheet.Columns.OrderBy(kv => kv.Key))
        {
            hash.Add(index);
            hash.Add(column.Width);
            hash.Add(column.IsHidden);
        }
        return hash.ToHashCode();
    }
}
