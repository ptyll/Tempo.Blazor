using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The models tried for ONE request, in the order they were tried, as a flow that ends in an Answer node — every attempt
/// with its result, the reason it failed or was skipped, how long it took and when — plus an edit mode in which a reader
/// reorders the chain, adds a model from the host's catalogue and removes one.
/// <para>
/// The surface renders the run it is given and never polls or mutates the host's data: in the RUN view it only reads
/// <see cref="Attempts"/>, and in the EDIT view it keeps its own working copy of <see cref="Chain"/> and reports every
/// change through <see cref="ChainChanged"/> (a reorder through the shared <c>TmSortableList</c>, which supplies the
/// pointer drag AND its keyboard and menu alternatives, WCAG 2.5.7).
/// </para>
/// <para>
/// Accessibility: every attempt is a labelled node of an ordered list, its result is an ICON plus a localized WORD plus
/// a tone (colour is never the only carrier), a failure or a skip always prints its reason, and the decorative arrows are
/// <c>aria-hidden</c> because the list already carries the order. The table view carries exactly the same facts without
/// the geometry, so a screen-reader, keyboard-only or phone reader loses nothing. ONE polite live region speaks only
/// when the reader adds or removes a model — a reorder is narrated by the sortable list itself, so nothing is announced
/// twice.
/// </para>
/// </summary>
public partial class TmFallbackChain : TmComponentBase
{
    private readonly string _id = $"tm-ai-fallback-{Guid.NewGuid():n}"[..27];

    private List<FallbackAttempt> _attempts = [];
    private List<FallbackChainEntry> _chain = [];
    private IReadOnlyList<AiModelInfo> _addable = [];
    private IReadOnlyList<FallbackChainEntry>? _lastChainParameter;
    private bool _chainSeeded;
    private bool _chainDerived;

    private FallbackChainViewMode _viewMode = FallbackChainViewMode.Flow;
    private FallbackChainViewMode _lastViewModeParameter = FallbackChainViewMode.Flow;

    private FallbackResolutionPolicy _policy = FallbackResolutionPolicy.Pin;
    private FallbackResolutionPolicy _lastPolicyParameter = FallbackResolutionPolicy.Pin;

    private string? _selectedModelId;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // Inline rename state: which row edits and the draft the reader typed. Only ONE row edits at a time.
    private string? _renamingModelId;
    private string _renameDraft = string.Empty;
    private bool _focusRenameInputAfterRender;
    private string? _focusRenameButtonFor;
    private readonly Dictionary<string, ElementReference> _renameButtons = new(StringComparer.Ordinal);

    /// <summary>
    /// Labels the reader renamed in this session, keyed by model id. Applied on every resync so a host re-render with
    /// the same <see cref="Chain"/> reference does not revert the reader's rename before the host had a chance to
    /// persist it. The override holds the original label too: once the host's label catches up (matches the rename)
    /// or moves on (a different label), the entry is dropped so a stale rename never sits on top of the host's data.
    /// </summary>
    private readonly Dictionary<string, (string OriginalLabel, string Label)> _renamedLabels = new(StringComparer.Ordinal);

    // The editor's focus hand-back (WCAG 2.4.3): a control that removes or disables itself must not drop the
    // reader's focus onto the document body. The target is resolved on the click and consumed after the render,
    // because the element that has to receive it only exists once the new markup is patched.
    private TmSortableList<FallbackChainEntry>? _sortable;
    private ElementReference _addSelect;
    private ElementReference _editorHint;
    private ElementReference _renameInput;
    private EditorFocus? _pendingEditorFocus;

    /// <summary>Where the editor hands the focus back to after an edit that removed the focused control.</summary>
    private enum EditorFocusTarget
    {
        /// <summary>A chain row, by its index in the working chain.</summary>
        Row,

        /// <summary>The "add a model" select (the only way forward on an empty chain).</summary>
        AddModel,

        /// <summary>The localized editor hint — the last resort when no other control can take it.</summary>
        Hint,
    }

    /// <summary>One pending focus hand-back: what to focus and, for a row, which one.</summary>
    private readonly record struct EditorFocus(EditorFocusTarget Target, int Index);

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The attempts, in the order they were tried. Each entry is one node of the flow; null or empty renders the
    /// localized empty state, and a null entry inside the list is skipped. The component never mutates the list.
    /// </summary>
    [Parameter] public IReadOnlyList<FallbackAttempt>? Attempts { get; set; }

    /// <summary>
    /// Whether the surface reports the run or edits the chain. Default <see cref="FallbackChainMode.View"/>. The host
    /// owns this switch (it is not two-way): the component never changes it on its own.
    /// </summary>
    [Parameter] public FallbackChainMode Mode { get; set; } = FallbackChainMode.View;

    /// <summary>
    /// The run presentation, two-way bound with <see cref="ViewModeChanged"/> (<c>@bind-ViewMode</c>). Default
    /// <see cref="FallbackChainViewMode.Flow"/>. It applies to the run view; the editor has its own presentation.
    /// </summary>
    [Parameter] public FallbackChainViewMode ViewMode { get; set; } = FallbackChainViewMode.Flow;

    /// <summary>Fires with the new presentation when the reader uses the View switcher.</summary>
    [Parameter] public EventCallback<FallbackChainViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// The models in fallback order, two-way bound with <see cref="ChainChanged"/> (<c>@bind-Chain</c>). Null (the
    /// default) derives the chain from <see cref="Attempts"/>, which is what an editor opened over a finished run shows.
    /// A pinned list is copied, never mutated: the reader's reorder reports a NEW list through
    /// <see cref="ChainChanged"/>.
    /// </summary>
    [Parameter] public IReadOnlyList<FallbackChainEntry>? Chain { get; set; }

    /// <summary>Fires with the new chain whenever the reader reorders, adds or removes a model.</summary>
    [Parameter] public EventCallback<IReadOnlyList<FallbackChainEntry>> ChainChanged { get; set; }

    /// <summary>
    /// The host's model catalogue, from which the editor offers what is NOT already in the chain. Null or empty renders
    /// the localized "no models are available" note and a disabled Add button.
    /// </summary>
    [Parameter] public IReadOnlyList<AiModelInfo>? AvailableModels { get; set; }

    /// <summary>
    /// The rule the chain follows, two-way bound with <see cref="PolicyChanged"/> (<c>@bind-Policy</c>). Default
    /// <see cref="FallbackResolutionPolicy.Pin"/>, so an unset policy never falls back to another model by accident.
    /// The editor offers the three policies as a segmented radio group; the run view reports the value as a word.
    /// </summary>
    [Parameter] public FallbackResolutionPolicy Policy { get; set; } = FallbackResolutionPolicy.Pin;

    /// <summary>Fires with the new policy when the reader picks one in the editor.</summary>
    [Parameter] public EventCallback<FallbackResolutionPolicy> PolicyChanged { get; set; }

    /// <summary>
    /// Whether the policy is shown at all. Default <c>true</c> — the run view prints it as a read-out and the editor
    /// offers the radio group.
    /// </summary>
    [Parameter] public bool ShowPolicy { get; set; } = true;

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Fallback chain"); the heading also names the
    /// surface, so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Whether the editor offers an inline rename of a chain entry's label. Default <c>false</c> — a host that never
    /// bound <see cref="OnRename"/> sees no rename button. The rename commits on Enter and rolls back on Escape, exactly
    /// like the memory panel's inline edit; the host owns persistence.
    /// </summary>
    [Parameter] public bool AllowRename { get; set; }

    /// <summary>
    /// Fires with the committed rename (<see cref="FallbackChainRenameArgs"/> carrying the model id and the new label)
    /// when the reader commits an inline edit with Enter. Escape rolls back without raising this callback. The component
    /// updates its own working copy so the row shows the new label at once; the host applies the change to its own
    /// persistence.
    /// </summary>
    [Parameter] public EventCallback<FallbackChainRenameArgs> OnRename { get; set; }

    /// <summary>
    /// The culture that formats the latencies and the attempt times. Null (the default) means the current culture at
    /// render time.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The attempts being rendered, in the order they were tried (null entries removed).</summary>
    public IReadOnlyList<FallbackAttempt> VisibleAttempts => _attempts;

    /// <summary>
    /// The chain the editor currently holds: the host's pinned <see cref="Chain"/> (copied) or the one derived from
    /// <see cref="Attempts"/>, after the reader's own edits. The component reports changes through
    /// <see cref="ChainChanged"/> and never writes back to the host's list.
    /// </summary>
    public IReadOnlyList<FallbackChainEntry> ChainEntries => _chain;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmFallbackChain_Title"] : Title!;

    private bool IsEdit => EffectiveMode == FallbackChainMode.Edit;

    private string ViewSlug => IsEdit
        ? "edit"
        : EffectiveViewMode == FallbackChainViewMode.Table ? "table" : "flow";

    private FallbackChainMode EffectiveMode
        => FallbackPresentation.IsKnownMode(Mode) ? Mode : FallbackChainMode.View;

    private FallbackChainViewMode EffectiveViewMode
        => FallbackPresentation.IsKnownViewMode(_viewMode) ? _viewMode : FallbackChainViewMode.Flow;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private string EditorHintId => _id + "-editor-hint";

    private string AddSelectId => _id + "-add-model";

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    private string RootClass
    {
        get
        {
            var root = "tm-ai-fallback" + (IsEdit ? " tm-ai-fallback--edit" : string.Empty);
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    private string PolicyText => FallbackPresentation.PolicyText(Loc, _policy);

    private string PolicySlug => FallbackPresentation.PolicySlug(_policy);

    private bool IsEmpty => _attempts.Count == 0;

    private FallbackAttempt? Answer => _attempts.FirstOrDefault(attempt => attempt.Succeeded);

    private string AnswerText => Answer is { } answer
        ? Loc["TmFallbackChain_AnswerFrom", answer.DisplayName]
        : Loc["TmFallbackChain_AnswerNone"];

    private bool HasAnswer => Answer is not null;

    private IReadOnlyList<SegmentedOption<FallbackChainViewMode>> ViewOptions =>
    [
        new() { Label = Loc["TmFallbackChain_ViewFlow"], Value = FallbackChainViewMode.Flow },
        new() { Label = Loc["TmFallbackChain_ViewTable"], Value = FallbackChainViewMode.Table },
    ];

    private IReadOnlyList<SegmentedOption<FallbackResolutionPolicy>> PolicyOptions =>
    [
        new() { Label = Loc["TmFallbackPolicy_Pin"], Value = FallbackResolutionPolicy.Pin },
        new() { Label = Loc["TmFallbackPolicy_Fallback"], Value = FallbackResolutionPolicy.Fallback },
        new() { Label = Loc["TmFallbackPolicy_Auto"], Value = FallbackResolutionPolicy.Auto },
    ];

    private bool CanAdd => _addable.Count > 0;

    private string AddNote => (AvailableModels is null || AvailableModels.Count == 0)
        ? Loc["TmFallbackChain_NoModels"]
        : Loc["TmFallbackChain_NoModelsLeft"];

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _attempts = Attempts is null ? [] : [.. Attempts.Where(attempt => attempt is not null)];

        if (ViewMode != _lastViewModeParameter)
        {
            _lastViewModeParameter = ViewMode;
            _viewMode = FallbackPresentation.IsKnownViewMode(ViewMode) ? ViewMode : FallbackChainViewMode.Flow;
        }

        if (Policy != _lastPolicyParameter)
        {
            _lastPolicyParameter = Policy;
            _policy = FallbackPresentation.NormalizePolicy(Policy);
        }

        SeedChain();
        RefreshAddable();
        PruneRenameState();
    }

    /// <summary>
    /// Drops the rename bookkeeping for models that are no longer in the chain: a rename button reference or a
    /// remembered override for a model that left the chain would only leak memory and could shadow the label if the
    /// model ever comes back with a different host-supplied name.
    /// </summary>
    private void PruneRenameState()
    {
        PruneKeysNotInChain(_renameButtons);
        PruneKeysNotInChain(_renamedLabels);
    }

    private void PruneKeysNotInChain<TValue>(Dictionary<string, TValue> dictionary)
    {
        if (dictionary.Count == 0)
        {
            return;
        }

        List<string>? stale = null;
        foreach (var key in dictionary.Keys)
        {
            if (_chain.All(entry => !string.Equals(entry.ModelId, key, StringComparison.Ordinal)))
            {
                (stale ??= []).Add(key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var key in stale)
        {
            dictionary.Remove(key);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The rename input takes the focus as soon as it exists: the reader pressed a button and expects to type.
        if (_focusRenameInputAfterRender)
        {
            _focusRenameInputAfterRender = false;
            if (_renameInput.Id is not null)
            {
                try
                {
                    await _renameInput.FocusAsync();
                }
                catch (Exception exception) when (exception is Microsoft.JSInterop.JSException
                    or Microsoft.JSInterop.JSDisconnectedException
                    or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
                {
                    // No JavaScript, or the element is already gone: the input is still reachable with Tab.
                }
            }
        }

        // The rename editor closed (Enter committed or Escape cancelled): the input is gone, so the focus would fall
        // onto the document body (WCAG 2.4.3). It is handed back to the rename button of the same row instead.
        // The hand-back yields once so the browser finishes the key event that closed the editor: focusing the
        // button synchronously inside the Enter keydown would make the browser fire the button's click on the
        // keyup (Enter activates a focused button), re-opening the editor.
        if (_focusRenameButtonFor is { } renameTarget)
        {
            _focusRenameButtonFor = null;
            if (_renameButtons.TryGetValue(renameTarget, out var renameButton))
            {
                // Yield so the Enter keyup that committed the rename does not activate the newly focused button
                // and reopen the editor.
                await Task.Yield();
                await TryFocusAsync(renameButton);
            }
        }

        // The focus move happens AFTER the paint: the control that has to receive it is created (or enabled) by this
        // very render, and a browser only focuses an element once it is patched.
        if (_pendingEditorFocus is not { } pending)
        {
            return;
        }

        _pendingEditorFocus = null;

        if (pending.Target == EditorFocusTarget.Row
            && _sortable is not null
            && await _sortable.FocusItemAsync(pending.Index))
        {
            return;
        }

        if (pending.Target != EditorFocusTarget.Hint && await FocusAddControlAsync())
        {
            return;
        }

        await FocusHintAsync();
    }

    /// <summary>
    /// Takes the host's chain when it changes (a new reference is the host's own edit), and otherwise keeps the working
    /// copy — a reader half-way through a reorder is never reset by an unrelated render of the host.
    /// </summary>
    private void SeedChain()
    {
        var hostChanged = !_chainSeeded || !ReferenceEquals(Chain, _lastChainParameter);

        if (hostChanged)
        {
            _chainSeeded = true;
            _lastChainParameter = Chain;
            _chainDerived = Chain is null;
            _chain = Chain is null ? DeriveChain() : Normalize(Chain);
            return;
        }

        if (Chain is null && !_chainDerived && _attempts.Count > 0)
        {
            // The run arrived after the first render (a live page): the editor starts from it instead of staying empty.
            // It happens ONCE — a chain the reader emptied stays empty, because "remove every model" is an edit, not a
            // missing value.
            _chainDerived = true;
            _chain = DeriveChain();
        }
    }

    private List<FallbackChainEntry> DeriveChain()
        => Normalize(_attempts.Select(attempt => new FallbackChainEntry(attempt.ModelId, attempt.Label)));

    /// <summary>
    /// Copies the host's list, dropping nulls and duplicate model ids: a chain is a sequence of DISTINCT models, and two
    /// rows with one key would break the sortable list's keyed diff. Labels the reader renamed in this session are
    /// applied over the host's values, so a host re-render with the same reference does not revert a committed rename —
    /// but only while the host's label still matches the one the rename started from. Once the host catches up (its
    /// label equals the rename) or moves on (a different label arrives), the override is dropped so a stale rename never
    /// sits on top of the host's data.
    /// </summary>
    private List<FallbackChainEntry> Normalize(IEnumerable<FallbackChainEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<FallbackChainEntry>();

        foreach (var entry in entries)
        {
            if (entry is null || !seen.Add(entry.ModelId))
            {
                continue;
            }

            if (!_renamedLabels.TryGetValue(entry.ModelId, out var renamed))
            {
                chain.Add(entry);
                continue;
            }

            if (string.Equals(entry.DisplayName, renamed.Label, StringComparison.Ordinal)
                || !string.Equals(entry.DisplayName, renamed.OriginalLabel, StringComparison.Ordinal))
            {
                // The host caught up (its label now equals the rename) or moved on (a different label arrived):
                // either way the override has served its purpose and must not shadow the host's value any longer.
                _renamedLabels.Remove(entry.ModelId);
                chain.Add(entry);
                continue;
            }

            chain.Add(entry with { Label = renamed.Label });
        }

        return chain;
    }

    /// <summary>Recomputes what the Add control offers and keeps the selection on something that can actually be added.</summary>
    private void RefreshAddable()
    {
        if (AvailableModels is null || AvailableModels.Count == 0)
        {
            _addable = [];
            _selectedModelId = null;
            return;
        }

        var inChain = new HashSet<string>(_chain.Select(entry => entry.ModelId), StringComparer.Ordinal);
        _addable = [.. AvailableModels.Where(model => model is not null && !inChain.Contains(model.Id))];

        if (_selectedModelId is null || !_addable.Any(model => string.Equals(model.Id, _selectedModelId, StringComparison.Ordinal)))
        {
            _selectedModelId = _addable.Count > 0 ? _addable[0].Id : null;
        }
    }

    // ── Inline rename ────────────────────────────────────────────────────────

    private bool IsRenaming(string modelId)
        => string.Equals(_renamingModelId, modelId, StringComparison.Ordinal);

    private void BeginRename(FallbackChainEntry entry)
    {
        if (!AllowRename || IsRenaming(entry.ModelId))
        {
            return;
        }

        _renamingModelId = entry.ModelId;
        _renameDraft = entry.DisplayName;
        _focusRenameInputAfterRender = true;
        StateHasChanged();
    }

    private Task OnRenameInputAsync(ChangeEventArgs args)
    {
        _renameDraft = args.Value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    private async Task OnRenameKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter" when !args.ShiftKey:
                await CommitRenameAsync();
                break;
            case "Escape":
                CancelRename();
                break;
        }
    }

    /// <summary>Commits the draft: an unchanged or empty label closes the editor without reporting.</summary>
    private async Task CommitRenameAsync()
    {
        if (_renamingModelId is not { } modelId)
        {
            return;
        }

        var entry = _chain.FirstOrDefault(candidate =>
            string.Equals(candidate.ModelId, modelId, StringComparison.Ordinal));
        var draft = _renameDraft.Trim();

        CloseRenameEditor();

        if (entry is null || string.IsNullOrWhiteSpace(draft)
            || string.Equals(entry.DisplayName, draft, StringComparison.Ordinal))
        {
            StateHasChanged();
            return;
        }

        var index = _chain.IndexOf(entry);
        if (index >= 0)
        {
            _chain[index] = entry with { Label = draft };
        }

        // Remember the rename so a host re-render with the same Chain reference does not revert it before the host
        // persists the change. The original label is stored alongside so Normalize can tell when the host has caught
        // up (its label now equals the rename) or moved on (a different label arrived) and drop the override.
        _renamedLabels[modelId] = (OriginalLabel: entry.DisplayName, Label: draft);

        if (OnRename.HasDelegate)
        {
            await OnRename.InvokeAsync(new FallbackChainRenameArgs(modelId, draft));
        }

        Announce(Loc["TmFallbackChain_Renamed", draft]);
        StateHasChanged();
    }

    /// <summary>Closes the editor without reporting anything.</summary>
    private void CancelRename()
    {
        if (_renamingModelId is null)
        {
            return;
        }

        CloseRenameEditor();
        StateHasChanged();
    }

    /// <summary>
    /// Leaving the rename input (Tab, click elsewhere) commits the draft, exactly like the memory panel's inline edit:
    /// a half-typed label must not stay half-edited. The commit is idempotent — <see cref="CommitRenameAsync"/> returns
    /// early when the editor is already closed (Enter then blur, or double blur).
    /// </summary>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    private async Task OnRenameFocusOutAsync()
    {
        if (_renamingModelId is not null)
        {
            await CommitRenameAsync();
        }
    }

    private void CloseRenameEditor()
    {
        // The input is about to disappear: remember which row was being renamed so the focus can be handed back to
        // that row's rename button once the editor closes (WCAG 2.4.3).
        _focusRenameButtonFor = _renamingModelId;
        _renamingModelId = null;
        _renameDraft = string.Empty;
    }

    // ── Editing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends the picked catalogue model to the chain, announces it once and reports the new chain. Does nothing when
    /// every model is already in the chain.
    /// <para>
    /// Adding the LAST addable model disables the button the reader pressed, and a browser drops the focus of a control
    /// that becomes disabled onto the document body. The focus is therefore handed to the row the add created (WCAG
    /// 2.4.3); while another model stays addable the button keeps the focus it already has.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    public async Task AddSelectedAsync()
    {
        var model = _addable.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _selectedModelId, StringComparison.Ordinal))
            ?? _addable.FirstOrDefault();

        if (model is null || FallbackChainEntry.FromModel(model) is not { } entry)
        {
            return;
        }

        _chain.Add(entry);
        RefreshAddable();
        _pendingEditorFocus = CanAdd ? null : new EditorFocus(EditorFocusTarget.Row, _chain.Count - 1);
        Announce(Loc["TmFallbackChain_Added", entry.DisplayName]);
        await RaiseChainChangedAsync();
        StateHasChanged();
    }

    /// <summary>
    /// Removes one model from the chain, announces it once and reports the new chain. A chain entry the current chain
    /// does not hold is ignored.
    /// <para>
    /// The focused Remove button is deleted with its row, so the focus would land on the document body and a keyboard
    /// or screen-reader reader would have to start again from the top of the page (WCAG 2.4.3). The reader is
    /// therefore put on the row that took the removed one's place; when the chain is empty the focus moves to the Add
    /// control, and when that is disabled too it stops on the editor hint.
    /// </para>
    /// </summary>
    /// <param name="entry">The entry to remove.</param>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    public async Task RemoveAsync(FallbackChainEntry entry)
    {
        var index = entry is null ? -1 : _chain.IndexOf(entry);
        if (index < 0 || !_chain.Remove(entry))
        {
            return;
        }

        RefreshAddable();
        _pendingEditorFocus = NextFocusAfterRemoval(index);
        Announce(Loc["TmFallbackChain_Removed", entry.DisplayName]);
        await RaiseChainChangedAsync();
        StateHasChanged();
    }

    /// <summary>
    /// Where the focus goes once a row is gone: the row now at the removed index (or the last one when the removal was
    /// the tail), the Add control on an empty chain, and the hint when nothing can be added either.
    /// </summary>
    private EditorFocus NextFocusAfterRemoval(int removedIndex)
    {
        if (_chain.Count > 0)
        {
            return new EditorFocus(EditorFocusTarget.Row, Math.Clamp(removedIndex, 0, _chain.Count - 1));
        }

        return CanAdd
            ? new EditorFocus(EditorFocusTarget.AddModel, 0)
            : new EditorFocus(EditorFocusTarget.Hint, 0);
    }

    /// <summary>
    /// Focuses the "add a model" select, but only while it can actually take the focus: a disabled select cannot, so
    /// moving the focus there would leave it on the body again.
    /// </summary>
    private async Task<bool> FocusAddControlAsync()
        => CanAdd && await TryFocusAsync(_addSelect);

    private async Task FocusHintAsync() => await TryFocusAsync(_editorHint);

    private static async Task<bool> TryFocusAsync(ElementReference element)
    {
        if (element.Id is null)
        {
            return false;
        }

        try
        {
            await element.FocusAsync();
            return true;
        }
        catch (Exception exception) when (exception is Microsoft.JSInterop.JSException
            or Microsoft.JSInterop.JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the element is already gone: every control is still reachable with Tab, so a failed
            // hand-back must never surface as an unhandled exception.
            return false;
        }
    }

    /// <summary>
    /// Applies one committed reorder of the shared sortable list to the working chain and reports it. The move is
    /// announced by the sortable list itself, so nothing is announced here (no duplicate announcement).
    /// </summary>
    /// <param name="args">The committed move, in the indexes the list reported.</param>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    private async Task ReorderAsync(SortableReorderArgs args)
    {
        if (args.FromIndex < 0 || args.FromIndex >= _chain.Count)
        {
            return;
        }

        var target = Math.Clamp(args.ToIndex, 0, _chain.Count - 1);
        if (target == args.FromIndex)
        {
            return;
        }

        var entry = _chain[args.FromIndex];
        _chain.RemoveAt(args.FromIndex);
        _chain.Insert(target, entry);

        await RaiseChainChangedAsync();
        StateHasChanged();
    }

    private async Task RaiseChainChangedAsync()
    {
        if (ChainChanged.HasDelegate)
        {
            await ChainChanged.InvokeAsync([.. _chain]);
        }
    }

    private async Task OnViewModeChangedAsync(FallbackChainViewMode mode)
    {
        if (mode == _viewMode || !FallbackPresentation.IsKnownViewMode(mode))
        {
            return;
        }

        // The host's own value is deliberately NOT marked as seen: the reader's pick wins until the host MOVES the
        // parameter, so a host that re-renders with the same value cannot silently undo the choice.
        _viewMode = mode;
        StateHasChanged();

        if (ViewModeChanged.HasDelegate)
        {
            await ViewModeChanged.InvokeAsync(mode);
        }
    }

    private async Task OnPolicyChangedAsync(FallbackResolutionPolicy policy)
    {
        var next = FallbackPresentation.NormalizePolicy(policy);
        if (next == _policy)
        {
            return;
        }

        // The host's own value is deliberately NOT marked as seen: the reader's pick wins until the host MOVES the
        // parameter, so a host that re-renders with the same value cannot silently undo the choice.
        _policy = next;
        StateHasChanged();

        if (PolicyChanged.HasDelegate)
        {
            await PolicyChanged.InvokeAsync(next);
        }
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    // Every number on the surface follows the culture the host bound, never CultureInfo.CurrentCulture — a component
    // that mixed the two would print "1.5 s" beside a clock in the bound culture.
    private string PositionText(int position)
        => Loc["TmFallbackChain_Position", position.ToString(FormatCulture)];

    private string TimeText(DateTimeOffset value) => value.ToLocalTime().ToString("t", FormatCulture);

    /// <summary>
    /// A measured latency, in the unit that reads best: whole milliseconds under a second, one decimal in seconds
    /// above it. Never invented — an unmeasured attempt renders no latency at all in the flow and the localized
    /// "not measured" in the table.
    /// </summary>
    private string LatencyText(FallbackAttempt attempt)
    {
        var milliseconds = attempt.LatencyMs!.Value;

        return milliseconds < 1000
            ? Loc["TmFallbackChain_LatencyMs", milliseconds.ToString("0", FormatCulture)]
            : Loc["TmFallbackChain_LatencySeconds",
                (milliseconds / 1000d).ToString("0.0", FormatCulture)];
    }

    private string TableLatencyText(FallbackAttempt attempt)
        => attempt.HasLatency ? LatencyText(attempt) : Loc["TmFallbackChain_NotMeasured"];

    private string TableTimeText(FallbackAttempt attempt)
        => attempt.At is { } at ? TimeText(at) : Loc["TmFallbackChain_NotRecorded"];

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
