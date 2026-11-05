using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Several attempts at the SAME piece of work, side by side: one column per attempt with its state, its score, its own
/// summary, the body the host renders and the votes it collected — and a winner the reader picks and confirms.
/// <para>
/// The surface renders the attempts it is given and never mutates them and never invents a score: the best attempt is
/// the host's explicit pick (<see cref="BestAttemptId"/>) when it names one, otherwise
/// <see cref="ParallelAttemptRanking"/> resolves the highest score, so the column badge, the comparison table and a
/// test can never disagree.
/// </para>
/// <para>
/// Picking a winner is deliberately TWO steps. The native radios of one shared name are the choice — the arrow keys
/// walk them natively, with no key emulation — and a pick asks for confirmation before
/// <see cref="OnSelect"/> fires, so a stray arrow key can never replace a recorded result. The confirmation lives in a
/// named group, the pick is announced once through the surface's ONE polite live region, and the focus returns to the
/// radio that opened the question.
/// </para>
/// <para>
/// The columns are a horizontal scroll-snap track: a narrow container turns them into a carousel with no JavaScript and
/// no second layout engine. The comparison table below repeats every number as text, which is the surface a screen
/// reader or a phone reads.
/// </para>
/// </summary>
public partial class TmParallelAttempts : TmComponentBase
{
    private readonly string _id = $"tm-ai-parallel-{Guid.NewGuid():n}"[..27];
    private readonly HashSet<string> _expandedVotes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioHandle> _radios = new(StringComparer.Ordinal);

    private List<ParallelAttempt> _attempts = [];
    private string? _bestAttemptId;
    private string? _lastBestParameter;
    private string? _selectedId;
    private string? _lastSelectedParameter;
    private string? _pendingId;
    private string? _pendingFocusAttemptId;
    private int _radioVersion;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The attempts to compare, in the host's own order. Each one is a column and a row of the comparison table; the
    /// surface never reorders them, so the host's order stays the reader's order. Null or empty renders the localized
    /// empty state.
    /// </summary>
    [Parameter] public IReadOnlyList<ParallelAttempt>? Attempts { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Parallel attempts").
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// The host's explicit pick of the best attempt. When it names one of <see cref="Attempts"/> that attempt wears the
    /// "Best" mark whatever its score; when it names nothing (or something absent) the highest score decides. Null by
    /// default.
    /// </summary>
    [Parameter] public string? BestAttemptId { get; set; }

    /// <summary>
    /// The confirmed winner, two-way bound with <see cref="SelectedAttemptIdChanged"/> (<c>@bind-SelectedAttemptId</c>).
    /// Its radio is checked and its column carries the "Chosen" mark. A host that BINDS it delegates the decision: the
    /// reader's confirmation reports the change and the next render from the host decides. A host that only PINS it
    /// (a value without <see cref="SelectedAttemptIdChanged"/>) keeps control: the confirmed pick is shown until the
    /// host renders again, and then the pinned value is restored. A host that sets neither keeps the reader's pick.
    /// </summary>
    [Parameter] public string? SelectedAttemptId { get; set; }

    /// <summary>Fires with the newly confirmed winner (never a merely picked one).</summary>
    [Parameter] public EventCallback<string?> SelectedAttemptIdChanged { get; set; }

    /// <summary>
    /// Raised with the attempt id once the reader CONFIRMED it as the result. A host that only needs the value can bind
    /// <see cref="SelectedAttemptId"/> instead; this callback is the command channel (record the winner, start the next
    /// step).
    /// </summary>
    [Parameter] public EventCallback<string> OnSelect { get; set; }

    /// <summary>
    /// Whether a pick asks for confirmation before it becomes the winner. Default <c>true</c> — the safe default, so a
    /// stray arrow key inside the radio group cannot replace a recorded result. A host that wants a one-step choice
    /// passes <c>false</c> and the pick raises <see cref="OnSelect"/> immediately.
    /// </summary>
    [Parameter] public bool ConfirmSelection { get; set; } = true;

    /// <summary>
    /// Whether the comparison table is rendered. Default <c>true</c> — it is the text alternative of the columns (the
    /// same numbers as a table), so a host hides it only when it renders its own.
    /// </summary>
    [Parameter] public bool ShowComparison { get; set; } = true;

    /// <summary>
    /// The host's own body for one attempt — a diff, a document, a code block, plain prose. Rendered inside that
    /// attempt's column, so every body stays bound to its own attempt. Null renders no body.
    /// </summary>
    [Parameter] public RenderFragment<ParallelAttempt>? ContentTemplate { get; set; }

    /// <summary>
    /// The culture that formats the scores. Null (the default) means the current culture at render time. It never
    /// rescales a score: the host's scale is the host's.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The attempts as the surface paints them (the host's order, nulls dropped).</summary>
    public IReadOnlyList<ParallelAttempt> AttemptList => _attempts;

    /// <summary>
    /// The best attempt: the host's explicit pick when it names one, otherwise the highest score. Null when nothing
    /// can be ranked (an empty list, or no attempt scored).
    /// </summary>
    public ParallelAttempt? BestAttempt => ParallelAttemptRanking.Best(_attempts, _bestAttemptId);

    /// <summary>The confirmed winner, or null when nothing has been confirmed yet (or it is gone).</summary>
    public ParallelAttempt? SelectedAttempt => _selectedId is { } id
        ? _attempts.FirstOrDefault(attempt => string.Equals(attempt.Id, id, StringComparison.Ordinal))
        : null;

    /// <summary>The attempt a pick is waiting for confirmation on, or null when there is no open question.</summary>
    public ParallelAttempt? PendingAttempt => _pendingId is { } id && !string.Equals(id, _selectedId, StringComparison.Ordinal)
        ? _attempts.FirstOrDefault(attempt => string.Equals(attempt.Id, id, StringComparison.Ordinal))
        : null;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmParallelAttempts_Title"] : Title!;

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    private string RootClass => string.IsNullOrWhiteSpace(Class)
        ? "tm-ai-parallel-attempts"
        : "tm-ai-parallel-attempts " + Class;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private string GroupName => _id + "-winner";

    private string ConfirmQuestionId => _id + "-confirm";

    /// <summary>One radio group per surface: the name is unique, so two surfaces on a page cannot share a group.</summary>
    private string VotesId(ParallelAttempt attempt) => _id + "-votes-" + attempt.Id;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _lastBestParameter = BestAttemptId;
        _lastSelectedParameter = SelectedAttemptId;
        _bestAttemptId = BestAttemptId;
        _selectedId = SelectedAttemptId;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A host that changes a parameter wins; the reader's own pick survives an unrelated render.
        if (!string.Equals(BestAttemptId, _lastBestParameter, StringComparison.Ordinal))
        {
            _lastBestParameter = BestAttemptId;
            _bestAttemptId = BestAttemptId;
        }

        if (!string.Equals(SelectedAttemptId, _lastSelectedParameter, StringComparison.Ordinal))
        {
            _lastSelectedParameter = SelectedAttemptId;
            _selectedId = SelectedAttemptId;
            _pendingId = null;

            // The host moved the winner: re-mount the radios so the browser's own checked state follows the
            // attribute instead of keeping the radio the reader last clicked.
            _radioVersion++;
        }

        // A PINNED value the host never binds is authoritative: the host keeps control, so a confirmation it has not
        // reflected back is restored on its very next render. A host that names no winner (or binds the value) is not
        // touched here — the reader's confirmation stands until the host says otherwise.
        if (!SelectedAttemptIdChanged.HasDelegate
            && SelectedAttemptId is { } pinned
            && !string.Equals(_selectedId, pinned, StringComparison.Ordinal))
        {
            _selectedId = pinned;
            _radioVersion++;
        }

        _attempts = Attempts is null
            ? []
            : [.. Attempts.Where(attempt => attempt is not null)];

        Reconcile();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusAttemptId is { } pending)
        {
            _pendingFocusAttemptId = null;
            await FocusRadioAsync(pending);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Picks <paramref name="attemptId"/> as the winner — the same path a radio takes. With
    /// <see cref="ConfirmSelection"/> the pick only opens the confirmation; without it the pick IS the confirmation.
    /// </summary>
    /// <param name="attemptId">The attempt the reader picked. An id that names no attempt is ignored.</param>
    /// <returns>A task that completes when the pick has been applied (and confirmed, when confirmation is off).</returns>
    public async Task ChooseAsync(string attemptId)
    {
        if (Find(attemptId) is not { } attempt)
        {
            return;
        }

        // Picking the attempt that is ALREADY the result opens no question: there is nothing to confirm. (It can
        // happen — the arrow keys inside the radio group walk back onto the confirmed radio, which raises a change.)
        if (ConfirmSelection && string.Equals(attemptId, _selectedId, StringComparison.Ordinal))
        {
            _pendingId = null;
            StateHasChanged();
            return;
        }

        _pendingId = attemptId;

        if (!ConfirmSelection)
        {
            await ConfirmAsync();
            return;
        }

        Announce(Loc["TmParallelAttempts_PendingAnnouncement", AttemptLabel(attempt)]);
        StateHasChanged();
    }

    /// <summary>
    /// Confirms the open pick: it becomes the winner, <see cref="OnSelect"/> and
    /// <see cref="SelectedAttemptIdChanged"/> fire, the question disappears and the focus returns to the radio.
    /// </summary>
    /// <returns>A task that completes when the winner has been reported.</returns>
    public async Task ConfirmAsync()
    {
        if (PendingAttempt is not { } attempt)
        {
            return;
        }

        _selectedId = attempt.Id;
        _lastSelectedParameter = SelectedAttemptId;
        _pendingId = null;
        _pendingFocusAttemptId = attempt.Id;
        _lastBestParameter = BestAttemptId;

        Announce(Loc["TmParallelAttempts_ChosenAnnouncement", AttemptLabel(attempt)]);
        StateHasChanged();

        if (SelectedAttemptIdChanged.HasDelegate)
        {
            await SelectedAttemptIdChanged.InvokeAsync(attempt.Id);
        }

        if (OnSelect.HasDelegate)
        {
            await OnSelect.InvokeAsync(attempt.Id);
        }
    }

    /// <summary>
    /// Cancels the open pick: the previous winner stays the winner, the question disappears and the focus returns to
    /// the radio of the confirmed attempt (the first radio when nothing was confirmed yet).
    /// </summary>
    /// <returns>A task that completes when the pick has been dropped.</returns>
    public Task CancelAsync()
    {
        _pendingId = null;

        // A controlled `checked` attribute only sets the DEFAULT checkedness, so the radio the reader clicked would
        // keep showing the pick that was just dropped: re-mounting the inputs is what actually reverts them.
        _radioVersion++;
        _pendingFocusAttemptId = _selectedId ?? _attempts.FirstOrDefault()?.Id;

        Announce(Loc["TmParallelAttempts_CancelledAnnouncement"]);
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Opens or closes the vote reasons of one attempt.</summary>
    /// <param name="attemptId">The attempt whose votes are toggled.</param>
    /// <returns>A task that completes when the disclosure has been repainted.</returns>
    public Task ToggleVotesAsync(string attemptId)
    {
        if (Find(attemptId) is null)
        {
            return Task.CompletedTask;
        }

        if (!_expandedVotes.Add(attemptId))
        {
            _expandedVotes.Remove(attemptId);
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── View helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the local pick and the expanded vote panels inside the attempts the host still supplies: a pick that names
    /// an attempt which disappeared would keep a question nobody can answer.
    /// </summary>
    private void Reconcile()
    {
        if (_pendingId is { } pending && Find(pending) is null)
        {
            _pendingId = null;
        }

        if (_selectedId is { } selected && Find(selected) is null)
        {
            _selectedId = null;
        }

        if (_expandedVotes.Count > 0)
        {
            _expandedVotes.RemoveWhere(id => Find(id) is null);
        }

        if (_radios.Count > 0)
        {
            foreach (var id in _radios.Keys.Where(id => Find(id) is null).ToList())
            {
                _radios.Remove(id);
            }
        }
    }

    private ParallelAttempt? Find(string? attemptId)
        => string.IsNullOrEmpty(attemptId)
            ? null
            : _attempts.FirstOrDefault(attempt => string.Equals(attempt.Id, attemptId, StringComparison.Ordinal));

    private bool IsBest(ParallelAttempt attempt)
        => BestAttempt is { } best && string.Equals(best.Id, attempt.Id, StringComparison.Ordinal);

    private bool IsChosen(ParallelAttempt attempt)
        => string.Equals(_selectedId, attempt.Id, StringComparison.Ordinal);

    /// <summary>
    /// The radio's state: the open pick wins over the confirmed winner, so the control shows what the reader just chose
    /// while the question is open (and a cancelled pick falls back to the confirmed one).
    /// </summary>
    private bool IsChecked(ParallelAttempt attempt)
        => string.Equals(_pendingId ?? _selectedId, attempt.Id, StringComparison.Ordinal);

    private string ColumnClass(ParallelAttempt attempt)
    {
        var builder = new System.Text.StringBuilder("tm-ai-parallel-attempts__attempt");
        if (IsBest(attempt))
        {
            builder.Append(" tm-ai-parallel-attempts__attempt--best");
        }

        if (IsChosen(attempt))
        {
            builder.Append(" tm-ai-parallel-attempts__attempt--chosen");
        }

        return builder.ToString();
    }

    /// <summary>The attempt's own name: the host's label, or the localized numbered fallback.</summary>
    private string AttemptLabel(ParallelAttempt attempt)
    {
        if (attempt.HasLabel)
        {
            return attempt.Label;
        }

        var index = _attempts.FindIndex(candidate => string.Equals(candidate.Id, attempt.Id, StringComparison.Ordinal)) + 1;
        return Loc["TmParallelAttempts_AttemptFallback", index.ToString(CultureInfo.CurrentCulture)];
    }

    private string ScoreText(ParallelAttempt attempt)
        => attempt.HasScore ? FormatScore(attempt.Score) : Loc["TmParallelAttempts_NoScore"];

    private string FormatScore(double? score)
        => score is { } value ? value.ToString(FormatCulture) : string.Empty;

    private string VoterName(AttemptVote vote)
        => vote.DisplayName.Length > 0 ? vote.DisplayName : Loc["TmParallelAttempts_UnknownVoter"];

    private string OutcomeText(AttemptVoteOutcome outcome) => outcome switch
    {
        AttemptVoteOutcome.Approve => Loc["TmParallelAttempts_OutcomeApprove"],
        AttemptVoteOutcome.Reject => Loc["TmParallelAttempts_OutcomeReject"],
        _ => Loc["TmParallelAttempts_OutcomeAbstain"],
    };

    private string ChooseLabel(ParallelAttempt attempt)
        => Loc["TmParallelAttempts_ChooseAriaLabel", AttemptLabel(attempt)];

    /// <summary>
    /// The column's accessible name: its label first (label in name), then the state, the score and — when it is —
    /// the best pick, so a reader who lands on the group hears the whole comparison in one sentence.
    /// </summary>
    private string ColumnAriaLabel(ParallelAttempt attempt)
    {
        var parts = new List<string>
        {
            AttemptLabel(attempt),
            AgentStatusPresentation.Text(Loc, attempt.Status),
            attempt.HasScore
                ? Loc["TmParallelAttempts_ScoreSentence", FormatScore(attempt.Score)]
                : Loc["TmParallelAttempts_NoScoreSentence"],
        };

        if (IsBest(attempt))
        {
            parts.Add(Loc["TmParallelAttempts_BestMark"]);
        }

        return string.Join(". ", parts);
    }

    /// <summary>The radio's render key: one per attempt and per radio version, so a revert re-mounts it.</summary>
    private string RadioKey(ParallelAttempt attempt)
        => attempt.Id + "#" + _radioVersion.ToString(CultureInfo.InvariantCulture);

    private RadioHandle RadioHandleFor(string attemptId)
    {
        if (!_radios.TryGetValue(attemptId, out var handle))
        {
            handle = new RadioHandle();
            _radios[attemptId] = handle;
        }

        return handle;
    }

    private async Task FocusRadioAsync(string attemptId)
    {
        if (!_radios.TryGetValue(attemptId, out var handle) || handle.Ref.Id is null)
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
            // No JavaScript, or the element is already gone: the radio the reader chose is still the checked one, so a
            // screen reader continues from there.
        }
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

    /// <summary>One radio's element reference, kept alive across renders so the focus move can reach it.</summary>
    private sealed class RadioHandle
    {
        /// <summary>The radio input's element reference, assigned by the render.</summary>
        public ElementReference Ref { get; set; }
    }
}
