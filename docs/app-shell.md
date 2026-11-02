# TmAppShell

The application frame: one composite that renders the page's landmarks — a top bar, ONE named
navigation column, the content and an optional resizable work area — and resolves its own layout from
its **container** width. It composes the existing primitives instead of reimplementing them:
`TmLayoutObserver` (measurement + cascaded `TmLayoutContext`), `TmSidebar` (navigation model,
sections, `aria-current`, rail tooltips), `TmTopBar` (the bar chrome and its account menu) and a modal
`TmDrawer` (the tablet navigation overlay, with its focus trap, Escape and focus restore).

- Source: `src/Tempo.Blazor/Components/Layout/TmAppShell.razor` (+ `.razor.cs`, `.Parameters.razor.cs`)
- Styles: `wwwroot/css/components/_app-shell.css`
- Shared resize helper: `wwwroot/js/tm-panel-resize.js` (over `pointer-drag.js`); C# mirror
  `Tempo.Blazor.Helpers.PanelResize` / `PanelResizeInterop`
- Demo: `/app-shell` (plan phase 6)
- Tests: `tests/Tempo.Blazor.Tests/Layout/TmAppShellTests.cs`,
  `tests/Tempo.Blazor.Tests/Layout/TmAppShellWorkAreaTests.cs`,
  `src/Tempo.Blazor/wwwroot/js/__tests__/tm-panel-resize.test.mjs`

## Layouts

The frame classifies **its own width**, never the viewport, so it can be embedded at any size:

| Layout | Navigation | Content | Work area |
|---|---|---|---|
| Desktop (≥ 1200 px by default) | docked `TmSidebar` column, collapsible to an icon rail | `main` | docked, right, resizable |
| Tablet (768–1199 px) | icon rail; the full navigation opens in a **modal overlay drawer** | `main` | docked, right, resizable |
| Phone (< 768 px, the plan's mobile phase) | the tablet rail is the fallback until the phone chrome ships | `main` | not docked (the plan's mobile phase presents it as a sheet) |

`LayoutMode` pins a mode (a test renders one without a DOM) and `Breakpoints` overrides the pair
(default `768` / `1200`). The stylesheet carries **no width condition**: only the markup branch moves,
so a per-instance pair can never disagree with a media query.

## Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Variant` | `AppShellVariant` | `Inset` | `Inset` = the content is a rounded card on the workspace; `Flat` = edge to edge with a hairline divider. |
| `LayoutMode` | `TmLayoutMode` | `Auto` | `Auto` measures the shell's root; any other value pins the layout. |
| `InitialMode` | `TmLayoutMode` | `Desktop` | The mode rendered before the first measurement (SSR, prerender). |
| `Breakpoints` | `TmLayoutBreakpoints?` | `null` → 768 / 1200 | Per-instance thresholds. |
| `ResolvedLayoutChanged` | `EventCallback<TmLayoutMode>` | — | Reports a resolved-mode change; never two-way bind it. |
| `NavItems` | `IReadOnlyList<ISidebarNavItem>?` | `null` | The flat built-in navigation list. |
| `NavSections` | `IReadOnlyList<SidebarNavSection>?` | `null` | Named groups under visible headings; **wins over `NavItems`**. |
| `Sidebar` | `RenderFragment?` | `null` | A custom navigation surface; the frame wraps it in its one named `nav`. |
| `SidebarHeader` / `SidebarFooter` | `RenderFragment?` | `null` | Logo / account slots of the built-in sidebar. |
| `SidebarCollapsed` / `SidebarCollapsedChanged` | `bool` / `EventCallback<bool>` | `false` | The docked column's icon-rail state (desktop only). |
| `SidebarNavLabel` | `string?` | `null` → "Main navigation" | Accessible name of the navigation landmark and title of the overlay drawer. |
| `TopBar` | `RenderFragment?` | `null` | Custom bar content; rendered inside the frame's own `header`. |
| `Title` | `string?` | `null` | Brand title of the built-in bar. Without any bar content the shell renders **no** header. |
| `TopBarActions` | `RenderFragment?` | `null` | Right-aligned bar actions. |
| `User` / `UserMenuItems` / `OnLogout` | `IUserInfo?` / `IReadOnlyList<UserMenuItem>?` / `EventCallback` | `null` | The built-in bar's account menu. |
| `ChildContent` | `RenderFragment?` | `null` | The page content, inside `main`. |
| `SkipLinkTarget` | `string?` | `null` → unique | The id the skip link targets and `main` carries. |
| `WorkArea` | `RenderFragment?` | `null` | The right-hand region. |
| `WorkAreaOpen` / `WorkAreaOpenChanged` | `bool` / `EventCallback<bool>` | `false` / — | Visibility. **With** a handler the region renders its own close button (which raises the callback) and moves the focus to the content when the host echoes the close; without one it renders no close control. |
| `WorkAreaWidth` / `WorkAreaWidthChanged` | `int` / `EventCallback<int>` | `420` | Two-way width in CSS pixels. |
| `WorkAreaMinWidth` / `WorkAreaMaxWidth` | `int` | `280` / `640` | The resize bounds (a max below the min throws). |
| `MinContentWidth` | `int` | `480` | The content width a resize never takes away. |
| `WorkAreaLabel` | `string?` | `null` → "Work area" | Heading of the region, which also names it. |
| `PersistKey` | `string?` | `null` | Persists the user's width and collapse choice in `localStorage`. |
| `Class` / `AdditionalAttributes` | | | Forwarded to the root. |

## Landmarks

The frame owns the page's landmarks **exactly once**: one `header` (only when there is bar content),
one `nav` with an `aria-label`, one `main` with the skip link's id, and one `aside` for the work area.
That is why the two primitives it composes surrender theirs:

```razor
<TmTopBar IsLandmark="false" … />   <!-- renders a <div class="tm-topbar">, the frame owns the <header> -->
<TmSidebar IsLandmark="false" … />  <!-- renders a <div class="tm-sidebar">, its <nav> keeps naming the menu -->
```

Both parameters default to `true`, so a host that uses `TmTopBar` / `TmSidebar` on their own keeps
today's markup and semantics. `TmAppShell` is also the one component allowed to emit `<main>`:
`LandmarkOwnershipTests` keeps an explicit, justified allow-list for it, and the shell's own tests pin
"each landmark once".

A host slot must not add a landmark of its own: `TopBar` content renders inside the frame's `header`,
and `Sidebar` content inside the frame's `nav` — so neither may render a `header` / `nav` element.

## Skip link

The first focusable element of the frame is a "Skip to content" link (localized, visually hidden until
it takes the focus) pointing at `main`, which is `tabindex="-1"` so the skip lands focus on the region
instead of only scrolling. `SkipLinkTarget` overrides the generated id. The hiding uses the
visually-hidden pattern (`clip-path: inset(50%)` + a 1 px clipped box), not a transform: the frame
clips nothing — a host embeds the shell mid-page — so a link merely moved above the frame would still
paint over the host's own content.

## Resize, keyboard and the non-drag alternative

The work area's separator is a real ARIA separator: `role="separator"`, `aria-orientation="vertical"`,
`aria-valuenow` / `aria-valuemin` / `aria-valuemax`, `aria-controls` the region, `tabindex="0"` and a
localized label. Three paths set the width, and every one of them shares the same clamp:

| Path | Behaviour |
|---|---|
| Pointer drag | `tm-panel-resize.js` (over the shared `pointer-drag.js`): live width as a CSS custom property, one commit on release, Escape / `pointercancel` / a mid-drag detach restore the start width. |
| Keyboard | `ArrowLeft` grows / `ArrowRight` shrinks by 16 px (64 with Shift), `Home` / `End` jump to the bounds — WCAG 2.5.7's non-drag alternative. |
| Double click | Resets to the width the host supplied (the same reset the keyboard path reaches by stepping back). |

Every path clamps to `[WorkAreaMinWidth, WorkAreaMaxWidth]` **and** keeps the content at
`MinContentWidth`: growth is refused (never reversed) when the measured content is already too narrow,
which is why `aria-valuemax` reports the width the user can really reach rather than the configured
maximum.

`MinContentWidth` protects a **resize** — the shell never shrinks a width the host supplied. A host
that docks a work area on a narrow container (a 768 px tablet with rail + work area) therefore passes
a smaller `WorkAreaWidth` / `MinContentWidth`, or opens the work area only where there is room. The separator widens its hit area under `(pointer: coarse)` to 24 px without changing the
6 px visual strip.

## Persistence

`PersistKey` stores — under `tempo.tm-app-shell.<key>` — only what the **user** chose: the work-area
width with the host width it was resized from, and the collapsed state they picked. What persists is
the user's **intent**, never a diff against the parameter: a host that binds `@bind-SidebarCollapsed`
writes the value straight back into `SidebarCollapsed`, so comparing the two would store nothing for
exactly the hosts that bind it. A value the host picks on its own (one the user did not ask for) clears
the stored collapse, and a restored value counts as the user's own choice, so a later resize keeps it.
A restore applies only while the host still supplies that base width, and it is reported through
`WorkAreaWidthChanged` / `SidebarCollapsedChanged` (a restore counts as a user value). A host width set
**after** a user resize wins and clears the stored entry. Storage failures (private mode, quota, SSR)
are swallowed: the shell keeps its parameter values.

## Tablet overlay

On a tablet (and the phone fallback) the navigation column is the icon rail and the full navigation
lives in a modal `TmDrawer`: the focus moves into the drawer, Escape and its close button close it, the
page behind it is inert and the focus returns to the rail toggle that opened it. The drawer is a
**sibling** of the frame, never a child: the frame is a CSS size container (`container-type:
inline-size`), and a fixed-position overlay inside it would be clipped and pinned by it. The rail's
opener is `TmSidebar`'s own toggle for the built-in navigation; a host `Sidebar` slot gets a
shell-rendered toggle (`app-shell-nav-toggle`, with `aria-expanded` / `aria-controls`). The built-in
rail toggle reports the SAME state through `TmSidebar.ToggleAriaExpanded` /
`TmSidebar.ToggleAriaControls`: a tablet rail toggle opens the drawer, so it carries `aria-expanded`
and points `aria-controls` at the drawer element while that drawer exists. The docked desktop column
reports neither (it opens no surface), and neither does the copy inside the drawer (the drawer it sits
in is already open).

## Accessibility notes

- One polite live region is not needed: nothing in the frame announces on its own, and the only
  transient surface (the overlay drawer) carries its own dialog semantics.
- Focus-visible: every interactive part (skip link, rail toggle, close button, separator, `main`)
  paints `--tm-focus-ring`, and under `forced-colors: active` it paints a 2 px `Highlight` ring. The
  separator's hover / dragging highlight also switches to the system `Highlight` colour there: the
  surface tokens collapse to `Canvas` in High Contrast, so the strip under the pointer would otherwise
  be invisible.
- The skip link is hidden with `clip-path: inset(50%)` rather than an off-frame transform, so nothing
  paints over a host page above an embedded shell (see *Skip link*).
- Coarse pointers get 44 px targets (`--tm-touch-target`) on the rail toggle and the work area's close
  button, plus the widened separator hit area.
- No new transition or animation is declared by the frame's stylesheet, so the reduced-motion block
  needs no entry; the overlay and the rail transitions it inherits are already covered by
  `TmDrawer` / `TmSidebar`.

## Hosting notes

- The shell fills its parent's height (`block-size: 100%`); give it a height (`100dvh`, a grid row or
  the demo's resizable box) so the navigation column and the work area can stretch.
- Two shells on one page: set `TestIdPrefix` so the generated `data-testid`s stay unique, and
  `SkipLinkTarget` so the two `main` ids cannot collide.
- `PersistKey` must differ per shell — one key per frame.

## See also

- [docs/sidebar-and-topbar.md](sidebar-and-topbar.md) — `IsLandmark` and the other additive parameters
- [docs/touch-interaction.md](touch-interaction.md) — the shared `pointer-drag.js` / `pointer-gesture.js` contract
- [docs/editor-shell.md](editor-shell.md) — the other container-owned shell (`TmEditorShell`); it uses the same resize helper
