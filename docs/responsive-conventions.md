# Responsive conventions

A component decides its layout from **its own width**, never from the browser viewport. A 390px
dashboard inside a 1440px page is mobile. These conventions are the contract every later plan
builds on.

## Breakpoints

CSS custom properties are illegal inside an `@media` or `@container` condition, so the widths are
literals. The same numbers live in `TmBreakpoints` (`Tempo.Blazor.Abstractions.Layout`) and a test
reads the stylesheets to prove they match.

| Name | Width | `TmLayoutMode` boundary |
|------|-------|-------------------------|
| `Sm` | 640px | below this the container is `Mobile` |
| `Md` | 768px | a named step, not a mode boundary |
| `Lg` | 1024px | at this width the container becomes `Desktop` |
| `Xl` | 1280px | the wide desktop step |

`TmLayoutMode` therefore resolves as:

- **Mobile** — container width below 640px
- **Tablet** — from 640px up to, but not including, 1024px
- **Desktop** — 1024px and above

A new `@media` or `@container` condition uses one of these four widths. `TmBreakpointsTests`
fails when `breakpoints.css` or `_dashboard.css` uses another.

Conditions are half-open, identical to `TmBreakpoints.Classify`: `@media (width < 768px)` and
`@media (width >= 768px)`, never `max-width` or `min-width`. The boundary belongs to exactly one
side, so nothing is hidden twice at 768.

## Container query or `TmLayoutMode`

| Question | Use |
|----------|-----|
| Must the rule agree with a markup branch, or define the structural layout? | `[data-layout=…]` on the component root. Blazor renders it from the resolved mode. |
| Is it presentation that cannot conflict with markup, or a finer step inside one mode? | A container query. The dashboard's two-column step inside tablet is one. |

A container query styles descendants of the container, never the container itself — put
`container-type` on an ancestor of the elements you style. A query on `.tm-dashboard-grid` that
restyles `.tm-dashboard-grid` matches nothing.

## Container queries

A component that adapts sets both on an ancestor of the elements the queries style:

```css
.tm-example {
  container-type: inline-size;
  container-name: tm-example;
}
```

Rules then read `@container tm-example (width < 640px)`. The name is the component's, so two
nested components do not steal each other's queries.

`container-type: inline-size` removes the element's intrinsic inline size. Do not set it on a
component whose parent sizes it with `fit-content`, `width: auto` inside a shrink-to-fit context,
or `display: inline-block` — the element collapses to nothing. `TmLayoutObserver` takes
`IsContainer="false"` for those hosts. A data-table opt-in is not part of this convention yet.

Placement that must change per container **cannot** be an inline `grid-column` or `grid-row`: an
inline style beats every container query. Emit the placement as variables (`--tm-w-x`,
`--tm-w-span`) and let the stylesheet turn them into columns.

## `TmLayoutMode` and `TmLayoutObserver`

CSS container queries cover the visual change. A component that must **branch its render** (drop a
pane, swap a dialog for a sheet) uses the mode, because a container query cannot change the markup.

```razor
<TmLayoutObserver LayoutMode="LayoutMode" ResolvedLayoutChanged="OnLayoutChanged">
    <LayoutContent Context="layout">
        <MyPane Layout="layout" />
    </LayoutContent>
</TmLayoutObserver>
```

```razor
@* MyPane.razor *@
@if (Layout?.IsMobile == true)
{
    <Sheet />
}
else
{
    <Pane />
}

@code {
    [CascadingParameter] private TmLayoutContext? Layout { get; set; }
}
```

`MyPane` reads the cascaded `TmLayoutContext`. An icon-only button is
`<TmButton Class="tm-btn-icon" />`: the size modifier drives the square.

Resolution order, in `TmLayout.Resolve`: an explicit (non-Auto) parameter, then a forced ancestor
(a cascaded context whose `Mode` is not Auto), then this component's own measurement, then the
ancestor's resolved mode when that ancestor is itself Auto (the app-level fallback), then
`InitialMode`. An Auto component under a forced ancestor adopts the ancestor's resolved mode and
does not import the observer.

The pre-measure mode — static SSR, prerender, and the first frame — is `InitialMode`, which
defaults to Desktop. Desktop markup must degrade acceptably through the `data-layout` CSS, because
that frame has no measurement yet.

An overlay, sheet or dropdown must not measure its own root. It resolves from the trigger's
cascaded context, and falls back to an app-level `<TmLayoutObserver>` in the host layout (the
viewport) when the trigger cascaded nothing.

- `LayoutMode="Auto"` measures the component root with `layout-observer.js` (`ResizeObserver`) and
  calls .NET only when the resolved mode changes. The script never writes `data-layout`; Blazor does.
- `LayoutMode="Mobile"`, `Tablet` or `Desktop` forces that layout and **never imports the
  observer**. A test renders a mode without a DOM, and a host can pin a layout.
- Children read the cascaded `TmLayoutContext` (`Mode`, `Resolved`, `IsMobile`, `IsTablet`,
  `IsDesktop`, `CssModifier`). The root also carries `tm-layout--{mode}` and `data-layout`, so CSS
  can branch without the context.
- `ResolvedLayoutChanged` fires when the rendered mode changes. The initial value is not a change.
  Never pair `LayoutMode` with `ResolvedLayoutChanged`: the callback reports the resolved mode, and a
  two-way bind would pin that mode. An owner that must branch its own markup uses `LayoutContent`,
  which receives the context; it does not keep a copy of the mode.
- An app-level `<TmLayoutObserver IsViewportScope="true">` also cascades its context under
  `TmLayoutScopes.Viewport`. Anything positioned against the viewport (a modal, a dialog, a bottom
  sheet, an action bar) resolves `explicit parameter > that viewport context > InitialMode`. The
  trigger's own container only sizes inline content, such as a popover's width.

`TmDashboard` is the pilot consumer. Its grid follows the resolved mode: 12 columns on desktop, six
on tablet, one on mobile. Inside tablet, below 768px of the dashboard's own container, the grid
becomes two columns. A widget wider than half the desktop grid spans the full tablet row. Forcing
`LayoutMode` restacks the grid, because the grid reads `data-layout`. Drag and resize are desktop
only. The placement variables (`--tm-w-x`, `--tm-w-span`, `--tm-w-span-md`, `--tm-w-y`,
`--tm-w-rows`, `--tm-w-order`) are scoped to `TmDashboard`; another component does not read them.

## Touch

`--tm-touch-target` is `2.75rem` (44px).

- `.tm-touch-target` opts an element into that minimum size.
- `@media (pointer: coarse)` applies it to `.tm-btn-icon` and to the dashboard's icon-sized
  controls (the corner resize handle, the widget-card add button, menu actions, widget header
  buttons). The edge resize handles are hidden on a coarse pointer; only the corner handle remains,
  at 44px. A component with its own icon control adds the same rule in its own stylesheet. The
  shared close buttons (dialog, modal, drawer, popover) follow the same minimum.
- `@media (hover: hover) and (pointer: fine)` is the only place an action may be hidden until
  hover. `.tm-reveal-on-hover` does this and stays visible under `:focus-within` and
  `:focus-visible`, so keyboard users reach it too. On `(hover: none)` the action is always
  visible — a touch screen has no hover to reveal it.
- `.tm-reveal-host` is the container for a `.tm-reveal-on-hover` action. The action stays hidden
  until hover or focus on a fine pointer, and is always visible on `(hover: none)`.
- `.tm-safe-area-bottom` and `.tm-safe-area-top` add `env(safe-area-inset-*)` on top of
  `--tm-safe-area-base` (which falls back to `--tm-space-2`), for bars and sheets that sit under the
  home indicator or the notch. The inset is zero unless the host page sets `viewport-fit=cover`; set
  `--tm-safe-area-base` to the component's own padding.

## Motion

Anything these utilities animate (the reveal, the dashboard handles) drops its transition under
`@media (prefers-reduced-motion: reduce)`.

## What a later plan copies

1. The root may be a container only if the queries style its descendants. A structural change (how
   many columns, which pane) follows `[data-layout]`; a container query is only for a finer step
   inside one mode.
2. Write `@container` and `@media` rules at 640, 768, 1024 or 1280 only, in range syntax. A width
   outside that set, or `min-width`/`max-width`, is debt tracked by `media-width-baseline.txt`,
   which may only shrink.
3. An owner reads its own resolved mode only through `LayoutContent`. Never derive state from
   `ResolvedLayoutChanged`: the callback reports the resolved mode, and a two-way bind would pin it.
4. Anything positioned against the viewport resolves from the `TmLayoutScopes.Viewport` cascade, not
   from the trigger's container. Add the coarse-pointer minimum and the `(hover: none)` reveal to
   every hover-only action.
