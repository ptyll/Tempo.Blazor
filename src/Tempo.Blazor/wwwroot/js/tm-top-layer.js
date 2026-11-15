// Tempo.Blazor top-layer registry (ES module).
//
// The shared promote/demote/raise helpers every modal viewport-anchored overlay root uses
// (TopLayerInterop). Every modal viewport-anchored overlay root carries popover="manual" and
// promotes itself at open; the pinned-on-top registry keeps pinned roots (the toast containers and,
// since 2.12.0, any host element pinned through tm-pin-on-top.js / TopLayerPinService) above every
// modal surface (F3 review round 3, R3-M1; decision TOP-LAYER-PIN, docs/top-layer-pin.md).
//
// Extracted from tm-sheet.js in F6 (F3 round-3 follow-up): the sheet module owns gestures, this
// module owns the top layer. tm-sheet.js re-exports everything for hosts that still import it
// from there.
//
// ONE registry, ONE ordering rule (docs/top-layer-pin.md §4.3): pins are ordered by
// (priority ascending, registration order ascending) — a higher priority paints higher, equal
// priorities keep the order they registered in. Re-pinning an element updates its priority and
// keeps its registration slot.
//
// A NATIVE modal <dialog> (showModal) is respected (§4.2): while one is open the browser makes
// everything outside it inert, so a pin re-raised above it would cover the dialog while being
// impossible to use. No pin is promoted or re-raised while `dialog:modal` matches; a request made
// meanwhile is remembered and the close re-sync (one capture-phase `close` listener on document)
// lands it, in registry order, the moment no native modal is left.

// ONE registry per document, even when two copies of this module load: a <script type="module">
// with the plain URL and an IJSRuntime import rewritten to the fingerprinted URL by an import map
// are two module instances. A module-level Map would split the contract (a root pinned through
// one copy would never be re-raised by a promote through the other), so the state lives on
// globalThis under one versioned key.
//   pinned: element -> { priority, seq, pending } — `pending` records a show a native modal deferred.
const REGISTRY_KEY = '__tmTopLayerRegistry_v1';
const registry = globalThis[REGISTRY_KEY] ??= { pinned: new Map(), nextSeq: 0, closeListenerInstalled: false };
const pinned = registry.pinned;

function priorityOf(options) {
    const value = options && typeof options === 'object' ? Number(options.priority) : 0;
    return Number.isFinite(value) ? Math.trunc(value) : 0;
}

function ordered() {
    return [...pinned.entries()]
        .sort(([, a], [, b]) => (a.priority - b.priority) || (a.seq - b.seq));
}

function supportsPopover(root) {
    return !!root && typeof root.showPopover === 'function';
}

function isOpen(root) {
    try {
        return !!root && typeof root.matches === 'function' && root.matches(':popover-open');
    }
    catch {
        return false;
    }
}

function installCloseListener() {
    if (registry.closeListenerInstalled) return;
    if (typeof document === 'undefined' || !document || typeof document.addEventListener !== 'function') return;
    registry.closeListenerInstalled = true;
    // `close` does not bubble: only a CAPTURE listener on document sees every dialog's close.
    document.addEventListener('close', () => {
        if (!isNativeModalOpen()) syncPinnedRoots();
    }, true);
}

/**
 * Whether a native modal dialog (`<dialog>.showModal()`) is currently open. A browser without the
 * `:modal` pseudo-class (or no document at all) reads as "no native modal".
 * @returns {boolean}
 */
export function isNativeModalOpen() {
    try {
        return typeof document !== 'undefined' && !!document
            && typeof document.querySelector === 'function'
            && document.querySelector('dialog:modal') !== null;
    }
    catch {
        return false;
    }
}

/**
 * Registers a root as pinned on top, or updates the priority of a root that is already pinned
 * (keeping its registration slot). It does NOT show the root — promote()/raise() do. Explicit
 * owners (the toast containers) pass no options and pin at priority 0.
 * @param {HTMLElement} root the pinned root
 * @param {{priority?: number}} [options] order between pins; higher paints higher (default 0)
 */
export function pinRoot(root, options) {
    if (!root) return;
    const priority = priorityOf(options);
    const entry = pinned.get(root);
    if (entry) {
        entry.priority = priority;
    }
    else {
        pinned.set(root, { priority, seq: registry.nextSeq++, pending: false });
    }
    installCloseListener();
}

/**
 * Removes a root from the pinned registry (the container demoted empty, the pin was disposed). No-op
 * for a root that was never pinned. It does NOT hide the root.
 * @param {HTMLElement} root the previously pinned root
 */
export function unpinRoot(root) {
    pinned.delete(root);
}

/**
 * Re-raises (hidePopover + showPopover) every pinned root that is open and accepted by `filter`,
 * in registry order. A pinned root that left the DOM is unregistered instead of re-raised — a
 * detached popover is hidden by the browser anyway. A closed root is skipped: for a toast
 * container :popover-open IS the has-content signal, so re-raising a closed one would pop an empty
 * overlay above everything.
 */
function reRaise(filter) {
    for (const [root, entry] of ordered()) {
        if (!root.isConnected) {
            pinned.delete(root);
            continue;
        }
        if (!filter(root, entry)) continue;
        if (!isOpen(root)) continue;
        reShow(root);
    }
}

function reShow(root) {
    try {
        root.hidePopover();
        root.showPopover();
    }
    catch {
        // InvalidStateError — the element is being detached; the browser hides a dead top-layer
        // element on its own.
    }
}

/**
 * Shows a popover root in the browser top layer, no-op when it is already open or the element is
 * being detached.
 * @param {HTMLElement} root the overlay root
 */
function show(root) {
    try {
        if (!isOpen(root)) {
            root.showPopover();
        }
    }
    catch {
        // InvalidStateError — the element is being detached; the browser hides a dead top-layer
        // element on its own.
    }
}

/**
 * Re-raises every pinned root that is open or has a deferred show, in registry order. Used by the
 * native-dialog close listener and by tm-pin-on-top.js after it changed the pinned set. A no-op
 * while a native modal dialog is still open.
 */
export function syncPinnedRoots() {
    if (isNativeModalOpen()) return;
    for (const [root, entry] of ordered()) {
        if (!root.isConnected) {
            pinned.delete(root);
            continue;
        }
        const deferred = entry.pending;
        const wanted = deferred || isOpen(root);
        entry.pending = false;
        if (!wanted || !supportsPopover(root)) continue;
        if (isOpen(root)) reShow(root);
        else show(root);
        if (deferred) notifyShown(root);
    }
}

/**
 * Tells the owner of a root whose show a native modal deferred that it is now on screen: a
 * `tm-pin-shown` event on the root (TmToastContainer re-announces its visible toasts — the live
 * regions of a hidden container were never spoken). No-op without CustomEvent/dispatchEvent.
 */
function notifyShown(root) {
    try {
        if (typeof root.dispatchEvent === 'function' && typeof CustomEvent === 'function') {
            root.dispatchEvent(new CustomEvent('tm-pin-shown'));
        }
    }
    catch {
        // A listener threw or the root is being detached — the show itself already happened.
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
 * API is missing. After its own promotion every PINNED root that is open is re-raised above it, so
 * pinned status UI stays above every modal surface, always (F3 review round 3, R3-M1).
 *
 * Promoting a PINNED root ranks it inside its priority band (pins of a higher priority re-raise
 * above it). While a native modal dialog is open a pinned root is not shown — the show is deferred
 * to the close re-sync — and no pin is re-raised above the newly promoted surface.
 * @param {HTMLElement} root the overlay root (a drawer focus-scope element, a modal overlay, the
 *   command palette backdrop, the toast container, a pinned host element, …)
 */
export function promote(root) {
    if (!supportsPopover(root)) {
        return;
    }
    const entry = pinned.get(root);
    if (entry && isNativeModalOpen()) {
        entry.pending = true;
        return;
    }
    show(root);
    if (isNativeModalOpen()) return;
    if (entry) {
        reRaise((other, otherEntry) => other !== root && otherEntry.priority > entry.priority);
    }
    else {
        reRaise((other) => other !== root);
    }
}

/**
 * Demotes a promoted root (hidePopover) and cancels a show a native modal deferred. No-op where the
 * Popover API is missing or the element is not currently promoted. Callers whose root STAYS in the
 * DOM (the toast containers, an observed pin the host hid) demote on close; an overlay that
 * unmounts on close (drawer/modal/dialog) needs nothing — a detached top-layer element is hidden by
 * the browser automatically.
 * @param {HTMLElement} root the overlay root
 */
export function demote(root) {
    const entry = root ? pinned.get(root) : undefined;
    if (entry) entry.pending = false;
    if (!root || typeof root.hidePopover !== 'function') {
        return;
    }
    try {
        if (isOpen(root)) {
            root.hidePopover();
        }
    }
    catch {
        // InvalidStateError — the element is being detached; nothing to demote.
    }
}

/**
 * Re-raises a promoted root above everything promoted after it (hidePopover + showPopover in one
 * synchronous step, so no frame paints without it). The pinned roots of the same or a lower
 * priority re-raise FIRST, the pusher next, and the pinned roots of a higher priority LAST — the
 * toast that just arrived tops its own band but never a higher one. TmToastContainer calls this
 * on every push, so a toast always lands above sheets and dialogs opened before it (they stay
 * promoted underneath). While a native modal dialog is open a pinned pusher is deferred to the
 * close re-sync instead.
 * @param {HTMLElement} root the overlay root
 */
export function raise(root) {
    if (!supportsPopover(root)) {
        return;
    }
    const entry = pinned.get(root);
    if (isNativeModalOpen()) {
        if (entry) entry.pending = true;
        else { demote(root); show(root); }
        return;
    }
    const band = entry ? entry.priority : Number.POSITIVE_INFINITY;
    reRaise((other, otherEntry) => other !== root && otherEntry.priority <= band);
    try {
        if (isOpen(root)) root.hidePopover();
    }
    catch {
        // InvalidStateError — the element is being detached.
    }
    show(root);
    if (entry) entry.pending = false;
    reRaise((other, otherEntry) => other !== root && otherEntry.priority > band);
}

/** Drops the registry and the close-listener flag. The node tests call it between cases. */
export function __resetForTests() {
    pinned.clear();
    registry.nextSeq = 0;
    registry.closeListenerInstalled = false;
}
