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

**Per-instance thresholds.** `TmLayoutObserver` and `TmEditorShell` take an optional
`Breakpoints` (`TmLayoutBreakpoints(sm, lg)`, default `TmLayoutBreakpoints.Default` = the table
above) for a component whose content needs other mode boundaries (an e-mail editor: mobile below 768,
desktop from 1200). It moves only the *markup* branch — a stylesheet keeps the shared literals — and
classification stays half-open. See [editor-shell.md](editor-shell.md).

A new `@media` or `@container` condition uses one of these four widths. `TmBreakpointsTests` scans
every `src/**/wwwroot/**/*.css` for `@container` widths; `@media` widths are held by
`media-width-baseline.txt`, which is shrink-only and counts each occurrence.

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
        @if (layout.IsMobile)
        {
            <Sheet />
        }
        else
        {
            <Pane />
        }
    </LayoutContent>
</TmLayoutObserver>
```

The owner branches on the `layout` context directly. Passing it as a parameter to a component that
only declares `[CascadingParameter]` throws. A child that should read the cascade goes under
`ChildContent` instead, where the context reaches it:

```razor
<TmLayoutObserver>
    <ChildContent>
        <MyPane />
    </ChildContent>
</TmLayoutObserver>
```

Resolution order, in `TmLayout.Resolve`: an explicit (non-Auto) parameter, then a forced ancestor
(a cascaded context whose `Mode` is not Auto), then this component's own measurement, then the
ancestor's resolved mode when that ancestor is itself Auto (the app-level fallback), then
`InitialMode`. An Auto component under a forced ancestor adopts the ancestor's resolved mode and
does not import the observer.

The pre-measure mode — static SSR, prerender, and the first frame — is `InitialMode`, which
defaults to Desktop. Desktop markup must degrade acceptably through the `data-layout` CSS, because
that frame has no measurement yet.

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
- What a surface resolves from depends on what it is positioned against.
  - Positioned against the viewport — a modal, a dialog, a modal bottom sheet, a dropdown or popover
    presented as a sheet, an action bar with `Placement=FixedViewport` — resolves
    `explicit parameter > the TmLayoutScopes.Viewport context > InitialMode`.
  - Positioned against its container — a `StickyContainer` or `Inline` action bar, a non-modal sheet
    inside a container, a `TmSidePanel` choosing docked or sheet, a popover's width, a menu's density
    — resolves from the nearest unnamed context. The popover-versus-sheet choice follows the
    viewport; the popover's size follows the trigger's container.
  - Read the viewport context by name and pass it, not the container context:

    ```razor
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] private TmLayoutContext? Viewport { get; set; }

    var mode = TmLayout.Resolve(LayoutMode, Viewport, null, InitialMode);
    ```

  - A sheet caps at 85% of the visible viewport, so the content behind it stays a sliver and the
    gesture reads as a sheet. The sheet module measures `visualViewport` and writes two lengths on
    the sheet root: `--tm-sheet-viewport` (`${height}px`, default `100dvh`) and `--tm-sheet-keyboard`
    (`${hidden}px`, default `0px`). The snap height is a fraction of that length, and the keyboard
    length pads the sheet so it lifts above the keyboard. Both are lengths, so the sheet is usable
    before the module runs. The offset is a custom property, never an inline height.
  - A host should place an app-level `<TmLayoutObserver IsViewportScope="true" IsContainer="false">`
    as the outermost full-viewport layout element (decision `F2-VIEWPORT-HOST-REQUIREMENT`: should,
    not must). `IsContainer="false"` is mandatory on that scope. An overlay with no viewport scope
    measures the viewport itself through an internal probe, so a phone still gets the sheet; the first
    frame uses `InitialMode` until that measurement arrives. The probe is internal and reads no cascade:
    a fixed, hidden box the size of the viewport, reported on every measurement including the first.
    `TmModal`, `TmDialog` and `TmDrawer` share one helper for that resolution. Set `InitialMode` on the
    overlay when that first frame matters. The missing-scope hint is logged once per process, at
    Information, under the `OverlayLayout` logger category. A host that does not want it filters
    that category; the environment is not sniffed.

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
- `.tm-reveal-host` is the hover and focus-within scope for a `.tm-reveal-on-hover` action: the
  action reveals when the pointer or focus is anywhere inside the host, not only on the action.
- `.tm-safe-area-bottom` and `.tm-safe-area-top` add `env(safe-area-inset-*)` on top of
  `--tm-safe-area-base` (which falls back to `--tm-space-2`), for bars and sheets that sit under the
  home indicator or the notch. The inset is zero unless the host page sets `viewport-fit=cover`; set
  `--tm-safe-area-base` to the component's own padding.

## Bottom navigation vs action bar (B4)

Two bottom bars exist and they are not interchangeable:

- **`TmBottomNavigation` is application chrome.** It switches the app's top-level destinations,
  is rendered once by the layout, and anchors to the viewport for the whole session. It answers
  "where can the user go?".
- **`TmMobileActionBar` (F5) is a component's action bar.** It exposes the actions of one
  component (a dashboard's edit/save/cancel, a list's filter/export, …), lives inside that
  component's own container — the default `StickyContainer` placement uses `position: sticky`,
  never `position: fixed` — and scrolls away with the content it belongs to. It answers "what
  can the user do here?". A host that needs viewport anchoring opts into
  `Placement="FixedViewport"` deliberately. `Auto` visibility hides the bar above the mobile
  breakpoint: a `StickyContainer`/`Inline` bar resolves from the nearest unnamed layout
  context (its own container), a `FixedViewport` bar resolves from the `TmLayoutScopes.Viewport`
  cascade like every other viewport-anchored surface. Actions that overflow `MaxVisible` move
  into a "More" menu that composes `TmOverlayPanel` — the anchored popover on desktop, the
  shared bottom-sheet primitive on a mobile viewport.

  Host rules that come with the bar:

  - **Never anchor a `FixedViewport` bar next to `TmBottomNavigation`** — two bars must not
    fight for the viewport bottom edge. If a page genuinely needs both, the action bar is the
    component-scoped one (`StickyContainer`) and the navigation keeps the viewport edge; a
    deliberate stack must offset the bar (custom property) and is the host's own design debt.
  - **The content reserve is published**: `--tm-mobile-action-bar-reserve-block-size`
    (tokens.css) defaults to the bar height plus the bottom safe area and is what the
    `FixedViewport` body padding consumes. A host scrolling content under a fixed bar reads the
    same token for its own `scroll-padding-bottom`, so focused content is never hidden behind
    the bar.
  - **`position: sticky` breaks under an `overflow: hidden` or `overflow: auto` ancestor**
    (the scroll container becomes the sticky box's viewport — often the component itself, so
    the bar can never move). `overflow: clip` is fine. Render the bar outside clipped
    ancestors; a transformed/filtered/contained ancestor re-anchors a `FixedViewport` bar per
    CSS too.
  - **Without `ChildContent` the bar's own root is the sticky box** (`--bare`): the sticky
    containing block spans the host component instead of a bar-height wrapper, so a bare
    container like the dashboard's actually sticks.
  - **The safe-area inset is zero unless the host page sets `viewport-fit=cover`** on its
    viewport meta (see Touch above).

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
3. An owner exposes and forwards `LayoutMode`, `ResolvedLayoutChanged` and `InitialMode`. Its markup
   reads the resolved mode only through `LayoutContent`. A `ResolvedLayoutChanged` handler may react
   to a transition — restore focus, close a pane — but must not keep a copy of the mode as something
   it renders from. Never pair `LayoutMode` with `ResolvedLayoutChanged`: the callback reports the
   resolved mode, and a two-way bind would pin it.
4. Resolve from what the surface is positioned against: the `TmLayoutScopes.Viewport` cascade for a
   viewport surface, the nearest unnamed context for an in-container one. A `[CascadingParameter]
   TmLayoutContext` subscriber re-renders on every owner render; an expensive leaf takes `IsMobile`
   as a value parameter or overrides `ShouldRender`. Add the coarse-pointer minimum and the
   `(hover: none)` reveal to every hover-only action.
