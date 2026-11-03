using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Filters;

/// <summary>
/// The logic half of <see cref="TmFacetFilter"/>: the selection state, the per-group UI state
/// (collapsed, search text, "show all") and the layout the presentation resolves from. The render is
/// a pure projection of that state, so every user gesture goes through one of the private handlers
/// below and every handler raises the same immutable snapshot through
/// <see cref="SelectedChanged"/>.
/// <para>
/// <b>Selection contract.</b> The component owns a mutable internal copy of the selection while the
/// user works and hands the host an IMMUTABLE snapshot on every change. The host's
/// <see cref="Selected"/> parameter is an INPUT: it is adopted when the host supplies a new value
/// (a bound <c>@bind-Selected</c> feeds the snapshot straight back, which is detected as equal and
/// ignored), and an unchanged parameter never undoes what the reader picked.
/// </para>
/// <para>
/// <b>No live region duplicates a native announcement.</b> A checkbox's state change is announced by
/// the browser through <c>aria-checked</c> and the disclosure's count through the button's own
/// accessible name, so neither is repeated here. The ONE polite region announces the RESULT COUNT
/// when the host changes <see cref="ResultCount"/> — that number has no other announcement path.
/// </para>
/// <para>
/// <b>Focus handoff.</b> A gesture whose own control leaves the DOM (a chip removal, a clear-all, a
/// "show more") records where focus must go and <c>OnAfterRenderAsync</c> applies it exactly once,
/// so focus never falls to the page body (WCAG 2.4.3). Nothing else moves focus.
/// </para>
/// </summary>
public partial class TmFacetFilter : TmComponentBase, IAsyncDisposable
{
    /// <summary>The shared overlay module — the focus handoff borrows its <c>focusById</c> export.</summary>
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";

    /// <summary>The internal, mutable selection (group key → selected values).</summary>
    private readonly Dictionary<string, HashSet<string>> _selection = new(StringComparer.Ordinal);

    /// <summary>The user's disclosure state per group key; seeded from the model's initial state.</summary>
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);

    /// <summary>The live search text per group key (kept across an overlay close — the reader returns to the same list).</summary>
    private readonly Dictionary<string, string> _searches = new(StringComparer.Ordinal);

    /// <summary>Groups the reader asked to see in full; the "show more" truncation does not re-apply to them.</summary>
    private readonly HashSet<string> _showAll = new(StringComparer.Ordinal);

    private readonly OverlayLayout _layout = new();

    /// <summary>
    /// The ONE literal prefix every DOM id of this instance is built from. Ids are built from the
    /// POSITION (instance prefix + group index + option index), never from a slug of a host value: a
    /// slug is lossy, so a label's <c>for</c>/<c>aria-controls</c> could point at a neighbour.
    /// </summary>
    private readonly string _idPrefix = $"tm-facet-filter-{Guid.NewGuid():n}";

    /// <summary>The groups actually rendered this render — non-null with a non-blank key, in host order.</summary>
    private List<FacetGroup> _groups = [];

    /// <summary>The <see cref="Selected"/> reference the host supplied last, used to tell a host change from a re-render.</summary>
    private IReadOnlyDictionary<string, IReadOnlySet<string>>? _lastHostSelection;

    /// <summary>Whether any parameter set has been applied (the first one seeds, it is not a change).</summary>
    private bool _sawParameters;

    /// <summary>The open state of the popover or the sheet.</summary>
    private bool _open;

    private ElementReference _triggerRef;

    /// <summary>The last result count the live region has reported.</summary>
    private int? _lastResultCount;

    /// <summary>Whether a parameter set has supplied a result count yet (the first one seeds, it is not a change).</summary>
    private bool _sawResultCount;

    /// <summary>The text of the single polite live region (empty until the host changes the count).</summary>
    private string _liveMessage = string.Empty;

    /// <summary>Keys the live region's child so the same wording is re-announced as an insertion.</summary>
    private int _liveSequence;

    /// <summary>The element the next render must hand focus to after a gesture removed the focused one.</summary>
    private FacetFocusTarget _focusTarget;

    /// <summary>The index the pending focus addresses — a chip, or an option within its group.</summary>
    private int _focusIndex;

    /// <summary>The group key the pending option focus belongs to.</summary>
    private string? _focusGroupKey;

    /// <summary>The lazily imported overlay module (imported on the first focus handoff that needs it).</summary>
    private LazyJsModule? _overlayModule;

    // ---------- parameters ----------

    /// <summary>
    /// The groups of the filter, in the order they are rendered. A group with a blank
    /// <see cref="FacetGroup.Key"/> is skipped — its options could not be reported back to the host.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<FacetGroup> Groups { get; set; } = [];

    /// <summary>
    /// The current selection, keyed by <see cref="FacetGroup.Key"/>. Two-way bound with
    /// <see cref="SelectedChanged"/>; the supported usage is <c>@bind-Selected</c>. Keys and values
    /// are matched ORDINALLY. A group with no selected value is omitted from the snapshots the
    /// component produces (a selection never ships empty sets), while a host-supplied key that
    /// matches no group is preserved untouched — the component does not destroy data it cannot
    /// interpret (it renders such a value as a removable chip labelled with the raw value).
    /// <para>
    /// <b>Forcing a reset.</b> A re-render that re-supplies the same reference never undoes what the
    /// reader picked, because the component cannot tell a refused change from an ordinary re-render.
    /// A host that rejects a change — a validation guard, or a round-trip that kept the old filter —
    /// must therefore pass a NEW dictionary instance holding the selection it wants rendered.
    /// </para>
    /// </summary>
    [Parameter]
    public IReadOnlyDictionary<string, IReadOnlySet<string>>? Selected { get; set; }

    /// <summary>
    /// Fires with an IMMUTABLE snapshot of the whole selection after every change — a toggle, a chip
    /// removal, a clear-all. The snapshot is a fresh dictionary of fresh sets, so a host that keeps
    /// the previous value never sees it mutate.
    /// </summary>
    [Parameter]
    public EventCallback<IReadOnlyDictionary<string, IReadOnlySet<string>>> SelectedChanged { get; set; }

    /// <summary>
    /// How the groups present. Default <see cref="FacetFilterPresentation.Popover"/>: a trigger plus
    /// an anchored dialog popover on a wide layout and a bottom sheet on a mobile one.
    /// <see cref="FacetFilterPresentation.Inline"/> renders the groups in the page flow with no
    /// trigger, no overlay and therefore no sheet at any width.
    /// </summary>
    [Parameter]
    public FacetFilterPresentation Presentation { get; set; } = FacetFilterPresentation.Popover;

    /// <summary>
    /// The number of records the current selection leaves, when the host knows it. Supplying it turns
    /// the confirmation action into the primary "Show {n} results" button and makes the single polite
    /// live region announce the number when it changes. <see langword="null"/> (the default) keeps
    /// the neutral "Show results" label and nothing is announced — the host opted out of that flow.
    /// </summary>
    [Parameter]
    public int? ResultCount { get; set; }

    /// <summary>
    /// Fires when the reader confirms the filters with the primary action (the footer button of the
    /// popover or the sheet). The component closes the overlay first, so a host that reloads its data
    /// here never fights an open surface. Filters otherwise apply on every toggle — the confirmation
    /// is a dismissal, not the only way to apply a filter.
    /// </summary>
    [Parameter]
    public EventCallback OnApply { get; set; }

    /// <summary>
    /// The visible label of the trigger button. Null means the localized default ("Filters"). It is
    /// also the head of the trigger's accessible name once something is selected
    /// ("Filters (selected: 2)").
    /// </summary>
    [Parameter]
    public string? TriggerLabel { get; set; }

    /// <summary>
    /// The heading of the filter surface: the popover's visible title (which also NAMES the dialog),
    /// the sheet's header title and the inline heading. Null means the localized default ("Filters").
    /// </summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>
    /// How many options turn a group's own search box on, when the group does not override it. The
    /// box appears at <c>options.Count &gt;= SearchThreshold</c>. Default 8; a value of <c>0</c> or
    /// less disables the search box everywhere (a short list does not need one).
    /// </summary>
    [Parameter]
    public int SearchThreshold { get; set; } = 8;

    /// <summary>
    /// How many options a group renders before its "Show {n} more" button appears, when the group
    /// does not override it. Default 6; a value of <c>0</c> or less renders every option.
    /// </summary>
    [Parameter]
    public int MaxVisible { get; set; } = 6;

    /// <summary>
    /// Renders the result counts — next to the option label and inside its accessible name. Default
    /// <see langword="true"/>. A host whose counts are unknown leaves the option's
    /// <see cref="FacetOption.Count"/> null instead; this switch is for a host that has the counts but
    /// does not want them shown.
    /// </summary>
    [Parameter]
    public bool ShowCounts { get; set; } = true;

    /// <summary>
    /// Renders the active filters as removable chips below the trigger (or below the inline groups).
    /// Default <see langword="true"/>; the chips are the only place a value can be removed without
    /// opening the surface, so turning them off costs the reader that shortcut.
    /// </summary>
    [Parameter]
    public bool ShowActiveFilters { get; set; } = true;

    /// <summary>
    /// Renders the clear-all action — once in the chip row of the page and once in the footer of the
    /// open surface (the modal surface makes the page one unreachable, so each presentation needs its
    /// own). Default <see langword="true"/>.
    /// </summary>
    [Parameter]
    public bool ShowClearAll { get; set; } = true;

    /// <summary>
    /// The culture that formats the result counts. Null (the default) means the current culture at
    /// render time, so the counts follow the request culture without being told. It formats numbers
    /// only — the grammatical plural forms come from the localizer's UI culture.
    /// </summary>
    [Parameter]
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The layout the presentation resolves from. <see cref="TmLayoutMode.Auto"/> follows the
    /// viewport scope; a forced value never measures, so a test or a host can pin a phone
    /// presentation without a DOM.
    /// </summary>
    [Parameter]
    public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The layout rendered before the first measurement. Desktop unless the host says otherwise.</summary>
    [Parameter]
    public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>
    /// Raised when the resolved layout changes. The initial value is not a change. Never pair
    /// <see cref="LayoutMode"/> with this callback: it reports the resolved mode, and a two-way bind
    /// would pin that mode.
    /// </summary>
    [Parameter]
    public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>The viewport's layout — the phone band is measured from it when no mode is forced.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)]
    public TmLayoutContext? Viewport { get; set; }

    [Inject]
    private ILogger<OverlayLayout> LayoutLogger { get; set; } = default!;

    /// <summary>The runtime the focus handoff borrows the shared overlay module from.</summary>
    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    // ---------- derived state ----------

    /// <summary>The layout the presentation resolves from.</summary>
    private TmLayoutContext Context => _layout.Resolve(LayoutMode, Viewport, InitialMode);

    /// <summary>Whether the popover presents as the mobile bottom sheet instead.</summary>
    private bool UseSheet => Presentation == FacetFilterPresentation.Popover && Context.IsMobile;

    /// <summary>The layout rendered before a measurement, and the mode the probe fills in.</summary>
    private bool NeedsProbe => Presentation == FacetFilterPresentation.Popover
        && Viewport is null
        && LayoutMode == TmLayoutMode.Auto;

    /// <summary>The culture the counts are formatted with.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>The root classes: the base class plus the host's own.</summary>
    private string RootClass => string.IsNullOrEmpty(Class) ? "tm-facet-filter" : "tm-facet-filter " + Class;

    /// <summary>The presentation modifier the markup and the E2E lane key off.</summary>
    private string PresentationCss => Presentation == FacetFilterPresentation.Inline ? "inline" : "popover";

    /// <summary>The visible trigger label — the host's override or the localized default.</summary>
    private string ResolvedTriggerLabel
        => string.IsNullOrWhiteSpace(TriggerLabel) ? Loc["TmFacetFilter_Trigger"] : TriggerLabel!;

    /// <summary>The visible title of the surface — the host's override or the localized default.</summary>
    private string ResolvedTitle
        => string.IsNullOrWhiteSpace(Title) ? Loc["TmFacetFilter_Title"] : Title!;

    /// <summary>The DOM id of the popover's heading (the dialog's accessible name).</summary>
    private string PanelTitleId => _idPrefix + "-title";

    /// <summary>The DOM id of the popover panel (the trigger's <c>aria-controls</c> target).</summary>
    private string PanelId => _idPrefix + "-panel";

    /// <summary>The DOM id of the inline heading.</summary>
    private string InlineTitleId => _idPrefix + "-inline-title";

    /// <summary>The total number of selected values across every group (including host keys not in the model).</summary>
    private int ActiveCount
    {
        get
        {
            var count = 0;
            foreach (var values in _selection.Values)
            {
                count += values.Count;
            }

            return count;
        }
    }

    /// <summary>Whether anything at all is selected.</summary>
    private bool HasSelection => ActiveCount > 0;

    /// <summary>The trigger's accessible name — only while a count is worth announcing.</summary>
    private string? TriggerAriaLabel
        => ActiveCount > 0 ? Loc["TmFacetFilter_TriggerActive", ResolvedTriggerLabel, ActiveCount] : null;

    /// <summary>The label of the primary action: the counted form when the host knows the number.</summary>
    private string ConfirmLabel
        => ResultCount.HasValue
            ? Loc.Plural("TmFacetFilter_ShowResults", ResultCount.Value)
            : Loc["TmFacetFilter_ShowResultsPlain"];

    // ---------- ids built from position ----------

    /// <summary>The DOM id of one group's disclosure button.</summary>
    /// <param name="index">The group's position in <see cref="_groups"/>.</param>
    private string GroupToggleId(int index) => $"{_idPrefix}-group-{index}";

    /// <summary>The DOM id of one group's body (the disclosure's <c>aria-controls</c> target).</summary>
    /// <param name="index">The group's position in <see cref="_groups"/>.</param>
    private string GroupBodyId(int index) => $"{_idPrefix}-body-{index}";

    /// <summary>The DOM id of the checkbox of one option (positional: group body id + option index).</summary>
    /// <param name="groupIndex">The group's position in <see cref="_groups"/>.</param>
    /// <param name="optionIndex">The option's position in the group's visible list.</param>
    private string OptionInputId(int groupIndex, int optionIndex) => $"{GroupBodyId(groupIndex)}-opt-{optionIndex}";

    /// <summary>The DOM id of one chip's remove button (positional, so the focus handoff can address it).</summary>
    /// <param name="index">The chip's position in the rendered chip row.</param>
    private string ChipRemoveId(int index) => $"{_idPrefix}-chip-{index}";

    /// <summary>The DOM id of the surface's confirmation action — the home focus takes after a footer clear.</summary>
    private string ConfirmButtonId => _idPrefix + "-confirm";

    /// <summary>The DOM id of the trigger — the home focus takes when a page-level removal empties the chip row.</summary>
    private string TriggerId => _idPrefix + "-trigger";

    // ---------- lifecycle ----------

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        SyncGroups();
        SyncSelection();
        SyncResultCount();
        await ReportResolvedAsync();
    }

    /// <summary>
    /// Applies the focus a gesture requested, once the render that removed the focused element has
    /// landed. A target is armed by exactly one handler and cleared here, so a later
    /// parameter-driven render never moves focus on its own (WCAG 3.2.1).
    /// </summary>
    /// <param name="firstRender">Whether this is the first render of the component.</param>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var target = _focusTarget;
        if (target == FacetFocusTarget.None)
        {
            return;
        }

        _focusTarget = FacetFocusTarget.None;
        var index = _focusIndex;
        var groupKey = _focusGroupKey;
        _focusGroupKey = null;

        switch (target)
        {
            case FacetFocusTarget.Primary:
                await FocusPrimaryAsync();
                break;
            case FacetFocusTarget.ChipRemove:
                await FocusChipOrPrimaryAsync(index);
                break;
            case FacetFocusTarget.OptionInput:
                await FocusOptionInputAsync(groupKey, index);
                break;
            case FacetFocusTarget.Confirm:
                await FocusByIdAsync(ConfirmButtonId);
                break;
        }
    }

    /// <summary>
    /// The fallback home for focus when the element that held it is gone: the trigger in popover
    /// mode, and in inline mode the first group's disclosure (the heading when there are no groups —
    /// it carries <c>tabindex="-1"</c> for exactly that case).
    /// </summary>
    private async Task FocusPrimaryAsync()
    {
        if (Presentation == FacetFilterPresentation.Popover)
        {
            await FocusByIdAsync(TriggerId);
            return;
        }

        await FocusByIdAsync(_groups.Count > 0 ? GroupToggleId(0) : InlineTitleId);
    }

    /// <summary>
    /// Hands focus to the chip that took the removed one's place — the next chip, or the previous one
    /// when the removed chip was last — and to the primary control when the row is now empty.
    /// </summary>
    /// <param name="removedIndex">The position the removed chip occupied before the render.</param>
    private async Task FocusChipOrPrimaryAsync(int removedIndex)
    {
        // Rebuild the row: the ids are positional, so the chip that moved up now carries the removed
        // chip's id — which is the id the browser needs to focus.
        var remaining = BuildChips().Count;
        if (remaining == 0)
        {
            await FocusPrimaryAsync();
            return;
        }

        await FocusByIdAsync(ChipRemoveId(Math.Min(removedIndex, remaining - 1)));
    }

    /// <summary>Moves focus to the checkbox of the first option a "show more" revealed.</summary>
    /// <param name="groupKey">The group the action belongs to.</param>
    /// <param name="optionIndex">The option's new position — the truncation limit before the reveal.</param>
    private async Task FocusOptionInputAsync(string? groupKey, int optionIndex)
    {
        if (groupKey is null)
        {
            return;
        }

        var groupIndex = _groups.FindIndex(group => string.Equals(group.Key, groupKey, StringComparison.Ordinal));
        if (groupIndex < 0)
        {
            return;
        }

        // The option may be further down the surface: let the browser scroll it into view.
        await FocusByIdAsync(OptionInputId(groupIndex, optionIndex), preventScroll: false);
    }

    /// <summary>
    /// Focuses the element with the given DOM id through the shared overlay module (<c>focusById</c>).
    /// Unconditional by design: every caller arms it from an explicit gesture, so this is a focus
    /// hand-off rather than a steal. Interop failures are swallowed — there is nothing to focus once
    /// the browser or the circuit is gone, and a focus move must never break the render.
    /// </summary>
    /// <param name="id">The DOM id of the element to focus.</param>
    /// <param name="preventScroll">Whether the focus move must not scroll the page.</param>
    private async Task FocusByIdAsync(string id, bool preventScroll = true)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        _overlayModule ??= new LazyJsModule(JS, OverlayModulePath);
        await _overlayModule.TryInvokeVoidAsync("focusById", id, new { preventScroll });
    }

    /// <summary>Releases the lazily imported overlay module.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_overlayModule is not null)
        {
            await _overlayModule.DisposeAsync();
            _overlayModule = null;
        }
    }

    /// <summary>Filters the host's groups to the renderable ones and prunes the UI state of the groups that left.</summary>
    private void SyncGroups()
    {
        _groups = (Groups ?? []).Where(group => group is not null && !string.IsNullOrWhiteSpace(group.Key)).ToList();

        foreach (var group in _groups)
        {
            if (!_expanded.ContainsKey(group.Key))
            {
                _expanded[group.Key] = !group.IsCollapsed;
            }
        }

        if (_expanded.Count == _groups.Count && _groups.All(group => _expanded.ContainsKey(group.Key)))
        {
            return;
        }

        var keys = new HashSet<string>(_groups.Select(group => group.Key), StringComparer.Ordinal);
        foreach (var stale in _expanded.Keys.Where(key => !keys.Contains(key)).ToList())
        {
            _expanded.Remove(stale);
        }

        foreach (var stale in _searches.Keys.Where(key => !keys.Contains(key)).ToList())
        {
            _searches.Remove(stale);
        }

        foreach (var stale in _showAll.Where(key => !keys.Contains(key)).ToList())
        {
            _showAll.Remove(stale);
        }
    }

    /// <summary>
    /// Adopts the host's <see cref="Selected"/> when it actually changed. A re-render that re-supplies
    /// the same reference (or an identical snapshot the host fed back through
    /// <see cref="SelectedChanged"/>) keeps the user's live state.
    /// </summary>
    private void SyncSelection()
    {
        if (_sawParameters && ReferenceEquals(Selected, _lastHostSelection))
        {
            return;
        }

        _sawParameters = true;
        _lastHostSelection = Selected;

        if (SelectionEquals(Selected, _selection))
        {
            return;
        }

        _selection.Clear();
        if (Selected is null)
        {
            return;
        }

        foreach (var (key, values) in Selected)
        {
            if (string.IsNullOrEmpty(key) || values is null || values.Count == 0)
            {
                continue;
            }

            _selection[key] = new HashSet<string>(values.Where(value => !string.IsNullOrEmpty(value)), StringComparer.Ordinal);
        }
    }

    /// <summary>Reports a changed result count through the single polite live region.</summary>
    private void SyncResultCount()
    {
        if (!_sawResultCount)
        {
            _sawResultCount = true;
            _lastResultCount = ResultCount;
            return;
        }

        if (_lastResultCount == ResultCount)
        {
            return;
        }

        var hadCount = _lastResultCount.HasValue;
        _lastResultCount = ResultCount;

        if (!ResultCount.HasValue)
        {
            if (hadCount)
            {
                _liveMessage = string.Empty;
                _liveSequence++;
            }

            return;
        }

        _liveMessage = ConfirmLabel;
        _liveSequence++;
    }

    /// <summary>Compares a host selection with the live internal one, ignoring empty groups on both sides.</summary>
    private static bool SelectionEquals(
        IReadOnlyDictionary<string, IReadOnlySet<string>>? external,
        Dictionary<string, HashSet<string>> internalState)
    {
        var externalCount = external?.Count(pair => pair.Value is { Count: > 0 }) ?? 0;
        var internalCount = internalState.Count(pair => pair.Value.Count > 0);
        if (externalCount != internalCount)
        {
            return false;
        }

        if (external is null)
        {
            return internalCount == 0;
        }

        foreach (var (key, values) in external)
        {
            if (values is null || values.Count == 0)
            {
                continue;
            }

            if (!internalState.TryGetValue(key, out var mine) || mine.Count != values.Count || !values.All(mine.Contains))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The immutable snapshot handed to the host — a fresh dictionary of fresh sets, empty groups omitted.</summary>
    private IReadOnlyDictionary<string, IReadOnlySet<string>> BuildSnapshot()
    {
        var snapshot = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var (key, values) in _selection)
        {
            if (values.Count == 0)
            {
                continue;
            }

            snapshot[key] = new HashSet<string>(values, StringComparer.Ordinal);
        }

        return snapshot;
    }

    private Task ReportResolvedAsync() => _layout.ReportAsync(
        Context.Resolved, mode => ResolvedLayoutChanged.InvokeAsync(mode));

    /// <summary>
    /// The internal viewport probe, opened from C# because a Razor tag binds only to public components
    /// (an internal tag compiles to a literal element — RZ10012, an error in this repo).
    /// </summary>
    private RenderFragment Probe => builder =>
    {
        builder.OpenComponent<ViewportProbe>(0);
        builder.AddComponentParameter(1, nameof(ViewportProbe.OnMeasured),
            EventCallback.Factory.Create<TmLayoutMode>(this, OnProbed));
        builder.CloseComponent();
    };

    /// <summary>Records a probe measurement; the raised callback re-renders the component.</summary>
    private Task OnProbed(TmLayoutMode mode)
    {
        _layout.Measured(mode);
        OverlayLayout.HintOnce(LayoutLogger);
        return ReportResolvedAsync();
    }

    // ---------- group state ----------

    /// <summary>The display label of a group (the key as a fallback, so a group is never nameless).</summary>
    private static string LabelOf(FacetGroup group)
        => string.IsNullOrWhiteSpace(group.Label) ? group.Key : group.Label;

    /// <summary>The display label of an option (the value as a fallback).</summary>
    private static string LabelOf(FacetOption option)
        => string.IsNullOrWhiteSpace(option.Label) ? option.Value : option.Label;

    /// <summary>Whether the group's body is rendered.</summary>
    private bool IsExpanded(FacetGroup group)
        => !_expanded.TryGetValue(group.Key, out var expanded) || expanded;

    /// <summary>How many of the group's options are selected.</summary>
    private int SelectedCount(FacetGroup group)
        => _selection.TryGetValue(group.Key, out var values) ? values.Count : 0;

    /// <summary>Whether one option is selected.</summary>
    private bool IsSelected(FacetGroup group, FacetOption option)
        => _selection.TryGetValue(group.Key, out var values) && values.Contains(option.Value);

    /// <summary>The group's disclosure accessible name: the label, plus the selected count when there is one.</summary>
    private string? GroupAriaLabel(FacetGroup group)
    {
        var count = SelectedCount(group);
        return count > 0 ? Loc["TmFacetFilter_GroupActive", LabelOf(group), count] : null;
    }

    /// <summary>The options of a group that survive its search text, in host order.</summary>
    private List<FacetOption> FilteredOptions(FacetGroup group)
    {
        var options = (group.Options ?? [])
            .Where(option => option is not null && !string.IsNullOrEmpty(option.Value))
            .ToList();

        if (!_searches.TryGetValue(group.Key, out var term) || string.IsNullOrWhiteSpace(term))
        {
            return options;
        }

        return options.Where(option => AccentInsensitiveText.Contains(LabelOf(option), term)).ToList();
    }

    /// <summary>How many options the group renders at most (a non-positive value means "all of them").</summary>
    private int MaxVisibleOf(FacetGroup group) => group.MaxVisible ?? MaxVisible;

    /// <summary>The options actually rendered: the filtered list, truncated unless the reader asked for all.</summary>
    private List<FacetOption> VisibleOptions(FacetGroup group)
    {
        var options = FilteredOptions(group);
        var limit = MaxVisibleOf(group);
        return limit <= 0 || _showAll.Contains(group.Key) || options.Count <= limit
            ? options
            : options.Take(limit).ToList();
    }

    /// <summary>How many options the group hides behind its "show more" button.</summary>
    private int HiddenCount(FacetGroup group)
    {
        var options = FilteredOptions(group);
        var limit = MaxVisibleOf(group);
        if (limit <= 0 || _showAll.Contains(group.Key) || options.Count <= limit)
        {
            return 0;
        }

        return options.Count - limit;
    }

    /// <summary>Whether the group gets its own search box.</summary>
    private bool ShowSearch(FacetGroup group)
    {
        var threshold = group.SearchThreshold ?? SearchThreshold;
        if (threshold <= 0)
        {
            return false;
        }

        return (group.Options?.Count(option => option is not null && !string.IsNullOrEmpty(option.Value)) ?? 0) >= threshold;
    }

    /// <summary>The search box's placeholder, which is also its accessible name — it names the group so two boxes stay distinguishable.</summary>
    private string SearchPlaceholder(FacetGroup group) => Loc["TmFacetFilter_Search", LabelOf(group)];

    /// <summary>The current search text of a group.</summary>
    private string SearchText(FacetGroup group) => _searches.TryGetValue(group.Key, out var text) ? text : string.Empty;

    /// <summary>The "show more" label, in the grammatical form of the hidden count.</summary>
    private string ShowMoreLabel(FacetGroup group) => Loc.Plural("TmFacetFilter_ShowMore", HiddenCount(group));

    /// <summary>Formats an option's result count with the effective culture.</summary>
    private string FormatCount(int count) => count.ToString("N0", EffectiveCulture);

    /// <summary>
    /// The option checkbox's accessible name: the label plus the count, so the number is never a
    /// visual-only fact. The group already names the region the option lives in
    /// (<c>aria-labelledby</c> on the group body), so the group's name is not repeated here.
    /// </summary>
    private string OptionAriaLabel(FacetOption option)
    {
        var label = LabelOf(option);
        return ShowCounts && option.Count.HasValue
            ? Loc["TmFacetFilter_OptionWithCount", label, FormatCount(option.Count.Value)]
            : label;
    }

    /// <summary>
    /// The native attributes of an option's checkbox — one splat, so the input carries the explicit
    /// name and the positional id the "show more" focus handoff addresses.
    /// </summary>
    /// <param name="ariaLabel">The option's accessible name (label plus count).</param>
    /// <param name="groupIndex">The group's position in <see cref="_groups"/>.</param>
    /// <param name="optionIndex">The option's position in the group's visible list.</param>
    private Dictionary<string, object> OptionInputAttributes(string ariaLabel, int groupIndex, int optionIndex)
        => new(2, StringComparer.Ordinal)
        {
            ["aria-label"] = ariaLabel,
            ["id"] = OptionInputId(groupIndex, optionIndex),
        };

    // ---------- chips ----------

    /// <summary>The active filters, in group order, then unknown host keys.</summary>
    private List<FacetChip> BuildChips()
    {
        var chips = new List<FacetChip>();

        foreach (var group in _groups)
        {
            if (!_selection.TryGetValue(group.Key, out var values) || values.Count == 0)
            {
                continue;
            }

            var options = (group.Options ?? [])
                .Where(option => option is not null && !string.IsNullOrEmpty(option.Value))
                .ToList();

            foreach (var option in options)
            {
                if (values.Contains(option.Value))
                {
                    chips.Add(new FacetChip(group.Key, LabelOf(group), option.Value, LabelOf(option)));
                }
            }

            foreach (var orphan in values.Where(value => options.All(option => option.Value != value)).OrderBy(value => value, StringComparer.Ordinal))
            {
                chips.Add(new FacetChip(group.Key, LabelOf(group), orphan, orphan));
            }
        }

        var knownKeys = new HashSet<string>(_groups.Select(group => group.Key), StringComparer.Ordinal);
        foreach (var key in _selection.Keys.Where(key => !knownKeys.Contains(key)).OrderBy(key => key, StringComparer.Ordinal))
        {
            foreach (var value in _selection[key].OrderBy(value => value, StringComparer.Ordinal))
            {
                chips.Add(new FacetChip(key, key, value, value));
            }
        }

        return chips;
    }

    /// <summary>Projects the active filter as the <see cref="ActiveFilter"/> a <c>TmFilterChip</c> renders.</summary>
    private static ActiveFilter ToActiveFilter(FacetChip chip)
        => new(chip.GroupKey, chip.GroupLabel, FilterOperator.Equals, chip.Value, chip.Label);

    // ---------- handlers ----------

    /// <summary>Opens or closes the surface.</summary>
    private Task ToggleOpenAsync() => SetOpenAsync(!_open);

    /// <summary>Sets the open state (the overlay panels report their own dismissal through this).</summary>
    /// <param name="open">The new state.</param>
    private Task SetOpenAsync(bool open)
    {
        _open = open;
        return Task.CompletedTask;
    }

    /// <summary>Expands or collapses one group.</summary>
    private Task ToggleGroupAsync(FacetGroup group)
    {
        _expanded[group.Key] = !IsExpanded(group);
        return Task.CompletedTask;
    }

    /// <summary>Stores one group's search text.</summary>
    private Task SetSearchAsync(FacetGroup group, string value)
    {
        _searches[group.Key] = value ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>Reveals every option of one group and hands focus to the first one it revealed.</summary>
    /// <param name="group">The group whose truncation is lifted.</param>
    private Task ShowAllAsync(FacetGroup group)
    {
        _showAll.Add(group.Key);
        if (MaxVisibleOf(group) > 0)
        {
            _focusTarget = FacetFocusTarget.OptionInput;
            _focusGroupKey = group.Key;
            _focusIndex = MaxVisibleOf(group);
        }

        return Task.CompletedTask;
    }

    /// <summary>Toggles one option through the checkbox's own change event.</summary>
    private async Task ToggleOptionAsync(FacetGroup group, FacetOption option, bool isChecked)
    {
        if (option.IsDisabled)
        {
            return;
        }

        if (!_selection.TryGetValue(group.Key, out var values))
        {
            _selection[group.Key] = values = new HashSet<string>(StringComparer.Ordinal);
        }

        if (isChecked)
        {
            values.Add(option.Value);
        }
        else
        {
            values.Remove(option.Value);
        }

        await RaiseSelectionChangedAsync();
    }

    /// <summary>
    /// Removes one active filter value and hands focus to the chip that takes its place — the remove
    /// button that had focus leaves the DOM, so without this the reader restarts from the page top.
    /// </summary>
    /// <param name="chip">The chip to remove.</param>
    /// <param name="index">The chip's position in the rendered chip row.</param>
    private async Task RemoveChipAsync(FacetChip chip, int index)
    {
        if (_selection.TryGetValue(chip.GroupKey, out var values))
        {
            values.Remove(chip.Value);
        }

        _focusTarget = FacetFocusTarget.ChipRemove;
        _focusIndex = index;
        await RaiseSelectionChangedAsync();
    }

    /// <summary>
    /// Clears the whole selection from the page-level chip row. The row (including the button that
    /// held focus) disappears, so focus returns to the trigger — or to the inline groups' first
    /// disclosure, which is the closest equivalent an inline filter has.
    /// </summary>
    private async Task ClearAllFromPageAsync()
    {
        _selection.Clear();
        _focusTarget = FacetFocusTarget.Primary;
        await RaiseSelectionChangedAsync();
    }

    /// <summary>
    /// Clears the whole selection from the open surface's footer. The footer action disables itself
    /// when nothing is selected, which drops focus in some browsers, so focus moves to the
    /// confirmation action of the still-open surface.
    /// </summary>
    private async Task ClearAllFromPanelAsync()
    {
        _selection.Clear();
        _focusTarget = FacetFocusTarget.Confirm;
        await RaiseSelectionChangedAsync();
    }

    /// <summary>
    /// Opens the surface on the chip's group and expands that group, so editing a filter lands on the
    /// control that produced it instead of on a closed disclosure.
    /// </summary>
    private Task EditChipAsync(FacetChip chip)
    {
        if (Presentation == FacetFilterPresentation.Popover)
        {
            _open = true;
        }

        if (_expanded.ContainsKey(chip.GroupKey))
        {
            _expanded[chip.GroupKey] = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>Confirms the filters: the surface closes first, then the host is told.</summary>
    private async Task ApplyAsync()
    {
        _open = false;
        await OnApply.InvokeAsync();
    }

    /// <summary>Hands the host an immutable snapshot of the whole selection.</summary>
    private async Task RaiseSelectionChangedAsync()
    {
        var snapshot = BuildSnapshot();
        await SelectedChanged.InvokeAsync(snapshot);
    }

    /// <summary>One active filter: its group, its value and both labels.</summary>
    /// <param name="GroupKey">The owning group's key.</param>
    /// <param name="GroupLabel">The owning group's display label.</param>
    /// <param name="Value">The selected option value.</param>
    /// <param name="Label">The selected option's display label.</param>
    private sealed record FacetChip(string GroupKey, string GroupLabel, string Value, string Label);

    /// <summary>
    /// Where the next render hands focus. A gesture whose own control leaves the DOM (a chip
    /// removal, a clear-all that hides the chip row or disables its button, a "show more" that
    /// removes itself) records exactly one target here; <see cref="OnAfterRenderAsync"/> applies it
    /// and clears it, so nothing else ever moves focus.
    /// </summary>
    private enum FacetFocusTarget
    {
        /// <summary>Nothing to move — the state every other render keeps.</summary>
        None,

        /// <summary>The trigger (popover), or the inline heading / first disclosure when no trigger exists.</summary>
        Primary,

        /// <summary>The remove button of the chip that now occupies the removed chip's position.</summary>
        ChipRemove,

        /// <summary>The checkbox of the first option a "show more" revealed.</summary>
        OptionInput,

        /// <summary>The confirmation action of the open surface (focus that the disabling footer action drops).</summary>
        Confirm,
    }
}
