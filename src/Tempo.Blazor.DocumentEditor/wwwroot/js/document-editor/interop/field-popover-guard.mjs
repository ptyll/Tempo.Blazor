// The form-field popover is anchored to a VIEWPORT rectangle the canvas engine reported when the field was selected (redesign
// 146c7e1b, phase 5). A rectangle in the viewport does not follow the page: when the canvas scrolls (or the window resizes) the popover
// would hang over the wrong text. The core overlay cannot track it - the anchor is a fixed element, not a descendant of a scroller - so
// the popover closes: this module reports the first scroll of the document (capture phase: scroll does not bubble) and the first
// window / visual-viewport resize, throttled to one report per animation frame, and the component closes itself.
//
// A short grace period after attach ignores the scroll the engine itself performs to bring the caret into view right after the field
// was selected; a scroll INSIDE the popover (its own body) never counts.

export const GRACE_MS = 250;
const PANEL_SELECTOR = '.tm-document-floating-panel, .tm-overlay-panel-sheet';

const watchers = new Map();

/** Whether a scroll target is part of the popover itself (its body scrolls on a short viewport). */
export function isInsidePanel(target) {
    return !!target && typeof target.closest === 'function' && !!target.closest(PANEL_SELECTOR);
}

/**
 * Starts reporting viewport moves. The callback runs at most once per animation frame, never inside the grace period, and never for
 * a scroll that happens inside the popover. Returns false when the key is already watched (idempotent).
 * `host` carries the document/window/rAF/clock so the node tests run without a DOM.
 */
export function watch(key, callback, host = globalThis, now = () => Date.now()) {
    if (watchers.has(key)) {
        return false;
    }

    const started = now();
    let frame = 0;
    const report = () => {
        frame = 0;
        callback();
    };
    const schedule = () => {
        if (frame || now() - started < GRACE_MS) {
            return;
        }

        frame = (host.requestAnimationFrame ?? ((fn) => setTimeout(fn, 16)))(report);
    };
    const onScroll = (event) => {
        if (!isInsidePanel(event?.target)) {
            schedule();
        }
    };

    host.document?.addEventListener?.('scroll', onScroll, true);
    host.addEventListener?.('resize', schedule);
    host.visualViewport?.addEventListener?.('resize', schedule);
    watchers.set(key, () => {
        host.document?.removeEventListener?.('scroll', onScroll, true);
        host.removeEventListener?.('resize', schedule);
        host.visualViewport?.removeEventListener?.('resize', schedule);
        if (frame) {
            (host.cancelAnimationFrame ?? clearTimeout)(frame);
            frame = 0;
        }
    });
    return true;
}

/** Stops reporting. Idempotent. */
export function unwatch(key) {
    const stop = watchers.get(key);
    if (!stop) {
        return false;
    }

    watchers.delete(key);
    stop();
    return true;
}

/** Blazor entry: reports through a DotNetObjectReference. */
export function watchViewport(key, dotNetRef, method) {
    return watch(key, () => { dotNetRef.invokeMethodAsync(method).catch(() => {}); });
}
