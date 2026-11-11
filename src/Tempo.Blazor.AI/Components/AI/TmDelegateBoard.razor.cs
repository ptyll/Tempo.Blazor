using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Work handed out to agents: a backlog column and one column per agent, where a task moves by pointer drag, by the
/// per-card Assign menu, or by the automatic pass that hands each unassigned task to the first free agent.
/// <para>
/// Every card states what it is and what it costs (title, priority word, estimate) and every column states the HOST's
/// capacity facts (<c>ActiveCount</c> of <c>MaxConcurrency</c>, how many cards it holds, and until when the agent is
/// busy). A column at capacity is painted as full — a word plus an icon — and REFUSES a drop with an announcement
/// instead of quietly accepting work it cannot run.
/// </para>
/// <para>
/// The board never mutates the host's lists. It keeps its own copy of what the reader moved (the target agent per task)
/// and drops that copy the moment the host supplies a NEW instance of the task, so a host that re-renders the same
/// snapshot cannot undo the reader's move and a host that accepts it is never fought by a stale overlay. Every move is
/// reported once through <see cref="OnAssign"/> or <see cref="OnUnassign"/>.
/// </para>
/// <para>
/// Accessibility: the pointer drag is the shared <c>tm-sortable.js</c> over <c>pointer-drag.js</c> (through
/// <c>TmSortableList</c>'s group support — this component writes no gesture), and every card's Assign menu is the
/// keyboard and tap alternative, so nothing is drag-only (WCAG 2.5.7). Every state is a word plus an icon, the
/// occupied-until preview is driven by focus as well as by hover, and the surface owns exactly ONE polite live region.
/// </para>
/// </summary>
public partial class TmDelegateBoard : TmComponentBase, IDisposable
{
    /// <summary>The list id of the backlog column inside the board's drag group.</summary>
    internal const string BacklogListId = "backlog";

    private readonly string _id = $"tm-ai-delegate-{Guid.NewGuid():n}"[..25];

    private readonly Dictionary<string, DelegateTask> _overlayTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _overlayAssignees = new(StringComparer.Ordinal);
    private readonly HashSet<string> _autoHandled = new(StringComparer.Ordinal);
    private readonly HashSet<string> _readerBacklog = new(StringComparer.Ordinal);
    private readonly List<DelegateTask> _autoQueue = [];
    private readonly HashSet<string> _autoQueueIds = new(StringComparer.Ordinal);

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private DateTimeOffset _now = DateTimeOffset.MinValue;

    // The switch the reader sees. It shadows AutoAssign through the documented rule: the reader's pick wins until the
    // parameter MOVES, so a host that re-renders the same value cannot undo it and a host that never binds the two-way
    // parameter still gets a switch that reacts (instead of one that looks stuck).
    private bool _autoState;
    private bool _autoSeeded;
    private bool _lastAutoParameter;

    // The columns are rebuilt once per render-ish pass, never per property read: a property that built a list on every
    // access would give the sortable lists a new item list on every markup expression.
    private IReadOnlyList<DelegateTask> _backlog = [];
    private IReadOnlyList<DelegateColumnView> _columns = [];
    private IReadOnlyList<DelegateAgent> _agentsView = [];
    private IReadOnlyList<DelegateTask> _tasksView = [];

    private string? _previewAgentId;
    private string? _previewTaskId;

    // The LIVE drag (opt-in through TmSortableList.TrackDragOver): which card is being dragged, where it came from
    // and the one column it would land in. Null while no gesture is running. The lifted card itself is resolved once
    // per report (and once per rebuild) instead of on every read: the projection asks for it once per column.
    private DelegateTask? _dragTask;
    private string? _dragTaskId;
    private string? _dragSourceListId;
    private string? _dragTargetListId;
    // The grace window a column's preview survives after focus/pointer leaves it: a focus move between two
    // cards of the SAME column fires focusout then focusin, and clearing on the first one blinks the preview.
    private CancellationTokenSource? _previewLeaveCts;
    private bool _disposed;
    private string _notice = string.Empty;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The tasks to show, assigned or not. Null renders an empty backlog and no cards; a null entry inside the list is
    /// skipped. A task whose <see cref="DelegateTask.AssignedAgentId"/> names an agent the board does not have stays in
    /// the backlog, so nothing can disappear.
    /// </summary>
    [Parameter] public IReadOnlyList<DelegateTask>? Tasks { get; set; }

    /// <summary>
    /// The agents, one column each, in the order they should be read (and in the order the automatic pass offers them).
    /// Null or empty shows the backlog alone plus a note that there are no agents.
    /// </summary>
    [Parameter] public IReadOnlyList<DelegateAgent>? Agents { get; set; }

    /// <summary>Raised once per committed move that puts a task on an agent (from the backlog or from another agent).</summary>
    [Parameter] public EventCallback<DelegateMoveRequest> OnAssign { get; set; }

    /// <summary>Raised once per committed move that returns a task to the backlog.</summary>
    [Parameter] public EventCallback<DelegateMoveRequest> OnUnassign { get; set; }

    /// <summary>
    /// Whether the automatic pass runs, two-way bound with <see cref="AutoAssignChanged"/> (<c>@bind-AutoAssign</c>).
    /// While it is on, every unassigned task is handed to the first free agent — the one that becomes free first, per
    /// <see cref="DelegateAutoAssigner"/> — and each such move is reported through <see cref="OnAssign"/>. A task the
    /// reader puts BACK in the backlog stays there until the switch is turned off and on again, so the pass never
    /// fights the reader.
    /// </summary>
    [Parameter] public bool AutoAssign { get; set; }

    /// <summary>Fires with the new switch state.</summary>
    [Parameter] public EventCallback<bool> AutoAssignChanged { get; set; }

    /// <summary>The heading above the board. Null renders the localized default ("Delegate board").</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>The accessible name of the column area. Null uses the localized default ("Delegate board").</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// The clock the occupied-until preview reads. Null takes the injected <see cref="TimeProvider"/> and falls back to
    /// <see cref="TimeProvider.System"/>, so a test can pin the projection.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The agents, with nulls dropped — one column each, in the host's order.</summary>
    internal IReadOnlyList<DelegateAgent> AgentColumns => _agentsView;

    /// <summary>The tasks the board shows, with nulls dropped.</summary>
    internal IReadOnlyList<DelegateTask> AllTasks => _tasksView;

    /// <summary>The unassigned tasks, most urgent first (the order the automatic pass and the backlog use).</summary>
    internal IReadOnlyList<DelegateTask> BacklogTasks => _backlog;

    /// <summary>Every agent column with the tasks it holds, in the host's order.</summary>
    internal IReadOnlyList<DelegateColumnView> Columns => _columns;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    internal int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The column headings sit one level below the surface title, never past <c>h6</c>.</summary>
    internal int ColumnHeadingLevel => Math.Min(6, EffectiveHeadingLevel + 1);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    internal string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmDelegateBoard_Title"] : Title!;

    /// <summary>The settled accessible name of the column area.</summary>
    internal string EffectiveLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmDelegateBoard_BoardLabel"] : AriaLabel!;

    /// <summary>The drag group every column of this board shares. Two boards never share one.</summary>
    internal string DragGroup => _id;

    /// <summary>The one polite live region's current text.</summary>
    internal string Announcement => _announcement;

    /// <summary>The visible refusal / error line, or an empty string.</summary>
    internal string Notice => _notice;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private string AutoHintId => _id + "-auto-hint";

    private string BacklogTitleId => _id + "-backlog-title";

    private string ColumnTitleId(DelegateAgent agent) => _id + "-agent-" + agent.Id;

    private string SummaryText => Loc["TmDelegateBoard_Summary",
        Count(BacklogTasks.Count),
        Count(AgentColumns.Count(agent => agent.HasCapacity)),
        Count(AgentColumns.Count)];

    private string RootClass
    {
        get
        {
            var root = "tm-ai-delegate";
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _clock = TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;
        _now = _clock.GetUtcNow();

        if (!_autoSeeded || AutoAssign != _lastAutoParameter)
        {
            _autoSeeded = true;
            _lastAutoParameter = AutoAssign;
            _autoState = AutoAssign;
        }

        RefreshView();
        PruneOverlay();
        Rebuild();

        // Auto mode reacts to the tasks that ARRIVED unassigned; a task the reader returned stays where they put it.
        if (_autoState)
        {
            QueueAutoAssign();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The automatic pass awaits the host's callbacks, so it runs AFTER the paint instead of inside the parameter
        // set: an EventCallback invoked there could re-enter this component mid-render.
        if (_autoQueue.Count == 0)
        {
            return;
        }

        var pending = _autoQueue.ToArray();
        _autoQueue.Clear();
        _autoQueueIds.Clear();

        // ONE plan for the whole queue: the strategy tracks the slots the pass itself consumes, so three tasks do not all
        // land on the first agent that still has a free slot according to the host's (unchanged) ActiveCount.
        var plan = DelegateAutoAssigner.Plan(pending, AgentColumns, _now);
        var moved = 0;
        foreach (var assignment in plan)
        {
            if (await MoveAsync(assignment.Task, assignment.Agent, auto: true))
            {
                moved++;
            }
        }

        var unplaced = pending.Where(task => !plan.Any(item =>
            string.Equals(item.Task.Id, task.Id, StringComparison.Ordinal))).ToList();

        if (moved > 0)
        {
            Announce(Loc.Plural("TmDelegateBoard_AutoSummary", moved));
            StateHasChanged();
        }
        else if (unplaced.Count > 0)
        {
            Announce(Loc["TmDelegateBoard_AutoNone", TitleOf(unplaced[0])]);
            StateHasChanged();
        }

        // A task that found no agent is remembered as handled (AutoAssignAsync marks it) so the pass does not repeat
        // its announcement on every render.
        foreach (var task in unplaced)
        {
            _autoHandled.Add(task.Id);
        }
    }

    // ── The overlay (the reader's own moves) ─────────────────────────────────

    /// <summary>
    /// The agent that holds a task right now: the reader's own move when there is one, otherwise the host's assignment.
    /// Returns null for the backlog, including a task assigned to an agent this board does not show.
    /// </summary>
    /// <param name="task">The task to resolve.</param>
    /// <returns>The agent holding it, or null.</returns>
    internal DelegateAgent? Assignee(DelegateTask task)
    {
        if (task is null)
        {
            return null;
        }

        var assignedId = _overlayAssignees.TryGetValue(task.Id, out var overlay) ? overlay : task.AssignedAgentId;
        return FindAgent(assignedId);
    }

    /// <summary>The agent with the supplied id, or null when the board has no such column.</summary>
    /// <param name="agentId">The id to look for.</param>
    /// <returns>The agent, or null.</returns>
    internal DelegateAgent? FindAgent(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return null;
        }

        foreach (var agent in AgentColumns)
        {
            if (string.Equals(agent.Id, agentId, StringComparison.Ordinal))
            {
                return agent;
            }
        }

        return null;
    }

    /// <summary>The tasks the board shows in one column (null = the backlog), in display order.</summary>
    /// <param name="agentId">The column's agent id, or null for the backlog.</param>
    /// <returns>The column's tasks.</returns>
    internal IReadOnlyList<DelegateTask> TasksOf(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return _backlog;
        }

        foreach (var column in _columns)
        {
            if (string.Equals(column.Agent.Id, agentId, StringComparison.Ordinal))
            {
                return column.Tasks;
            }
        }

        return [];
    }

    /// <summary>The board's card with the supplied id, or null when this board does not show it.</summary>
    /// <param name="taskId">The id to look for.</param>
    /// <returns>The card, or null.</returns>
    private DelegateTask? FindTask(string? taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return null;
        }

        foreach (var task in _tasksView)
        {
            if (string.Equals(task.Id, taskId, StringComparison.Ordinal))
            {
                return task;
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the host's parameters into the view the rest of the component reads. It runs BEFORE the overlay is pruned,
    /// because pruning compares the overlay's remembered instance with the one the host supplies NOW — against the
    /// previous render's list every instance would look new (or, worse, every one would look unchanged).
    /// </summary>
    private void RefreshView()
    {
        _agentsView = Agents is null ? [] : [.. Agents.Where(agent => agent is not null)];
        _tasksView = Tasks is null ? [] : [.. Tasks.Where(task => task is not null)];
    }

    /// <summary>
    /// Rebuilds the visible grouping from the host's lists plus the reader's overlay. Called from the parameter set and
    /// after every committed move, so the columns a render sees always agree with the overlay it would resolve.
    /// </summary>
    private void Rebuild()
    {
        RefreshView();

        var unassigned = new List<DelegateTask>();
        var assigned = new Dictionary<string, List<DelegateTask>>(StringComparer.Ordinal);
        foreach (var task in _tasksView)
        {
            var holder = Assignee(task);
            if (holder is null)
            {
                unassigned.Add(task);
                continue;
            }

            if (!assigned.TryGetValue(holder.Id, out var list))
            {
                list = [];
                assigned[holder.Id] = list;
            }

            list.Add(task);
        }

        _backlog = DelegateAutoAssigner.InAssignmentOrder(unassigned);
        _columns = [.. _agentsView.Select(agent => new DelegateColumnView(
            agent,
            assigned.TryGetValue(agent.Id, out var tasks) ? tasks : []))];

        // A live drag keeps pointing at the host's CURRENT instance of the card: a host that re-supplies it must not
        // have the preview project stale data (the id is the same, so the gesture itself continues).
        if (_dragTaskId is not null)
        {
            _dragTask = FindTask(_dragTaskId);
        }
    }

    /// <summary>
    /// Drops the overlay entries the host has spoken about: a task the host re-supplied as a NEW instance is the host's
    /// word now (it either accepted the move or refused it), and an entry for a task that is gone cannot be replayed.
    /// </summary>
    private void PruneOverlay()
    {
        var tasks = AllTasks;
        if (_overlayTasks.Count > 0)
        {
            List<string>? stale = null;
            foreach (var (taskId, seen) in _overlayTasks)
            {
                var current = tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
                if (current is null || !ReferenceEquals(current, seen))
                {
                    (stale ??= []).Add(taskId);
                }
            }

            if (stale is not null)
            {
                foreach (var taskId in stale)
                {
                    _overlayTasks.Remove(taskId);
                    _overlayAssignees.Remove(taskId);
                }
            }
        }

        var live = new HashSet<string>(tasks.Select(task => task.Id), StringComparer.Ordinal);
        _autoHandled.RemoveWhere(id => !live.Contains(id));
        _readerBacklog.RemoveWhere(id => !live.Contains(id));
        if (_dragTaskId is not null && !live.Contains(_dragTaskId))
        {
            // The host dropped the card that was being dragged: a preview for a task that no longer exists would
            // project a lie, so the gesture's state goes with it.
            ClearDrag();
        }
    }

    // ── Interaction: the menu and the automatic pass ─────────────────────────

    private async Task AssignFromMenuAsync(DelegateTask task, DelegateAgent agent)
    {
        _previewTaskId = task.Id;
        await MoveAsync(task, agent);
    }

    private async Task UnassignAsync(DelegateTask task)
    {
        _readerBacklog.Add(task.Id);
        await MoveAsync(task, null);
    }

    /// <summary>The switch handler: re-arms the automatic pass and reports the new state to the host.</summary>
    /// <param name="value">The new switch state.</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task OnAutoAssignChangedAsync(bool value)
    {
        // The reader's pick is NOT recorded as "seen": only a MOVED parameter may override it.
        _autoState = value;

        if (value)
        {
            // A fresh switch press reconsiders the whole backlog, including what the reader parked there.
            _autoHandled.Clear();
            _readerBacklog.Clear();
        }

        await AutoAssignChanged.InvokeAsync(value);

        if (value)
        {
            QueueAutoAssign();
            StateHasChanged();
        }
    }

    /// <summary>Collects the tasks one automatic pass should place, without touching the host from the parameter set.</summary>
    private void QueueAutoAssign()
    {
        foreach (var task in BacklogTasks)
        {
            if (_autoHandled.Contains(task.Id) || _readerBacklog.Contains(task.Id) || _autoQueueIds.Contains(task.Id))
            {
                continue;
            }

            _autoQueue.Add(task);
            _autoQueueIds.Add(task.Id);
        }
    }

    /// <summary>
    /// Hands one task to the first free agent. A task nobody can take is remembered as handled, so the pass never
    /// re-runs for it on every render (a host that keeps re-rendering the same backlog would otherwise announce the
    /// same failure forever), and it is announced once.
    /// </summary>
    /// <summary>Hands ONE task to the first free agent — the path a host drives directly (and the switch's first step).</summary>
    /// <param name="task">The task to place.</param>
    /// <returns>Whether the task was placed.</returns>
    internal async Task<bool> AutoAssignAsync(DelegateTask task)
    {
        _autoHandled.Add(task.Id);

        var agent = DelegateAutoAssigner.FirstFree(AgentColumns, _now);
        if (agent is null)
        {
            Announce(Loc["TmDelegateBoard_AutoNone", TitleOf(task)]);
            StateHasChanged();
            return false;
        }

        var moved = await MoveAsync(task, agent, auto: true);
        if (moved)
        {
            Announce(Loc.Plural("TmDelegateBoard_AutoSummary", 1));
        }

        return moved;
    }

    // ── Interaction: the drag (one commit from the module) ───────────────────

    /// <summary>
    /// The single commit path of the shared drag: the source column, the target column and the task's index in the
    /// source column come from <see cref="SortableTransferArgs"/>.
    /// </summary>
    /// <param name="args">What the module reported.</param>
    /// <returns>A task that completes when the move has been applied or refused.</returns>
    internal async Task OnTransferAsync(SortableTransferArgs args)
    {
        if (args is null)
        {
            return;
        }

        var sourceId = string.Equals(args.FromListId, BacklogListId, StringComparison.Ordinal) ? null : args.FromListId;
        var targetId = string.Equals(args.ToListId, BacklogListId, StringComparison.Ordinal) ? null : args.ToListId;
        var source = TasksOf(sourceId);

        if (args.FromIndex < 0 || args.FromIndex >= source.Count)
        {
            return;
        }

        var task = source[args.FromIndex];
        var target = FindAgent(targetId);

        if (target is null)
        {
            // The backlog is a legal target: dropping a card there returns it.
            _readerBacklog.Add(task.Id);
            await MoveAsync(task, null);
            return;
        }

        if (target.IsFull)
        {
            Refuse(task, target);
            return;
        }

        await MoveAsync(task, target);
    }

    /// <summary>
    /// Applies one move: records the reader's copy, announces it and reports it to the host. A move onto the agent that
    /// already holds the task is not a change and reports nothing.
    /// </summary>
    /// <param name="task">The task that moves.</param>
    /// <param name="target">The agent that should hold it, or null for the backlog.</param>
    /// <param name="auto">Whether the automatic pass made the move (its announcement is its own).</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task<bool> MoveAsync(DelegateTask task, DelegateAgent? target, bool auto = false)
    {
        var from = Assignee(task);
        if (string.Equals(from?.Id, target?.Id, StringComparison.Ordinal))
        {
            return false;
        }

        _overlayTasks[task.Id] = task;
        _overlayAssignees[task.Id] = target?.Id;
        if (target is null)
        {
            _readerBacklog.Add(task.Id);
        }
        else
        {
            _readerBacklog.Remove(task.Id);
            _autoHandled.Add(task.Id);
        }

        _notice = string.Empty;

        // An automatic move stays quiet until the whole pass is done: the pass announces its own summary, so twenty
        // tasks do not produce twenty sentences.
        if (!auto)
        {
            Announce(target is null
                ? Loc["TmDelegateBoard_Returned", TitleOf(task)]
                : Loc["TmDelegateBoard_Assigned", TitleOf(task), NameOf(target)]);
        }

        Rebuild();
        StateHasChanged();

        if (target is null)
        {
            if (OnUnassign.HasDelegate)
            {
                await OnUnassign.InvokeAsync(new DelegateMoveRequest(task, from, null));
            }

            return true;
        }

        if (OnAssign.HasDelegate)
        {
            await OnAssign.InvokeAsync(new DelegateMoveRequest(task, from, target));
        }

        return true;
    }

    /// <summary>Refuses a drop on a full agent: the card stays put and both the notice line and the region say why.</summary>
    private void Refuse(DelegateTask task, DelegateAgent agent)
    {
        _notice = Loc["TmDelegateBoard_Refused",
            TitleOf(task),
            NameOf(agent),
            Count(agent.Load),
            Count(agent.EffectiveMaxConcurrency)];
        Announce(_notice);
        StateHasChanged();
    }

    // ── The live drag-over preview (the module reports, the board projects) ──

    /// <summary>Whether a pointer drag of one of the board's cards is running right now.</summary>
    internal bool IsDragging => _dragTaskId is not null;

    /// <summary>The card the reader is dragging, or null when no gesture is running.</summary>
    internal DelegateTask? DraggedTask => _dragTask;

    /// <summary>Whether this column is the one the dragged card would land in right now.</summary>
    /// <param name="agent">The column's agent.</param>
    /// <returns>True for exactly one column while a drag is live.</returns>
    internal bool IsDragTarget(DelegateAgent agent)
        => _dragTaskId is not null
            && string.Equals(_dragTargetListId, agent.Id, StringComparison.Ordinal)
            && !string.Equals(_dragSourceListId, agent.Id, StringComparison.Ordinal);

    /// <summary>Whether the dragged card currently hangs over the backlog (the "give it back" target).</summary>
    internal bool IsBacklogDragTarget
        => _dragTaskId is not null
            && string.Equals(_dragTargetListId, BacklogListId, StringComparison.Ordinal)
            && !string.Equals(_dragSourceListId, BacklogListId, StringComparison.Ordinal);

    /// <summary>
    /// The throttled drag-over report of the shared module (one per changed hovered list, plus the resting report when
    /// the gesture ends or is cancelled). The board does not move anything here: it only records which card is being
    /// dragged and which column it would land in, so the columns can project the landing BEFORE the commit.
    /// </summary>
    /// <param name="args">What the module reported.</param>
    /// <returns>A task that completes when the preview state is up to date.</returns>
    internal Task OnDragOverAsync(SortableDragOverArgs args)
    {
        if (args is null)
        {
            return Task.CompletedTask;
        }

        if (args.ToListId is null)
        {
            // Resting: release, Escape, pointercancel or a detached gesture. Every column goes back to its own
            // projection (including the focus/hover preview the reader had before the drag started).
            if (_dragTaskId is null && _dragTargetListId is null && _dragSourceListId is null)
            {
                return Task.CompletedTask;
            }

            ClearDrag();
            StateHasChanged();
            return Task.CompletedTask;
        }

        var task = TaskAt(args.FromListId, args.FromIndex);
        if (task is null)
        {
            ClearDrag();
            StateHasChanged();
            return Task.CompletedTask;
        }

        if (string.Equals(_dragTaskId, task.Id, StringComparison.Ordinal)
            && string.Equals(_dragSourceListId, args.FromListId, StringComparison.Ordinal)
            && string.Equals(_dragTargetListId, args.ToListId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _dragTask = task;
        _dragTaskId = task.Id;
        _dragSourceListId = args.FromListId;
        _dragTargetListId = args.ToListId;
        _notice = string.Empty;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>The card at one index of a column of the drag group (the backlog addressed by its own list id).</summary>
    /// <param name="listId">The list id the module reported.</param>
    /// <param name="index">The index inside that list's items.</param>
    /// <returns>The card, or null when the report names something this board does not show.</returns>
    private DelegateTask? TaskAt(string? listId, int index)
    {
        if (index < 0)
        {
            return null;
        }

        var columnId = string.Equals(listId, BacklogListId, StringComparison.Ordinal) ? null : listId;
        var tasks = TasksOf(columnId);
        return index < tasks.Count ? tasks[index] : null;
    }

    private void ClearDrag()
    {
        _dragTask = null;
        _dragTaskId = null;
        _dragSourceListId = null;
        _dragTargetListId = null;
    }

    // ── The occupied-until preview (focus OR hover, never hover alone) ───────

    /// <summary>
    /// How long a column keeps its preview after the pointer or the focus leaves it. Long enough to span the
    /// <c>focusout</c> → <c>focusin</c> (or <c>mouseleave</c> → <c>mouseenter</c>) pair the browser fires when the
    /// reader steps between two cards of ONE column, short enough to be imperceptible when the reader really leaves.
    /// </summary>
    internal static readonly TimeSpan PreviewLeaveGrace = TimeSpan.FromMilliseconds(150);

    /// <summary>Points the preview at a column the reader entered, cancelling any leave still in its grace window.</summary>
    /// <param name="agent">The column's agent.</param>
    internal void OnAgentPreviewEnter(DelegateAgent agent)
    {
        CancelPendingPreviewLeave();
        if (string.Equals(_previewAgentId, agent.Id, StringComparison.Ordinal))
        {
            return;
        }

        _previewAgentId = agent.Id;
        StateHasChanged();
    }

    /// <summary>
    /// Clears the preview once the grace window closes, unless another enter (or a newer leave) cancelled this one.
    /// The clear is DEFERRED on purpose: <c>focusout</c> bubbles from every card, so a focus move inside one column
    /// produces a leave immediately followed by an enter, and clearing synchronously would blink the projection off.
    /// </summary>
    /// <param name="agent">The column's agent.</param>
    /// <returns>A task that completes when the grace window has elapsed or the leave was cancelled.</returns>
    internal async Task OnAgentPreviewLeaveAsync(DelegateAgent agent)
    {
        CancelPendingPreviewLeave();
        var pending = new CancellationTokenSource();
        _previewLeaveCts = pending;
        try
        {
            await Task.Delay(PreviewLeaveGrace, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_previewLeaveCts, pending))
            {
                _previewLeaveCts = null;
            }

            pending.Dispose();
        }

        if (_disposed || !string.Equals(_previewAgentId, agent.Id, StringComparison.Ordinal))
        {
            return;
        }

        _previewAgentId = null;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Cancels a deferred clear (the reader came back to a column before its grace window closed).</summary>
    private void CancelPendingPreviewLeave()
    {
        var pending = _previewLeaveCts;
        _previewLeaveCts = null;
        pending?.Cancel();
    }

    /// <summary>Stops a pending preview clear when the board goes away, so no continuation renders a disposed component.</summary>
    public void Dispose()
    {
        _disposed = true;
        CancelPendingPreviewLeave();
    }

    /// <summary>
    /// Points the preview at the card the reader is on, so hovering or focusing an agent can project THIS task. Driven by
    /// focus as well as by pointer enter, because a preview that needs a mouse is not available to everybody.
    /// </summary>
    /// <param name="task">The card, or null to return to the resting state.</param>
    internal void PreviewTask(DelegateTask? task)
    {
        var id = task?.Id;
        if (string.Equals(_previewTaskId, id, StringComparison.Ordinal))
        {
            return;
        }

        _previewTaskId = id;
        StateHasChanged();
    }

    /// <summary>
    /// ONE agent column's preview state, projected ONCE per column per render. The availability sentence, its state
    /// slug, its estimate flag and the drag-over marker each used to read the dragged card and re-project the column
    /// on their own, so a single column paid five or six lookups per render.
    /// </summary>
    /// <param name="Projection">The availability projection this column shows.</param>
    /// <param name="IsDragTarget">Whether the dragged card would land in this column right now.</param>
    /// <param name="PointsAtTask">
    /// Whether the hover/focus preview points at this column; false whenever a dragged card resolves — the drag is
    /// reported by <paramref name="IsDragTarget"/> instead.
    /// </param>
    internal readonly record struct ColumnPreview(
        DelegateOccupancyProjection Projection,
        bool IsDragTarget,
        bool PointsAtTask);

    /// <summary>
    /// Projects one agent column once. This is the single source <see cref="PreviewFor"/>, <see cref="PreviewState"/>
    /// and <see cref="PreviewText"/> read from; the markup calls it once per column and feeds the result to the
    /// <see cref="ColumnPreview"/> overloads.
    /// </summary>
    /// <param name="agent">The column's agent.</param>
    /// <returns>The column's projection plus the two flags that explain why it looks the way it does.</returns>
    internal ColumnPreview ColumnPreviewFor(DelegateAgent agent)
    {
        if (DraggedTask is { } dragged)
        {
            // A live drag points at exactly ONE column: the one the card would land in. Every other column — the
            // SOURCE included — falls back to its own resting load, so the source shows its projection WITHOUT the
            // lifted card, and a FULL column never promises a time for a card it is going to refuse.
            var dragTarget = IsDragTarget(agent);
            var projected = dragTarget && !agent.IsFull ? dragged : null;
            return new ColumnPreview(DelegatePresentation.Project(agent, projected, _now), dragTarget, PointsAtTask: false);
        }

        var pointsAtTask = string.Equals(_previewAgentId, agent.Id, StringComparison.Ordinal);
        return new ColumnPreview(
            DelegatePresentation.Project(agent, pointsAtTask ? PreviewTask() : null, _now),
            // IsDragTarget can still be true while the gesture is live but the dragged card no longer resolves;
            // keep reporting it so the drop marker stays.
            IsDragTarget(agent),
            pointsAtTask);
    }

    /// <summary>The projection one agent column shows: the task the reader is pointing at, else the agent's own load.</summary>
    /// <param name="agent">The column's agent.</param>
    /// <returns>The projection.</returns>
    internal DelegateOccupancyProjection PreviewFor(DelegateAgent agent) => ColumnPreviewFor(agent).Projection;

    /// <summary>The task the reader last pointed at, or null.</summary>
    internal DelegateTask? PreviewTask() => FindTask(_previewTaskId);

    /// <summary>The stable state slug of a column's availability line.</summary>
    /// <param name="agent">The column's agent.</param>
    /// <returns><c>free</c>, <c>busy</c> or <c>preview</c>.</returns>
    internal string PreviewState(DelegateAgent agent) => ColumnPreviewState(ColumnPreviewFor(agent));

    /// <summary>The stable state slug of an ALREADY projected column (<c>free</c>, <c>busy</c> or <c>preview</c>).</summary>
    /// <param name="preview">The column's projection.</param>
    /// <returns>The state slug.</returns>
    internal static string ColumnPreviewState(ColumnPreview preview)
        => preview.Projection.UsesEstimate && (preview.IsDragTarget || preview.PointsAtTask)
            ? "preview"
            : preview.Projection.IsKnown ? "busy" : "free";

    /// <summary>
    /// The availability line: "Occupied until 14:20", "Occupied until 14:50 with this task" while a card with an
    /// estimate is pointed at, or "Free now" when there is nothing to project.
    /// </summary>
    /// <param name="agent">The column's agent.</param>
    /// <returns>The localized sentence.</returns>
    internal string PreviewText(DelegateAgent agent) => ColumnPreviewText(ColumnPreviewFor(agent), agent);

    /// <summary>The availability line of an ALREADY projected column (same sentence the parameter overload returns).</summary>
    /// <param name="preview">The column's projection.</param>
    /// <param name="agent">The column's agent (the full-state wording depends on it).</param>
    /// <returns>The localized sentence.</returns>
    internal string ColumnPreviewText(ColumnPreview preview, DelegateAgent agent)
    {
        if (!preview.Projection.IsKnown)
        {
            return Loc["TmDelegateBoard_FreeNow"];
        }

        var time = DelegatePresentation.TimeText(preview.Projection.Until, CultureInfo.CurrentCulture);
        // The drag wording belongs to a column the card can actually LAND in: a full target keeps its own load
        // sentence, because claiming a projected end time for a card it will refuse is a lie.
        if (preview.IsDragTarget && !agent.IsFull)
        {
            return Loc["TmDelegateBoard_DragUntil", time];
        }

        return preview.Projection.UsesEstimate && preview.PointsAtTask
            ? Loc["TmDelegateBoard_PreviewUntil", time]
            : Loc["TmDelegateBoard_OccupiedUntil", time];
    }

    // ── Labels ───────────────────────────────────────────────────────────────

    /// <summary>The card's title, or the localized fallback for a task the host left untitled.</summary>
    /// <param name="task">The task to name.</param>
    /// <returns>The title.</returns>
    internal string TitleOf(DelegateTask task)
        => string.IsNullOrWhiteSpace(task.Title) ? Loc["TmDelegateBoard_TaskUntitled"] : task.Title;

    /// <summary>The agent's display name, or the localized fallback for an unnamed agent.</summary>
    /// <param name="agent">The agent to name.</param>
    /// <returns>The name.</returns>
    internal string NameOf(DelegateAgent agent)
        => agent.Agent.HasName ? agent.Agent.Name : Loc["TmDelegateBoard_AgentUnknown"];

    /// <summary>The label the menu shows for an agent: its name, plus the full state when it has no room.</summary>
    /// <param name="agent">The agent to name.</param>
    /// <returns>The localized menu label.</returns>
    internal string AssignOptionLabel(DelegateAgent agent)
        => agent.IsFull ? Loc["TmDelegateBoard_AssignFull", NameOf(agent)] : NameOf(agent);

    /// <summary>The accessible name of one agent's card list.</summary>
    /// <param name="agent">The agent whose list it is.</param>
    /// <returns>The localized name.</returns>
    internal string AgentListLabel(DelegateAgent agent) => Loc["TmDelegateBoard_AgentList", NameOf(agent)];

    /// <summary>The live region's label for a card: its title and who holds it.</summary>
    /// <param name="task">The task to name.</param>
    /// <returns>The name the reorder announcements use.</returns>
    internal string TaskLabel(DelegateTask task)
    {
        var holder = Assignee(task);
        return holder is null
            ? Loc["TmDelegateBoard_TaskUnassigned", TitleOf(task)]
            : Loc["TmDelegateBoard_TaskAssigned", TitleOf(task), NameOf(holder)];
    }

    private static object TaskKey(DelegateTask task) => task.Id;

    private static string Count(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>One agent column as this pass resolves it: the agent and the tasks it holds.</summary>
    /// <param name="Agent">The column's agent.</param>
    /// <param name="Tasks">The tasks the column shows, in the host's order.</param>
    internal sealed record DelegateColumnView(DelegateAgent Agent, IReadOnlyList<DelegateTask> Tasks);

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
}
