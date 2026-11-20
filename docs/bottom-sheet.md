# Bottom sheet

A bottom sheet is a `TmDrawer` with `Position="DrawerPosition.Bottom"`. `TmModal` and `TmDialog`
present as a sheet on a narrow viewport, but a host that wants a sheet composes the drawer. The modal
and dialog sheets size to their content and dismiss; they do not snap.

`MobilePresentation.Auto` gives a sheet on the narrow viewport and the centred dialog otherwise.
`MobilePresentation.AutoFullscreen` is the same choice for a TALL surface (a tabbed form, a table, a
preview): a fullscreen panel on the narrow viewport, the centred dialog everywhere else — a sheet would
clip it. A forced `Sheet` / `Fullscreen` / `Dialog` still renders that presentation at every width.

## Snaps and content height

`SnapPoints` is a list of fractions, ascending, each between 0 and 1. The default is half and full
(`[0.5, 1.0]`). A value above `MaxHeight` is clamped to it and duplicates are removed, so the
gesture never sees a snap it cannot reach or two identical snaps. A modal sheet measures those
fractions against the visible viewport. An inline sheet (`Modal="false"`) measures them against its
host. An empty list sizes the panel to its content, still under `MaxHeight` (default 0.85).
`SnapIndex` is two-way: the drawer adopts a change of the parameter, and a gesture
reports the snap it settled on. A parent re-render that passes the same index does not reset a snap
the user just dragged to.

`SwipeToDismiss` is on by default, and every dismiss honours it. A release below the lowest snap by
0.15 dismisses. A flick moves ONE snap in the direction of the drag as a whole — the net direction
between the press and the release, not the tail of the last 80ms — from the snap the drag started
on: a downward flick from the lowest snap dismisses, a downward flick from a higher snap steps one
snap down, and an upward flick steps one snap up, whatever the tail velocity says. Only a release
faster than about 2 px/ms closes the sheet from any snap, and only while `SwipeToDismiss` is on:
with the flag off, a fast flick steps one snap down like any flick and clamps at the lowest snap;
it never dismisses. A sheet with one
snap dismisses only past a quarter of the height it started at, or on that same downward flick — a
10px drag does not close it. The flick is the travel over the last 80ms of the drag, not the gap
between the last move and `pointerup`: a real release lands on that last move, so a velocity taken
from it alone is always 0. A content sheet dismisses when the drag passes a quarter of the height
it started at, or on a flick. A tap never dismisses, and an upward drag never dismisses.

The gesture writes only an inline `height` and `transition`, and clears both on release, cancel and
a vetoed dismiss. `--tm-sheet-height` stays Blazor-owned. Deleting it collapses a snap whose index
did not change, because Blazor will not rewrite a style it still considers current. The gesture runs
on the shared `pointer-drag.js` helper: a second finger cannot take over an active drag, and Escape
during a drag cancels the drag first — the inline height is rolled back, the snap C# owns stays, the
Escape is consumed and does not reach the dialog — so it takes a second Escape to close the sheet.

### `TmSidePanel` on a phone: snaps or content height

`TmSidePanel` composes this drawer as a bottom sheet on a phone viewport and, until 2.12, always used the
drawer defaults — a half-height sheet (`[0.5, 1.0]`, the top snap clamped to the 0.85 cap), so a form of four
fields opened with its last field and its buttons below the fold and the reader had to drag the handle first.
The panel now hands the sheet height to the host, in two ways; both apply to the BOTTOM sheet only (the side
sheet and the docked panel have no height of their own and ignore them), and every default is the pre-2.12
behaviour.

| Parameter | Default | Meaning |
|---|---|---|
| `SheetHeight` | `Snap` | `Snap` rests on snap points. `Content` sizes the sheet to its content under the cap. |
| `SheetSnapPoints` | `null` | The drawer's `SnapPoints` (`[0.7, 1.0]` opens a form at 70 %). `null` keeps `[0.5, 1.0]`. |
| `SheetSnapIndex` / `SheetSnapIndexChanged` | `0` | The snap the sheet renders, two-way like the drawer's `SnapIndex`. |
| `SheetMaxHeight` | `null` | The drawer's `MaxHeight`, the cap of the snaps and of the content height; `null` keeps 0.85. |

`SheetHeight="SidePanelSheetHeight.Content"` is the same mode `TmModal` uses on a phone (`tm-sheet--content`):
the panel is as tall as its header, body and footer, **a short form shows whole without dragging**, and the cap
still applies. A longer body scrolls inside the sheet while the `FooterContent` (`tm-drawer__footer--sticky`)
stays pinned to the bottom. There are no snaps, so `SheetSnapPoints` / `SheetSnapIndex` are ignored, the grabber
is a decorative swipe target (a downward swipe still dismisses) and the handle is not a slider.

Choose by the content: a short form, a confirmation or an editor of a few fields wants `Content`; a tall,
open-ended surface (a long list, a filter panel the reader wants to expand) wants snaps — usually with
`SheetSnapPoints` raised above the half default so it opens usefully tall, and `SheetMaxHeight` when 0.85 leaves
too much (or too little) of the page behind it.

The cap of a content-height MODAL sheet is `--tm-sheet-max` of the visible viewport. The drawer's own
`max-block-size: 100dvh` used to out-specify the shared `.tm-sheet__panel` cap (they are the same property), so a
content sheet with a long body filled the whole screen; `_drawer.css` now restates the cap at a higher
specificity, which also makes every other content-height bottom drawer honour `MaxHeight`.

## Modal and inline

A modal sheet anchors to the viewport and lifts above the on-screen keyboard. `TmModal` and `TmDialog`
sheets render their grabber only while the sheet can actually be dismissed (`CloseOnOverlayClick`):
the grabber is the swipe target, so a sheet that cannot be dismissed does not paint one. `Modal="false"`
renders the sheet inside its host: the host must be positioned, the sheet fills it
(`position: absolute; inset: 0`) and does not track the viewport. Its snap fractions are of that
host, not of the viewport. Escape closes an inline sheet only when focus is inside it.

## Backdrop and nesting

The drawer's backdrop is rendered inside the focus-scope root and carries `data-tm-backdrop`. A backdrop
without that marker is marked `inert` and swallows the click that should close the sheet. A dialog
declared in page content can open while a drawer is open: the trap releases the ancestor it had
inerted, so the dialog is reachable, and closing it hands the drawer back.

## Viewport fallback

An overlay reads the viewport scope. With no scope it measures the viewport itself, which costs one
frame of `InitialMode` before the measurement arrives. Add
`<TmLayoutObserver IsViewportScope="true" IsContainer="false">` to the layout to avoid that flash.
