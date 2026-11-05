using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// A multi-agent run as one LANE per agent on a shared time axis: a block per piece of work, the review verdict a
/// block carries, handoff arrows between the lanes and the current moment as a vertical line.
/// <para>
/// The surface renders the snapshot it is given (plus the incremental events the host streams in) and never polls,
/// fetches or mutates anything: the host owns the transport and the component owns its view. The injected
/// <see cref="TimeProvider"/> drives both the live "now" line and the paint throttle, so a test with a fake clock
/// controls the whole surface.
/// </para>
/// <para>
/// The timeline is never the only way to read the run. The View switcher renders the SAME layout as two plain tables
/// (blocks and handoffs) for a screen reader, a keyboard-only reader or a narrow phone, the timeline exposes every
/// block as a native button named by its visible activity FIRST and then by its agent, kind, time, state and marker
/// (so a voice-control user can say what they see), and the blocks keep their geometry in the axis while the table
/// carries the exact timestamps.
/// </para>
/// </summary>
public partial class TmAgentLanes : TmComponentBase, IAsyncDisposable, IDisposable
{
    /// <summary>The factor one zoom step applies, so + and − walk the same geometric ladder.</summary>
    private const double ZoomStep = 1.5;

    /// <summary>
    /// The scale a zoom level of 100 % means: 600 px per hour, i.e. ten pixels per minute. An agent run is MINUTES
    /// long, so the default has to be readable at that scale — a per-hour default would paint every block as a
    /// sliver.
    /// </summary>
    public const double DefaultPixelsPerHour = 600;

    private readonly string _id = $"tm-ai-agent-lanes-{Guid.NewGuid():n}"[..30];

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AiElapsedTicker? _ticker;
    private AgentLanesInterop? _interop;
    private AgentRenderThrottle _throttle = new();
    private AgentEventStream<AgentLaneEvent>? _stream;

    private AgentLanesSnapshot? _snapshotParameter;
    private long _snapshotSequence;
    private bool _hasSnapshot;

    private AgentLanesSnapshot _view = new();
    private AgentLanesLayoutResult _layout = AgentLanesLayout.Build(null, DateTimeOffset.UnixEpoch);
    private AgentLanesViewMode _viewMode = AgentLanesViewMode.Timeline;
    private AgentLanesViewMode _lastViewMode = AgentLanesViewMode.Timeline;
    private double _pixelsPerHour = DefaultPixelsPerHour;
    private double _lastPixelsPerHourParameter = DefaultPixelsPerHour;
    private double _gestureBasePixelsPerHour = DefaultPixelsPerHour;
    private bool _gestureChanged;
    private string? _selectedBlockId;
    private string? _lastSelectedParameter;

    private DotNetObjectReference<TmAgentLanes>? _self;
    private ElementReference _viewportRef;
    private string? _interopViewportId;
    private ITimer? _trailingPaint;
    private bool _paintNow = true;
    private bool _disposed;
    private bool _gapAnnounced;
    private long? _lastReportedGap;
    private RenderSignature? _signature;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The COMPLETE state of the run at one moment: the agents, their work blocks and the handoffs between them, plus
    /// the sequence the snapshot includes. The component never mutates it. Null renders the localized empty state
    /// (the component is usable before the host's first load lands).
    /// </summary>
    [Parameter] public AgentLanesSnapshot? Snapshot { get; set; }

    /// <summary>
    /// The incremental events applied ON TOP of <see cref="Snapshot"/>, in any order. The shared
    /// <see cref="AgentEventStream{TEvent}"/> sorts them, drops what was already applied and rejects a batch with a
    /// hole (raising <see cref="OnGapDetected"/>), so a host may pass the same batch twice, or a cumulative list,
    /// without de-duplicating itself. Null means "the snapshot is all there is".
    /// </summary>
    [Parameter] public IReadOnlyList<AgentLaneEvent>? Events { get; set; }

    /// <summary>
    /// Raised once per batch that contains a HOLE — with the first sequence the stream does not have. The documented
    /// repair is the host's own: load a new snapshot and pass it (with its sequence) as <see cref="Snapshot"/>.
    /// </summary>
    [Parameter] public EventCallback<long> OnGapDetected { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Agent lanes"); the heading also names the
    /// timeline region, so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Which presentation is shown, two-way bound with <see cref="ViewModeChanged"/>. Default
    /// <see cref="AgentLanesViewMode.Timeline"/>; <see cref="AgentLanesViewMode.Table"/> is the text alternative with
    /// exactly the same data.
    /// </summary>
    [Parameter] public AgentLanesViewMode ViewMode { get; set; } = AgentLanesViewMode.Timeline;

    /// <summary>Fires with the new presentation when the reader uses the View switcher.</summary>
    [Parameter] public EventCallback<AgentLanesViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// The selected block's id, two-way bound with <see cref="SelectedBlockIdChanged"/> (<c>@bind-SelectedBlockId</c>).
    /// The matching block carries <c>aria-pressed="true"</c>; with <see cref="DetailTemplate"/> set the block's detail
    /// renders in a named region below the surface.
    /// </summary>
    [Parameter] public string? SelectedBlockId { get; set; }

    /// <summary>Fires with the new selected block id when the reader selects a block (a null value clears it).</summary>
    [Parameter] public EventCallback<string?> SelectedBlockIdChanged { get; set; }

    /// <summary>
    /// The host's detail surface for the selected block — the full activity text, links, actions. Rendered in a
    /// <c>role="region"</c> named by the localized "Block detail". Null renders no detail at all.
    /// </summary>
    [Parameter] public RenderFragment<AgentLaneBlock>? DetailTemplate { get; set; }

    /// <summary>
    /// Whether the zoom controls are rendered (timeline only). Default <c>true</c>. They are the WCAG 2.5.7
    /// alternative to the pinch/ctrl+wheel gesture, so a host is expected to keep them unless its own chrome zooms.
    /// </summary>
    [Parameter] public bool ShowZoom { get; set; } = true;

    /// <summary>
    /// The horizontal scale in PIXELS PER HOUR, two-way bound with <see cref="PixelsPerHourChanged"/>. Default 600
    /// (<see cref="DefaultPixelsPerHour"/>) — ten pixels per minute, the scale an agent run is read at.
    /// The value is clamped into <see cref="MinPixelsPerHour"/>–<see cref="MaxPixelsPerHour"/>; a host that pins it
    /// keeps control (the reader's zoom reports the change and the next render from the host decides).
    /// </summary>
    [Parameter] public double PixelsPerHour { get; set; } = DefaultPixelsPerHour;

    /// <summary>Fires with the new scale when the reader zooms (a button, "fit", or a pinch/ctrl+wheel gesture).</summary>
    [Parameter] public EventCallback<double> PixelsPerHourChanged { get; set; }

    /// <summary>The narrowest allowed scale in pixels per hour. Default 24.</summary>
    [Parameter] public double MinPixelsPerHour { get; set; } = 24;

    /// <summary>The widest allowed scale in pixels per hour. Default 2400.</summary>
    [Parameter] public double MaxPixelsPerHour { get; set; } = 2400;

    /// <summary>
    /// The height of one sub-row of a lane in pixels. Default 48, which leaves a 44 px block — the coarse-pointer
    /// target — inside every sub-row.
    /// </summary>
    [Parameter] public double RowHeight { get; set; } = 48;

    /// <summary>
    /// The width of the sticky agent column in pixels. Default 176. It is a parameter rather than a stylesheet-only
    /// value because "fit to width" subtracts it from the room the reader has.
    /// </summary>
    [Parameter] public double LabelWidth { get; set; } = 176;

    /// <summary>Whether the vertical "now" line is painted (timeline only). Default <c>true</c>.</summary>
    [Parameter] public bool ShowNowLine { get; set; } = true;

    /// <summary>Whether the handoff arrows are painted (timeline only). Default <c>true</c>; the table keeps them either way.</summary>
    [Parameter] public bool ShowHandoffs { get; set; } = true;

    /// <summary>
    /// The culture the axis, the block timestamps and the table format in. Null uses
    /// <see cref="CultureInfo.CurrentCulture"/>. It never changes the values, only their shape.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The clock the "now" line and the paint throttle measure on. Null uses the container's
    /// <see cref="TimeProvider"/> and falls back to <see cref="TimeProvider.System"/> — the host's clock always wins
    /// over the machine's.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// The paint-gate window for event-driven updates. Null uses the shared
    /// <see cref="AgentRenderThrottle.DefaultInterval"/> (100 ms); <see cref="TimeSpan.Zero"/> paints every batch.
    /// The STATE is never throttled, only the paint.
    /// </summary>
    [Parameter] public TimeSpan? RenderThrottle { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The snapshot the surface currently renders: the host's <see cref="Snapshot"/> plus every applied event.</summary>
    public AgentLanesSnapshot View => _view;

    /// <summary>The last layout pass, so the tests (and the component's own markup) read one geometry.</summary>
    internal AgentLanesLayoutResult Layout => _layout;

    /// <summary>The selected block from <see cref="View"/>, or null when nothing is selected (or it is gone).</summary>
    public AgentLaneBlock? SelectedBlock => _selectedBlockId is { } id
        ? _view.Blocks.FirstOrDefault(block => string.Equals(block.Id, id, StringComparison.Ordinal))
        : null;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAgentLanes_Title"] : Title!;

    /// <summary>The zoom level as a percentage of <see cref="DefaultPixelsPerHour"/>, for the visible read-out.</summary>
    public int ZoomPercent => (int)Math.Round(
        _pixelsPerHour / DefaultPixelsPerHour * 100,
        MidpointRounding.AwayFromZero);

    private string RootClass => string.IsNullOrWhiteSpace(Class)
        ? "tm-ai-agent-lanes"
        : "tm-ai-agent-lanes " + Class;

    private string RootStyle
    {
        get
        {
            var builder = new StringBuilder("--tm-agent-lanes-label-width:");
            builder.Append(Px(Math.Max(0, LabelWidth)));
            builder.Append(";--tm-agent-lanes-row-height:");
            builder.Append(Px(Math.Max(1, RowHeight)));
            builder.Append(';');
            return builder.ToString();
        }
    }

    private string ViewModeSlug => _viewMode == AgentLanesViewMode.Table ? "table" : "timeline";

    private string ZoomPercentText => Loc["TmAgentLanes_ZoomLevel", ZoomPercent.ToString(CultureInfo.CurrentCulture)];

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private string ArrowMarkerId => _id + "-arrow";

    private string WidthText => Px(_layout.WidthPx);

    private string WidthValue => Number(_layout.WidthPx);

    private string HeightValue => Number(_layout.HeightPx);

    private string CanvasStyle => $"inline-size: calc(var(--tm-agent-lanes-label-width) + {Px(_layout.WidthPx)});";

    private string NowOffsetText => $"calc(var(--tm-agent-lanes-label-width) + {Px(_layout.NowPx)})";

    /// <summary>
    /// The now label flips to the LEFT of the line when the line is close to the content's trailing edge, so the
    /// label can never stick out of the canvas and create a scrollbar the fit-to-width scale just removed.
    /// </summary>
    private string NowClass => _layout.WidthPx - _layout.NowPx < 80 ? "tm-ai-agent-lanes__now--end" : string.Empty;

    private IReadOnlyList<SegmentedOption<AgentLanesViewMode>> ViewOptions =>
    [
        new() { Label = Loc["TmAgentLanes_ViewTimeline"], Value = AgentLanesViewMode.Timeline },
        new() { Label = Loc["TmAgentLanes_ViewTable"], Value = AgentLanesViewMode.Table },
    ];

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _self = DotNetObjectReference.Create(this);
        _clock = ResolveClock();
        _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        _throttle = new AgentRenderThrottle(_clock, RenderThrottle);
        _interop = new AgentLanesInterop(JS);
        _viewMode = ViewMode;
        _lastViewMode = ViewMode;
        _pixelsPerHour = ClampZoom(PixelsPerHour);
        _lastPixelsPerHourParameter = PixelsPerHour;
        _selectedBlockId = SelectedBlockId;
        _lastSelectedParameter = SelectedBlockId;
        Recompute();

        // The FIRST paint is never gated by ShouldRender (Blazor does not ask before it), so the window is opened
        // here: a burst of events that lands right after the snapshot is collapsed into one trailing paint instead
        // of one render per event.
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The clock a host pins wins over the one captured at initialization.
        var clock = ResolveClock();
        if (!ReferenceEquals(clock, _clock))
        {
            _clock = clock;
            _ticker?.Dispose();
            _ticker = new AiElapsedTicker(_clock, OnTickAsync);
            _throttle = new AgentRenderThrottle(_clock, RenderThrottle);
        }

        var visualChange = ParametersChangedExceptEvents();

        // A host that changes a parameter wins; the reader's own choice survives an unrelated render.
        if (ViewMode != _lastViewMode)
        {
            _lastViewMode = ViewMode;
            _viewMode = ViewMode;
        }

        if (Math.Abs(PixelsPerHour - _lastPixelsPerHourParameter) > 0.001)
        {
            _lastPixelsPerHourParameter = PixelsPerHour;
            _pixelsPerHour = ClampZoom(PixelsPerHour);
        }

        if (!string.Equals(SelectedBlockId, _lastSelectedParameter, StringComparison.Ordinal))
        {
            _lastSelectedParameter = SelectedBlockId;
            _selectedBlockId = SelectedBlockId;
        }

        var appliedAny = ApplyEvents();
        Recompute();

        if (visualChange)
        {
            CancelTrailingPaint();
            PaintNow();
        }
        else if (appliedAny)
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

        // An event batch was held back by the paint gate; the trailing timer repaints when the window closes.
        return _throttle.ShouldRender();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Blazor never asks ShouldRender before the FIRST render, so the flag the first parameter set raised is
            // still set; clearing it here keeps the gate honest for the very first event-driven paint.
            _paintNow = false;
        }

        if (NeedsTicking)
        {
            _ticker?.Start();
        }
        else
        {
            _ticker?.Stop();
        }

        if (_viewMode != AgentLanesViewMode.Timeline || _layout.IsEmpty)
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

    private bool NeedsTicking
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            foreach (var block in _view.Blocks)
            {
                if (block is not null && block.IsOpen)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Folds the host's incremental batch into the view. The shared stream owns ordering, de-duplication and hole
    /// detection; this method only applies what it accepted, ON TOP of the view it already has, and forwards a hole
    /// to the host.
    /// <para>
    /// Only the NEWLY accepted events are folded in: the view keeps everything applied before, so a parameter set
    /// costs the size of the batch and not the length of the session (a replayed tail would grow without bound on a
    /// long-running surface). A new snapshot is the ONE reset — the view IS that snapshot, and the events after it
    /// are re-applied by the re-based stream.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> when at least one event was new.</returns>
    private bool ApplyEvents()
    {
        var snapshotChanged = !ReferenceEquals(Snapshot, _snapshotParameter)
            || !_hasSnapshot
            || (Snapshot is { } current && current.Sequence != _snapshotSequence);

        if (snapshotChanged)
        {
            _snapshotParameter = Snapshot;
            _snapshotSequence = Snapshot?.Sequence ?? 0;
            _hasSnapshot = true;
            _view = Snapshot ?? new AgentLanesSnapshot();
            _stream = new AgentEventStream<AgentLaneEvent>(static change => change.Sequence, Snapshot?.Sequence);
            _throttle.Reset();
            _gapAnnounced = false;
            _lastReportedGap = null;
        }

        var applied = 0;
        if (Events is { Count: > 0 } && _stream is not null)
        {
            var accepted = _stream.Apply(Events);
            if (accepted.Count > 0)
            {
                _view = _view.Apply(accepted);
                applied = accepted.Count;
            }

            if (_stream.HasGap)
            {
                RaiseGap(_stream.LastSequence);
            }
        }

        return applied > 0;
    }

    /// <summary>
    /// Reports a stream hole once per contiguous run: the host reloads the snapshot, and the surface says so to the
    /// reader instead of silently painting stale data.
    /// </summary>
    private void RaiseGap(long? lastSequence)
    {
        var expected = lastSequence is { } sequence ? sequence + 1 : 0;

        if (!_gapAnnounced)
        {
            _gapAnnounced = true;
            Announce(Loc["TmAgentLanes_Gap"]);
        }

        // The same hole is reported ONCE: a render that keeps handing the same stale batch must not ask the host to
        // reload on every parameter set (a reload-driven host would spin).
        if (OnGapDetected.HasDelegate && _lastReportedGap != expected)
        {
            _lastReportedGap = expected;
            _ = InvokeAsync(() => OnGapDetected.InvokeAsync(expected));
        }
    }

    /// <summary>Recomputes the layout of the current moment and the current zoom.</summary>
    private void Recompute()
    {
        _layout = AgentLanesLayout.Build(
            _view,
            _clock.GetUtcNow(),
            new AgentLanesLayoutOptions { PixelsPerHour = _pixelsPerHour, RowHeightPx = RowHeight });
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the block with <paramref name="blockId"/> — the same path a click, Enter or Space takes. A null id
    /// clears the selection; an id that names no supplied block still raises the callback (the host may know a block
    /// this render does not show).
    /// </summary>
    /// <param name="blockId">The block to select, or null to clear the selection.</param>
    /// <returns>A task that completes when the selection has been reported.</returns>
    public async Task SelectBlockAsync(string? blockId)
    {
        _selectedBlockId = blockId;
        _lastSelectedParameter = SelectedBlockId;

        if (blockId is not null && FindBlock(blockId) is { } block)
        {
            Announce(Loc["TmAgentLanes_Selected", BlockText(block)]);
        }

        await SelectedBlockIdChanged.InvokeAsync(blockId);
        Paint();
    }

    /// <summary>Zooms in by one step (×1.5), clamped into the allowed window.</summary>
    /// <returns>A task that completes when the new scale has been reported.</returns>
    public Task ZoomInAsync() => ApplyZoomAsync(_pixelsPerHour * ZoomStep, announce: true);

    /// <summary>Zooms out by one step (÷1.5), clamped into the allowed window.</summary>
    /// <returns>A task that completes when the new scale has been reported.</returns>
    public Task ZoomOutAsync() => ApplyZoomAsync(_pixelsPerHour / ZoomStep, announce: true);

    /// <summary>
    /// Fits the whole run into the room the reader has: the scale becomes the measured scrollport width (minus the
    /// sticky agent column) divided by the run's length. Where nothing can be measured (prerender, no JavaScript) the
    /// current zoom is kept instead of jumping to a guess.
    /// </summary>
    /// <returns>A task that completes when the new scale has been reported.</returns>
    public async Task FitToViewAsync()
    {
        var measured = _interop is null ? 0 : await _interop.MeasureWidthAsync(_viewportRef);
        if (measured <= 0)
        {
            Announce(Loc["TmAgentLanes_Zoom", ZoomPercentText]);
            return;
        }

        var usable = Math.Max(1, measured - Math.Max(0, LabelWidth));
        var hours = (_layout.RangeEnd - _layout.RangeStart).TotalHours;
        if (hours <= 0)
        {
            return;
        }

        var candidate = ClampZoom(usable / hours);

        // Zooming out may widen the range to the readable minimum, so the resulting content width is measured BEFORE
        // the scale is applied: the reader (and a host binding the value) sees ONE final level, never two.
        var projected = AgentLanesLayout.Build(
            _view,
            _layout.Now,
            new AgentLanesLayoutOptions { PixelsPerHour = candidate, RowHeightPx = RowHeight });

        if (projected.WidthPx > 0)
        {
            candidate = ClampZoom(candidate * (usable / projected.WidthPx));
        }

        await ApplyZoomAsync(candidate, announce: true);
    }

    // ── JS callbacks (pinch / ctrl+wheel) ────────────────────────────────────

    /// <summary>Called by the package's JS module when a pinch or ctrl+wheel gesture starts.</summary>
    /// <returns>A completed task; the gesture itself is owned by the browser.</returns>
    [JSInvokable]
    public Task OnLanesZoomGestureStartAsync()
    {
        _gestureBasePixelsPerHour = _pixelsPerHour;
        _gestureChanged = false;
        return Task.CompletedTask;
    }

    /// <summary>Called by the package's JS module with the gesture's scale relative to the level it started from.</summary>
    /// <param name="scale">The gesture's relative scale (1 = unchanged).</param>
    /// <returns>A completed task; the gesture itself is owned by the browser.</returns>
    [JSInvokable]
    public Task OnLanesZoomGestureChangeAsync(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return Task.CompletedTask;
        }

        var next = ClampZoom(_gestureBasePixelsPerHour * scale);
        if (Math.Abs(next - _pixelsPerHour) < 0.01)
        {
            return Task.CompletedTask;
        }

        _pixelsPerHour = next;
        _gestureChanged = true;
        Recompute();
        Paint();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the package's JS module when the gesture ends (or is cancelled). The reader's gesture is reported to
    /// the host here rather than on every tick, so a host binding the scale does not re-render mid-gesture.
    /// </summary>
    /// <returns>A task that completes when the scale has been reported to the host.</returns>
    [JSInvokable]
    public async Task OnLanesZoomGestureEndAsync()
    {
        if (!_gestureChanged)
        {
            return;
        }

        _gestureChanged = false;
        Announce(Loc["TmAgentLanes_Zoom", ZoomPercentText]);
        _lastPixelsPerHourParameter = PixelsPerHour;
        await PixelsPerHourChanged.InvokeAsync(_pixelsPerHour);
    }

    // ── Events and rendering ─────────────────────────────────────────────────

    private async Task OnViewModeChangedAsync(AgentLanesViewMode mode)
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
        if (Math.Abs(next - _pixelsPerHour) < 0.01)
        {
            if (announce)
            {
                Announce(Loc["TmAgentLanes_Zoom", ZoomPercentText]);
            }

            return;
        }

        _pixelsPerHour = next;
        _lastPixelsPerHourParameter = PixelsPerHour;
        Recompute();
        Paint();

        if (announce)
        {
            Announce(Loc["TmAgentLanes_Zoom", ZoomPercentText]);
        }

        if (PixelsPerHourChanged.HasDelegate)
        {
            await PixelsPerHourChanged.InvokeAsync(next);
        }
    }

    private double ClampZoom(double value)
    {
        var min = double.IsFinite(MinPixelsPerHour) && MinPixelsPerHour > 0 ? MinPixelsPerHour : 24;
        var max = double.IsFinite(MaxPixelsPerHour) && MaxPixelsPerHour > min
            ? MaxPixelsPerHour
            : Math.Max(min, 2400);

        return !double.IsFinite(value) || value <= 0 ? min : Math.Min(max, Math.Max(min, value));
    }

    /// <summary>The signature of every render-affecting parameter EXCEPT the event batch, whose effect is throttled.</summary>
    private RenderSignature CurrentSignature() => new(
        Snapshot,
        ViewMode,
        SelectedBlockId,
        PixelsPerHour,
        Title,
        ShowTitle,
        HeadingLevel,
        ShowZoom,
        ShowNowLine,
        ShowHandoffs,
        RowHeight,
        LabelWidth,
        DetailTemplate,
        Class,
        Culture);

    /// <summary>Whether a parameter other than <see cref="Events"/> changed since the previous render.</summary>
    private bool ParametersChangedExceptEvents()
    {
        var signature = CurrentSignature();
        if (_signature is { } previous && previous.Equals(signature))
        {
            return false;
        }

        _signature = signature;
        return true;
    }

    /// <summary>Requests a render the paint gate must let through, cancelling a pending trailing paint.</summary>
    private void Paint()
    {
        CancelTrailingPaint();
        PaintNow();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Forces the next render AND opens a new paint window, so a burst of events that lands right after a paint the
    /// component made for its own reason (a snapshot, a selection, a zoom, a live tick) is collapsed into one trailing
    /// paint instead of one render per event. The first render never asks <c>ShouldRender</c>, which is exactly why
    /// the window is opened here rather than there.
    /// </summary>
    private void PaintNow()
    {
        _throttle.Reset();
        _throttle.ShouldRender();
        _paintNow = true;
    }

    /// <summary>Applies the shared paint gate to an event-driven update, scheduling one trailing paint when held back.</summary>
    private void GatePaint()
    {
        if (_throttle.ShouldRender())
        {
            CancelTrailingPaint();
            _paintNow = true;
            return;
        }

        ScheduleTrailingPaint();
    }

    private void ScheduleTrailingPaint()
    {
        if (_disposed)
        {
            return;
        }

        var delay = _throttle.DelayUntilNextRender();
        if (delay <= TimeSpan.Zero)
        {
            _paintNow = true;
            return;
        }

        CancelTrailingPaint();
        _trailingPaint = _clock.CreateTimer(
            OnTrailingPaint,
            null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private void CancelTrailingPaint()
    {
        _trailingPaint?.Dispose();
        _trailingPaint = null;
    }

    private void OnTrailingPaint(object? state)
    {
        CancelTrailingPaint();
        if (_disposed)
        {
            return;
        }

        _throttle.Reset();
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            _paintNow = true;
            Recompute();
            StateHasChanged();
        });
    }

    // ── Clock ────────────────────────────────────────────────────────────────

    private async Task OnTickAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                Recompute();
                PaintNow();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer went away between the tick and the hop.
        }
        catch (InvalidOperationException)
        {
            // The renderer has already been torn down.
        }
    }

    // ── Presentation helpers ─────────────────────────────────────────────────

    private AgentLaneBlock? FindBlock(string blockId)
        => _view.Blocks.FirstOrDefault(block => string.Equals(block.Id, blockId, StringComparison.Ordinal));

    private AgentLane? LaneOf(int laneIndex)
        => laneIndex >= 0 && laneIndex < _layout.Lanes.Count ? _layout.Lanes[laneIndex] : null;

    private string LaneAgentName(int laneIndex)
        => LaneOf(laneIndex) is { } lane ? AgentName(lane.Agent) : Loc["TmAgentBadge_NameFallback"];

    private string AgentName(AgentRef agent) => agent.HasName ? agent.Name : Loc["TmAgentBadge_NameFallback"];

    private string BlockText(AgentLaneBlock block)
        => string.IsNullOrWhiteSpace(block.Label)
            ? AgentLanePresentation.KindText(Loc, block.Kind)
            : block.Label;

    private string? BlockTitle(AgentLaneBlock block)
        => string.IsNullOrWhiteSpace(block.Label) ? null : block.Label;

    private string? StatusSlug(AgentLaneBlock block)
        => AgentStatusPresentation.IsKnown(block.Status) ? AgentStatusPresentation.Tone(block.Status) : null;

    private string? MarkerSlug(AgentLaneBlock block)
        => block.Marker is { } marker && AgentLanePresentation.IsKnownMarker(marker)
            ? AgentLanePresentation.MarkerSlug(marker)
            : null;

    private string StatusText(AgentActivityStatus status)
        => AgentStatusPresentation.IsKnown(status)
            ? AgentStatusPresentation.Text(Loc, status)
            : Loc["TmAgentLanes_StatusUnknown"];

    private string BlockAriaLabel(AgentLanesBlockPlacement placement)
    {
        var baseText = Loc[
            "TmAgentLanes_BlockAriaLabel",
            LaneAgentName(placement.LaneIndex),
            BlockText(placement.Block),
            AgentLanePresentation.KindText(Loc, placement.Block.Kind),
            Instant(placement.Block.Start),
            placement.Block.IsOpen ? Loc["TmAgentLanes_Now"] : Instant(placement.Block.End!.Value),
            StatusText(placement.Block.Status)];

        return placement.Block.Marker is { } marker && AgentLanePresentation.IsKnownMarker(marker)
            ? Loc["TmAgentLanes_BlockAriaLabelMarked", baseText, AgentLanePresentation.MarkerText(Loc, marker)]
            : baseText;
    }

    private bool IsSelected(string blockId)
        => string.Equals(_selectedBlockId, blockId, StringComparison.Ordinal);

    private string BlockClass(AgentLanesBlockPlacement placement)
    {
        var builder = new StringBuilder("tm-ai-agent-lanes__block");

        if (StatusSlug(placement.Block) is { } statusSlug)
        {
            builder.Append(" tm-ai-agent-lanes__block--").Append(statusSlug);
        }

        if (placement.Block.IsOpen)
        {
            builder.Append(" tm-ai-agent-lanes__block--open");
        }

        if (IsSelected(placement.Block.Id))
        {
            builder.Append(" tm-ai-agent-lanes__block--selected");
        }

        return builder.ToString();
    }

    private string BlockStyle(AgentLanesBlockPlacement placement)
        => $"inset-inline-start:{Px(placement.LeftPx)};inline-size:{Px(placement.WidthPx)};inset-block-start:{Px(placement.RowIndex * RowHeight)};";

    private string Points(AgentHandoffLine line)
        => string.Join(' ', line.Points.Select(point => Number(point.X) + "," + Number(point.Y)));

    private string Instant(DateTimeOffset value)
        => value.ToString(FormatCulture.DateTimeFormat.ShortTimePattern, FormatCulture);

    private string TickText(AgentLanesTick tick)
    {
        var format = FormatCulture.DateTimeFormat;
        var pattern = _layout.Unit switch
        {
            AgentLanesTickUnit.Seconds => format.LongTimePattern,
            AgentLanesTickUnit.Days => format.ShortDatePattern + " " + format.ShortTimePattern,
            _ when _layout.SpansMultipleDays => format.ShortDatePattern + " " + format.ShortTimePattern,
            _ => format.ShortTimePattern,
        };

        return tick.Time.ToString(pattern, FormatCulture);
    }

    /// <summary>
    /// The state a lane header reports: the verdict that matters RIGHT NOW. Work that has not started yet is ignored
    /// (a queued block in the future must not label a finished agent as "queued"), a waiting approval wins over
    /// everything because nothing moves until the reader acts, then an open block means running, and otherwise the
    /// latest finished block's outcome is what the agent last did.
    /// </summary>
    private AgentActivityStatus? LaneStatus(AgentLane lane)
    {
        var now = _layout.Now;
        var started = lane.Blocks
            .Where(placement => placement.Block.Start <= now)
            .ToList();

        if (started.Count == 0)
        {
            return lane.Blocks.Count == 0 ? null : lane.Blocks.MinBy(placement => placement.Block.Start)!.Block.Status;
        }

        if (started.Any(placement => placement.Block.Status == AgentActivityStatus.WaitingForApproval))
        {
            return AgentActivityStatus.WaitingForApproval;
        }

        if (started.Any(placement => placement.Block.IsOpen))
        {
            return AgentActivityStatus.Running;
        }

        return started.MaxBy(placement => placement.EffectiveEnd)!.Block.Status;
    }

    private string HandoffFromText(AgentHandoffLink handoff)
    {
        if (!string.IsNullOrWhiteSpace(handoff.FromBlockId)
            && FindBlock(handoff.FromBlockId) is { } block)
        {
            return BlockText(block);
        }

        if (!string.IsNullOrWhiteSpace(handoff.FromAgentId))
        {
            return _view.Agents.FirstOrDefault(agent =>
                string.Equals(agent.Id, handoff.FromAgentId, StringComparison.Ordinal)) is { } agent
                ? AgentName(agent)
                : handoff.FromAgentId!;
        }

        return Loc["TmAgentLanes_UnknownSource"];
    }

    private string HandoffToText(AgentHandoffLink handoff)
        => _view.Agents.FirstOrDefault(agent => string.Equals(agent.Id, handoff.ToAgentId, StringComparison.Ordinal))
            is { } agent
            ? AgentName(agent)
            : handoff.ToAgentId;

    private string Px(double value) => Number(value) + "px";

    private static string Number(double value)
        => double.IsFinite(value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : "0";

    /// <summary>
    /// Replaces the live region's text. Sticky (never cleared on a re-render, which could wipe an announcement before
    /// assistive technology read it); when two consecutive transitions would write the same sentence, one invisible
    /// marker character is toggled so the region still sees a mutation.
    /// </summary>
    private void Announce(string message)
    {
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelTrailingPaint();
        _ticker?.Dispose();
        _ticker = null;
        _self?.Dispose();
        _self = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Dispose();

        if (_interop is { } interop)
        {
            _interop = null;
            await interop.DisposeAsync();
        }
    }

    /// <summary>
    /// The value signature of every parameter that changes what the surface paints, so an event batch is the ONE thing
    /// the paint gate may hold back while a snapshot, a selection or a zoom change always paints immediately.
    /// </summary>
    private readonly record struct RenderSignature(
        AgentLanesSnapshot? Snapshot,
        AgentLanesViewMode ViewMode,
        string? SelectedBlockId,
        double PixelsPerHour,
        string? Title,
        bool ShowTitle,
        int HeadingLevel,
        bool ShowZoom,
        bool ShowNowLine,
        bool ShowHandoffs,
        double RowHeight,
        double LabelWidth,
        RenderFragment<AgentLaneBlock>? DetailTemplate,
        string? Class,
        CultureInfo? Culture);
}
