using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models.Diffs;
using Tempo.Blazor.Diff;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Approvals;

/// <summary>
/// An inline comparison of two texts, built on <see cref="TextDiff"/>: every change is rendered as a
/// real <c>&lt;ins&gt;</c> / <c>&lt;del&gt;</c> pair preceded by a visually hidden label, so the
/// difference is carried by semantics and text rather than by colour alone.
/// <para>
/// <see cref="InlineDiffMode.Display"/> (the default) presents the changes; <see cref="InlineDiffMode.Review"/>
/// adds one decision per change — a marker button that opens a non-modal popover with Accept / Reject
/// (and the <c>A</c> / <c>R</c> shortcuts inside it), bulk Accept all / Reject all, one level of Undo
/// and a two-way <see cref="AcceptedHunkIds"/>. The component is a controlled component over that
/// set: the host owns it, hears every change through <see cref="AcceptedHunkIdsChanged"/> and receives
/// the resulting wording through <see cref="ResultChanged"/> — always the exact
/// <see cref="TextDiffResult.Apply"/> output, so an accepted subset never produces a text the engine
/// did not describe. <see cref="OnDecision"/> runs before a decision is applied; a throw leaves the
/// change undecided and surfaces the localized error, so a failed save never looks like an accepted
/// change.
/// </para>
/// <para>
/// Accessibility: the changes are element semantics plus a hidden label, the marker is a labelled
/// button (<c>aria-haspopup</c> / <c>aria-expanded</c> / <c>aria-controls</c>), opening the popover
/// moves focus into it and Escape closes it again with focus restored to the marker, exactly one
/// polite live region announces every result, and the single-key shortcuts can be turned off and never
/// fire from a repeat or a modified key press.
/// </para>
/// </summary>
public partial class TmInlineDiff : TmComponentBase
{
    private static readonly string AcceptShortcutKeys = "A";
    private static readonly string RejectShortcutKeys = "R";

    private readonly string _id = $"tm-inline-diff-{Guid.NewGuid():N}";

    private readonly Dictionary<string, ElementReference> _markerRefs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _accepted = new(StringComparer.Ordinal);
    private readonly HashSet<string> _decided = new(StringComparer.Ordinal);

    private TextDiffResult _result = TextDiff.Words(string.Empty, string.Empty);
    private List<HunkView> _view = [];
    private string _resultSignature = string.Empty;

    private IReadOnlyList<string>? _acceptedParameter;
    private IReadOnlyList<string>? _lastRaised;

    private DecisionSnapshot? _undo;
    private string? _openHunkId;
    private string? _restoreMarkerHunkId;
    private string? _errorMessage;
    private bool _deciding;
    private string _announcement = string.Empty;
    private FocusTarget _focusTarget;

    private ElementReference _rootElement;
    private ElementReference _popoverAcceptElement;
    private ElementReference _undoElement;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>The original text; <c>null</c> is read as empty.</summary>
    [Parameter] public string? OldText { get; set; }

    /// <summary>The proposed text; <c>null</c> is read as empty.</summary>
    [Parameter] public string? NewText { get; set; }

    /// <summary>Comparison granularity. <see cref="TextDiffGranularity.Words"/> (the default) gives one
    /// decision per changed word, <see cref="TextDiffGranularity.Lines"/> one per changed line.</summary>
    [Parameter] public TextDiffGranularity Granularity { get; set; } = TextDiffGranularity.Words;

    /// <summary>How the comparison is presented. Default is <see cref="InlineDiffMode.Display"/>.</summary>
    [Parameter] public InlineDiffMode Mode { get; set; } = InlineDiffMode.Display;

    /// <summary>When true, every decision surface is removed even in
    /// <see cref="InlineDiffMode.Review"/> — the diff degrades to the read-only presentation. Default
    /// is false; the effective switch is <see cref="IsReview"/>.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Ids of the changes the host has accepted. The rest are rejected, so an empty or
    /// <c>null</c> set means "nothing accepted yet" and <see cref="ResultText"/> is the old text. Use it
    /// with <c>@bind-AcceptedHunkIds</c> to keep the decision on the host.</summary>
    [Parameter] public IReadOnlyList<string>? AcceptedHunkIds { get; set; }

    /// <summary>Raised whenever the accepted set changes through a decision, a bulk action or Undo.</summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> AcceptedHunkIdsChanged { get; set; }

    /// <summary>Raised together with <see cref="AcceptedHunkIdsChanged"/> with the resulting text —
    /// the exact <see cref="TextDiffResult.Apply"/> output for the new accepted set.</summary>
    [Parameter] public EventCallback<string> ResultChanged { get; set; }

    /// <summary>Callback invoked for every change a decision is about to apply. A throw leaves the
    /// change undecided, shows the localized error and announces it, so a failed save never looks like
    /// an accepted change. The decision carries the resulting text, so a host can persist both in one
    /// round trip.</summary>
    [Parameter] public Func<InlineDiffDecision, Task>? OnDecision { get; set; }

    /// <summary>Whether the <c>A</c> (accept) and <c>R</c> (reject) shortcuts work inside an open
    /// popover. Default is true; the shortcut is advertised through <c>aria-keyshortcuts</c> only while
    /// it is enabled.</summary>
    [Parameter] public bool EnableShortcuts { get; set; } = true;

    /// <summary>Limits of the comparison. Leave it <c>null</c> for the engine defaults; a lower
    /// <see cref="TextDiffOptions.MaxTokens"/> / <see cref="TextDiffOptions.MaxCells"/> makes the diff
    /// degrade (and say so) sooner.</summary>
    [Parameter] public TextDiffOptions? Options { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Accessible name of the diff group. Defaults to a localized "Inline diff".</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Unmatched attributes forwarded to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Whether the component currently offers decisions: <see cref="Mode"/> is
    /// <see cref="InlineDiffMode.Review"/> and <see cref="ReadOnly"/> is false.</summary>
    public bool IsReview => Mode == InlineDiffMode.Review && !ReadOnly;

    /// <summary>The text the current accepted set produces, exactly as
    /// <see cref="TextDiffResult.Apply"/> computes it.</summary>
    public string ResultText => _result.Apply(ApplySet(_accepted));

    /// <summary>Ids of the accepted changes of the current comparison, in document order.</summary>
    public IReadOnlyList<string> AcceptedIds
        => _result.Changes.Where(hunk => _accepted.Contains(hunk.Id)).Select(hunk => hunk.Id).ToList();

    /// <summary>Whether a decision is being persisted through <see cref="OnDecision"/> right now.
    /// While it runs the diff is <c>aria-busy</c> and every decision surface is disabled.</summary>
    public bool IsBusy => _deciding;

    /// <summary>Whether there is a decision to undo. Exactly one level is kept, so a second Undo is a
    /// no-op.</summary>
    public bool CanUndo => _undo is not null;

    /// <summary>
    /// The comparison behind the rendering — granularity, fallback and the hunks themselves. Named
    /// <c>Diff</c> because <see cref="ResultText"/> is the resulting text and <see cref="ResultChanged"/>
    /// raises that text: three properties called "result" would be three different things.
    /// </summary>
    public TextDiffResult Diff => _result;

    /// <summary>Latest announcement, mirrored into the component's single live region.</summary>
    public string Announcement => _announcement;

    /// <summary>Accepts one change: runs <see cref="OnDecision"/> and applies the decision only when it
    /// completes without throwing. A no-op for an unknown id or outside Review mode.</summary>
    /// <param name="hunkId">Id of the change (a hunk's <c>Id</c>).</param>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public Task AcceptHunkAsync(string hunkId) => DecideHunkAsync(hunkId, accepted: true);

    /// <summary>Rejects one change. Behaves like <see cref="AcceptHunkAsync"/>.</summary>
    /// <param name="hunkId">Id of the change (a hunk's <c>Id</c>).</param>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public Task RejectHunkAsync(string hunkId) => DecideHunkAsync(hunkId, accepted: false);

    /// <summary>Accepts every change that is not accepted yet. Each of them runs
    /// <see cref="OnDecision"/> first; a throw applies none of them.</summary>
    /// <returns>A task that completes when the bulk decision has been applied or refused.</returns>
    public Task AcceptAllAsync()
        => ApplyBulkAsync(accepted: true);

    /// <summary>Rejects every change that is still accepted. Behaves like
    /// <see cref="AcceptAllAsync"/>.</summary>
    /// <returns>A task that completes when the bulk decision has been applied or refused.</returns>
    public Task RejectAllAsync()
        => ApplyBulkAsync(accepted: false);

    /// <summary>Restores the accepted set from before the last decision, raises both callbacks and
    /// announces the undo. A no-op when nothing is undoable.</summary>
    /// <returns>A task that completes when the accepted set has been restored.</returns>
    public async Task UndoAsync()
    {
        if (_undo is not { } snapshot)
        {
            return;
        }

        _accepted.Clear();
        _accepted.UnionWith(snapshot.Accepted);
        _decided.Clear();
        _decided.UnionWith(snapshot.Decided);
        _undo = null;
        _errorMessage = null;
        _announcement = Loc["TmInlineDiff_AnnounceUndo"];

        await InvokeAsync(StateHasChanged);
        await RaiseAsync();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var signature = string.Concat(
            Granularity.ToString(),
            "\u0000",
            OldText ?? string.Empty,
            "\u0000",
            NewText ?? string.Empty);

        if (!string.Equals(signature, _resultSignature, StringComparison.Ordinal))
        {
            _resultSignature = signature;
            _result = Granularity == TextDiffGranularity.Lines
                ? TextDiff.Lines(OldText, NewText, Options)
                : TextDiff.Words(OldText, NewText, Options);
            _view = BuildView(_result);
            _openHunkId = null;
            _restoreMarkerHunkId = null;
            _undo = null;
            _decided.Clear();
            _accepted.Clear();
            _acceptedParameter = null;
            _lastRaised = null;
            _errorMessage = null;
        }

        SyncAcceptedSet();
        PruneDecisions();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_restoreMarkerHunkId is { } restore
            && _markerRefs.TryGetValue(restore, out var marker))
        {
            _restoreMarkerHunkId = null;
            await FocusAsync(marker);
            return;
        }

        if (_focusTarget == FocusTarget.PopoverAccept)
        {
            _focusTarget = FocusTarget.None;
            await FocusAsync(_popoverAcceptElement);
            return;
        }

        if (_focusTarget == FocusTarget.Root)
        {
            _focusTarget = FocusTarget.None;
            await FocusAsync(_rootElement);
        }
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    private async Task DecideHunkAsync(string hunkId, bool accepted)
    {
        if (!IsReview || _deciding)
        {
            return;
        }

        var hunk = _result.Changes.FirstOrDefault(candidate => candidate.Id == hunkId);
        if (hunk is null)
        {
            return;
        }

        if (IsAccepted(hunkId) == accepted && _decided.Contains(hunkId))
        {
            // Already in that state — do not stack an undo step for a decision that changes nothing.
            ClosePopover();
            return;
        }

        await ApplyAsync([(hunk, accepted)], bulk: false);
    }

    private async Task ApplyBulkAsync(bool accepted)
    {
        if (!IsReview || _deciding)
        {
            return;
        }

        var targets = _result.Changes
            .Where(hunk => IsAccepted(hunk.Id) != accepted)
            .Select(hunk => (hunk, accepted))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        await ApplyAsync(targets, bulk: true);
    }

    private async Task ApplyAsync(IReadOnlyList<(TextDiffHunk Hunk, bool Accepted)> decisions, bool bulk)
    {
        var snapshot = new DecisionSnapshot(new HashSet<string>(_accepted, StringComparer.Ordinal),
            new HashSet<string>(_decided, StringComparer.Ordinal));

        _errorMessage = null;
        _deciding = true;
        await InvokeAsync(StateHasChanged);

        // Every decision the callback sees carries the text the decision WOULD produce — including the
        // earlier members of the same batch — but nothing is applied until all of them succeeded, so a
        // bulk accept over a failing save never leaves half of the diff decided.
        var preview = new HashSet<string>(_accepted, StringComparer.Ordinal);
        foreach (var (hunk, accepted) in decisions)
        {
            if (accepted)
            {
                preview.Add(hunk.Id);
            }
            else
            {
                preview.Remove(hunk.Id);
            }

            var decision = new InlineDiffDecision(hunk.Id, accepted, _result.Apply(ApplySet(preview)));
            if (OnDecision is not null)
            {
                try
                {
                    await OnDecision(decision);
                }
                catch (Exception)
                {
                    _deciding = false;
                    _errorMessage = Loc["TmInlineDiff_Error"];
                    _announcement = _errorMessage;
                    _focusTarget = FocusTarget.Root;
                    ClosePopover(restoreFocus: false);
                    await InvokeAsync(StateHasChanged);
                    return;
                }
            }
        }

        _deciding = false;
        _undo = snapshot;
        foreach (var (hunk, accepted) in decisions)
        {
            _decided.Add(hunk.Id);
            if (accepted)
            {
                _accepted.Add(hunk.Id);
            }
            else
            {
                _accepted.Remove(hunk.Id);
            }
        }

        _errorMessage = null;
        _openHunkId = null;
        _announcement = AnnouncementFor(
            decisions.Count,
            decisions[0].Accepted,
            bulk,
            ChangeIndex(decisions[0].Hunk.Id));

        if (!bulk)
        {
            // A decision closes the popover, so focus goes back to the marker that owned it — the
            // reader stays where the change is instead of dropping to the document start.
            _restoreMarkerHunkId = decisions[0].Hunk.Id;
        }

        await InvokeAsync(StateHasChanged);
        await RaiseAsync();
    }

    private async Task RaiseAsync()
    {
        var accepted = AcceptedIds;
        _lastRaised = accepted;
        _acceptedParameter = accepted;

        if (AcceptedHunkIdsChanged.HasDelegate)
        {
            await AcceptedHunkIdsChanged.InvokeAsync(accepted);
        }

        if (ResultChanged.HasDelegate)
        {
            await ResultChanged.InvokeAsync(ResultText);
        }
    }

    private void SyncAcceptedSet()
    {
        var changeIds = _result.Changes.Select(hunk => hunk.Id).ToHashSet(StringComparer.Ordinal);
        var incoming = AcceptedHunkIds is null
            ? []
            : AcceptedHunkIds
                .Where(id => !string.IsNullOrEmpty(id) && changeIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

        if (_lastRaised is not null && SequenceEqual(incoming, _lastRaised))
        {
            // The host echoed the set the component itself just raised. Replacing the session state
            // with it would drop the undo step and the rejected/pending distinction the user sees.
            return;
        }

        if (SequenceEqual(incoming, _acceptedParameter))
        {
            return;
        }

        _acceptedParameter = incoming;
        _accepted.Clear();
        _accepted.UnionWith(incoming);
        _decided.Clear();
        _decided.UnionWith(incoming);
        _undo = null;
    }

    /// <summary>Drops session decisions whose hunk no longer exists in the current comparison.</summary>
    private void PruneDecisions()
    {
        var changeIds = _result.Changes.Select(hunk => hunk.Id).ToHashSet(StringComparer.Ordinal);
        _accepted.RemoveWhere(id => !changeIds.Contains(id));
        _decided.RemoveWhere(id => !changeIds.Contains(id));

        if (_openHunkId is not null && !changeIds.Contains(_openHunkId))
        {
            _openHunkId = null;
        }
    }

    // ── Popover ──────────────────────────────────────────────────────────────

    private void TogglePopover(string hunkId)
    {
        if (_openHunkId == hunkId)
        {
            ClosePopover();
            return;
        }

        _openHunkId = hunkId;
        _restoreMarkerHunkId = null;
        _focusTarget = FocusTarget.PopoverAccept;
    }

    private void ClosePopover(bool restoreFocus = true)
    {
        if (_openHunkId is null)
        {
            return;
        }

        if (restoreFocus)
        {
            _restoreMarkerHunkId = _openHunkId;
        }

        _openHunkId = null;
    }

    /// <summary>
    /// A <c>Tab</c> pressed on the FIRST or the LAST stop of the popover moves focus out of it, so the
    /// non-modal popover closes instead of staying behind. The two inner moves (Accept → Reject forwards,
    /// Reject → Accept backwards) keep it open.
    /// <para>
    /// This is deliberately keyed on the press rather than on a <c>focusout</c> handler:
    /// <see cref="FocusEventArgs"/> carries no related target in Blazor (only <c>Type</c>), so a
    /// <c>focusout</c> cannot tell "moved to the next popover button" from "left the popover" — closing
    /// there would break the second focus stop. The default Tab behaviour is untouched: the browser still
    /// moves focus, and closing the popover does not pull it back.
    /// </para>
    /// </summary>
    private void HandlePopoverTab(KeyboardEventArgs args, bool isFirst, bool isLast)
    {
        if (!string.Equals(args.Key, "Tab", StringComparison.Ordinal) || _openHunkId is null)
        {
            return;
        }

        var leaves = args.ShiftKey ? isFirst : isLast;
        if (leaves)
        {
            ClosePopover(restoreFocus: false);
        }
    }

    /// <summary>
    /// A pointer press on the diff text dismisses the open popover — the marker and the popover itself stop
    /// the click, so only the surface AROUND a decision closes it. (A click outside this component is not
    /// observable from Blazor without a document-level listener; <see cref="TogglePopover"/> already
    /// guarantees at most ONE popover is open, so no second stale surface can appear either way.)
    /// </summary>
    private void HandleContentClick()
    {
        if (_openHunkId is not null)
        {
            ClosePopover(restoreFocus: false);
        }
    }

    private Task HandlePopoverKeyDown(KeyboardEventArgs args)
    {
        if (_openHunkId is not { } hunkId)
        {
            return Task.CompletedTask;
        }

        if (args.Key == "Escape")
        {
            ClosePopover();
            return Task.CompletedTask;
        }

        // A held or modified key is a different gesture — the announced shortcut is the bare letter.
        if (!EnableShortcuts || args.Repeat || args.CtrlKey || args.AltKey || args.MetaKey)
        {
            return Task.CompletedTask;
        }

        if (string.Equals(args.Key, "a", StringComparison.OrdinalIgnoreCase))
        {
            return DecideHunkAsync(hunkId, accepted: true);
        }

        if (string.Equals(args.Key, "r", StringComparison.OrdinalIgnoreCase))
        {
            return DecideHunkAsync(hunkId, accepted: false);
        }

        return Task.CompletedTask;
    }

    private async Task FocusAsync(ElementReference element)
    {
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

    // ── View model ───────────────────────────────────────────────────────────

    private static List<HunkView> BuildView(TextDiffResult result)
    {
        var view = new List<HunkView>(result.Hunks.Count);
        var changeIndex = 0;
        foreach (var hunk in result.Hunks)
        {
            view.Add(new HunkView(hunk, hunk.IsChange ? ++changeIndex : 0));
        }

        return view;
    }

    /// <summary>
    /// The ids to hand to <see cref="TextDiffResult.Apply"/> for a set of accepted CHANGES. The
    /// unchanged hunks must be in the set too: they normally carry identical old and new text, but
    /// the separator between two changes belongs to the hunk before it, so an equal hunk can hold the
    /// whitespace that separates a rejected change from an accepted one. Passing only the changed ids
    /// would silently drop that separator.
    /// </summary>
    private List<string> ApplySet(HashSet<string> acceptedChanges)
    {
        if (acceptedChanges.Count == 0)
        {
            // Nothing accepted must reproduce the OLD input character for character — including the
            // separators an equal hunk would otherwise take from the new text.
            return [];
        }

        var ids = new List<string>(_result.Hunks.Count);
        foreach (var hunk in _result.Hunks)
        {
            if (!hunk.IsChange || acceptedChanges.Contains(hunk.Id))
            {
                ids.Add(hunk.Id);
            }
        }

        return ids;
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string>? right)
    {
        if (right is null)
        {
            return left.Count == 0;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // ── Derived state ────────────────────────────────────────────────────────

    private string ModeName => IsReview ? "review" : "display";

    private string GranularityName => _result.Granularity == TextDiffGranularity.Lines ? "lines" : "words";

    private string FallbackName => _result.Fallback switch
    {
        TextDiffFallbackKind.LineLevel => "line-level",
        TextDiffFallbackKind.SingleChange => "single-change",
        _ => "none",
    };

    private string? FallbackMessage => _result.Fallback switch
    {
        TextDiffFallbackKind.LineLevel => Loc["TmInlineDiff_FallbackLineLevel"],
        TextDiffFallbackKind.SingleChange => Loc["TmInlineDiff_FallbackSingleChange"],
        _ => null,
    };

    private string? ErrorMessage => _errorMessage;

    private bool HasChanges => _result.Changes.Count > 0;

    private string EffectiveAriaLabel => AriaLabel ?? Loc["TmInlineDiff_AriaLabel"];

    private string CountText => string.Format(
        CultureInfo.CurrentCulture, Loc["TmInlineDiff_ChangesLabel"], _result.Changes.Count);

    private string? AcceptShortcuts => EnableShortcuts ? AcceptShortcutKeys : null;

    private string? RejectShortcuts => EnableShortcuts ? RejectShortcutKeys : null;

    private string AnnouncementId => $"{_id}-announcements";

    private string RootClass => string.Concat(
        "tm-inline-diff",
        _result.Granularity == TextDiffGranularity.Lines ? " tm-inline-diff--lines" : string.Empty,
        IsReview ? " tm-inline-diff--review" : string.Empty,
        _result.IsDegraded ? " tm-inline-diff--degraded" : string.Empty,
        string.IsNullOrWhiteSpace(Class) ? string.Empty : $" {Class}");

    private string PopoverId(string hunkId) => $"{_id}-popover-{hunkId}";

    private bool IsAccepted(string hunkId) => _accepted.Contains(hunkId);

    private string StateName(string hunkId)
        => IsAccepted(hunkId) ? "accepted"
            : _decided.Contains(hunkId) ? "rejected"
            : "pending";

    private string ChangeModifier(string hunkId) => StateName(hunkId) switch
    {
        "accepted" => "tm-inline-diff__change--accepted",
        "rejected" => "tm-inline-diff__change--rejected",
        _ => "tm-inline-diff__change--pending",
    };

    private static string KindName(TextDiffHunkKind kind) => kind switch
    {
        TextDiffHunkKind.Insert => "insert",
        TextDiffHunkKind.Delete => "delete",
        _ => "replace",
    };

    private string KindLabel(TextDiffHunkKind kind) => kind switch
    {
        TextDiffHunkKind.Insert => Loc["TmInlineDiff_KindInserted"],
        TextDiffHunkKind.Delete => Loc["TmInlineDiff_KindRemoved"],
        _ => Loc["TmInlineDiff_KindReplaced"],
    };

    private string StateLabel(string hunkId) => StateName(hunkId) switch
    {
        "accepted" => Loc["TmInlineDiff_DecisionAccepted"],
        "rejected" => Loc["TmInlineDiff_DecisionRejected"],
        _ => Loc["TmInlineDiff_DecisionPending"],
    };

    private string MarkerLabel(int changeIndex, TextDiffHunk hunk)
        => string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmInlineDiff_MarkerLabel"],
            string.Format(CultureInfo.CurrentCulture, Loc["TmInlineDiff_ChangeOf"],
                changeIndex, _result.Changes.Count, KindLabel(hunk.Kind)),
            StateLabel(hunk.Id));

    private string PopoverLabel(int changeIndex)
        => string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmInlineDiff_PopoverLabel"],
            changeIndex,
            _result.Changes.Count);

    private string AnnouncementFor(int count, bool accepted, bool bulk, int changeIndex)
    {
        if (bulk)
        {
            // A bulk decision announces a NUMBER OF changes in a sentence, so it reads the plural family
            // (docs/localization-plurals.md): one template plus a substituted count says "All 1 changes"
            // in English and "Přijato všech 2 změn" in Czech, where the 2–4 form is a different noun case.
            return Loc.Plural(
                accepted ? "TmInlineDiff_AnnounceAcceptedAll" : "TmInlineDiff_AnnounceRejectedAll",
                count);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            accepted ? Loc["TmInlineDiff_AnnounceAccepted"] : Loc["TmInlineDiff_AnnounceRejected"],
            changeIndex,
            _result.Changes.Count);
    }

    /// <summary>1-based position of a change among the reviewable changes of the current comparison,
    /// or 0 when the id names no change.</summary>
    private int ChangeIndex(string hunkId)
    {
        var changes = _result.Changes;
        for (var index = 0; index < changes.Count; index++)
        {
            if (string.Equals(changes[index].Id, hunkId, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }

    private readonly record struct HunkView(TextDiffHunk Hunk, int ChangeIndex);

    private sealed record DecisionSnapshot(HashSet<string> Accepted, HashSet<string> Decided);

    private enum FocusTarget
    {
        None,
        Root,
        PopoverAccept,
    }
}
