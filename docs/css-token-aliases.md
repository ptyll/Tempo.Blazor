# CSS token aliases

Canonical names for the redesign tokens, and the aliases that analyses kept inventing for tokens
that already exist. Source: `output/imagegen/tempo-reimagined/analysis/00-cross-cutting.md` §7.3.

**Do not add an ad-hoc alias to the core.** A name in the left column is not a token; use the
right column. `scripts/audit-css-strict.mjs` reports a `var(--tm-*)` whose token is defined
nowhere, fallback or not, so a new alias fails CI instead of rendering its fallback forever.

| Alias used in analyses | Canonical token |
|---|---|
| `--tm-primary` | `--tm-color-primary` |
| `--tm-border` | `--tm-border-color` |
| `--tm-border-subtle` | `--tm-color-border-subtle` |
| `--tm-bg-subtle` | `--tm-bg-surface-secondary` |
| `--tm-bg-hover` | `--tm-color-surface-hover` |
| `--tm-surface-2` | `--tm-bg-surface-secondary` |
| `--tm-text-on-primary` | `--tm-color-on-primary` |
| `--tm-color-white` (as text on a primary fill) | `--tm-color-on-primary` |
| `--tm-color-primary-focus` | `--tm-focus-ring` |
| `--tm-radius` | `--tm-radius-md` |
| `--tm-text` | `--tm-text-primary` |
| `--tm-text-2` | `--tm-text-secondary` |
| `--tm-accent` | `--tm-color-primary` |
| `--tm-danger` | `--tm-color-danger` |
| `--tm-hover` | `--tm-color-surface-hover` |
| `--tm-bg-2` | `--tm-bg-surface-secondary` |
| `--tm-color-primary-100`, `--tm-color-primary-soft`, `--tm-accent-subtle` | `--tm-color-primary-subtle` |
| `--tm-color-primary-700`, `--tm-accent-hover` | `--tm-color-primary-hover` |
| `--tm-on-primary` | `--tm-color-on-primary` |
| `--tm-text-color` | `--tm-text-primary` |
| `--tm-bg-1`, `--tm-surface` | `--tm-bg-surface` |
| `--tm-surface-elevated` | `--tm-color-surface-elevated` |
| `--tm-hover-bg` | `--tm-color-surface-hover` |
| `--tm-green` | `--tm-color-success` |
| `--tm-red` | `--tm-color-danger` |
| `--tm-yellow`, `--tm-warning` | `--tm-color-warning` |
| `--tm-border-strong` | `--tm-border-color-strong` |
| `--tm-color-border` | `--tm-border-color` |

## Tokens added for the redesign

Declared in `tokens.css` and re-declared in `tokens-dark.css`. A token that exists only on
`:root` substitutes its `var()` where it is declared, so a dark theme switched on a parent
element would inherit the light value — which is why every one of these is named again in the
dark file.

| Token | Light | Purpose |
|---|---|---|
| `--tm-bg-workspace` | alias of `--tm-bg-page` | Canvas behind cards. Only the indigo theme overrides it; `--tm-bg-page` never moves. |
| `--tm-radius-card` | `--tm-radius-lg` | Cards. Equal to today's card radius, so consuming it changes nothing. |
| `--tm-radius-control` | `--tm-radius-md` | Buttons, inputs, chips. Equal to today's control radius. |
| `--tm-shadow-card` | `--tm-shadow-md` | Resting card elevation. |
| `--tm-shadow-popover` | `--tm-shadow-lg` | Popovers, menus, sheets. |
| `--tm-touch-target` | `2.75rem` (44px) | Minimum target size (WCAG 2.5.5). |
| `--tm-focus-ring` | `0 0 0 2px var(--tm-bg-surface), 0 0 0 4px var(--tm-color-primary)` | A whole `box-shadow` value: a surface gap, then a primary ring. |
| `--tm-border-color-strong` | `--tm-color-gray-500` (dark: `--tm-color-gray-400`) | A separator that holds 3:1. gray-400 is 2.54:1 in light. |
| `--tm-status-{open,inprogress,done,closed}-{bg,fg}` | see `tokens.css` | Task status chips. The suffix is the lower-cased `TmWorkItemStatus`. Text on the fill holds 4.5:1. |
| `--tm-priority-{lowest,low,medium,high,highest}` | see `tokens.css` | Priority glyphs. The suffix is the lower-cased `TmWorkItemPriority`. Held to 3:1 against the page. |
| `--tm-scheduler-event-fg` | `--tm-color-white` | Text on an event whose fill is a consumer-supplied colour. |

## Indigo theme

`theme-indigo.css` is opt-in (decision G4). The blue scale stays the default. Set
`data-tm-theme="indigo"` on `<html>`, the same element that carries the `:root` aliases, so
every alias derived from the primary and gray scales recomputes. **Subtree theming is not
supported**: portaled overlays and sheets render outside the subtree and would keep the default
scale. Dark mode is the same element plus `data-theme="dark"` (or `.tm-dark`), and a dark region
nested inside the indigo root is covered by the descendant selectors. The theme overrides only
`--tm-bg-workspace`; the host paints that token itself.

The demo exposes the switch and persists the choice under `tm-demo-color-theme` in
`localStorage`.

## Translucent fills

Write a translucent fill as `color-mix(in srgb, var(--tm-color-primary) 15%, transparent)`, not as
`rgba(var(--tm-color-primary-500-rgb), 0.15)`. The channel tokens exist for the cases a
`color-mix()` cannot express.

## What the strict audit enforces

`scripts/audit-css-strict.mjs` reports four kinds, and a component dark block is an error, not a
warning:

- `undefined-token` — a `var(--tm-*)` whose token is defined nowhere, fallback or not. Tokens set
  from script at runtime count as defined.
- `color-literal` — a hex, `rgb()`, `rgba()`, `hsl()`, `hsla()`, or the named colours `white` and
  `black` standing alone, outside the token and theme files. A custom-property definition matching
  `--tm-<name>-(palette|option|role|annotation|category)-<name>` is data and is exempt. Widening the
  detector to named colours surfaced debt the previous baseline could not see; that debt was frozen
  once, and the count may only shrink from there.
- `white-on-primary` — a rule whose body paints a primary fill and sets its colour to `#fff`,
  `#ffffff`, `white`, or `var(--tm-color-white)`. Judged per rule, so the two declarations may be
  on different lines.
- `component-dark` — a `[data-theme="dark"]` or `.tm-dark` block inside a component stylesheet.

A deprecated alias may live only in a package `*-variables.css`, and only with the phase that
removes it named next to it.
