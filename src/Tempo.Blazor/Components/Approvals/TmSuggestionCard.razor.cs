using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Approvals;

/// <summary>
/// One machine suggestion a human accepts or dismisses: the suggested text, a localized confidence
/// label (with an optional percentage), the rationale behind a native disclosure and, when the host
/// offers more than one wording, a <c>radiogroup</c> of alternatives.
/// <para>
/// Accept and Dismiss are asynchronous callbacks. The card leaves the pending state only when they
/// complete without throwing, so a failed save never looks like a decision; with <see cref="UndoWindow"/>
/// the decided row offers Undo until that window elapses (a <see cref="TimeProvider"/> clock, so a host
/// can drive it in tests). Exactly one polite live region announces every decision and focus follows
/// the collapse — to Undo when it is offered, back to the primary action on undo.
/// </para>
/// </summary>
public partial class TmSuggestionCard : TmComponentBase, IDisposable
{
    private readonly string _id = $"tm-suggestion-card-{Guid.NewGuid():N}";

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private ApprovalCountdownTicker? _ticker;
    private bool _disposed;

    private SuggestionState _state = SuggestionState.Pending;
    private string? _selectedAlternative;
    private string? _selectedAlternativeParameter;
    private string? _acceptedText;
    private string? _errorMessage;
    private string _announcement = string.Empty;
    private bool _busy;
    private DateTimeOffset? _undoDeadline;
    private FocusTarget _focusTarget;

    private ElementReference _rootElement;
    private ElementReference _acceptElement;
    private ElementReference _undoElement;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>The suggested text — what the machine proposes and what Accept applies when no
    /// alternative is selected.</summary>
    [Parameter, EditorRequired]
    public string Text { get; set; } = string.Empty;

    /// <summary>Optional heading naming the suggestion. When omitted the group is named by
    /// <see cref="AriaLabel"/> (or a localized default).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>How sure the machine is, from <c>0</c> to <c>1</c>. The card renders it as a
    /// localized band (<c>High</c> / <c>Medium</c> / <c>Low confidence</c>) and, with
    /// <see cref="ShowConfidencePercent"/>, as a percentage. When <c>null</c> no confidence is shown.
    /// </summary>
    [Parameter] public double? Confidence { get; set; }

    /// <summary>Whether the numeric percentage is shown next to the localized confidence label.
    /// Default is false — the band alone is the message.</summary>
    [Parameter] public bool ShowConfidencePercent { get; set; }

    /// <summary>The reasoning behind the suggestion, shown behind a native disclosure. When null or
    /// blank the disclosure is not rendered at all.</summary>
    [Parameter] public string? Rationale { get; set; }

    /// <summary>Whether the rationale disclosure starts expanded. Default is false.</summary>
    [Parameter] public bool RationaleDefaultExpanded { get; set; }

    /// <summary>Other wordings the machine offers. When supplied they are presented as a
    /// <c>radiogroup</c>; the selected one (not <see cref="Text"/>) becomes the accepted text.</summary>
    [Parameter] public IReadOnlyList<string>? Alternatives { get; set; }

    /// <summary>The selected alternative (two-way). <c>null</c> means none is selected, so Accept
    /// applies <see cref="Text"/>. Pair it with <c>@bind-SelectedAlternative</c>.</summary>
    [Parameter] public string? SelectedAlternative { get; set; }

    /// <summary>Raised when the user selects an alternative.</summary>
    [Parameter] public EventCallback<string?> SelectedAlternativeChanged { get; set; }

    /// <summary>Callback that persists an acceptance. It receives the accepted text — the selected
    /// alternative, or <see cref="Text"/> when none is selected. The card stays pending while it runs
    /// (actions disabled, card <c>aria-busy</c>) and only collapses when it completes without
    /// throwing; a throw shows the localized error and keeps the suggestion decidable.</summary>
    [Parameter] public Func<string, Task>? OnAccept { get; set; }

    /// <summary>Raised together with <see cref="OnAccept"/> for hosts that prefer an
    /// <see cref="EventCallback"/>.</summary>
    [Parameter] public EventCallback<string> Accepted { get; set; }

    /// <summary>Callback that persists a dismissal. Behaves like <see cref="OnAccept"/>; a throw keeps
    /// the suggestion pending.</summary>
    [Parameter] public Func<Task>? OnDismiss { get; set; }

    /// <summary>Raised together with <see cref="OnDismiss"/>.</summary>
    [Parameter] public EventCallback Dismissed { get; set; }

    /// <summary>How long after an acceptance the decided row offers Undo. When null (the default) or
    /// zero, no Undo is offered.</summary>
    [Parameter] public TimeSpan? UndoWindow { get; set; }

    /// <summary>Callback invoked when the user undoes an acceptance. It runs first and, when it
    /// succeeds, the card returns to pending; a throw keeps the decision and shows the localized
    /// error. Undo is offered only when this callback (or <see cref="Undone"/>) is wired.</summary>
    [Parameter] public Func<Task>? OnUndo { get; set; }

    /// <summary>Raised together with <see cref="OnUndo"/>.</summary>
    [Parameter] public EventCallback Undone { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Accessible name of the card when <see cref="Title"/> is omitted. Defaults to a
    /// localized "Suggestion".</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Unmatched attributes forwarded to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>Whether the card is waiting for an asynchronous callback to finish.</summary>
    public bool IsBusy => _busy;

    /// <summary>Whether the suggestion is still decidable.</summary>
    public bool CanDecide => _state == SuggestionState.Pending && !_busy;

    /// <summary>The alternative the user picked, or <c>null</c> when none is selected.</summary>
    public string? CurrentAlternative => _selectedAlternative;

    /// <summary>Latest announcement, mirrored into the card's single live region.</summary>
    public string Announcement => _announcement;

    private bool IsCollapsed => _state != SuggestionState.Pending;

    private bool HasConfidence => Confidence is not null;

    private bool HasRationale => !string.IsNullOrWhiteSpace(Rationale);

    private bool HasAlternatives => Alternatives is { Count: > 0 };

    private string? ErrorMessage => _errorMessage;

    private string StateName => _state switch
    {
        SuggestionState.Accepted => "accepted",
        SuggestionState.Dismissed => "dismissed",
        _ => "pending",
    };

    private string ConfidenceName => ConfidenceBand switch
    {
        ConfidenceLevel.High => "high",
        ConfidenceLevel.Medium => "medium",
        ConfidenceLevel.Low => "low",
        _ => "none",
    };

    private ConfidenceLevel? ConfidenceBand => Confidence switch
    {
        null => null,
        >= 0.8 => ConfidenceLevel.High,
        >= 0.5 => ConfidenceLevel.Medium,
        _ => ConfidenceLevel.Low,
    };

    private string ConfidenceLabel => ConfidenceBand switch
    {
        ConfidenceLevel.High => Loc["TmSuggestionCard_ConfidenceHigh"],
        ConfidenceLevel.Medium => Loc["TmSuggestionCard_ConfidenceMedium"],
        ConfidenceLevel.Low => Loc["TmSuggestionCard_ConfidenceLow"],
        _ => Loc["TmSuggestionCard_Confidence"],
    };

    private string ConfidenceIcon => ConfidenceBand switch
    {
        ConfidenceLevel.High => IconNames.CheckCircle,
        ConfidenceLevel.Medium => IconNames.Info,
        _ => IconNames.AlertCircle,
    };

    private string ConfidencePercentText
    {
        get
        {
            var percent = (int)Math.Round(Math.Clamp(Confidence ?? 0, 0, 1) * 100, MidpointRounding.AwayFromZero);
            return string.Format(
                CultureInfo.CurrentCulture, Loc["TmSuggestionCard_ConfidencePercent"], percent);
        }
    }

    private string StateIcon => _state switch
    {
        SuggestionState.Accepted => IconNames.CheckCircle,
        SuggestionState.Dismissed => IconNames.XCircle,
        // Only a DECIDED card draws the result glyph, so a pending one must not fall back to the
        // accepted icon (it would read as a check next to an undecided suggestion).
        _ => string.Empty,
    };

    private string SummaryText => _state switch
    {
        SuggestionState.Accepted => string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmSuggestionCard_AcceptedSummary"],
            _acceptedText ?? Text),
        SuggestionState.Dismissed => Loc["TmSuggestionCard_DismissedSummary"],
        _ => string.Empty,
    };

    private bool ShowUndo => _state == SuggestionState.Accepted
        && _undoDeadline is { } deadline
        && deadline > _clock.GetUtcNow()
        && (OnUndo is not null || Undone.HasDelegate);

    // No count and no format arguments: a translation containing a literal brace must render as-is
    // instead of throwing FormatException out of the render.
    private string UndoLabel => Loc["TmSuggestionCard_UndoLabel"];

    private string EffectiveAriaLabel => AriaLabel ?? Loc["TmSuggestionCard_AriaLabel"];

    private string AlternativeGroupName => $"{_id}-alternatives";

    private string RootClass => string.Concat(
        "tm-suggestion-card",
        HasConfidence ? $" tm-suggestion-card--confidence-{ConfidenceName}" : string.Empty,
        IsCollapsed ? " tm-suggestion-card--collapsed" : string.Empty,
        _busy ? " tm-suggestion-card--busy" : string.Empty,
        string.IsNullOrWhiteSpace(Class) ? string.Empty : $" {Class}");

    private bool NeedsTicking => _undoDeadline is { } deadline && deadline > _clock.GetUtcNow();

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
        if (!string.Equals(SelectedAlternative, _selectedAlternativeParameter, StringComparison.Ordinal))
        {
            _selectedAlternativeParameter = SelectedAlternative;
            _selectedAlternative = SelectedAlternative;
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

        if (_focusTarget != FocusTarget.None)
        {
            var target = _focusTarget;
            _focusTarget = FocusTarget.None;
            await FocusAsync(target);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Accepts the suggestion: runs <see cref="OnAccept"/> (and <see cref="Accepted"/>) with
    /// the selected alternative or <see cref="Text"/>, and collapses the card only when they succeed.
    /// A no-op while the suggestion is not decidable.</summary>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public async Task AcceptAsync()
    {
        if (!CanDecide)
        {
            return;
        }

        var text = _selectedAlternative ?? Text;
        _busy = true;
        _errorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            if (OnAccept is not null)
            {
                await OnAccept(text);
            }

            if (Accepted.HasDelegate)
            {
                await Accepted.InvokeAsync(text);
            }
        }
        catch (Exception)
        {
            _busy = false;
            _errorMessage = Loc["TmSuggestionCard_Error"];
            _announcement = _errorMessage;
            _focusTarget = FocusTarget.Root;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _busy = false;
        _acceptedText = text;
        _state = SuggestionState.Accepted;
        _undoDeadline = UndoWindow is { } window && window > TimeSpan.Zero
            && (OnUndo is not null || Undone.HasDelegate)
                ? _clock.GetUtcNow() + window
                : null;
        _announcement = Loc["TmSuggestionCard_AcceptAnnouncement"];
        _focusTarget = _undoDeadline is not null ? FocusTarget.Undo : FocusTarget.Root;

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Dismisses the suggestion: runs <see cref="OnDismiss"/> (and <see cref="Dismissed"/>) and
    /// collapses the card only when they succeed. A no-op while the suggestion is not decidable.</summary>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public async Task DismissAsync()
    {
        if (!CanDecide)
        {
            return;
        }

        _busy = true;
        _errorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            if (OnDismiss is not null)
            {
                await OnDismiss();
            }

            if (Dismissed.HasDelegate)
            {
                await Dismissed.InvokeAsync();
            }
        }
        catch (Exception)
        {
            _busy = false;
            _errorMessage = Loc["TmSuggestionCard_Error"];
            _announcement = _errorMessage;
            _focusTarget = FocusTarget.Root;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _busy = false;
        _state = SuggestionState.Dismissed;
        _undoDeadline = null;
        _announcement = Loc["TmSuggestionCard_DismissAnnouncement"];
        _focusTarget = FocusTarget.Root;

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Withdraws an acceptance while the undo window is open: <see cref="OnUndo"/> /
    /// <see cref="Undone"/> runs and, when it succeeds, the card returns to pending and focus returns
    /// to the primary action. A failing undo keeps the decision; a no-op once the window has elapsed.</summary>
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
            _errorMessage = Loc["TmSuggestionCard_Error"];
            _announcement = _errorMessage;
            _focusTarget = FocusTarget.Undo;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _undoDeadline = null;
        _state = SuggestionState.Pending;
        _acceptedText = null;
        _errorMessage = null;
        _announcement = Loc["TmSuggestionCard_UndoAnnouncement"];
        _focusTarget = FocusTarget.Accept;

        await InvokeAsync(StateHasChanged);
    }

    // ── Alternative selection ────────────────────────────────────────────────

    private async Task SelectAlternative(string alternative)
    {
        if (!CanDecide || IsSelected(alternative))
        {
            return;
        }

        _selectedAlternative = alternative;
        _selectedAlternativeParameter = alternative;

        if (SelectedAlternativeChanged.HasDelegate)
        {
            await SelectedAlternativeChanged.InvokeAsync(alternative);
        }
    }

    private bool IsSelected(string alternative)
        => string.Equals(_selectedAlternative, alternative, StringComparison.Ordinal);

    // ── Clock ────────────────────────────────────────────────────────────────

    private void EvaluateClockTransitions()
    {
        if (_undoDeadline is { } undo && undo <= _clock.GetUtcNow())
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
            FocusTarget.Accept => _acceptElement,
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
            // The element left the DOM between the render and the focus call.
        }
        catch (InvalidOperationException)
        {
            // No renderer / the element reference is no longer attached.
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

    // ── Nested types ─────────────────────────────────────────────────────────

    private enum SuggestionState
    {
        Pending,
        Accepted,
        Dismissed,
    }

    private enum ConfidenceLevel
    {
        High,
        Medium,
        Low,
    }

    private enum FocusTarget
    {
        None,
        Root,
        Accept,
        Undo,
    }
}
