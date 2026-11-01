# TmInlineDiff

An inline comparison of two texts: the changes are rendered as real `<ins>` / `<del>` elements with a
visually hidden label, and in Review mode every change is a decision a human can accept or reject. The
comparison itself comes from the shared `Tempo.Blazor.Diff` engine (`TextDiff`), so the component and
the two historical diff helpers (`DocumentTextDiffHelper`, `TmTextLineDiff`) never disagree.

Plan: `10b3966f` (human-in-the-loop, phase 4). Package: `Tempo.Blazor` (core).
Models: `Tempo.Blazor.Abstractions.Models.Diffs` (`InlineDiffMode`, `InlineDiffDecision`),
`Tempo.Blazor.Diff` (`TextDiff`, `TextDiffResult`, `TextDiffHunk`, `TextDiffOptions`).

```razor
@using Tempo.Blazor.Components.Approvals
@using Tempo.Blazor.Abstractions.Models.Diffs

<TmInlineDiff OldText="@original"
              NewText="@proposed"
              Mode="InlineDiffMode.Review"
              AcceptedHunkIds="@accepted"
              AcceptedHunkIdsChanged="@(ids => accepted = ids)"
              ResultChanged="@(text => result = text)"
              OnDecision="@PersistAsync" />
```

## Modes

| Mode | What it renders |
|---|---|
| `InlineDiffMode.Display` (default) | The `<ins>` / `<del>` pairs with their hidden labels, the equal text between them, and the localized fallback note when the comparison degraded. No actions at all. |
| `InlineDiffMode.Review` | The same diff plus the toolbar (`Changes: N`, Accept all, Reject all, Undo), one marker button per change and the non-modal Accept / Reject popover. |
| `ReadOnly="true"` | Removes every decision surface even in `Review` — the effective switch is `IsReview` (public), and the root carries `data-readonly="true"`. |

The root carries `data-mode` (`display` / `review`), `data-granularity` (`words` / `lines` — the
granularity the engine actually used) and `data-fallback` (`none` / `line-level` / `single-change`).

## Every change is one decision

A replacement is ONE decision even though it renders both sides: the change span carries
`data-kind="replace"`, the `del` and the `ins`. The marker button is labelled with its position, its
kind and its current state — "Change 1 of 2: replaced text, not reviewed. Review the change." — and
declares `aria-haspopup`, `aria-expanded` and `aria-controls` for the popover it owns.

```
<span class="tm-inline-diff__change" data-hunk-id="replace-6-6" data-state="pending">
  <span class="tm-sr-only">Removed: </span><del>beta </del>
  <span class="tm-sr-only">Inserted: </span><ins>BETA </ins>
  <button aria-haspopup="true" aria-expanded="false" …>…</button>
</span>
```

## The accepted set is host-owned

`AcceptedHunkIds` lists the ids of the accepted **changes**; `AcceptedHunkIdsChanged` reports every
change (a decision, a bulk action, Undo) and `ResultChanged` reports the resulting text, which is
always the exact `TextDiffResult.Apply` output. Because the hunks tile both inputs, an empty set
returns the old text character for character and a full set returns the new text.

Two details matter when a host owns the set:

- **The unchanged hunks are part of the applied set internally.** The separator between two changes
  belongs to the hunk before it, so an equal hunk can carry the whitespace between a rejected and an
  accepted change. The component therefore applies the accepted changes *plus* every equal hunk —
  passing only the changed ids would silently drop that whitespace.
- **An echo is not a new decision.** When the host echoes back the set the component itself raised,
  the session state (the rejected / pending distinction and the undo step) is kept. A host value that
  differs from both the last raised set and the previous parameter replaces the decision and drops the
  undo step.

## Persisting a decision

`OnDecision` is `Func<InlineDiffDecision, Task>` and runs BEFORE the decision is applied. The decision
carries the hunk id, the outcome and the whole resulting text, so a host can persist both in one round
trip. A throw leaves the change undecided, shows the localized error and announces it — a failed save
never looks like an accepted change. A bulk action runs the callback for each change that actually
changes state and applies NONE of them when one call throws.

## Undo

Exactly one level. The undo snapshot is the accepted set *and* the per-change decision state, so Undo
brings back both the wording and the visual state of the changes. Undo raises
`AcceptedHunkIdsChanged` / `ResultChanged` again but does not run `OnDecision` — a host that persists
through the decision callback saves the result of `ResultChanged` (or re-reads `AcceptedHunkIds`) for an
undo.

## Popover, focus and keyboard

The popover is a non-modal `role="group"` anchored inside the change span. Opening it moves focus to
its Accept button; `Escape` closes it and returns focus to the marker; a decision closes it and returns
focus to the marker as well. Two more gestures dismiss it without pulling focus back: a `Tab` past the
FIRST or the LAST stop (the inner Accept → Reject moves keep it open) and a click on the diff text
around the decision. A click outside the component is not observable from Blazor (its
`FocusEventArgs` carries no related target, so a `focusout` cannot tell an inner move from a leave);
only one popover can be open at a time, so no second stale surface can appear. Inside the popover `A`
accepts and `R` rejects, both announced through `aria-keyshortcuts`. The shortcuts are disabled with
`EnableShortcuts="false"` (the attribute then disappears) and never fire for a repeated key or a key
combined with Ctrl / Alt / Meta.

## Degraded comparisons

`Options` (a `TextDiffOptions`) tunes the engine limits. Above `MaxTokens` / `MaxCells` a word diff
degrades to line level, and a line diff to a single whole-text change. The component never throws for
such an input: it renders the coarser hunks, sets `data-fallback` and shows a localized note
("The comparison is too large, so whole lines are compared instead of words." /
"…so the whole text is presented as a single change.").

## Accessibility

- The change is carried by three channels: the `ins` / `del` element semantics, the visually hidden
  "Inserted:" / "Removed:" label, and the colour. Under `forced-colors` the UA keeps the underline and
  the strike-through, and every focus stop re-declares the system `Highlight` outline.
- Exactly ONE polite live region announces every decision and every error; nothing else in the
  component claims a live role. The bulk announcements are PLURAL families
  (`TmInlineDiff_AnnounceAcceptedAll_One` / `_Other`, `…_Few` / `…_Many` in Czech and Slovak) resolved
  through `Loc.Plural`, so a count of one says "1 change accepted." instead of "All 1 changes accepted."
  and Czech's 2–4 form is the one the sentence needs (see [localization-plurals.md](localization-plurals.md)).
- The marker, the popover buttons and the toolbar actions are 44 px targets on a coarse pointer.
- Both the popover and the change background animate, and both have a reduced-motion opt-out in the
  component stylesheet and in the global `animations.css` enumeration.

## Public API

| Member | Description |
|---|---|
| `IsReview` | `Mode == Review && !ReadOnly` — whether decisions are offered. |
| `ResultText` | The text the current accepted set produces. |
| `AcceptedIds` | The accepted change ids, in document order. |
| `CanUndo` | Whether there is a decision to undo. |
| `Diff` | The `TextDiffResult` behind the rendering. |
| `Announcement` | The latest announcement (mirrored into the live region). |
| `AcceptHunkAsync(id)` / `RejectHunkAsync(id)` | Decide one change programmatically. |
| `AcceptAllAsync()` / `RejectAllAsync()` | Bulk decisions. |
| `UndoAsync()` | Restore the previous accepted set. |
