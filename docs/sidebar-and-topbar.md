# TmSidebar and TmTopBar

The two navigation primitives an application shell is built from: a collapsible side rail and the
top bar. Both are additive — a page that renders `<TmSidebar Items="…" />` or
`<TmTopBar BrandTitle="…" />` gets the behaviour it always had. This page documents the navigation
contract they implement: named groups, the current-page marker, the icon-only rail, and the top bar's
disclosure button and account menu.

- Source: `src/Tempo.Blazor/Components/Layout/TmSidebar.razor`, `TmTopBar.razor`
- Styles: `wwwroot/css/components/_sidebar-nav.css`, `_topbar.css`
- Models: `Tempo.Blazor.Models.SidebarNavSection`, `Tempo.Blazor.Models.UserMenuItem`,
  `Tempo.Blazor.Components.Layout.SidebarNavMatchMode`
- Demo: `/layout` (the *Sidebar* and *TopBar* sections)
- Tests: `tests/Tempo.Blazor.Tests/Components/Layout/TmSidebarTests.cs`, `TmTopBarTests.cs`,
  `LayoutPageChromeTests.cs`, `tests/Tempo.Blazor.E2E/LayoutChromeE2ETests.cs`

## TmSidebar

### Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Items` | `IReadOnlyList<ISidebarNavItem>` | `[]` | One ungrouped list. Rendered first, so it combines with `Sections`. |
| `Sections` | `IReadOnlyList<SidebarNavSection>?` | `null` | Named groups, each rendered under its own heading. |
| `Collapsed` | `bool` | `false` | Icon-only state. |
| `OnToggle` | `EventCallback` | — | Raised by the header toggle. The host owns the state. |
| `NavLabel` | `string?` | `null` → localized | Accessible name of the `<nav>` landmark. |
| `AutoActive` | `bool` | `false` | Mark the item whose `Href` matches the current location. |
| `AutoActiveMatchMode` | `SidebarNavMatchMode` | `Exact` | `Exact` or `Prefix` (sub-pages, on a segment boundary). |
| `CollapseTooltips` | `bool` | `true` | Render a `TmTooltip` per collapsed item; off restores the rail's vertical scroller. |
| `Header`, `Footer` | `RenderFragment?` | `null` | Slots around the navigation. |
| `Class`, `AdditionalAttributes` | | | Forwarded to the root `<aside>`. |

`SidebarNavSection` is `Title` + `Items`; `ISidebarNavItem` supplies `Id`, `Label`, `Icon`, `Href`,
`BadgeCount`, `IsActive` and `Children`.

### Named groups

```razor
<TmSidebar Sections="sections" NavLabel="Product navigation" AutoActive="true" />
```

Each section renders

```html
<div class="tm-sidebar-section">
  <h2 class="tm-sidebar-section-title" id="tm-sidebar-section-…-0">Workspace</h2>
  <ul class="tm-sidebar-nav-list" aria-labelledby="tm-sidebar-section-…-0"> … </ul>
</div>
```

The heading is the group's visible name AND the accessible name of its list, so a screen-reader user
hears the group before its items instead of one long unlabelled list. Heading ids are per component
instance, so two rails on one page never share one. The heading is a real `h2`: keep the host page's
outline consistent (the rail is a `nav` landmark inside its `aside`).

### The current page (`aria-current="page"`)

`IsActive` on an item marks it explicitly — that behaviour is unchanged. `AutoActive` additionally
resolves the page from the `NavigationManager`:

- `Exact` (default) matches the path only: `/settings` is **not** current on `/settings/profile`.
- `Prefix` also matches a sub-page, on a segment boundary: `/settings` is current on
  `/settings/profile`, never on `/settings-old`. The root (`/`) entry stays exact in both modes —
  as a prefix it would claim every page.
- An explicit `IsActive` wins; a query string and a fragment are ignored; matching is
  case-insensitive.
- The component subscribes to `LocationChanged` only while `AutoActive` is on, so an SPA route change
  repaints the marker and the default renders no subscription.

The marker is on the `<a>` (not the `<li>`), and the `tm-sidebar-nav-item-active` class follows it for
the visual wash.

### The icon-only rail

A collapsed link is only an icon, so it always carries its label as `aria-label` — that is not
optional. With `CollapseTooltips` (the default) a `TmTooltip` shows the same text on hover and focus,
which is what a sighted user needs from an icon rail.

The badge count is part of the collapsed accessible name (`"Charts (2)"`): the badge itself is not
rendered in the icon-only state, and a count nobody can read is a lost notification.

**Tradeoff, by design:** the tooltip is an absolutely positioned box that has to escape the 3.5rem
rail, and a scroll container clips on both axes. The component therefore adds `tm-sidebar-tooltips`
while the collapsed rail shows tooltips, and the stylesheet turns the rail's clipping off — a
collapsed rail with tooltips does not scroll its overflow. A host whose collapsed navigation is
taller than the rail passes `CollapseTooltips="false"` and gets the scroller back; the accessible name
stays either way.

### Accessibility summary

- Landmark: `<nav aria-label="…">` named from `NavLabel` or the localization (`TmSidebar_NavLabel`).
- Toggle: `aria-label` from `TmSidebar_CollapseMenu` / `TmSidebar_ExpandMenu`, `:focus-visible` ring.
- Current page: `aria-current="page"` plus the system `Highlight` ring under `forced-colors`.
- Keyboard: native links and a native toggle — no key emulation, one activation per press.
- Touch: the toggle and every link reach `--tm-touch-target` (44px) on a coarse pointer.
- Motion: the width transition and the link hover are covered by the reduced-motion block.

## TmTopBar

### Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `BrandTitle`, `BrandContent` | `string?`, `RenderFragment?` | `null` | Brand area (a title or custom markup). |
| `CenterContent` | `RenderFragment?` | `null` | Centre slot (breadcrumbs, a document title). |
| `ActionsContent` | `RenderFragment?` | `null` | Action slot (notifications, theme switch). |
| `ShowMenuButton` | `bool` | `false` | Navigation disclosure button (opens the host's drawer). |
| `MenuExpanded` | `bool` | `false` | Reported as `aria-expanded`; the host owns the state. |
| `OnMenuToggle` | `EventCallback` | — | Raised by the disclosure button. |
| `MenuControlsId` | `string?` | `null` | `aria-controls` target (the drawer's id). |
| `CommandPaletteShortcut` | `string?` | `"Ctrl+K"` | Chord printed in the palette trigger; empty hides the hint. |
| `CommandPaletteKeyShortcuts` | `string?` | `"Control+K Meta+K"` | `aria-keyshortcuts` value; pass the chord the host actually binds. |
| `User` | `IUserInfo?` | `null` | Signed-in user (avatar + name + account menu). |
| `UserMenuItems` | `IReadOnlyList<UserMenuItem>?` | `null` | Host entries above the built-in sign out. |
| `UserMenuLabel` | `string?` | `null` → localized | Accessible name of the account button and menu. |
| `OnCommandPalette` | `EventCallback` | — | Palette trigger clicked (the trigger renders only with a handler). |
| `OnLogout` | `EventCallback` | — | Sign-out entry clicked. |
| `Class`, `AdditionalAttributes` | | | Forwarded to the root `<header>`. |

### Disclosure button

```razor
<TmTopBar BrandTitle="Admin"
          ShowMenuButton="true"
          MenuExpanded="drawerOpen"
          MenuControlsId="app-drawer"
          OnMenuToggle="() => drawerOpen = !drawerOpen" />
```

It is a native `<button>` with a localized name (`TmTopBar_MenuButton`), `aria-expanded` from
`MenuExpanded` and `aria-controls` from `MenuControlsId` (omitted when null). The component never
toggles the drawer itself — a host that owns the drawer state (a `TmDrawer`, a responsive rail)
decides.

### Account menu

```razor
<TmTopBar User="currentUser" UserMenuItems="accountItems" OnLogout="HandleLogout" />
```

`UserMenuItem` entries render in the host's order; a `Href` entry is a link, an `OnClick` entry a
button, `Separator` a group separator, `IsDanger` the destructive style (colour is a secondary cue —
the label must be self-explanatory). The localized sign out renders last, separated from the host
entries, and only when `OnLogout` is bound. Choosing an entry closes the menu and raises its callback.

`OnClick` is a plain `Func<Task>`, not an `EventCallback` (the same shape as
`ICommandPaletteAction.Execute`): the callback is awaited and its exception surfaces, but a host that
changes its OWN state from it must ask for the render (`StateHasChanged()`) — a delegate invoked by a
child component does not re-render the parent. A callback that only calls a service needs nothing.

The dropdown is an APG **menu button**:

- trigger: `aria-haspopup="menu"`, `aria-expanded` (and `aria-label` from `UserMenuLabel`);
- container: `role="menu"` with an accessible name;
- entries: `role="menuitem"` with `tabindex="-1"` (they are not tab stops), separators
  `role="separator"` (no `aria-hidden` — the role already removes them from the accessibility tree);
- opening the menu **moves focus into it** (first entry). ArrowDown on the trigger opens it at the
  first entry, ArrowUp at the last;
- ArrowDown/ArrowUp step through the entries with wraparound, Home/End jump to the first/last entry,
  each by `ElementReference.FocusAsync` (no JS listener);
- Escape and Tab (or Shift+Tab, from ANY entry) close the menu and return focus to the trigger —
  Tab is not prevented, the browser's own move continues from the re-focused trigger;
- a click anywhere outside closes it through a transparent backdrop element and returns focus to the
  trigger (no document-level JS listener, so the component stays JS-free and the page keeps its own
  handlers for the next click).

Choosing an entry is the one close path that does not re-focus the trigger: the entry navigates or
runs a command, so there is no element left to return to that the user came from.

No key is emulated: Enter/Space on a native button and Enter on a link are the browser's own
activation. The page scroll that ArrowDown/ArrowUp/Home/End produce is **accepted**, exactly as
`TmTabs`, `TmRadioGroup` and `TmColorGradient` document: `@onkeydown:preventDefault` is a render-time
bool, so switching it on for the menu would also swallow Tab and the entries' native Enter/Space.

### Narrow bars

The brand is the flexible slot: the actions (and the account button) never shrink, so on a bar too
narrow for everything the brand title ellipsises and finally yields. A host that must always show a
mark uses `BrandContent` with a compact logo instead of a title.

### Command palette trigger

The trigger renders only when `OnCommandPalette` has a handler. It prints `CommandPaletteShortcut`
(a label, not a keybinding — the host binds the shortcut), advertises `CommandPaletteKeyShortcuts`
through `aria-keyshortcuts` (default `"Control+K Meta+K"`; null or empty omits the attribute, so a
rebound chord can never be announced with the old default), and keeps the localized name
`TmTopBar_OpenCommandPalette`.

## Localization keys

| Key | English | Used by |
|---|---|---|
| `TmSidebar_NavLabel` | Sidebar navigation | default `nav` name |
| `TmSidebar_ItemLabelWithCount` | `{0} ({1})` | collapsed accessible name with a badge |
| `TmSidebar_CollapseMenu` / `TmSidebar_ExpandMenu` | Collapse menu / Expand menu | the toggle |
| `TmTopBar_MenuButton` | Navigation menu | the disclosure button |
| `TmTopBar_SignOut` | Sign out | the account menu |
| `TmTopBar_UserMenu` | User menu | default account button/menu name |
| `TmTopBar_OpenCommandPalette` | Open command palette | the palette trigger |

All keys exist in English, Czech, Slovak and French (`TmResources{,.cs,.sk,.fr}.json`).
