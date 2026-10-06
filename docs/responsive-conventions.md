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

Write the boundary as the named width itself (`max-width: 640px`), not `639px`. The one pixel of
overlap with the next range is the documented choice; it keeps the literal identical to the
constant the test compares.

## Container queries

A component that adapts sets both on its root:

```css
.tm-example {
  container-type: inline-size;
  container-name: tm-example;
}
```

Rules then read `@container tm-example (max-width: 640px)`. The name is the component's, so two
nested components do not steal each other's queries.

`container-type: inline-size` removes the element's intrinsic inline size. Do not set it on a
component whose parent sizes it with `fit-content`, `width: auto` inside a shrink-to-fit context,
or `display: inline-block` — the element collapses to nothing. For those, gate the container on an
opt-in class (the data table does this; see its own plan).

Placement that must change per container **cannot** be an inline `grid-column` or `grid-row`: an
inline style beats every container query. Emit the placement as variables (`--tm-w-x`,
`--tm-w-span`) and let the stylesheet turn them into columns.

## `TmLayoutMode` and `TmLayoutObserver`

CSS container queries cover the visual change. A component that must **branch its render** (drop a
pane, swap a dialog for a sheet) uses the mode, because a container query cannot change the markup.

```razor
<TmLayoutObserver LayoutMode="LayoutMode" LayoutModeChanged="OnLayoutChanged">
    <LayoutBranch />
</TmLayoutObserver>

@code {
    [CascadingParameter] private TmLayoutContext? Layout { get; set; }
}
```

`LayoutBranch` reads the cascaded `TmLayoutContext` and renders its mobile or desktop markup. The
observer does not expose the context as a `RenderFragment` argument.

- `LayoutMode="Auto"` measures the component root with `layout-observer.js` (`ResizeObserver`) and
  calls .NET only when the resolved mode changes. The first frame, before a measurement, renders
  `Desktop`.
- `LayoutMode="Mobile"`, `Tablet` or `Desktop` forces that layout and **never imports the
  observer**. A test renders a mode without a DOM, and a host can pin a layout.
- Children read the cascaded `TmLayoutContext` (`Mode`, `Resolved`, `IsMobile`, `IsTablet`,
  `IsDesktop`, `CssModifier`). The root also carries `tm-layout--{mode}` and `data-layout`, so CSS
  can branch without the context.
- `LayoutModeChanged` fires when the rendered mode changes. The initial value is not a change.

`TmDashboard` is the pilot consumer. Its grid follows the container, not the viewport: 12 columns
at desktop, six below 1024px, two below 768px, one below 640px. A widget wider than half the
desktop grid spans the full row in the two-column layout. Its `LayoutMode` parameter reports the
measured mode (or a forced one) on `data-layout`; it does not itself restack the grid, because the
grid is CSS.

## Touch

`--tm-touch-target` is `2.75rem` (44px).

- `.tm-touch-target` opts an element into that minimum size.
- `@media (pointer: coarse)` applies it to `.tm-btn-icon` and to the dashboard's icon-sized
  controls (resize handles, the widget-card add button, menu actions, widget header buttons).
  A component with its own icon control adds the same rule in its own stylesheet.
- `@media (hover: hover) and (pointer: fine)` is the only place an action may be hidden until
  hover. `.tm-reveal-on-hover` does this and stays visible under `:focus-within` and
  `:focus-visible`, so keyboard users reach it too. On `(hover: none)` the action is always
  visible — a touch screen has no hover to reveal it.
- `.tm-safe-area-bottom` and `.tm-safe-area-top` add `env(safe-area-inset-*)` on top of the
  component's own padding, for bars and sheets that sit under the home indicator or the notch.

## Motion

Anything these utilities animate (the reveal, the dashboard handles) drops its transition under
`@media (prefers-reduced-motion: reduce)`.

## What a later plan copies

1. Add `container-type` and `container-name` to the component root, unless the collapse caveat
   applies — then gate it on an opt-in class.
2. Write `@container` rules at 640, 768, 1024 or 1280 only.
3. Where the render itself must change, take a `TmLayoutMode LayoutMode` parameter (default
   `Auto`) and either wrap the root in `TmLayoutObserver` or consume the cascaded
   `TmLayoutContext`. Keep the old parameter as an `[Obsolete]` alias rather than removing it.
4. Add the coarse-pointer minimum and the `(hover: none)` reveal to every hover-only action.
