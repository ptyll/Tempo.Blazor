// Tempo.Blazor.AI — the .NET-facing half of TmAutonomyDial's scroll guard (ES module).
//
// A `role="slider"` is a div: the browser gives it no behaviour of its own, and the one thing it DOES do — scroll the
// page on ArrowUp/ArrowDown/Home/End — is exactly what a reader driving the dial does not want. Blazor cannot fix that
// from C#: `@onkeydown:preventDefault` is unconditional per element, so it would also swallow Tab, Escape and every other
// key the surface must keep.
//
// This module owns the conditional part and NOTHING else: it listens for `keydown` on the slider element and calls
// preventDefault for the six keys that belong to the dial. It never stops propagation, so Blazor still receives the same
// event and the component's own keyboard model is untouched. Without JavaScript the dial keeps working — only the page
// may scroll along with it.
//
// The listener is idempotent per element (a property on the node), so a re-render that hands the module the same slider
// twice cannot stack two handlers.

/** The keys the dial owns: the four arrows and the two ends. Every other key keeps its browser behaviour. */
export const DIAL_KEYS = Object.freeze(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End']);

const DIAL_KEY_SET = new Set(DIAL_KEYS);

const HANDLER_KEY = Symbol.for('tempo.autonomyDial.keyGuard');

/**
 * Whether a key belongs to the dial, i.e. whether the browser must be told NOT to scroll with it. Pure, so the node test
 * pins the decision without a DOM.
 * @param {string} key the `KeyboardEvent.key` value
 * @returns {boolean} true for the four arrows and Home/End
 */
export function shouldPreventScroll(key) {
    return typeof key === 'string' && DIAL_KEY_SET.has(key);
}

/**
 * The `keydown` handler the guard installs. Exported so a node test can drive it directly with a fake event.
 * @param {{key?: string, preventDefault?: Function}} event the keyboard event
 * @returns {boolean} whether the default was prevented
 */
export function handleKeyDown(event) {
    if (!event || !shouldPreventScroll(event.key)) {
        return false;
    }

    if (typeof event.preventDefault === 'function') {
        event.preventDefault();
    }

    return true;
}

/**
 * Installs the guard on ONE slider. Idempotent: a second call for the same element returns false and adds no listener.
 * A null or detached element (SSR, a segmented presentation, a missing ref) is a no-op, never a throw.
 * @param {Element|null} slider the focused `role="slider"` element
 * @returns {boolean} true when this call installed the guard
 */
export function attachDialKeys(slider) {
    if (!slider || typeof slider.addEventListener !== 'function') {
        return false;
    }

    if (slider[HANDLER_KEY]) {
        return false;
    }

    slider[HANDLER_KEY] = handleKeyDown;
    slider.addEventListener('keydown', handleKeyDown);
    return true;
}

/**
 * Removes the guard from one slider. Safe to call on an element that never carried one (a detached node, a double
 * dispose).
 * @param {Element|null} slider the element the guard was installed on
 * @returns {boolean} true when this call removed the guard
 */
export function detachDialKeys(slider) {
    if (!slider || typeof slider.removeEventListener !== 'function') {
        return false;
    }

    const handler = slider[HANDLER_KEY];
    if (!handler) {
        return false;
    }

    slider.removeEventListener('keydown', handler);
    try {
        delete slider[HANDLER_KEY];
    } catch {
        // A frozen element keeps the property; the listener is gone either way, which is what matters.
        slider[HANDLER_KEY] = null;
    }

    return true;
}
