using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Observability;

/// <summary>
/// A generic span waterfall for traces: one row per span with its tree indentation, a bar on a shared
/// time axis, the duration and any host-declared metric columns. The layout maths lives in the pure
/// <see cref="TraceWaterfallLayout"/>; this component owns the interaction (treegrid semantics, roving
/// tabindex, zoom, selection and the live ticker) and the rendering.
/// </summary>
/// <remarks>
/// <para>
/// The public surface is the <see cref="Spans"/> list and the two-way <see cref="SelectedSpanId"/>
/// plus the optional <see cref="DetailTemplate"/>. Everything visual is derived: bar offsets come from
/// the shared axis, the slowest LEAF span gets a localized badge, an orphan (a span whose parent is
/// missing or rejected) is attached under the root, and a cycle is reported by the layout instead of
/// hanging the render.
/// </para>
/// <para>
/// Accessibility: the surface is a div-based treegrid (<c>role="treegrid"</c>) with
/// <c>role="row"</c>/<c>columnheader</c>/<c>gridcell</c> children, <c>aria-level</c> and
/// <c>aria-expanded</c> on every row, a roving <c>tabindex</c>, ←/→ to collapse/expand (← also walks
/// to the parent), ↑/↓ to move, Home/End, Enter and Space to select. The keys a row handles do not
/// also scroll the page: a per-key cancel in the <c>tm-trace-waterfall.js</c> module suppresses only
/// those defaults (a render-time <c>@onkeydown:preventDefault</c> cannot be per key and would swallow
/// Tab), so Tab, Enter and the disclosure's native Space keep their behaviour. Colour is never the only
/// carrier of information: every row carries an icon plus a visually hidden kind name, an error carries
/// an icon AND localized text, and the one polite live region is written only from a user action —
/// never from a live tick.
/// </para>
/// </remarks>
public partial class TmTraceWaterfall : TmComponentBase, IDisposable
{
    /// <summary>
    /// The row count above which the body switches to <see cref="Microsoft.AspNetCore.Components.Web.Virtualize{TItem}"/>.
    /// A waterfall of a thousand spans must not put a thousand rows in the DOM on every tick.
    /// </summary>
    internal const int VirtualizeThreshold = 200;

    /// <summary>The largest zoom-in step, as a fraction of the trace (1 % of the range).</summary>
    private const double MinimumZoomWidth = 0.01;

    /// <summary>The layout of the current render (rows, axis, slowest span, rejected cycles).</summary>
    private TraceWaterfallLayoutResult _layout = TraceWaterfallLayoutResult.Empty(DateTimeOffset.MinValue);

    /// <summary>The rows currently visible — the layout rows minus everything below a collapsed row.</summary>
    private List<TraceWaterfallRow> _visibleRows = [];

    /// <summary>The sanitized metric columns of the current render.</summary>
    private List<TraceMetricColumn> _metricColumns = [];

    /// <summary>The ids the user collapsed. Empty means every subtree is expanded.</summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>
    /// One <see cref="ElementReference"/> slot per row of the FULL tree (keyed by
    /// <see cref="TraceWaterfallRow.Index"/>), so the roving tabindex can move real DOM focus without
    /// any custom JavaScript.
    /// </summary>
    private readonly List<ElementReference> _rowRefs = [];

    /// <summary>The row that owns the roving tabindex (the only row with <c>tabindex="0"</c>).</summary>
    private string? _activeSpanId;

    /// <summary>The row index to focus in the next render, or null when no focus move is pending.</summary>
    private int? _pendingFocusIndex;

    /// <summary>The span the detail panel currently shows (resolved from <see cref="SelectedSpanId"/>).</summary>
    private TraceSpan? _selectedSpan;

    /// <summary>The start of the visible window, as a fraction of the trace.</summary>
    private double _zoomStart;

    /// <summary>The width of the visible window, as a fraction of the trace.</summary>
    private double _zoomWidth = 1;

    /// <summary>The text of the single polite live region; written only by a user action.</summary>
    private string _announcement = string.Empty;

    /// <summary>The live-mode timer, or null when the component does not tick.</summary>
    private ITimer? _timer;

    /// <summary>The interval the current timer was created with, so a changed interval restarts it.</summary>
    private TimeSpan _timerInterval;

    /// <summary>
    /// The clock the current timer was created from. A host may swap <see cref="TimeProvider"/> at
    /// runtime (a test, or a host upgrading to a real clock after prerender); the interval alone does
    /// not identify the timer, so the provider is compared by reference too and a swap restarts it —
    /// otherwise the replaced clock's timer would keep ticking against the new clock's "now".
    /// </summary>
    private TimeProvider? _timerProvider;

    /// <summary>Set by <see cref="Dispose"/> so a late timer callback becomes a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// The treegrid element, used only as the attachment point of the scroll-key guard (the grid is
    /// the one element every row's key event bubbles through).
    /// </summary>
    private ElementReference _gridRef;

    /// <summary>
    /// The <see cref="ElementReference.Id"/> the scroll-key guard is currently attached to, so a
    /// render that keeps the same grid does not re-enter JavaScript, while a grid that the empty state
    /// removed and re-created (a new element) does get a fresh guard.
    /// </summary>
    private string? _guardedGridId;

    /// <summary>The scroll-key guard's JavaScript module (see <see cref="TraceWaterfallInterop"/>).</summary>
    private TraceWaterfallInterop? _interop;

    /// <summary>
    /// The spans of the trace, as a FLAT list. The tree is built from <see cref="TraceSpan.ParentId"/>:
    /// a missing parent attaches the span under the root as an orphan, and a parent cycle is rejected
    /// (reported by the layout, never thrown).
    /// </summary>
    [Parameter]
    public IReadOnlyList<TraceSpan> Spans { get; set; } = [];

    /// <summary>
    /// An optional heading above the waterfall. It also becomes the grid's accessible name when
    /// <see cref="AriaLabel"/> is not set.
    /// </summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>
    /// The accessible name of the <c>treegrid</c>. Null falls back to the title, then to the localized
    /// default ("Trace waterfall").
    /// </summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>
    /// The selected span's id, two-way bound with <see cref="SelectedSpanIdChanged"/>
    /// (<c>@bind-SelectedSpanId</c>). The matching row carries <c>aria-selected="true"</c> and the
    /// detail panel renders (when <see cref="DetailTemplate"/> is supplied).
    /// </summary>
    [Parameter]
    public string? SelectedSpanId { get; set; }

    /// <summary>Fires with the newly selected span id (click, Enter or Space on a row).</summary>
    [Parameter]
    public EventCallback<string?> SelectedSpanIdChanged { get; set; }

    /// <summary>
    /// The detail panel of the selected span, placed to the RIGHT of the grid on a wide container and
    /// BELOW it on a narrow one (the placement is a container query on the component's own root — the
    /// viewport is never consulted).
    /// </summary>
    [Parameter]
    public RenderFragment<TraceSpan>? DetailTemplate { get; set; }

    /// <summary>
    /// The numeric metric columns (key, label, .NET format string) rendered after the duration. A
    /// column whose key no span reports renders an empty cell; a span that has children also shows the
    /// subtree sum, so "this call" and "this call and everything under it" are both visible.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TraceMetricColumn> MetricColumns { get; set; } = [];

    /// <summary>
    /// The culture that formats the durations, the axis ticks and the metric values. Null (the default)
    /// means the current culture at render time.
    /// </summary>
    [Parameter]
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// When true the component redraws on every <see cref="LiveTickInterval"/> WHILE some span is still
    /// running, so its bar grows. A tick with nothing running redraws nothing.
    /// </summary>
    [Parameter]
    public bool LiveMode { get; set; }

    /// <summary>
    /// The live-mode redraw interval. Default one second; a non-positive value falls back to one second
    /// (a zero period would spin).
    /// </summary>
    [Parameter]
    public TimeSpan LiveTickInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The clock the component reads "now" from and schedules the live ticker with. Default
    /// <see cref="System.TimeProvider.System"/>; a host (and the tests) can pass a fake one.
    /// </summary>
    [Parameter]
    public TimeProvider TimeProvider { get; set; } = System.TimeProvider.System;

    /// <summary>
    /// The maximum block size of the scrolling grid, as a CSS length. Default <c>"420px"</c>; an empty
    /// value leaves it to the stylesheet. The header sticks to the top of this area, so it is counted
    /// in the height rather than scrolling away above the rows.
    /// </summary>
    [Parameter]
    public string? MaxHeight { get; set; } = "420px";

    /// <summary>Renders the zoom group (in, out, reset). Default <see langword="true"/>.</summary>
    [Parameter]
    public bool ShowZoom { get; set; } = true;

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>The JS runtime the scroll-key guard module is imported from.</summary>
    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    /// <summary>The culture the durations and metric values are formatted with.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>The root classes: the base class plus the host's own.</summary>
    private string RootClass
        => string.IsNullOrEmpty(Class) ? "tm-trace-waterfall" : "tm-trace-waterfall " + Class;

    /// <summary>
    /// The root's inline custom properties. The track SIZES come from the
    /// <c>--tm-trace-waterfall-*</c> knobs the stylesheet declares (and its narrow-container query
    /// tightens), so a phone gets a tighter grid without this class knowing any pixel value; only the
    /// track COUNT is decided here (a host supplies any number of metric columns).
    /// <para>
    /// The second property is the template's total inline size. The header is its OWN grid and every
    /// row is another one, so they line up only while all of them have the SAME width — with
    /// content-sized boxes a long span label made one row wider than the next and the columns drifted.
    /// The header and the rowgroup therefore size themselves from this one number (never below 100 % of
    /// the grid, so a wide host still fills it), and the grid scrolls exactly the template's overflow.
    /// </para>
    /// </summary>
    private string RootStyle
    {
        get
        {
            var columns = new List<string>
            {
                "minmax(var(--tm-trace-waterfall-name-width), 2fr)",
                "minmax(var(--tm-trace-waterfall-timeline-width), 3fr)",
                "var(--tm-trace-waterfall-duration-width)"
            };
            var tracks = new List<string>
            {
                "var(--tm-trace-waterfall-name-width)",
                "var(--tm-trace-waterfall-timeline-width)",
                "var(--tm-trace-waterfall-duration-width)"
            };
            for (var index = 0; index < _metricColumns.Count; index++)
            {
                columns.Add("var(--tm-trace-waterfall-metric-width)");
                tracks.Add("var(--tm-trace-waterfall-metric-width)");
            }

            // One --tm-space-2 gap between every adjacent pair of tracks (columns - 1).
            var templateWidth = $"calc({string.Join(" + ", tracks)} + {tracks.Count - 1} * var(--tm-space-2))";

            return $"--tm-trace-waterfall-columns:{string.Join(' ', columns)};"
                + $"--tm-trace-waterfall-template-width:{templateWidth};";
        }
    }

    /// <summary>
    /// The scrolling grid's inline maximum block size (null when the host blanked
    /// <see cref="MaxHeight"/>). It sits on the treegrid, not on the rowgroup: the grid is the single
    /// scroll owner in BOTH axes, which is what keeps the sticky header over the scrolling rows.
    /// </summary>
    private string? ScrollStyle
        => string.IsNullOrWhiteSpace(MaxHeight) ? null : $"max-block-size:{MaxHeight}";

    /// <summary>The grid's accessible name: the host's label, else the title, else the localized default.</summary>
    private string ResolvedAriaLabel
        => !string.IsNullOrWhiteSpace(AriaLabel) ? AriaLabel!
        : !string.IsNullOrWhiteSpace(Title) ? Title!
        : Loc["TmTraceWaterfall_AriaLabel"];

    /// <summary>Whether zooming further in is possible.</summary>
    private bool CanZoomIn => _zoomWidth > MinimumZoomWidth;

    /// <summary>Whether the window is narrower than the whole trace.</summary>
    private bool IsZoomed => _zoomWidth < 1 || _zoomStart > 0;

    /// <summary>Whether zooming back out is possible.</summary>
    private bool CanZoomOut => IsZoomed;

    /// <summary>The localized text of the visible window ("Window: {duration}"), i.e. the zoom level.</summary>
    private string ZoomWindowText
        => Loc["TmTraceWaterfall_ZoomWindow", FormatDurationOf(_layout.Axis.Duration)];

    /// <summary>
    /// The toolbar summary: the row count, the trace's total duration and the error count. Built from
    /// the full trace (not the zoomed window), so zooming never changes what the trace IS.
    /// </summary>
    private string Summary => Loc[
        "TmTraceWaterfall_Summary",
        _layout.Rows.Count,
        FormatDurationOf(_layout.TotalDuration),
        _layout.ErrorCount];

    /// <summary>
    /// Recomputes the layout from the current parameters and the clock. Called on every parameter set
    /// and on every live tick, which is why it reads "now" itself instead of taking it from a caller.
    /// </summary>
    protected override void OnParametersSet()
    {
        Rebuild();
    }

    /// <summary>
    /// Applies the work that needs a real DOM: a pending row focus (the row's <c>tabindex</c> and its
    /// element reference only exist after a render), the scroll-key guard on the grid, and the live
    /// timer matching the current parameters.
    /// </summary>
    /// <param name="firstRender">True on the first render.</param>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed)
        {
            return;
        }

        if (_pendingFocusIndex is { } focusIndex)
        {
            _pendingFocusIndex = null;
            await FocusRowAsync(focusIndex);
        }

        await SyncScrollKeyGuardAsync();
        SyncLiveTimer();
    }

    /// <summary>
    /// (Re)attaches the one scroll-key guard the grid needs, and only when the element behind
    /// <see cref="_gridRef"/> actually changed: the renderer replaces the grid when the empty state
    /// takes over (or gives it back), while an ordinary render — including every live tick — keeps the
    /// same element and must not pay an interop round trip.
    /// </summary>
    private async Task SyncScrollKeyGuardAsync()
    {
        var id = _visibleRows.Count > 0 ? _gridRef.Id : null;
        if (string.IsNullOrEmpty(id))
        {
            _guardedGridId = null;
            return;
        }

        if (string.Equals(id, _guardedGridId, StringComparison.Ordinal))
        {
            return;
        }

        _guardedGridId = id;
        _interop ??= new TraceWaterfallInterop(JS);
        await _interop.AttachAsync(_gridRef);
    }

    /// <summary>
    /// Rebuilds the layout and the visible rows. Kept in one place so the parameter set, the zoom and
    /// the live tick all produce exactly the same state.
    /// </summary>
    private void Rebuild()
    {
        _metricColumns = (MetricColumns ?? [])
            .Where(column => column is not null && !string.IsNullOrWhiteSpace(column.Key))
            .ToList();

        _layout = TraceWaterfallLayout.Build(
            Spans,
            TimeProvider.GetUtcNow(),
            new TraceWaterfallWindow(_zoomStart, _zoomWidth));

        while (_rowRefs.Count < _layout.Rows.Count)
        {
            _rowRefs.Add(default);
        }

        _visibleRows = BuildVisibleRows();
        if (_activeSpanId is null
            || !_visibleRows.Exists(row => string.Equals(row.Span.Id, _activeSpanId, StringComparison.Ordinal)))
        {
            _activeSpanId = _visibleRows.Count > 0 ? _visibleRows[0].Span.Id : null;
        }

        _selectedSpan = string.IsNullOrEmpty(SelectedSpanId)
            ? null
            : _layout.Rows.FirstOrDefault(row =>
                string.Equals(row.Span.Id, SelectedSpanId, StringComparison.Ordinal))?.Span;
    }

    /// <summary>
    /// The layout rows minus every row below a COLLAPSED row. Pre-order means a hidden subtree is a
    /// contiguous run of deeper rows, so one depth marker is enough — no ancestor walk per row.
    /// </summary>
    private List<TraceWaterfallRow> BuildVisibleRows()
    {
        var visible = new List<TraceWaterfallRow>(_layout.Rows.Count);
        var hiddenBelowDepth = int.MaxValue;
        foreach (var row in _layout.Rows)
        {
            if (hiddenBelowDepth != int.MaxValue && row.Depth <= hiddenBelowDepth)
            {
                hiddenBelowDepth = int.MaxValue;
            }

            if (hiddenBelowDepth != int.MaxValue)
            {
                continue;
            }

            visible.Add(row);
            if (row.HasChildren && _collapsed.Contains(row.Span.Id))
            {
                hiddenBelowDepth = row.Depth;
            }
        }

        return visible;
    }

    /// <summary>Whether the row owns the roving tabindex.</summary>
    /// <param name="row">The row.</param>
    private bool IsActive(TraceWaterfallRow row)
        => string.Equals(_activeSpanId, row.Span.Id, StringComparison.Ordinal);

    /// <summary>Whether the row is the selected span.</summary>
    /// <param name="row">The row.</param>
    private bool IsSelected(TraceWaterfallRow row)
        => string.Equals(SelectedSpanId, row.Span.Id, StringComparison.Ordinal);

    /// <summary>Whether the row's subtree is collapsed.</summary>
    /// <param name="spanId">The span id.</param>
    private bool IsCollapsed(string spanId) => _collapsed.Contains(spanId);

    /// <summary>The row classes: base, plus the selected/error/slowest modifiers.</summary>
    /// <param name="row">The row.</param>
    private string RowClass(TraceWaterfallRow row)
    {
        var classes = "tm-trace-waterfall__row";
        if (IsSelected(row))
        {
            classes += " tm-trace-waterfall__row--selected";
        }

        if (row.Span.Status == TraceSpanStatus.Error)
        {
            classes += " tm-trace-waterfall__row--error";
        }

        if (row.IsSlowest)
        {
            classes += " tm-trace-waterfall__row--slowest";
        }

        return classes;
    }

    /// <summary>The name cell's inline custom property: the tree depth the indent is computed from.</summary>
    /// <param name="row">The row.</param>
    private static string IndentStyle(TraceWaterfallRow row)
        => $"--tm-trace-waterfall-depth:{row.Depth.ToString(CultureInfo.InvariantCulture)};";

    /// <summary>The bar's inline position: its offset and width as a percentage of the visible axis.</summary>
    /// <param name="row">The row.</param>
    private static string BarStyle(TraceWaterfallRow row)
        => $"--tm-trace-waterfall-offset:{row.OffsetPercent.ToString("0.###", CultureInfo.InvariantCulture)}%;"
           + $"--tm-trace-waterfall-width:{row.WidthPercent.ToString("0.###", CultureInfo.InvariantCulture)}%;";

    /// <summary>The tick's inline position along the axis.</summary>
    /// <param name="tick">The tick.</param>
    private static string TickStyle(TraceWaterfallAxisTick tick)
        => $"--tm-trace-waterfall-tick:{tick.Percent.ToString("0.###", CultureInfo.InvariantCulture)}%;";

    /// <summary>
    /// The bar's class, one static modifier per kind (a static literal rather than an interpolated
    /// suffix, so the class-coverage sweep can verify every modifier has a rule).
    /// </summary>
    /// <param name="kind">The span kind.</param>
    private static string BarClass(TraceSpanKind kind) => kind switch
    {
        TraceSpanKind.Llm => "tm-trace-waterfall__bar tm-trace-waterfall__bar--llm",
        TraceSpanKind.Tool => "tm-trace-waterfall__bar tm-trace-waterfall__bar--tool",
        TraceSpanKind.Agent => "tm-trace-waterfall__bar tm-trace-waterfall__bar--agent",
        TraceSpanKind.Http => "tm-trace-waterfall__bar tm-trace-waterfall__bar--http",
        TraceSpanKind.Db => "tm-trace-waterfall__bar tm-trace-waterfall__bar--db",
        _ => "tm-trace-waterfall__bar tm-trace-waterfall__bar--custom"
    };

    /// <summary>The kind glyph's class — the same kind colour the bar paints, so icon and bar agree.</summary>
    /// <param name="kind">The span kind.</param>
    private static string KindIconClass(TraceSpanKind kind) => kind switch
    {
        TraceSpanKind.Llm => "tm-trace-waterfall__kind tm-trace-waterfall__kind--llm",
        TraceSpanKind.Tool => "tm-trace-waterfall__kind tm-trace-waterfall__kind--tool",
        TraceSpanKind.Agent => "tm-trace-waterfall__kind tm-trace-waterfall__kind--agent",
        TraceSpanKind.Http => "tm-trace-waterfall__kind tm-trace-waterfall__kind--http",
        TraceSpanKind.Db => "tm-trace-waterfall__kind tm-trace-waterfall__kind--db",
        _ => "tm-trace-waterfall__kind tm-trace-waterfall__kind--custom"
    };

    /// <summary>The status chip's class.</summary>
    /// <param name="status">The span status.</param>
    private static string StatusClass(TraceSpanStatus status) => status switch
    {
        TraceSpanStatus.Error => "tm-trace-waterfall__status tm-trace-waterfall__status--error",
        TraceSpanStatus.Running => "tm-trace-waterfall__status tm-trace-waterfall__status--running",
        _ => "tm-trace-waterfall__status tm-trace-waterfall__status--cancelled"
    };

    /// <summary>The icon of a kind — never the only carrier: the hidden kind name sits right next to it.</summary>
    /// <param name="kind">The span kind.</param>
    private static string KindIcon(TraceSpanKind kind) => kind switch
    {
        TraceSpanKind.Llm => Components.Icons.IconNames.WandSparkles,
        TraceSpanKind.Tool => Components.Icons.IconNames.Settings,
        TraceSpanKind.Agent => Components.Icons.IconNames.Users,
        TraceSpanKind.Http => Components.Icons.IconNames.Globe,
        TraceSpanKind.Db => Components.Icons.IconNames.Database,
        _ => Components.Icons.IconNames.Box
    };

    /// <summary>The icon of a rendered status.</summary>
    /// <param name="status">The span status.</param>
    private static string StatusIcon(TraceSpanStatus status) => status switch
    {
        TraceSpanStatus.Error => Components.Icons.IconNames.AlertCircle,
        TraceSpanStatus.Running => Components.Icons.IconNames.Clock,
        _ => Components.Icons.IconNames.Ban
    };

    /// <summary>The localized name of a kind, rendered visually hidden next to its icon.</summary>
    /// <param name="kind">The span kind.</param>
    private string KindLabel(TraceSpanKind kind) => Loc["TmTraceWaterfall_Kind" + kind];

    /// <summary>The localized name of a status, rendered next to its icon.</summary>
    /// <param name="status">The span status.</param>
    private string StatusLabel(TraceSpanStatus status) => Loc["TmTraceWaterfall_Status" + status];

    /// <summary>The row's visible name: the span's name, falling back to its id so a row is never nameless.</summary>
    /// <param name="row">The row.</param>
    private static string RowLabel(TraceWaterfallRow row)
        => string.IsNullOrWhiteSpace(row.Span.Name) ? row.Span.Id : row.Span.Name;

    /// <summary>The disclosure button's accessible name ("Expand/Collapse {span}").</summary>
    /// <param name="row">The row.</param>
    private string ToggleLabel(TraceWaterfallRow row) => Loc[
        IsCollapsed(row.Span.Id) ? "TmTraceWaterfall_Expand" : "TmTraceWaterfall_Collapse",
        RowLabel(row)];

    /// <summary>The column header: the host's label, falling back to the key when it is blank.</summary>
    /// <param name="column">The column.</param>
    private static string ResolvedColumnLabel(TraceMetricColumn column)
        => string.IsNullOrWhiteSpace(column.Label) ? column.Key : column.Label;

    /// <summary>
    /// Formats a duration with the culture's separators and the unit it reads best in ("820 ms",
    /// "1.24 s", "2.5 min"); the unit label comes from the resources, never from this class.
    /// </summary>
    /// <param name="value">The duration.</param>
    private string FormatDurationOf(TimeSpan value)
    {
        var (text, unit) = TraceWaterfallFormat.FormatDuration(value, EffectiveCulture);
        return $"{text} {Loc[TraceWaterfallFormat.UnitKey(unit)]}";
    }

    /// <summary>An axis tick's label: the numeric offset plus the localized unit of the axis.</summary>
    /// <param name="tick">The tick.</param>
    private string AxisTickText(TraceWaterfallAxisTick tick)
        => $"{_layout.Axis.FormatTick(tick, EffectiveCulture)} {Loc[TraceWaterfallFormat.UnitKey(_layout.Axis.Unit)]}";

    /// <summary>Formats one metric value in a column's format.</summary>
    /// <param name="value">The value.</param>
    /// <param name="column">The column that owns the format.</param>
    private string FormatMetric(double value, TraceMetricColumn column)
        => TraceWaterfallFormat.FormatMetric(value, column.Format, EffectiveCulture);

    /// <summary>Selects a row (click, Enter or Space) and raises <see cref="SelectedSpanIdChanged"/>.</summary>
    /// <param name="row">The row selected.</param>
    private async Task SelectAsync(TraceWaterfallRow row)
    {
        if (string.Equals(SelectedSpanId, row.Span.Id, StringComparison.Ordinal))
        {
            return;
        }

        SelectedSpanId = row.Span.Id;
        _selectedSpan = row.Span;
        _activeSpanId = row.Span.Id;
        _announcement = Loc["TmTraceWaterfall_Selected", RowLabel(row)];
        await SelectedSpanIdChanged.InvokeAsync(row.Span.Id);
    }

    /// <summary>
    /// Expands or collapses a row's subtree and announces the new state. The work is synchronous (the
    /// render the event handler triggers paints it), so it returns a completed task instead of an
    /// <c>async</c> method with nothing to await.
    /// </summary>
    /// <param name="row">The row toggled.</param>
    private Task ToggleAsync(TraceWaterfallRow row)
    {
        if (!row.HasChildren)
        {
            return Task.CompletedTask;
        }

        var expanded = _collapsed.Remove(row.Span.Id);
        if (!expanded)
        {
            _collapsed.Add(row.Span.Id);
        }

        _visibleRows = BuildVisibleRows();
        if (_activeSpanId is null
            || !_visibleRows.Exists(visibleRow => string.Equals(visibleRow.Span.Id, _activeSpanId, StringComparison.Ordinal)))
        {
            _activeSpanId = _visibleRows.Count > 0 ? _visibleRows[0].Span.Id : null;
        }

        _announcement = Loc[
            expanded ? "TmTraceWaterfall_Expanded" : "TmTraceWaterfall_Collapsed",
            RowLabel(row)];
        return Task.CompletedTask;
    }

    /// <summary>
    /// The row's key handling, following the repo's keyboard-activation convention: Enter activates on
    /// KEYDOWN (guarded against auto-repeat), Space activates on KEYUP, and ←/→ are the APG tree keys
    /// (collapse/expand, with ← walking to the parent when the row is already collapsed).
    /// </summary>
    /// <param name="row">The focused row.</param>
    /// <param name="args">The key event.</param>
    private async Task HandleRowKeyDownAsync(TraceWaterfallRow row, KeyboardEventArgs args)
    {
        var index = IndexOf(row);
        if (index < 0)
        {
            return;
        }

        switch (args.Key)
        {
            case "ArrowDown":
                await MoveToAsync(index + 1);
                break;
            case "ArrowUp":
                await MoveToAsync(index - 1);
                break;
            case "Home":
                await MoveToAsync(0);
                break;
            case "End":
                await MoveToAsync(_visibleRows.Count - 1);
                break;
            case "ArrowRight":
                // APG treegrid: expand a collapsed row, move to the first child of an expanded one,
                // do nothing on a leaf (a leaf's next row is its parent's sibling, not its child).
                if (row.HasChildren)
                {
                    if (IsCollapsed(row.Span.Id))
                    {
                        await ToggleAsync(row);
                    }
                    else
                    {
                        await MoveToAsync(index + 1);
                    }
                }

                break;
            case "ArrowLeft":
                if (row.HasChildren && !IsCollapsed(row.Span.Id))
                {
                    await ToggleAsync(row);
                }
                else
                {
                    await MoveToParentAsync(index);
                }

                break;
            case "Enter":
                if (!args.Repeat)
                {
                    await SelectAsync(row);
                }

                break;
        }
    }

    /// <summary>Space selects on KEYUP, exactly where a native button fires its click.</summary>
    /// <param name="row">The focused row.</param>
    /// <param name="args">The key event.</param>
    private async Task HandleRowKeyUpAsync(TraceWaterfallRow row, KeyboardEventArgs args)
    {
        if (args.Key is " " or "Spacebar")
        {
            await SelectAsync(row);
        }
    }

    /// <summary>
    /// The zoom keyboard shortcuts, handled on the grid (not on the root, so typing + or − inside a
    /// host's detail template never zooms the trace): + zooms in, − zooms out, 0 resets.
    /// </summary>
    /// <param name="args">The key event.</param>
    private async Task HandleZoomKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "+":
            case "=":
                await ZoomInAsync();
                break;
            case "-":
            case "_":
                await ZoomOutAsync();
                break;
            case "0":
                await ZoomResetAsync();
                break;
        }
    }

    /// <summary>Halves the visible window around its centre.</summary>
    private Task ZoomInAsync() => ApplyZoomAsync(
        Math.Max(MinimumZoomWidth, _zoomWidth / 2),
        _zoomStart + (_zoomWidth / 4));

    /// <summary>Doubles the visible window around its centre, clamped to the whole trace.</summary>
    private Task ZoomOutAsync()
    {
        var width = Math.Min(1, _zoomWidth * 2);
        return ApplyZoomAsync(width, Math.Max(0, _zoomStart - ((width - _zoomWidth) / 2)));
    }

    /// <summary>Returns the window to the whole trace.</summary>
    private Task ZoomResetAsync() => ApplyZoomAsync(1, 0);

    /// <summary>
    /// Sets the visible window, re-lays out and announces the new window. Synchronous for the same
    /// reason as <see cref="ToggleAsync"/>.
    /// </summary>
    /// <param name="width">The new window width as a fraction of the trace.</param>
    /// <param name="start">The new window start as a fraction of the trace.</param>
    private Task ApplyZoomAsync(double width, double start)
    {
        var normalized = new TraceWaterfallWindow(start, width).Normalized();
        _zoomStart = normalized.Start;
        _zoomWidth = normalized.Width;
        Rebuild();
        _announcement = Loc["TmTraceWaterfall_Zoomed", ZoomWindowText];
        return Task.CompletedTask;
    }

    /// <summary>Moves the roving tabindex to a visible row and focuses it after the render.</summary>
    /// <param name="index">The index in <see cref="_visibleRows"/>.</param>
    private async Task MoveToAsync(int index)
    {
        if (index < 0 || index >= _visibleRows.Count)
        {
            return;
        }

        var target = _visibleRows[index];
        _activeSpanId = target.Span.Id;
        _pendingFocusIndex = target.Index;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Moves focus from a row to its parent (← on an already collapsed row).</summary>
    /// <param name="index">The row's index in <see cref="_visibleRows"/>.</param>
    private async Task MoveToParentAsync(int index)
    {
        var depth = _visibleRows[index].Depth;
        if (depth == 0)
        {
            return;
        }

        for (var candidate = index - 1; candidate >= 0; candidate--)
        {
            if (_visibleRows[candidate].Depth == depth - 1)
            {
                await MoveToAsync(candidate);
                return;
            }
        }
    }

    /// <summary>The index of a row in the visible list, or -1 when it is not visible.</summary>
    /// <param name="row">The row.</param>
    private int IndexOf(TraceWaterfallRow row)
        => _visibleRows.FindIndex(candidate =>
            string.Equals(candidate.Span.Id, row.Span.Id, StringComparison.Ordinal));

    /// <summary>
    /// Focuses the captured row element. A row that the virtualizer removed meanwhile has no element
    /// any more, and losing that race must not break the interaction, so the interop failure is
    /// swallowed — the roving tabindex has already moved, and the next Tab press lands on the row.
    /// </summary>
    /// <param name="rowIndex">The row's index in the FULL tree.</param>
    private async Task FocusRowAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rowRefs.Count)
        {
            return;
        }

        try
        {
            await _rowRefs[rowIndex].FocusAsync();
        }
        catch (JSException)
        {
            // The row is not in the DOM (virtualized away) — the tabindex move still stands.
        }
        catch (InvalidOperationException)
        {
            // No element reference was captured for that slot yet.
        }
    }

    /// <summary>
    /// Creates, restarts or stops the live timer so it always matches the current parameters: it exists
    /// only while <see cref="LiveMode"/> is on and the component is alive, its period follows
    /// <see cref="LiveTickInterval"/> and it is created from the CURRENT <see cref="TimeProvider"/>
    /// (a swapped clock replaces the timer, so the old one cannot keep driving redraws).
    /// </summary>
    private void SyncLiveTimer()
    {
        if (!LiveMode || _disposed)
        {
            _timer?.Dispose();
            _timer = null;
            _timerProvider = null;
            return;
        }

        var interval = LiveTickInterval > TimeSpan.Zero
            ? LiveTickInterval
            : TimeSpan.FromSeconds(1);

        if (_timer is not null
            && _timerInterval == interval
            && ReferenceEquals(_timerProvider, TimeProvider))
        {
            return;
        }

        _timer?.Dispose();
        _timerInterval = interval;
        _timerProvider = TimeProvider;
        _timer = TimeProvider.CreateTimer(OnLiveTick, null, interval, interval);
    }

    /// <summary>
    /// One live tick: redraws ONLY while some span is still running, so a finished trace costs nothing.
    /// The render goes through <see cref="Microsoft.AspNetCore.Components.ComponentBase.InvokeAsync(Action)"/>
    /// because the timer callback runs off the renderer's dispatcher.
    /// </summary>
    /// <param name="state">The timer state (unused).</param>
    private void OnLiveTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed || !HasRunningSpan())
                {
                    return;
                }

                Rebuild();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer went away between the tick and the dispatch; Dispose already ran.
        }
        catch (InvalidOperationException)
        {
            // The renderer is shutting down; nothing to redraw.
        }
    }

    /// <summary>Whether any supplied span is still running (the only state the live ticker serves).</summary>
    private bool HasRunningSpan()
        => Spans is not null && Spans.Any(span => span is not null && span.Status == TraceSpanStatus.Running);

    /// <summary>Stops the live timer and releases the scroll-key guard module; the component owns both.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        _timerProvider = null;
        var interop = _interop;
        _interop = null;
        if (interop is not null)
        {
            _ = interop.DisposeAsync();
        }
    }
}
