using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// An orchestrator's fan-out as ONE card: a summary line (how much finished, what it cost, when it should be over) and
/// a grid of tiles — one tile per unit of work, with its worker, its state, its attempt count and a live countdown to
/// its estimate.
/// <para>
/// The surface renders the task list it is given and never polls or mutates anything: the host owns the data and the
/// transport, the component owns its view. The injected <see cref="TimeProvider"/> drives the countdown AND the ticker
/// that repaints it, so a test with a fake clock closes a second without sleeping and a finished fan-out arms no timer
/// at all.
/// </para>
/// <para>
/// The grid is an APG grid: ONE tab stop (a roving tabindex), the arrow keys walk the tiles in reading order, Home/End
/// stay inside the current row and Ctrl+Home/Ctrl+End jump across the whole grid, PageUp/PageDown jump a page of rows.
/// A tile is a native <c>&lt;button&gt;</c>, so Enter and Space activate it exactly once with no emulation
/// (docs/keyboard-activation-convention.md). A state is never carried by colour alone: the tile paints an icon, a
/// localized WORD and a tone.
/// </para>
/// <para>
/// The detail follows the tile the reader focused (or pointed at) and carries the Retry and Cancel actions — the same
/// actions a host would put on a row menu, reachable without a pointer. Hundreds of tasks mount a bounded window of
/// tiles while the grid still declares its full <c>aria-rowcount</c> and every mounted row keeps its real
/// <c>aria-rowindex</c>, so what a screen reader counts is the grid, not the window.
/// </para>
/// </summary>
public partial class TmFanOutGrid : TmComponentBase, IDisposable
{
    /// <summary>The column count used when a host does not set one.</summary>
    private const int DefaultColumns = 4;

    /// <summary>The widest grid a surface offers (more columns stop reading as a grid).</summary>
    private const int MaxColumns = 8;

    /// <summary>How many rows PageUp/PageDown cross.</summary>
    private const int PageRows = 10;

    /// <summary>The window size used when a host does not set one.</summary>
    private const int DefaultVisibleTaskLimit = 60;

    private readonly string _id = $"tm-ai-fan-out-{Guid.NewGuid():n}"[..26];

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AiElapsedTicker? _ticker;
    private DateTimeOffset _now;
    private bool _disposed;

    private List<FanOutTask> _tasks = [];
    private List<FanOutTask> _visible = [];
    private FanOutSummary _summary = new();
    private IReadOnlyList<FanOutRow> _rows = [];
    private int _windowStart;
    private int _windowCount;

    private FanOutFilter _filter = FanOutFilter.All;
    private FanOutFilter _lastFilterParameter = FanOutFilter.All;
    private string? _activeTaskId;
    private string? _detailTaskId;
    private bool _tileHadFocus;
    private string? _pendingFocusTaskId;
    private readonly Dictionary<string, TileHandle> _handles = new(StringComparer.Ordinal);
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The units of work the orchestrator split the job into. Each tile is one entry; the summary counts the same list.
    /// Null or empty renders the localized empty state. The component never mutates the list and never reorders it —
    /// the grid paints the host's order, because that order is the host's own progress.
    /// </summary>
    [Parameter] public IReadOnlyList<FanOutTask>? Tasks { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Fan-out"); the heading also names the card,
    /// so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Whether the summary line is rendered. Default <c>true</c>. It carries the finished / running / failed counts,
    /// the total of the priced tasks and the estimate of the last active one.
    /// </summary>
    [Parameter] public bool ShowSummary { get; set; } = true;

    /// <summary>
    /// How many tiles sit on one grid row, clamped to 1–8. Default 4. The value is also written onto the root as
    /// <c>--tm-fan-out-columns</c>, so the stylesheet lays the rows out with the same number the C# window uses.
    /// </summary>
    [Parameter] public int Columns { get; set; } = DefaultColumns;

    /// <summary>
    /// Whether the compact presentation is used: a tile keeps its label, its state word and its attempt badge and drops
    /// the countdown and the worker line. Default <c>false</c>. The grid keys, the detail and the actions are unchanged
    /// — compact hides information, never a way to act.
    /// </summary>
    [Parameter] public bool Compact { get; set; }

    /// <summary>Whether the state filter bar is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowFilter { get; set; } = true;

    /// <summary>
    /// The state filter, two-way bound with <see cref="FilterChanged"/> (<c>@bind-Filter</c>). Default
    /// <see cref="FanOutFilter.All"/>. A host that pins it keeps control: the reader's pick reports the change and the
    /// next render from the host decides.
    /// </summary>
    [Parameter] public FanOutFilter Filter { get; set; } = FanOutFilter.All;

    /// <summary>Fires with the new filter when the reader picks one in the filter bar.</summary>
    [Parameter] public EventCallback<FanOutFilter> FilterChanged { get; set; }

    /// <summary>
    /// How many tiles may be mounted at once. Default 60; <c>0</c> (or a negative value) mounts the whole filtered
    /// list. Above the limit the grid mounts a window of whole rows that follows the focused tile, while
    /// <c>aria-rowcount</c> keeps declaring the filtered total — hundreds of tasks stay scrollable and cheap.
    /// </summary>
    [Parameter] public int VisibleTaskLimit { get; set; } = DefaultVisibleTaskLimit;

    /// <summary>
    /// Raised when the reader asks for a retry of a failed task — the action lives in the tile's detail and is offered
    /// only while <see cref="FanOutTask.CanRetry"/> holds. The component does not change the task: the host owns the
    /// data and re-renders with the retried value.
    /// </summary>
    [Parameter] public EventCallback<FanOutTask> OnRetry { get; set; }

    /// <summary>
    /// Raised when the reader asks to cancel a task — the action lives in the tile's detail and is offered only while
    /// <see cref="FanOutTask.CanCancel"/> holds. The component does not change the task.
    /// </summary>
    [Parameter] public EventCallback<FanOutTask> OnCancel { get; set; }

    /// <summary>
    /// Formats a cost given in MICRO-UNITS (1,000,000 is 1.00 of the currency). Null uses
    /// <see cref="AiCostFormatter"/> with <see cref="Culture"/> — the culture's own currency, with micro-unit precision
    /// below one hundredth of a unit. A host with a credit price model overrides it.
    /// </summary>
    [Parameter] public Func<long, string>? CostFormatter { get; set; }

    /// <summary>
    /// The culture that formats the costs and the clock times. Null (the default) means the current culture at render
    /// time. The countdowns are clock-style (<c>m:ss</c>) on purpose and are not localized.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The clock the countdowns and the repaint ticker read. Null uses the container's <see cref="TimeProvider"/> and
    /// falls back to <see cref="TimeProvider.System"/> — the host's clock always wins over the machine's.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The roll-up the summary line prints — the same numbers the tiles show.</summary>
    public FanOutSummary SummaryData => _summary;

    /// <summary>The tasks the current filter shows, in the host's order.</summary>
    public IReadOnlyList<FanOutTask> VisibleTasks => _visible;

    /// <summary>The task whose detail is open, or null when the reader has not read a tile yet.</summary>
    public FanOutTask? DetailTask => _detailTaskId is { } id
        ? _visible.FirstOrDefault(task => string.Equals(task.Id, id, StringComparison.Ordinal))
        : null;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>
    /// The detail card's title level: ONE below the surface heading (and never past h6), so the card is a real section
    /// of the surface and a host that renders the heading at h2 cannot end up with a skipped level.
    /// </summary>
    public int DetailHeadingLevel => Math.Min(6, EffectiveHeadingLevel + 1);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmFanOutGrid_Title"] : Title!;

    private int EffectiveColumns => Math.Clamp(Columns, 1, MaxColumns);

    private string RootClass => string.IsNullOrWhiteSpace(Class)
        ? "tm-ai-fan-out" + (Compact ? " tm-ai-fan-out--compact" : string.Empty)
        : "tm-ai-fan-out" + (Compact ? " tm-ai-fan-out--compact" : string.Empty) + " " + Class;

    private string FilterSlug => FanOutPresentation.Slug(_filter);

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private IReadOnlyList<FanOutTask> TaskList => _tasks;

    private IReadOnlyList<FanOutRow> Rows => _rows;

    private int RowCount => _visible.Count == 0 ? 0 : (int)Math.Ceiling(_visible.Count / (double)EffectiveColumns);

    private int WindowStart => _windowStart;

    private int WindowCount => _windowCount;

    private bool IsWindowed => _windowCount < _visible.Count;

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    private string TasksText => Loc["TmFanOutGrid_TasksValue",
        _summary.Finished.ToString(CultureInfo.CurrentCulture),
        _summary.Total.ToString(CultureInfo.CurrentCulture)];

    private string CostText => FormatCost(_summary.TotalCostMicroUnits);

    /// <summary>
    /// The summary's countdown: the live time to the last active estimate, the localized "Finished" once nothing is
    /// active any more, or "No estimate" when the host gave none.
    /// </summary>
    private string EtaText
    {
        get
        {
            if (_summary.RemainingAt(_now) is { } remaining)
            {
                return AiClockFormatter.FormatClock(remaining);
            }

            return _summary.IsComplete ? Loc["TmFanOutGrid_SummaryFinished"] : Loc["TmFanOutGrid_SummaryNoEstimate"];
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _clock = ResolveClock();
        _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        _filter = FanOutPresentation.IsKnown(Filter) ? Filter : FanOutFilter.All;
        _lastFilterParameter = Filter;
        _now = _clock.GetUtcNow();
        Recompute();
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var clock = ResolveClock();
        if (!ReferenceEquals(clock, _clock))
        {
            _clock = clock;
            _ticker?.Dispose();
            _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        }

        _now = _clock.GetUtcNow();

        // A host that changes the parameter wins; the reader's own pick survives an unrelated render.
        if (Filter != _lastFilterParameter)
        {
            _lastFilterParameter = Filter;
            _filter = FanOutPresentation.IsKnown(Filter) ? Filter : FanOutFilter.All;
        }

        _tasks = Tasks is null
            ? []
            : [.. Tasks.Where(task => task is not null)];

        Recompute();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (NeedsTicking)
        {
            _ticker?.Start();
        }
        else
        {
            _ticker?.Stop();
        }

        // The focus move happens AFTER the paint: the tile that has to receive it carries a NEW tabindex and a new
        // detail in this very render, and a browser only focuses the element once it is patched.
        if (_pendingFocusTaskId is { } pending)
        {
            _pendingFocusTaskId = null;
            await FocusTileAfterRenderAsync(pending);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ticker?.Dispose();
        _ticker = null;
    }

    // ── View computation ─────────────────────────────────────────────────────

    /// <summary>Whether the countdown is live: an active task carries an estimate worth repainting every second.</summary>
    private bool NeedsTicking => !_disposed && _summary.HasEstimate;

    private TimeProvider ResolveClock()
        => TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;

    private async Task OnTickAsync()
    {
        if (_disposed)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            _now = _clock.GetUtcNow();
            StateHasChanged();
        });
    }

    /// <summary>Filters the host's list, rolls it up, reconciles the roving stop and rebuilds the mounted window.</summary>
    private void Recompute()
    {
        _summary = FanOutSummary.Create(_tasks);

        var wanted = FanOutPresentation.Status(_filter);
        _visible = wanted is { } status
            ? _tasks.Where(task => task.Status == status).ToList()
            : [.. _tasks];

        ReconcileActive();
        BuildRows();
        PruneHandles();
    }

    /// <summary>
    /// Keeps the roving tab stop and the detail inside the tasks the surface is actually painting: a filter (or new
    /// host data) can take the tile the reader was on away, and neither a tabindex nor a detail may point at a tile
    /// that is no longer mounted.
    /// </summary>
    private void ReconcileActive()
    {
        var lost = _activeTaskId is { } active && IndexOf(active) < 0;

        if (lost)
        {
            _activeTaskId = null;
            _detailTaskId = null;
        }

        _activeTaskId ??= _visible.Count == 0 ? null : _visible[0].Id;

        if (lost && _tileHadFocus)
        {
            // The reader WAS on the tile that vanished, so the focus would now be on the document body: hand it to the
            // tile that took the roving stop and say so. A filter click deliberately does not come through here —
            // the reader is on the filter button and stays there.
            _pendingFocusTaskId = _activeTaskId;
            Announce(Loc["TmFanOutGrid_FocusLost"]);
        }

        if (_detailTaskId is { } detail && IndexOf(detail) < 0)
        {
            _detailTaskId = null;
        }
    }

    /// <summary>Builds the mounted rows: the whole filtered list, or a bounded window of whole rows around the focus.</summary>
    private void BuildRows()
    {
        var columns = EffectiveColumns;
        var total = _visible.Count;

        _windowStart = 0;
        _windowCount = total;

        if (VisibleTaskLimit > 0 && total > VisibleTaskLimit)
        {
            var windowRows = Math.Max(1, VisibleTaskLimit / columns);
            var rowCount = (int)Math.Ceiling(total / (double)columns);
            var active = IndexOf(_activeTaskId);
            var activeRow = active < 0 ? 0 : active / columns;
            var startRow = Math.Clamp(activeRow - (windowRows / 2), 0, Math.Max(0, rowCount - windowRows));

            _windowStart = startRow * columns;
            _windowCount = Math.Min(total - _windowStart, windowRows * columns);
        }

        var rows = new List<FanOutRow>((_windowCount / columns) + 1);
        for (var start = _windowStart; start < _windowStart + _windowCount; start += columns)
        {
            var cells = new List<FanOutCell>(columns);
            for (var column = 0; column < columns && start + column < _windowStart + _windowCount; column++)
            {
                cells.Add(new FanOutCell(_visible[start + column], column + 1));
            }

            rows.Add(new FanOutRow((start / columns) + 1, cells));
        }

        _rows = rows;
    }

    /// <summary>
    /// Keeps one element handle per mounted tile: the handle owns the element reference a focus move needs, so
    /// rebuilding it would drop that reference until the next paint. Handles of tiles that are no longer mounted are
    /// dropped, so a 300-task fan-out never grows the dictionary without bound.
    /// </summary>
    private void PruneHandles()
    {
        if (_handles.Count == 0)
        {
            return;
        }

        List<string>? stale = null;
        foreach (var id in _handles.Keys)
        {
            if (IndexOf(id) < 0)
            {
                (stale ??= []).Add(id);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var id in stale)
        {
            _handles.Remove(id);
        }
    }

    private TileHandle HandleFor(string taskId)
    {
        if (!_handles.TryGetValue(taskId, out var handle))
        {
            handle = new TileHandle();
            _handles[taskId] = handle;
        }

        return handle;
    }

    private int IndexOf(string? taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return -1;
        }

        for (var index = 0; index < _visible.Count; index++)
        {
            if (string.Equals(_visible[index].Id, taskId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the state filter — the same path the filter bar takes. The tiles narrow, the roving tab stop is
    /// reconciled and the change is announced once; an unknown value is read as
    /// <see cref="FanOutFilter.All"/>.
    /// </summary>
    /// <param name="filter">The filter to apply.</param>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    public async Task SetFilterAsync(FanOutFilter filter)
    {
        var next = FanOutPresentation.IsKnown(filter) ? filter : FanOutFilter.All;
        if (next == _filter)
        {
            // Re-announce the current slice instead of silently doing nothing: a reader who presses the pressed filter
            // is asking "how many are there?".
            AnnounceFiltered();
            StateHasChanged();
            return;
        }

        _filter = next;
        _lastFilterParameter = Filter;
        _tileHadFocus = false;
        Recompute();
        AnnounceFiltered();

        StateHasChanged();

        if (FilterChanged.HasDelegate)
        {
            await FilterChanged.InvokeAsync(next);
        }
    }

    // ── Tiles and keys ───────────────────────────────────────────────────────

    private bool IsActive(string taskId) => string.Equals(_activeTaskId, taskId, StringComparison.Ordinal);

    private string FilterClass(FanOutFilter filter)
        => filter == _filter ? "tm-ai-fan-out__filter tm-ai-fan-out__filter--active" : "tm-ai-fan-out__filter";

    private string? FilterText(FanOutFilter filter) => FanOutPresentation.Text(Loc, filter);

    private string FilterCount(FanOutFilter filter)
    {
        var count = 0;
        if (FanOutPresentation.Status(filter) is { } status)
        {
            foreach (var task in _tasks)
            {
                if (task.Status == status)
                {
                    count++;
                }
            }
        }
        else
        {
            count = _tasks.Count;
        }

        return count.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The avatar's palette slot, written inline from the worker's own token (<c>var(--tm-agent-color-{0..7})</c>)
    /// — the same identity the lanes and the graph paint. A tile without an <see cref="AgentRef"/> returns null
    /// and keeps the neutral avatar the stylesheet declares.
    /// </summary>
    private static string? AvatarStyle(FanOutTask task)
        => task.Worker is { } worker && !string.IsNullOrEmpty(worker.ColorToken)
            ? $"--tm-fan-out-tile-avatar: var({worker.ColorToken}); --tm-fan-out-tile-avatar-ink: var(--tm-agent-color-ink)"
            : null;

    private string TileClass(FanOutTask task)
        => "tm-ai-fan-out__tile tm-ai-fan-out__tile--" + AgentStatusPresentation.Tone(task.Status);

    /// <summary>The visible label (or the localized placeholder, which the aria-label renames too).</summary>
    private string TileLabel(FanOutTask task)
        => task.HasLabel ? task.Label : Loc["TmFanOutGrid_TileFallback"];

    /// <summary>
    /// The attempt badge: "2/3" while the host declared a limit, "2" when it did not, and nothing at all for a first
    /// attempt with no limit — a badge that says "1" on every tile is noise.
    /// </summary>
    private string? BadgeText(FanOutTask task)
    {
        if (task.HasAttemptLimit)
        {
            return Loc["TmFanOutGrid_AttemptBadge",
                task.EffectiveAttempt.ToString(CultureInfo.CurrentCulture),
                task.MaxAttempts!.Value.ToString(CultureInfo.CurrentCulture)];
        }

        return task.EffectiveAttempt > 1
            ? Loc["TmFanOutGrid_AttemptBadgeOpen", task.EffectiveAttempt.ToString(CultureInfo.CurrentCulture)]
            : null;
    }

    private string? EtaOf(FanOutTask task)
    {
        if (task.IsOverdueAt(_now))
        {
            return Loc["TmFanOutGrid_Overdue"];
        }

        return task.RemainingAt(_now) is { } remaining ? AiClockFormatter.FormatClock(remaining) : null;
    }

    private string TileTitle(FanOutTask task)
        => Loc["TmFanOutGrid_TileTitle", TileLabel(task), AgentStatusPresentation.Text(Loc, task.Status)];

    /// <summary>
    /// The tile's accessible name: its own label first (WCAG 2.5.3, so saying what is on screen works), then the
    /// state, then every fact the tile paints — the attempt, the countdown, the worker, the cost and the failure. The
    /// parts are joined into ONE sentence so a reader hears a tile, not six fragments.
    /// </summary>
    private string TileAriaLabel(FanOutTask task)
    {
        var parts = new List<string>
        {
            TileLabel(task),
            AgentStatusPresentation.Text(Loc, task.Status),
        };

        if (task.HasAttemptLimit)
        {
            parts.Add(Loc["TmFanOutGrid_TileAttempt",
                task.EffectiveAttempt.ToString(CultureInfo.CurrentCulture),
                task.MaxAttempts!.Value.ToString(CultureInfo.CurrentCulture)]);
        }
        else if (task.EffectiveAttempt > 1)
        {
            parts.Add(Loc["TmFanOutGrid_TileAttemptOpen", task.EffectiveAttempt.ToString(CultureInfo.CurrentCulture)]);
        }

        if (task.IsOverdueAt(_now))
        {
            parts.Add(Loc["TmFanOutGrid_Overdue"]);
        }
        else if (task.RemainingAt(_now) is { } remaining)
        {
            parts.Add(Loc["TmFanOutGrid_TileEndsIn", AiClockFormatter.FormatClock(remaining)]);
        }

        if (task.HasWorker)
        {
            parts.Add(Loc["TmFanOutGrid_TileWorker", task.WorkerName]);
        }

        if (task.HasCost)
        {
            parts.Add(Loc["TmFanOutGrid_TileCost", FormatCost(task.CostMicroUnits!.Value)]);
        }

        if (task.HasError)
        {
            parts.Add(Loc["TmFanOutGrid_TileError", task.Error!]);
        }

        return string.Join(". ", parts);
    }

    /// <summary>
    /// The keys a tile answers ITSELF. Enter and Space are deliberately absent: a tile is a native
    /// <c>&lt;button&gt;</c>, so the browser already activates it exactly once and emulating them here would activate it
    /// twice (docs/keyboard-activation-convention.md).
    /// </summary>
    private async Task OnTileKeyDownAsync(FanOutTask task, KeyboardEventArgs args)
    {
        var index = IndexOf(task.Id);
        if (index < 0)
        {
            return;
        }

        var count = _visible.Count;
        var columns = EffectiveColumns;
        var rowStart = index - (index % columns);

        int? target = args.Key switch
        {
            "ArrowRight" => Wrap(index + 1, count),
            "ArrowLeft" => Wrap(index - 1, count),
            "ArrowDown" => Wrap(index + columns, count),
            "ArrowUp" => Wrap(index - columns, count),
            "Home" => args.CtrlKey ? 0 : rowStart,
            "End" => args.CtrlKey ? count - 1 : Math.Min(count - 1, rowStart + columns - 1),
            "PageDown" => Math.Min(count - 1, index + (columns * PageRows)),
            "PageUp" => Math.Max(0, index - (columns * PageRows)),
            _ => null,
        };

        if (target is not { } next || next == index || next < 0 || next >= count)
        {
            return;
        }

        await FocusTileAsync(_visible[next].Id);
    }

    private static int Wrap(int index, int count) => count == 0 ? 0 : ((index % count) + count) % count;

    /// <summary>
    /// Moves the roving tab stop onto one tile and REPAINTS; the DOM focus follows in
    /// <see cref="OnAfterRenderAsync"/>, where the tile already carries the tabindex the paint just gave it.
    /// </summary>
    private Task FocusTileAsync(string? taskId)
    {
        if (taskId is null || IndexOf(taskId) < 0)
        {
            return Task.CompletedTask;
        }

        _activeTaskId = taskId;
        _detailTaskId = taskId;
        _tileHadFocus = true;
        _pendingFocusTaskId = taskId;

        // The mounted window follows the focus, and a key press does not re-run OnParametersSet, so the rows are
        // rebuilt here — otherwise the tile the reader just jumped to would not be mounted yet.
        Recompute();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task FocusTileAfterRenderAsync(string taskId)
    {
        if (!_handles.TryGetValue(taskId, out var handle) || handle.Ref.Id is null)
        {
            return;
        }

        try
        {
            await handle.Ref.FocusAsync();
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the element is already gone: the roving tab stop has still moved, so the reader's next
            // Tab lands on the right tile.
        }
    }

    private void OnTileFocused(string taskId)
    {
        _tileHadFocus = true;

        if (IsActive(taskId) && string.Equals(_detailTaskId, taskId, StringComparison.Ordinal))
        {
            return;
        }

        _activeTaskId = taskId;
        _detailTaskId = taskId;
        _ = InvokeAsync(() =>
        {
            Recompute();
            StateHasChanged();
        });
    }

    private void OnTileHovered(string taskId)
    {
        if (string.Equals(_detailTaskId, taskId, StringComparison.Ordinal))
        {
            return;
        }

        _detailTaskId = taskId;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnTilePinnedAsync(string taskId)
    {
        _tileHadFocus = true;
        _activeTaskId = taskId;
        _detailTaskId = taskId;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private async Task RetryAsync(FanOutTask task)
    {
        _tileHadFocus = false;
        Announce(Loc["TmFanOutGrid_RetryRequested", TileLabel(task)]);

        if (OnRetry.HasDelegate)
        {
            await OnRetry.InvokeAsync(task);
        }
    }

    private async Task CancelAsync(FanOutTask task)
    {
        _tileHadFocus = false;
        Announce(Loc["TmFanOutGrid_CancelRequested", TileLabel(task)]);

        if (OnCancel.HasDelegate)
        {
            await OnCancel.InvokeAsync(task);
        }
    }

    // ── Formatting and announcements ─────────────────────────────────────────

    private string FormatCost(long microUnits)
        => CostFormatter?.Invoke(microUnits) ?? AiCostFormatter.Format(microUnits, FormatCulture);

    private string TimeText(DateTimeOffset value) => value.ToLocalTime().ToString("t", FormatCulture);

    private string AttemptText(FanOutTask task) => task.HasAttemptLimit
        ? Loc["TmFanOutGrid_DetailAttemptOf",
            task.EffectiveAttempt.ToString(CultureInfo.CurrentCulture),
            task.MaxAttempts!.Value.ToString(CultureInfo.CurrentCulture)]
        : Loc["TmFanOutGrid_DetailAttemptOpen", task.EffectiveAttempt.ToString(CultureInfo.CurrentCulture)];

    private void AnnounceFiltered()
    {
        if (_visible.Count == 0)
        {
            Announce(Loc["TmFanOutGrid_EmptyFiltered"]);
            return;
        }

        Announce(Loc["TmFanOutGrid_Filtered",
            _visible.Count.ToString(CultureInfo.CurrentCulture),
            _tasks.Count.ToString(CultureInfo.CurrentCulture)]);
    }

    /// <summary>
    /// Writes the announcement into the ONE polite region. An identical sentence is re-announced by toggling one
    /// invisible marker, because a live region announces a MUTATION and not a repeat of the same text.
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

    // ── Nested shapes ────────────────────────────────────────────────────────

    /// <summary>One mounted row: its REAL position in the filtered grid plus the cells it carries.</summary>
    private sealed record FanOutRow(int Index, IReadOnlyList<FanOutCell> Cells);

    /// <summary>One mounted cell: the task plus its 1-based column.</summary>
    private sealed record FanOutCell(FanOutTask Task, int Column);

    /// <summary>One mounted tile's element reference, kept alive across renders so the focus move can reach it.</summary>
    private sealed class TileHandle
    {
        /// <summary>The tile button's element reference, assigned by the render.</summary>
        public ElementReference Ref { get; set; }
    }
}
