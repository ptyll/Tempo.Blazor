# TmAgentPlan — the agent's plan, reviewed and then watched

`TmAgentPlan` (namespace `Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) is the plan of steps an
agent intends to run. The same rows carry two very different surfaces:

| Mode | What the reader can do |
|---|---|
| `AgentPlanMode.Review` (default) | Rename a step inline, add a step below a row or at the end, remove a step and undo it, reorder the plan (pointer, keyboard or the row menu), approve the plan, start the run |
| `AgentPlanMode.Running` | Read only. A running step counts its live elapsed time, the summary adds the plan total, a failed step shows its error and offers **Retry from this step** |

The component **never mutates `Steps`**. Every edit raises
`OnPlanChanged(IReadOnlyList<AgentPlanStep> steps, AgentPlanEdit edit)` with the whole proposed list plus the
operation that produced it — exactly the controlled contract `TmSortableList` follows for its order.

```razor
<TmAgentPlan Steps="@steps"
             OnPlanChanged="ApplyAsync"
             OnApprove="ApproveAsync"
             OnRun="RunAsync" />

@code {
    private IReadOnlyList<AgentPlanStep> steps = [
        new("Draft the outline"),
        new("Confirm the sources"),
        new("Publish the summary"),
    ];

    private Task ApplyAsync(IReadOnlyList<AgentPlanStep> next, AgentPlanEdit edit)
    {
        steps = next;       // the host owns the plan; the component only proposes
        StateHasChanged();  // a plain delegate does not re-render the host (see below)
        return Task.CompletedTask;
    }
}
```

> **Re-render the host yourself.** `OnPlanChanged` is a `Func<…>` because an edit carries two values, so
> Blazor's automatic "re-render the component whose event handler ran" fires on `TmAgentPlan`, not on the
> page that owns `Steps`. Call `StateHasChanged()` (or await something that does) after storing the list, or
> the page keeps painting the previous list until its next unrelated render.

## API

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Steps` | `IReadOnlyList<AgentPlanStep>?` | `null` | The plan in display order. Null/empty renders `EmptyContent` or the localized empty sentence. |
| `Mode` | `AgentPlanMode` | `Review` | `Review` is the editable surface, `Running` the read-only one. |
| `OnPlanChanged` | `Func<IReadOnlyList<AgentPlanStep>, AgentPlanEdit, Task>?` | `null` | Every edit: the whole new list plus the `AgentPlanEdit` describing it. |
| `OnApprove` | `EventCallback` | — | The plan action; rendered only when bound. Review mode only. |
| `OnRun` | `EventCallback` | — | Starts the run; rendered only when bound. Review mode only. |
| `OnRetryFrom` | `EventCallback<AgentPlanStep>` | — | The failed step to restart the run **from** (the steps before it keep their state). Offered in both modes. |
| `Title` | `string?` | `null` | Heading text; null is the localized "Plan". |
| `ShowTitle` | `bool` | `true` | Set `false` when the host page already provides the heading. |
| `HeadingLevel` | `int` | `3` | Heading level of the rendered title, clamped to 2–6. |
| `ShowSummary` | `bool` | `true` | The "4 of 7 done · 1:24" line. |
| `AllowEditing` | `bool` | `true` | `false` keeps the rows and Approve but removes every edit path. |
| `EmptyContent` | `RenderFragment?` | `null` | Replaces the localized "No steps yet." |
| `AriaLabel` | `string?` | `null` | Explicit accessible name; without it the plan is named by its heading. |
| `Class` | `string?` | `null` | Extra root classes. |
| `DataTestId` / `TestIdPrefix` | from `TmComponentBase` | — | Standard test-id namespacing. |

Public methods: `BeginRename(int index)` (the same path a click on the title or F2 takes) and `ClearUndo()`.
Read-only state: `Announcement`, `CanEdit`, `CanUndoRemove`, `DoneCount`.

Models (package `Tempo.Blazor.Abstractions`, namespace `Tempo.Blazor.AI.Models`):

| Type | Members |
|---|---|
| `AgentPlanStep` (record) | `Id` (generated when omitted), `Title`, `Description`, `Status`, `StartedAt`, `CompletedAt`, `Error`, `IsEditable` (default `true`); computed `IsFinished`, `Duration`, `Elapsed(now)` |
| `AgentStepStatus` | `Pending`, `Running`, `Done`, `Failed`, `Skipped` |
| `AgentPlanEdit` (record) | `Kind`, `Index`, `StepId`, `Title`, `FromIndex`, `ToIndex`, `Step`; built only through `Rename`, `Add`, `Remove`, `Reorder` |
| `AgentPlanEditKind` | `Rename`, `Add`, `Remove`, `Reorder` |
| `AgentPlanMode` | `Review`, `Running` |

`AgentPlanStep` is a **value**: a host replaces a step with `step with { Status = AgentStepStatus.Done }`.
`Id` is the row's `@key` and what an `AgentPlanEdit` names, so it must be unique per plan; a missing one is
generated at construction. `IsEditable = false` pins a step the plan did not author (imported from a
template, produced by the agent): the row keeps its content and offers no rename, no add-below and no
remove. The mode always wins — a running plan is read-only for every step.

## Editing

* **Rename** — a click on the row's title button or **F2** opens an inline `<input>` holding the current
  title with the focus already in it. **Enter** saves, **Escape** cancels, and the save/cancel pair next to
  the field is the pointer and touch path of the same edit. A whitespace-only title is refused and the
  editor stays open (the plan announces "A step needs a title." **and** shows it: the field carries
  `aria-invalid="true"` and the sentence is rendered under it, cleared as soon as the reader types, saves or
  cancels), so no row can lose its name. A rename that does not change the text raises nothing. The focus
  returns to the title button that owned it. A title that is empty or whitespace reads as the localized
  "Untitled step" — dimmed and italic, so a stand-in is never mistaken for a name — in the row, in the
  rename button's name and in the list's row label.
* **Add** — the per-row button inserts a step *below that row*; the footer action appends at the end. The
  new row carries the localized "New step" title and opens in its rename editor, so the reader names it in
  the same gesture. `OnPlanChanged` describes it as `AgentPlanEdit.Add(index, step)`. **Escape** (or Cancel)
  closes the editor and leaves the placeholder row in the plan: the insertion was already reported to the
  host, so cancelling the *name* must not silently report a second, destructive edit — the row is removed
  with its own Remove action, which offers the usual Undo.
* **Remove and Undo** — removal is immediate and confirmed through **Undo** rather than a modal: the emitted
  `AgentPlanEdit.Remove(index, step)` carries the removed step, the plan shows an Undo strip naming it **by
  title** (the number it had no longer points at a visible row, because the rows below were renumbered), and
  the strip's Undo emits `AgentPlanEdit.Add(index, step)` — the same shape an insertion has, so the host
  needs no second code path. Every other edit retires the offer. A host that never reported the removal —
  no `OnPlanChanged` handler at all, or a list that still contains the step — is not offered an undo, and a
  stale offer that is somehow still up is retired without inserting a second row with the same `Id`. The
  focus follows the plan: the next row, the previous one when it was last, and the add action — or the plan
  itself, when it may not be edited — when the plan emptied.
* **Reorder** — the rows *are* a `TmSortableList`: the decorative grip starts a pointer drag (400 ms
  long-press on touch in whole-row mode is not used here), the **row itself is the keyboard grip** (Enter or
  Space picks it up, the arrows move it, Enter or Space drops it, Escape rolls back, Tab leaves without a
  move) and the per-row move menu is the tap and mouse alternative (WCAG 2.5.7). A committed move raises
  `AgentPlanEdit.Reorder(from, to, stepId)`.

`AgentPlanEdit.Index` is always the position in the **new** list: the renamed row, the insertion point, the
hole a removal left behind, or the new position of a moved row.

## Running

In `AgentPlanMode.Running` every row is a plain title with its state in words; there is no rename, no add,
no remove, no reorder and no plan action. What the mode adds:

* **A live clock** — a step whose `Status` is `Running` and whose `StartedAt` is set counts
  `Elapsed(now)`, where `now` comes from the injected `TimeProvider` (an unregistered one falls back to
  `System`) and is sampled **once per render**, so every row's clock and the plan total describe the same
  instant. The ticker runs only while something is running, so a finished plan stops re-rendering. A
  finished step keeps its recorded `Duration` instead of counting on.
* **The plan total** — the summary adds every step's elapsed time: `4 of 7 done · 1:24`. The clock-style
  duration is invariant (`m:ss`, `h:mm:ss`) on purpose: it is a duration, not a quantity of a unit, and the
  four shipped cultures read it identically.
* **The failure** — a `Failed` step renders its `Error` under the title and offers **Retry from this step**,
  which raises `OnRetryFrom(step)`; the host restarts the run from there and rewrites the steps. The retry
  action is the *only* action a running plan offers.

## Accessibility

* The plan is a `role="group"` named by the rendered heading (`aria-labelledby`) — so the name a reader
  hears is the one they see — or by `AriaLabel` when the host supplies one, and described by the hidden
  keyboard instructions.
* Every state is an icon **and** a word (`Pending` / `Running` / `Done` / `Failed` / `Skipped`), so the
  colour is never the only carrier; the icons are `aria-hidden` and the number badge (the visual order) is
  decorative, because a list already announces its positions.
* **ONE polite live region** carries the plan's own events: renamed, rename cancelled, added, removed,
  restored, and a refused title. A **move** is deliberately not announced here — the nested
  `TmSortableList` already narrates it, and a second sentence would read the same fact twice.
* The ticking clock and the summary are **not** live regions: a value that changes every second would talk
  over everything else. Both are `aria-live="off"`.
* Every action is a native `<button>` (no key emulation, so one key press activates exactly once) and every
  focus stop keeps a visible ring, repainted in the system `Highlight` colour under `forced-colors`.
* **Retry is named by its visible words** ("Retry from this step") and *described* by its step's title, so
  a speech-control user can say what they see and a screen reader still hears which step the action belongs
  to (WCAG 2.5.3). Every other action's accessible name either is its text or starts with it.
* A refused empty title is not an announcement-only fact: the field is `aria-invalid` and the reason is
  rendered on screen under it (WCAG 3.3.1), and under `forced-colors` the invalid field keeps a
  `CanvasText` edge.
* `(pointer: coarse)` grows the actions, the title button, the rename field and the retry pill to the 44 px
  `--tm-touch-target` and reflows the row into two columns so a phone never scrolls sideways.
* The plan declares **no transition and no animation**: it re-renders every second while a step runs, and a
  moving surface would keep animating for the whole run. That is also why it needs no reduced-motion entry.

## CSS classes

`.tm-ai-agent-plan`, `--running`, `__header`, `__heading`, `__title`, `__summary`, `__actions`, `__action`,
`__action--icon`, `__step`, `__index`, `__status`, `__status-text`, `__body`, `__title-button`,
`__title-text`, `__title-text--placeholder`, `__description`, `__error`, `__time`, `__step-actions`,
`__retry`, `__rename-editor`, `__rename`, `__rename-error`, `__footer`, `__undo`, `__undo-text`, `__empty`
— tokens only (no colour literal, no raw palette step, no `[data-theme]` block), guarded by
`AiMarkdownViewCssTests` and `TmAgentPlanCssTests`.

## Wireframe and localization

The component is a wireframe citizen: the `agent-plan` UI role, the `TmAgentPlan` schema (`title`, `steps`,
`mode`, `showSummary`, `showAddStep`) and its declarative stencil, which draws the numbered rows, the state
glyphs (a check for done, a filled dot for running, a cross plus a Retry pill for failed), the add row and
the Approve / Run pair — all of it branching on `mode`.

Keys: `TmAgentPlan_*` (label, title, instructions, the five states, both summary shapes, the rename field and
its two actions plus the visible "a step needs a title" reason, the untitled fallback, add/remove/undo, the
three plain announcements (added, renamed, rename cancelled) and the two that name a step by title (removed,
restored), and the retry action) in English, Czech, Slovak and French.

Demo: [`/agent-plan`](../src/Tempo.Blazor.Demo.SharedUI/Pages/AgentPlanPage.razor) — a review plan whose
every edit is reported to the page holding the list, a plan mid-run with four finished steps, a live running
step, a failure with Retry, and a review that may only be approved.
