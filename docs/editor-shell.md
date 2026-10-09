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

| Layout (container width) | Panels | Canvas | Actions |
|---|---|---|---|
| **Desktop** (≥ 1024) | Docked columns. A strip toggle per panel hides it, collapses it to a 44px rail, or expands it again. | Fills the remaining width. | Toolbar slot. |
| **Tablet** (640–1023) | Each open panel is a **modal side sheet** (a promoted `TmDrawer`). **At most one** shows at a time: the most recently opened wins, closing it reveals the other if that one is open too. | Always visible behind the sheet. | Toolbar slot. |
| **Mobile** (< 640) | One **inline, non-modal bottom sheet** (snaps 50 % / 100 %) anchored to the shell, listing the open panels stacked — or as `Tabs` (`MobilePanelPresentation.Tabs`, a `role="tablist"` strip such as *Blocks / Properties*). | Keeps its own scroll; reserves the first snap so the last block stays reachable. | `MobileActions` through `TmMobileActionBar` (sticky at the bottom of the shell). |

The mobile sheet is inline on purpose (decision F6-SHELL-MOBILE-SHEET-INLINE): it is positioned
against the shell, not the viewport, so it is **not** promoted to the top layer and the canvas stays
usable beside it. The tablet sheets are viewport-modal and promote themselves.

## Using it

```razor
<TmEditorShell LeftTitle="@Loc["Blocks"]"
               RightTitle="@Loc["Properties"]"
               @bind-LeftOpen="leftOpen"
               @bind-RightOpen="rightOpen"
               @bind-CollapsedPanels="collapsed"
               MobilePanelPresentation="MobilePanelPresentation.Tabs"
               MobileActions="mobileActions"
               PersistWidthsKey="my-editor">
    <Header>…title, breadcrumb, saved state…</Header>
    <Toolbar>…undo / redo / preview / save…</Toolbar>
    <Left>…toolbox…</Left>
    <Canvas>…the editing surface…</Canvas>
    <Right>…inspector…</Right>
    <StatusBar>…zoom, selection…</StatusBar>
</TmEditorShell>
```

Give the shell a **definite height** (a flex child, a grid row or an explicit `block-size`): it
fills the box it is given and scrolls the canvas and panels inside it.

### State is the host's

`LeftOpen`, `RightOpen` and `CollapsedPanels` are two-way. **Every** state change — a strip toggle,
the sheet's close button, Escape, the backdrop, a swipe — raises the matching `…Changed` callback;
the shell never keeps a private "open" flag that could disagree with the host. Bind them (or pass
the value and handle the callback) and the `aria-expanded` of every toggle is always accurate.

* hidden = `LeftOpen=false`; collapsed rail = `LeftOpen=true` + `CollapsedPanels` has the flag;
  expanded otherwise. The rail only exists on desktop; tablet and mobile ignore `CollapsedPanels`.
* On tablet and mobile a closed panel is reachable through a toggle button, so a layout never
  strands the user without a way back.

### Keyboard and focus

* Escape closes the topmost sheet (the shell leaves Escape to `TmDrawer`'s focus scope).
* Focus returns to the toggle that opened the sheet on **every** close path — Escape, the header
  close button, the backdrop, a swipe, or the host setting the open flag itself. The shell arms a
  flag and re-asserts focus from its own `OnAfterRenderAsync`, because the focus trap's restore
  can race the toggle's re-render. Copy this pattern if you add a surface of your own (the F5 action
  bar uses it too).
* Toggles carry `aria-expanded` and `aria-controls`; the mobile tabs use `role="tablist"` /
  `tab` / `tabpanel` with arrow-key roving.
* A viewport flip (mobile → desktop → mobile) resets armed focus restores and the sheet snap, so a
  closed panel never reopens by itself.

### Widths

`LeftWidth` / `RightWidth` (CSS lengths, default 280px / 320px) size the desktop columns and the
tablet sheets. `PersistWidthsKey` stores the effective widths in `localStorage` and restores them on
the next visit; leave it null to disable. Persistence is best-effort (private mode, SSR).

## Embedding checklist for editor packages

1. **Slots, not forks.** Put your toolbox/canvas/inspector in `Left` / `Canvas` / `Right`. Do not
   add editor-specific parameters to the shell — if you need one, it is a new slot or a
   `TmSidePanel`.
2. **Mobile actions.** Map the 3–5 editor actions people need on a phone to `TmActionItem`s
   (`Priority` is only the overflow rank). Everything else lives in the *More* menu. Editors are
   *view + light edit* on mobile (plan decision G2): do not ship the heavy toolbar.
3. **Canvas keeps the pointer.** The canvas region scrolls; do not move canvas gestures (pan, zoom,
   marquee) into the sheet. Opening a panel must never reset the canvas state.
4. **Inert behind modal sheets.** On tablet the sheet is modal and the canvas goes `inert`. Anything
   that must stay operable while a panel is open (a minimap, undo/redo) belongs in the `Toolbar`
   slot's mobile action items, not in the canvas.
5. **Test with a forced mode.** `LayoutMode="TmLayoutMode.Tablet"` renders a layout with no DOM
   measurement — bUnit can assert each layout; use the demo's forced-tablet section as the model.
6. **E2E viewport shots**, never page-height stretches: 1440, 1024 (touch), 390 and 320 (touch), plus
   `cs` / `fr` labels at 320 (tab labels must stay on one line).

## Per-editor mapping

| Editor | `Left` | `Canvas` | `Right` | Mobile actions |
|---|---|---|---|---|
| **DiagramEditor** | Stencil palette | SVG canvas (pan/zoom owned by the canvas) | Format / Arrange / Layers | Undo, Redo, Fit, Layers; *More*: export, layout |
| **Wireframe** | Component palette | Page canvas | Properties, outline | Undo, Redo, Preview; *More*: export |
| **Signing designer** | Field types | Document page + overlays | Field properties, signers | Undo, Redo, Preview, Send |
| **PdfAnnotator** | Annotation tools / thumbnails | PDF page | Annotation list, comments | Prev/Next page, Undo, Save |
| **Modeling** | Notation palette | Model diagram | Element properties, relationships | Undo, Redo, Validate; *More*: view switcher |
| **E-mail template editor** (plan da0a2531) | Blocks | E-mail canvas | Properties | Undo, Redo, Preview, Validate, Save — mobile uses `Tabs` (*Blocks / Properties*), tablet one side sheet |

Decision `F6-EDITOR-SHELL-EMAIL-API` records that the e-mail editor needs no shell amendments.

## `TmSidePanel` — the inspector for non-editors

```razor
<TmSidePanel @bind-Open="detailOpen" Title="@Loc["EventDetail"]" Width="360px"
             RestoreFocusTargetId="open-detail">
    <HeaderActions><TmButton OnClick="Delete">…</TmButton></HeaderActions>
    <ChildContent>…detail…</ChildContent>
</TmSidePanel>
```

`Presentation` is `Auto` (default), `Docked` or `Sheet`. `Auto` docks in-flow (a grid column or a flex
child of your layout) when its **container** is desktop, and becomes a modal right side sheet on
tablet or a modal bottom sheet on mobile. Pass `RestoreFocusTargetId` (the opener's id) so the
sheet returns focus to the opener on every close path.

Unlike the shell's mobile sheet, a side-panel sheet **is** modal and promoted: it is a transient
detail that should not compete with the content behind it. Scheduler (`SidePanel`,
`EventDetailTemplate`) and Gantt (task panel) are the intended first consumers.
