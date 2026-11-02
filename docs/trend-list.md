# TmTrendList

`TmTrendList` shows a list of metrics and their change over a period — the "what moved" companion
of a KPI card. One row per `TrendItem`, rendered as a **semantic list** (the default) or as a
**table** with real column headers.

```razor
<TmTrendList Title="Key metrics" Items="metrics" />

@code {
    private readonly TrendItem[] metrics =
    [
        new() { Id = "revenue", Label = "Revenue", NumericValue = 12450, Delta = 12.5, Sparkline = [8, 12, 10, 16] },
        new() { Id = "churn", Label = "Churn", Value = "2.1 %", Delta = -3, PositiveIsGood = false },
        new() { Id = "tickets", Label = "Open tickets", Value = "42", Delta = 0 }
    ];
}
```

## The model

| Member | Type | Meaning |
| --- | --- | --- |
| `Id` | `string` | Stable row identity, rendered as `data-item-id` (never a DOM id — a host value is not one, and a slug collides). |
| `Label` | `string` | The metric's name; also the link text when `Href` renders. |
| `Value` | `string?` | Host-formatted value, rendered verbatim. Ignored when `NumericValue` is set. |
| `NumericValue` | `double?` | The value as a number, formatted with `ValueFormat` (default `N0`) and `Culture`. |
| `Delta` | `double?` | The change in **percent points**. Null renders no change marker. |
| `PositiveIsGood` | `bool` (default `true`) | Whether a rise is the good outcome for THIS row. |
| `Sparkline` | `double[]?` | Optional mini chart through `TmSparkline`. |
| `Href` | `string?` | Optional link target, rendered as one native `<a>` around the label. |

The list keeps the host's order — a trend is often chronological, so the component never re-sorts.

## Delta, direction and tone

`Delta` is formatted with the culture's own percent convention: `12.5` renders as `+12.5%` in English
and `+12,5&nbsp;%` in Czech, because the symbol and its spacing come from
`NumberFormatInfo.PercentPositivePattern`. `DeltaFormat` overrides the number's shape; a malformed
format falls back to the convention instead of throwing out of a render.

Three facts stay separate, exactly like `TmStatCard`:

| Fact | Decided by | What it paints |
| --- | --- | --- |
| direction | the sign of `Delta` | the arrow icon, `data-trend="up\|down\|flat"` and the hidden word (`Increase` / `Decrease` / `No change`) |
| verdict | `TrendItem.PositiveIsGood` | only the tone class (`--positive` / `--negative` / `--neutral`) |
| number | `DeltaFormat` + `Culture` | the delta text |

So a falling churn rate passes `PositiveIsGood=false`, is painted as good news and still reads
"Decrease". Colour is never the only carrier: the arrow is `aria-hidden` (decorative) and the
direction word is a `tm-sr-only` span, so a reader hears the fact once.

## Layouts

`Layout="TrendListLayout.Table"` renders a `<table>` with a visually hidden `<caption>` (the title,
the host's `AriaLabel`, or the localized default), `th scope="col"` headers (`LabelHeader`,
`ValueHeader`, `ChangeHeader` override them) and a `th scope="row"` per metric. The list layout is a
`<ul>` whose accessible name is the heading (through `aria-labelledby`) or the localized default.

## Links

`TrendItem.Href` turns the label into one native `<a>` — the row stays a single focus stop. Only a
relative URL and the `http`, `https` and `mailto` schemes render; a refused value (`javascript:`,
`data:`, `vbscript:`, protocol-relative `//host`, or anything carrying a control character a browser
would strip before reading the scheme) means **no target at all** rather than a rewritten link. The
focus indicator is the library's two-layer `--tm-focus-ring` plus a transparent outline, so forced
colors still paints the system Highlight ring.

## States

- `IsLoading` renders skeletons plus a visually hidden localized "Loading values" text and
  `aria-busy="true"` on the root; no stale row stays on screen.
- An empty `Items` renders the localized empty message. There is no live region: a list that
  re-renders on every poll must not become a chorus.

## Localization

| Key | English |
| --- | --- |
| `TmTrendList_AriaLabel` | Trend list |
| `TmTrendList_TrendUp` / `TrendDown` / `TrendFlat` | Increase / Decrease / No change |
| `TmTrendList_Loading` / `TmTrendList_Empty` | Loading values / No data to show |
| `TmTrendList_ColumnLabel` / `ColumnValue` / `ColumnChange` | Item / Value / Change |

## Accessibility

- Semantic list or real table; the table has a caption, column headers and row headers.
- The direction is an icon + a hidden word, never colour alone.
- One native focus stop per row, with the shared focus ring in light, dark, forced-colors and coarse
  pointers (the link and the row reach the 44 px touch target on `(pointer: coarse)`).
- No animation, so no reduced-motion rule is needed.

## CSS classes

`.tm-trend-list`, `--table`, `__title`, `__items`, `__item`, `__label`, `__link`, `__value`, `__trend`
(+ `--positive|--negative|--neutral`), `__delta`, `__sparkline`, `__table`, `__row`, `__change`,
`__empty`, `__skeleton`. Every colour, size and radius is a `--tm-*` token; the dark theme is a token
override, not a component block.
