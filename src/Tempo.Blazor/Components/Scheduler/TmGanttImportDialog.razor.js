// File-input helpers for TmGanttImportDialog.
//
// registerFilePickerTrigger wires a NATIVE click listener directly on the trigger button, once,
// ahead of any click — input.click() then runs synchronously inside the browser's own click
// event, with NO Blazor/interop round-trip in between. A C#-mediated call (the previous
// openFilePicker(container), invoked FROM an @onclick handler) breaks the user-activation
// chain WebKit requires to honor input.click() (N204, review 2026-09-22).

export function registerFilePickerTrigger(triggerContainer, fileInputContainer) {
    const button = triggerContainer?.querySelector('button');
    const input = fileInputContainer?.querySelector('input[type="file"]');
    if (!button || !input || button.dataset.tmFilePickerRegistered === 'true') {
        return;
    }
    button.dataset.tmFilePickerRegistered = 'true';
    button.addEventListener('click', () => input.click());
}
