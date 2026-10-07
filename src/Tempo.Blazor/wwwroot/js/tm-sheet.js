// Tempo.Blazor bottom sheet gesture (ES module).
//
// A bottom drawer follows the finger while it is dragged and settles to a snap point on release.
// The drag is a pointer gesture, which Blazor cannot track without a per-move render, so it lives
// here. The decision of where the sheet settles is pure, so the node tests can prove it without a
// browser: a release past the lowest snap dismisses, anything else snaps to the nearest point, and a
// sheet that opted out of swipe-to-dismiss never dismisses.
//
// Heights are fractions of the viewport, matching the --tm-sheet-height custom property the
// stylesheet reads.

/**
 * The snap a released sheet settles to.
 * @param {number[]} snaps fractions of the viewport height, ascending
 * @param {number} released the fraction the finger released at
 * @param {boolean} swipeToDismiss whether a release below the lowest snap closes the sheet
 * @returns {{ dismiss: boolean, snap: number }} the snap to settle to, or dismiss when it closes
 */
export function settle(snaps, released, swipeToDismiss) {
    if (!Array.isArray(snaps) || snaps.length === 0) {
        throw new Error('A sheet needs at least one snap point.');
    }

    const lowest = snaps[0];
    if (swipeToDismiss && released < lowest) {
        return { dismiss: true, snap: lowest };
    }

    const nearest = snaps.reduce((best, snap) =>
        Math.abs(snap - released) < Math.abs(best - released) ? snap : best);
    return { dismiss: false, snap: nearest };
}

/**
 * How an open keyboard changes the sheet. The visible height is measured rather than read from
 * 100dvh, because a faked visualViewport shrinks visualViewport.height without shrinking dvh.
 * Both values are unitless pixels: the stylesheet multiplies them by 1px, and a calc() cannot
 * subtract a px length from a unitless product.
 * @param {number} layoutHeight window.innerHeight
 * @param {{ height: number, offsetTop: number }} viewport window.visualViewport
 * @returns {{ viewport: number, keyboard: number }} the visible height and the hidden gap
 */
export function keyboardOffset(layoutHeight, viewport) {
    const hidden = Math.max(0, layoutHeight - viewport.height - viewport.offsetTop);
    return { viewport: viewport.height, keyboard: hidden };
}

const sheets = new Map();

/**
 * Attaches the drag gesture to a sheet. The handle is what the finger grabs; the panel is what
 * moves. A host that already disposed the element simply gets nothing.
 * @param {HTMLElement} handle the drag handle
 * @param {HTMLElement} panel the sheet panel
 * @param {number[]} snaps fractions of the viewport height
 * @param {boolean} swipeToDismiss whether a release below the lowest snap closes the sheet
 * @param {{ invokeMethodAsync: Function }} host the component to tell about a dismiss
 * @param {string} id the sheet's id, so a second attach replaces the first
 */
export function attach(handle, panel, snaps, swipeToDismiss, host, id) {
    if (!handle || !panel) return;
    detach(id);

    let startY = 0;
    let startHeight = 0;
    let dragging = false;

    const onDown = (event) => {
        dragging = true;
        startY = event.clientY;
        startHeight = panel.getBoundingClientRect().height;
        panel.style.transition = 'none';
    };

    const onMove = (event) => {
        if (!dragging) return;
        const viewport = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        const next = Math.max(0, startHeight - (event.clientY - startY));
        panel.style.height = `${next}px`;
        panel.style.setProperty('--tm-sheet-height', String(next / viewport));
    };

    const onUp = (event) => {
        if (!dragging) return;
        dragging = false;
        panel.style.transition = '';
        const viewport = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        const released = panel.getBoundingClientRect().height / viewport;
        const result = settle(snaps, released, swipeToDismiss);
        if (result.dismiss) {
            host?.invokeMethodAsync('HandleSheetDismissedAsync').catch(() => {});
            return;
        }
        panel.style.height = '';
        panel.style.setProperty('--tm-sheet-height', String(result.snap));
    };

    // The on-screen keyboard shrinks the visual viewport, and a fixed sheet does not follow it, so the
    // footer slides under the keyboard. The offset is the gap between the layout viewport and the
    // visible one. It is a custom property, never an inline height: the stylesheet subtracts it from
    // the snap height, and an inline height would freeze the sheet at whatever the gesture last set.
    const onViewport = () => {
        const viewport = window.visualViewport;
        if (!viewport) return;
        const offset = keyboardOffset(window.innerHeight, viewport);
        const hidden = offset.keyboard;
        // The offset goes on the sheet root, not the panel. The height rule reads the variable
        // through inheritance, and a panel-level variable would not reach a rule that selects the
        // panel from an ancestor (the drawer's root carries the sheet class, the panel does not).
        //
        // The visible height is measured here rather than read from 100dvh. A faked visualViewport
        // (a test, an embedded frame) shrinks visualViewport.height without shrinking dvh, so a
        // stylesheet that subtracted the offset from 100dvh would clamp the sheet to nothing.
        const root = panel.closest('.tm-drawer, .tm-modal-overlay') ?? panel;
        // Unitless, like the viewport. The stylesheet multiplies the whole difference by 1px;
        // a px value here would make the subtraction invalid.
        root.style.setProperty('--tm-sheet-keyboard', String(hidden));
        // Unitless. A calc() can multiply two numbers but not a number by a px length, so the
        // stylesheet turns this back into pixels itself.
        root.style.setProperty('--tm-sheet-viewport', String(offset.viewport));
    };

    handle.addEventListener('pointerdown', onDown);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    // Both events: visualViewport fires for a real keyboard, and a host that only resizes the window
    // (a test, an embedded frame) still moves the sheet.
    if (window.visualViewport) window.visualViewport.addEventListener('resize', onViewport);
    window.addEventListener('resize', onViewport);
    onViewport();
    sheets.set(id, { handle, onDown, onMove, onUp, onViewport });
}

/** Removes the gesture listeners for a sheet. Safe to call twice. */
export function detach(id) {
    const sheet = sheets.get(id);
    if (!sheet) return;
    sheets.delete(id);
    sheet.handle.removeEventListener('pointerdown', sheet.onDown);
    window.removeEventListener('pointermove', sheet.onMove);
    window.removeEventListener('pointerup', sheet.onUp);
    if (sheet.onViewport) {
        if (window.visualViewport) window.visualViewport.removeEventListener('resize', sheet.onViewport);
        window.removeEventListener('resize', sheet.onViewport);
    }
}
