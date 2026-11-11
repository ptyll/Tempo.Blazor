# TmBreakdownCard

`TmBreakdownCard` shows a whole in parts: the total, a proportional strip and a table of the parts,
biggest first. The rounded shares come from a **largest-remainder** split, so they add up to exactly
100 % — a naive per-part rounding renders three equal parts as 33 + 33 + 33 = 99 %, which makes the
card look broken.

```razor
<TmBreakdownCard Title="Traffic by channel" Parts="parts" />

@code {
    private readonly BreakdownPart[] parts =
    [
        new() { Id = "direct", Label = "Direct", Value = 4200, Color = "var(--tm-color-primary)" },
        new() { Id = "search", Label = "Search", Value = 3100 },
        new() { Id = "social", Label = "Social", Value = 1800 },
        new() { Id = "email", Label = "E-mail", Value = 900 }
    ];
}
```

## The model

| Member | Type | Meaning |
| --- | --- | --- |
| `Id` | `string` | Stable part identity, rendered as `data-part-id` (never a DOM id). Should be unique per card — see [Row identity](#row-identity). |
| `Label` | `string` | The part's name, rendered as the row header. |
| `Value` | `double` | The amount. A negative value counts as zero for the share while the row stays visible. |
| `Color` | `string?` | Optional CSS colour (token reference, hex, `rgb`/`hsl`/`oklch`/`color-mix`, named colour), written into the segment's and the swatch's inline `background-color`. |

A host-supplied `Color` is validated before it reaches the `style` attribute: a CSS class name (the
pre-2.9.0 `SubValueColor` mistake) or anything carrying `;`, `{`, `}` or `url(` emits no style and
the part falls back to the rotating token tone (`--tm-category-1`…`6`, which carry dark values).

## Row identity

`Id` is the row identity: the Blazor `@key` that keeps a row's DOM node while the list re-sorts, and
the key of the FLIP shift lookup. **It should be unique within one card.**

Duplicate and empty ids are *tolerated*, not trusted. `Id` defaults to `string.Empty`, so parts
written with only `Label`/`Value` would all carry the same `@key`; Blazor throws *"More than one
sibling of element 'tbody' has the same key value"* and the whole card fails. The card therefore
builds a private row key per row: the host id when it is non-empty and still unused, otherwise a
derived positional key in its own namespace. Consequences:

- every row renders, whatever the ids are — the table never throws;
- `data-part-id` stays the host's **raw** value (empty or repeated), never the internal key;
- a row **without a unique id is not tracked across a reorder**: an empty id, or a repeat of an id an
  earlier part already took, falls back to a positional key, and a positional key does not move by
  definition — so that row is not animated (the data order still changes immediately). Give the parts
  stable, unique ids to get the FLIP.

The `/data-display` demo carries a LIVE fixture for this (`[data-testid="breakdown-fixture"]`, inside
`breakdown-card-section`): two parts with an empty `Id` and two that share one, with its own period switch.
It is what the browser lane looks at —
`DataDisplayTrendBreakdownE2ETests.Breakdown_DuplicateAndEmptyIds_RenderAndReSortInTheBrowser` proves all
four rows render and still re-sort, and `…Breakdown_ForcedColors_KeepsTheStripPaint_AndTheBarKeepsItsCanvasTextEdge`
reads the same card's computed styles under Chromium's forced-colors emulation.

## The total and the shares

- `Total` is the declared whole. Null (default) means the parts' sum, and the rounded shares add up
  to exactly 100 %.
- A declared total **bigger** than the parts' sum adds an explicit, localized "Other" row and
  segment for the rest, so the visible shares still add up to 100 %.
- A declared total **below** the sum cannot be the whole: it is ignored (the sum is used) and the
  card never renders a share above 100 %.
- All-zero parts render 0 % rows and no segments — the rows are still the data.

`BreakdownMath.Compute` is the single implementation (unit-tested directly): it clamps negatives,
sorts biggest-first with a stable tie-break, computes the target percentage and distributes the
leftover points by largest remainder.

## The strip is decorative

The proportional strip is `aria-hidden="true"`, and the `<table>` is the accessible presentation: a
visually hidden `<caption>` names it (the title, the host's `AriaLabel`, or "Breakdown"), the columns
are `Label` / `Value` / `Share` and each part is a `th scope="row"`. One fact is read once — a
visually hidden duplicate would be announced twice and copied twice by a user selecting the card.

The row header stays a real table cell: `.tm-breakdown-card__label` is the `th` and declares no
`display` (a `display:flex` on a `th` makes the browser wrap it in an anonymous cell, and
WebKit/VoiceOver then loses the row-header role and the header links). The swatch + label layout rides
the inner `.tm-breakdown-card__label-inner` wrapper, and every swatch is `aria-hidden="true"` — the
label text already carries the meaning, so the empty swatch must not join the accessible name.

In forced-colors mode the strip and the swatches are painted only through `background-color`, which
the mode removes; they opt out with `forced-color-adjust: none` and the bar keeps a `CanvasText`
edge, so the proportions stay visible instead of collapsing into an empty box.

## Period switcher

`PeriodOptions` (`SelectOption<string>`) + `@bind-Period` render a single-choice segmented control
above the total. The card does not interpret the values: selecting one raises `PeriodChanged` and the
host loads the matching data. The switcher is always named for a reader (`PeriodLabel`, localized
default "Period"). The switcher currently renders through `TmRadioGroup Appearance=Segmented`; it
switches to `TmSegmentedControl` in phase 8 of plan ff043b2e without any change to this contract.

## Reorder animation (FLIP)

When the sorted order changes, the moved rows slide into their new places with a CSS-only FLIP. The
component computes each moved row's shift in row units ("First/Last/Invert") and writes it into that
row's own `--tm-breakdown-card-row-shift`; the `--flip` class then runs ONE keyframe animation
(`tm-breakdown-row-flip` in `animations.css`) from the old position home ("Play"). A single render
starts it — no second paint is needed to sample the start position — and no layout is measured and no
JavaScript is involved. A row that did not move gets neither the class nor the variable, and the row
key is versioned so a second reorder restarts the animation. A row that fell back to a positional key
is not animated — see [Row identity](#row-identity). `AnimateReorder="false"` turns it off,
and `prefers-reduced-motion: reduce` clamps the animation duration to 0.01 ms, so the order lands
immediately; the table's order is the same either way.

## States

`IsLoading` renders skeletons, a visually hidden localized "Loading breakdown" text and
`aria-busy="true"`; an empty `Parts` renders the localized empty message. `ShowTotal` and `ShowBar`
are opt-outs; the table always renders.

## Localization

| Key | English |
| --- | --- |
| `TmBreakdownCard_AriaLabel` | Breakdown |
| `TmBreakdownCard_Loading` / `TmBreakdownCard_Empty` | Loading breakdown / No data to show |
| `TmBreakdownCard_TotalLabel` / `PeriodLabel` / `RemainderLabel` | Total / Period / Other |
| `TmBreakdownCard_ColumnLabel` / `ColumnValue` / `ColumnShare` | Item / Value / Share |

Shares use the same percent convention as `TmTrendList` deltas: English `34%`, Czech `34&nbsp;%`
(no-break space, so the number and its symbol cannot wrap apart mid-cell).

## CSS classes

`.tm-breakdown-card`, `__title`, `__period`, `__total` (+ `__total-label`, `__total-value`), `__bar`,
`__segment` (+ `--tone-1`…`--tone-6`, `--remainder`), `__table`, `__row` (+ `--remainder`,
`--flip`), `__label` (+ `__label-inner`), `__swatch` (+ `--tone-1`…`--tone-6`), `__value`, `__share`,
`__empty`, `__skeleton`. Every colour, size and radius is a `--tm-*` token; the dark theme is a token
override, not a component block. The single exception is the forced-colors `CanvasText` edge on the
bar, which has to be the system colour.
