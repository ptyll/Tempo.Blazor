// Tempo.Blazor bottom sheet (ES module).
//
// Two jobs, deliberately separate. trackViewport writes the visible viewport and the keyboard gap as
// lengths on the sheet root, so the stylesheet can lift the sheet above an on-screen keyboard without
// knowing which component owns it. attachGesture follows a pointer on the handle and reports the snap
// it settled on; it never writes the height. C# owns the snap index, and an inline height written
// here would freeze the sheet at whatever the last gesture set.
//
// The module also owns the shared top-layer promote/demote/raise helpers (TopLayerInterop). Every
// modal viewport-anchored overlay root carries popover="manual" and promotes itself at open; the
// pinned-on-top registry keeps toast containers above every modal surface (F3 review round 3, R3-M1).
//
// Heights are fractions of the visible viewport, matching --tm-sheet-height. A sheet with no snap
// points sizes to its content under the max-height cap, so a short menu is not forced to half height.

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

/**
 * The snap a released sheet settles to.
 * @param {number[]} snaps fractions of the viewport height, ascending; empty means content height
 * @param {number} released the fraction the finger released at
 * @param {boolean} swipeToDismiss whether a release below the lowest snap closes the sheet
 * @param {number} velocity px/ms over the last ~80ms of the drag; negative is upward
 * @param {number|null} startFraction the fraction the drag started at, so a flick knows which snap
 *   it leaves; null falls back to the released fraction
 * @param {boolean=} downward the NET direction of the drag (start Y vs release Y), which decides
 *   whether a flick steps down or up; a caller that omits it gets the velocity's sign, the
 *   pre-round-5 behaviour
 * @returns {{ dismiss: boolean, snap: number|null, index: number }} the snap to settle to
 */
export function settle(snaps, released, swipeToDismiss, velocity = 0, startFraction = null, downward) {
    if (!Array.isArray(snaps) || snaps.length === 0) {
        return { dismiss: false, snap: null, index: -1 };
    }

    const lowest = snaps[0];
    const startIndex = nearestIndex(snaps, startFraction ?? released);
    // The tail flick may point the other way than the drag as a whole (drag up to grow, then a fast
    // downward flick of the last 80ms). A decision recorded for the drawer: a net-upward drag never
    // dismisses, and its tail flick steps UP, not down.
    const netDownward = downward ?? velocity >= 0;
    // A multi-snap sheet dismisses past the lowest snap by a margin. A downward flick steps ONE
    // snap in its direction from the snap it started on — from a higher snap that is a step down,
    // because a 300px/300ms swipe is a "back to half" gesture, not a close. Only a very fast
    // release (> 2 px/ms, vaul's behaviour) closes the sheet from any snap. Every dismiss here
    // honours swipeToDismiss: with the flag off, a flick from the lowest snap clamps to it and a
    // fast flick settles at it. A one-snap sheet is not decided here: its dismiss is relative to
    // the height it started at.
    if (snaps.length > 1 && swipeToDismiss && released < lowest - 0.15) {
        return { dismiss: true, snap: lowest, index: 0 };
    }
    if (snaps.length > 1 && netDownward && swipeToDismiss && velocity > 2) {
        return { dismiss: true, snap: lowest, index: 0 };
    }
    if (snaps.length > 1 && netDownward && velocity > 0.5) {
        if (startIndex === 0 && swipeToDismiss) return { dismiss: true, snap: lowest, index: 0 };
        const down = Math.max(0, startIndex - 1);
        return { dismiss: false, snap: snaps[down], index: down };
    }
    if (snaps.length > 1 && !netDownward && velocity < -0.5) {
        const up = Math.min(startIndex + 1, snaps.length - 1);
        return { dismiss: false, snap: snaps[up], index: up };
    }

    const index = nearestIndex(snaps, released);
    return { dismiss: false, snap: snaps[index], index };
}

/** The index of the snap closest to the value. */
function nearestIndex(snaps, value) {
    let index = 0;
    for (let i = 1; i < snaps.length; i++) {
        if (Math.abs(snaps[i] - value) < Math.abs(snaps[index] - value)) index = i;
    }
    return index;
}

/**
 * How an open keyboard changes the sheet. The visible height is measured rather than read from
 * 100dvh, because a faked visualViewport shrinks visualViewport.height without shrinking dvh.
 * Both values are lengths: the stylesheet reads them directly, and a sheet that has not imported
 * this module yet falls back to the lengths the stylesheet declares.
 * @param {number} layoutHeight window.innerHeight
 * @param {{ height: number, offsetTop: number }} viewport window.visualViewport
 * @returns {{ viewport: string, keyboard: string }} the visible height and the hidden gap
 */
export function keyboardOffset(layoutHeight, viewport) {
    const hidden = Math.max(0, layoutHeight - viewport.height - viewport.offsetTop);
    return { viewport: `${viewport.height}px`, keyboard: `${hidden}px` };
}

/**
 * Writes the visible viewport and the keyboard gap onto the sheet root, and keeps them current.
 * Listens to visualViewport resize and scroll: iOS moves the visual viewport with offsetTop on
 * scroll rather than resize. Returns a function that removes the listeners.
 * @param {HTMLElement} root the sheet root
 * @returns {() => void} stop
 */
export function trackViewport(root, host) {
    if (!root) return () => {};

    const apply = () => {
        const viewport = window.visualViewport;
        if (!viewport) return;
        const offset = keyboardOffset(window.innerHeight, viewport);
        root.style.setProperty('--tm-sheet-viewport', offset.viewport);
        root.style.setProperty('--tm-sheet-keyboard', offset.keyboard);
        const open = Number.parseFloat(offset.keyboard) > 0;
        if (root.classList) {
            if (root.classList.contains('tm-sheet--keyboard') === open) return;
            root.classList.toggle('tm-sheet--keyboard', open);
            host?.invokeMethodAsync('HandleSheetKeyboardAsync', open).catch(() => {});
        }
    };

    apply();
    if (window.visualViewport) {
        window.visualViewport.addEventListener('resize', apply);
        window.visualViewport.addEventListener('scroll', apply);
    }
    window.addEventListener('resize', apply);

    return () => {
        if (window.visualViewport) {
            window.visualViewport.removeEventListener('resize', apply);
            window.visualViewport.removeEventListener('scroll', apply);
        }
        window.removeEventListener('resize', apply);
    };
}

const gestures = new Map();

/**
 * Attaches the drag gesture to a handle. The panel is what moves while the finger is down; on
 * release the inline height and transition are cleared and the host is told which snap to render.
 * Pointer capture keeps the gesture alive when the finger leaves the handle, and a pointercancel
 * restores the snap C# already owns instead of leaving the sheet mid-drag.
 * @param {HTMLElement} handle the drag handle, or the header when the sheet has no grabber
 * @param {HTMLElement} panel the sheet panel
 * @param {number[]} snaps fractions of the viewport height, already sorted
 * @param {boolean} swipeToDismiss whether a release below the lowest snap closes the sheet
 * @param {{ invokeMethodAsync: Function }|null} host the component to tell about a snap or a dismiss
 * @param {string} id the sheet's id, so a second attach replaces the first
 */
export function attachGesture(handle, panel, snaps, swipeToDismiss, host, id, track = true) {
    if (!panel) return;
    detach(id);

    const sorted = Array.isArray(snaps) ? snaps : [];
    let startY = 0;
    let startHeight = 0;
    let startFraction = 0;
    let dragging = false;
    let armed = false;
    let pointerId = null;
    // Recent pointer samples. A real pointerup lands on the last move, so velocity taken from that
    // move alone is always 0 and a flick never dismisses. The window is the last ~80ms.
    const samples = [];

    const remember = (y, time) => {
        samples.push({ y, time });
        const cutoff = time - 80;
        while (samples.length > 1 && samples[0].time < cutoff) samples.shift();
    };

    // An inline sheet is a fraction of its host, not of the viewport. A modal sheet tracks the
    // visible viewport. Measured at the call, so a release sees the host as it is now.
    const basis = () => {
        if (!track) {
            const hostBox = panel.closest?.('.tm-drawer')?.getBoundingClientRect?.().height
                ?? panel.parentElement?.clientHeight;
            if (hostBox > 0) return hostBox;
        }
        return window.visualViewport ? window.visualViewport.height : window.innerHeight;
    };

    const resetInline = () => {
        // height and transition only. --tm-sheet-height is Blazor-owned (TmDrawer.PanelStyle);
        // deleting it collapses a snap Blazor will not rewrite, because the index did not change.
        panel.style.height = '';
        panel.style.transition = '';
    };

    const capture = (event) => {
        if (typeof handle.setPointerCapture === 'function' && event.pointerId !== undefined) {
            try { handle.setPointerCapture(event.pointerId); } catch { /* already released */ }
        }
    };

    const onDown = (event) => {
        if (event.button !== undefined && event.button !== 0) return;
        dragging = true;
        // Never arm on pointerdown. A tap, and a press on the close button, stay a click until
        // the finger actually moves. Arming immediately dismissed a content sheet on a tap.
        armed = false;
        pointerId = event.pointerId;
        startY = event.clientY;
        samples.length = 0;
        remember(event.clientY, event.timeStamp ?? 0);
        startHeight = panel.getBoundingClientRect().height;
        // A press on the handle itself captures immediately. A fast drag whose first move leaves
        // the 44px handle would otherwise never arm. A header press still waits for the 6px arm,
        // so a click on the close button stays a click.
        if (event.target === handle || event.target?.closest?.('.tm-sheet__handle') === handle) capture(event);
        const basisNow = basis();
        startFraction = basisNow > 0 ? startHeight / basisNow : 0;
    };

    const onMove = (event) => {
        if (!dragging || (pointerId !== null && event.pointerId !== pointerId)) return;
        if (!armed) {
            // A press on the close button stays a click until the finger actually moves.
            if (Math.abs(event.clientY - startY) < 6) return;
            armed = true;
            panel.style.transition = 'none';
            capture(event);
        }
        remember(event.clientY, event.timeStamp ?? samples.at(-1)?.time ?? 0);
        const next = Math.max(0, startHeight - (event.clientY - startY));
        panel.style.height = `${next}px`;
    };

    const finish = (event, cancelled) => {
        if (!dragging || (pointerId !== null && event.pointerId !== undefined && event.pointerId !== pointerId)) return;
        const wasArmed = armed;
        dragging = false;
        armed = false;
        pointerId = null;

        // A tap never armed. Leave the inline styles alone: clearing them is a no-op, but a click
        // that follows pointerup must still land on the grabber, not on a backdrop the panel
        // collapsed onto.
        if (!wasArmed) return;

        // Measure before clearing: resetInline drops the inline height, and a read after it sees 0.
        const viewport = basis();
        const height = panel.getBoundingClientRect().height;
        const now = event.timeStamp ?? samples.at(-1)?.time ?? 0;
        const recent = samples.filter(sample => sample.time >= now - 80);
        const origin = recent[0];
        const elapsed = origin ? Math.max(1, now - origin.time) : 1;
        const velocity = origin ? (event.clientY - origin.y) / elapsed : 0;
        resetInline();
        if (cancelled || !wasArmed) return;

        const released = viewport > 0 ? height / viewport : 0;
        // An upward drag never dismisses, whatever the velocity sample says. The start fraction
        // tells settle which snap the gesture left, so a flick steps from there.
        const downward = (event.clientY - startY) > 0;
        // A content sheet, and a one-snap sheet, dismiss only past a quarter of the height they
        // started at, or on a downward flick. A multi-snap sheet uses the lowest-snap margin and
        // steps one snap per flick.
        if (sorted.length <= 1) {
            if (swipeToDismiss && downward && (released < startFraction * 0.75 || velocity > 0.5)) {
                host?.invokeMethodAsync('HandleSheetDismissedAsync').catch(() => {});
            }
            else if (sorted.length === 1) {
                host?.invokeMethodAsync('HandleSheetSnappedAsync', 0).catch(() => {});
            }
            return;
        }
        const result = settle(sorted, released, swipeToDismiss, velocity, startFraction, downward);
        if (result.dismiss) {
            host?.invokeMethodAsync('HandleSheetDismissedAsync').catch(() => {});
            return;
        }
        host?.invokeMethodAsync('HandleSheetSnappedAsync', result.index).catch(() => {});
    };

    const onUp = (event) => finish(event, false);
    const onCancel = (event) => finish(event, true);
    if (handle) {
        handle.addEventListener('pointerdown', onDown);
        handle.addEventListener('pointermove', onMove);
        handle.addEventListener('pointerup', onUp);
        handle.addEventListener('pointercancel', onCancel);
    }

    const stopViewport = track
        ? trackViewport(panel.closest?.('.tm-sheet, .tm-drawer, .tm-modal-overlay') ?? panel, host)
        : () => {};
    gestures.set(id, { handle, onDown, onMove, onUp, onCancel, stopViewport });
}

/** Removes the gesture and the viewport tracking for a sheet. Safe to call twice. */
export function detach(id) {
    const gesture = gestures.get(id);
    if (!gesture) return;
    gestures.delete(id);
    if (gesture.handle) {
        gesture.handle.removeEventListener('pointerdown', gesture.onDown);
        gesture.handle.removeEventListener('pointermove', gesture.onMove);
        gesture.handle.removeEventListener('pointerup', gesture.onUp);
        gesture.handle.removeEventListener('pointercancel', gesture.onCancel);
    }
    gesture.stopViewport?.();
}

/** @deprecated Use attachGesture. Kept so a host that imported attach before the split still works. */
export function attach(handle, panel, snaps, swipeToDismiss, host, id) {
    attachGesture(handle, panel, snaps, swipeToDismiss, host, id);
}
