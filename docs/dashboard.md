# TmDashboard & TmWidgetSelector

`TmDashboard` is a provider-backed, customizable widget dashboard: multiple named dashboards
per user, a drag/resize grid, an edit mode, and a widget selector. The 2.9.1 redesign
(dashboard redesign plan, phases F1–F7) rebuilt its chrome, responsiveness, touch support,
keyboard alternative and dialogs around the shared redesign foundation — the component docs
here are the user-facing summary; the history and before/after migration snippets live in
[`CHANGELOG.md`](../CHANGELOG.md) under `2.9.1 (unreleased)`.

## Setup

```csharp
builder.Services.AddTempoBlazor(); // registers IDashboardProvider / IWidgetRegistry defaults

// your implementations:
builder.Services.AddSingleton<IDashboardProvider, MyDashboardProvider>();
builder.Services.AddSingleton<IWidgetRegistry, MyWidgetRegistry>();
```

```html
<!-- dashboard.js is required by TmDashboard (drag, resize, scroll-into-view).
     pointer-drag.js is NOT a second script tag: dashboard.js dynamic-import()s it
     from the same folder. -->
<script src="_content/Tempo.Blazor/js/dashboard.js"></script>
```

## Header and chrome parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Appearance` | `DashboardAppearance` | `Plain` | `Plain` paints the dashboard directly on the workspace background (widget cards are the only surfaces); `Card` restores a wrapping surface card (`.tm-dashboard--framed`) for contrasting pages. |
| `Title` | `string?` | `null` | Heading text overriding the current dashboard's name. Without a `Title` (and with a provider) the view-mode heading is visually hidden — the switcher trigger already shows the name; the `h2` stays in the DOM (`tm-sr-only`). |
| `Subtitle` | `string?` | `null` | Secondary line under the heading in view mode. |
| `HeaderStart` | `RenderFragment?` | `null` | Host content at the start of the header (breadcrumbs, back link). |
| `HeaderActions` | `RenderFragment?` | `null` | Host actions at the end of the view-mode header, rendered before the Edit button (DOM order = tab order). Not rendered in edit mode. |
| `ShowHeader` | `bool` | `true` | Hides the whole toolbar in view mode. Edit mode always renders the toolbar (it carries Save/Cancel). |
| `ShowTitle` | `bool` | `true` | Hides the heading (and subtitle) while keeping the switcher and header actions — a different concern than `ShowHeader` and intentionally not obsoleted. |
| `ShowWidgetIcons` | `bool` | `true` | Renders the 40px category icon tile in every widget header, tinted with the category colour via `color-mix` over the `--tm-category-tint` token. |
| `ShowWidgetDescriptions` | `bool` | `false` | Renders `WidgetDefinition.Description` as a secondary line under the widget title. |
| `WidgetHeaderActions` | `RenderFragment<WidgetInstance>?` | `null` | Host slot in every widget header (badge, "view all" link, period select), rendered before the collapse chevron, view mode only. The host owns its content's accessible names. |

## Widget model hooks

`WidgetDefinition` (registered in `IWidgetRegistry`) gained `IsCollapsible` (default `true`) —
a widget registered with `false` renders no collapse chevron. `IsDraggable`, `IsResizable`,
`MinWidth/MaxWidth/MinHeight/MaxHeight` (`0` = unlimited) are respected by drag, resize and the
keyboard alternative; the component renders the clamps as `data-min-w/data-max-w/data-min-h/data-max-h`
for `dashboard.js`.

## Reordering without drag (WCAG 2.1.1 / 2.5.7)

- **Keyboard grab mode (desktop):** the drag handle is a real `<button aria-pressed>`
  (`.tm-widget-drag-handle`). Enter/Space (native activation) grab and drop; arrow keys move the
  grabbed widget one cell (clamped to the grid); Shift+arrow resizes one step (clamped to the
  definition's min/max); Escape cancels and restores the pre-grab geometry. Every step is
  announced through a polite live region ("Widget: column 3, row 2, 4 × 3"). The whole move and
  resize logic lives in C# — `dashboard.js` supplies only the pointer drag/resize.
- **Compact edit mode (tablet portrait, mobile):** drag and resize handles do not render.
  Each widget card grows **Move up / Move down** buttons (≥ 44 px targets, named for the widget
  through `aria-describedby`) that swap the card's `(X, Y)` with its previous/next neighbour in
  the linear `(Y, X)` order and push overlaps when `GridConfig.AllowOverlap` is false.
- A freshly added widget is highlighted briefly (`tm-widget--highlight`, quiet under
  `prefers-reduced-motion`) and scrolled into view (`tempoDashboard.scrollWidgetIntoView`).

## Touch drag and resize (F5)

Widget move/resize uses the shared `pointer-drag.js` helper (Pointer Events: mouse, touch, pen)
— not HTML5 drag-and-drop. Escape/pointercancel roll the gesture back, the drop preview follows
the pointer, and `touch-action: none` lives on the handles only so the page keeps scrolling
everywhere else. See [touch-interaction.md](touch-interaction.md); never write a second drag
implementation.

## Responsive layout (F1/F3)

`TmDashboard` renders inside a `TmLayoutObserver` and stamps `data-layout="desktop|tablet|mobile"`
from the resolved `TmLayoutMode` (`LayoutMode="Auto"` measures the dashboard's own width; a forced
value never measures — useful for tests). Structural CSS keys off `data-layout`: 12 columns on
desktop, 6 on tablet (2 via a container query below 768px of the grid container), 1 on mobile.
Widget DOM order equals visual `(Y, X, InstanceId)` order in every mode, a collapsed card releases
its height (desktop keeps the saved row slot), and the view-mode header stacks below desktop.
`ResolvedLayoutChanged` reports the resolved mode — never pair it with `LayoutMode` in a two-way
bind. See [responsive-conventions.md](responsive-conventions.md).

## TmWidgetSelector and `CloseOnSelect`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `CloseOnSelect` | `bool` | `true` | With `false` the selector stays open while the host keeps adding widgets: the added card shows a green "✓ Added" badge, an `AllowMultiple` widget grows an added counter, and a live region announces the addition. The dashboard still highlights/scrolls to the newest widget once the selector closes. |
| `LayoutMode` | `TmLayoutMode` | `Auto` | Pins the presentation (including the mobile chip row and the bottom sheet) for tests and embedded hosts. |

The selector presents as an anchored modal on desktop, keeps the category sidebar at two card
columns on tablet, and becomes a bottom sheet with a horizontal chip radio group on mobile —
keyed off `data-layout`, never a viewport media query.

## Dialogs on mobile (F7)

The dashboard switcher (`TmOverlayPanel`), the delete confirmation (`TmDialog`,
`FooterLayout=Stacked`) and the widget selector sheet all present as bottom sheets on a mobile
layout with ≥ 44/48 px targets and focus restored to their triggers on every close path.

## Accessibility summary

- The root is a named `role="region"` (`aria-labelledby` the heading, `aria-label` fallback), so
  several dashboards on one page stay distinguishable.
- Widget titles are real `h3` headings with stable ids; collapse/remove controls reference them
  through `aria-describedby`.
- All moving parts announce through `tm-dashboard-live-region` (polite).
- Enter/Space activate exactly once per keypress; native buttons do the activating — see
  [keyboard-activation-convention.md](keyboard-activation-convention.md).
