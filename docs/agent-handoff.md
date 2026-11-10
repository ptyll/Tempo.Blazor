# TmAgentHandoff — the run handed from agent to agent

`TmAgentHandoff` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) shows a multi-agent run as a CHAIN of
handoffs: one labelled step per handover, with who handed what to whom, when, how the receiving agent answered, and —
behind a Details button — the two lists that matter, **what the next agent received** and **what was lost, with the
reason**. A chain-wide sentence below the chain names what the run actually dropped.

The shared identity model (`AgentRef`, `AgentActivityStatus`) and the accessibility rules every agent visualization
follows are documented once in [ai-agents.md](ai-agents.md). This page is the `TmAgentHandoff` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmAgentHandoff Steps="_steps"
                Title="Invoice run handoffs"
                @bind-ViewMode="_view"
                @bind-ExpandedStepIds="_expanded" />

@code {
    private IReadOnlyList<HandoffStep> _steps = [];
    private AgentHandoffViewMode _view = AgentHandoffViewMode.Chain;
    private IReadOnlyList<string>? _expanded;   // null = every step open
}
```

## The models

| Type | What it is |
|---|---|
| `HandoffStep` | One handover: `Id`, `FromAgent?` (`AgentRef`), `ToAgent?`, `FromAgentId?`, `ToAgentId?`, `At?`, `Summary`, `PassedItems`, `LostItems`, `Status`. A record VALUE a host recomposes with `step with { … }`. |
| `HandoffItem` | One thing that travelled — or did not: `Label`, `Kind` (`HandoffItemKind`), `Reason?` (read only on a **lost** item). `item.Lost("…")` copies a passed item as a lost one. |
| `HandoffChainSummary` | The roll-up the summary block prints, computed by `HandoffChainSummary.Create(steps)`: the per-state counts, `SettledCount`, `PassedItems`, `LostItems`, `ItemCount`, `LossRate`, `HasLosses`, `HasPending`, `IsComplete`, `WorstStepId` and `LostLabels`. |

Enums: `HandoffStatus` (`Pending` | `Accepted` | `Rejected` | `Completed`) and `HandoffItemKind` (`Context` |
`Instruction` | `Tool` | `Artifact` | `Memory`).

A blank `Id` is generated at construction (the same rule `AgentRef` follows), so a list built in a loop still renders.
`HandoffStatus.Pending` is the **default** (0) on purpose: a step a host has not classified must never read as
"the work is now someone else's". A `HandoffItemKind` value from a newer backend falls back to `Context` and an unknown
`HandoffStatus` falls back to `Pending`, so a forward-compatible backend never throws and never claims a handover.

### The rules the model carries

- **`PassedCount` / `LostCount` / `ItemCount`** are arithmetic over exactly the two lists the surface prints, so a
  summary can never disagree with the steps it summarises.
- **`LossRate`** is `LostCount / ItemCount`, and **0** when the step carried nothing (there is no rate without a whole).
- **`IsSettled`** is `Accepted` or `Completed`; `Rejected` is **not** settled — a refused handoff was never taken over.
- **`LostLabels`** de-duplicates the lost item labels case-insensitively and keeps the order the step declared them, which
  is the "what did we actually drop" list a reader can act on.
- **The `AgentRef` wins when both identity shapes are set.** A host mid-migration may fill `FromAgent` *and*
  `FromAgentId`; there is no merge and no validation, and a raw id that names a different agent is simply not read —
  the name, the initials and the palette slot all come from the same reference, so a step can never paint one agent's
  name on another agent's colour. A nameless `AgentRef` does not silence the fallback either: `FromName` / `ToName`
  still print the raw id, because an unnamed agent must never render blank.
- **`HandoffChainSummary.WorstStepId`** is the first step that lost the most, or `null` when nothing was lost, so a
  surface can point at the damage deterministically.

## The chain view

`ViewMode="Chain"` (the default) renders an ordered list (`role="list"` / `role="listitem"`, `aria-describedby` the
keyboard instructions) of labelled steps. Each step carries:

- its position, its heading (`Handoff {n}`, one heading level below the surface title and never past `h6`),
- the **state** as an icon, a localized word and a tone — a rail on the step repeats the tone, but the word is what
  carries it (WCAG 1.4.1),
- the route (`From` → `To`) painted through the shared `TmAgentBadge`, so the initials and the palette slot match every
  other agent surface; a host that tracks agents by id only gets its raw ids printed, and a step with neither prints
  the localized "Unknown agent",
- the host's summary, and `At` in the bound culture,
- a native **Details** button (`aria-expanded` + `aria-controls`) that opens the passed and lost lists.

Between two steps sits a decorative SVG arrow (`aria-hidden`): the list already carries the order, so the arrow is never
the only signal and a screen reader does not hear "arrow, arrow, arrow".

`Orientation="Horizontal"` lays the SAME list across the surface (the connector turns with it); nothing about the data,
the labels or the keyboard changes.

### The passed and lost lists

Inside the detail region sit two named groups (`role="group"` + a label, the visible group title is `aria-hidden` so it
is not announced twice):

- **Passed on** — each item as its kind's icon, its label and the localized kind word.
- **Lost** — the same, plus `Reason: …` when the host supplied one, tinted with the danger ink **and** spelled out. The
  wash is applied only when the step actually lost something (`data-has-losses="true"` on the step), because tinting
  "Nothing was lost." in the danger colour would be a colour-only verdict on an empty list.

An empty list says so ("Nothing was passed on." / "Nothing was lost.") instead of rendering nothing, so a reader can tell
"nothing was lost" from "the list did not load".

`ExpandedStepIds` is two-way (`@bind-ExpandedStepIds`): `null` (the default) opens **every** step, because the two lists
are the reason to look at a chain at all; an empty list closes them all. The component reports the expanded ids in CHAIN
order, so a host that stores the value keeps a stable list.

Internally the expanded and focus state is keyed by **position** (1-based), not by `Id`: two steps that share one `Id`
(bad host data) still expand and focus independently. When the host inserts or removes a step while keeping the same
`ExpandedStepIds` reference, each expanded position is remapped by the step id it held before, so the expanded state
follows the step and not the raw position. A duplicate `Id` logs a warning through the optionally injected
`ILoggerFactory` — logged once per duplicate streak, not on every parameter set — the component keeps rendering, but
the host should supply distinct ids.

### The loss summary

Below the chain sits ONE sentence: `Lost across the chain: {labels}` when anything was lost, and
`Nothing was lost across the chain.` when nothing was. The same fact is a `data-has-losses` hook, so a host can style it
without parsing the text. The sentence is derived from `HandoffChainSummary.LostLabels` — the same value the summary
block prints as a number.

## The summary block

`ShowSummary="true"` (the default) renders the settled/total step count, the passed and lost totals, the loss rate and a
state sentence ("Every handoff is settled" / "{n} still pending"). It is hidden for an empty chain, because there is
nothing to summarise. `ShowTitle` / `Title` / `HeadingLevel` (clamped to 2–6) control the heading in the usual way.

## The table alternative

`ViewMode="Table"` renders the same steps as one table — step, from, to, at, status, passed on (every item label), lost
(every label with its reason). It is the text alternative of the chain (WCAG 1.1.1 / 1.4.1): the arrows and the tone
rails add nothing the table does not carry, so a screen-reader, keyboard-only or phone reader loses no fact. The View
switcher (`TmSegmentedControl`) is two-way with `@bind-ViewMode`; the reader's pick paints immediately and the host's
next value moves it.

## Accessibility

- One tab stop per step (the Details button); **no key emulation** — a native `<button>` activates itself exactly once
  (`docs/keyboard-activation-convention.md`).
- A state and an item kind are always **an icon, a localized word and a tone**, never colour alone.
- The chain is a real ordered list, the arrows are `aria-hidden`, the group titles are `aria-hidden` because their groups
  are already named.
- ONE polite live region speaks **only** when a step the reader was on disappears from the host's data: the focus is
  handed to the step that took its place (after the render, never inside the handler) and the move is announced once.
- `(pointer: coarse)` raises the Details button to the 44 px `--tm-touch-target`.
- `forced-colors` hands the borders to `CanvasText`, the fills to `Canvas` and every focus ring to a 3 px `Highlight`
  outline.
- The partial declares no transition and no animation, so `ReducedMotionSelectorTests` has nothing to allowlist.

## CSS

`.tm-ai-handoff`, `--vertical` / `--horizontal`, `__header`, `__title`, `__view`, `__summary`, `__stats`, `__stat`,
`__stat-label`, `__stat-value`, `__summary-state`, `__empty`, `__table`, `__chain`, `__step`,
`__step--{pending|accepted|rejected|completed}`, `__step-head`, `__index`, `__step-title`, `__status`, `__status-icon`,
`__status-text`, `__route`, `__agent`, `__agent-name`, `__route-arrow`, `__at`, `__step-summary`, `__toggle`,
`__toggle-text`, `__toggle-icon`, `__detail`, `__group`, `__group--lost`, `__group-title`, `__group-icon`,
`__group-text`, `__group-empty`, `__items`, `__item`, `__item--lost`, `__item-icon`, `__item-label`, `__item-kind`,
`__item-reason`, `__connector`, `__lost-summary` — tokens only (no colour literal, no raw palette step, no
`[data-theme]` block). A state modifier SETS `--tm-handoff-step-tone` while the base rule READS it, so source order
cannot decide a colour; the chip's ink and fill are knobs too (`--tm-handoff-status-*`). Guarded by
`AiMarkdownViewCssTests` and `TmAgentHandoffCssTests`.

## Localization

`TmAgentHandoff_*` (title, summary labels and the rate, the settled/pending sentences, the chain label and instructions,
the empty state, the step label and fallback, the unknown-agent fallback, Details and its named form, the group labels
and their empty sentences, the item fallback, the lost reason and its format, the loss summary in both shapes, the
"None" cell, the view labels, the table caption and columns, and the focus-lost sentence), `TmHandoffStatus_*` and
`TmHandoffItemKind_*` in English, Czech, Slovak and French.

## Wireframe

The `agent-handoff` UI role (synonyms in both languages) plus the `TmAgentHandoff` schema (`title`, `steps`,
`orientation`, `viewMode`, `showSummary`) and its declarative stencil, which draws the heading, the summary line, one
step per supplied entry with its route, its state chip, the passed and lost lines and the Details toggle.
