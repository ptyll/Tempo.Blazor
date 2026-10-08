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

- `Auto` (default) — a content-height **bottom sheet** on a mobile viewport, the anchored
  popover otherwise. The choice resolves from the **viewport** (the `TmLayoutScopes.Viewport`
  cascade or the internal probe), because the sheet is positioned against the viewport; the
  popover's *size* still follows the **trigger's container**.
- `Popover` / `Sheet` — forced, render without measurement (testable in bUnit without a DOM).

In sheet mode the panel **composes `TmDrawer Position=Bottom`** (content height, `Modal`) — it
never copies sheet, gesture or focus logic. The sheet header shows `Title` (fall back:
`AriaLabel`, then a localized generic "Menu") and a **Done** action (`Tm_Done`) that closes
without selecting. The sheet renders **in place** — a sheet opened inside a `TmModal` stays in
the modal's DOM, stacks above the modal content, and nests inside the modal's focus trap.

`TmDropdown` and `TmPopover` forward `MobilePresentation`, `LayoutMode`, `InitialMode` and
`ResolvedLayoutChanged`; the dropdown titles its sheet with the trigger text. Anchoring options
(`Placement`, `Align`, `Offset`, `MatchAnchorWidth`, …) apply to the popover only.

### Modality of dialog popups

A `Role="dialog"` popup that must trap focus (calendar popups, the column picker) passes
`TrapFocus="true"`: the `TmFocusScope` **is** the panel root, so Tab cycles inside, the
background goes inert, and `aria-modal="true"` is enforced by a real trap rather than promised
without one. Escape stays with `overlay.js`, which restores focus to the anchor. Popups that
must stay light (menus, suggestion lists) leave it off.

## What packages copy today

`Tempo.Blazor.NotionEditor` carries ~5 local copies of clamp/flip math for its floating menus
(AI menu, mention menu, comment mention, notification center). They should become
`TmOverlayPanel` consumers in the NotionEditor redesign plan and delete their local math —
the engine, the top layer, the keyboard-aware viewport read and the mobile sheet then come for
free. Same for `Tempo.Blazor.DocumentEditor`'s four local focus traps: migrate one dialog at a
time onto `TmFocusScope` (see [focus-scope-migration.md](focus-scope-migration.md)).

## Rules

1. One engine per surface kind — never re-implement flip/clamp/scroll-tracking in a package.
2. New CSS for a panel positions nothing itself; the panel root gets surface styling only
   (background, border, radius, shadow), never `position`/`top`/`left`/`z-index`.
3. Every sheet is named (title or fallback) — an unnamed dialog fails axe.
4. Focus returns to the trigger on every close path (Escape, outside, Done, selection).
5. Anchored surfaces resolve the popover-vs-sheet choice from the viewport; in-container
   surfaces resolve from their container ([responsive-conventions.md](responsive-conventions.md)).
