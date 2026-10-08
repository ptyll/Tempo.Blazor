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
