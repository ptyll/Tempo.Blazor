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

A backdrop belongs inside the scope root, and it carries `data-tm-backdrop`. The module inerts every
sibling it walks, so a backdrop left outside the root is inert and swallows the click that should
close the overlay. The attribute is the only contract; a class name is not recognised.

The cycling, the initial-focus move, the inert background and the restore are things Blazor cannot do
itself, so they live in `wwwroot/js/tm-focus-trap.js`. Everything else — the dialog role, `aria-modal`,
the labelled-by wiring — is markup the scope renders.

## Initial focus in TmDialog

Every `TmDialog` names an explicit initial-focus target on its scope (`InitialFocusTargetId`), so the
trap never falls back to "first focusable". The target is per dialog type, chosen as the least
destructive action the dialog offers:

| Type | Initial focus |
|------|---------------|
| `Prompt` | the prompt input, so typing starts immediately and Enter submits |
| `Confirm` (and `IsDangerous`) | the Cancel button — the destructive action is one Tab away, never focused by default |
| `Alert` | the OK button, the only action there is |
| `Custom` | unset — the content decides (first focusable) |

The scroller `div.tm-dialog-content` is deliberately NOT a hardcoded tab stop. Before it was
`tabindex="0"` in the markup, which made it the first tabbable element of every dialog: the trap
focused the whole title+message block, a prompt never reached its input, and a keyboard-opened
dialog outlined the content region. The module (`syncScrollRegion` in `tm-focus-trap.js`) adds
`tabindex="0"` plus `role="region"`/`aria-labelledby` only while the content actually overflows
(`scrollHeight > clientHeight`, kept current by a `ResizeObserver`), and removes them when it does
not — a short dialog grows no extra tab stop. Even when the region exists, it never takes initial
focus; a keyboard user reaches it with Tab and scrolls it with the arrow keys.

## Who already uses it

`TmModal`, `TmDialog`, `TmDrawer`, `TmCommandPalette` and `TmGanttImportDialog` render a `TmFocusScope`.
A host of those components changes nothing: Escape, the focus restore and the inert background behave
as before. The scope root is a `div.tm-focus-scope` with `tabindex="-1"` while active, so a test that
looked for `role="dialog"` on the old element finds it on this root.

## Still claiming a modal without a trap

`TmLightbox` and `TmKeyboardShortcutsHelp` wrap their content in a `TmFocusScope`, so Tab stays inside
and Escape closes them. `TmDatePicker`, `TmDateRangePicker` and `TmDateTimePicker` used to render
`role="dialog"` popups without enforcing the modality — the `aria-modal` that was removed in F2
(marking a modality nothing enforced) came back in F3 through `TmOverlayPanel.TrapFocus`: the focus
scope **is** the top-layer panel root, so the calendar popup traps Tab, marks the background inert
and announces a real `aria-modal="true"` dialog. Escape stays with overlay.js, which closes the
popup and restores focus to the trigger. Other popups (menus, suggestion lists) keep `TrapFocus`
off and stay light.

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
in the tree.

Two consequences for a host that renders a modal inside its own component — a Notion block, an
editor canvas, a dashboard:

- The host's own scoped CSS (`.razor.css`) does not style the modal. Style the modal through the
  shared classes (`tm-modal`, `tm-modal-overlay`) or through a `Class` the host passes, declared in
  the shared bundle. This is why `TmViewManager` can hand its dialog to `TmModal` and keep its form
  markup: the form's classes are the host's, the overlay's classes are the library's.
- The host must not establish a fixed-position containing block around the modal. Per CSS,
  `position: fixed` resolves against the viewport unless an ancestor creates a containing block
  for it, which these properties do: `transform` (any value but `none`), `filter`, `backdrop-filter`,
  `perspective` (any value but `none`), `will-change` naming `transform`, `perspective` or `filter`,
  and `contain: layout | paint | strict | content`. `container-type: inline-size` does **not** pin
  a fixed descendant — a container query ancestor is fine. Render the modal outside such an
  ancestor, or accept that it anchors to it. The app-level viewport scope is `IsContainer="false"`
  for the same reason.

`TmDashboard` and `TmViewManager` are the reference: both render `TmModal` with
`MobilePresentation="Auto"`, so the dialog is a centered dialog on desktop and a bottom sheet on a
mobile viewport, with no component-specific sheet code.
