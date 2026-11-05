using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Localization;

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
    private readonly string _id = $"tm-ai-memory-{Guid.NewGuid():n}"[..22];
    private readonly Dictionary<string, MemoryItem> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryItem> _hostSeen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _editButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _rowRefs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

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
    private ElementReference _editBox;
    private bool _focusEditBoxAfterRender;
    private string? _pendingEditButtonId;
    private string? _pendingRowFocusId;

    private MemoryItem? _pendingForget;
    private DateTimeOffset _pendingForgetExpiresAt;
    private int _undoSecondsLeft;
    private ITimer? _forgetTimer;

    private bool _bulkBusy;
    private string? _error;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private TimeProvider _clock = TimeProvider.System;
    private bool _disposed;

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

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

    private string UndoText => _pendingForget is null
        ? string.Empty
        : Loc["TmMemoryPanel_UndoText", Shorten(_pendingForget.Text)];

    private string UndoTimerText => Loc["TmMemoryPanel_UndoSeconds",
        Math.Max(_undoSecondsLeft, 0).ToString(FormatCulture)];

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

            if (await TryFocusAsync(_editBox))
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
    /// Disposes the panel: the undo countdown is stopped AND a removal the reader confirmed inside that window is reported
    /// to the host, because the browser the window was offered in is going away.
    /// <para>
    /// Disposing happens when the page navigates or the circuit ends, so nothing may render and nothing may throw: the
    /// removal is awaited WITHOUT the busy state and the announcement (both would call <c>StateHasChanged</c> on a
    /// component that is already gone), the callback is invoked directly and a disconnected circuit is swallowed.
    /// <see cref="UndoForgetAsync"/> still wins when it ran first — a restored memory has no pending removal left.
    /// </para>
    /// </summary>
    /// <returns>A task that completes once the pending removal (if any) has been reported.</returns>
    public async ValueTask DisposeAsync()
    {
        // Take the pending removal BEFORE the window closes: CloseUndoWindow clears it and stops the timer, so the
        // countdown can never report the same memory a second time while the callback below is in flight.
        var pending = _pendingForget;
        CloseUndoWindow();

        if (pending is not null && OnForget.HasDelegate)
        {
            try
            {
                await OnForget.InvokeAsync(pending);
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

    // ── Inline editing ───────────────────────────────────────────────────────

    private Task BeginEditAsync(MemoryItem item)
    {
        if (!_enabled || _editingId == item.Id)
        {
            return Task.CompletedTask;
        }

        // Only ONE row edits at a time: opening another draft discards the previous one, which is never a save.
        _editingId = item.Id;
        _draft = item.Text;
        _focusEditBoxAfterRender = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnDraftInputAsync(ChangeEventArgs args)
    {
        _draft = args.Value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    private async Task OnEditKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter" when !args.ShiftKey:
                await CommitEditAsync();
                break;
            case "Escape":
                CancelEdit();
                break;
        }
    }

    /// <summary>Commits the draft: an unchanged text closes the editor without reporting a second time.</summary>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    public async Task CommitEditAsync()
    {
        if (_editingId is not { } itemId)
        {
            return;
        }

        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        var text = _draft;

        // A SAVE does not queue the Edit button as the focus target: the saved row takes the focus (see CloseEditor).
        CloseEditor(itemId, returnFocusToEditButton: false);

        if (item is null || string.Equals(item.Text, text, StringComparison.Ordinal))
        {
            StateHasChanged();
            return;
        }

        var edited = item with { Text = text };

        // The save runs its OWN busy/error cycle instead of the shared row-action plumbing: a commit that happens while
        // another request holds the row must still be attempted (the reader pressed Enter on their own draft), and only a
        // real FAILURE reopens the editor.
        _busy.Add(itemId);
        _error = null;
        StateHasChanged();

        var saved = false;
        try
        {
            if (OnEdit.HasDelegate)
            {
                await OnEdit.InvokeAsync(edited);
            }

            _overrides[itemId] = edited;
            MergeItems();
            Announce(Loc["TmMemoryPanel_AnnounceEdited"]);
            _pendingRowFocusId = itemId;
            saved = true;
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

        if (!saved)
        {
            // A failed save must not throw the reader's work away: the draft comes back in the editor, with the error
            // already printed beside the row.
            _editingId = itemId;
            _draft = text;
            _focusEditBoxAfterRender = true;
            StateHasChanged();
        }
    }

    /// <summary>Closes the editor without reporting anything and hands the focus back to the Edit button.</summary>
    public void CancelEdit()
    {
        if (_editingId is not { } itemId)
        {
            return;
        }

        CloseEditor(itemId);
        StateHasChanged();
    }

    /// <summary>
    /// Closes the editor. A CANCEL queues the row's Edit button as the focus target; a SAVE queues the ROW instead, because
    /// the browser still has the Enter key sequence in flight and would ACTIVATE a button that takes the focus before the
    /// keypress is dispatched — the click would reopen the editor the reader just closed.
    /// </summary>
    private void CloseEditor(string itemId, bool returnFocusToEditButton = true)
    {
        _editingId = null;
        _draft = string.Empty;

        if (returnFocusToEditButton)
        {
            _pendingEditButtonId = itemId;
        }
    }

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
        // One window at a time: opening a new one reports the previous removal immediately, so nothing is lost silently.
        if (_pendingForget is { } previous)
        {
            CloseUndoWindow();
            await ReportForgetAsync(previous);
        }

        _pendingForget = item;
        _undoSecondsLeft = Math.Max((int)Math.Ceiling(UndoWindow.TotalSeconds), 1);
        _pendingForgetExpiresAt = _clock.GetUtcNow() + UndoWindow;

        _forgetTimer?.Dispose();
        _forgetTimer = _clock.CreateTimer(
            _ => _ = InvokeAsync(OnUndoTickAsync),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private async Task OnUndoTickAsync()
    {
        if (_disposed || _pendingForget is null)
        {
            return;
        }

        var remaining = _pendingForgetExpiresAt - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            var forgotten = _pendingForget;
            CloseUndoWindow();
            await ReportForgetAsync(forgotten);
            return;
        }

        _undoSecondsLeft = Math.Max((int)Math.Ceiling(remaining.TotalSeconds), 0);
        StateHasChanged();
    }

    /// <summary>Restores a memory the reader just removed, within the undo window.</summary>
    /// <returns>A task that completes when the row is back.</returns>
    public Task UndoForgetAsync()
    {
        if (_pendingForget is not { } item)
        {
            return Task.CompletedTask;
        }

        CloseUndoWindow();
        _removed.Remove(item.Id);
        MergeItems();
        Announce(Loc["TmMemoryPanel_AnnounceRestored"]);
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void CloseUndoWindow()
    {
        _forgetTimer?.Dispose();
        _forgetTimer = null;
        _pendingForget = null;
        _undoSecondsLeft = 0;
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
