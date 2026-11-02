// Tempo.Blazor.Signing — signing-editor.js (Signing redesign phase 7 review)
//
// The neutral helper module of the Signing EDITORS (TmRecipientRoleEditor, TmConditionBuilder,
// TmFormulaBuilder): the post-render focus hand-off after a remove/move and the cursor-aware
// insertion into the formula textarea. The module is imported on demand (dynamic import(), like
// signing-runner.js — no <script> tag in the host); when the module cannot load (bUnit, static
// rendering, a disposed circuit) the editors keep their local fallbacks (the Add action stays the
// focus fallback, an insertion appends). signing-runner.js re-exports focusElement, so the
// runner-side wrapper keeps its public surface.

/**
 * Focuses the first element matching `selector` in the whole document (phase 7 review: an editor
 * whose remove/move action rebuilds the rows must move the focus somewhere useful — the next row's
 * action — instead of dropping it to <body>). Returns whether the element really owns the focus
 * afterwards, so the caller can fall back to another target. A malformed selector must disable the
 * move, never throw into the circuit.
 *
 * @param {string} selector the CSS selector of the element to focus
 * @param {{
 *   document?: Document,
 * }} [options] injectable environment for the node lane
 * @returns {boolean} whether the focus landed on the element
 */
export function focusElement(selector, options = {}) {
    const doc = options.document ?? (typeof document !== 'undefined' ? document : null);
    if (!doc || !selector) {
        return false;
    }

    let element = null;
    try {
        element = doc.querySelector(selector);
    } catch {
        return false;
    }

    if (!element || typeof element.focus !== 'function') {
        return false;
    }

    element.focus();
    return doc.activeElement === element;
}

/**
 * Inserts `text` into the formula textarea at the current selection and returns the new value.
 * `host` may be the textarea itself or a wrapper element containing it (Blazor hands out the
 * reference of the wrapper the core TmTextArea renders inside). Without an active selection the
 * fragment lands at the caret position; a host without a textarea resolves to `null` — the caller
 * then appends the fragment instead (the C# wrapper treats null as the append fallback).
 *
 * @param {Element|HTMLTextAreaElement} host the textarea or a wrapper containing it
 * @param {string} text the fragment to insert
 * @returns {string|null} the new textarea value (also written back to the element), or `null`
 *   when no textarea could be resolved
 */
export function insertAtCursor(host, text) {
    // A plain object is neither a wrapper (querySelector) nor a textarea (value/selection) —
    // resolve to null so the caller's append fallback fires instead of a TypeError.
    const textarea = host && typeof host.querySelector === 'function'
        ? host.querySelector('textarea')
        : (host && (typeof host.value === 'string' || typeof host.selectionStart === 'number')
            ? host
            : null);
    if (!textarea) {
        return null;
    }

    const start = typeof textarea.selectionStart === 'number' ? textarea.selectionStart : textarea.value.length;
    const end = typeof textarea.selectionEnd === 'number' ? textarea.selectionEnd : start;
    const value = textarea.value ?? '';

    const next = value.slice(0, start) + text + value.slice(end);
    textarea.value = next;

    // Move the caret past the inserted fragment, so consecutive clicks keep appending in order.
    const caret = start + text.length;
    try {
        textarea.setSelectionRange(caret, caret);
    } catch {
        // A detached element may refuse the selection — the value is already correct.
    }

    return next;
}
