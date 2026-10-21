using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Net.Http.Json;
using Tempo.Blazor.Abstractions.Wireframe.Export;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Top-level wireframe editor component. Composes the canvas, toolbox, properties panel,
/// minimap, and toolbar into a fully working editor.
///
/// <para>Creates and owns a <see cref="WireframeCommandStack"/> and exposes it to all child
/// components via <c>CascadingValue</c>. Each editor instance has an isolated undo history,
/// so multiple editors on the same page work independently.</para>
///
/// <para>Supports JSON import/export, SVG export, undo/redo, zoom, and a collapsible
/// toolbox / properties panel. All write operations are guarded by <see cref="ReadOnly"/>.</para>
///
/// <para>Multi-page support: each page has its own canvas dimensions, elements, connectors,
/// and isolated undo/redo stack. Pages can be added, duplicated, renamed, reordered (drag &amp; drop),
/// and deleted (with confirmation when the page has elements).</para>
/// </summary>
public partial class TmWireframeEditor : ComponentBase, IDisposable
{
    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private WireframeComponentRegistry _registry { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>Document being edited. Bind with <c>@bind-Document</c>.</summary>
    [Parameter] public WireframeDocument? Document { get; set; }

    /// <summary>Raised after every mutation.</summary>
    [Parameter] public EventCallback<WireframeDocument> DocumentChanged { get; set; }

    /// <summary>
    /// Host-pinned read-only mode: prevents all editing interactions. The component NEVER assigns
    /// this parameter — the built-in Read-only/Edit toggle drives an internal user state instead
    /// (surfaced as <see cref="IsReadOnly"/>) and reports changes through <see cref="ReadOnlyChanged"/>.
    /// While <c>ReadOnly="true"</c> is pinned and <see cref="ReadOnlyChanged"/> is not bound, the toggle
    /// is not rendered, so the user cannot unlock the editor. Use <c>@bind-ReadOnly</c> when the host
    /// wants to follow (and own) the state.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Raised with the new effective read-only state when the user flips the built-in Read-only/Edit
    /// toggle. Binding it (<c>@bind-ReadOnly</c>) keeps the toggle available while the host value is
    /// <c>true</c>, because the host decides whether to accept the change.
    /// </summary>
    [Parameter] public EventCallback<bool> ReadOnlyChanged { get; set; }

    /// <summary>
    /// Effective read-only state: the host-pinned <see cref="ReadOnly"/> parameter OR the user's
    /// local Read-only toggle. Every editing path (toolbar, keyboard, context menu, import, toolbox
    /// insertion, canvas interop) is gated on this state.
    /// </summary>
    public bool IsReadOnly => ReadOnly || _userReadOnly;

    /// <summary>Show the component toolbox panel on the left.</summary>
    [Parameter] public bool ShowToolbox { get; set; } = true;

    /// <summary>Show the properties panel on the right.</summary>
    [Parameter] public bool ShowPropertiesPanel { get; set; } = true;

    /// <summary>Show the minimap overlay in the bottom-right corner of the canvas.</summary>
    [Parameter] public bool ShowMinimap { get; set; } = true;

    /// <summary>Show the top toolbar (title, zoom, undo/redo, export/import).</summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>Show grid lines on the canvas.</summary>
    [Parameter] public bool ShowGrid { get; set; } = true;

    /// <summary>Snap-to-grid cell size. 0 = disabled.</summary>
    [Parameter] public int GridSize { get; set; } = 8;

    /// <summary>Additional CSS class on the editor root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Always show page tabs even when there is only one page.</summary>
    [Parameter] public bool ShowPageTabs { get; set; } = true;

    /// <summary>
    /// Initial / host-driven visibility of the rulers around the canvas. The component NEVER assigns
    /// this parameter — the toolbar toggle flips an internal state and reports it through
    /// <see cref="ShowRulersChanged"/>; a later change of the parameter value is adopted again.
    /// </summary>
    [Parameter] public bool ShowRulers { get; set; }

    /// <summary>Raised with the new value when the user toggles the rulers button (use <c>@bind-ShowRulers</c>).</summary>
    [Parameter] public EventCallback<bool> ShowRulersChanged { get; set; }

    /// <summary>Optional application scope used to resolve custom wireframe components.</summary>
    [Parameter] public WireframeComponentScope? ComponentScope { get; set; }

    // ── Child component refs ──────────────────────────────────────────────────

    private TmWireframeDesignerCanvas? _canvas;
    private TmWireframeMinimap?        _minimap;
    private ElementReference           _downloadAnchor;

    // ── Command stacks (one per page) ─────────────────────────────────────────

    private readonly Dictionary<string, WireframeCommandStack> _pageCommandStacks = new();
    private WireframeCommandStack _commandStack = new();

    // ── Derived / UI state ────────────────────────────────────────────────────

    // Internal copy of Document: keeps a stable reference for child components.
    private WireframeDocument? _document;
    private WireframePage?     _activePage;

    // User-toggled read-only / rulers state; deliberately separate from the [Parameter]s, which are never assigned.
    private bool _userReadOnly;
    private bool _showRulers;
    private bool? _lastHostReadOnly;
    private bool? _lastHostShowRulers;

    private string[]         _selectedIds       = [];
    private string[]         _selectedConnectorIds = [];
    private string?          _activeLayerId;
    private MinimapViewport? _minimapViewport;

    private double _zoomLevel = 1.0;
    private string _zoomLabel => $"{(int)Math.Round(_zoomLevel * 100)}%";

    // Canvas viewBox snapshot (updated by JS OnViewBoxChanged)
    private double _viewBoxX;
    private double _viewBoxY;
    private double _viewBoxW = 1200;
    private double _viewBoxH = 800;
    private bool   _viewBoxReported;

    // Ruler cursor positions (SVG space)
    private double? _rulerCursorX;
    private double? _rulerCursorY;

    private bool _toolboxCollapsed;
    private bool _propsCollapsed;
    private bool _exportMenuOpen;
    private bool _exportDialogOpen;
    private string? _exportDialogFormat;
    private bool _alignMenuOpen;
    private bool _distributeMenuOpen;
    private string? _importError;

    // Tool mode: 'select' | 'pan' | 'connector'
    private string _toolMode = "select";

    // Canvas size editing
    private bool   _canvasSizeEditing;
    private string _canvasWInput = "";
    private string _canvasHInput = "";
    private bool   _snapToObjects;
    private bool   _hasStyleOnClipboard => WireframeClipboard.HasStyle;

    // Context menu
    private TmWireframeContextMenu? _contextMenu;
    private bool _ctxMenuOpen;
    private double _ctxMenuX;
    private double _ctxMenuY;
    private WireframeContextMenuType _ctxMenuType;

    // Page tabs
    private string? _renamingPageId;
    private string? _draggedPageId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        _commandStack.OnStackChanged += OnStackChanged;
        _document = Document ?? CreateEmptyDocument();
        _document.EnsureActivePage();
        _activePage = _document.ActivePage;
        _activeLayerId = _document.ActiveLayerId;
        if (_activePage is not null)
            _pageCommandStacks[_activePage.Id] = _commandStack;
    }

    protected override void OnParametersSet()
    {
        // A host-side change of ReadOnly / ShowRulers wins over the local user state (it is the
        // source of truth once bound); an unchanged parameter keeps the user's toggle.
        if (_lastHostReadOnly != ReadOnly)
        {
            _lastHostReadOnly = ReadOnly;
            _userReadOnly = false;
        }

        if (_lastHostShowRulers != ShowRulers)
        {
            _lastHostShowRulers = ShowRulers;
            _showRulers = ShowRulers;
        }

        // If the caller supplies a new Document reference, adopt it and clear stacks.
        if (Document is not null && !ReferenceEquals(Document, _document))
        {
            _document = Document;
            _document.EnsureActivePage();
            _activePage = _document.ActivePage;
            _activeLayerId = _document.ActiveLayerId;

            foreach (var stack in _pageCommandStacks.Values)
                stack.OnStackChanged -= OnStackChanged;
            _pageCommandStacks.Clear();
            _commandStack.Clear();

            _commandStack = new WireframeCommandStack();
            _commandStack.OnStackChanged += OnStackChanged;
            if (_activePage is not null)
                _pageCommandStacks[_activePage.Id] = _commandStack;

            _selectedIds = [];
        }
    }

    public void Dispose()
    {
        foreach (var stack in _pageCommandStacks.Values)
            stack.OnStackChanged -= OnStackChanged;
        _commandStack.OnStackChanged -= OnStackChanged;
    }

    // ── Stack change → re-render ──────────────────────────────────────────────

    private void OnStackChanged() => InvokeAsync(StateHasChanged);

    // ── Document change propagation ───────────────────────────────────────────

    private async Task OnDocumentChanged(WireframeDocument doc)
    {
        _document = doc;
        await DocumentChanged.InvokeAsync(doc);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private void OnSelectionChanged(string[] ids)
    {
        _selectedIds = ids;
        _selectedConnectorIds = []; // clear connector selection
        StateHasChanged();
    }

    private void OnConnectorSelectionChanged(string[] ids)
    {
        _selectedConnectorIds = ids;
        _selectedIds = []; // clear element selection
        StateHasChanged();
    }

    private async Task OnConnectorCreated((string FromId, string ToId) args)
    {
        if (_document is null || IsReadOnly) return;
        var conn = new WireframeConnector
        {
            FromId = args.FromId,
            ToId = args.ToId,
            ZIndex = _document.Connectors.Count > 0 ? _document.Connectors.Max(c => c.ZIndex) + 1 : 0,
        };
        _commandStack.Push(new AddConnectorCommand(_document, conn));
        _selectedConnectorIds = [conn.Id];
        _selectedIds = [];
        await DocumentChanged.InvokeAsync(_document);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnDeleteConnectors(string[] ids)
    {
        if (_document is null || IsReadOnly) return;
        _commandStack.Push(new RemoveConnectorsCommand(_document, ids));
        _selectedConnectorIds = [];
        await DocumentChanged.InvokeAsync(_document);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnConnectorWaypointDragged((string ConnectorId, int WaypointIndex, double X, double Y) args)
    {
        if (_document is null || IsReadOnly) return;
        var conn = _document.Connectors.FirstOrDefault(c => c.Id == args.ConnectorId);
        if (conn is null) return;

        // Update existing waypoint position
        var newWaypoints = conn.Waypoints.ToList();
        if (args.WaypointIndex >= 0 && args.WaypointIndex < newWaypoints.Count)
        {
            newWaypoints[args.WaypointIndex] = new DiagramPoint(args.X, args.Y);
        }

        _commandStack.Push(new UpdateConnectorWaypointsCommand(_document, args.ConnectorId, conn.Waypoints.ToList(), newWaypoints));
        await DocumentChanged.InvokeAsync(_document);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnConnectorWaypointAdded((string ConnectorId, double X, double Y) args)
    {
        if (_document is null || IsReadOnly) return;
        var conn = _document.Connectors.FirstOrDefault(c => c.Id == args.ConnectorId);
        if (conn is null) return;

        var newWaypoints = conn.Waypoints.ToList();
        newWaypoints.Add(new DiagramPoint(args.X, args.Y));

        _commandStack.Push(new UpdateConnectorWaypointsCommand(_document, args.ConnectorId, conn.Waypoints.ToList(), newWaypoints));
        await DocumentChanged.InvokeAsync(_document);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnConnectorDragged((string ConnectorId, double Dx, double Dy) args)
    {
        if (_document is null || IsReadOnly) return;
        var conn = _document.Connectors.FirstOrDefault(c => c.Id == args.ConnectorId);
        if (conn is null) return;

        var newWaypoints = conn.Waypoints
            .Select(w => new DiagramPoint(w.X + args.Dx, w.Y + args.Dy))
            .ToList();

        _commandStack.Push(new UpdateConnectorWaypointsCommand(_document, args.ConnectorId, conn.Waypoints.ToList(), newWaypoints));
        await DocumentChanged.InvokeAsync(_document);
        await InvokeAsync(StateHasChanged);
    }

    private void OnActiveLayerChanged(string? layerId)
    {
        _activeLayerId = layerId;
        if (_document is not null)
            _document.ActiveLayerId = layerId;
        StateHasChanged();
    }

    private void OnCanvasViewBoxChanged((double X, double Y, double W, double H) args)
    {
        _viewBoxReported = true;
        _viewBoxX = args.X;
        _viewBoxY = args.Y;
        _viewBoxW = args.W;
        _viewBoxH = args.H;
        StateHasChanged();
    }

    private void OnCanvasMouseMoved((double SvgX, double SvgY) args)
    {
        _rulerCursorX = args.SvgX;
        _rulerCursorY = args.SvgY;
        StateHasChanged();
    }

    // ── Toolbar: Tool mode (Select / Pan / Connector) ─────────────────────────

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

    // ── Toolbar: Canvas size ──────────────────────────────────────────────────

    private void OpenCanvasSize()
    {
        _canvasWInput = ((int)(_activePage?.Width  ?? 1280)).ToString();
        _canvasHInput = ((int)(_activePage?.Height ?? 800)).ToString();
        _canvasSizeEditing = true;
    }

    private async Task ApplyCanvasSize()
    {
        _canvasSizeEditing = false;
        if (_activePage is null || IsReadOnly) return;
        if (!double.TryParse(_canvasWInput, out var w) || w < 100) return;
        if (!double.TryParse(_canvasHInput, out var h) || h < 100) return;
        if (Math.Abs(w - _activePage.Width) < 0.5 && Math.Abs(h - _activePage.Height) < 0.5) return;

        _commandStack.Push(new Commands.ResizeCanvasCommand(_document!, _activePage.Width, _activePage.Height, w, h));
        if (_canvas is not null) await _canvas.UpdateCanvasSize(w, h);
        await OnDocumentChanged(_document!);
    }

    // ── Toolbar: Title ────────────────────────────────────────────────────────

    private async Task OnTitleChanged(ChangeEventArgs e)
    {
        if (_document is null || IsReadOnly) return;
        _document.Title = e.Value?.ToString() ?? "";
        await OnDocumentChanged(_document);
    }

    // ── Toolbar: Undo / Redo ──────────────────────────────────────────────────

    private async Task OnUndoClicked()
    {
        if (IsReadOnly) return;
        _commandStack.Undo();
        await RerenderCanvas();
    }

    private async Task OnRedoClicked()
    {
        if (IsReadOnly) return;
        _commandStack.Redo();
        await RerenderCanvas();
    }

    // ── Toolbar: Arrange (Bring to Front / Send to Back / Lock) ───────────────

    private async Task OnBringToFront()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        _commandStack.Push(new Commands.BringToFrontCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    private async Task OnSendToBack()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        _commandStack.Push(new Commands.SendToBackCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    private bool IsSelectionLocked()
    {
        if (_document is null || _selectedIds.Length == 0) return false;
        return _selectedIds
            .Select(id => _document.Elements.FirstOrDefault(e => e.Id == id))
            .Where(e => e is not null)
            .All(e => e!.IsLocked || !string.IsNullOrEmpty(e.LockedBy));
    }

    private async Task OnToggleLock()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        var currentlyLocked = IsSelectionLocked();
        if (currentlyLocked)
            _commandStack.Push(new Commands.UnlockElementsCommand(_document, _selectedIds));
        else
            _commandStack.Push(new Commands.LockElementsCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    private bool SelectedIdsContainGroup()
    {
        if (_document is null || _selectedIds.Length == 0) return false;
        return _selectedIds
            .Select(id => _document.Elements.FirstOrDefault(e => e.Id == id))
            .Any(e => e is not null && e.Type == "__group__");
    }

    private async Task OnGroup()
    {
        if (_document is null || _selectedIds.Length < 2 || IsReadOnly) return;
        _commandStack.Push(new Commands.GroupElementsCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    private async Task OnUngroup()
    {
        if (_document is null || IsReadOnly) return;
        var groupIds = _selectedIds
            .Select(id => _document.Elements.FirstOrDefault(e => e.Id == id))
            .Where(e => e is not null && e.Type == "__group__")
            .Select(e => e!.Id)
            .ToList();
        foreach (var gid in groupIds)
            _commandStack.Push(new Commands.UngroupElementsCommand(_document, gid));
        await RerenderCanvas();
    }

    private async Task OnAlign(WireframeAlignment alignment)
    {
        if (_document is null || _selectedIds.Length < 2 || IsReadOnly) return;
        _commandStack.Push(new Commands.AlignElementsCommand(_document, _selectedIds, alignment));
        await RerenderCanvas();
    }

    private async Task OnDistribute(WireframeDistribution distribution)
    {
        if (_document is null || _selectedIds.Length < 3 || IsReadOnly) return;
        _commandStack.Push(new Commands.DistributeElementsCommand(_document, _selectedIds, distribution));
        await RerenderCanvas();
    }

    private async Task ToggleSnapToObjects()
    {
        _snapToObjects = !_snapToObjects;
        if (_canvas is not null)
            await JS.InvokeVoidAsync("tmWireframeDesigner.setSnapToObjects", _canvas.GetSvgRef(), _snapToObjects);
    }

    private async Task ToggleRulers()
    {
        _showRulers = !_showRulers;
        await ShowRulersChanged.InvokeAsync(_showRulers);
    }

    private async Task OnCopyStyle()
    {
        if (_document is null || _selectedIds.Length != 1 || IsReadOnly) return;
        var el = _document.Elements.FirstOrDefault(e => e.Id == _selectedIds[0]);
        if (el is null) return;
        new Commands.CopyStyleCommand(el, includeSize: false).Execute();
        if (_canvas is not null)
            await JS.InvokeVoidAsync("tmWireframeDesigner.setFormatPainterActive", _canvas.GetSvgRef(), true);
        StateHasChanged(); // refresh toolbar active state
    }

    private async Task OnPasteStyle()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        _commandStack.Push(new Commands.PasteStyleCommand(_document, _selectedIds, _registry));
        await RerenderCanvas();
    }

    private async Task OnPasteSize()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        _commandStack.Push(new Commands.PasteSizeCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    // ── Context Menu ──────────────────────────────────────────────────────────

    private async Task OnCanvasContextMenuRequested((double SvgX, double SvgY, double ScreenX, double ScreenY) args)
    {
        _selectedIds = [];
        if (_canvas is not null)
            await _canvas.ClearSelection();
        StateHasChanged();
        await ShowContextMenu(WireframeContextMenuType.Canvas, args.ScreenX, args.ScreenY);
    }

    private async Task OnElementContextMenuRequested((string Id, double ScreenX, double ScreenY) args)
    {
        if (_document is null) return;

        // If right-clicked element is not in current selection, select only that element
        if (!_selectedIds.Contains(args.Id))
        {
            _selectedIds = [args.Id];
            if (_canvas is not null)
                await _canvas.SelectElements(args.Id);
            StateHasChanged();
        }

        var menuType = _selectedIds.Length > 1
            ? WireframeContextMenuType.MultiSelect
            : WireframeContextMenuType.Element;
        await ShowContextMenu(menuType, args.ScreenX, args.ScreenY);
    }

    private async Task OnConnectorContextMenuRequested((string Id, double ScreenX, double ScreenY) args)
    {
        if (_document is null) return;

        // Select the connector that was right-clicked
        _selectedConnectorIds = [args.Id];
        _selectedIds = [];
        StateHasChanged();

        await ShowContextMenu(WireframeContextMenuType.Connector, args.ScreenX, args.ScreenY);
    }

    private async Task ShowContextMenu(WireframeContextMenuType type, double screenX, double screenY)
    {
        _ctxMenuType = type;
        _ctxMenuX = screenX;
        _ctxMenuY = screenY;
        _ctxMenuOpen = true;
        StateHasChanged();

        if (_canvas is not null)
        {
            await JS.InvokeVoidAsync("tmWireframeDesigner.openContextMenu", _canvas.GetSvgRef(), _contextMenu?.GetMenuRef());
        }
    }

    private async Task CloseContextMenu()
    {
        if (!_ctxMenuOpen) return;
        _ctxMenuOpen = false;
        if (_canvas is not null)
        {
            await JS.InvokeVoidAsync("tmWireframeDesigner.closeContextMenu", _canvas.GetSvgRef());
        }
        StateHasChanged();
    }

    private async Task OnSelectAll()
    {
        if (_document is null) return;
        var allIds = _document.Elements.Select(e => e.Id).ToArray();
        _selectedIds = allIds;
        if (_canvas is not null)
            await _canvas.SelectElements(allIds);
        StateHasChanged();
    }

    private async Task OnDuplicateFromContextMenu()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        var ids = _selectedIds.ToList();
        var newIds = new List<string>();
        foreach (var id in ids)
        {
            var el = _document.Elements.FirstOrDefault(e => e.Id == id);
            if (el is null) continue;
            var copy = el.DeepCopy();
            copy.X += 20;
            copy.Y += 20;
            copy.ZIndex = _document.Elements.Count > 0
                ? _document.Elements.Max(e => e.ZIndex) + 1 : 0;
            _document.Elements.Add(copy);
            newIds.Add(copy.Id);
        }
        if (newIds.Count > 0)
        {
            _selectedIds = newIds.ToArray();
            if (_canvas is not null)
                await _canvas.SelectElements(newIds.ToArray());
        }
        await RerenderCanvas();
    }

    private async Task OnDeleteFromContextMenu()
    {
        if (_document is null || IsReadOnly) return;
        if (_selectedIds.Length > 0)
        {
            var ids = _selectedIds.ToArray();
            _commandStack.Push(new Commands.RemoveElementsCommand(_document, ids));
            _selectedIds = [];
            if (_canvas is not null)
                await _canvas.ClearSelection();
        }
        else if (_selectedConnectorIds.Length > 0)
        {
            var ids = _selectedConnectorIds.ToArray();
            _commandStack.Push(new Commands.RemoveConnectorsCommand(_document, ids));
            _selectedConnectorIds = [];
        }
        await RerenderCanvas();
    }

    private async Task OnLockFromContextMenu()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        _commandStack.Push(new Commands.LockElementsCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    private async Task OnUnlockFromContextMenu()
    {
        if (_document is null || _selectedIds.Length == 0 || IsReadOnly) return;
        _commandStack.Push(new Commands.UnlockElementsCommand(_document, _selectedIds));
        await RerenderCanvas();
    }

    private async Task ContextMenuToggleGrid()
    {
        ShowGrid = !ShowGrid;
        StateHasChanged();
    }

    private async Task ContextMenuToggleSnapToObjects()
    {
        await ToggleSnapToObjects();
    }

    private async Task ContextMenuFitToView()
    {
        await FitToView();
    }

    private async Task OnEditConnectorLabelFromContextMenu()
    {
        if (_document is null || _selectedConnectorIds.Length == 0 || IsReadOnly) return;
        var connId = _selectedConnectorIds[0];
        var conn = _document.Connectors.FirstOrDefault(c => c.Id == connId);
        if (conn is null) return;
        // Toggle: if label exists, clear it; otherwise set a default
        var newLabel = string.IsNullOrEmpty(conn.Label) ? "Label" : null;
        _commandStack.Push(new UpdateConnectorLabelCommand(_document, connId, conn.Label, newLabel));
        await RerenderCanvas();
    }

    private async Task OnSetConnectorRoutingFromContextMenu(string routing)
    {
        if (_document is null || _selectedConnectorIds.Length == 0 || IsReadOnly) return;
        var connId = _selectedConnectorIds[0];
        var conn = _document.Connectors.FirstOrDefault(c => c.Id == connId);
        if (conn is null) return;
        _commandStack.Push(new UpdateConnectorRoutingCommand(_document, connId, conn.Routing, conn.Waypoints.ToList(), routing, conn.Waypoints.ToList()));
        await RerenderCanvas();
    }

    // ── Toolbar: Zoom ─────────────────────────────────────────────────────────

    private static readonly double[] ZoomSteps = [0.25, 0.33, 0.5, 0.67, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0];

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
        _zoomLevel = level;
        if (_canvas is not null)
            await _canvas.SetZoom(level);
    }

    private async Task FitToView()
    {
        if (_canvas is not null)
            _zoomLevel = await _canvas.FitToView();
    }

    // ── Toolbar: Export ───────────────────────────────────────────────────────

    private void ToggleExportMenu() => _exportMenuOpen = !_exportMenuOpen;
    private void CloseExportMenu()  => _exportMenuOpen = false;

    private void ToggleAlignMenu() => _alignMenuOpen = !_alignMenuOpen;
    private void CloseAlignMenu()  => _alignMenuOpen = false;

    private void ToggleDistributeMenu() => _distributeMenuOpen = !_distributeMenuOpen;
    private void CloseDistributeMenu()  => _distributeMenuOpen = false;

    private async Task ExportJson()
    {
        _exportMenuOpen = false;
        if (_document is null) return;
        var json     = WireframeSerializer.Serialize(_document);
        var fileName = SanitizeFileName(_document.Title) + ".wireframe.json";
        await DownloadFile(fileName, "application/json", System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task ExportSvg()
    {
        _exportMenuOpen = false;
        if (_canvas is null) return;
        var svg      = await _canvas.ExportSvg();
        var fileName = SanitizeFileName(_document?.Title ?? Loc["TmWireframe_ExportFilename"]) + ".svg";
        await DownloadFile(fileName, "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>Returns the current canvas state as an SVG string for use by embedded callers.</summary>
    public async Task<string> ExportSvgAsync()
    {
        if (_canvas is null) return string.Empty;
        return await _canvas.ExportSvg();
    }

    private void ExportPng()
    {
        _exportMenuOpen = false;
        _exportDialogFormat = "png";
        _exportDialogOpen = true;
    }

    private void ExportPdf()
    {
        _exportMenuOpen = false;
        _exportDialogFormat = "pdf";
        _exportDialogOpen = true;
    }

    private void CloseExportDialog()
    {
        _exportDialogOpen = false;
        _exportDialogFormat = null;
    }

    private async Task OnExportDialogExport(WireframeExportDialogResult result)
    {
        _exportDialogOpen = false;
        if (_canvas is null) return;

        var svg = await _canvas.ExportSvg();
        if (string.IsNullOrWhiteSpace(svg)) return;

        var request = new WireframeExportRequest
        {
            Svg = svg,
            FileName = result.FileName,
            Options = result.Options
        };

        var endpoint = result.Format == "pdf"
            ? "api/wireframe/export/pdf"
            : "api/wireframe/export/png";

        try
        {
            var response = await Http.PostAsJsonAsync(endpoint, request);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = result.Format == "pdf" ? "application/pdf" : "image/png";
            var ext = result.Format == "pdf" ? ".pdf" : ".png";
            var fileName = SanitizeFileName(result.FileName) + ext;

            await DownloadFile(fileName, contentType, bytes);
        }
        catch (Exception ex)
        {
            _importError = string.Format(Loc["TmWireframe_ExportError"], ex.Message);
        }
    }

    // ── Toolbar: Import ───────────────────────────────────────────────────────

    private async Task OnImportFile(InputFileChangeEventArgs e)
    {
        _importError = null;
        var file = e.File;
        if (file is null) return;

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            using var reader = new System.IO.StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            if (!WireframeSerializer.TryDeserialize(json, out var doc) || doc is null)
            {
                _importError = Loc["TmWireframe_ImportError_InvalidJson"];
                return;
            }

            _document = doc;
            _document.EnsureActivePage();
            _activePage = _document.ActivePage;

            foreach (var stack in _pageCommandStacks.Values)
                stack.OnStackChanged -= OnStackChanged;
            _pageCommandStacks.Clear();
            _commandStack.Clear();
            _commandStack = new WireframeCommandStack();
            _commandStack.OnStackChanged += OnStackChanged;
            if (_activePage is not null)
                _pageCommandStacks[_activePage.Id] = _commandStack;

            _selectedIds = [];
            await DocumentChanged.InvokeAsync(_document);
            await RerenderCanvas();
        }
        catch (Exception ex)
        {
            _importError = Loc["TmWireframe_ImportError_Generic", ex.Message];
        }
    }

    // ── Toolbar: Read-only toggle ─────────────────────────────────────────────

    private async Task ToggleReadOnly()
    {
        var next = !IsReadOnly;
        _userReadOnly = next;
        await ReadOnlyChanged.InvokeAsync(next);
    }

    /// <summary>The Read-only/Edit toggle is hidden while the host pins <c>ReadOnly="true"</c> without binding <see cref="ReadOnlyChanged"/>.</summary>
    private bool ShowReadOnlyToggle => !ReadOnly || ReadOnlyChanged.HasDelegate;

    // ── Toolbox keyboard activation ───────────────────────────────────────────

    private async Task OnToolboxActivated(string componentType)
    {
        if (_document is null || _canvas is null || IsReadOnly) return;

        // Non-drag alternative to HTML5 drag-and-drop (WCAG 2.5.7): click, tap and Enter/Space on a
        // toolbox item insert the element centred in the currently visible area of the canvas.
        var def = _registry.GetDef(componentType, ComponentScope);
        var w   = def?.DefaultWidth  ?? 160;
        var h   = def?.DefaultHeight ?? 40;
        var (x, y) = GetVisibleCentreOrigin(w, h);

        var el = WireframeDocumentExtensions.NewElement(
            componentType, x, y,
            w, h,
            def?.Props ?? []);
        el.ZIndex = _document.Elements.Count > 0
            ? _document.Elements.Max(e => e.ZIndex) + 1 : 0;
        el.LayerId = _document.ActiveLayerId;

        await _canvas.AddElement(el);
        await _canvas.SelectElements(el.Id);
        OnSelectionChanged([el.Id]);
    }

    /// <summary>
    /// Top-left origin that centres an element of the given size on the visible canvas area
    /// (the viewBox last reported by the JS canvas, or the document extent before the first report),
    /// snapped to the grid when snapping is on and clamped to non-negative coordinates.
    /// </summary>
    private (double X, double Y) GetVisibleCentreOrigin(double w, double h)
    {
        double vx = _viewBoxX, vy = _viewBoxY, vw = _viewBoxW, vh = _viewBoxH;
        if (!_viewBoxReported && _activePage is { } page)
        {
            vx = 0;
            vy = 0;
            vw = page.Width;
            vh = page.Height;
        }

        var x = vx + vw / 2 - w / 2;
        var y = vy + vh / 2 - h / 2;

        if (GridSize > 0)
        {
            x = Math.Round(x / GridSize) * GridSize;
            y = Math.Round(y / GridSize) * GridSize;
        }

        return (Math.Max(0, x), Math.Max(0, y));
    }

    // ── Minimap navigation ────────────────────────────────────────────────────

    private async Task OnMinimapNavigate(MinimapNavigateArgs args)
    {
        if (_canvas is null) return;
        await JS.InvokeVoidAsync(
            "tmWireframeDesigner.scrollTo",
            _canvas.GetSvgRef(),
            args.CentreX, args.CentreY);
    }

    // ── Page management ───────────────────────────────────────────────────────

    private async Task AddPage()
    {
        if (_document is null || IsReadOnly) return;

        var page = new WireframePage
        {
            Name = $"{Loc["TmWireframe_Page"]} {_document.Pages.Count + 1}",
            Width = _activePage?.Width ?? 1280,
            Height = _activePage?.Height ?? 800,
        };

        _document.Pages.Add(page);
        await SwitchPage(page.Id);
        await OnDocumentChanged(_document);
    }

    private async Task DuplicatePage()
    {
        if (_document is null || _activePage is null || IsReadOnly) return;

        var json = System.Text.Json.JsonSerializer.Serialize(_activePage, WireframeJsonOptions.Default);
        var copy = System.Text.Json.JsonSerializer.Deserialize<WireframePage>(json, WireframeJsonOptions.Default)!;
        copy.Id = "p" + Guid.NewGuid().ToString("N")[..7];
        copy.Name = $"{_activePage.Name} ({Loc["TmWireframe_Copy"]})";

        _document.Pages.Add(copy);
        await SwitchPage(copy.Id);
        await OnDocumentChanged(_document);
    }

    private async Task DeletePage(string pageId)
    {
        if (_document is null || IsReadOnly) return;
        if (_document.Pages.Count <= 1) return;

        var page = _document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;

        _document.Pages.Remove(page);
        if (_pageCommandStacks.Remove(pageId, out var stack))
            stack.OnStackChanged -= OnStackChanged;

        // Switch to nearest page
        var newActive = _document.Pages.FirstOrDefault();
        if (newActive is not null)
            await SwitchPage(newActive.Id);

        await OnDocumentChanged(_document);
    }

    private async Task SwitchPage(string pageId)
    {
        if (_document is null || _activePage?.Id == pageId) return;

        // Clear selection and context menu before switching
        _selectedIds = [];
        _ctxMenuOpen = false;
        if (_canvas is not null)
            await _canvas.ClearSelection();

        _document.ActivePageId = pageId;
        _activePage = _document.ActivePage;
        _activeLayerId = _document.ActiveLayerId;

        // Get or create command stack for the new page
        if (_activePage is not null)
        {
            if (!_pageCommandStacks.TryGetValue(_activePage.Id, out var stack))
            {
                stack = new WireframeCommandStack();
                stack.OnStackChanged += OnStackChanged;
                _pageCommandStacks[_activePage.Id] = stack;
            }
            _commandStack = stack;
        }

        StateHasChanged();
        await OnDocumentChanged(_document);
    }

    private void StartRenamePage(string pageId)
    {
        if (IsReadOnly) return;
        _renamingPageId = pageId;
        StateHasChanged();
    }

    private async Task OnPageRenamed(string pageId, ChangeEventArgs e)
    {
        if (_document is null || IsReadOnly) return;
        var page = _document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;
        page.Name = e.Value?.ToString() ?? page.Name;
        _renamingPageId = null;
        await OnDocumentChanged(_document);
    }

    private void OnPageDragStart(string pageId)
    {
        if (IsReadOnly) return;
        _draggedPageId = pageId;
    }

    private void OnPageDrop(string targetPageId)
    {
        if (_document is null || IsReadOnly || _draggedPageId is null || _draggedPageId == targetPageId) return;

        var draggedIdx = _document.Pages.FindIndex(p => p.Id == _draggedPageId);
        var targetIdx = _document.Pages.FindIndex(p => p.Id == targetPageId);
        if (draggedIdx < 0 || targetIdx < 0) return;

        var page = _document.Pages[draggedIdx];
        _document.Pages.RemoveAt(draggedIdx);
        _document.Pages.Insert(targetIdx, page);

        _draggedPageId = null;
        _ = OnDocumentChanged(_document);
    }

    private void OnRenameInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            _renamingPageId = null;
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (_document is null) return;

        if (e.CtrlKey && e.Key == "PageUp")
        {
            var idx = _document.Pages.FindIndex(p => p.Id == _activePage?.Id);
            if (idx > 0)
                await SwitchPage(_document.Pages[idx - 1].Id);
        }
        else if (e.CtrlKey && e.Key == "PageDown")
        {
            var idx = _document.Pages.FindIndex(p => p.Id == _activePage?.Id);
            if (idx >= 0 && idx < _document.Pages.Count - 1)
                await SwitchPage(_document.Pages[idx + 1].Id);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RerenderCanvas()
    {
        StateHasChanged();
        if (_document is not null)
            await DocumentChanged.InvokeAsync(_document);
    }

    private WireframeDocument CreateEmptyDocument()
    {
        var doc = new WireframeDocument { Title = Loc["TmWireframe_DefaultTitle"] };
        doc.EnsureActivePage();
        return doc;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "wireframe" : name;
    }

    private async Task DownloadFile(string fileName, string mimeType, byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        await JS.InvokeVoidAsync(
            "tmWireframeDesigner.downloadFile",
            _downloadAnchor, fileName, mimeType, base64);
    }
}
