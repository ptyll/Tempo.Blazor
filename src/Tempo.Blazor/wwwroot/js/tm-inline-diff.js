// tm-inline-diff.js — overflow tab stops for TmInlineDiff Whole layout.
//
// Each Whole box is a height-capped scroller. A hardcoded tabindex="0" would mint an extra
// tab stop on a short value that never overflows. This module measures scrollHeight vs
// clientHeight and sets tabindex / role / aria-label ONLY while a box actually overflows.
// A 1px tolerance matches tm-focus-trap.js syncScrollRegion so sub-pixel rounding cannot
// mint a tab stop.
//
// Contract with the component:
//   syncOverflow(id, newBox, newLabel, oldBox, oldLabel)
//   release(id)
//
// State is one session per caller id. syncOverflow is idempotent (re-sync replaces the
// observers). Node tests: __tests__/tm-inline-diff.test.mjs.

const sessions = new Map();

function overflows(element) {
    if (!element) return false;
    return element.scrollHeight > element.clientHeight + 1;
}

function applyBox(element, label) {
    if (!element) return;
    if (overflows(element)) {
        element.setAttribute('tabindex', '0');
        element.setAttribute('role', 'region');
        if (label) {
            element.setAttribute('aria-label', label);
        } else {
            element.removeAttribute('aria-label');
        }
    } else {
        element.removeAttribute('tabindex');
        element.removeAttribute('role');
        element.removeAttribute('aria-label');
    }
}

function stopSession(session) {
    if (!session || session.stopped) return;
    session.stopped = true;
    if (session.applyHandle != null && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(session.applyHandle);
        session.applyHandle = null;
    }
    session.observer?.disconnect();
    session.observer = null;
    applyBox(session.newBox, null);
    applyBox(session.oldBox, null);
    session.newBox?.removeAttribute('tabindex');
    session.oldBox?.removeAttribute('tabindex');
    session.newBox?.removeAttribute('role');
    session.oldBox?.removeAttribute('role');
    session.newBox?.removeAttribute('aria-label');
    session.oldBox?.removeAttribute('aria-label');
}

function schedule(session) {
    if (session.stopped || session.applyScheduled) return;
    if (typeof requestAnimationFrame !== 'function') {
        session.apply();
        return;
    }
    session.applyScheduled = true;
    session.applyHandle = requestAnimationFrame(() => {
        session.applyScheduled = false;
        session.applyHandle = null;
        if (!session.stopped) session.apply();
    });
}

/**
 * Starts (or replaces) overflow observation of the two Whole boxes.
 * @param {string} id caller key so a re-attach replaces the observers
 * @param {HTMLElement} newBox the proposed-value scroller
 * @param {string|null} newLabel accessible name used while the box overflows
 * @param {HTMLElement} oldBox the current-value scroller
 * @param {string|null} oldLabel accessible name used while the box overflows
 */
export function syncOverflow(id, newBox, newLabel, oldBox, oldLabel) {
    release(id);
    if (!id) return;
    const session = {
        newBox, newLabel, oldBox, oldLabel,
        observer: null, apply: null,
        applyScheduled: false, applyHandle: null, stopped: false,
    };
    session.apply = () => {
        applyBox(session.newBox, session.newLabel);
        applyBox(session.oldBox, session.oldLabel);
    };
    session.apply();
    if (typeof ResizeObserver === 'function') {
        session.observer = new ResizeObserver(() => schedule(session));
        if (newBox) session.observer.observe(newBox);
        if (oldBox) session.observer.observe(oldBox);
        for (const child of newBox?.children ?? []) session.observer.observe(child);
        for (const child of oldBox?.children ?? []) session.observer.observe(child);
    }
    sessions.set(id, session);
}

/** Stops observing and strips overflow attributes. Idempotent. */
export function release(id) {
    const session = sessions.get(id);
    if (!session) return;
    sessions.delete(id);
    stopSession(session);
}
