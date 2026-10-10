# Editor shell

`TmEditorShell` is the responsive frame for **canvas editors** — a toolbox on the left, a canvas in
the middle, an inspector on the right — and `TmSidePanel` is the standalone inspector for
everything that is not an editor (a Scheduler event detail, a Gantt task panel). They are generic
primitives: nothing in them knows about e-mail, diagrams or PDFs. An editor package brings its own
toolbox, canvas and inspector as slot content and gets the responsive behaviour for free.

Read [responsive-conventions.md](responsive-conventions.md) first: the shell follows its contract
(the shell owns `data-layout`, resolves from its **container**, and composes `TmDrawer` /
`TmMobileActionBar` instead of copying sheet, gesture, focus or top-layer logic).

## What each layout renders

| Layout (container width, default thresholds) | Panels | Canvas | Actions |
|---|---|---|---|
| **Desktop** (≥ 1024) | Docked, resizable columns. A strip toggle per panel hides it, collapses it to a 44px rail, or expands it again. | Fills the remaining width. | Toolbar slot. |
| **Tablet** (640–1023) | The **same docked panels**: `CollapsedPanels` is honoured, and **at most one** panel is expanded — expanding (or opening) one rails the other and raises `CollapsedPanelsChanged`. **No modal, nothing inert.** | Always visible and operable. | Toolbar slot. |
| **Mobile** (< 640) | `MobilePanelPresentation.Sheet` (default): the canvas plus one **inline, non-modal bottom sheet** (snaps 50 % / 100 %); with two open panels the sheet shows a Left / Right tab strip. `MobilePanelPresentation.Tabs`: a full-region `tablist` **Left \| Canvas \| Right**, no sheet. | Keeps the pointer and its own scroll outside the sheet panel. | `MobileActions` through `TmMobileActionBar`; when set, the **Toolbar slot is not rendered** on mobile (the actions replace it). |

`LeftOpen` / `RightOpen` mean the same on desktop and tablet (hidden vs shown); on tablet a panel
that is shown but not the one expanded reads as a rail. The mobile sheet is inline on purpose
(decision F6-SHELL-MOBILE-SHEET-INLINE): it is positioned against the shell's stage, **not** the
viewport, so it is not promoted to the top layer and the canvas outside the sheet panel stays
usable (the inline drawer root is click-through).

## Using it

```razor
<TmEditorShell LeftTitle="@Loc["Blocks"]"
               RightTitle="@Loc["Properties"]"
               @bind-LeftOpen="leftOpen"
               @bind-RightOpen="rightOpen"
               @bind-CollapsedPanels="collapsed"
               @bind-ActiveMobilePanel="mobilePanel"
               @bind-MobileSheetSnapIndex="snap"
               Breakpoints="new TmLayoutBreakpoints(768, 1200)"
               MobileActions="mobileActions"
               PersistWidthsKey="my-editor">
    <Header>…title, breadcrumb, saved state…</Header>
    <Toolbar>…undo / redo / preview / save…</Toolbar>
    <Left>…toolbox…</Left>
    <LeftRail>…icon strip of the toolbox (tablet / collapsed)…</LeftRail>
    <Canvas>…the editing surface…</Canvas>
    <Right>…inspector…</Right>
    <RightRail>…optional icon strip of the inspector…</RightRail>
    <StatusBar>…zoom, selection…</StatusBar>
</TmEditorShell>
```

Give the shell a **definite height** (a flex child, a grid row or an explicit `block-size`): it
fills the box it is given and scrolls the canvas and panels inside it. The mobile stage keeps a
15rem floor, so an auto-height host never collapses it to nothing.

### State is the host's

`LeftOpen`, `RightOpen`, `CollapsedPanels`, `ActiveMobilePanel`, `MobileSheetSnapIndex`, `LeftWidth`
and `RightWidth` are two-way. **Every** state change — a strip toggle, the sheet's close button,
Escape, a swipe, a separator drag — raises the matching `…Changed` callback; the shell never keeps
a private flag that could disagree with the host.

* hidden = `LeftOpen=false`; collapsed rail = `LeftOpen=true` + `CollapsedPanels` has the flag;
  expanded otherwise. Desktop and tablet are the same states.
* The tablet "at most one expanded" rule **writes into the host-bound `CollapsedPanels`**: when two
  panels would be expanded the shell rails the other one and raises `CollapsedPanelsChanged`. The
  shell also applies the swap itself, so a rail it imposed can be expanded even when the host binds
  nothing or ignores the callback. Host state stays the truth: after a tablet → desktop rotation the
  railed panel is still a rail (the host-bound `CollapsedPanels` kept the flag) — that is intended;
  the user expands it again.
* `ActiveMobilePanel` is the selected mobile tab: `Left`/`Right` in the sheet, and in the Tabs
  presentation `None` is the **canvas** tab. The sheet treats `Left` | `Right` as `None` when that
  panel is not open or has no content (it falls back to the one open panel), and raises
  `ActiveMobilePanelChanged` when the effective tab moves because the host closed a panel. A host switches to the properties when a block is
  selected by setting `ActiveMobilePanel="EditorShellPanel.Right"`; it collapses the sheet on an
  "add block" action by setting `MobileSheetSnapIndex` (0 = half height). A layout flip resets the
  snap to 0 and raises the callback.
* `CanvasTitle` labels the canvas tab of the Tabs presentation (localized default).
* On a closed mobile sheet a labelled bar names what is behind it ("Blocks · Properties", from
  `LeftTitle` / `RightTitle`).

### Layout thresholds (`Breakpoints`)

The shell resolves from its own container with `TmLayoutBreakpoints.Default` (mobile below 640,
desktop from 1024 — the shared `TmBreakpoints` pair). An editor whose content needs other steps
passes `Breakpoints="new TmLayoutBreakpoints(768, 1200)"` (desktop ≥ 1200, tablet 768–1199, mobile
< 768, half-open like `TmBreakpoints.Classify`). The same parameter exists on `TmLayoutObserver`;
only the markup branch moves — structural CSS keeps the shared literals. Changing the pair at
runtime re-registers the observer, so the same width is re-classified.

The shell **forces its mobile-only children to its resolved mode**: the internal `TmMobileActionBar`
receives `LayoutMode=Mobile`, so a 700px container is mobile for both the Toolbar slot (hidden) and
the bar (shown) under `Breakpoints(768, 1200)`. Host content that nests its **own** `Auto`
`TmLayoutObserver` inside a custom-threshold shell re-measures with the default thresholds — read
the cascaded `TmLayoutContext` (the shell cascades its resolved mode) instead of observing again.
**Never set `Breakpoints` on the viewport-scope observer** (`IsViewportScope="true"`): the overlays
that read the viewport scope classify it with the defaults, so the observer throws on that
combination. Per-instance thresholds belong to container observers.

### Resizing and persistence

Each **expanded docked** panel (desktop and tablet) has a resize separator on its inner edge:
`role="separator"`, `aria-orientation="vertical"`, `aria-valuenow/min/max` in pixels and a localized
label ("Resize left panel"). Drag it (pointer capture; Escape cancels) or use the keyboard:
ArrowLeft/Right ±16px, Shift ±64px, Home = minimum, End = maximum. The width is clamped to
`MinLeftWidth` / `MaxLeftWidth` (200 / 480) and `MinRightWidth` / `MaxRightWidth` (240 / 560) **and**
never takes the canvas below `MinCanvasWidth` (400). A resize raises `LeftWidthChanged` /
`RightWidthChanged` with the new `"NNNpx"`. `aria-valuemax` is the width the user can really reach
(the configured maximum, capped by what the canvas can give up above `MinCanvasWidth`); under a
coarse pointer an invisible 24px hit area widens the 6px strip.

`PersistWidthsKey` stores **only widths the user resized**, as `{"left":{"w":300,"base":"280px"}}`
in `localStorage`. Precedence: a stored width applies on load only while the host still supplies the
same `base` width and the value is a number inside the min/max range (raw strings are never put in
a style); an explicit `LeftWidth` / `RightWidth` the host sets **after** a user resize wins and
clears the stored entry (a bound `@bind-LeftWidth` echoing the user's value is not an override).
A restore counts as a user value: a restored width is reported through `LeftWidthChanged` /
`RightWidthChanged`. A `PersistWidthsKey` set after the first render, or changed later, is loaded when
it changes; resizing back to the host width removes the stored override. For a non-pixel host width
(`20rem`, `%`) the keyboard steps from the width the panel actually renders at.
Storage errors, prerender and disposal are swallowed. Persisting `CollapsedPanels` is the host's job.

### Keyboard and focus

* Escape closes the mobile sheet (the shell leaves Escape to `TmDrawer`'s focus scope); desktop and
  tablet have no overlay, so there is nothing to dismiss.
* After a state change that replaces a trigger (a strip toggle collapses to a rail, the mobile
  sheet closes) the shell re-asserts focus on the new trigger **only if focus was lost** — on
  `<body>`, on a detached element, or inside the closing surface (`focusIfLost` in
  `tm-focus-trap.js`). A focus the user or the host placed elsewhere is never stolen.
* Toggles carry `aria-expanded`; `aria-controls` names the real panel and is **omitted** when the
  target is not rendered. A rail's expand button reads `aria-expanded="false"`. The mobile tabs use
  `role="tablist"` / `tab` / `tabpanel` (each tab `aria-controls` its panel) with arrow-key roving;
  the inline sheet and the docked asides carry an accessible name.
* Focus is never dropped on `<body>` when the shell itself removes or hides the focused element:
  reopening from the closed "Blocks · Properties" bar moves focus to the selected sheet tab (or the
  heading when one panel is open); a host-driven `ActiveMobilePanel` switch in the Tabs presentation
  moves focus to the new tab only when it was inside the hidden panel; a desktop → tablet flip moves
  focus to the rail button only when it was inside the panel that got railed. All of it goes through
  `focusIfLost`, so a focus on a host button or the canvas stays put. Escape closes the sheet that
  holds focus (non-modal traps close by focus ownership, not by registration order — two inline
  sheets on one page both work).
* A viewport flip resets armed focus restores and the sheet snap, so a closed panel never reopens
  by itself.

## Embedding checklist for editor packages

1. **Slots, not forks.** Put your toolbox/canvas/inspector in `Left` / `Canvas` / `Right`, a toolbox
   icon strip in `LeftRail`. Do not add editor-specific parameters to the shell — if you need one,
   it is a new slot or a `TmSidePanel`.
2. **Mobile actions.** Map the 3–5 editor actions people need on a phone to `TmActionItem`s
   (`Priority` is only the overflow rank). Editors are *view + light edit* on mobile (plan decision
   G2): do not ship the heavy toolbar. The `Toolbar` slot is skipped on mobile when `MobileActions`
   is set; slot content receives the shell's cascaded `TmLayoutContext` so a host can adapt.
3. **Canvas keeps the pointer.** The canvas region scrolls; do not move canvas gestures (pan, zoom,
   marquee) into the sheet. Opening a panel must never reset the canvas state.
4. **Nothing is inert.** Desktop/tablet panels are docked and the mobile sheet is non-modal, so a
   minimap or undo/redo in the canvas stays operable while a panel is open.
5. **Test with a forced mode.** `LayoutMode="TmLayoutMode.Tablet"` renders a layout with no DOM
   measurement — bUnit can assert each layout. E2E the AUTO shell at a size that resolves each
   layout (1280×800 → ~886px container resolves tablet with the default thresholds).
6. **E2E viewport shots**, never page-height stretches: 1440, 1280, 1024, 768, 390 and 320 (touch).

## Per-editor mapping

| Editor | `Left` | `Canvas` | `Right` | Mobile actions |
|---|---|---|---|---|
| **DiagramEditor** | Stencil palette | SVG canvas (pan/zoom owned by the canvas) | Format / Arrange / Layers | Undo, Redo, Fit, Layers; *More*: export, layout |
| **Wireframe** | Component palette | Page canvas | Properties, outline | Undo, Redo, Preview; *More*: export |
| **Signing designer** | Field types | Document page + overlays | Field properties, signers | Undo, Redo, Preview, Send |
| **PdfAnnotator** | Annotation tools / thumbnails | PDF page | Annotation list, comments | Prev/Next page, Undo, Save |
| **Modeling** | Notation palette | Model diagram | Element properties, relationships | Undo, Redo, Validate; *More*: view switcher |
| **E-mail template editor** (plan da0a2531) | Blocks | E-mail canvas | Properties | see below |

### The e-mail editor mapping (Bloky / Obsah / Vlastnosti)

Decision `F6-EDITOR-SHELL-EMAIL-API` records the real mapping, which needs **no shell amendments**:

* **Desktop** (≥ 1200 via `Breakpoints="new TmLayoutBreakpoints(768, 1200)"`): Bloky (left) ·
  e-mail canvas · Vlastnosti (right), both resizable and persisted.
* **Tablet** (768–1199): the same docked panels, one expanded. The toolbox is the 44px rail
  (`CollapsedPanels="EditorShellPanel.Left"` + `LeftRail` with the block icons); the inspector stays
  expanded. Expanding Bloky rails Vlastnosti.
* **Mobile** (< 768) with the default `Sheet` presentation: the e-mail canvas plus a bottom sheet
  with *Bloky* / *Vlastnosti* tabs. Selecting a block sets `ActiveMobilePanel` to `Right`
  (Vlastnosti); "add block" sets `MobileSheetSnapIndex` to collapse the sheet. Use
  `MobilePanelPresentation.Tabs` for a full-region *Bloky | Obsah | Vlastnosti* tablist instead
  (`CanvasTitle="Obsah"`).
* Mobile actions: Undo, Redo, Preview, Validate, Save through `MobileActions`.

## `TmSidePanel` — the inspector for non-editors

```razor
<TmSidePanel @bind-Open="detailOpen" Title="@Loc["EventDetail"]" Width="360px"
             Side="SidePanelSide.Right" RestoreFocusTargetId="open-detail">
    <HeaderActions><TmButton OnClick="Delete">…</TmButton></HeaderActions>
    <ChildContent>…detail…</ChildContent>
    <FooterContent><TmButton OnClick="Apply">…</TmButton></FooterContent>
</TmSidePanel>
```

`Presentation` is `Auto` (default), `Docked` or `Sheet`. `Auto` docks in-flow (a grid column or a
flex child of your layout) when its **container** resolves Desktop (≥ 1024 by default) and otherwise
becomes a modal sheet. The sheet's geometry follows the **viewport** scope
(`TmLayoutScopes.Viewport`): a side sheet from the `Side` edge (`SidePanelSide.Right` default,
`Left` for a navigation / task panel) unless the viewport resolves Mobile, which gives a bottom
sheet. With no viewport scope the container decides; a forced `LayoutMode` wins over both. A
`FooterContent` slot hosts apply / cancel actions (sticky in the sheet), the docked `<aside>` is
named by its title (a localized default when there is none), and `HeaderActions` keep a gap from
the title. Pass `RestoreFocusTargetId` (the opener's id) so the sheet returns focus to the opener on
every close path.

Unlike the shell's mobile sheet, a side-panel sheet **is** modal and promoted: it is a transient
detail that should not compete with the content behind it. Scheduler (`SidePanel`,
`EventDetailTemplate`) and Gantt (task panel) are the intended first consumers.