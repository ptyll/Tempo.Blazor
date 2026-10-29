using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Format;
using Tempo.Blazor.Components.Spreadsheet.Formula;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Spreadsheet.Rendering;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Renders a spreadsheet sheet using a hybrid canvas surface with HTML editing and accessibility overlays.
/// </summary>
public partial class TmSpreadsheetCanvasGrid : IAsyncDisposable, ISpreadsheetGridController
{
    /// <summary>
    /// The DOM id of the focusable grid root. The host supplies a per-instance id so an overlay can
    /// name the grid as its focus-restore target (<c>TmFocusScope.RestoreFocusTargetId</c>); null
    /// renders no id, which keeps a standalone grid unchanged.
    /// </summary>
    [Parameter] public string? RootDomId { get; set; }

    private const int RowOverscanCount = 32;
    private const int ColumnOverscanCount = 10;
    private const int FormulaRefColorCount = 6;
    private readonly SpreadsheetGridGeometry _geometry = new();
    private readonly SpreadsheetSelectionState _selection = new();
    private readonly Dictionary<(int Row, int Col), SpreadsheetRange> _mergedStartLookup = [];
    private readonly HashSet<(int Row, int Col)> _mergedHiddenLookup = [];
    private readonly Dictionary<string, int> _formulaRefColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(int Sr, int Sc, int Er, int Ec, int Ci)> _formulaRangeColors = [];
    private ElementReference _rootElement;
    private ElementReference _canvasElement;
    private ElementReference _headerCanvasElement;
    private ElementReference _selectionCanvasElement;
    private ElementReference _editInput;
    private DotNetObjectReference<TmSpreadsheetCanvasGrid>? _dotNetRef;
    private SpreadsheetSheet? _geometrySheet;
    private SpreadsheetViewportState _viewport = SpreadsheetViewportState.Default;
    private double _geometryRowHeight;
    private double _geometryColumnWidth;
    private double _geometryZoom = 1.0;
    private int _geometryRowCount;
    private int _geometryColumnCount;
    private int _geometryRowsMetadataCount;
    private int _geometryColumnsMetadataCount;
    private int _geometryMergedCount;
    private int _geometryFreezeRowCount;
    private int _geometryFreezeColumnCount;
    private bool _registered;
    private bool _needsRender = true;
    private bool _shouldFocusAfterRender;
    /// <summary>
    /// Set by an EXPLICIT navigation (name box, Find next, a named range): the grid takes the focus even
    /// when the focus currently sits in another control. A pointer-originated request (a canvas click)
    /// stays guarded by <c>isFocusOutside</c>, so a late frame cannot blur what the user just focused.
    /// </summary>
    private bool _forceFocusAfterRender;
    private bool _shouldFocusEditorAfterRender;
    private string? _editValue;
    private bool _contextMenuVisible;
    private double _contextMenuX;
    private double _contextMenuY;
    private int _contextMenuRowIndex;
    private int _contextMenuColIndex;
    private bool _showColWidthDialog;
    private bool _showRowHeightDialog;
    private string _colWidthInputValue = string.Empty;
    private string _rowHeightInputValue = string.Empty;
    private long _canvasInteractionVersion;
    private long _lastCanvasCommandId;
    private bool _pointerCoarse;
    private bool _pointerCoarseResolved;
    private double _geometryDensityRowScale = 1.0;
    private bool _lastShowGridLines = true;
    private bool _lastFormatPainterActive;
    private bool _lastExternalFormulaSessionActive;
    private string? _lastExternalFormulaOriginCellRef;
    private bool _externalFormulaSessionGuardOverride;
    private string? _externalFormulaOriginCellRefOverride;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The sheet to render.</summary>
    [Parameter] public SpreadsheetSheet? Sheet { get; set; }

    /// <summary>Default row height in pixels.</summary>
    [Parameter] public double RowHeight { get; set; } = 20;

    /// <summary>Default column width in pixels.</summary>
    [Parameter] public double ColumnWidth { get; set; } = 64;

    /// <summary>The zoom factor applied to the grid geometry and fonts (1.0 = 100%).</summary>
    [Parameter] public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Requested grid density. <see cref="SpreadsheetDensity.Auto"/> is resolved against the browser's
    /// coarse-pointer media query — once before the first canvas frame and again whenever that query
    /// flips (a mouse attached to a tablet) — and scales the RENDERED row heights, fonts, handles and
    /// header gutters only (the sheet model and every XLSX height keep their values).
    /// </summary>
    [Parameter] public SpreadsheetDensity Density { get; set; } = SpreadsheetDensity.Auto;

    /// <summary>The requested density resolved against the browser's pointer kind (SP1).</summary>
    private SpreadsheetDensityScale DensityScale => SpreadsheetDensityScale.Resolve(Density, _pointerCoarse);

    /// <summary>The row-header gutter width, scaled by the render density (SP1).</summary>
    private double RowHeaderWidthPx => Math.Round(SpreadsheetGridConstants.RowHeaderWidth * DensityScale.GutterScale);

    /// <summary>The column-header gutter height, scaled by the render density (SP1).</summary>
    private double ColumnHeaderHeightPx => Math.Round(SpreadsheetGridConstants.ColumnHeaderHeight * DensityScale.GutterScale);

    /// <summary>
    /// Raised when a two-finger pinch commits a zoom factor (a hard 0.5–2.0, whole 10 % steps). The
    /// pinch is one of the WCAG 2.5.7 paths — the zoom buttons in the status bar / the phone's More
    /// menu are its single-pointer alternative and drive the same <see cref="OnZoomChanged"/>.
    /// </summary>
    [Parameter] public EventCallback<double> OnZoomChanged { get; set; }

    /// <summary>
    /// Raised when the user asks for a fill without dragging the fill handle — the cell menu's Fill
    /// down / Fill right entries or Ctrl+D / Ctrl+R. The WCAG 2.5.7 non-drag alternative to the fill
    /// handle drag; the workbook command itself lives in <see cref="TmSpreadsheet"/>.
    /// </summary>
    [Parameter] public EventCallback<SpreadsheetFillDirection> OnFillRequested { get; set; }

    /// <summary>
    /// Converts a RENDERED row height (what a canvas resize drag reports) to the model height. The
    /// geometry draws rows at <c>zoom × density row scale</c>, so a commit in rendered pixels has to be
    /// divided by both — otherwise a 1.5x comfortable grid writes 1.5x heights (and 150% zoom writes
    /// 1.5x too) into the workbook and every XLSX export.
    /// </summary>
    private double ToModelRowHeight(double renderedHeight)
    {
        var scale = Zoom * DensityScale.RowScale;
        return renderedHeight / (scale > 0.01 ? scale : 1);
    }

    /// <summary>
    /// Converts a RENDERED column width to the model width. Columns are not density-scaled, so only the
    /// zoom factor applies.
    /// </summary>
    private double ToModelColumnWidth(double renderedWidth)
    {
        var scale = Zoom > 0.01 ? Zoom : 1;
        return renderedWidth / scale;
    }

    /// <summary>The external formula editor value when reference-picking is driven by the formula bar instead of inline cell editing.</summary>
    [Parameter] public string? ExternalFormulaEditValue { get; set; }

    /// <summary>Whether a formula-bar editing session is currently active even before the latest live value is mirrored into the grid.</summary>
    [Parameter] public bool ExternalFormulaSessionActive { get; set; }

    /// <summary>The origin cell of an external formula-bar session, used as an additional guard against unintended pointer selection changes.</summary>
    [Parameter] public string? ExternalFormulaOriginCellRef { get; set; }

    /// <summary>When true the grid is displayed in read-only mode: cell editing, context menu, and editing-related callbacks are disabled.</summary>
    [Parameter] public bool IsReadonly { get; set; }

    /// <summary>
    /// The responsive layout of the hosting spreadsheet. <see cref="TmSpreadsheet"/> always passes its own
    /// resolved CONTAINER layout. STANDALONE use: <see cref="TmLayoutMode.Auto"/> falls back to the app-level
    /// VIEWPORT scope (desktop when the host provides none), because a bare grid has no parent container to
    /// measure — pass the layout explicitly when the grid lives in a narrow column, and pass
    /// <see cref="RenderContextMenu"/>="false" so the menu can be rendered outside the layout container.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The viewport's layout — only read when <see cref="LayoutMode"/> is <see cref="TmLayoutMode.Auto"/>.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Viewport { get; set; }

    /// <summary>
    /// Whether this component renders the cell context menu itself. The default keeps a STANDALONE grid working
    /// (the anchored menu on a fine pointer). <see cref="TmSpreadsheet"/> passes <c>false</c> and renders the menu
    /// (and the mobile sheet) as a SIBLING of its layout container: the container is a
    /// <c>container-type: inline-size</c> box, which makes it the containing block of the menu's
    /// <c>position: fixed</c> surface, and a menu rendered inside the scrolling grid is clipped as well.
    /// A standalone grid at a phone width must pass <c>false</c> too and render its own sheet.
    /// </summary>
    [Parameter] public bool RenderContextMenu { get; set; } = true;

    /// <summary>
    /// Raised with the gesture when <see cref="RenderContextMenu"/> is <c>false</c>: the host owns the menu. The
    /// cell (or column header) under the gesture has already become active, so the host's commands act on it.
    /// </summary>
    [Parameter] public EventCallback<SpreadsheetContextMenuRequest> OnContextMenuRequested { get; set; }

    /// <summary>Called when the active cell changes.</summary>
    [Parameter] public EventCallback<string?> ActiveCellChanged { get; set; }

    /// <summary>Called when the user Ctrl+clicks a hyperlink in a cell.</summary>
    [Parameter] public EventCallback<SpreadsheetHyperlink> OnHyperlinkClick { get; set; }

    /// <summary>Called when a cell value is committed after editing.</summary>
    [Parameter] public EventCallback<(string CellRef, string? Value)> CellValueCommitted { get; set; }

    /// <summary>Called when a batch of cell values is committed from the JavaScript engine.</summary>
    [Parameter] public EventCallback<IReadOnlyList<CanvasCellEditCommit>> CellValuesCommittedBatch { get; set; }

    /// <summary>Called when the user clicks the auto-filter dropdown button of a header cell.</summary>
    [Parameter] public EventCallback<CanvasFilterButtonClick> FilterButtonClicked { get; set; }

    /// <summary>Called when the user clicks the in-cell validation dropdown arrow (list type).</summary>
    [Parameter] public EventCallback<(int Row, int Col, double ClientX, double ClientY)> ValidationDropButtonClicked { get; set; }

    /// <summary>Called when the user requests a copy operation.</summary>
    [Parameter] public EventCallback OnCopyRequested { get; set; }

    /// <summary>Called when the user requests a paste operation.</summary>
    [Parameter] public EventCallback OnPasteRequested { get; set; }

    /// <summary>Called when the user requests a paste-special operation (Ctrl+Shift+V).</summary>
    [Parameter] public EventCallback OnPasteSpecialRequested { get; set; }

    /// <summary>Called when the user requests a cut operation.</summary>
    [Parameter] public EventCallback OnCutRequested { get; set; }

    /// <summary>Called when the user requests insert row.</summary>
    [Parameter] public EventCallback OnInsertRowRequested { get; set; }

    /// <summary>Called when the user requests delete row.</summary>
    [Parameter] public EventCallback OnDeleteRowRequested { get; set; }

    /// <summary>Called when the user requests insert column.</summary>
    [Parameter] public EventCallback OnInsertColumnRequested { get; set; }

    /// <summary>Called when the user requests delete column.</summary>
    [Parameter] public EventCallback OnDeleteColumnRequested { get; set; }

    /// <summary>Called when the user requests delete selection.</summary>
    [Parameter] public EventCallback OnDeleteRequested { get; set; }

    /// <summary>Called when the user requests undo.</summary>
    [Parameter] public EventCallback OnUndoRequested { get; set; }

    /// <summary>Called when the user requests redo.</summary>
    [Parameter] public EventCallback OnRedoRequested { get; set; }

    /// <summary>Called when the user requests bold toggle.</summary>
    [Parameter] public EventCallback OnBoldToggleRequested { get; set; }

    /// <summary>Called when the user requests italic toggle.</summary>
    [Parameter] public EventCallback OnItalicToggleRequested { get; set; }

    /// <summary>Called when the user requests underline toggle.</summary>
    [Parameter] public EventCallback OnUnderlineToggleRequested { get; set; }

    /// <summary>Called when the user requests select all.</summary>
    [Parameter] public EventCallback OnSelectAllRequested { get; set; }

    /// <summary>Called when a cell enters or exits edit mode.</summary>
    [Parameter] public EventCallback<SpreadsheetCellEditEventArgs> OnCellEdit { get; set; }

    /// <summary>Called when formula point mode inserts a cell reference.</summary>
    [Parameter] public EventCallback<string> OnCellReferenceRequested { get; set; }

    /// <summary>Called when a column is resized.</summary>
    [Parameter] public EventCallback<(int ColIndex, double Width)> OnColumnResizeRequested { get; set; }

    /// <summary>Called when a row is resized.</summary>
    [Parameter] public EventCallback<(int RowIndex, double Height)> OnRowResizeRequested { get; set; }

    /// <summary>Called when the user requests the Format Cells dialog.</summary>
    [Parameter] public EventCallback OnFormatCellsRequested { get; set; }

    /// <summary>Called when the user requests strikethrough toggle.</summary>
    [Parameter] public EventCallback OnStrikeThroughToggleRequested { get; set; }

    /// <summary>Whether Format Painter mode is active.</summary>
    [Parameter] public bool IsFormatPainterActive { get; set; }

    /// <summary>Called when Format Painter applies to a cell.</summary>
    [Parameter] public EventCallback<string> OnFormatPainterApply { get; set; }

    /// <summary>Called when Format Painter is cancelled.</summary>
    [Parameter] public EventCallback OnFormatPainterCancel { get; set; }

    /// <summary>Called when selected rows should be hidden.</summary>
    [Parameter] public EventCallback<(int Start, int End)> OnHideRowsRequested { get; set; }

    /// <summary>Called when hidden rows near the selection should be shown.</summary>
    [Parameter] public EventCallback<(int Start, int End)> OnUnhideRowsRequested { get; set; }

    /// <summary>Called when selected columns should be hidden.</summary>
    [Parameter] public EventCallback<(int Start, int End)> OnHideColumnsRequested { get; set; }

    /// <summary>Called when hidden columns near the selection should be shown.</summary>
    [Parameter] public EventCallback<(int Start, int End)> OnUnhideColumnsRequested { get; set; }

    /// <summary>Called when Format Painter is activated from context menu.</summary>
    [Parameter] public EventCallback OnFormatPainterActivateRequested { get; set; }

    /// <summary>Called when formatting should be cleared.</summary>
    [Parameter] public EventCallback OnClearFormattingRequested { get; set; }

    /// <summary>Called when content should be cleared.</summary>
    [Parameter] public EventCallback OnClearContentRequested { get; set; }

    /// <summary>Called when content and formatting should be cleared.</summary>
    [Parameter] public EventCallback OnClearAllRequested { get; set; }

    /// <summary>Called when the zoom should change by one step (+1 to zoom in, -1 to zoom out).</summary>
    [Parameter] public EventCallback<int> OnZoomStep { get; set; }

    /// <summary>Called when the zoom should reset to 100% (Ctrl+0).</summary>
    [Parameter] public EventCallback OnZoomResetRequested { get; set; }

    /// <summary>Called when the Find panel should open (Ctrl+F).</summary>
    [Parameter] public EventCallback OnFindRequested { get; set; }

    /// <summary>Called when the Replace panel should open (Ctrl+H).</summary>
    [Parameter] public EventCallback OnReplaceRequested { get; set; }

    /// <summary>Whether a cell is currently being edited.</summary>
    public bool IsEditing { get; private set; }

    /// <inheritdoc />
    public string? SelectionStartRef => _selection.SelectionStartRef;

    /// <inheritdoc />
    public string? SelectionEndRef => _selection.SelectionEndRef;

    /// <inheritdoc />
    public bool IsInFormulaPointMode => IsEditing && _editValue?.StartsWith("=") == true;

    /// <inheritdoc />
    public string? CurrentEditValue => IsEditing ? _editValue : null;

    private bool HasExternalFormulaSessionGuard => ExternalFormulaSessionActive
        || !string.IsNullOrWhiteSpace(ExternalFormulaOriginCellRef)
        || _externalFormulaSessionGuardOverride
        || !string.IsNullOrWhiteSpace(_externalFormulaOriginCellRefOverride);

    private string ActiveCellDomId => $"tm-spreadsheet-canvas-active-{GetHashCode():x}";
    private string LiveRegionDomId => $"tm-spreadsheet-canvas-live-{GetHashCode():x}";
    private string CanvasAccessibilityDescriptionDomId => $"tm-spreadsheet-canvas-a11y-{GetHashCode():x}";
    private string ColWidthInputDomId => $"tm-spreadsheet-canvas-col-width-{GetHashCode():x}";
    private string RowHeightInputDomId => $"tm-spreadsheet-canvas-row-height-{GetHashCode():x}";
    private string ActiveCellAriaText => $"{Sheet?.ActiveCellRef ?? "A1"} {GetActiveCellDisplayValue()}".Trim();
    private string LiveRegionText => ActiveCellAriaText;
    private int ActiveCellAriaRowIndex => GetActiveCellCoordinates().row + 1;
    private int ActiveCellAriaColIndex => GetActiveCellCoordinates().col + 1;
    private string TotalScrollableWidthPx => $"{_geometry.ContentWidth + RowHeaderWidthPx}px";
    private string TotalScrollableHeightPx => $"{_geometry.ContentHeight + ColumnHeaderHeightPx}px";
    private readonly record struct CanvasRowFrame(int Index, double Top, double Height, bool Frozen);
    private readonly record struct CanvasColumnFrame(int Index, double Left, double Width, string Label, bool Frozen);

    /// <summary>Represents one cell edit committed by the canvas JavaScript editor.</summary>
    public sealed class CanvasCellEditCommit
    {
        /// <summary>Zero-based row index.</summary>
        public int Row { get; set; }

        /// <summary>Zero-based column index.</summary>
        public int Col { get; set; }

        /// <summary>Committed cell value.</summary>
        public string? Value { get; set; }

        /// <summary>Canvas interaction version associated with the commit.</summary>
        public long InteractionVersion { get; set; }
    }

    /// <summary>Carries the screen position of an auto-filter dropdown button click.</summary>
    public readonly record struct CanvasFilterButtonClick(int Column, double ClientX, double ClientY);

    /// <summary>Represents one command emitted by the canvas JavaScript engine.</summary>
    public sealed class CanvasCommandLogEntry
    {
        /// <summary>Monotonic JavaScript command id.</summary>
        public long Id { get; set; }

        /// <summary>Command type, for example cellChanged or viewportSettled.</summary>
        public string? Type { get; set; }

        /// <summary>Alias for <see cref="Type"/> used by some JavaScript payloads.</summary>
        public string? Event { get; set; }

        /// <summary>Canvas interaction version associated with the command.</summary>
        public long InteractionVersion { get; set; }

        /// <summary>Viewport scroll left for viewportSettled commands.</summary>
        public double ScrollLeft { get; set; }

        /// <summary>Viewport scroll top for viewportSettled commands.</summary>
        public double ScrollTop { get; set; }

        /// <summary>Viewport client width for viewportSettled commands.</summary>
        public double ClientWidth { get; set; }

        /// <summary>Viewport client height for viewportSettled commands.</summary>
        public double ClientHeight { get; set; }

        /// <summary>Selection snapshot for selection and viewport commands.</summary>
        public CanvasSelectionSnapshot? Selection { get; set; }

        /// <summary>Cell edits carried by cellChanged, rangeChanged, or formulaCommitted commands.</summary>
        public IReadOnlyList<CanvasCellEditCommit>? CellEdits { get; set; }

        /// <summary>Resize axis for resize commands.</summary>
        public string? Axis { get; set; }

        /// <summary>Zero-based row or column index for resize commands.</summary>
        public int Index { get; set; }

        /// <summary>Committed size for resize commands.</summary>
        public double Size { get; set; }
    }

    /// <summary>Represents a canvas selection snapshot emitted by JavaScript.</summary>
    public sealed class CanvasSelectionSnapshot
    {
        /// <summary>Active row index.</summary>
        public int Row { get; set; }

        /// <summary>Active column index.</summary>
        public int Col { get; set; }

        /// <summary>Selection start row index.</summary>
        public int StartRow { get; set; }

        /// <summary>Selection start column index.</summary>
        public int StartCol { get; set; }

        /// <summary>Selection end row index.</summary>
        public int EndRow { get; set; }

        /// <summary>Selection end column index.</summary>
        public int EndCol { get; set; }
    }

    protected override void OnParametersSet()
    {
        var structureChanged = HasStructureChanged();
        if (structureChanged)
        {
            _geometry.Update(Sheet, RowHeight, ColumnWidth, Zoom, DensityScale.RowScale);
            RebuildMergedCellCache();
            CaptureStructureSignature();
            _needsRender = true;
        }

        if (Sheet is not null)
        {
            Sheet.ActiveCellRef ??= "A1";
            _selection.ActiveCellRef = Sheet.ActiveCellRef;
            _selection.SelectionStartRef ??= Sheet.ActiveCellRef;
            _selection.SelectionEndRef ??= Sheet.ActiveCellRef;
            var presentationChanged = _lastShowGridLines != Sheet.ShowGridLines
                || _lastFormatPainterActive != IsFormatPainterActive
                || _lastExternalFormulaSessionActive != ExternalFormulaSessionActive
                || !string.Equals(_lastExternalFormulaOriginCellRef, ExternalFormulaOriginCellRef, StringComparison.Ordinal);
            if (structureChanged || !_registered || presentationChanged)
                _needsRender = true;
            _lastShowGridLines = Sheet.ShowGridLines;
            _lastFormatPainterActive = IsFormatPainterActive;
            _lastExternalFormulaSessionActive = ExternalFormulaSessionActive;
            _lastExternalFormulaOriginCellRef = ExternalFormulaOriginCellRef;
        }
    }

    private bool HasStructureChanged()
    {
        if (!ReferenceEquals(_geometrySheet, Sheet))
            return true;

        if (Sheet is null)
            return false;

        return Math.Abs(_geometryRowHeight - RowHeight) > 0.01
            || Math.Abs(_geometryColumnWidth - ColumnWidth) > 0.01
            || Math.Abs(_geometryZoom - Zoom) > 0.001
            || Math.Abs(_geometryDensityRowScale - DensityScale.RowScale) > 0.001
            || _geometryRowCount != Sheet.RowCount
            || _geometryColumnCount != Sheet.ColumnCount
            || _geometryRowsMetadataCount != Sheet.Rows.Count
            || _geometryColumnsMetadataCount != Sheet.Columns.Count
            || _geometryMergedCount != Sheet.MergedCells.Count
            || _geometryFreezeRowCount != Sheet.FreezeRowCount
            || _geometryFreezeColumnCount != Sheet.FreezeColumnCount;
    }

    private void CaptureStructureSignature()
    {
        _geometrySheet = Sheet;
        _geometryRowHeight = RowHeight;
        _geometryColumnWidth = ColumnWidth;
        _geometryZoom = Zoom;
        _geometryDensityRowScale = DensityScale.RowScale;
        _geometryRowCount = Sheet?.RowCount ?? 0;
        _geometryColumnCount = Sheet?.ColumnCount ?? 0;
        _geometryRowsMetadataCount = Sheet?.Rows.Count ?? 0;
        _geometryColumnsMetadataCount = Sheet?.Columns.Count ?? 0;
        _geometryMergedCount = Sheet?.MergedCells.Count ?? 0;
        _geometryFreezeRowCount = Sheet?.FreezeRowCount ?? 0;
        _geometryFreezeColumnCount = Sheet?.FreezeColumnCount ?? 0;
    }

    /// <inheritdoc />
    public async Task FocusAsync()
    {
        try { await _rootElement.FocusAsync(); } catch { }
    }

    /// <inheritdoc />
    public async Task BeginInlineEditAsync()
    {
        if (Sheet is null)
            return;

        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.openEditorAtActive", _rootElement);
            return;
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }

        StartEdit(Sheet.ActiveCellRef ?? "A1");
    }

    /// <summary>Clears any residual formula reference highlight visuals for external formula sessions in the JavaScript engine.</summary>
    public async Task ClearEngineFormulaHighlightsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.clearFormulaReferenceHighlights", _rootElement);
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Highlights the supplied search-hit cells in the JavaScript engine, emphasising the active one.</summary>
    public async Task ApplyEngineSearchHighlightsAsync(IEnumerable<string> cellRefs, string? activeCellRef)
    {
        if (!_registered)
            return;

        var refs = cellRefs
            .Where(static cellRef => !string.IsNullOrWhiteSpace(cellRef))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.setSearchHighlights", _rootElement, new
            {
                Cells = refs,
                Active = activeCellRef
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Clears any search-hit highlights in the JavaScript engine.</summary>
    public async Task ClearEngineSearchHighlightsAsync()
    {
        if (!_registered)
            return;

        try { await JS.InvokeVoidAsync("tmSpreadsheetCanvas.clearSearchHighlights", _rootElement); }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    /// <inheritdoc />
    public async Task MoveActiveCellByAsync(int dRow, int dCol, bool extendSelection = false)
    {
        MoveActiveCell(dRow, dCol, extendSelection);
        await ApplyEngineSelectionPatchAsync();
        await FocusAsync();
    }

    /// <inheritdoc />
    public async Task NavigateToCellAsync(string cellRef)
    {
        if (Sheet is null || string.IsNullOrWhiteSpace(cellRef))
            return;

        var (row, col) = SpreadsheetSelectionState.ParseCellRef(cellRef);
        row = Math.Clamp(row, 0, Sheet.RowCount - 1);
        col = Math.Clamp(col, 0, Sheet.ColumnCount - 1);
        CommitEditIfNeeded();
        SetActiveCell(row, col, extendSelection: false, render: true);
        // An explicit navigation always lands the focus in the grid (the name box, Find next and the named
        // -range list all navigate so the user can keep typing) — see _forceFocusAfterRender.
        _forceFocusAfterRender = true;
        await ApplyEngineSelectionPatchAsync();
        await EnsureCellVisibleAsync(row, col);
        await FocusAsync();
    }

    /// <inheritdoc />
    public void SelectAllCells()
    {
        if (Sheet is null) return;
        CommitEditIfNeeded();
        _selection.SetActiveCell("A1");
        _selection.SelectionEndRef = SpreadsheetSelectionState.ToCellRef(Sheet.RowCount - 1, Sheet.ColumnCount - 1);
        Sheet.ActiveCellRef = "A1";
        _ = ActiveCellChanged.InvokeAsync(Sheet.ActiveCellRef);
        _needsRender = true;
        StateHasChanged();
    }

    /// <inheritdoc />
    public IEnumerable<string> GetSelectedCellRefs() =>
        Sheet is null ? [] : _selection.GetSelectedCellRefs(Sheet);

    /// <inheritdoc />
    public void AppendEditValue(string text)
    {
        _editValue = (_editValue ?? string.Empty) + text;
        _shouldFocusEditorAfterRender = true;
        _needsRender = true;
        StateHasChanged();
    }

    /// <inheritdoc />
    public void InsertCellRefIntoFormula(string cellRef)
    {
        _editValue = FormulaReferenceAdjuster.InsertOrReplaceLastRef(_editValue ?? "=", cellRef);
        RefreshFormulaRefColors();
        _shouldFocusEditorAfterRender = true;
        _needsRender = true;
        StateHasChanged();
    }

    /// <inheritdoc />
    public void InvalidateRenderedCells(IEnumerable<string> cellRefs)
    {
        var refs = cellRefs
            .Where(static cellRef => !string.IsNullOrWhiteSpace(cellRef))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (refs.Length == 0)
            return;

        InvalidateCanvasSnapshots(new { cells = refs });
    }

    /// <inheritdoc />
    public void InvalidateRenderedRows(IEnumerable<int> rowIndices)
    {
        var rows = rowIndices.Where(static row => row >= 0).Distinct().ToArray();
        if (rows.Length == 0)
            return;

        InvalidateCanvasSnapshots(new { rows });
    }

    /// <inheritdoc />
    public void InvalidateRenderedColumns(IEnumerable<int> columnIndices)
    {
        var columns = columnIndices.Where(static col => col >= 0).Distinct().ToArray();
        if (columns.Length == 0)
            return;

        InvalidateCanvasSnapshots(new { columns });
    }

    /// <inheritdoc />
    public void ClearRenderedCache()
    {
        InvalidateCanvasSnapshots(new { clear = true });
    }

    private void InvalidateCanvasSnapshots(object payload)
    {
        if (!_registered)
            return;

        _ = InvalidateCanvasSnapshotsAsync(payload);
    }

    private async Task InvalidateCanvasSnapshotsAsync(object payload)
    {
        try { await JS.InvokeVoidAsync("tmSpreadsheetCanvas.invalidateCellSnapshots", _rootElement, payload); }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    internal void RequestFullRender()
    {
        _needsRender = true;
    }

    /// <summary>
    /// Forces the layout geometry to be rebuilt on the next parameter set. Needed when row/column
    /// hidden state changes without the metadata count changing (e.g. auto-filter toggling
    /// <see cref="Models.SpreadsheetRow.IsHidden"/> on an existing row), which the structure-change
    /// heuristic would otherwise miss.
    /// </summary>
    internal void InvalidateGeometry()
    {
        _geometrySheet = null;
        _needsRender = true;
    }

    internal void SetExternalFormulaSessionGuard(bool active, string? originCellRef)
    {
        _externalFormulaSessionGuardOverride = active;
        _externalFormulaOriginCellRefOverride = active ? originCellRef : null;
    }

    internal async Task ApplyEngineCellPatchesAsync(IEnumerable<string> cellRefs)
    {
        if (Sheet is null)
            return;

        if (!_registered)
        {
            _needsRender = true;
            return;
        }

        var refs = cellRefs
            .Where(static cellRef => !string.IsNullOrWhiteSpace(cellRef))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (refs.Length == 0)
            return;

        var cells = refs.Select(cellRef => BuildCanvasCellPatch(cellRef)).ToArray();
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.applyCommand", _rootElement, new
            {
                Type = "upsertCells",
                Cells = cells
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    internal async Task ApplyEngineLayoutPatchesAsync(IEnumerable<int>? rowIndices = null, IEnumerable<int>? columnIndices = null)
    {
        if (Sheet is null)
            return;

        if (!_registered)
        {
            _needsRender = true;
            return;
        }

        _geometry.Update(Sheet, RowHeight, ColumnWidth, Zoom, DensityScale.RowScale);

        var rows = rowIndices?
            .Where(static row => row >= 0)
            .Distinct()
            .Select(row => BuildRow(row, frozen: IsFrozenRow(row)))
            .ToArray();
        var columns = columnIndices?
            .Where(static col => col >= 0)
            .Distinct()
            .Select(col => BuildColumn(col, frozen: IsFrozenCol(col)))
            .ToArray();

        if ((rows is null || rows.Length == 0) && (columns is null || columns.Length == 0))
            return;

        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.applyCommand", _rootElement, new
            {
                Type = "syncLayoutAxes",
                RowCount = Sheet.RowCount,
                ColumnCount = Sheet.ColumnCount,
                TotalWidth = _geometry.ContentWidth + RowHeaderWidthPx,
                TotalHeight = _geometry.ContentHeight + ColumnHeaderHeightPx,
                FreezeRowCount = Math.Clamp(Sheet.FreezeRowCount, 0, Sheet.RowCount),
                FreezeColumnCount = Math.Clamp(Sheet.FreezeColumnCount, 0, Sheet.ColumnCount),
                Rows = rows,
                Columns = columns
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    internal async Task ApplyEngineSelectionPatchAsync()
    {
        if (Sheet is null)
            return;

        if (!_registered)
        {
            _needsRender = true;
            return;
        }

        var activeRef = Sheet.ActiveCellRef ?? "A1";
        var (row, col) = SpreadsheetSelectionState.ParseCellRef(activeRef);
        var (startRow, startCol) = SpreadsheetSelectionState.ParseCellRef(_selection.SelectionStartRef ?? activeRef);
        var (endRow, endCol) = SpreadsheetSelectionState.ParseCellRef(_selection.SelectionEndRef ?? activeRef);

        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.applyCommand", _rootElement, new
            {
                Type = "syncSelection",
                ActiveCellRef = activeRef,
                Selection = new
                {
                    Row = row,
                    Col = col,
                    StartRow = startRow,
                    StartCol = startCol,
                    EndRow = endRow,
                    EndCol = endCol
                }
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    internal async Task PreviewEngineStylePatchesAsync(IEnumerable<string> cellRefs, Action<SpreadsheetCellStyle> mutate)
    {
        if (Sheet is null)
            return;

        if (!_registered)
        {
            _needsRender = true;
            return;
        }

        var refs = cellRefs
            .Where(static cellRef => !string.IsNullOrWhiteSpace(cellRef))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (refs.Length == 0)
            return;

        var cells = refs.Select(cellRef => BuildCanvasCellPatch(cellRef, mutate)).ToArray();
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.applyCommand", _rootElement, new
            {
                Type = "upsertCells",
                Cells = cells,
                SuppressRedraw = false
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    private object BuildCanvasCellPatch(string cellRef, Action<SpreadsheetCellStyle>? mutateStyle = null)
    {
        var (row, col) = SpreadsheetSelectionState.ParseCellRef(cellRef);
        SpreadsheetCell? cell = null;
        var hasCell = Sheet?.Cells.TryGetValue(cellRef, out cell) == true;
        var active = string.Equals(Sheet?.ActiveCellRef, cellRef, StringComparison.OrdinalIgnoreCase);
        var selected = IsSelected(row, col, cellRef);
        SpreadsheetCellStyle? style = null;
        if (hasCell)
            style = cell?.Style?.Clone() ?? new SpreadsheetCellStyle();
        else if (mutateStyle is not null)
            style = new SpreadsheetCellStyle();

        mutateStyle?.Invoke(style ??= new SpreadsheetCellStyle());

        // Resolve validation info for this cell (check sheet-level DataValidations)
        var validationRule = cell?.Validation
            ?? Sheet?.DataValidations.FirstOrDefault(dv => dv.Range.Contains(row, col));
        object? validationData = validationRule is null ? null : new
        {
            Type = validationRule.Type.ToString(),
            ShowDropDown = validationRule.ShowDropDown
        };

        return new
        {
            Row = row,
            Col = col,
            Ref = cellRef,
            Value = hasCell ? GetCellDisplayValue(cellRef, cell) : string.Empty,
            Formula = hasCell ? cell?.Formula : null,
            Active = active,
            Selected = selected,
            SelectionEnd = IsSelectionEndCell(row, col),
            FormulaRefColorIndex = hasCell ? GetFormulaRefColorIndex(cellRef) : -1,
            ImageUrl = hasCell ? cell?.ImageUrl : null,
            Hyperlink = hasCell ? cell?.Hyperlink?.GetUri() : null,
            Style = style is not null ? BuildCanvasStyle(style, cell) : null,
            Validation = validationData
        };
    }

    [JSInvokable]
    public Task CanvasFilterButtonClicked(int col, double clientX, double clientY)
        => FilterButtonClicked.HasDelegate
            ? FilterButtonClicked.InvokeAsync(new CanvasFilterButtonClick(col, clientX, clientY))
            : Task.CompletedTask;

    [JSInvokable]
    public Task CanvasValidationDropButtonClicked(int row, int col, double clientX, double clientY)
        => ValidationDropButtonClicked.HasDelegate
            ? ValidationDropButtonClicked.InvokeAsync((row, col, clientX, clientY))
            : Task.CompletedTask;

    /// <summary>Sends invalid-cell circle overlay data to the JS engine.</summary>
    internal async Task ApplyValidationCirclesAsync(IEnumerable<string>? invalidCellRefs)
    {
        if (!_registered) return;
        var refs = invalidCellRefs?.ToArray() ?? Array.Empty<string>();
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.applyCommand", _rootElement, new
            {
                Type = "setValidationCircles",
                Refs = refs
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    [JSInvokable]
    public Task OnCanvasViewportChanged(
        double scrollLeft,
        double scrollTop,
        double clientWidth,
        double clientHeight,
        int row,
        int col,
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        long interactionVersion)
    {
        CaptureCanvasInteractionVersion(interactionVersion);
        if (!(IsInFormulaPointMode || HasExternalFormulaSessionGuard))
            SyncSelectionFromCanvas(row, col, startRow, startCol, endRow, endCol);
        return ApplyCanvasViewportAsync(scrollLeft, scrollTop, clientWidth, clientHeight);
    }

    [JSInvokable]
    public async Task<long> OnCanvasCommandLogBatch(IReadOnlyList<CanvasCommandLogEntry>? commands)
    {
        if (commands is null || commands.Count == 0)
            return _lastCanvasCommandId;

        var shouldRender = false;
        foreach (var command in commands.OrderBy(static command => command.Id))
        {
            if (command.Id <= _lastCanvasCommandId)
                continue;

            _lastCanvasCommandId = command.Id;
            CaptureCanvasInteractionVersion(command.InteractionVersion);
            var type = command.Type ?? command.Event ?? string.Empty;
            switch (type)
            {
                case "cellChanged":
                case "rangeChanged":
                    if (command.CellEdits is not null)
                    {
                        if (CellValuesCommittedBatch.HasDelegate)
                        {
                            var commits = command.CellEdits
                                .Select(static edit => new CanvasCellEditCommit
                                {
                                    Row = edit.Row,
                                    Col = edit.Col,
                                    Value = edit.Value,
                                    InteractionVersion = edit.InteractionVersion
                                })
                                .ToArray();
                            await CellValuesCommittedBatch.InvokeAsync(commits);
                            shouldRender = true;
                        }
                        else
                        {
                            foreach (var edit in command.CellEdits)
                                shouldRender |= await CommitCanvasCellEditAsync(edit.Row, edit.Col, edit.Value);
                        }
                    }
                    break;
                case "formulaCommitted":
                    if (command.CellEdits is not null)
                    {
                        foreach (var edit in command.CellEdits)
                            shouldRender |= await CommitCanvasCellEditAsync(edit.Row, edit.Col, edit.Value);
                    }
                    break;
                case "selectionSettled":
                    if (command.Selection is not null)
                    {
                        if (!(IsInFormulaPointMode || HasExternalFormulaSessionGuard))
                        {
                            SyncSelectionFromCanvas(command.Selection.Row, command.Selection.Col, command.Selection.StartRow, command.Selection.StartCol, command.Selection.EndRow, command.Selection.EndCol);
                            await ActiveCellChanged.InvokeAsync(Sheet?.ActiveCellRef);
                        }
                    }
                    break;
                case "viewportSettled":
                    if (command.Selection is not null)
                    {
                        if (!(IsInFormulaPointMode || HasExternalFormulaSessionGuard))
                            SyncSelectionFromCanvas(command.Selection.Row, command.Selection.Col, command.Selection.StartRow, command.Selection.StartCol, command.Selection.EndRow, command.Selection.EndCol);
                    }
                    shouldRender |= ApplyCanvasViewport(command.ScrollLeft, command.ScrollTop, command.ClientWidth, command.ClientHeight);
                    break;
                case "columnResized":
                    shouldRender |= await ApplyCanvasColumnResizeAsync(command.Index, command.Size);
                    break;
                case "rowResized":
                    shouldRender |= await ApplyCanvasRowResizeAsync(command.Index, command.Size);
                    break;
            }
        }

        if (shouldRender)
            await InvokeAsync(StateHasChanged);

        return _lastCanvasCommandId;
    }

    private Task ApplyCanvasViewportAsync(double scrollLeft, double scrollTop, double clientWidth, double clientHeight)
        => ApplyCanvasViewport(scrollLeft, scrollTop, clientWidth, clientHeight)
            ? InvokeAsync(StateHasChanged)
            : Task.CompletedTask;

    private bool ApplyCanvasViewport(double scrollLeft, double scrollTop, double clientWidth, double clientHeight)
    {
        var next = new SpreadsheetViewportState(
            Math.Max(0, scrollLeft),
            Math.Max(0, scrollTop),
            clientWidth > 0 ? clientWidth : SpreadsheetViewportState.Default.Width,
            clientHeight > 0 ? clientHeight : SpreadsheetViewportState.Default.Height);

        if (Math.Abs(_viewport.ScrollLeft - next.ScrollLeft) < 0.5
            && Math.Abs(_viewport.ScrollTop - next.ScrollTop) < 0.5
            && Math.Abs(_viewport.Width - next.Width) < 0.5
            && Math.Abs(_viewport.Height - next.Height) < 0.5)
        {
            return false;
        }

        _viewport = next;
        _needsRender = true;
        return true;
    }

    private void CaptureCanvasInteractionVersion(long interactionVersion)
    {
        if (interactionVersion > _canvasInteractionVersion)
            _canvasInteractionVersion = interactionVersion;
    }

    private void SyncSelectionFromCanvas(int row, int col, int startRow, int startCol, int endRow, int endCol)
    {
        if (Sheet is null)
            return;

        row = Math.Clamp(row, 0, Sheet.RowCount - 1);
        col = Math.Clamp(col, 0, Sheet.ColumnCount - 1);
        startRow = Math.Clamp(startRow, 0, Sheet.RowCount - 1);
        startCol = Math.Clamp(startCol, 0, Sheet.ColumnCount - 1);
        endRow = Math.Clamp(endRow, 0, Sheet.RowCount - 1);
        endCol = Math.Clamp(endCol, 0, Sheet.ColumnCount - 1);

        var activeRef = SpreadsheetSelectionState.ToCellRef(row, col);
        _selection.ActiveCellRef = activeRef;
        _selection.SelectionStartRef = SpreadsheetSelectionState.ToCellRef(startRow, startCol);
        _selection.SelectionEndRef = SpreadsheetSelectionState.ToCellRef(endRow, endCol);
        Sheet.ActiveCellRef = activeRef;
    }

    [JSInvokable]
    public Task OnCanvasPointer(double contentX, double contentY, bool shiftKey, bool ctrlKey)
    {
        if (Sheet is null)
            return Task.CompletedTask;

        if (contentY < 0 && contentX >= 0)
        {
            var col = Math.Clamp(_geometry.FindColumnAtOffset(contentX), 0, Sheet.ColumnCount - 1);
            SelectColumn(col);
            return Task.CompletedTask;
        }

        if (contentX < 0 && contentY >= 0)
        {
            var row = Math.Clamp(_geometry.FindRowAtOffset(contentY), 0, Sheet.RowCount - 1);
            SelectRow(row);
            return Task.CompletedTask;
        }

        var (hitRow, hitCol) = _geometry.HitTest(contentX, contentY);
        if (hitRow < 0 || hitCol < 0)
            return Task.CompletedTask;

        var cellRef = SpreadsheetSelectionState.ToCellRef(hitRow, hitCol);
        if (IsFormatPainterActive)
        {
            SetActiveCell(hitRow, hitCol, extendSelection: false);
            _ = OnFormatPainterApply.InvokeAsync(cellRef);
            return Task.CompletedTask;
        }

        if (IsInFormulaPointMode || HasExternalFormulaSessionGuard)
        {
            if (!string.Equals(cellRef, Sheet.ActiveCellRef, StringComparison.OrdinalIgnoreCase))
            {
                return OnCellReferenceRequested.InvokeAsync(cellRef);
            }
            return Task.CompletedTask;
        }

        if (IsEditing)
            CommitEdit();

        SetActiveCell(hitRow, hitCol, shiftKey && !string.IsNullOrEmpty(_selection.SelectionStartRef));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnCanvasCellPointer(int row, int col, bool shiftKey, bool ctrlKey)
    {
        if (Sheet is null)
            return;

        row = Math.Clamp(row, 0, Sheet.RowCount - 1);
        col = Math.Clamp(col, 0, Sheet.ColumnCount - 1);
        var cellRef = SpreadsheetSelectionState.ToCellRef(row, col);

        if (IsFormatPainterActive)
        {
            SetActiveCell(row, col, extendSelection: false, render: false);
            _ = OnFormatPainterApply.InvokeAsync(cellRef);
            return;
        }

        if (IsInFormulaPointMode || HasExternalFormulaSessionGuard)
        {
            if (!string.Equals(cellRef, Sheet.ActiveCellRef, StringComparison.OrdinalIgnoreCase))
            {
                _ = OnCellReferenceRequested.InvokeAsync(cellRef);
            }
            return;
        }

        if (IsEditing)
            CommitEdit();

        SetActiveCell(row, col, shiftKey && !string.IsNullOrEmpty(_selection.SelectionStartRef), render: false);
        await ActiveCellChanged.InvokeAsync(Sheet.ActiveCellRef);

        // The JS canvas has already painted the new selection locally (same fast path
        // as keyboard navigation). Forcing _needsRender + StateHasChanged here pushed a
        // full model re-render back to the canvas on every click, which is what made
        // clicking feel sluggish versus arrow keys. Mirror OnCanvasSelectionChanged and
        // skip it; an in-progress edit still re-renders via CommitEdit above.
    }

    [JSInvokable]
    public Task OnCanvasFormulaReferenceUpdated(string referenceText)
    {
        if (string.IsNullOrWhiteSpace(referenceText))
            return Task.CompletedTask;

        return OnCellReferenceRequested.InvokeAsync(referenceText);
    }

    [JSInvokable]
    public Task HandleHyperlinkClick(int kind, string target)
    {
        var hyperlink = new SpreadsheetHyperlink
        {
            Kind = (SpreadsheetHyperlinkKind)kind,
            Target = target
        };
        return OnHyperlinkClick.InvokeAsync(hyperlink);
    }

    [JSInvokable]
    public Task OnCanvasSelectionChanged(int row, int col, int startRow, int startCol, int endRow, int endCol, long interactionVersion)
    {
        if (Sheet is null)
            return Task.CompletedTask;

        CaptureCanvasInteractionVersion(interactionVersion);
        if (IsInFormulaPointMode || HasExternalFormulaSessionGuard)
            return Task.CompletedTask;
        SyncSelectionFromCanvas(row, col, startRow, startCol, endRow, endCol);
        return ActiveCellChanged.InvokeAsync(Sheet.ActiveCellRef);
    }

    [JSInvokable]
    public Task OnCanvasDoubleClick(double contentX, double contentY)
    {
        if (Sheet is null)
            return Task.CompletedTask;

        var (row, col) = _geometry.HitTest(contentX, contentY);
        if (row >= 0 && col >= 0)
        {
            StartEdit(SpreadsheetSelectionState.ToCellRef(row, col));
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnCanvasCellDoubleClick(int row, int col)
    {
        if (Sheet is null)
            return;

        row = Math.Clamp(row, 0, Sheet.RowCount - 1);
        col = Math.Clamp(col, 0, Sheet.ColumnCount - 1);
        var cellRef = SpreadsheetSelectionState.ToCellRef(row, col);
        await InvokeAsync(() =>
        {
            StartEdit(cellRef);
            StateHasChanged();
        });
    }

    [JSInvokable]
    public async Task OnCanvasCellEditCommitted(int row, int col, string? value)
    {
        var changed = await CommitCanvasCellEditAsync(row, col, value);
        if (changed)
            await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnCanvasCellEditCommittedBatch(IReadOnlyList<CanvasCellEditCommit>? commits)
    {
        if (Sheet is null || commits is null)
            return;

        var shouldRender = false;
        foreach (var commit in commits)
        {
            CaptureCanvasInteractionVersion(commit.InteractionVersion);
            shouldRender |= await CommitCanvasCellEditAsync(commit.Row, commit.Col, commit.Value);
        }

        if (shouldRender)
            await InvokeAsync(StateHasChanged);
    }

    private async Task<bool> CommitCanvasCellEditAsync(int row, int col, string? value)
    {
        if (Sheet is null)
            return false;

        row = Math.Clamp(row, 0, Sheet.RowCount - 1);
        col = Math.Clamp(col, 0, Sheet.ColumnCount - 1);
        var cellRef = SpreadsheetSelectionState.ToCellRef(row, col);
        var previousValue = Sheet.Cells.TryGetValue(cellRef, out var cell)
            ? cell.Formula ?? cell.Value?.ToString() ?? string.Empty
            : string.Empty;
        var changed = !string.Equals(previousValue, value ?? string.Empty, StringComparison.Ordinal);

        await CellValueCommitted.InvokeAsync((cellRef, value));
        await OnCellEdit.InvokeAsync(new SpreadsheetCellEditEventArgs(Sheet, cellRef, false));
        _needsRender = changed;
        return changed;
    }

    [JSInvokable]
    public Task OnCanvasContextMenu(
        double contentX, double contentY, double clientX, double clientY, string? anchor = null, double viewportHeight = 0)
    {
        if (Sheet is null || IsInFormulaPointMode || HasExternalFormulaSessionGuard)
            return Task.CompletedTask;

        var (row, col) = _geometry.HitTest(contentX, contentY);
        var anchorKind = string.IsNullOrWhiteSpace(anchor) ? null : anchor;
        if (row >= 0 && col >= 0)
        {
            // Excel: a right click / long press INSIDE the current range keeps that range selected, so the
            // menu's commands (Fill down, Clear, Format Cells) act on the whole block. A press outside it
            // moves the active cell to the pressed cell first.
            var bounds = _selection.GetBounds();
            var insideSelection = row >= bounds.StartRow && row <= bounds.EndRow
                && col >= bounds.StartCol && col <= bounds.EndCol;
            if (!insideSelection)
                SetActiveCell(row, col, extendSelection: false);
        }

        _contextMenuRowIndex = Math.Max(0, row);
        _contextMenuColIndex = Math.Max(0, col);
        _contextMenuX = clientX;
        _contextMenuY = clientY;

        if (!RenderContextMenu)
        {
            // The host renders the menu outside the container; the gesture has already set the active cell.
            return InvokeAsync(() => OnContextMenuRequested.InvokeAsync(new SpreadsheetContextMenuRequest
            {
                Row = _contextMenuRowIndex,
                Column = _contextMenuColIndex,
                ClientX = clientX,
                ClientY = clientY,
                Anchor = anchorKind,
                ViewportHeight = viewportHeight,
            }));
        }

        _contextMenuVisible = true;
        _needsRender = true;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnCanvasColumnResize(int colIndex, double width)
    {
        return ApplyCanvasColumnResizeAsync(colIndex, width);
    }

    [JSInvokable]
    public Task OnCanvasRowResize(int rowIndex, double height)
    {
        return ApplyCanvasRowResizeAsync(rowIndex, height);
    }

    private async Task<bool> ApplyCanvasColumnResizeAsync(int colIndex, double width)
    {
        if (Sheet is null)
            return false;

        _geometry.Clear();
        if (OnColumnResizeRequested.HasDelegate)
        {
            await OnColumnResizeRequested.InvokeAsync((
                Math.Clamp(colIndex, 0, Sheet.ColumnCount - 1),
                Math.Max(16, ToModelColumnWidth(width))));
        }
        _needsRender = true;
        return true;
    }

    private async Task<bool> ApplyCanvasRowResizeAsync(int rowIndex, double height)
    {
        if (Sheet is null)
            return false;

        _geometry.Clear();
        if (OnRowResizeRequested.HasDelegate)
        {
            await OnRowResizeRequested.InvokeAsync((
                Math.Clamp(rowIndex, 0, Sheet.RowCount - 1),
                Math.Max(8, ToModelRowHeight(height))));
        }
        _needsRender = true;
        return true;
    }

    /// <summary>Handles non-navigation keyboard commands forwarded by the canvas hot path.</summary>
    [JSInvokable]
    public Task OnCanvasKeyCommand(string? key, bool shiftKey, bool ctrlKey, bool altKey, bool metaKey)
    {
        if (IsEditing || Sheet is null)
            return Task.CompletedTask;

        var shouldRender = HandleCanvasKeyCommand(key ?? string.Empty, shiftKey, ctrlKey, altKey, metaKey);
        return shouldRender ? InvokeAsync(StateHasChanged) : Task.CompletedTask;
    }

    /// <summary>Handles a Ctrl+wheel zoom gesture forwarded from the canvas.</summary>
    [JSInvokable]
    public Task OnCanvasZoomDelta(int direction)
        => OnZoomStep.InvokeAsync(direction);

    /// <summary>
    /// Handles a two-finger pinch that committed a zoom factor. The canvas has already clamped and
    /// quantized it (0.5–2.0 in 10 % steps), so the component only applies it — the same
    /// <see cref="OnZoomChanged"/> the status-bar zoom buttons and the phone's More menu drive.
    /// </summary>
    [JSInvokable]
    public Task OnCanvasZoomChanged(double zoom)
        => OnZoomChanged.HasDelegate ? OnZoomChanged.InvokeAsync(zoom) : Task.CompletedTask;

    /// <summary>
    /// Re-resolves the Auto density when the browser's coarse-pointer media query flips (a mouse attached
    /// to a tablet, a 2-in-1 undocked). The grid re-measures with the new render scales and repaints; a
    /// forced <see cref="SpreadsheetDensity.Compact"/>/<see cref="SpreadsheetDensity.Comfortable"/> is
    /// unaffected.
    /// </summary>
    [JSInvokable]
    public Task OnCanvasPointerKindChanged(bool coarsePointer)
    {
        _pointerCoarseResolved = true;
        if (_pointerCoarse == coarsePointer)
            return Task.CompletedTask;

        _pointerCoarse = coarsePointer;
        if (Sheet is not null && Math.Abs(DensityScale.RowScale - _geometryDensityRowScale) > 0.001)
        {
            _geometry.Update(Sheet, RowHeight, ColumnWidth, Zoom, DensityScale.RowScale);
            RebuildMergedCellCache();
            CaptureStructureSignature();
        }

        _needsRender = true;
        return InvokeAsync(StateHasChanged);
    }

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (IsEditing || Sheet is null)
            return;

        if (HandleCanvasKeyCommand(e.Key, e.ShiftKey, e.CtrlKey, e.AltKey, e.MetaKey))
            StateHasChanged();
    }

    private bool HandleCanvasKeyCommand(string key, bool shiftKey, bool ctrlKey, bool altKey, bool metaKey)
    {
        if (Sheet is null)
            return false;

        if (ctrlKey || metaKey)
        {
            var shortcutKey = key.Length == 1 ? key.ToLowerInvariant() : key;
            switch (shortcutKey)
            {
                case "c": _ = OnCopyRequested.InvokeAsync(); return false;
                case "v":
                    if (shiftKey) _ = OnPasteSpecialRequested.InvokeAsync();
                    else _ = OnPasteRequested.InvokeAsync();
                    return false;
                case "x": _ = OnCutRequested.InvokeAsync(); return false;
                case "z": _ = OnUndoRequested.InvokeAsync(); return false;
                case "y": _ = OnRedoRequested.InvokeAsync(); return false;
                case "b": _ = OnBoldToggleRequested.InvokeAsync(); return false;
                case "i": _ = OnItalicToggleRequested.InvokeAsync(); return false;
                case "u": _ = OnUnderlineToggleRequested.InvokeAsync(); return false;
                case "a": _ = OnSelectAllRequested.InvokeAsync(); return false;
                case "1": _ = OnFormatCellsRequested.InvokeAsync(); return false;
                case "5": _ = OnStrikeThroughToggleRequested.InvokeAsync(); return false;
                case "f": _ = OnFindRequested.InvokeAsync(); return false;
                case "h": _ = OnReplaceRequested.InvokeAsync(); return false;
                case "0": _ = OnZoomResetRequested.InvokeAsync(); return false;
                // Fill without the fill-handle drag (WCAG 2.5.7): Ctrl+D / Ctrl+R, the Excel chords.
                case "d": _ = OnFillRequested.InvokeAsync(SpreadsheetFillDirection.Down); return false;
                case "r": _ = OnFillRequested.InvokeAsync(SpreadsheetFillDirection.Right); return false;
                case "Home": MoveToCell(0, 0, shiftKey); return true;
                case "End": MoveToLastUsedCell(shiftKey); return true;
            }
        }

        switch (key)
        {
            case "ArrowUp": MoveActiveCell(-1, 0, shiftKey); break;
            case "ArrowDown": MoveActiveCell(1, 0, shiftKey); break;
            case "ArrowLeft": MoveActiveCell(0, -1, shiftKey); break;
            case "ArrowRight": MoveActiveCell(0, 1, shiftKey); break;
            case "Tab": MoveActiveCell(0, shiftKey ? -1 : 1); break;
            case "Home":
                var (row, _) = SpreadsheetSelectionState.ParseCellRef(Sheet.ActiveCellRef ?? "A1");
                MoveToCell(row, 0, shiftKey);
                break;
            case "End":
                var (activeRow, _) = SpreadsheetSelectionState.ParseCellRef(Sheet.ActiveCellRef ?? "A1");
                MoveToCell(activeRow, Sheet.ColumnCount - 1, shiftKey);
                break;
            case "Enter":
            case "F2":
                StartEdit(Sheet.ActiveCellRef ?? "A1");
                break;
            case "Escape":
                if (IsFormatPainterActive)
                    _ = OnFormatPainterCancel.InvokeAsync();
                else
                    _selection.SelectionEndRef = _selection.SelectionStartRef;
                break;
            case "Delete":
                _ = OnDeleteRequested.InvokeAsync();
                break;
            default:
                if (key.Length == 1 && !altKey && !ctrlKey && !metaKey)
                    StartEdit(Sheet.ActiveCellRef ?? "A1", key);
                else
                    return false;
                break;
        }

        _needsRender = true;
        return true;
    }

    private void MoveActiveCell(int dRow, int dCol, bool extendSelection = false)
    {
        if (Sheet is null) return;
        var (row, col) = SpreadsheetSelectionState.ParseCellRef(Sheet.ActiveCellRef ?? "A1");
        MoveToCell(Math.Clamp(row + dRow, 0, Sheet.RowCount - 1), Math.Clamp(col + dCol, 0, Sheet.ColumnCount - 1), extendSelection);
    }

    private void MoveToCell(int row, int col, bool extendSelection = false)
    {
        SetActiveCell(row, col, extendSelection);
        _ = EnsureCellVisibleAsync(row, col);
    }

    private void MoveToLastUsedCell(bool extendSelection = false)
    {
        if (Sheet is null) return;
        var lastRow = 0;
        var lastCol = 0;
        foreach (var cellRef in Sheet.Cells.Keys)
        {
            var (row, col) = SpreadsheetSelectionState.ParseCellRef(cellRef);
            if (row > lastRow) lastRow = row;
            if (col > lastCol) lastCol = col;
        }
        MoveToCell(lastRow, lastCol, extendSelection);
    }

    private void SetActiveCell(int row, int col, bool extendSelection)
        => SetActiveCell(row, col, extendSelection, render: true);

    private void SetActiveCell(int row, int col, bool extendSelection, bool render)
    {
        if (Sheet is null) return;
        var cellRef = SpreadsheetSelectionState.ToCellRef(row, col);
        if (extendSelection)
            _selection.ExtendTo(cellRef);
        else
            _selection.SetActiveCell(cellRef);

        Sheet.ActiveCellRef = cellRef;
        _shouldFocusAfterRender = true;
        if (render)
        {
            _ = ActiveCellChanged.InvokeAsync(Sheet.ActiveCellRef);
            _needsRender = true;
            StateHasChanged();
        }
    }

    private void SelectRow(int rowIndex)
    {
        if (Sheet is null) return;
        CommitEditIfNeeded();
        var startRef = SpreadsheetSelectionState.ToCellRef(rowIndex, 0);
        _selection.SetActiveCell(startRef);
        _selection.SelectionEndRef = SpreadsheetSelectionState.ToCellRef(rowIndex, Sheet.ColumnCount - 1);
        Sheet.ActiveCellRef = startRef;
        _ = ActiveCellChanged.InvokeAsync(Sheet.ActiveCellRef);
        _needsRender = true;
        StateHasChanged();
    }

    private void SelectColumn(int colIndex)
    {
        if (Sheet is null) return;
        CommitEditIfNeeded();
        var startRef = SpreadsheetSelectionState.ToCellRef(0, colIndex);
        _selection.SetActiveCell(startRef);
        _selection.SelectionEndRef = SpreadsheetSelectionState.ToCellRef(Sheet.RowCount - 1, colIndex);
        Sheet.ActiveCellRef = startRef;
        _ = ActiveCellChanged.InvokeAsync(Sheet.ActiveCellRef);
        _needsRender = true;
        StateHasChanged();
    }

    private void StartEdit(string cellRef, string? initialValue = null)
    {
        if (Sheet is null)
            return;

        if (!string.Equals(Sheet.ActiveCellRef, cellRef, StringComparison.OrdinalIgnoreCase))
        {
            var (row, col) = SpreadsheetSelectionState.ParseCellRef(cellRef);
            SetActiveCell(row, col, extendSelection: false, render: false);
        }

        IsEditing = true;
        _editValue = initialValue;
        if (_editValue is null && Sheet.Cells.TryGetValue(cellRef, out var cell))
            _editValue = Format.SpreadsheetCellEditText.GetEditText(cell, System.Globalization.CultureInfo.CurrentCulture);
        RefreshFormulaRefColors();

        _shouldFocusEditorAfterRender = true;
        _needsRender = true;
        _ = OnCellEdit.InvokeAsync(new SpreadsheetCellEditEventArgs(Sheet, cellRef, true));
        StateHasChanged();
    }

    private void CommitEditIfNeeded()
    {
        if (IsEditing)
            CommitEdit();
    }

    private void CommitEdit()
    {
        if (!IsEditing)
            return;

        IsEditing = false;
        ClearFormulaRefColors();
        var cellRef = Sheet?.ActiveCellRef;
        if (cellRef is not null && _editValue is not null)
            _ = CellValueCommitted.InvokeAsync((cellRef, _editValue));

        _ = OnCellEdit.InvokeAsync(new SpreadsheetCellEditEventArgs(Sheet!, cellRef ?? "A1", false));
        _editValue = null;
        _shouldFocusAfterRender = true;
        _needsRender = true;
        StateHasChanged();
    }

    private void CancelEdit()
    {
        if (!IsEditing)
            return;

        IsEditing = false;
        ClearFormulaRefColors();
        _ = OnCellEdit.InvokeAsync(new SpreadsheetCellEditEventArgs(Sheet!, Sheet?.ActiveCellRef ?? "A1", false));
        _editValue = null;
        _shouldFocusAfterRender = true;
        _needsRender = true;
        StateHasChanged();
    }

    private async Task EnsureCellVisibleAsync(int row, int col)
    {
        var rect = _geometry.GetCellRect(row, col);
        var request = new
        {
            Left = RowHeaderWidthPx + rect.Left,
            Top = ColumnHeaderHeightPx + rect.Top,
            Right = RowHeaderWidthPx + rect.Left + rect.Width,
            Bottom = ColumnHeaderHeightPx + rect.Top + rect.Height,
            FrozenRow = IsFrozenRow(row),
            FrozenColumn = IsFrozenCol(col)
        };

        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetCanvas.ensureCellVisible", _rootElement, request, new
            {
                RowHeaderWidthPx,
                ColumnHeaderHeightPx
            });
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    private string GetActiveCellDisplayValue()
    {
        if (Sheet?.ActiveCellRef is null)
            return string.Empty;

        var cell = Sheet.Cells.GetValueOrDefault(Sheet.ActiveCellRef);
        return GetCellDisplayValue(Sheet.ActiveCellRef, cell);
    }

    private string GetCellDisplayValue(string cellRef, SpreadsheetCell? cell)
    {
        if (cell is null)
            return string.Empty;

        if (!string.IsNullOrEmpty(cell.Formula) && (cell.DisplayValue is null || cell.Value is null))
            Sheet?.EvaluateFormula(cellRef);

        return !string.IsNullOrEmpty(cell.DisplayValue)
            ? cell.DisplayValue
            : SpreadsheetNumberFormatter.Format(cell.Value, cell.Style.NumberFormat) ?? string.Empty;
    }

    private (int row, int col) GetActiveCellCoordinates()
    {
        if (Sheet?.ActiveCellRef is null)
            return (0, 0);

        return SpreadsheetSelectionState.ParseCellRef(Sheet.ActiveCellRef);
    }

    private object BuildCanvasFrame()
    {
        var sheet = Sheet!;
        var rows = GetRenderRows(sheet).ToArray();
        var columns = GetRenderColumns(sheet).ToArray();
        var cells = BuildRenderCells(sheet, rows, columns).ToArray();
        var selection = _selection.GetBounds();

        return new
        {
            RowCount = sheet.RowCount,
            ColumnCount = sheet.ColumnCount,
            RowHeaderWidth = RowHeaderWidthPx,
            ColumnHeaderHeight = ColumnHeaderHeightPx,
            ScrollLeft = _viewport.ScrollLeft,
            ScrollTop = _viewport.ScrollTop,
            ViewportWidth = _viewport.Width,
            ViewportHeight = _viewport.Height,
            TotalWidth = _geometry.ContentWidth + RowHeaderWidthPx,
            TotalHeight = _geometry.ContentHeight + ColumnHeaderHeightPx,
            ShowGridLines = sheet.ShowGridLines,
            Zoom = _geometry.Zoom,
            Density = Density.ToString(),
            DensityResolved = DensityScale.Resolved.ToString(),
            DensityRowScale = DensityScale.RowScale,
            DensityFontScale = DensityScale.FontScale,
            DensityHandleScale = DensityScale.HandleScale,
            // The canvas never multiplies a gutter itself (RowHeaderWidth/ColumnHeaderHeight above are
            // already scaled); this is the parity contract the node lane and the perf/UX evidence read.
            DensityGutterScale = DensityScale.GutterScale,
            PointerCoarse = _pointerCoarse,
            FreezeRowCount = Math.Clamp(sheet.FreezeRowCount, 0, sheet.RowCount),
            FreezeColumnCount = Math.Clamp(sheet.FreezeColumnCount, 0, sheet.ColumnCount),
            ActiveCellRef = sheet.ActiveCellRef,
            IsFormulaPointMode = IsInFormulaPointMode,
            ExternalFormulaSessionActive,
            ExternalFormulaOriginCellRef,
            IsFormatPainterActive,
            UseJsEngine = true,
            InteractionVersion = _canvasInteractionVersion,
            Selection = new { selection.StartRow, selection.StartCol, selection.EndRow, selection.EndCol },
            Rows = rows,
            Columns = columns,
            Cells = cells
        };
    }

    private IEnumerable<CanvasRowFrame> GetRenderRows(SpreadsheetSheet sheet)
    {
        var seen = new HashSet<int>();
        var frozenCount = Math.Clamp(sheet.FreezeRowCount, 0, sheet.RowCount);
        for (var row = 0; row < frozenCount; row++)
        {
            if (seen.Add(row))
                yield return BuildRow(row, frozen: true);
        }

        var (start, end) = _geometry.GetVisibleRows(sheet, _viewport, RowOverscanCount);
        for (var row = start; row <= end; row++)
        {
            if (seen.Add(row))
                yield return BuildRow(row, frozen: false);
        }
    }

    private CanvasRowFrame BuildRow(int row, bool frozen) => new(
        row,
        _geometry.GetCumulativeRowHeight(row),
        _geometry.GetRowHeight(row),
        frozen);

    private IEnumerable<CanvasColumnFrame> GetRenderColumns(SpreadsheetSheet sheet)
    {
        var seen = new HashSet<int>();
        var frozenCount = Math.Clamp(sheet.FreezeColumnCount, 0, sheet.ColumnCount);
        for (var col = 0; col < frozenCount; col++)
        {
            if (seen.Add(col))
                yield return BuildColumn(col, frozen: true);
        }

        var viewport = _viewport with { Width = Math.Max(0, _viewport.Width - RowHeaderWidthPx) };
        var (start, end) = _geometry.GetVisibleColumns(sheet, viewport, ColumnOverscanCount);
        for (var col = start; col <= end; col++)
        {
            if (seen.Add(col))
                yield return BuildColumn(col, frozen: false);
        }
    }

    private CanvasColumnFrame BuildColumn(int col, bool frozen) => new(
        col,
        _geometry.GetCumulativeColumnWidth(col),
        _geometry.GetColumnWidth(col),
        SpreadsheetRange.ColumnIndexToLetters(col),
        frozen);

    private IEnumerable<object> BuildRenderCells(SpreadsheetSheet sheet, IReadOnlyList<CanvasRowFrame> rows, IReadOnlyList<CanvasColumnFrame> columns)
    {
        foreach (var row in rows)
        {
            var rowIndex = row.Index;
            if (_geometry.GetRowHeight(rowIndex) <= 0)
                continue;

            foreach (var column in columns)
            {
                var colIndex = column.Index;
                if (_geometry.GetColumnWidth(colIndex) <= 0 || _mergedHiddenLookup.Contains((rowIndex, colIndex)))
                    continue;

                var cellRef = SpreadsheetSelectionState.ToCellRef(rowIndex, colIndex);
                sheet.Cells.TryGetValue(cellRef, out var cell);
                var merged = _mergedStartLookup.GetValueOrDefault((rowIndex, colIndex));
                var rect = GetMergedAwareRect(rowIndex, colIndex, merged);
                var selected = IsSelected(rowIndex, colIndex, cellRef);
                yield return new
                {
                    Row = rowIndex,
                    Col = colIndex,
                    Ref = cellRef,
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height,
                    Value = GetCellDisplayValue(cellRef, cell),
                    Formula = cell?.Formula,
                    Active = string.Equals(sheet.ActiveCellRef, cellRef, StringComparison.OrdinalIgnoreCase),
                    Selected = selected,
                    SelectionEnd = IsSelectionEndCell(rowIndex, colIndex),
                    FormulaRefColorIndex = GetFormulaRefColorIndex(cellRef),
                    ImageUrl = cell?.ImageUrl,
                    Hyperlink = cell?.Hyperlink?.GetUri(),
                    Filter = BuildFilterButtonPayload(sheet, rowIndex, colIndex),
                    Style = BuildCanvasStyle(cell?.Style, cell)
                };
            }
        }
    }

    private static object? BuildFilterButtonPayload(SpreadsheetSheet sheet, int rowIndex, int colIndex)
    {
        var filter = sheet.AutoFilter;
        if (filter is null
            || rowIndex != filter.HeaderRow
            || colIndex < filter.Range.StartCol
            || colIndex > filter.Range.EndCol)
        {
            return null;
        }

        var active = filter.GetColumn(colIndex)?.IsActive ?? false;
        return new { Active = active };
    }

    private (double Left, double Top, double Width, double Height) GetMergedAwareRect(int row, int col, SpreadsheetRange? merged)
    {
        var rect = _geometry.GetCellRect(row, col);
        if (merged is null)
            return rect;

        var width = rect.Width;
        var height = rect.Height;
        for (var c = merged.StartCol + 1; c <= merged.EndCol; c++)
            width += _geometry.GetColumnWidth(c);
        for (var r = merged.StartRow + 1; r <= merged.EndRow; r++)
            height += _geometry.GetRowHeight(r);
        return (rect.Left, rect.Top, width, height);
    }

    private static object? BuildCanvasStyle(SpreadsheetCellStyle? style, SpreadsheetCell? cell)
    {
        if (style is null || !HasCanvasStylePayload(style, cell))
            return null;

        return new
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline || style.DoubleUnderline,
            DoubleUnderline = style.DoubleUnderline,
            StrikeThrough = style.StrikeThrough,
            ForeColor = IsDefaultForeColor(style.ForeColor) ? null : style.ForeColor,
            BackgroundColor = string.IsNullOrWhiteSpace(style.BackgroundColor) || style.BackgroundColor == "transparent" ? null : style.BackgroundColor,
            HorizontalAlign = GetEffectiveHAlign(style, cell).ToString().ToLowerInvariant(),
            VerticalAlign = style.VerticalAlign.ToString().ToLowerInvariant(),
            NumberFormat = style.NumberFormat,
            TextWrap = style.TextWrap,
            BorderTop = BuildCanvasBorder(style.BorderTop),
            BorderRight = BuildCanvasBorder(style.BorderRight),
            BorderBottom = BuildCanvasBorder(style.BorderBottom),
            BorderLeft = BuildCanvasBorder(style.BorderLeft)
        };
    }

    private static bool HasCanvasStylePayload(SpreadsheetCellStyle style, SpreadsheetCell? cell) =>
        cell?.Value is not null
        || !string.IsNullOrEmpty(cell?.DisplayValue)
        || !string.IsNullOrEmpty(cell?.Formula)
        || cell?.Hyperlink is not null
        || !string.IsNullOrEmpty(cell?.ImageUrl)
        || style.Bold
        || style.Italic
        || style.Underline
        || style.DoubleUnderline
        || style.StrikeThrough
        || style.TextWrap
        || style.HorizontalAlign != SpreadsheetHorizontalAlign.General
        || style.VerticalAlign != SpreadsheetVerticalAlign.Bottom
        || !string.Equals(style.FontFamily, "Calibri", StringComparison.OrdinalIgnoreCase)
        || Math.Abs(style.FontSize - 11) > 0.01
        || !IsDefaultForeColor(style.ForeColor)
        || (!string.IsNullOrWhiteSpace(style.BackgroundColor) && style.BackgroundColor != "transparent")
        || style.BorderTop.Style != SpreadsheetBorderStyle.None
        || style.BorderRight.Style != SpreadsheetBorderStyle.None
        || style.BorderBottom.Style != SpreadsheetBorderStyle.None
        || style.BorderLeft.Style != SpreadsheetBorderStyle.None;

    private static bool IsDefaultForeColor(string? color) =>
        string.IsNullOrWhiteSpace(color)
        || string.Equals(color, "#000000", StringComparison.OrdinalIgnoreCase)
        || string.Equals(color, "black", StringComparison.OrdinalIgnoreCase);

    private static object BuildCanvasBorder(SpreadsheetBorder border) => new
    {
        Style = border.Style.ToString().ToLowerInvariant(),
        Color = border.Color
    };

    private static SpreadsheetHorizontalAlign GetEffectiveHAlign(SpreadsheetCellStyle style, SpreadsheetCell? cell)
    {
        if (style.HorizontalAlign != SpreadsheetHorizontalAlign.General)
            return style.HorizontalAlign;
        return cell?.Value switch
        {
            double or decimal or float or int or long or short or byte => SpreadsheetHorizontalAlign.Right,
            DateTime => SpreadsheetHorizontalAlign.Right,
            bool => SpreadsheetHorizontalAlign.Center,
            _ => SpreadsheetHorizontalAlign.Left
        };
    }

    private bool IsSelected(int row, int col, string cellRef)
    {
        if (string.Equals(Sheet?.ActiveCellRef, cellRef, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!_selection.HasRangeSelection)
            return false;
        var bounds = _selection.GetBounds();
        return row >= bounds.StartRow && row <= bounds.EndRow && col >= bounds.StartCol && col <= bounds.EndCol;
    }

    private bool IsSelectionEndCell(int row, int col)
    {
        if (!_selection.HasRangeSelection)
            return string.Equals(Sheet?.ActiveCellRef, SpreadsheetSelectionState.ToCellRef(row, col), StringComparison.OrdinalIgnoreCase);
        var bounds = _selection.GetBounds();
        return row == bounds.EndRow && col == bounds.EndCol;
    }

    private void RefreshFormulaRefColors()
    {
        ClearFormulaRefColors();
        if (!IsInFormulaPointMode || string.IsNullOrEmpty(_editValue))
            return;

        var refs = FormulaReferenceAdjuster.ParseFormulaReferences(_editValue);
        for (var i = 0; i < refs.Count; i++)
        {
            var raw = refs[i].Replace("$", "", StringComparison.Ordinal).ToUpperInvariant();
            var colorIdx = i % FormulaRefColorCount;
            if (raw.Contains(':', StringComparison.Ordinal))
            {
                try
                {
                    var range = SpreadsheetRange.Parse(raw);
                    _formulaRangeColors.Add((range.StartRow, range.StartCol, range.EndRow, range.EndCol, colorIdx));
                }
                catch { }
            }
            else
            {
                _formulaRefColors[raw] = colorIdx;
            }
        }
    }

    private void ClearFormulaRefColors()
    {
        _formulaRefColors.Clear();
        _formulaRangeColors.Clear();
    }

    private int GetFormulaRefColorIndex(string cellRef)
    {
        if (_formulaRefColors.Count == 0 && _formulaRangeColors.Count == 0)
            return -1;

        if (_formulaRefColors.TryGetValue(cellRef.Replace("$", "", StringComparison.Ordinal).ToUpperInvariant(), out var idx))
            return idx;

        var (row, col) = SpreadsheetSelectionState.ParseCellRef(cellRef);
        foreach (var (sr, sc, er, ec, ci) in _formulaRangeColors)
        {
            if (row >= sr && row <= er && col >= sc && col <= ec)
                return ci;
        }

        return -1;
    }

    private bool IsFrozenRow(int row) => Sheet?.FreezeRowCount > 0 && row < Sheet.FreezeRowCount;
    private bool IsFrozenCol(int col) => Sheet?.FreezeColumnCount > 0 && col < Sheet.FreezeColumnCount;

    private void RebuildMergedCellCache()
    {
        _mergedStartLookup.Clear();
        _mergedHiddenLookup.Clear();
        if (Sheet is null)
            return;

        foreach (var range in Sheet.MergedCells)
        {
            _mergedStartLookup[(range.StartRow, range.StartCol)] = range;
            for (var row = range.StartRow; row <= range.EndRow; row++)
            {
                for (var col = range.StartCol; col <= range.EndCol; col++)
                {
                    if (row != range.StartRow || col != range.StartCol)
                        _mergedHiddenLookup.Add((row, col));
                }
            }
        }
    }

    private void ContextMenuFormatCells() { CloseContextMenu(); _ = OnFormatCellsRequested.InvokeAsync(); }
    private void ContextMenuCopy() { CloseContextMenu(); _ = OnCopyRequested.InvokeAsync(); }
    private void ContextMenuCut() { CloseContextMenu(); _ = OnCutRequested.InvokeAsync(); }
    private void ContextMenuPaste() { CloseContextMenu(); _ = OnPasteRequested.InvokeAsync(); }
    private void ContextMenuInsertRow() { CloseContextMenu(); _ = OnInsertRowRequested.InvokeAsync(); }
    private void ContextMenuDeleteRow() { CloseContextMenu(); _ = OnDeleteRowRequested.InvokeAsync(); }
    private void ContextMenuInsertColumn() { CloseContextMenu(); _ = OnInsertColumnRequested.InvokeAsync(); }
    private void ContextMenuDeleteColumn() { CloseContextMenu(); _ = OnDeleteColumnRequested.InvokeAsync(); }
    private void ContextMenuFillDown() { CloseContextMenu(); _ = OnFillRequested.InvokeAsync(SpreadsheetFillDirection.Down); }
    private void ContextMenuFillRight() { CloseContextMenu(); _ = OnFillRequested.InvokeAsync(SpreadsheetFillDirection.Right); }
    private void ContextMenuClearFormatting() { CloseContextMenu(); _ = OnClearFormattingRequested.InvokeAsync(); }
    private void ContextMenuClearContent() { CloseContextMenu(); _ = OnClearContentRequested.InvokeAsync(); }
    private void ContextMenuClearAll() { CloseContextMenu(); _ = OnClearAllRequested.InvokeAsync(); }

    private void ContextMenuSetColumnWidth()
    {
        CloseContextMenu();
        // The dialog edits the MODEL size, never the rendered one (`GetColumnWidth` multiplies by zoom).
        var width = Sheet is not null
            && Sheet.Columns.TryGetValue(_contextMenuColIndex, out var column)
            && column.Width is { } stored
                ? stored
                : ColumnWidth;
        _colWidthInputValue = ((int)Math.Round(width)).ToString();
        _showColWidthDialog = true;
    }

    private void ContextMenuSetRowHeight()
    {
        CloseContextMenu();
        // The dialog edits the MODEL height, never the rendered one (`GetRowHeight` multiplies by
        // zoom × density row scale, so showing it wrote the scaled value back on Apply).
        var height = Sheet is not null
            && Sheet.Rows.TryGetValue(_contextMenuRowIndex, out var row)
            && row.Height is { } stored
                ? stored
                : RowHeight;
        _rowHeightInputValue = ((int)Math.Round(height)).ToString();
        _showRowHeightDialog = true;
    }

    /// <summary>Closes the built-in context menu (a host that owns the menu does not need it).</summary>
    public void CloseContextMenu() => _contextMenuVisible = false;

    private void ApplyColWidth()
    {
        if (double.TryParse(_colWidthInputValue, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var width))
        {
            _ = OnColumnResizeRequested.InvokeAsync((_contextMenuColIndex, Math.Max(16, width)));
            _geometry.Clear();
            _needsRender = true;
        }
        _showColWidthDialog = false;
    }

    private void ApplyRowHeight()
    {
        if (double.TryParse(_rowHeightInputValue, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var height))
        {
            _ = OnRowResizeRequested.InvokeAsync((_contextMenuRowIndex, Math.Max(8, height)));
            _geometry.Clear();
            _needsRender = true;
        }
        _showRowHeightDialog = false;
    }

    // Enter commits from the field itself (the "confirm what was typed" contract). Escape is NOT
    // handled here: the core TmModal owns the document-level dismissal and closes through
    // CloseColumnWidthDialog / CloseRowHeightDialog, so the field does not dismiss twice.
    private void OnColWidthInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") ApplyColWidth();
    }

    private void OnRowHeightInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") ApplyRowHeight();
    }

    private void CloseColumnWidthDialog() => _showColWidthDialog = false;

    private void CloseRowHeightDialog() => _showRowHeightDialog = false;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await RegisterCanvasAsync();

        if (_needsRender && Sheet is not null)
        {
            _needsRender = false;
            try { await JS.InvokeVoidAsync("tmSpreadsheetCanvas.render", _rootElement, _canvasElement, BuildCanvasFrame()); }
            catch (JSException) { }
            catch (InvalidOperationException) { }
        }

        if (_shouldFocusEditorAfterRender)
        {
            _shouldFocusEditorAfterRender = false;
            try { await _editInput.FocusAsync(); } catch { }
        }
        else if (_shouldFocusAfterRender || _forceFocusAfterRender)
        {
            var force = _forceFocusAfterRender;
            _shouldFocusAfterRender = false;
            _forceFocusAfterRender = false;
            // Phase 7: a canvas activation owns the focus only until the user moves it elsewhere. A late
            // frame (the command-log flush, the viewport sync the grid's own focusout schedules) must not
            // yank the caret back out of a control the user has since focused — the name box resets its
            // typed ref on blur, and the Enter meant for it is then handled by the grid. An EXPLICIT
            // navigation (force) is different: the user asked the grid to go somewhere, so it takes the
            // focus and typing continues in the grid.
            var focusElsewhere = false;
            if (!force)
            {
                try { focusElsewhere = await JS.InvokeAsync<bool>("tmSpreadsheetCanvas.isFocusOutside", _rootElement); }
                catch (JSException) { }
                catch (InvalidOperationException) { }
                catch (NotSupportedException) { }
            }

            if (force || !focusElsewhere)
            {
                try { await _rootElement.FocusAsync(); } catch { }
            }
        }
    }

    private async Task RegisterCanvasAsync()
    {
        if (_registered)
            return;

        // The coarse-pointer media query is the one fact Blazor cannot know: ask the browser once per
        // grid, before the first frame, so the density it resolves is the one the geometry uses.
        if (!_pointerCoarseResolved)
        {
            _pointerCoarseResolved = true;
            try { _pointerCoarse = await JS.InvokeAsync<bool>("tmSpreadsheetCanvas.isCoarsePointer"); }
            catch (JSException) { }
            catch (InvalidOperationException) { }
            catch (NotSupportedException) { }

            // The first render had to assume a fine pointer: re-measure and repaint when the browser
            // reported a coarse one (the row geometry and the density attribute both change).
            if (Sheet is not null && Math.Abs(DensityScale.RowScale - _geometryDensityRowScale) > 0.001)
            {
                _geometry.Update(Sheet, RowHeight, ColumnWidth, Zoom, DensityScale.RowScale);
                RebuildMergedCellCache();
                CaptureStructureSignature();
                _needsRender = true;
                StateHasChanged();
            }
        }

        _dotNetRef ??= DotNetObjectReference.Create(this);
        try
        {
            if (Sheet is not null)
            {
                await JS.InvokeVoidAsync("tmSpreadsheetCanvas.initEngine", _rootElement, _canvasElement, _headerCanvasElement, _selectionCanvasElement, _dotNetRef, BuildCanvasFrame());
                _needsRender = false;
            }
            _registered = true;
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_registered)
        {
            try { await JS.InvokeVoidAsync("tmSpreadsheetCanvas.dispose", _rootElement); }
            catch (JSException) { }
            catch (InvalidOperationException) { }
        }

        _dotNetRef?.Dispose();
    }
}
