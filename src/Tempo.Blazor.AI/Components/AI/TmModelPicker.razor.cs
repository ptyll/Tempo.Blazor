using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Components.Overlay;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Picks the model a conversation runs on: the trigger names the chosen model and its state tags, and the
/// popup lists the host's catalogue as a <c>role="listbox"</c> whose every row compares the models on speed,
/// quality, price per million input tokens and context window — as a bar AND as the number in words, because a
/// bar alone is colour-only information.
/// <para>
/// The list is grouped by <see cref="GroupBy"/> (provider, family or nothing) and searched live; the hits are
/// marked by <c>TmHighlightedText</c>, and a group with no surviving model disappears instead of leaving an
/// empty heading. A model that does not report a value renders no bar and no chip — "not reported" is never
/// painted as a zero.
/// </para>
/// <para>
/// The whole answer is <see cref="Selection"/> (<c>@bind-Selection</c>): the model id, the extended-thinking
/// switch and the reasoning-effort level. The reasoning options of the SELECTED model are rendered above the
/// list, never inside an option (an option is a leaf of the listbox), and are offered only for a model that
/// reports them. A model that supports neither renders no options block at all.
/// </para>
/// <para>
/// Keyboard (APG): the trigger is a native button (Enter/Space open it natively, the down/up arrows open it
/// too). Focus moves into the search field, which is the popup's <c>combobox</c> carrying
/// <c>aria-activedescendant</c>; with <see cref="ShowSearch"/> off the listbox itself is the tab stop and owns
/// the arrow keys, Enter (keydown) and Space (keyup), plus type-ahead. Escape closes the popup through the
/// overlay (which returns the focus to the trigger), a pick closes it and returns the focus itself, and the
/// reader's pick is announced in the component's single polite live region.
/// </para>
/// <para>
/// On a phone the popup presents as a bottom sheet (the core overlay panel's mobile presentation), so the
/// list is never clipped by the page. The component owns no drag gesture and no animation.
/// </para>
/// <para>
/// The trigger is named by its own visible text rather than an <c>aria-label</c>, so the model, its provider
/// and the state tags (deprecated, extended thinking, effort) are all announced; only a host-supplied
/// <see cref="AriaLabel"/> replaces that name, and the tags then travel through the trigger's description.
/// The listbox itself is named by the localized "Models" label, so the popup title is not spoken twice.
/// </para>
/// </summary>
public partial class TmModelPicker : TmComponentBase
{
    /// <summary>How long a typed character stays in the type-ahead buffer. APG's usual window.</summary>
    private static readonly TimeSpan TypeaheadWindow = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// How long a trigger activation that carries no pointer press of its own is ignored after a keyboard
    /// pick. The popup returns the focus to the trigger while the pick's keydown is still being dispatched,
    /// and a browser then delivers that keydown's activation to the newly focused button; the window covers
    /// exactly that delivery (measured at ~130 ms in Chromium) without eating a deliberate second press.
    /// </summary>
    private static readonly TimeSpan ActivationBlockWindow = TimeSpan.FromMilliseconds(500);

    private const string BarVariable = "--tm-model-picker-bar";

    private readonly string _id = $"tm-model-picker-{Guid.NewGuid():n}";

    private readonly List<ModelGroup> _groups = [];
    private readonly List<AiModelInfo> _visible = [];

    private ElementReference _triggerRef;
    private ElementReference _searchRef;
    private ElementReference _listRef;
    private TmOverlayPanel? _panel;

    private TimeProvider _clock = TimeProvider.System;

    private AiModelSelection? _selection;
    private AiModelSelection? _lastSelectionParam;
    private bool _isOpen;
    private bool _focusPanelPending;
    private bool _focusTriggerAfterClose;
    private string _searchText = string.Empty;
    private int _activeIndex = -1;
    private string _typeaheadBuffer = string.Empty;
    private DateTimeOffset _typeaheadAt;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private DateTimeOffset _activationBlockedUntil;
    private bool _pointerPressed;

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The catalogue to choose from. The component never mutates it; the order of the list is the reading
    /// order, and a group keeps the order its models arrive in. Null or empty renders an empty state rather
    /// than an empty listbox.
    /// </summary>
    [Parameter] public IReadOnlyList<AiModelInfo>? Models { get; set; }

    /// <summary>
    /// The current answer, two-way bound with <see cref="SelectionChanged"/> (<c>@bind-Selection</c>). Null
    /// renders the placeholder trigger. A host may keep the picker uncontrolled: a pick still renders and the
    /// host's stale parameter never overwrites it; the whole value is reported through
    /// <see cref="SelectionChanged"/> either way.
    /// </summary>
    [Parameter] public AiModelSelection? Selection { get; set; }

    /// <summary>Fires with the whole new selection: a picked model (reasoning options at the model's
    /// defaults), a toggled extended thinking or a chosen effort level.</summary>
    [Parameter] public EventCallback<AiModelSelection?> SelectionChanged { get; set; }

    /// <summary>
    /// How the list is grouped. Default <see cref="AiModelGroupBy.Provider"/>; a model that does not report
    /// the property lands in the localized "Other" group and is never dropped.
    /// </summary>
    [Parameter] public AiModelGroupBy GroupBy { get; set; } = AiModelGroupBy.Provider;

    /// <summary>Whether the search field is rendered. Default <c>true</c>. Without it the listbox is the
    /// popup's tab stop and offers type-ahead instead.</summary>
    [Parameter] public bool ShowSearch { get; set; } = true;

    /// <summary>The placeholder of the search field. Null uses the localized default.</summary>
    [Parameter] public string? SearchPlaceholder { get; set; }

    /// <summary>Whether a row renders the model's capability chips. Default <c>true</c>.</summary>
    [Parameter] public bool ShowCapabilities { get; set; } = true;

    /// <summary>Whether a row renders its speed bar and value. Default <c>true</c>; a model that reports no
    /// score renders neither.</summary>
    [Parameter] public bool ShowSpeed { get; set; } = true;

    /// <summary>Whether a row renders its quality bar and value. Default <c>true</c>; a model that reports no
    /// score renders neither.</summary>
    [Parameter] public bool ShowQuality { get; set; } = true;

    /// <summary>Whether a row renders its input price bar and value. Default <c>true</c>; an unreported price
    /// renders neither (an unknown price is never painted as free).</summary>
    [Parameter] public bool ShowPrice { get; set; } = true;

    /// <summary>Whether a row renders its context window chip. Default <c>true</c>; an unreported window
    /// renders no chip.</summary>
    [Parameter] public bool ShowContextWindow { get; set; } = true;

    /// <summary>Whether the selected model's extended-thinking switch is offered when the model supports it.
    /// Default <c>true</c>.</summary>
    [Parameter] public bool ShowExtendedThinking { get; set; } = true;

    /// <summary>Whether the selected model's reasoning-effort control is offered when the model reports
    /// levels. Default <c>true</c>.</summary>
    [Parameter] public bool ShowEffort { get; set; } = true;

    /// <summary>
    /// How many prompt tokens the next request is expected to send. When set (and the selected model reports
    /// an input price) the panel renders the estimated prompt cost, formatted with the culture's currency.
    /// Null renders no estimate.
    /// </summary>
    [Parameter] public int? EstimatedPromptTokens { get; set; }

    /// <summary>
    /// Whether a pick closes the popup. Default <c>true</c> (the combobox behaviour). <c>false</c> keeps the
    /// list open so the reader can compare models; the picked row is then marked as selected in place.
    /// </summary>
    [Parameter] public bool CloseOnSelect { get; set; } = true;

    /// <summary>Disables the trigger and every control of the popup: nothing opens and nothing is raised.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// The culture that formats the prices, the estimate and the speed/quality numbers. Null (the default)
    /// means the current culture at render time.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Formats a money amount — a price per million tokens and the prompt estimate. Null uses
    /// <c>AiCostFormatter</c> with <see cref="Culture"/> (the culture's own currency and symbol position). A
    /// host with credits or a fixed exchange rate overrides it.
    /// </summary>
    [Parameter] public Func<decimal, string>? PriceFormatter { get; set; }

    /// <summary>
    /// The visible heading of the popup (and of the bottom sheet, when the popup presents as one). Null uses
    /// the localized "Choose a model". The listbox is named separately by the localized "Models" label, so
    /// the title is not spoken twice.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered inside the popup. Default <c>true</c>; the listbox keeps its
    /// name either way.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// An optional accessible name for the trigger. Null (the default) lets the trigger's own visible text
    /// name it — the localized "Model:" prefix plus the model, its provider and every state tag — and the
    /// root group is named by the localized "Model". Setting it replaces that name; the state tags are then
    /// exposed through the trigger's description, so a deprecated or reasoning model is still announced.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// The layout the popup presentation resolves from. <see cref="TmLayoutMode.Auto"/> follows the viewport;
    /// a forced value never measures, so a test can render a mode without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// How the popup presents on a narrow viewport. Default <see cref="PanelPresentation.Auto"/> — the
    /// anchored popover on a wide layout and a bottom sheet on a phone.
    /// </summary>
    [Parameter] public PanelPresentation MobilePresentation { get; set; } = PanelPresentation.Auto;

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived names ────────────────────────────────────────────────────────

    private string InstructionsId => $"{_id}-instructions";

    private string TagsId => $"{_id}-trigger-tags";

    private string ListId => $"{_id}-list";

    private string OptionsTitleId => $"{_id}-options-title";

    private string PanelTitle => string.IsNullOrWhiteSpace(Title) ? Loc["TmModelPicker_Title"] : Title!;

    /// <summary>
    /// The in-body heading. A bottom sheet already carries <see cref="PanelTitle"/> in its own header, so the
    /// body does not repeat it; the POPOVER needs the heading because nothing else names the card.
    /// </summary>
    private bool ShowBodyTitle => ShowTitle && !(_panel?.IsSheet ?? false);

    private string GroupBySlug => GroupBy switch
    {
        AiModelGroupBy.Provider => "provider",
        AiModelGroupBy.Family => "family",
        _ => "none",
    };

    /// <summary>
    /// The host's own accessible name, or null. Null is the default and leaves the trigger named by its OWN
    /// visible text — the model, its provider and every state tag — so nothing visible is hidden from a screen
    /// reader. A host-supplied name replaces that text, so the state tags move into the description instead.
    /// </summary>
    private string? HostAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? null : AriaLabel;

    /// <summary>
    /// What describes the trigger: the keyboard instructions, plus the state tags when a host-supplied name
    /// hides them.
    /// </summary>
    private string TriggerDescriptionIds => string.IsNullOrWhiteSpace(AriaLabel) || TriggerTags.Count == 0
        ? InstructionsId
        : $"{InstructionsId} {TagsId}";

    private string SheetFocusSelector => ShowSearch
        ? ".tm-model-picker__search-input"
        : ".tm-model-picker__list";

    private string? ActiveOptionId => _activeIndex >= 0 && _activeIndex < _visible.Count
        ? OptionId(_activeIndex)
        : null;

    private string AnnouncementText => _announcementMarker ? _announcement + "\u200B" : _announcement;

    private AiModelInfo? SelectedModel => _selection is null
        ? null
        : (Models ?? []).FirstOrDefault(model => string.Equals(model.Id, _selection.ModelId, StringComparison.Ordinal));

    private bool SelectedExtendedThinking => _selection?.ExtendedThinking ?? false;

    private string? SelectedEffort => _selection?.EffortLevel;

    private bool ShowReasoningOptions =>
        SelectedModel is { } model
        && ((ShowExtendedThinking && model.SupportsExtendedThinking) || (ShowEffort && EffortOptions.Count > 0));

    private IReadOnlyList<SegmentedOption<string?>> EffortOptions
    {
        get
        {
            if (SelectedModel is not { } model || !model.HasEffortLevels)
            {
                return [];
            }

            var options = new List<SegmentedOption<string?>>(4)
            {
                new() { Label = Loc["TmModelPicker_EffortDefault"], Value = null },
            };

            foreach (var level in model.EffortLevels ?? [])
            {
                if (string.IsNullOrWhiteSpace(level.Id))
                {
                    continue;
                }

                options.Add(new SegmentedOption<string?> { Label = level.Label, Value = level.Id });
            }

            return options;
        }
    }

    private string EmptyText => _visible.Count > 0
        ? string.Empty
        : string.IsNullOrWhiteSpace(_searchText)
            ? Loc["TmModelPicker_NoModels"]
            : Loc["TmModelPicker_NoResults", _searchText.Trim()];

    private string? Estimate => SelectedModel is { InputPricePerMillion: { } price } model
        && EstimatedPromptTokens is { } tokens and > 0
            ? Money(price * tokens / 1_000_000m)
            : null;

    private IReadOnlyList<TriggerTag> TriggerTags
    {
        get
        {
            var tags = new List<TriggerTag>(3);
            if (SelectedModel is not { } model)
            {
                return tags;
            }

            if (_selection?.ExtendedThinking == true)
            {
                tags.Add(new TriggerTag(Loc["TmModelPicker_ExtendedThinking"], false));
            }

            if (!string.IsNullOrWhiteSpace(_selection?.EffortLevel))
            {
                var label = model.EffortLevels?
                    .FirstOrDefault(level => string.Equals(level.Id, _selection.EffortLevel, StringComparison.Ordinal))
                    ?.Label ?? _selection.EffortLevel!;
                tags.Add(new TriggerTag(label, false));
            }

            if (model.IsDeprecated)
            {
                tags.Add(new TriggerTag(Loc["TmModelPicker_Deprecated"], true));
            }

            return tags;
        }
    }

    private IFormatProvider FormatProvider => Culture ?? CultureInfo.CurrentCulture;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // The clock is optional: the type-ahead buffer needs one to decide whether two keystrokes belong to
        // the same search, and a host (or a test) may register a fake one. Nothing else ticks here.
        _clock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!Equals(Selection, _lastSelectionParam))
        {
            _selection = Selection;
            _lastSelectionParam = Selection;
        }

        Rebuild();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusPanelPending)
        {
            _focusPanelPending = false;
            try
            {
                if (ShowSearch)
                {
                    await _searchRef.FocusAsync();
                }
                else
                {
                    await _listRef.FocusAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // Best effort: the element may be gone (a prerender, a fast close). Never throw from focus.
            }
        }

        if (_focusTriggerAfterClose)
        {
            _focusTriggerAfterClose = false;
            try
            {
                await _triggerRef.FocusAsync(preventScroll: true);
            }
            catch (InvalidOperationException)
            {
                // Same best-effort contract as above.
            }
        }
    }

    // ── Open / close ─────────────────────────────────────────────────────────

    /// <summary>
    /// The trigger's pointer press. A REAL activation always starts here, which is what tells the
    /// activation guard below that a click is the reader's own and not the browser's second delivery of the
    /// key press that closed the popup.
    /// </summary>
    private void OnTriggerPointerDown() => _pointerPressed = true;

    /// <summary>
    /// The trigger's activation. A native button owns Enter and Space, so this is the only path — and it is
    /// the path that receives the browser's EXTRA click: moving the focus back to the trigger while the
    /// pick's <c>Enter</c> keydown is still being dispatched makes the browser deliver that keydown's default
    /// action to the newly focused button, which re-opened the popup the key had just closed. A synthesized
    /// activation (no pointer press of its own) therefore only counts once the guard expires; a pointer
    /// activation is never guarded.
    /// </summary>
    private void OnTriggerClick()
    {
        if (Disabled)
        {
            return;
        }

        var pointer = _pointerPressed;
        _pointerPressed = false;

        if (!pointer && _clock.GetUtcNow() < _activationBlockedUntil)
        {
            return;
        }

        if (_isOpen)
        {
            _isOpen = false;
            return;
        }

        Open();
    }

    private void Open()
    {
        _isOpen = true;
        _searchText = string.Empty;
        _typeaheadBuffer = string.Empty;
        Rebuild();
        _activeIndex = SelectedIndex();
    }

    /// <summary>
    /// Opens the popup from the trigger's arrow keys. The trigger is a native button, so Enter and Space are
    /// never emulated here — only the keys the button does not own.
    /// </summary>
    private Task OnTriggerKeyDownAsync(KeyboardEventArgs e)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        if (!_isOpen && e.Key is "ArrowDown" or "ArrowUp")
        {
            Open();
        }

        return Task.CompletedTask;
    }

    /// <summary>Raised by the overlay panel: a dismissal gesture (Escape, an outside pointer) or the panel
    /// closing itself. The overlay owns the focus return on that path, so no second focus move happens
    /// here.</summary>
    private Task OnOpenChangedAsync(bool open)
    {
        _isOpen = open;
        if (!open)
        {
            _typeaheadBuffer = string.Empty;
        }

        return Task.CompletedTask;
    }

    /// <summary>Arms the focus move once the panel is actually displayed (overlay.js has run
    /// <c>showPopover()</c>), exactly as the other popup components do — a move before that lands on a
    /// hidden element and is dropped.</summary>
    private void OnPanelOpened() => _focusPanelPending = true;

    /// <summary>The panel resolved another presentation (popover vs sheet): the body heading follows it.</summary>
    private void OnPanelLayoutResolved(TmLayoutMode mode) => _ = InvokeAsync(StateHasChanged);

    private Task CloseAsync(bool fromKeyboard = false)
    {
        _isOpen = false;
        _focusTriggerAfterClose = true;
        if (fromKeyboard)
        {
            // The pick's own keydown is still in flight: the focus move below happens inside it, so the
            // browser will deliver that keydown's activation to this (now focused) trigger. Guard the
            // window; a pointer press clears it immediately.
            _activationBlockedUntil = _clock.GetUtcNow() + ActivationBlockWindow;
        }

        return Task.CompletedTask;
    }

    // ── Search ───────────────────────────────────────────────────────────────

    private Task OnSearchInputAsync(ChangeEventArgs e)
    {
        _searchText = e.Value?.ToString() ?? string.Empty;
        _typeaheadBuffer = string.Empty;
        Rebuild();
        _activeIndex = _visible.Count > 0 ? 0 : -1;
        Announce(Loc["TmModelPicker_SearchMatchCount", _visible.Count.ToString(FormatProvider)]);
        return Task.CompletedTask;
    }

    private async Task OnSearchKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown":
                MoveActive(1);
                break;
            case "ArrowUp":
                MoveActive(-1);
                break;
            case "Enter":
                await SelectActiveAsync();
                break;
        }
    }

    private void MoveActive(int delta)
    {
        if (_visible.Count == 0)
        {
            _activeIndex = -1;
            return;
        }

        _activeIndex = _activeIndex < 0
            ? (delta > 0 ? 0 : _visible.Count - 1)
            : Math.Clamp(_activeIndex + delta, 0, _visible.Count - 1);
    }

    // ── List keyboard ────────────────────────────────────────────────────────

    /// <summary>
    /// The listbox's own keys, used only when <see cref="ShowSearch"/> is off (otherwise the search field is
    /// the combobox). Enter is emulated on keydown and Space on keyup, the native split, because the listbox
    /// is a non-native focusable. Escape is NOT handled here: the overlay consumes it at the window capture
    /// phase.
    /// </summary>
    private async Task OnListKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.CtrlKey || e.AltKey || e.MetaKey)
        {
            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                MoveActive(1);
                return;
            case "ArrowUp":
                MoveActive(-1);
                return;
            case "Home":
                if (_visible.Count > 0)
                {
                    _activeIndex = 0;
                }

                return;
            case "End":
                if (_visible.Count > 0)
                {
                    _activeIndex = _visible.Count - 1;
                }

                return;
            case "Enter":
                if (!e.Repeat)
                {
                    await SelectActiveAsync();
                }

                return;
        }

        Typeahead(e);
    }

    /// <inheritdoc />
    private async Task OnListKeyUpAsync(KeyboardEventArgs e)
    {
        if (e.Key == " ")
        {
            await SelectActiveAsync();
        }
    }

    /// <summary>
    /// APG type-ahead: printable characters build a prefix, the buffer resets after
    /// <see cref="TypeaheadWindow"/>, and a buffer that names nothing falls back to its last character
    /// searching AFTER the active row — which is how repeated presses of the same letter cycle.
    /// </summary>
    private void Typeahead(KeyboardEventArgs e)
    {
        if (e.Key.Length != 1 || char.IsControl(e.Key[0]) || char.IsWhiteSpace(e.Key[0]))
        {
            return;
        }

        var now = _clock.GetUtcNow();
        if (now - _typeaheadAt > TypeaheadWindow)
        {
            _typeaheadBuffer = string.Empty;
        }

        _typeaheadAt = now;
        _typeaheadBuffer += e.Key;

        var match = FindByPrefix(_typeaheadBuffer, searchAfterActive: false);
        if (match < 0 && _typeaheadBuffer.Length > 1)
        {
            _typeaheadBuffer = _typeaheadBuffer[^1..];
            match = FindByPrefix(_typeaheadBuffer, searchAfterActive: true);
        }

        if (match >= 0)
        {
            _activeIndex = match;
        }
    }

    private int FindByPrefix(string prefix, bool searchAfterActive)
    {
        if (_visible.Count == 0)
        {
            return -1;
        }

        var start = searchAfterActive && _activeIndex >= 0
            ? (_activeIndex + 1) % _visible.Count
            : Math.Max(_activeIndex, 0);

        for (var step = 0; step < _visible.Count; step++)
        {
            var index = (start + step) % _visible.Count;
            if (PrefixMatches(_visible[index], prefix))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool PrefixMatches(AiModelInfo model, string prefix)
        => CultureInfo.InvariantCulture.CompareInfo.IsPrefix(
            model.Name,
            prefix,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    // ── Selection ────────────────────────────────────────────────────────────

    private async Task SelectActiveAsync()
    {
        if (_activeIndex >= 0 && _activeIndex < _visible.Count)
        {
            await SelectAsync(_visible[_activeIndex], fromKeyboard: true);
        }
    }

    private async Task SelectAsync(AiModelInfo model, bool fromKeyboard = false)
    {
        if (Disabled)
        {
            return;
        }

        // Re-picking the same model is not a reset: the reader's reasoning options survive a second look at
        // the same model. A different model starts from that model's defaults.
        var next = _selection is { } current && string.Equals(current.ModelId, model.Id, StringComparison.Ordinal)
            ? current
            : new AiModelSelection(model.Id);

        await ApplyAsync(next, Loc["TmModelPicker_Selected", model.Name]);

        if (CloseOnSelect)
        {
            await CloseAsync(fromKeyboard);
        }
    }

    private async Task OnExtendedThinkingChangedAsync(bool value)
    {
        if (Disabled || _selection is null)
        {
            return;
        }

        await ApplyAsync(
            _selection with { ExtendedThinking = value },
            Loc[value ? "TmModelPicker_ExtendedThinkingOn" : "TmModelPicker_ExtendedThinkingOff"]);
    }

    private async Task OnEffortChangedAsync(string? value)
    {
        if (Disabled || _selection is null)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(value)
            ? null
            : SelectedModel?.EffortLevels?
                .FirstOrDefault(level => string.Equals(level.Id, value, StringComparison.Ordinal))?.Label ?? value;

        var announcement = label is null
            ? Loc["TmModelPicker_EffortDefaultSet"]
            : Loc["TmModelPicker_EffortSet", label];

        await ApplyAsync(_selection with { EffortLevel = string.IsNullOrWhiteSpace(value) ? null : value }, announcement);
    }

    private async Task ApplyAsync(AiModelSelection next, string announcement)
    {
        _selection = next;
        _typeaheadBuffer = string.Empty;
        Announce(announcement);
        await SelectionChanged.InvokeAsync(next);
    }

    private int SelectedIndex()
    {
        if (_selection is { } selection)
        {
            for (var index = 0; index < _visible.Count; index++)
            {
                if (string.Equals(_visible[index].Id, selection.ModelId, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return _visible.Count > 0 ? 0 : -1;
    }

    private bool IsSelected(AiModelInfo model)
        => _selection is { } selection && string.Equals(selection.ModelId, model.Id, StringComparison.Ordinal);

    // ── Grouping ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var query = _searchText.Trim();
        var models = Models ?? [];
        var filtered = query.Length == 0
            ? models.ToList()
            : models.Where(model => Matches(model, query)).ToList();

        _visible.Clear();
        _visible.AddRange(filtered);
        _groups.Clear();

        if (GroupBy == AiModelGroupBy.None)
        {
            _groups.Add(new ModelGroup(null, null, filtered));
        }
        else
        {
            // Group order follows the FIRST appearance of each key in the host's list, so the catalogue's own
            // ordering survives, and a model that reports nothing lands in the localized "Other" group.
            var order = new List<string?>();
            var buckets = new Dictionary<string, List<AiModelInfo>>(StringComparer.Ordinal);

            foreach (var model in filtered)
            {
                var key = GroupKey(model);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    buckets[key] = bucket;
                    order.Add(key);
                }

                bucket.Add(model);
            }

            var groupIndex = 0;
            foreach (var key in order)
            {
                var title = string.IsNullOrWhiteSpace(key) ? Loc["TmModelPicker_OtherGroup"] : key!;
                _groups.Add(new ModelGroup(title, $"{_id}-grp-{groupIndex++}", buckets[key]));
            }
        }

        if (_activeIndex >= _visible.Count)
        {
            _activeIndex = _visible.Count - 1;
        }
    }

    private string GroupKey(AiModelInfo model)
    {
        var value = GroupBy == AiModelGroupBy.Provider ? model.Provider : model.Family;
        return value?.Trim() ?? string.Empty;
    }

    private static bool Matches(AiModelInfo model, string query)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        if (compare.IndexOf(model.Name, query, options) >= 0
            || compare.IndexOf(model.Provider ?? string.Empty, query, options) >= 0
            || compare.IndexOf(model.Family ?? string.Empty, query, options) >= 0
            || compare.IndexOf(model.Description ?? string.Empty, query, options) >= 0)
        {
            return true;
        }

        foreach (var capability in model.Capabilities ?? [])
        {
            if (compare.IndexOf(capability ?? string.Empty, query, options) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // ── Presentation helpers ─────────────────────────────────────────────────

    private string OptionId(int index) => $"{_id}-opt-{index}";

    private string SelectedText(AiModelInfo model) => IsSelected(model).ToString().ToLowerInvariant();

    /// <summary>
    /// The option's classes. The names are built here rather than with inline Razor conditionals, so the
    /// markup stays readable and the stylesheet gate can find every emitted class as a string literal.
    /// </summary>
    private string OptionClass(AiModelInfo model, int index)
    {
        var classes = new System.Text.StringBuilder("tm-model-picker__option");
        if (IsSelected(model))
        {
            classes.Append(" tm-model-picker__option--selected");
        }

        if (index == _activeIndex)
        {
            classes.Append(" tm-model-picker__option--active");
        }

        if (model.IsDeprecated)
        {
            classes.Append(" tm-model-picker__option--deprecated");
        }

        return classes.ToString();
    }

    private IReadOnlyList<string> Capabilities(AiModelInfo model)
        => (model.Capabilities ?? [])
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .ToList();

    private string ScoreText(double score)
        => Math.Clamp(score, 0d, 5d).ToString("0.#", FormatProvider);

    private static double ScoreFraction(double score) => Math.Clamp(score, 0d, 5d) / 5d * 100d;

    private double PriceFraction(decimal price)
    {
        var maximum = (Models ?? [])
            .Select(model => model.InputPricePerMillion)
            .Where(value => value is > 0m)
            .Select(value => value!.Value)
            .DefaultIfEmpty(0m)
            .Max();

        if (maximum <= 0m || price <= 0m)
        {
            return 0d;
        }

        return (double)(price / maximum) * 100d;
    }

    private string BarStyle(double percent)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{BarVariable}: {Math.Clamp(percent, 0d, 100d):0.#}%");

    private string Money(decimal amount)
        => PriceFormatter?.Invoke(amount) ?? AiCostFormatter.Format(amount, FormatProvider);

    /// <summary>
    /// A short context-window label ("200k", "1M"). The culture's decimal separator is honoured, the suffix
    /// is the token-count convention rather than a word, and the row's chip captions it ("200k context").
    /// </summary>
    private string ShortTokens(int tokens)
    {
        if (tokens >= 1_000_000)
        {
            return (tokens / 1_000_000d).ToString("0.#", FormatProvider) + "M";
        }

        if (tokens >= 1_000)
        {
            return (tokens / 1_000d).ToString("0.#", FormatProvider) + "k";
        }

        return tokens.ToString(FormatProvider);
    }

    private void Announce(string text)
    {
        // A live region announces a MUTATION: writing the same sentence twice in a row would be silent, so a
        // repeated wording toggles one invisible marker character. A different wording clears it, which keeps
        // the region's text exactly what a reader hears.
        text ??= string.Empty;
        _announcementMarker = text.Length > 0 && string.Equals(text, _announcement, StringComparison.Ordinal)
            ? !_announcementMarker
            : false;
        _announcement = text;
    }

    /// <summary>One rendered group: its header (null for an ungrouped list), the header's DOM id and its
    /// models in reading order.</summary>
    private sealed record ModelGroup(string? Title, string? Id, IReadOnlyList<AiModelInfo> Models);

    /// <summary>One tag of the trigger: the word and whether it is the deprecated marker.</summary>
    private readonly record struct TriggerTag(string Text, bool IsDeprecated);
}
