# Overlays: popover vs sheet vs modal

Three primitives cover every floating surface. Pick by **what the user is doing**, not by
component:

| Surface | Primitive | When |
|---------|-----------|------|
| Anchored panel — menu, dropdown, popup calendar, picker | `TmOverlayPanel` (`MobilePresentation`) | The content belongs to a trigger: it opens next to it, closes on Escape/outside, and returns focus to the trigger. |
| Blocking decision or form | `TmModal` / `TmDialog` (`MobilePresentation`) | The page must not be used until the user decides; the rest goes inert. |
| Revealed set of options / actions at the bottom of the screen | `TmDrawer Position=Bottom` | A host wants a sheet directly (snap points, swipe gestures) or a panel chooses it as its mobile presentation. |

A fourth case — inline disclosure (accordion, expanding section) — is not an overlay at all;
do not reach for a floating primitive.

## The anchored panel

`TmOverlayPanel` is the single positioning engine for anchored surfaces:
`popover="manual"` puts the panel in the browser top layer (no ancestor `overflow`/`transform`
can clip it), and `overlay.js` measures, flips, clamps and re-places it on scroll, resize and
`visualViewport` changes (an on-screen keyboard opening flips a panel into view instead of
letting it open under the keyboard). Escape and outside pointerdown dismiss it and restore
focus to the anchor.

### Mobile presentation

Anchored panels carry `MobilePresentation` (`PanelPresentation`):

- `Popover` — **the `TmOverlayPanel` default**: the anchored popover at every width.
- `Auto` — a content-height **bottom sheet** on a mobile viewport, the anchored
  popover otherwise. The choice resolves from the **viewport** (the `TmLayoutScopes.Viewport`
  cascade or the internal probe), because the sheet is positioned against the viewport; the
  popover's *size* still follows the **trigger's container**. `TmDropdown` and `TmPopover` keep
  `Auto` as their wrapper default; the column picker and the date/time pickers opt into `Auto`
  explicitly.
- `Sheet` — forced, renders without measurement (testable in bUnit without a DOM).

**A surface whose anchor keeps focus never presents as a sheet.** A typeahead
(`TmEntityPicker`, `TmQueryInput`), a caret menu (the Notion mention/AI menus) or an inline
toolbar owns the user's keystrokes; a modal sheet moves focus to its header and the typing is
lost. Those consumers stay on the `Popover` default — enforced by a source sweep in
`TmOverlayPanelDefaultPresentationTests`. If a combobox ever wants a sheet, it needs a
search-in-sheet design: the input moves into the sheet header and is autofocused there.

> **F4 note — editor and formatting-toolbar overflow menus.** An overflow menu opened from an
> editor or formatting toolbar counts as "the anchor keeps focus/selection": the user's text
> selection lives in the anchor surface and a modal sheet would move focus to the sheet header,
> collapsing the selection and any half-typed formatting command. Such menus stay on the
> `Popover` presentation — or, if a host genuinely needs the sheet, they must restore the
> selection when the sheet closes. Do not opt them into `Auto` in the F4 pass.

In sheet mode the panel **composes `TmDrawer Position=Bottom`** (content height, `Modal`) — it
never copies sheet, gesture or focus logic. The sheet header shows `Title` (fall back:
`AriaLabel`, then a localized generic "Menu") and a **Done** action (`Tm_Done`) that closes
without selecting; a consumer mid-task can keep Done disabled through
`TmOverlayPanel.SheetDoneEnabled` — the date-range picker disables it until BOTH dates are
picked, while Escape, the backdrop click and the swipe keep closing with the partial selection
(cancel semantics). The sheet renders **in place** — a sheet opened inside a `TmModal` stays in
the modal's DOM, stacks above the modal content, and nests inside the modal's focus trap — and
its root is **promoted to the browser top layer** (`popover="manual"` + `showPopover`), so an
ancestor stacking context (a sticky `TmTopBar` under `TmBottomNavigation`, a transformed host)
can neither confine nor cover it. The DOM order is untouched, so the nested trap and the Escape
order are unchanged. `ChildContent` wraps in `.tm-overlay-panel-sheet__content`, which carries
`Role` (never `dialog` — that role stays on the drawer's focus-scope root), `Id` and
`AdditionalAttributes`, so menu/listbox ownership (axe `aria-required-parent`) and
`aria-controls` targets survive the popover→sheet switch; menus and listboxes get their initial
focus on the first `menuitem`/`option`, not on Done.

`TmDropdown` and `TmPopover` forward `MobilePresentation`, `LayoutMode`, `InitialMode` and
`ResolvedLayoutChanged`; the dropdown titles its sheet with the trigger text. Anchoring options
(`Placement`, `Align`, `Offset`, `MatchAnchorWidth`, …) apply to the popover only.

### Modality of dialog popups

A `Role="dialog"` popup that must trap focus (calendar popups) passes
`TrapFocus="true"`: the `TmFocusScope` **is** the panel root, so Tab cycles inside, the
background goes inert, and `aria-modal="true"` is enforced by a real trap rather than promised
without one. Escape stays with `overlay.js`, which restores focus to the anchor. The trap
activates only after the popover has been shown, so the initial-focus move can never land on
`<body>`. Popups that must stay light (menus, suggestion lists) leave it off.

**Cost, stated plainly:** `TrapFocus` marks the rest of the page `inert` and locks document
scroll while the popup is open — on desktop too. The picker therefore loses the
"outside-click-into-a-field-keeps-typing" convenience: a pointerdown outside the popup closes
it and returns focus to the trigger. Do not reach for `TrapFocus` to fix stacking or scrolling;
it is a modality contract. (The column picker deliberately does **not** use it — its panel is a
light dialog; keep doc and code in agreement.)

## What packages copy today

`Tempo.Blazor.NotionEditor` **already consumes `TmOverlayPanel`** for its AI menu, mention
menu, comment-mention input and notification center — those surfaces get the engine, the top
layer, the keyboard-aware viewport read and the popover/sheet choice for free. The remaining
local clamp copies in `notion-editor.js` belong to surfaces that have not migrated yet:
`clampFixedElementToViewport` (~line 2597), `adjustSlashMenuPosition` (~2624),
`adjustTypeSwitcherPosition` (~2680), `adjustEmojiPickerPosition` (~2716),
`adjustColorPickerPosition` (~2737) and `adjustInlineToolbarPosition` (~3128). They should
become `TmOverlayPanel` consumers one surface at a time and delete their local math. Same for
`Tempo.Blazor.DocumentEditor`'s four local focus traps: migrate one dialog at a time onto
`TmFocusScope` (see [focus-scope-migration.md](focus-scope-migration.md)).

### The caret/virtual-anchor recipe

A caret menu has no element to anchor to — the position comes from the selection. Render a
**zero-size span as a virtual anchor** and move it to the caret rect (the AI menu does exactly
this: `.tm-notion-ai__anchor`, an empty span positioned at the page top-right), then pass it as
`Anchor`. The panel positions, flips, clamps and re-places against it exactly like against a
real element; such menus keep the `Popover` presentation because their anchor surface (the
editor) keeps focus.

## Rules

1. One engine per surface kind — never re-implement flip/clamp/scroll-tracking in a package.
   Packages consume `TmOverlayPanel`; they never import `overlay.js` directly (it is an
   implementation detail of the panel — today only `TmOverlayPanel` imports it).
2. New CSS for a panel positions nothing itself; the panel root gets surface styling only
   (background, border, radius, shadow), never `position`/`top`/`left`/`z-index`.
3. Every sheet is named (title or fallback) — an unnamed dialog fails axe.
4. Focus returns to the trigger on every close path (Escape, outside, Done, selection).
5. Anchored surfaces resolve the popover-vs-sheet choice from the viewport; in-container
   surfaces resolve from their container ([responsive-conventions.md](responsive-conventions.md)).
6. A surface whose anchor keeps focus stays `Popover` at every width (see
   [Mobile presentation](#mobile-presentation)).
7. Viewport-anchored overlays live in the browser **top layer** (the popover panel and the
   promoted sheet root); nothing may recreate that guarantee with a z-index arms race in
   package CSS.
8. **Top-layer order equals open order.** Every modal viewport-anchored overlay root is itself
   top-layer: a modal `TmDrawer` at any position, the `TmModal`/`TmDialog` overlay root, the
   `TmCommandPalette` backdrop, the `TmKeyboardShortcutsHelp` overlay, the `TmLightbox`
   focus-scope root, the `TmGanttImportDialog` overlay and the two toast containers
   (`TmToastContainer`, `TmNotificationToastContainer`) carry `popover="manual"` and promote
   themselves at open through the shared promote helper (`TopLayerInterop` → `tm-sheet.js`). A
   surface opened from inside one (a dialog from a sheet, a toast from a dialog) promotes *after*
   it, so it always paints above — DOM stays in place and the trap/inert/Escape order are
   untouched. A host cannot cover an open modal surface with a z-index band anymore; only another
   promoted surface can. Toast containers are **pinned on top**: they register in the helper's
   pinned-on-top registry while they hold toasts and re-raise on every push *and* after every
   promotion of another surface, so a toast that is already on screen is never covered by a
   drawer, sheet or dialog opened after it, and a toast pushed at the `MaxVisible` cap (count
   unchanged) still lands above. Render **one `TmToastContainer` per app** (in the layout); two
   containers sharing a `ToastService` both promote and every toast paints twice. Tooltips stay
   z-index painted *inside* their own surface (`TmTooltip`'s content is a DOM child of its
   trigger): a tooltip inside a promoted surface paints with it — no extra promotion — and a
   tooltip on an element behind a modal is inert with its trigger, which is the desired
   behaviour. Every promoted root resets the UA `[popover]` defaults at zero specificity
   (`:where(...)` — background transparent, color inherit, overflow visible, margin/border/padding
   zeroed), so the page behind stays dimmed through the surface's own scrim and themed colours
   inherit into the body. Two contracts ride on this rule:
   - **`data-tm-inert-exempt`** is the attribute a modal trap's inert walk skips exactly like
     `data-tm-backdrop`. Only the toast containers (`TmToastContainer`,
     `TmNotificationToastContainer`) carry it — status UI a user must reach under any modal (a
     dismiss click, an `role=alert` announcement). **Placement rule: render toast containers as
     layout-level siblings, outside `@Body` wrappers.** The walk inerts every sibling of the
     trap's ancestors; a container nested inside a `@Body` wrapper is inerted with the wrapper
     and the exemption on the container never gets to act.
   - **Non-modal anchored popovers are intentionally NOT promoted.** A dropdown, menu or picker
     popup (`TmOverlayPanel` with `TrapFocus` off) is a light surface and stays in the z-index
     band by design — promoting it would hand every light popup top-layer membership and the
     stacking contract above would no longer say anything. Consequence, stated rather than
     discovered: a toast already pinned on top can paint OVER a dropdown opened after it (the
     toast re-raises on every promotion; the dropdown never promotes). That trade is accepted
     for light surfaces; a popup that must paint above a toast is a modal surface and promotes
     like one.
9. **Known non-promoted surfaces (recorded exceptions).** Dialogs living in the extension
   packages still paint in their z-index band and were NOT promoted in this pass: the rich-text
   editor link/image/table/video/find dialogs, the Notion editor package surfaces, the
   spreadsheet package dialogs, the document-editor package dialogs, the signing package dialogs
   and the dashboard widget-selector layer (`_dashboard.css`). Opened while a core promoted
   surface is up they can paint under it — migrate them onto `TmModal`/`TmDialog` (which promote
   themselves) one surface at a time, and never cover them from package CSS with a z-index arms
   race. Any NEW modal surface must promote through `TopLayerInterop`.
