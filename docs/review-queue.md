# TmReviewQueue

A queue of items a human has to decide on. It sorts items by risk and priority, shows each item with
a localized risk badge and its own decisions, offers filter tabs with counts, and can switch from a
list to a one-at-a-time Focus view with a position counter ("Item 3 of 12") and previous/next
navigation.

Plan: `10b3966f` (human-in-the-loop, phase 3). Package: `Tempo.Blazor` (core).
Models: `Tempo.Blazor.Abstractions.Models.Reviews`.

```razor
@using Tempo.Blazor.Components.Approvals
@using Tempo.Blazor.Abstractions.Models.Approvals
@using Tempo.Blazor.Abstractions.Models.Reviews

<TmReviewQueue TItem="InvoiceDto"
               Items="@_invoices"
               IdSelector="invoice => invoice.Id"
               TitleSelector="invoice => invoice.Number"
               SubtitleSelector="invoice => invoice.Supplier"
               RiskSelector="invoice => invoice.Risk"
               PrioritySelector="invoice => invoice.Priority"
               Actions="ReviewQueueActions.Decisions"
               OnAction="HandleActionAsync"
               Tabs="@_tabs"
               @bind-ActiveTabId="_activeTabId"
               @bind-ActiveItemId="_activeInvoiceId" />
```

## The queue is controlled over a host-owned list

`Items` is whatever the host passes. The queue sorts it, marks resolved items, and **never removes an
item from it**: a resolved item stays in the list as a collapsed result row, so the reviewer keeps
the audit trail. The resolved set lives in the queue's own session state; `HandledItemIds` exposes
it, `ClearHandledAsync()` forgets it (call it after re-fetching the list), and ids that leave `Items`
are pruned automatically.

`OnAction` is `Func<ReviewQueueActionArgs<TItem>, Task>`. While it runs the queue is
`aria-busy="true"` and every action button is disabled. The item is resolved — and the selection
advances — **only** when it completes without throwing; a throw shows a localized error in a
`TmAlert`, announces it, and leaves the item pending, because a failed save must never look like a
decision.

## Modes

| Mode | What renders |
|---|---|
| `ReviewQueueMode.List` (default) | Every item as a selectable row with its own decisions. The container is an APG `listbox` when the rows carry no buttons and a `grid` when they do — an `option` must not contain interactive controls, so the pattern follows the content instead of shipping invalid ARIA. |
| `ReviewQueueMode.Focus` | One item at a time: the `DetailTemplate`, a localized position ("Item 3 of 12"), previous/next, and one shared action group. On a mobile container the actions move into `TmMobileActionBar` (no duplicated tab stops). When nothing is pending any more it shows the "Everything is reviewed" state. |

`Mode` is a plain parameter: the queue has no built-in mode switch, because where that control
belongs is the host's chrome.

## Ordering

`SortMode` decides the order; sorting is stable, so items with an equal rank keep the order the host
supplied.

| `SortMode` | Order |
|---|---|
| `RiskThenPriority` (default) | Risk descending (Critical → High → Medium → Low), then the highest `PrioritySelector` value. |
| `PriorityThenRisk` | The highest `PrioritySelector` value first, then risk. |
| `InputOrder` | The order of `Items`, untouched. |

`PrioritySelector` returns an integer where a **higher** value ranks first; omit it and priority is
constant. `RiskSelector` returns an `ApprovalRisk` (the model `TmApprovalCard` uses too) and defaults
to `Low`.

## Actions and the decision-inbox variant

`Actions` is a flags set of the built-in verbs — `Approve` (A), `Edit` (E), `Reject` (R), `Skip` (S),
the `Decisions` pair (Approve + Reject) or `All`. Every button carries its `aria-keyshortcuts` while
`EnableShortcuts` is true.

`QuickActions` adds host-defined buttons to the same group — the "decision inbox" variant, where an
item is resolved with two purpose-named buttons instead of the built-in verbs:

```razor
<TmReviewQueue TItem="InvoiceDto"
               Items="@_invoices"
               Actions="ReviewQueueActions.None"
               QuickActions="@_quickActions"
               OnAction="HandleActionAsync" />

@code {
    private readonly ReviewQueueQuickAction[] _quickActions =
    [
        new() { Id = "file", Label = "Approve & file", Icon = IconNames.CheckCircle,
                Variant = ReviewQueueQuickActionVariant.Primary },
        new() { Id = "return", Label = "Send back to the author",
                Variant = ReviewQueueQuickActionVariant.Danger },
    ];

    private Task HandleActionAsync(ReviewQueueActionArgs<InvoiceDto> args) => args.QuickActionId switch
    {
        "file" => ApproveAndFileAsync(args.Item),
        "return" => ReturnToAuthorAsync(args.Item),
        _ => HandleBuiltInAsync(args),
    };
}
```

A quick action raises `OnAction` with `Action = ReviewQueueAction.QuickAction` and its id in
`QuickActionId`; it resolves the item like any other decision and its `Label` is the verb the live
region announces. Unlike the built-in verbs it has no keyboard shortcut — its visible label is
host-defined, so a fixed letter would be a guess.

`Skip` is the one verb that does **not** resolve the item: it calls `OnAction` (a host may want to
record the deferral), keeps the item pending and moves the selection on.

## Tabs

`Tabs` renders a `TmTabs` strip with an optional count badge per tab. The queue raises
`ActiveTabIdChanged` and does **not** filter `Items` — only the host knows what "Open" or "Needs my
review" means for its data, so it passes the filtered list back. The counts are the host's numbers
for the same queries, which keeps a tab badge and its list in agreement.

The tab strip keeps its **own** keyboard contract. The queue's key handler sits on the list body and
the Focus view — never on the queue root — so `ArrowLeft`/`ArrowRight`/`Home`/`End` pressed on a tab
move the tab selection only, and `A`/`E`/`R`/`S`/`J`/`K` on a focused tab never approve, reject,
skip or move an item the reviewer never focused.

## Selection and focus

`ActiveItemId` is the selected item (the row that owns the roving tabindex and, in Focus mode, the
item whose detail renders). It is two-way: a click, an arrow key, a skip, auto-advance and
`RaiseActionAsync` all raise `ActiveItemIdChanged`; when the parameter is null or names an item that
is gone, the first pending item is selected.

- Arrow Down / Arrow Up move the selection and real DOM focus with it; `Home` / `End` jump to the
  first / last pending item. Navigation is the widget's own keyboard contract and stays active even
  with `EnableShortcuts="false"`. The focused row is **scrolled into view**: in a list taller than
  the viewport a focus ring that stays off screen is not a focus move (WCAG 2.4.7).
- `J` / `K` do the same and are part of the shortcut opt-out; the previous/next buttons advertise
  them through `aria-keyshortcuts` so the two are consistent with the `A`/`E`/`R`/`S` buttons.
- `AutoAdvance` (default true) moves the selection to the next pending item after a decision. With
  `false` the resolved item stays selected and shows its result with a localized "Go to the next
  item" button, so a reviewer can read the outcome before moving on. When nothing is left, the empty
  state takes over in either case.

## Accessibility

- **One polite live region.** Every result and every error is announced there:
  `"Approved: Invoice #2042. Remaining: 3."`. Nothing else in the queue claims a live role; the busy
  indicator has no role at all (`aria-busy` on the queue expresses that state), and the error alert
  renders as `role="group"`.
- **The ARIA pattern follows the content.** Rows with buttons are a `grid` (row → gridcell), rows
  without are a `listbox` (option). The selected row carries `aria-selected`; a resolved row carries
  `aria-disabled="true"`, is never a tab stop and is skipped by navigation.
- **Repeated buttons are named by their item.** In a list row every button's accessible name is
  `"Approve: Invoice #2042"`; the visible verb stays inside the name (WCAG 2.5.3), and a screen
  reader's button list no longer reads four identical "Approve" entries (WCAG 2.4.6).
- **Shortcuts never fire while typing.** They are disableable (`EnableShortcuts="false"`, WCAG 2.1.4)
  and advertised through `aria-keyshortcuts`. The queue fences its own template slots — the row body
  and the Focus detail — with `@onkeydown:stopPropagation`, so a `<textarea>`, a `<select>` or a text
  input inside `ItemTemplate` / `DetailTemplate` keeps its arrows, `Home` and `End` even when the host
  forgets to fence them. A host fence on its own controls stays supported and recommended as defence
  in depth (the same rule as `TmDataTable`'s cell templates — see
  [keyboard-activation-convention.md](keyboard-activation-convention.md)).
- **Keyboard ownership is per surface.** Navigation and shortcuts are handled by the list body and
  the Focus view, not by the queue root: the `TmTabs` strip inside the root owns `Home`/`End` and its
  own arrows, so mixing the two never moves the queue while a reviewer is picking a tab.
- **The collapse is visual, the state is not.** A resolved row and its result half stay mounted so the
  height can transition; the inactive half is `inert`, which removes it from the tab order and the
  accessibility tree. The transition is disabled under `prefers-reduced-motion`.
- **Touch.** On `(pointer: coarse)` every action and navigation button is at least
  `--tm-touch-target` (44 px). The mobile Focus mode reuses `TmMobileActionBar` rather than rendering
  a second action group.

## Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Items` | `IReadOnlyList<TItem>?` | `null` | The items to review, normally already filtered by `ActiveTabId`. Never mutated or removed by the queue. |
| `IdSelector` | `Func<TItem, string>?` | `null` | Stable, unique identity used as `data-item-id` and as the key of the resolved set. Falls back to the item's index. |
| `TitleSelector` | `Func<TItem, string>?` | `null` | The row heading, also used in announcements. Falls back to `ToString()`. |
| `SubtitleSelector` | `Func<TItem, string?>?` | `null` | Optional secondary line. |
| `RiskSelector` | `Func<TItem, ApprovalRisk>?` | `Low` | The item's risk; drives the badge and `RiskThenPriority`. |
| `PrioritySelector` | `Func<TItem, int>?` | `0` | The item's priority — a higher value ranks first. |
| `ItemTemplate` | `RenderFragment<TItem>?` | `null` | Extra row body rendered above the row's decisions. |
| `DetailTemplate` | `RenderFragment<TItem>?` | `null` | The Focus mode body (a diff, a preview, key/value pairs). |
| `EmptyContent` | `RenderFragment?` | `null` | Replaces the built-in "Nothing to review" state. |
| `LoadingContent` | `RenderFragment?` | `null` | Replaces the built-in skeleton rows. |
| `IsLoading` | `bool` | `false` | Renders the skeleton, marks the queue `aria-busy` and ignores actions. |
| `Mode` | `ReviewQueueMode` | `List` | `List` (rows) or `Focus` (one item at a time). |
| `SortMode` | `ReviewQueueSort` | `RiskThenPriority` | The presentation order. |
| `Actions` | `ReviewQueueActions` | `All` | The built-in verbs offered per item. |
| `QuickActions` | `IReadOnlyList<ReviewQueueQuickAction>?` | `null` | Host-defined buttons rendered with the built-in verbs. |
| `OnAction` | `Func<ReviewQueueActionArgs<TItem>, Task>?` | `null` | Persists a decision. A throw keeps the item pending and shows the localized error. |
| `Tabs` | `IReadOnlyList<ReviewQueueTab>?` | `null` | Filter tabs with optional count badges. |
| `ActiveTabId` / `ActiveTabIdChanged` | `string?` / `EventCallback<string>` | first tab | Two-way tab selection; the host filters `Items`. |
| `ActiveItemId` / `ActiveItemIdChanged` | `string?` / `EventCallback<string?>` | first pending | Two-way item selection. |
| `AutoAdvance` | `bool` | `true` | Hands the selection to the next pending item after a decision. |
| `EnableShortcuts` | `bool` | `true` | Enables the A / E / R / S and J / K keys. |
| `AriaLabel` | `string?` | localized "Review queue" | Accessible name of the queue group. |
| `TabsAriaLabel` | `string?` | localized "Review tabs" | Accessible name of the tab strip. |
| `Class` | `string?` | `null` | Additional CSS classes for the root. |
| `LayoutMode` | `TmLayoutMode` | `Auto` | The layout Focus mode resolves its action presentation from; a forced value pins it. |
| `InitialMode` | `TmLayoutMode` | `Desktop` | The layout rendered before the first measurement. |

Public state and methods: `OrderedItems`, `PendingItems`, `PendingCount`, `HandledItemIds`,
`ActiveItemKey`, `ActiveItem`, `IsBusy`, `IsLoadingItems`, `RaiseActionAsync(action, quickActionId)`
and `ClearHandledAsync()`.

## Empty and loading states

- No items at all → the "Nothing to review" empty state (or `EmptyContent`).
- Focus mode with items but nothing pending → the "Everything is reviewed" empty state.
- List mode keeps the resolved rows visible as collapsed result rows, so "all handled" still shows
  what happened.
- `IsLoading` renders three skeleton rows inside an `aria-busy` region and suppresses the empty
  state, so a slow first load never lies about an empty queue.

## CSS

`_review-queue.css` ships with the core bundle (`tempo-blazor.css` / `tempo-blazor.bundled.css`).
Every value is a `--tm-*` token, so the dark theme and a host's token overrides apply unchanged. The
root carries `data-mode` (`list` / `focus`) and `data-layout` (`desktop` / `tablet` / `mobile`), and
the BEM modifiers `tm-review-queue--list`, `--focus` and `--busy`.

## Migration

Nothing to migrate: the component, its models, its resource keys and its stylesheet are new, and no
existing component changed.
