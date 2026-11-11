# TmUsageMeter

`TmUsageMeter` shows **how much of a quota is gone** — credits, seats, API calls, storage. One
`role="meter"` track carries the used share of the limit, next to it a culture-formatted readout, a
warning/critical state, an over-limit reading, the category breakdown, the reset date and the daily
history.

```razor
<TmUsageMeter Title="Monthly credits"
              Used="8200" Limit="10000" Unit="credits"
              ResetsAt="@resetsAt"
              Categories="@categories"
              DailyUsage="@daily">
    <NearLimitContent>
        <TmButton Variant="ButtonVariant.Primary" Size="ButtonSize.Sm" OnClick="Upgrade">
            Buy more credits
        </TmButton>
    </NearLimitContent>
</TmUsageMeter>

@code {
    private readonly DateTimeOffset resetsAt = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly double[] daily = [180, 420, 260, 640, 310, 880, 520];
    private readonly UsageCategory[] categories =
    [
        new() { Key = "llm", Label = "LLM", Used = 5500 },
        new() { Key = "tools", Label = "Tools", Used = 1400, Color = "var(--tm-color-info)" },
        new() { Key = "storage", Label = "Storage", Used = 1300 }
    ];
}
```

## The model

`UsageCategory` (namespace `Tempo.Blazor.Components.DataDisplay`) explains the consumption — it never
replaces the headline number.

| Member | Type | Meaning |
| --- | --- | --- |
| `Key` | `string` | Programmatic key, rendered as `data-category-key` (never a DOM id). A category with an empty key is skipped and its usage becomes the "Other" row. |
| `Label` | `string` | Row header of the category table. A blank label falls back to `Key`. |
| `Used` | `double` | The amount consumed, in the meter's own unit. A negative amount counts as zero for the segment, the share and the row's cell (it reads `0`), but the row itself stays visible. |
| `Color` | `string?` | A CSS colour (`var(--tm-*)`, hex, `rgb`/`hsl`/`oklch`/`color-mix`, a named colour). It wins over the rotating `--tm-category-*` fallback tone; anything that is not a colour emits no style. |

## The meter value contract

The track is the meter element, so the machine-readable value lives on exactly one node:

```html
<div class="tm-usage-meter__track" role="meter"
     aria-valuemin="0" aria-valuemax="10000" aria-valuenow="8200"
     aria-valuetext="8,200 of 10,000 credits, 82%"
     aria-labelledby="tm-usage-meter-title-…"
     aria-describedby="tm-usage-meter-reset-…"></div>
```

- `aria-valuemax` is `Limit`, `aria-valuenow` is `Used` and `aria-valuemin` is always `0`. They are
  formatted **invariantly** — an attribute is read by assistive technology, not by a human, and a
  Czech group separator inside `aria-valuenow` would be a different number.
- `aria-valuetext` is the localized, culture-formatted sentence: `"{amount} of {limit} {unit}, {percent}"`.
  The unit is dropped (together with its space) when the host supplies none, and the percentage comes
  from the culture's own `NumberFormatInfo.PercentPositivePattern`, so Czech renders `82 %`.
- The visible readout (`.tm-usage-meter__amount` / `__percent`) repeats the **same** numbers for
  sighted users — the ordinary visible-label pattern of a native `<meter>`. There is no second,
  hidden copy of the number anywhere.
- `Title` names the meter through `aria-labelledby` and is VISIBLE in every presentation, including
  `Compact` (where it is a plain `<span>` label instead of a heading). Without a title the host's
  `AriaLabel` (or the localized default "Usage") is the `aria-label` — never both, so a meter always
  carries exactly one name. The reset line and the over-limit line are description targets, so the
  value the reader lands on already includes when the quota renews.
- `HeadingLevel` (default `3`, clamped to 2–6) picks the heading tag of the full presentation, so the
  meter can sit under an `h1`, `h2` or `h4` page outline without breaking it. The compact label is not
  a heading at all.
- The meter is **not interactive**: it has no tab stop of its own, because every fact it shows is
  document text a reader meets in order. Nothing is announced on load.

## States

| State | `data-state` | Class | What it renders |
| --- | --- | --- | --- |
| Normal | `normal` | — | the primary fill, no badge |
| Warning (`WarningThreshold`, default `0.8`) | `warning` | `tm-usage-meter--warning` | warning fill, an alert-triangle icon, "Near limit", and the `NearLimitContent` slot |
| Critical (`CriticalThreshold`, default `1.0`) | `critical` | `tm-usage-meter--critical` | danger fill, an alert-circle icon, "Limit reached", and the `NearLimitContent` slot |

While the state is warning or critical the **track also carries a ring** in the state colour, because
a category breakdown can cover the whole fill — the verdict has to survive the segments, the way the
badge's word and icon already do.

The thresholds are **ratios of the limit**, not percentages, and the state is checked critical-first,
so a `CriticalThreshold` below the warning one still wins. A negative threshold counts as `0` (warn
from the first measurable amount) and `double.NaN` falls back to the documented default rather than
silently disabling the state. With an unknown limit (`0` or less) nothing is measurable, so the meter
stays **normal whatever the thresholds say** — a negative threshold cannot claim a verdict for a
`0 of 0` meter. The verdict is carried by an **icon, a text and a colour** — in that order of
importance: a monochrome or forced-colours rendering keeps the glyph (triangle vs. circle) and the
word, so colour is never the only carrier.

## Over the limit

A usage above the limit is not hidden: `aria-valuenow` reports the true amount (`11200`), the
percentage keeps counting (`112%`), the fill stays capped at 100 % of the track (a bar cannot paint
past its box) and the meter adds `data-over-limit="true"` plus a localized over-limit line with the
exact excess in the host's unit ("Over limit by 1,200 credits").

## Categories

`Categories` draws one segment per category **inside the filled part of the track** (the strip is
`aria-hidden` — it is decorative, and the segments carry no hover-only `title`) and lists the same data
as a real `<table>`: a visually hidden `<caption>`, `th scope="col"` headers (Category / Used / Share)
and a `th scope="row"` per category. The strip repeats nothing to a reader — the table is what a screen
reader and a copy/paste get.

- The segment and the row share one clamped amount, one fallback tone and one remainder decision, so
  the strip and the table can never disagree.
- The shares are rounded with the **largest remainder** method, so they add up to exactly 100 %
  (three equal parts are `33 %` + `33 %` + `34 %`, never 99 %).
- `Used` stays the headline: the categories only explain it. Usage they do not account for becomes an
  explicit localized **"Other"** row (`tm-usage-meter__row--other`, plus a neutral remainder segment),
  so the table never silently loses a number.
- An inconsistent host (categories claiming more than `Used`) is tolerated: the strip's weights are
  measured against the larger of the two, so the segments can never paint past the fill.
- A category's `Color` is validated as a CSS colour (a class name or a payload carrying `;`, `{`, `}`
  or `url(` emits no style) and written into the segment's and the swatch's inline `background-color`.

## Compact

`Compact="true"` renders the visible title, the track, the amount, the percentage and the state badge
— and nothing else. The reset line, the daily sparkline and the category table are dropped, because a
breakdown without its numbers would be colour-only. The title stays on screen as a plain label (a
`<span>`, not a heading), because a compact bar without its label is unreadable the moment several of
them sit side by side; it also names the meter through `aria-labelledby`. Without a title the meter
falls back to `AriaLabel`, then to the localized default.

## Reset and daily history

`ResetsAt` renders a localized "Resets on {date}" line in the culture's short date format. The date is
formatted with the value's own offset — it is never shifted into another time zone, so the day the host
names is the day the reader sees.

`DailyUsage` renders a bar sparkline under the meter. The graphic names its own numbers through
`aria-label` ("Daily usage: minimum 180, maximum 880, last value 520"), so it is a text alternative
rather than a decorative squiggle.

## Accessibility

| Concern | Behaviour |
| --- | --- |
| Semantics | `role="meter"` on the track with `aria-valuemin`/`max`/`now` and the culture-formatted `aria-valuetext`; the label is the visible title (`aria-labelledby`, in both presentations) or a single `aria-label`. |
| Headings | `Title` is an `<h2>`–`<h6>` chosen by `HeadingLevel` (default `h3`), so the page outline stays intact; the compact presentation renders the same text as a plain label. |
| Colour | Every state ships an icon **and** a word; category colours always sit next to their label in the table. |
| Live region | Exactly **one** polite, atomic region, and it announces a **threshold transition only** — into warning, into critical and back within the limit. A freshly rendered meter says nothing, and a re-render inside the same state says nothing, so a periodic refresh cannot talk over the reader. The sentence stays sticky between transitions (clearing it could wipe an announcement before it is read); when two transitions would produce the SAME sentence, one invisible marker character is toggled so the region still sees a change. |
| Focus | The meter has no tab stop and no focus management of its own; `NearLimitContent` is host markup, so its focus behaviour is the host's. |
| Forced colours | The fill, the segments and the swatches opt out of the colour substitution (`forced-color-adjust: none`) and the track keeps a `CanvasText` edge, so the filled share stays visible instead of collapsing into an empty box. |
| Reduced motion | The one animated subject — the fill's width transition — is enumerated in the `animations.css` reduced-motion block, so `prefers-reduced-motion: reduce` clamps it to 0.01 ms. |
| Coarse pointers | The state badge, the reset line and the over-limit line step up to the small font size, and a `button`, an `a` or a `[role='button']` control in the `NearLimitContent` slot gets `min-block-size: var(--tm-touch-target)` (44 px) on `(pointer: coarse)`. The slot is host markup, so the meter — not the host's button size — owns the finger target. |

## Edge cases

| Input | Behaviour |
| --- | --- |
| `Limit` = 0 (or negative) | The limit is UNKNOWN: `aria-valuemax="0"`, `aria-valuenow="0"`, "0 of 0, 0%" and the normal state — no division by zero, no claim of an infinite limit, and no threshold can turn it into a warning. |
| `Used` negative | Counted as zero (`aria-valuenow="0"`); the limit still renders. |
| `Limit` default | `100` — a percentage meter. |
| `WarningThreshold` / `CriticalThreshold` negative | Counted as `0`, so the meter warns (or turns critical) from the first measurable amount. |
| `WarningThreshold` / `CriticalThreshold` = `NaN` | Falls back to the documented default (`0.8` / `1.0`) instead of silently disabling the state. |
| `HeadingLevel` outside 2–6 | Clamped to the 2–6 band (an `h1` request renders an `h2`), so the meter never emits an invalid heading. |
| `ValueFormat` malformed | Falls back to `N0` instead of throwing out of a render. |
| Category with an empty `Key` | Skipped; its usage is reported by the "Other" row. |
| Category with a negative `Used` | Counts as zero for the segment and the share (the row's cell reads `0`), but the row stays visible. |
| `Categories` empty | No table, no segments — a plain quota meter. |

## Surface

`Used`, `Limit`, `Unit`, `ValueFormat`, `ValueFormatter`, `Culture`, `WarningThreshold`,
`CriticalThreshold`, `ResetsAt`, `DailyUsage`, `Categories`, `Compact`, `Title`, `HeadingLevel`,
`AriaLabel`, `NearLimitContent`, `Class` (+ inherited `DataTestId`, `TestIdPrefix`).

### CSS classes

`.tm-usage-meter`, `--compact`, `--warning`, `--critical`, `__title`, `__track`, `__fill`
(`.tm-usage-meter__fill` is the painted share of the limit), `__segment`
(`--tone-1..6`, `--remainder`), `__readout`, `__amount`, `__percent`, `__state`, `__state-text`,
`__over`, `__over-icon`, `__reset`, `__reset-icon`, `__daily`, `__sparkline`, `__cta`,
`__table` (`.tm-usage-meter__table`, the accessible category breakdown), `__row` (`--other`),
`__label`, `__label-inner`, `__swatch` (`--tone-1..6`, `--remainder`), `__value`, `__share`.

Every colour, size and radius is a `--tm-*` token (`--tm-color-primary` / `--tm-color-warning` /
`--tm-color-danger` for the fill, `--tm-category-1..6` for the fallback tones, `--tm-bg-muted` for the
track), so the dark theme re-points the tokens and the component stylesheet has no `[data-theme]`
block.

## Localisation

`TmUsageMeter_AriaLabel`, `TmUsageMeter_ValueOf`, `TmUsageMeter_AriaValueText`,
`TmUsageMeter_AriaValueTextNoUnit`, `TmUsageMeter_StateWarning`, `TmUsageMeter_StateCritical`,
`TmUsageMeter_OverLimit`, `TmUsageMeter_ResetsAt`, `TmUsageMeter_DailyUsageSummary`,
`TmUsageMeter_CategoryCaption`, `TmUsageMeter_CategoryColumn`, `TmUsageMeter_UsedColumn`,
`TmUsageMeter_ShareColumn`, `TmUsageMeter_OtherCategory`, `TmUsageMeter_AnnounceNormal`,
`TmUsageMeter_AnnounceWarning`, `TmUsageMeter_AnnounceCritical` — in English, Czech, Slovak and
French.

## Wireframe

The component is a wireframe citizen with the `usage-meter` UI role and a declarative stencil
(`TmUsageMeter` schema: `title`, `used`, `limit`, `unit`, `showReset`, `showDaily`, `showCategories`,
`compact`). The sketch derives the warning and critical state from `used`/`limit`, so the palette
draws the same states the component paints.
