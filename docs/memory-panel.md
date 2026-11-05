# TmMemoryPanel — what the agent remembers

`TmMemoryPanel` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) shows the facts an agent stored about the
workspace and lets the reader act on them: a master switch, category groups with counts, an inline edit, pause and resume,
a removal that can be undone, the review of what the agent proposed, a search and a status filter, and bulk actions over a
selection.

The shared identity model and the accessibility rules every agent surface follows are documented once in
[ai-agents.md](ai-agents.md). This page is the `TmMemoryPanel` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmMemoryPanel Items="_items"
               @bind-Enabled="_enabled"
               @bind-FilterText="_filter"
               OnEdit="OnEdit"
               OnPause="OnPause"
               OnResume="OnResume"
               OnForget="OnForget"
               OnReview="OnReview"
               OnBulkAction="OnBulk" />
```

## The model

| Type | What it is |
|---|---|
| `MemoryItem` | One fact: `Id`, `Category`, `Text`, `Status` (`MemoryItemStatus`), `Confidence?` (a RATIO in 0…1), `Source?`, `CreatedAt`, `UpdatedAt?`, plus `HasCategory`, `HasConfidence`, `HasSource`, `HasBeenUpdated`, `IsActive` / `IsPaused` / `IsForgotten` / `IsPendingReview`, `HasKnownStatus`. A record VALUE. |
| `MemoryItemStatus` | `Active` (0, the default) \| `Paused` \| `Forgotten` \| `PendingReview`. |
| `MemoryReview` / `MemoryReviewDecision` | The reader's decision about a proposal: the item plus `Approve` or `Reject`. |
| `MemoryBulkAction` / `MemoryBulkActionRequest` | What to do with a SELECTION (`Pause`, `Resume`, `Forget`) and the copied list of items the reader acted on. |
| `MemoryForgetMode` | `UndoWindow` (0, the default) \| `Immediate`. |
| `MemoryStatusFilter` | `All` (0, the default) \| `Active` \| `Paused` \| `PendingReview` \| `Forgotten`. |

`Active` and `UndoWindow` are the defaults on purpose: an unclassified memory must never claim it was forgotten, and
losing a fact without a way back must never be the unset behaviour. `Id` is REQUIRED and must be stable in the host's
store — a blank value throws `ArgumentException`, because an id invented at construction would change on every rebuild of
the snapshot and silently drop the row's selection, edit and undo state. A blank `Category` means "no category" — every
uncategorised memory shares the ONE localized **Uncategorized** group instead of a blank heading. Nothing is invented: an
unmeasured `Confidence` (null, negative, above 1 or NaN) and a missing `Source` print nothing at all, and `UpdatedAt` is
optional.

## Groups

The rows are grouped by category (trimmed, ordinal), the groups are ordered by their DISPLAY name in the current culture,
and each group is an APG disclosure:

- the header is a native `<button>` with `aria-expanded` and `aria-controls` (the list's id); a collapsed group keeps
  nothing in the DOM, and collapsing never moves the focus — the button the reader pressed is still there;
- the count badge is part of the button's accessible name ("Preferences 2");
- `CollapsedCategories` / `CollapsedCategoriesChanged` carry the folded keys two-way, and a key the host pins starts
  collapsed;
- with `Selectable` on, the header also carries a select-all checkbox for the group's VISIBLE rows.

## The master switch

`Enabled` / `EnabledChanged` (`@bind-Enabled`) is the storage switch. While it is off:

- the panel still lists what is already known (the reader came to see it), and says so
  (`TmMemoryPanel_OffNote`);
- **no** action is offered: no edit, pause, forget, review or selection;
- the switch itself stays enabled, because a reader must be able to turn memory back on.

The component keeps the reader's pick in its own state: a host that re-renders the SAME value cannot undo it, and the host
wins as soon as the parameter actually MOVES.

## A row

A row states its status as an ICON plus a localized WORD plus a tone (`data-status="active|paused|pending-review|
forgotten"`), then the confidence as a percentage in the bound culture, the source ("From: …") and the timestamp
("Updated …", or "Learned …" when the host never updated it). The actions are native buttons:

| Status | Actions |
|---|---|
| `Active` | Edit, Pause, Forget |
| `Paused` | Edit, Resume, Forget |
| `PendingReview` | Edit, Approve, Reject, Forget (no Pause — a proposal cannot be paused before it is accepted) |
| `Forgotten` | Edit, Pause/Resume, Forget (the row is a record of a removal, still readable) |

### Inline edit

Edit turns the text into a `<textarea>` and moves the focus into it. **Enter** commits, **Shift+Enter** writes a new line,
**Escape** rolls the draft back, and the Save / Cancel buttons are the pointer alternative (WCAG 2.5.7). An unchanged
draft closes without reporting a second time, and opening another row's editor discards the first draft — it is never a
save. The committed text is shown immediately (the panel keeps its own copy) and `OnEdit` reports the new instance; the
focus returns to that row's Edit button, so the reader does not fall back to the top of the page (WCAG 2.4.3).

### Forgetting, with an undo window

`Forget` removes the row at once. With `ForgetMode="UndoWindow"` (the default) the host is told only when the window
closes, and one undo bar stays on screen with the memory, an **Undo** action and the remaining seconds (driven by the
injected `TimeProvider`, so a test controls it without sleeping):

- **Undo** puts the row back and the host is never told;
- when the window expires the host receives `OnForget(item)` exactly once;
- opening a second window reports the previous removal IMMEDIATELY — nothing is lost silently;
- disposing the panel (navigation) commits a pending removal — `OnForget` fires once, because the undo bar the reader
  would have used is gone with the page;
- `UndoWindow` of zero (or negative) behaves like `Immediate`, because a window nobody can reach would silently lose the
  memory.

`ForgetMode="Immediate"` reports `OnForget` at once and offers no undo.

### Review

A `PendingReview` row is what the agent proposed. **Approve** reports `OnReview(new MemoryReview(item,
MemoryReviewDecision.Approve))` and the row becomes active; **Reject** reports the rejection and the row becomes forgotten
(it stays visible under the default filter as the record of the decision). The component reports the decision and never
writes the host's store.

## Search and filter

The search field is a native `<input type="search">` (named and hinted: the accessible name "Search memory" plus the same placeholder) that narrows the rows by the memory's TEXT, its CATEGORY and its
SOURCE (case-insensitively) and hides the groups that end up empty. The status filter is a segmented radio group
(All / Active / Paused / To review / Forgotten) whose segments carry the stable slugs `all`, `active`, `paused`,
`pending-review` and `forgotten`.

Both are two-way (`@bind-FilterText`, `@bind-StatusFilter`) and both are a VIEW: they never report anything to the host.
Each change is announced ONCE ("Showing {n} of {total} memories." — a plural family). An empty panel says "No memory yet.";
a filter with no hits says "No memory matches the search or the filter." — two honest, different sentences.

## Bulk actions

With `Selectable` (the default) every row carries a checkbox named by its text, each group header a select-all. Selecting
anything raises a bar above the groups with the count and four native buttons: Pause, Resume, Forget and Clear selection.

A bulk action is ONE request: `OnBulkAction(new MemoryBulkActionRequest(items, action))` fires once with a COPY of the
selection (in the order the host supplied), and a bulk removal reports through that callback only — it does not fire
`OnForget` per row. The selection is cleared after the action and pruned whenever a filter hides a selected row, so a
reader can never act on a memory they can no longer see.

## Async callbacks, pending and failure

Every action awaits its callback. While it runs:

- the row's buttons disable (a second press must not double the request) and a "Saving…" note appears;
- the root carries `data-busy="true"` and `aria-busy="true"`.

If the callback THROWS, the surface prints the localized `The change could not be saved: {message}` next to the rows,
announces it, and leaves the row untouched — a failed request changes nothing, not even locally.

## Accessibility

- Every state is a WORD plus an icon; the tone is the third, redundant cue.
- Groups are real disclosures with native buttons; checkboxes are native `<input type="checkbox">` (Space is the
  browser's own activation, never emulated).
- Enter commits an edit and Escape rolls it back; the focus moves into the saved row (or back to the Edit button on cancel), so it never falls to the document (WCAG 2.4.3).
- ONE polite live region announces every change — including a failure — and an identical sentence is re-announced through
  an invisible marker. There is no second region: the visible error text is the announcement.
- The undo bar's countdown is a live-updated text (not an animation) and the bar itself never steals the focus.
- `(pointer: coarse)` raises every button, the search field and even the checkboxes to the 44 px `--tm-touch-target`.
- `forced-colors` hands the borders to `CanvasText`, the notes to `CanvasText` and every focus ring to a 3 px `Highlight`
  outline.
- The partial declares no transition and no animation: every state is also text.

## CSS

`.tm-ai-memory`, `--off`, `__header`, `__title`, `__switch`, `__hint`, `__off-note`, `__toolbar`, `__search`,
`__filter`, `__bulk`, `__bulk-count`, `__bulk-actions`, `__bulk-action`, `__groups`, `__group`, `__group-head`,
`__group-title`, `__group-toggle`, `__group-chevron`, `__group-name`, `__group-count`, `__group-select`, `__list`,
`__item`, `__select`, `__body`, `__text`, `__edit-box`, `__edit-actions`, `__save`, `__cancel`, `__meta`, `__status`,
`__status-icon`, `__status-text`, `__confidence`, `__source`, `__updated`, `__actions`, `__action`,
`__action--danger`, `__busy`, `__empty`, `__error`, `__undo`, `__undo-text`, `__undo-action`, `__undo-timer` — tokens
only. A status modifier SETS `--tm-memory-status-ink` / `-fill` / `-border` and the base chip READS them; a forgotten row
keeps `text-decoration: line-through` as a second, non-colour cue. Guarded by `AiMarkdownViewCssTests` and
`TmMemoryPanelCssTests`.

## Localization

`TmMemoryPanel_*` (title, switch label and hint, the off note, the search label, its placeholder and the filter label, the five filter words, the
four status words and the unknown fallback, the uncategorized group name, the confidence / source / learned / updated
formats, the edit / save / cancel / pause / resume / forget / approve / reject labels, the busy note, both empty states,
the selection labels, the bulk bar and its four actions, the undo sentence, action and countdown, the plural "showing"
family, the failure sentence, and the announcements including the three bulk plural families) in English, Czech, Slovak
and French.

## Wireframe

The `memory-panel` UI role (synonyms in both languages) plus the `TmMemoryPanel` schema (`title`, `memories`, `enabled`,
`filter`) and its declarative stencil, which draws the heading, the switch, the filter chip, a category header with its
count and one row per supplied memory with its status rail and chip, plus the row action hint.

## Demo

`/ai-agents` → the Memory section: six host memories across three categories, one paused, one the agent proposed and one
already forgotten. The PAGE is the store — every callback is applied to the page's own list (a replacement INSTANCE, which
is what makes the panel drop its local copy) — and the page's log line names what it received, so an undone removal is
visibly never reported.
