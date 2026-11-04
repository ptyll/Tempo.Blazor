using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The agent's plan of steps: what it intends to do, reviewed and edited BEFORE the run, then watched while
/// it runs.
/// <para>
/// In <see cref="AgentPlanMode.Review"/> a step can be renamed inline (F2 or a click on its title, Enter
/// saves, Escape cancels), added below any row or at the end, removed with an Undo strip, and reordered —
/// by pointer, keyboard or the row's move menu — through the core <c>TmSortableList</c>. The plan-level
/// Approve / Run actions are rendered only when the host binds them.
/// </para>
/// <para>
/// In <see cref="AgentPlanMode.Running"/> the same rows are read-only: a running step counts its live
/// elapsed time from the injected <see cref="TimeProvider"/>, the summary adds the plan's total
/// ("4 of 7 done · 1:24"), and a failed step shows its error and offers "Retry from this step".
/// </para>
/// <para>
/// The component NEVER mutates <see cref="Steps"/>. Every edit raises
/// <c>OnPlanChanged(IReadOnlyList&lt;AgentPlanStep&gt; steps, AgentPlanEdit edit)</c> with the whole proposed
/// list plus the operation that produced it, and the host applies it — exactly the controlled contract
/// <c>TmSortableList</c> follows for its order. A host that ignores the callback keeps its plan and the row
/// snaps back.
/// </para>
/// <para>
/// Accessibility: the plan is a <c>role="group"</c> named by its heading (or by <see cref="AriaLabel"/>)
/// with one polite live region for the plan's own events — renaming, adding, removing, undoing and a
/// refused empty title. Reordering is announced by the nested sortable list, which owns the move sentence,
/// so a move is never read twice. Every action is a native button, the state of a step is always the word
/// next to its icon, and the focus that an edit or a removal takes away is handed on after the render.
/// </para>
/// </summary>
public partial class TmAgentPlan : TmComponentBase, IDisposable
{
    private readonly string _id = $"tm-ai-agent-plan-{Guid.NewGuid():N}";

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AiElapsedTicker? _ticker;
    private bool _disposed;

    /// <summary>
    /// The instant this render paints with: sampled ONCE per render (parameter set or tick), so a row's
    /// clock and the plan's total never describe two slightly different moments.
    /// </summary>
    private DateTimeOffset _now;

    private string? _editingStepId;
    private string? _renameDraft;

    /// <summary>Whether the last save was refused because the title is empty; the editor is then marked
    /// invalid and renders the reason under the field.</summary>
    private bool _renameError;

    private (AgentPlanStep Step, int Index)? _pendingUndo;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private FocusTarget _focusTarget;
    private string? _focusStepId;

    private readonly Dictionary<string, ElementReference> _titleRefs = new(StringComparer.Ordinal);
    private ElementReference _renameInput;
    private ElementReference _addActionElement;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The plan, in display order. The component renders exactly this list (plus the inline rename editor)
    /// and never writes back to it: every edit is reported through <see cref="OnPlanChanged"/>, so a host
    /// that ignores the callback keeps the plan it passed. Null renders the empty state.
    /// </summary>
    [Parameter] public IReadOnlyList<AgentPlanStep>? Steps { get; set; }

    /// <summary>
    /// Whether the plan is being reviewed or is running. Default
    /// <see cref="AgentPlanMode.Review"/>. A <see cref="AgentPlanMode.Running"/> plan is read-only — no
    /// rename, no add, no remove, no reorder and no Approve / Run — but it keeps the live timer of a running
    /// step, the summary, a failed step's error and its Retry action.
    /// </summary>
    [Parameter] public AgentPlanMode Mode { get; set; } = AgentPlanMode.Review;

    /// <summary>
    /// Raised once per edit with the WHOLE proposed step list and the <see cref="AgentPlanEdit"/> that
    /// produced it. The component does not apply the edit itself; the host stores the list and the next
    /// render shows it. Not raised for a rename that changed nothing, for a refused empty title, or for a
    /// reorder that did not move a row.
    /// <para>
    /// This is a plain delegate, not an <see cref="Microsoft.AspNetCore.Components.EventCallback"/>, because
    /// an edit carries two values. Blazor re-renders a component after its own event handler returns, so a
    /// host that mutates its own field here must call <c>StateHasChanged()</c> (or await something that
    /// does) — otherwise the new list is stored but the page keeps painting the previous one until the next
    /// unrelated render.
    /// </para>
    /// </summary>
    [Parameter] public Func<IReadOnlyList<AgentPlanStep>, AgentPlanEdit, Task>? OnPlanChanged { get; set; }

    /// <summary>
    /// Raised when the reader approves the plan. The action is rendered only when this callback is bound, so
    /// a plan without a host decision has no dead button. Review mode only.
    /// </summary>
    [Parameter] public EventCallback OnApprove { get; set; }

    /// <summary>
    /// Raised when the reader starts the run. Rendered only when this callback is bound. Review mode only —
    /// a running plan offers no second start.
    /// </summary>
    [Parameter] public EventCallback OnRun { get; set; }

    /// <summary>
    /// Raised with the failed step whose Retry the reader chose: the host restarts the run FROM that step
    /// (the steps before it keep their state). Available in both modes, because a failure is reported while
    /// the run is going.
    /// </summary>
    [Parameter] public EventCallback<AgentPlanStep> OnRetryFrom { get; set; }

    /// <summary>
    /// The heading above the plan. Null renders the localized default ("Plan"). The heading is not rendered
    /// when both <see cref="Title"/> and <see cref="ShowTitle"/> say no.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// Whether the heading is rendered. Default <c>true</c>; set <c>false</c> when the host page already
    /// provides the section heading. The plan stays named through <see cref="AriaLabel"/> either way.
    /// </summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// The heading level of the rendered title, clamped to 2–6. Default 3, so the plan can sit under any page
    /// outline; the level has no effect when <see cref="ShowTitle"/> is false.
    /// </summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Whether the "4 of 7 done · 1:24" summary is rendered next to the title. Default <c>true</c>; the
    /// summary is the only place the plan's whole progress and total elapsed time are stated.
    /// </summary>
    [Parameter] public bool ShowSummary { get; set; } = true;

    /// <summary>
    /// Whether the plan may be edited at all. Default <c>true</c> — and only meaningful in
    /// <see cref="AgentPlanMode.Review"/>, because a running plan is read-only regardless. Set
    /// <c>false</c> for a review that may only be read and approved.
    /// </summary>
    [Parameter] public bool AllowEditing { get; set; } = true;

    /// <summary>
    /// Rendered instead of the localized "No steps yet." when <see cref="Steps"/> is null or empty. The
    /// plan's instructions and its live region stay rendered either way.
    /// </summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// The accessible name of the plan. Null names it by the rendered heading — so the name a reader hears is
    /// the one they see — and, without a heading, by the localized "Agent plan".
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>Latest announcement, mirrored into the plan's single live region.</summary>
    public string Announcement => _announcement;

    /// <summary>Whether the plan may be edited right now: review mode with
    /// <see cref="AllowEditing"/> on.</summary>
    public bool CanEdit => Mode == AgentPlanMode.Review && AllowEditing;

    /// <summary>Whether the reader can undo the last removal. Cleared by any other edit and by a host whose
    /// list still contains the removed step.</summary>
    public bool CanUndoRemove => _pendingUndo is not null;

    /// <summary>Number of steps whose state is <see cref="AgentStepStatus.Done"/>.</summary>
    public int DoneCount => StepList.Count(step => step.Status == AgentStepStatus.Done);

    private IReadOnlyList<AgentPlanStep> StepList => Steps ?? [];

    private bool IsEmpty => StepList.Count == 0;

    private bool IsRunning => Mode == AgentPlanMode.Running;

    private string InstructionsId => _id + "-instructions";

    private string TitleId => _id + "-title";

    /// <summary>The id of the rename field's on-screen error, the field's <c>aria-describedby</c> target.</summary>
    private string RenameErrorId => _id + "-rename-error";

    private string ModeName => IsRunning ? "running" : "review";

    private string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAgentPlan_Title"] : Title!;

    private string ListLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmAgentPlan_AriaLabel"] : AriaLabel!;

    private string? GroupAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? null : AriaLabel;

    private string? GroupLabelledBy => GroupAriaLabel is null ? (ShowTitle ? TitleId : null) : null;

    private string? GroupFallbackLabel => GroupAriaLabel is null && !ShowTitle ? ListLabel : null;

    private int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The plan's total elapsed time, or null when no step carries a start.</summary>
    private TimeSpan? TotalElapsed
    {
        get
        {
            TimeSpan? total = null;
            foreach (var step in StepList)
            {
                if (step.Elapsed(_now) is { } elapsed)
                {
                    total = (total ?? TimeSpan.Zero) + elapsed;
                }
            }

            return total;
        }
    }

    private string SummaryText
    {
        get
        {
            var done = DoneCount.ToString(CultureInfo.CurrentCulture);
            var count = StepList.Count.ToString(CultureInfo.CurrentCulture);

            return TotalElapsed is { } elapsed
                ? Loc["TmAgentPlan_SummaryWithElapsed", done, count, FormatClock(elapsed)]
                : Loc["TmAgentPlan_Summary", done, count];
        }
    }

    private bool NeedsTicking => !_disposed
        && StepList.Any(step => step.Status == AgentStepStatus.Running && step.StartedAt is not null);

    private string RootClass
    {
        get
        {
            var classes = new System.Text.StringBuilder("tm-ai-agent-plan");
            if (IsRunning) classes.Append(" tm-ai-agent-plan--running");
            if (!string.IsNullOrEmpty(Class)) classes.Append(' ').Append(Class);
            return classes.ToString();
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _clock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        _ticker = new AiElapsedTicker(_clock, OnTickAsync);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var steps = StepList;

        // One clock reading for the whole render: the per-row clocks and the plan total then agree.
        _now = _clock.GetUtcNow();

        // A step that left the list can no longer be renamed, and a running plan is read-only: both drop the
        // editor without touching the host's data.
        if (_editingStepId is { } editing && (!CanEdit || !steps.Any(step => step.Id == editing)))
        {
            _editingStepId = null;
            _renameDraft = null;
            _renameError = false;
        }

        // The host refused the removal when its list still carries the step: there is nothing to undo.
        if (_pendingUndo is { } undo && steps.Any(step => step.Id == undo.Step.Id))
        {
            _pendingUndo = null;
        }

        PruneTitleRefs(steps);
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

        if (_focusTarget != FocusTarget.None)
        {
            var target = _focusTarget;
            var stepId = _focusStepId;
            _focusTarget = FocusTarget.None;
            _focusStepId = null;
            await MoveFocusAsync(target, stepId);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Starts renaming the step at <paramref name="index"/> of <see cref="Steps"/> — the same path the title
    /// click and F2 take. A no-op when the plan is read-only, the index is out of range, or the step is
    /// <see cref="AgentPlanStep.IsEditable"/> false.
    /// </summary>
    /// <param name="index">The zero-based index of the step to rename.</param>
    public void BeginRename(int index)
    {
        if (!CanEdit || index < 0 || index >= StepList.Count)
        {
            return;
        }

        var step = StepList[index];
        if (!step.IsEditable || _editingStepId == step.Id)
        {
            return;
        }

        _editingStepId = step.Id;
        _renameDraft = step.Title;
        _renameError = false;
        _focusTarget = FocusTarget.RenameInput;
        _focusStepId = step.Id;

        // A host may call this from anywhere (a shortcut, a command palette); its own handler re-renders,
        // but a call from outside a UI event would otherwise leave the editor unrendered.
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Drops the pending removal offer without restoring the step — the host decided to keep its own plan.
    /// </summary>
    public void ClearUndo()
    {
        if (_pendingUndo is null)
        {
            return;
        }

        _pendingUndo = null;
        _ = InvokeAsync(StateHasChanged);
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    private void OnRenameInput(ChangeEventArgs args)
    {
        _renameDraft = args.Value?.ToString() ?? string.Empty;

        // Typing is the fix the error asks for, so the refusal clears as soon as the reader types.
        _renameError = false;
    }

    private async Task OnRenameKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !args.Repeat)
        {
            await CommitRenameAsync();
        }
        else if (args.Key == "Escape")
        {
            CancelRename();
        }
    }

    private async Task CommitRenameAsync()
    {
        if (_editingStepId is not { } editing)
        {
            return;
        }

        var index = IndexOf(editing);
        if (index < 0)
        {
            CancelRename();
            return;
        }

        var title = (_renameDraft ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            // A step without a title is an unreadable row. Keep the editor open so the reader can fix it
            // instead of silently discarding the edit, and show the reason next to the field.
            _renameError = true;
            Announce(Loc["TmAgentPlan_TitleRequired"]);
            return;
        }

        var step = StepList[index];
        _editingStepId = null;
        _renameDraft = null;
        _renameError = false;
        _focusTarget = FocusTarget.TitleButton;
        _focusStepId = step.Id;

        if (string.Equals(title, step.Title, StringComparison.Ordinal))
        {
            return;
        }

        var list = StepList.ToList();
        list[index] = step with { Title = title };
        _pendingUndo = null;
        Announce(Loc["TmAgentPlan_AnnounceRenamed", (index + 1).ToString(CultureInfo.CurrentCulture), title]);
        await RaiseAsync(list, AgentPlanEdit.Rename(index, step.Id, title));
    }

    private void CancelRename()
    {
        if (_editingStepId is not { } editing)
        {
            return;
        }

        _editingStepId = null;
        _renameDraft = null;
        _renameError = false;
        _focusTarget = FocusTarget.TitleButton;
        _focusStepId = editing;
        Announce(Loc["TmAgentPlan_AnnounceRenameCancelled"]);
    }

    // ── Add and remove ───────────────────────────────────────────────────────

    /// <summary>Inserts a new, automatically titled step at <paramref name="index"/> and starts renaming it.</summary>
    private async Task AddStepAsync(int index)
    {
        if (!CanEdit)
        {
            return;
        }

        var position = Math.Clamp(index, 0, StepList.Count);
        var step = new AgentPlanStep(Loc["TmAgentPlan_NewStepTitle"]);
        var list = StepList.ToList();
        list.Insert(position, step);

        _pendingUndo = null;
        _editingStepId = step.Id;
        _renameDraft = step.Title;
        _renameError = false;
        _focusTarget = FocusTarget.RenameInput;
        _focusStepId = step.Id;
        Announce(Loc["TmAgentPlan_AnnounceAdded"]);

        await RaiseAsync(list, AgentPlanEdit.Add(position, step));
    }

    private async Task RemoveStepAsync(int index)
    {
        if (!CanEdit || index < 0 || index >= StepList.Count)
        {
            return;
        }

        var step = StepList[index];
        if (!step.IsEditable)
        {
            return;
        }

        // Without a handler there is no host that can apply the removal: the row would stay, so nothing is
        // reported, announced, or offered as an undo (an Undo strip over a row that never went away is a
        // lie — and its Undo would insert a second row with the same Id).
        if (OnPlanChanged is null)
        {
            return;
        }

        var list = StepList.ToList();
        list.RemoveAt(index);

        _pendingUndo = (step, index);
        _editingStepId = null;
        _renameDraft = null;
        _renameError = false;

        // The removal takes the row (and its button) away, so the focus follows the plan: the next row, the
        // previous one when it was last, and the add action when the plan emptied.
        _focusStepId = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)].Id;
        _focusTarget = list.Count == 0 ? FocusTarget.AddAction : FocusTarget.TitleButton;

        Announce(Loc["TmAgentPlan_Removed", (index + 1).ToString(CultureInfo.CurrentCulture), TitleText(step)]);
        await RaiseAsync(list, AgentPlanEdit.Remove(index, step));
    }

    private async Task UndoRemoveAsync()
    {
        if (_pendingUndo is not { } undo)
        {
            return;
        }

        // The host put the step back itself (or never applied the removal): inserting it again would give
        // the list two rows with one @key. The offer is stale, so it is retired without an edit.
        if (IndexOf(undo.Step.Id) >= 0)
        {
            _pendingUndo = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        var list = StepList.ToList();
        var position = Math.Clamp(undo.Index, 0, list.Count);
        list.Insert(position, undo.Step);

        _pendingUndo = null;
        _focusTarget = FocusTarget.TitleButton;
        _focusStepId = undo.Step.Id;
        Announce(Loc["TmAgentPlan_RemoveUndone", (position + 1).ToString(CultureInfo.CurrentCulture), TitleText(undo.Step)]);

        await RaiseAsync(list, AgentPlanEdit.Add(position, undo.Step));
    }

    // ── Reorder (the nested TmSortableList owns the gesture and its announcement) ──

    private async Task OnStepReorderedAsync(SortableReorderArgs args)
    {
        var steps = StepList;
        if (args.FromIndex < 0 || args.FromIndex >= steps.Count
            || args.ToIndex < 0 || args.ToIndex >= steps.Count
            || args.FromIndex == args.ToIndex)
        {
            return;
        }

        var list = steps.ToList();
        var moved = list[args.FromIndex];
        list.RemoveAt(args.FromIndex);
        list.Insert(args.ToIndex, moved);

        _pendingUndo = null;
        await RaiseAsync(list, AgentPlanEdit.Reorder(args.FromIndex, args.ToIndex, moved.Id));
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private async Task RaiseAsync(IReadOnlyList<AgentPlanStep> steps, AgentPlanEdit edit)
    {
        // Paint the local consequence (the closed editor, the undo strip) before the host's handler runs; a
        // handler that awaits will then re-render over a state that already matches the reported list.
        await InvokeAsync(StateHasChanged);

        if (OnPlanChanged is not null)
        {
            await OnPlanChanged(steps, edit);
        }
    }

    private async Task ApproveAsync() => await OnApprove.InvokeAsync();

    private async Task RunAsync() => await OnRun.InvokeAsync();

    private Task RetryAsync(AgentPlanStep step) => OnRetryFrom.InvokeAsync(step);

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
                if (!_disposed)
                {
                    _now = _clock.GetUtcNow();
                    StateHasChanged();
                }
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

    // ── Rendering helpers ────────────────────────────────────────────────────

    private int IndexOf(string stepId)
    {
        for (var index = 0; index < StepList.Count; index++)
        {
            if (string.Equals(StepList[index].Id, stepId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsEditing(AgentPlanStep step)
        => CanEdit && step.IsEditable && string.Equals(step.Id, _editingStepId, StringComparison.Ordinal);

    private bool IsStepEditable(AgentPlanStep step) => CanEdit && step.IsEditable;

    private static bool IsUntitled(AgentPlanStep step) => string.IsNullOrWhiteSpace(step.Title);

    /// <summary>
    /// The title a reader is shown. A step whose title is missing or whitespace still has to be nameable
    /// (and clickable), so it reads as the localized "Untitled step" instead of an empty label.
    /// </summary>
    private string TitleText(AgentPlanStep step) => IsUntitled(step) ? Loc["TmAgentPlan_UntitledStep"] : step.Title;

    /// <summary>The title element's classes; the placeholder modifier marks a stand-in rather than a name.</summary>
    private static string TitleTextClass(AgentPlanStep step)
        => IsUntitled(step)
            ? "tm-ai-agent-plan__title-text tm-ai-agent-plan__title-text--placeholder"
            : "tm-ai-agent-plan__title-text";

    /// <summary>The id of a step's title element, which describes the row's Retry action.</summary>
    private string TitleElementId(AgentPlanStep step) => _id + "-step-title-" + step.Id;

    /// <summary>
    /// The Retry pill's description: the step's title. While the row is being renamed there is no title
    /// element to point at, so the attribute is dropped rather than left dangling.
    /// </summary>
    private string? RetryDescriptionId(AgentPlanStep step) => IsEditing(step) ? null : TitleElementId(step);

    private string PositionText(int index) => (index + 1).ToString(CultureInfo.CurrentCulture);

    private string StatusSlug(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => "running",
        AgentStepStatus.Done => "done",
        AgentStepStatus.Failed => "failed",
        AgentStepStatus.Skipped => "skipped",
        _ => "pending",
    };

    private string StatusText(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => Loc["TmAgentPlan_StatusRunning"],
        AgentStepStatus.Done => Loc["TmAgentPlan_StatusDone"],
        AgentStepStatus.Failed => Loc["TmAgentPlan_StatusFailed"],
        AgentStepStatus.Skipped => Loc["TmAgentPlan_StatusSkipped"],
        _ => Loc["TmAgentPlan_StatusPending"],
    };

    private static string StatusIcon(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => IconNames.Play,
        AgentStepStatus.Done => IconNames.CheckCircle,
        AgentStepStatus.Failed => IconNames.XCircle,
        AgentStepStatus.Skipped => IconNames.SkipForward,
        _ => IconNames.Circle,
    };

    /// <summary>
    /// A clock-style duration: <c>0:42</c>, <c>1:24</c>, <c>1:02:07</c>. Invariant digits and separator on
    /// purpose — this is a duration, not a number of a unit, and the four shipped cultures all read it the
    /// same way while a localized digit set would make it unrecognizable next to a timestamp.
    /// </summary>
    /// <param name="value">The duration to format; a negative value reads as zero.</param>
    /// <returns>The duration as <c>m:ss</c> or <c>h:mm:ss</c>.</returns>
    internal static string FormatClock(TimeSpan value)
    {
        var span = value < TimeSpan.Zero ? TimeSpan.Zero : value;

        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture,
                $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
    }

    private void PruneTitleRefs(IReadOnlyList<AgentPlanStep> steps)
    {
        if (_titleRefs.Count == 0)
        {
            return;
        }

        var live = steps.Select(step => step.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _titleRefs.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _titleRefs.Remove(stale);
        }
    }

    /// <summary>
    /// Replaces the live region's text. Sticky (never cleared on a re-render, which could wipe an
    /// announcement before assistive technology read it); when two consecutive transitions would write the
    /// same sentence, one invisible marker character is toggled so the region still sees a mutation.
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

    // ── Focus ────────────────────────────────────────────────────────────────

    private async Task MoveFocusAsync(FocusTarget target, string? stepId)
    {
        ElementReference element;
        switch (target)
        {
            case FocusTarget.RenameInput:
                element = _renameInput;
                break;
            case FocusTarget.TitleButton:
                if (stepId is null || !_titleRefs.TryGetValue(stepId, out element) || element.Id is null)
                {
                    element = _addActionElement;
                }

                break;
            case FocusTarget.AddAction:
                element = _addActionElement;
                break;
            default:
                element = default;
                break;
        }

        if (element.Id is null)
        {
            // The row a hand-back targets may have left the DOM (the last row was removed) and the add
            // action is not rendered on a plan that may not be edited: the plan itself carries tabindex="-1",
            // so it takes the focus instead of the browser dropping it on <body>.
            if (target == FocusTarget.RenameInput)
            {
                return;
            }

            element = _rootElement;
            if (element.Id is null)
            {
                return;
            }
        }

        try
        {
            await element.FocusAsync(preventScroll: true);
        }
        catch (Microsoft.JSInterop.JSDisconnectedException)
        {
            // Blazor Server: the circuit dropped, so there is no focus to move.
        }
        catch (Microsoft.JSInterop.JSException)
        {
            // The element left the DOM between the render and the focus call.
        }
        catch (InvalidOperationException)
        {
            // No renderer, or the element reference is no longer attached.
        }
    }

    /// <summary>The element a finished action hands the focus to.</summary>
    private enum FocusTarget
    {
        /// <summary>Nothing to move.</summary>
        None,

        /// <summary>The rename editor of the row being edited.</summary>
        RenameInput,

        /// <summary>The title button of the row named by the pending focus id.</summary>
        TitleButton,

        /// <summary>The plan's add action (the plan emptied).</summary>
        AddAction,
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
}
