using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>Renders a diagram stencil shape inside a node.</summary>
public partial class TmDiagramStencilShape : ComponentBase, IAsyncDisposable
{
    /// <summary>Node rendered by this shape.</summary>
    [Parameter] public DiagramNode Node { get; set; } = default!;
    /// <summary>Whether the node is selected (enables section editing affordances).</summary>
    [Parameter] public bool IsSelected { get; set; }
    /// <summary>Raised with the port id when the pointer goes down on one of the node's ports.</summary>
    [Parameter] public EventCallback<string> OnPortMouseDownEvent { get; set; }
    /// <summary>Raised with the data key and the new value when an inline section edit commits.</summary>
    [Parameter] public EventCallback<(string DataKey, object Value)> OnSectionEdit { get; set; }
    /// <summary>Selected cells (row, column) when the node is a table.</summary>
    [Parameter] public List<(int Row, int Column)> SelectedTableCells { get; set; } = [];
    /// <summary>Page that owns the node (used for page-relative lookups).</summary>
    [Parameter] public DiagramPage? Page { get; set; }
    /// <summary>Document that owns the node (layers, styles).</summary>
    [Parameter] public DiagramDocument? Document { get; set; }

    /// <summary>The effective read-only state: inline edits (sections, swimlane and table cells)
    /// never start, and a flip closes any open inline editor without saving.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    [Inject] private DiagramStencilRegistry StencilRegistry { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private DiagramStencil? _stencil;
    private string? _editingDataKey;
    private string _editingText = "";
    private bool _editFocusPending;
    private ElementReference _editInputRef;
    private ElementReference _editTextareaRef;

    private (int Row, int Column)? _editingSwimlaneCell;
    private string _editingSwimlaneText = "";
    private bool _editSwimlaneFocusPending;
    private ElementReference _editSwimlaneInputRef;

    private (int Row, int Column)? _editingTableCell;
    private string _editingTableText = "";
    private bool _editTableFocusPending;
    private ElementReference _editTableInputRef;
    private ElementReference _shapeRef;

    // Per-node DotNetRef registered with the JS layer so that global mousedown /
    // dblclick handlers on the canvas container can address THIS shape directly
    // (e.g. trigger cell edit from JS dblclick bypass Blazor's @ondblclick which
    // is unreliable during SignalR re-renders).
    private DotNetObjectReference<TmDiagramStencilShape>? _dotNetRef;
    private string? _registeredNodeId;

    protected override void OnParametersSet()
    {
        _stencil = StencilRegistry.GetStencil(Node.StencilId);

        // A read-only flip (host parameter or the editor toggle) closes every open inline
        // editor WITHOUT saving — the blur commit path is not allowed to fire in read-only.
        if (ReadOnly)
        {
            CancelEdit();
            CancelSwimlaneEdit();
            CancelTableCellEdit();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_editFocusPending && _editingDataKey is not null)
        {
            _editFocusPending = false;
            var section = _stencil?.Layout.Sections.FirstOrDefault(s => s.DataKey == _editingDataKey);
            try
            {
                if (section?.Type == "list")
                    await _editTextareaRef.FocusAsync();
                else
                    await _editInputRef.FocusAsync();
            }
            catch { }
        }

        if (_editSwimlaneFocusPending && _editingSwimlaneCell.HasValue)
        {
            _editSwimlaneFocusPending = false;
            try
            {
                await _editSwimlaneInputRef.FocusAsync();
            }
            catch { }
        }

        if (_editTableFocusPending && _editingTableCell.HasValue)
        {
            _editTableFocusPending = false;
            try
            {
                await _editTableInputRef.FocusAsync();
            }
            catch { }
        }

        // Register this shape's DotNetRef with the JS layer so the canvas-level
        // dblclick / drill-in handlers can address it directly by Node.Id.
        // Happens on first render and whenever Node.Id changes.
        if (Node.Id != _registeredNodeId)
        {
            await UnregisterWithJsAsync();
            _dotNetRef ??= DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmDiagramStencilShape.register", Node.Id, _dotNetRef);
                _registeredNodeId = Node.Id;
            }
            catch
            {
                // JS interop may fail during prerendering or circuit tear-down; ignore.
            }
        }
    }

    private void StartEdit(string? dataKey, string text)
    {
        if (ReadOnly || Node.IsLocked) return;
        if (string.IsNullOrEmpty(dataKey)) return;
        _editingDataKey = dataKey;
        _editingText = text;
        _editFocusPending = true;
    }

    private void StartEditList(string? dataKey, IEnumerable<string> list)
    {
        if (ReadOnly || Node.IsLocked) return;
        if (string.IsNullOrEmpty(dataKey)) return;
        _editingDataKey = dataKey;
        _editingText = string.Join("\n", list);
        _editFocusPending = true;
    }

    private void SaveEdit()
    {
        if (string.IsNullOrEmpty(_editingDataKey)) return;
        var section = _stencil?.Layout.Sections.FirstOrDefault(s => s.DataKey == _editingDataKey);
        object value = section?.Type == "list"
            ? _editingText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .ToList()
            : _editingText;
        _ = OnSectionEdit.InvokeAsync((_editingDataKey, value));
        _editingDataKey = null;
        _editingText = "";
    }

    private void CancelEdit()
    {
        _editingDataKey = null;
        _editingText = "";
    }

    private void OnEditKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            SaveEdit();
        }
        else if (e.Key == "Escape")
        {
            CancelEdit();
        }
    }

    private void OnEditFocusOut(FocusEventArgs e)
    {
        if (_editingDataKey is not null)
        {
            SaveEdit();
        }
    }

    private string GetShapeStyle(bool contentBelow)
    {
        var flex = contentBelow ? "display:flex;flex-direction:column;" : "";
        return $"position:relative;width:100%;height:100%;overflow:hidden;{flex}{GetShapeStyleVars()}";
    }

    private string GetShapeStyleVars()
    {
        var layout = _stencil?.Layout;
        var fill = Node.Style.Fill ?? layout?.Fill ?? "#ffffff";
        var stroke = Node.Style.Stroke ?? layout?.Stroke ?? "#111827";
        var strokeWidth = Node.Style.StrokeWidth ?? layout?.StrokeWidth ?? 1.5;
        var opacity = Node.Style.Opacity ?? 1.0;
        var shadow = (Node.Style.HasShadow ?? false) ? "box-shadow: 2px 2px 6px rgba(0,0,0,0.25);" : "";
        var baseOpacity = $"opacity: {F(opacity)};";
        return $"--stencil-fill:{fill};--stencil-stroke:{stroke};--stencil-stroke-width:{F(strokeWidth)}px;{baseOpacity} {shadow}";
    }

    private string GetSvgBgStyle(bool contentBelow)
        => contentBelow
            ? "position:relative;flex:1 1 auto;width:100%;height:auto;overflow:hidden;"
            : "position:absolute;inset:0;width:100%;height:100%;overflow:hidden;pointer-events:none;";

    private string GetContentStyle(bool contentBelow)
    {
        var sb = new System.Text.StringBuilder();
        if (contentBelow)
        {
            sb.Append("position:relative;flex:0 0 auto;width:100%;height:auto;");
        }
        else
        {
            sb.Append("position:absolute;inset:0;");
            sb.Append("height:100%;");
        }
        sb.Append($"display:flex;justify-content:{GetVerticalAlignCss(Node.Style.VerticalAlign ?? "middle")};");
        return sb.ToString();
    }

    private string GetTextStyle(DiagramStencilTextStyle? ts)
    {
        var sb = new System.Text.StringBuilder();
        var align = Node.Style.TextAlign ?? GetTextAlign(ts?.TextAlign);
        sb.Append($"text-align: {align};");
        var color = Node.Style.Color ?? ts?.Color;
        if (color is not null) sb.Append($" color: {color};");
        var fontSize = Node.Style.FontSize ?? ts?.FontSize;
        if (fontSize is not null) sb.Append($" font-size: {F(fontSize.Value)}px;");
        var fontFamily = Node.Style.FontFamily ?? ts?.FontFamily;
        if (fontFamily is not null) sb.Append($" font-family: {fontFamily};");
        if (ts?.TextTransform is not null) sb.Append($" text-transform: {ts.TextTransform};");
        if (ts?.LetterSpacing is not null) sb.Append($" letter-spacing: {ts.LetterSpacing};");
        if (Node.Style.IsUnderline == true) sb.Append(" text-decoration: underline;");
        return sb.ToString();
    }

    private string GetTextInnerStyle(DiagramStencilTextStyle? ts)
    {
        var sb = new System.Text.StringBuilder();
        var isBold = Node.Style.IsBold ?? ts?.IsBold ?? false;
        var isItalic = Node.Style.IsItalic ?? ts?.IsItalic ?? false;
        if (isBold) sb.Append(" font-weight: 700;");
        if (isItalic) sb.Append(" font-style: italic;");
        return sb.ToString();
    }

    private static string GetTextAlign(StencilTextAlign? align)
        => align switch
        {
            StencilTextAlign.Center => "center",
            StencilTextAlign.Right => "right",
            _ => "left",
        };

    // `safe` falls back to start when the content overflows, so the first section (the UML
    // class name) stays visible and only the tail clips. Engines without `safe` drop the
    // declaration and use the initial flex-start, which is acceptable too.
    private static string GetVerticalAlignCss(string align)
        => align switch
        {
            "top" => "flex-start",
            "bottom" => "safe flex-end",
            _ => "safe center",
        };

    private string GetSectionText(DiagramStencilSection section)
    {
        if (section.DataKey is not null && Node.Data.TryGetValue(section.DataKey, out var value))
        {
            var text = value?.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return section.DefaultText ?? "";
    }

    private static bool ContainsMath(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("$$") || text.Contains("\\(") || text.Contains("\\)") || text.Contains('`');
    }

    private MarkupString GetSectionContent(DiagramStencilSection section)
    {
        var text = GetSectionText(section);
        if (Page is not null && Document is not null)
            text = PlaceholderHelper.ReplacePlaceholders(text, Page, Document);

        if (string.IsNullOrEmpty(text) || Node.Style.EnableMathJax != true || !ContainsMath(text))
            return (MarkupString)System.Net.WebUtility.HtmlEncode(text);

        return (MarkupString)$"""<span class="tm-diagram-math">{System.Net.WebUtility.HtmlEncode(text)}</span>""";
    }

    private IReadOnlyList<DiagramStencilSection> GetRenderableSections(IReadOnlyList<DiagramStencilSection> sections)
    {
        if (sections.Count == 0)
            return [];

        var visible = new bool[sections.Count];
        for (var index = 0; index < sections.Count; index++)
        {
            visible[index] = ShouldRenderSection(sections[index]);
        }

        var result = new List<DiagramStencilSection>(sections.Count);
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            if (section.Type == "divider")
            {
                if (HasRenderableContentBefore(sections, visible, index)
                    && HasRenderableContentAfter(sections, visible, index))
                {
                    result.Add(section);
                }

                continue;
            }

            if (visible[index])
                result.Add(section);
        }

        return result;
    }

    private bool ShouldRenderSection(DiagramStencilSection section)
        => section.Type != "list" || GetSectionList(section).Any();

    private static bool HasRenderableContentBefore(IReadOnlyList<DiagramStencilSection> sections, bool[] visible, int index)
    {
        for (var cursor = index - 1; cursor >= 0; cursor--)
        {
            if (sections[cursor].Type == "divider")
                continue;

            return visible[cursor];
        }

        return false;
    }

    private static bool HasRenderableContentAfter(IReadOnlyList<DiagramStencilSection> sections, bool[] visible, int index)
    {
        for (var cursor = index + 1; cursor < sections.Count; cursor++)
        {
            if (sections[cursor].Type == "divider")
                continue;

            return visible[cursor];
        }

        return false;
    }

    private IEnumerable<string> GetSectionList(DiagramStencilSection section)
    {
        if (section.DataKey is not null && Node.Data.TryGetValue(section.DataKey, out var value))
        {
            if (value is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    return je.EnumerateArray().Select(e => e.ToString()).ToList();
                }
                return [je.ToString()];
            }
            if (value is IEnumerable<string> strs)
            {
                return strs;
            }
            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                return enumerable.Cast<object>().Select(o => o?.ToString() ?? "").ToList();
            }
            var s = value?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return [s];
        }
        var def = section.DefaultText;
        if (!string.IsNullOrWhiteSpace(def)) return [def];
        return [];
    }

    private string GetSwimlaneLabel(int row, int column)
    {
        var data = Node.SwimlaneData;
        if (data is null) return "";
        int idx = row * data.ColumnCount + column;
        if (idx >= 0 && idx < data.CellLabels.Count)
            return data.CellLabels[idx];
        return $"Lane {idx + 1}";
    }

    private void StartSwimlaneEdit(int row, int column)
    {
        if (ReadOnly || Node.IsLocked) return;
        _editingSwimlaneCell = (row, column);
        _editingSwimlaneText = GetSwimlaneLabel(row, column);
        _editSwimlaneFocusPending = true;
    }

    private void SaveSwimlaneEdit()
    {
        if (_editingSwimlaneCell is not { } cell || Node.SwimlaneData is not { } data) return;
        int idx = cell.Row * data.ColumnCount + cell.Column;
        while (data.CellLabels.Count <= idx)
            data.CellLabels.Add($"Lane {data.CellLabels.Count + 1}");
        data.CellLabels[idx] = _editingSwimlaneText;
        _ = OnSectionEdit.InvokeAsync(("swimlane", data.CellLabels));
        _editingSwimlaneCell = null;
        _editingSwimlaneText = "";
    }

    private void CancelSwimlaneEdit()
    {
        _editingSwimlaneCell = null;
        _editingSwimlaneText = "";
    }

    private void OnSwimlaneEditKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            SaveSwimlaneEdit();
        }
        else if (e.Key == "Escape")
        {
            CancelSwimlaneEdit();
        }
    }

    // ── Table helpers ────────────────────────────────────────────────────────

    private int? GetTableRowCount()
    {
        if (Node.Data.TryGetValue("rowCount", out var value) && value is not null)
        {
            if (value is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
                return je.GetInt32();
            if (int.TryParse(value.ToString(), out var n)) return n;
        }
        return null;
    }

    private int? GetTableColumnCount()
    {
        if (Node.Data.TryGetValue("columnCount", out var value) && value is not null)
        {
            if (value is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
                return je.GetInt32();
            if (int.TryParse(value.ToString(), out var n)) return n;
        }
        return null;
    }

    // Cached JsonSerializerOptions for table-cell deserialisation. Using
    // PropertyNameCaseInsensitive = true lets us read back JsonElement values
    // regardless of whether they were serialised with PascalCase defaults
    // (UpdateNodeDataCommand.DeepCopy does a round-trip with defaults, yielding
    // "Row"/"Column") or camelCase from external sources (disk, remote APIs).
    private static readonly JsonSerializerOptions s_cellJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private List<DiagramTableCellData> GetTableCells()
    {
        if (Node.Data.TryGetValue("cells", out var value) && value is not null)
        {
            if (value is List<DiagramTableCellData> list) return list;
            if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    return je.Deserialize<List<DiagramTableCellData>>(s_cellJsonOptions) ?? [];
                }
                catch
                {
                    // Malformed payload — fall through to empty list rather than throw
                    // during a Blazor render pass (which would crash the whole component).
                    return [];
                }
            }
        }
        return [];
    }

    private DiagramTableCellData? GetTableCellAt(int row, int column)
    {
        var cells = GetTableCells();
        return cells.FirstOrDefault(c => c.Row == row && c.Column == column);
    }

    private bool IsTableCellCovered(int row, int column)
    {
        var cells = GetTableCells();
        foreach (var cell in cells)
        {
            if (cell.Row == row && cell.Column == column) continue;
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
            {
                for (int c = cell.Column; c < cell.Column + cell.ColSpan; c++)
                {
                    if (r == row && c == column) return true;
                }
            }
        }
        return false;
    }

    private string GetTableCellStyle(DiagramTableCellStyle? style)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("border: 1px solid var(--tm-color-border, #e5e7eb);");
        sb.Append(" padding: 4px;");
        if (!string.IsNullOrEmpty(style?.BackgroundColor))
            sb.Append($" background-color: {style.BackgroundColor};");
        if (!string.IsNullOrEmpty(style?.BorderColor))
            sb.Append($" border-color: {style.BorderColor};");
        if (!string.IsNullOrEmpty(style?.TextAlign))
            sb.Append($" text-align: {style.TextAlign};");
        if (!string.IsNullOrEmpty(style?.FontWeight))
            sb.Append($" font-weight: {style.FontWeight};");
        return sb.ToString();
    }

    private void StartTableCellEdit(int row, int column, string text)
    {
        if (ReadOnly || Node.IsLocked) return;
        _editingTableCell = (row, column);
        _editingTableText = text;
        _editTableFocusPending = true;
    }

    private void SaveTableCellEdit()
    {
        if (ReadOnly || Node.IsLocked) return;
        if (_editingTableCell is not { } cell) return;
        var cells = GetTableCells();
        var existing = cells.FirstOrDefault(c => c.Row == cell.Row && c.Column == cell.Column);
        if (existing is null)
        {
            existing = new DiagramTableCellData { Row = cell.Row, Column = cell.Column };
            cells.Add(existing);
        }
        existing.Text = _editingTableText;
        Node.Data["cells"] = cells;
        _ = OnSectionEdit.InvokeAsync(("cells", cells));
        _editingTableCell = null;
        _editingTableText = "";
    }

    private void CancelTableCellEdit()
    {
        _editingTableCell = null;
        _editingTableText = "";
    }

    private void OnTableEditKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            SaveTableCellEdit();
        }
        else if (e.Key == "Escape")
        {
            CancelTableCellEdit();
        }
    }

    private string GetTableCellSelectedClass(int row, int column)
    {
        return SelectedTableCells.Any(c => c.Row == row && c.Column == column)
            ? "tm-diagram-node__table-cell--selected"
            : "";
    }

    /// <summary>Invoked from JS when the user double-clicks a table cell on the canvas.</summary>
    [JSInvokable]
    public void StartTableCellEditFromJs(int row, int column)
    {
        if (ReadOnly || Node.IsLocked) return;
        var cell = GetTableCellAt(row, column);
        StartTableCellEdit(row, column, cell?.Text ?? string.Empty);
        StateHasChanged();
    }

    private async ValueTask UnregisterWithJsAsync()
    {
        if (_registeredNodeId is null) return;
        var id = _registeredNodeId;
        _registeredNodeId = null;
        try
        {
            await JS.InvokeVoidAsync("tmDiagramStencilShape.unregister", id);
        }
        catch
        {
            // Circuit may be disconnected; ignore.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnregisterWithJsAsync();
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
