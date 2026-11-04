# TmContextUsage

What a context window is made of — the system prompt, the tools, the memory, the files, the conversation and the
tool results — measured against the model's budget, with the tokens each part takes and the parts the reader can
still drop.

`TmContextUsage` lives in the **Tempo.Blazor.AI** package; the models live in **Tempo.Blazor.Abstractions**
(`Tempo.Blazor.AI.Models`) and reference no provider SDK. The component renders a SNAPSHOT: it never polls
anything, so a host pushes a new `ContextUsageSnapshot` as the conversation grows (SignalR, polling, a
server-side channel).

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmContextUsage Snapshot="@snapshot"
                Title="@Loc["Chat_ContextWindow"]"
                OnRemoveItem="OnRemoveAsync"
                OnCompact="CompactAsync" />

@code {
    private ContextUsageSnapshot snapshot = new()
    {
        BudgetTokens = 16_000,
        CacheHit = 0.62,
        Sections =
        [
            new("system", "System prompt") { Kind = ContextUsageKind.System, Tokens = 1_200, IsRequired = true },
            new("tools", "Tools")
            {
                Kind = ContextUsageKind.Tools,
                Tokens = 800,
                CanRemove = true,
                Items = [new("search", "Web search") { Tokens = 500, CanRemove = true }],
            },
            new("memory", "Memory") { Kind = ContextUsageKind.Memory, Tokens = 400, IsSummarized = true },
            new("files", "Files") { Kind = ContextUsageKind.Files, Tokens = 5_000 },
            new("chat", "Chat history") { Kind = ContextUsageKind.Chat, Tokens = 6_000 },
            new("results", "Tool results") { Kind = ContextUsageKind.Results, Tokens = 600, IsOmitted = true },
        ],
    };
}
```

## The snapshot

| Member | Meaning |
| --- | --- |
| `BudgetTokens` | The model's context window. `0` or less means UNKNOWN: no percentage is claimed and no over-budget verdict is possible |
| `Sections` | The parts of the window, in the host's reading order (`null`/empty renders the empty state) |
| `CacheHit` | The share of the prompt the provider served from its cache, as a fraction (`0.62` = 62 %). `null` renders no badge; the value is clamped |

A section is `ContextUsageSection(Key, Label)`:

| Member | Meaning |
| --- | --- |
| `Kind` | `ContextUsageKind` — `System`, `Tools`, `Memory`, `Files`, `Chat`, `Results` or `Other` (default). Selects the section's TONE only |
| `Tokens` | The section's authoritative total. When it reports nothing, the section's tokens are the sum of its items |
| `IsRequired` | Cannot be dropped: no Remove action, never compactable |
| `IsSummarized` | The provider already summarised it (localized "Summarized" word) |
| `IsOmitted` | The content was dropped from the request (localized "Omitted" word) — the row and its tokens stay visible |
| `CanRemove` | The whole section may be dropped by the reader |
| `Items` | The entries the section is made of (`ContextUsageItem(Id, Label)` with `Tokens` and its own `CanRemove`) |

The three state words are independent facts, not one enum: a summarised section can still be required, and an
omitted section keeps its row so the reader can see what is no longer being sent.

The arithmetic is the public `ContextUsageMath` — `TokensOf`, `UsedTokens`, `BudgetPercent`, `IsOverBudget`,
`Shares` (largest remainder, so the rounded shares add up to exactly 100) and `IsCompactable`. A host computing
the same numbers elsewhere cannot drift from the component.

## The picture and the legend

| `Variant` | The picture |
| --- | --- |
| `Bar` (default) | One stacked bar; each segment's width is its section's share of the BUDGET, so the tail of the track is the headroom that is still free |
| `Grid` | A hundred cells, one per percent of the BUDGET — used cells carry the tone of their section, the free tail shows the headroom |
| `Treemap` | The sections as areas through the core `TmChart` (`Treemap` type), one tile per entry |

The picture is decorative (`aria-hidden`). The **legend table** under it is what a reader, a screen reader and a
copy/paste get: label, tokens, whole percentage and the localized state words, with the expand and Remove
controls inside it — the table is the accessible view itself, not a second hidden copy of the picture.

Every picture carries a minimum: a section with tokens whose rounded share is zero paints a 0.5 % sliver and
reads "< 1 %" in the legend, so a token-bearing part is never invisible. The bar clips that floor (`overflow:
hidden`), so the total can never grow the card.

The per-kind tones are component-local knobs declared on the root (`--tm-context-usage-system`, `-tools`,
`-memory`, `-files`, `-chat`, `-results`, `-other`), each aliasing a semantic `--tm-color-*` token, so a host
re-points one section without touching the package.

## The meter

The total is one `role="meter"` element: `aria-valuemin="0"`, `aria-valuemax` = the budget, `aria-valuenow` = the
used tokens **clamped to the budget** (ARIA does not allow `aria-valuenow` above `aria-valuemax`, and a browser may
cap such a value on its own) and a localized `aria-valuetext` ("14,000 of 16,000 tokens used (88 %)"). The
over-budget context is therefore never hidden: the REAL total and the real percentage live in the `aria-valuetext`
(which is what a screen reader reads) and in the visible read-out, which repeats the same sentence for sighted
users and is `aria-hidden`, so the amount is heard ONCE.

The cache-hit badge and the over-budget line are the meter's `aria-describedby`, because they add facts the value
text cannot carry (the cache share and the exact excess). An unknown budget renders the amount with no percentage
and no verdict.

## Reading and acting

- A section with `Items` gets a disclosure button (a native button with `aria-expanded`; `aria-controls` only
  while open, pointing at the detail row). A section without items renders its label as text.
- The section-level Remove action appears only when the model says `CanRemove` **and** a handler is bound; it
  reports `ContextUsageRemoval` with `Item = null` (`IsSection` is `true`).
- An entry's Remove action appears when the entry says `CanRemove` and a handler is bound; it reports the
  section and the entry.
- The component never edits the snapshot — it reports the intent and the host applies it (drop the section,
  summarise it, re-request the prompt).
- The Compact action appears only when `OnCompact` is bound; it is **disabled** while no section is compactable
  (`IsCompactable`: optional, not yet summarised, not yet omitted, carrying tokens), so a button is never offered
  that would do nothing.
- After a removal the pressed control may be gone with it, so the focus is handed back to the section's own
  disclosure button (an entry removal keeps the section) — or to the component root (`tabindex="-1"`) when the
  section itself left, lost its last entry or the host dropped it in the same update. A focus move that the
  browser rejects (the element is no longer in the page) is swallowed: a lost focus move is never an error UI or
  a dead Server circuit.

## Keyboard and accessibility

- Every control is a native `<button>` — Enter and Space are never emulated
  ([keyboard-activation-convention](keyboard-activation-convention.md)).
- The grid is hoverable for a pointer and reachable for a keyboard: hovering a cell highlights its section, and
  focusing any control in a legend row (the disclosure button, the section-level Remove, an entry's Remove)
  highlights the same section's cells and row, so the highlight never has a mouse-only path.
- The grid cells are not focus stops (`aria-hidden` decoration); one tab stop per interactive control keeps the
  panel short.
- ONE polite live region per panel: a budget CROSSING ("The context window is over its budget." / "… back within
  its budget."). A first render says nothing, so a page load never talks over the reader; a repeated sentence
  toggles one invisible marker so it is still a mutation.
- `(pointer: coarse)` raises the disclosure, Remove and Compact controls to the 44 px `--tm-touch-target`.
- `forced-colors` hands every focus ring back to the system `Highlight`, opts the tone-coded segment / cell /
  swatch out of the palette collapse (`forced-color-adjust: none`), and keeps the bar and the free cells
  outlined in `CanvasText`. The over-budget state is a word plus an icon, never colour alone.
- The surface declares NO motion: a panel refreshed by a polling host must not flicker.

## Presentation knobs

| Parameter | Default | Meaning |
| --- | --- | --- |
| `Snapshot` | `null` | The snapshot to render; `null`/empty renders the localized empty state |
| `Variant` | `ContextUsageVariant.Bar` | `Bar`, `Grid` or `Treemap` |
| `Title` / `ShowTitle` | `null` / `true` | The visible heading; it also names the meter (`aria-labelledby`) |
| `HeadingLevel` | `3` | The heading's level, clamped to 2–6 |
| `AriaLabel` | `null` | The name used when no heading is rendered (falls back to the localized "Context usage") |
| `OnRemoveItem` | unbound | `EventCallback<ContextUsageRemoval>` — no removal action without it |
| `OnCompact` | unbound | The compaction request — no compaction action without it |
| `Culture` | `null` | The culture of the token counts and percentages; the machine attributes stay invariant |
| `Class` | `null` | Additional CSS classes on the root |

## CSS classes

`.tm-context-usage`, `--over-budget`, `__title`, `__readout`, `__amount`, `__percent`, `__cache`,
`__cache-icon`, `__cache-text`, `__over-budget`, `__over-icon`, `__over-text`, `__meter`, `__meter--sr`,
`__bar`, `__segment`, `__segment--{kind}`, `__segment--highlight`, `__grid`, `__cell`, `__cell--used`,
`__cell--empty`, `__cell--{kind}`, `__cell--highlight`, `__treemap`, `__table`, `__row`, `__row--highlight`,
`__section`, `__swatch`, `__swatch--{kind}`, `__swatch--highlight`, `__label`, `__count`, `__tag`,
`__tag--required|--summarized|--omitted`, `__tokens`, `__share`, `__actions`, `__toggle`, `__remove`,
`__detail`, `__detail-cell`, `__items`, `__item`, `__item-label`, `__item-tokens`, `__item-remove`,
`__actions-bar`, `__compact`, `__compact-text`, `__empty`.

The style sheet is `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` (the package entry point imports the
partial), and it stays tokens-only.

## Wireframe

The `context-usage` UI role plus the `TmContextUsage` schema (`title`, `sections`, `variant`, `budget`,
`showTags`, `showCompact`) and its declarative stencil, which draws the readout, the bar / grid / treemap branch
and the legend rows.
