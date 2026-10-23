// File-chooser trigger of TmToolbarFileButton (colocated ES module).
//
// The chooser is opened by a NATIVE, document-level click listener - never by a Blazor handler. A C#-mediated
// input.click() (an @onclick that round-trips through the interop) severs the user-activation chain WebKit requires;
// here input.click() runs synchronously inside the browser's own click event (the TmGanttImportDialog precedent).
//
// Two things open the chooser, both resolved to the hidden input of the owning TmToolbarFileButton:
//   - the bar button, which carries the input's id as data-tm-file-trigger;
//   - its entry in a TmToolbar "More" menu (.tm-toolbar-more__item), a button rendered by the menu host with data-action-id = the nested
//     TmToolbarButton's menu id, which C# registers once per button through acquire(inputId, entryId).
// One listener serves every file button on the page; it is installed with the first registration and removed with the last.

const byEntry = new Map();   // entryId -> inputId
const byInput = new Map();   // inputId -> entryId
let installed = false;

/**
 * Resolves the hidden input a click on <paramref name="target"/> should open.
 * @param {any} target the click target
 * @param {(id: string) => any} getById document.getElementById
 * @returns {any|null} the input element, or null when the click is not on a file trigger
 */
export function resolveInput(target, getById) {
    const trigger = target?.closest?.('[data-tm-file-trigger], .tm-toolbar-more__item[data-action-id]');
    if (!trigger) return null;
    if (trigger.disabled === true || trigger.getAttribute?.('aria-disabled') === 'true') return null;
    const inputId = trigger.getAttribute?.('data-tm-file-trigger')
        ?? byEntry.get(trigger.getAttribute?.('data-action-id'));
    if (!inputId) return null;
    const input = getById(inputId);
    return input && input.disabled !== true ? input : null;
}

function onClick(event) {
    const input = resolveInput(event.target, id => globalThis.document?.getElementById(id));
    if (!input) return;
    // Clear the previous selection BEFORE opening: choosing the same file again (re-importing a fixed JSON) would
    // otherwise be no change at all, and a cancelled chooser must not leave the earlier file behind.
    try { input.value = ''; } catch { /* some engines refuse writes to a file input; opening still works */ }
    input.click();
}

function install() {
    if (installed) return;
    // Capture phase: runs before Blazor's own (bubble-phase) delegated handlers, so the "More" menu closing and
    // unmounting its entry cannot take the target out from under us.
    globalThis.document?.addEventListener('click', onClick, true);
    installed = true;
}

function uninstallWhenIdle() {
    if (!installed || byInput.size > 0) return;
    globalThis.document?.removeEventListener('click', onClick, true);
    installed = false;
}

/**
 * Drops registrations whose input is no longer in the DOM (a release that never arrived, e.g. a lost circuit), so the
 * maps and the document listener cannot outlive the buttons. Skipped without a document (node tests, SSR).
 */
function pruneDetached() {
    const doc = globalThis.document;
    if (!doc?.getElementById) return;
    for (const [inputId, entryId] of [...byInput]) {
        if (doc.getElementById(inputId)) continue;
        byInput.delete(inputId);
        if (entryId !== undefined) byEntry.delete(entryId);
    }
}

/**
 * Registers a file button. Idempotent per input id; a changed entry id replaces the previous one.
 * @param {string} inputId the id of the hidden file input
 * @param {string} entryId the id of the button's "More" menu entry (data-action-id)
 */
export function acquire(inputId, entryId) {
    if (!inputId) return;
    pruneDetached();
    const previous = byInput.get(inputId);
    if (previous !== undefined) byEntry.delete(previous);
    byInput.set(inputId, entryId);
    if (entryId) byEntry.set(entryId, inputId);
    install();
}

/**
 * Unregisters a file button (it left the page).
 * @param {string} inputId the id of the hidden file input
 */
export function release(inputId) {
    const entryId = byInput.get(inputId);
    if (entryId !== undefined) byEntry.delete(entryId);
    byInput.delete(inputId);
    uninstallWhenIdle();
}

/** Test seam: forget every registration and the listener. */
export function __resetForTests() {
    byEntry.clear();
    byInput.clear();
    if (installed) globalThis.document?.removeEventListener('click', onClick, true);
    installed = false;
}
