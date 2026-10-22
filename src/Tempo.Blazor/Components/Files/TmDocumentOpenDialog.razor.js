// TmDocumentOpenDialog helpers (colocated ES module).
//
// Space scroll guard. The list rows and the grid cards are div[tabindex=0] — non-native focusables — so
// their Enter/Space activation is emulated in C# (Enter on keydown, Space on KEYUP, exactly where a native
// <button> clicks: docs/keyboard-activation-convention.md). The browser's default action for Space keydown on
// a focused non-form element is to scroll the scrollable region, and there is no per-key
// `@onkeydown:preventDefault` in Blazor (it is a render-time bool; a static `true` would also swallow Tab and
// trap focus). This listener is the scoped answer: it cancels the default of the Space keydown ONLY when the
// event target IS a row, a card or a tree item itself — never for a keydown that bubbles off an input, a button or a
// text field inside the dialog, and never for Tab / Enter. The same listener also cancels the default scroll of the arrow keys,
// Home and End on those three targets (shouldSuppressNavigationScroll): they move the roving focus, the browser must not also scroll.
//
// Roving focus. The folder tree, the list rows and the grid cards are ONE tab stop each (roving tabindex) and the
// arrow keys move focus inside them. Blazor owns the tabindex attributes; once it has rendered the new stop this
// module moves the DOM focus to it (focusRoving) — a tabindex alone never moves focus. getGridColumns reads how
// many cards share a grid row, which only the browser knows (the grid is auto-fill), so Up/Down can step a row.

const ITEM_SELECTOR = '.tm-dod-row, .tm-dod-card, .tm-dod-tree-node';
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

const NAVIGATION_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End']);

/**
 * Whether a navigation key's default (scrolling the tree / list container) should be cancelled: the arrows, Home and End
 * move FOCUS between the roving stops (Blazor + focusRoving do that), so the browser's own scroll of the scrollable
 * container would only fight it. Only when the target IS a tree item, row or card — a text field or a button
 * inside the dialog keeps its native caret / scroll behaviour.
 * @param {{ key?: string, target?: any }} event
 * @returns {boolean}
 */
export function shouldSuppressNavigationScroll(event) {
    if (!event || !NAVIGATION_KEYS.has(event.key)) return false;
    // Alt+Left/Right is the browser's back/forward, Ctrl+Home/End and Shift+arrows are the user's own gestures: leave them alone.
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
    const target = event.target;
    if (!target || typeof target.matches !== 'function') return false;
    return target.matches(ITEM_SELECTOR);
}

export function attachSpaceGuard(root) {
    if (!root || handlers.has(root)) return;
    const handler = function (event) {
        if (shouldSuppressSpaceScroll(event) || shouldSuppressNavigationScroll(event)) event.preventDefault();
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

/**
 * The element a roving focus request targets, or null.
 * @param {{ querySelectorAll?: Function }} root The dialog root.
 * @param {string} kind 'tree' (matched by data-path) or 'item' (a row or card, matched by data-id).
 * @param {string} key The folder path or the document id.
 */
export function findRovingTarget(root, kind, key) {
    if (!root || typeof root.querySelectorAll !== 'function') return null;
    const selector = kind === 'tree' ? '.tm-dod-tree-node' : '.tm-dod-row, .tm-dod-card';
    const attribute = kind === 'tree' ? 'data-path' : 'data-id';
    for (const element of root.querySelectorAll(selector)) {
        if (element.getAttribute(attribute) === key) return element;
    }
    return null;
}

/** Moves DOM focus to the roving stop Blazor just rendered. The root is the dialog content root (it holds the tree and the content). */
export function focusRoving(root, kind, key) {
    const target = findRovingTarget(root, kind, key);
    if (target && typeof target.focus === 'function') target.focus();
}

/**
 * How many tracks the auto-fill grid currently has: the number of cards that share the first row's top edge.
 * @param {{ querySelectorAll?: Function }} root
 * @returns {number} At least 1.
 */
export function getGridColumns(root) {
    if (!root || typeof root.querySelectorAll !== 'function') return 1;
    const cards = [...root.querySelectorAll('.tm-dod-card')];
    if (cards.length === 0) return 1;
    const top = cards[0].offsetTop;
    let columns = 0;
    for (const card of cards) {
        if (card.offsetTop !== top) break;
        columns++;
    }
    return Math.max(1, columns);
}
