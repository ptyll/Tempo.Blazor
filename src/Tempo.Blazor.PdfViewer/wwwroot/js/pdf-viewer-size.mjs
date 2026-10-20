/**
 * Tempo.Blazor.PdfViewer — pure canvas sizing math (single source of truth).
 *
 * The backing store of every rendered page canvas (single page, continuous
 * scroll and thumbnails) is the CSS viewport size multiplied by the device
 * pixel ratio, capped at MAX_DEVICE_PIXEL_RATIO so a 3× phone does not
 * quadruple the render cost. pdf-viewer.js loads this module via a dynamic
 * import keyed off its script directory; the node test suite imports it
 * directly (npm run test:pdf-viewer).
 */

/** Upper bound for the device pixel ratio used for canvas backing stores. */
export const MAX_DEVICE_PIXEL_RATIO = 2;

/**
 * Clamps a devicePixelRatio-like value to a positive number capped at
 * {@link MAX_DEVICE_PIXEL_RATIO}; non-finite or non-positive values fall back to 1.
 * @param {number} devicePixelRatio Host device pixel ratio.
 * @returns {number} Safe device pixel ratio (≥ 1, ≤ MAX_DEVICE_PIXEL_RATIO).
 */
export function maxDevicePixelRatio(devicePixelRatio) {
    const value = Number(devicePixelRatio);
    if (!Number.isFinite(value) || value <= 0) return 1;
    return Math.min(value, MAX_DEVICE_PIXEL_RATIO);
}

/**
 * Computes the canvas sizes for one page render.
 * @param {number} cssWidth Desired CSS width (the viewport width).
 * @param {number} cssHeight Desired CSS height (the viewport height).
 * @param {number} devicePixelRatio Host device pixel ratio.
 * @returns {{width: number, height: number, cssWidth: number, cssHeight: number, dpr: number}}
 *   width/height are the backing-store pixels for canvas.width/height and
 *   cssWidth/cssHeight the CSS pixels for canvas.style.width/height.
 */
export function canvasSize(cssWidth, cssHeight, devicePixelRatio = 1) {
    const dpr = maxDevicePixelRatio(devicePixelRatio);
    const w = Math.max(cssWidth, 0);
    const h = Math.max(cssHeight, 0);
    return {
        width: Math.max(1, Math.round(w * dpr)),
        height: Math.max(1, Math.round(h * dpr)),
        cssWidth: w,
        cssHeight: h,
        dpr
    };
}
