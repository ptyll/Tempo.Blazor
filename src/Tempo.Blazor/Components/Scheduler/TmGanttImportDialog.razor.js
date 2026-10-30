// File-input helpers for TmGanttImportDialog.
//
// registerFilePickerTrigger wires a NATIVE click listener directly on the trigger button, once,
// ahead of any click — input.click() then runs synchronously inside the browser's own click
// event, with NO Blazor/interop round-trip in between. A C#-mediated call (the previous
// openFilePicker(container), invoked FROM an @onclick handler) breaks the user-activation
// chain WebKit requires to honor input.click() (N204, review 2026-09-22).
//
// The input is resolved AT CLICK TIME, not at registration: "Remove file" and a tab switch
// recreate the <input type=file> (the only way to clear a native file input without more
// interop) while the trigger button - and therefore this listener - lives on, so a reference
// captured here would point at a detached input.

export function registerFilePickerTrigger(triggerContainer, fileInputContainer) {
    const button = triggerContainer?.querySelector('button');
    if (!button || !fileInputContainer || button.dataset.tmFilePickerRegistered === 'true') {
        return;
    }
    button.dataset.tmFilePickerRegistered = 'true';
    button.addEventListener('click', () => fileInputContainer.querySelector('input[type="file"]')?.click());
}

// registerTablistKeys stops the browser's default scroll for the keys the ARIA tablist handles in C#
// (Home / End / ArrowLeft / ArrowRight / ArrowUp / ArrowDown) - and ONLY those: Tab, Escape and everything else keep
// their default. The Blazor @onkeydown handler still runs (preventDefault does not stop propagation).
const TABLIST_KEYS = new Set(['Home', 'End', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);

export function registerTablistKeys(tablist) {
    if (!tablist || tablist.dataset.tmTablistKeysRegistered === 'true') {
        return;
    }
    tablist.dataset.tmTablistKeysRegistered = 'true';
    tablist.addEventListener('keydown', event => {
        if (TABLIST_KEYS.has(event.key) && !event.altKey && !event.ctrlKey && !event.metaKey) {
            event.preventDefault();
        }
    });
}
