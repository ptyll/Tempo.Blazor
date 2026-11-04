# Segmented control

`TmSegmentedControl<TValue>` (`Tempo.Blazor.Components.Inputs`) is a value out of a small set, drawn as
one segmented tray with a sliding highlight. It is the "always-visible choice" control: use it where a
`TmSelect` would bury a decision behind a click and a full radio group would be too tall for the space
(a period, a view density, an alignment, a preview mode).

> Until 2.10.0 this page argued that no `TmSegmentedControl` should exist and that `TmTabs` +
> `TmRadioGroup Appearance="Segmented"` covered both jobs. The tray needs two things neither of those can
> give it — a highlight measured in the browser and a horizontal overflow scroller — so the component now
> exists and `TmBreakdownCard`'s period switcher uses it. The "which control when" table below survives,
> because choosing the control is still the important decision.

## Which control when

| The segments change… | Use | Why |
| --- | --- | --- |
| **a value** out of a small set, in a compact place | `TmSegmentedControl` (this page) | The selection is one value, so it stays a `radiogroup` — and the tray is compact, scrolls when it must and keeps one tab stop. |
| **a value**, in a form where every option must be spelled out | `TmRadioGroup` | A radio group lists its options vertically with room for help text and a longer label; the segmented tray is the compact variant, not the form control. |
| **which content is shown** (a view, a panel, a page region) | `TmSegmentedControl Role="SegmentedControlRole.Tabs"`, or `TmTabs` when the tabs need a full strip of their own | Either way the selection owns panels, so it needs tab semantics (`tablist` / `tab` / `aria-controls`). `TmTabs` remains the full tab strip; the segmented tray is the same semantics in a compact control. |

## Two ARIA patterns, one design

`Role` decides what the tray MEANS; the visual design does not change.

- **`SegmentedControlRole.RadioGroup`** (default) — `role="radiogroup"` on the track and one
  `role="radio"` with `aria-checked` per segment. The segments pick a value, so the control behaves like a
  radio group: **selection follows focus**.
- **`SegmentedControlRole.Tabs`** — `role="tablist"` on the track and one `role="tab"` with
  `aria-selected` per segment, plus `aria-controls` pointing at the panel. `PanelId` names one shared
  panel and `SegmentedOption.PanelId` overrides it per segment. With neither, the tab carries **no**
  `aria-controls` at all — an idref to an element that is not in the document is worse than no idref. The
  host owns the panels (`role="tabpanel"`, its own id and label).

## Keyboard and focus

Both patterns share one keyboard model:

| Key | Effect |
| --- | --- |
| `Tab` | Enters the tray on its **single tab stop** — the selected segment, or the first enabled one when nothing is selected. `Tab` again leaves the tray; every other segment has `tabindex="-1"`. |
| `→` / `↓` | Selects and focuses the next **enabled** segment, wrapping from the last to the first. |
| `←` / `↑` | Selects and focuses the previous enabled segment, wrapping from the first to the last. |
| `Home` / `End` | Selects and focuses the first / last **enabled** segment (a disabled first segment is skipped, not selected). |
| `Enter` / `Space` | Activates the focused segment through its **native** `<button>` click — the component never emulates these keys, so one key press activates exactly once. |

Disabled segments keep their place (native `disabled` + `aria-disabled`) and are skipped by every key, so
removing one never renumbers its neighbours. `Disabled` on the control disables all of them and leaves the
tray with no tab stop.

The keyboard path focuses the target segment, which makes the browser scroll it into view — that is the
required behaviour in a tray that overflows, and the reason the tray sets `scroll-padding-inline` and
`scroll-snap-type`.

## The sliding highlight

The highlight is a decorative `aria-hidden` strip inside the tray, positioned by **measuring** the selected
segment (`TmSegmentedControl.razor.js`: `place`, `focusOption`, `release`). It cannot be expressed in CSS:
the tray is a flex row of content-sized segments inside a horizontal scroller, so there is no static
relationship between a segment's box and the tray's edge that a selector could address. The strip is moved
with `transform` (compositable, and `prefers-reduced-motion` drops the travel to a jump) and re-measured on
every render plus on a `ResizeObserver` over the tray **and** every segment — a font swap or a container
resize changes the selected segment's box without changing the tray's width.

**Without JavaScript the control is not degraded.** The strip is only claimed once a measurement succeeded
(`data-tm-indicator="sliding"` on the track hides the segment's own surface); with no measurement the
selected segment paints its own background, border and weight instead. A prerendered page therefore looks
intentional, and a host without the static asset still gets a complete control.

## Focus visibility

The focus indicator is an **inset outline** (`outline: 2px solid var(--tm-color-primary)` with
`outline-offset: -2px`), not the two-layer box-shadow ring the rest of the library uses. The tray clips
anything painted outside a segment (that is what `overflow-x: auto` means), so a box-shadow ring would be
cut off by the very overflow that makes the control work. Under `forced-colors` the outline is painted in
the system `Highlight` colour; the measured strip is dropped there and the selected segment is re-declared
with the `Highlight`/`HighlightText` pair, because a background painted from tokens disappears.

## Sizes, counts, badges and width

```razor
@code {
    private readonly IReadOnlyList<SegmentedOption<string>> periods =
    [
        new() { Label = "Day", Value = "day" },
        new() { Label = "Week", Value = "week" },
        new() { Label = "Month", Value = "month", Count = 12 },
        new() { Label = "Year", Value = "year", IsDisabled = true }
    ];

    private string? period = "week";
}

<TmSegmentedControl TValue="string" Label="Period" Options="periods" @bind-Value="period" />
```

- `Size` — `Sm` (32 px), `Md` (38 px, the default) and `Lg` (44 px); `Sm`/`Md` line the tray up with the
  input heights beside it. On `(pointer: coarse)` every size grows to the 44 px `--tm-touch-target`.
- `FullWidth` — the tray fills its container and shares the width equally between the segments. Without it
  the tray hugs its content and scrolls when it runs out of room.
- `SegmentedOption.Count` and `SegmentedOption.Badge` — plain TEXT inside the segment, so they are part of
  its accessible name ("Inbox 12"); the count uses tabular figures and the badge is a small pill. They are
  never the only carrier of information and never a colour-only cue.
- `SegmentedOption.Icon` — a decorative `aria-hidden` icon before the label.

## Accessible name

Name the group, always. In priority order:

1. `AriaLabelledBy` — the id of an element outside the control (a page heading, a legend).
2. `Label` — a **visible** label above the tray; the group is named through `aria-labelledby`, so the name
   a screen reader reads is the name a reader sees (WCAG 2.5.3). Do not add an `AriaLabel` on top of it.
3. `AriaLabel` — a programmatic name when no visible label exists.
4. The localized default (`TmSegmentedControl_DefaultLabel`, "Options").

A whitespace-only `Label` counts as no label rather than becoming an empty accessible name.

The control renders **no live region**. The option's own `aria-checked` / `aria-selected` state is what a
screen reader announces on every change; a second announcement would be noise, and a host that needs a
confirmation announces the *consequence* of the change instead (a result count, the period that loaded).

## Tab mode and panels

```razor
<TmSegmentedControl TValue="string" Role="SegmentedControlRole.Tabs"
                    Label="Preview mode" PanelId="preview-panel"
                    Options="modes" @bind-Value="mode" />

<div id="preview-panel" role="tabpanel" aria-label="Preview">…</div>
```

Every tab points at `preview-panel` through `aria-controls`. A per-segment `SegmentedOption.PanelId` wins
over the control-wide `PanelId`, which is what a host with one panel per tab passes. The component does not
mount or hide the panels — the host does, because it is the host that knows whether switching a tab
reloads data.

## Related

- [docs/keyboard-activation-convention.md](keyboard-activation-convention.md) — why Enter/Space are never
  emulated on a native control.
- [docs/combobox-match-highlight.md](combobox-match-highlight.md) — the other half of this phase: the match
  highlight for combobox rows. A segment label is a value, not a search result, so the tray never highlights.
