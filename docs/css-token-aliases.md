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

## Tokens added for the redesign

Declared in `tokens.css` and re-declared in `tokens-dark.css`. A token that exists only on
`:root` substitutes its `var()` where it is declared, so a dark theme switched on a parent
element would inherit the light value — which is why every one of these is named again in the
dark file.

| Token | Light | Purpose |
|---|---|---|
| `--tm-bg-workspace` | alias of `--tm-bg-page` | Canvas behind cards. The indigo theme paints it off-white. |
| `--tm-radius-card` | `--tm-radius-xl` (12px) | Cards. Opt-in; existing rules keep `--tm-radius-md`. |
| `--tm-radius-control` | `--tm-radius-lg` (8px) | Buttons, inputs, chips. Opt-in. |
| `--tm-shadow-card` | `--tm-shadow-md` | Resting card elevation. |
| `--tm-shadow-popover` | `--tm-shadow-lg` | Popovers, menus, sheets. |
| `--tm-touch-target` | `2.75rem` (44px) | Minimum target size (WCAG 2.5.5). |
| `--tm-focus-ring` | `--tm-shadow-focus` | The non-text focus indicator. |
| `--tm-border-color-strong` | `--tm-color-gray-400` | A separator that stays visible on a muted panel. |
| `--tm-status-{open,inprogress,done,closed}-{bg,fg}` | see `tokens.css` | Task status chips. Text on the fill holds 4.5:1. |
| `--tm-priority-{low,medium,high,critical}` | see `tokens.css` | Priority glyphs. Held to 3:1 against the page. |

## Indigo theme

`theme-indigo.css` is opt-in (decision G4). The blue scale stays the default. A host activates
it with `data-tm-theme="indigo"` on `<html>` or any ancestor; dark mode is the same attribute
plus `data-theme="dark"`. The demo exposes the switch and persists the choice under
`tm-demo-color-theme` in `localStorage`.
