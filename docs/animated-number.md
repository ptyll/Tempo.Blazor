# TmAnimatedNumber

`TmAnimatedNumber` renders one metric as a number that counts up to its target value. It is the KPI
read-out of a dashboard: a value that lands rather than appears.

```razor
<TmAnimatedNumber Value="@revenue" Format="N0" Suffix=" users" />
```

## The value is the final number, the animation is decoration

The component renders the **final** formatted value into the document and lets the module repaint that
same text between frames. That gives three properties at once, and all three are pinned by
`TmAnimatedNumberTests`:

- **One node carries the value.** A screen reader and a copy/paste meet the number exactly once. The
  first paint already shows the target — there is no flash of `0`.
- **A running ticker announces nothing.** The repainted text is not a live region, so no intermediate
  frame reaches assistive technology. `Announce="true"` is the only way to opt in: the component then
  renders one polite, atomic `role="status"` region holding the final value, and the visual span steps
  out of the accessibility tree so the region stays the only text a reader meets. A page with many
  tickers must not become a chorus, which is why the opt-in is per instance.
- **A host without JavaScript gets the right number.** Prerender, a torn-down circuit and a missing
  module all end in the same place: Blazor's final text, no exception, no placeholder.

## Reduced motion, duration and retargeting

- `prefers-reduced-motion: reduce` is probed once, before the first animation. When it is set, the
  ticker never starts; the component simply renders the target.
- `Duration` is milliseconds, default `600`, clamped to 0–10000. `Duration="0"` means instant, and no
  JavaScript is imported at all.
- `Animate="false"` is a pure-Blazor path: no import, no motion-preference probe.
- A value that changes while the ticker runs **retargets** the animation: the loop starts again from
  the number currently on screen towards the new target, so a live metric never finishes an obsolete
  animation first and never jumps back to `StartValue`. `StartValue` only seeds the first frame
  (default `0`).
- An unchanged value on an unrelated parent render does not replay the animation.

## Formatting

`Format` is a .NET numeric format string, default `"N0"` (whole, group-separated numbers).
`Culture` is null by default, which means the current culture at render time. `Prefix` and `Suffix`
wrap the number (a currency symbol, `" %"`); they are host data, not localized strings.

The final text always comes from `Value.ToString(Format, Culture)`. The intermediate frames need the
same shape in JavaScript, so `AnimatedNumberFormat.Describe` maps the format into an
`Intl.NumberFormat` descriptor (locale, style, fraction digits). `N`, `F`, `P`, `C` and the custom
`0`/`#` shapes are mirrored; an unsupported format (`E`, `G`, `X`, …) still animates, and only its
in-flight frames fall back to whole numbers — the last frame is always the exact .NET result.

## Accessibility

- No key handling and no focusable surface: the component is a read-out.
- The digits are `font-variant-numeric: tabular-nums`, so the text does not reflow frame by frame.
- `Announce` renders exactly one live region (see above); there is no second announcement channel.

## Module contract

`tm-animated-number.js` is served from `_content/Tempo.Blazor/js/tm-animated-number.js` and exports:

| Export | Purpose |
| --- | --- |
| `prefersReducedMotion()` | the machine's motion preference (missing `matchMedia` means "no preference") |
| `animate(element, from, to, duration, options)` | starts or retargets the ticker on one element; `from = null` continues from the painted number |
| `cancel(element)` | stops one element's loop (idempotent) |
| `cancelAll()` | stops every loop the module started (host teardown path) |

The module keeps one loop per element in a `WeakMap`, stops a loop when its element left the document,
and is cancelled by the component's `DisposeAsync` — a removed ticker never keeps a
`requestAnimationFrame` frame alive.

## CSS

`.tm-animated-number` is the inline-flex shell; `.tm-animated-number__value` carries the number.
Both paint through semantic tokens only, the sheet adds no CSS animation (so it adds no subject to the
reduced-motion enumeration in `animations.css`), and the committed bundle
`tempo-blazor.bundled.css` carries the rules.

## Wireframe

The component is a wireframe citizen with the `animated-number` role and a declarative stencil
(`value`, `format`, `prefix`, `suffix`, `announce`), so a designer can place a KPI read-out without
the editor depending on the live component.
