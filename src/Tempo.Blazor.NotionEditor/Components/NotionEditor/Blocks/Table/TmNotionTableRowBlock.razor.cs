using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using TableColumnAlignment = Tempo.Blazor.DocumentEditor.Models.TableColumnAlignment;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Table;

public partial class TmNotionTableRowBlock : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public IPageBlock Row { get; set; } = default!;

    [Parameter] public int  RowIndex        { get; set; }
    [Parameter] public int  ColumnCount     { get; set; }

    /// <summary>Per-column horizontal alignment; columns beyond the list keep the renderer default.</summary>
    [Parameter] public IReadOnlyList<TableColumnAlignment> ColumnAlignments { get; set; } = [];

    /// <summary>Optional preferred width for each column in CSS pixels.</summary>
    [Parameter] public IReadOnlyList<double?> ColumnWidths { get; set; } = [];
    [Parameter] public bool IsHeaderRow     { get; set; }
    [Parameter] public bool HasHeaderColumn { get; set; }
    [Parameter] public bool ReadOnly        { get; set; }
    [Parameter] public bool IsDragging      { get; set; }
    [Parameter] public bool IsDragOver      { get; set; }
    [Parameter] public bool CanDelete       { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<NotionTableCell>> OnCellsChanged { get; set; }
    [Parameter] public EventCallback                        OnDeleteRequested        { get; set; }
    [Parameter] public EventCallback                        OnFocused               { get; set; }
    [Parameter] public EventCallback                        OnTabFromLastCell        { get; set; }
    [Parameter] public EventCallback                        OnShiftTabFromFirstCell  { get; set; }
    [Parameter] public EventCallback                        OnDragStart             { get; set; }
    [Parameter] public EventCallback                        OnDragOver              { get; set; }
    [Parameter] public EventCallback                        OnDrop                  { get; set; }
    [Parameter] public EventCallback                        OnDragEnd               { get; set; }
    [Parameter] public EventCallback<TableCellSelectionRequest> OnCellMouseDown      { get; set; }
    [Parameter] public EventCallback<TableCellSelectionRequest> OnCellMouseEnter     { get; set; }
    [Parameter] public EventCallback<TableCellSelectionRequest> OnCellMouseUp        { get; set; }
    [Parameter] public Func<int, int, bool>? IsCellSelected                          { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference                              _rowRef;
    private ElementReference[]                           _cellRefs     = [];
    private DotNetObjectReference<TmNotionTableRowBlock>? _dotNetRef;
    private List<NotionTableCell>                        _cells        = [];
    private int                                          _lastColumnCount;
    private bool                                         _initialized;
    private bool                                         _kbInitialized;
    private bool                                         _needsContentUpdate;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        var source = NormalizeCells(Row.Content as ITableRowBlockContent, ColumnCount);

        if (!_initialized)
        {
            _initialized     = true;
            _lastColumnCount = ColumnCount;
            _cellRefs        = new ElementReference[ColumnCount];
            _cells           = source;
            _needsContentUpdate = true;
        }
        else if (ColumnCount != _lastColumnCount || !CellsEqual(_cells, source))
        {
            var prev         = _cells;
            _lastColumnCount = ColumnCount;
            _cellRefs        = new ElementReference[ColumnCount];
            _cells           = Enumerable.Range(0, ColumnCount)
                .Select(i => i < source.Count ? source[i] : (i < prev.Count ? prev[i].Clone() : new NotionTableCell()))
                .ToList();
            _needsContentUpdate = true;
            _kbInitialized      = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ReadOnly) return;

        if (!_kbInitialized)
        {
            _kbInitialized = true;
            _dotNetRef?.Dispose();
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.initTableRowKeyboardHandler",
                    _rowRef, _dotNetRef, ColumnCount);
            }
            catch { }
        }

        if (_needsContentUpdate)
        {
            _needsContentUpdate = false;
            for (var c = 0; c < Math.Min(_cells.Count, _cellRefs.Length); c++)
            {
                if (_cells[c].IsMergeHidden) continue;
                try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _cellRefs[c], SanitizeForRender(EffectiveHtml(_cells[c]))); }
                catch { }
            }
        }
    }

    // ── Cell events ───────────────────────────────────────────────────────────

    /// <summary>
    /// A cell is written into the DOM with innerHTML, so stored markup would run on render.
    /// The editor's own inline chrome — status chips, mentions, inline math — is preserved.
    /// </summary>
    private static string SanitizeForRender(string html)
        => NotionHtmlSanitizer.SanitizeBlockContent(html);

    private static string EffectiveHtml(NotionTableCell cell)
        => cell.DisplayHtml ?? cell.Html;

    private async Task OnCellBlurAsync(int colIndex)
    {
        if (ReadOnly || colIndex >= _cellRefs.Length) return;
        string html;
        try
        {
            html = SanitizeForRender(
                await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _cellRefs[colIndex]));
        }
        catch (JSException)
        {
            return;
        }

        if (html == EffectiveHtml(_cells[colIndex])) return;
        _cells[colIndex].Html = html;
        _cells[colIndex].DisplayHtml = null;
        _cells[colIndex].Inlines = [];
        await OnCellsChanged.InvokeAsync(_cells.AsReadOnly());
    }

    private async Task OnFocusedAsync() => await OnFocused.InvokeAsync();

    private Task OnCellMouseDownAsync(int columnIndex, MouseEventArgs args)
        => OnCellMouseDown.InvokeAsync(new TableCellSelectionRequest(RowIndex, columnIndex, args.ShiftKey));

    private Task OnCellMouseEnterAsync(int columnIndex, MouseEventArgs args)
        => OnCellMouseEnter.InvokeAsync(new TableCellSelectionRequest(RowIndex, columnIndex, args.ShiftKey));

    private Task OnCellMouseUpAsync(int columnIndex, MouseEventArgs args)
        => OnCellMouseUp.InvokeAsync(new TableCellSelectionRequest(RowIndex, columnIndex, args.ShiftKey));

    private bool IsSelected(int columnIndex)
        => IsCellSelected?.Invoke(RowIndex, columnIndex) == true;

    private string CellStyle(NotionTableCell cell, int columnIndex)
    {
        var declarations = new List<string>(8);
        AddColorVariable(declarations, "--tm-notion-table-cell-background", cell.BackgroundColor);
        AddColorVariable(declarations, "--tm-notion-table-cell-text", cell.TextColor);

        var width = cell.Width ??
                    (columnIndex < ColumnWidths.Count ? ColumnWidths[columnIndex] : null);
        if (width is > 0 and <= 4096 && double.IsFinite(width.Value))
        {
            declarations.Add($"--tm-notion-table-cell-width:{width.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}px");
        }

        // The stylesheet already falls back to top via var(--tm-notion-table-cell-vertical, top),
        // so the default alignment adds no styling. Skipping it keeps the style attribute empty
        // for untouched cells — including historical cells whose unsafe CSS was dropped.
        if (cell.VerticalAlignment != NotionTableVerticalAlignment.Top)
        {
            declarations.Add($"--tm-notion-table-cell-vertical:{cell.VerticalAlignment.ToString().ToLowerInvariant()}");
        }
        AddBorder(declarations, "top", cell.Borders.Top);
        AddBorder(declarations, "right", cell.Borders.Right);
        AddBorder(declarations, "bottom", cell.Borders.Bottom);
        AddBorder(declarations, "left", cell.Borders.Left);
        return string.Join(';', declarations);
    }

    private static void AddColorVariable(List<string> declarations, string name, string? value)
    {
        if (NotionCssNormalizer.TryNormalizeColor(value, out var color) && color is not null)
        {
            declarations.Add($"{name}:{color}");
        }
    }

    private static void AddBorder(
        List<string> declarations,
        string side,
        NotionTableBorder? border)
    {
        if (border is null ||
            border.Width is <= 0 or > 32 ||
            !double.IsFinite(border.Width) ||
            !NotionCssNormalizer.TryNormalizeColor(border.Color, out var color) ||
            color is null)
        {
            return;
        }

        var style = border.Style switch
        {
            NotionTableBorderStyle.Solid => "solid",
            NotionTableBorderStyle.Dashed => "dashed",
            NotionTableBorderStyle.Dotted => "dotted",
            NotionTableBorderStyle.Double => "double",
            _ => "none"
        };
        declarations.Add(
            $"--tm-notion-table-cell-border-{side}:{border.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}px {style} {color}");
    }

    /// <summary>BEM modifier carrying the column's imported horizontal alignment, if any.</summary>
    private string AlignmentClass(int columnIndex, NotionTableCell cell)
    {
        var alignment = cell.HorizontalAlignment switch
        {
            NotionTableHorizontalAlignment.Center => TableColumnAlignment.Center,
            NotionTableHorizontalAlignment.Right => TableColumnAlignment.Right,
            _ when columnIndex < ColumnAlignments.Count => ColumnAlignments[columnIndex],
            _ => TableColumnAlignment.None
        };

        return alignment switch
        {
            TableColumnAlignment.Left => "tm-notion-table__cell-td--align-left",
            TableColumnAlignment.Center => "tm-notion-table__cell-td--align-center",
            TableColumnAlignment.Right => "tm-notion-table__cell-td--align-right",
            _ => string.Empty
        };
    }

    private static List<NotionTableCell> NormalizeCells(ITableRowBlockContent? content, int columnCount)
    {
        var rich = content?.RichCells;
        if (rich is { Count: > 0 })
        {
            var cells = rich.Select(cell => cell.Clone()).ToList();
            while (cells.Count < columnCount)
                cells.Add(new NotionTableCell());
            return cells.Take(columnCount).ToList();
        }

        return Enumerable.Range(0, columnCount)
            .Select(_ => new NotionTableCell())
            .ToList();
    }

    private static bool CellsEqual(IReadOnlyList<NotionTableCell> left, IReadOnlyList<NotionTableCell> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].Html != right[i].Html ||
                left[i].DisplayHtml != right[i].DisplayHtml ||
                left[i].ColSpan != right[i].ColSpan ||
                left[i].RowSpan != right[i].RowSpan ||
                left[i].BackgroundColor != right[i].BackgroundColor ||
                left[i].TextColor != right[i].TextColor ||
                left[i].HorizontalAlignment != right[i].HorizontalAlignment ||
                left[i].VerticalAlignment != right[i].VerticalAlignment ||
                left[i].Width != right[i].Width ||
                !BordersEqual(left[i].Borders, right[i].Borders) ||
                !InlinesEqual(left[i].Inlines, right[i].Inlines) ||
                left[i].IsMergeHidden != right[i].IsMergeHidden ||
                left[i].MergeOriginRow != right[i].MergeOriginRow ||
                left[i].MergeOriginColumn != right[i].MergeOriginColumn)
                return false;
        }

        return true;
    }

    private static bool BordersEqual(NotionTableCellBorders left, NotionTableCellBorders right)
        => BorderEqual(left.Top, right.Top) &&
           BorderEqual(left.Right, right.Right) &&
           BorderEqual(left.Bottom, right.Bottom) &&
           BorderEqual(left.Left, right.Left);

    private static bool BorderEqual(NotionTableBorder? left, NotionTableBorder? right)
        => left is null
            ? right is null
            : right is not null &&
              left.Style == right.Style &&
              left.Color == right.Color &&
              left.Width == right.Width;

    private static bool InlinesEqual(
        IReadOnlyList<NotionRichTextInline> left,
        IReadOnlyList<NotionRichTextInline> right)
        => left.Count == right.Count &&
           left.Zip(right).All(pair =>
               pair.First.Text == pair.Second.Text &&
               pair.First.Href == pair.Second.Href &&
               pair.First.Bold == pair.Second.Bold &&
               pair.First.Italic == pair.Second.Italic &&
               pair.First.Underline == pair.Second.Underline &&
               pair.First.Strikethrough == pair.Second.Strikethrough &&
               pair.First.Code == pair.Second.Code &&
               pair.First.TextColor == pair.Second.TextColor &&
               pair.First.BackgroundColor == pair.Second.BackgroundColor);

    // ── JS callbacks for Tab navigation ──────────────────────────────────────

    [JSInvokable]
    public async Task InvokeTabFromLastCell() => await OnTabFromLastCell.InvokeAsync();

    [JSInvokable]
    public async Task InvokeShiftTabFromFirstCell() => await OnShiftTabFromFirstCell.InvokeAsync();

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_kbInitialized)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyTableRowKeyboardHandler", _rowRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }
}
