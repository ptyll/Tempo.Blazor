// Tempo.Blazor.AI — the measuring half of TmAgentPresenceOverlay (ES module).
//
// The component paints the presence layer (the cursors, their labels and the optional roster for screen readers) and
// owns every WORD on it. This module owns the one thing Blazor cannot express: WHERE an anchored cursor sits, measured
// from the real DOM. It reads the marker elements the component rendered, finds each marker's anchor inside the
// content, measures both boxes and writes the two CSS custom properties the stylesheet paints from.
//
// Why CSS properties instead of a .NET round trip: a cursor moves on every keystroke of a collaborator. Reporting each
// position back to .NET would re-render the component (and its host's content) dozens of times a second — on a Server
// circuit that is a message per keystroke. Writing two custom properties stays inside the browser and never touches the
// render tree.
//
// The placement is kept honest by three guards:
//  - ResizeObserver on the content AND every anchor, so a reflow re-measures without a single event listener on window;
//  - MutationObserver on the content, so an anchor that a re-render replaced is found again (the marker is hidden until
//    then rather than painted at a stale position);
//  - a throttle (rAF plus a minimum interval) so a burst of mutations costs one measurement.
//
// Reduced motion is NOT handled here: the module writes positions, the stylesheet animates the move and its
// prefers-reduced-motion block removes the animation. A module that animated would be a second, JS-only motion source.
//
// PLACEMENT. A label that would run past the content's inline end used to be mirrored onto the other side of its
// point — which on a phone put it straight over the last words of the very paragraph it is anchored to. The module now
// MEASURES the label and decides where it belongs: beside the point while that fits, otherwise UNDER (or above) the
// anchored line, and only as the very last resort mirrored next to it. Both answers are LOGICAL (`inset-inline-` /
// `inset-block-` in the stylesheet, `row-reverse` along the inline axis), so no consumer has to assume a direction.

/** The attribute the component puts on a marker to name its anchor. */
export const ANCHOR_ATTRIBUTE = 'data-presence-anchor';

/** The attribute the component puts on every marker element. */
export const MARKER_ATTRIBUTE = 'data-presence-marker';

/** Set by this module once a marker has a measured position; the stylesheet only reveals placed markers. */
export const PLACED_ATTRIBUTE = 'data-presence-placed';

/**
 * Set by this module to `start` when the marker is mirrored: its inline END sits on the measured point, so the label
 * grows INTO the content instead of widening the page. Absent means `end` (the label grows to the inline end).
 */
export const PLACE_ATTRIBUTE = 'data-presence-place';

/**
 * Set by this module to `below` or `above` when the label is stacked against the anchored line instead of beside it.
 * Absent means the marker sits beside its point.
 */
export const STACK_ATTRIBUTE = 'data-presence-stack';

/** The custom property carrying the anchor's measured block size — a stacked label starts below the line box. */
export const HEIGHT_PROPERTY = '--tm-presence-h';

/** The room a label keeps from the content's edges before the placement changes. */
export const FLIP_GUTTER = 8;

/** The custom property carrying the horizontal position. */
export const X_PROPERTY = '--tm-presence-x';

/** The custom property carrying the vertical position. */
export const Y_PROPERTY = '--tm-presence-y';

/** How often a burst of mutations may produce a measurement, in milliseconds. */
export const DEFAULT_MINIMUM_INTERVAL = 80;

/**
 * The selector that finds an anchored element inside the content. Attribute values are quoted, so a quote or a
 * backslash in an id is escaped instead of ending the selector early (and throwing on querySelector).
 * @param {string} anchorId the value of the anchor's data-presence-anchor attribute
 * @returns {string} a CSS attribute selector
 */
export function anchorSelector(anchorId) {
    const escaped = String(anchorId ?? '').replace(/["\\]/g, match => '\\' + match);
    return `[${ANCHOR_ATTRIBUTE}="${escaped}"]`;
}

/**
 * Where a marker belongs, in pixels relative to the content's padding box. The anchor's own top-left corner is the
 * cursor's tip, which is what a reader reads as "here".
 * @param {{left:number,top:number,width:number,height:number}|null} contentRect the content box
 * @param {{left:number,top:number}|null} anchorRect the anchor's box
 * @returns {{x:number,y:number}|null} the offsets, or null when either box cannot be measured
 */
export function resolveAnchorPosition(contentRect, anchorRect) {
    if (!contentRect || !anchorRect) {
        return null;
    }

    const width = Number(contentRect.width);
    const height = Number(contentRect.height);
    const left = Number(contentRect.left);
    const top = Number(contentRect.top);
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
        return null;
    }

    if (!Number.isFinite(left) || !Number.isFinite(top)) {
        return null;
    }

    const x = Number(anchorRect.left) - left;
    const y = Number(anchorRect.top) - top;
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
        return null;
    }

    // The box size travels with the offsets: a label stacked UNDER the anchor has to clear the anchored LINE box, so
    // the placement needs the height (and the width costs nothing).
    return { x, y, width: sizeOf(anchorRect.width), height: sizeOf(anchorRect.height) };
}

/**
 * A finite, non-negative box measurement, or 0 — a browser that reports nothing must not poison the placement math.
 * @param {unknown} value the raw measurement
 * @returns {number}
 */
function sizeOf(value) {
    const number = Number(value);
    return Number.isFinite(number) && number > 0 ? number : 0;
}

/**
 * A CSS pixel value, rounded to two decimals so a sub-pixel reflow does not rewrite the property endlessly.
 * @param {number} value the pixel count
 * @returns {string|null} e.g. "12.34px", or null for anything that is not a finite number
 */
export function offsetValue(value) {
    const number = Number(value);
    if (!Number.isFinite(number)) {
        return null;
    }

    return `${Math.round(number * 100) / 100}px`;
}

/**
 * Paints a marker at a measured position and marks it placed (the stylesheet reveals placed markers only, so a marker
 * whose anchor is gone is not left at a stale position).
 * @param {Element|null} marker the marker element
 * @param {{x:number,y:number}|null} position the measured offsets
 * @returns {boolean} whether the marker was painted
 */
export function placeMarker(marker, position) {
    if (!marker || !marker.style || !position) {
        return false;
    }

    const x = offsetValue(position.x);
    const y = offsetValue(position.y);
    if (x === null || y === null) {
        return false;
    }

    marker.style.setProperty(X_PROPERTY, x);
    marker.style.setProperty(Y_PROPERTY, y);

    const height = offsetValue(position.height);
    if (height !== null) {
        marker.style.setProperty(HEIGHT_PROPERTY, height);
    }

    marker.setAttribute(PLACED_ATTRIBUTE, 'true');
    return true;
}

/**
 * Where a marker's label belongs, given the measured boxes. The order is deliberate:
 *  1. beside the point, growing to the inline end — a cursor label never covers its own line;
 *  2. UNDER the anchored line, then above it: the mirrored label used to cover the last words of the paragraph it is
 *     anchored to, which is exactly the defect this replaces;
 *  3. mirrored beside the point — the last resort, when no vertical room is left either;
 *  4. beside the point again (clipped by the layer) — the honest answer when nothing fits.
 *
 * The result is LOGICAL: `place` names the inline side the marker is pinned by, `stack` the block side, and the
 * stylesheet turns those into `inset-inline-*` / `inset-block-*` / `flex-direction` — no physical left or right.
 * @param {{x?:number, y?:number, width?:number, height?:number, contentWidth?:number, contentHeight?:number, anchorHeight?:number, gutter?:number}} metrics the measured boxes
 * @returns {{place:string, stack:string}} the placement
 */
export function resolveLabelPlacement(metrics) {
    const input = metrics ?? {};
    const beside = { place: 'end', stack: 'none' };
    const x = finiteOf(input.x);
    const y = finiteOf(input.y);
    const width = finiteOf(input.width);
    const height = finiteOf(input.height);
    const contentWidth = finiteOf(input.contentWidth);
    const contentHeight = finiteOf(input.contentHeight);
    if (x === null || y === null || width === null || height === null
        || contentWidth === null || contentHeight === null
        || width <= 0 || height <= 0 || contentWidth <= 0 || contentHeight <= 0) {
        return beside;
    }

    const anchorHeight = sizeOf(input.anchorHeight);
    const gutter = finiteOf(input.gutter);
    const margin = gutter !== null && gutter >= 0 ? gutter : FLIP_GUTTER;
    const fitsEnd = x + width + margin <= contentWidth;
    if (fitsEnd) {
        return beside;
    }

    const fitsStart = x - width - margin >= 0;
    // Horizontally the stacked label goes where it loses least: the fitting side, then whichever edge is closer.
    const place = fitsStart
        ? 'start'
        : x + width <= contentWidth
            ? 'end'
            : x - width >= 0
                ? 'start'
                : x > contentWidth / 2 ? 'start' : 'end';

    if (y + anchorHeight + height + margin <= contentHeight) {
        return { place, stack: 'below' };
    }

    if (y - height - margin >= 0) {
        return { place, stack: 'above' };
    }

    return { place, stack: 'none' };
}

/**
 * A finite number, or null for anything a measurement can wrongly produce.
 * @param {unknown} value the raw value
 * @returns {number|null}
 */
function finiteOf(value) {
    const number = Number(value);
    return Number.isFinite(number) ? number : null;
}

/**
 * Writes a marker's placement into the two attributes the stylesheet reads, touching only what changed — a re-measure
 * that lands in the same place must not rewrite the DOM.
 * @param {Element|null} marker the marker element
 * @param {{place?:string, stack?:string}} placement the answer from resolveLabelPlacement
 */
export function applyPlacement(marker, placement) {
    if (!marker || !marker.setAttribute || !marker.removeAttribute) {
        return;
    }

    const place = placement?.place === 'start' ? 'start' : null;
    const stack = placement?.stack === 'below' || placement?.stack === 'above' ? placement.stack : null;
    writeAttribute(marker, PLACE_ATTRIBUTE, place);
    writeAttribute(marker, STACK_ATTRIBUTE, stack);
}

/**
 * Sets an attribute to a value, or removes it when the value is null — and only when the DOM actually differs.
 * @param {Element} marker the marker element
 * @param {string} name the attribute name
 * @param {string|null} value the wanted value
 */
function writeAttribute(marker, name, value) {
    const current = marker.getAttribute?.(name) ?? null;
    if (value === null) {
        if (current !== null) {
            marker.removeAttribute(name);
        }

        return;
    }

    if (current !== value) {
        marker.setAttribute(name, value);
    }
}

/**
 * Hides a marker whose anchor cannot be found: the stylesheet fades an unplaced marker out, so a cursor never sits at
 * the content's origin pretending its element is there.
 * @param {Element|null} marker the marker element
 */
export function hideMarker(marker) {
    if (marker && marker.removeAttribute) {
        marker.removeAttribute(PLACED_ATTRIBUTE);
    }
}

/** The element list of a root, tolerating a missing root (prerender, a detached component). */
function markersOf(root) {
    if (!root || !root.querySelectorAll) {
        return [];
    }

    return Array.from(root.querySelectorAll(`[${MARKER_ATTRIBUTE}]`));
}

/**
 * Attaches the measurement to a rendered presence overlay.
 * @param {Element|null} root the overlay's root element (markers live in it)
 * @param {Element|null} content the element the anchors are measured against
 * @param {{minimumInterval?:number, window?:any}} [options] throttle and the window to observe/listen on
 * @returns {{refresh:Function, detach:Function, placed:Function}} the handle the .NET side keeps
 */
export function attachPresence(root, content, options = {}) {
    const settings = options ?? {};
    const win = settings.window
        ?? root?.ownerDocument?.defaultView
        ?? (typeof window !== 'undefined' ? window : null);
    const minimumInterval = Number.isFinite(settings.minimumInterval) && settings.minimumInterval >= 0
        ? settings.minimumInterval
        : DEFAULT_MINIMUM_INTERVAL;

    const resizeObserver = win?.ResizeObserver ? new win.ResizeObserver(() => schedule()) : null;
    const mutationObserver = win?.MutationObserver && content
        ? new win.MutationObserver(() => {
            observeAnchors();
            schedule();
        })
        : null;

    const observedAnchors = new Set();
    let attached = true;
    let timer = null;
    let frame = null;
    let lastRun = 0;
    let placeCount = 0;

    const now = () => (win?.performance?.now ? win.performance.now() : Date.now());

    /** Keeps a ResizeObserver watching every anchor the content currently holds. */
    function observeAnchors() {
        if (!resizeObserver || !content || !content.querySelector) {
            return;
        }

        for (const marker of markersOf(root)) {
            const anchorId = marker.getAttribute?.(ANCHOR_ATTRIBUTE);
            if (!anchorId) {
                continue;
            }

            const anchor = content.querySelector(anchorSelector(anchorId));
            if (!anchor || observedAnchors.has(anchor)) {
                continue;
            }

            observedAnchors.add(anchor);
            resizeObserver.observe(anchor);
        }
    }

    /** One measurement pass: every anchored marker is placed, or hidden when its anchor is gone. */
    function measure() {
        if (!attached || !content || !content.getBoundingClientRect) {
            return;
        }

        const contentRect = content.getBoundingClientRect();
        for (const marker of markersOf(root)) {
            const anchorId = marker.getAttribute?.(ANCHOR_ATTRIBUTE);
            if (!anchorId) {
                continue; // a point marker is painted by the component from its own fractions
            }

            const anchor = content.querySelector
                ? content.querySelector(anchorSelector(anchorId))
                : null;
            if (!anchor || !anchor.getBoundingClientRect) {
                hideMarker(marker);
                continue;
            }

            const anchorRect = anchor.getBoundingClientRect();
            const position = resolveAnchorPosition(contentRect, anchorRect);
            if (!placeMarker(marker, position)) {
                hideMarker(marker);
                continue;
            }

            const markerRect = marker.getBoundingClientRect ? marker.getBoundingClientRect() : null;
            applyPlacement(marker, resolveLabelPlacement({
                x: position.x,
                y: position.y,
                width: Number(markerRect?.width),
                height: Number(markerRect?.height),
                contentWidth: contentRect.width,
                contentHeight: contentRect.height,
                anchorHeight: position.height,
            }));
            placeCount += 1;
        }

        observeAnchors();
    }

    /** The throttled body: measures at most once per interval, in a frame when the browser has one. */
    function run() {
        timer = null;
        frame = null;
        lastRun = now();
        measure();
    }

    /** Asks for a measurement, honouring the throttle. Idempotent while one is already pending. */
    function schedule() {
        if (!attached || timer !== null || frame !== null) {
            return;
        }

        const elapsed = now() - lastRun;
        const delay = minimumInterval - elapsed;
        if (delay > 0 && win?.setTimeout) {
            timer = win.setTimeout(run, delay);
            return;
        }

        if (win?.requestAnimationFrame) {
            frame = win.requestAnimationFrame(run);
            return;
        }

        run();
    }

    const onReflow = () => schedule();

    if (win?.addEventListener) {
        win.addEventListener('resize', onReflow, { passive: true });
        win.addEventListener('scroll', onReflow, { passive: true, capture: true });
    }

    if (resizeObserver && content) {
        resizeObserver.observe(content);
    }

    if (mutationObserver) {
        mutationObserver.observe(content, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: [ANCHOR_ATTRIBUTE],
        });
    }

    measure();

    return {
        /** Re-measures after a render that may have moved an anchor. */
        refresh: () => schedule(),

        /** How many markers the last pass placed (a diagnostic the component's tests read). */
        placed: () => placeCount,

        /** Releases every observer, listener and pending measurement. Idempotent. */
        detach: () => {
            if (!attached) {
                return;
            }

            attached = false;
            resizeObserver?.disconnect();
            mutationObserver?.disconnect();
            observedAnchors.clear();

            if (timer !== null && win?.clearTimeout) {
                win.clearTimeout(timer);
            }

            if (frame !== null && win?.cancelAnimationFrame) {
                win.cancelAnimationFrame(frame);
            }

            timer = null;
            frame = null;

            if (win?.removeEventListener) {
                win.removeEventListener('resize', onReflow, { passive: true });
                win.removeEventListener('scroll', onReflow, { passive: true, capture: true });
            }
        },
    };
}
