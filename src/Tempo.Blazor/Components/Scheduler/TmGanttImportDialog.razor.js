// File-input helpers for TmGanttImportDialog.
//
// openFilePicker forwards the still-active user gesture: the button's click handler reaches this
// call synchronously through Blazor's event dispatch, so input.click() opens the native file
// chooser for pointer, Enter and Space activations alike — a <label for> used to provide this for
// free but the label could never be focused, so keyboard users had no path at all.

export function openFilePicker(container) {
    const input = container?.querySelector('input[type="file"]');
    input?.click();
}
