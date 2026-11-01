# TmSuggestionCard

One machine suggestion a human accepts or dismisses: the suggested text, a localized confidence band
(with an optional percentage), the rationale behind a native disclosure, and — when the host offers
more than one wording — a `radiogroup` of alternatives.

Plan: `10b3966f` (human-in-the-loop, phase 4). Package: `Tempo.Blazor` (core).

```razor
@using Tempo.Blazor.Components.Approvals

<TmSuggestionCard Title="Layout suggestion"
                  Text="Move the summary above the fold."
                  Confidence="0.87"
                  ShowConfidencePercent="true"
                  Rationale="The summary is the first thing a reader scans."
                  Alternatives="@alternatives"
                  @bind-SelectedAlternative="selected"
                  UndoWindow="@(TimeSpan.FromSeconds(30))"
                  OnAccept="@SaveAsync"
                  OnDismiss="@DismissAsync"
                  OnUndo="@WithdrawAsync" />
```

## States

| `data-state` | Card |
|---|---|
| `pending` | The proposal: title, confidence, text, optional rationale disclosure, optional alternatives, Accept / Dismiss. |
| `accepted` | Collapsed row: `Accepted: {the accepted text}` and — while the undo window is open — Undo. |
| `dismissed` | Collapsed row: `Dismissed`. No Undo (Undo withdraws an acceptance). |

The pending half and the decided half are BOTH mounted so the collapse can transition the height
(`grid-template-rows: 1fr` → `0fr`); the inactive half carries `inert`, so it leaves the tab order and
the accessibility tree. The card is self-managed: render it with `@key` when a host reuses one instance
for a different suggestion.

## Confidence

`Confidence` is a `double?` from `0` to `1` and is presented as a localized band — never as a bare
number:

| Value | Root modifier | Label |
|---|---|---|
| `>= 0.8` | `tm-suggestion-card--confidence-high` | High confidence |
| `>= 0.5` | `tm-suggestion-card--confidence-medium` | Medium confidence |
| `< 0.5` | `tm-suggestion-card--confidence-low` | Low confidence |
| `null` | none | (no chip, `data-confidence="none"`) |

`ShowConfidencePercent` (default `false`) adds the percentage next to the label (`87 %`). The colour
accent of the card follows the band, but the band is always spelled out, so colour is never the only
signal.

## Rationale

`Rationale` renders behind a native `<details>`: keyboard-operable and announced as a disclosure without
a JS handler. `RationaleDefaultExpanded` starts it open. A blank or missing rationale removes the
disclosure entirely rather than shipping a dead focus stop.

## Alternatives

`Alternatives` renders a `role="radiogroup"` with the localized "Alternatives" name and one radio per
option. The group starts without a selection (the suggestion itself is `Text`), `SelectedAlternative` /
`SelectedAlternativeChanged` are two-way, and Accept applies the selected alternative — or `Text` when
none is selected.

## Decisions are asynchronous

`OnAccept` is `Func<string, Task>` and receives the accepted text; `OnDismiss` is `Func<Task>`. While a
callback runs the card is `aria-busy="true"` and both actions are disabled. The card leaves the pending
state only when the callback (and the paired `Accepted` / `Dismissed` `EventCallback`) completes without
throwing; a throw keeps the suggestion pending, shows the localized error and announces it. Both an
`EventCallback` and a `Func<…, Task>` are offered so a host can pick the style it already uses.

## Undo

`UndoWindow` starts the window on acceptance (offered only when `OnUndo` or `Undone` is wired); the
window is driven by the injected `TimeProvider`, so a host can drive it in tests. Undo returns the card
to pending and moves focus back to the primary action. A failing undo keeps the decision and shows the
error. Dismissal is not undoable.

## Accessibility

- The confidence is a label, the alternatives are a real radiogroup and the rationale is a real
  `<details>` — every state is conveyed by text, not by colour.
- Exactly ONE polite live region announces every decision, the undo and every error.
- Focus follows the collapse: to Undo when it is offered, otherwise to the card root (`tabindex="-1"`),
  and back to Accept on undo.
- The actions and the alternative rows are 44 px targets on a coarse pointer; the collapse has a
  reduced-motion opt-out in the component stylesheet and in the global `animations.css` enumeration.
- Under `forced-colors` every focus stop re-declares the system `Highlight` outline.

## Public API

| Member | Description |
|---|---|
| `IsBusy` | Whether a callback is in flight. |
| `CanDecide` | Whether the suggestion is still decidable. |
| `CurrentAlternative` | The picked alternative, or `null`. |
| `Announcement` | The latest announcement (mirrored into the live region). |
| `AcceptAsync()` / `DismissAsync()` | Run the decision programmatically. |
| `UndoAsync()` | Withdraw an acceptance while the window is open. |
