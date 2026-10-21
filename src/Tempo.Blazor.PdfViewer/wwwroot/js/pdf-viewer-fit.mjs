/**
 * Tempo.Blazor.PdfViewer — pure fit-mode logic (single source of truth).
 *
 * pdf-viewer.js keeps ONE ResizeObserver per fit container. A fit-width → fit-page
 * switch reuses the same container element (and thus the same observer), so the
 * observer callback must resolve the mode from the LIVE state at CALL time — a mode
 * captured at registration would silently keep recomputing the old mode. The pure
 * decision helpers here are node-tested (npm run test:pdf-viewer); pdf-viewer.js
 * loads this module via a dynamic import keyed off its script directory and only
 * reads the DOM around them.
 */

/** Lower absolute bound of a computed fit scale (mirrors TmPdfViewer.MinScale). */
export const FIT_MIN_SCALE = 0.1;

/** Upper absolute bound of a computed fit scale (mirrors TmPdfViewer.MaxScale). */
export const FIT_MAX_SCALE = 5;

/**
 * Resolves the fit mode a resize-triggered recompute must use, read from the live
 * viewer state at CALL time. Deliberately takes no "mode captured at registration":
 * the observer outlives fit-mode switches on the same container, so the mode may only
 * be read when the resize actually fires.
 * @param {{pdfDoc?: unknown, fitMode?: string|null}|undefined} state Live per-root viewer state.
 * @returns {string|null} 'fit-width' | 'fit-page' | null (no document, or the mode
 *   returned to Custom via clearFit — nothing to recompute).
 */
export function resolveRecomputeFitMode(state) {
    return state?.pdfDoc && state.fitMode ? state.fitMode : null;
}

/**
 * Whether a recompute that resolved `mode` may still report after its async page
 * lookup: the live mode must be unchanged — a switch to Custom (clearFit) or to the
 * other fit mode meanwhile invalidates the pending result.
 * @param {{fitMode?: string|null}|undefined} state Live per-root viewer state.
 * @param {string} mode Mode resolved when the recompute started.
 * @returns {boolean}
 */
export function isRecomputeValid(state, mode) {
    return !!state && state.fitMode === mode;
}

/**
 * Clamps a raw container ratio into the absolute fit bounds. A fit scale is ALLOWED
 * to fall outside the component's zoom steps (the exact container ratio beats the
 * steps) — it is clamped only to these absolute bounds so the canvas can never
 * collapse or explode. Documented on TmPdfViewer.ApplyZoomModeAsync /
 * OnFitScaleComputed.
 * @param {number} scale Raw available-space ratio.
 * @returns {number} Scale clamped to FIT_MIN_SCALE..FIT_MAX_SCALE.
 */
export function clampFitScale(scale) {
    return Math.min(FIT_MAX_SCALE, Math.max(FIT_MIN_SCALE, scale));
}

/**
 * Computes the fit scale from plain numbers (no DOM).
 * @param {{width: number, height: number}} viewport Page viewport at scale 1 with the
 *   current rotation.
 * @param {number} availableWidth Container inner width (clientWidth − horizontal padding).
 * @param {number|null} availableHeight Container inner height (clientHeight − vertical
 *   padding), or null for fit-width.
 * @param {'fit-width'|'fit-page'} mode Active fit mode.
 * @returns {number} Clamped scale, or 0 when there is no usable space.
 */
export function computeFitScale(viewport, availableWidth, availableHeight, mode) {
    if (!viewport || !(availableWidth >= 1)) return 0;
    let scale;
    if (mode === 'fit-page') {
        if (!(availableHeight >= 1)) return 0;
        scale = Math.min(availableWidth / viewport.width, availableHeight / viewport.height);
    } else {
        scale = availableWidth / viewport.width;
    }
    return clampFitScale(scale);
}
