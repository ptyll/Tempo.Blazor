# Interface density (TG-007)

Tempo components render in the **comfortable** density by default — the look every pre-2.11 host
already has. A host can opt any subtree into the **compact** density with one attribute:

```html
<div data-tm-density="compact"> … </div>
```

Set it on the app root (next to `data-theme`) to compact the whole application, or on any wrapper
to compact one region (a dense data grid, an admin panel). `data-tm-density="comfortable"` is the
explicit form of the default; omitting the attribute is equivalent.

## What changes in compact

The compact scope (in `density.css`, imported right after `tokens.css`) re-declares the density
tokens — no component stylesheet carries a density rule of its own, everything flows through
tokens:

| Token | Comfortable (default) | Compact |
|-------|----------------------|---------|
| `--tm-density-scale` | `1` | `0.85` |
| `--tm-input-height-md` | `2.375rem` (38 px) | `2rem` (32 px) |
| `--tm-data-table-cell-py` | `var(--tm-space-3)` (12 px) | `var(--tm-space-2)` (8 px) |
| `--tm-data-table-cell-px` | `var(--tm-space-4)` (16 px) | `var(--tm-space-3)` (12 px) |
| `--tm-card-header-padding` | `var(--tm-space-4) var(--tm-space-5)` | `var(--tm-space-3) var(--tm-space-4)` |
| `--tm-card-content-padding` | `var(--tm-space-5)` | `var(--tm-space-4)` |
| `--tm-card-footer-padding` | `var(--tm-space-3) var(--tm-space-5)` | `var(--tm-space-2) var(--tm-space-4)` |
| `--tm-sidebar-item-padding` | `var(--tm-space-2) var(--tm-space-3)` | `var(--tm-space-1-5) var(--tm-space-3)` |

Consumers of `--tm-input-height-md` — `TmButton`, `TmTextInput`, `TmSelect`, `TmSearchInput`,
`TmSegmentedControl`, `TmOtpInput` and the other base controls — follow automatically.
`TmDataTable` cells, `TmCard` paddings, `TmTabs` row padding (every variant: Line row 42 → 34 px)
and `TmSidebar` nav items are re-metered the same way.

## What does NOT change

- **Touch layouts.** `--tm-touch-target` (44 px) and every `(pointer: coarse)` rule keep their
  targets; compact is a desktop data-density opt-in.
- **The pre-existing per-component `--compact` modifiers** (`tm-usage-meter--compact`,
  `tm-approval-card--compact`, `tm-calendar-view--compact`, …). They stay local overrides and can
  be combined with the global scope.
- **Comfortable rendering.** With no attribute, every token resolves to the exact pre-TG-007 value
  (guarded by `InterfaceDensityTests`).

## Order is load-bearing

`:root` and `[data-tm-density="compact"]` carry the same specificity, and the compact scope
re-declares tokens that `tokens.css` also declares on `:root`. Two orders make the attribute win:

1. **Across files:** `tempo-blazor.css` imports `density.css` **after** `tokens.css` — import
   order is the cascade order for the equal-specificity `:root` rules.
2. **Inside `density.css`:** the comfortable `:root` defaults precede the compact block.

`InterfaceDensityTests` guards both. The separate file also keeps the flat "last declaration
wins" token readers (the documented-token drift guard, the button-height sweep) resolving the
comfortable `:root` value — the compact re-declaration is scoped to the attribute, which those
readers intentionally do not model.

## Root-level derived reserves

`:root`-level tokens that derive from `--tm-input-height-md` evaluate the token with the
`:root` value by design. The complete list (guarded by an allow-list test in
`InterfaceDensityTests`, so a new derivation fails loudly instead of silently staying
comfortable):

- `--tm-form-action-bar-reserve-block-size` — declared twice on `:root` (the base block and
  the `<768px` media-query block); under compact both keep reserving the comfortable height —
  a slightly larger reserve, never an overlap. A host that wants a compact-exact reserve
  re-declares the reserve on its own wrapper.

Neighbouring reserves are NOT density-derived and need no entry: `--tm-mobile-action-bar-reserve-block-size`
builds from `--tm-touch-target` (44 px, untouched by density — see *What does NOT change*).

## Mapping a user's density preference (PromptHelper)

PromptHelper writes `--ph-user-density-scale` on `:root`, which Tempo components ignore — the
library never reads a `--ph-*` token. Map the preference to the attribute instead:

```js
// once, after loading the user profile
const scale = parseFloat(getComputedStyle(document.documentElement)
    .getPropertyValue('--ph-user-density-scale'));
document.documentElement.dataset.tmDensity = scale > 0 && scale < 1 ? 'compact' : 'comfortable';
```

```razor
@* or directly in Blazor *@
<div data-tm-density="@(_userDensityScale < 1 ? "compact" : "comfortable")">
```

`--tm-density-scale` is the published multiplier (1 / 0.85) for hosts that derive their own
density-aware metrics from one knob; the library stylesheets themselves read the resolved tokens
above, so overriding one concrete token is never fought by a scale.

## Demo

The `/density` demo page (nav *Design System → Interface Density*) renders a form, a data table,
a card, tabs and a sidebar inside the scoped region with a Comfortable/Compact switcher above it.

## Tests

- bUnit `InterfaceDensityTests` — token contract, comfortable-equals-today, component token
  consumption, preserved per-component modifiers, documentation ratchet.
- E2E `DensityTG007E2ETests` — real-browser computed metrics (comfortable vs compact) and
  locator screenshots (light/dark, reduced motion, phone).
