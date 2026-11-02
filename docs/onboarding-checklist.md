# TmOnboardingChecklist

The setup guide and progress tracker of an application: a checklist of onboarding steps, a counted
progress line, collapsible groups, an optional call-to-action per step and a celebration state once
every required step is done. Pure Blazor — no JavaScript, no drag gesture, no popover.

```razor
<TmOnboardingChecklist Tasks="Steps"
                      Title="Get started"
                      Layout="OnboardingChecklistLayout.Card"
                      Dismissible="true"
                      OnTaskToggle="HandleStepToggled"
                      OnDismiss="HandleDismissed" />
```

## Model

`OnboardingTask` is a mutable class, not a record: the checklist writes `IsDone` on the instance it
was given, so it works **without** a bound `OnTaskToggle` handler (the checkbox, the progress line and
the celebration all follow the model). A host that persists progress handles `OnTaskToggle` and stores
the value; a host that reloads the steps from storage hands the checklist new instances.

| Member | Purpose |
|--------|---------|
| `Id` | Stable identifier, unique within one checklist. It is the suffix of the step's `data-testid`, so two steps that share one make host tests ambiguous. It never reaches a DOM id: the checkbox, description and group-body ids are built from the step's **position** (its group index and its index inside that group), so two ids that reduce to the same id-safe form (`a.b` and `a-b`) cannot collide. |
| `Title` | The step's text — the visible label **and** the accessible name of its checkbox. |
| `Description` | Optional explanation, wired to the checkbox through `aria-describedby`. |
| `IsDone` | Whether the step is done. Written by the checklist when the user toggles the box. |
| `IsOptional` | Renders a localized "Optional" badge and never blocks the celebration. |
| `Group` | Named group: the steps that share it render under one disclosure header with their own progress count. |
| `ActionLabel` | Visible text of the call-to-action. An action renders only when it has a target. |
| `ActionHref` | Navigation target — renders a link. Only a **relative** URL or an `http`, `https` or `mailto` scheme is rendered; `javascript:`, `data:` and `vbscript:` values are refused, and so is a protocol-relative `//host/path` (it is technically relative but leaves the origin). A refused value is no target at all, so the action renders nothing unless `OnAction` is set. |
| `OnAction` | `Func<OnboardingTask, Task>` command target — renders a button; **wins over `ActionHref`**. |

## Progress

`ShowProgress` (default `true`) renders the progress block: a `TmProgressBar` plus a counted
sentence. The bar is named by its **role**
(`TmOnboardingChecklist_ProgressLabel`, "Progress") and reports a whole percentage as
`aria-valuenow`; the sentence next to it carries the counts ("3 of 7 tasks done"). Splitting the two
means a screen-reader user hears the numbers once, not the same text from two nodes.

The sentence is a real plural family (`Loc.Plural`), and its counted argument is the **total**, not
the done count: Czech "3 z 7 úkolů hotovo", Slovak "3 z 7 úloh hotovo" and French
"3 sur 7 tâches terminées" all agree the counted noun with the total, while English swaps only
`task`/`tasks`. The done
count is the extra argument `{1}`, so the family has the forms each language needs:

```json
"TmOnboardingChecklist_Progress_One":   "{1} of {0} task done",
"TmOnboardingChecklist_Progress_Other": "{1} of {0} tasks done"
```

`IsComplete` is reached when at least one step exists and every **required** step is done — optional
steps never block it, and a checklist that consists only of optional steps is complete from the start.

## Celebration

`ShowCelebration` (default `true`) renders the celebration block once `IsComplete` is true: a trophy
icon plus `CompletedTitle` and `CompletedText` (both default to localized text). The icon is
decorative (`aria-hidden`), the text carries the meaning, and the block is **not** a live region — the
completion is announced through the announcer, so nothing is announced twice. Its short pop
(`tm-onboarding-celebration-pop`) is the only animated subject of the component.

## Groups

Steps that share `OnboardingTask.Group` render as an APG **disclosure**: a native `<button>` with
`aria-expanded` and `aria-controls` over a body that stays in the DOM and is hidden through the
`hidden` attribute. Keeping the body mounted means `aria-controls` always resolves and checkbox state
survives a collapse; `hidden` removes it from the accessibility tree and from the tab order (the sheet
declares `[hidden] { display: none }` itself, because its author `display: flex` outranks the user
agent rule).

`CollapseCompletedGroups` (default **`false`**) collapses a group when its last step is checked. The
opt-in is deliberate — a group that folds under the user's cursor hides the work just finished — and
when it is on, the focus moves from the now-hidden checkbox to that group's **header**, which already
reports `aria-expanded="false"`. Without the move a keyboard user would land on the document body. The
component captures each header's `ElementReference` through a render-tree fragment per group, because
`@ref` takes a single assignment target whose capture callback every loop iteration would overwrite.

Steps without a group render in a plain list of their own, outside any disclosure.

## Layouts

| `Layout` | Presentation |
|----------|--------------|
| `List` (default) | A plain vertical list without card chrome — for a sidebar or a page section that already has a surface. |
| `Card` | One bordered, rounded, shadowed surface with the header and the progress block — the setup-guide presentation. |
| `Grid` | The tips panel: one tile per step in an `auto-fit` grid (`minmax(15rem, 1fr)`), each tile bordered. Prefer short titles here. |

`Title` renders as `role="heading"` with `aria-level` (`HeadingLevel`, 1–6, default 2, out-of-range
falls back to 2) and names the root `<section>`; without a title the region keeps the localized
"Onboarding checklist" name. A component never emits `<main>` — the root is a named region.

## Dismissal

`Dismissible` renders an icon button whose accessible name is the localized "Dismiss checklist".
Clicking it raises `OnDismiss` **and** hides the checklist locally in every case: a host that never
binds the callback is not left with a surface it cannot close. A **new `Tasks` list identity** is
regarded as a new onboarding run and brings the checklist back.

A host that also removes the component from its own layout owns the focus afterwards. The button that
had the focus no longer exists, so move the focus to a stable element — a page heading, or the control
that opened the guide. That is the one focus rule of this component that cannot live inside it: a
component may not leave the focus on `<body>`, and it cannot focus an element it does not render.

The pattern is a captured `ElementReference` plus a `FocusAsync()` call in the `OnDismiss` handler:

```razor
<h1 @ref="_heading">Get started</h1>

<TmOnboardingChecklist Tasks="Steps"
                      Title="Checklist"
                      Dismissible="true"
                      OnDismiss="HandleDismissed" />

@code {
    private ElementReference _heading;

    private async Task HandleDismissed()
    {
        // The dismiss button has just unmounted — the heading is the stable target.
        await _heading.FocusAsync();
    }
}
```

`Class` adds host CSS classes to the root `<section>`, and any unmatched attribute
(`AdditionalAttributes`) splats onto it — so a host can pass `id`, `aria-*` or `data-*` hooks
without a dedicated parameter.

## Accessibility

- **Exactly one polite live region per checklist** — a visually hidden `role="status"` with
  `aria-live="polite"` and `aria-atomic="true"`, the only place the checklist announces anything. It is mounted empty on the first render (a
  pre-filled region can be announced as soon as it registers) and carries the result of the user's own
  interaction: "«step» — done. 4 of 7 tasks done", "«step» — reopened. 2 of 7 tasks done", and
  "«step» — done. 2 of 2 tasks done. All steps are complete." for the last required step. That last
  message is one resource, `TmOnboardingChecklist_AnnounceTaskDoneAllComplete` (`"{0}. {1}"`), so the
  punctuation between the step sentence and the completion sentence belongs to the translator.
- **No duplicate announcements** (nothing is announced twice). The visible celebration block is not a
  live region, and the group disclosures announce nothing at all — `aria-expanded` already reports
  their state.
- **Native controls, exactly one activation per key press.** Steps are `<input type="checkbox">`
  associated with their own `<label>`; group headers are native `<button>`s. The component adds no
  `@onkeydown` emulation, so Enter/Space cannot toggle twice.
- **Done styling never hides the text.** A done step is struck through with the title still in the
  DOM, so its visible label and its accessible name stay identical.
- **Focus ring** on the dismissal, the group headers, the checkboxes and the actions (`:focus-visible`
  plus a `:focus` variant), painted with the system `Highlight` colour under `forced-colors: active`.
- **44 px coarse targets** for the dismissal, the group header, the action and the whole label row
  (the label is what enlarges the checkbox's hit area, so the box keeps its visual size). On a fine
  pointer the dismissal is a 32 px square — above the 24 px minimum of WCAG 2.5.8, with room to spare
  (24 px only just passed it).
- **Reduced motion**: the celebration's pop (`tm-onboarding-celebration-pop`) is the only animated
  subject and is neutralised in the `prefers-reduced-motion: reduce` enumeration of `animations.css`.

## Theming

`_onboarding-checklist.css` paints through semantic tokens only — `--tm-bg-surface`,
`--tm-border-color`, `--tm-radius-card`, `--tm-shadow-card`, `--tm-color-success-soft`,
`--tm-color-success-text`, `--tm-text-primary/-secondary/-muted`, `--tm-color-surface-hover`,
`--tm-focus-ring`, `--tm-touch-target`, `--tm-space-*`. The dark theme redefines those tokens; the
sheet declares no dark block and no palette literal.

## Test ids

| `data-testid` | Element |
|---------------|---------|
| `onboarding-checklist` | the root `<section>` |
| `onboarding-checklist-title` | the heading (only with `Title`) |
| `onboarding-checklist-dismiss` | the dismissal button (only with `Dismissible`) |
| `onboarding-checklist-progress` / `-progress-text` | the progress block and its counted sentence |
| `onboarding-checklist-celebration` | the celebration block |
| `onboarding-checklist-empty` | the empty state |
| `onboarding-checklist-announcer` | the live region |
| `onboarding-group-toggle-{index}` / `onboarding-group-body-{index}` | one group's header and body |
| `onboarding-task-{task.Id}` | one step's `<li>` |
| `onboarding-action-{task.Id}` | one step's call-to-action |

`TestIdPrefix` namespaces every one of them; `DataTestId` overrides the root id.

## Limits

- The checklist has no keyboard reordering and no drag: a setup guide is a linear list.
- It does not persist anything by itself — dismissal and progress are the host's to store.
- A step's action is one action: a link (`ActionHref`) or a command (`OnAction`), never both.
