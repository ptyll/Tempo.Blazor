# TmButton — the shared disabled idiom

`TmButton` (`Tempo.Blazor.Components.Buttons`) is the general-purpose button of the core library.
This page pins the **disabled-state contract** the core button shares with the AI package
(decision `AI-PACKAGE-DISABLED-IDIOM`, extended to the core by plan b75365fb fáze 4e); the full
parameter surface lives in [COMPONENTS.md](../COMPONENTS.md) and the JSON documentation overlay.

## The idiom

A disabled `.tm-btn` speaks exactly one vocabulary:

```css
.tm-btn:disabled,
.tm-btn[disabled],
a.tm-btn[aria-disabled="true"] {
  opacity: var(--tm-opacity-disabled);   /* 0.5 — the token, never a literal */
  cursor: not-allowed;
}
```

- **The dim is a token.** `--tm-opacity-disabled` resolves to 0.5 in both themes; a literal alpha in a
  disabled rule is the drift the fáze 4e ratchet (`ButtonDisabledIdiomTests`) fails on. The one
  documented alternative is `opacity: 1` paired with legible ink tokens (the dashboard dialogs use it
  for their disabled primary action, where the veil would leave the label at ≈ 1.4:1) — an *undim*,
  never a second dim value.
- **The cursor is part of the idiom.** `cursor: default` on a disabled rule is banned by the same
  ratchet.
- **Hover never lights a disabled button up.** Every variant hover/active rule is guarded with
  `:hover:not(:disabled)`, so restoring pointer events (below) cannot repaint the fill. Note the
  guard reads `:not(:disabled)` only — it says nothing about `aria-disabled` (see below).
- **Forced colors.** Under `@media (forced-colors: active)` the shared dim would only lower the
  contrast the mode itself guarantees, so the system `GrayText` (color and border) carries the state
  at full `opacity: 1` instead. The block sits at the end of `_button.css` so it wins the (0,2,0)
  tie against the variant ink by source order.

## Pointer events: the button receives them, the anchor does not

Before fáze 4e the disabled rule carried `pointer-events: none`. That single declaration:

- swallowed the rule's own `cursor: not-allowed` — the element never saw a hover, so the cursor
  never showed;
- blocked host `title` tooltips, `mouseenter` handlers and `document.elementFromPoint` on the button;
- let a click **fall through** the button onto whatever element sat under it.

A native `<button disabled>` already refuses activation (click, Enter, Space) and every hover guard
keeps the fill at rest, so the property is gone from the button form: a disabled `TmButton` now
receives pointer events like any other element — the not-allowed cursor actually paints, a host's
`title` attribute shows, `mouseenter` handlers fire, `elementFromPoint` returns the button, and a
`TmTooltip` wrapped around it keeps working (its hover always lived on the wrapper).

The **anchor form keeps the property**:

```css
a.tm-btn[disabled],
a.tm-btn[aria-disabled="true"] {
  pointer-events: none;
}
```

`<a class="tm-btn" disabled>` has no native disabled — nothing but `pointer-events: none` stops a
*mouse* from navigating. **But the property blocks the pointer only**: the keyboard still activates
an `<a href>`. A disabled link must therefore ALSO lose its `href` (or render `tabindex="-1"` with
`aria-disabled="true"`); `pointer-events: none` alone never makes an anchor inert. `TmButton` itself
always renders a `<button>`; the anchor form exists for host markup that styles a link as a button.

## `aria-disabled` on a `.tm-btn`: component-owned on a button, base rule on an anchor

`aria-disabled="true"` on a **`<button>`** is a state the *component* owns: it keeps the button
focusable (WCAG 2.4.3), refuses re-entry in its own handler and paints its own disabled look. The
base rule deliberately does **not** name it — the library's own aria-disabled buttons
(`TmActivityComments` submit while a save runs, the `TmEmailTemplateEditor` Save, the
`TmPromptLibrary` Use) each carry their own `[aria-disabled="true"]` rule, and a generic base dim
would also meet a live hover fill, because `aria-disabled` is not `:disabled` and every variant hover
guard is `:hover:not(:disabled)`. A host that puts `aria-disabled` on its own `.tm-btn` button owns
the look (and the activation guard) the same way.

The **anchor** form `a.tm-btn[aria-disabled="true"]` has no component owner and no native disabled,
so the base rule dims it through the shared token and the anchor rule above keeps it pointer-inert —
subject to the keyboard caveat in the previous section.

`TmButton` never emits `aria-disabled` on its own — it renders the native `disabled` attribute from
`Disabled`/`IsLoading`.

## Tooltip over a disabled button

A `TmTooltip` (or a native `title`) on a disabled button is **hover-only**: a native disabled button
is not focusable, so a keyboard or screen-reader user never opens the tooltip. The reason an action
is unavailable must therefore ALSO exist outside the tooltip — a visible hint near the button, a
description referenced by `aria-describedby`, or text in the surrounding flow. The `/buttons` demo
card shows the pattern: a disabled button with a tooltip *and* a visible reason line under it
(`button-disabled-reason`).

## Host migration (fáze 4e behaviour change)

A host that relied on a disabled `<button class="tm-btn">` being **transparent to the pointer** —
e.g. an `elementFromPoint` hit-test that expected the element *under* the button, or a click that was
meant to pass through — must restore the old behaviour with its own rule:

```css
/* before fáze 4e this was the library default; after, opt back in per surface */
.my-surface .tm-btn:disabled {
  pointer-events: none;
}
```

The anchor variant is unchanged, so link-buttons need no migration.

## Verification

- bUnit/CSS gate: `tests/Tempo.Blazor.Tests/Theme/ButtonDisabledIdiomTests.cs` — the resolved idiom
  over the shipped bundle (token, cursor, no `pointer-events` on the button), the anchor rule, the
  component-owned aria-disabled contract (no base-rule dim on a `<button>`, the token on the anchor),
  the forced-colors block, the ratchet over every core stylesheet (no literal dim, no
  `cursor: default` in a `.tm-btn` disabled rule) and the pointer-events ratchet (`pointer-events:
  none` on a disabled `.tm-btn` selector only ever on the `a.tm-btn` anchor form).
- E2E (WASM `/buttons`, card `buttons-disabled-idiom-card`):
  `tests/Tempo.Blazor.E2E/TmButtonDisabledIdiomE2ETests.cs` — computed opacity/cursor/pointer-events,
  the decisive `elementFromPoint` hit-test (including the `title`-carrying button), a forced click
  that never fires `OnClick`, a hover that keeps the resting fill, the tooltip regression spec, the
  forced-colors GrayText/full-opacity probe, and light/dark/tooltip/forced-colors screenshots.
- Demo: `/buttons` → *Button States* → **Disabled idiom (shared)**.
