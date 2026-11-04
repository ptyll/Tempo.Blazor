# TmDateRangePicker — presets, comparison windows and the clock

`TmDateRangePicker` reports an inclusive `(DateOnly? Start, DateOnly? End)` range. This page documents the
three additive capabilities of 2.10.0: presets computed from an injected `TimeProvider`, localized preset
labels, and the **comparison window** — the second period a report compares the reported range against.

Everything here is additive: `TmDateRangePicker` without `Comparison` renders the pre-2.10 markup and raises
nothing.

## Public surface

| Member | Type | Default | What it does |
|---|---|---|---|
| `Comparison` | `DateRangeComparison` | `None` | How the compared window is derived. `None` renders no comparison surface at all |
| `ComparisonRange` | `(DateOnly? Start, DateOnly? End)` | `(null, null)` | The derived comparison window, two-way bound. The picker raises the derived value, so a host never re-implements the arithmetic; `Custom` uses it as the seed |
| `ComparisonRangeChanged` | `EventCallback<(DateOnly? Start, DateOnly? End)>` | — | Fires when the compared window changes. `None` never raises |
| `Presets` | `IReadOnlyList<DateRangePreset>?` | `null` | A host list wins over the built-in row |
| `ShowPresets` | `bool` | `true` | Whether the preset row renders at all |

`DateRangeComparison` (`Tempo.Blazor.Models`):

| Value | Meaning |
|---|---|
| `None` | No comparison — the default and the pre-2.10 behaviour |
| `PreviousPeriod` | The equally long window ending the day **before** the reported range starts: 10–14 October is compared against 5–9 October |
| `PreviousYear` | The same window one year earlier. A leap day is clamped by the calendar, so 29 February 2024 is compared against 28 February 2023 |
| `Custom` | A second window the reader (or the host) picks explicitly |

## Presets come from a clock, not from `DateTime.Today`

`DateRangePresets.Create(TimeProvider, ITmLocalizer)` builds the five-preset row (`Today`, `Last week`,
`Last month`, `Last quarter`, `This year`) from `TimeProvider.GetLocalNow()` and labels every entry through
the localizer. `TmDateRangePicker` calls it with the `TimeProvider` from the service provider — an
unregistered clock falls back to `TimeProvider.System`, so a consumer that never registers one keeps working.

A host that pins the clock (a `FakeTimeProvider` in tests, a tenant clock in production) therefore gets
presets on **its** day rather than the server's. A label the localizer does not translate falls back to the
English wording, so a partial translation table can never render a resource key into the preset row.

The static members of 2.9.x (`DateRangePresets.Today`, `LastWeek`, …) stay exactly as they were — machine
date, hardcoded English label — and are deliberately **not** `[Obsolete]`, because existing consumers keep
compiling and behaving identically. New code should use `Create`.

## The compared window is public arithmetic

`DateRangeComparisonCalculator` exposes the two derived modes so a backend can compute **exactly** the window
the picker reports instead of re-deriving it — a report header and the query behind it must agree to the day.

```csharp
var (start, end) = DateRangeComparisonCalculator.PreviousPeriod(rangeStart, rangeEnd);
var (yearAgoStart, yearAgoEnd) = DateRangeComparisonCalculator.PreviousYear(rangeStart, rangeEnd);
```

Both accept a reversed range (a UI artefact of picking the end first) and normalize it, and neither ever
returns an empty window.

## What the reader sees

- **The trigger** keeps the reported range and appends the comparison summary
  (`.tm-date-range-trigger__comparison`). The summary is part of the trigger's accessible name — text inside a
  button has to be, per WCAG 2.5.3 — so a screen reader reads the period and the window it is compared with.
- **The popup** adds a `.tm-date-range-comparison` block: a decorative swatch (the same neutral wash the
  calendar uses), the localized label and the compared window in words ("Previous period: 5 Sep – 4 Oct").
- **The calendar** marks the compared window with `.tm-cal-day--comparison`, `--comparison-start` and
  `--comparison-end`. The fill is the neutral `--tm-cal-comparison-bg` and the ends carry a **dashed**
  `--tm-cal-comparison-border`, so the compared window is never mistaken for a second reported range, and the
  distinction survives a reader who cannot see colour. Under forced colors the ends keep a `CanvasText` dashed
  edge. Every marked day is `aria-describedby`-ed by a visually hidden description, so the marking is not
  visual-only.
- **`Custom`** is the one mode where the calendars have two possible subjects, so the block also renders a
  target switcher (`.tm-date-range-comparison__targets`, a `role="group"`, one `aria-pressed` button per
  window) that decides which window a click edits.

Hosts and tests can key off the root's stable attributes instead of parsing text: `data-comparison`
(`previousPeriod` / `previousYear` / `custom`, absent for `None`), `data-comparison-start` and
`data-comparison-end` (invariant `yyyy-MM-dd`).

## Usage

```razor
@* The report header: the period, and the window it is compared against. *@
<TmDateRangePicker @bind-Value="_range"
                   Comparison="DateRangeComparison.PreviousPeriod"
                   ComparisonRange="_comparison" />

@* A pasted-in fixed window, common in tests and in a saved report. *@
<TmDateRangePicker @bind-Value="_range"
                   Comparison="DateRangeComparison.Custom"
                   @bind-ComparisonRange="_customComparison" />
```

A host that wants both bindings can use `@bind-ComparisonRange`; the picker still reports every derived
change through `ComparisonRangeChanged`, so an unbound `ComparisonRange` is never stale — the page that reads
`ComparisonRange` alone sees the value the picker computed on the last change.

## Accessibility and motion

- The comparison block is text plus a decorative swatch; nothing is announced twice, and the visually hidden
  day descriptions are the only screen-reader-facing addition.
- The target switcher's buttons are native `<button>`s with `aria-pressed`; keyboard activation is the
  browser's own (see [keyboard-activation-convention.md](keyboard-activation-convention.md)).
- `.tm-date-range-comparison__target` is enumerated in the `animations.css` reduced-motion block, so the
  transition is clamped under `prefers-reduced-motion: reduce`.
- No new design token is required: the compared window reads `--tm-cal-comparison-bg` /
  `--tm-cal-comparison-border`, which both themes define.
