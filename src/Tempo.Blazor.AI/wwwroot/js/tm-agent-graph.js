// Tempo.Blazor.AI — the .NET-facing half of TmAgentGraph's pinch zoom and two-finger pan (ES module).
//
// The component owns the zoom LEVEL and paints the grid of nodes, the curves and the pulses from it; this module owns the
// two things Blazor cannot express: the browser gesture that produces a scale (and the two-finger pan delta), and the
// scrollport measurement "fit to view" derives a scale from. Ctrl+wheel (a trackpad pinch) and a two-finger pinch are
// NOT re-implemented here — they come from the core `_content/Tempo.Blazor/js/pointer-gesture.js` helper (see
// docs/touch-interaction.md), imported across the static-asset boundary exactly like the PDF viewer and TmAgentLanes do.
// Plain wheel is left alone, so a reader who is not holding Ctrl still scrolls the page.
//
// PAN is native scrolling: the module only applies the gesture's delta to the scrollport, so a mouse drag on the
// scrollbar, a wheel, a trackpad and the keyboard keep panning the picture without a second drag implementation.
//
// WCAG 2.5.7: a pinch is a dragging movement, so the component ships +/−/fit buttons beside it — this module is an
// enhancement, never the only way to change the scale.

/** The default floor of the scale (mirrors the component's MinZoom). */
export const DEFAULT_MIN_ZOOM = 0.5;

/** The default ceiling of the scale (mirrors the component's MaxZoom). */
export const DEFAULT_MAX_ZOOM = 2.5;

/**
 * Clamps a scale into the component's window. A value that is not a finite positive number falls back to the floor, so a
 * broken host state can never paint a zero-width drawing or a negative one.
 * @param {number} value the candidate scale
 * @param {number} [min] the floor; a non-positive or non-finite value uses the default floor
 * @param {number} [max] the ceiling; anything not above the floor uses the default ceiling
 * @returns {number} the scale to paint with
 */
export function clampZoom(value, min, max) {
    const floor = Number.isFinite(min) && min > 0 ? min : DEFAULT_MIN_ZOOM;
    const ceiling = Number.isFinite(max) && max > floor ? max : Math.max(floor, DEFAULT_MAX_ZOOM);
    const number = Number(value);

    if (!Number.isFinite(number) || number <= 0) {
        return floor;
    }

    return Math.min(ceiling, Math.max(floor, number));
}

/**
 * The scale a gesture asks for: the level the gesture started from times the gesture's CUMULATIVE scale, clamped. The
 * shared helper reports a scale relative to the gesture's start, so a second tick must not multiply the first one's
 * result.
 * @param {number} baseZoom the scale when the gesture started
 * @param {number} gestureScale the gesture's scale relative to its start (1 = unchanged)
 * @param {number} [min] the floor
 * @param {number} [max] the ceiling
 * @returns {number} the scale to paint with
 */
export function zoomFromGesture(baseZoom, gestureScale, min, max) {
    const base = clampZoom(baseZoom, min, max);
    const scale = Number(gestureScale);

    return clampZoom(base * (Number.isFinite(scale) && scale > 0 ? scale : 1), min, max);
}

/**
 * Measures the room inside the scrollport so "fit to view" can derive a scale. `clientWidth`/`clientHeight` are the
 * CONTENT box — the border and the scrollbar are already excluded — which is exactly what the drawing may occupy; a rect
 * would overshoot by the border and leave a scrollbar's worth of overflow. Returns an empty array where nothing can be
 * measured (SSR, a detached element), and the component then keeps its current zoom instead of jumping to a guess.
 * @param {Element|null} element the scrollport
 * @returns {number[]} `[width, height]` in pixels, or an empty array
 */
export function measureViewport(element) {
    if (!element) {
        return [];
    }

    const width = Number.isFinite(element.clientWidth) ? element.clientWidth : 0;
    const height = Number.isFinite(element.clientHeight) ? element.clientHeight : 0;
    if (width > 0 && height > 0) {
        return [width, height];
    }

    if (typeof element.getBoundingClientRect !== 'function') {
        return [];
    }

    const rect = element.getBoundingClientRect();
    const rectWidth = Number.isFinite(rect?.width) ? rect.width : 0;
    const rectHeight = Number.isFinite(rect?.height) ? rect.height : 0;
    return rectWidth > 0 && rectHeight > 0 ? [rectWidth, rectHeight] : [];
}

/**
 * Applies a pan delta to a scrollport, clamped to what the scrollport can actually scroll.
 * @param {Element|null} element the scrollport
 * @param {number} startLeft the scroll position when the gesture started
 * @param {number} startTop the scroll position when the gesture started
 * @param {number} dx the gesture's horizontal delta from its start, in pixels
 * @param {number} dy the gesture's vertical delta from its start, in pixels
 * @returns {boolean} whether the scrollport accepted the call
 */
export function applyPan(element, startLeft, startTop, dx, dy) {
    if (!element || typeof element !== 'object') {
        return false;
    }

    const left = Number.isFinite(startLeft) ? startLeft : 0;
    const top = Number.isFinite(startTop) ? startTop : 0;
    const offsetX = Number.isFinite(dx) ? dx : 0;
    const offsetY = Number.isFinite(dy) ? dy : 0;

    try {
        element.scrollLeft = left - offsetX;
        element.scrollTop = top - offsetY;
        return true;
    }
    catch {
        return false;
    }
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
 * Loads the shared pinch/wheel helper. The URL is resolved against THIS module, so it works under any base path a host
 * serves the package from, and it is a dynamic import so a host without the core asset still gets the buttons.
 * @returns {Promise<{ attachPinch: Function }>} the helper's module namespace
 */
export function loadPinch() {
    const url = new URL('../../Tempo.Blazor/js/pointer-gesture.js', import.meta.url);
    return import(url.href);
}

/**
 * Attaches the zoom gesture (and the two-finger pan it carries) to the graph's scrollport and reports it to the
 * component.
 *
 * Reports `onStart` (so .NET can remember the level the gesture began from), `onChange` with the gesture's relative
 * scale, and `onEnd` once — a cancelled gesture reports `onEnd` too, because the component only needs to know the gesture
 * is over. The pan delta is applied to the scrollport here instead of being sent to .NET: a pan is a scroll position,
 * not a component state. A missing .NET target, a missing helper or a dead circuit is swallowed: the buttons keep
 * working.
 *
 * @param {Element|null} element the graph's scrollport
 * @param {{ invokeMethodAsync: Function }|null} dotNetRef the component's .NET reference
 * @param {{ pinchModule?: object|Function }} [options] test seam: supply the helper instead of importing it
 * @returns {Promise<{ detach: Function, cancel: Function }>} the handle the component disposes
 */
export async function attachGraphGesture(element, dotNetRef, options = {}) {
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

    let startLeft = 0;
    let startTop = 0;

    let gesture;
    try {
        gesture = attachPinch(element, {
            wheel: true,
            onStart: () => {
                startLeft = Number.isFinite(element.scrollLeft) ? element.scrollLeft : 0;
                startTop = Number.isFinite(element.scrollTop) ? element.scrollTop : 0;
                notify('OnGraphZoomGestureStartAsync');
            },
            onChange: info => {
                applyPan(element, startLeft, startTop, info?.dx, info?.dy);

                const scale = Number(info?.scale);
                if (Number.isFinite(scale) && scale > 0) {
                    notify('OnGraphZoomGestureChangeAsync', scale);
                }
            },
            onEnd: () => notify('OnGraphZoomGestureEndAsync'),
            onCancel: () => notify('OnGraphZoomGestureEndAsync'),
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
