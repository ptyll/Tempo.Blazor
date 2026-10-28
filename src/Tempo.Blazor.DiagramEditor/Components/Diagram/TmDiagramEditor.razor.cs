using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Models;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Top-level diagram editor component. Composes the canvas, toolbox, properties panel,
/// and toolbar into a fully working editor.
/// </summary>
public partial class TmDiagramEditor : TmComponentBase, IDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] private DiagramStencilRegistry StencilRegistry { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>Document being edited. Bind with <c>@bind-Document</c>.</summary>
    [Parameter] public DiagramDocument? Document { get; set; }

    /// <summary>Raised after every mutation.</summary>
    [Parameter] public EventCallback<DiagramDocument> DocumentChanged { get; set; }

    /// <summary>
    /// Host-pinned read-only mode: prevents all editing interactions and hides the built-in
    /// Read-only/Edit toggle. The component NEVER assigns this parameter — the user-facing
    /// toggle flips <see cref="IsReadOnly"/>'s internal <c>_userReadOnly</c> component instead
    /// (DI1), so a hosted read-only preview can no longer be unlocked from the UI.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Effective read-only state: the host-pinned <see cref="ReadOnly"/> parameter OR the user's
    /// local Read-only toggle. Every editing path (toolbar, keyboard shortcuts, context menu,
    /// import, templates, paste, canvas interop) is gated on this state.
    /// </summary>
    public bool IsReadOnly => ReadOnly || _userReadOnly;

    /// <summary>User-toggled read-only state; deliberately separate from <see cref="ReadOnly"/>.</summary>
    private bool _userReadOnly;

    /// <summary>The effective read-only state seen by the last transition pass: every change
    /// (host parameter or user toggle) runs <see cref="ApplyReadOnlyTransition"/> so the state
    /// flip applies once — closing edit-only surfaces and disarming the edge tool.</summary>
    private bool _lastEffectiveReadOnly;

    /// <summary>Show the component toolbox panel on the left.</summary>
    [Parameter] public bool ShowToolbox { get; set; } = true;

    /// <summary>Show the properties panel on the right.</summary>
    [Parameter] public bool ShowPropertiesPanel { get; set; } = true;

    /// <summary>Show the layers panel on the right.</summary>
    [Parameter] public bool ShowLayersPanel { get; set; } = true;

    /// <summary>Show the minimap at the bottom of the right sidebar.</summary>
    [Parameter] public bool ShowMinimap { get; set; } = true;

    /// <summary>
    /// Show the editor chrome around the canvas: the header bar's built-in controls (document title,
    /// mobile undo/redo, search and panel toggles, "⋯" view menu), the command toolbar row and the
    /// status bar (zoom, Read-only toggle); on a mobile layout also the floating zoom pill and the
    /// action bar. <c>false</c> leaves the page tabs, canvas, docked panels and minimap (the
    /// pre-redesign meaning: everything that lived in the old toolbar). The host slots
    /// <see cref="HeaderStart"/> / <see cref="HeaderActions"/> still render. Default <c>true</c>.
    /// </summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>Show grid lines on the canvas background.</summary>
    [Parameter] public bool ShowGrid { get; set; } = true;

    /// <summary>Snap-to-grid cell size in pixels. 0 = disabled.</summary>
    [Parameter] public int GridSize { get; set; } = 8;

    /// <summary>Show page view with shadow and boundaries on the canvas.</summary>
    [Parameter] public bool ShowPageView { get; set; } = true;

    /// <summary>Optional named HttpClient to use for SVG/PNG/PDF export calls.</summary>
    [Parameter] public string ExportHttpClientName { get; set; } = "";

    /// <summary>Additional CSS class on the editor root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when selected diagram item identifiers change.</summary>
    [Parameter] public EventCallback<string[]> OnSelectedIdsChanged { get; set; }

    /// <summary>
    /// Stencil ids the connect dialog offers first (newest first, at most 8). The list is never
    /// shared between editor instances. Two usages:
    /// <list type="bullet">
    /// <item><description><b>Controlled</b> — the host owns the recents and replaces the reference when they
    /// change (bind it together with <see cref="RecentStencilIdsChanged"/> to persist them per user).
    /// The editor adopts a new reference whenever it changes and raises every update.</description></item>
    /// <item><description><b>Uncontrolled</b> — the parameter is left unset (<c>null</c>) and the editor keeps
    /// its own per-instance list; <see cref="RecentStencilIdsChanged"/> still fires, so a host can
    /// observe picks without taking ownership.</description></item>
    /// </list>
    /// </summary>
    [Parameter] public IReadOnlyList<string>? RecentStencilIds { get; set; }

    /// <summary>
    /// Raised with the updated recents (newest first) after the user connects a shape through the
    /// connect dialog. In controlled usage the host feeds the value back through
    /// <see cref="RecentStencilIds"/> (a <c>@bind</c> does this automatically); the editor adopts it
    /// only when the reference changes, so a stale render can never clobber a just-added recent.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> RecentStencilIdsChanged { get; set; }

    // ── Child component refs ─────────────────────────────────────────────────

    private TmDiagramCanvas? _canvas;
    private ElementReference _downloadAnchor;

    // ── Command stack ────────────────────────────────────────────────────────
    // One isolated stack per page so undo/redo is scoped to the active page.

    private readonly Dictionary<string, DiagramCommandStack> _pageCommandStacks = new();

    private DiagramCommandStack ActiveCommandStack
    {
        get
        {
            var pageId = _document?.ActivePage?.Id;
            if (pageId is null)
                return new DiagramCommandStack(); // fallback, should never happen

            if (!_pageCommandStacks.TryGetValue(pageId, out var stack))
            {
                stack = new DiagramCommandStack();
                stack.OnStackChanged += OnStackChanged;
                _pageCommandStacks[pageId] = stack;
            }
            return stack;
        }
    }

    // ── Derived / UI state ───────────────────────────────────────────────────

    private DiagramDocument? _document;
    private string[] _selectedIds = [];
    private readonly Stack<string> _groupStack = new();
    private string? ActiveGroupId => _groupStack.Any() ? _groupStack.Peek() : null;

    private double _zoomLevel = 1.0;
    private string _zoomLabel => $"{(int)Math.Round(_zoomLevel * 100)}%";

    /// <summary>Zoom-in is a no-op at the shared maximum (DiagramZoomLimits): the button disables
    /// there instead of pretending to zoom (review fix, mirrors the JS wheel no-op).</summary>
    private bool CanZoomIn => _zoomLevel < DiagramZoomLimits.MaxScale - 1e-9;

    /// <summary>Zoom-out disables at the shared minimum (DiagramZoomLimits).</summary>
    private bool CanZoomOut => _zoomLevel > DiagramZoomLimits.MinScale + 1e-9;

    private bool CanArrange => _selectedIds.Length > 1;

    private bool _toolboxCollapsed;
    // Invariant: _propsCollapsed/_layersCollapsed are always written together via
    // SetInspectorOpen; per-panel presence is ShowPropertiesPanel/ShowLayersPanel.
    private bool _propsCollapsed;
    private bool _layersCollapsed;
    private bool _showTableInserter;

    // UX review phase 4: the picker renders at the editor root as a fixed popover anchored to
    // the Insert trigger (or the More trigger once the group collapsed), measured in JS at the
    // moment it opens — never a child of a toolbar item that may collapse into "More".
    private double _tableInserterX;
    private double _tableInserterY;
    private ElementReference _editorRoot;

    /// <summary>Inline style pinning the fixed popover at the measured viewport point (invariant format — a CSS value must not follow the UI culture).</summary>
    private string TableInserterStyle =>
        string.Create(CultureInfo.InvariantCulture, $"left:{_tableInserterX:0.#}px;top:{_tableInserterY:0.#}px");

    private bool _minimapCollapsed;
    private bool _exportMenuOpen;
    private bool _exporting;
    private string? _importError;
    private string? _exportError;
    private DiagramMinimapViewport? _minimapViewport;

    private string? _activeLayerId;

    private string _toolMode = "select";
    private string? _activeEdgeStencilId;

    private bool _showRulers;
    private double _rulerViewportX;
    private double _rulerViewportY;
    private double _rulerViewportW;
    private double _rulerViewportH;
    private double _rulerCursorX;
    private double _rulerCursorY;

    private static readonly IReadOnlyList<SelectOption<MeasurementUnit>> _rulerUnitOptions =
    [
        new() { Value = MeasurementUnit.Px, Label = "px" },
        new() { Value = MeasurementUnit.Pt, Label = "pt" },
        new() { Value = MeasurementUnit.In, Label = "in" },
        new() { Value = MeasurementUnit.Mm, Label = "mm" },
        new() { Value = MeasurementUnit.M, Label = "m" },
    ];

    private bool _canvasSizeEditing;
    private string _canvasWInput = "";
    private string _canvasHInput = "";

    // Connect modal state
    private bool _connectModalOpen;
    private string? _connectSourceNodeId;
    private string? _connectDirection;

    /// <summary>Stencil of the node the connect arrow was dragged from — it scopes the connect dialog's default palette (DI4).</summary>
    private string? ConnectSourceStencilId =>
        _connectSourceNodeId is null
            ? null
            : _document?.Nodes.FirstOrDefault(n => n.Id == _connectSourceNodeId)?.StencilId;

    internal const int MaxRecentStencils = 8;
    private readonly List<string> _recentStencilIds = [];

    /// <summary>
    /// The last host-supplied recents value the editor adopted. Tracked separately from the
    /// snapshot raised through <see cref="RecentStencilIdsChanged"/>: adoption runs only when
    /// the host REFERENCE changes, so an unrelated re-render before the binding feeds back can
    /// never clobber a recent the user just added.
    /// </summary>
    private IReadOnlyList<string>? _lastHostRecents;

    private async Task RememberRecentStencilAsync(string stencilId)
    {
        _recentStencilIds.Remove(stencilId);
        _recentStencilIds.Insert(0, stencilId);
        if (_recentStencilIds.Count > MaxRecentStencils)
            _recentStencilIds.RemoveRange(MaxRecentStencils, _recentStencilIds.Count - MaxRecentStencils);
        await RecentStencilIdsChanged.InvokeAsync(_recentStencilIds.ToArray());
    }

    /// <summary>Label of the connect source for the dialog title: the node's own text, else its stencil name; capped at 40 text elements.</summary>
    private string? ConnectSourceLabel
    {
        get
        {
            var node = _connectSourceNodeId is null ? null : _document?.Nodes.FirstOrDefault(n => n.Id == _connectSourceNodeId);
            if (node is null) return null;
            var text = DiagramNodeText.GetDisplayLabel(node);
            if (string.IsNullOrEmpty(text))
            {
                var stencil = StencilRegistry.GetStencil(node.StencilId);
                text = stencil is null ? null : DiagramStencilDisplayNames.GetStencilDisplayName(stencil, Loc);
            }
            return text is null ? null : DiagramNodeText.Truncate(text);
        }
    }

    // SQL import dialog state
    private bool _sqlImportDialogOpen;
    private bool _csvImportDialogOpen;

    // Template gallery state
    private bool _showTemplateGallery;

    // ── Context menu ─────────────────────────────────────────────────────────

    private enum ContextMenuType { None, Node, Edge, Canvas, TableCell }

    private bool _contextMenuOpen;
    private ContextMenuType _contextMenuType;

    /// <summary>The pointer type that opened the context menu ('mouse' or 'touch'). A touch menu
    /// is anchored offset from the finger (data-anchor="touch" on the menu element).</summary>
    private string? _contextMenuOrigin;
    private string? _contextMenuNodeId;
    private string? _contextMenuEdgeId;
    private (int Row, int Column)? _contextMenuTableCell;
    private double _contextMenuScreenX;
    private double _contextMenuScreenY;
    private double _contextMenuCanvasX;
    private double _contextMenuCanvasY;
    private ElementReference _contextMenuRef;

    private List<(int Row, int Column)> _selectedTableCells = [];

    // ── Page tabs state ──────────────────────────────────────────────────────

    private string? _pageRenamingId;
    private string _pageRenameInput = "";
    private ElementReference _pageRenameInputRef;
    private string? _pageDragId;
    private string? _pageDragOverId;

    // ── Search panel ─────────────────────────────────────────────────────────

    private bool _showSearchPanel;
    private string _searchQuery = "";
    private string _replaceQuery = "";
    private List<DiagramSearchResult> _searchResults = [];
    private int _searchCurrentIndex;
    private bool _searchAllPages;
    private bool _searchUseRegex;
    private string? _regexError;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        _document = Document ?? CreateEmptyDocument();
        if (_document.Pages.Count == 1 && _document.Pages[0].Name == "Page 1")
        {
            _document.Pages[0].Name = $"{Loc["TmDiagramEditor_PageName"]} 1";
        }
        EnsureDocumentLayers(_document);
        _document.SnapToGrid(GridSize);
        EnsureCommandStackForActivePage();
    }

    protected override void OnParametersSet()
    {
        if (Document is not null && !ReferenceEquals(Document, _document))
        {
            DetachAllStacks();
            _pageCommandStacks.Clear();
            _groupStack.Clear();
            _document = Document;
            _document.EnsurePages();
            EnsureDocumentLayers(_document);
            _document.SnapToGrid(GridSize);
            _selectedIds = [];
            EnsureCommandStackForActivePage();
        }

        // A FORCED layout is the resolution: the shell reports changes only, and the initial
        // resolution is not a change — so a host (or test) that pins Tablet/Desktop/Mobile must
        // see the tablet presentation from the first frame. The initial value is not a change,
        // except that an Auto observer which inherits a non-Desktop resolution from a cascading
        // parent reports it once so late-mounted consumers adopt it. Under Auto the shell's
        // callback owns this state (it fires on the first container measurement, or on that
        // inherited first resolution).
        if (LayoutMode != TmLayoutMode.Auto && LayoutMode != _resolvedLayout)
        {
            ApplyResolvedLayout(LayoutMode);
            // The forced mode resolves on THIS pass, before the shell's own collapse raise: mark the
            // generation so that raise is read as a layout state (U2/D27), like the shell callback path.
            _layoutGeneration++;
        }

        // Every effective read-only change (host parameter or user toggle) applies its transition
        // here exactly once — so a host flip behaves like the user's toggle.
        ApplyReadOnlyTransition();

        // Adopt the host's recents only when the host reference changes (controlled usage:
        // the host owns the list and may replace it at any time). Uncontrolled usage — the
        // parameter never set — keeps the editor's own per-instance list; the snapshot raised
        // through RecentStencilIdsChanged is NOT the adoption baseline, so a re-render that
        // arrives before the binding feeds back cannot wipe a just-added recent.
        if (!ReferenceEquals(RecentStencilIds, _lastHostRecents))
        {
            _lastHostRecents = RecentStencilIds;
            if (RecentStencilIds is not null)
            {
                _recentStencilIds.Clear();
                _recentStencilIds.AddRange(RecentStencilIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxRecentStencils));
            }
        }
    }

    /// <summary>Applies a change of the effective read-only state: entering read-only closes the
    /// surfaces that only make sense while editing and disarms the edge draw tool (an armed
    /// click-to-connect would otherwise be the one editing path left open).</summary>
    private void ApplyReadOnlyTransition()
    {
        if (IsReadOnly == _lastEffectiveReadOnly) return;
        _lastEffectiveReadOnly = IsReadOnly;
        if (!IsReadOnly) return;
        CloseEditSurfaces();
        if (_toolMode == "edge")
        {
            _ = SetToolMode("select");
        }
    }

    private void EnsureDocumentLayers(DiagramDocument doc)
    {
        if (doc is null) return;
        var page = doc.ActivePage;
        if (page.Layers.Count == 0)
        {
            var defaultLayer = new DiagramLayer { Name = Loc["TmDiagramLayers_DefaultLayer"], Order = 0 };
            page.Layers.Add(defaultLayer);
        }
        var defaultLayerId = page.Layers.OrderBy(l => l.Order).First().Id;
        foreach (var node in page.Nodes.Where(n => string.IsNullOrEmpty(n.LayerId)))
            node.LayerId = defaultLayerId;
        foreach (var edge in page.Edges.Where(e => string.IsNullOrEmpty(e.LayerId)))
            edge.LayerId = defaultLayerId;
        if (string.IsNullOrEmpty(_activeLayerId))
            _activeLayerId = defaultLayerId;
    }

    public void Dispose()
    {
        DetachAllStacks();
    }

    private void EnsureCommandStackForActivePage()
    {
        _document?.EnsurePages();
        var _ = ActiveCommandStack; // triggers creation and attachment
    }

    private void DetachAllStacks()
    {
        foreach (var stack in _pageCommandStacks.Values)
        {
            stack.OnStackChanged -= OnStackChanged;
        }
    }

    // ── Stack change → re-render ─────────────────────────────────────────────

    private void OnStackChanged() => InvokeAsync(StateHasChanged);

    // ── Document change propagation ──────────────────────────────────────────

    private async Task OnDocumentChanged(DiagramDocument doc)
    {
        _document = doc;
        // The chrome-only canvas latch must not swallow a content edit (phase 7 gate).
        _canvas?.NotifyContentMutated();
        await DocumentChanged.InvokeAsync(doc);
        await InvokeAsync(StateHasChanged);
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private async Task OnSelectionChanged(string[] ids)
    {
        _selectedIds = ids;
        // Phase 6 (tablet): a selection is an editing intent — surface the inspector even
        // when the palette took the single expanded dock slot (DI3).
        ExpandInspectorForSelectionOnTablet();
        // Phase 7 (mobile): the same intent opens the panels sheet PEEKED on the properties.
        ExpandPropertiesForSelectionOnMobile();
        await OnSelectedIdsChanged.InvokeAsync(ids);
        await InvokeAsync(StateHasChanged);
    }

    // ── Toolbar: Tool mode ───────────────────────────────────────────────────

    private async Task SetToolMode(string mode)
    {
        _toolMode = mode;
        if (_canvas is not null) await _canvas.SetToolMode(mode);
    }

    private void OnCanvasToolModeChanged(string mode)
    {
        _toolMode = mode;
        StateHasChanged();
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private async Task OnNodeContextMenu((string NodeId, double ScreenX, double ScreenY, string Origin) args)
    {
        if (IsReadOnly) return;
        _contextMenuType = ContextMenuType.Node;
        _contextMenuOpen = true;
        _contextMenuOrigin = args.Origin;
        _contextMenuNodeId = args.NodeId;
        _contextMenuEdgeId = null;
        _contextMenuTableCell = null;
        _contextMenuScreenX = args.ScreenX;
        _contextMenuScreenY = args.ScreenY;
        ClampContextMenuPosition();

        if (!_selectedIds.Contains(args.NodeId) && _canvas is not null)
        {
            _selectedIds = [args.NodeId];
            await _canvas.SetSelection(args.NodeId);
        }
        await InvokeAsync(StateHasChanged);
        if (_canvas is not null && !IsMobileLayout)
            await JS.InvokeVoidAsync("tmDiagramEditor.openContextMenu", _canvas.GetContainerRef(), _contextMenuRef);
    }

    private async Task OnEdgeContextMenu((string EdgeId, double ScreenX, double ScreenY, string Origin) args)
    {
        if (IsReadOnly) return;
        _contextMenuType = ContextMenuType.Edge;
        _contextMenuOpen = true;
        _contextMenuOrigin = args.Origin;
        _contextMenuEdgeId = args.EdgeId;
        _contextMenuNodeId = null;
        _contextMenuTableCell = null;
        _contextMenuScreenX = args.ScreenX;
        _contextMenuScreenY = args.ScreenY;
        ClampContextMenuPosition();

        _selectedIds = [args.EdgeId];
        if (_canvas is not null) await _canvas.SetSelection([]);
        await InvokeAsync(StateHasChanged);
        if (_canvas is not null && !IsMobileLayout)
            await JS.InvokeVoidAsync("tmDiagramEditor.openContextMenu", _canvas.GetContainerRef(), _contextMenuRef);
    }

    private async Task OnTableCellContextMenu((string NodeId, int Row, int Column, double ScreenX, double ScreenY, string Origin) args)
    {
        if (IsReadOnly) return;
        _contextMenuType = ContextMenuType.TableCell;
        _contextMenuOpen = true;
        _contextMenuOrigin = args.Origin;
        _contextMenuNodeId = args.NodeId;
        _contextMenuEdgeId = null;
        _contextMenuTableCell = (args.Row, args.Column);
        _contextMenuScreenX = args.ScreenX;
        _contextMenuScreenY = args.ScreenY;
        ClampContextMenuPosition();

        // Make the right-clicked cell visibly the target of the context menu.
        // If it isn't already in the multi-selection, replace the selection with
        // just this cell so the user sees which cell will be affected by
        // "Insert row / column", "Format cell", etc.
        if (!_selectedTableCells.Any(c => c.Row == args.Row && c.Column == args.Column))
        {
            _selectedTableCells.Clear();
            _selectedTableCells.Add((args.Row, args.Column));
        }

        await InvokeAsync(StateHasChanged);
        if (_canvas is not null && !IsMobileLayout)
            await JS.InvokeVoidAsync("tmDiagramEditor.openContextMenu", _canvas.GetContainerRef(), _contextMenuRef);
    }

    private async Task OnCanvasContextMenu((double CanvasX, double CanvasY, double ScreenX, double ScreenY, string Origin) args)
    {
        if (IsReadOnly) return;
        _contextMenuType = ContextMenuType.Canvas;
        _contextMenuOpen = true;
        _contextMenuOrigin = args.Origin;
        _contextMenuNodeId = null;
        _contextMenuEdgeId = null;
        _contextMenuTableCell = null;
        _contextMenuCanvasX = args.CanvasX;
        _contextMenuCanvasY = args.CanvasY;
        _contextMenuScreenX = args.ScreenX;
        _contextMenuScreenY = args.ScreenY;
        ClampContextMenuPosition();

        _selectedIds = [];
        if (_canvas is not null) await _canvas.SetSelection([]);
        await InvokeAsync(StateHasChanged);
        if (_canvas is not null && !IsMobileLayout)
            await JS.InvokeVoidAsync("tmDiagramEditor.openContextMenu", _canvas.GetContainerRef(), _contextMenuRef);
    }

    private void ClampContextMenuPosition()
    {
        // Basic guard; precise viewport-aware clamping (with flip) is done in JS after render
        if (_contextMenuScreenX < 0) _contextMenuScreenX = 8;
        if (_contextMenuScreenY < 0) _contextMenuScreenY = 8;
    }

    private async Task CloseContextMenu()
    {
        _contextMenuOpen = false;
        _contextMenuOrigin = null;
        if (_canvas is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("tmDiagramEditor.closeContextMenu", _canvas.GetContainerRef());
            }
            catch
            {
                // Ignore JS interop errors during cleanup
            }
        }
    }

    private async Task HandleTableCellSelect((string NodeId, int Row, int Column, bool IsCtrlHeld) args)
    {
        if (IsReadOnly) return;
        if (!args.IsCtrlHeld)
        {
            _selectedTableCells.Clear();
            _selectedTableCells.Add((args.Row, args.Column));
        }
        else
        {
            var existing = _selectedTableCells.FirstOrDefault(s => s.Row == args.Row && s.Column == args.Column);
            if (existing != default)
                _selectedTableCells.Remove(existing);
            else
                _selectedTableCells.Add((args.Row, args.Column));
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnSelectedTableCellsChanged(List<(int Row, int Column)> cells)
    {
        _selectedTableCells = cells.ToList();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>First-stage Escape cascade: clears the table cell sub-selection only,
    /// leaving the parent node selected (draw.io-style drill-out).</summary>
    private async Task HandleClearTableCellSelection()
    {
        if (_selectedTableCells.Count == 0) return;
        _selectedTableCells.Clear();
        await InvokeAsync(StateHasChanged);
    }

    private bool IsTableNode(string? nodeId)
    {
        if (nodeId is null) return false;
        var node = _document?.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return false;
        var stencil = StencilRegistry.GetStencil(node.StencilId);
        return stencil?.IsTable == true;
    }

    private bool GetContextMenuNodeLocked()
    {
        if (_contextMenuNodeId is null || _document is null) return false;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _contextMenuNodeId);
        return node?.IsLocked ?? false;
    }

    private bool CanContextMenuGroup()
    {
        if (_document is null) return false;
        return _selectedIds.Length > 1
            && _selectedIds.All(id => _document.Nodes.Any(n => n.Id == id && !n.IsLocked));
    }

    private bool CanContextMenuUngroup()
    {
        if (_document is null || _selectedIds.Length != 1) return false;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _selectedIds[0]);
        return !string.IsNullOrEmpty(node?.GroupId) || node?.StencilId == "general.group";
    }

    private bool CanContextMenuMergeCells()
    {
        if (_contextMenuNodeId is null || _document is null) return false;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _contextMenuNodeId);
        return node is not null && Services.TableLayoutService.CanMerge(node, _selectedTableCells);
    }

    private bool CanContextMenuSplitCell()
    {
        if (_contextMenuTableCell is null || _contextMenuNodeId is null || _document is null) return false;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _contextMenuNodeId);
        return node is not null && Services.TableLayoutService.CanSplit(node, _contextMenuTableCell.Value.Row, _contextMenuTableCell.Value.Column);
    }

    private bool IsContextMenuNodeLocked()
    {
        if (_contextMenuNodeId is null || _document is null) return false;
        return _document.Nodes.FirstOrDefault(n => n.Id == _contextMenuNodeId)?.IsLocked ?? false;
    }

    // ── Node actions ─────────────────────────────────────────────────────────

    private async Task ContextMenuCut()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        ActiveCommandStack.Push(new CutNodesCommand(_document, ids));
        _selectedIds = [];
        if (_canvas is not null) await _canvas.SetSelection([]);
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuCopy()
    {
        await CloseContextMenu();
        if (_document is null || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        new CopyNodesCommand(_document, ids).Execute();
    }

    private async Task ContextMenuDuplicate()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        ActiveCommandStack.Push(new DuplicateNodesCommand(_document, ids));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuPaste()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        var cmd = new PasteNodesCommand(_document, 16, 16, useInternalClipboard: true, parentGroupId: ActiveGroupId);
        if (cmd.PastedNodes.Count == 0) return;
        ActiveCommandStack.Push(cmd);
        _selectedIds = cmd.PastedNodes.Select(n => n.Id).ToArray();
        if (_canvas is not null) await _canvas.SetSelection(_selectedIds);
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuPasteHere()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        var cmd = new PasteNodesCommand(_document, _contextMenuCanvasX, _contextMenuCanvasY, useInternalClipboard: true, pasteHere: true, parentGroupId: ActiveGroupId);
        if (cmd.PastedNodes.Count == 0) return;
        ActiveCommandStack.Push(cmd);
        _selectedIds = cmd.PastedNodes.Select(n => n.Id).ToArray();
        if (_canvas is not null) await _canvas.SetSelection(_selectedIds);
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuSelectAll()
    {
        await CloseContextMenu();
        if (_document is null) return;
        _selectedIds = _document.Nodes.Select(n => n.Id).ToArray();
        if (_canvas is not null) await _canvas.SetSelection(_selectedIds);
        await InvokeAsync(StateHasChanged);
    }

    private async Task ContextMenuUndo()
    {
        if (IsReadOnly) return;
        await CloseContextMenu();
        if (ActiveCommandStack.CanUndo)
        {
            ActiveCommandStack.Undo();
            await OnDocumentChanged(_document!);
        }
    }

    private async Task ContextMenuRedo()
    {
        if (IsReadOnly) return;
        await CloseContextMenu();
        if (ActiveCommandStack.CanRedo)
        {
            ActiveCommandStack.Redo();
            await OnDocumentChanged(_document!);
        }
    }

    private async Task ContextMenuDelete()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        var nodeIds = ids.Where(id => _document.Nodes.Any(n => n.Id == id)).ToArray();
        var edgeIds = ids.Where(id => _document.Edges.Any(e => e.Id == id)).ToArray();

        if (nodeIds.Length > 0 && edgeIds.Length > 0)
        {
            using var tx = ActiveCommandStack.TransactionScope(Loc["TmDiagramEditor_Delete"]);
            ActiveCommandStack.Push(new RemoveNodesCommand(_document, nodeIds));
            ActiveCommandStack.Push(new RemoveEdgesCommand(_document, edgeIds));
        }
        else if (nodeIds.Length > 0)
        {
            ActiveCommandStack.Push(new RemoveNodesCommand(_document, nodeIds));
        }
        else if (edgeIds.Length > 0)
        {
            ActiveCommandStack.Push(new RemoveEdgesCommand(_document, edgeIds));
        }

        _selectedIds = [];
        if (_canvas is not null) await _canvas.SetSelection([]);
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuBringToFront()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        var before = ids.ToDictionary(id => id, id => _document.Nodes.FirstOrDefault(n => n.Id == id)?.ZIndex ?? 0);
        var maxZ = _document.Nodes.Count > 0 ? _document.Nodes.Max(n => n.ZIndex) : 0;
        var after = ids.ToDictionary(id => id, id => maxZ + 1);
        ActiveCommandStack.Push(new UpdateZIndexCommand(_document, before, after));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuSendToBack()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        var before = ids.ToDictionary(id => id, id => _document.Nodes.FirstOrDefault(n => n.Id == id)?.ZIndex ?? 0);
        var minZ = _document.Nodes.Count > 0 ? _document.Nodes.Min(n => n.ZIndex) : 0;
        var after = ids.ToDictionary(id => id, id => minZ - 1);
        ActiveCommandStack.Push(new UpdateZIndexCommand(_document, before, after));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuLock()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        ActiveCommandStack.Push(new LockNodesCommand(_document, ids));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuUnlock()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        ActiveCommandStack.Push(new UnlockNodesCommand(_document, ids));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuGroup()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        if (!CanContextMenuGroup()) return;
        var ids = _selectedIds.Where(id => _document.Nodes.FirstOrDefault(n => n.Id == id)?.IsLocked != true).ToArray();
        if (ids.Length < 2) return;
        ActiveCommandStack.Push(new GroupNodesCommand(_document, ids));
        _selectedIds = [];
        if (_canvas is not null) await _canvas.SetSelection([]);
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuUngroup()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        if (!CanContextMenuUngroup()) return;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _selectedIds[0]);
        var groupId = node?.StencilId == "general.group" ? node.Id : node?.GroupId;
        if (string.IsNullOrEmpty(groupId)) return;
        // Block ungroup if any child is locked
        if (_document.Nodes.Any(n => n.ParentGroupId == groupId && n.IsLocked)) return;
        ActiveCommandStack.Push(new UngroupNodesCommand(_document, groupId));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuPasteStyle()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        if (DiagramClipboard.Style is null) return;
        ActiveCommandStack.Push(new PasteStyleCommand(_document, ids));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuPasteSize()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var ids = _selectedIds.Contains(_contextMenuNodeId) ? _selectedIds : [_contextMenuNodeId];
        if (DiagramClipboard.Width is null || DiagramClipboard.Height is null) return;
        ActiveCommandStack.Push(new PasteSizeCommand(_document, ids));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuReplaceShape(string stencilId)
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null) return;
        var stencil = StencilRegistry.GetStencil(stencilId);
        if (stencil is null) return;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _contextMenuNodeId);
        if (node is null) return;
        var newPorts = stencil.Ports.Select(p => new DiagramPort
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = p.Name,
            Side = p.Side,
            Offset = p.Offset,
            IsInput = p.IsInput,
            IsOutput = p.IsOutput
        }).ToList();
        ActiveCommandStack.Push(new ReplaceShapeCommand(_document, _contextMenuNodeId, stencilId, newPorts, stencil.DefaultWidth, stencil.DefaultHeight));
        await OnDocumentChanged(_document);
    }

    // ── Edge actions ─────────────────────────────────────────────────────────

    private async Task ContextMenuDeleteEdge()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        ActiveCommandStack.Push(new RemoveEdgesCommand(_document, [_contextMenuEdgeId]));
        await OnDocumentChanged(_document);
    }

    // Detach both endpoints of the right-clicked edge. Preserves the edge's
    // current visible geometry by freezing source/target positions into
    // SourcePoint / TargetPoint before wiping the connection fields.
    private async Task ContextMenuDetachEdgeEndpoints()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == _contextMenuEdgeId);
        if (edge is null) return;
        ActiveCommandStack.Push(new DetachEdgeEndpointsCommand(_document, edge.Id, detachSource: true, detachTarget: true));
        await OnDocumentChanged(_document);
    }

    private bool ContextMenuCanDetachEdgeEndpoints()
    {
        if (IsReadOnly || _document is null || _contextMenuEdgeId is null) return false;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == _contextMenuEdgeId);
        if (edge is null) return false;
        // At least one endpoint must be attached for detach to make sense.
        bool sourceAttached = !string.IsNullOrEmpty(edge.SourceNodeId) || !string.IsNullOrEmpty(edge.SourceEdgeId) || edge.SourceConstraint is not null;
        bool targetAttached = !string.IsNullOrEmpty(edge.TargetNodeId) || !string.IsNullOrEmpty(edge.TargetEdgeId) || edge.TargetConstraint is not null;
        return sourceAttached || targetAttached;
    }

    private async Task ContextMenuEditEdgeLabel()
    {
        await CloseContextMenu();
        if (_document is null || _contextMenuEdgeId is null) return;
        // Trigger inline label editing via the canvas
        _canvas?.StartEdgeLabelEdit(_contextMenuEdgeId);
    }

    private async Task ContextMenuChangeEdgeConnector(string routing)
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == _contextMenuEdgeId);
        if (edge is null || edge.Routing == routing) return;
        ActiveCommandStack.Push(new UpdateEdgeRoutingCommand(_document, _contextMenuEdgeId, edge.Routing, routing, edge.Waypoints.ToList(), []));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuFlipEdge()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == _contextMenuEdgeId);
        if (edge is null) return;
        await _canvas?.OnEdgeToolbarAction(_contextMenuEdgeId, "flip")!;
    }

    private async Task ContextMenuClearWaypoints()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == _contextMenuEdgeId);
        if (edge is null) return;
        await _canvas?.OnEdgeToolbarAction(_contextMenuEdgeId, "clearWaypoints")!;
    }

    private async Task ContextMenuChangeEdgeArrowheadStart(string arrowhead)
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        ActiveCommandStack.Push(new UpdateEdgeArrowheadsCommand(_document, [_contextMenuEdgeId], newStartArrow: arrowhead));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuChangeEdgeArrowheadEnd(string arrowhead)
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        ActiveCommandStack.Push(new UpdateEdgeArrowheadsCommand(_document, [_contextMenuEdgeId], newEndArrow: arrowhead));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuChangeEdgeLineStyle(string? dasharray)
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuEdgeId is null) return;
        ActiveCommandStack.Push(new UpdateEdgeLineStyleCommand(_document, [_contextMenuEdgeId], dasharray));
        await OnDocumentChanged(_document);
    }

    // ── Table cell actions ───────────────────────────────────────────────────

    private async Task ContextMenuInsertRowAbove()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null || IsContextMenuNodeLocked()) return;
        var row = _contextMenuTableCell?.Row ?? 0;
        using (ActiveCommandStack.TransactionScope(Loc["TmDiagramEditor_InsertRowAbove"]))
        {
            ActiveCommandStack.Push(new InsertTableRowCommand(_document, _contextMenuNodeId, row));
        }
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuInsertRowBelow()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null || IsContextMenuNodeLocked()) return;
        var row = (_contextMenuTableCell?.Row ?? 0) + 1;
        var rowCount = Services.TableLayoutService.GetRowCount(_document.Nodes.First(n => n.Id == _contextMenuNodeId));
        if (row > rowCount) row = rowCount;
        using (ActiveCommandStack.TransactionScope(Loc["TmDiagramEditor_InsertRowBelow"]))
        {
            ActiveCommandStack.Push(new InsertTableRowCommand(_document, _contextMenuNodeId, row));
        }
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuInsertColumnLeft()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null || IsContextMenuNodeLocked()) return;
        var col = _contextMenuTableCell?.Column ?? 0;
        using (ActiveCommandStack.TransactionScope(Loc["TmDiagramEditor_InsertColumnLeft"]))
        {
            ActiveCommandStack.Push(new InsertTableColumnCommand(_document, _contextMenuNodeId, col));
        }
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuInsertColumnRight()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null || IsContextMenuNodeLocked()) return;
        var col = (_contextMenuTableCell?.Column ?? 0) + 1;
        var colCount = Services.TableLayoutService.GetColumnCount(_document.Nodes.First(n => n.Id == _contextMenuNodeId));
        if (col > colCount) col = colCount;
        using (ActiveCommandStack.TransactionScope(Loc["TmDiagramEditor_InsertColumnRight"]))
        {
            ActiveCommandStack.Push(new InsertTableColumnCommand(_document, _contextMenuNodeId, col));
        }
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuMergeCells()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null || IsContextMenuNodeLocked()) return;
        if (!CanContextMenuMergeCells()) return;
        ActiveCommandStack.Push(new MergeTableCellsCommand(_document, _contextMenuNodeId, _selectedTableCells));
        _selectedTableCells.Clear();
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuSplitCell()
    {
        await CloseContextMenu();
        if (_contextMenuTableCell is null || _document is null || IsReadOnly || _contextMenuNodeId is null || IsContextMenuNodeLocked()) return;
        var (row, col) = _contextMenuTableCell.Value;
        ActiveCommandStack.Push(new SplitTableCellCommand(_document, _contextMenuNodeId, row, col));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuEditTableCellText()
    {
        await CloseContextMenu();
        if (_contextMenuTableCell is null) return;
        // The stencil shape handles inline editing via double-click; we can't easily trigger it remotely.
        // For now we rely on the Properties panel for inline text editing.
    }

    private async Task ContextMenuFormatTableCell(string backgroundColor, string borderColor)
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly || _contextMenuNodeId is null || _contextMenuTableCell is null || IsContextMenuNodeLocked()) return;
        var (row, col) = _contextMenuTableCell.Value;
        var cell = Services.TableLayoutService.GetCells(_document.Nodes.First(n => n.Id == _contextMenuNodeId))
            .FirstOrDefault(c => c.Row == row && c.Column == col);
        var newStyle = new DiagramTableCellStyle
        {
            BackgroundColor = backgroundColor,
            BorderColor = borderColor,
            TextAlign = cell?.Style?.TextAlign,
            FontWeight = cell?.Style?.FontWeight
        };
        ActiveCommandStack.Push(new UpdateTableCellStyleCommand(_document, _contextMenuNodeId, row, col, newStyle));
        await OnDocumentChanged(_document);
    }

    // ── Canvas actions ───────────────────────────────────────────────────────

    private async Task ContextMenuInsertText()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        var node = new DiagramNode { StencilId = "general.text", X = _contextMenuCanvasX, Y = _contextMenuCanvasY, W = 80, H = 30 };
        node.Data["text"] = "Text";
        ActiveCommandStack.Push(new AddNodeCommand(_document, node));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuInsertRectangle()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        var node = new DiagramNode { StencilId = "general.rectangle", X = _contextMenuCanvasX, Y = _contextMenuCanvasY };
        ActiveCommandStack.Push(new AddNodeCommand(_document, node));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuInsertEllipse()
    {
        await CloseContextMenu();
        if (_document is null || IsReadOnly) return;
        var node = new DiagramNode { StencilId = "general.ellipse", X = _contextMenuCanvasX, Y = _contextMenuCanvasY };
        ActiveCommandStack.Push(new AddNodeCommand(_document, node));
        await OnDocumentChanged(_document);
    }

    private async Task ContextMenuRunLayout(string algorithm, string direction)
    {
        await CloseContextMenu();
        await RunLayout(algorithm, direction);
    }

    private async Task ContextMenuToggleGrid()
    {
        await CloseContextMenu();
        ShowGrid = !ShowGrid;
        StateHasChanged();
    }

    private async Task ContextMenuTogglePageView()
    {
        await CloseContextMenu();
        ShowPageView = !ShowPageView;
        StateHasChanged();
    }

    private async Task ContextMenuToggleSnapToGrid()
    {
        await CloseContextMenu();
        GridSize = GridSize == 0 ? 8 : 0;
        StateHasChanged();
    }

    // ── Search panel ─────────────────────────────────────────────────────────

    private void OpenSearchPanel()
    {
        _showSearchPanel = true;
        _searchQuery = "";
        _replaceQuery = "";
        _searchResults = [];
        _searchCurrentIndex = 0;
        _searchAllPages = false;
        _searchUseRegex = false;
        _regexError = null;
        if (_canvas is not null)
            _ = _canvas.SetActiveSearchResult(null);
    }

    /// <summary>Ctrl+H. In the effective read-only state the panel renders search-only (TmDiagramSearchPanel.ReadOnly).</summary>
    private void OpenReplacePanel()
    {
        _showSearchPanel = true;
        if (string.IsNullOrEmpty(_searchQuery))
        {
            _searchQuery = "";
            _replaceQuery = "";
            _searchResults = [];
            _searchCurrentIndex = 0;
            _searchAllPages = false;
            _searchUseRegex = false;
            _regexError = null;
        }
        if (_canvas is not null)
            _ = _canvas.SetActiveSearchResult(null);
    }

    private void CloseSearchPanel()
    {
        _showSearchPanel = false;
        _searchQuery = "";
        _replaceQuery = "";
        _searchResults = [];
        _searchCurrentIndex = 0;
        _searchAllPages = false;
        _searchUseRegex = false;
        _regexError = null;
        if (_canvas is not null)
            _ = _canvas.SetActiveSearchResult(null);
    }

    private void OnSearchQueryChanged(string query)
    {
        _searchQuery = query;
        ValidateRegex();
        ExecuteSearch();
    }

    private void OnReplaceQueryChanged(string query)
    {
        _replaceQuery = query;
    }

    private void OnSearchUseRegexChanged(bool value)
    {
        _searchUseRegex = value;
        ValidateRegex();
        ExecuteSearch();
    }

    private void ValidateRegex()
    {
        _regexError = null;
        if (_searchUseRegex && !string.IsNullOrWhiteSpace(_searchQuery))
        {
            if (!DiagramSearchService.TryCreateRegex(_searchQuery.Trim(), out _, out var error))
            {
                _regexError = Loc["TmDiagram_RegexError", error ?? string.Empty];
            }
        }
    }

    /// <summary>The replacement text handed to Regex.Replace: as typed in regex mode, with `$`
    /// escaped in literal mode so "$0x" replaces to the text "$0x" instead of group 0 + "x".</summary>
    private string ReplacementText => _searchUseRegex ? _replaceQuery : _replaceQuery.Replace("$", "$$");

    /// <summary>The replace pattern: the query as typed in regex mode, the escaped literal otherwise.</summary>
    private bool TryCreateReplaceRegex(out System.Text.RegularExpressions.Regex? regex)
    {
        var pattern = _searchUseRegex
            ? _searchQuery.Trim()
            : System.Text.RegularExpressions.Regex.Escape(_searchQuery.Trim());
        return DiagramSearchService.TryCreateRegex(pattern, out regex, out _);
    }

    private void ExecuteSearch()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery) || !string.IsNullOrEmpty(_regexError))
        {
            _searchResults = [];
            _searchCurrentIndex = 0;
            if (_canvas is not null)
                _ = _canvas.SetActiveSearchResult(null);
            return;
        }

        _searchResults = _searchAllPages
            ? DiagramSearchService.SearchAllPages(_document, _searchQuery, _searchUseRegex)
            : DiagramSearchService.Search(_document, _searchQuery, _searchUseRegex);
        _searchCurrentIndex = _searchResults.Count > 0 ? 0 : 0;
        if (_canvas is not null)
        {
            var activeId = _searchResults.Count > 0
                ? _searchResults[0].NodeId ?? _searchResults[0].EdgeId
                : null;
            _ = _canvas.SetActiveSearchResult(activeId);
        }
    }

    private async Task OnSearchIndexChanged(int index)
    {
        _searchCurrentIndex = index;
        if (_searchResults.Count == 0 || _canvas is null) return;

        var result = _searchResults[index];

        if (_searchAllPages && result.PageIndex.HasValue && result.PageIndex.Value != _document?.ActivePageIndex)
        {
            if (_document is not null)
            {
                _document.ActivePageIndex = result.PageIndex.Value;
                EnsureCommandStackForActivePage();
                _selectedIds = [];
                await RerenderCanvas();
            }
        }

        var activeId = result.NodeId ?? result.EdgeId;
        await _canvas.SetActiveSearchResult(activeId);

        if (result.NodeId is not null)
        {
            await _canvas.FocusOnNode(result.NodeId);
            _selectedIds = [result.NodeId];
        }
        else if (result.EdgeId is not null)
        {
            await _canvas.FocusOnEdge(result.EdgeId);
            _selectedIds = [result.EdgeId];
        }
        await InvokeAsync(StateHasChanged);
    }

    private void OnSearchAllPagesChanged(bool value)
    {
        _searchAllPages = value;
        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            ExecuteSearch();
        }
    }

    private async Task OnReplace()
    {
        if (IsReadOnly || _document is null) return;
        if (_searchResults.Count == 0 || _searchCurrentIndex < 0 || _searchCurrentIndex >= _searchResults.Count)
            return;
        if (!TryCreateReplaceRegex(out var regex))
            return;

        var result = _searchResults[_searchCurrentIndex];
        var page = result.PageIndex.HasValue && _document is not null
            ? _document.Pages[result.PageIndex.Value]
            : _document?.ActivePage;
        if (page is null) return;

        var replaceResult = DiagramSearchService.ReplaceInResult(_document!, result, regex!, ReplacementText);
        if (replaceResult is null) return;

        if (result.NodeId is not null)
        {
            var node = page.Nodes.FirstOrDefault(n => n.Id == result.NodeId);
            if (node is null) return;
            var oldData = DeepCopy(node.Data);
            var newData = DeepCopy(node.Data);
            newData[replaceResult.DataKey] = replaceResult.NewValue;
            ActiveCommandStack.Push(new UpdateNodeDataCommand(_document!, result.NodeId, oldData, newData));
        }
        else if (result.EdgeId is not null)
        {
            var edge = page.Edges.FirstOrDefault(e => e.Id == result.EdgeId);
            if (edge is null) return;
            ActiveCommandStack.Push(new UpdateEdgeLabelCommand(_document!, result.EdgeId, replaceResult.OldValue, replaceResult.NewValue));
        }

        ExecuteSearch();
        if (_searchResults.Count > 0)
        {
            var nextIndex = Math.Min(_searchCurrentIndex, _searchResults.Count - 1);
            await OnSearchIndexChanged(nextIndex);
        }
        // A replacement mutates the document: the host must learn about it (DocumentChanged).
        await OnDocumentChanged(_document!);
    }

    private async Task OnReplaceAll()
    {
        if (IsReadOnly || _document is null) return;
        if (_searchResults.Count == 0 || !TryCreateReplaceRegex(out var regex))
            return;

        var changed = false;
        // The transaction commits BEFORE the document notification (one undo step).
        using (ActiveCommandStack.TransactionScope("Replace all"))
        {
            var processed = new HashSet<string>();
            foreach (var result in _searchResults.ToList())   // ToList: ExecuteSearch() replaces the list later
            {
                var replaceResult = DiagramSearchService.ReplaceInResult(_document, result, regex!, ReplacementText);
                if (replaceResult is null) continue;

                var key = result.NodeId is not null ? $"node:{result.NodeId}:{replaceResult.DataKey}" : $"edge:{result.EdgeId}:{replaceResult.DataKey}";
                if (!processed.Add(key)) continue;

                var page = result.PageIndex.HasValue
                    ? _document.Pages[result.PageIndex.Value]
                    : _document.ActivePage;
                if (page is null) continue;

                if (result.NodeId is not null)
                {
                    var node = page.Nodes.FirstOrDefault(n => n.Id == result.NodeId);
                    if (node is null) continue;
                    var oldData = DeepCopy(node.Data);
                    var newData = DeepCopy(node.Data);
                    newData[replaceResult.DataKey] = regex!.Replace(replaceResult.OldValue, ReplacementText);
                    ActiveCommandStack.Push(new UpdateNodeDataCommand(_document, result.NodeId, oldData, newData));
                    changed = true;
                }
                else if (result.EdgeId is not null)
                {
                    var edge = page.Edges.FirstOrDefault(e => e.Id == result.EdgeId);
                    if (edge is null) continue;
                    ActiveCommandStack.Push(new UpdateEdgeLabelCommand(_document, result.EdgeId, replaceResult.OldValue, regex!.Replace(replaceResult.OldValue, ReplacementText)));
                    changed = true;
                }
            }
        }
        ExecuteSearch();
        if (_searchResults.Count > 0)
            await OnSearchIndexChanged(0);
        if (changed) await OnDocumentChanged(_document);
        else await InvokeAsync(StateHasChanged);
    }

    private static Dictionary<string, object> DeepCopy(Dictionary<string, object> source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? [];
    }

    private void OnCanvasZoomChanged(double scale)
    {
        _zoomLevel = scale;
        StateHasChanged();
    }

    private void OnCanvasViewportChanged(DiagramMinimapViewport vp)
    {
        _minimapViewport = vp;
        _rulerViewportX = vp.X;
        _rulerViewportY = vp.Y;
        _rulerViewportW = vp.Width;
        _rulerViewportH = vp.Height;
    }

    private void ToggleRulers() => _showRulers = !_showRulers;

    private void OnRulerCursorMoved((double X, double Y) args)
    {
        _rulerCursorX = args.X;
        _rulerCursorY = args.Y;
        StateHasChanged();
    }

    private async Task OnRulerUnitChanged(MeasurementUnit unit)
    {
        if (_document?.ActivePage is null || IsReadOnly) return;
        _document.ActivePage.RulerUnit = unit;
        if (_canvas is not null)
            await _canvas.SetRulerUnit(unit.ToString().ToLowerInvariant());
        // A document property (serialized): the host must learn about it. Not undoable (like canvas size).
        await OnDocumentChanged(_document);
    }

    private async Task OnPageScaleChanged(double scale)
    {
        if (_document?.ActivePage is null || scale <= 0) return;
        _document.ActivePage.PageScale = scale;
        if (_canvas is not null)
            await _canvas.SetPageScale(scale);
        StateHasChanged();
    }

    private async Task OnMinimapNavigate(DiagramMinimapNavigateArgs args)
    {
        if (_canvas is not null)
            await _canvas.ScrollTo(args.CentreX, args.CentreY);
    }

    // ── Toolbar: Canvas size ─────────────────────────────────────────────────

    private void OpenCanvasSize()
    {
        if (IsReadOnly) return;
        _canvasWInput = ((int)(_document?.Width ?? 3000)).ToString();
        _canvasHInput = ((int)(_document?.Height ?? 2000)).ToString();
        _canvasSizeEditing = true;
    }

    private async Task ApplyCanvasSize()
    {
        _canvasSizeEditing = false;
        if (_document is null || IsReadOnly) return;
        if (!double.TryParse(_canvasWInput, out var w) || w < 100) return;
        if (!double.TryParse(_canvasHInput, out var h) || h < 100) return;
        if (Math.Abs(w - _document.Width) < 0.5 && Math.Abs(h - _document.Height) < 0.5) return;

        // Direct mutation (no dedicated command for canvas resize in MVP)
        _document.Width = w;
        _document.Height = h;
        if (_canvas is not null) await _canvas.UpdateCanvasSize(w, h);
        await OnDocumentChanged(_document);
    }

    // ── Toolbar: Title ───────────────────────────────────────────────────────

    private async Task OnTitleChanged(ChangeEventArgs e)
    {
        if (_document is null || IsReadOnly) return;
        _document.Title = e.Value?.ToString() ?? "";
        await OnDocumentChanged(_document);
    }

    private async Task HandlePageSizeChanged()
    {
        if (_document is null) return;
        if (_canvas is not null)
            await _canvas.UpdateCanvasSize(_document.Width, _document.Height);
        await OnDocumentChanged(_document);
    }

    // ── Toolbar: Undo / Redo ─────────────────────────────────────────────────

    private async Task OnUndoClicked()
    {
        if (IsReadOnly) return;
        ActiveCommandStack.Undo();
        await RerenderCanvas();
    }

    private async Task OnRedoClicked()
    {
        if (IsReadOnly) return;
        ActiveCommandStack.Redo();
        await RerenderCanvas();
    }

    // ── Toolbar: Edit operations ─────────────────────────────────────────────

    private async Task OnCutClicked()
    {
        if (_canvas is null || IsReadOnly || _selectedIds.Length == 0) return;
        await _canvas.OnCopy(_selectedIds);
        await _canvas.OnDeleteSelected(_selectedIds);
    }

    private async Task OnCopyClicked()
    {
        if (_canvas is null || IsReadOnly || _selectedIds.Length == 0) return;
        await _canvas.OnCopy(_selectedIds);
    }

    private async Task OnPasteClicked()
    {
        if (_canvas is null || IsReadOnly) return;
        await _canvas.OnPaste();
    }

    private async Task OnDuplicateClicked()
    {
        if (_canvas is null || IsReadOnly || _selectedIds.Length == 0) return;
        await _canvas.OnDuplicate(_selectedIds);
    }

    private async Task OnDeleteClicked()
    {
        if (_canvas is null || IsReadOnly || _selectedIds.Length == 0) return;
        await _canvas.OnDeleteSelected(_selectedIds);
    }

    private bool CanAlign => _document?.Nodes.Count(n => _selectedIds.Contains(n.Id)) > 1 && _selectedIds.All(id => _document?.Nodes.Any(n => n.Id == id) == true);
    private bool CanDistribute => _document?.Nodes.Count(n => _selectedIds.Contains(n.Id)) > 2 && _selectedIds.All(id => _document?.Nodes.Any(n => n.Id == id) == true);

    private async Task OnAlign(string alignment)
    {
        if (_document is null || IsReadOnly || !CanAlign) return;
        var ids = _document.Nodes.Where(n => _selectedIds.Contains(n.Id)).Select(n => n.Id).ToList();
        if (ids.Count <= 1) return;
        var cmd = new AlignNodesCommand(_document, ids, alignment);
        ActiveCommandStack.Push(cmd);
        await DocumentChanged.InvokeAsync(_document);
    }

    private async Task OnDistribute(string axis)
    {
        if (_document is null || IsReadOnly || !CanDistribute) return;
        var ids = _document.Nodes.Where(n => _selectedIds.Contains(n.Id)).Select(n => n.Id).ToList();
        if (ids.Count <= 2) return;
        var cmd = new DistributeNodesCommand(_document, ids, axis);
        ActiveCommandStack.Push(cmd);
        await DocumentChanged.InvokeAsync(_document);
    }

    private void OnSearchClicked()
    {
        _showSearchPanel = true;
        _searchQuery = string.Empty;
        _searchResults = [];
        _searchCurrentIndex = -1;
    }

    private void OnToggleGrid()
    {
        ShowGrid = !ShowGrid;
        StateHasChanged();
    }

    private void OnToggleSnap()
    {
        GridSize = GridSize == 0 ? 8 : 0;
        StateHasChanged();
    }

    private void OnTogglePageView()
    {
        ShowPageView = !ShowPageView;
        StateHasChanged();
    }

    // ── Toolbar: Format Painter ──────────────────────────────────────────────

    private void OnCopyStyle()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        var node = _document.Nodes.FirstOrDefault(n => n.Id == _selectedIds[0]);
        if (node is null) return;
        new CopyStyleCommand(node, includeSize: false).Execute();
    }

    private async Task OnPasteStyle()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        ActiveCommandStack.Push(new PasteStyleCommand(_document, _selectedIds));
        await OnDocumentChanged(_document);
    }

    private async Task OnPasteSize()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        ActiveCommandStack.Push(new PasteSizeCommand(_document, _selectedIds));
        await OnDocumentChanged(_document);
    }

    // ── Toolbar: Zoom ────────────────────────────────────────────────────────

    private static readonly double[] ZoomSteps =
        [DiagramZoomLimits.MinScale, 0.25, 0.33, 0.5, 0.67, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, DiagramZoomLimits.MaxScale];

    private async Task ZoomIn()
    {
        var next = ZoomSteps.FirstOrDefault(z => z > _zoomLevel + 1e-9);
        if (next == 0) next = ZoomSteps[^1];
        await ApplyZoom(next);
    }

    private async Task ZoomOut()
    {
        var prev = ZoomSteps.LastOrDefault(z => z < _zoomLevel - 1e-9);
        if (prev == 0) prev = ZoomSteps[0];
        await ApplyZoom(prev);
    }

    private async Task ApplyZoom(double level)
    {
        _zoomLevel = DiagramZoomLimits.Clamp(level);
        if (_canvas is not null)
            await _canvas.SetZoom(_zoomLevel);
    }

    private async Task FitToView()
    {
        if (_canvas is not null)
        {
            var scale = await JS.InvokeAsync<double>("tmDiagramEditor.fitToView", _canvas.GetContainerRef(), 40);
            _zoomLevel = scale;
        }
    }

    private bool _layoutMenuOpen;

    private void ToggleLayoutMenu() => _layoutMenuOpen = !_layoutMenuOpen;

    private async Task RunLayout(string algorithm, string direction = "TB")
    {
        _layoutMenuOpen = false;
        if (IsReadOnly) return;
        if (_canvas is null) return;
        await _canvas.RunLayoutAsync(algorithm, direction);
    }

    private async Task HandleLayoutSelect(string value)
    {
        switch (value)
        {
            case "dagre-tb": await RunLayout("dagre", "TB"); break;
            case "dagre-lr": await RunLayout("dagre", "LR"); break;
            case "tree-tb": await RunLayout("tree", "TB"); break;
            case "tree-lr": await RunLayout("tree", "LR"); break;
            case "force": await RunLayout("force"); break;
            case "circle": await RunLayout("circle"); break;
            case "grid": await RunLayout("grid"); break;
        }
    }

    private async Task HandleArrangeSelect(string value)
    {
        switch (value)
        {
            case "align-left": await OnAlign("left"); break;
            case "align-center": await OnAlign("center"); break;
            case "align-right": await OnAlign("right"); break;
            case "align-top": await OnAlign("top"); break;
            case "align-middle": await OnAlign("middle"); break;
            case "align-bottom": await OnAlign("bottom"); break;
            case "distribute-horizontal": await OnDistribute("horizontal"); break;
            case "distribute-vertical": await OnDistribute("vertical"); break;
        }
    }

    private async Task HandleInsertSelect(string value)
    {
        if (IsReadOnly) return;
        switch (value)
        {
            case "table":
                _showTableInserter = true;
                await PositionTableInserterAsync();
                break;
            case "text": await InsertText(); break;
        }
    }

    /// <summary>
    /// UX review phase 4: the picker is anchored where the user invoked it — the Insert trigger
    /// while the group sits on the bar, the "More" trigger once the group collapsed there. The
    /// measurement happens before the popover renders; without JS (tests, disposed host) the
    /// popover keeps its last/fallback position.
    /// </summary>
    private async Task PositionTableInserterAsync()
    {
        try
        {
            var anchor = await JS.InvokeAsync<double[]?>("tmDiagramEditor.tableInserterAnchor", _editorRoot);
            if (anchor is { Length: 2 })
            {
                _tableInserterX = anchor[0];
                _tableInserterY = anchor[1];
            }
        }
        catch (JSException)
        {
            // Keep the previous position; the popover still opens.
        }
        catch (JSDisconnectedException)
        {
            // Host is going away — nothing to anchor.
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleTableInsert((int Rows, int Columns) args)
    {
        _showTableInserter = false;
        await InsertTable(args.Rows, args.Columns);
    }

    private async Task InsertTable(int rows, int columns)
    {
        if (_document is null || IsReadOnly) return;
        var stencil = StencilRegistry.GetStencil("table.basic");
        if (stencil is null) return;

        var w = columns * 80 + 20;
        var h = rows * 30 + 20;
        // Place at the visible viewport centre — see HandleQuickInsert for rationale.
        var page = _document.Pages[_document.ActivePageIndex];
        double cx = page.Width / 2, cy = page.Height / 2;
        if (_canvas is not null)
        {
            var vp = _canvas.GetViewport();
            if (vp.W > 0 && vp.H > 0)
            {
                cx = vp.X + vp.W / 2;
                cy = vp.Y + vp.H / 2;
            }
        }
        var x = Math.Round((cx - w / 2) / GridSize) * GridSize;
        var y = Math.Round((cy - h / 2) / GridSize) * GridSize;

        var node = new DiagramNode
        {
            StencilId = "table.basic",
            X = x,
            Y = y,
            W = w,
            H = h,
            ZIndex = _document.Nodes.Count > 0 ? _document.Nodes.Max(n => n.ZIndex) + 1 : 0,
            LayerId = _activeLayerId,
            IsCollapsible = false,
        };

        var cells = new List<DiagramTableCellData>();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                cells.Add(new DiagramTableCellData
                {
                    Row = r,
                    Column = c,
                    Text = r == 0 ? $"Header {c + 1}" : $"Cell {r},{c}",
                    Style = r == 0 ? new DiagramTableCellStyle { FontWeight = "bold", BackgroundColor = "#f3f4f6" } : null
                });
            }
        }

        node.Data["rowCount"] = rows;
        node.Data["columnCount"] = columns;
        node.Data["cells"] = cells;

        ActiveCommandStack.Push(new AddNodeCommand(_document, node));
        await OnDocumentChanged(_document);
        if (_canvas is not null)
        {
            await _canvas.SetSelection(node.Id);
            await OnSelectionChanged([node.Id]);
        }
    }

    private async Task InsertText()
    {
        if (_document is null || IsReadOnly) return;
        await HandleQuickInsert("general.text");
    }    

    private async Task HandleExportSelect(string value)
    {
        switch (value)
        {
            case "json": await ExportJson(); break;
            case "svg": await ExportSvg(); break;
            case "png": await ExportPng(); break;
            case "pdf": await ExportPdf(); break;
            case "viewportSvg": await ExportViewportSvg(); break;
        }
    }

    private void HandleImportSelect(string value)
    {
        switch (value)
        {
            case "sql": OpenSqlImportDialog(); break;
            case "csv": OpenCsvImportDialog(); break;
        }
    }

    // ── Toolbar: Export ──────────────────────────────────────────────────────

    private void ToggleExportMenu() => _exportMenuOpen = !_exportMenuOpen;

    private async Task ExportJson()
    {
        _exportMenuOpen = false;
        if (_document is null) return;
        var json = DiagramSerializer.Serialize(_document);
        var fileName = SanitizeFileName(_document.Title) + ".diagram.json";
        await DownloadFile(fileName, "application/json", System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task ExportSvg()
    {
        _exportMenuOpen = false;
        await ExportToServerAsync("svg", "image/svg+xml", ".svg");
    }

    private async Task ExportPng()
    {
        _exportMenuOpen = false;
        await ExportToServerAsync("png", "image/png", ".png");
    }

    private async Task ExportPdf()
    {
        _exportMenuOpen = false;
        await ExportToServerAsync("pdf", "application/pdf", ".pdf");
    }

    private async Task ExportViewportSvg()
    {
        _exportMenuOpen = false;
        if (_canvas is null) return;
        try
        {
            var svg = await _canvas.ExportViewportAsSvgAsync();
            if (string.IsNullOrEmpty(svg)) return;
            var fileName = SanitizeFileName(_document?.Title ?? "diagram") + "-viewport.svg";
            await DownloadFile(fileName, "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(svg));
        }
        catch (Exception ex)
        {
            _exportError = Loc["TmDiagramEditor_ExportError", ex.Message];
        }
    }

    private async Task ExportToServerAsync(string format, string mimeType, string extension, bool exportAllPages = false)
    {
        if (_document is null) return;

        _exporting = true;
        _exportError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            var client = string.IsNullOrEmpty(ExportHttpClientName)
                ? HttpClientFactory.CreateClient()
                : HttpClientFactory.CreateClient(ExportHttpClientName);

            var request = new
            {
                Document = _document,
                Options = new
                {
                    PageIndex = _document.ActivePageIndex,
                    ExportAllPages = exportAllPages,
                    Padding = 20,
                }
            };

            var response = await client.PostAsJsonAsync($"/api/diagram/export/{format}", request);
            response.EnsureSuccessStatusCode();

            byte[] data;
            if (format == "svg")
            {
                var svg = await response.Content.ReadAsStringAsync();
                data = System.Text.Encoding.UTF8.GetBytes(svg);
            }
            else
            {
                data = await response.Content.ReadAsByteArrayAsync();
            }

            var fileName = SanitizeFileName(_document.Title) + extension;
            await DownloadFile(fileName, mimeType, data);
        }
        catch (Exception ex)
        {
            _exportError = Loc["TmDiagramEditor_ExportError", ex.Message];
        }
        finally
        {
            _exporting = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ── Toolbar: Import ──────────────────────────────────────────────────────

    private async Task OnImportFile(InputFileChangeEventArgs e)
    {
        _importError = null;
        var file = e.File;
        if (file is null) return;
        if (IsReadOnly) return;

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            using var reader = new System.IO.StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            if (!DiagramSerializer.TryDeserialize(json, out var doc) || doc is null)
            {
                _importError = Loc["TmDiagramEditor_ImportError_InvalidJson"];
                return;
            }

            _document = doc;
            _document.SnapToGrid(GridSize);
            ActiveCommandStack.Clear();
            _selectedIds = [];
            await DocumentChanged.InvokeAsync(_document);
            await RerenderCanvas();
        }
        catch (Exception ex)
        {
            _importError = Loc["TmDiagramEditor_ImportError_Generic", ex.Message];
        }
    }

    // ── Toolbar: Read-only toggle ────────────────────────────────────────────

    // DI1: the toggle flips the internal user state only. Assigning the [Parameter] ReadOnly
    // from inside the component let a hosted read-only preview unlock itself — that is gone.
    private void ToggleReadOnly()
    {
        _userReadOnly = !_userReadOnly;
        ApplyReadOnlyTransition();
    }

    /// <summary>Entering the effective read-only state closes the surfaces that only make sense
    /// while editing (open context menu, canvas-size editor, table picker, template gallery,
    /// SQL/CSV dialogs, connect dialog, layout menu).</summary>
    private void CloseEditSurfaces()
    {
        if (_contextMenuOpen) _ = CloseContextMenu();
        _canvasSizeEditing = false;
        _showTableInserter = false;
        _showTemplateGallery = false;
        _sqlImportDialogOpen = false;
        _csvImportDialogOpen = false;
        CloseConnectModal();
        _layoutMenuOpen = false;
    }

    // ── Toolbox drag activation ──────────────────────────────────────────────

    private async Task OnToolboxDragStart(string stencilId)
    {
        if (IsReadOnly) return;
        var stencil = StencilRegistry.GetStencil(stencilId);
        if (stencil?.Kind == DiagramStencilKind.Edge)
        {
            _activeEdgeStencilId = stencil.Id;
            await SetToolMode("edge");
        }
    }

    private async Task OnToolboxDrop((string StencilId, double X, double Y) drop)
    {
        if (_document is null || IsReadOnly) return;

        var stencil = StencilRegistry.GetStencil(drop.StencilId);
        if (stencil?.Kind == DiagramStencilKind.Edge)
        {
            _activeEdgeStencilId = stencil.Id;
            await SetToolMode("edge");
            return;
        }

        var w = stencil?.DefaultWidth ?? 120;
        var h = stencil?.DefaultHeight ?? 60;
        var x = Math.Round(drop.X / GridSize) * GridSize;
        var y = Math.Round(drop.Y / GridSize) * GridSize;

        var node = new DiagramNode
        {
            StencilId = drop.StencilId,
            X = x,
            Y = y,
            W = w,
            H = h,
            ZIndex = _document.Nodes.Count > 0 ? _document.Nodes.Max(n => n.ZIndex) + 1 : 0,
            LayerId = _activeLayerId,
            IsCollapsible = stencil?.IsCollapsible ?? false,
        };

        // Copy default data
        foreach (var kvp in stencil?.DefaultData ?? [])
        {
            node.Data[kvp.Key] = kvp.Value;
        }

        DiagramUmlAutoHeight.Fit(node);

        // Generate ports from stencil
        foreach (var portDef in stencil?.Ports ?? [])
        {
            node.Ports.Add(new DiagramPort
            {
                Name = portDef.Name,
                Side = portDef.Side,
                Offset = portDef.Offset,
                IsInput = portDef.IsInput,
                IsOutput = portDef.IsOutput,
                MagnetStrategy = portDef.MagnetStrategy,
            });
        }

        // Swimlane drop detection — the centre must come from the FITTED node size
        // (class-like UML stencils grow in Fit above), otherwise a taller-than-default
        // node is dropped into the cell its top half overlaps.
        double cx = node.X + node.W / 2;
        double cy = node.Y + node.H / 2;
        foreach (var candidate in _document.Nodes
            .Where(n => n.SwimlaneData is not null)
            .OrderByDescending(n => n.ZIndex))
        {
            if (Services.SwimlaneLayoutService.ComputeCell(candidate, cx, cy) is var cell && cell.HasValue)
            {
                node.ParentNodeId = candidate.Id;
                node.SwimlaneRow = cell.Value.Row;
                node.SwimlaneColumn = cell.Value.Column;
                Services.SwimlaneLayoutService.ArrangeChild(candidate, node);
                break;
            }
        }

        // Initialize swimlane data for swimlane stencils
        if (stencil?.IsSwimlane == true)
        {
            node.SwimlaneData = new()
            {
                IsHorizontal = stencil.Layout.BackgroundShape == "swimlane-horizontal",
                RowCount = 2,
                ColumnCount = 1,
                HeaderSize = 30,
                RowSizes = [],
                ColumnSizes = [],
                CellLabels = ["Lane 1", "Lane 2"]
            };
        }

        ActiveCommandStack.Push(new AddNodeCommand(_document, node));
        await OnDocumentChanged(_document);
        if (_canvas is not null)
        {
            await _canvas.SetSelection(node.Id);
            await OnSelectionChanged([node.Id]);
        }
    }

    // ── Port interaction / Edge creation ────────────────────────────────────

    private void OnPortMouseDownEvent((string NodeId, string PortId) args)
    {
        // Edge drawing is handled in JS; this is a hook for future extensions
    }

    private async Task OnEdgeCreated((string? SourceNodeId, string? SourcePortId, string? TargetNodeId, string? TargetPortId, string? SourceSide, double SourceOffset, string? TargetSide, double TargetOffset, string? TargetEdgeId, double TargetEdgeT, double? SourceConstraintRx, double? SourceConstraintRy, bool? SourceConstraintPerimeter, double? TargetConstraintRx, double? TargetConstraintRy, bool? TargetConstraintPerimeter, double? TargetPointX, double? TargetPointY, double? SourcePointX, double? SourcePointY, double[]? WaypointsXY) args)
    {
        if (_document is null || IsReadOnly) return;

        if (GridSize > 0)
        {
            if (args.TargetPointX is not null && args.TargetPointY is not null)
            {
                args.TargetPointX = Math.Round(args.TargetPointX.Value / GridSize) * GridSize;
                args.TargetPointY = Math.Round(args.TargetPointY.Value / GridSize) * GridSize;
            }

            if (args.SourcePointX is not null && args.SourcePointY is not null)
            {
                args.SourcePointX = Math.Round(args.SourcePointX.Value / GridSize) * GridSize;
                args.SourcePointY = Math.Round(args.SourcePointY.Value / GridSize) * GridSize;
            }

            if (args.WaypointsXY is not null)
            {
                for (var i = 0; i + 1 < args.WaypointsXY.Length; i += 2)
                {
                    args.WaypointsXY[i] = Math.Round(args.WaypointsXY[i] / GridSize) * GridSize;
                    args.WaypointsXY[i + 1] = Math.Round(args.WaypointsXY[i + 1] / GridSize) * GridSize;
                }
            }
        }

        var edge = new DiagramEdge();

        // Inherit the last used edge style (stroke, arrow, routing…) so the
        // look-and-feel carries across newly drawn edges until the user
        // explicitly edits one through the properties panel.
        _document.LastUsedEdgeStyle?.ApplyTo(edge);
        if (_activeEdgeStencilId is not null && StencilRegistry.GetStencil(_activeEdgeStencilId) is { Kind: DiagramStencilKind.Edge } edgeStencil)
        {
            DiagramEdgeStencilFactory.ApplyDefaults(edge, edgeStencil);
        }

        // ── Source terminal ──────────────────────────────────────────────────
        // Either attached to a node (with optional port / constraint / side) or
        // floating at an absolute document point. Constraint takes priority
        // over port when both were supplied by the JS layer.
        bool floatingSource;
        if (!string.IsNullOrEmpty(args.SourceNodeId))
        {
            var sourceNode = _document.Nodes.FirstOrDefault(n => n.Id == args.SourceNodeId);
            if (sourceNode is null) return;

            string? sourcePortId = args.SourcePortId;
            if (sourcePortId is null && !string.IsNullOrEmpty(args.SourceSide))
            {
                var side = Enum.Parse<PortSide>(args.SourceSide, true);
                var port = sourceNode.Ports.FirstOrDefault(p => p.Side == side);
                if (port is null)
                {
                    port = new DiagramPort { Side = side, Offset = args.SourceOffset, IsInput = false, IsOutput = true };
                    sourceNode.Ports.Add(port);
                }
                sourcePortId = port.Id;
            }

            edge.SourceNodeId = args.SourceNodeId;
            edge.SourcePortId = sourcePortId;

            if (args.SourceConstraintRx is not null && args.SourceConstraintRy is not null)
            {
                edge.SourceConstraint = new DiagramConnectionConstraint
                {
                    RelativeX = args.SourceConstraintRx.Value,
                    RelativeY = args.SourceConstraintRy.Value,
                    Perimeter = args.SourceConstraintPerimeter ?? true
                };
                edge.SourcePortId = null;
            }
            floatingSource = false;
        }
        else if (args.SourcePointX is not null && args.SourcePointY is not null)
        {
            edge.SourceNodeId = null;
            edge.SourcePortId = null;
            edge.SourceConstraint = null;
            edge.SourcePoint = new DiagramPoint(args.SourcePointX.Value, args.SourcePointY.Value);
            floatingSource = true;
        }
        else
        {
            return; // No valid source
        }

        if (!string.IsNullOrEmpty(args.TargetEdgeId))
        {
            // Edge-to-edge connection
            edge.TargetEdgeId = args.TargetEdgeId;
            edge.TargetEdgeT = args.TargetEdgeT;
        }
        else if (!string.IsNullOrEmpty(args.TargetNodeId))
        {
            var targetNode = _document.Nodes.FirstOrDefault(n => n.Id == args.TargetNodeId);
            if (targetNode is null) return;

            string? targetPortId = args.TargetPortId;
            if (targetPortId is null && !string.IsNullOrEmpty(args.TargetSide))
            {
                var side = Enum.Parse<PortSide>(args.TargetSide, true);
                var port = targetNode.Ports.FirstOrDefault(p => p.Side == side);
                if (port is null)
                {
                    port = new DiagramPort { Side = side, Offset = args.TargetOffset, IsInput = true, IsOutput = false };
                    targetNode.Ports.Add(port);
                }
                targetPortId = port.Id;
            }

            edge.TargetNodeId = args.TargetNodeId;
            edge.TargetPortId = targetPortId;

            if (args.TargetConstraintRx is not null && args.TargetConstraintRy is not null)
            {
                edge.TargetConstraint = new DiagramConnectionConstraint
                {
                    RelativeX = args.TargetConstraintRx.Value,
                    RelativeY = args.TargetConstraintRy.Value,
                    Perimeter = args.TargetConstraintPerimeter ?? true
                };
                edge.TargetPortId = null;
            }
        }
        else if (args.TargetPointX is not null && args.TargetPointY is not null)
        {
            edge.TargetPoint = new DiagramPoint(args.TargetPointX.Value, args.TargetPointY.Value);
        }
        else
        {
            return; // No valid target
        }

        // Routing matrix: a straight line feels most natural when neither end
        // is anchored to a node (there is no perimeter to route around). All
        // other combinations keep the default orthogonal routing coming from
        // `new DiagramEdge()`.
        bool floatingTarget =
            string.IsNullOrEmpty(args.TargetNodeId)
            && string.IsNullOrEmpty(args.TargetEdgeId)
            && args.TargetPointX is not null && args.TargetPointY is not null;
        if (floatingSource && floatingTarget)
        {
            edge.Routing = "straight";
        }

        // Polyline: flat [x0,y0, x1,y1, ...] array from JS becomes the edge's
        // manually routed waypoint list. Ignore malformed (odd-length) input
        // defensively. A non-empty list also forces manual routing so the
        // router respects the exact click path the user drew.
        if (args.WaypointsXY is { Length: > 0 } wp && (wp.Length % 2 == 0))
        {
            var points = new List<DiagramPoint>(wp.Length / 2);
            for (int i = 0; i + 1 < wp.Length; i += 2)
            {
                points.Add(new DiagramPoint(wp[i], wp[i + 1]));
            }
            edge.Waypoints = points;
            edge.IsManuallyRouted = true;
            if (floatingSource && floatingTarget)
            {
                edge.Routing = "straight";
            }
        }

        ActiveCommandStack.Push(new AddEdgeCommand(_document, edge));
        await OnDocumentChanged(_document);

        // Select the new edge
        _selectedIds = [edge.Id];
        if (_canvas is not null)
        {
            await _canvas.SetSelection(edge.Id);
        }
    }

    private async Task HandleEdgeRoutingChanged((string EdgeId, string OldRouting, string NewRouting) args)
    {
        if (_document is null || _canvas is null || IsReadOnly) return;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == args.EdgeId);
        if (edge is null) return;

        var oldWaypoints = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();

        if (args.NewRouting is "orthogonal" or "elbow" or "segment")
        {
            edge.Waypoints = await _canvas.ComputeOrthogonalWaypointsAsync(edge);
        }
        else
        {
            edge.Waypoints.Clear();
        }

        var newWaypoints = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
        ActiveCommandStack.Push(new UpdateEdgeRoutingCommand(_document, edge.Id, args.OldRouting, args.NewRouting, oldWaypoints, newWaypoints));
        await OnDocumentChanged(_document);
    }

    // When the user flips "Manual routing" to true on an orthogonal-like edge
    // that has no explicit Waypoints yet (because it has always been
    // auto-routed), seed Waypoints from the current auto-routed path so the
    // hand-off is visually invisible. Without this, `IsManuallyRouted=true`
    // combined with `Waypoints=[]` renders as a straight 2-point line (the
    // router skips manual edges), which visibly "breaks" the edge at the
    // moment the user intends to preserve its shape for manual tweaking.
    private async Task HandleEdgeManualRoutingChanged((string EdgeId, bool Value) args)
    {
        if (_document is null || _canvas is null || IsReadOnly) return;
        if (!args.Value) return;
        var edge = _document.Edges.FirstOrDefault(e => e.Id == args.EdgeId);
        if (edge is null) return;
        if (edge.Waypoints.Count > 0) return;
        if (edge.Routing is not ("orthogonal" or "elbow" or "segment")) return;

        // Temporarily clear the manual flag so `ComputeOrthogonalWaypointsAsync`
        // does not short-circuit and return the (still-empty) current list.
        edge.IsManuallyRouted = false;
        var seeded = await _canvas.ComputeOrthogonalWaypointsAsync(edge);
        edge.IsManuallyRouted = true;
        if (seeded.Count > 0)
        {
            edge.Waypoints = seeded;
            await OnDocumentChanged(_document);
        }
    }

    // ── Connect modal ────────────────────────────────────────────────────────

    private void OnConnectArrowClicked((string NodeId, string Direction) args)
    {
        if (IsReadOnly || _document is null) return;
        _connectSourceNodeId = args.NodeId;
        _connectDirection = args.Direction;
        _connectModalOpen = true;
        StateHasChanged();
    }

    private void CloseConnectModal()
    {
        _connectModalOpen = false;
        _connectSourceNodeId = null;
        _connectDirection = null;
    }

    private void OpenSqlImportDialog()
    {
        if (IsReadOnly) return;
        _sqlImportDialogOpen = true;
    }

    private void CloseSqlImportDialog()
    {
        _sqlImportDialogOpen = false;
    }

    private async Task OnSqlImport(SqlImportResult result)
    {
        _sqlImportDialogOpen = false;
        if (IsReadOnly) return;

        Document = result.Document;
        _document = result.Document;
        _selectedIds = [];
        _selectedTableCells.Clear();
        _pageCommandStacks.Clear();
        ActiveCommandStack.Clear();

        await OnDocumentChanged(_document);

        if (_canvas is not null && result.Document.ActivePage.Nodes.Count > 0)
        {
            await _canvas.SetSelection([]);
            if (result.LayoutDirection.Equals("LR", StringComparison.OrdinalIgnoreCase))
                await RunLayout("dagre", "LR");
            else
                await RunLayout("dagre", "TB");
        }
    }

    private void OpenCsvImportDialog()
    {
        if (IsReadOnly) return;
        _csvImportDialogOpen = true;
    }

    private void CloseCsvImportDialog()
    {
        _csvImportDialogOpen = false;
    }

    private async Task OnCsvImport(CsvImportResult result)
    {
        _csvImportDialogOpen = false;
        if (IsReadOnly) return;

        Document = result.Document;
        _document = result.Document;
        _selectedIds = [];
        _selectedTableCells.Clear();
        _pageCommandStacks.Clear();
        ActiveCommandStack.Clear();

        await OnDocumentChanged(_document);

        if (_canvas is not null && result.Document.ActivePage.Nodes.Count > 0)
        {
            await _canvas.SetSelection([]);
            if (!string.IsNullOrWhiteSpace(result.LayoutAlgorithm))
                await RunLayout(result.LayoutAlgorithm, "TB");
        }
    }

    // ── Template gallery ─────────────────────────────────────────────────────

    private void OpenTemplateGallery()
    {
        if (IsReadOnly) return;
        _showTemplateGallery = true;
    }

    private void CloseTemplateGallery()
    {
        _showTemplateGallery = false;
    }

    private async Task OnTemplateSelected(Tempo.Blazor.Components.Diagram.Templates.DiagramTemplate template)
    {
        _showTemplateGallery = false;
        if (IsReadOnly) return;

        DiagramDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(template.DocumentJson))
        {
            DiagramSerializer.TryDeserialize(template.DocumentJson, out doc);
        }

        if (doc is not null)
        {
            doc = doc.CloneWithNewIds();
        }
        else
        {
            doc = CreateEmptyDocument();
        }

        doc.Title = template.Name;
        DiagramUmlAutoHeight.FitAll(doc);

        Document = doc;
        _document = doc;
        _selectedIds = [];
        _selectedTableCells.Clear();
        _pageCommandStacks.Clear();
        ActiveCommandStack.Clear();
        EnsureCommandStackForActivePage();

        await OnDocumentChanged(_document);

        if (_canvas is not null)
        {
            await _canvas.SetSelection([]);
            await _canvas.FitToView();
        }
    }

    private async Task OnStencilSelectedForConnect(string stencilId)
    {
        if (_document is null || IsReadOnly || _connectSourceNodeId is null || _connectDirection is null) return;

        var sourceNode = _document.Nodes.FirstOrDefault(n => n.Id == _connectSourceNodeId);
        var stencil = StencilRegistry.GetStencil(stencilId);
        if (sourceNode is null || stencil is null) return;

        var w = stencil.DefaultWidth;
        var h = stencil.DefaultHeight;

        var newNode = new DiagramNode
        {
            StencilId = stencilId,
            X = 0,
            Y = 0,
            W = w,
            H = h,
            ZIndex = _document.Nodes.Count > 0 ? _document.Nodes.Max(n => n.ZIndex) + 1 : 0,
            LayerId = sourceNode.LayerId ?? _activeLayerId,
            IsCollapsible = stencil?.IsCollapsible ?? false,
        };

        foreach (var kvp in stencil.DefaultData)
            newNode.Data[kvp.Key] = kvp.Value;

        DiagramUmlAutoHeight.Fit(newNode);

        // Position AFTER the fit: class-like UML stencils grow (e.g. abstract-class 140→150),
        // and the new node must be centred/offset by the height the editor actually inserts.
        var (x, y) = ComputeConnectPosition(sourceNode, w, newNode.H, _connectDirection);
        x = Math.Round(x / GridSize) * GridSize;
        y = Math.Round(y / GridSize) * GridSize;
        newNode.X = x;
        newNode.Y = y;

        foreach (var portDef in stencil.Ports)
        {
            newNode.Ports.Add(new DiagramPort
            {
                Name = portDef.Name,
                Side = portDef.Side,
                Offset = portDef.Offset,
                IsInput = portDef.IsInput,
                IsOutput = portDef.IsOutput,
            });
        }

        using (ActiveCommandStack.TransactionScope(Loc["TmDiagramEditor_ConnectTransaction"]))
        {
            ActiveCommandStack.Push(new AddNodeCommand(_document, newNode));

            // Determine ports based on direction
            var (sourcePort, targetPort) = ResolveConnectPorts(sourceNode, newNode, _connectDirection);

            var edge = new DiagramEdge
            {
                SourceNodeId = sourceNode.Id,
                SourcePortId = sourcePort?.Id,
                TargetNodeId = newNode.Id,
                TargetPortId = targetPort?.Id,
            };

            ActiveCommandStack.Push(new AddEdgeCommand(_document, edge));

            if (_canvas is not null)
            {
                edge.Waypoints = await _canvas.ComputeOrthogonalWaypointsAsync(edge);
            }
        }

        _selectedIds = [newNode.Id];
        await OnDocumentChanged(_document);
        if (_canvas is not null)
        {
            await _canvas.SetSelection(newNode.Id);
            await OnSelectionChanged([newNode.Id]);
        }

        // The recent is recorded only after the node and edge exist: a failed creation leaves the
        // recents untouched.
        await RememberRecentStencilAsync(stencilId);

        CloseConnectModal();
    }

    private static (double X, double Y) ComputeConnectPosition(DiagramNode source, double newW, double newH, string direction)
    {
        const double padding = 40.0;
        return direction switch
        {
            "n" => (source.X + source.W / 2.0 - newW / 2.0, source.Y - newH - padding),
            "e" => (source.X + source.W + padding, source.Y + source.H / 2.0 - newH / 2.0),
            "s" => (source.X + source.W / 2.0 - newW / 2.0, source.Y + source.H + padding),
            "w" => (source.X - newW - padding, source.Y + source.H / 2.0 - newH / 2.0),
            _ => (source.X + source.W + padding, source.Y),
        };
    }

    private static (DiagramPort? SourcePort, DiagramPort? TargetPort) ResolveConnectPorts(DiagramNode source, DiagramNode target, string direction)
    {
        var sourceSide = direction switch
        {
            "n" => PortSide.Top,
            "e" => PortSide.Right,
            "s" => PortSide.Bottom,
            "w" => PortSide.Left,
            _ => PortSide.Right,
        };
        var targetSide = direction switch
        {
            "n" => PortSide.Bottom,
            "e" => PortSide.Left,
            "s" => PortSide.Top,
            "w" => PortSide.Right,
            _ => PortSide.Left,
        };

        var sp = source.Ports.FirstOrDefault(p => p.Side == sourceSide) ?? source.Ports.FirstOrDefault();
        var tp = target.Ports.FirstOrDefault(p => p.Side == targetSide) ?? target.Ports.FirstOrDefault();
        return (sp, tp);
    }

    // ── Page tabs ────────────────────────────────────────────────────────────

    private void AddPage()
    {
        if (_document is null || IsReadOnly) return;

        var newPage = new DiagramPage
        {
            Name = $"{Loc["TmDiagramEditor_PageName"]} {_document.Pages.Count + 1}",
            Width = _document.ActivePage.Width,
            Height = _document.ActivePage.Height,
        };
        _document.Pages.Add(newPage);
        _document.ActivePageIndex = _document.Pages.Count - 1;
        EnsureCommandStackForActivePage();
        _selectedIds = [];
        _ = RerenderCanvas();
    }

    private void RemovePage(string pageId)
    {
        if (_document is null || IsReadOnly || _document.Pages.Count <= 1) return;

        var index = _document.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0) return;

        if (_pageCommandStacks.TryGetValue(pageId, out var stack))
        {
            stack.OnStackChanged -= OnStackChanged;
            _pageCommandStacks.Remove(pageId);
        }

        _document.Pages.RemoveAt(index);
        if (_document.ActivePageIndex >= _document.Pages.Count)
            _document.ActivePageIndex = _document.Pages.Count - 1;
        if (_document.ActivePageIndex < 0)
            _document.ActivePageIndex = 0;

        EnsureCommandStackForActivePage();
        _selectedIds = [];
        _ = RerenderCanvas();
    }

    private void DuplicatePage(string pageId)
    {
        if (_document is null || IsReadOnly) return;

        var source = _document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (source is null) return;

        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var copy = System.Text.Json.JsonSerializer.Deserialize<DiagramPage>(json);
        if (copy is null) return;

        copy.Id = Guid.NewGuid().ToString();
        copy.Name = $"{source.Name} ({Loc["TmDiagramEditor_CopySuffix"] ?? "copy"})";

        var index = _document.Pages.FindIndex(p => p.Id == pageId);
        _document.Pages.Insert(index + 1, copy);
        _document.ActivePageIndex = index + 1;
        EnsureCommandStackForActivePage();
        _selectedIds = [];
        _ = RerenderCanvas();
    }

    private void SelectPage(string pageId)
    {
        if (_document is null) return;
        var index = _document.Pages.FindIndex(p => p.Id == pageId);
        if (index < 0 || index == _document.ActivePageIndex) return;

        _document.ActivePageIndex = index;
        EnsureCommandStackForActivePage();
        _selectedIds = [];
        _groupStack.Clear();
        _ = RerenderCanvas();
    }

    private void StartRenamePage(string pageId)
    {
        if (IsReadOnly) return;
        var page = _document?.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;

        _pageRenamingId = pageId;
        _pageRenameInput = page.Name;
        _ = FocusRenameInputAsync();
    }

    private async Task FocusRenameInputAsync()
    {
        await Task.Yield();
        try
        {
            await _pageRenameInputRef.FocusAsync();
        }
        catch
        {
            // ignore if element not yet rendered
        }
    }

    private void CommitRenamePage(string pageId)
    {
        if (_document is null || IsReadOnly) return;
        var page = _document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is not null && !string.IsNullOrWhiteSpace(_pageRenameInput))
        {
            page.Name = _pageRenameInput.Trim();
        }
        _pageRenamingId = null;
        _pageRenameInput = "";
        _ = RerenderCanvas();
    }

    private void OnRenamePageKeyDown(KeyboardEventArgs e, string pageId)
    {
        if (e.Key == "Enter")
        {
            CommitRenamePage(pageId);
        }
        else if (e.Key == "Escape")
        {
            _pageRenamingId = null;
            _pageRenameInput = "";
            _ = RerenderCanvas();
        }
    }

    private void OnPageTabDragStart(string pageId)
    {
        if (IsReadOnly) return;
        _pageDragId = pageId;
    }

    private void OnPageTabDragEnter(string pageId)
    {
        if (IsReadOnly || _pageDragId is null || _pageDragId == pageId) return;
        _pageDragOverId = pageId;
        StateHasChanged();
    }

    private void OnPageTabDragLeave()
    {
        _pageDragOverId = null;
        StateHasChanged();
    }

    private void OnPageTabDrop(string targetPageId)
    {
        if (IsReadOnly || _pageDragId is null || _pageDragId == targetPageId) return;

        var sourceIndex = _document?.Pages.FindIndex(p => p.Id == _pageDragId) ?? -1;
        var targetIndex = _document?.Pages.FindIndex(p => p.Id == targetPageId) ?? -1;

        if (_document is not null && sourceIndex >= 0 && targetIndex >= 0)
        {
            var page = _document.Pages[sourceIndex];
            _document.Pages.RemoveAt(sourceIndex);
            _document.Pages.Insert(targetIndex, page);
            _document.ActivePageIndex = targetIndex;
        }

        _pageDragId = null;
        _pageDragOverId = null;
        _ = RerenderCanvas();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RerenderCanvas()
    {
        _canvas?.NotifyContentMutated();
        StateHasChanged();
        if (_document is not null)
            await DocumentChanged.InvokeAsync(_document);
    }

    #region Keyboard shortcuts handlers

    private async Task HandleQuickInsert(string stencilId)
    {
        if (_document is null || IsReadOnly) return;
        var stencil = StencilRegistry.GetStencil(stencilId);
        var w = stencil?.DefaultWidth ?? 120;
        var h = stencil?.DefaultHeight ?? 60;
        // Place at the visible viewport centre rather than the full-page centre.
        // After F3 the canvas auto-fits content on init, which shrinks the
        // rendered viewport away from the whole page; placing at page centre
        // would drop the node far outside the visible rect and the viewport
        // culling in `TmDiagramCanvas.IsNodeVisible` would hide it entirely.
        // Falling back to page centre when no canvas/viewport info is known.
        var page = _document.Pages[_document.ActivePageIndex];
        double cx = page.Width / 2, cy = page.Height / 2;
        if (_canvas is not null)
        {
            var vp = _canvas.GetViewport();
            if (vp.W > 0 && vp.H > 0)
            {
                cx = vp.X + vp.W / 2;
                cy = vp.Y + vp.H / 2;
            }
        }
        // Mobile tap-insert with the panels sheet open (UX cleanup): the sheet's panel
        // covers the canvas' lower band, so a viewport-centre insert can land partly
        // under it. Target the centre of the band that stays VISIBLE above the sheet
        // panel instead — JS measures it and converts it to doc coordinates. Desktop
        // and tablet keep the plain viewport centre; the fallback below also covers
        // a JS that cannot measure (tests, no overlap).
        if (IsMobileSheetOpen)
        {
            var visibleCentre = await TryGetVisibleBandCentreAsync();
            if (visibleCentre is { } band)
            {
                cx = band.X;
                cy = band.Y;
            }
        }
        var x = Math.Round((cx - w / 2) / GridSize) * GridSize;
        var y = Math.Round((cy - h / 2) / GridSize) * GridSize;
        await OnToolboxDrop((stencilId, x, y));
    }

    private async Task HandleNavigateToCorner(string corner)
    {
        if (_document is null || _canvas is null) return;
        var page = _document.Pages[_document.ActivePageIndex];
        var (cx, cy) = corner switch
        {
            "top-left" => (0.0, 0.0),
            "bottom-right" => (page.Width, page.Height),
            _ => (page.Width / 2, page.Height / 2)
        };
        await _canvas.ScrollTo(cx, cy);
    }

    private async Task HandleSwitchPage(int delta)
    {
        if (_document is null || _document.Pages.Count <= 1) return;
        var newIndex = (_document.ActivePageIndex + delta) % _document.Pages.Count;
        if (newIndex < 0) newIndex += _document.Pages.Count;
        SelectPage(_document.Pages[newIndex].Id);
        await Task.CompletedTask;
    }

    private async Task HandleGroupSelected()
    {
        if (_document is null || IsReadOnly) return;
        if (!CanContextMenuGroup()) return;
        var ids = _selectedIds.Where(id => _document.Nodes.FirstOrDefault(n => n.Id == id)?.IsLocked != true).ToArray();
        if (ids.Length < 2) return;
        ActiveCommandStack.Push(new GroupNodesCommand(_document, ids));
        _selectedIds = [];
        if (_canvas is not null) await _canvas.SetSelection([]);
        await OnDocumentChanged(_document);
    }

    private async Task HandleLockSelected()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        var anyLocked = _selectedIds.Any(id => _document.Nodes.FirstOrDefault(n => n.Id == id)?.IsLocked == true);
        if (anyLocked)
        {
            ActiveCommandStack.Push(new UnlockNodesCommand(_document, _selectedIds));
        }
        else
        {
            ActiveCommandStack.Push(new LockNodesCommand(_document, _selectedIds));
        }
        await OnDocumentChanged(_document);
    }

    private async Task HandleToggleBold()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        await ToggleStyleFlag(s => s.IsBold == true, s => s.IsBold = true, s => s.IsBold = false);
    }

    private async Task HandleToggleItalic()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        await ToggleStyleFlag(s => s.IsItalic == true, s => s.IsItalic = true, s => s.IsItalic = false);
    }

    private async Task HandleToggleUnderline()
    {
        if (_document is null || IsReadOnly || _selectedIds.Length == 0) return;
        await ToggleStyleFlag(s => s.IsUnderline == true, s => s.IsUnderline = true, s => s.IsUnderline = false);
    }

    private async Task HandleNodeLinkClicked((string NodeId, string Link) args)
    {
        var link = args.Link;
        if (string.IsNullOrEmpty(link)) return;

        if (link.StartsWith("page://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = link[7..];
            var parts = rest.Split('/', 2);
            var pageId = parts[0];
            var targetNodeId = parts.Length > 1 ? parts[1] : null;

            if (_document?.Pages.FirstOrDefault(p => p.Id == pageId) is { } page)
            {
                SelectPage(pageId);
                if (!string.IsNullOrEmpty(targetNodeId) && _canvas is not null)
                {
                    _ = _canvas.FocusOnNode(targetNodeId);
                    _selectedIds = [targetNodeId];
                    await _canvas.SetSelection(_selectedIds);
                }
            }
        }
        else
        {
            NavigationManager.NavigateTo(link, forceLoad: true);
        }
    }

    private async Task HandleEnterGroup(string groupId)
    {
        if (_document is null) return;
        var container = _document.Nodes.FirstOrDefault(n => n.Id == groupId && n.StencilId == "general.group");
        if (container is null) return;
        _groupStack.Push(groupId);
        _selectedIds = [];
        if (_canvas is not null) _ = _canvas.SetSelection(_selectedIds);
        _ = _canvas?.ZoomToRect(container.X, container.Y, container.W, container.H, 40);
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleExitGroup()
    {
        if (_groupStack.Count == 0 || _document is null) return;
        var groupId = _groupStack.Pop();
        _selectedIds = [groupId];
        if (_canvas is not null) _ = _canvas.SetSelection(_selectedIds);
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleStyleFlag(Func<DiagramStyle, bool> isActive, Action<DiagramStyle> apply, Action<DiagramStyle> remove)
    {
        if (_document is null) return;
        var nodes = _selectedIds
            .Select(id => _document.Nodes.FirstOrDefault(n => n.Id == id))
            .Where(n => n is not null && !n.IsLocked)
            .ToList();
        if (nodes.Count == 0) return;
        var allActive = nodes.All(n => isActive(n!.Style));
        var beforeStyles = nodes.Select(n => new DiagramStyle
        {
            Fill = n!.Style.Fill,
            Stroke = n.Style.Stroke,
            StrokeWidth = n.Style.StrokeWidth,
            StrokeDasharray = n.Style.StrokeDasharray,
            StrokeDashPattern = n.Style.StrokeDashPattern,
            Color = n.Style.Color,
            FontFamily = n.Style.FontFamily,
            FontSize = n.Style.FontSize,
            Opacity = n.Style.Opacity,
            Radius = n.Style.Radius,
            TextAlign = n.Style.TextAlign,
            VerticalAlign = n.Style.VerticalAlign,
            IsBold = n.Style.IsBold,
            IsItalic = n.Style.IsItalic,
            IsUnderline = n.Style.IsUnderline,
            HasShadow = n.Style.HasShadow
        }).ToList();
        var afterStyle = new DiagramStyle();
        foreach (var n in nodes)
        {
            if (allActive) remove(n!.Style); else apply(n.Style);
        }
        afterStyle.IsBold = nodes[0]!.Style.IsBold;
        afterStyle.IsItalic = nodes[0]!.Style.IsItalic;
        afterStyle.IsUnderline = nodes[0]!.Style.IsUnderline;
        // Copy other properties from first node so UpdateNodesStyleCommand has complete after style
        afterStyle.Fill = nodes[0]!.Style.Fill;
        afterStyle.Stroke = nodes[0]!.Style.Stroke;
        afterStyle.StrokeWidth = nodes[0]!.Style.StrokeWidth;
        afterStyle.StrokeDasharray = nodes[0]!.Style.StrokeDasharray;
        afterStyle.StrokeDashPattern = nodes[0]!.Style.StrokeDashPattern;
        afterStyle.Color = nodes[0]!.Style.Color;
        afterStyle.FontFamily = nodes[0]!.Style.FontFamily;
        afterStyle.FontSize = nodes[0]!.Style.FontSize;
        afterStyle.Opacity = nodes[0]!.Style.Opacity;
        afterStyle.Radius = nodes[0]!.Style.Radius;
        afterStyle.TextAlign = nodes[0]!.Style.TextAlign;
        afterStyle.VerticalAlign = nodes[0]!.Style.VerticalAlign;
        afterStyle.HasShadow = nodes[0]!.Style.HasShadow;

        ActiveCommandStack.Push(new UpdateNodesStyleCommand(_document, _selectedIds, beforeStyles, afterStyle));
        await OnDocumentChanged(_document);
    }

    #endregion

    private static DiagramDocument CreateEmptyDocument()
    {
        var doc = new DiagramDocument { Title = "Untitled diagram" };
        doc.Pages.Add(new DiagramPage { Name = "Page 1", Width = 3000, Height = 2000 });
        doc.ActivePageIndex = 0;
        return doc;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "diagram" : name;
    }

    private async Task DownloadFile(string fileName, string mimeType, byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        await JS.InvokeVoidAsync(
            "tmWireframeDesigner.downloadFile",
            _downloadAnchor, fileName, mimeType, base64);
    }
}
