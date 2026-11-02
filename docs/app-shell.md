# TmAppShell

The application frame: one composite that renders the page's landmarks — a top bar, ONE named
navigation column, the content and an optional resizable work area — and resolves its own layout from
its **container** width. It composes the existing primitives instead of reimplementing them:
`TmLayoutObserver` (measurement + cascaded `TmLayoutContext`), `TmSidebar` (navigation model,
sections, `aria-current`, rail tooltips), `TmTopBar` (the bar chrome and its account menu), a modal
`TmDrawer` (the tablet and phone navigation overlay, with its focus trap, Escape and focus restore),
`TmBottomNavigation` (the phone bottom bar) and `TmSidePanel` (the phone work-area sheet).

- Source: `src/Tempo.Blazor/Components/Layout/TmAppShell.razor` (+ `.razor.cs`, `.Parameters.razor.cs`)
- Styles: `wwwroot/css/components/_app-shell.css`
- Shared resize helper: `wwwroot/js/tm-panel-resize.js` (over `pointer-drag.js`); C# mirror
  `Tempo.Blazor.Helpers.PanelResize` / `PanelResizeInterop`
- Demo: `/app-shell` (plan phase 6)
- Tests: `tests/Tempo.Blazor.Tests/Layout/TmAppShellTests.cs`,
  `tests/Tempo.Blazor.Tests/Layout/TmAppShellWorkAreaTests.cs`,
  `tests/Tempo.Blazor.Tests/Layout/TmAppShellMobileTests.cs`,
  `tests/Tempo.Blazor.Tests/Layout/TmAppShellMobileCssTests.cs`,
  `src/Tempo.Blazor/wwwroot/js/__tests__/tm-panel-resize.test.mjs`

## Layouts

The frame classifies **its own width**, never the viewport, so it can be embedded at any size:

| Layout | Navigation | Content | Work area |
|---|---|---|---|
| Desktop (≥ 1200 px by default) | docked `TmSidebar` column, collapsible to an icon rail | `main` | docked, right, resizable |
| Tablet (768–1199 px) | icon rail; the full navigation opens in a **modal overlay drawer** (opener: the rail toggle) | `main` | docked, right, resizable |
| Phone (< 768 px by default) | no column at all: a **hamburger in the top bar** opens the full navigation in a modal drawer (`MobileNav="Drawer"`, the default), or a **`TmBottomNavigation` bar** with no drawer (`MobileNav="BottomNavigation"`) | `main` | a **`TmSidePanel` bottom sheet**; **no resize separator** |

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
| `Sidebar` | `RenderFragment?` | `null` | A custom navigation surface; the frame wraps it in its one named `nav`. On a phone the drawer closes when the location changes, never on a bare click — a slot command that must dismiss it calls `CloseNavigationAsync()` (see *Only a chosen destination closes the phone drawer*). |
| `SidebarHeader` / `SidebarFooter` | `RenderFragment?` | `null` | Logo / account slots of the built-in sidebar — rendered in the docked column **and in both drawers**, so the signed-in account stays reachable on a phone. |
| `SidebarCollapsed` / `SidebarCollapsedChanged` | `bool` / `EventCallback<bool>` | `false` | The docked column's icon-rail state (desktop only). |
| `SidebarNavLabel` | `string?` | `null` → "Main navigation" | Accessible name of the navigation landmark and title of the overlay drawer. |
| `MobileNav` | `AppShellMobileNav` | `Drawer` | The phone presentation: `Drawer` (hamburger → modal drawer) or `BottomNavigation` (a `TmBottomNavigation` bar and no drawer). Desktop and tablet ignore it. |
| `BottomNavItems` | `IReadOnlyList<BottomNavItem>?` | `null` → derived | Entries of the phone bottom bar. Null derives one entry per top-level navigation item (sections in order, sub-entries ignored). |
| `BottomNavItemClicked` | `EventCallback<BottomNavItem>` | — | Raised when a bottom-bar entry is tapped, before the shell follows its `Href`. |
| `ShowTopBarMenuButton` | `bool?` | `null` → auto | Whether the top bar renders the phone drawer's hamburger. Auto = a phone layout in `Drawer` mode while a navigation model exists. Pass `false` when a custom `TopBar` carries its own opener. |
| `TopBar` | `RenderFragment?` | `null` | Custom bar content; rendered inside the frame's own `header`. On a phone in `Drawer` mode the frame renders its own hamburger in front of the slot (opt out with `ShowTopBarMenuButton="false"`). |
| `Title` | `string?` | `null` | Brand title of the built-in bar. Without any bar content the shell renders **no** header — except on a phone with navigation, which needs a bar for its hamburger. |
| `TopBarActions` | `RenderFragment?` | `null` | Right-aligned bar actions. |
| `User` / `UserMenuItems` / `OnLogout` | `IUserInfo?` / `IReadOnlyList<UserMenuItem>?` / `EventCallback` | `null` | The built-in bar's account menu. |
| `ChildContent` | `RenderFragment?` | `null` | The page content, inside `main`. |
| `SkipLinkTarget` | `string?` | `null` → unique | The id the skip link targets and `main` carries. |
| `WorkArea` | `RenderFragment?` | `null` | The right-hand region: a docked, resizable column on desktop and tablet, a modal `TmSidePanel` bottom sheet on a phone. |
| `WorkAreaOpen` / `WorkAreaOpenChanged` | `bool` / `EventCallback<bool>` | `false` / — | Visibility. **With** a handler the docked region renders its own close button (which raises the callback) and moves the focus to the content when the host echoes the close; the phone sheet is **always** closable (see *The phone work-area sheet*). |
| `WorkAreaWidth` / `WorkAreaWidthChanged` | `int` / `EventCallback<int>` | `420` | Two-way width in CSS pixels (desktop and tablet only). |
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

On a tablet the navigation column is the icon rail and the full navigation lives in a modal
`TmDrawer`: the focus moves into the drawer, Escape and its close button close it, the page behind it is
inert and the focus returns to the rail toggle that opened it. The drawer is a **sibling** of the
frame, never a child: the frame is a CSS size container (`container-type: inline-size`), and a
fixed-position overlay inside it would be clipped and pinned by it. The rail's opener is
`TmSidebar`'s own toggle for the built-in navigation; a host `Sidebar` slot gets a shell-rendered
toggle (`app-shell-nav-toggle`, with `aria-expanded` / `aria-controls`). The built-in rail toggle
reports the SAME state through `TmSidebar.ToggleAriaExpanded` / `TmSidebar.ToggleAriaControls`: a
tablet rail toggle opens the drawer, so it carries `aria-expanded` and points `aria-controls` at the
drawer element while that drawer exists. The docked desktop column reports neither (it opens no
surface), and neither does the copy inside the drawer (the drawer it sits in is already open).

## Phone navigation

A phone container renders **no navigation column at all** — neither the docked column nor the tablet
rail. `MobileNav` (`AppShellMobileNav`) decides what replaces it:

| `MobileNav` | Navigation on a phone |
|---|---|
| `Drawer` (default) | A **hamburger** in the top bar (`aria-label` from the resources, `aria-expanded`, `aria-controls` pointing at the drawer while it exists, `aria-controls` omitted while it does not) opens the SAME `TmDrawer` the tablet uses, with the full navigation, the `SidebarHeader` slot and the `SidebarFooter` slot (the signed-in account). Focus enters the drawer, Escape / the close button / **choosing an entry** close it, and the focus returns to the hamburger: the frame owns that button's id (`TmTopBar.MenuButtonId`) and passes it as the drawer's restore target. The drawer's width is `min(var(--tm-app-shell-nav-width), 85vw)` so it never covers the whole screen. |
| `BottomNavigation` | A **`TmBottomNavigation` bar** at the bottom of the frame and **no drawer at all** — the top bar renders no hamburger, because a hamburger that opens nothing lies. Entries come from `BottomNavItems`, or are derived from the navigation model (one entry per top-level item, sections in order, sub-entries ignored). A tap raises `BottomNavItemClicked` and the frame follows the entry's `Href` — a bar of links that never navigates is dead. |

### Only a chosen destination closes the phone drawer

The drawer body is **not** one big close target. It holds the `SidebarHeader` slot (a workspace
switcher), the `SidebarFooter` slot (the account / `TmUserMenu`), the section headings, empty padding
and any host `Sidebar` slot content — dismissing on a tap there would unmount the surface a menu is
about to open in. The frame therefore closes the phone drawer from exactly two signals:

- **A chosen entry** — the built-in `TmSidebar` reports it through the additive, unbound-by-default
  `TmSidebar.OnItemSelected` (`EventCallback<ISidebarNavItem>`), raised from the link's own click and
  never preventing the default. This also covers choosing the page the user is already on, which
  raises no location change at all. The frame binds it **only** to the copy inside the phone drawer, so
  the docked column and the tablet drawer keep their previous behaviour.
- **A location change** — `NavigationManager.LocationChanged` closes the phone drawer, which is what
  covers a host `Sidebar` slot: its links navigate through their own markup, and the frame cannot see
  what they were. It is phone-only too; a tablet drawer survives a route change exactly as before.

A host `Sidebar` slot whose control should close the drawer **without** navigating (an action that
switches workspace in place) calls `CloseNavigationAsync()` itself — the frame never guesses.

```razor
<TmAppShell @ref="shell" NavItems="@items" SidebarHeader="switcher" SidebarFooter="account" … />
@code {
    // A slot command that must dismiss the drawer but stay on the page.
    private Task SwitchWorkspaceAsync(Workspace workspace) => shell!.CloseNavigationAsync();
}
```

A phone frame with navigation but no bar content still renders a `<header>`: that is where the
hamburger lives. A custom `TopBar` slot has no opener of its own, so the frame renders one **in front
of** it (the header becomes a two-column row, `tm-app-shell__topbar--menu`); a bar that carries its own
opener passes `ShowTopBarMenuButton="false"` and keeps the phase-3 markup, driving the drawer through
`OpenNavigationAsync()` / `CloseNavigationAsync()` instead.

The bottom bar is a **layout ROW** of the frame (`flex: 0 0 auto` after the body), not viewport-fixed
chrome: the frame classifies its own container, so a `position: fixed` bar would be pinned to (and
clipped by) the frame and would cover the content. Its own stylesheet's fixed positioning is therefore
reset for the frame's copy (`.tm-bottom-nav--app-shell { position: static }`), while the standalone
component keeps anchoring to the viewport.

Inside the phone drawer the built-in sidebar drops its collapse toggle
(`TmSidebar.ShowToggle="false"`): the drawer is already open and the chevron would be labelled
"Collapse menu" while closing nothing it shows — the drawer's own close button is the way out. The
tablet drawer keeps the toggle (phase-3 markup).

### Public methods

| Method | Behaviour |
|---|---|
| `OpenNavigationAsync()` | Opens the navigation drawer (the surface a tablet rail toggle and a phone hamburger open). A no-op on a desktop layout, in `BottomNavigation` mode and while it is already open. |
| `CloseNavigationAsync()` | Closes it; the focus scope inside hands the focus back to the opener. Also the way a host `Sidebar` slot dismisses the phone drawer from its own control (the frame never guesses from a click). |

```razor
@* A custom bar with its own opener: the frame keeps the drawer, the bar keeps the button. *@
<TmAppShell @ref="shell" TopBar="customBar" ShowTopBarMenuButton="false"> … </TmAppShell>
<button type="button" @onclick="() => shell!.OpenNavigationAsync()">Menu</button>
```

## The phone work-area sheet

`WorkArea` is a docked, resizable column on desktop and tablet and a modal **`TmSidePanel` bottom
sheet** on a phone (`Presentation="Sheet"`, `LayoutMode="Mobile"`, titled by `WorkAreaLabel`). That is
why a phone renders **no resize separator**: there is no column to drag. The sheet's close button,
Escape, its backdrop and a swipe all funnel through the panel's open callback, and the frame closes the
sheet in **every** case — a modal surface whose close does nothing would trap the user on a phone. A
host that binds `@bind-WorkAreaOpen` hears about the close; a host that does not still gets a
dismissable sheet, and its own `WorkAreaOpen` value decides again the next time it changes.

**Re-opening a dismissed sheet without a handler**: a host that keeps `WorkAreaOpen="true"` and never
binds `WorkAreaOpenChanged` cannot bring the sheet back by leaving the value alone — the frame
remembers the dismissal until the parameter actually **transitions**, so the host sets `WorkAreaOpen`
to `false` and then `true` again. Binding the parameter (the recommended shape) removes that
round-trip: the close comes back as `false` and the host re-opens with `true`.

```razor
@* The recommended shape: the host owns the flag, the frame reports the close. *@
<TmAppShell WorkArea="sources" @bind-WorkAreaOpen="showSources" … />
```

## Accessibility notes

- One polite live region is not needed: nothing in the frame announces on its own, and the transient
  surfaces (the overlay drawer, the work-area sheet) carry their own dialog semantics. There is no
  second announcement of the same state.
- Focus-visible: every interactive part (skip link, rail toggle, hamburger, bottom-bar entries, close
  buttons, separator, `main`) paints `--tm-focus-ring`, and under `forced-colors: active` it paints a
  2 px `Highlight` ring. The separator's hover / dragging highlight also switches to the system
  `Highlight` colour there: the surface tokens collapse to `Canvas` in High Contrast, so the strip
  under the pointer would otherwise be invisible. The frame's ring rules keep
  `outline: 2px solid transparent` so the system colour paints under forced colors.
- The skip link is hidden with `clip-path: inset(50%)` rather than an off-frame transform, so nothing
  paints over a host page above an embedded shell (see *Skip link*).
- Coarse pointers get 44 px targets (`--tm-touch-target`) on the rail toggle, the hamburger, the
  work-area close button and the bottom-bar entries, plus the widened separator hit area.
- Safe-area insets: the top bar pads for `env(safe-area-inset-top)`, the bottom bar and the drawer's
  navigation pad for `safe-area-inset-bottom` / `-left` / `-right`, so a notch or a home indicator
  never covers the phone chrome.
- No new transition or animation is declared by the frame's stylesheet, so the reduced-motion block
  needs no entry; the overlay, the sheet and the rail transitions it inherits are already covered by
  `TmDrawer` / `TmSidebar` / `TmBottomNavigation` (all three subjects are enumerated in
  `animations.css`). A motion the frame ever adds has to join that block.

## Hosting notes

- The shell fills its parent's height (`block-size: 100%`); give it a height (`100dvh`, a grid row or
  the demo's resizable box) so the navigation column and the work area can stretch.
- Two shells on one page: set `TestIdPrefix` so the generated `data-testid`s stay unique, and
  `SkipLinkTarget` so the two `main` ids cannot collide.
- `PersistKey` must differ per shell — one key per frame.
- A phone host gives the frame the navigation model, not a hamburger: `MobileNav="Drawer"` (default)
  renders the opener itself, and `MobileNav="BottomNavigation"` needs no opener at all. A custom
  `TopBar` that already carries its own opener passes `ShowTopBarMenuButton="false"` and calls
  `OpenNavigationAsync()`.

## See also

- [docs/sidebar-and-topbar.md](sidebar-and-topbar.md) — `IsLandmark` and the other additive parameters
- [docs/touch-interaction.md](touch-interaction.md) — the shared `pointer-drag.js` / `pointer-gesture.js` contract
- [docs/editor-shell.md](editor-shell.md) — the other container-owned shell (`TmEditorShell`); it uses the same resize helper
