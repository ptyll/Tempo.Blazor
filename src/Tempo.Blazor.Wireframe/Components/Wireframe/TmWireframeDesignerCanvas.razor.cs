using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// SVG-based wireframe canvas. Renders <see cref="WireframeDocument"/> elements and
/// communicates with <c>wireframe-designer.js</c> for pan, zoom, drag, resize, and
/// multi-select interactions.
///
/// All mutating operations go through the cascaded <see cref="WireframeCommandStack"/>
/// so that Undo/Redo works automatically. The stack is optional: when null (e.g. used
/// standalone without <c>TmWireframeEditor</c>), mutations are applied directly.
/// </summary>
public partial class TmWireframeDesignerCanvas : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private WireframeComponentRegistry _registry { get; set; } = default!;

    // ── Cascaded command stack (provided by TmWireframeEditor) ───────────────

    /// <summary>
    /// Cascaded from <c>TmWireframeEditor</c>. When present all mutations are routed
    /// through it; when absent mutations are applied directly (standalone usage).
    /// </summary>
    [CascadingParameter] public WireframeCommandStack? CommandStack { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>Document to display and edit. Mutated in-place; bind with @bind-Document.</summary>
    [Parameter] public WireframeDocument? Document { get; set; }

    /// <summary>Raised after every mutation so callers can persist the document.</summary>
    [Parameter] public EventCallback<WireframeDocument> DocumentChanged { get; set; }

    /// <summary>Show grid lines on the canvas background.</summary>
    [Parameter] public bool ShowGrid { get; set; } = true;

    /// <summary>Snap-to-grid cell size in SVG units (pixels). 0 = disabled.</summary>
    [Parameter] public int GridSize { get; set; } = 8;

    /// <summary>When true, dragged elements snap to edges/centers of other elements.</summary>
    [Parameter] public bool SnapToObjects { get; set; }

    /// <summary>Prevent all editing interactions (drag, resize, drop).</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS class applied to the canvas wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Optional application scope used to resolve custom wireframe components.</summary>
    [Parameter] public WireframeComponentScope? ComponentScope { get; set; }

    /// <summary>Raised when the user changes the selection (single or multi).</summary>
    [Parameter] public EventCallback<string[]> OnSelectionChanged { get; set; }

    /// <summary>Raised when the user changes the connector selection.</summary>
    [Parameter] public EventCallback<string[]> OnConnectorSelectionChanged { get; set; }

    /// <summary>Raised when the user creates a connector by dragging from one element to another.</summary>
    [Parameter] public EventCallback<(string FromId, string ToId)> OnConnectorCreated { get; set; }

    /// <summary>Raised when user requests deletion of selected connectors.</summary>
    [Parameter] public EventCallback<string[]> OnDeleteConnectors { get; set; }

    /// <summary>Raised when user drags a connector waypoint to a new position.</summary>
    [Parameter] public EventCallback<(string ConnectorId, int WaypointIndex, double X, double Y)> OnConnectorWaypointDragged { get; set; }

    /// <summary>Raised when user double-clicks a connector path to add a new waypoint.</summary>
    [Parameter] public EventCallback<(string ConnectorId, double X, double Y)> OnConnectorWaypointAdded { get; set; }

    /// <summary>Raised when user drags a selected connector to move all its waypoints.</summary>
    [Parameter] public EventCallback<(string ConnectorId, double Dx, double Dy)> OnConnectorDragged { get; set; }

    /// <summary>Raised when user requests undo (Ctrl+Z). Parent handles the command stack.</summary>
    [Parameter] public EventCallback OnUndo { get; set; }

    /// <summary>Raised when user requests redo (Ctrl+Y / Ctrl+Shift+Z).</summary>
    [Parameter] public EventCallback OnRedo { get; set; }

    /// <summary>Raised when the user switches tool mode via H/V keyboard shortcut.</summary>
    [Parameter] public EventCallback<string> OnToolModeChanged { get; set; }

    /// <summary>Raised when user requests copy style (Ctrl+Shift+C).</summary>
    [Parameter] public EventCallback OnCopyStyleRequested { get; set; }

    /// <summary>Raised when user requests paste style (Ctrl+Shift+V).</summary>
    [Parameter] public EventCallback OnPasteStyleRequested { get; set; }

    /// <summary>Raised when user requests paste size (Ctrl+Shift+S).</summary>
    [Parameter] public EventCallback OnPasteSizeRequested { get; set; }

    /// <summary>Raised when user opens context menu on an element (right-click).</summary>
    [Parameter] public EventCallback<(string Id, double ScreenX, double ScreenY)> OnElementContextMenuRequested { get; set; }

    /// <summary>Raised when user opens context menu on a connector (right-click).</summary>
    [Parameter] public EventCallback<(string Id, double ScreenX, double ScreenY)> OnConnectorContextMenuRequested { get; set; }

    /// <summary>Raised when user opens context menu on empty canvas (right-click).</summary>
    [Parameter] public EventCallback<(double SvgX, double SvgY, double ScreenX, double ScreenY)> OnCanvasContextMenuRequested { get; set; }

    /// <summary>Raised when context menu should close (click outside / Escape).</summary>
    [Parameter] public EventCallback OnCloseContextMenu { get; set; }

    /// <summary>Raised when the viewBox changes (pan/zoom).</summary>
    [Parameter] public EventCallback<(double X, double Y, double W, double H)> OnViewBoxChangedEvent { get; set; }

    /// <summary>Raised when mouse moves over the canvas (SVG coordinates).</summary>
    [Parameter] public EventCallback<(double SvgX, double SvgY)> OnCanvasMouseMoved { get; set; }

    // ── Internal state ───────────────────────────────────────────────────────

    private ElementReference _svgRef;
    private ElementReference _wrapRef;
    private DotNetObjectReference<TmWireframeDesignerCanvas>? _dotNetRef;
    private bool _jsInitialized;
    private bool _jsReadOnly;

    // Stable IDs for SVG defs so multiple canvas instances on the same page don't collide
    private readonly string _svgId       = "tm-wd-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _gridSmallId;
    private readonly string _gridLargeId;

    // ViewBox tracks pan/zoom state driven by JS; not re-rendered by Blazor
    private string _viewBox = "0 0 1200 800";

    // Grid dimensions as strings for Razor binding
    private string _gs = "8";
    private string _gl = "80";

    // Snapshot of element positions at drag-start (for MoveElementsCommand coalescing)
    private Dictionary<string, (double X, double Y)>? _dragStartPositions;

    // Current JS selection – kept in sync so we can re-apply handles after Blazor re-renders
    private string[] _currentSelectionIds = [];

    // Current connector selection IDs (drives .tm-wd-connector--selected CSS class)
    private string[] _currentConnectorSelectionIds = [];

    public TmWireframeDesignerCanvas()
    {
        _gridSmallId = _svgId + "-gs";
        _gridLargeId = _svgId + "-gl";
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        _gs = GridSize.ToString(CultureInfo.InvariantCulture);
        _gl = (GridSize * 10).ToString(CultureInfo.InvariantCulture);

        if (Document is not null)
        {
            _viewBox = $"0 0 {Document.Width.ToString("0.##", CultureInfo.InvariantCulture)} " +
                       $"{Document.Height.ToString("0.##", CultureInfo.InvariantCulture)}";
        }
    }

    /// <summary>How many times the canvas has rendered (a test seam: a left-tab switch or opening the "More" menu must not move it).</summary>
    internal int RenderCount { get; private set; }

    protected override void OnAfterRender(bool firstRender) => RenderCount++;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_jsInitialized)
        {
            _jsInitialized = true;
            _dotNetRef = DotNetObjectReference.Create(this);

            var options = new
            {
                readOnly       = ReadOnly,
                gridSize       = GridSize,
                showGrid       = ShowGrid,
                snapToObjects  = SnapToObjects,
                canvasWidth    = Document?.Width  ?? 1200,
                canvasHeight   = Document?.Height ?? 800,
            };

            _jsReadOnly = ReadOnly;
            await JS.InvokeVoidAsync("tmWireframeDesigner.init", _svgRef, _dotNetRef, options);
        }
        else if (_jsInitialized && _jsReadOnly != ReadOnly)
        {
            // The editor's Read-only toggle (or a host change) flips ReadOnly after init: keep the JS
            // pointer handling in sync so drag/resize is blocked client-side too, not only by the
            // server-side guards.
            _jsReadOnly = ReadOnly;
            await JS.InvokeVoidAsync("tmWireframeDesigner.setReadOnly", _svgRef, ReadOnly);
        }

        if (!firstRender && _jsInitialized && _currentSelectionIds.Length > 0)
        {
            // After Blazor re-renders the SVG (e.g. element resized via Properties Panel),
            // the JS selection handles read data-w/data-h from the DOM. Re-apply the
            // selection so handles snap to the updated element dimensions.
            await JS.InvokeVoidAsync("tmWireframeDesigner.setSelection", _svgRef, _currentSelectionIds);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsInitialized)
        {
            try { await JS.InvokeVoidAsync("tmWireframeDesigner.destroy", _svgRef); }
            catch { /* ignore JS errors during disposal */ }
        }
        _dotNetRef?.Dispose();
    }

    // ── JS → C# callbacks ───────────────────────────────────────────────────

    /// <summary>
    /// Called by JS at mousedown on an element to snapshot positions before drag begins.
    /// Needed so <see cref="MoveElementsCommand"/> can record the "before" state.
    /// </summary>
    [JSInvokable]
    public void OnDragStarted(string[] ids)
    {
        if (Document is null) return;
        _dragStartPositions = [];
        foreach (var id in ids)
        {
            var el = Document.Elements.FirstOrDefault(e => e.Id == id);
            if (el is not null)
                _dragStartPositions[id] = (el.X, el.Y);
        }
    }

    /// <summary>Single element moved by dragging.</summary>
    [JSInvokable]
    public async Task OnElementMoved(string id, double x, double y)
    {
        if (Document is null || ReadOnly) return;

        var before = _dragStartPositions is not null && _dragStartPositions.TryGetValue(id, out var bp)
            ? new Dictionary<string, (double X, double Y)> { [id] = bp }
            : null;
        var after  = new Dictionary<string, (double X, double Y)> { [id] = (x, y) };

        ExecuteMove(before, after);
        await NotifyAndRender();
    }

    /// <summary>Multiple elements moved together (group drag).</summary>
    [JSInvokable]
    public async Task OnElementsMoved(ElementMove[] moves)
    {
        if (Document is null || ReadOnly) return;

        var before = _dragStartPositions;
        var after  = moves.ToDictionary(m => m.Id, m => (m.X, m.Y));

        ExecuteMove(before, after);
        await NotifyAndRender();
    }

    private void ExecuteMove(
        Dictionary<string, (double X, double Y)>? before,
        Dictionary<string, (double X, double Y)> after)
    {
        if (Document is null) return;

        if (CommandStack is not null)
        {
            // Build "before" from current document state when no snapshot exists
            var beforeSnapshot = before ?? after.Keys.ToDictionary(
                id => id,
                id => Document.Elements.FirstOrDefault(e => e.Id == id) is { } el
                    ? (el.X, el.Y) : (0.0, 0.0));

            CommandStack.Push(new MoveElementsCommand(Document, beforeSnapshot, after));
        }
        else
        {
            // Standalone mode: apply directly
            foreach (var el in Document.Elements)
            {
                if (after.TryGetValue(el.Id, out var pos))
                { el.X = pos.X; el.Y = pos.Y; }
            }
        }
    }

    /// <summary>Element resized via a handle.</summary>
    [JSInvokable]
    public async Task OnElementResized(string id, double x, double y, double w, double h)
    {
        if (Document is null || ReadOnly) return;
        var el = Document.Elements.FirstOrDefault(e => e.Id == id);
        if (el is null) return;

        if (CommandStack is not null)
        {
            CommandStack.Push(new ResizeElementCommand(
                Document, id,
                el.X, el.Y, el.W, el.H,
                x, y, w, h));
        }
        else
        {
            el.X = x; el.Y = y; el.W = w; el.H = h;
        }

        await NotifyAndRender();
    }

    /// <summary>Component dropped from toolbox onto the canvas.</summary>
    [JSInvokable]
    public async Task OnElementDropped(string type, double x, double y)
    {
        if (Document is null || ReadOnly) return;
        var def = _registry.GetDef(type, ComponentScope);
        var el  = WireframeDocumentExtensions.NewElement(
            type, x, y,
            def?.DefaultWidth  ?? 160,
            def?.DefaultHeight ?? 40,
            def?.Props ?? []);
        el.ZIndex = Document.Elements.Count > 0
            ? Document.Elements.Max(e => e.ZIndex) + 1
            : 0;
        el.LayerId = Document.ActiveLayerId;

        if (CommandStack is not null)
            CommandStack.Push(new AddElementCommand(Document, el));
        else
            Document.Elements.Add(el);

        await NotifyAndRender();
    }

    /// <summary>Rubber-band selection completed.</summary>
    [JSInvokable]
    public async Task OnMultiSelect(string[] ids)
    {
        _currentSelectionIds = ids;
        await OnSelectionChanged.InvokeAsync(ids);
    }

    /// <summary>Selection changed (single click or shift+click).</summary>
    [JSInvokable("OnSelectionChanged")]
    public async Task JsOnSelectionChanged(string[] ids)
    {
        _currentSelectionIds = ids;
        _currentConnectorSelectionIds = []; // clear connector selection when element selected
        await OnSelectionChanged.InvokeAsync(ids);
    }

    /// <summary>Connector selection changed.</summary>
    [JSInvokable("OnConnectorSelectionChanged")]
    public async Task JsOnConnectorSelectionChanged(string[] ids)
    {
        _currentConnectorSelectionIds = ids;
        _currentSelectionIds = []; // clear element selection when connector selected
        await OnConnectorSelectionChanged.InvokeAsync(ids);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>User dragged from one element to another in connector tool mode.</summary>
    [JSInvokable("OnConnectorCreated")]
    public async Task JsOnConnectorCreated(string fromId, string toId)
    {
        await OnConnectorCreated.InvokeAsync((fromId, toId));
    }

    /// <summary>Delete key pressed for connectors.</summary>
    [JSInvokable("OnDeleteConnectors")]
    public async Task JsOnDeleteConnectors(string[] ids)
    {
        await OnDeleteConnectors.InvokeAsync(ids);
    }

    /// <summary>User dragged a connector waypoint to a new position.</summary>
    [JSInvokable("OnConnectorWaypointDragged")]
    public async Task JsOnConnectorWaypointDragged(string connectorId, int waypointIndex, double x, double y)
    {
        await OnConnectorWaypointDragged.InvokeAsync((connectorId, waypointIndex, x, y));
    }

    /// <summary>User double-clicked a connector path to add a new waypoint.</summary>
    [JSInvokable("OnConnectorWaypointAdded")]
    public async Task JsOnConnectorWaypointAdded(string connectorId, double x, double y)
    {
        await OnConnectorWaypointAdded.InvokeAsync((connectorId, x, y));
    }

    /// <summary>User dragged a connector to move all its waypoints.</summary>
    [JSInvokable("OnConnectorDragged")]
    public async Task JsOnConnectorDragged(string connectorId, double dx, double dy)
    {
        await OnConnectorDragged.InvokeAsync((connectorId, dx, dy));
    }

    /// <summary>Delete key pressed – remove the selected elements.</summary>
    [JSInvokable]
    public async Task OnDeleteSelected(string[] ids)
    {
        if (Document is null || ReadOnly) return;

        if (CommandStack is not null)
            CommandStack.Push(new RemoveElementsCommand(Document, ids));
        else
            Document.Elements.RemoveAll(e => ids.Contains(e.Id));

        await NotifyAndRender();
    }

    /// <summary>Select-all (Ctrl+A).</summary>
    [JSInvokable]
    public async Task OnSelectAll()
    {
        if (Document is null) return;
        var ids = Document.Elements.Select(e => e.Id).ToArray();
        _currentSelectionIds = ids;
        await JS.InvokeVoidAsync("tmWireframeDesigner.setSelection", _svgRef, ids);
        await OnSelectionChanged.InvokeAsync(ids);
    }

    /// <summary>Clear selection (Escape).</summary>
    [JSInvokable]
    public async Task OnClearSelection()
    {
        _currentSelectionIds = [];
        await OnSelectionChanged.InvokeAsync([]);
    }

    /// <summary>Undo requested by keyboard shortcut.</summary>
    [JSInvokable("OnUndo")]
    public async Task JsOnUndo()
    {
        if (CommandStack is not null)
        {
            CommandStack.Undo();
            await NotifyAndRender();
        }
        else
        {
            await OnUndo.InvokeAsync();
        }
    }

    /// <summary>Redo requested by keyboard shortcut.</summary>
    [JSInvokable("OnRedo")]
    public async Task JsOnRedo()
    {
        if (CommandStack is not null)
        {
            CommandStack.Redo();
            await NotifyAndRender();
        }
        else
        {
            await OnRedo.InvokeAsync();
        }
    }

    /// <summary>Bring to front requested by keyboard shortcut.</summary>
    [JSInvokable]
    public async Task OnBringToFront(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        if (CommandStack is not null)
            CommandStack.Push(new BringToFrontCommand(Document, ids));
        else
        {
            var maxZ = Document.Elements.Max(e => e.ZIndex);
            foreach (var id in ids)
            {
                var el = Document.Elements.FirstOrDefault(e => e.Id == id);
                if (el is not null) el.ZIndex = ++maxZ;
            }
        }
        await NotifyAndRender();
    }

    /// <summary>Send to back requested by keyboard shortcut.</summary>
    [JSInvokable]
    public async Task OnSendToBack(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        if (CommandStack is not null)
            CommandStack.Push(new SendToBackCommand(Document, ids));
        else
        {
            var minZ = Document.Elements.Min(e => e.ZIndex);
            var offset = ids.Length;
            foreach (var id in ids)
            {
                var el = Document.Elements.FirstOrDefault(e => e.Id == id);
                if (el is not null) el.ZIndex = minZ - offset--;
            }
        }
        await NotifyAndRender();
    }

    /// <summary>Toggle lock requested by keyboard shortcut.</summary>
    [JSInvokable]
    public async Task OnToggleLock(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        var elements = ids
            .Select(id => Document.Elements.FirstOrDefault(e => e.Id == id))
            .Where(e => e is not null)
            .ToList();
        if (elements.Count == 0) return;

        var allLocked = elements.All(e => e!.IsLocked || !string.IsNullOrEmpty(e.LockedBy));
        if (allLocked)
        {
            if (CommandStack is not null)
                CommandStack.Push(new UnlockElementsCommand(Document, ids));
            else
                foreach (var el in elements) el!.IsLocked = false;
        }
        else
        {
            if (CommandStack is not null)
                CommandStack.Push(new LockElementsCommand(Document, ids));
            else
                foreach (var el in elements) el!.IsLocked = true;
        }
        await NotifyAndRender();
    }

    /// <summary>Group selected elements (Ctrl+G).</summary>
    [JSInvokable]
    public async Task OnGroup(string[] ids)
    {
        if (Document is null || ReadOnly || ids.Length < 2) return;

        if (CommandStack is not null)
            CommandStack.Push(new GroupElementsCommand(Document, ids));
        else
        {
            // Standalone mode: apply directly
            var cmd = new GroupElementsCommand(Document, ids);
            cmd.Execute();
        }

        await NotifyAndRender();
    }

    /// <summary>Ungroup selected groups (Ctrl+Shift+G).</summary>
    [JSInvokable]
    public async Task OnUngroup(string[] ids)
    {
        if (Document is null || ReadOnly || ids.Length == 0) return;

        if (CommandStack is not null)
        {
            foreach (var id in ids)
                CommandStack.Push(new UngroupElementsCommand(Document, id));
        }
        else
        {
            foreach (var id in ids)
            {
                var cmd = new UngroupElementsCommand(Document, id);
                cmd.Execute();
            }
        }

        await NotifyAndRender();
    }

    /// <summary>Duplicate selected elements (Ctrl+D).</summary>
    [JSInvokable]
    public async Task OnDuplicate(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        var maxZ   = Document.Elements.Count > 0 ? Document.Elements.Max(e => e.ZIndex) : 0;
        var offset = GridSize > 0 ? GridSize * 2 : 16;
        var added  = new List<string>();

        foreach (var id in ids)
        {
            var src = Document.Elements.FirstOrDefault(e => e.Id == id);
            if (src is null) continue;
            var copy = src.DeepCopy();
            copy.X += offset; copy.Y += offset;
            copy.ZIndex = ++maxZ;

            if (CommandStack is not null)
                CommandStack.Push(new AddElementCommand(Document, copy));
            else
                Document.Elements.Add(copy);

            added.Add(copy.Id);
        }

        if (added.Count == 0) return;
        await NotifyAndRender();
        await JS.InvokeVoidAsync("tmWireframeDesigner.setSelection", _svgRef, added.ToArray());
        await OnSelectionChanged.InvokeAsync(added.ToArray());
    }

    /// <summary>Element rotated via rotate handle.</summary>
    [JSInvokable]
    public async Task OnElementRotated(string id, double rotation)
    {
        if (Document is null || ReadOnly) return;
        var el = Document.Elements.FirstOrDefault(e => e.Id == id);
        if (el is null || el.IsLocked || !string.IsNullOrEmpty(el.LockedBy)) return;

        var before = el.Rotation;
        if (CommandStack is not null)
            CommandStack.Push(new RotateElementCommand(Document, id, before, rotation));
        else
            el.Rotation = rotation;

        await NotifyAndRender();
    }

    /// <summary>Zoom level changed by wheel.</summary>
    [JSInvokable]
    public Task OnZoomChanged(double scale) => Task.CompletedTask;

    /// <summary>ViewBox updated after pan.</summary>
    [JSInvokable]
    public async Task OnViewBoxChanged(double x, double y, double w, double h)
    {
        _viewBox = $"{F(x)} {F(y)} {F(w)} {F(h)}";
        await OnViewBoxChangedEvent.InvokeAsync((x, y, w, h));
    }

    /// <summary>Context menu on element.</summary>
    [JSInvokable]
    public async Task OnElementContextMenu(string id, double screenX, double screenY)
        => await OnElementContextMenuRequested.InvokeAsync((id, screenX, screenY));

    /// <summary>Context menu on connector.</summary>
    [JSInvokable]
    public async Task OnConnectorContextMenu(string id, double screenX, double screenY)
        => await OnConnectorContextMenuRequested.InvokeAsync((id, screenX, screenY));

    /// <summary>Context menu on empty canvas.</summary>
    [JSInvokable]
    public async Task OnCanvasContextMenu(double svgX, double svgY, double screenX, double screenY)
        => await OnCanvasContextMenuRequested.InvokeAsync((svgX, svgY, screenX, screenY));

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Programmatically zoom to a specific scale level.</summary>
    public async Task SetZoom(double scale)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmWireframeDesigner.zoomTo", _svgRef, scale);
    }

    /// <summary>Fit all elements into view. Returns the computed scale.</summary>
    public async Task<double> FitToView()
    {
        if (!_jsInitialized) return 1.0;
        return await JS.InvokeAsync<double>("tmWireframeDesigner.fitToView", _svgRef, 40);
    }

    /// <summary>Add an element via command stack (undoable) and trigger a re-render.</summary>
    public async Task AddElement(WireframeElement el)
    {
        if (Document is null) return;
        if (el.ZIndex == 0 && Document.Elements.Count > 0)
            el.ZIndex = Document.Elements.Max(e => e.ZIndex) + 1;

        if (CommandStack is not null)
            CommandStack.Push(new AddElementCommand(Document, el));
        else
            Document.Elements.Add(el);

        await NotifyAndRender();
    }

    /// <summary>Remove elements via command stack (undoable) and trigger a re-render.</summary>
    public async Task RemoveElements(params string[] ids)
    {
        if (Document is null) return;

        if (CommandStack is not null)
            CommandStack.Push(new RemoveElementsCommand(Document, ids));
        else
            Document.Elements.RemoveAll(e => ids.Contains(e.Id));

        await NotifyAndRender();
    }

    /// <summary>Set the JS-side selection highlights without raising OnSelectionChanged.</summary>
    public async Task SelectElements(params string[] ids)
    {
        if (!_jsInitialized) return;
        _currentSelectionIds = ids;
        await JS.InvokeVoidAsync("tmWireframeDesigner.setSelection", _svgRef, ids);
    }

    /// <summary>Clear all selection highlights.</summary>
    public async Task ClearSelection()
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmWireframeDesigner.setSelection", _svgRef, Array.Empty<string>());
    }

    /// <summary>Switches the JS tool mode programmatically (called from toolbar buttons).</summary>
    public async Task SetToolMode(string mode)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmWireframeDesigner.setToolMode", _svgRef, mode);
    }

    /// <summary>Notifies JS of updated canvas dimensions after a ResizeCanvasCommand.</summary>
    public async Task UpdateCanvasSize(double w, double h)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmWireframeDesigner.updateCanvasSize", _svgRef, w, h);
    }

    /// <summary>JS invokes this when user presses H or V to switch tool mode.</summary>
    [JSInvokable("OnToolModeChanged")]
    public async Task JsOnToolModeChanged(string mode)
        => await OnToolModeChanged.InvokeAsync(mode);

    /// <summary>JS invokes this when user presses Ctrl+Shift+C.</summary>
    [JSInvokable]
    public async Task OnCopyStyle()
        => await OnCopyStyleRequested.InvokeAsync();

    /// <summary>JS invokes this when user presses Ctrl+Shift+V.</summary>
    [JSInvokable]
    public async Task OnPasteStyle()
        => await OnPasteStyleRequested.InvokeAsync();

    /// <summary>JS invokes this when user presses Ctrl+Shift+S.</summary>
    [JSInvokable]
    public async Task OnPasteSize()
        => await OnPasteSizeRequested.InvokeAsync();

    /// <summary>Returns true when the element's layer is visible (or element has no layer).</summary>
    public bool IsLayerVisible(string? layerId)
    {
        if (Document is null || string.IsNullOrEmpty(layerId)) return true;
        var layer = Document.Layers.FirstOrDefault(l => l.Id == layerId);
        return layer?.IsVisible ?? true;
    }

    /// <summary>Returns true when the element's layer is locked (or element has no layer).</summary>
    public bool IsLayerLocked(string? layerId)
    {
        if (Document is null || string.IsNullOrEmpty(layerId)) return false;
        var layer = Document.Layers.FirstOrDefault(l => l.Id == layerId);
        return layer?.IsLocked ?? false;
    }

    /// <summary>Mouse moved over the canvas in SVG coordinates.</summary>
    [JSInvokable("OnCanvasMouseMoved")]
    public async Task JsOnCanvasMouseMoved(double svgX, double svgY)
    {
        await OnCanvasMouseMoved.InvokeAsync((svgX, svgY));
    }

    /// <summary>Returns the underlying SVG element reference (needed by TmWireframeEditor for JS calls).</summary>
    public ElementReference GetSvgRef() => _svgRef;

    /// <summary>Export the canvas as a clean SVG string (selection handles stripped).</summary>
    public async Task<string> ExportSvg()
    {
        if (!_jsInitialized) return string.Empty;
        return await JS.InvokeAsync<string>("tmWireframeDesigner.exportSvg", _svgRef);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task NotifyAndRender()
    {
        if (Document is not null)
            await DocumentChanged.InvokeAsync(Document);
        await InvokeAsync(StateHasChanged);
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ── Helper DTOs ──────────────────────────────────────────────────────────

    /// <summary>Payload sent by JS for a bulk move operation.</summary>
    public sealed class ElementMove
    {
        public string Id { get; set; } = "";
        public double X  { get; set; }
        public double Y  { get; set; }
    }
}
