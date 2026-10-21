using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Net.Http.Json;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Wireframe.Export;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Top-level wireframe editor component. Renders through the core <c>TmEditorShell</c> (toolbar row, left panel
/// with Components / Pages / Layers tabs, canvas with rulers and minimap, inspector) and composes the canvas,
/// toolbox, pages, layers, properties panel, minimap and a core <c>TmToolbar</c> into a fully working editor.
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

    /// <summary>
    /// Show the component toolbox: the Components tab of the left panel. When no tab is left (no toolbox, no Layers, no Pages)
    /// the left panel is not rendered. Collapsing the panel is shell state: the shell's strip toggle or the Components
    /// panel entry in the toolbar's "More" menu.
    /// </summary>
    [Parameter] public bool ShowToolbox { get; set; } = true;

    /// <summary>
    /// Show the right-hand inspector and the Layers tab of the left panel (the two lived together in the right column
    /// before the shell restructure). Collapsing the inspector is shell state: the shell's strip toggle or the
    /// Properties panel entry in the toolbar's "More" menu.
    /// </summary>
    [Parameter] public bool ShowPropertiesPanel { get; set; } = true;

    /// <summary>Show the minimap overlay in the bottom-right corner of the canvas.</summary>
    [Parameter] public bool ShowMinimap { get; set; } = true;

    /// <summary>Show the top toolbar (title, zoom, undo/redo, export/import).</summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>
    /// Initial / host-driven visibility of the grid lines on the canvas. The component NEVER assigns this parameter —
    /// the inspector's Grid section flips an internal state and reports it through <see cref="ShowGridChanged"/>.
    /// </summary>
    [Parameter] public bool ShowGrid { get; set; } = true;

    /// <summary>Raised with the new value when the user toggles the grid in the inspector (use <c>@bind-ShowGrid</c>).</summary>
    [Parameter] public EventCallback<bool> ShowGridChanged { get; set; }

    /// <summary>
    /// Initial / host-driven snap-to-grid cell size. 0 = snapping off. The component NEVER assigns this parameter — the
    /// inspector's Grid section changes an internal value and reports it through <see cref="GridSizeChanged"/>.
    /// </summary>
    [Parameter] public int GridSize { get; set; } = 8;

    /// <summary>Raised with the new cell size when the user edits the grid size in the inspector (use <c>@bind-GridSize</c>).</summary>
    [Parameter] public EventCallback<int> GridSizeChanged { get; set; }

    /// <summary>Additional CSS class on the editor root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Show the page navigation (the horizontal page tabs, or the Pages tab of the left panel with
    /// <see cref="PageNavigation"/> = <see cref="WireframePageNavigation.Panel"/>), even when there is only one page.
    /// </summary>
    [Parameter] public bool ShowPageTabs { get; set; } = true;

    /// <summary>
    /// Where the pages of the document are presented. <see cref="WireframePageNavigation.Tabs"/> (default) keeps the
    /// horizontal page tabs above the canvas (the Components and Layers tabs in the left panel);
    /// <see cref="WireframePageNavigation.Panel"/> adds a Pages tab (<see cref="TmWireframePagesPanel"/>) to the left
    /// panel and drops the horizontal tabs.
    /// </summary>
    [Parameter] public WireframePageNavigation PageNavigation { get; set; } = WireframePageNavigation.Tabs;

    /// <summary>
    /// How the editor resolves its layout: <see cref="TmLayoutMode.Auto"/> (default) measures the editor's own container
    /// against <see cref="LayoutBreakpoints"/>; a forced mode pins the shell's presentation (used by tests and host layouts).
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// Per-instance layout thresholds for the editor container. <c>null</c> uses the editor default: mobile below 640,
    /// desktop from 860 (the docked left panel, the canvas and the inspector need about that much room).
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? LayoutBreakpoints { get; set; }

    /// <summary>
    /// Show the shell header row. <c>false</c> (default) keeps the pre-shell look: the document title sits at the start of
    /// the toolbar row and no header renders — unless a host supplies <see cref="HeaderStart"/>, <see cref="HeaderActions"/>
    /// or <see cref="Title"/>, which opens the header on its own. With a header the built-in title moves into it.
    /// </summary>
    [Parameter] public bool ShowHeader { get; set; }

    /// <summary>Host content rendered at the start of the header, before the title (G9 host slot). There is no built-in Save or Open: hosts supply their own actions here.</summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>Host content rendered at the end of the header, after the title (G9 host slot).</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>Host content that replaces the built-in editable document title (G9 host slot). Opens the header like <see cref="HeaderStart"/>.</summary>
    [Parameter] public RenderFragment? Title { get; set; }

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

    // Shell state: the user's collapse of the left / right panel, the tablet "one expanded" rails and the resolved layout.
    private static readonly TmLayoutBreakpoints EditorBreakpoints = new(640, 860);
    private bool _leftOpen = true;
    private bool _rightOpen = true;
    private EditorShellPanel _collapsedPanels;
    private TmLayoutMode _resolvedLayout = TmLayoutMode.Desktop;

    // Grid state: seeded from / re-adopted from the [Parameter]s, which are never assigned.
    private bool _showGrid = true;
    private int _gridSize = 8;
    private bool? _lastHostShowGrid;
    private int? _lastHostGridSize;

    private bool _exportDialogOpen;
    private string? _exportDialogFormat;
    private string? _importError;

    // Tool mode: 'select' | 'pan' | 'connector'
    private string _toolMode = "select";

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

        if (_lastHostShowGrid != ShowGrid)
        {
            _lastHostShowGrid = ShowGrid;
            _showGrid = ShowGrid;
        }

        if (_lastHostGridSize != GridSize)
        {
            _lastHostGridSize = GridSize;
            _gridSize = GridSize;
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
        PruneCommandStacks();
        await DocumentChanged.InvokeAsync(doc);
    }

    /// <summary>Drops the undo history of pages that no longer exist (deleted through the page tabs or the Pages panel).</summary>
    private void PruneCommandStacks()
    {
        if (_document is null) return;
        foreach (var id in _pageCommandStacks.Keys.Where(k => _document.Pages.All(p => p.Id != k)).ToList())
        {
            if (_pageCommandStacks.Remove(id, out var stack))
                stack.OnStackChanged -= OnStackChanged;
        }
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

    private async Task ContextMenuToggleGrid() => await OnInspectorShowGridChanged(!_showGrid);

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


    // ── Shell integration (phase 4) ───────────────────────────────────────────

    private bool HasHeader => ShowHeader || HeaderStart is not null || HeaderActions is not null || Title is not null;

    private bool ShowsPageTabs => PageNavigation == WireframePageNavigation.Tabs && ShowPageTabs && _document?.Pages.Count > 0;

    private bool ShowsPagesTab => PageNavigation == WireframePageNavigation.Panel && ShowPageTabs && _document?.Pages.Count > 0;

    /// <summary>The left panel exists while at least one of its tabs (Components, Pages, Layers) is available.</summary>
    private bool HasLeftContent => ShowToolbox || ShowPropertiesPanel || ShowsPagesTab;

    private bool SelectionLocked => _selectedIds.Length > 0 && IsSelectionLocked();

    private void OnShellResolvedLayoutChanged(TmLayoutMode mode)
    {
        _resolvedLayout = mode;
        StateHasChanged();
    }

    private void OnShellLeftOpenChanged(bool open) => _leftOpen = open;

    private void OnShellRightOpenChanged(bool open) => _rightOpen = open;

    private void OnShellCollapsedPanelsChanged(EditorShellPanel collapsed) => _collapsedPanels = collapsed;

    private void ToggleLeftPanel() => _leftOpen = !_leftOpen;

    private void ToggleRightPanel() => _rightOpen = !_rightOpen;

    // ── Toolbar: Align / Distribute dropdowns and their "More" entries ────────

    private static readonly (string Value, string LabelKey, string Icon)[] AlignChoices =
    [
        ("left", "TmWireframe_AlignLeft", IconNames.AlignLeft),
        ("centerh", "TmWireframe_AlignCenterH", IconNames.AlignCenter),
        ("right", "TmWireframe_AlignRight", IconNames.AlignRight),
        ("top", "TmWireframe_AlignTop", IconNames.ArrowUpFromLine),
        ("centerv", "TmWireframe_AlignCenterV", IconNames.AlignVerticalDistributeCenter),
        ("bottom", "TmWireframe_AlignBottom", IconNames.ArrowDownToLine),
    ];

    private IReadOnlyList<TmActionItem> AlignMenuItems => AlignChoices
        .Select(c => new TmActionItem
        {
            Id = "wd-align-" + c.Value,
            Label = Loc[c.LabelKey],
            Icon = c.Icon,
            Disabled = _selectedIds.Length < 2 || IsReadOnly,
            OnClick = EventCallback.Factory.Create(this, () => HandleAlignSelect(c.Value)),
        })
        .ToList();

    private IReadOnlyList<TmActionItem> DistributeMenuItems =>
    [
        new TmActionItem
        {
            Id = "wd-distribute-horizontal",
            Label = Loc["TmWireframe_DistributeHorizontal"],
            Disabled = _selectedIds.Length < 3 || IsReadOnly,
            OnClick = EventCallback.Factory.Create(this, () => HandleDistributeSelect("horizontal")),
        },
        new TmActionItem
        {
            Id = "wd-distribute-vertical",
            Label = Loc["TmWireframe_DistributeVertical"],
            Disabled = _selectedIds.Length < 3 || IsReadOnly,
            OnClick = EventCallback.Factory.Create(this, () => HandleDistributeSelect("vertical")),
        },
    ];

    private Task HandleAlignSelect(string value) => value switch
    {
        "left" => OnAlign(WireframeAlignment.Left),
        "centerh" => OnAlign(WireframeAlignment.CenterH),
        "right" => OnAlign(WireframeAlignment.Right),
        "top" => OnAlign(WireframeAlignment.Top),
        "centerv" => OnAlign(WireframeAlignment.CenterV),
        "bottom" => OnAlign(WireframeAlignment.Bottom),
        _ => Task.CompletedTask,
    };

    private Task HandleDistributeSelect(string value) => value switch
    {
        "horizontal" => OnDistribute(WireframeDistribution.Horizontal),
        "vertical" => OnDistribute(WireframeDistribution.Vertical),
        _ => Task.CompletedTask,
    };

    // ── Inspector callbacks ───────────────────────────────────────────────────

    private async Task OnInspectorShowGridChanged(bool show)
    {
        _showGrid = show;
        await ShowGridChanged.InvokeAsync(show);
        StateHasChanged();
    }

    private async Task OnInspectorGridSizeChanged(int size)
    {
        _gridSize = size;
        if (_canvas is not null)
            await JS.InvokeVoidAsync("tmWireframeDesigner.setGridSize", _canvas.GetSvgRef(), size);
        await GridSizeChanged.InvokeAsync(size);
        StateHasChanged();
    }

    private async Task OnInspectorCanvasResized((double Width, double Height) size)
    {
        if (_canvas is not null) await _canvas.UpdateCanvasSize(size.Width, size.Height);
        StateHasChanged();
    }

    private Task OnDeleteFromInspector() => OnDeleteFromContextMenu();

    /// <summary>The Pages tab switched, added, duplicated or deleted-the-active page: follow it (selection, history, layer).</summary>
    private async Task OnPagesPanelActivePageChanged(string pageId) => await SwitchPage(pageId);
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

    private async Task HandleExportSelect(string format)
    {
        switch (format)
        {
            case "json": await ExportJson(); break;
            case "svg": await ExportSvg(); break;
            case "png": ExportPng(); break;
            case "pdf": ExportPdf(); break;
        }
    }

    private async Task ExportJson()
    {
        if (_document is null) return;
        var json     = WireframeSerializer.Serialize(_document);
        var fileName = SanitizeFileName(_document.Title) + ".wireframe.json";
        await DownloadFile(fileName, "application/json", System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task ExportSvg()
    {
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
        _exportDialogFormat = "png";
        _exportDialogOpen = true;
    }

    private void ExportPdf()
    {
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
    /// (the viewBox last reported by the JS canvas, or the document extent before the first report).
    /// Snapped to the grid only when snapping is on (<see cref="GridSize"/> &gt; 0), then clamped INTO the
    /// page - <c>0 ≤ x ≤ pageWidth − w</c>, <c>0 ≤ y ≤ pageHeight − h</c> - so an element inserted while the
    /// user has panned away from the page still lands on it (an element wider than the page pins to 0).
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

        if (_gridSize > 0)
        {
            x = Math.Round(x / _gridSize) * _gridSize;
            y = Math.Round(y / _gridSize) * _gridSize;
        }

        var maxX = Math.Max(0, (_activePage?.Width  ?? _document?.Width  ?? 1200) - w);
        var maxY = Math.Max(0, (_activePage?.Height ?? _document?.Height ?? 800) - h);
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
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
