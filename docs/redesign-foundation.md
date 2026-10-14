# Redesign foundation (2.9.1)

The redesign of the heavy components (data table, scheduler, gantt, dashboard, the
editors) stands on one shared foundation: semantic tokens and an opt-in theme, one
responsive contract, one sheet, one focus scope, one overlay engine, one overflow
toolbar, one mobile action bar, one editor shell, one pointer gesture, and one
plural/relative-time implementation. New parameters are opt-in — a parameter's default
is today's behaviour — except for the breaking and migration entries listed in the
CHANGELOG; everything lives in `Tempo.Blazor` or `Tempo.Blazor.Abstractions`. No logic specific to one component belongs in core.

This page is the map. Each contract has its own document; read that one before
building on the primitive. The `CHANGELOG.md` heading `2.9.1 (unreleased)` lists the
breaking and migration entries this foundation introduced.

## Status of the foundation

Shipped on `main` (plan `faf503eb`, phases F0–F8). The component plans that were
waiting on these gates can start; what each phase unblocked is in
[What this unblocks](#what-this-unblocks).

Recorded follow-ups that are **not** part of the foundation and are **not** done:

| Follow-up | Owner |
| --- | --- |
| Move the NotionEditor nested-dark aliases into the token files | Notion plan (Fáze 2 tokenizace) |
| The dashboard grid (`.tm-dashboard-grid-container`, not the `.tm-dashboard` root) is a size container, and its observer is deliberately `IsContainer="false"` | Dashboard plan (the shrink-to-fit rule for a size container is in [responsive-conventions.md](responsive-conventions.md)) |
| The private pointer gestures listed in [touch-interaction.md](touch-interaction.md) migrate onto `pointer-drag.js` | each owner's plan |
| Scheduler event resize has no keyboard alternative yet; new adopters use `attachKeyboardMove` | Scheduler plan |
| `TmDocumentEditorToolbar` keeps its own overflow mechanism; core exposes `TmToolbar.OverflowPresentation="Popover"` for selection-keeping editor toolbars | DocumentEditor plan (DE-1.2) |
| `TmDiagramEditor` renders its toolbar buttons outside `TmToolbar` | DiagramEditor plan |
| Extension-package dialogs still paint in their z-index band (rule 9 of [overlays.md](overlays.md#rules)) | each package's plan |

## Where to read what

| You are building… | Read |
| --- | --- |
| anything that changes with width or pointer type | [responsive-conventions.md](responsive-conventions.md) |
| a bottom sheet or a mobile dialog | [bottom-sheet.md](bottom-sheet.md) and [overlays.md](overlays.md) |
| a modal that traps focus | [focus-scope-migration.md](focus-scope-migration.md) |
| an editor with panels | [editor-shell.md](editor-shell.md) |
| a toolbar or a "More" menu | [toolbar.md](toolbar.md) |
| a drag | [touch-interaction.md](touch-interaction.md) |
| a count or a "5 min ago" stamp | [localization-plurals.md](localization-plurals.md) |
| a segmented control | [segmented-control.md](segmented-control.md) |
| a clickable non-button | [keyboard-activation-convention.md](keyboard-activation-convention.md) |
| new CSS | [css-token-aliases.md](css-token-aliases.md) and [scoped-css-ownership.md](scoped-css-ownership.md) |

## F0 — Tokens and the indigo theme

New semantic tokens live in `src/Tempo.Blazor/wwwroot/css/tokens.css` with an identical
re-declaration in `tokens-dark.css` (the contrast test requires the closure: a `var()`
alias that reaches a themed token must be re-declared in the dark file). The list and
the indigo opt-in are in [css-token-aliases.md](css-token-aliases.md#tokens-added-for-the-redesign).

- **Opt-in only.** `:root[data-tm-theme="indigo"]` on the `<html>` element. Nothing
  changes for a host that does not set it.
- **No new `:root` custom properties in a component stylesheet.** A token goes in
  `tokens.css`.
- **The strict audit may only shrink.** `scripts/audit-css-strict.mjs` against
  `scripts/css-token-baseline.json`, ceiling in `CssTokenAuditTests`. New CSS must add
  zero violations: no hex, no undefined `--tm-*` alias. `ScopedCssOwnershipTests` has
  its own shrink-only ceiling ([scoped-css-ownership.md](scoped-css-ownership.md)).

## F1 — Layout

`TmLayoutMode` (`Auto`, `Desktop`, `Tablet`, `Mobile`), `TmBreakpoints`
(`Sm = 640`, `Md = 768`, `Lg = 1024`, `Xl = 1280`) and `TmLayout.Resolve` live in
`Tempo.Blazor.Abstractions.Layout`. The boundaries are half-open and identical to
`TmBreakpoints.Classify`: below 640 is mobile, below 1024 is tablet, 1024 and up is
desktop.

Three rules that get copied wrong if they are only in prose:

- The Blazor-owned `data-layout` attribute on the observing root is the single
  structural source. Structural CSS keys off `[data-layout=…]`; a container query is
  for a finer step or for presentation, and a container query never styles its own
  container.
- A surface positioned against the **viewport** (a modal, a dialog, a modal bottom
  sheet, a popover or dropdown presented as a sheet, an action bar with
  `Placement="FixedViewport"`) resolves
  `TmLayout.Resolve(LayoutMode, [CascadingParameter(Name = TmLayoutScopes.Viewport)] Viewport, measuredMode, InitialMode)`
  — the third argument is a `TmLayoutMode?`, never a width.
  `TmLayoutScopes.Viewport` is `"TmViewportLayout"`. An in-container surface uses the
  nearest unnamed cascade.
- Never name a callback `LayoutModeChanged`. The observer raises
  `ResolvedLayoutChanged`.

`TmLayoutObserver` (`src/Tempo.Blazor/Components/Layout/TmLayoutObserver.razor`) owns
the measurement. Its `Breakpoints` parameter takes a `TmLayoutBreakpoints`, so a
component that lives inside app chrome can stop guessing from the viewport table — a
1280px laptop is a tablet once the sidebar is open. The full contract, the precedence
inside `TmLayout.Resolve`, and the touch utilities (`.tm-touch-target`, the
coarse-pointer 44px floor, `.tm-reveal-on-hover` / `.tm-reveal-host`, the safe-area
helpers) are in [responsive-conventions.md](responsive-conventions.md).

## F2 — Sheet, dialog, focus

- **One sheet.** `TmDrawer` with `Position="DrawerPosition.Bottom"`: grabber, snap
  points, sticky footer, safe area, Escape, focus return. Do not write a second one.
  [bottom-sheet.md](bottom-sheet.md).
- **`MobilePresentation`** (`Dialog`, `Sheet`, `Fullscreen`, `Auto`) on `TmModal` and
  `TmDialog`. `Auto` follows the viewport scope.
- **One focus scope.** `TmFocusScope` with `Active`, `InitialFocusTarget` and
  `RestoreFocus`. A modal that claims focus uses it; copying the trap is the bug
  [focus-scope-migration.md](focus-scope-migration.md) exists to prevent.

## F3 — Overlays and the top layer

`PanelPresentation` is `Popover`, `Sheet` or `Auto`. `TmOverlayPanel` defaults to
`Popover`; `TmDropdown` and `TmPopover` default to `Auto`. A surface whose anchor
keeps focus (a typeahead, a caret menu, an editor toolbar) stays `Popover` at every
width — a sheet would steal the selection.

The top-layer contract, in short, because it is the one packages keep re-implementing:

- Viewport-anchored modal roots promote themselves through `TopLayerInterop`
  (`src/Tempo.Blazor/Helpers/TopLayerInterop.cs`) into `tm-top-layer.js`. `tm-sheet.js`
  re-exports the same functions, so there is exactly one pinned-root registry.
- Top-layer order equals open order. Toast containers are pinned on top and carry
  `data-tm-inert-exempt`.
- Every promoted root resets the user-agent `[popover]` defaults with a
  **zero-specificity `:where(.tm-modal-overlay)`-style rule on its own class** —
  margin, border, padding, inset, background, color, overflow and the max size — so
  themed colours inherit and the scrim shows through. There is no global
  `:where([popover])` rule.
- A new modal surface must promote through `TopLayerInterop`. Covering one with a
  z-index from package CSS is the defect, not the fix.
- A panel that `overlay.js` opens promotes itself too, and that promotion re-raises
  pinned toasts. A date, date-time or date-range picker sets `TrapFocus`, so its
  calendar is a dialog that reaches the top layer this way and never through
  `TopLayerInterop`. A toast already on screen stays above it.

Rules 1–10 and the recorded non-promoted exceptions are in
[overlays.md](overlays.md#rules).

## F4 — Toolbar overflow

`TmToolbar` defaults to `Overflow="ToolbarOverflow.None"` — opt in with `Menu` —
and `Labels="ToolbarLabels.Auto"`. `Labels="Auto"` follows the **toolbar's own width**
(`@container tm-toolbar (width < 640px)`), not the shell's thresholds; a host with its
own thresholds passes `Labels` from the cascaded `TmLayoutContext`.

- `ToolbarButtonPriority` is `Primary` (default), `Secondary`, `OverflowOnly`,
  `Pinned`. `Pinned` is the last member and never moves into the More menu — the
  trailing Save. The values are **not ranks**; never cast them.
- The order of record is the **DOM order**. `tm-toolbar.js` reports the fit count and
  the ordered ids; C# re-sorts the registered buttons before it resolves the menu, so
  registration order never decides what overflows.
- A `data-tm-toolbar-group` (and any wrapper holding items) is measured as a group:
  the wrapper's horizontal padding and border count as fixed width, its margin and its
  own gap are not. An unmeasurable bar reports nothing — never "0 fit".
- `TmToolbar.OverflowPresentation` defaults to `Auto`. An editor toolbar that keeps a
  selection passes `Popover`.

`TmToolbar` is a size container with **zero intrinsic inline width**. A host lays it
out with `flex: 1 1 auto; min-width: 0` or `width: 100%`. The rest — the roving
tabindex, the divider rules, the migration from the DocumentEditor toolbar — is in
[toolbar.md](toolbar.md).

## F5 — Mobile action bar

`TmMobileActionBar` takes an `Items` list of `TmActionItem` models. `Placement` defaults to
`ActionBarPlacement.StickyContainer` (in flow, `position: sticky`); `FixedViewport` is
the deliberate opt-in and resolves against the viewport scope; `Inline` never sticks.
`TmActionItem.Overflow` is `Auto` (default), `Never` or `Always`, independent of
`Priority`, and `Danger` marks the destructive item. The overflow menu is the same
`ActionOverflowMenu` the toolbar uses, so the two surfaces cannot drift.
[overlays.md](overlays.md#the-action-surface-overflow-menu-f5).

## F6 — Editor shell

`TmEditorShell` is the frame an editor embeds: a top slot, a canvas, and up to two
side panels. The host owns the state (`LeftOpen`, `RightOpen`, `CollapsedPanels`,
`LeftWidth`, `RightWidth`); the shell raises changes and never stores them.

- **Tablet is docked.** The tablet layout renders the same docked panels as desktop,
  honours the collapsed rail, and expands **at most one** panel — expanding one rails
  the other. There is no modal surface on tablet: nothing is inert, no focus trap, a
  first render moves no focus.
- **`Breakpoints` is per instance.** Pass a `TmLayoutBreakpoints` when the editor sits
  inside app chrome; the shared viewport table guesses wrong there.
- **The resizer** is `pointer-drag.js` (`attach` with `threshold: 0`); the keyboard
  path is the `role="separator"` (arrows, Home, End) and both paths share the clamp.
  Widths persist through `PersistWidthsKey`.

[editor-shell.md](editor-shell.md). `TmSidePanel` is the same idea for a page that is
not an editor: `SidePanelPresentation` defaults to `Auto` (docked when its container
resolves desktop, a sheet otherwise).

## F7 — Pointer drag, plurals, relative time

### The one drag

`src/Tempo.Blazor/wwwroot/js/pointer-drag.js` exports `attach`, `attachKeyboardMove`
and `prefersReducedMotion`. New drags use it; the private gestures still to migrate
are inventoried in [touch-interaction.md](touch-interaction.md). Multi-pointer work is
the sibling `pointer-gesture.js` (`attachPinch` — two-finger pinch/pan with ctrl+wheel
zoom, `createAutoScroller` — edge auto-scroll); never a second pinch inside a package.

The import is **relative**. A bare `_content/…` string is not a module specifier:

```js
import { attach } from './pointer-drag.js';                       // from wwwroot/js/ inside core
import { attach } from '../../Tempo.Blazor/js/pointer-drag.js';    // from a split package's js/
```

A classic script has no import path. `scheduler.js` resolves the helper from
`document.currentScript.src` at load time; capture it at the top of the file, not in a
later callback.

`attachKeyboardMove(element, { onStep, selector, shift })` is the keyboard alternative
(WCAG 2.5.7). It cancels **only** the `Alt`+Arrow chord — `Alt+ArrowLeft` is the
browser Back shortcut, so the `preventDefault` has to live in the keydown listener; a
Blazor `@onkeydown:preventDefault` would swallow Tab as well — and reports
`{ key, x, y, shift }` (`ArrowLeft` is `{ x: -1, y: 0 }`). With `shift: true` it also
accepts `Alt`+Shift+Arrow (the resize alternative) and reports `shift: true` for that
chord; without the option `Alt`+Shift+Arrow stays ignored and unprevented. A canvas
editor that already nudges with plain arrows may keep doing so and not call it. The
scheduler's event resize does not have its alternative yet; that is the Scheduler
plan's, and a new adopter does not get the same exemption.

### Plurals and relative time

`Loc.Plural("Key", n)` picks the CLDR category from the UI culture and formats the
number itself. The resource family is `Key_One`, `Key_Few`, `Key_Many`, `Key_Other`
(`TmPluralCategory` has exactly these four); the lookup order is the category form,
then `Key_Other`, then the plain `Key`. [localization-plurals.md](localization-plurals.md).

`ITmRelativeTimeFormatter` (`Format`, plus a default `Absolute` that prints the
absolute stamp) is registered **scoped** by `AddTempoBlazor()`. No
component takes a time-zone parameter: the zone comes from the clock. On Blazor
Server the host registers a scoped `ITmRelativeTimeFormatter` over its **own**
`TimeProvider` that reads the circuit's zone lazily and re-renders once the zone
arrives. It never replaces the app-wide `TimeProvider` — Kestrel and cookie
authentication consume that one from singletons, and a scoped replacement fails the
host under `ValidateScopes`. The registration snippet is
[localization-plurals.md](localization-plurals.md#relative-time-itmrelativetimeformatter).

## F8 — Small additive extensions

All default to the previous behaviour.

| Extension | Default |
| --- | --- |
| `TmKeyboardShortcutsHelp.Description` | unset; the dialog names itself as before |
| `TmKeyboardShortcut.KeySeparator` | `"+"` |
| `TmCopyButton.ShowLabel` | `false` |
| `TmCommandPalette.GroupByCategory`, `SplitShortcuts` | `false` |
| `TmSignatureCapture.ClearButtonPlacement` (`TmSignatureClearButtonPlacement`) | `Actions`; `Overlay` moves the same button over the surface |
| `TmRadioGroup.Appearance` (`RadioAppearance`) | `Radio`; `Segmented` is the single-choice segmented control |
| `TmTabs.Variant` (`TabVariant`) | `Line`; `Pill` is the content-switching segmented control |

Which of the last two to use is [segmented-control.md](segmented-control.md). There is
no `TmSegmentedControl`.

## Gates

A change that builds on the foundation keeps these green:

- `TokenContrastTests` and `CssTokenAuditTests` (the strict audit; the ceiling must
  not grow).
- `ScopedCssOwnershipTests` and `TmBreakpointsTests` (a new `@container` width is a
  `TmBreakpoints` constant; a new `@media` width updates the baseline).
- `ComponentDocumentationFreshnessTests`. After touching a `[Parameter]` surface, run
  `dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- JsonDocumentation generate`
  and commit the bundles in the **same** commit (history gate N328).
- `npm run test:overlay` — the Node suite over `wwwroot/js/__tests__`: `pointer-drag.js`,
  `pointer-gesture.js`, `tm-toolbar.js`, `tm-top-layer.js`/`overlay.js`, `tm-sheet.js`,
  `tm-splitter.js`, `tm-menu-nav.js` and the scheduler/workflow/data-table gesture suites.
- The smoke E2E lane: `scripts/run-e2e-smoke.ps1` ([e2e-test-lanes.md](e2e-test-lanes.md#smoke-lane-pr-gate--20-minutes)).

## Demo

Each primitive has a reference page in `src/Tempo.Blazor.Demo.SharedUI/Pages/`:
`/responsive-conventions` (`ResponsiveConventionsPage`), the sheet on `/feedback`
(`FeedbackPage`), `/overlay` and `/overlay-no-scope` (`OverlayPage`,
`OverlayNoScopePage`), `/editor-shell` (`EditorShellPage`), `/toolbar-forms`
(`ToolbarFormsPage`), `/mobile-action-bar` (`MobileActionBarPage`). The additive
extensions have no page of their own — the demo shows them on `/layout`, `/forms`,
`/feedback` and `/signing-components`. The theme switch (light/dark, default/indigo) is the
demo's own chrome, not a page.

## What this unblocks

From the dependency matrix
(`output/imagegen/tempo-reimagined/analysis/00-cross-cutting.md`, §10.1):

| Foundation | Component plans whose gates it opens |
| --- | --- |
| F0 tokens, theme, audit | DataTable, Dashboard, Scheduler, Gantt, PdfViewer, Notion, Spreadsheet, Signing, DocumentEditor, DiagramEditor, Modeling, Wireframe; the EmailTemplates 2.10.0 theme prerequisite |
| F1 layout | every plan's desktop/visual phase; Dashboard container layout; DocumentEditor workspace |
| F2 sheet, dialog, focus | the mobile dialogs — the most common dependency (17 groups) |
| F3 overlays | anchored menus and pickers on mobile |
| F4 toolbar | Scheduler and Gantt toolbars; DocumentEditor (DE-1.2) and DiagramEditor — the core prerequisites they waited on ([toolbar.md](toolbar.md)) are in since 2.9.1 |
| F5 action bar | mobile primary actions |
| F6 editor shell | DocumentEditor, DiagramEditor, Notion, Spreadsheet, Signing and Wireframe panel frames |
| F7 pointer drag | Scheduler (pilot), then Gantt, Dashboard, DiagramEditor, Wireframe, Signing |
| F8 extensions | icon and segmented-control consumers (Spreadsheet, Signing) |
