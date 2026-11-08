# TmChart — funnel (`ChartType.Funnel`)

A funnel is the conversion of a pipeline: one centered trapezoid per stage, each stage as wide as its
value, with the per-stage conversion measured against the **first** stage. This page is the contract for
the funnel's stage labels and zero-value stages; the rest of `TmChart` (legend, tooltip, click, theming)
is described in [chart-tooltip.md](chart-tooltip.md) and `JsonDocumentation/Components/TmChart.json`.

## Label placement — `FunnelLabelPlacement`

```razor
<TmChart Type="ChartType.Funnel" Data="@funnel" ShowValues="true"
         FunnelLabelPlacement="FunnelLabelPlacement.Outside" Height="320px" />
```

| Value | Where the label goes |
|---|---|
| `Auto` (default) | Inside the stage when its band is at least 150 viewBox units wide, beside the funnel otherwise. **This is the pre-2.11.0 rule** — a funnel that never sets the parameter renders the same geometry as before. |
| `Inside` | Always inside the band, centered. A thin band keeps its label centered over it instead of moving the text out of the shape. |
| `Outside` | Always in a gutter to the right of the funnel. The funnel narrows to 62 % of the plot width and starts at the plot's left edge, so every label reads at the **same x** — a column, not a symmetric hole — and never sits on a fill colour. |

The label **value** line (`value (pct %)`, rendered with `ShowValues`) always follows its stage label:
same x, same anchor, same ink. Both lines are `tspan`s of ONE `<text>` (`dy="-0.2em"` for the stage name,
`dy="1.25em"` for the value), so the distance between them follows the resolved font size instead of a
pair of fixed baselines.

### The gutter is measured, not guessed

An outside label starts one gap (10 viewBox units) right of the funnel's widest band and may use the rest
of the 600-unit viewBox. The funnel is therefore sized from the **longest** line the stages will render:

- the plot keeps 62 % of the chart width (`--tm-chart-funnel-*` viewBox arithmetic in
  `TmChart.Funnel.cs`, `FunnelPlotWidth`), narrowed further when
  `longest line × worst-case font × 0.6 em/char` needs more gutter than that share leaves;
- it never narrows below 45 % of the width — past that point the label is **truncated** with an ellipsis
  and the full text moves into the label's `<title>` tooltip, so a very long stage name is readable on
  hover instead of being clipped at the viewBox edge.

The worst case is the narrow type step (`--tm-chart-funnel-label-size-narrow`,
`--tm-chart-funnel-value-size-narrow`), because clipping happens in viewBox units whatever the container's
rendered scale is. `TmChartFunnelLabelContrastTests` pins the two C# constants to those tokens.

## Ink — tokens, never an inline hex

The label no longer carries a `fill` attribute. Its placement class names the ink:

| Class | Token |
|---|---|
| `tm-chart__funnel-label--inside` | `--tm-chart-funnel-label-inside` (light ink for a dark fill) |
| `tm-chart__funnel-label--inside tm-chart__funnel-label--on-light` | `--tm-chart-funnel-label-inside-on-light` (dark ink for a light fill) |
| `tm-chart__funnel-label--outside` | `--tm-chart-funnel-label-outside` (`var(--tm-text-secondary)`) |

An inside label sits **on the stage's fill**, not on a surface, so the component resolves which of the
two inside inks measures better against the resolved fill (WCAG relative luminance, the 0.179 crossover)
and adds `--on-light` when the dark ink wins. The built-in palette (`#3b82f6 … #6366f1`) measures
2.0–4.4:1 against white, so most stages take the dark ink — that is the accessibility fix, not a taste
choice. A fill the component cannot resolve (a `var(--tm-…)` token or a colour keyword) keeps the light
ink, the pre-2.11.0 assumption of a dark brand fill.

`--tm-chart-funnel-label-outside` is an alias of `--tm-text-secondary` and is therefore **re-declared on
the dark theme roots** (`[data-theme="dark"]`, `.tm-dark`) and in the light island
(`[data-theme="light"]`) of `tokens-dark.css`: a token declared only on `:root` substitutes its `var()`
where it is declared, so a parent-scoped dark theme would otherwise keep the light ink.

## Zero and NaN stages — `FunnelMinSegmentWidth`

A stage whose value is `0` used to collapse to a zero-width path: the row was in the data but invisible
in the chart. The funnel now floors every band at `FunnelMinSegmentWidth` viewBox units (default `4`), so
a stalled stage draws a thin, visible band and keeps its `0 (0 %)` label. `NaN`/`Infinity` and negative
values are drawn as `0` — never as `NaN` geometry. `FunnelMinSegmentWidth="0"` restores the old
zero-width behaviour.

## Type scale — the RENDERED size is the contract

A funnel's text is drawn in **viewBox units** and the SVG meets into its box
(`preserveAspectRatio="xMidYMid meet"`), so what a reader meets is

```
rendered px = font-size (viewBox units) × min(plotWidth / 600, plotHeight / 400)
```

That second factor is a **scale, not a width**, and it is height-limited on a wide desktop card as well as
on a phone: a 320 px chart card keeps ≈ 271 px of plot once its legend is laid out, so a `max-w-2xl`
672 px card meets at ≈ 0.68 and a phone's ~358 px card at ≈ 0.60. The pre-2.11.0 funnel used a fixed
12-unit label (a 10-unit value line), which reached the reader at ≈ 8,2 px (≈ 6,8 px) in the first case and
at ≈ 7,2 px (≈ 6,0 px) on a phone, and a width-only container query could never fire at 1440 px.

Two containers fix it, and both are on the funnel's own DOM:

```css
.tm-chart--funnel            { container: tm-chart-funnel / inline-size; }      /* the width step */
.tm-chart--funnel .tm-chart__plot { container: tm-chart-funnel-plot / size; }   /* both axes */
```

The root is a **named** container, so `@container tm-chart-funnel (width < 32.5rem)` never binds to a
nested container a host dropped inside the chart. The thresholds are component-local **density** steps, so
they are declared in `rem` behind half-open range syntax — the form the scheduler's card queries use —
while `TmBreakpoints` (and its guard) owns the four LAYOUT widths in `px`. The plot box (`.tm-chart__plot`,
the box the SVG meets into) is a **size** container, because the compensation needs the block axis:

```css
/* the exact compensation — a length divided by a container length turns plot pixels into viewBox units */
@supports (font-size: calc(1px * 600 / 100cqw * 1px)) {
    .tm-chart__funnel-label {
        font-size: max(
            var(--tm-chart-funnel-label-size-viewbox),
            calc(var(--tm-chart-funnel-label-size) * 600 / 100cqw * 1px),
            calc(var(--tm-chart-funnel-label-size) * 400 / 100cqh * 1px));
    }
}
```

With that in place the rendered size is the token's **floor** at every container size: a stage label never
renders below 12 px, a value line never below 11 px. The `max()` keeps the viewBox step as the floor, so a
large chart keeps type proportional to its own shape instead of shrinking to 12 px words.

| Token | Default | Meaning |
|---|---|---|
| `--tm-chart-funnel-label-size` | `12px` | **Rendered** floor of a stage label (px on screen) |
| `--tm-chart-funnel-value-size` | `11px` | **Rendered** floor of the value line |
| `--tm-chart-funnel-label-size-viewbox` | `18px` | Wide-container fallback in viewBox units (≈ 12,2 px at a 0.68 scale) |
| `--tm-chart-funnel-value-size-viewbox` | `17px` | Wide-container value fallback (≈ 11,5 px) |
| `--tm-chart-funnel-label-size-narrow` | `23px` | Below a 32.5 rem container (≈ 520 px) **or** a 15.3125 rem plot (≈ 245 px) (≈ 13 px at a 0.56 scale) |
| `--tm-chart-funnel-value-size-narrow` | `21px` | The same step for a value line (≈ 11,9 px) |

The four `-viewbox`/`-narrow` tokens are the **fallback** for an engine that cannot divide a length by a
container length: it drops the whole `@supports` declaration and keeps two discrete viewBox steps, which
are dimensioned to reach the same floor at the scales a funnel actually runs at. The bigger step is taken
by **two** conditions — a container under 32.5 rem (≈ 520 px) *or* a plot under 15.3125 rem (≈ 245 px) tall
— because the scale is height limited: a wide card that is short needs the same bigger type as a phone.
Every query and the compensation read the chart's own box — never the viewport — so a funnel in a dashboard
column and a funnel in a full-width page each get the scale their own container needs.

The fallback's envelope: a step is a constant, so it can only be calibrated for the scales it meets. The
two steps cover the cards a funnel lives in (any width at the default ~320 px height, plus phone-sized
containers). A *far* smaller box — a chart of ~200 px height — is still better than it used to be but is
only guaranteed by the compensation formula; pass a taller `Height` for a hero funnel.

### `Width="auto"` and container containment

`container-type` applies **size containment**, so the contained element's own size can no longer come from
its content:

- the root carries only `inline-size` containment, so a block-level `Width="auto"` still fills its parent
  (the default `Width="100%"` is unaffected). A host that sets `Width="auto"` inside a **shrink-to-fit**
  context — a flex row, a float, an inline-block — asks the root to size itself from content it is no
  longer allowed to measure, and gets a **zero-width chart**. Give the chart an explicit width there;
- the plot box is a `size` container, so its height must be definite. Height comes from the chart's own
  `Height` (default `300px`), and `aspect-ratio: 600 / 400` — the viewBox's shape — is the fallback when a
  host leaves `Height` auto or uses a percentage of an auto-height parent. `Height="auto"` is not a
  supported value for a funnel: pass a length.

## Screen readers

The SVG is `role="img"` with a localized name, so a reader hears only "Funnel chart". The funnel
additionally renders a visually hidden (`tm-sr-only`) table — one row per **drawn** stage — with its
caption and column headers: stage, value and conversion from the first stage as
`value / first (pct %)`. Toggling a stage off through the interactive legend removes its row, so the
table keeps describing exactly what is on screen. The strings are localized (`TmChart_Funnel*`) and the
three numbers follow the **reader's culture**: `#,0.##`, so a Czech reader gets
`610 / 1 240 (49,19 %)` and an English one `610 / 1,240 (49.19 %)`, with a non-breaking space before the
percent sign. The SVG value line uses the same formatting.

## Migration from 2.10.x

No API is removed and no parameter changed meaning. What a host can notice:

| | Before (≤ 2.10) | After (2.11) |
|---|---|---|
| Label position | Only the built-in rule (inside a wide band, beside a narrow one) | Same default; `Inside` / `Outside` are opt-in |
| Inside ink | `#ffffff`, 2.0–4.4:1 on the palette | Token ink, ≥ 4.5:1 on the resolved fill |
| Beside-the-funnel ink | `#6b7280`, hardcoded | `--tm-text-secondary` |
| Zero-value stage | Zero-width, invisible | `FunnelMinSegmentWidth`-wide band with its `0 (0 %)` label |
| Label attributes | Inline `fill` + `font-size` | Placement class + tokens |
| Label type scale | Fixed 12 / 10 viewBox units, so ≈ 8,2 / 6,8 px on a desktop card and ≈ 7,2 / 6,0 px on a phone | Tokens with a RENDERED floor: ≥ 12 px label, ≥ 11 px value at every container size |
| Stage numbers | Invariant `1000` / `45 %` | The reader's culture: `1,000` / `45 %` (en-US), `1 000` / `45 %` (cs-CZ) |
| A label longer than the gutter | Ran past the viewBox edge and was clipped | The funnel narrows to fit; past 45 % the label is truncated with `…` and the full text is its tooltip |

To keep the old white inside ink on every fill, set both inside tokens to the same value and accept the
contrast it measures:

```css
:root {
  --tm-chart-funnel-label-inside: #ffffff;
  --tm-chart-funnel-label-inside-on-light: #ffffff;
}
```

To restore the zero-width stage, pass `FunnelMinSegmentWidth="0"`.
