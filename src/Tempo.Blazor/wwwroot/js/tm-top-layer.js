// Tempo.Blazor top-layer registry (ES module).
//
// The shared promote/demote/raise helpers every modal viewport-anchored overlay root uses
// (TopLayerInterop). Every modal viewport-anchored overlay root carries popover="manual" and
// promotes itself at open; the pinned-on-top registry keeps toast containers above every modal
// surface (F3 review round 3, R3-M1).
//
// Extracted from tm-sheet.js in F6 (F3 round-3 follow-up): the sheet module owns gestures, this
// module owns the top layer. tm-sheet.js re-exports everything for hosts that still import it
// from there.

// Roots that must stay above every other promoted surface while they hold content — the toast
// containers register here through TopLayerInterop. promote() and raise() re-raise every registered
// root that is still popover-open AFTER their own showPopover, so a surface promoted later (a
// drawer, sheet or dialog opened after a toast went up) can never cover it.
const pinnedRoots = new Set();

/**
 * Registers a root as pinned on top. Called by a toast container when it first promotes; the
 * registry is what promote()/raise() re-raise after every later promotion.
 * @param {HTMLElement} root the pinned root (a toast container)
 */
export function pinRoot(root) {
    if (root) pinnedRoots.add(root);
}

/**
 * Removes a root from the pinned registry (the container demoted empty or disposed). No-op for a
 * root that was never pinned.
 * @param {HTMLElement} root the previously pinned root
 */
export function unpinRoot(root) {
    pinnedRoots.delete(root);
}

/**
 * Re-raises every pinned root that is still open, in registration order. Called after a promotion
 * so pinned status UI stays above the newly promoted surface. A pinned root that left the DOM is
 * unregistered instead of re-raised — a detached popover is hidden by the browser anyway.
 * @param {HTMLElement|null} except a root to skip (the one that was just promoted/raised)
 */
function reRaisePinnedRoots(except) {
    for (const pinned of [...pinnedRoots]) {
        if (pinned === except || pinned === null || pinned === undefined) continue;
        if (!pinned.isConnected) {
            pinnedRoots.delete(pinned);
            continue;
        }
        try {
            // An empty toast container demotes itself, so :popover-open IS the has-content signal:
            // re-raising a closed container would pop an empty overlay above everything.
            if (pinned.matches(':popover-open')) {
                pinned.hidePopover();
                pinned.showPopover();
            }
        }
        catch {
            // InvalidStateError — the element is being detached; the browser hides a dead
            // top-layer element on its own.
        }
    }
}

/**
 * Shows a popover root in the browser top layer, no-op when it is already open, the Popover API is
 * missing or the element is being detached.
 * @param {HTMLElement} root the overlay root
 */
function show(root) {
    try {
        if (!root.matches(':popover-open')) {
            root.showPopover();
        }
    }
    catch {
        // InvalidStateError — the element is being detached; the browser hides a dead top-layer
        // element on its own.
    }
}

/**
 * Promotes a modal overlay root to the browser top layer. Every modal viewport-anchored overlay
 * root carries popover="manual" (F3 review round 2, U1): a modal drawer at any position, the
 * TmModal/TmDialog overlay root, the command palette, the keyboard-shortcuts overlay, the
 * lightbox, the Gantt import dialog and the toast containers. showPopover() lifts the root above
 * every z-index stacking context and outside every ancestor's overflow/transform clipping — a
 * sticky app bar (TmTopBar under TmBottomNavigation) or a transformed host can then neither
 * confine nor cover it, and a surface opened LATER from inside it (a dialog from a sheet, a toast
 * from a dialog) promotes after it, so the top-layer order equals the open order. The DOM stays
 * in place, so the nested focus trap and the Escape order are unchanged. No-op where the Popover
 * API is missing. After its own promotion every PINNED root (a toast container holding toasts) is
 * re-raised above it, so toasts stay above every modal surface, always — the drawer's own
 * promote must not cover a toast that is already on screen (F3 review round 3, R3-M1).
 * @param {HTMLElement} root the overlay root (a drawer focus-scope element, a modal overlay, the
 *   command palette backdrop, the toast container, …)
 */
export function promote(root) {
    if (!root || typeof root.showPopover !== 'function') {
        return;
    }
    show(root);
    reRaisePinnedRoots(root);
}

/**
 * Demotes a promoted root (hidePopover). No-op where the Popover API is missing or the element
 * is not currently promoted. Callers whose root STAYS in the DOM (the toast containers) demote on
 * close; an overlay that unmounts on close (drawer/modal/dialog) needs nothing — a detached
 * top-layer element is hidden by the browser automatically.
 * @param {HTMLElement} root the overlay root
 */
export function demote(root) {
    if (!root || typeof root.hidePopover !== 'function') {
        return;
    }
    try {
        if (root.matches(':popover-open')) {
            root.hidePopover();
        }
    }
    catch {
        // InvalidStateError — the element is being detached; nothing to demote.
    }
}

/**
 * Re-raises a promoted root above everything promoted after it, pinned roots included: the other
 * pinned roots re-raise FIRST and the pusher LAST, so the toast that just arrived paints topmost.
 * hidePopover + showPopover in one synchronous step, so no frame paints without it.
 * TmToastContainer calls this on every push, so a toast always lands above sheets and dialogs
 * opened before it (they stay promoted underneath).
 * @param {HTMLElement} root the overlay root
 */
export function raise(root) {
    if (!root || typeof root.showPopover !== 'function') {
        return;
    }
    reRaisePinnedRoots(root);
    demote(root);
    show(root);
}
