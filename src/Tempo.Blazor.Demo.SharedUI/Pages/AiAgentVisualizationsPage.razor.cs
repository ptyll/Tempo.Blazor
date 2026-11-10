using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// The <c>/ai-agent-visualizations</c> demo page: ONE simulated multi-agent run rendered by every agent surface of
/// <c>Tempo.Blazor.AI</c> at the same time.
/// <para>
/// The page is the HOST of the whole scenario. The simulator and the three surfaces whose data it changes (the lanes,
/// the graph and the fan-out) live in <c>AiAgentRunLiveSurfaces</c>, so a tick repaints only that subtree; the ten
/// decision surfaces here are the page's own store and never re-render for a tick they do not care about. Every
/// component on the page only renders what it is handed and reports intent back — none of them polls, starts a timer or
/// mutates anything.
/// </para>
/// <para>
/// Every graphical surface has a text or table alternative a keyboard reaches: the components with a view switcher
/// (lanes, graph, handoff chain, fallback chain, scheduled runs) own theirs, and the page adds a table presentation
/// for the fan-out grid, the parallel attempts, the memory panel, the delegate board, the conflict radar and the
/// presence roster. The page owns exactly ONE polite live region, which announces the simulator's own controls; the
/// simulated events are deliberately NOT announced (they would read the whole run aloud) and every reader action is
/// announced by the component that owns it, so nothing is said twice.
/// </para>
/// </summary>
public partial class AiAgentVisualizationsPage
{
    private readonly HashSet<string> _textViews = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<AgentRef> _agents = AiAgentVisualizationsSamples.Agents();

    private string? _attemptWinnerId;
    private string? _bestAttemptId = "draft-b";
    private HandoffOrientation _handoffOrientation = HandoffOrientation.Vertical;
    private FallbackChainMode _fallbackMode = FallbackChainMode.View;
    private FallbackChainViewMode _fallbackView = FallbackChainViewMode.Flow;
    private IReadOnlyList<FallbackChainEntry>? _fallbackChain;
    private FallbackResolutionPolicy _fallbackPolicy = FallbackResolutionPolicy.Pin;
    private AutonomyDialPresentation _autonomyPresentation = AutonomyDialPresentation.Dial;
    private int _autonomyValue = 1;
    private string? _autonomyLoggedLabel;

    private IReadOnlyList<ParallelAttempt> _attempts = [];
    private IReadOnlyList<HandoffStep> _handoffSteps = [];
    private IReadOnlyList<FallbackAttempt> _fallbackAttempts = [];
    private IReadOnlyList<AiModelInfo> _models = [];
    private IReadOnlyList<AutonomyLevel> _autonomyLevels = [];
    private IReadOnlyList<AutonomyLock> _autonomyLocks = [];
    private IReadOnlyList<MemoryItem> _memories = [];
    private bool _memoryEnabled = true;
    private string _memoryFilter = string.Empty;
    private MemoryStatusFilter _memoryStatus = MemoryStatusFilter.All;
    private IReadOnlyList<string> _collapsedCategories = [];
    private string? _memoryLogKey;
    private string? _memoryLogValue;

    private IReadOnlyList<DelegateTask> _delegateTasks = [];
    private IReadOnlyList<DelegateAgent> _delegateAgents = [];
    private bool _delegateAuto;
    private string? _delegateLogKey;
    private string? _delegateLogValue;
    private string? _delegateLogSecond;

    private IReadOnlyList<RunWindow> _windows = [];
    private IReadOnlyList<ScheduledRun> _runs = [];
    private RunViewMode _runsView = RunViewMode.Week;
    private string? _runsLogKey;
    private string? _runsLogValue;
    private string? _runsLogSecond;

    private IReadOnlyList<AgentBranchChanges> _branches = [];
    private IReadOnlyList<ConflictResolution> _resolutions = [];
    private IReadOnlySet<string> _collapsedDirectories = new HashSet<string>(StringComparer.Ordinal);
    private ConflictMergeRequest? _merge;
    private bool _conflictOnly;

    [Inject]
    private ITmLocalizer Loc { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    // ── Data the surfaces render ─────────────────────────────────────────────

    /// <summary>The three scored drafts of the parallel-attempts surface.</summary>
    private IReadOnlyList<ParallelAttempt> Attempts => _attempts;

    /// <summary>The three handoffs of the run.</summary>
    private IReadOnlyList<HandoffStep> HandoffSteps => _handoffSteps;

    /// <summary>The recorded fallback run.</summary>
    private IReadOnlyList<FallbackAttempt> FallbackAttempts => _fallbackAttempts;

    /// <summary>The fictional model catalogue the fallback editor offers.</summary>
    private IReadOnlyList<AiModelInfo> Models => _models;

    /// <summary>The chain the fallback editor starts with, or the recorded attempts when the reader edited none.</summary>
    private IReadOnlyList<FallbackChainEntry> FallbackChainEntries => _fallbackChain
        ?? [.. _fallbackAttempts.Select(attempt => new FallbackChainEntry(attempt.ModelId, attempt.Label))];

    /// <summary>The four grades of autonomy.</summary>
    private IReadOnlyList<AutonomyLevel> AutonomyLevels => _autonomyLevels;

    /// <summary>The limits an administrator enforces.</summary>
    private IReadOnlyList<AutonomyLock> AutonomyLocks => _autonomyLocks;

    /// <summary>The submitted body of a draft, rendered through the attempts' content slot.</summary>
    private static string AttemptBody(string attemptId) => AiAgentVisualizationsSamples.AttemptBody(attemptId);

    // ── Text alternatives ────────────────────────────────────────────────────

    /// <summary>Whether a section shows its table presentation instead of its graphical one.</summary>
    /// <param name="key">The section key.</param>
    /// <returns><c>true</c> when the table is shown.</returns>
    private bool IsTextView(string key) => _textViews.Contains(key);

    /// <summary>Switches one section between its graphical and its table presentation.</summary>
    /// <param name="key">The section key.</param>
    private void ToggleTextView(string key)
    {
        if (!_textViews.Remove(key))
        {
            _textViews.Add(key);
        }
    }

    /// <summary>The label of a section's view toggle: it NAMES the presentation a press shows.</summary>
    /// <param name="key">The section key.</param>
    /// <returns>The localized label.</returns>
    private string TextViewLabel(string key)
        => IsTextView(key) ? Loc["TmAiAgentVizDemo_ShowVisual"] : Loc["TmAiAgentVizDemo_ShowTable"];

    // ── Localized words ──────────────────────────────────────────────────────

    /// <summary>The state word of an activity state, from the shared library keys.</summary>
    private string StatusText(AgentActivityStatus status) => status switch
    {
        AgentActivityStatus.Queued => Loc["TmAgentStatus_Queued"],
        AgentActivityStatus.Running => Loc["TmAgentStatus_Running"],
        AgentActivityStatus.WaitingForApproval => Loc["TmAgentStatus_WaitingForApproval"],
        AgentActivityStatus.Succeeded => Loc["TmAgentStatus_Succeeded"],
        AgentActivityStatus.Failed => Loc["TmAgentStatus_Failed"],
        AgentActivityStatus.Cancelled => Loc["TmAgentStatus_Cancelled"],
        _ => Loc["TmAgentStatus_Idle"],
    };

    /// <summary>The state word of a memory item.</summary>
    private string MemoryStatusText(MemoryItemStatus status) => status switch
    {
        MemoryItemStatus.Active => Loc["TmMemoryPanel_StatusActive"],
        MemoryItemStatus.Paused => Loc["TmMemoryPanel_StatusPaused"],
        MemoryItemStatus.Forgotten => Loc["TmMemoryPanel_StatusForgotten"],
        MemoryItemStatus.PendingReview => Loc["TmMemoryPanel_StatusPendingReview"],
        _ => Loc["TmMemoryPanel_StatusUnknown"],
    };

    /// <summary>The word of a delegate priority grade.</summary>
    private string PriorityText(DelegateTaskPriority? priority) => priority switch
    {
        DelegateTaskPriority.Low => Loc["TmDelegateBoard_PriorityLow"],
        DelegateTaskPriority.Normal => Loc["TmDelegateBoard_PriorityNormal"],
        DelegateTaskPriority.High => Loc["TmDelegateBoard_PriorityHigh"],
        DelegateTaskPriority.Urgent => Loc["TmDelegateBoard_PriorityUrgent"],
        _ => Loc["TmDelegateBoard_PriorityUnknown"],
    };

    /// <summary>The word of a change on a branch.</summary>
    private string ChangeText(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => Loc["TmConflictRadar_ChangeAdded"],
        ChangeKind.Deleted => Loc["TmConflictRadar_ChangeDeleted"],
        ChangeKind.Renamed => Loc["TmConflictRadar_ChangeRenamed"],
        _ => Loc["TmConflictRadar_ChangeModified"],
    };

    /// <summary>An agent's display name, or its id when the run never named it.</summary>
    private string AgentName(string agentId)
        => _agents.FirstOrDefault(agent => string.Equals(agent.Id, agentId, StringComparison.Ordinal))?.Name ?? agentId;

    /// <summary>An agent's role, or an empty line for an agent the run does not know.</summary>
    private string AgentRole(string agentId)
        => _agents.FirstOrDefault(agent => string.Equals(agent.Id, agentId, StringComparison.Ordinal))?.Role ?? string.Empty;

    /// <summary>A workspace path without its repository prefix, so a table cell stays readable.</summary>
    private static string ShortPath(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    // ── Reader actions the page owns ─────────────────────────────────────────

    /// <summary>The page records only the draft the reader confirmed.</summary>
    private void AcceptAttempt(string attemptId)
    {
        _attemptWinnerId = attemptId;
        _bestAttemptId = attemptId;
    }

    /// <summary>The page owns the chain: an edit in the component replaces the page's own list.</summary>
    private void OnFallbackChainChanged(IReadOnlyList<FallbackChainEntry> chain) => _fallbackChain = chain;

    /// <summary>The page owns the policy.</summary>
    private void OnFallbackPolicyChanged(FallbackResolutionPolicy policy) => _fallbackPolicy = policy;

    // ── Memory ───────────────────────────────────────────────────────────────

    /// <summary>The memory list as one sentence: the last change the page received, or nothing yet.</summary>
    private string MemoryLog => _memoryLogKey is null
        ? Loc["TmAiAgentVizDemo_NoEventYet"]
        : Loc[_memoryLogKey] + (_memoryLogValue is null ? string.Empty : " – " + _memoryLogValue);

    private void OnMemoryEnabledChanged(bool enabled)
    {
        _memoryEnabled = enabled;
        _memoryLogKey = enabled ? "TmMemoryPanel_AnnounceEnabled" : "TmMemoryPanel_AnnounceDisabled";
        _memoryLogValue = null;
    }

    private void OnMemoryFilterChanged(string filter) => _memoryFilter = filter;

    private void OnMemoryStatusChanged(MemoryStatusFilter status) => _memoryStatus = status;

    private void OnMemoryCollapsedChanged(IReadOnlyList<string> collapsed) => _collapsedCategories = collapsed;

    private void OnMemoryEdited(MemoryItem item)
    {
        ReplaceMemory(item);
        _memoryLogKey = "TmMemoryPanel_AnnounceEdited";
        _memoryLogValue = item.Text;
    }

    private void OnMemoryPaused(MemoryItem item)
    {
        ReplaceMemory(item with { Status = MemoryItemStatus.Paused });
        _memoryLogKey = "TmMemoryPanel_AnnouncePaused";
        _memoryLogValue = item.Text;
    }

    private void OnMemoryResumed(MemoryItem item)
    {
        ReplaceMemory(item with { Status = MemoryItemStatus.Active });
        _memoryLogKey = "TmMemoryPanel_AnnounceResumed";
        _memoryLogValue = item.Text;
    }

    private void OnMemoryForgotten(MemoryItem item)
    {
        _memories = [.. _memories.Where(memory => memory.Id != item.Id)];
        _memoryLogKey = "TmMemoryPanel_AnnounceForgotten";
        _memoryLogValue = item.Text;
    }

    private void OnMemoryReviewed(MemoryReview review)
    {
        ReplaceMemory(review.Item with
        {
            Status = review.Decision == MemoryReviewDecision.Approve
                ? MemoryItemStatus.Active
                : MemoryItemStatus.Forgotten,
        });
        _memoryLogKey = review.Decision == MemoryReviewDecision.Approve
            ? "TmMemoryPanel_AnnounceApproved"
            : "TmMemoryPanel_AnnounceRejected";
        _memoryLogValue = review.Item.Text;
    }

    private void OnMemoryBulk(MemoryBulkActionRequest request)
    {
        foreach (var item in request.Items)
        {
            switch (request.Action)
            {
                case MemoryBulkAction.Pause:
                    ReplaceMemory(item with { Status = MemoryItemStatus.Paused });
                    break;
                case MemoryBulkAction.Resume:
                    ReplaceMemory(item with { Status = MemoryItemStatus.Active });
                    break;
                default:
                    _memories = [.. _memories.Where(memory => memory.Id != item.Id)];
                    break;
            }
        }

        _memoryLogKey = request.Action switch
        {
            MemoryBulkAction.Pause => "TmMemoryPanel_BulkPause",
            MemoryBulkAction.Resume => "TmMemoryPanel_BulkResume",
            _ => "TmMemoryPanel_BulkForget",
        };
        _memoryLogValue = "× " + request.Count.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Swaps one memory for the page's new version of it — a NEW instance, which is what the panel watches.</summary>
    private void ReplaceMemory(MemoryItem item)
        => _memories = [.. _memories.Select(memory => memory.Id == item.Id ? item : memory)];

    // ── Delegate board ───────────────────────────────────────────────────────

    /// <summary>The board's summary: the last change the page received, or nothing yet.</summary>
    private string DelegateLog => _delegateLogKey is null
        ? Loc["TmAiAgentVizDemo_NoEventYet"]
        : Loc[_delegateLogKey, _delegateLogValue ?? string.Empty, _delegateLogSecond ?? string.Empty];

    private void OnDelegateAutoChanged(bool auto)
    {
        _delegateAuto = auto;
        _delegateLogKey = auto ? "TmDelegateBoard_AutoLabel" : "TmDelegateBoard_BoardLabel";
        _delegateLogValue = null;
        _delegateLogSecond = null;
    }

    private void OnDelegateAssigned(DelegateMoveRequest request)
    {
        ReplaceTask(request.Task with { AssignedAgentId = request.AssigneeId });
        _delegateLogKey = "TmDelegateBoard_Assigned";
        _delegateLogValue = request.Task.Title;
        _delegateLogSecond = request.To?.Agent.Name;
    }

    private void OnDelegateUnassigned(DelegateMoveRequest request)
    {
        ReplaceTask(request.Task with { AssignedAgentId = null });
        _delegateLogKey = "TmDelegateBoard_Returned";
        _delegateLogValue = request.Task.Title;
        _delegateLogSecond = null;
    }

    /// <summary>Replaces one task in the page's own list, so the board receives a NEW instance and follows it.</summary>
    private void ReplaceTask(DelegateTask task)
        => _delegateTasks = [.. _delegateTasks.Select(current => current.Id == task.Id ? task : current)];

    // ── Scheduled runs ───────────────────────────────────────────────────────

    /// <summary>The scheduler's summary: the last move the page received, or nothing yet.</summary>
    private string RunsLog => _runsLogKey is null
        ? Loc["TmAiAgentVizDemo_NoEventYet"]
        : Loc[_runsLogKey, _runsLogValue ?? string.Empty, _runsLogSecond ?? string.Empty];

    /// <summary>The page owns the plan: a reported move becomes the run's preferred start window.</summary>
    private void OnRunMoved(ScheduledRunMoveRequest request)
    {
        _runs =
        [
            .. _runs.Select(run => run.Id == request.Run.Id
                ? run with
                {
                    Segments =
                    [
                        new RunSegment(request.To.Id, request.To.Start, request.To.End, run.PlannedUnits),
                    ],
                }
                : run),
        ];
        _runsLogKey = "TmScheduledRuns_Moved";
        _runsLogValue = request.Run.Title;
        _runsLogSecond = request.To.Id;
    }

    // ── Conflict radar ───────────────────────────────────────────────────────

    /// <summary>The radar's summary: the last decision list or merge request the page received.</summary>
    private string ConflictLog => _merge is { } merge
        ? Loc["TmAiAgentsDemo_ConflictMerged",
            merge.FileCount.ToString(CultureInfo.CurrentCulture),
            merge.ResolvedConflictCount.ToString(CultureInfo.CurrentCulture)]
        : _resolutions.Count == 0
            ? Loc["TmAiAgentVizDemo_NoEventYet"]
            : Loc["TmAiAgentsDemo_ConflictResolved",
                _resolutions.Count.ToString(CultureInfo.CurrentCulture)];

    /// <summary>The page is the store of the reader's decisions; the radar follows the list it echoes back.</summary>
    private void OnResolutionsChanged(IReadOnlyList<ConflictResolution> resolutions)
    {
        _resolutions = resolutions;
        _merge = null;
    }

    /// <summary>The merge is reported once and applied by the host — the demo logs what it received.</summary>
    private void OnMerge(ConflictMergeRequest request) => _merge = request;

    /// <summary>The decision a conflict row carries, as the page's table presentation reads it.</summary>
    private string ResolutionText(string path)
    {
        var resolution = _resolutions.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.Ordinal));

        return resolution is null
            ? Loc["TmAiAgentVizDemo_NotDecided"]
            : resolution.Kind == ConflictResolutionKind.Manual
                ? Loc["TmConflictRadar_ManualSide"]
                : AgentName(resolution.AgentId ?? string.Empty);
    }


    /// <summary>The chain as one line: the edit the reader made is visible here.</summary>
    private string FallbackLog => Loc["TmAiAgentsDemo_FallbackChainLog",
        string.Join(" \u2192 ", FallbackChainEntries.Select(entry => entry.DisplayName))];

    /// <summary>The last autonomy change the page received, or an honest "nothing yet".</summary>
    private string AutonomyLog => _autonomyLoggedLabel is null
        ? Loc["TmAiAgentVizDemo_NoEventYet"]
        : Loc["TmAutonomyDial_Announce", _autonomyLoggedLabel];

    /// <summary>The two chain directions the page offers.</summary>
    private IReadOnlyList<SegmentedOption<HandoffOrientation>> HandoffOrientationOptions => _handoffOrientationOptions
        ??=
        [
            new() { Label = Loc["TmAiAgentsDemo_HandoffVertical"], Value = HandoffOrientation.Vertical },
            new() { Label = Loc["TmAiAgentsDemo_HandoffHorizontal"], Value = HandoffOrientation.Horizontal },
        ];

    /// <summary>The two presentations of the fallback surface: the recorded run, or the editor over the chain.</summary>
    private IReadOnlyList<SegmentedOption<FallbackChainMode>> FallbackModeOptions => _fallbackModeOptions
        ??=
        [
            new() { Label = Loc["TmAiAgentsDemo_FallbackRun"], Value = FallbackChainMode.View },
            new() { Label = Loc["TmAiAgentsDemo_FallbackEdit"], Value = FallbackChainMode.Edit },
        ];

    /// <summary>The two presentations of the autonomy dial: the slider, or one segment per grade.</summary>
    private IReadOnlyList<SegmentedOption<AutonomyDialPresentation>> AutonomyPresentationOptions =>
        _autonomyPresentationOptions ??=
        [
            new() { Label = Loc["TmAiAgentsDemo_AutonomyDial"], Value = AutonomyDialPresentation.Dial },
            new() { Label = Loc["TmAiAgentsDemo_AutonomySegmented"], Value = AutonomyDialPresentation.Segmented },
        ];

    private IReadOnlyList<SegmentedOption<HandoffOrientation>>? _handoffOrientationOptions;
    private IReadOnlyList<SegmentedOption<FallbackChainMode>>? _fallbackModeOptions;
    private IReadOnlyList<SegmentedOption<AutonomyDialPresentation>>? _autonomyPresentationOptions;

    /// <summary>The page owns the direction; the component only reports it.</summary>
    private void OnHandoffOrientationChanged(HandoffOrientation orientation) => _handoffOrientation = orientation;

    /// <summary>The page owns the fallback presentation.</summary>
    private void OnFallbackModeChanged(FallbackChainMode mode) => _fallbackMode = mode;

    /// <summary>The page owns the autonomy presentation.</summary>
    private void OnAutonomyPresentationChanged(AutonomyDialPresentation presentation)
        => _autonomyPresentation = presentation;

    /// <summary>The page owns the grade: the dial reports it and the page keeps it.</summary>
    private void OnAutonomyChanged(int value)
    {
        _autonomyValue = value;
        _autonomyLoggedLabel = _autonomyLevels.FirstOrDefault(level => level.Value == value)?.Label;
    }

    /// <summary>A duration as a reader counts it: minutes under an hour, whole hours, then hours and minutes.</summary>
    private string EstimateText(TimeSpan? duration)
    {
        if (duration is not { } value || value <= TimeSpan.Zero)
        {
            return Loc["TmAiAgentVizDemo_NoEstimate"];
        }

        var minutes = (int)Math.Round(value.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 60)
        {
            return Loc["TmDelegateBoard_Minutes", minutes.ToString(CultureInfo.CurrentCulture)];
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        var hoursText = hours.ToString(CultureInfo.CurrentCulture);

        return rest == 0
            ? Loc["TmDelegateBoard_Hours", hoursText]
            : Loc["TmDelegateBoard_HoursMinutes", hoursText, rest.ToString(CultureInfo.CurrentCulture)];
    }

    /// <summary>A memory's confidence as a percentage, or an honest dash when the agent never measured it.</summary>
    private string ConfidenceText(double? confidence)
        => confidence is { } value ? value.ToString("P0", CultureInfo.CurrentCulture) : "—";

    /// <summary>The page owns the view of the scheduler.</summary>
    private void OnRunsViewChanged(RunViewMode view) => _runsView = view;

    /// <summary>The paths more than one agent changed — the same conflict rule the radar applies.</summary>
    private IReadOnlyList<string> ConflictingPaths() =>
    [
        .. _branches
            .SelectMany(branch => branch.ChangedPaths.Select(change => change.Path))
            .GroupBy(path => path, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(path => path, StringComparer.Ordinal),
    ];

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        var now = Clock.GetUtcNow();

        _attempts = AiAgentVisualizationsSamples.Attempts();
        _handoffSteps = AiAgentVisualizationsSamples.HandoffSteps(now);
        _fallbackAttempts = AiAgentVisualizationsSamples.FallbackAttempts(now);
        _models = AiAgentVisualizationsSamples.Models();
        _autonomyLevels = AiAgentVisualizationsSamples.AutonomyLevels();
        _autonomyLocks = AiAgentVisualizationsSamples.AutonomyLocks();
        _memories = AiAgentVisualizationsSamples.Memories(now);
        _delegateAgents = AiAgentVisualizationsSamples.DelegateAgents(now);
        _delegateTasks = AiAgentVisualizationsSamples.DelegateTasks();
        _windows = AiAgentVisualizationsSamples.RunWindows(now);
        _runs = AiAgentVisualizationsSamples.ScheduledRuns();
        _branches = AiAgentVisualizationsSamples.Branches();
    }
}
