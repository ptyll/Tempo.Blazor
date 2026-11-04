// Tempo.Blazor.AI — agent run tree scroll-key guard.
//
// A row of TmAgentRunTree is a div[role="row"] carrying a tabindex: the roving tabindex moves with
// ↑/↓, ←/→ collapse or expand (and walk to the parent), Home/End jump to the edges and Enter/Space
// select. The browser's default action for those keys on a focused non-input element is to SCROLL —
// the page, or the row area the focused row lives in — so the row the reader is navigating visibly
// jumps out of place.
//
// Blazor cannot decide preventDefault per key: a render-time @onkeydown:preventDefault is one bool for
// every keydown on the element, so a static `true` would also swallow Tab and trap the focus inside the
// grid. The decision therefore lives here, exactly like tm-trace-waterfall.js makes it.
//
// The guard is attached to the GRID, not to each row: the row set changes on every filter, every
// collapse and every tick, and none of those needs its own listener. Only a key whose target is a row
// of THIS grid — and not a control inside it — is cancelled.

const NAV_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', ' ', 'Spacebar'];

// A native control inside a row owns its own keys: the disclosure button turns Space into a click
// itself, and stopping that would break the button, not the scroll guard.
const CONTROL_SELECTOR =
    'button, input, textarea, select, a[href], [contenteditable="true"], [role="button"]';

const DEFAULT_ROW_SELECTOR = '.tm-ai-agent-run-tree__row';

// WeakMap: a grid that unmounts (or a component that is disposed) never keeps its detached element and
// its listener alive through this module.
const guards = new WeakMap();

/**
 * Cancels the scroll default of the treegrid's navigation keys on a row.
 * Idempotent: attaching twice to the same grid is a no-op.
 * @param {HTMLElement} grid the element carrying role="treegrid"
 * @param {string} [rowSelector] the row selector of this grid; defaults to the agent run tree's row
 */
export function attachScrollKeyGuard(grid, rowSelector) {
    if (!grid || typeof grid.addEventListener !== 'function' || guards.has(grid)) return;

    const selector = typeof rowSelector === 'string' && rowSelector.length > 0
        ? rowSelector
        : DEFAULT_ROW_SELECTOR;

    const onKeyDown = (event) => {
        if (!event || event.defaultPrevented) return;
        // Ctrl/Alt/Meta combinations belong to the browser (or to a host's shortcut), never to us.
        if (event.ctrlKey || event.altKey || event.metaKey) return;
        if (NAV_KEYS.indexOf(event.key) === -1) return;

        const target = event.target;
        if (!target || typeof target.closest !== 'function') return;
        // Only a row of this grid answers the navigation keys; a target parked anywhere else in the
        // grid keeps the browser default.
        if (!target.closest(selector)) return;
        // ...and a control INSIDE a row owns its keys.
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
