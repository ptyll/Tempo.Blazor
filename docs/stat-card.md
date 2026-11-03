# TmStatCard

`TmStatCard` shows one metric: a value, its label and an optional secondary line. Since 2.10.0 it also
carries an **opt-in** KPI surface — a delta with its direction and comparison label, an icon, a
sparkline, a loading skeleton, an animated numeric value and a whole-card link or button target.

```razor
<!-- 2.9.x card: byte-identical markup -->
<TmStatCard Title="Active users" Value="12,345" />

<!-- 2.10.0 KPI card -->
<TmStatCard Title="Revenue"
            NumericValue="@revenue"
            AnimateValue="true"
            Delta="12.5"
            ComparisonLabel="vs. last month"
            Icon="trending-up"
            Sparkline="@dailyRevenue" />
```

## Additivity is the contract

A card that passes none of the new parameters renders the exact 2.9.x markup — byte-identical: same
root element, same three spans, same whitespace, no `data-testid`, no extra class. `TmStatCardTests` pins that with
an exact string comparison (`WithoutNewParameters_RendersTheExactPreTwoTenMarkup`), because a host
that styles `.tm-stat-card` or asserts on its DOM must not notice this release. The card documents its
own whitespace too: the value/label pair carries the legacy separator explicitly
(`ValueLabelSeparator`) since the value is now produced by an `@if`/`@else` chain.

## Delta, direction and tone

`Delta` is the change over the comparison period **in percent points**: `12.5` renders as `+12.5%` in
English and `+12,5 %` in Czech, because the symbol and its spacing come from the culture's own percent
pattern (`NumberFormatInfo.PercentPositivePattern`), not from a hardcoded string. `DeltaFormat` takes
an explicit .NET format when a host wants a different shape; a malformed one falls back to the culture
convention instead of throwing out of a render.

Three separate facts drive the trend marker, and keeping them apart is the point:

| Fact | Parameter | What it decides |
| --- | --- | --- |
| direction | `TrendDirection` (`Auto` derives it from the sign of `Delta`) | the arrow icon, the `data-trend` slug and the hidden word (`TmStatCard_TrendUp` / `Down` / `Flat`) |
| verdict | `PositiveIsGood` (default `true`) | only the tone: `tm-stat-card__trend--positive` / `--negative` / `--neutral` |
| comparison | `ComparisonLabel` | the host's own wording ("vs. last month") rendered next to the delta |

So a metric where less is better (churn, latency, error rate) passes `PositiveIsGood="false"` and still
reads "Decrease" — it is simply painted as the good outcome. Colour is never the only carrier: the
arrow and the visually hidden direction word travel with it. A direction without a delta is
legitimate: `TrendDirection="Down"` renders the marker without a number.

## The rest of the surface

- **`Icon`** — an icon name (`IconNames` or a registry entry) in the card's top corner. Decorative by
  design (`aria-hidden`): the label and value already name the card. Because the icon is out of the flow,
  setting it also emits `tm-stat-card--has-icon`, which reserves the corner gutter on the value and the
  label (`padding-inline-end: calc(2.25rem + var(--tm-space-2))`) — without it a long title or a wide
  value runs underneath the icon. A loading card paints no icon and therefore carries no modifier, and
  the default card keeps its single class.
- **`Sparkline`** — `double[]` rendered through `TmSparkline` under the metric. On a static card the
  chart keeps its own accessible name; on an interactive card (`Href`/`OnClick`) the wrapper is
  `aria-hidden="true"`, because the target's accessible name is built from its content and the chart's
  `role="img"` label would otherwise be spliced into it ("Revenue 1,234 Increase +12.5 % vs last month
  Sparkline"). Next to a labelled value the chart is decoration, so hiding it loses nothing.
- **`IsLoading`** — three skeletons in the value slot plus a visually hidden localized "Loading" text
  and `aria-busy="true"` on the root. The label stays visible because it is static information.
- **`NumericValue` + `AnimateValue` + `ValueFormat` + `ValuePrefix`/`ValueSuffix` + `Culture`** — the metric as a number, rendered
  through `TmAnimatedNumber`. `NumericValue` wins over the string `Value` (a card cannot show two
  values), and `AnimateValue` defaults to `false`, which is a pure-Blazor path with no JavaScript.
  `ValuePrefix`/`ValueSuffix` wrap it (a currency symbol, `" %"`). See
  [animated-number.md](animated-number.md) for the reader contract.

## One native target for the whole card

`Href` renders the root as `<a class="tm-stat-card tm-stat-card--interactive">`; `OnClick` renders it
as `<button type="button" class="tm-stat-card tm-stat-card--interactive">`. Either way the whole
surface is one native focus stop with the platform's own activation — no click layer over a `div`, no
JavaScript, and `OnClick` wins when both are set.

Only a relative URL and the `http`, `https` and `mailto` schemes render. A refused value
(`javascript:`, `data:`, `vbscript:`, a protocol-relative `//elsewhere.example`) means **no target at
all**: the card falls back to the plain `div` rather than rendering a link that lies about being one,
and the value is trimmed first because a browser strips leading space characters before resolving the
scheme. `button.tm-stat-card` resets the browser chrome (`appearance`, `font`, `text-align`) so the two
targets look identical.

The scheme is parsed by the component, not by `Uri`: the WHATWG URL parser removes ASCII tab, LF and CR
from a URL before it reads the scheme, so `"java\tscript:alert(1)"` navigates exactly like
`"javascript:alert(1)"` while `Uri.TryCreate` calls the first one relative. An href containing **any**
control character is refused outright (stripping it is not enough — the stripped text can still spell a
script URL), and a scheme is "a colon before the first `/`, `?` or `#`", matched case-insensitively
against the three accepted names (`docs/page:1` is a relative path, not a scheme).

Accessibility of the interactive card: one focus indicator taken from the shared
`--tm-focus-ring` token (`box-shadow`, so it follows the dark theme and clears 3:1 against the card
surface in both themes), the same ring from the system `Highlight` colour under `forced-colors` (the rule
keeps an `outline: 2px solid transparent`, which is invisible normally and painted by the UA there), and
a `min-block-size: var(--tm-touch-target)` floor on a coarse pointer.

**Phrasing content:** a `<button>`/`<a>` may only contain phrasing content, and two opt-in children
default to block roots — the loading branch's `TmSkeleton` (`<div>`) and `TmSparkline` (`<div>`). The card
therefore renders both with `Element="TmRootElement.Span"` (see [chart-tooltip.md](chart-tooltip.md) for
the sparkline parameter), so a whole-card target holds phrasing content only and `button.tm-stat-card`
with `Sparkline`/`IsLoading` is valid HTML. Both parameters default to `TmRootElement.Div`, so a
standalone sparkline or skeleton is unchanged. A nested **interactive** control is still not supported in
any case.

## CSS

The opt-in parts live in `_stat-card.css` and paint through semantic tokens only — the dark theme
redefines tokens, never this sheet. Classes: `tm-stat-card`, `tm-stat-card--interactive`,
`tm-stat-card--has-icon`, `tm-stat-card__icon`, `tm-stat-card__trend(--positive|--negative|--neutral)`,
`tm-stat-card__delta`, `tm-stat-card__comparison`, `tm-stat-card__sparkline`,
`tm-stat-card__skeletons`, plus the 2.9.x `tm-stat-value`, `tm-stat-label` and `tm-stat-subvalue`.

## Wireframe

The stat card keeps its `stat-card` role; its schema gained `delta`, `deltaFormat`,
`comparisonLabel`, `trend` (an explicit override), `positiveIsGood`, `icon`, `sparkline`, `loading`
and `animateValue`, and the built-in pack draws each of them conditionally, so a placed card can
express the same KPI surface the component renders.
