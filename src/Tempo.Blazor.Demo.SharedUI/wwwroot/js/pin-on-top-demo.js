// Demo-only ES module for the /pin-on-top page: opens a NATIVE modal <dialog> (showModal has no
// Blazor equivalent) so the page can show how pins wait below a native modal and land on top when
// it closes. Lives in the demo's own static assets; nothing in the library depends on it.
export function showModal(dialog) {
    if (dialog && typeof dialog.showModal === 'function' && !dialog.open) {
        dialog.showModal();
    }
}
