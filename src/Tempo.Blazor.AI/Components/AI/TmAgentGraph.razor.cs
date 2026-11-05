using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Agents and the tools, resources and humans around them as ONE drawn network: a box per node in a column its kind
/// belongs to, a curve per connection whose thickness is how often it was called, and a short pulse on the curve every
/// time a new call arrives.
/// <para>
/// The surface renders the snapshot it is given (plus the incremental calls the host streams in) and never polls,
/// fetches or mutates anything: the host owns the transport and the component owns its view. The injected
/// <see cref="TimeProvider"/> drives the pulse window and the paint throttle, so a test with a fake clock controls the
/// whole surface.
/// </para>
/// <para>
/// The drawing is never the only way to read the graph. The scrollport is the ONE labelled group (`role="group"`) and
/// its nodes are focusable buttons named by their label, kind, state and call count; the ARROW keys walk the actual
/// CONNECTIONS (not the DOM order), Home/End jump to the edges and Enter/Space select, so a keyboard-only reader
/// navigates the network the way it is drawn. The View switcher renders the SAME data as two plain tables (every
/// connection with its call count, and every node with its state), which is what a screen reader or a narrow phone
/// reads.
/// </para>
/// <para>
/// A node is a native <c>&lt;button&gt;</c>, so Enter and Space activate it exactly once with no emulation
/// (docs/keyboard-activation-convention.md). The page scroll an ARROW key may produce is accepted rather than
/// suppressed: a render-time <c>@onkeydown:preventDefault</c> cannot be per-key and would swallow Tab and the button's
/// own native activation (the same decision <c>TmTabs</c> and <c>TmTopBar</c> document).
/// </para>
/// </summary>
public partial class TmAgentGraph : TmComponentBase, IAsyncDisposable, IDisposable
{
    /// <summary>The factor one zoom step applies, so + and − walk the same geometric ladder.</summary>
    private const double ZoomStep = 1.25;

    /// <summary>How many characters one label line of a node box holds at the default box width.</summary>
    private const int LabelCharsPerLine = 19;

    private readonly string _id = $"tm-ai-agent-graph-{Guid.NewGuid():n}"[..29];

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AgentGraphInterop? _interop;
    private AgentRenderThrottle _throttle = new();
    private AgentEventStream<AgentGraphCall>? _stream;

    private AgentGraphSnapshot? _snapshotParameter;
    private long _snapshotSequence;
    private bool _hasSnapshot;

    private AgentGraphSnapshot _view = new();
    private AgentGraphLayoutResult _layout = AgentGraphLayout.Build(null);
    private readonly List<NodeHandle> _handles = [];

    private AgentGraphViewMode _viewMode = AgentGraphViewMode.Graph;
    private AgentGraphViewMode _lastViewMode = AgentGraphViewMode.Graph;
    private double _zoom = 1;
    private double _lastZoomParameter = 1;
    private double _gestureBaseZoom = 1;
    private bool _gestureChanged;
    private string? _selectedNodeId;
    private string? _lastSelectedParameter;
    private string? _activeNodeId;

    private readonly List<PulseEntry> _pulses = [];
    private ITimer? _pulseTimer;

    private DotNetObjectReference<TmAgentGraph>? _self;
    private ElementReference _viewportRef;
    private string? _interopViewportId;
    private bool _focusViewportAfterRender;
    private string? _pendingFocusNodeId;
    private ITimer? _trailingPaint;
    private bool _paintNow = true;
    private bool _disposed;
    private bool _gapAnnounced;
    private bool _gapPaintPending;
    private long? _lastReportedGap;
    private RenderSignature? _signature;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The COMPLETE state of the graph at one moment: the nodes, the connections between them and the calls each
    /// connection carried, plus the sequence the snapshot includes. The component never mutates it. Null renders the
    /// localized empty state (the surface is usable before the host's first load lands).
    /// </summary>
    [Parameter] public AgentGraphSnapshot? Snapshot { get; set; }

    /// <summary>
    /// The incremental calls applied ON TOP of <see cref="Snapshot"/>, in any order. The shared
    /// <see cref="AgentEventStream{TEvent}"/> sorts them, drops what was already applied and rejects a batch with a hole
    /// (raising <see cref="OnGapDetected"/>), so a host may pass the same batch twice, or a cumulative list, without
    /// de-duplicating itself. Null means "the snapshot is all there is".
    /// </summary>
    [Parameter] public IReadOnlyList<AgentGraphCall>? Events { get; set; }

    /// <summary>
    /// Raised once per batch that contains a HOLE — with the first sequence the stream does not have. The documented
    /// repair is the host's own: load a new snapshot and pass it (with its sequence) as <see cref="Snapshot"/>.
    /// </summary>
    [Parameter] public EventCallback<long> OnGapDetected { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Agent graph"); the heading also names the
    /// drawing, so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Which presentation is shown, two-way bound with <see cref="ViewModeChanged"/>. Default
    /// <see cref="AgentGraphViewMode.Graph"/>; <see cref="AgentGraphViewMode.Table"/> is the text alternative with
    /// exactly the same data.
    /// </summary>
    [Parameter] public AgentGraphViewMode ViewMode { get; set; } = AgentGraphViewMode.Graph;

    /// <summary>Fires with the new presentation when the reader uses the View switcher.</summary>
    [Parameter] public EventCallback<AgentGraphViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// The selected node's id, two-way bound with <see cref="SelectedNodeIdChanged"/> (<c>@bind-SelectedNodeId</c>).
    /// The matching node carries <c>aria-pressed="true"</c> and its neighbours are highlighted; with
    /// <see cref="DetailTemplate"/> set the node's detail renders in a named region below the surface.
    /// </summary>
    [Parameter] public string? SelectedNodeId { get; set; }

    /// <summary>Fires with the new selected node id when the reader selects a node (a null value clears it).</summary>
    [Parameter] public EventCallback<string?> SelectedNodeIdChanged { get; set; }

    /// <summary>
    /// The host's detail surface for the selected node — the full label, the call numbers, links, actions. Rendered in a
    /// <c>role="region"</c> named by the localized "Node detail". Null renders no detail at all.
    /// </summary>
    [Parameter] public RenderFragment<AgentGraphNode>? DetailTemplate { get; set; }

    /// <summary>
    /// Whether the zoom controls are rendered. Default <c>true</c>. They are the WCAG 2.5.7 alternative to the pinch
    /// gesture, so a host is expected to keep them unless its own chrome zooms.
    /// </summary>
    [Parameter] public bool ShowZoom { get; set; } = true;

    /// <summary>
    /// The scale of the drawing, two-way bound with <see cref="ZoomChanged"/>. Default 1 (100 %), clamped into
    /// <see cref="MinZoom"/>–<see cref="MaxZoom"/>. A host that pins it keeps control: the reader's zoom reports the
    /// change and the next render from the host decides.
    /// </summary>
    [Parameter] public double Zoom { get; set; } = 1;

    /// <summary>Fires with the new scale when the reader zooms (a button, "fit", or a pinch/ctrl+wheel gesture).</summary>
    [Parameter] public EventCallback<double> ZoomChanged { get; set; }

    /// <summary>The smallest allowed scale. Default 0.5 (50 %).</summary>
    [Parameter] public double MinZoom { get; set; } = 0.5;

    /// <summary>The largest allowed scale. Default 2.5 (250 %).</summary>
    [Parameter] public double MaxZoom { get; set; } = 2.5;

    /// <summary>
    /// The nodes the HOST pinned, by node id (a hand-placed sketch, a restored layout). A pinned node keeps its exact
    /// coordinate and the canvas grows to contain it; everything else is still laid out around it. Null pins nothing.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, AgentGraphPosition>? NodePositions { get; set; }

    /// <summary>
    /// How many edges may pulse at once. Default 6. A burst of calls from a fan-out agent must not light up the whole
    /// picture, so only the NEWEST pulses survive: an older one is retired as soon as the cap is exceeded.
    /// </summary>
    [Parameter] public int MaxPulses { get; set; } = 6;

    /// <summary>
    /// How long one pulse stays on its edge. Default 600 ms. The highlight is removed by a timer on the injected
    /// <see cref="TimeProvider"/>, so a fake clock closes it without sleeping; under
    /// <c>prefers-reduced-motion</c> the same window keeps a STILL highlight instead of the animation. The value is also
    /// written onto the root as <c>--tm-agent-graph-pulse-duration</c>, so the CSS animation and the timer share ONE
    /// length and a host that changes this parameter never has to restyle the knob.
    /// </summary>
    [Parameter] public TimeSpan PulseDuration { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// The culture the "last call" time formats in. Null uses <see cref="CultureInfo.CurrentCulture"/>. It never changes
    /// the values, only their shape.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The clock the pulse window and the paint throttle measure on. Null uses the container's
    /// <see cref="TimeProvider"/> and falls back to <see cref="TimeProvider.System"/> — the host's clock always wins
    /// over the machine's.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// The paint-gate window for event-driven updates. Null uses the shared
    /// <see cref="AgentRenderThrottle.DefaultInterval"/> (100 ms); <see cref="TimeSpan.Zero"/> paints every batch. The
    /// STATE is never throttled, only the paint.
    /// </summary>
    [Parameter] public TimeSpan? RenderThrottle { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The snapshot the surface currently renders: the host's <see cref="Snapshot"/> plus every applied call.</summary>
    public AgentGraphSnapshot View => _view;

    /// <summary>The last layout pass, so the tests (and the component's own markup) read one geometry.</summary>
    internal AgentGraphLayoutResult Layout => _layout;

    /// <summary>The edges currently pulsing, oldest first — the window a call lights up, readable without the markup.</summary>
    internal IReadOnlyList<string> PulsingEdgeIds => _pulses.Select(entry => entry.EdgeId).ToList();

    /// <summary>The selected node from <see cref="View"/>, or null when nothing is selected (or it is gone).</summary>
    public AgentGraphNode? SelectedNode => _selectedNodeId is { } id
        ? _view.Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal))
        : null;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAgentGraph_Title"] : Title!;

    /// <summary>The current scale as a percentage, for the visible read-out and the root's state hook.</summary>
    public int ZoomPercent => (int)Math.Round(_zoom * 100, MidpointRounding.AwayFromZero);

    private string RootClass => string.IsNullOrWhiteSpace(Class)
        ? "tm-ai-agent-graph"
        : "tm-ai-agent-graph " + Class;

    private string ViewSlug => _viewMode == AgentGraphViewMode.Table ? "table" : "graph";

    private string ZoomPercentText => Loc["TmAgentGraph_ZoomLevel", ZoomPercent.ToString(CultureInfo.CurrentCulture)];

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private string ArrowMarkerId => _id + "-arrow";

    private string CanvasWidth => Px(_layout.WidthPx * _zoom);

    private string CanvasHeight => Px(_layout.HeightPx * _zoom);

    private string ViewBox => $"0 0 {Number(_layout.WidthPx)} {Number(_layout.HeightPx)}";

    private IReadOnlyList<SegmentedOption<AgentGraphViewMode>> ViewOptions =>
    [
        new() { Label = Loc["TmAgentGraph_ViewGraph"], Value = AgentGraphViewMode.Graph },
        new() { Label = Loc["TmAgentGraph_ViewTable"], Value = AgentGraphViewMode.Table },
    ];

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _self = DotNetObjectReference.Create(this);
        _clock = ResolveClock();
        _throttle = new AgentRenderThrottle(_clock, RenderThrottle);
        _interop = new AgentGraphInterop(JS);
        _viewMode = ViewMode;
        _lastViewMode = ViewMode;
        _zoom = ClampZoom(Zoom);
        _lastZoomParameter = Zoom;
        _selectedNodeId = SelectedNodeId;
        _lastSelectedParameter = SelectedNodeId;
        Recompute();
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var clock = ResolveClock();
        if (!ReferenceEquals(clock, _clock))
        {
            _clock = clock;
            _throttle = new AgentRenderThrottle(_clock, RenderThrottle);
        }

        var visualChange = ParametersChangedExceptEvents();

        // A host that changes a parameter wins; the reader's own choice survives an unrelated render.
        if (ViewMode != _lastViewMode)
        {
            _lastViewMode = ViewMode;
            _viewMode = ViewMode;
        }

        if (Math.Abs(Zoom - _lastZoomParameter) > 0.0001)
        {
            _lastZoomParameter = Zoom;
            _zoom = ClampZoom(Zoom);
        }

        if (!string.Equals(SelectedNodeId, _lastSelectedParameter, StringComparison.Ordinal))
        {
            _lastSelectedParameter = SelectedNodeId;
            _selectedNodeId = SelectedNodeId;
        }

        var applied = ApplyEvents();
        Recompute();
        ReconcileSelection();

        if (visualChange || _gapPaintPending)
        {
            // A gap is rare and the reader has to be told, so it paints at once instead of waiting for the next event.
            _gapPaintPending = false;
            CancelTrailingPaint();
            PaintNow();
        }
        else if (applied)
        {
            GatePaint();
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        if (_paintNow)
        {
            _paintNow = false;
            return true;
        }

        return _throttle.ShouldRender();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Blazor never asks ShouldRender before the FIRST render, so the flag the first parameter set raised is
            // still set; clearing it here keeps the gate honest for the first event-driven paint.
            _paintNow = false;
        }

        // Focus is moved AFTER the render: the node that has to receive it carries a NEW tabindex and new aria state in
        // this very paint, and the browser only focuses the element once it is patched (the same rule TmTabs follows
        // with its own after-render focus move).
        if (_focusViewportAfterRender)
        {
            _focusViewportAfterRender = false;
            await FocusAsync(_viewportRef);
        }

        if (_pendingFocusNodeId is { } pendingFocus)
        {
            _pendingFocusNodeId = null;
            var handle = _handles.FirstOrDefault(candidate =>
                string.Equals(candidate.Placement.Node.Id, pendingFocus, StringComparison.Ordinal));

            if (handle is not null)
            {
                await FocusAsync(handle.Ref);
            }
        }

        if (_viewMode != AgentGraphViewMode.Graph || _layout.IsEmpty)
        {
            _interopViewportId = null;
            return;
        }

        if (_viewportRef.Id is { } viewportId && !string.Equals(viewportId, _interopViewportId, StringComparison.Ordinal))
        {
            _interopViewportId = viewportId;
            if (_interop is not null)
            {
                await _interop.AttachAsync(_viewportRef, _self);
            }
        }
    }

    private TimeProvider ResolveClock()
        => TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;

    // ── Layout ───────────────────────────────────────────────────────────────

    /// <summary>Recomputes the layout of the current view and refreshes the per-node render handles.</summary>
    private void Recompute()
    {
        _layout = AgentGraphLayout.Build(_view, new AgentGraphLayoutOptions { NodePositions = NodePositions });

        // A node that is STILL in the graph keeps its handle, because the handle owns the element reference the focus
        // move uses: rebuilding it would drop that reference until the next paint, and a paint the gate holds back
        // leaves an arrow key pointing at nothing (the browser then keeps the focus where it was).
        var existing = new Dictionary<string, NodeHandle>(StringComparer.Ordinal);
        foreach (var handle in _handles)
        {
            existing[handle.Placement.Node.Id] = handle;
        }

        var handles = new List<NodeHandle>(_layout.Nodes.Count);
        foreach (var placement in _layout.Nodes)
        {
            if (existing.TryGetValue(placement.Node.Id, out var handle))
            {
                handle.Placement = placement;
                handles.Add(handle);
            }
            else
            {
                handles.Add(new NodeHandle { Placement = placement });
            }
        }

        _handles.Clear();
        _handles.AddRange(handles);

        if (_pulses.Count > 0)
        {
            _pulses.RemoveAll(entry => _layout.Edges.All(edge =>
                !string.Equals(edge.Edge.Id, entry.EdgeId, StringComparison.Ordinal)));
            SchedulePulseSweep();
        }
    }

    /// <summary>
    /// Keeps the selection and the roving tab stop inside the graph the surface is actually painting: a node the host
    /// removed cannot stay selected (and nothing may stay focused on a control that disappeared).
    /// </summary>
    private void ReconcileSelection()
    {
        if (_selectedNodeId is { } selected && _layout.Find(selected) is null)
        {
            _selectedNodeId = null;
            _activeNodeId = null;
            _focusViewportAfterRender = true;
            Announce(Loc["TmAgentGraph_SelectionLost"]);
        }

        if (_activeNodeId is { } active && _layout.Find(active) is null)
        {
            _activeNodeId = null;
        }

        _activeNodeId ??= _selectedNodeId ?? _layout.Nodes.FirstOrDefault()?.Node.Id;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the node with <paramref name="nodeId"/> — the same path a click, Enter or Space takes. A null id clears
    /// the selection; an id that names no supplied node still raises the callback (the host may know a node this render
    /// does not show).
    /// </summary>
    /// <param name="nodeId">The node to select, or null to clear the selection.</param>
    /// <returns>A task that completes when the selection has been reported.</returns>
    public async Task SelectNodeAsync(string? nodeId)
    {
        _selectedNodeId = nodeId;
        _lastSelectedParameter = SelectedNodeId;
        _activeNodeId = nodeId ?? _activeNodeId;

        if (nodeId is not null && FindNode(nodeId) is { } node)
        {
            Announce(Loc["TmAgentGraph_Selected", node.DisplayName]);
        }

        await SelectedNodeIdChanged.InvokeAsync(nodeId);
        Paint();
    }

    /// <summary>Zooms in by one step (×1.25), clamped into the allowed window.</summary>
    /// <returns>A task that completes when the new scale has been reported.</returns>
    public Task ZoomInAsync() => ApplyZoomAsync(_zoom * ZoomStep, announce: true);

    /// <summary>Zooms out by one step (÷1.25), clamped into the allowed window.</summary>
    /// <returns>A task that completes when the new scale has been reported.</returns>
    public Task ZoomOutAsync() => ApplyZoomAsync(_zoom / ZoomStep, announce: true);

    /// <summary>
    /// Fits the whole drawing into the room the reader has: the scale becomes the measured scrollport divided by the
    /// canvas, clamped into <see cref="MinZoom"/>–<see cref="MaxZoom"/>. Where nothing can be measured (prerender, no
    /// JavaScript) the current scale is kept instead of jumping to a guess.
    /// </summary>
    /// <returns>A task that completes when the new scale has been reported.</returns>
    public async Task FitToViewAsync()
    {
        var measured = _interop is null ? [] : await _interop.MeasureViewportAsync(_viewportRef);
        if (measured.Length < 2 || measured[0] <= 0 || measured[1] <= 0
            || _layout.WidthPx <= 0 || _layout.HeightPx <= 0)
        {
            Announce(Loc["TmAgentGraph_Zoom", ZoomPercentText]);
            return;
        }

        var candidate = Math.Min(measured[0] / _layout.WidthPx, measured[1] / _layout.HeightPx);
        await ApplyZoomAsync(candidate, announce: true);
    }

    // ── JS callbacks (pinch / ctrl+wheel) ────────────────────────────────────

    /// <summary>Called by the package's JS module when a pinch or ctrl+wheel gesture starts.</summary>
    /// <returns>A completed task; the gesture itself is owned by the browser.</returns>
    [JSInvokable]
    public Task OnGraphZoomGestureStartAsync()
    {
        _gestureBaseZoom = _zoom;
        _gestureChanged = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the package's JS module with the gesture's scale relative to the level it started from. The value is
    /// CUMULATIVE, so two ticks of 1.5 are a scale of 1.5, not 2.25.
    /// </summary>
    /// <param name="scale">The gesture's relative scale (1 = unchanged).</param>
    /// <returns>A completed task; the gesture itself is owned by the browser.</returns>
    [JSInvokable]
    public Task OnGraphZoomGestureChangeAsync(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return Task.CompletedTask;
        }

        var next = ClampZoom(_gestureBaseZoom * scale);
        if (Math.Abs(next - _zoom) < 0.0001)
        {
            return Task.CompletedTask;
        }

        _zoom = next;
        _gestureChanged = true;
        Paint();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the package's JS module when the gesture ends (or is cancelled). The reader's gesture is reported to
    /// the host here rather than on every tick, so a host binding the scale does not re-render mid-gesture.
    /// </summary>
    /// <returns>A task that completes when the scale has been reported to the host.</returns>
    [JSInvokable]
    public async Task OnGraphZoomGestureEndAsync()
    {
        if (!_gestureChanged)
        {
            return;
        }

        _gestureChanged = false;
        Announce(Loc["TmAgentGraph_Zoom", ZoomPercentText]);
        _lastZoomParameter = Zoom;
        Paint();
        await ZoomChanged.InvokeAsync(_zoom);
    }

    // ── Events and rendering ─────────────────────────────────────────────────

    private async Task OnViewModeChangedAsync(AgentGraphViewMode mode)
    {
        if (mode == _viewMode)
        {
            return;
        }

        _viewMode = mode;
        _lastViewMode = ViewMode;
        _signature = CurrentSignature();
        Paint();

        if (ViewModeChanged.HasDelegate)
        {
            await ViewModeChanged.InvokeAsync(mode);
        }
    }

    private async Task ApplyZoomAsync(double value, bool announce)
    {
        var next = ClampZoom(value);
        if (Math.Abs(next - _zoom) < 0.0001)
        {
            if (announce)
            {
                Announce(Loc["TmAgentGraph_Zoom", ZoomPercentText]);
            }

            return;
        }

        _zoom = next;
        _lastZoomParameter = Zoom;
        Paint();

        if (announce)
        {
            Announce(Loc["TmAgentGraph_Zoom", ZoomPercentText]);
        }

        if (ZoomChanged.HasDelegate)
        {
            await ZoomChanged.InvokeAsync(next);
        }
    }

    private double ClampZoom(double value)
    {
        var min = double.IsFinite(MinZoom) && MinZoom > 0 ? MinZoom : 0.5;
        var max = double.IsFinite(MaxZoom) && MaxZoom > min ? MaxZoom : Math.Max(min, 2.5);
        return !double.IsFinite(value) || value <= 0 ? min : Math.Min(max, Math.Max(min, value));
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The keys a node answers ITSELF. Enter and Space are deliberately absent: a node is a native
    /// <c>&lt;button&gt;</c>, so the browser already activates it exactly once and emulating them here would activate it
    /// twice (docs/keyboard-activation-convention.md).
    /// </summary>
    private async Task OnNodeKeyDownAsync(AgentGraphNodePlacement placement, KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowRight":
                await MoveFocusAsync(placement, horizontal: 1, vertical: 0);
                return;

            case "ArrowLeft":
                await MoveFocusAsync(placement, horizontal: -1, vertical: 0);
                return;

            case "ArrowDown":
                await MoveFocusAsync(placement, horizontal: 0, vertical: 1);
                return;

            case "ArrowUp":
                await MoveFocusAsync(placement, horizontal: 0, vertical: -1);
                return;

            case "Home":
                await FocusNodeAsync(_layout.Nodes.FirstOrDefault());
                return;

            case "End":
                await FocusNodeAsync(_layout.Nodes.LastOrDefault());
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Moves the roving focus along a CONNECTION in the requested direction: the neighbour whose centre is furthest
    /// that way wins, with the cross-axis distance and then the painting order breaking a tie. A node with no
    /// connection that way keeps the focus instead of jumping across the picture.
    /// </summary>
    private async Task MoveFocusAsync(AgentGraphNodePlacement placement, int horizontal, int vertical)
    {
        AgentGraphNodePlacement? best = null;
        var bestPrimary = 0d;
        var bestCross = double.MaxValue;

        foreach (var edge in _layout.Edges)
        {
            var other = string.Equals(edge.From.Node.Id, placement.Node.Id, StringComparison.Ordinal)
                ? edge.To
                : string.Equals(edge.To.Node.Id, placement.Node.Id, StringComparison.Ordinal)
                    ? edge.From
                    : null;

            if (other is null || string.Equals(other.Node.Id, placement.Node.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var dx = other.CenterX - placement.CenterX;
            var dy = other.CenterY - placement.CenterY;
            var primary = (dx * horizontal) + (dy * vertical);
            var cross = Math.Abs((dx * vertical) - (dy * horizontal));

            if (primary <= 0.5)
            {
                continue;
            }

            if (best is null || primary > bestPrimary + 0.001
                || (Math.Abs(primary - bestPrimary) <= 0.001 && cross < bestCross - 0.001))
            {
                best = other;
                bestPrimary = primary;
                bestCross = cross;
            }
        }

        if (best is not null)
        {
            await FocusNodeAsync(best);
        }
    }

    /// <summary>
    /// Moves the roving tab stop onto one node and REPAINTS; the DOM focus follows in <see cref="OnAfterRenderAsync"/>,
    /// where the node already carries the tabindex the paint just gave it.
    /// </summary>
    private Task FocusNodeAsync(AgentGraphNodePlacement? placement)
    {
        if (placement is null)
        {
            return Task.CompletedTask;
        }

        _activeNodeId = placement.Node.Id;
        _pendingFocusNodeId = placement.Node.Id;
        Paint();
        return Task.CompletedTask;
    }

    private static async Task FocusAsync(ElementReference reference)
    {
        if (reference.Id is null)
        {
            return;
        }

        try
        {
            await reference.FocusAsync();
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the element is already gone: the roving tab stop has still moved, so the reader's next
            // Tab lands on the right node.
        }
    }

    private void OnNodeFocused(string nodeId)
    {
        if (string.Equals(_activeNodeId, nodeId, StringComparison.Ordinal))
        {
            return;
        }

        _activeNodeId = nodeId;
        _ = InvokeAsync(StateHasChanged);
    }
}
