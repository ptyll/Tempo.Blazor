using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Models.Approvals;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Approvals;

/// <summary>
/// A request for a human decision: heading, description, a localized risk badge, a host-supplied
/// preview and key/value details, an optional countdown to the deadline, and Approve / Reject
/// actions with an optional note.
/// <para>
/// The card is a controlled component — the host owns <see cref="State"/> and hears every transition
/// through <see cref="StateChanged"/>. It leaves <see cref="ApprovalState.Pending"/> only after
/// <see cref="OnDecision"/> completes without throwing, so a failed save is reported as a
/// <c>TmAlert</c> and the request stays decidable. A decided card collapses into a single row that
/// names who decided and when; with <see cref="UndoWindow"/> it also offers Undo until that window
/// elapses.
/// </para>
/// <para>
/// Accessibility: one polite live region carries every announcement (the ticking countdown itself is
/// <c>aria-live="off"</c>), the risk is always spelled out next to its colour, focus follows the
/// collapse and returns to the primary action on undo, and the Ctrl/Cmd shortcuts only fire while
/// focus is inside the card — never from inside the note field, whose own keys are fenced with
/// <c>@onkeydown:stopPropagation</c>.
/// </para>
/// </summary>
public partial class TmApprovalCard : TmComponentBase, IDisposable
{
    private static readonly string ApproveShortcutKeys = "Control+Enter Meta+Enter";
    private static readonly string RejectShortcutKeys = "Control+Backspace Meta+Backspace";

    private readonly string _id = $"tm-approval-card-{Guid.NewGuid():N}";

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private ApprovalCountdownTicker? _ticker;
    private bool _disposed;

    private ApprovalState _state = ApprovalState.Pending;
    private ApprovalState _stateParameter = ApprovalState.Pending;
    private ApprovalState? _actionInFlight;
    private ApprovalState? _stateNotification;
    private string? _errorMessage;
    private string _announcement = string.Empty;
    private string _note = string.Empty;
    private string? _submittedNote;
    private bool _noteInvalid;
    private bool _expiryWarningAnnounced;
    private DateTimeOffset? _undoDeadline;
    private DateTimeOffset? _decidedAtLocal;
    private FocusTarget _focusTarget;

    private ElementReference _rootElement;
    private ElementReference _approveElement;
    private ElementReference _noteElement;
    private ElementReference _undoElement;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>Heading of the request. When omitted the group is named by
    /// <see cref="AriaLabel"/> (or a localized default) and no heading is rendered.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Free-text description of what is being approved.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Risk level shown as a localized badge and as the card's accent. Default is
    /// <see cref="ApprovalRisk.Low"/>.</summary>
    [Parameter] public ApprovalRisk Risk { get; set; } = ApprovalRisk.Low;

    /// <summary>Host-rendered preview of the thing being approved (a document excerpt, a diff, a
    /// diff of the pending change). Hidden in <see cref="Compact"/> mode.</summary>
    [Parameter] public RenderFragment? PreviewContent { get; set; }

    /// <summary>Key/value details of the request, rendered as a definition list. Hidden in
    /// <see cref="Compact"/> mode.</summary>
    [Parameter] public IReadOnlyList<KeyValuePair<string, string>>? Details { get; set; }

    /// <summary>Deadline of the request. While the card is pending the remaining time is shown as a
    /// countdown; once it elapses the state becomes <see cref="ApprovalState.Expired"/> and the
    /// actions are disabled.</summary>
    [Parameter] public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When true a rejection without a note is refused: the note field is marked
    /// <c>aria-invalid</c>, the reason is announced and the decision is not raised. Default is
    /// false.</summary>
    [Parameter] public bool RequireNoteOnReject { get; set; }

    /// <summary>Whether the optional note field is offered at all. Default is true; the field is
    /// always rendered when <see cref="RequireNoteOnReject"/> is set.</summary>
    [Parameter] public bool AllowNote { get; set; } = true;

    /// <summary>Current state of the request. Default is <see cref="ApprovalState.Pending"/>.</summary>
    [Parameter] public ApprovalState State { get; set; } = ApprovalState.Pending;

    /// <summary>Raised whenever the card transitions state — a decision, the automatic expiry and
    /// Undo. Use it with <c>@bind-State</c>.</summary>
    [Parameter] public EventCallback<ApprovalState> StateChanged { get; set; }

    /// <summary>Display name of the person who decided, shown in the collapsed row. When omitted
    /// the row says "Unknown".</summary>
    [Parameter] public string? DecidedBy { get; set; }

    /// <summary>Moment of the decision, shown in the collapsed row. When omitted the moment the
    /// decision was raised is used.</summary>
    [Parameter] public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Note to show in the collapsed row. When omitted the note typed by the decider is
    /// used, so a host that does not echo it still shows what was written.</summary>
    [Parameter] public string? DecisionNote { get; set; }

    /// <summary>Callback that persists the decision. The card stays
    /// <see cref="ApprovalState.Pending"/> while it runs (actions disabled, card
    /// <c>aria-busy</c>); when it throws, the error is shown in a <c>TmAlert</c> and the request
    /// stays decidable.</summary>
    [Parameter] public Func<ApprovalDecision, Task>? OnDecision { get; set; }

    /// <summary>How long after a decision the collapsed row offers Undo. When null (the default) or
    /// zero, no Undo is offered.</summary>
    [Parameter] public TimeSpan? UndoWindow { get; set; }

    /// <summary>Callback invoked when the user undoes a decision. It runs first and, when it
    /// succeeds, the card returns to <see cref="ApprovalState.Pending"/>; a throw keeps the
    /// decision and shows the localized error. Undo is offered only when this callback (or
    /// <see cref="Undone"/>) is wired.</summary>
    [Parameter] public Func<Task>? OnUndo { get; set; }

    /// <summary>Raised together with <see cref="OnUndo"/> for hosts that prefer an
    /// <see cref="EventCallback"/>.</summary>
    [Parameter] public EventCallback Undone { get; set; }

    /// <summary>Whether the Ctrl/Cmd+Enter (approve) and Ctrl/Cmd+Backspace (reject) shortcuts are
    /// active while focus is inside the card. Default is true; the card never reacts to those keys
    /// typed in the note field.</summary>
    [Parameter] public bool EnableShortcuts { get; set; } = true;

    /// <summary>Renders the condensed single-row variant: no description, preview or details, the
    /// heading, risk, countdown and actions on one line. Default is false.</summary>
    [Parameter] public bool Compact { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Accessible name of the group when <see cref="Title"/> is omitted. Defaults to a
    /// localized "Approval request".</summary>
    [Parameter] public string? AriaLabel { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>Whether the request still accepts a decision.</summary>
    public bool CanDecide => _state == ApprovalState.Pending && _actionInFlight is null;

    /// <summary>Latest announcement, mirrored into the card's single live region.</summary>
    public string Announcement => _announcement;

    private bool IsBusy => _actionInFlight is not null;
    private bool IsApproving => _actionInFlight == ApprovalState.Approved;
    private bool IsRejecting => _actionInFlight == ApprovalState.Rejected;
    private bool IsCollapsed => _state != ApprovalState.Pending;
    private bool IsExpired => _state == ApprovalState.Expired;
    private bool ShowNote => AllowNote || RequireNoteOnReject;
    private bool HasDetails => Details is { Count: > 0 };
    private bool NoteInvalid => _noteInvalid;

    private string? ErrorMessage => _errorMessage;

    /// <summary>The note shown in the collapsed row: the host's echo when it supplies one, and
    /// otherwise the note the decider actually typed.</summary>
    private string? SummaryNote => string.IsNullOrWhiteSpace(DecisionNote)
        ? (string.IsNullOrWhiteSpace(_submittedNote) ? null : _submittedNote)
        : DecisionNote;

    private string StateName => _state.ToString().ToLowerInvariant();

    private string StateIcon => _state switch
    {
        ApprovalState.Approved => IconNames.CheckCircle,
        ApprovalState.Rejected => IconNames.XCircle,
        ApprovalState.Expired => IconNames.Clock,
        ApprovalState.Cancelled => IconNames.XCircle,
        _ => IconNames.Clock,
    };

    private string RootClass => string.Concat(
        "tm-approval-card",
        $" tm-approval-card--risk-{Risk.ToString().ToLowerInvariant()}",
        $" tm-approval-card--{StateName}",
        Compact ? " tm-approval-card--compact" : string.Empty,
        IsCollapsed ? " tm-approval-card--collapsed" : string.Empty,
        IsBusy ? " tm-approval-card--busy" : string.Empty,
        string.IsNullOrWhiteSpace(Class) ? string.Empty : $" {Class}");

    private string GroupLabel => AriaLabel
        ?? (string.IsNullOrWhiteSpace(Title) ? Loc["TmApprovalCard_AriaLabel"] : Title!);

    private string RiskLabel => Risk switch
    {
        ApprovalRisk.Medium => Loc["TmApprovalCard_RiskMedium"],
        ApprovalRisk.High => Loc["TmApprovalCard_RiskHigh"],
        ApprovalRisk.Critical => Loc["TmApprovalCard_RiskCritical"],
        _ => Loc["TmApprovalCard_RiskLow"],
    };

    private string RiskIcon => Risk switch
    {
        ApprovalRisk.Medium => IconNames.AlertCircle,
        ApprovalRisk.High => IconNames.AlertTriangle,
        ApprovalRisk.Critical => IconNames.Shield,
        _ => IconNames.CheckCircle,
    };

    private BadgeVariant RiskBadgeVariant => Risk switch
    {
        ApprovalRisk.Medium => BadgeVariant.Warning,
        ApprovalRisk.High => BadgeVariant.Danger,
        ApprovalRisk.Critical => BadgeVariant.Danger,
        _ => BadgeVariant.Success,
    };

    private string? ApproveShortcuts => EnableShortcuts ? ApproveShortcutKeys : null;
    private string? RejectShortcuts => EnableShortcuts ? RejectShortcutKeys : null;

    private string NoteId => $"{_id}-note";
    private string NoteErrorId => $"{_id}-note-error";
    private string ErrorId => $"{_id}-error";

    private string? ExpiresAtIso => ExpiresAt?.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Time left until <see cref="ExpiresAt"/>, never negative.</summary>
    private TimeSpan Remaining
    {
        get
        {
            if (ExpiresAt is not { } expires)
            {
                return TimeSpan.Zero;
            }

            var remaining = expires - _clock.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>The countdown as <c>mm:ss</c>, or <c>h:mm:ss</c> once an hour or more remains.</summary>
    private string CountdownText
    {
        get
        {
            var remaining = Remaining;
            return remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
                : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
        }
    }

    private string Decider => string.IsNullOrWhiteSpace(DecidedBy)
        ? Loc["TmApprovalCard_UnknownDecider"]
        : DecidedBy!;

    private string DecidedAtText
        => (DecidedAt ?? _decidedAtLocal ?? _clock.GetUtcNow())
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);

    private string SummaryText => _state switch
    {
        ApprovalState.Approved => string.Format(
            CultureInfo.CurrentCulture, Loc["TmApprovalCard_ApprovedSummary"], Decider, DecidedAtText),
        ApprovalState.Rejected => string.Format(
            CultureInfo.CurrentCulture, Loc["TmApprovalCard_RejectedSummary"], Decider, DecidedAtText),
        ApprovalState.Cancelled => Loc["TmApprovalCard_CancelledSummary"],
        ApprovalState.Expired => Loc["TmApprovalCard_Expired"],
        _ => string.Empty,
    };

    private bool ShowUndo => IsCollapsed
        && _undoDeadline is { } deadline
        && deadline > _clock.GetUtcNow()
        && (OnUndo is not null || Undone.HasDelegate);

    private string UndoLabel => string.Format(
        CultureInfo.CurrentCulture,
        Loc["TmApprovalCard_UndoLabel"],
        string.IsNullOrWhiteSpace(Title) ? Loc["TmApprovalCard_AriaLabel"] : Title);

    private bool NeedsTicking => _actionInFlight is null
        && ((_state == ApprovalState.Pending && ExpiresAt is not null)
            || (_undoDeadline is { } deadline && deadline > _clock.GetUtcNow()));

    /// <summary>The note as typed. Clearing the flag on the first non-blank keystroke is what makes
    /// <c>aria-invalid</c> describe the CURRENT draft rather than the last refusal.</summary>
    private string Note
    {
        get => _note;
        set
        {
            _note = value ?? string.Empty;
            if (_noteInvalid && !string.IsNullOrWhiteSpace(_note))
            {
                _noteInvalid = false;
            }
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _clock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        _ticker = new ApprovalCountdownTicker(_clock, OnTickAsync);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (State != _stateParameter)
        {
            _stateParameter = State;
            _state = State;
            if (_state == ApprovalState.Pending)
            {
                ResetDecisionArtifacts();
            }
        }

        EvaluateClockTransitions();
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

        if (_stateNotification is { } notification)
        {
            _stateNotification = null;
            await RaiseStateChangedAsync(notification);
        }

        if (_focusTarget != FocusTarget.None)
        {
            var target = _focusTarget;
            _focusTarget = FocusTarget.None;
            await FocusAsync(target);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Approves the request: runs <see cref="OnDecision"/> and, on success, leaves the
    /// pending state. A no-op when the request is not decidable.</summary>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public Task ApproveAsync() => DecideAsync(ApprovalState.Approved);

    /// <summary>Rejects the request. When <see cref="RequireNoteOnReject"/> is set and the note is
    /// blank, the request stays pending and the note field is marked invalid.</summary>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public Task RejectAsync() => DecideAsync(ApprovalState.Rejected);

    /// <summary>Withdraws the last decision while the undo window is open: <see cref="OnUndo"/> /
    /// <see cref="Undone"/> runs and, when it succeeds, the card returns to
    /// <see cref="ApprovalState.Pending"/> and focus returns to the primary action. A failing undo
    /// keeps the decision (the error is shown in a <c>TmAlert</c>); a no-op once the window has
    /// elapsed.</summary>
    /// <returns>A task that completes when the card is pending again.</returns>
    public async Task UndoAsync()
    {
        if (!ShowUndo)
        {
            return;
        }

        try
        {
            if (OnUndo is not null)
            {
                await OnUndo();
            }

            if (Undone.HasDelegate)
            {
                await Undone.InvokeAsync();
            }
        }
        catch (Exception)
        {
            _errorMessage = Loc["TmApprovalCard_Error"];
            _announcement = _errorMessage;
            _focusTarget = FocusTarget.Root;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _undoDeadline = null;
        _state = ApprovalState.Pending;
        ResetDecisionArtifacts();
        _announcement = Loc["TmApprovalCard_UndoAnnouncement"];
        _focusTarget = FocusTarget.Approve;
        await InvokeAsync(StateHasChanged);
        await RaiseStateChangedAsync(ApprovalState.Pending);
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    private async Task DecideAsync(ApprovalState state)
    {
        if (!CanDecide)
        {
            return;
        }

        var note = ShowNote ? _note.Trim() : string.Empty;

        if (state == ApprovalState.Rejected && RequireNoteOnReject && note.Length == 0)
        {
            _noteInvalid = true;
            _errorMessage = null;
            _announcement = Loc["TmApprovalCard_NoteRequired"];
            _focusTarget = FocusTarget.Note;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _noteInvalid = false;
        _errorMessage = null;
        _actionInFlight = state;
        await InvokeAsync(StateHasChanged);

        var decision = new ApprovalDecision(
            state,
            note.Length == 0 ? null : note,
            DecidedBy,
            DecidedAt ?? _clock.GetUtcNow());

        try
        {
            if (OnDecision is not null)
            {
                await OnDecision(decision);
            }
        }
        catch (Exception)
        {
            _actionInFlight = null;

            // The deadline may have passed while the save was running — EvaluateClockTransitions
            // skipped it because the decision was in flight. Honour it now: a throw must never leave
            // a request that ran out of time decidable.
            EvaluateClockTransitions();

            if (_state == ApprovalState.Pending)
            {
                // Only then is there something left to retry, so only then is the save error the
                // message. An expired request collapses into its result row, and "try again" over a
                // deadline that has already passed would be a lie — the expiry is what the reader
                // needs to hear.
                _errorMessage = Loc["TmApprovalCard_Error"];
                _announcement = _errorMessage;
            }

            await InvokeAsync(StateHasChanged);
            return;
        }

        _actionInFlight = null;
        _submittedNote = note.Length == 0 ? null : note;
        _decidedAtLocal = decision.DecidedAt;
        _state = state;
        _undoDeadline = UndoWindow is { } window && window > TimeSpan.Zero
            && (OnUndo is not null || Undone.HasDelegate)
                ? _clock.GetUtcNow() + window
                : null;
        _announcement = state == ApprovalState.Approved
            ? Loc["TmApprovalCard_ApproveAnnouncement"]
            : Loc["TmApprovalCard_RejectAnnouncement"];
        _focusTarget = _undoDeadline is not null ? FocusTarget.Undo : FocusTarget.Root;

        await InvokeAsync(StateHasChanged);
        await RaiseStateChangedAsync(state);
    }

    private async Task HandleCardKeyDown(KeyboardEventArgs args)
    {
        if (!EnableShortcuts || !CanDecide || args.Repeat || !(args.CtrlKey || args.MetaKey))
        {
            return;
        }

        if (args.Key == "Enter")
        {
            await ApproveAsync();
        }
        else if (args.Key == "Backspace")
        {
            await RejectAsync();
        }
    }

    /// <summary>
    /// The note field fences its own keys — a shortcut must never decide from inside a text field.
    /// The barrier is the <c>@onkeydown:stopPropagation</c> attribute in the markup; this handler
    /// gives the fence a real code path (and Escape drops the draft note), so the barrier is not an
    /// attribute nobody can test.
    /// </summary>
    private void HandleNoteKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && _note.Length > 0)
        {
            Note = string.Empty;
            _noteInvalid = false;
        }
    }

    // ── Clock ────────────────────────────────────────────────────────────────

    private void EvaluateClockTransitions()
    {
        // A decision that is being persisted owns the state. Re-evaluating a deadline that passes
        // while OnDecision runs would flip the card to Expired, queue an Expired notification and
        // then overwrite both with the real outcome — the host would hear "Expired" and "Approved"
        // for the same request. A FAILED decision re-runs this method itself (see DecideAsync), so
        // skipping the evaluation here never leaves a request decidable past its deadline.
        if (_actionInFlight is not null)
        {
            return;
        }

        var now = _clock.GetUtcNow();

        if (_state == ApprovalState.Pending && ExpiresAt is { } expires)
        {
            if (expires <= now)
            {
                _state = ApprovalState.Expired;
                _expiryWarningAnnounced = false;
                _announcement = Loc["TmApprovalCard_ExpiredAnnouncement"];
                _stateNotification = ApprovalState.Expired;
            }
            else if (!_expiryWarningAnnounced && expires - now <= TimeSpan.FromMinutes(1))
            {
                _expiryWarningAnnounced = true;
                _announcement = Loc["TmApprovalCard_ExpiringSoon"];
            }
        }

        if (_undoDeadline is { } undo && undo <= now)
        {
            _undoDeadline = null;
        }
    }

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

                EvaluateClockTransitions();
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

    // ── Focus ────────────────────────────────────────────────────────────────

    private async Task FocusAsync(FocusTarget target)
    {
        var element = target switch
        {
            FocusTarget.Approve => _approveElement,
            FocusTarget.Note => _noteElement,
            FocusTarget.Undo => _undoElement,
            _ => _rootElement,
        };

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
            // The element left the DOM between the render and the focus call (a host that
            // navigated or re-keyed the card in its StateChanged handler) — losing the focus move
            // is better than tearing the circuit down.
        }
        catch (InvalidOperationException)
        {
            // No renderer / element reference is no longer attached.
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ResetDecisionArtifacts()
    {
        _submittedNote = null;
        _decidedAtLocal = null;
        _undoDeadline = null;
        _expiryWarningAnnounced = false;
        _errorMessage = null;
        _noteInvalid = false;
    }

    private Task RaiseStateChangedAsync(ApprovalState state)
    {
        if (!StateChanged.HasDelegate)
        {
            return Task.CompletedTask;
        }

        return StateChanged.InvokeAsync(state);
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

    private enum FocusTarget
    {
        None,
        Root,
        Approve,
        Note,
        Undo,
    }
}
