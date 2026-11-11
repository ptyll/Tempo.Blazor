# Charts — index tooltip and sparkline tooltip

`TmChart` and `TmSparkline` are pure inline SVG (no JavaScript chart library). This page documents
the two additions that turn them into *explorable* graphics without adding a script tag: the
category **index tooltip** of `TmChart` and the opt-in **tooltip** plus text alternative of
`TmSparkline`. Both are additive — a chart that sets neither renders exactly what it rendered
before.

Related: [stat-card.md](stat-card.md), [trend-list.md](trend-list.md), [breakdown-card.md](breakdown-card.md).

## TmChart: `ChartTooltipMode`

| Mode | What it does |
|------|--------------|
| `Point` (default) | One card per hovered point, bar or slice — the pre-2.9.1 behaviour. |
| `Index` | One card per **category**: a full-height hover zone per index, a vertical crosshair on the active category and a card listing every visible series at that index. |

```razor
<TmChart Type="ChartType.Line" Data="revenue" TooltipMode="ChartTooltipMode.Index" />

<TmChart Type="ChartType.StackedBar" Data="costs" TooltipMode="ChartTooltipMode.Index">
    <TooltipIndexTemplate Context="ctx">
        <span class="tm-chart__tooltip-label">@ctx.Label</span>
        @foreach (var series in ctx.Series)
        {
            <span class="tm-chart__tooltip-value">@series.Label: @series.Value.ToString("N0")</span>
        }
        <span class="tm-chart__tooltip-value">@ctx.Total.ToString("N0")</span>
    </TooltipIndexTemplate>
</TmChart>
```

### Which types honour the mode

`Line`, `Area`, `Bar`, `StackedBar` and `StackedArea` — the types whose plot has a category axis.
Every other type (`Pie`, `Donut`, `HorizontalBar`, `StackedHorizontalBar`, `Funnel`, `Heatmap`,
`Treemap`, `Waterfall`) falls back to `Point`: a mode the plot cannot honour degrades to the mode it
can, rather than rendering half a crosshair.

### The hover zone is the whole category

The zones are transparent rects painted **above** the marks, which is what lets one zone serve a
whole category (the middle of the plot, the space between two bars, the top of the chart). The cost
of that shape: a click lands on the zone, not on the bar beneath it, so in index mode
`OnSegmentClick` reports the index **once**, carrying the first visible series' value.

### Keyboard

| Key | Action |
|-----|--------|
| `Tab` | Focuses the chart (it is one tab stop, not one per point). |
| `→` / `←` | Moves the index. The first press lands on the first/last category. |
| `Home` / `End` | Jumps to the first/last category. |
| `Esc` | Hides the card without leaving the chart. |

Focus alone shows nothing; leaving the chart clears the index. `aria-keyshortcuts` advertises the
keys and `aria-describedby` points at a visually hidden hint.

### DOM state contract

The root element carries `data-tooltip-mode` (`point`/`index`) and `data-index-ordinal` (`-1` when
nothing is highlighted), so a host, a test or an E2E spec can read the chart's own idea of the active
category instead of inferring it from pixels.

### One live region

The card lives inside the `role="img"` SVG, so it is *not* exposed to a reader — the single polite
live region is the only announcement, and it reads the whole index at once:

> Q1: Licenses 100, Services 120

Nothing is announced before the plot is explored (hover or arrow key), so a page with several charts
does not talk over itself. Legend-hidden series, `NaN` gaps and series shorter than the index are
absent from both the card and the announcement.

Values inside the card and the announcement are formatted with the **current culture**; SVG
coordinates stay invariant.

### The demo fixtures the browser guard reads

`/charts` renders three index-mode cards in the `charts-demo-index-tooltip` section, and their shape
is the point, not decoration:

| Card | Shape | What it proves |
|------|-------|----------------|
| `charts-demo-index-default` | Line, 2 series, 6 months | the default card: a zone per category, one row per series, one live region |
| `charts-demo-index-custom` | StackedBar, 2 series, `TooltipIndexTemplate` | a host template keeps the card, the placement and the total |
| `charts-demo-index-many` | Line, **7 series**, 6 months, own `AriaLabel` | the >5-series path |

The third card exists because the overlay defect the review found is invisible below six series: the
old card gave each dot a growing sequence band, and from the sixth series on a dot took the sequence
number of the hover-**zones** group. Blazor then treated the appearing dot as a replacement of the
group, the zone under the pointer was detached, and the browser never delivered the `mouseout` that
closes the card. A two-series demo can never fail that way. The browser lanes assert it on the real
seven-series chart
(`TmChartIndexTooltipE2EBase.IndexTooltip_SevenSeries_ListsEveryRow_AndKeepsItsHoverZoneConnected`):
the card lists seven rows with seven swatches, the hover zone is still `isConnected` while the card
is open, and leaving the plot clears both the card and `data-index-ordinal`.

## TmChart: the chart's accessible name

The `aria-label` of the SVG comes from `TmChart_AriaLabel` (`"{0} chart"` in English, so the English
output is byte-identical to the pre-2.9.1 hardcoded string), formatted with the chart type. A host
whose chart carries a name of its own passes `AriaLabel` (the seven-series demo card does), and it
wins over the generic text alternative instead of wrapping the chart in a `<figure>`.

## TmSparkline: text alternative and tooltip

| Parameter | Default | What it does |
|-----------|---------|--------------|
| `AriaLabel` | `null` | The graphic's text alternative. Null builds a localized summary of the series — minimum, maximum and last value in the current culture (`TmSparkline_Summary`). |
| `Color` | `null` | Stroke/fill of a Line, Bar or Area sparkline, as any CSS colour (typically a `var(--tm-*)` token). Null keeps the built-in blue. `Pie` keeps its categorical palette and ignores it. |
| `ShowMinMax` | `false` | A `min … max` caption in the corner. It is `aria-hidden`: the same two numbers are already in the `AriaLabel` summary, so announcing them twice would be a duplicate. |
| `ShowLastPoint` | `false` | A ring on the final value (Line and Area only). Decorative — the value is in the summary. |
| `ShowTooltip` | `false` | A tooltip for the hovered point/bar plus a focus stop: `→`/`←` walk the points, `Home`/`End` jump to the edges, `Esc` hides the card, and one polite live region announces `Point {n} of {m}: {value}`. `Pie` ignores it. |
| `Element` | `TmRootElement.Div` | Root element of the sparkline box: `TmRootElement.Div` (default) or `TmRootElement.Span`. A sparkline inside PHRASING content — the whole-card `<a>`/`<button>` target of `TmStatCard` — passes `TmRootElement.Span` so neither the box, its min/max caption, its tooltip nor its empty state nests a block element in an inline one. An undefined enum value falls back to `div`. |

```razor
<TmSparkline Data="trend" ShowMinMax="true" />
<TmSparkline Data="trend" Type="SparklineType.Area" Color="var(--tm-color-success)"
             ShowLastPoint="true" ShowTooltip="true" />
```

Why the tooltip stays inside the sparkline's own box: a sparkline usually lives in a table cell, a
grid area or a card header, and a bubble above the box would be clipped by the first ancestor with
`overflow: hidden`. The card is therefore positioned in the box's own corner.

## Styling

`--tm-*` tokens only, in both themes (dark redefines tokens in `tokens-dark.css`, never these
sheets):

| Class | Rule |
|-------|------|
| `.tm-chart__index-zone`, `.tm-chart__index-zones` | Transparent hit band per category, `pointer-events: all`. |
| `.tm-chart__crosshair` | Dashed vertical line in `--tm-border-color-strong`. |
| `.tm-chart__index-dot` | The active index's value marker on Line/Area. |
| `.tm-chart__tooltip--index`, `.tm-chart__tooltip-series*` | The index card: one row per series with the value pushed to the end. |
| `.tm-sparkline__point`, `--active` | Value markers; the active one is bigger **and** ringed, so the change does not rely on colour. |
| `.tm-sparkline__last-point`, `.tm-sparkline__minmax` | Decorative marks, `pointer-events: none`. |
| `.tm-sparkline__tooltip` | The in-box value card. |

## Accessibility checklist

- **One tab stop per graphic**, with `aria-keyshortcuts` and a visually hidden hint via
  `aria-describedby`.
- **`focus-visible`** paints `--tm-focus-ring` over a transparent outline; under `forced-colors` the
  outline becomes a `Highlight` ring (box-shadow is dropped there, an outline is not).
- **One live region** per graphic, `role="status"`, `aria-live="polite"`, `aria-atomic="true"`.
- **Reduced motion**: the only new animated subject (`.tm-sparkline__point`) is enumerated in
  `animations.css`'s `prefers-reduced-motion` block.
- **No colour-only information**: the active point/bar is bigger and ringed as well as coloured; the
  index card carries the series label and the value as text.
- **Culture-aware numbers** in every string a reader meets; invariant numbers for SVG geometry.
