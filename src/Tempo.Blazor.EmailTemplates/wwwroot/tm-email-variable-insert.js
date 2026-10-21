// Tracks the last-focused CONTENT field so the variable picker can insert a token at the caret even after
// focus moves to the picker button. A search box is never a content field: focusing the picker's own filter
// (or the palette's block search) used to make the token land in the filter, and a picker-internal input
// must not be able to steal the remembered target either. The zones are marked with data attributes
// (`data-tm-variable-picker`, `data-tm-toolbox-search`) so a future text input inside either surface is
// excluded without touching this module again.
let lastEditable = null;

const IGNORED_ZONES = '[data-tm-variable-picker],[data-tm-toolbox-search]';

function isEditableTarget(target) {
    if (!target) return false;
    if (target.tagName !== 'INPUT' && target.tagName !== 'TEXTAREA') return false;
    if (typeof target.type === 'string' && target.type.toLowerCase() === 'search') return false;
    if (typeof target.closest === 'function' && target.closest(IGNORED_ZONES)) return false;
    return true;
}

document.addEventListener('focusin', (e) => {
    if (isEditableTarget(e.target)) {
        lastEditable = e.target;
    }
});

export function insertToken(token) {
    const el = lastEditable;
    if (!el || !el.isConnected) {
        return false;
    }
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? el.value.length;
    el.value = el.value.slice(0, start) + token + el.value.slice(end);
    const caret = start + token.length;
    el.setSelectionRange(caret, caret);
    // Notify Blazor's binding so the model updates.
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.focus();
    return true;
}
