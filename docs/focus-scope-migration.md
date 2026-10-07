# Focus scope migration

`TmFocusScope` (`Tempo.Blazor.Components.Feedback`) is the public focus-trap primitive. It replaces
the internal `Tempo.Blazor.Helpers.FocusTrap`, which stays internal because a helper tied to one
`ElementReference` is not something a package can reuse.

## What the scope does

Wrap content in it and it traps Tab and Shift+Tab inside, moves initial focus in on activation,
restores focus on deactivation, and marks the background `inert` while it is modal. Only the
innermost active scope handles Tab, and only the topmost one handles Escape.

```razor
<TmFocusScope Active="@open"
              CloseOnEscape="true"
              OnEscape="Close"
              AriaLabelledBy="sheet-title"
              RestoreFocusTarget="canvas">
    <h2 id="sheet-title">Filters</h2>
    @ChildContent
</TmFocusScope>
```

`RestoreFocusTarget` is the element focus returns to. A canvas editor passes its canvas, so closing
the scope does not land focus back on the trigger that opened it. Leave it unset and the scope
restores whatever was focused when it opened, which is what an overlay wants.

A backdrop rendered as a sibling of the scope root must carry `data-tm-backdrop`. The module inerts
every sibling it walks, and a backdrop without the marker swallows the click that should close the
overlay. `.tm-drawer__overlay` and `.tm-command-palette-backdrop` are recognised without the attribute.

The cycling, the initial-focus move, the inert background and the restore are things Blazor cannot do
itself, so they live in `wwwroot/js/tm-focus-trap.js`. Everything else — the dialog role, `aria-modal`,
the labelled-by wiring — is markup the scope renders.

## Who already uses it

`TmModal`, `TmDialog`, `TmDrawer`, `TmCommandPalette` and `TmGanttImportDialog` render a `TmFocusScope`.
A host of those components changes nothing: Escape, the focus restore and the inert background behave
as before. The scope root is a `div.tm-focus-scope` with `tabindex="-1"` while active, so a test that
looked for `role="dialog"` on the old element finds it on this root.

## Packages that keep their own trap

`Tempo.Blazor.DocumentEditor` has four copies (`DocumentEditorFocusTrapController`, used by the version
dialog, the compare dialog, the JSON debug modal and the clipboard debug modal) and
`Tempo.Blazor.NotionEditor` traps focus through `tmNotionEditor.initFocusTrap` (the block context menu
and the media upload dialog). Both keep working. To move one onto the shared scope:

1. Replace the controller or the `initFocusTrap` call with a `TmFocusScope` around the dialog content.
2. Pass the canvas or the block as `RestoreFocusTarget` where the old code restored focus explicitly.
3. Delete the package's own trap module once nothing calls it.

The move is per dialog, so a package can migrate one surface at a time.

## Rendering a modal inside another component

`TmModal` renders its overlay with `position: fixed` against the viewport, and its styles live in
the shared `tempo-blazor.css` bundle, not in a scoped stylesheet. That is what makes it embeddable:
a scoped stylesheet would not reach the overlay once Blazor teleports nothing and the overlay stays
in the tree, and a `container-type` ancestor would pin the fixed overlay to that ancestor instead of
the viewport.

Two consequences for a host that renders a modal inside its own component — a Notion block, an
editor canvas, a dashboard:

- The host's own scoped CSS (`.razor.css`) does not style the modal. Style the modal through the
  shared classes (`tm-modal`, `tm-modal-overlay`) or through a `Class` the host passes, declared in
  the shared bundle. This is why `TmViewManager` can hand its dialog to `TmModal` and keep its form
  markup: the form's classes are the host's, the overlay's classes are the library's.
- The host must not establish a containment context around the modal. `container-type`, `transform`,
  `filter` and `will-change` all turn the ancestor into the containing block for `position: fixed`,
  which pins the sheet to that ancestor. Render the modal outside such an ancestor, or accept that
  it anchors to it. The app-level viewport scope is `IsContainer="false"` for the same reason.

`TmDashboard` and `TmViewManager` are the reference: both render `TmModal` with
`MobilePresentation="Auto"`, so the dialog is a centered dialog on desktop and a bottom sheet on a
mobile viewport, with no component-specific sheet code.
