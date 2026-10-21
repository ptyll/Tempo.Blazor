// TmDocumentOpenDialog helpers (colocated ES module).
//
// Space scroll guard. The list rows and the grid cards are div[tabindex=0] — non-native focusables — so
// their Enter/Space activation is emulated in C# (Enter on keydown, Space on KEYUP, exactly where a native
// <button> clicks: docs/keyboard-activation-convention.md). The browser's default action for Space keydown on
// a focused non-form element is to scroll the scrollable region, and there is no per-key
// `@onkeydown:preventDefault` in Blazor (it is a render-time bool; a static `true` would also swallow Tab and
// trap focus). This listener is the scoped answer: it cancels the default of the Space keydown ONLY when the
// event target IS a row or a card itself — never for a keydown that bubbles off an input, a button or a
// text field inside the dialog, and never for any other key.

const ITEM_SELECTOR = '.tm-dod-row, .tm-dod-card';
const handlers = new WeakMap();

/**
 * Whether a keydown should have its default (page scroll) cancelled.
 * @param {{ key?: string, target?: any, defaultPrevented?: boolean }} event
 * @returns {boolean}
 */
export function shouldSuppressSpaceScroll(event) {
    if (!event || event.key !== ' ') return false;
    const target = event.target;
    if (!target || typeof target.matches !== 'function') return false;
    return target.matches(ITEM_SELECTOR);
}

export function attachSpaceGuard(root) {
    if (!root || handlers.has(root)) return;
    const handler = function (event) {
        if (shouldSuppressSpaceScroll(event)) event.preventDefault();
    };
    root.addEventListener('keydown', handler);
    handlers.set(root, handler);
}

export function detachSpaceGuard(root) {
    const handler = root && handlers.get(root);
    if (!handler) return;
    root.removeEventListener('keydown', handler);
    handlers.delete(root);
}
