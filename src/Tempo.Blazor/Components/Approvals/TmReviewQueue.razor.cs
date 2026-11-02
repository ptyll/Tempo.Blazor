using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models.Approvals;
using Tempo.Blazor.Abstractions.Models.Reviews;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Approvals;

/// <summary>
/// A queue of items a human has to decide on: rows sorted by risk and priority, a localized risk
/// badge per item, optional filter tabs with counts, Approve / Edit / Reject / Skip actions (buttons
/// and single-key shortcuts), and a one-at-a-time Focus mode with a "Item 3 of 12" position and
/// previous/next navigation.
/// <para>
/// The queue is a <em>controlled</em> component over a host-owned list: it sorts and presents
/// <see cref="Items"/>, tracks which items were resolved in the current session, and never removes an
/// item from the host's data. It resolves an item only after <see cref="OnAction"/> completes without
/// throwing — a failed save shows a localized error and leaves the item pending. A resolved item
/// collapses into a result row (a CSS height transition, no JS) and leaves the pending sequence.
/// </para>
/// <para>
/// Accessibility: the list is an APG <c>listbox</c> when its rows carry no buttons and switches to a
/// <c>grid</c> when they do (an <c>option</c> may not contain interactive controls); selection
/// follows focus with a roving tabindex, every row button carries the item in its accessible name,
/// exactly ONE polite live region announces a result, and the single-key shortcuts are disableable.
/// The queue's own key handler sits on the elements it owns — the list body and the Focus view —
/// never on the root, so keys pressed on the <c>TmTabs</c> strip (Home/End there) or inside a
/// templated text field cannot move the selection or fire an action; a host's own
/// <c>@onkeydown:stopPropagation</c> fence is defence in depth rather than a requirement. A focus
/// move scrolls its target into view, and the previous/next buttons advertise the J/K shortcuts.
/// </para>
/// </summary>
/// <typeparam name="TItem">The item type the queue is given. The component never inspects it: every
/// label and rank comes from the selector parameters, so a DTO, a view model or a domain entity all
/// work without an adapter.</typeparam>
public partial class TmReviewQueue<TItem> : TmComponentBase
{
    /// <summary>Live handle per row, keyed by item id, for the roving-tabindex focus move.</summary>
    private readonly Dictionary<string, ElementReference> _rowElements = [];

    /// <summary>Items resolved in this session, keyed by item id, with the verb that resolved them.</summary>
    private readonly Dictionary<string, HandledResult> _handled = [];

    private List<ItemEntry> _ordered = [];
    private string? _activeId;
    private string? _activeIdParameter;
    private string? _notifiedActiveId;
    private string? _activeTabId;

    private string? _actionItemId;
    private ReviewQueueAction? _actionInFlight;
    private string? _quickActionInFlight;
    private string? _errorMessage;
    private string _announcement = string.Empty;

    private FocusTarget _focusTarget;
    private ElementReference _rootElement;
    private ElementReference _focusElement;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The items to review — normally already filtered by <see cref="ActiveTabId"/>. The queue sorts
    /// them but never removes one: a resolved item stays in the list as a collapsed result row.
    /// </summary>
    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }

    /// <summary>
    /// Stable identity of an item. Used as the render key, the value of <c>data-item-id</c> and the
    /// key of the resolved set, so it must be stable across renders and unique within
    /// <see cref="Items"/>. When omitted the item's index is used (stable only while the list order
    /// is).
    /// </summary>
    [Parameter] public Func<TItem, string>? IdSelector { get; set; }

    /// <summary>The item's heading, rendered as the row title and used in announcements. Defaults to
    /// <see cref="object.ToString"/>.</summary>
    [Parameter] public Func<TItem, string>? TitleSelector { get; set; }

    /// <summary>Optional secondary line under the title (a category, owner or due date).</summary>
    [Parameter] public Func<TItem, string?>? SubtitleSelector { get; set; }

    /// <summary>
    /// The item's risk, shown as a localized badge and used by
    /// <see cref="ReviewQueueSort.RiskThenPriority"/>. Defaults to
    /// <see cref="ApprovalRisk.Low"/>.
    /// </summary>
    [Parameter] public Func<TItem, ApprovalRisk>? RiskSelector { get; set; }

    /// <summary>
    /// The item's priority for sorting — a HIGHER value ranks first. Ties fall back to the other
    /// rank and then to the order <see cref="Items"/> arrived in. Defaults to 0 (no ranking).
    /// </summary>
    [Parameter] public Func<TItem, int>? PrioritySelector { get; set; }

    /// <summary>Optional row body rendered under the title, above the row's action buttons. The
    /// slot is fenced: keys typed into a field inside it keep their browser behaviour instead of
    /// moving the selection or firing a shortcut.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>
    /// The Focus mode body of the active item (a diff, a document preview, a key/value grid). When
    /// omitted Focus mode shows the title, the subtitle and the risk badge only. The slot is fenced
    /// like <see cref="ItemTemplate"/>: a textarea or a select inside it owns its arrows and
    /// Home/End, and the prev/next buttons remain the way to move on.
    /// </summary>
    [Parameter] public RenderFragment<TItem>? DetailTemplate { get; set; }

    /// <summary>Content shown instead of the built-in empty state when the queue has no items.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Content shown while <see cref="IsLoading"/> is true. Defaults to three skeleton rows.</summary>
    [Parameter] public RenderFragment? LoadingContent { get; set; }

    /// <summary>
    /// Renders the loading skeleton, marks the queue <c>aria-busy</c> and ignores actions. Default
    /// false.
    /// </summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>List (many rows) or Focus (one item at a time). Default
    /// <see cref="ReviewQueueMode.List"/>.</summary>
    [Parameter] public ReviewQueueMode Mode { get; set; } = ReviewQueueMode.List;

    /// <summary>How the pending items are ordered. Default
    /// <see cref="ReviewQueueSort.RiskThenPriority"/>.</summary>
    [Parameter] public ReviewQueueSort SortMode { get; set; } = ReviewQueueSort.RiskThenPriority;

    /// <summary>
    /// The built-in verbs offered per item. Default <see cref="ReviewQueueActions.All"/>. Combine it
    /// with <see cref="QuickActions"/> for the "decision inbox" variant (two host-named buttons):
    /// set this to <see cref="ReviewQueueActions.None"/> and supply the two quick actions.
    /// </summary>
    [Parameter] public ReviewQueueActions Actions { get; set; } = ReviewQueueActions.All;

    /// <summary>
    /// Host-defined buttons rendered in the same group as the built-in verbs, for a decision inbox
    /// whose verbs are named by the workflow ("Approve &amp; file", "Send back to the author").
    /// Activating one raises <see cref="OnAction"/> with
    /// <see cref="ReviewQueueAction.QuickAction"/> and
    /// <see cref="ReviewQueueActionArgs{TItem}.QuickActionId"/>.
    /// </summary>
    [Parameter] public IReadOnlyList<ReviewQueueQuickAction>? QuickActions { get; set; }

    /// <summary>
    /// Callback that persists a decision. While it runs the queue is <c>aria-busy</c> and every
    /// action button is disabled. When it throws, a localized error is shown in an alert, the error
    /// is announced and the item stays pending — a failed save must never look like a decision.
    /// </summary>
    [Parameter] public Func<ReviewQueueActionArgs<TItem>, Task>? OnAction { get; set; }

    /// <summary>
    /// The filter tabs rendered above the queue. The queue raises
    /// <see cref="ActiveTabIdChanged"/> and renders whatever <see cref="Items"/> the host passes back
    /// — it never guesses the predicate, because only the host knows what a tab means for its data.
    /// </summary>
    [Parameter] public IReadOnlyList<ReviewQueueTab>? Tabs { get; set; }

    /// <summary>
    /// The selected tab id. When it is null or names no tab, the first tab is selected. Pair it with
    /// <see cref="ActiveTabIdChanged"/> (<c>@bind-ActiveTabId</c>); the host filters
    /// <see cref="Items"/> in response.
    /// </summary>
    [Parameter] public string? ActiveTabId { get; set; }

    /// <summary>Raised when the user selects a tab.</summary>
    [Parameter] public EventCallback<string> ActiveTabIdChanged { get; set; }

    /// <summary>
    /// The selected item id (the row that owns the roving tabindex and, in Focus mode, the item whose
    /// detail is rendered). When null or unknown, the first pending item is selected. Pair it with
    /// <see cref="ActiveItemIdChanged"/> (<c>@bind-ActiveItemId</c>).
    /// </summary>
    [Parameter] public string? ActiveItemId { get; set; }

    /// <summary>Raised when the selection or the tab follows it moves — a click, an arrow key, a
    /// skip, auto-advance or a programmatic <see cref="RaiseActionAsync"/>. Null when nothing is
    /// pending any more.</summary>
    [Parameter] public EventCallback<string?> ActiveItemIdChanged { get; set; }

    /// <summary>
    /// Whether a resolved item hands the selection to the next pending item automatically. Default
    /// true. With false the resolved item stays displayed (its detail shows a result banner and a
    /// localized "next item" button), which suits a reviewer who wants to read the result first.
    /// <see cref="ReviewQueueAction.Skip"/> always moves on.
    /// </summary>
    [Parameter] public bool AutoAdvance { get; set; } = true;

    /// <summary>
    /// Whether the A / E / R / S single-key shortcuts and the J / K navigation keys are active.
    /// Default true; the arrow keys, Home and End are the widget's own keyboard contract and stay
    /// active either way. WCAG 2.1.4 requires the opt-out this parameter provides.
    /// </summary>
    [Parameter] public bool EnableShortcuts { get; set; } = true;

    /// <summary>Accessible name of the queue group. Defaults to a localized "Review queue".</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Accessible name of the tab strip. Defaults to a localized "Review tabs".</summary>
    [Parameter] public string? TabsAriaLabel { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// The layout Focus mode resolves its action presentation from. <see cref="TmLayoutMode.Auto"/>
    /// (the default) measures this queue's own container; a forced value pins it for a host or a
    /// test. On a mobile layout the Focus mode actions move into <c>TmMobileActionBar</c>.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The layout rendered before the first measurement (static SSR and the first frame).
    /// Default <see cref="TmLayoutMode.Desktop"/>.</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>Unmatched attributes forwarded to the queue root.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Public state ─────────────────────────────────────────────────────────

    /// <summary>Every item in the order the queue presents it (pending and resolved alike).</summary>
    public IReadOnlyList<TItem> OrderedItems => _ordered.Select(entry => entry.Item).ToList();

    /// <summary>The items that still need a decision, in presentation order.</summary>
    public IReadOnlyList<TItem> PendingItems => PendingEntries().Select(entry => entry.Item).ToList();

    /// <summary>The number of items that still need a decision.</summary>
    public int PendingCount => PendingEntries().Count;

    /// <summary>Ids of the items resolved in this session. Only shrinks when the host passes a new
    /// list without them or calls <see cref="ClearHandledAsync"/>.</summary>
    public IReadOnlyCollection<string> HandledItemIds => _handled.Keys;

    /// <summary>The id of the item that currently owns the selection, or null when nothing is
    /// pending.</summary>
    public string? ActiveItemKey => _activeId;

    /// <summary>The active item, or default when the queue is empty.</summary>
    public TItem? ActiveItem => ActiveEntry is { } entry ? entry.Item : default;

    /// <summary>Whether an action is being persisted right now.</summary>
    public bool IsBusy => _actionItemId is not null;

    /// <summary>Whether the queue renders the loading skeleton (mirrors <see cref="IsLoading"/>).</summary>
    public bool IsLoadingItems => IsLoading;

    // ── Public methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Raises <paramref name="action"/> for the active item, exactly as its button would (including
    /// the pending/disabled state, the auto-advance and the announcement). A no-op when nothing is
    /// active, the verb is not enabled or an action is already running.
    /// </summary>
    /// <param name="action">The built-in verb to raise.</param>
    /// <param name="quickActionId">The quick action's id when
    /// <paramref name="action"/> is <see cref="ReviewQueueAction.QuickAction"/>.</param>
    /// <returns>A task that completes when the action has been persisted or refused.</returns>
    public Task RaiseActionAsync(ReviewQueueAction action, string? quickActionId = null)
    {
        if (ActiveEntry is not { } entry)
        {
            return Task.CompletedTask;
        }

        if (action == ReviewQueueAction.QuickAction && quickActionId is null)
        {
            return Task.CompletedTask;
        }

        return RunActionAsync(entry, action, quickActionId);
    }

    /// <summary>
    /// Forgets every resolved item of this session, so the queue is fully pending again (the host's
    /// own data was never touched). Use it after the host re-fetches the list.
    /// </summary>
    /// <returns>A task that completes when the queue has re-rendered.</returns>
    public async Task ClearHandledAsync()
    {
        if (_handled.Count == 0)
        {
            return;
        }

        _handled.Clear();
        EnsureActiveItem();
        await InvokeAsync(StateHasChanged);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _ordered = BuildOrder();

        PruneHandled();
        AdoptActiveItemParameter();
        EnsureActiveItem();
        AdoptActiveTabParameter();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusTarget != FocusTarget.None)
        {
            await ApplyFocusAsync();
        }

        if (!string.Equals(_activeId, _notifiedActiveId, StringComparison.Ordinal))
        {
            _notifiedActiveId = _activeId;
            if (ActiveItemIdChanged.HasDelegate)
            {
                await ActiveItemIdChanged.InvokeAsync(_activeId);
            }
        }
    }

    // ── Rendering helpers ────────────────────────────────────────────────────

    private string RootClass => string.Concat(
        "tm-review-queue",
        Mode == ReviewQueueMode.Focus ? " tm-review-queue--focus" : " tm-review-queue--list",
        IsBusy ? " tm-review-queue--busy" : string.Empty,
        string.IsNullOrWhiteSpace(Class) ? string.Empty : $" {Class}");

    private string EffectiveAriaLabel => AriaLabel ?? Loc["TmReviewQueue_AriaLabel"];

    private string EffectiveTabsLabel => TabsAriaLabel ?? Loc["TmReviewQueue_TabsLabel"];

    private string EffectiveTabId => _activeTabId ?? string.Empty;

    private string? TabBadge(ReviewQueueTab tab)
        => tab.Count?.ToString(CultureInfo.CurrentCulture);

    /// <summary>The active item with its identity, rank and order.</summary>
    private ItemEntry? ActiveEntry
    {
        get
        {
            if (_activeId is not null)
            {
                var match = _ordered.FirstOrDefault(entry => entry.Id == _activeId);
                if (match is not null)
                {
                    return match;
                }
            }

            // Never fall back to a RESOLVED item: when nothing is pending the Focus mode must show
            // its "everything is reviewed" state, not the last handled card.
            return PendingEntries().FirstOrDefault();
        }
    }

    /// <summary>
    /// The rows carry buttons whenever at least one built-in verb or quick action is configured.
    /// That decides the ARIA pattern: an APG <c>option</c> must not contain interactive controls, so
    /// rows with buttons render as a <c>grid</c> instead of an invalid listbox.
    /// </summary>
    private bool HasRowButtons => EnabledActions().Count > 0 || QuickActions is { Count: > 0 };

    /// <summary>Whether the active item offers any action at all (a read-only queue renders none).</summary>
    private bool HasActions => HasRowButtons;

    private string ListRole => HasRowButtons ? "grid" : "listbox";

    private string RowRole => HasRowButtons ? "row" : "option";

    private string ListLabel => Loc["TmReviewQueue_ListLabel"];

    private bool IsHandled(ItemEntry entry) => _handled.ContainsKey(entry.Id);

    private bool IsActive(ItemEntry entry) => string.Equals(entry.Id, _activeId, StringComparison.Ordinal);

    private string RowClass(ItemEntry entry) => string.Concat(
        "tm-review-queue__row",
        IsActive(entry) ? " tm-review-queue__row--active" : string.Empty,
        IsHandled(entry) ? " tm-review-queue__row--handled" : string.Empty);

    /// <summary>The resolved label of an item ("Approved", "Edited", a quick action's label).</summary>
    private string ResultLabelOf(ItemEntry entry)
        => _handled.TryGetValue(entry.Id, out var result)
            ? result.QuickLabel ?? ResultLabel(result.Action)
            : string.Empty;

    /// <summary>Row content available to the default templates: the risk as a localized word.</summary>
    private string RiskLabel(ApprovalRisk risk) => risk switch
    {
        ApprovalRisk.Medium => Loc["TmReviewQueue_RiskMedium"],
        ApprovalRisk.High => Loc["TmReviewQueue_RiskHigh"],
        ApprovalRisk.Critical => Loc["TmReviewQueue_RiskCritical"],
        _ => Loc["TmReviewQueue_RiskLow"],
    };

    private string RiskIcon(ApprovalRisk risk) => risk switch
    {
        ApprovalRisk.Medium => IconNames.AlertCircle,
        ApprovalRisk.High => IconNames.AlertTriangle,
        ApprovalRisk.Critical => IconNames.Shield,
        _ => IconNames.CheckCircle,
    };

    private BadgeVariant RiskBadgeVariant(ApprovalRisk risk) => risk switch
    {
        ApprovalRisk.Medium => BadgeVariant.Warning,
        ApprovalRisk.High => BadgeVariant.Danger,
        ApprovalRisk.Critical => BadgeVariant.Danger,
        _ => BadgeVariant.Success,
    };

    private string TitleOf(ItemEntry entry) => entry.Title;

    private string? PositionText
    {
        get
        {
            if (ActiveEntry is not { } active || IsHandled(active))
            {
                return null;
            }

            var pending = PendingEntries();
            var index = pending.FindIndex(entry => entry.Id == active.Id);
            return index < 0
                ? null
                : string.Format(
                    CultureInfo.CurrentCulture, Loc["TmReviewQueue_Position"], index + 1, pending.Count);
        }
    }

    private bool CanGoPrevious => ActiveEntry is { } active
        && NavigationPosition(active, PendingEntries()) > 0;

    private bool CanGoNext
    {
        get
        {
            if (ActiveEntry is not { } active)
            {
                return false;
            }

            var pending = PendingEntries();
            return NavigationPosition(active, pending) < pending.Count - 1;
        }
    }

    private string? ErrorMessage => _errorMessage;

    private bool ShowShortcutHint => EnableShortcuts && EnabledActions().Count > 0;

    private string ShortcutHint => Loc["TmReviewQueue_ShortcutsHint"];

    /// <summary>Whether this exact button is the action currently being persisted.</summary>
    private bool IsActingOn(ItemEntry entry, ActionButtonModel button)
        => IsBusy
            && string.Equals(_actionItemId, entry.Id, StringComparison.Ordinal)
            && _actionInFlight == button.Action
            && string.Equals(_quickActionInFlight, button.QuickActionId, StringComparison.Ordinal);

    private string ActionsGroupLabel(ItemEntry entry) => string.Format(
        CultureInfo.CurrentCulture, Loc["TmReviewQueue_ItemActions"], TitleOf(entry));

    private string CurrentItemLabel => Loc["TmReviewQueue_CurrentItem"];

    // ── Ordering / selection ─────────────────────────────────────────────────

    private List<ItemEntry> BuildOrder()
    {
        var items = Items ?? [];
        var entries = new List<ItemEntry>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = IdSelector?.Invoke(item);
            if (string.IsNullOrEmpty(id))
            {
                id = index.ToString(CultureInfo.InvariantCulture);
            }

            entries.Add(new ItemEntry(
                item,
                id,
                index,
                RiskSelector?.Invoke(item) ?? ApprovalRisk.Low,
                PrioritySelector?.Invoke(item) ?? 0,
                TitleSelector?.Invoke(item) ?? item?.ToString() ?? string.Empty,
                SubtitleSelector?.Invoke(item)));
        }

        // A duplicate id would collide in the resolved set and in data-item-id, so the SECOND
        // occurrence is qualified with its index: the host sees its own ids, the DOM stays valid.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            if (!seen.Add(entries[index].Id))
            {
                var unique = $"{entries[index].Id}#{index.ToString(CultureInfo.InvariantCulture)}";
                seen.Add(unique);
                entries[index] = entries[index] with { Id = unique };
            }
        }

        IEnumerable<ItemEntry> ordered = SortMode switch
        {
            ReviewQueueSort.InputOrder => entries.OrderBy(entry => entry.InputIndex),
            ReviewQueueSort.PriorityThenRisk => entries
                .OrderByDescending(entry => entry.Priority)
                .ThenByDescending(entry => entry.Risk)
                .ThenBy(entry => entry.InputIndex),
            _ => entries
                .OrderByDescending(entry => entry.Risk)
                .ThenByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.InputIndex),
        };

        // The Order field is the position in presentation order; it is what navigation and the
        // "next pending after the active one" search use, so a resolved row never breaks them.
        return ordered.Select((entry, index) => entry with { Order = index }).ToList();
    }

    private List<ItemEntry> PendingEntries()
        => _ordered.Where(entry => !_handled.ContainsKey(entry.Id)).ToList();

    private void PruneHandled()
    {
        if (_handled.Count == 0)
        {
            return;
        }

        var live = _ordered.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _handled.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _handled.Remove(id);
        }

        foreach (var id in _rowElements.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _rowElements.Remove(id);
        }
    }

    private void AdoptActiveItemParameter()
    {
        if (string.Equals(ActiveItemId, _activeIdParameter, StringComparison.Ordinal))
        {
            return;
        }

        _activeIdParameter = ActiveItemId;
        if (!string.IsNullOrEmpty(ActiveItemId))
        {
            _activeId = ActiveItemId;
        }
    }

    private void AdoptActiveTabParameter()
    {
        var tabs = Tabs;
        if (tabs is not { Count: > 0 })
        {
            _activeTabId = null;
            return;
        }

        if (!string.IsNullOrEmpty(ActiveTabId) && tabs.Any(tab => tab.Id == ActiveTabId))
        {
            _activeTabId = ActiveTabId;
            return;
        }

        if (_activeTabId is null || tabs.All(tab => tab.Id != _activeTabId))
        {
            _activeTabId = tabs[0].Id;
        }
    }

    private void EnsureActiveItem()
    {
        if (_activeId is not null && _ordered.Any(entry => entry.Id == _activeId))
        {
            return;
        }

        _activeId = PendingEntries().FirstOrDefault()?.Id;
    }

    /// <summary>
    /// Where the active item sits relative to the pending sequence: the number of pending items
    /// before it. For a pending item this is its index; for a resolved item it is the insertion
    /// point, so "next" still lands on the item that followed it.
    /// </summary>
    private static int NavigationPosition(ItemEntry active, IReadOnlyList<ItemEntry> pending)
        => pending.Count(entry => entry.Order < active.Order);

    // ── Actions ──────────────────────────────────────────────────────────────

    private static ReviewQueueActions FlagOf(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => ReviewQueueActions.Approve,
        ReviewQueueAction.Edit => ReviewQueueActions.Edit,
        ReviewQueueAction.Reject => ReviewQueueActions.Reject,
        ReviewQueueAction.Skip => ReviewQueueActions.Skip,
        _ => ReviewQueueActions.None,
    };

    private List<ActionButtonModel> EnabledActions()
    {
        var buttons = new List<ActionButtonModel>(4);
        foreach (var action in new[]
                 {
                     ReviewQueueAction.Approve,
                     ReviewQueueAction.Edit,
                     ReviewQueueAction.Reject,
                     ReviewQueueAction.Skip,
                 })
        {
            var flag = FlagOf(action);
            if (!Actions.HasFlag(flag))
            {
                continue;
            }

            buttons.Add(new ActionButtonModel(
                action,
                null,
                ActionLabel(action),
                ActionIcon(action),
                ActionCssClass(action),
                $"review-{action.ToString().ToLowerInvariant()}",
                EnableShortcuts ? ActionShortcut(action) : null,
                action == ReviewQueueAction.Reject,
                null));
        }

        return buttons;
    }

    private string ActionLabel(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => Loc["TmReviewQueue_Approve"],
        ReviewQueueAction.Edit => Loc["TmReviewQueue_Edit"],
        ReviewQueueAction.Reject => Loc["TmReviewQueue_Reject"],
        _ => Loc["TmReviewQueue_Skip"],
    };

    private static string ActionIcon(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => IconNames.CheckCircle,
        ReviewQueueAction.Edit => IconNames.Edit,
        ReviewQueueAction.Reject => IconNames.XCircle,
        _ => IconNames.SkipForward,
    };

    private static string ActionCssClass(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => "tm-btn-primary",
        ReviewQueueAction.Reject => "tm-btn-danger",
        _ => "tm-btn-ghost",
    };

    private static string ActionShortcut(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => "A",
        ReviewQueueAction.Edit => "E",
        ReviewQueueAction.Reject => "R",
        _ => "S",
    };

    /// <summary>
    /// The buttons of one item: the enabled built-in verbs followed by the host's quick actions.
    /// <paramref name="itemQualifiedName"/> is true for a list row, where several items render the
    /// same verbs — the accessible name then carries the item (WCAG 2.4.6), and the visible verb
    /// stays inside it (WCAG 2.5.3).
    /// </summary>
    private List<ActionButtonModel> ButtonsFor(ItemEntry entry, bool itemQualifiedName)
    {
        var buttons = EnabledActions();

        foreach (var quick in QuickActions ?? [])
        {
            if (string.IsNullOrEmpty(quick.Id) || string.IsNullOrWhiteSpace(quick.Label))
            {
                continue;
            }

            var label = quick.Label;
            buttons.Add(new ActionButtonModel(
                ReviewQueueAction.QuickAction,
                quick.Id,
                label,
                quick.Icon,
                quick.Variant switch
                {
                    ReviewQueueQuickActionVariant.Primary => "tm-btn-primary",
                    ReviewQueueQuickActionVariant.Danger => "tm-btn-danger",
                    _ => "tm-btn-ghost",
                },
                $"review-quick-action-{quick.Id}",
                null,
                quick.Variant == ReviewQueueQuickActionVariant.Danger,
                itemQualifiedName
                    ? quick.AriaLabel ?? string.Format(
                        CultureInfo.CurrentCulture, Loc["TmReviewQueue_ItemActionLabel"], label, TitleOf(entry))
                    : quick.AriaLabel));
        }

        if (itemQualifiedName)
        {
            for (var index = 0; index < buttons.Count; index++)
            {
                var button = buttons[index];
                if (button.AriaLabel is null)
                {
                    buttons[index] = button with
                    {
                        AriaLabel = string.Format(
                            CultureInfo.CurrentCulture,
                            Loc["TmReviewQueue_ItemActionLabel"],
                            button.Label,
                            TitleOf(entry)),
                    };
                }
            }
        }

        return buttons;
    }

    /// <summary>The TmMobileActionBar items of the active item's actions (a mobile Focus mode).</summary>
    private IReadOnlyList<TmActionItem> MobileActionItems(ItemEntry entry)
        => ButtonsFor(entry, itemQualifiedName: false)
            .Select(button => new TmActionItem
            {
                Id = button.QuickActionId is null
                    ? button.Action.ToString().ToLowerInvariant()
                    : $"quick-{button.QuickActionId}",
                Label = button.Label,
                Icon = button.Icon,
                Danger = button.Danger,
                Disabled = IsBusy,
                OnClick = EventCallback.Factory.Create(
                    this, () => RunActionAsync(entry, button.Action, button.QuickActionId)),
            })
            .ToList();

    private async Task RunActionAsync(ItemEntry entry, ReviewQueueAction action, string? quickActionId = null)
    {
        if (IsBusy || IsLoading)
        {
            return;
        }

        if (action == ReviewQueueAction.QuickAction)
        {
            if (QuickActions is null || quickActionId is null
                || QuickActions.All(quick => quick.Id != quickActionId))
            {
                return;
            }
        }
        else if (!Actions.HasFlag(FlagOf(action)))
        {
            return;
        }

        _actionItemId = entry.Id;
        _actionInFlight = action;
        _quickActionInFlight = quickActionId;
        _errorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            if (OnAction is not null)
            {
                await OnAction(new ReviewQueueActionArgs<TItem>(
                    entry.Item, action, Actions, quickActionId));
            }
        }
        catch (Exception)
        {
            _actionItemId = null;
            _actionInFlight = null;
            _quickActionInFlight = null;
            _errorMessage = Loc["TmReviewQueue_Error"];
            _announcement = _errorMessage;
            // Return to the element that owns the keyboard contract (the row, or the Focus view):
            // the queue root does not handle keys any more — the tab strip there owns its own.
            _focusTarget = Mode == ReviewQueueMode.Focus ? FocusTarget.Detail : FocusTarget.Row;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _actionItemId = null;
        _actionInFlight = null;
        _quickActionInFlight = null;

        if (action != ReviewQueueAction.Skip)
        {
            _handled[entry.Id] = new HandledResult(
                action,
                action == ReviewQueueAction.QuickAction ? QuickActionLabel(quickActionId) : null);
        }

        AdvanceAfterAction(entry, action == ReviewQueueAction.Skip);
        _announcement = BuildAnnouncement(entry, action, quickActionId);
        _focusTarget = Mode == ReviewQueueMode.Focus ? FocusTarget.Detail : FocusTarget.Row;

        await InvokeAsync(StateHasChanged);
    }

    private string? QuickActionLabel(string? quickActionId)
        => QuickActions?.FirstOrDefault(quick => quick.Id == quickActionId)?.Label;

    private void AdvanceAfterAction(ItemEntry entry, bool skip)
    {
        var pending = PendingEntries();

        if (skip)
        {
            // A skip never resolves the item, so it stays in the sequence: move to the next pending
            // item after it and wrap to the first one when it was the last.
            var next = pending.FirstOrDefault(candidate => candidate.Order > entry.Order)
                ?? pending.FirstOrDefault(candidate => candidate.Id != entry.Id)
                ?? pending.FirstOrDefault();
            _activeId = next?.Id ?? entry.Id;
            return;
        }

        if (pending.Count == 0)
        {
            _activeId = null;
            return;
        }

        if (!AutoAdvance)
        {
            // The reviewer asked to read the result before moving on. Keeping a resolved item
            // selected is only useful while something is left; with an empty queue the empty state
            // takes over.
            _activeId = entry.Id;
            return;
        }

        var nextPending = pending.FirstOrDefault(candidate => candidate.Order > entry.Order)
            ?? pending[0];
        _activeId = nextPending.Id;
    }

    private string BuildAnnouncement(ItemEntry entry, ReviewQueueAction action, string? quickActionId)
    {
        var verb = action == ReviewQueueAction.QuickAction
            ? QuickActionLabel(quickActionId) ?? ResultLabel(ReviewQueueAction.Edit)
            : ResultLabel(action);
        var remaining = string.Format(
            CultureInfo.CurrentCulture, Loc["TmReviewQueue_Remaining"], PendingEntries().Count);

        return string.Format(
            CultureInfo.CurrentCulture,
            Loc["TmReviewQueue_Announcement"],
            verb,
            TitleOf(entry),
            remaining);
    }

    private string ResultLabel(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => Loc["TmReviewQueue_ResultApproved"],
        ReviewQueueAction.Edit => Loc["TmReviewQueue_ResultEdited"],
        ReviewQueueAction.Reject => Loc["TmReviewQueue_ResultRejected"],
        ReviewQueueAction.Skip => Loc["TmReviewQueue_ResultSkipped"],
        _ => Loc["TmReviewQueue_ResultEdited"],
    };

    // ── Selection / navigation ───────────────────────────────────────────────

    private async Task SelectAsync(ItemEntry entry)
    {
        if (IsHandled(entry) || IsActive(entry))
        {
            return;
        }

        _activeId = entry.Id;
        await InvokeAsync(StateHasChanged);
    }

    private Task MoveAsync(int delta)
    {
        var pending = PendingEntries();
        if (pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        int targetIndex;
        if (ActiveEntry is { } active)
        {
            var position = NavigationPosition(active, pending);
            targetIndex = position + delta;
        }
        else
        {
            targetIndex = 0;
        }

        if (targetIndex < 0 || targetIndex >= pending.Count)
        {
            return Task.CompletedTask;
        }

        return MoveSelectionToAsync(pending[targetIndex]);
    }

    private Task MoveToEdgeAsync(bool first)
    {
        var pending = PendingEntries();
        if (pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        return MoveSelectionToAsync(first ? pending[0] : pending[^1]);
    }

    private async Task MoveSelectionToAsync(ItemEntry entry)
    {
        if (IsActive(entry))
        {
            return;
        }

        _activeId = entry.Id;
        // Focus follows the selection: a row in list mode, the detail card in Focus mode (there are
        // no rows there, and the detail is what the reader needs to hear change).
        _focusTarget = Mode == ReviewQueueMode.Focus ? FocusTarget.Detail : FocusTarget.Row;
        await InvokeAsync(StateHasChanged);
    }

    private Task MoveNextAsync() => MoveAsync(1);

    private Task MovePreviousAsync() => MoveAsync(-1);

    /// <summary>
    /// The "next item" button of the resolved detail (AutoAdvance=false): the resolved item is
    /// selected, so the plain next arrow is disabled — this finds the first pending item after it.
    /// </summary>
    private async Task MoveToEdgeAfterHandled()
    {
        var pending = PendingEntries();
        if (pending.Count == 0 || ActiveEntry is not { } active)
        {
            return;
        }

        var next = pending.FirstOrDefault(entry => entry.Order > active.Order) ?? pending[0];
        _activeId = next.Id;
        _focusTarget = FocusTarget.Detail;
        await InvokeAsync(StateHasChanged);
    }

    private Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Repeat)
        {
            return Task.CompletedTask;
        }

        // Navigation is the widget's own keyboard contract: arrows, Home and End work even when the
        // author turned the letter shortcuts off.
        switch (args.Key)
        {
            case "ArrowDown":
                return MoveAsync(1);
            case "ArrowUp":
                return MoveAsync(-1);
            case "Home":
                return MoveToEdgeAsync(first: true);
            case "End":
                return MoveToEdgeAsync(first: false);
        }

        if (!EnableShortcuts || args.CtrlKey || args.MetaKey || args.AltKey)
        {
            return Task.CompletedTask;
        }

        return args.Key switch
        {
            "j" or "J" => MoveAsync(1),
            "k" or "K" => MoveAsync(-1),
            "a" or "A" => RaiseForActiveAsync(ReviewQueueAction.Approve),
            "e" or "E" => RaiseForActiveAsync(ReviewQueueAction.Edit),
            "r" or "R" => RaiseForActiveAsync(ReviewQueueAction.Reject),
            "s" or "S" => RaiseForActiveAsync(ReviewQueueAction.Skip),
            _ => Task.CompletedTask,
        };
    }

    private async Task RaiseForActiveAsync(ReviewQueueAction action)
    {
        if (IsBusy || ActiveEntry is not { } entry)
        {
            return;
        }

        await RunActionAsync(entry, action);
    }

    // ── Focus ────────────────────────────────────────────────────────────────

    private async Task ApplyFocusAsync()
    {
        var target = _focusTarget;
        _focusTarget = FocusTarget.None;

        var (element, preventScroll) = target switch
        {
            FocusTarget.Row when _activeId is not null && _rowElements.TryGetValue(_activeId, out var row)
                => (row, false),
            FocusTarget.Detail when ActiveEntry is not null => (_focusElement, false),
            _ => (_rootElement, true),
        };

        try
        {
            // A row or the Focus card may sit outside the viewport (a list taller than the screen,
            // ArrowDown/End/J): the browser has to scroll it into view, otherwise the focus ring is
            // moved somewhere nobody can see it (WCAG 2.4.7). Only the ROOT fallback — the queue
            // chrome, where the reader already is — keeps its scroll position.
            await element.FocusAsync(preventScroll);
        }
        catch (JSDisconnectedException)
        {
            // Blazor Server: the circuit dropped, so there is no focus to move.
        }
        catch (JSException)
        {
            // The element left the DOM between the render and the focus call.
        }
        catch (InvalidOperationException)
        {
            // No renderer, or the element reference is no longer attached.
        }
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────

    private async Task OnTabChangedAsync(string tabId)
    {
        _activeTabId = tabId;

        if (ActiveTabIdChanged.HasDelegate)
        {
            await ActiveTabIdChanged.InvokeAsync(tabId);
        }

        await InvokeAsync(StateHasChanged);
    }

    // ── Local records ────────────────────────────────────────────────────────

    /// <summary>One item with everything the render needs, computed once per parameter set.</summary>
    /// <param name="Item">The host's item.</param>
    /// <param name="Id">Stable identity.</param>
    /// <param name="InputIndex">The position the host supplied.</param>
    /// <param name="Risk">Numeric risk rank.</param>
    /// <param name="Priority">Priority rank (higher first).</param>
    /// <param name="Title">Resolved heading.</param>
    /// <param name="Subtitle">Resolved secondary line.</param>
    /// <param name="Order">Position in the presentation order.</param>
    private sealed record ItemEntry(
        TItem Item,
        string Id,
        int InputIndex,
        ApprovalRisk Risk,
        int Priority,
        string Title,
        string? Subtitle,
        int Order = 0);

    /// <summary>A rendered action button: verb, label, icon, test id and the shortcut it advertises.</summary>
    /// <param name="Action">The verb the button raises.</param>
    /// <param name="QuickActionId">The quick action's id, when it is a quick action.</param>
    /// <param name="Label">The visible label.</param>
    /// <param name="Icon">Optional icon name.</param>
    /// <param name="CssClass">The tm-btn variant class.</param>
    /// <param name="TestIdName">The component-local test id stem.</param>
    /// <param name="Shortcut">The <c>aria-keyshortcuts</c> value, when the letter shortcuts are on.</param>
    /// <param name="Danger">Whether the button takes the danger colour.</param>
    /// <param name="AriaLabel">The accessible name override (a row's item-qualified name).</param>
    private sealed record ActionButtonModel(
        ReviewQueueAction Action,
        string? QuickActionId,
        string Label,
        string? Icon,
        string CssClass,
        string TestIdName,
        string? Shortcut,
        bool Danger,
        string? AriaLabel);

    /// <summary>What resolved an item: the verb and, for a quick action, its host label.</summary>
    /// <param name="Action">The verb that resolved the item.</param>
    /// <param name="QuickLabel">The quick action's label, when one fired.</param>
    private sealed record HandledResult(ReviewQueueAction Action, string? QuickLabel);

    /// <summary>
    /// Which element the next render should move focus to. There is deliberately no "root" entry:
    /// the queue root does not own keys (the tab strip there does), so a focus move always targets
    /// the list row or the Focus view — the two keyboard surfaces.
    /// </summary>
    private enum FocusTarget
    {
        None,
        Row,
        Detail,
    }
}
