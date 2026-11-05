// Tempo.Blazor.AI — the .NET-facing half of TmAgentLanes' pointer zoom (ES module).
//
// The component owns the zoom LEVEL and paints the axis, the blocks and the "now" line from it; this module owns the
// one thing Blazor cannot express: the browser gesture that produces a scale. Ctrl+wheel (a trackpad pinch) and a
// two-finger pinch are NOT re-implemented here — they come from the core `_content/Tempo.Blazor/js/pointer-gesture.js`
// helper (see docs/touch-interaction.md), imported across the static-asset boundary exactly like the PDF viewer does.
// Plain wheel is left alone, so a reader who is not holding Ctrl still scrolls the page.
//
// WCAG 2.5.7: a pinch is a dragging movement, so the component ships +/−/fit buttons beside it — this module is an
// enhancement, never the only way to change the scale.

/** The default floor of the horizontal scale, in pixels per hour (mirrors the component's MinPixelsPerHour). */
export const DEFAULT_MIN_PIXELS_PER_HOUR = 24;

/** The default ceiling of the horizontal scale, in pixels per hour (mirrors the component's MaxPixelsPerHour). */
export const DEFAULT_MAX_PIXELS_PER_HOUR = 2400;

/**
 * Clamps a horizontal scale into the component's window. A value that is not a finite positive number falls back to
 * the floor, so a broken host state can never paint a zero-width timeline or a negative one.
 * @param {number} value the candidate scale in pixels per hour
 * @param {number} [min] the floor; a non-positive or non-finite value uses the default floor
 * @param {number} [max] the ceiling; anything not above the floor uses the default ceiling
 * @returns {number} the scale to paint with
 */
export function clampPixelsPerHour(value, min, max) {
    const floor = Number.isFinite(min) && min > 0 ? min : DEFAULT_MIN_PIXELS_PER_HOUR;
    const ceiling = Number.isFinite(max) && max > floor
        ? max
        : Math.max(floor, DEFAULT_MAX_PIXELS_PER_HOUR);
    const number = Number(value);

    if (!Number.isFinite(number) || number <= 0) {
        return floor;
    }

    return Math.min(ceiling, Math.max(floor, number));
}

/**
 * The scale a gesture asks for: the level the gesture started from times the gesture's relative scale, clamped.
 * @param {number} basePixelsPerHour the scale when the gesture started
 * @param {number} gestureScale the gesture's scale relative to its start (1 = unchanged)
 * @param {number} [min] the floor
 * @param {number} [max] the ceiling
 * @returns {number} the scale to paint with
 */
export function scaleFromGesture(basePixelsPerHour, gestureScale, min, max) {
    const base = clampPixelsPerHour(basePixelsPerHour, min, max);
    const scale = Number(gestureScale);

    return clampPixelsPerHour(base * (Number.isFinite(scale) && scale > 0 ? scale : 1), min, max);
}

/** A gesture handle that does nothing — used where JavaScript cannot attach (prerender, a missing element). */
function inert() {
    return {
        detach() {},
        cancel() {},
        get active() { return false; },
    };
}

/**
 * Loads the shared pinch/wheel helper. The URL is resolved against THIS module, so it works under any base path a
 * host serves the package from, and it is a dynamic import so a host without the core asset still gets the buttons.
 * @returns {Promise<{ attachPinch: Function }>} the helper's module namespace
 */
export function loadPinch() {
    const url = new URL('../../Tempo.Blazor/js/pointer-gesture.js', import.meta.url);
    return import(url.href);
}

/**
 * Measures the room inside the scrollport so "fit to width" can derive a scale. `clientWidth` is the CONTENT box —
 * the border and the scrollbar are already excluded — which is exactly what the timeline's content may occupy; a
 * rect width would overshoot by the border and leave a scrollbar's worth of overflow. Returns 0 where nothing can be
 * measured (SSR, a detached element), and the component then keeps its current zoom instead of jumping to a guess.
 * @param {Element|null} element the scrollport
 * @returns {number} its content-box width in pixels, or 0
 */
export function measureWidth(element) {
    if (!element) {
        return 0;
    }

    if (Number.isFinite(element.clientWidth) && element.clientWidth > 0) {
        return element.clientWidth;
    }

    if (typeof element.getBoundingClientRect !== 'function') {
        return 0;
    }

    const rect = element.getBoundingClientRect();
    return Number.isFinite(rect?.width) && rect.width > 0 ? rect.width : 0;
}

/**
 * Attaches the zoom gesture to the timeline scrollport and reports it to the component.
 *
 * Reports `onStart` (so .NET can remember the level the gesture began from), `onChange` with the gesture's relative
 * scale, and `onEnd` once — a cancelled gesture reports `onEnd` too, because the component only needs to know the
 * gesture is over. A missing .NET target, a missing helper or a dead circuit is swallowed: the buttons keep working.
 *
 * @param {Element|null} element the timeline scrollport
 * @param {{ invokeMethodAsync: Function }|null} dotNetRef the component's .NET reference
 * @param {{ pinchModule?: object|Function }} [options] test seam: supply the helper instead of importing it
 * @returns {Promise<{ detach: Function, cancel: Function }>} the handle the component disposes
 */
export async function attachLanesZoom(element, dotNetRef, options = {}) {
    if (!element || typeof element.addEventListener !== 'function' || !dotNetRef
        || typeof dotNetRef.invokeMethodAsync !== 'function') {
        return inert();
    }

    let pinchModule = options.pinchModule;
    if (!pinchModule) {
        try {
            pinchModule = await loadPinch();
        }
        catch {
            return inert();
        }
    }

    const attachPinch = typeof pinchModule === 'function' ? pinchModule : pinchModule?.attachPinch;
    if (typeof attachPinch !== 'function') {
        return inert();
    }

    const notify = (method, ...args) => {
        try {
            const pending = dotNetRef.invokeMethodAsync(method, ...args);
            if (pending && typeof pending.catch === 'function') {
                pending.catch(() => {
                    // The circuit went away between the gesture and the call: nothing left to update.
                });
            }
        }
        catch {
            // Same reason as above, thrown synchronously by a disposed runtime.
        }
    };

    let gesture;
    try {
        gesture = attachPinch(element, {
            wheel: true,
            onStart: () => notify('OnLanesZoomGestureStartAsync'),
            onChange: info => {
                const scale = Number(info?.scale);
                if (Number.isFinite(scale) && scale > 0) {
                    notify('OnLanesZoomGestureChangeAsync', scale);
                }
            },
            onEnd: () => notify('OnLanesZoomGestureEndAsync'),
            onCancel: () => notify('OnLanesZoomGestureEndAsync'),
        });
    }
    catch {
        return inert();
    }

    return {
        detach() {
            try {
                gesture?.detach?.();
            }
            catch {
                // Already detached by a reconnect.
            }
        },
        cancel() {
            try {
                gesture?.cancel?.('host');
            }
            catch {
                // Already cancelled.
            }
        },
        get active() {
            return gesture?.active === true;
        },
    };
}
