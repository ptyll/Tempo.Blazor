# TmTimeSelector — spinbutton time of day

`TmTimeSelector` is the inline APG spinbutton readout: hours / minutes / optional seconds with
native +/−, optional AM/PM, an hour/minute grid, host-owned presets and a JS module for keys,
typeahead, auto-repeat, fit and **opt-in swipe**. Demo: `/time-input`. Decision:
[ADR-0007](adr/0007-time-selector.md).

Everything here is additive. Default `EnableSwipe=false` keeps today's DOM, CSS and behaviour.

## Public surface (Fáze 8)

| Member | Type | Default | What it does |
|---|---|---|---|
| `EnableSwipe` | `bool` | `false` | Vertical swipe on a spinbutton. When true the root carries `data-tm-time-swipe="true"` and `tm-time-selector.js` attaches the shared `pointer-drag.js` helper |
| `AllowTyping` | `bool` | `true` | Typeahead in the spinbuttons and the **Type the time** switch on a coarse pointer. The switch is never offered in a square fit (decision 15) |

`TmTimePicker.EnableSwipe` and `TmTimeRangePicker.EnableSwipe` are pass-throughs (Popup/Auto only;
Segments ignores them). Default `false` keeps today's overlay.

## Swipe contract

- Opt-in. Without `EnableSwipe` there is no `data-tm-time-swipe`, no `touch-action: none`, no
  pointer-drag session.
- Gesture exclusively via shared `pointer-drag.js` (`attach(element, { threshold, onStart, onMove,
  onEnd, onCancel, onRelease })`). Never a second `pointermove` / capture implementation.
- **24 px = 1 step**, swipe **up = +**, threshold **6 px**.
- `OnSwipeStartAsync` snapshots the origin; `OnSwipeStepAsync` ticks with `announce=false`;
  `OnSwipeEndAsync` announces (`TmTimeSelector_Set`) and fires `OnCommitted` once; ticks never
  fire it. `OnSwipeCancelAsync` (pointercancel / Escape) restores the origin through
  `ValueChanged` (an empty origin restores `null`) and never fires `OnCommitted`.
- `touch-action: none` is CSS-owned and applies **only** to `.tm-time-selector__spin` while the
  root has `data-tm-time-swipe="true"`. +/− stay the WCAG 2.5.7 alternative and never collide with
  the sheet grabber.
- `Disabled` / `ReadOnly` are no-ops.

## Keyboard “Type the time”

Already implemented in Fáze 5. EnableSwipe does not change it:

- Offered on **Mobile/Tablet regular** when `AllowTyping` (default true).
- **Never** in a square fit. Square + `keyboard=open` is a one-line readout (no grid, no presets,
  no +/−).
- The field is `<input inputmode="numeric">`.

## Render gate

Auto-repeat and swipe ticks set a fingerprint gate (`ShouldRender`): a clamped step whose readout,
live text and invalid state are unchanged skips the Blazor render. The live region still announces
on release (`announce=false` during ticks).

## Usage

```razor
<TmTimeSelector @bind-Value="_time"
                HourCycle="TimeHourCycle.H23"
                EnableSwipe="true" />

<TmTimePicker @bind-Value="_meeting"
              Label="Meeting"
              Presentation="TimePickerPresentation.Popup"
              EnableSwipe="true" />
```

## Accessibility and motion

- +/− remain native `<button>`s (`tabindex="-1"`, `aria-controls`). Swipe is extra, not a
  replacement.
- Reduced motion does not affect auto-repeat or swipe (they are not animations).
- No new localization keys: swipe reuses `TmTimeSelector_Set` / `TmTimeSelector_HourValue_*`.
- No new design tokens: `touch-action: none` is the only CSS addition.

## Wireframe

`TmTimeSelector` is a wireframe citizen: schema in `BuiltInComponentSchemas`, role `time-picker`
(alongside `TmTimePicker` / `TmTimeInput`) and a pack entry in `tempo.stencil.json`.
