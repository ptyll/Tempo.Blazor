// Tempo.Blazor splitter (ES module) — the TmSplitterPane resizer.
//
// The pointer gesture is the shared pointer-drag helper; the live size is an inline `flex` on the
// pane (NO Blazor re-render while the pointer moves) and the size is reported to .NET exactly once
// on release. Escape, pointercancel, a lost capture and a mid-drag detach restore the start style
// VERBATIM (the drag's `flex` shorthand destroys the authored inline `flex-basis`, so clearing one
// property is not enough) and commit nothing. The horizontal delta inverts with the writing
// direction. The keyboard path (arrows, Home/End on the role="separator") lives in Blazor and
// commits through the same OnSplitterResizeCommitted — this module only prevents the axis keys and
// Home/End from scrolling the page.

import { attach } from './pointer-drag.js';

const DRAGGING_CLASS = 'tm-splitter__resizer--dragging';
const COLLAPSE_SELECTOR = '.tm-splitter__collapse-btn';

const resizers = new Map();

/**
 * Clamps a requested pane size: whole pixels, never negative, within [min, max] where the bounds
 * are finite. The same rules the C# keyboard clamp applies (SplitterResize.Clamp).
 * @param {number} requested the size the pointer asked for
 * @param {number} min smallest allowed size in px
 * @param {number} max largest allowed size in px (Infinity when unknown)
 * @returns {number} the size in whole pixels
 */
export function clampSize(requested, min, max) {
    let size = Math.round(requested);
    if (Number.isFinite(min)) size = Math.max(min, size);
    if (Number.isFinite(max)) size = Math.min(max, size);
    return Math.max(0, size);
}

/**
 * The rendered size of a pane in whole pixels, or -1 when it cannot be measured. The keyboard path
 * steps from this when the pane's Size is not a plain pixel length (%, rem).
 * @param {HTMLElement|null} pane the pane element
 * @param {'horizontal'|'vertical'} orientation the splitter's orientation
 * @returns {number}
 */
export function measureSize(pane, orientation) {
    const rect = pane?.getBoundingClientRect?.();
    if (!rect) return -1;
    const size = orientation === 'vertical' ? rect.height : rect.width;
    return typeof size === 'number' && Number.isFinite(size) ? Math.round(size) : -1;
}

/**
 * The writing direction of an element, read live from its computed style: the horizontal delta
 * inverts in RTL, so the splitter has to ask the DOM (a `dir` attribute or CSS can flip it at any
 * level above the strip). Falls back to `ltr` wherever the answer cannot be determined (Node, SSR).
 * @param {HTMLElement|null} element any element inside the splitter
 * @returns {'ltr'|'rtl'}
 */
export function directionOf(element) {
    try {
        if (typeof getComputedStyle !== 'function') return 'ltr';
        return getComputedStyle(element)?.direction === 'rtl' ? 'rtl' : 'ltr';
    }
    catch {
        return 'ltr';
    }
}

/**
 * Attaches the pointer-drag resize to a separator strip. A press that starts on the collapse
 * button (which visually overlaps the strip) is vetoed, so the button keeps its click. The pane's
 * size follows the pointer live as an inline `flex`; on release the clamped size is reported ONCE
 * through OnSplitterResizeCommitted. Escape, pointercancel and a lost capture restore the start
 * style verbatim without reporting.
 * @param {HTMLElement} handle the separator strip (role="separator")
 * @param {HTMLElement} pane the pane the strip resizes
 * @param {{ invokeMethodAsync: Function }|null} dotnet the pane, which receives OnSplitterResizeCommitted
 * @param {string} id registration key; attaching again with the same id replaces the previous one
 * @param {{ orientation: 'horizontal'|'vertical', min: number, max: number }} options
 */
export function attachResize(handle, pane, dotnet, id, options) {
    if (!handle || !pane) return;
    detachResize(id);

    const { orientation = 'horizontal', min = 0, max = Infinity } = options ?? {};
    const horizontal = orientation === 'horizontal';

    let start = 0;
    let size = 0;
    let rtl = false;
    let startStyle = null;

    // Puts back the EXACT inline style the pane carried when the gesture began (null → no style
    // attribute at all). Restoring one property would not be enough: assigning the `flex` shorthand
    // destroyed the pane's authored inline `flex-basis`, so only the verbatim snapshot undoes the
    // drag completely.
    const restoreStartStyle = () => {
        if (startStyle === null) pane.removeAttribute('style');
        else pane.setAttribute('style', startStyle);
    };

    const show = (value) => {
        pane.style.flex = `0 0 ${value}px`;
        handle.setAttribute('aria-valuenow', String(value));
    };

    const drag = attach(handle, {
        threshold: 0,
        dragClass: DRAGGING_CLASS,
        // The strip owns no click of its own, and the collapse button keeps its click on veto.
        suppressClick: false,
        // CSS owns touch-action: none on the strip (.tm-splitter__resizer).
        touchAction: null,
        onPress(info) {
            if (info?.event?.target?.closest?.(COLLAPSE_SELECTOR)) return false;
            start = size = measureSize(pane, orientation);
            if (start < 0) return false;
            rtl = directionOf(handle) === 'rtl';
            startStyle = pane.getAttribute?.('style') ?? null;
            return true;
        },
        onMove({ dx, dy }) {
            const delta = horizontal ? (rtl ? -dx : dx) : dy;
            size = clampSize(start + delta, min, max);
            show(size);
        },
        onEnd() {
            const committed = size;
            if (committed !== start) {
                // The circuit may already be gone: a rejected invoke must not become an unhandled rejection.
                dotnet?.invokeMethodAsync('OnSplitterResizeCommitted', committed)?.catch?.(() => {});
            } else {
                // Back where it started: no commit, and the dragged flex must not survive as the
                // pane's style — Blazor never re-renders, so restore the start style here too.
                restoreStartStyle();
            }
        },
        // Escape, pointercancel, a lost capture, or a pane that unmounts mid-drag (detach): the
        // start style goes back verbatim so Blazor's own declarations apply again. Nothing is
        // committed.
        onCancel() {
            restoreStartStyle();
            handle.setAttribute('aria-valuenow', String(start));
        },
    });

    // Only the keys this separator actually handles must not scroll the page; every other key
    // (including the perpendicular arrows) keeps its browser behaviour. Blazor performs the resize.
    const onKey = (event) => {
        const key = event.key;
        if (key === 'Home' || key === 'End') {
            event.preventDefault?.();
            return;
        }
        if (horizontal && (key === 'ArrowLeft' || key === 'ArrowRight')) event.preventDefault?.();
        if (!horizontal && (key === 'ArrowUp' || key === 'ArrowDown')) event.preventDefault?.();
    };
    handle.addEventListener('keydown', onKey);

    resizers.set(id, { handle, drag, onKey });
}

/** Removes the resize registered under the id. Safe to call twice or for an unknown id. */
export function detachResize(id) {
    const entry = resizers.get(id);
    if (!entry) return;
    resizers.delete(id);
    entry.drag.detach();
    entry.handle.removeEventListener('keydown', entry.onKey);
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...resizers.keys()]) detachResize(id);
}
