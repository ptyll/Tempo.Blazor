# TmUserMenu

The account menu of an application shell: the avatar trigger, the signed-in user card, the host's
entries plus the built-in profile / settings / completion / theme / sign-out lines, and an extra slot
for a language or workspace switcher. It is an APG **menu button** whose popup is the core
`TmOverlayPanel` with `Role="menu"`, so the whole keyboard contract comes from the shared menu engine
instead of a second implementation.

- Source: `src/Tempo.Blazor/Components/Layout/TmUserMenu.razor`
- Styles: `wwwroot/css/components/_user-menu.css`
- Models: `Tempo.Blazor.Models.UserMenuItem` (`Id`, `Label`, `Icon`, `Href`, `OnClick`, `IsDanger`,
  `Separator`), `Tempo.Blazor.Interfaces.IUserInfo`
- Demo: `/app-shell` (plan phase 6)
- Tests: `tests/Tempo.Blazor.Tests/Components/Layout/TmUserMenuTests.cs`

## Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `User` | `IUserInfo?` | `null` | Avatar + display name on the trigger, account card in the popup. Null renders no card. |
| `ShowUserName` | `bool` | `true` | Print the display name next to the avatar on the trigger. |
| `Items` | `IReadOnlyList<UserMenuItem>?` | `null` | Host entries (`Href` = link, `OnClick` = command, `Separator`, `IsDanger`). Give each a unique `Id` — it is the `data-testid` suffix. |
| `ProfileHref` / `SettingsHref` | `string?` | `null` | Turn on the built-in localized Profile / Settings entries. |
| `ProfileCompletion` | `int?` | `null` | 0–100 (clamped) → `TmProgressBar` + "Profile 40% complete". |
| `ProfileCompletionHref` | `string?` | `null` | Target of the "Complete your profile" entry; with `OnProfileCompletion` too, the link wins. |
| `OnProfileCompletion` | `EventCallback` | — | Command form of the completion action. |
| `ShowThemeToggle` | `bool` | `false` | `menuitemcheckbox` bound to the ambient `ThemeService`. |
| `ExtraContent` | `RenderFragment?` | `null` | Slot in a named `role="group"` above the sign out — every element in it must be a menu item (see [Extra slot](#extra-slot)). |
| `OnItemSelected` | `EventCallback<UserMenuItem>` | — | Raised AFTER the awaited `UserMenuItem.OnClick`. |
| `OnLogout` | `EventCallback` | — | Turns on the built-in sign out; the host owns the auth flow. |
| `AriaLabel` | `string?` | `null` → localized | Accessible name of the trigger and the popup (`"{name} — user menu"` while the name is visible). |
| `MobilePresentation` | `PanelPresentation` | `Auto` | Sheet on a mobile viewport, anchored popover otherwise. |
| `Class`, `AdditionalAttributes` | | | Forwarded to the root `<div>`. |

## Accessible name

The trigger is named `"{display name} — user menu"` while the display name is visible, and the plain
localized "User menu" when the trigger is avatar-only. The visible text has to appear in the
accessible name (WCAG 2.5.3 Label in Name): a speech-input user says what they see, so a button
labelled only "User menu" next to the text "Jane Doe" would not answer to "Jane Doe". The popup keeps
the plain localized name — it is the menu, not the account. A host `AriaLabel` wins for both, so a
host that overrides it owns that contract.

## Keyboard and focus

The trigger is a native `<button>`: Enter and Space activate it exactly once, the component does not
emulate them. ArrowDown opens the menu onto its **first** entry, ArrowUp onto its **last**
(`MenuNavInterop.FocusLastAsync`), a click onto the first. Inside the open menu the shared
`tm-menu-nav.js` engine — attached by `TmOverlayPanel` because the panel carries `role="menu"` —
provides ArrowDown/ArrowUp with wraparound, Home/End, typeahead over the entries' labels (diacritics
and case folded, so `cs`/`fr` labels match) and the roving tabindex, and Tab that leaves the menu and
its trigger dismisses it. Escape and an outside pointerdown are overlay.js dismissals: the panel
closes and overlay.js returns focus to the trigger itself, so this component never focuses twice. A
chosen entry destroys the element that owns the focus, so that path hands it back to the trigger —
except in the mobile sheet, where `TmDrawer`'s restore target already does it.

Arrow key presses accept the page scroll they cause, the same trade-off `TmTabs`, `TmRadioGroup` and
the top bar's account menu document: a render-time `@onkeydown:preventDefault` cannot be per-key and
would swallow Tab and the entries' own Enter/Space.

## Structure of the popup

```html
<div role="menu" aria-label="User menu">
  <div role="group" aria-label="Signed-in account">        <!-- avatar, name, e-mail -->
  <div role="group" aria-label="Profile completion">       <!-- progressbar + text -->
  <a role="menuitem" tabindex="-1" href="/profile">Profile</a>
  <div role="separator"></div>
  <button role="menuitem" tabindex="-1">Billing</button>
  <a role="menuitem" tabindex="-1" href="/profile/edit">Complete your profile</a>
  <button role="menuitemcheckbox" aria-checked="true" data-checked="true">Dark theme</button>
  <div role="group" aria-label="Account options">          <!-- ExtraContent -->
  <button role="menuitem" tabindex="-1">Sign out</button>
</div>
```

A `role="menu"` may only own `menuitem` / `menuitemcheckbox` / `menuitemradio` / `separator` /
`group` children (axe `aria-required-children`), which is why the card, the completion block and the
extra slot are groups. The completion **call to action is a menu item**, not a plain button: a control
inside a menu that is not a menu item is unreachable in menu interaction mode. Exactly one separator
sits between two non-empty sections; a host list that starts or ends with a separator does not double
it, and consecutive ones collapse. Every entry is `tabindex="-1"` in markup — the engine moves the
single tab stop with the focus ring.

The theme switch keeps the **same** icon in both states: the label is "Dark theme", so the trailing
checkmark reports the state and a sun icon next to a checked "Dark theme" row would be a second,
contradicting signal.

## Host entries and their ids

Give every entry in `Items` a unique `Id` (the constructor's first argument). The id is what the
entry's `data-testid` is built from (`user-menu-item-{Id}`), so two entries that share one — or both
leave the default empty string — render fine, but host tests and E2E selectors can no longer tell
them apart. Separators need no id at all.

Rows are keyed by position, not by that id: `item-{n}-{Id}` for a host entry, `host-sep-{n}` for a
host separator and `section-sep-{n}` for a built-in one. All rows are **siblings** under one body, and
a duplicate `@key` among siblings is a render exception that takes the whole host shell down, not a
cosmetic slip — hence three non-overlapping key shapes. Two id-less entries, two default separators
and a separator whose id happens to be a number (which used to collide with a built-in separator key)
all render safely.

## Extra slot

`ExtraContent` renders inside the named `role="group"` "Account options" above the sign out — a
language or workspace switcher. The slot sits **inside menu interaction mode**, and that mode has one
rule: every interactive element in it must be a menu item — `role="menuitem"`, `menuitemradio` or
`menuitemcheckbox` with `tabindex="-1"`. Arrow keys only move between those roles and Tab dismisses
the menu, so a plain `<button>`, a `<select>` (`TmSelect` included) or a link without a menu role is
visible but unreachable by keyboard (WCAG 2.1.1). Nothing focusable may sit between two entries
either.

```razor
<TmUserMenu User="currentUser" OnLogout="HandleLogout">
    <ExtraContent>
        @foreach (var culture in Cultures)
        {
            <button type="button"
                    role="menuitemradio"
                    tabindex="-1"
                    aria-checked="@(culture.Code == currentCulture ? "true" : "false")"
                    @onclick="() => SetCultureAsync(culture.Code)">
                @culture.DisplayName
            </button>
        }
    </ExtraContent>
</TmUserMenu>
```

The slot adds no focus stops of its own: the engine finds its entries because they carry the menu
roles, and `aria-checked` on a `menuitemradio` is the state signal — the same pattern the built-in
theme switch uses. The demo's `ExtraContent` slot gets an axe scan plus a keyboard walk in the phase 6
E2E lane.

## Choosing an entry

A `UserMenuItem.OnClick` is a plain `Func<Task>` invoked by this component, so it does **not**
re-render the host on its own (Blazor only re-renders the component that received the event). Use
`OnItemSelected` — an `EventCallback` raised after the awaited command — for anything the host has to
show, and call `StateHasChanged()` yourself only when you deliberately keep a host-side delegate.
The menu closes before either runs, `IsDanger` is a colour cue only (the label must say what it does),
and a `Href` entry renders as a real link so middle-click and "open in new tab" keep working — an
entry that carries **both** `Href` and `OnClick` navigates and does not invoke the callback, so one
activation is one action (the same rule as `UserMenuItem`'s own contract).

## Theme switch

`ShowThemeToggle="true"` renders a `menuitemcheckbox` whose `aria-checked` follows
`ThemeService.IsDark`; the component subscribes to `ThemeService.OnChanged` and re-renders on a theme
change from anywhere. Activating the switch does **not** close the menu — it reports a state change,
which the reader can hear. Without a registered `ThemeService` (a host that does not call
`AddTempoBlazor()`) the entry is not rendered at all: there would be nothing to report or switch.

## Mobile

`MobilePresentation="Auto"` (the default) presents the same markup as a content-height bottom sheet on
a mobile viewport: the sheet header is titled with the localized "User menu", `TmDrawer` moves focus
to the first entry and restores it to the trigger on close. Forcing `Sheet` or `Popover` renders that
presentation without measuring, which is what the unit tests pin. The layout resolves from the
viewport scope, never from this component's own width — a menu is an overlay, not a container.

## Tokens

`_user-menu.css` reads only semantic tokens (`--tm-bg-surface`, `--tm-color-surface-hover`,
`--tm-text-primary/secondary`, `--tm-border-color`, `--tm-color-danger`, `--tm-focus-ring`,
`--tm-shadow-popover`, `--tm-touch-target`, `--tm-radius-*`, `--tm-space-*`,
`--tm-font-size-*`), so the dark theme needs no block of its own. The popover caps its height at the
room on the resolved side (`ConstrainHeight`) and scrolls the list — it has no tooltip or flyout child
that a scroller could clip. Every focus stop paints the
two-layer ring with `outline: 2px solid transparent` (painted as the system `Highlight` under
`forced-colors`, where the ring's box-shadow is suppressed) and every command reaches
`--tm-touch-target` on a coarse pointer. The two transitions are listed in the reduced-motion block of
`animations.css`.

The destructive line is the one colour pair that is not self-evidently safe, so it is measured rather
than assumed. The resting `--tm-color-danger` is 4.83:1 on the light popover surface (`#dc2626` on
white) and 5.29:1 on the dark one (`#f87171` on `#1e293b`) — both AA. On hover the label becomes
`--tm-color-danger-hover` over `--tm-color-surface-hover`: **5.88:1** in the light theme (`#b91c1c` on
`#f3f4f6`) and **5.46:1** in the dark theme (`#fca5a5` on `#334155`), still above 4.5:1 — which is
why the hover keeps that token. Keeping the resting colour on hover instead would drop the same pair
to 4.39:1 (light) and 3.74:1 (dark). The phase 6 screenshot and axe contrast scan re-measures both
themes, so a token change cannot silently take it under AA.
