# Bottom sheet

A bottom sheet is a `TmDrawer` with `Position="DrawerPosition.Bottom"`. `TmModal` and `TmDialog`
present as a sheet on a narrow viewport, but a host that wants a sheet composes the drawer. The modal
and dialog sheets size to their content and dismiss; they do not snap.

## Snaps and content height

`SnapPoints` is a list of fractions, ascending, each between 0 and 1. The default is half and full
(`[0.5, 1.0]`). A modal sheet measures those fractions against the visible viewport. An inline sheet
(`Modal="false"`) measures them against its host. Snaps are clamped to `MaxHeight` and de-duplicated
before the gesture sees them. An empty list sizes the panel to its content, still under `MaxHeight`
(default 0.85). `SnapIndex` is two-way: the drawer adopts a change of the parameter, and a gesture
reports the snap it settled on. A parent re-render that passes the same index does not reset a snap
the user just dragged to.

`SwipeToDismiss` is on by default. A sheet with more than one snap dismisses only below the lowest
snap by 0.15, or on a downward flick faster than 0.5 px/ms. A sheet with one snap dismisses only
past a quarter of the height it started at, or on that same flick — a 10px drag does not close it.
The flick is the travel over the last 80ms of the drag, not the gap between the last move and
`pointerup`: a real release lands on that last move, so a velocity taken from it alone is always 0.
A content sheet dismisses when the drag passes a quarter of the height it started at, or on a flick.
A tap never dismisses, and an upward drag never dismisses.

The gesture writes only an inline `height` and `transition`, and clears both on release, cancel and
a vetoed dismiss. `--tm-sheet-height` stays Blazor-owned. Deleting it collapses a snap whose index
did not change, because Blazor will not rewrite a style it still considers current.

## Modal and inline

A modal sheet anchors to the viewport and lifts above the on-screen keyboard. `Modal="false"` renders
the sheet inside its host: the host must be positioned, the sheet fills it (`position: absolute;
inset: 0`) and does not track the viewport. Its snap fractions are of that host, not of the
viewport. Escape closes an inline sheet only when focus is inside it.

## Backdrop and nesting

The drawer's backdrop is rendered inside the focus-scope root and carries `data-tm-backdrop`. A backdrop
without that marker is marked `inert` and swallows the click that should close the sheet. A dialog
declared in page content can open while a drawer is open: the trap releases the ancestor it had
inerted, so the dialog is reachable, and closing it hands the drawer back.

## Viewport fallback

An overlay reads the viewport scope. With no scope it measures the viewport itself, which costs one
frame of `InitialMode` before the measurement arrives. Add
`<TmLayoutObserver IsViewportScope="true" IsContainer="false">` to the layout to avoid that flash.
