// Tempo.Blazor visual-viewport tracker (ES module).
//
// An on-screen keyboard shrinks `window.visualViewport` while the layout viewport (and with it every
// fixed or sticky containing block) stays put, so a footer or a status bar pinned to the layout
// viewport sits UNDER the keyboard. attach() measures how far the keyboard reaches into the tracked
// element and writes the covered gap as the custom property `--tm-visual-viewport-inset-block-end` (a
// length) INLINE on that element, plus a `data-tm-keyboard-open` marker while a keyboard is assumed.
// tokens.css declares the default (0px) on :root, so every consumer reads a defined length and an
// element that is not tracked stays untouched; the inline value on the tracked element beats the
// inherited default.
//
// The gap is RELATIVE TO THE TRACKED ELEMENT: how far its bottom edge lies below the visual viewport's
// bottom edge. An element that ends above the keyboard (a frame in a page with a footer under it) gets
// 0, never the full keyboard height. A shrink below MIN_INSET of the raw keyboard height is browser
// chrome, not a keyboard.
//
// Trackers are keyed by a STRING ID, not by the element: the owner (a Blazor component) detaches by the
// id it chose, so a node that Blazor already removed from the DOM can still be released, and a tracker
// whose element is gone self-detaches on its next tick. One implementation for every consumer
// (TmModal.TrackVirtualKeyboard, the DocumentEditor's phone frame); `options.property` lets another
// consumer (the AI composer) reuse it with its own knob. The write is coalesced into one animation frame
// and a pinch zoom keeps the last reading. Idempotent per id.

/** The raw keyboard height below which browser chrome (an address bar) is assumed instead of a keyboard. */
export const MIN_INSET = 80;

export const INSET_PROPERTY = '--tm-visual-viewport-inset-block-end';

const OPEN_ATTRIBUTE = 'data-tm-keyboard-open';

/** Trackers keyed by the owner's id. */
const trackers = new Map();

/**
 * The gap the keyboard covers below an element, in CSS pixels; 0 below the browser-chrome threshold.
 * @param {number} layoutHeight window.innerHeight
 * @param {{ height: number, offsetTop?: number, width?: number }|null|undefined} viewport window.visualViewport
 * @param {number} [minInset] the raw keyboard height below which no keyboard is assumed
 * @param {number|null} [elementBottom] the tracked element's bottom edge (client coordinates); null = the layout viewport's bottom
 */
export function computeInset(layoutHeight, viewport, minInset = MIN_INSET, elementBottom = null) {
    if (!viewport) {
        return 0;
    }

    if ((viewport.height || 0) < 2 || (viewport.width ?? 2) < 2) {
        return 0;
    }

    const visibleBottom = (viewport.offsetTop || 0) + (viewport.height || 0);
    const keyboard = Math.max(0, (layoutHeight || 0) - visibleBottom);
    if (keyboard < minInset) {
        return 0;
    }

    if (typeof elementBottom !== 'number' || !Number.isFinite(elementBottom)) {
        return Math.round(keyboard);
    }

    return Math.max(0, Math.round(elementBottom - visibleBottom));
}

/**
 * Starts tracking the keyboard for an element and applies the first reading at once. A second attach with
 * the same id replaces the first.
 * @param {HTMLElement} element the element that carries the property
 * @param {{ id: string, property?: string, minInset?: number }} options id is required
 */
export function attach(element, options) {
    if (!element?.style || typeof element.style.setProperty !== 'function') {
        throw new TypeError('tm-visual-viewport.js: an element is required.');
    }

    const id = options?.id == null ? '' : String(options.id);
    if (!id) {
        throw new TypeError('tm-visual-viewport.js: an id is required.');
    }

    detach(id);

    const property = typeof options?.property === 'string' && options.property ? options.property : INSET_PROPERTY;
    const minInset = Number.isFinite(options?.minInset) && options.minInset > 0 ? options.minInset : MIN_INSET;
    const viewport = typeof globalThis !== 'undefined' ? globalThis.visualViewport : null;
    let written = null;
    let frame = null;
    let removed = false;

    function currentInset() {
        if (!viewport) {
            return 0;
        }

        // A pinch zoom shrinks the visual viewport without a keyboard: keep what was last measured.
        if (typeof viewport.scale === 'number' && Math.abs(viewport.scale - 1) > 0.01) {
            return written ?? 0;
        }

        const bottom = element.getBoundingClientRect?.().bottom;
        return computeInset(globalThis.innerHeight || 0, viewport, minInset, typeof bottom === 'number' ? bottom : null);
    }

    function apply() {
        frame = null;
        if (removed) {
            return;
        }

        // The owner's node is gone (Blazor removed it without a detach): release the listeners instead of leaking them.
        if (element.isConnected === false) {
            detach(id);
            return;
        }

        const inset = currentInset();
        if (inset === written) {
            return;
        }

        written = inset;
        if (inset > 0) {
            element.style.setProperty(property, `${inset}px`);
            element.setAttribute?.(OPEN_ATTRIBUTE, '');
        } else {
            element.style.removeProperty(property);
            element.removeAttribute?.(OPEN_ATTRIBUTE);
        }
    }

    function schedule() {
        if (frame !== null || removed) {
            return;
        }

        if (typeof globalThis.requestAnimationFrame === 'function') {
            frame = globalThis.requestAnimationFrame(apply);
        } else {
            apply();
        }
    }

    viewport?.addEventListener?.('resize', schedule, { passive: true });
    viewport?.addEventListener?.('scroll', schedule, { passive: true });
    globalThis.addEventListener?.('resize', schedule, { passive: true });
    // The element's own bottom edge moves with the page scroll.
    globalThis.addEventListener?.('scroll', schedule, { passive: true });

    trackers.set(id, {
        element,
        property,
        remove() {
            removed = true;
            if (frame !== null) {
                globalThis.cancelAnimationFrame?.(frame);
                frame = null;
            }

            viewport?.removeEventListener?.('resize', schedule);
            viewport?.removeEventListener?.('scroll', schedule);
            globalThis.removeEventListener?.('resize', schedule);
            globalThis.removeEventListener?.('scroll', schedule);
        },
    });

    apply();
}

/**
 * Stops tracking: removes the listeners, the property and the marker. Safe to call twice and for an id that
 * was never attached.
 * @param {string} id the id given to attach()
 */
export function detach(id) {
    const key = id == null ? '' : String(id);
    const tracker = trackers.get(key);
    if (!tracker) {
        return;
    }

    tracker.remove();
    trackers.delete(key);
    tracker.element.style?.removeProperty?.(tracker.property);
    tracker.element.removeAttribute?.(OPEN_ATTRIBUTE);
}

/** Whether the id is currently tracked. */
export function isAttached(id) {
    return trackers.has(id == null ? '' : String(id));
}
