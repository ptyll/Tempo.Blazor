using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Unified SVG diagram canvas (draw.io‑inspired 4‑pane architecture:
/// backgroundPane / drawPane / overlayPane / decoratorPane).
/// Renders <see cref="DiagramDocument"/> nodes and edges as native SVG
/// primitives and <see cref="foreignObject"/> labels, and communicates
/// with <c>diagram-editor.js</c> for pan, zoom, drag, and selection.
/// </summary>
public partial class TmDiagramCanvas : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private DiagramStencilRegistry StencilRegistry { get; set; } = default!;

    // ── Cascaded command stack ───────────────────────────────────────────────

    [CascadingParameter] public DiagramCommandStack? CommandStack { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>Document to display and edit.</summary>
    [Parameter] public DiagramDocument? Document { get; set; }

    /// <summary>Raised after every mutation.</summary>
    [Parameter] public EventCallback<DiagramDocument> DocumentChanged { get; set; }

    /// <summary>Show grid lines on the canvas background.</summary>
    [Parameter] public bool ShowGrid { get; set; } = true;

    /// <summary>Snap-to-grid cell size in pixels. 0 = disabled.</summary>
    [Parameter] public int GridSize { get; set; } = 8;

    /// <summary>Prevent all editing interactions.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS class applied to the canvas wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Current zoom level used for pixel-to-document conversion.</summary>
    [Parameter] public double Zoom { get; set; } = 1.0;

    /// <summary>Raised when the user changes the selection.</summary>
    [Parameter] public EventCallback<string[]> OnSelectionChanged { get; set; }

    /// <summary>Raised when user requests undo.</summary>
    [Parameter] public EventCallback OnUndo { get; set; }

    /// <summary>Raised when user requests redo.</summary>
    [Parameter] public EventCallback OnRedo { get; set; }

    /// <summary>Raised when the user switches tool mode.</summary>
    [Parameter] public EventCallback<string> OnToolModeChanged { get; set; }

    /// <summary>Raised when the user presses Ctrl+F.</summary>
    [Parameter] public EventCallback OnShowSearch { get; set; }

    [Parameter] public EventCallback OnShowReplace { get; set; }

    /// <summary>Active group identifier. Only nodes with matching ParentGroupId are rendered.</summary>
    [Parameter] public string? ActiveGroupId { get; set; }

    /// <summary>Raised when the user presses a quick-insert stencil key (A,S,D,F,R).</summary>
    [Parameter] public EventCallback<string> OnQuickInsert { get; set; }

    /// <summary>Raised when the user presses Ctrl+Home/End to navigate to a corner.</summary>
    [Parameter] public EventCallback<string> OnNavigateToCorner { get; set; }

    /// <summary>Raised when the user presses Ctrl+PageUp/PageDown to switch page.</summary>
    [Parameter] public EventCallback<int> OnSwitchPage { get; set; }

    /// <summary>Raised when the user presses Ctrl+Shift+G to group selected nodes.</summary>
    [Parameter] public EventCallback OnGroupSelected { get; set; }

    /// <summary>Raised when the user presses Ctrl+Shift+L to lock selected nodes.</summary>
    [Parameter] public EventCallback OnLockSelected { get; set; }

    /// <summary>Raised when the user double-clicks or presses Ctrl+Shift+G on a group to enter it.</summary>
    [Parameter] public EventCallback<string> OnEnterGroup { get; set; }

    /// <summary>Raised when the user presses Ctrl+Shift+F to exit the current group.</summary>
    [Parameter] public EventCallback OnExitGroup { get; set; }

    /// <summary>Raised when the user presses Ctrl+B to toggle bold on selected nodes.</summary>
    [Parameter] public EventCallback OnToggleBold { get; set; }

    /// <summary>Raised when the user presses Ctrl+I to toggle italic on selected nodes.</summary>
    [Parameter] public EventCallback OnToggleItalic { get; set; }

    /// <summary>Raised when the user presses Ctrl+U to toggle underline on selected nodes.</summary>
    [Parameter] public EventCallback OnToggleUnderline { get; set; }

    /// <summary>Raised when the zoom level changes (e.g. via wheel).</summary>
    [Parameter] public EventCallback<double> ZoomChanged { get; set; }

    /// <summary>Raised when the viewport changes after pan, zoom, or fit.</summary>
    [Parameter] public EventCallback<DiagramMinimapViewport> ViewportChanged { get; set; }

    /// <summary>Raised when the ruler cursor moves.</summary>
    [Parameter] public EventCallback<(double X, double Y)> OnRulerCursorMoved { get; set; }

    /// <summary>Raised when a port is clicked to start drawing an edge.</summary>
    [Parameter] public EventCallback<(string NodeId, string PortId)> OnPortMouseDown { get; set; }

    /// <summary>Raised when a node is right-clicked or long-pressed to open a context menu.</summary>
    [Parameter] public EventCallback<(string NodeId, double ScreenX, double ScreenY)> OnNodeContextMenu { get; set; }

    /// <summary>Raised when an edge is right-clicked to open a context menu.</summary>
    [Parameter] public EventCallback<(string EdgeId, double ScreenX, double ScreenY)> OnEdgeContextMenu { get; set; }

    /// <summary>Raised when a table cell is right-clicked to open a context menu.</summary>
    [Parameter] public EventCallback<(string NodeId, int Row, int Column, double ScreenX, double ScreenY)> OnTableCellContextMenu { get; set; }

    /// <summary>Raised when the user presses Escape and there is a table cell sub-selection
    /// that should be cleared before the node selection. Used to implement the draw.io-style
    /// "drill-out" cascade (first Escape exits cell mode, second Escape clears node selection).</summary>
    [Parameter] public EventCallback OnClearTableCellSelection { get; set; }

    /// <summary>Raised when the empty canvas is right-clicked to open a context menu.</summary>
    [Parameter] public EventCallback<(double CanvasX, double CanvasY, double ScreenX, double ScreenY)> OnCanvasContextMenu { get; set; }

    /// <summary>Raised when the context menu should be closed (e.g. second right-click).</summary>
    [Parameter] public EventCallback OnCloseContextMenu { get; set; }

    /// <summary>Selected table cells for multi-select merge UI.</summary>
    [Parameter] public List<(int Row, int Column)> SelectedTableCells { get; set; } = [];

    /// <summary>Raised when a table cell is clicked (with or without Ctrl).</summary>
    [Parameter] public EventCallback<(string NodeId, int Row, int Column, bool IsCtrlHeld)> OnTableCellSelect { get; set; }

    /// <summary>Raised when a node with a link is clicked.</summary>
    [Parameter] public EventCallback<(string NodeId, string Link)> OnNodeLinkClicked { get; set; }

    // ── Internal state ───────────────────────────────────────────────────────

    private ElementReference _containerRef;
    private ElementReference _svgRef;
    private ElementReference _labelInputRef;
    private DotNetObjectReference<TmDiagramCanvas>? _dotNetRef;
    private bool _jsInitialized;

    private readonly string _svgId = "tmd-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _gridSmallId;
    private readonly string _gridLargeId;

    private string _viewBox = "0 0 3000 2000";
    private string _gs = "8";
    private string _gl = "80";

    /// <summary>Show page view with shadow and boundaries.</summary>
    [Parameter] public bool ShowPageView { get; set; } = true;

    private const double PageViewMargin = 200;
    private string _pageShadowFilterId => _svgId + "-ps";

    private Dictionary<string, NodeMoveState>? _dragStartPositions;
    private bool _isJsDragging;
    private string[] _currentSelectionIds = [];
    private string? _activeSearchResultId;

    private string? _resizeNodeId;
    private string? _resizeHandle;
    private (double X, double Y) _resizeStartScreen;
    private (double X, double Y, double W, double H) _resizeStartRect;
    private Dictionary<string, (double X, double Y, double W, double H)>? _resizeGroupMemberStartRects;
    private const double MinNodeSize = 20;
    private const double RotateSnap = 15.0;

    private double _viewportX;
    private double _viewportY;
    private double _viewportW = 3000;
    private double _viewportH = 2000;
    // True once JS has sent its first OnViewportChanged callback; before that
    // _viewport{X,Y,W,H} hold conservative defaults (0,0,3000,2000) so that
    // viewport culling is effectively off. `GetViewport()` returns a zeroed
    // viewport while _viewportInitialized is false so callers (e.g. toolbar
    // insert commands running in bUnit without JS) can fall back to a sensible
    // document-center placement.
    private bool _viewportInitialized;
    private const double ViewportMargin = 200;
    private string? _editingEdgeLabelId;
    private string _editingLabelValue = "";
    private (string EdgeId, int WaypointIndex)? _pendingVirtualBend;

    public TmDiagramCanvas()
    {
        _gridSmallId = _svgId + "-gs";
        _gridLargeId = _svgId + "-gl";
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        _gs = GridSize.ToString(CultureInfo.InvariantCulture);
        _gl = (GridSize * 10).ToString(CultureInfo.InvariantCulture);

        // Only seed `_viewBox` from the document dimensions before JS has taken
        // control of the viewBox. After init, JS drives the viewBox (pan, zoom,
        // fit-to-view) and pushes updates via OnViewBoxChanged. Overwriting
        // _viewBox here on every parameter change would revert the live view
        // back to the full document rect and hide the actual rendered content.
        //
        // Since F2 there is no CSS transform:scale on <svg>; Page.Scale is
        // folded directly into viewBox dimensions so the initial render honours
        // the page zoom without a second CSS-level multiplier.
        if (!_jsInitialized && Document is not null)
        {
            var scale = Math.Max(0.01, Document.ActivePage?.Scale ?? 1.0);
            var inv = 1.0 / scale;
            if (ShowPageView)
            {
                _viewBox = $"{(-PageViewMargin * inv).ToString("0.##", CultureInfo.InvariantCulture)} " +
                           $"{(-PageViewMargin * inv).ToString("0.##", CultureInfo.InvariantCulture)} " +
                           $"{((Document.Width + 2 * PageViewMargin) * inv).ToString("0.##", CultureInfo.InvariantCulture)} " +
                           $"{((Document.Height + 2 * PageViewMargin) * inv).ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            else
            {
                _viewBox = $"0 0 {(Document.Width * inv).ToString("0.##", CultureInfo.InvariantCulture)} " +
                           $"{(Document.Height * inv).ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            _lastAppliedPageScale = scale;
        }
    }

    // Tracks the last Page.Scale value we've pushed into the JS viewBox so we can
    // detect when the properties panel changes Page.Scale and forward it as a
    // zoomTo() call. Kept in sync with Document.ActivePage.Scale after init and
    // reseeded whenever OnParametersSet recomputes the initial viewBox.
    private double _lastAppliedPageScale = 1.0;

    // Tracks the ReadOnly value last pushed into the JS instance (seeded by the
    // init options). Read-only must be propagated to JS on EVERY change, not
    // only at init — otherwise diagram-editor.js keeps mutating the document
    // (Delete/arrows/undo/redo) after the host flips ReadOnly.
    private bool _lastAppliedReadOnly;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_jsInitialized)
        {
            _jsInitialized = true;
            _dotNetRef = DotNetObjectReference.Create(this);

            var options = new
            {
                readOnly = ReadOnly,
                gridSize = GridSize,
                showGrid = ShowGrid,
                canvasWidth = Document?.Width ?? 3000,
                canvasHeight = Document?.Height ?? 2000,
                activeGroupId = ActiveGroupId,
                rulerUnit = Document?.ActivePage?.RulerUnit.ToString().ToLowerInvariant() ?? "px",
                pageScale = Document?.ActivePage?.PageScale ?? 1.0,
            };

            await JS.InvokeVoidAsync("tmDiagramEditor.init", _containerRef, _dotNetRef, options);
            _lastAppliedReadOnly = ReadOnly;
        }
        else if (_jsInitialized)
        {
            // F2: syncHtmlTransform was removed — there is no HTML overlay.
            // The unified SVG canvas uses viewBox for zoom/pan. We only forward
            // Page.Scale changes (driven by the Properties panel) to JS zoomTo
            // so the properties widget keeps its "page zoom" semantics.
            var pageScale = Math.Max(0.01, Document?.ActivePage?.Scale ?? 1.0);
            if (Math.Abs(pageScale - _lastAppliedPageScale) > 1e-4)
            {
                _lastAppliedPageScale = pageScale;
                try { await JS.InvokeVoidAsync("tmDiagramEditor.zoomTo", _containerRef, pageScale); }
                catch { /* JS may be mid-teardown */ }
            }

            // Propagate read-only flips (user toggle or host parameter change)
            // into the JS instance so keyboard and pointer interactions stop
            // mutating the document.
            if (ReadOnly != _lastAppliedReadOnly)
            {
                _lastAppliedReadOnly = ReadOnly;
                try { await JS.InvokeVoidAsync("tmDiagramEditor.setReadOnly", _containerRef, ReadOnly); }
                catch { /* JS may be mid-teardown */ }
            }

            if (_currentSelectionIds.Length > 0)
            {
                await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, _currentSelectionIds);
            }
        }

        if (_editingEdgeLabelId is not null)
        {
            try { await _labelInputRef.FocusAsync(); }
            catch { /* ignore focus errors */ }
        }

        if (_jsInitialized && Document is not null)
        {
            try { await JS.InvokeVoidAsync("tmDiagramEditor.typesetMath", _containerRef); }
            catch { /* ignore MathJax errors */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsInitialized)
        {
            try { await JS.InvokeVoidAsync("tmDiagramEditor.destroy", _containerRef); }
            catch { /* ignore JS errors during disposal */ }
        }
        _dotNetRef?.Dispose();
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => !_isJsDragging;

    // ── JS → C# callbacks ───────────────────────────────────────────────────

    [JSInvokable]
    public void OnDragStarted(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        _isJsDragging = true;
        _dragStartPositions = [];
        foreach (var id in ids)
        {
            var node = Document.Nodes.FirstOrDefault(n => n.Id == id);
            if (node is not null)
                _dragStartPositions[id] = new NodeMoveState(node.X, node.Y, node.ParentNodeId, node.SwimlaneRow, node.SwimlaneColumn);
        }
    }

    [JSInvokable]
    public async Task OnElementMoved(string id, double x, double y)
    {
        if (Document is null || ReadOnly)
        {
            // Release the JS-drag latch even when the move is rejected, so a
            // read-only flip mid-drag can't leave ShouldRender() disabled
            // (ShouldRender() => !_isJsDragging) and freeze the canvas.
            _isJsDragging = false;
            return;
        }
        var movedNode = Document.Nodes.FirstOrDefault(n => n.Id == id);
        if (movedNode is not null && IsNodeLocked(movedNode))
        {
            _isJsDragging = false;
            return;
        }

        var before = _dragStartPositions is not null && _dragStartPositions.TryGetValue(id, out var bp)
            ? new Dictionary<string, NodeMoveState> { [id] = bp }
            : null;
        var afterState = ResolveSwimlaneState(id, x, y, before?.GetValueOrDefault(id));
        var after = new Dictionary<string, NodeMoveState> { [id] = afterState };

        ExecuteMove(before, after);
        await RecalculateOrthogonalWaypointsForMovedNodesAsync([id]);
        _isJsDragging = false;
        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnElementsMoved(ElementMove[] moves)
    {
        if (Document is null || ReadOnly)
        {
            // See OnElementMoved: release the JS-drag latch on rejection.
            _isJsDragging = false;
            return;
        }
        if (moves.Any(m => Document.Nodes.FirstOrDefault(n => n.Id == m.Id) is { } node && IsNodeLocked(node)))
        {
            _isJsDragging = false;
            return;
        }

        var before = _dragStartPositions;
        var after = moves.ToDictionary(
            m => m.Id,
            m => ResolveSwimlaneState(m.Id, m.X, m.Y, before?.GetValueOrDefault(m.Id)));

        ExecuteMove(before, after);
        await RecalculateOrthogonalWaypointsForMovedNodesAsync(moves.Select(m => m.Id));
        _isJsDragging = false;
        await NotifyAndRender();
    }

    private NodeMoveState ResolveSwimlaneState(string id, double x, double y, NodeMoveState? original)
    {
        if (Document is null) return original ?? new NodeMoveState(x, y, null, -1, -1);

        var node = Document.Nodes.FirstOrDefault(n => n.Id == id);
        if (node is null) return original ?? new NodeMoveState(x, y, null, -1, -1);

        double cx = x + node.W / 2;
        double cy = y + node.H / 2;

        // Find top-most swimlane that contains the node center (excluding self)
        foreach (var candidate in Document.Nodes
            .Where(n => n.SwimlaneData is not null && n.Id != id)
            .OrderByDescending(n => n.ZIndex))
        {
            if (Services.SwimlaneLayoutService.ComputeCell(candidate, cx, cy) is var cell && cell.HasValue)
            {
                return new NodeMoveState(x, y, candidate.Id, cell.Value.Row, cell.Value.Column);
            }
        }

        // Left swimlane: keep original parent if still within bounds, otherwise detach
        if (!string.IsNullOrEmpty(node.ParentNodeId))
        {
            var parent = Document.Nodes.FirstOrDefault(n => n.Id == node.ParentNodeId);
            if (parent?.SwimlaneData is not null)
            {
                if (Services.SwimlaneLayoutService.ComputeCell(parent, cx, cy) is var cell && cell.HasValue)
                {
                    return new NodeMoveState(x, y, parent.Id, cell.Value.Row, cell.Value.Column);
                }
            }
        }

        return new NodeMoveState(x, y, null, -1, -1);
    }

    private void ExecuteMove(
        Dictionary<string, NodeMoveState>? before,
        Dictionary<string, NodeMoveState> after)
    {
        if (Document is null) return;

        if (CommandStack is not null)
        {
            var beforeSnapshot = before ?? after.Keys.ToDictionary(
                id => id,
                id => Document.Nodes.FirstOrDefault(n => n.Id == id) is { } node
                    ? new NodeMoveState(node.X, node.Y, node.ParentNodeId, node.SwimlaneRow, node.SwimlaneColumn)
                    : new NodeMoveState(0, 0, null, -1, -1));

            CommandStack.Push(new MoveNodesCommand(Document, beforeSnapshot, after));
        }
        else
        {
            foreach (var node in Document.Nodes)
            {
                if (after.TryGetValue(node.Id, out var state))
                {
                    node.X = state.X;
                    node.Y = state.Y;
                    node.ParentNodeId = state.ParentNodeId;
                    node.SwimlaneRow = state.SwimlaneRow;
                    node.SwimlaneColumn = state.SwimlaneColumn;
                }
            }
        }
    }

    // ── Resize handles ───────────────────────────────────────────────────────

    private static readonly string[] _resizeHandles = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
    private static readonly (string Css, string Label)[] _connectDirections =
    [
        ("n", "Top"),
        ("e", "Right"),
        ("s", "Bottom"),
        ("w", "Left")
    ];

    /// <summary>Raised when a connect arrow is clicked.</summary>
    [Parameter] public EventCallback<(string NodeId, string Direction)> OnConnectArrowClicked { get; set; }

    private void HandleConnectArrowClicked(string nodeId, string direction)
    {
        if (Document is null) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || IsNodeLocked(node)) return;
        _ = OnConnectArrowClicked.InvokeAsync((nodeId, direction));
    }

    private static bool IsGroupContainer(DiagramNode node) => node.StencilId == "general.group";

    private List<DiagramNode> GetGroupMembers(string groupId) =>
        Document?.Nodes.Where(n => n.ParentGroupId == groupId).ToList() ?? [];

    private void OnResizeStart(MouseEventArgs e, string nodeId, string handle)
    {
        if (ReadOnly || Document is null) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || IsNodeLocked(node)) return;

        _resizeNodeId = nodeId;
        _resizeHandle = handle;
        _resizeStartScreen = (e.ClientX, e.ClientY);
        _resizeStartRect = (node.X, node.Y, node.W, node.H);
        _resizeGroupMemberStartRects = null;

        if (IsGroupContainer(node))
        {
            _resizeGroupMemberStartRects = [];
            foreach (var member in GetGroupMembers(nodeId))
            {
                _resizeGroupMemberStartRects[member.Id] = (member.X, member.Y, member.W, member.H);
            }
        }
    }

    private void OnRotateStartJs(MouseEventArgs e, string nodeId)
    {
        if (ReadOnly || Document is null) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || IsNodeLocked(node) || !_jsInitialized) return;
        _ = JS.InvokeVoidAsync("tmDiagramEditor.startRotate", _containerRef, nodeId, e.ClientX, e.ClientY, node.Rotation, RotateSnap);
    }

    [JSInvokable]
    public async Task OnRotateEnded(string nodeId, double angle)
    {
        if (Document is null || ReadOnly) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || IsNodeLocked(node)) return;
        var before = node.Rotation;
        node.Rotation = angle;

        if (IsGroupContainer(node))
        {
            var members = GetGroupMembers(nodeId);
            var delta = angle - before;
            var oldRotations = new Dictionary<string, double> { [nodeId] = before };
            var newRotations = new Dictionary<string, double> { [nodeId] = angle };

            foreach (var member in members)
            {
                oldRotations[member.Id] = member.Rotation;
                member.Rotation += delta;
                newRotations[member.Id] = member.Rotation;
            }

            if (CommandStack is not null && Math.Abs(delta) > 0.001)
            {
                CommandStack.Push(new RotateGroupCommand(Document, oldRotations, newRotations));
                await DocumentChanged.InvokeAsync(Document);
            }
        }
        else
        {
            if (CommandStack is not null && Math.Abs(angle - before) > 0.001)
            {
                CommandStack.Push(new RotateNodeCommand(Document, nodeId, before, angle));
                await DocumentChanged.InvokeAsync(Document);
            }
        }
    }

    private static double SnapAngle(double angle, double snap)
    {
        if (snap <= 0) return angle;
        return Math.Round(angle / snap) * snap;
    }

    private void OnCanvasMouseMove(MouseEventArgs e)
    {
        if (_resizeNodeId is null || Document is null || string.IsNullOrEmpty(_resizeHandle) || Zoom <= 0) return;

        var dx = (e.ClientX - _resizeStartScreen.X) / Zoom;
        var dy = (e.ClientY - _resizeStartScreen.Y) / Zoom;

        var (x, y, w, h) = ComputeResize(_resizeStartRect, _resizeHandle, dx, dy);
        var resizeNode = Document.Nodes.FirstOrDefault(n => n.Id == _resizeNodeId);
        if (resizeNode is null) return;

        resizeNode.X = x;
        resizeNode.Y = y;
        resizeNode.W = w;
        resizeNode.H = h;

        if (IsGroupContainer(resizeNode) && _resizeGroupMemberStartRects is not null)
        {
            var scaleX = _resizeStartRect.W > 0 ? w / _resizeStartRect.W : 1;
            var scaleY = _resizeStartRect.H > 0 ? h / _resizeStartRect.H : 1;

            foreach (var (memberId, memberStart) in _resizeGroupMemberStartRects)
            {
                var member = Document.Nodes.FirstOrDefault(n => n.Id == memberId);
                if (member is null) continue;
                var relX = memberStart.X - _resizeStartRect.X;
                var relY = memberStart.Y - _resizeStartRect.Y;
                member.X = x + relX * scaleX;
                member.Y = y + relY * scaleY;
                member.W = memberStart.W * scaleX;
                member.H = memberStart.H * scaleY;
            }
        }

        StateHasChanged();

        // Re-sync JS selection outline to follow the new bounds
        if (_jsInitialized)
        {
            _ = JS.InvokeVoidAsync("tmDiagramEditor.updateSelectionOutlines", _containerRef);
        }
    }

    private void OnCanvasMouseUp(MouseEventArgs e)
    {
        if (_resizeNodeId is null || Document is null) return;

        var node = Document.Nodes.FirstOrDefault(n => n.Id == _resizeNodeId);
        if (node is not null && CommandStack is not null)
        {
            if (Math.Abs(node.X - _resizeStartRect.X) > 0.001 || Math.Abs(node.Y - _resizeStartRect.Y) > 0.001 ||
                Math.Abs(node.W - _resizeStartRect.W) > 0.001 || Math.Abs(node.H - _resizeStartRect.H) > 0.001)
            {
                if (IsGroupContainer(node) && _resizeGroupMemberStartRects is not null)
                {
                    var oldRects = new Dictionary<string, NodeRect>
                    {
                        [node.Id] = new NodeRect(_resizeStartRect.X, _resizeStartRect.Y, _resizeStartRect.W, _resizeStartRect.H)
                    };
                    foreach (var (mid, rect) in _resizeGroupMemberStartRects)
                        oldRects[mid] = new NodeRect(rect.X, rect.Y, rect.W, rect.H);

                    var newRects = new Dictionary<string, NodeRect>
                    {
                        [node.Id] = new NodeRect(node.X, node.Y, node.W, node.H)
                    };
                    foreach (var (mid, _) in _resizeGroupMemberStartRects)
                    {
                        var m = Document.Nodes.FirstOrDefault(n => n.Id == mid);
                        if (m is not null)
                            newRects[mid] = new NodeRect(m.X, m.Y, m.W, m.H);
                    }

                    CommandStack.Push(new ResizeGroupCommand(Document, oldRects, newRects));
                }
                else
                {
                    CommandStack.Push(new ResizeNodeCommand(Document, _resizeNodeId,
                        _resizeStartRect.X, _resizeStartRect.Y, _resizeStartRect.W, _resizeStartRect.H,
                        node.X, node.Y, node.W, node.H));
                }
                _ = DocumentChanged.InvokeAsync(Document);
            }
        }

        _resizeNodeId = null;
        _resizeHandle = null;
        _resizeGroupMemberStartRects = null;
    }

    private (double X, double Y, double W, double H) ComputeResize(
        (double X, double Y, double W, double H) start, string handle, double dx, double dy)
    {
        var x = start.X;
        var y = start.Y;
        var w = start.W;
        var h = start.H;

        if (handle.Contains('e')) w = start.W + dx;
        if (handle.Contains('w')) { w = start.W - dx; x = start.X + dx; }
        if (handle.Contains('s')) h = start.H + dy;
        if (handle.Contains('n')) { h = start.H - dy; y = start.Y + dy; }

        if (w < MinNodeSize)
        {
            w = MinNodeSize;
            if (handle.Contains('w')) x = start.X + start.W - MinNodeSize;
        }
        if (h < MinNodeSize)
        {
            h = MinNodeSize;
            if (handle.Contains('n')) y = start.Y + start.H - MinNodeSize;
        }

        if (GridSize > 0)
        {
            x = Math.Round(x / GridSize) * GridSize;
            y = Math.Round(y / GridSize) * GridSize;
            w = Math.Round(w / GridSize) * GridSize;
            h = Math.Round(h / GridSize) * GridSize;
        }

        return (x, y, w, h);
    }

    /// <summary>Calls the JS Manhattan router for an edge and returns new waypoints.
    /// Returns existing waypoints unchanged when <see cref="DiagramEdge.IsManuallyRouted"/> is true.</summary>
    public async Task<List<DiagramPoint>> ComputeOrthogonalWaypointsAsync(DiagramEdge edge)
    {
        if (Document is null) return [];
        if (edge.IsManuallyRouted) return edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();

        double x1, y1, x2, y2;
        string side1, side2;
        object? srcBounds = null, tgtBounds = null;

        if (!string.IsNullOrEmpty(edge.SourceEdgeId))
        {
            var srcEdge = Document.Edges.FirstOrDefault(e => e.Id == edge.SourceEdgeId);
            if (srcEdge is null) return [];
            (x1, y1) = DiagramGeometryHelper.ComputeEdgePointAtT(Document, srcEdge, edge.SourceEdgeT ?? 0.5);
            side1 = "right";
        }
        else if (!string.IsNullOrEmpty(edge.SourceNodeId))
        {
            var srcNode = Document.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
            if (srcNode is null) return [];

            if (edge.SourceConstraint is not null)
            {
                var c = edge.SourceConstraint;
                if (c.Perimeter)
                {
                    var (px, py) = DiagramGeometryHelper.ProjectToPerimeter(srcNode.W, srcNode.H, c.RelativeX, c.RelativeY, srcNode.BackgroundShape);
                    x1 = srcNode.X + px + c.Dx;
                    y1 = srcNode.Y + py + c.Dy;
                }
                else
                {
                    x1 = srcNode.X + srcNode.W * c.RelativeX + c.Dx;
                    y1 = srcNode.Y + srcNode.H * c.RelativeY + c.Dy;
                }
                side1 = DiagramGeometryHelper.InferSideFromConstraint(c.RelativeX, c.RelativeY).ToString().ToLowerInvariant();
            }
            else
            {
                var srcPort = edge.SourcePortId is not null
                    ? srcNode.Ports.FirstOrDefault(p => p.Id == edge.SourcePortId)
                    : srcNode.Ports.FirstOrDefault();
                var sPort = srcPort ?? new DiagramPort { Side = PortSide.Right, Offset = 0.5 };
                (x1, y1) = DiagramGeometryHelper.ComputePortPosition(srcNode, sPort);
                side1 = sPort.Side.ToString().ToLowerInvariant();
            }
            srcBounds = new { x = srcNode.X, y = srcNode.Y, w = srcNode.W, h = srcNode.H, isSwimlane = srcNode.SwimlaneData is not null, isHorizontal = srcNode.SwimlaneData?.IsHorizontal ?? false };
        }
        else if (edge.SourcePoint is not null)
        {
            x1 = edge.SourcePoint.X;
            y1 = edge.SourcePoint.Y;
            side1 = "right"; // default direction for dangling source end
        }
        else
        {
            return [];
        }

        if (!string.IsNullOrEmpty(edge.TargetEdgeId))
        {
            var tgtEdge = Document.Edges.FirstOrDefault(e => e.Id == edge.TargetEdgeId);
            if (tgtEdge is null) return [];
            (x2, y2) = DiagramGeometryHelper.ComputeEdgePointAtT(Document, tgtEdge, edge.TargetEdgeT ?? 0.5);
            side2 = "left";
        }
        else if (!string.IsNullOrEmpty(edge.TargetNodeId))
        {
            var tgtNode = Document.Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);
            if (tgtNode is null) return [];

            if (edge.TargetConstraint is not null)
            {
                var c = edge.TargetConstraint;
                if (c.Perimeter)
                {
                    var (px, py) = DiagramGeometryHelper.ProjectToPerimeter(tgtNode.W, tgtNode.H, c.RelativeX, c.RelativeY, tgtNode.BackgroundShape);
                    x2 = tgtNode.X + px + c.Dx;
                    y2 = tgtNode.Y + py + c.Dy;
                }
                else
                {
                    x2 = tgtNode.X + tgtNode.W * c.RelativeX + c.Dx;
                    y2 = tgtNode.Y + tgtNode.H * c.RelativeY + c.Dy;
                }
                side2 = DiagramGeometryHelper.InferSideFromConstraint(c.RelativeX, c.RelativeY).ToString().ToLowerInvariant();
            }
            else
            {
                var tgtPort = edge.TargetPortId is not null
                    ? tgtNode.Ports.FirstOrDefault(p => p.Id == edge.TargetPortId)
                    : tgtNode.Ports.FirstOrDefault();
                var tPort = tgtPort ?? new DiagramPort { Side = PortSide.Left, Offset = 0.5 };
                (x2, y2) = DiagramGeometryHelper.ComputePortPosition(tgtNode, tPort);
                side2 = tPort.Side.ToString().ToLowerInvariant();
            }
            tgtBounds = new { x = tgtNode.X, y = tgtNode.Y, w = tgtNode.W, h = tgtNode.H, isSwimlane = tgtNode.SwimlaneData is not null, isHorizontal = tgtNode.SwimlaneData?.IsHorizontal ?? false };
        }
        else if (edge.TargetPoint is not null)
        {
            x2 = edge.TargetPoint.X;
            y2 = edge.TargetPoint.Y;
            side2 = "left"; // default direction for dangling target end
        }
        else
        {
            return [];
        }

        // Collect obstacles from visible nodes at the current group level
        var obstacles = Document.Nodes
            .Where(n => n.Id != edge.SourceNodeId && n.Id != edge.TargetNodeId)
            .Where(n => n.ParentGroupId == ActiveGroupId)
            .Select(n => new { x = n.X, y = n.Y, w = n.W, h = n.H })
            .ToList();

        var result = await JS.InvokeAsync<double[][]>("tmDiagramEditor.computeOrthogonalWaypoints", x1, y1, side1, x2, y2, side2, edge.Routing, edge.SourceSpacing ?? 0, edge.TargetSpacing ?? 0, srcBounds, tgtBounds, obstacles, edge.ElbowOrientation);
        if (result is null) return [];
        return result.Select(r => new DiagramPoint(r[0], r[1])).ToList();
    }

    private async Task RecalculateOrthogonalWaypointsForMovedNodesAsync(IEnumerable<string> nodeIds)
    {
        if (Document is null) return;
        var affectedEdges = Document.Edges
            .Where(e => e.Routing == "orthogonal" && !e.IsManuallyRouted &&
                (nodeIds.Contains(e.SourceNodeId) || nodeIds.Contains(e.TargetNodeId)))
            .ToList();

        foreach (var edge in affectedEdges)
        {
            var before = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
            var newWaypoints = await ComputeOrthogonalWaypointsAsync(edge);
            edge.Waypoints = newWaypoints;
            var after = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();

            if (CommandStack is not null && !WaypointListsEqual(before, after))
                CommandStack.Push(new UpdateEdgeWaypointsCommand(Document, edge.Id, before, after));
        }
    }

    private static bool WaypointListsEqual(List<DiagramPoint> a, List<DiagramPoint> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].X != b[i].X || a[i].Y != b[i].Y) return false;
        return true;
    }

    [JSInvokable("OnSelectionChanged")]
    public async Task JsOnSelectionChanged(string[] ids)
    {
        _currentSelectionIds = ids;
        await OnSelectionChanged.InvokeAsync(ids);
    }

    [JSInvokable]
    public async Task OnMultiSelect(string[] ids)
    {
        _currentSelectionIds = ids;
        await OnSelectionChanged.InvokeAsync(ids);
    }

    /// <summary>
    /// True when the current selection contains at least one edge and no nodes.
    /// Used to hide node connection-points while an edge is selected, so they
    /// do not occlude the inline edge toolbar.
    /// </summary>
    private bool IsOnlyEdgeSelected()
    {
        if (Document is null || _currentSelectionIds.Length == 0) return false;
        bool anyEdge = false;
        foreach (var id in _currentSelectionIds)
        {
            if (Document.Nodes.Any(n => n.Id == id)) return false;
            if (Document.Edges.Any(e => e.Id == id)) anyEdge = true;
        }
        return anyEdge;
    }

    [JSInvokable]
    public async Task OnDeleteSelected(string[] ids)
    {
        if (Document is null || ReadOnly) return;

        var targetIds = _currentSelectionIds.Length > 0 ? _currentSelectionIds : ids;
        if (targetIds.Length == 0) return;

        // If exactly one group node is selected, ungroup it instead of deleting
        if (targetIds.Length == 1)
        {
            var groupNode = Document.Nodes.FirstOrDefault(n => n.Id == targetIds[0] && n.StencilId == "general.group");
            if (groupNode is not null)
            {
                var anyLockedMember = Document.Nodes.Any(n => n.ParentGroupId == groupNode.Id && n.IsLocked);
                if (!anyLockedMember)
                {
                    if (CommandStack is not null)
                    {
                        CommandStack.Push(new UngroupNodesCommand(Document, groupNode.Id));
                    }
                    else
                    {
                        new UngroupNodesCommand(Document, groupNode.Id).Execute();
                    }

                    _currentSelectionIds = [];
                    await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, Array.Empty<string>());
                    await OnSelectionChanged.InvokeAsync([]);
                    await NotifyAndRender();
                    return;
                }
            }
        }

        var nodeIds = targetIds.Where(id => Document.Nodes.Any(n => n.Id == id && !IsNodeLocked(n))).ToArray();
        var edgeIds = targetIds.Where(id => Document.Edges.Any(e => e.Id == id)).ToArray();

        if (CommandStack is not null)
        {
            if (nodeIds.Length > 0 && edgeIds.Length > 0)
            {
                using var tx = CommandStack.TransactionScope("Delete selection");
                CommandStack.Push(new RemoveNodesCommand(Document, nodeIds));
                CommandStack.Push(new RemoveEdgesCommand(Document, edgeIds));
            }
            else if (nodeIds.Length > 0)
            {
                CommandStack.Push(new RemoveNodesCommand(Document, nodeIds));
            }
            else if (edgeIds.Length > 0)
            {
                CommandStack.Push(new RemoveEdgesCommand(Document, edgeIds));
            }
        }
        else
        {
            var nodeIdSet = nodeIds.ToHashSet();
            var edgeIdSet = edgeIds.ToHashSet();
            Document.Nodes.RemoveAll(n => nodeIdSet.Contains(n.Id));
            Document.Edges.RemoveAll(e => edgeIdSet.Contains(e.Id) || nodeIdSet.Contains(e.SourceNodeId) || nodeIdSet.Contains(e.TargetNodeId));
        }

        _currentSelectionIds = [];
        await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, Array.Empty<string>());
        await OnSelectionChanged.InvokeAsync([]);
        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnSelectAll()
    {
        if (Document is null) return;
        var ids = Document.Nodes.Select(n => n.Id).ToArray();
        _currentSelectionIds = ids;
        await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, ids);
        await OnSelectionChanged.InvokeAsync(ids);
    }

    [JSInvokable]
    public async Task OnClearSelection()
    {
        // draw.io-style cascade: if a table cell is currently sub-selected, the first
        // Escape only drops the cell selection and keeps the node selected. A second
        // Escape (when no cells are selected) then clears the node selection.
        if (SelectedTableCells is { Count: > 0 })
        {
            await OnClearTableCellSelection.InvokeAsync();
            return;
        }

        _currentSelectionIds = [];
        await OnSelectionChanged.InvokeAsync([]);
    }

    [JSInvokable("OnUndo")]
    public async Task JsOnUndo()
    {
        if (ReadOnly) return;
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

    [JSInvokable("OnRedo")]
    public async Task JsOnRedo()
    {
        if (ReadOnly) return;
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

    [JSInvokable("OnToolModeChanged")]
    public async Task JsOnToolModeChanged(string mode)
        => await OnToolModeChanged.InvokeAsync(mode);

    [JSInvokable("OnShowSearch")]
    public async Task JsOnShowSearch()
        => await OnShowSearch.InvokeAsync();

    [JSInvokable("OnShowReplace")]
    public async Task JsOnShowReplace()
        => await OnShowReplace.InvokeAsync();

    [JSInvokable("OnQuickInsert")]
    public async Task JsOnQuickInsert(string stencilId)
        => await OnQuickInsert.InvokeAsync(stencilId);

    [JSInvokable("OnNavigateToCorner")]
    public async Task JsOnNavigateToCorner(string corner)
        => await OnNavigateToCorner.InvokeAsync(corner);

    [JSInvokable("OnSwitchPage")]
    public async Task JsOnSwitchPage(int delta)
        => await OnSwitchPage.InvokeAsync(delta);

    [JSInvokable("OnGroupSelected")]
    public async Task JsOnGroupSelected()
        => await OnGroupSelected.InvokeAsync();

    [JSInvokable("OnLockSelected")]
    public async Task JsOnLockSelected()
        => await OnLockSelected.InvokeAsync();

    [JSInvokable("OnToggleBold")]
    public async Task JsOnToggleBold()
        => await OnToggleBold.InvokeAsync();

    [JSInvokable("OnToggleItalic")]
    public async Task JsOnToggleItalic()
        => await OnToggleItalic.InvokeAsync();

    [JSInvokable("OnToggleUnderline")]
    public async Task JsOnToggleUnderline()
        => await OnToggleUnderline.InvokeAsync();

    [JSInvokable("OnEnterGroup")]
    public async Task JsOnEnterGroup(string groupId)
        => await OnEnterGroup.InvokeAsync(groupId);

    [JSInvokable("OnExitGroup")]
    public async Task JsOnExitGroup()
        => await OnExitGroup.InvokeAsync();

    [JSInvokable("OnNodeContextMenu")]
    public async Task JsOnNodeContextMenu(string nodeId, double screenX, double screenY)
        => await OnNodeContextMenu.InvokeAsync((nodeId, screenX, screenY));

    [JSInvokable("OnEdgeContextMenu")]
    public async Task JsOnEdgeContextMenu(string edgeId, double screenX, double screenY)
        => await OnEdgeContextMenu.InvokeAsync((edgeId, screenX, screenY));

    [JSInvokable("OnTableCellContextMenu")]
    public async Task JsOnTableCellContextMenu(string nodeId, int row, int column, double screenX, double screenY)
        => await OnTableCellContextMenu.InvokeAsync((nodeId, row, column, screenX, screenY));

    /// <summary>Invoked from JS when the user left-clicks (without drag) on a table cell
    /// of an already-selected node (drill-in pattern).</summary>
    [JSInvokable("OnTableCellLeftClick")]
    public async Task JsOnTableCellLeftClick(string nodeId, int row, int column, bool isCtrlHeld)
        => await OnTableCellSelect.InvokeAsync((nodeId, row, column, isCtrlHeld));

    [JSInvokable("OnCanvasContextMenu")]
    public async Task JsOnCanvasContextMenu(double canvasX, double canvasY, double screenX, double screenY)
        => await OnCanvasContextMenu.InvokeAsync((canvasX, canvasY, screenX, screenY));

    [JSInvokable("CloseContextMenu")]
    public async Task JsOnCloseContextMenu()
        => await OnCloseContextMenu.InvokeAsync();

    [JSInvokable("OnNodeLinkClicked")]
    public async Task JsOnNodeLinkClicked(string nodeId, string link)
        => await OnNodeLinkClicked.InvokeAsync((nodeId, link));

    [JSInvokable("OnRulerCursorMoved")]
    public async Task JsOnRulerCursorMoved(double x, double y)
        => await OnRulerCursorMoved.InvokeAsync((x, y));

    [JSInvokable]
    public async Task OnZoomChanged(double scale)
    {
        OnZoomChangedInternal(scale);
        await ZoomChanged.InvokeAsync(scale);
    }

    [JSInvokable]
    public void OnViewBoxChanged(double x, double y, double w, double h)
    {
        _viewBox = $"{F(x)} {F(y)} {F(w)} {F(h)}";
    }

    [JSInvokable]
    public async Task OnViewportChanged(double x, double y, double w, double h)
    {
        _viewportX = x;
        _viewportY = y;
        _viewportW = w;
        _viewportH = h;
        _viewportInitialized = true;
        await ViewportChanged.InvokeAsync(new DiagramMinimapViewport(x, y, w, h));
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Returns the current canvas viewport in document-space coordinates,
    /// or a zeroed tuple if JS hasn't initialised it yet (e.g. in bUnit).</summary>
    public (double X, double Y, double W, double H) GetViewport()
        => _viewportInitialized
            ? (_viewportX, _viewportY, _viewportW, _viewportH)
            : (0, 0, 0, 0);

    private bool IsNodeVisible(DiagramNode node)
    {
        bool inViewport = node.X + node.W >= _viewportX - ViewportMargin
            && node.Y + node.H >= _viewportY - ViewportMargin
            && node.X <= _viewportX + _viewportW + ViewportMargin
            && node.Y <= _viewportY + _viewportH + ViewportMargin;

        if (!inViewport) return false;
        if (Document is null) return true;

        // Filter by active group
        if (node.ParentGroupId != ActiveGroupId)
        {
            // At root level, show grouped nodes whose container is also at root level
            // (visual grouping: nodes remain visible inside their group container)
            if (ActiveGroupId == null && !string.IsNullOrEmpty(node.ParentGroupId))
            {
                var container = Document.Nodes.FirstOrDefault(n => n.Id == node.ParentGroupId);
                if (container?.ParentGroupId == null)
                    return true;
            }
            return false;
        }

        // Hide children of collapsed containers
        var current = node;
        while (!string.IsNullOrEmpty(current.ParentNodeId))
        {
            var parent = Document.Nodes.FirstOrDefault(n => n.Id == current.ParentNodeId);
            if (parent is { IsCollapsible: true, Collapsed: true })
                return false;
            current = parent;
            if (current is null) break;
        }

        // Hide grouped nodes inside a collapsed container
        if (!string.IsNullOrEmpty(node.GroupId))
        {
            var collapsedContainer = Document.Nodes.FirstOrDefault(n =>
                n.IsCollapsible && n.Collapsed
                && n.GroupId == node.GroupId
                && node.X >= n.X && node.Y >= n.Y
                && node.X + node.W <= n.X + n.W
                && node.Y + node.H <= n.Y + n.H);
            if (collapsedContainer is not null) return false;
        }

        return true;
    }

    private bool IsLayerVisible(DiagramNode node)
    {
        if (node.LayerId is null) return true;
        var layer = Document?.Layers.FirstOrDefault(l => l.Id == node.LayerId);
        return layer?.IsVisible ?? true;
    }

    private bool IsLayerVisible(DiagramEdge edge)
    {
        if (edge.LayerId is null) return true;
        var layer = Document?.Layers.FirstOrDefault(l => l.Id == edge.LayerId);
        return layer?.IsVisible ?? true;
    }

    private bool IsNodeLocked(DiagramNode node)
    {
        if (node.IsLocked) return true;
        var layerId = node.LayerId;
        if (layerId is null && Document?.Layers.Count > 0)
            layerId = Document.Layers.OrderBy(l => l.Order).First().Id;
        var layer = Document?.Layers.FirstOrDefault(l => l.Id == layerId);
        return layer?.IsLocked ?? false;
    }

    [JSInvokable]
    public async Task OnDuplicate(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        var maxZ = Document.Nodes.Count > 0 ? Document.Nodes.Max(n => n.ZIndex) : 0;
        var offset = GridSize > 0 ? GridSize * 2 : 16;
        var added = new List<string>();

        foreach (var id in ids)
        {
            var src = Document.Nodes.FirstOrDefault(n => n.Id == id);
            if (src is null || IsNodeLocked(src)) continue;
            var copy = DeepCopyNode(src);
            copy.X += offset; copy.Y += offset;
            copy.ZIndex = ++maxZ;

            if (CommandStack is not null)
                CommandStack.Push(new AddNodeCommand(Document, copy));
            else
                Document.Nodes.Add(copy);

            added.Add(copy.Id);
        }

        if (added.Count == 0) return;
        await NotifyAndRender();
        await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, added.ToArray());
        await OnSelectionChanged.InvokeAsync(added.ToArray());
    }

    [JSInvokable]
    public async Task OnCopy(string[] ids)
    {
        if (Document is null || ReadOnly) return;
        var cmd = new CopyNodesCommand(Document, ids);
        cmd.Execute();
        if (!string.IsNullOrEmpty(CopyNodesCommand.SharedClipboardJson))
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "tm-diagram-clipboard", CopyNodesCommand.SharedClipboardJson);
        }
    }

    [JSInvokable]
    public async Task OnPaste()
    {
        if (Document is null || ReadOnly) return;
        if (string.IsNullOrEmpty(CopyNodesCommand.SharedClipboardJson))
        {
            var stored = await JS.InvokeAsync<string?>("localStorage.getItem", "tm-diagram-clipboard");
            if (!string.IsNullOrEmpty(stored))
                CopyNodesCommand.SharedClipboardJson = stored;
        }
        if (CommandStack is not null)
            CommandStack.Push(new PasteNodesCommand(Document, CommandStack, JS, _containerRef, ActiveGroupId));
        else
        {
            var cmd = new PasteNodesCommand(Document, null, JS, _containerRef, ActiveGroupId);
            cmd.Execute();
        }
        await NotifyAndRender();
        var pastedIds = PasteNodesCommand.LastPastedNodeIds;
        if (pastedIds.Count > 0)
        {
            await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, pastedIds.ToArray());
            await OnSelectionChanged.InvokeAsync(pastedIds.ToArray());
        }
    }

    [JSInvokable]
    public async Task OnCopyStyle()
    {
        if (Document is null || ReadOnly || _currentSelectionIds.Length == 0) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == _currentSelectionIds[0]);
        if (node is null) return;
        new CopyStyleCommand(node, includeSize: false).Execute();
    }

    [JSInvokable]
    public async Task OnPasteStyle()
    {
        if (Document is null || ReadOnly || _currentSelectionIds.Length == 0) return;
        if (CommandStack is not null)
            CommandStack.Push(new PasteStyleCommand(Document, _currentSelectionIds));
        else
            new PasteStyleCommand(Document, _currentSelectionIds).Execute();
        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnPasteSize()
    {
        if (Document is null || ReadOnly || _currentSelectionIds.Length == 0) return;
        if (CommandStack is not null)
            CommandStack.Push(new PasteSizeCommand(Document, _currentSelectionIds));
        else
            new PasteSizeCommand(Document, _currentSelectionIds).Execute();
        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnLayoutApplied(ElementMove[] moves)
    {
        if (Document is null || ReadOnly || moves.Length == 0) return;
        var dict = moves.ToDictionary(m => m.Id, m => (m.X, m.Y));
        if (CommandStack is not null)
            CommandStack.Push(new ApplyLayoutCommand(Document, dict));
        else
        {
            var cmd = new ApplyLayoutCommand(Document, dict);
            cmd.Execute();
        }
        await NotifyAndRender();
        await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, moves.Select(m => m.Id).ToArray());
        await OnSelectionChanged.InvokeAsync(moves.Select(m => m.Id).ToArray());
    }

    public async Task RunLayoutAsync(string algorithm = "dagre", string direction = "TB")
    {
        if (Document is null || ReadOnly || _currentSelectionIds.Length == 0) return;
        var nodeIds = _currentSelectionIds.Where(id => Document.Nodes.Any(n => n.Id == id)).ToList();
        if (nodeIds.Count == 0) return;

        var nodes = nodeIds.Select(id =>
        {
            var n = Document.Nodes.First(x => x.Id == id);
            return new { id = n.Id, width = n.W, height = n.H, gridRow = n.Data.TryGetValue("gridRow", out var gr) ? gr : null, gridColumn = n.Data.TryGetValue("gridColumn", out var gc) ? gc : null };
        }).ToList();

        var edges = Document.Edges
            .Where(e => nodeIds.Contains(e.SourceNodeId) && nodeIds.Contains(e.TargetNodeId))
            .Select(e => new { source = e.SourceNodeId, target = e.TargetNodeId })
            .ToList();

        ElementMove[]? result = null;
        switch (algorithm)
        {
            case "dagre":
                result = await JS.InvokeAsync<ElementMove[]?>("tmDiagramEditor.runDagreLayout", _containerRef, nodes, edges, direction);
                break;
            case "tree":
                result = await JS.InvokeAsync<ElementMove[]?>("tmDiagramEditor.runTreeLayout", _containerRef, nodes, edges, direction);
                break;
            case "force":
                result = await JS.InvokeAsync<ElementMove[]?>("tmDiagramEditor.runForceLayout", _containerRef, nodes, edges, new { width = Document.Width, height = Document.Height });
                break;
            case "circle":
                result = await JS.InvokeAsync<ElementMove[]?>("tmDiagramEditor.runCircleLayout", _containerRef, nodes, new { });
                break;
            case "grid":
                result = await JS.InvokeAsync<ElementMove[]?>("tmDiagramEditor.runGridLayout", _containerRef, nodes, new { });
                break;
        }

        if (result is not null)
            await OnLayoutApplied(result);
    }

    // ── Drag & Drop from toolbox ─────────────────────────────────────────────

    [JSInvokable]
    public async Task OnDropFromToolbox(string stencilId, double x, double y)
    {
        if (ReadOnly || Document is null) return;
        if (string.IsNullOrWhiteSpace(stencilId)) return;

        x = Math.Round(x / GridSize) * GridSize;
        y = Math.Round(y / GridSize) * GridSize;

        await OnToolboxDrop.InvokeAsync((StencilId: stencilId, X: x, Y: y));
    }

    [Parameter] public EventCallback<(string StencilId, double X, double Y)> OnToolboxDrop { get; set; }

    [Parameter] public EventCallback<(string? SourceNodeId, string? SourcePortId, string? TargetNodeId, string? TargetPortId, string? SourceSide, double SourceOffset, string? TargetSide, double TargetOffset, string? TargetEdgeId, double TargetEdgeT, double? SourceConstraintRx, double? SourceConstraintRy, bool? SourceConstraintPerimeter, double? TargetConstraintRx, double? TargetConstraintRy, bool? TargetConstraintPerimeter, double? TargetPointX, double? TargetPointY, double? SourcePointX, double? SourcePointY, double[]? WaypointsXY)> OnEdgeCreated { get; set; }

    // ── Port interactions ────────────────────────────────────────────────────

    private async Task HandlePortMouseDown(string nodeId, string portId)
    {
        if (ReadOnly) return;
        await OnPortMouseDownEvent.InvokeAsync((nodeId, portId));
    }

    [Parameter] public EventCallback<(string NodeId, string PortId)> OnPortMouseDownEvent { get; set; }

    private void HandlePortMouseEnter(string nodeId, string portId) { }
    private void HandlePortMouseLeave(string nodeId, string portId) { }

    /// <summary>
    /// Computes the absolute position of a stencil connection point relative to the node's top-left corner.
    /// When <paramref name="cp.Perimeter"/> is true, the point is projected onto the node perimeter.
    /// </summary>
    private static (double Px, double Py) GetConnectionPointPosition(DiagramNode node, DiagramStencilConnectionPoint cp)
    {
        if (cp.Perimeter)
        {
            var (px, py) = DiagramGeometryHelper.ProjectToPerimeter(node.W, node.H, cp.RelativeX, cp.RelativeY, node.BackgroundShape);
            return (px, py);
        }
        return (node.W * cp.RelativeX, node.H * cp.RelativeY);
    }

    [JSInvokable]
    public async Task JsOnEdgeCreated(
        string? sourceNodeId, string? sourcePortId, string? targetNodeId, string? targetPortId,
        string? sourceSide = null, double sourceOffset = 0.5, string? targetSide = null, double targetOffset = 0.5,
        string? targetEdgeId = null, double targetEdgeT = 0.5,
        double? sourceConstraintRx = null, double? sourceConstraintRy = null, bool? sourceConstraintPerimeter = null,
        double? targetConstraintRx = null, double? targetConstraintRy = null, bool? targetConstraintPerimeter = null,
        double? targetPointX = null, double? targetPointY = null,
        double? sourcePointX = null, double? sourcePointY = null,
        double[]? waypointsXY = null)
    {
        if (ReadOnly || Document is null) return;

        if (GridSize > 0)
        {
            if (targetPointX is not null && targetPointY is not null)
            {
                targetPointX = Math.Round(targetPointX.Value / GridSize) * GridSize;
                targetPointY = Math.Round(targetPointY.Value / GridSize) * GridSize;
            }

            if (sourcePointX is not null && sourcePointY is not null)
            {
                sourcePointX = Math.Round(sourcePointX.Value / GridSize) * GridSize;
                sourcePointY = Math.Round(sourcePointY.Value / GridSize) * GridSize;
            }

            if (waypointsXY is not null)
            {
                for (var i = 0; i + 1 < waypointsXY.Length; i += 2)
                {
                    waypointsXY[i] = Math.Round(waypointsXY[i] / GridSize) * GridSize;
                    waypointsXY[i + 1] = Math.Round(waypointsXY[i + 1] / GridSize) * GridSize;
                }
            }
        }

        await OnEdgeCreated.InvokeAsync((sourceNodeId, sourcePortId, targetNodeId, targetPortId, sourceSide, sourceOffset, targetSide, targetOffset, targetEdgeId, targetEdgeT,
            sourceConstraintRx, sourceConstraintRy, sourceConstraintPerimeter,
            targetConstraintRx, targetConstraintRy, targetConstraintPerimeter,
            targetPointX, targetPointY,
            sourcePointX, sourcePointY,
            waypointsXY));
    }

    // ── Edge interactions ────────────────────────────────────────────────────

    private async Task OnEdgeClicked(string edgeId, MouseEventArgs e)
    {
        if (e.CtrlKey || e.MetaKey)
        {
            var current = _currentSelectionIds.ToList();
            if (current.Contains(edgeId))
                current.Remove(edgeId);
            else
                current.Add(edgeId);
            _currentSelectionIds = current.ToArray();
        }
        else
        {
            _currentSelectionIds = [edgeId];
            await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, Array.Empty<string>());
        }
        await OnSelectionChanged.InvokeAsync(_currentSelectionIds);
        await InvokeAsync(StateHasChanged);
        await OnSelectionChanged.InvokeAsync(_currentSelectionIds);
    }

    private async Task OnEdgeDoubleClicked(string edgeId, MouseEventArgs e)
    {
        if (ReadOnly || Document is null) return;
        var pt = await JS.InvokeAsync<DocPoint>("tmDiagramEditor.screenToDoc", _containerRef, e.ClientX, e.ClientY);

        // Find closest segment and insert waypoint
        var edge = Document.Edges.FirstOrDefault(ed => ed.Id == edgeId);
        if (edge is null) return;

        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        if (pts.Length < 2) return;

        int bestIndex = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var dist = DistancePointToSegment(pt.X, pt.Y, pts[i], pts[i + 1]);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        var insertIndex = bestIndex + 1;
        if (GridSize > 0)
        {
            pt.X = Math.Round(pt.X / GridSize) * GridSize;
            pt.Y = Math.Round(pt.Y / GridSize) * GridSize;
        }

        if (CommandStack is not null)
            CommandStack.Push(new InsertEdgeWaypointCommand(Document, edgeId, insertIndex, new DiagramPoint(pt.X, pt.Y)));
        else
            edge.Waypoints.Insert(insertIndex, new DiagramPoint(pt.X, pt.Y));

        _currentSelectionIds = [edgeId];
        await OnSelectionChanged.InvokeAsync(_currentSelectionIds);
        await NotifyAndRender();
    }

    private static double DistancePointToSegment(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Math.Sqrt((px - a.X) * (px - a.X) + (py - a.Y) * (py - a.Y));
        var t = ((px - a.X) * dx + (py - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Max(0, Math.Min(1, t));
        var projX = a.X + t * dx;
        var projY = a.Y + t * dy;
        return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
    }

    private class DocPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    private async Task OnEdgeWaypointDoubleClick(string edgeId, int handleIndex)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        // Elbow flip: double-clicking the single bend handle flips orientation
        if (edge.Routing == "elbow" && edge.Waypoints.Count > 0)
        {
            var oldOrientation = edge.ElbowOrientation;
            var newOrientation = oldOrientation == "horizontal" ? "vertical" : "horizontal";

            // Temporarily set orientation to compute new waypoints
            edge.ElbowOrientation = newOrientation;
            var newWaypoints = await ComputeOrthogonalWaypointsAsync(edge);
            edge.ElbowOrientation = oldOrientation; // restore for command

            if (CommandStack is not null)
                CommandStack.Push(new FlipElbowOrientationCommand(edge, newOrientation, newWaypoints));
            else
            {
                edge.ElbowOrientation = newOrientation;
                edge.Waypoints.Clear();
                foreach (var wp in newWaypoints)
                    edge.Waypoints.Add(wp);
            }
            await NotifyAndRender();
        }
        else
        {
            // Fallback to delete for other routing types
            await OnEdgeWaypointDelete(edgeId, handleIndex);
        }
    }

    private async Task OnEdgeWaypointDelete(string edgeId, int waypointIndex)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;
        var actualIndex = waypointIndex - 1;
        if (actualIndex < 0 || actualIndex >= edge.Waypoints.Count) return;

        var removed = edge.Waypoints[actualIndex];
        if (CommandStack is not null)
            CommandStack.Push(new DeleteEdgeWaypointCommand(Document, edgeId, actualIndex, removed));
        else
            edge.Waypoints.RemoveAt(actualIndex);

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task<int> OnVirtualBendInsert(string edgeId, int segmentIndex, double x, double y)
    {
        if (ReadOnly || Document is null) return -1;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return -1;

        var insertIndex = segmentIndex + 1;
        if (GridSize > 0)
        {
            x = Math.Round(x / GridSize) * GridSize;
            y = Math.Round(y / GridSize) * GridSize;
        }

        if (CommandStack is not null)
        {
            CommandStack.BeginTransaction(Loc["TmDiagramEditor_VirtualBend"]);
            CommandStack.Push(new InsertEdgeWaypointCommand(Document, edgeId, insertIndex, new DiagramPoint(x, y)));
        }
        else
        {
            edge.Waypoints.Insert(insertIndex, new DiagramPoint(x, y));
        }

        _pendingVirtualBend = (edgeId, insertIndex - 1);
        _currentSelectionIds = [edgeId];
        await OnSelectionChanged.InvokeAsync(_currentSelectionIds);
        await NotifyAndRender();

        return insertIndex - 1; // return as 0-based waypoint index for JS
    }

    [JSInvokable]
    public async Task OnEdgeSegmentDragged(string edgeId, List<DiagramPoint> waypoints)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        var before = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
        edge.Waypoints = waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
        edge.IsManuallyRouted = true;
        var after = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();

        if (CommandStack is not null)
            CommandStack.Push(new UpdateEdgeSegmentOffsetCommand(Document, edgeId, before, after));

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnDanglingTerminalMoved(string edgeId, string type, double x, double y)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        if (GridSize > 0)
        {
            x = Math.Round(x / GridSize) * GridSize;
            y = Math.Round(y / GridSize) * GridSize;
        }

        var isSource = type == "source";
        var oldNodeId = isSource ? edge.SourceNodeId : edge.TargetNodeId;
        var oldPortId = isSource ? edge.SourcePortId : edge.TargetPortId;
        var oldPoint = isSource
            ? (edge.SourcePoint is not null ? new DiagramPoint(edge.SourcePoint.X, edge.SourcePoint.Y) : null)
            : (edge.TargetPoint is not null ? new DiagramPoint(edge.TargetPoint.X, edge.TargetPoint.Y) : null);
        var oldConstraint = isSource ? edge.SourceConstraint?.Clone() : edge.TargetConstraint?.Clone();

        if (isSource)
        {
            edge.SourceNodeId = null;
            edge.SourcePortId = null;
            edge.SourcePoint = new DiagramPoint(x, y);
            edge.SourceConstraint = null;
        }
        else
        {
            edge.TargetNodeId = null;
            edge.TargetPortId = null;
            edge.TargetPoint = new DiagramPoint(x, y);
            edge.TargetConstraint = null;
        }

        if (CommandStack is not null)
        {
            CommandStack.Push(new UpdateEdgeTerminalCommand(
                Document, edgeId, isSource,
                oldNodeId, oldPortId, oldPoint, oldConstraint,
                null, null, new DiagramPoint(x, y), null));
        }

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnEdgeTerminalReconnected(string edgeId, string type, string nodeId, string? portId)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        var isSource = type == "source";
        var oldNodeId = isSource ? edge.SourceNodeId : edge.TargetNodeId;
        var oldPortId = isSource ? edge.SourcePortId : edge.TargetPortId;
        var oldPoint = isSource
            ? (edge.SourcePoint is not null ? new DiagramPoint(edge.SourcePoint.X, edge.SourcePoint.Y) : null)
            : (edge.TargetPoint is not null ? new DiagramPoint(edge.TargetPoint.X, edge.TargetPoint.Y) : null);
        var oldConstraint = isSource ? edge.SourceConstraint?.Clone() : edge.TargetConstraint?.Clone();

        if (isSource)
        {
            edge.SourceNodeId = nodeId;
            edge.SourcePortId = portId;
            edge.SourcePoint = null;
            edge.SourceConstraint = null;
        }
        else
        {
            edge.TargetNodeId = nodeId;
            edge.TargetPortId = portId;
            edge.TargetPoint = null;
            edge.TargetConstraint = null;
        }

        if (CommandStack is not null)
        {
            CommandStack.Push(new UpdateEdgeTerminalCommand(
                Document, edgeId, isSource,
                oldNodeId, oldPortId, oldPoint, oldConstraint,
                nodeId, portId, null, null));
        }

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnEdgeTerminalOutlineConnected(string edgeId, string type, string nodeId, double relativeX, double relativeY)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        var isSource = type == "source";
        var oldNodeId = isSource ? edge.SourceNodeId : edge.TargetNodeId;
        var oldPortId = isSource ? edge.SourcePortId : edge.TargetPortId;
        var oldPoint = isSource
            ? (edge.SourcePoint is not null ? new DiagramPoint(edge.SourcePoint.X, edge.SourcePoint.Y) : null)
            : (edge.TargetPoint is not null ? new DiagramPoint(edge.TargetPoint.X, edge.TargetPoint.Y) : null);
        var oldConstraint = isSource ? edge.SourceConstraint?.Clone() : edge.TargetConstraint?.Clone();

        var newConstraint = new DiagramConnectionConstraint
        {
            RelativeX = relativeX,
            RelativeY = relativeY,
            Perimeter = false
        };

        if (isSource)
        {
            edge.SourceNodeId = nodeId;
            edge.SourcePortId = null;
            edge.SourcePoint = null;
            edge.SourceConstraint = newConstraint;
        }
        else
        {
            edge.TargetNodeId = nodeId;
            edge.TargetPortId = null;
            edge.TargetPoint = null;
            edge.TargetConstraint = newConstraint;
        }

        if (CommandStack is not null)
        {
            CommandStack.Push(new UpdateEdgeTerminalCommand(
                Document, edgeId, isSource,
                oldNodeId, oldPortId, oldPoint, oldConstraint,
                nodeId, null, null, newConstraint));
        }

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnWholeEdgeDragged(string edgeId, double dx, double dy)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        if (CommandStack is not null)
            CommandStack.Push(new DragWholeEdgeCommand(Document, edgeId, dx, dy));
        else
        {
            // Direct execution when no command stack is available
            var cmd = new DragWholeEdgeCommand(Document, edgeId, dx, dy);
            cmd.Execute();
        }

        await NotifyAndRender();
    }

    public async Task OnEdgeToolbarAction(string edgeId, string action)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        switch (action)
        {
            case "flip":
                {
                    // Reverse waypoints so the geometric path flows the other way.
                    // For orthogonal/elbow/segment edges the waypoints will be recomputed
                    // automatically the next time a connected node moves.
                    var newWaypoints = edge.Waypoints.AsEnumerable().Reverse().Select(p => new DiagramPoint(p.X, p.Y)).ToList();

                    if (CommandStack is not null)
                        CommandStack.Push(new ReverseEdgeCommand(edge, newWaypoints));
                    else
                    {
                        var cmd = new ReverseEdgeCommand(edge, newWaypoints);
                        cmd.Execute();
                    }
                }
                break;

            case "clearWaypoints":
                if (CommandStack is not null)
                    CommandStack.Push(new ResetEdgeRoutingCommand(Document, edgeId));
                else
                {
                    edge.IsManuallyRouted = false;
                    edge.Waypoints.Clear();
                }
                break;

            case "straight":
            case "orthogonal":
            case "elbow":
            case "segment":
            case "curved":
                {
                    var oldRouting = edge.Routing;
                    edge.Routing = action;
                    List<DiagramPoint> oldWaypoints = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
                    if (action is "orthogonal" or "elbow" or "segment")
                    {
                        edge.Waypoints = await ComputeOrthogonalWaypointsAsync(edge);
                    }
                    else
                    {
                        edge.Waypoints.Clear();
                    }
                    var newWaypoints = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
                    if (CommandStack is not null)
                        CommandStack.Push(new UpdateEdgeRoutingCommand(Document, edgeId, oldRouting, action, oldWaypoints, newWaypoints));
                }
                break;
        }

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnEdgeWaypointMoved(string edgeId, int waypointIndex, double x, double y)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null || waypointIndex < 0 || waypointIndex >= edge.Waypoints.Count) return;

        var wasVirtualBend = _pendingVirtualBend is { EdgeId: var vbEdge, WaypointIndex: var vbWp }
            && vbEdge == edgeId && vbWp == waypointIndex;

        // Smart removal: if dropped onto a straight line between neighbours, delete it
        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        var actualIndex = waypointIndex + 1; // +1 because GetEdgePoints includes source at 0
        if (actualIndex > 0 && actualIndex < pts.Length - 1)
        {
            var prev = pts[actualIndex - 1];
            var curr = (x, y);
            var next = pts[actualIndex + 1];
            if (DiagramGeometryHelper.IsCollinear(prev, curr, next))
            {
                var removed = edge.Waypoints[waypointIndex];
                if (CommandStack is not null)
                    CommandStack.Push(new DeleteEdgeWaypointCommand(Document, edgeId, waypointIndex, removed));
                else
                    edge.Waypoints.RemoveAt(waypointIndex);
                _pendingVirtualBend = null;
                if (wasVirtualBend && CommandStack?.IsInTransaction == true)
                    CommandStack.CommitTransaction();
                await NotifyAndRender();
                return;
            }
        }

        // Merge removal: if dropped onto another waypoint (within 5px), delete it
        for (int i = 0; i < edge.Waypoints.Count; i++)
        {
            if (i == waypointIndex) continue;
            var other = edge.Waypoints[i];
            var dx = other.X - x;
            var dy = other.Y - y;
            if (dx * dx + dy * dy < 25) // 5px tolerance squared
            {
                var removed = edge.Waypoints[waypointIndex];
                if (CommandStack is not null)
                    CommandStack.Push(new DeleteEdgeWaypointCommand(Document, edgeId, waypointIndex, removed));
                else
                    edge.Waypoints.RemoveAt(waypointIndex);
                _pendingVirtualBend = null;
                if (wasVirtualBend && CommandStack?.IsInTransaction == true)
                    CommandStack.CommitTransaction();
                await NotifyAndRender();
                return;
            }
        }

        var before = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
        edge.Waypoints[waypointIndex] = new DiagramPoint(x, y);
        var after = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();

        if (CommandStack is not null)
            CommandStack.Push(new UpdateEdgeWaypointsCommand(Document, edgeId, before, after));

        _pendingVirtualBend = null;
        if (wasVirtualBend && CommandStack?.IsInTransaction == true)
            CommandStack.CommitTransaction();

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnCancelEdgeEdit()
    {
        if (CommandStack?.IsInTransaction == true)
        {
            CommandStack.RollbackTransaction();
        }
        _pendingVirtualBend = null;
        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnEdgeSpacingChanged(string edgeId, double? sourceSpacing, double? targetSpacing)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        if (CommandStack is not null)
            CommandStack.Push(new UpdateEdgeSpacingCommand(Document, edgeId, edge.SourceSpacing, sourceSpacing, edge.TargetSpacing, targetSpacing));
        else
        {
            if (sourceSpacing.HasValue) edge.SourceSpacing = sourceSpacing.Value;
            if (targetSpacing.HasValue) edge.TargetSpacing = targetSpacing.Value;
        }

        await NotifyAndRender();
    }

    [JSInvokable]
    public async Task OnEdgeLabelMoved(string edgeId, double t, double offsetX, double offsetY)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;

        var clampedT = Math.Clamp(t, 0, 1);
        if (CommandStack is not null)
            CommandStack.Push(new UpdateEdgeLabelPositionCommand(edge, clampedT, offsetX, offsetY));
        else
        {
            edge.LabelPositionT = clampedT;
            edge.LabelOffsetX = offsetX;
            edge.LabelOffsetY = offsetY;
        }

        await NotifyAndRender();
    }

    private async Task ToggleCollapse(string nodeId)
    {
        if (Document is null || ReadOnly) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is not { IsCollapsible: true }) return;

        if (CommandStack is not null)
            CommandStack.Push(new ToggleCollapseCommand(Document, nodeId));
        else
            new ToggleCollapseCommand(Document, nodeId).Execute();

        await NotifyAndRender();
    }

    private async Task OnEdgeLabelSelect(string edgeId)
    {
        _currentSelectionIds = [edgeId];
        await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, Array.Empty<string>());
        await OnSelectionChanged.InvokeAsync(_currentSelectionIds);
    }

    private void OnEdgeLabelEdit(string edgeId)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;
        _editingEdgeLabelId = edgeId;
        _editingLabelValue = edge.Label ?? "";
    }

    /// <summary>Programmatically starts editing an edge label.</summary>
    public void StartEdgeLabelEdit(string edgeId)
    {
        if (ReadOnly || Document is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;
        _editingEdgeLabelId = edgeId;
        _editingLabelValue = edge.Label ?? "";
        StateHasChanged();
    }

    private void OnLabelInputChanged(ChangeEventArgs e)
    {
        _editingLabelValue = e.Value?.ToString() ?? "";
    }

    private async Task OnLabelInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await SaveEdgeLabel();
        else if (e.Key == "Escape")
            CancelEdgeLabelEdit();
    }

    private async Task OnLabelInputBlur()
    {
        await SaveEdgeLabel();
    }

    private async Task SaveEdgeLabel()
    {
        if (Document is null || _editingEdgeLabelId is null) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == _editingEdgeLabelId);
        if (edge is null)
        {
            CancelEdgeLabelEdit();
            return;
        }

        var newLabel = _editingLabelValue.Trim();
        if (newLabel != (edge.Label ?? ""))
        {
            if (CommandStack is not null)
                CommandStack.Push(new UpdateEdgeLabelCommand(Document, edge.Id, edge.Label, newLabel));
            else
                edge.Label = newLabel;
        }

        CancelEdgeLabelEdit();
        await NotifyAndRender();
    }

    private void CancelEdgeLabelEdit()
    {
        _editingEdgeLabelId = null;
        _editingLabelValue = "";
    }

    private async Task HandleSectionEdit(string nodeId, string dataKey, object value)
    {
        if (Document is null) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;
        var oldData = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(node.Data)) ?? new Dictionary<string, object>();
        var newData = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(node.Data)) ?? new Dictionary<string, object>();
        newData[dataKey] = value;

        var oldHeight = node.H;
        var newHeight = ComputeUmlAutoHeight(node, newData);
        var shouldResize = node.StencilId?.StartsWith("uml.", StringComparison.OrdinalIgnoreCase) == true
            && Math.Abs(newHeight - oldHeight) > 2;

        if (CommandStack is not null)
        {
            using var tx = CommandStack.TransactionScope("Edit section");
            CommandStack.Push(new UpdateNodeDataCommand(Document, nodeId, oldData, newData));
            if (shouldResize)
            {
                CommandStack.Push(new ResizeNodeCommand(Document, nodeId,
                    node.X, node.Y, node.W, oldHeight,
                    node.X, node.Y, node.W, newHeight));
            }
        }
        else
        {
            node.Data = newData;
            if (shouldResize)
            {
                node.H = newHeight;
            }
        }
        await NotifyAndRender();
    }

    private static double ComputeUmlAutoHeight(DiagramNode node, Dictionary<string, object> data)
    {
        var stencilId = node.StencilId?.ToLowerInvariant() ?? "";
        if (!stencilId.StartsWith("uml.")) return node.H;

        double headerHeight = stencilId switch
        {
            "uml.abstract-class" or "uml.component" => 38, // stereotype + name
            _ => 30 // name only
        };

        double h = headerHeight;
        bool hasListSection = false;

        foreach (var key in new[] { "attributes", "methods", "values" })
        {
            if (!data.TryGetValue(key, out var listValue)) continue;
            var count = CountListItems(listValue);
            hasListSection = true;
            h += 1; // divider
            h += 16 + Math.Max(1, count) * 18; // padding + rows (min 1 row for empty section)
        }

        if (!hasListSection)
            h += 20;

        var minHeight = stencilId switch
        {
            "uml.class" or "uml.abstract-class" or "uml.component" => 140,
            "uml.enum" => 120,
            _ => 100
        };

        return Math.Max(minHeight, h);
    }

    private static int CountListItems(object? value)
    {
        if (value is null) return 0;
        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
                return je.EnumerateArray().Count(e => !string.IsNullOrWhiteSpace(e.ToString()));
            var s = je.ToString();
            return string.IsNullOrWhiteSpace(s) ? 0 : 1;
        }
        if (value is System.Collections.Generic.IEnumerable<string> strList)
            return strList.Count(s => !string.IsNullOrWhiteSpace(s));
        if (value is System.Collections.IEnumerable enumerable && value is not string)
            return enumerable.Cast<object>().Count(o => o is not null && !string.IsNullOrWhiteSpace(o.ToString()));
        if (value is string str)
            return str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                      .Count(l => !string.IsNullOrWhiteSpace(l));
        return 1;
    }

    private (double X, double Y) ComputeEdgePointAtT(DiagramEdge edge, double t)
        => Document is null ? (0, 0) : DiagramGeometryHelper.ComputeEdgePointAtT(Document, edge, t);

    private (double X, double Y) ComputeEdgeMidpoint(DiagramEdge edge) => ComputeEdgePointAtT(edge, 0.5);

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task SetZoom(double scale)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.zoomTo", _containerRef, scale);
    }

    public async Task FitToView()
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.fitToView", _containerRef, 40);
        if (ShowPageView)
            await JS.InvokeVoidAsync("tmDiagramEditor.addPageViewMargin", _containerRef, PageViewMargin);
    }

    public async Task SetSelection(params string[] ids)
    {
        _currentSelectionIds = ids;
        if (_jsInitialized)
        {
            var nodeIds = Document is null
                ? ids
                : ids.Where(id => Document.Nodes.Any(n => n.Id == id)).ToArray();
            await JS.InvokeVoidAsync("tmDiagramEditor.setSelection", _containerRef, nodeIds);
        }
        await InvokeAsync(StateHasChanged);
    }

    public async Task SetToolMode(string mode)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.setToolMode", _containerRef, mode);
    }

    public async Task UpdateCanvasSize(double w, double h)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.updateCanvasSize", _containerRef, w, h);
    }

    public async Task SetRulerUnit(string unit)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.setRulerUnit", _containerRef, unit);
    }

    public async Task SetPageScale(double scale)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.setPageScale", _containerRef, scale);
    }

    public async Task ScrollTo(double centreX, double centreY)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.scrollTo", _containerRef, centreX, centreY);
        if (ShowPageView)
            await JS.InvokeVoidAsync("tmDiagramEditor.addPageViewMargin", _containerRef, PageViewMargin);
    }

    public async Task ZoomToRect(double x, double y, double w, double h, double padding = 40)
    {
        if (!_jsInitialized) return;
        await JS.InvokeVoidAsync("tmDiagramEditor.zoomToRect", _containerRef, x, y, w, h, padding);
        if (ShowPageView)
            await JS.InvokeVoidAsync("tmDiagramEditor.addPageViewMargin", _containerRef, PageViewMargin);
    }

    /// <summary>Serializes the current viewport as a standalone SVG string.
    /// Decorator and overlay panes (handles, selection outlines) are stripped.
    /// Requires the canvas to be initialized.</summary>
    public async Task<string?> ExportViewportAsSvgAsync()
    {
        if (!_jsInitialized) return null;
        return await JS.InvokeAsync<string?>("tmDiagramEditor.exportViewportAsSvg", _containerRef);
    }

    public async Task FocusOnNode(string nodeId)
    {
        if (Document is null || !_jsInitialized) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;
        await ZoomToRect(node.X, node.Y, node.W, node.H, 40);
        await SetSelection(nodeId);
    }

    public async Task FocusOnEdge(string edgeId)
    {
        if (Document is null || !_jsInitialized) return;
        var edge = Document.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge is null) return;
        var mid = ComputeEdgeMidpoint(edge);
        await ZoomToRect(mid.X - 80, mid.Y - 40, 160, 80, 40);
        _currentSelectionIds = [edgeId];
        await OnSelectionChanged.InvokeAsync(_currentSelectionIds);
        await NotifyAndRender();
    }

    public Task SetActiveSearchResult(string? id)
    {
        _activeSearchResultId = id;
        return InvokeAsync(StateHasChanged);
    }

    public ElementReference GetContainerRef() => _containerRef;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task NotifyAndRender()
    {
        if (Document is not null)
            await DocumentChanged.InvokeAsync(Document);
        await InvokeAsync(StateHasChanged);
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string GetNodeDataString(DiagramNode node, string key)
        => node.Data.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private (double X, double Y, string TextAnchor, string Baseline) ComputeCardinalityPosition(DiagramEdge edge, bool isSource)
    {
        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        if (pts.Length < 2) return (0, 0, "middle", "middle");

        var (x1, y1) = isSource ? pts[0] : pts[^2];
        var (x2, y2) = isSource ? pts[1] : pts[^1];

        var dx = x2 - x1;
        var dy = y2 - y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
            return (x1, y1 - 15, "middle", "auto");

        // Perpendicular offset (~15px)
        var perpX = (-dy / len) * 15;
        var perpY = (dx / len) * 15;

        var tx = (isSource ? pts[0].X : pts[^1].X) + perpX;
        var ty = (isSource ? pts[0].Y : pts[^1].Y) + perpY;

        var anchor = perpX > 2 ? "start" : perpX < -2 ? "end" : "middle";
        var baseline = perpY > 2 ? "hanging" : perpY < -2 ? "auto" : "middle";

        return (tx, ty, anchor, baseline);
    }

    private double GetCurrentScale()
    {
        // Parse from viewBox width vs actual SVG width is done in JS;
        // For Drop we can approximate 1.0 if not initialized.
        if (!_jsInitialized) return 1.0;
        var vbW = Document?.Width ?? 3000;
        // We don't have direct DOM width here, but Drop offset is relative to container.
        // The container and SVG are 100% width, so offset is in screen pixels.
        // We need scale. We'll use JS to get scale, but for simplicity we can store it.
        return _currentScale;
    }

    private double _currentScale = 1.0;

    private static bool ContainsMath(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("$$") || text.Contains("\\(") || text.Contains("\\)") || text.Contains('`');
    }

    [JSInvokable]
    public void OnZoomChangedInternal(double scale) => _currentScale = scale;

    [JSInvokable("OnMathSvgCached")]
    public void JsOnMathSvgCached(string nodeId, string svgHtml)
    {
        if (Document is null) return;
        var node = Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;
        node.Data["__mathSvg"] = svgHtml;
    }

    private static DiagramNode DeepCopyNode(DiagramNode src)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(src);
        var copy = System.Text.Json.JsonSerializer.Deserialize<DiagramNode>(json)!;
        copy.Id = Guid.NewGuid().ToString("N")[..8];
        foreach (var p in copy.Ports)
            p.Id = Guid.NewGuid().ToString("N")[..8];
        return copy;
    }

    // ── Geometry helpers ─────────────────────────────────────────────────────

    private string ComputeEdgePath(DiagramEdge edge)
    {
        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        if (pts.Length < 2) return string.Empty;

        // Loop edge (self-connection): generate U-shaped cubic bezier
        if (!string.IsNullOrEmpty(edge.SourceNodeId) && edge.SourceNodeId == edge.TargetNodeId
            && string.IsNullOrEmpty(edge.SourceEdgeId) && string.IsNullOrEmpty(edge.TargetEdgeId))
        {
            var (x1, y1) = pts[0];
            var (x2, y2) = pts[^1];
            var loopSize = 40.0;
            var midX = (x1 + x2) / 2;
            var midY = (y1 + y2) / 2;
            var dirX = x2 - x1;
            var dirY = y2 - y1;
            var len = Math.Sqrt(dirX * dirX + dirY * dirY);
            double perpX, perpY;
            if (len > 0.001)
            {
                perpX = -dirY / len * loopSize;
                perpY = dirX / len * loopSize;
            }
            else
            {
                perpX = 0;
                perpY = -loopSize;
            }
            var cx1 = midX + perpX * 0.5;
            var cy1 = midY + perpY * 0.5;
            var cx2 = midX + perpX * 0.5;
            var cy2 = midY + perpY * 0.5;
            return $"M {F(x1)} {F(y1)} C {F(cx1)} {F(cy1)} {F(cx2)} {F(cy2)} {F(x2)} {F(y2)}";
        }

        // Inset endpoints to leave room for arrowhead markers
        var startInset = GetArrowheadInset(edge, true);
        var endInset = GetArrowheadInset(edge, false);
        if (startInset > 0)
            pts[0] = ShortenToward(pts[0], pts[1], startInset);
        if (endInset > 0)
            pts[^1] = ShortenToward(pts[^1], pts[^2], endInset);

        var sb = new System.Text.StringBuilder();
        sb.Append($"M {F(pts[0].X)} {F(pts[0].Y)}");
        if (edge.Routing == "curved")
        {
            if (pts.Length == 2)
            {
                var (x1, y1) = pts[0];
                var (x2, y2) = pts[1];
                var vx = x2 - x1;
                var vy = y2 - y1;
                var len = Math.Sqrt(vx * vx + vy * vy);
                double px, py;
                if (len > 0.001)
                {
                    px = -vy / len;
                    py = vx / len;
                }
                else
                {
                    px = 0;
                    py = -1;
                }
                var offset = Math.Min(Math.Max(len * 0.3, 20), 80);
                var c1x = x1 + vx * 0.5 + px * offset;
                var c1y = y1 + vy * 0.5 + py * offset;
                var c2x = x2 - vx * 0.5 + px * offset;
                var c2y = y2 - vy * 0.5 + py * offset;
                if (edge.CubicBezier)
                {
                    sb.Append($" C {F(c1x)} {F(c1y)} {F(c2x)} {F(c2y)} {F(x2)} {F(y2)}");
                }
                else
                {
                    var qcx = (c1x + c2x) / 2;
                    var qcy = (c1y + c2y) / 2;
                    sb.Append($" Q {F(qcx)} {F(qcy)} {F(x2)} {F(y2)}");
                }
            }
            else
            {
                var cps = GetCurvedControlPoints(pts);
                for (int i = 1; i < pts.Length; i++)
                {
                    var (cp1, cp2) = cps[i - 1];
                    var curr = pts[i];
                    if (edge.CubicBezier)
                    {
                        sb.Append($" C {F(cp1.X)} {F(cp1.Y)} {F(cp2.X)} {F(cp2.Y)} {F(curr.X)} {F(curr.Y)}");
                    }
                    else
                    {
                        var qcx = (cp1.X + cp2.X) / 2;
                        var qcy = (cp1.Y + cp2.Y) / 2;
                        sb.Append($" Q {F(qcx)} {F(qcy)} {F(curr.X)} {F(curr.Y)}");
                    }
                }
            }
        }
        else if (edge.Rounded && pts.Length > 2)
        {
            var r = edge.ArcSize ?? 8.0;
            for (int i = 1; i < pts.Length; i++)
            {
                var prev = pts[i - 1];
                var curr = pts[i];
                if (i < pts.Length - 1)
                {
                    var next = pts[i + 1];
                    // Compute shortened points for fillet
                    var (sx, sy) = ShortenToward(curr, prev, r);
                    var (ex, ey) = ShortenToward(curr, next, r);
                    sb.Append($" L {F(sx)} {F(sy)} Q {F(curr.X)} {F(curr.Y)} {F(ex)} {F(ey)}");
                }
                else
                {
                    sb.Append($" L {F(curr.X)} {F(curr.Y)}");
                }
            }
        }
        else if (edge.Routing == "orthogonal" && !string.IsNullOrEmpty(edge.JumpStyle) && Document is not null)
        {
            for (int i = 1; i < pts.Length; i++)
            {
                var prev = pts[i - 1];
                var curr = pts[i];
                var jumps = FindLineJumps(edge, i - 1, prev, curr);
                if (jumps.Count > 0)
                    AppendSegmentWithJumps(sb, prev, curr, edge.JumpStyle, edge.JumpSize ?? 10, jumps);
                else
                    sb.Append($" L {F(curr.X)} {F(curr.Y)}");
            }
        }
        else
        {
            for (int i = 1; i < pts.Length; i++)
                sb.Append($" L {F(pts[i].X)} {F(pts[i].Y)}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Computes a filled polygon path for block-arrow, double-arrow and flex-arrow edge shapes.
    /// </summary>
    private string ComputeBlockArrowPolygon(DiagramEdge edge)
    {
        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        if (pts.Length < 2) return string.Empty;

        double shaftWidth = edge.Style.StrokeWidth ?? 8;
        double arrowSize = edge.EndArrowSize ?? 10;

        return edge.Shape switch
        {
            "flexArrow" => ComputeTaperedPolygon(pts, shaftWidth, 0),
            "blockArrow" => ComputeConstantWidthPolygon(pts, shaftWidth, arrowSize, false, true),
            "doubleArrow" => ComputeConstantWidthPolygon(pts, shaftWidth, arrowSize, true, true),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Builds a closed polygon path with constant shaft width and optional triangular arrowheads.
    /// </summary>
    private static string ComputeConstantWidthPolygon((double X, double Y)[] pts, double shaftWidth, double arrowSize, bool startArrow, bool endArrow)
    {
        if (pts.Length < 2) return string.Empty;

        var left = new List<(double X, double Y)>();
        var right = new List<(double X, double Y)>();

        for (int i = 0; i < pts.Length; i++)
        {
            (double X, double Y) before = i > 0 ? pts[i - 1] : (pts[0].X * 2 - pts[1].X, pts[0].Y * 2 - pts[1].Y);
            (double X, double Y) after = i < pts.Length - 1 ? pts[i + 1] : (pts[^1].X * 2 - pts[^2].X, pts[^1].Y * 2 - pts[^2].Y);

            double dx = after.X - before.X;
            double dy = after.Y - before.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001)
            {
                left.Add(pts[i]);
                right.Add(pts[i]);
                continue;
            }

            double nx = dy / len * (shaftWidth / 2);
            double ny = -dx / len * (shaftWidth / 2);

            left.Add((pts[i].X + nx, pts[i].Y + ny));
            right.Add((pts[i].X - nx, pts[i].Y - ny));
        }

        var poly = new List<(double X, double Y)>();

        // Start arrowhead or rectangular end
        if (startArrow && pts.Length >= 2)
        {
            double dx = pts[1].X - pts[0].X;
            double dy = pts[1].Y - pts[0].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 0.001)
            {
                double dirX = dx / len;
                double dirY = dy / len;
                double perpX = -dirY;
                double perpY = dirX;
                double baseCX = pts[0].X + dirX * arrowSize;
                double baseCY = pts[0].Y + dirY * arrowSize;
                double baseLX = baseCX + perpX * (shaftWidth / 2);
                double baseLY = baseCY + perpY * (shaftWidth / 2);
                double baseRX = baseCX - perpX * (shaftWidth / 2);
                double baseRY = baseCY - perpY * (shaftWidth / 2);
                poly.Add((pts[0].X, pts[0].Y));
                poly.Add((baseRX, baseRY));
            }
            else
            {
                poly.Add(left[0]);
            }
        }
        else
        {
            poly.Add(left[0]);
        }

        // Left side forward
        int startIdx = startArrow ? 1 : 0;
        for (int i = startIdx; i < left.Count; i++)
            poly.Add(left[i]);

        // End arrowhead or rectangular end
        if (endArrow && pts.Length >= 2)
        {
            double dx = pts[^1].X - pts[^2].X;
            double dy = pts[^1].Y - pts[^2].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 0.001)
            {
                double dirX = dx / len;
                double dirY = dy / len;
                double perpX = -dirY;
                double perpY = dirX;
                double baseCX = pts[^1].X - dirX * arrowSize;
                double baseCY = pts[^1].Y - dirY * arrowSize;
                double baseLX = baseCX + perpX * (shaftWidth / 2);
                double baseLY = baseCY + perpY * (shaftWidth / 2);
                double baseRX = baseCX - perpX * (shaftWidth / 2);
                double baseRY = baseCY - perpY * (shaftWidth / 2);
                poly.Add((baseLX, baseLY));
                poly.Add((pts[^1].X, pts[^1].Y));
                poly.Add((baseRX, baseRY));
            }
            else
            {
                poly.Add(right[^1]);
            }
        }
        else
        {
            poly.Add(right[^1]);
        }

        // Right side backward
        int endIdx = endArrow ? right.Count - 2 : right.Count - 1;
        for (int i = endIdx; i >= 0; i--)
            poly.Add(right[i]);

        if (poly.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append($"M {F(poly[0].X)} {F(poly[0].Y)}");
        for (int i = 1; i < poly.Count; i++)
            sb.Append($" L {F(poly[i].X)} {F(poly[i].Y)}");
        sb.Append(" Z");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a closed polygon path that linearly tapers from <paramref name="startWidth"/> to <paramref name="endWidth"/>.
    /// </summary>
    private static string ComputeTaperedPolygon((double X, double Y)[] pts, double startWidth, double endWidth)
    {
        if (pts.Length < 2) return string.Empty;

        var left = new List<(double X, double Y)>();
        var right = new List<(double X, double Y)>();

        for (int i = 0; i < pts.Length; i++)
        {
            double t = pts.Length > 1 ? (double)i / (pts.Length - 1) : 0;
            double width = startWidth * (1 - t) + endWidth * t;

            (double X, double Y) before = i > 0 ? pts[i - 1] : (pts[0].X * 2 - pts[1].X, pts[0].Y * 2 - pts[1].Y);
            (double X, double Y) after = i < pts.Length - 1 ? pts[i + 1] : (pts[^1].X * 2 - pts[^2].X, pts[^1].Y * 2 - pts[^2].Y);

            double dx = after.X - before.X;
            double dy = after.Y - before.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001)
            {
                left.Add(pts[i]);
                right.Add(pts[i]);
                continue;
            }

            double nx = dy / len * (width / 2);
            double ny = -dx / len * (width / 2);

            left.Add((pts[i].X + nx, pts[i].Y + ny));
            right.Add((pts[i].X - nx, pts[i].Y - ny));
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"M {F(left[0].X)} {F(left[0].Y)}");
        for (int i = 1; i < left.Count; i++)
            sb.Append($" L {F(left[i].X)} {F(left[i].Y)}");
        for (int i = right.Count - 1; i >= 0; i--)
            sb.Append($" L {F(right[i].X)} {F(right[i].Y)}");
        sb.Append(" Z");
        return sb.ToString();
    }

    private static (double X, double Y) ShortenToward((double X, double Y) from, (double X, double Y) to, double r)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0) return from;
        var t = Math.Min(r / len, 0.5);
        return (from.X + dx * t, from.Y + dy * t);
    }

    private static double GetArrowheadInset(DiagramEdge edge, bool isStart)
        => DiagramArrowheadRenderer.GetArrowheadInset(edge, isStart);

    private static ((double X, double Y) cp1, (double X, double Y) cp2)[] GetCurvedControlPoints((double X, double Y)[] pts)
    {
        var result = new ((double X, double Y), (double X, double Y))[pts.Length - 1];
        for (int i = 0; i < pts.Length - 1; i++)
        {
            (double X, double Y) p0 = i > 0 ? pts[i - 1] : (pts[0].X * 2 - pts[1].X, pts[0].Y * 2 - pts[1].Y);
            (double X, double Y) p1 = pts[i];
            (double X, double Y) p2 = pts[i + 1];
            (double X, double Y) p3 = i < pts.Length - 2 ? pts[i + 2] : (pts[^1].X * 2 - pts[^2].X, pts[^1].Y * 2 - pts[^2].Y);

            (double X, double Y) cp1 = (p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
            (double X, double Y) cp2 = (p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
            result[i] = (cp1, cp2);
        }
        return result;
    }

    private List<double> FindLineJumps(DiagramEdge edge, int segmentIndex, (double X, double Y) a, (double X, double Y) b)
    {
        var jumps = new List<double>();
        if (Document is null || string.IsNullOrEmpty(edge.JumpStyle)) return jumps;

        bool isHoriz = Math.Abs(b.Y - a.Y) < 0.001;
        bool isVert = Math.Abs(b.X - a.X) < 0.001;
        if (!isHoriz && !isVert) return jumps;

        double len = isHoriz ? Math.Abs(b.X - a.X) : Math.Abs(b.Y - a.Y);
        if (len < 0.001) return jumps;

        foreach (var other in Document.Edges)
        {
            if (other.Id == edge.Id) continue;
            if (other.Routing != "orthogonal") continue;
            if (string.IsNullOrEmpty(other.JumpStyle)) continue;

            var otherPts = DiagramGeometryHelper.GetEdgePoints(Document, other);
            for (int j = 1; j < otherPts.Length; j++)
            {
                var oa = otherPts[j - 1];
                var ob = otherPts[j];

                bool oHoriz = Math.Abs(ob.Y - oa.Y) < 0.001;
                bool oVert = Math.Abs(ob.X - oa.X) < 0.001;

                if (isHoriz && !oVert) continue;
                if (isVert && !oHoriz) continue;

                double ix = isHoriz ? oa.X : a.X;
                double iy = isHoriz ? a.Y : oa.Y;

                const double eps = 0.5;
                if (ix < Math.Min(a.X, b.X) - eps || ix > Math.Max(a.X, b.X) + eps) continue;
                if (iy < Math.Min(a.Y, b.Y) - eps || iy > Math.Max(a.Y, b.Y) + eps) continue;
                if (ix < Math.Min(oa.X, ob.X) - eps || ix > Math.Max(oa.X, ob.X) + eps) continue;
                if (iy < Math.Min(oa.Y, ob.Y) - eps || iy > Math.Max(oa.Y, ob.Y) + eps) continue;

                double t = isHoriz
                    ? (Math.Abs(b.X - a.X) > 0.001 ? (ix - a.X) / (b.X - a.X) : 0)
                    : (Math.Abs(b.Y - a.Y) > 0.001 ? (iy - a.Y) / (b.Y - a.Y) : 0);

                if (t > 0.05 && t < 0.95)
                    jumps.Add(t);
            }
        }

        jumps.Sort();
        return jumps;
    }

    private static void AppendSegmentWithJumps(System.Text.StringBuilder sb, (double X, double Y) a, (double X, double Y) b, string jumpStyle, double jumpSize, List<double> jumps)
    {
        if (jumps.Count == 0)
        {
            sb.Append($" L {F(b.X)} {F(b.Y)}");
            return;
        }

        bool isHoriz = Math.Abs(b.Y - a.Y) < 0.001;
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double segLen = Math.Sqrt(dx * dx + dy * dy);
        double half = Math.Max(jumpSize / 2.0, 2.0);

        double prevT = 0;
        foreach (var t in jumps)
        {
            double t0 = Math.Max(prevT, t - half / segLen);
            double t1 = Math.Min(1.0, t + half / segLen);

            var x0 = a.X + dx * t0;
            var y0 = a.Y + dy * t0;
            var x1 = a.X + dx * t1;
            var y1 = a.Y + dy * t1;
            var xc = a.X + dx * t;
            var yc = a.Y + dy * t;

            sb.Append($" L {F(x0)} {F(y0)}");

            switch (jumpStyle)
            {
                case "arc":
                    sb.Append($" A {F(half)} {F(half)} 0 0 0 {F(x1)} {F(y1)}");
                    break;
                case "gap":
                    sb.Append($" M {F(x1)} {F(y1)}");
                    break;
                case "sharp":
                    if (isHoriz)
                        sb.Append($" L {F(xc)} {F(yc - half)} L {F(x1)} {F(y1)}");
                    else
                        sb.Append($" L {F(xc + half)} {F(yc)} L {F(x1)} {F(y1)}");
                    break;
                case "line":
                    if (isHoriz)
                    {
                        sb.Append($" L {F(xc - half * 0.3)} {F(yc - half)}");
                        sb.Append($" L {F(xc + half * 0.3)} {F(yc + half)}");
                        sb.Append($" L {F(x1)} {F(y1)}");
                    }
                    else
                    {
                        sb.Append($" L {F(xc - half)} {F(yc - half * 0.3)}");
                        sb.Append($" L {F(xc + half)} {F(yc + half * 0.3)}");
                        sb.Append($" L {F(x1)} {F(y1)}");
                    }
                    break;
                default:
                    sb.Append($" L {F(x1)} {F(y1)}");
                    break;
            }

            prevT = t1;
        }

        if (prevT < 1.0)
        {
            var xe = a.X + dx * 1.0;
            var ye = a.Y + dy * 1.0;
            sb.Append($" L {F(xe)} {F(ye)}");
        }
    }

    private static double GetEdgeStrokeWidth(DiagramEdge edge) => edge.Shape switch
    {
        "filledEdge" => edge.Style.StrokeWidth ?? 8,
        "blockArrow" or "doubleArrow" or "flexArrow" => edge.Style.StrokeWidth ?? 8,
        "pipe" => edge.Style.StrokeWidth ?? 10,
        "wire" => edge.Style.StrokeWidth ?? 2,
        _ => edge.Style.StrokeWidth ?? 1.5,
    };

    private static string GetEdgeStrokeDasharray(DiagramEdge edge)
    {
        if (edge.Shape == "wire") return "8,8";
        var pattern = edge.Style.StrokeDashPattern;
        if (string.IsNullOrEmpty(pattern)) return edge.ConnectorType == "dependency" ? "5,5" : "";
        return pattern switch
        {
            "solid" => "",
            "dashed" => "5,5",
            "dotted" => "1,3",
            "dash-dot" => "5,2,1,2",
            _ => pattern,
        };
    }

    private static string GetEdgeColor(DiagramEdge edge)
        => edge.Shape == "wire" ? "#ff0000" : (edge.Style.Stroke ?? "#111827");

    private static double GetEdgeOpacity(DiagramEdge edge)
        => edge.Style.Opacity ?? 1.0;

    /// <summary>Computes an offset path parallel to the main edge path (for "link" double-line shape).</summary>
    private string ComputeEdgeOffsetPath(DiagramEdge edge, double offset)
    {
        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        if (pts.Length < 2) return string.Empty;

        var startInset = GetArrowheadInset(edge, true);
        var endInset = GetArrowheadInset(edge, false);
        if (startInset > 0)
            pts[0] = ShortenToward(pts[0], pts[1], startInset);
        if (endInset > 0)
            pts[^1] = ShortenToward(pts[^1], pts[^2], endInset);

        var offsetPts = DiagramArrowheadRenderer.OffsetPolyline(pts, offset);
        if (offsetPts.Length < 2) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append($"M {F(offsetPts[0].X)} {F(offsetPts[0].Y)}");
        for (int i = 1; i < offsetPts.Length; i++)
            sb.Append($" L {F(offsetPts[i].X)} {F(offsetPts[i].Y)}");
        return sb.ToString();
    }



    private static bool GetEdgeArrowFill(DiagramEdge edge, bool isStart)
    {
        var arrow = isStart ? edge.StartArrow : edge.EndArrow;
        var userFill = isStart ? edge.StartArrowFill : edge.EndArrowFill;
        return DiagramArrowheadRegistry.GetEffectiveFill(arrow, userFill);
    }

    private string GetArrowheadSvg(DiagramEdge edge, bool isStart)
    {
        var pts = DiagramGeometryHelper.GetEdgePoints(Document, edge);
        var isFilled = GetEdgeArrowFill(edge, isStart);
        var strokeWidth = GetEdgeStrokeWidth(edge);
        var color = GetEdgeColor(edge);
        return DiagramArrowheadRenderer.RenderArrowhead(edge, isStart, pts, isFilled, strokeWidth, color);
    }

    // ── Helper DTOs ──────────────────────────────────────────────────────────

    public sealed class ElementMove
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>Swaps source and target endpoints of an edge (used temporarily before ComputeOrthogonalWaypointsAsync).</summary>
    private static void SwapEdgeEndpoints(DiagramEdge edge)
    {
        (edge.SourceNodeId, edge.TargetNodeId) = (edge.TargetNodeId, edge.SourceNodeId);
        (edge.SourcePortId, edge.TargetPortId) = (edge.TargetPortId, edge.SourcePortId);
        (edge.SourceEdgeId, edge.TargetEdgeId) = (edge.TargetEdgeId, edge.SourceEdgeId);
        (edge.SourceEdgeT, edge.TargetEdgeT) = (edge.TargetEdgeT, edge.SourceEdgeT);
        (edge.SourcePoint, edge.TargetPoint) = (edge.TargetPoint, edge.SourcePoint);
        (edge.SourceConstraint, edge.TargetConstraint) = (edge.TargetConstraint, edge.SourceConstraint);
        (edge.SourceSpacing, edge.TargetSpacing) = (edge.TargetSpacing, edge.SourceSpacing);
        (edge.SourceCardinality, edge.TargetCardinality) = (edge.TargetCardinality, edge.SourceCardinality);
        // StartArrow/EndArrow intentionally NOT swapped – they describe the physical
        // start/end of the edge line, which is already reversed by swapping source/target.
    }
}
