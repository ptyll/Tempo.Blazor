// Tempo.Blazor trace waterfall — the scroll-key guard of the treegrid.
//
// A row is a div[role="row"] carrying a tabindex: ↑/↓ move the roving tabindex, ←/→ collapse/expand,
// Home/End jump to the first/last visible row and Space selects. The browser's default action for
// those keys on a focused non-input element is to scroll — the page, or the row area the focused row
// lives in — so the row the user is navigating visibly jumps out of place. Blazor cannot decide
// preventDefault per key (a render-time @onkeydown:preventDefault is one bool for every keydown on
// the element, so it would also swallow Tab and trap focus inside the grid), which is why the
// decision lives here, exactly like tm-color-gradient.js and data-table.js make it. Tab, Enter and
// the +/−/0 zoom shortcuts are deliberately absent: they keep their browser behaviour.
//
// The guard is attached to the GRID, not to each row, so virtualization (a row that appears or
// disappears never needs its own listener) and the roving tabindex cost nothing. Only a key whose
// target is a row — and not a control inside it (the disclosure button) — is cancelled.

const NAV_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', ' ', 'Spacebar'];

// A native control inside the grid owns its own keys: the disclosure button turns Space into a
// click itself, and stopping that would break the button, not the scroll guard.
const CONTROL_SELECTOR =
    'button, input, textarea, select, a[href], [contenteditable="true"], [role="button"]';

const ROW_SELECTOR = '.tm-trace-waterfall__row';

// WeakMap: a grid that unmounts (or a component that is disposed) never keeps its detached element
// and its listener alive through this module.
const guards = new WeakMap();

/**
 * Cancels the scroll default of the treegrid's navigation keys on a row.
 * Idempotent: attaching twice to the same grid is a no-op.
 * @param {HTMLElement} grid the element carrying role="treegrid"
 */
export function attachScrollKeyGuard(grid) {
    if (!grid || typeof grid.addEventListener !== 'function' || guards.has(grid)) return;

    const onKeyDown = (event) => {
        if (!event || event.defaultPrevented) return;
        // Ctrl/Alt/Meta combinations belong to the browser (or to a host's shortcut), never to us.
        if (event.ctrlKey || event.altKey || event.metaKey) return;
        if (NAV_KEYS.indexOf(event.key) === -1) return;

        const target = event.target;
        if (!target || typeof target.closest !== 'function') return;
        // Only a row answers the navigation keys. A target parked anywhere else in the grid keeps
        // the browser default.
        if (!target.closest(ROW_SELECTOR)) return;
        // ...and a control INSIDE a row owns its keys (the disclosure button's native Space/Enter).
        if (target.closest(CONTROL_SELECTOR)) return;

        event.preventDefault();
    };

    grid.addEventListener('keydown', onKeyDown);
    guards.set(grid, onKeyDown);
}

/**
 * Removes the listener installed by {@link attachScrollKeyGuard}. Safe for a grid that was never
 * attached, for a detached element and for a missing argument.
 * @param {HTMLElement} grid the element the guard was attached to
 */
export function detachScrollKeyGuard(grid) {
    if (!grid || typeof grid.removeEventListener !== 'function') return;
    const onKeyDown = guards.get(grid);
    if (!onKeyDown) return;
    grid.removeEventListener('keydown', onKeyDown);
    guards.delete(grid);
}
