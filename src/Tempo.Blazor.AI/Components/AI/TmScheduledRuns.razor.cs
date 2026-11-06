using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Runs placed into the windows a scheduler offers: a week (or one day) of capacity bars, each bar showing what the host
/// has already committed and where every run sits, with a run moved to another window by pointer drag or by its own menu.
/// <para>
/// The placement is COMPUTED, never drawn by hand: <see cref="RunWindowPlanner"/> distributes each run's units over the
/// windows that still have room, splits a run that does not fit in one window, fills the gaps between runs and reports
/// the units that fit nowhere. The bar next to the plan is painted from the same segments, so the picture and the numbers
/// cannot disagree.
/// </para>
/// <para>
/// The surface keeps the reader's own placements (the window a run was dragged to) and drops them the moment the host
/// supplies a NEW instance of that run, so a host that re-renders the same snapshot cannot undo the move and a host that
/// accepts it is never fought by a stale overlay. Every move is reported once through <see cref="OnMove"/>.
/// </para>
/// <para>
/// Accessibility: a window states its committed load, its free room and — when the host committed more than it accepts —
/// an OVERLOADED line with an icon and a word, never a colour alone. The pointer drag is the shared
/// <c>tm-sortable.js</c> over <c>pointer-drag.js</c> (through <c>TmSortableList</c>'s group support) and every run's menu
/// lists the windows, so nothing is drag-only (WCAG 2.5.7). <see cref="RunViewMode.Table"/> is the text alternative and
/// the surface owns exactly ONE polite live region.
/// </para>
/// </summary>
public partial class TmScheduledRuns : TmComponentBase
{
    private readonly string _id = $"tm-ai-runs-{Guid.NewGuid():n}"[..21];

    private readonly Dictionary<string, ScheduledRun> _movedRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _movedWindows = new(StringComparer.Ordinal);

    private IReadOnlyList<RunWindow> _windowList = [];
    private IReadOnlyList<ScheduledRunPlan> _plans = [];
    private IReadOnlyList<ScheduledWindowView> _windowViews = [];
    private IReadOnlyList<ScheduledDayView> _days = [];
    private IReadOnlyList<ScheduledTableRow> _tableRows = [];
    private IReadOnlyList<ScheduledRun> _runsView = [];

    private RunViewMode _view = RunViewMode.Week;
    private RunViewMode _lastView;
    private bool _viewSeeded;

    private DateOnly? _selectedDay;
    private DateOnly? _lastSelectedDay;
    private bool _daySeeded;

    private string _announcement = string.Empty;
    private string _notice = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The windows the scheduler offers, in any order (they are rendered in their own time order). Null or empty renders
    /// the localized empty state. A window that closes before it opens, or that accepts no units, is skipped by the
    /// planner and not rendered at all.
    /// </summary>
    [Parameter] public IReadOnlyList<RunWindow>? Windows { get; set; }

    /// <summary>
    /// The runs to place, in the order they compete for capacity (which is also the order the plan fills the gaps in).
    /// Null or empty renders the localized "no runs" state and empty windows.
    /// </summary>
    [Parameter] public IReadOnlyList<ScheduledRun>? Runs { get; set; }

    /// <summary>
    /// Raised once per committed move of a run to another window. The surface keeps the reader's pick until the host
    /// supplies a new instance of the run, so the run visibly stays where it was dropped.
    /// </summary>
    [Parameter] public EventCallback<ScheduledRunMoveRequest> OnMove { get; set; }

    /// <summary>
    /// Which presentation is shown, two-way bound with <see cref="ViewModeChanged"/> (<c>@bind-ViewMode</c>). Default
    /// <see cref="RunViewMode.Week"/>; the reader's pick wins until the host MOVES the parameter.
    /// </summary>
    [Parameter] public RunViewMode ViewMode { get; set; }

    /// <summary>Fires with the new view.</summary>
    [Parameter] public EventCallback<RunViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// The day <see cref="RunViewMode.Day"/> shows, two-way bound with <see cref="SelectedDayChanged"/>. Null (the
    /// default) shows the first day that has windows; the day buttons move it inside the range the windows cover.
    /// </summary>
    [Parameter] public DateOnly? SelectedDay { get; set; }

    /// <summary>Fires with the new selected day.</summary>
    [Parameter] public EventCallback<DateOnly> SelectedDayChanged { get; set; }

    /// <summary>The heading above the surface. Null renders the localized default ("Scheduled runs").</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>The accessible name of the schedule area. Null uses the localized default ("Scheduled runs").</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The windows the plan can use, in their own time order.</summary>
    internal IReadOnlyList<RunWindow> WindowList => _windowList;

    /// <summary>What the planner decided for every run, in the host's order.</summary>
    internal IReadOnlyList<ScheduledRunPlan> Plans => _plans;

    /// <summary>One entry per rendered window: the window and where every run sits inside it.</summary>
    internal IReadOnlyList<ScheduledWindowView> WindowViews => _windowViews;

    /// <summary>The days the visible windows fall on, in order — one entry in <see cref="RunViewMode.Day"/>.</summary>
    internal IReadOnlyList<ScheduledDayView> Days => _days;

    /// <summary>The rows of <see cref="RunViewMode.Table"/>: one per run slice, plus the runs that fit nowhere.</summary>
    internal IReadOnlyList<ScheduledTableRow> TableRows => _tableRows;

    /// <summary>The runs the surface shows, with nulls dropped, in the host's order.</summary>
    internal IReadOnlyList<ScheduledRun> AllRuns => _runsView;

    /// <summary>The view being shown.</summary>
    internal RunViewMode View => _view;

    /// <summary>The day <see cref="RunViewMode.Day"/> shows, resolved to a real day of the supplied windows.</summary>
    internal DateOnly? CurrentDay => _selectedDay;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    internal int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The day headings sit one level below the surface title, and the window headings one below those.</summary>
    internal int DayHeadingLevel => Math.Min(6, EffectiveHeadingLevel + 1);

    /// <summary>The window headings, never past <c>h6</c>.</summary>
    internal int WindowHeadingLevel => Math.Min(6, EffectiveHeadingLevel + 2);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    internal string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmScheduledRuns_Title"] : Title!;

    /// <summary>The settled accessible name of the schedule area.</summary>
    internal string EffectiveLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmScheduledRuns_AreaLabel"] : AriaLabel!;

    /// <summary>The one polite live region's current text.</summary>
    internal string Announcement => _announcement;

    /// <summary>The visible refusal line, or an empty string. A refusal is announced through the same region.</summary>
    internal string Notice => _notice;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private bool IsTable => _view == RunViewMode.Table;

    private bool IsDay => _view == RunViewMode.Day;

    private string RootClass
    {
        get
        {
            var root = "tm-ai-runs tm-ai-runs--" + ViewSlug(_view);
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    /// <summary>Every run of one window, in the window's own capacity order.</summary>
    /// <param name="windowId">The window to look at.</param>
    /// <returns>The window's entries, or an empty list.</returns>
    internal IReadOnlyList<ScheduledWindowEntry> EntriesOf(string? windowId)
    {
        foreach (var view in _windowViews)
        {
            if (string.Equals(view.Window.Id, windowId, StringComparison.Ordinal))
            {
                return view.Entries;
            }
        }

        return [];
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A presentation the reader picked wins until the host MOVES its parameter: recording the host's current value in
        // the change handler would make a same-value re-render unable to ever override the pick.
        if (!_viewSeeded || ViewMode != _lastView)
        {
            _viewSeeded = true;
            _lastView = ViewMode;
            _view = IsKnownView(ViewMode) ? ViewMode : RunViewMode.Week;
        }

        PruneMoves();
        Rebuild();
    }

    private static bool IsKnownView(RunViewMode view)
        => view is RunViewMode.Week or RunViewMode.Day or RunViewMode.Table;

    /// <summary>The stable slug of a view, used as the <c>data-view</c> hook instead of a localized word.</summary>
    /// <param name="view">The view to name.</param>
    /// <returns><c>week</c>, <c>day</c> or <c>table</c>.</returns>
    internal static string ViewSlug(RunViewMode view) => view switch
    {
        RunViewMode.Day => "day",
        RunViewMode.Table => "table",
        _ => "week",
    };

    /// <summary>Drops the reader's own placements the host has spoken about (a new instance of the run, or a run gone).</summary>
    private void PruneMoves()
    {
        _runsView = Runs is null ? [] : [.. Runs.Where(run => run is not null)];

        if (_movedRuns.Count == 0)
        {
            return;
        }

        List<string>? stale = null;
        foreach (var (runId, seen) in _movedRuns)
        {
            var current = _runsView.FirstOrDefault(run => string.Equals(run.Id, runId, StringComparison.Ordinal));
            if (current is null || !ReferenceEquals(current, seen))
            {
                (stale ??= []).Add(runId);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var runId in stale)
        {
            _movedRuns.Remove(runId);
            _movedWindows.Remove(runId);
        }
    }

    /// <summary>
    /// Recomputes the plan, the per-window entries and the table rows from the host's data plus the reader's own
    /// placements. Called from the parameter set and after every committed move.
    /// </summary>
    private void Rebuild()
    {
        _windowList = RunWindowPlanner.OrderedWindows(Windows);
        _plans = RunWindowPlanner.Plan(_windowList, _runsView, _movedWindows);
        _windowViews = BuildWindowViews();
        _tableRows = BuildTableRows();
        ResolveDay();
        _days = BuildDays();
    }

    private IReadOnlyList<ScheduledWindowView> BuildWindowViews()
    {
        var views = new List<ScheduledWindowView>(_windowList.Count);
        foreach (var window in _windowList)
        {
            var entries = new List<ScheduledWindowEntry>();
            foreach (var plan in _plans)
            {
                var segment = plan.SegmentIn(window.Id);
                if (segment is not null)
                {
                    var part = 0;
                    for (var index = 0; index < plan.Segments.Count; index++)
                    {
                        if (ReferenceEquals(plan.Segments[index], segment))
                        {
                            part = index + 1;
                            break;
                        }
                    }

                    entries.Add(new ScheduledWindowEntry(plan, segment, part));
                }
            }

            views.Add(new ScheduledWindowView(window, entries));
        }

        return views;
    }

    private IReadOnlyList<ScheduledTableRow> BuildTableRows()
    {
        var rows = new List<ScheduledTableRow>();
        foreach (var plan in _plans)
        {
            if (plan.Segments.Count == 0)
            {
                rows.Add(new ScheduledTableRow(plan, null, 0, IsUnplaced: plan.UnitsUnplaced > 0));
                continue;
            }

            for (var index = 0; index < plan.Segments.Count; index++)
            {
                rows.Add(new ScheduledTableRow(plan, plan.Segments[index], index + 1, IsUnplaced: false));
            }

            if (plan.UnitsUnplaced > 0)
            {
                rows.Add(new ScheduledTableRow(plan, null, plan.Segments.Count + 1, IsUnplaced: true));
            }
        }

        return rows;
    }

    /// <summary>
    /// Settles which day <see cref="RunViewMode.Day"/> shows: the host's pick while it names a supplied day, otherwise the
    /// first day that has windows — a day with nothing in it would render an empty page for no reason.
    /// </summary>
    private void ResolveDay()
    {
        var firstDay = _windowViews.Count > 0 ? _windowViews[0].Window.Day : (DateOnly?)null;
        var known = SelectedDay is { } requested && _windowViews.Any(view => view.Window.Day == requested);

        if (!_daySeeded || SelectedDay != _lastSelectedDay)
        {
            _daySeeded = true;
            _lastSelectedDay = SelectedDay;
            _selectedDay = known ? SelectedDay : firstDay;
            return;
        }

        _selectedDay = _selectedDay is { } current && _windowViews.Any(view => view.Window.Day == current)
            ? current
            : firstDay;
    }

    private IReadOnlyList<ScheduledDayView> BuildDays()
    {
        var days = new List<ScheduledDayView>();
        foreach (var view in _windowViews)
        {
            var day = view.Window.Day;
            if (IsDay && _selectedDay is { } selected && day != selected)
            {
                continue;
            }

            if (days.Count > 0 && days[^1].Day == day)
            {
                days[^1].Windows.Add(view);
                continue;
            }

            days.Add(new ScheduledDayView(day, [view]));
        }

        return days;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    /// <summary>The nearest window that opens before (<paramref name="direction"/> −1) or after (+1) the selected day.</summary>
    /// <param name="direction">−1 for the previous day, +1 for the next.</param>
    /// <returns>The neighbouring day, or null at the end of the range.</returns>
    internal DateOnly? NeighbourDay(int direction)
    {
        if (_windowViews.Count == 0)
        {
            return null;
        }

        var days = _windowViews.Select(view => view.Window.Day).Distinct().OrderBy(day => day).ToList();
        var index = _selectedDay is { } selected ? days.IndexOf(selected) : 0;
        var target = index + Math.Sign(direction);
        return target >= 0 && target < days.Count ? days[target] : null;
    }

    /// <summary>The view switcher's handler: shows the view at once, then reports it to the host.</summary>
    /// <param name="view">The view the reader picked.</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task OnViewChangedAsync(RunViewMode view)
    {
        if (!IsKnownView(view) || view == _view)
        {
            return;
        }

        _view = view;
        // The reader's pick is NOT recorded as "seen": a host that re-renders the same parameter value must not be able
        // to undo it, and only a MOVED parameter may override it.
        Rebuild();
        StateHasChanged();
        await ViewModeChanged.InvokeAsync(view);
    }

    /// <summary>Steps the day view to the neighbouring day, when the window range has one.</summary>
    /// <param name="direction">−1 for the previous day, +1 for the next.</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task OnDayChangedAsync(int direction)
    {
        if (NeighbourDay(direction) is not { } day)
        {
            return;
        }

        _selectedDay = day;
        _lastSelectedDay = SelectedDay;
        Rebuild();
        StateHasChanged();
        await SelectedDayChanged.InvokeAsync(day);
    }

    /// <summary>
    /// The single commit path of the shared drag: the source window, the target window and the row's index inside the
    /// source window come from <see cref="SortableTransferArgs"/>.
    /// </summary>
    /// <param name="args">What the module reported.</param>
    /// <returns>A task that completes when the move has been applied.</returns>
    internal async Task OnTransferAsync(SortableTransferArgs args)
    {
        if (args is null)
        {
            return;
        }

        var source = EntriesOf(args.FromListId);
        var target = FindWindow(args.ToListId);

        if (args.FromIndex < 0 || args.FromIndex >= source.Count || target is null)
        {
            return;
        }

        await MoveToWindowAsync(source[args.FromIndex].Plan.Run, target);
    }

    /// <summary>
    /// Re-places one run in <paramref name="target"/>. The move is applied to the reader's own overlay and the plan is
    /// rebuilt, so the surface states what will really happen: a target that has no room for even the first slice REFUSES
    /// the move (the run stays where it was, the notice line and the live region say why) instead of reporting a move the
    /// plan would not honour.
    /// </summary>
    /// <param name="run">The run to move.</param>
    /// <param name="target">The window the reader dropped it into.</param>
    /// <returns>A task that completes when the move was applied, refused, or the host's callback returned.</returns>
    internal async Task MoveToWindowAsync(ScheduledRun run, RunWindow target)
    {
        var current = PlanOf(run.Id)?.FirstWindow;
        if (current is not null && string.Equals(current.Id, target.Id, StringComparison.Ordinal))
        {
            return;
        }

        _movedRuns[run.Id] = run;
        _movedWindows[run.Id] = target.Id;
        _notice = string.Empty;
        Rebuild();

        var placed = PlanOf(run.Id)?.FirstWindow;
        if (placed is null || !string.Equals(placed.Id, target.Id, StringComparison.Ordinal))
        {
            // Roll the reader's pick back: the surface never claims a placement the plan did not make.
            _movedWindows[run.Id] = current?.Id ?? string.Empty;
            if (string.IsNullOrEmpty(_movedWindows[run.Id]))
            {
                _movedWindows.Remove(run.Id);
                _movedRuns.Remove(run.Id);
            }

            Rebuild();
            _notice = Loc["TmScheduledRuns_Refused", TitleOf(run), WindowTitle(target)];
            Announce(_notice);
            StateHasChanged();
            return;
        }

        Announce(Loc["TmScheduledRuns_Moved", TitleOf(run), WindowTitle(target)]);
        StateHasChanged();

        if (OnMove.HasDelegate)
        {
            await OnMove.InvokeAsync(new ScheduledRunMoveRequest(run, current, target));
        }
    }

    private async Task MoveFromMenuAsync(ScheduledRun run, RunWindow target) => await MoveToWindowAsync(run, target);

    /// <summary>The plan of one run, or null when the host does not supply it.</summary>
    /// <param name="runId">The run to look for.</param>
    /// <returns>The plan, or null.</returns>
    internal ScheduledRunPlan? PlanOf(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        foreach (var plan in _plans)
        {
            if (string.Equals(plan.Run.Id, runId, StringComparison.Ordinal))
            {
                return plan;
            }
        }

        return null;
    }

    /// <summary>The window with the supplied id, or null when the plan does not use it.</summary>
    /// <param name="windowId">The id to look for.</param>
    /// <returns>The window, or null.</returns>
    internal RunWindow? FindWindow(string? windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
        {
            return null;
        }

        foreach (var window in _windowList)
        {
            if (string.Equals(window.Id, windowId, StringComparison.Ordinal))
            {
                return window;
            }
        }

        return null;
    }

    // ── Labels and formatting ────────────────────────────────────────────────

    /// <summary>The run's title, or the localized fallback for a run the host left untitled.</summary>
    /// <param name="run">The run to name.</param>
    /// <returns>The title.</returns>
    internal string TitleOf(ScheduledRun run)
        => string.IsNullOrWhiteSpace(run.Title) ? Loc["TmScheduledRuns_RunUntitled"] : run.Title;

    /// <summary>The window's heading: its time range, formatted in the supplied culture.</summary>
    /// <param name="window">The window to describe.</param>
    /// <param name="culture">The culture to format in.</param>
    /// <returns>The time range.</returns>
    internal static string WindowTitle(RunWindow window, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return window.HasDuration
            ? LocJoin(window.Start.ToString("t", culture), window.End.ToString("t", culture))
            : window.Start.ToString("t", culture);

        static string LocJoin(string start, string end) => start + " \u2013 " + end;
    }

    /// <summary>The time range one slice occupies, formatted in the supplied culture.</summary>
    /// <param name="segment">The slice to describe.</param>
    /// <param name="culture">The culture to format in.</param>
    /// <returns>The time range of the slice.</returns>
    internal static string SliceTimeText(ScheduledRunSegment segment, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return segment.Duration > TimeSpan.Zero
            ? segment.Start.ToString("t", culture) + " \u2013 " + segment.End.ToString("t", culture)
            : segment.Start.ToString("t", culture);
    }

    /// <summary>The day heading, formatted in the supplied culture ("Monday 9 November").</summary>
    /// <param name="day">The day to format.</param>
    /// <param name="culture">The culture to format in.</param>
    /// <returns>The formatted day.</returns>
    internal static string DayTitle(DateOnly day, CultureInfo? culture = null)
        => day.ToString("dddd d MMMM", culture ?? CultureInfo.CurrentCulture);

    /// <summary>The window's committed load, as the host reports it.</summary>
    /// <param name="window">The window to describe.</param>
    /// <returns>The localized capacity sentence.</returns>
    internal string CapacityText(RunWindow window)
        => Loc["TmScheduledRuns_Capacity", Count(window.EffectiveUsed), Count(window.EffectiveCapacity)];

    /// <summary>How many units a slice covers, with the grammatical plural of the count.</summary>
    /// <param name="units">The number of units.</param>
    /// <returns>The localized unit phrase.</returns>
    internal string UnitsText(int units) => Loc.Plural("TmScheduledRuns_Units", units);

    /// <summary>The overload line of a window the host committed beyond its capacity.</summary>
    /// <param name="window">The window to describe.</param>
    /// <returns>The localized sentence, or an empty string when the window is not overloaded.</returns>
    internal string OverloadText(RunWindow window)
        => window.IsOverloaded ? Loc["TmScheduledRuns_OverloadedBy", UnitsText(window.Overflow)] : string.Empty;

    /// <summary>The localized word for a run's status.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The word.</returns>
    internal string StatusLabel(ScheduledRunStatus status) => status switch
    {
        ScheduledRunStatus.Planned => Loc["TmScheduledRuns_StatusPlanned"],
        ScheduledRunStatus.Running => Loc["TmScheduledRuns_StatusRunning"],
        ScheduledRunStatus.Succeeded => Loc["TmScheduledRuns_StatusSucceeded"],
        ScheduledRunStatus.Failed => Loc["TmScheduledRuns_StatusFailed"],
        ScheduledRunStatus.Paused => Loc["TmScheduledRuns_StatusPaused"],
        _ => Loc["TmScheduledRuns_StatusUnknown"],
    };

    /// <summary>The stable slug of a status, used as the <c>data-status</c> hook instead of a localized word.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The slug.</returns>
    internal static string StatusSlug(ScheduledRunStatus status) => status switch
    {
        ScheduledRunStatus.Planned => "planned",
        ScheduledRunStatus.Running => "running",
        ScheduledRunStatus.Succeeded => "succeeded",
        ScheduledRunStatus.Failed => "failed",
        ScheduledRunStatus.Paused => "paused",
        _ => "unknown",
    };

    /// <summary>The palette token of one run, so the same run is recognisable in every window's bar and row.</summary>
    /// <param name="run">The run to colour.</param>
    /// <returns>A <c>--tm-agent-color-*</c> token name.</returns>
    internal string RunColorToken(ScheduledRun run)
    {
        var index = 0;
        for (var position = 0; position < _runsView.Count; position++)
        {
            if (string.Equals(_runsView[position].Id, run.Id, StringComparison.Ordinal))
            {
                index = position;
                break;
            }
        }

        return AgentRef.PaletteToken(index);
    }

    /// <summary>The inline style of one bar slice: where it starts, how wide it is and which run colour it carries.</summary>
    /// <param name="segment">The slice to describe.</param>
    /// <param name="colorToken">The run's palette token.</param>
    /// <returns>The style attribute value.</returns>
    internal static string SliceStyle(ScheduledRunSegment segment, string colorToken)
    {
        var capacity = segment.Window.EffectiveCapacity;
        if (capacity <= 0)
        {
            return string.Empty;
        }

        var start = UnitPercent(segment.Window, segment.Start);
        var size = UnitPercent(segment.Window, segment.End) - start;
        return string.Create(CultureInfo.InvariantCulture,
            $"--tm-runs-slice-start: {start:0.##}%; --tm-runs-slice-size: {size:0.##}%; --tm-runs-slice-color: var({colorToken})");
    }

    /// <summary>The inline style of a window's already-committed fill.</summary>
    /// <param name="window">The window to describe.</param>
    /// <returns>The style attribute value.</returns>
    internal static string CommittedStyle(RunWindow window)
    {
        var used = window.EffectiveCapacity == 0
            ? 0d
            : Math.Clamp((double)window.EffectiveUsed * 100d / window.EffectiveCapacity, 0d, 100d);
        return string.Create(CultureInfo.InvariantCulture, $"--tm-runs-slice-size: {used:0.##}%");
    }

    /// <summary>
    /// The share of the visible capacity that is committed or planned, in percent, clamped to <c>0…100</c>: a window the
    /// host over-committed pushes the total past 100, and the excess is reported by <see cref="OverloadedCount"/> and by
    /// the window's own overload line instead of by an impossible percentage.
    /// </summary>
    internal int UsagePercent
    {
        get
        {
            var capacity = 0;
            var used = 0;
            foreach (var view in _windowViews)
            {
                capacity += view.Window.EffectiveCapacity;
                used += view.Window.EffectiveUsed + view.Entries.Sum(entry => entry.Segment.Units);
            }

            // Clamped at 100: a window the host over-committed would otherwise report "104 %", and the excess is
            // already named by the overload line and the Overloaded count. Never below zero either.
            return capacity == 0
                ? 0
                : Math.Clamp((int)Math.Round(used * 100d / capacity, MidpointRounding.AwayFromZero), 0, 100);
        }
    }

    /// <summary>How many visible windows the host committed beyond their capacity.</summary>
    internal int OverloadedCount => _windowViews.Count(view => view.Window.IsOverloaded);

    /// <summary>The one-line summary above the schedule.</summary>
    internal string SummaryText => Loc["TmScheduledRuns_Summary",
        Count(_windowViews.Count),
        Count(UsagePercent),
        Count(OverloadedCount)];

    private static double UnitPercent(RunWindow window, DateTimeOffset instant)
    {
        var ticks = window.Duration.Ticks;
        if (ticks <= 0)
        {
            return 0d;
        }

        var offset = instant - window.Start;
        var percent = offset.Ticks * 100d / ticks;
        return Math.Clamp(percent, 0d, 100d);
    }

    private static string Count(int value) => value.ToString(CultureInfo.CurrentCulture);

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

    /// <summary>Every run slice one window holds, in the window's own capacity order.</summary>
    /// <param name="Window">The window.</param>
    /// <param name="Entries">The runs placed in it, in capacity order.</param>
    internal sealed record ScheduledWindowView(RunWindow Window, List<ScheduledWindowEntry> Entries);

    /// <summary>One run's slice inside one window.</summary>
    /// <param name="Plan">The run's plan (its status, title and unresolved units).</param>
    /// <param name="Segment">The slice this window holds.</param>
    /// <param name="PartNumber">Which slice of the run this is (1-based).</param>
    internal sealed record ScheduledWindowEntry(ScheduledRunPlan Plan, ScheduledRunSegment Segment, int PartNumber)
    {
        /// <summary>How many slices the run has in total.</summary>
        internal int PartCount => Plan.Segments.Count;
    }

    /// <summary>One day of the schedule, with its windows in time order.</summary>
    /// <param name="Day">The day.</param>
    /// <param name="Windows">The windows that start on it.</param>
    internal sealed record ScheduledDayView(DateOnly Day, List<ScheduledWindowView> Windows);

    /// <summary>One row of the table view.</summary>
    /// <param name="Plan">The run the row belongs to.</param>
    /// <param name="Segment">The slice the row describes, or null for a run that was not placed at all.</param>
    /// <param name="Part">Which slice of the run the row is (1-based).</param>
    /// <param name="IsUnplaced">Whether the row reports units that fit in no window.</param>
    internal sealed record ScheduledTableRow(ScheduledRunPlan Plan, ScheduledRunSegment? Segment, int Part, bool IsUnplaced);
}
