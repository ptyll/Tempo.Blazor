# TmChart — width, value axis and culture

Contract of the three additive pieces of `TmChart` added in 2.12.0 (plan 98c80bc3, phase 10b). Nothing here changes
the default look of a chart that does not opt in; the value axis and the number text changed in a visible but
non-API way (see the CHANGELOG, "Changed — chart axis, culture and label thinning").

## Responsive width (`Responsive`, opt-in)

By default the SVG draws into a fixed `viewBox="0 0 600 400"` that it *meets* into its box, so a chart in a wide card
is a 600-unit picture centred in the card and the type scales with the box.

```razor
<TmChart Type="ChartType.Line" Data="@data" Height="320px" Responsive="true" />
```

With `Responsive="true"` the viewBox is the **measured plot box** (`.tm-chart__plot`), one user unit = one CSS pixel:
the plot fills the card, the axis type keeps its real size and the label thinning below works with the real width.

- The box is reported by `Components/Charts/TmChart.razor.js` (a `ResizeObserver`). The module is imported **only**
  when `Responsive` is on; a default chart never touches JS. If the JS runtime is unavailable or the module fails to\n  load, the chart keeps the default 600 × 400 viewBox for the lifetime of the instance (the import is never retried, also\n  not when `Responsive` is switched off and on again).
- Before the first measurement (prerender, first frame) the chart draws in the default 600 × 400 viewBox.
- The rounded size is reported only when it changes; a degenerate 0-size box (hidden tab, `display:none`, viewport
  re-emulation) keeps the last good size.
- A plot smaller than its own padding is clamped to 160 × 160 units.
- `ChartType.Funnel` ignores the switch: its label type scale and outside gutter are calibrated on 600 × 400
  ([chart-funnel.md](chart-funnel.md)).
- CSS: `.tm-chart__plot[data-responsive]` takes the SVG out of flow (`position: absolute; inset: 0`) so the box gets
  its size from the card, never from the SVG — there is no feedback loop. `Height="auto"` falls back to the 600 : 400
  aspect ratio.

## Nice value axis

The grid of Bar, Line, Area, Waterfall and the stacked types used to divide `min..max` by five (`16.14, 11.28, 6.42`).
`ChartAxisScale.Nice` now picks a step of **1, 2 or 5 × 10ⁿ** and puts both ends of the axis on a tick:

| data range | axis |
|---|---|
| 6.42 … 16.14 | 0, 5, 10, 15, 20 |
| 0 … 45 (largest stack) | 0, 10, 20, 30, 40, 50 |
| −80 … 50 | −80, −60, −40, −20, 0, 20, 40, 60 |
| flat 0 | 0, 0.2, 0.4, 0.6, 0.8, 1 |

The number of grid lines follows the data (4–8) and the left gutter grows with the widest tick label (never below
the previous 50 units, which fits a five-character label).

## Culture (`Culture`, `ValueFormat`)

Every **text** the chart prints — axis ticks, value labels, point and index tooltips, the live announcement, the
funnel table and the time-axis dates — is formatted with `Culture ?? CultureInfo.CurrentCulture`, so a `cs-CZ` page
reads `16,1`. `ValueFormat` is a .NET numeric format string for those values (`"0.0 °C"`, `"N1"`, `"P0"`); `null`
keeps `0.##`; an invalid string falls back to the default instead of failing the render.

SVG coordinates, path data and every `data-*` attribute are a machine contract and stay on the invariant culture.

## Category label thinning

The step is `ceil((widestLabel × 6.5 + 4) / slotWidth)` — derived from the width a category owns, so 24 hourly labels
no longer overlap in the 600-unit plot (the old rule thinned only beyond 24 categories) and a wider responsive plot
keeps more of them. It applies to Bar, StackedBar, Line, Area, StackedArea, Waterfall, Heatmap columns, horizontal bar rows (`ChartAxisScale.RowStep`) and the time axis (`ChartAxisScale.LabelBudget` replaced the fixed 8).

## Wireframe stencil

Unchanged: the three parameters are render/format-only and add no visible stencil property (AGENTS.md rule 13 checked). A `Responsive` / `ValueFormat` / `Culture` knob in `BuiltInComponentSchemas` would be a control the canvas sketch ignores.

## Tests and demo

- bUnit: `ChartAxisScaleTests` (pure arithmetic), `TmChartAxisTests` (ticks, culture, format, thinning),
  `TmChartResponsiveTests` (viewBox follows the box, module lifecycle); node: `npm run test:core-modules`
  (`Components/Charts/__tests__/tm-chart.test.mjs`).
- E2E: `TmChartResponsiveE2ETests`, screenshots in `tests/Tempo.Blazor.E2E/__screenshots__/charts-f10b/`.
- Demo: the "Responsive width, nice axis and culture" section on `/charts`.
