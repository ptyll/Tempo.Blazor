// Tempo.Blazor bottom sheet (ES module).
//
// Two jobs, deliberately separate. trackViewport writes the visible viewport and the keyboard gap as
// lengths on the sheet root, so the stylesheet can lift the sheet above an on-screen keyboard without
// knowing which component owns it. attachGesture follows a pointer on the handle and reports the snap
// it settled on; it never writes the height. C# owns the snap index, and an inline height written
// here would freeze the sheet at whatever the last gesture set.
//
// Heights are fractions of the visible viewport, matching --tm-sheet-height. A sheet with no snap
// points sizes to its content under the max-height cap, so a short menu is not forced to half height.

/**
 * The snap a released sheet settles to.
 * @param {number[]} snaps fractions of the viewport height, ascending; empty means content height
 * @param {number} released the fraction the finger released at
 * @param {boolean} swipeToDismiss whether a release below the lowest snap closes the sheet
 * @returns {{ dismiss: boolean, snap: number|null, index: number }} the snap to settle to
 */
export function settle(snaps, released, swipeToDismiss, velocity = 0) {
    if (!Array.isArray(snaps) || snaps.length === 0) {
        return { dismiss: false, snap: null, index: -1 };
    }

    if (snaps.length === 0) {
        return { dismiss: false, snap: null, index: -1 };
    }

    const lowest = snaps[0];
    // A one-snap sheet dismisses from a real drag (below the snap). A multi-snap sheet dismisses
    // only past the lowest snap by a margin, or on a downward flick — a release between snaps
    // settles to the nearest one.
    const dismissAt = snaps.length === 1 ? lowest : lowest - 0.15;
    if (swipeToDismiss && (released < dismissAt || velocity > 0.5)) {
        return { dismiss: true, snap: lowest, index: 0 };
    }

    let nearest = snaps[0];
    let index = 0;
    for (let i = 1; i < snaps.length; i++) {
        if (Math.abs(snaps[i] - released) < Math.abs(nearest - released)) {
            nearest = snaps[i];
            index = i;
        }
    }
    return { dismiss: false, snap: nearest, index };
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
const INTERACTIVE = 'button, a, input, select, textarea, [role="button"]';

function isInteractive(target) {
    return !!target && typeof target.closest === 'function' && !!target.closest(INTERACTIVE);
}

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
        const viewportNow = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        startFraction = viewportNow > 0 ? startHeight / viewportNow : 0;
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

        // Measure before clearing: resetInline drops the inline height, and a read after it sees 0.
        const viewport = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        const height = panel.getBoundingClientRect().height;
        const now = event.timeStamp ?? samples.at(-1)?.time ?? 0;
        const recent = samples.filter(sample => sample.time >= now - 80);
        const origin = recent[0];
        const elapsed = origin ? Math.max(1, now - origin.time) : 1;
        const velocity = origin ? (event.clientY - origin.y) / elapsed : 0;
        resetInline();
        if (cancelled || !wasArmed) return;

        const released = viewport > 0 ? height / viewport : 0;
        // A content sheet has no snaps. It dismisses only on a real downward drag — past a quarter
        // of the height it started at — or a flick. An upward drag never dismisses.
        if (sorted.length === 0) {
            if (swipeToDismiss && (released < startFraction * 0.75 || velocity > 0.5)) {
                host?.invokeMethodAsync('HandleSheetDismissedAsync').catch(() => {});
            }
            return;
        }
        const result = settle(sorted, released, swipeToDismiss, velocity);
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
