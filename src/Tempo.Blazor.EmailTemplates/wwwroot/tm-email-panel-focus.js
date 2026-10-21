// Focus recovery for the email editor's HOST-OWNED rail buttons.
//
// The palette's rail toggle is the shell's own (TmEditorShell re-asserts the focus after it expands a
// panel), but the inspector's rail button ([data-tm-expand-properties]) belongs to the editor: the
// button unmounts with the rail and no core code knows the host's focus target. This module moves the
// focus onto the EXPANDED panel's toggle — but only when the focus was actually lost (the button left
// the DOM, the body, or the closed rail), never when the user moved it somewhere else.

/**
 * Focuses the expanded panel's toggle inside an editor root after a rail expansion.
 * @param {Element|null|undefined} root The editor root (a rail button lives inside it).
 * @param {string} selector The toggle to focus, e.g. '.tm-editor-shell__panel-toggle--right'.
 * @returns {boolean} True when focus moved.
 */
export function focusRailExpandedToggle(root, selector) {
    const target = root && typeof root.querySelector === 'function' ? root.querySelector(selector) : null;
    if (!target || target.isConnected === false || typeof target.focus !== 'function') {
        return false;
    }

    const doc = typeof document !== 'undefined' ? document : null;
    const active = doc ? doc.activeElement : null;
    const insideRail = !!active
        && typeof active.closest === 'function'
        && !!active.closest('.tm-editor-shell__rail');
    const lost = !active
        || active === (doc && doc.body)
        || active.isConnected === false
        || insideRail;
    if (!lost) {
        return false;
    }

    try {
        target.focus();
        return true;
    } catch {
        return false;
    }
}
