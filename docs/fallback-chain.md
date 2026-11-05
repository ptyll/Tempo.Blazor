# TmFallbackChain — the models tried, in order, and the answer

`TmFallbackChain` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) shows the models tried for ONE request, in
the order they were tried: a node per attempt with its result, the reason it failed or was skipped, how long it took and
when, joined by decorative arrows and ending in the **Answer** node the first successful attempt branches to. The same
component carries the EDITOR a host opens over that chain: reorder it, add a model from the catalogue, remove one and
pick the resolution policy.

The shared identity model and the accessibility rules every agent visualization follows are documented once in
[ai-agents.md](ai-agents.md). This page is the `TmFallbackChain` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmFallbackChain Attempts="_attempts"
                 Mode="@(editing ? FallbackChainMode.Edit : FallbackChainMode.View)"
                 Chain="_chain"
                 ChainChanged="OnChainChanged"
                 AvailableModels="_models"
                 @bind-Policy="_policy" />
```

## The models

| Type | What it is |
|---|---|
| `FallbackAttempt` | One attempt: `ModelId`, `Label`, `Status` (`FallbackAttemptStatus`), `FailureReason?`, `LatencyMs?`, `At?`, plus `DisplayName`, `HasLatency`, `Latency`, `Succeeded` / `Failed` / `Skipped` / `NotTried`, `IsPassive`, `HasKnownStatus`. A record VALUE. |
| `FallbackChainEntry` | One model in the ORDERED chain: `ModelId`, `Label`, `DisplayName`. `FallbackChainEntry.FromModel(AiModelInfo)` builds one from the catalogue. |
| `FallbackAttemptStatus` | `NotTried` (0, the default) \| `Skipped` \| `Failed` \| `Succeeded`. |
| `FallbackResolutionPolicy` | `Pin` (0, the default) \| `Fallback` \| `Auto`. |

`NotTried` and `Pin` are the defaults on purpose: an unclassified attempt must never claim an answer was produced, and an
unset policy must never fall back to another model silently. A blank `ModelId` is generated at construction, and a
`Label` that is blank falls back to the id, so no branch is ever blank.

### Latency is never invented

`LatencyMs` is `null` (or negative) when the host did not measure the attempt, and `HasLatency` / `Latency` say so. The
flow then renders **no latency at all** and the table states the localized "not measured" — a zero would be a
measurement the host never made.

## The run view

`ViewMode="Flow"` (the default) renders an ordered list of labelled nodes with, per attempt: the position
("Attempt {n}"), the model, the result as an icon, a localized word and a tone (a rail repeats the tone; the word carries
it), the failure or skip reason, and the measured latency and time. Between two nodes sits a decorative SVG arrow
(`aria-hidden`) — the list already carries the order.

### The Answer node

The first SUCCEEDED attempt ends the chain, so it — not a later one — is what the Answer node names:
`Answered by {model}`. The branch arrow and the Answer card are tinted with the success tone only when an answer exists
(`data-answered="true"` / `data-success="true"`); a chain without a success shows `No answer yet` in the neutral tone.
Nothing is guessed: an attempt that reports `Succeeded` is the only kind of evidence there is.

### The policy read-out

The run view REPORTS the resolution policy as a word (`PolicyLabel` + the value, `data-policy` hook) rather than
offering to change it — a reader looking at a finished run has no business switching its policy. `ShowPolicy="false"`
hides it.

## The table alternative

`ViewMode="Table"` renders the same attempts as one table — attempt, model, result, reason, latency, tried at — so a
screen-reader, keyboard-only or phone reader loses no fact. The View switcher (`TmSegmentedControl`) is two-way with
`@bind-ViewMode`.

## The editor

`Mode="Edit"` swaps the flow for the chain editor. `Mode` is a plain parameter: the HOST owns the switch (the component
never changes it on its own), so a page can offer "Run | Edit chain" without the surface guessing.

- **Order** — `Chain` is rendered through the shared `TmSortableList<FallbackChainEntry>`, which supplies the pointer
  drag (through the core `pointer-drag.js`), the per-row move menu and the keyboard model (Enter/Space grabs, arrows
  move, Enter/Space drops, Escape rolls back, Alt+arrow steps, Tab cancels and leaves — WCAG 2.5.7). The component
  reports the new order; it never mutates the host's list.
- **Add** — a native `<select>` of the `AvailableModels` that are NOT already in the chain, plus an Add button. The
  control disables itself and explains why ("Every available model is already in the chain." / "No models are available
  to add.").
- **Remove** — a labelled button per row.
- **Policy** — a segmented radio group (`TmSegmentedControl`, `Pin` | `Fallback` | `Auto`) bound with
  `@bind-Policy`. Its group label is an INSTRUCTION ("Choose how the chain resolves"), not the run view's noun phrase
  (`TmFallbackChain_PolicyEditLabel` vs `TmFallbackChain_PolicyLabel`): the same words twice would make the editor read
  as a second read-out instead of a choice the reader can change.

`Chain` is two-way with `@bind-Chain`: `ChainChanged` fires once per committed change with a NEW list. When `Chain` is
`null` (the default) the editor derives its starting chain from `Attempts`, which is what an editor opened over a
finished run shows; the component then keeps its own working copy, so an unrelated host render never resets a reorder
in progress. Duplicate model ids are dropped (a chain is a sequence of distinct models, and the sortable list needs a
unique key).

## Accessibility

- Every attempt is a list item with real text; the arrows are `aria-hidden`.
- A result is an icon, a localized word and a tone; a failure or a skip always prints its reason.
- The editor's controls are native (`select`, `button`), each with its own visible label or an `aria-label` that contains
  its visible text (WCAG 2.5.3).
- ONE polite live region announces an **add** and a **remove**. A reorder is narrated by the sortable list's own region,
  so the two never speak about the same event.
- **Focus hand-back (WCAG 2.4.3).** Removing a model deletes the Remove button the reader was on, so the focus would
  fall to `<body>` and a keyboard or screen-reader reader would have to start again from the top of the page. It is
  moved instead to the row that took the removed one's place (`min(index, Count - 1)`); when the chain is empty it goes
  to the Add control, and when that is disabled too it stops on the editor hint (`tabindex="-1"`). Adding the LAST
  addable model disables the Add button the reader pressed, so the focus moves to the row the add created; while another
  model stays addable the button keeps the focus it already has. The announcement stays the ADD/REMOVE sentence — the
  focus move is not narrated, because the reader is placed on the thing the message names.
- Every number on the run view — the position and the latency — follows the bound `Culture`, never the machine's
  `CultureInfo.CurrentCulture`, so a Czech page cannot print `1.5 s` beside a Czech clock.
- `(pointer: coarse)` raises the remove button, the add button and the model select to the 44 px `--tm-touch-target`.
- `forced-colors` hands the borders to `CanvasText`, the fills to `Canvas` and every focus ring to a 3 px `Highlight`
  outline.
- The partial declares no transition and no animation.

## CSS

`.tm-ai-fallback`, `--edit`, `__header`, `__title`, `__view`, `__policy`, `__policy-label`, `__policy-value`, `__flow`,
`__node`, `__node--{not-tried|skipped|failed|succeeded}`, `__node-head`, `__node-position`, `__node-label`,
`__node-state`, `__node-state-icon`, `__node-state-text`, `__node-reason`, `__node-reason-label`, `__node-reason-text`,
`__node-facts`, `__arrow`, `__answer-arrow`, `__answer`, `__answer-icon`, `__answer-text`, `__answer-label`,
`__answer-model`, `__editor`, `__editor-hint`, `__editor-empty`, `__editor-list`, `__editor-row`,
`__editor-row-label`, `__editor-row-id`, `__editor-remove`, `__editor-add`, `__editor-add-label`, `__editor-select`,
`__editor-add-button`, `__editor-note`, `__editor-policy`, `__table`, `__empty` — tokens only. A node modifier SETS
`--tm-fallback-node-tone` while the base rule READS it; the chip's ink and fill are knobs
(`--tm-fallback-status-*`), and so are the arrow ink, its size and the Answer card. Guarded by
`AiMarkdownViewCssTests` and `TmFallbackChainCssTests`.

## Localization

`TmFallbackChain_*` (title, flow label and instructions, the empty state, the position format, the Answer label and its
both sentences, the reason and latency labels, the millisecond/second formats, the time label and the "not recorded" /
"not measured" cells, the policy label, the view labels, the table caption and columns, the editor label, hint, list
label, the edit-mode policy instruction label, the empty-chain sentence, the add/remove labels and their
announcements, and both no-model notes), `TmFallbackStatus_*` and `TmFallbackPolicy_*` in English, Czech, Slovak and
French.

## Wireframe

The `fallback-chain` UI role (synonyms in both languages) plus the `TmFallbackChain` schema (`title`, `attempts`,
`policy`, `viewMode`) and its declarative stencil, which draws the heading, the policy and view chips, one node per
supplied attempt with its tone rail, its reason, its latency and its outcome chip, and the Answer card.
