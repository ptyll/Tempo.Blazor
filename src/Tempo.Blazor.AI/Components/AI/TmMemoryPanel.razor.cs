using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// What an agent remembers: the stored facts grouped by category, a master switch, an inline edit, pause and resume, a
/// removal that can be undone, the review of what the agent proposed, a search and a status filter, and bulk actions over
/// a selection.
/// <para>
/// The surface renders the snapshot it is given and keeps its own copy of what it reported, so the reader sees an edit, a
/// pause or a removal before the host's round-trip — and a host that re-renders the SAME switch or filter value cannot
/// undo the reader's pick. A failed callback prints a localized error and changes nothing; a slow one puts the row in a
/// busy state and disables its actions instead of letting a second press double the request.
/// </para>
/// <para>
/// Accessibility: every state is a WORD plus an icon (the tone is the third, redundant cue), a group is a real disclosure
/// whose native button keeps the focus, an edit commits on Enter and rolls back on Escape with the focus handed back into
/// the SAVED row (cancelling returns it to that row's Edit button), every target reaches the 44 px touch size on a coarse
/// pointer, and the surface owns exactly ONE polite live region — the error text is visible next to the rows and is
/// announced through that same region, never through a second one.
/// </para>
/// </summary>
public partial class TmMemoryPanel : TmComponentBase, IAsyncDisposable
{
    /// <summary>
    /// How many undo rows the queue shows at once (<see cref="MemoryUndoMode.Queue"/>). The stack is bounded by the
    /// number of rows a reader can still reach: past this bound the OLDEST pending removal is reported to the host
    /// immediately, because a removal nobody can see and nobody was told about would be lost.
    /// </summary>
    public const int MaxVisibleUndoRows = 5;

    private readonly string _id = $"tm-ai-memory-{Guid.NewGuid():n}"[..22];
    private readonly Dictionary<string, MemoryItem> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryItem> _hostSeen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _editButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _rowRefs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly List<PendingRemoval> _undoQueue = [];

    private List<MemoryItem> _items = [];

    private bool _enabled;
    private bool _enabledSeeded;
    private bool _lastEnabledParameter;

    private string _filterText = string.Empty;
    private bool _filterSeeded;
    private string? _lastFilterParameter;

    private MemoryStatusFilter _statusFilter;
    private bool _statusFilterSeeded;
    private MemoryStatusFilter _lastStatusFilterParameter;

    private List<string> _collapsed = [];
    private bool _collapsedSeeded;
    private IReadOnlyList<string>? _lastCollapsedParameter;

    private string? _editingId;
    private string _draft = string.Empty;
    private string _draftCategory = string.Empty;
    private string _draftSource = string.Empty;
    private ElementReference _editBox;
    private ElementReference _editCategory;
    private ElementReference _editSource;
    private bool _focusEditBoxAfterRender;
    private string? _pendingEditButtonId;
    private string? _pendingRowFocusId;

    private bool _bulkBusy;
    private string? _error;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private TimeProvider _clock = TimeProvider.System;
    private bool _disposed;

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>
    /// One pending removal: the memory, its own deadline, its own countdown and its own clock timer, so two removals in a
    /// row are two independent promises instead of one shared window.
    /// </summary>
    private sealed class PendingRemoval
    {
        /// <summary>The memory the reader removed.</summary>
        public required MemoryItem Item { get; init; }

        /// <summary>When this particular window closes, on the injected clock.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>The whole seconds still shown in this row's countdown.</summary>
        public int SecondsLeft { get; set; }

        /// <summary>The clock timer that drives this row's countdown, owned by this row alone.</summary>
        public ITimer? Timer { get; set; }
    }

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The memories to show. Null or empty renders the localized empty state, and a null entry inside the list is skipped.
    /// The component never mutates the list: it keeps its own copy of what it reported and drops that copy when the host
    /// supplies a new instance of the same memory.
    /// </summary>
    [Parameter] public IReadOnlyList<MemoryItem>? Items { get; set; }

    /// <summary>
    /// Whether the agent may store and use memories at all, two-way bound with <see cref="EnabledChanged"/>
    /// (<c>@bind-Enabled</c>). Default <c>true</c>. While it is off the panel still lists what is already known — without
    /// any action — and says that nothing is being stored.
    /// </summary>
    [Parameter] public bool Enabled { get; set; } = true;

    /// <summary>Fires with the new switch state.</summary>
    [Parameter] public EventCallback<bool> EnabledChanged { get; set; }

    /// <summary>
    /// The search text, two-way bound with <see cref="FilterTextChanged"/> (<c>@bind-FilterText</c>). It narrows the rows by
    /// the memory's text, its category and its source; the empty string shows everything.
    /// </summary>
    [Parameter] public string? FilterText { get; set; }

    /// <summary>Fires with the new search text.</summary>
    [Parameter] public EventCallback<string> FilterTextChanged { get; set; }

    /// <summary>
    /// Which states the panel shows, two-way bound with <see cref="StatusFilterChanged"/> (<c>@bind-StatusFilter</c>).
    /// Default <see cref="MemoryStatusFilter.All"/>; the filter is a VIEW and never reports anything to the host.
    /// </summary>
    [Parameter] public MemoryStatusFilter StatusFilter { get; set; }

    /// <summary>Fires with the new status filter.</summary>
    [Parameter] public EventCallback<MemoryStatusFilter> StatusFilterChanged { get; set; }

    /// <summary>
    /// The categories whose groups are folded, two-way bound with <see cref="CollapsedCategoriesChanged"/>. Null (the
    /// default) starts every group open.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? CollapsedCategories { get; set; }

    /// <summary>Fires with the new list of folded categories when a reader folds or unfolds one.</summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> CollapsedCategoriesChanged { get; set; }

    /// <summary>
    /// Whether every row carries a selection checkbox and the bulk bar offers its actions. Default <c>true</c>; a
    /// read-mostly host sets it to <c>false</c>.
    /// </summary>
    [Parameter] public bool Selectable { get; set; } = true;

    /// <summary>
    /// How a removal is reported. Default <see cref="MemoryForgetMode.UndoWindow"/>, so a memory is only forgotten after
    /// the reader can no longer take the removal back.
    /// </summary>
    [Parameter] public MemoryForgetMode ForgetMode { get; set; }

    /// <summary>
    /// How long the undo window stays open in <see cref="MemoryForgetMode.UndoWindow"/>. Default five seconds; a zero (or
    /// negative) window behaves like <see cref="MemoryForgetMode.Immediate"/>, because a window nobody can reach would
    /// silently lose the memory.
    /// </summary>
    [Parameter] public TimeSpan UndoWindow { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many removals may stay undoable at once. Default <see cref="MemoryUndoMode.Single"/> — the pre-2.11.0 rule, so
    /// a second Forget reports the first removal at once. <see cref="MemoryUndoMode.Queue"/> keeps one undo row and one
    /// countdown per pending removal, up to <see cref="MaxVisibleUndoRows"/>. An unknown value falls back to
    /// <see cref="MemoryUndoMode.Single"/>.
    /// </summary>
    [Parameter] public MemoryUndoMode UndoMode { get; set; }

    /// <summary>
    /// How the status filter is presented. Default <see cref="MemoryFilterPresentation.Auto"/>, which renders the
    /// five-segment tray on a wide card and a real select on a narrow one (a container query on the panel's own box, never
    /// the viewport). An unknown value falls back to <see cref="MemoryFilterPresentation.Auto"/>.
    /// </summary>
    [Parameter] public MemoryFilterPresentation FilterPresentation { get; set; }

    /// <summary>
    /// Which parts of a memory the inline editor may change. Default <see cref="MemoryEditableFields.Text"/> — exactly the
    /// pre-2.11.0 editor. Adding <see cref="MemoryEditableFields.Category"/> or
    /// <see cref="MemoryEditableFields.Source"/> renders that field beside the text and reports it through
    /// <see cref="OnEditItem"/>; <see cref="MemoryEditableFields.None"/> removes the Edit action entirely.
    /// </summary>
    [Parameter] public MemoryEditableFields EditableFields { get; set; } = MemoryEditableFields.Text;

    /// <summary>
    /// Fires with the ONE record describing a committed edit: the id, the values the reader left and the set of fields
    /// that actually moved. Preferred over <see cref="OnEdit"/> as soon as more than the text is editable, because a host
    /// then applies just what changed instead of diffing its own store. When it has a delegate,
    /// <see cref="OnEdit"/> is not invoked by the same commit.
    /// </summary>
    [Parameter] public EventCallback<MemoryEdit> OnEditItem { get; set; }

    /// <summary>
    /// Fires with the edited memory (the same id and the new text) when the reader commits an inline edit. The host applies
    /// the change; the panel shows its own copy of the new text until the host supplies a new instance.
    /// </summary>
    [Parameter] public EventCallback<MemoryItem> OnEdit { get; set; }

    /// <summary>Fires when the reader pauses a memory (it stays stored but is not used).</summary>
    [Parameter] public EventCallback<MemoryItem> OnPause { get; set; }

    /// <summary>Fires when the reader resumes a paused memory.</summary>
    [Parameter] public EventCallback<MemoryItem> OnResume { get; set; }

    /// <summary>
    /// Fires when a removal becomes final: at once in <see cref="MemoryForgetMode.Immediate"/>, and when the undo window
    /// closes in <see cref="MemoryForgetMode.UndoWindow"/>. A restored removal never fires it.
    /// </summary>
    [Parameter] public EventCallback<MemoryItem> OnForget { get; set; }

    /// <summary>Fires with the reader's decision about a memory the agent proposed.</summary>
    [Parameter] public EventCallback<MemoryReview> OnReview { get; set; }

    /// <summary>
    /// Fires ONCE with the whole selection when the reader applies a bulk action. A bulk removal reports through this
    /// callback only — it does not fire <see cref="OnForget"/> per row.
    /// </summary>
    [Parameter] public EventCallback<MemoryBulkActionRequest> OnBulkAction { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Memory").
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title and the group headings, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// The culture that formats the confidence, the timestamps and the undo countdown. Null (the default) means the current
    /// culture at render time.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The clock behind the undo countdown. Null (the default) resolves the host's registered <see cref="TimeProvider"/> and
    /// falls back to <see cref="TimeProvider.System"/>, so a test controls the window without sleeping.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>Whether the agent may store and use memories (the reader's switch, not the host's original value).</summary>
    public bool EffectiveEnabled => _enabled;

    /// <summary>How many memories the reader has selected.</summary>
    public int SelectedCount => _selected.Count;

    /// <summary>How many removals are still undoable right now (at most <see cref="MaxVisibleUndoRows"/>).</summary>
    public int PendingRemovalCount => _undoQueue.Count;

    /// <summary>
    /// A SNAPSHOT of the memories that are still undoable, oldest first. The list is built on every read, so it never
    /// exposes the panel's own queue and a later undo or expiry cannot mutate a list a caller is holding.
    /// </summary>
    public IReadOnlyList<MemoryItem> PendingRemovals => [.. _undoQueue.Select(entry => entry.Item)];

    /// <summary>Whether a callback is in flight (a row action or a bulk action).</summary>
    public bool IsBusy => _busy.Count > 0 || _bulkBusy;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmMemoryPanel_Title"] : Title!;

    private string GroupHeadingLevel => Math.Min(EffectiveHeadingLevel + 1, 6).ToString(CultureInfo.InvariantCulture);

    private string TitleId => _id + "-title";

    private string ErrorId => _id + "-error";

    private string EnabledHintId => _id + "-enabled-hint";

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    private MemoryForgetMode EffectiveForgetMode
        => MemoryPresentation.IsKnownForgetMode(ForgetMode) ? ForgetMode : MemoryForgetMode.UndoWindow;

    private bool OffersUndo => EffectiveForgetMode == MemoryForgetMode.UndoWindow && UndoWindow > TimeSpan.Zero;

    /// <summary>The undo rule in force: an unknown value falls back to the single window a host already knows.</summary>
    private MemoryUndoMode EffectiveUndoMode
        => UndoMode == MemoryUndoMode.Queue ? MemoryUndoMode.Queue : MemoryUndoMode.Single;

    private string UndoModeSlug => EffectiveUndoMode == MemoryUndoMode.Queue ? "queue" : "single";

    /// <summary>The filter presentation in force: an unknown value falls back to the responsive default.</summary>
    private MemoryFilterPresentation EffectiveFilterPresentation => FilterPresentation switch
    {
        MemoryFilterPresentation.Segmented => MemoryFilterPresentation.Segmented,
        MemoryFilterPresentation.Select => MemoryFilterPresentation.Select,
        _ => MemoryFilterPresentation.Auto,
    };

    private string FilterPresentationSlug => EffectiveFilterPresentation switch
    {
        MemoryFilterPresentation.Segmented => "segmented",
        MemoryFilterPresentation.Select => "select",
        _ => "auto",
    };

    /// <summary>
    /// In <see cref="MemoryFilterPresentation.Auto"/> BOTH surfaces render and the container query picks one: a surface
    /// that is not in the DOM can never be shown, and the width the query reads is only known to the browser.
    /// </summary>
    private bool ShowsSegmentedFilter
        => EffectiveFilterPresentation is MemoryFilterPresentation.Auto or MemoryFilterPresentation.Segmented;

    private bool ShowsSelectFilter
        => EffectiveFilterPresentation is MemoryFilterPresentation.Auto or MemoryFilterPresentation.Select;

    /// <summary>The editable fields in force: unknown bits are dropped, because they can name nothing the editor renders.</summary>
    private MemoryEditableFields EffectiveEditableFields => EditableFields & MemoryEditableFields.All;

    private bool CanEdit => EffectiveEditableFields != MemoryEditableFields.None;

    private bool CanEditText => EffectiveEditableFields.HasFlag(MemoryEditableFields.Text);

    private bool CanEditCategory => EffectiveEditableFields.HasFlag(MemoryEditableFields.Category);

    private bool CanEditSource => EffectiveEditableFields.HasFlag(MemoryEditableFields.Source);

    private string EditableFieldsSlug
    {
        get
        {
            var fields = EffectiveEditableFields;
            if (fields == MemoryEditableFields.None)
            {
                return "none";
            }

            // Stable order (text, category, source) so a host and a test can name the same value.
            var names = new List<string>(3);
            if (fields.HasFlag(MemoryEditableFields.Text))
            {
                names.Add("text");
            }

            if (fields.HasFlag(MemoryEditableFields.Category))
            {
                names.Add("category");
            }

            if (fields.HasFlag(MemoryEditableFields.Source))
            {
                names.Add("source");
            }

            return string.Join(' ', names);
        }
    }

    private IReadOnlyList<MemoryItem> VisibleItems
    {
        get
        {
            var query = _filterText.Trim();
            return
            [
                .. _items.Where(item =>
                    MemoryPresentation.MatchesFilter(_statusFilter, item.Status)
                    && MatchesSearch(item, query)),
            ];
        }
    }

    private IReadOnlyList<MemoryGroup> Groups
    {
        get
        {
            return
            [
                .. VisibleItems
                    .GroupBy(item => item.HasCategory ? item.Category.Trim() : string.Empty, StringComparer.Ordinal)
                    .Select(group => new MemoryGroup(
                        group.Key,
                        MemoryPresentation.CategoryName(Loc, group.Key),
                        [.. group]))
                    .OrderBy(group => group.Name, StringComparer.CurrentCulture),
            ];
        }
    }

    private IReadOnlyList<MemoryItem> SelectedItems
        => [.. VisibleItems.Where(item => _selected.Contains(item.Id))];

    private bool HasItems => _items.Count > 0;

    private bool IsEmpty => VisibleItems.Count == 0;

    private string EmptyText => HasItems ? Loc["TmMemoryPanel_EmptyFiltered"] : Loc["TmMemoryPanel_Empty"];

    private string? ErrorText => _error;

    private string CurrentFilterSlug => MemoryPresentation.FilterSlug(_statusFilter);

    private IReadOnlyList<SegmentedOption<string>> FilterOptions
        =>
        [
            .. Enum.GetValues<MemoryStatusFilter>().Select(filter => new SegmentedOption<string>
            {
                Label = Loc[MemoryPresentation.FilterLabelKey(filter)],
                Value = MemoryPresentation.FilterSlug(filter),
            }),
        ];

    /// <summary>
    /// The same five choices as the tray, in the shape the core select wants: the narrow container shows a real
    /// <c>&lt;select&gt;</c> instead of a tray that has to scroll, and both feed the SAME two-way filter.
    /// </summary>
    private IReadOnlyList<SelectOption<string>> FilterSelectOptions
        =>
        [
            .. Enum.GetValues<MemoryStatusFilter>().Select(filter => new SelectOption<string>(
                MemoryPresentation.FilterSlug(filter),
                Loc[MemoryPresentation.FilterLabelKey(filter)])),
        ];

    private string UndoText(MemoryItem item) => Loc["TmMemoryPanel_UndoText", Shorten(item.Text)];

    private string UndoLabel(MemoryItem item)
        => Loc["TmMemoryPanel_ActionOn", Loc["TmMemoryPanel_UndoAction"], Shorten(item.Text)];

    private string UndoTimerText(PendingRemoval entry)
        => Loc["TmMemoryPanel_UndoSeconds", Math.Max(entry.SecondsLeft, 0).ToString(FormatCulture)];

    private string RootClass
    {
        get
        {
            var root = "tm-ai-memory" + (_enabled ? string.Empty : " tm-ai-memory--off");
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    /// <summary>One category of memories: its key, the name it prints and the rows it holds.</summary>
    private sealed record MemoryGroup(string Key, string Name, IReadOnlyList<MemoryItem> Items);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _clock = ResolveClock();

        if (!_enabledSeeded || Enabled != _lastEnabledParameter)
        {
            _enabledSeeded = true;
            _lastEnabledParameter = Enabled;
            _enabled = Enabled;
        }

        if (!_filterSeeded || !string.Equals(FilterText, _lastFilterParameter, StringComparison.Ordinal))
        {
            _filterSeeded = true;
            _lastFilterParameter = FilterText;
            _filterText = FilterText ?? string.Empty;
        }

        if (!_statusFilterSeeded || StatusFilter != _lastStatusFilterParameter)
        {
            _statusFilterSeeded = true;
            _lastStatusFilterParameter = StatusFilter;
            _statusFilter = MemoryPresentation.IsKnownFilter(StatusFilter) ? StatusFilter : MemoryStatusFilter.All;
        }

        if (!_collapsedSeeded || !ReferenceEquals(CollapsedCategories, _lastCollapsedParameter))
        {
            _collapsedSeeded = true;
            _lastCollapsedParameter = CollapsedCategories;
            _collapsed = CollapsedCategories is null ? [] : [.. CollapsedCategories.Where(key => key is not null)];
        }

        MergeItems();
        PruneSelection();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The focus moves happen AFTER the paint: the control that has to receive the focus is created by this very
        // render, and a browser only focuses an element once it is patched.
        if (_focusEditBoxAfterRender)
        {
            _focusEditBoxAfterRender = false;

            // The editor may carry one field or three: the first one it rendered is where the reader's typing goes.
            if (await TryFocusAsync(_editBox))
            {
                return;
            }

            if (await TryFocusAsync(_editCategory))
            {
                return;
            }

            if (await TryFocusAsync(_editSource))
            {
                return;
            }
        }

        if (_pendingRowFocusId is { } savedId)
        {
            _pendingRowFocusId = null;

            if (_rowRefs.TryGetValue(savedId, out var savedRow))
            {
                await TryFocusAsync(savedRow);
            }

            return;
        }

        if (_pendingEditButtonId is { } itemId)
        {
            _pendingEditButtonId = null;

            if (_editButtons.TryGetValue(itemId, out var button))
            {
                await TryFocusAsync(button);
            }
        }
    }

    /// <summary>
    /// Disposes the panel: every undo countdown is stopped AND every removal the reader confirmed inside its window is
    /// reported to the host, because the browser the windows were offered in is going away — in
    /// <see cref="MemoryUndoMode.Queue"/> that is every pending removal, not just the newest one.
    /// <para>
    /// Disposing happens when the page navigates or the circuit ends, so nothing may render and nothing may throw: the
    /// removals are awaited WITHOUT the busy state and the announcement (both would call <c>StateHasChanged</c> on a
    /// component that is already gone), the callback is invoked directly and a disconnected circuit is swallowed.
    /// <see cref="UndoForgetAsync(string)"/> still wins when it ran first — a restored memory has no pending removal left.
    /// </para>
    /// </summary>
    /// <returns>A task that completes once the pending removals (if any) have been reported.</returns>
    public async ValueTask DisposeAsync()
    {
        // Take the pending removals BEFORE the windows close: CloseUndoWindow removes them from the queue and stops their
        // timers, so a countdown can never report the same memory a second time while the callbacks below are in flight.
        var pending = PendingRemovals;
        foreach (var entry in _undoQueue.ToArray())
        {
            CloseUndoWindow(entry);
        }

        foreach (var item in pending)
        {
            if (!OnForget.HasDelegate)
            {
                continue;
            }

            try
            {
                await OnForget.InvokeAsync(item);
            }
            catch (Exception exception) when (exception is Microsoft.JSInterop.JSDisconnectedException
                or ObjectDisposedException
                or InvalidOperationException)
            {
                // The circuit is gone: the host can no longer be told, and a disposal must never surface an exception.
            }
        }

        _disposed = true;
    }

    private TimeProvider ResolveClock()
        => TimeProvider
           ?? ServiceProvider.GetService<TimeProvider>()
           ?? System.TimeProvider.System;

    /// <summary>
    /// Merges the host's snapshot with the panel's own copy: a memory whose host instance CHANGED is taken from the host
    /// (the store caught up), one the reader forgot stays hidden, and everything else keeps the state the reader reported.
    /// </summary>
    private void MergeItems()
    {
        List<MemoryItem> host = Items is null ? [] : [.. Items.Where(item => item is not null)];
        var present = new HashSet<string>(host.Select(item => item.Id), StringComparer.Ordinal);

        // A removal stays hidden only while the host still reports the memory: once the store drops it, the id is free
        // again, so a memory later re-created under that id is a NEW fact and renders.
        _removed.RemoveWhere(id => !present.Contains(id));

        foreach (var id in _hostSeen.Keys.Where(key => !present.Contains(key)).ToList())
        {
            _hostSeen.Remove(id);
            _overrides.Remove(id);
            _editButtons.Remove(id);
        }

        _items = [];
        foreach (var item in host)
        {
            if (_hostSeen.TryGetValue(item.Id, out var seen) && !ReferenceEquals(seen, item))
            {
                _overrides.Remove(item.Id);
            }

            _hostSeen[item.Id] = item;

            if (_removed.Contains(item.Id))
            {
                continue;
            }

            _items.Add(_overrides.TryGetValue(item.Id, out var local) ? local : item);
        }
    }

    private void PruneSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var visible = new HashSet<string>(VisibleItems.Select(item => item.Id), StringComparer.Ordinal);
        _selected.RemoveWhere(id => !visible.Contains(id));
    }

    private static bool MatchesSearch(MemoryItem item, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        return item.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (item.Source?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    // ── The master switch ────────────────────────────────────────────────────

    private async Task OnEnabledChangedAsync(bool enabled)
    {
        if (enabled == _enabled)
        {
            return;
        }

        // The host's own value is deliberately NOT marked as seen: the reader's pick wins until the host MOVES the
        // parameter, so a host that re-renders with the same value cannot undo the choice.
        _enabled = enabled;
        if (!enabled)
        {
            _editingId = null;
            _selected.Clear();
        }

        Announce(enabled ? Loc["TmMemoryPanel_AnnounceEnabled"] : Loc["TmMemoryPanel_AnnounceDisabled"]);
        StateHasChanged();

        if (EnabledChanged.HasDelegate)
        {
            await EnabledChanged.InvokeAsync(enabled);
        }
    }

    // ── Search and filter ────────────────────────────────────────────────────

    private async Task OnSearchInputAsync(ChangeEventArgs args)
    {
        _filterText = args.Value?.ToString() ?? string.Empty;
        PruneSelection();
        AnnounceCount();

        if (FilterTextChanged.HasDelegate)
        {
            await FilterTextChanged.InvokeAsync(_filterText);
        }
    }

    private async Task OnStatusFilterChangedAsync(string slug)
    {
        if (!MemoryPresentation.TryParseFilterSlug(slug, out var filter) || filter == _statusFilter)
        {
            return;
        }

        _statusFilter = filter;
        PruneSelection();
        AnnounceCount();
        StateHasChanged();

        if (StatusFilterChanged.HasDelegate)
        {
            await StatusFilterChanged.InvokeAsync(filter);
        }
    }

    /// <summary>Announces how many of the stored memories the current view shows.</summary>
    private void AnnounceCount()
        => Announce(Loc.Plural("TmMemoryPanel_Showing", VisibleItems.Count, _items.Count));

    // ── Groups ───────────────────────────────────────────────────────────────

    private bool IsCollapsed(string key) => _collapsed.Contains(key, StringComparer.Ordinal);

    private string GroupBodyId(int index) => $"{_id}-group-{index.ToString(CultureInfo.InvariantCulture)}";

    private async Task ToggleGroupAsync(string key)
    {
        if (!_collapsed.Remove(key))
        {
            _collapsed.Add(key);
        }

        // The disclosure is a native button, so the focus never leaves it; only the host is told.
        if (CollapsedCategoriesChanged.HasDelegate)
        {
            await CollapsedCategoriesChanged.InvokeAsync([.. _collapsed]);
        }

        StateHasChanged();
    }

    private bool IsGroupFullySelected(MemoryGroup group)
        => group.Items.Count > 0 && group.Items.All(item => _selected.Contains(item.Id));

    private void ToggleGroupSelection(MemoryGroup group, bool selected)
    {
        foreach (var item in group.Items)
        {
            if (selected)
            {
                _selected.Add(item.Id);
            }
            else
            {
                _selected.Remove(item.Id);
            }
        }
    }

    private void ToggleSelection(string itemId, bool selected)
    {
        if (selected)
        {
            _selected.Add(itemId);
        }
        else
        {
            _selected.Remove(itemId);
        }
    }

    private void ClearSelection() => _selected.Clear();

    // ── Pause, resume, review, forget ────────────────────────────────────────

    private Task PauseAsync(MemoryItem item) => RunItemActionAsync(
        item.Id,
        async () =>
        {
            if (OnPause.HasDelegate)
            {
                await OnPause.InvokeAsync(item);
            }

            _overrides[item.Id] = item with { Status = MemoryItemStatus.Paused };
        },
        Loc["TmMemoryPanel_AnnouncePaused"]);

    private Task ResumeAsync(MemoryItem item) => RunItemActionAsync(
        item.Id,
        async () =>
        {
            if (OnResume.HasDelegate)
            {
                await OnResume.InvokeAsync(item);
            }

            _overrides[item.Id] = item with { Status = MemoryItemStatus.Active };
        },
        Loc["TmMemoryPanel_AnnounceResumed"]);

    private Task ReviewAsync(MemoryItem item, MemoryReviewDecision decision) => RunItemActionAsync(
        item.Id,
        async () =>
        {
            if (OnReview.HasDelegate)
            {
                await OnReview.InvokeAsync(new MemoryReview(item, decision));
            }

            _overrides[item.Id] = item with
            {
                Status = decision == MemoryReviewDecision.Approve
                    ? MemoryItemStatus.Active
                    : MemoryItemStatus.Forgotten,
            };
        },
        decision == MemoryReviewDecision.Approve
            ? Loc["TmMemoryPanel_AnnounceApproved"]
            : Loc["TmMemoryPanel_AnnounceRejected"]);

    /// <summary>
    /// Forgets a memory: the row leaves at once and the host is told either immediately or when the undo window closes.
    /// </summary>
    /// <param name="item">The memory to forget.</param>
    /// <returns>A task that completes when the removal has been reported (immediate) or the window opened.</returns>
    public async Task ForgetAsync(MemoryItem item)
    {
        if (!_enabled || item is null)
        {
            return;
        }

        _overrides.Remove(item.Id);
        _selected.Remove(item.Id);
        _editingId = _editingId == item.Id ? null : _editingId;
        _removed.Add(item.Id);
        MergeItems();

        if (!OffersUndo)
        {
            await ReportForgetAsync(item);
            return;
        }

        Announce(Loc["TmMemoryPanel_AnnounceForgotten"]);
        await StartUndoWindowAsync(item);
        StateHasChanged();
    }

    private async Task ReportForgetAsync(MemoryItem item)
    {
        await RunItemActionAsync(
            item.Id,
            async () =>
            {
                if (OnForget.HasDelegate)
                {
                    await OnForget.InvokeAsync(item);
                }
            },
            Loc["TmMemoryPanel_AnnounceForgotten"]);
    }

    private async Task StartUndoWindowAsync(MemoryItem item)
    {
        if (EffectiveUndoMode == MemoryUndoMode.Queue)
        {
            // A stack cannot grow past the rows a reader can still reach: the OLDEST pending removal is reported NOW
            // instead of sliding off the screen, because a removal nobody can see and nobody was told about is lost.
            while (_undoQueue.Count >= MaxVisibleUndoRows)
            {
                var oldest = _undoQueue[0];
                CloseUndoWindow(oldest);
                await ReportForgetAsync(oldest.Item);
            }
        }
        else
        {
            // One window at a time: opening a new one reports the previous removal immediately, so nothing is lost.
            var previous = _undoQueue.ToArray();
            foreach (var entry in previous)
            {
                CloseUndoWindow(entry);
            }

            foreach (var entry in previous)
            {
                await ReportForgetAsync(entry.Item);
            }
        }

        var window = new PendingRemoval
        {
            Item = item,
            ExpiresAt = _clock.GetUtcNow() + UndoWindow,
            SecondsLeft = Math.Max((int)Math.Ceiling(UndoWindow.TotalSeconds), 1),
        };

        window.Timer = _clock.CreateTimer(
            _ => _ = InvokeAsync(() => OnUndoTickAsync(window)),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

        _undoQueue.Add(window);
    }

    /// <summary>
    /// One row's countdown: its own timer updates its own seconds and reports its own removal when it expires, so the
    /// windows close in the order they were opened.
    /// </summary>
    private async Task OnUndoTickAsync(PendingRemoval entry)
    {
        if (_disposed || !_undoQueue.Contains(entry))
        {
            return;
        }

        var remaining = entry.ExpiresAt - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            CloseUndoWindow(entry);
            StateHasChanged();
            await ReportForgetAsync(entry.Item);
            return;
        }

        entry.SecondsLeft = Math.Max((int)Math.Ceiling(remaining.TotalSeconds), 0);
        StateHasChanged();
    }

    /// <summary>Restores the most recently removed memory that is still undoable.</summary>
    /// <returns>A task that completes when the row is back.</returns>
    public Task UndoForgetAsync()
        => _undoQueue.Count == 0 ? Task.CompletedTask : UndoForgetAsync(_undoQueue[^1].Item.Id);

    /// <summary>
    /// Restores ONE memory the reader removed, by the id its undo row carries — never merely the newest one, because a
    /// queue puts several rows in front of the reader.
    /// </summary>
    /// <param name="itemId">The id of the removed memory whose row the reader pressed.</param>
    /// <returns>A task that completes when the row is back (or the id was no longer pending).</returns>
    public Task UndoForgetAsync(string itemId)
    {
        var entry = _undoQueue.FirstOrDefault(candidate => string.Equals(candidate.Item.Id, itemId, StringComparison.Ordinal));
        if (entry is null)
        {
            return Task.CompletedTask;
        }

        CloseUndoWindow(entry);
        _removed.Remove(entry.Item.Id);
        MergeItems();

        // The button that had the focus left the DOM with its row, so the focus would drop to <body> and a keyboard
        // reader with several queued rows would lose their place. The RESTORED row takes it instead (WCAG 2.4.3).
        _pendingRowFocusId = entry.Item.Id;
        Announce(Loc["TmMemoryPanel_AnnounceRestored"]);
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Closes ONE window: its timer stops, its row leaves the stack and its removal is no longer offered.</summary>
    private void CloseUndoWindow(PendingRemoval entry)
    {
        entry.Timer?.Dispose();
        entry.Timer = null;
        entry.SecondsLeft = 0;
        _undoQueue.Remove(entry);
    }

    // ── Bulk actions ─────────────────────────────────────────────────────────

    private async Task RunBulkAsync(MemoryBulkAction action)
    {
        var items = SelectedItems;
        if (items.Count == 0 || _bulkBusy)
        {
            return;
        }

        _selected.Clear();
        _bulkBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            if (OnBulkAction.HasDelegate)
            {
                await OnBulkAction.InvokeAsync(new MemoryBulkActionRequest(items, action));
            }

            foreach (var item in items)
            {
                switch (action)
                {
                    case MemoryBulkAction.Pause:
                        _overrides[item.Id] = item with { Status = MemoryItemStatus.Paused };
                        break;
                    case MemoryBulkAction.Resume:
                        _overrides[item.Id] = item with { Status = MemoryItemStatus.Active };
                        break;
                    default:
                        _overrides.Remove(item.Id);
                        _removed.Add(item.Id);
                        break;
                }
            }

            MergeItems();
            Announce(Loc.Plural(action switch
            {
                MemoryBulkAction.Pause => "TmMemoryPanel_AnnounceBulkPaused",
                MemoryBulkAction.Resume => "TmMemoryPanel_AnnounceBulkResumed",
                _ => "TmMemoryPanel_AnnounceBulkForgotten",
            }, items.Count));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            _bulkBusy = false;
            StateHasChanged();
        }
    }

    // ── Shared action plumbing ───────────────────────────────────────────────

    /// <summary>
    /// Runs one row action with a busy state: the row's buttons disable while the callback is in flight, a failure is
    /// printed AND announced and changes nothing, and a success is announced once.
    /// </summary>
    private async Task<bool> RunItemActionAsync(string itemId, Func<Task> action, string successMessage)
    {
        if (_busy.Contains(itemId))
        {
            return true;
        }

        _busy.Add(itemId);
        _error = null;
        StateHasChanged();

        var succeeded = false;
        try
        {
            await action();
            MergeItems();
            Announce(successMessage);
            succeeded = true;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            _busy.Remove(itemId);
            StateHasChanged();
        }

        return succeeded;
    }

    private void Fail(Exception exception)
    {
        _error = Loc["TmMemoryPanel_ActionFailed", exception.Message];
        Announce(_error);
    }

    private bool IsRowBusy(string itemId) => _busy.Contains(itemId);

    private static string Shorten(string? text)
    {
        var value = text ?? string.Empty;
        return value.Length <= 60 ? value : value[..59] + "…";
    }

    private string SelectItemLabel(MemoryItem item) => Loc["TmMemoryPanel_SelectItem", Shorten(item.Text)];

    private string SelectGroupLabel(MemoryGroup group) => Loc["TmMemoryPanel_SelectGroup", group.Name];

    private string ConfidenceText(MemoryItem item)
        => Loc["TmMemoryPanel_Confidence", item.Confidence!.Value.ToString("P0", FormatCulture)];

    private string SourceText(MemoryItem item) => Loc["TmMemoryPanel_Source", item.Source];

    private string CreatedText(MemoryItem item)
        => Loc["TmMemoryPanel_Created", item.CreatedAt.ToLocalTime().ToString("g", FormatCulture)];

    private string UpdatedText(MemoryItem item)
        => Loc["TmMemoryPanel_Updated", item.UpdatedAt!.Value.ToLocalTime().ToString("g", FormatCulture)];

    /// <summary>
    /// The accessible name of a row action: the VISIBLE label the reader sees plus the memory it acts on (WCAG 2.5.3
    /// holds because the name contains the text on the button) — a list of bare "Pause" buttons tells a screen-reader
    /// reader nothing about which row they would pause.
    /// </summary>
    private string RowActionLabel(string actionKey, MemoryItem item)
        => Loc["TmMemoryPanel_ActionOn", Loc[actionKey], Shorten(item.Text)];

    /// <summary>
    /// Writes the announcement into the ONE polite region. An identical sentence is re-announced by toggling one invisible
    /// marker, because a live region announces a MUTATION and not a repeat of the same text.
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
}
