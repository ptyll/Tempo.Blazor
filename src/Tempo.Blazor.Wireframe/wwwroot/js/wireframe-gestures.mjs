// Tempo Wireframe Designer - pure, wireframe-specific gesture maths (ES module, no DOM, no dependencies).
//
// wireframe-designer.js (a classic script) loads this module with a dynamic import() resolved from its own
// URL. Everything GENERIC lives in the core: single-pointer drags and the touch long-press come from
// pointer-drag.js (attach), two-finger pinch/pan from pointer-gesture.js (attachPinch) - see
// docs/touch-interaction.md. This file therefore holds only what is specific to the wireframe canvas and can
// be unit-tested in Node: the double-tap tracker, the coarse-pointer handle sizes, the pinch -> viewBox maths
// with the 0.15x-5x zoom clamp, and the keyboard nudge step. It must never grow a pinch or a long-press
// detector of its own.

/** Touch long-press that opens the context menu (handed to pointer-drag.js as longPressMs). */
export const LONG_PRESS_MS = 500;

/** Pixels a press may move before it becomes a drag (a tap stays a tap, so a finger can select). */
export const DRAG_THRESHOLD = 5;

/** A handle (resize / rotate / waypoint) drag starts on the first movement: the finger is already on the handle. */
export const HANDLE_DRAG_THRESHOLD = 0;

/** Absolute canvas zoom limits (screen px per svg unit): the same clamp the wheel zoom has always used. */
export const ZOOM_MIN = 0.15;
export const ZOOM_MAX = 5;

/** Resize / rotate / waypoint handles are 1.5x larger on a coarse pointer. */
export const COARSE_HANDLE_SCALE = 1.5;

/** Resize handle edge and rotate handle radius in svg units on a fine pointer. */
export const HANDLE_SIZE = 8;
export const ROTATE_HANDLE_RADIUS = 5;

/** The invisible hit area of a handle never gets smaller than this many SCREEN pixels on a coarse pointer. */
export const MIN_HIT_SCREEN_PX = 24;

/** The PAINTED handle never gets smaller than this many SCREEN pixels on a coarse pointer (a fitted page is zoomed out). */
export const MIN_VISUAL_SCREEN_PX = 12;

/** Two taps within this window and radius are a double-tap (a dblclick). */
export const DOUBLE_TAP_MS = 350;
export const DOUBLE_TAP_SLOP = 24;

/** The context menu opened by a long-press is parked this many px from the finger so it is not under it. */
export const TOUCH_MENU_OFFSET = 28;

/** Fallback arrow-nudge step in svg units with Shift when snapping is off. */
export const DEFAULT_SHIFT_STEP = 10;

/**
 * Whether the primary input is coarse (a finger). Never throws; false where it cannot be determined.
 * @param {{ matchMedia?: (query: string) => { matches: boolean } }} [win]
 * @returns {boolean}
 */
export function isCoarsePointer(win = globalThis) {
    try {
        return win?.matchMedia?.('(pointer: coarse)')?.matches === true;
    }
    catch {
        return false;
    }
}

/** The handle size multiplier: 1.5 on a coarse pointer, 1 otherwise. */
export function handleScale(coarse) {
    return coarse ? COARSE_HANDLE_SCALE : 1;
}

/**
 * Sizes of one selection handle in svg units.
 *
 * `visual` is what is painted: the 8-unit handle on a fine pointer; on a coarse pointer x1.5 AND at least
 * `minVisualScreen` screen pixels at the current zoom (a fitted page is zoomed far out, so a fixed 12 units would be
 * a 3 px speck), capped at `maxVisual` so a small element is not swallowed by its own handles. `hit` is the size of
 * the invisible pointer target: equal to `visual` for a fine pointer; on a coarse pointer at least `minHitScreen`
 * screen pixels at the current zoom, but never above `maxHit` (a small element must not get handles that overlap
 * each other) and never below `visual`.
 *
 * @param {{ coarse?: boolean, zoom?: number, base?: number, minHitScreen?: number, minVisualScreen?: number,
 *           maxHit?: number, maxVisual?: number }} [options]
 * @returns {{ visual: number, hit: number }}
 */
export function handleSizes({ coarse = false, zoom = 1, base = HANDLE_SIZE, minHitScreen = MIN_HIT_SCREEN_PX,
    minVisualScreen = MIN_VISUAL_SCREEN_PX, maxHit = Infinity, maxVisual = Infinity } = {}) {
    const scaled = base * handleScale(coarse);
    if (!coarse || !(zoom > 0)) return { visual: scaled, hit: scaled };
    const floor = scaled;
    const visual = Math.min(Math.max(floor, maxVisual), Math.max(scaled, minVisualScreen / zoom));
    const wanted = minHitScreen / zoom;
    const cap = Math.max(visual, maxHit);
    return { visual, hit: Math.min(cap, Math.max(visual, wanted)) };
}

/**
 * Extends a viewBox so its aspect ratio matches the svg's client rect, keeping what is visible exactly where it is
 * (the `preserveAspectRatio="xMidYMid meet"` letterboxing made explicit). After this, client px map to svg units
 * with ONE scale on both axes, which is what the pinch maths and the zoom read-out assume.
 *
 * @param {{ x: number, y: number, w: number, h: number }} viewBox
 * @param {{ width: number, height: number }} rect
 * @returns {{ x: number, y: number, w: number, h: number }}
 */
export function matchAspect(viewBox, rect) {
    if (!(rect.width > 0) || !(rect.height > 0) || !(viewBox.w > 0) || !(viewBox.h > 0)) return { ...viewBox };
    const scale = Math.min(rect.width / viewBox.w, rect.height / viewBox.h);
    const w = rect.width / scale;
    const h = rect.height / scale;
    return { x: viewBox.x + (viewBox.w - w) / 2, y: viewBox.y + (viewBox.h - h) / 2, w, h };
}

/**
 * The real on-screen scale (screen px per svg unit) of a viewBox inside a rect: the smaller of the two axis scales,
 * which is what `meet` renders with. Equals `rect.width / viewBox.w` once the aspect ratios match.
 */
export function effectiveScale(viewBox, rect) {
    if (!(viewBox.w > 0) || !(viewBox.h > 0) || !(rect.width > 0) || !(rect.height > 0)) return 1;
    return Math.min(rect.width / viewBox.w, rect.height / viewBox.h);
}
/** Clamps an absolute canvas zoom (screen px per svg unit) to [min, max]. */
export function clampZoom(scale, min = ZOOM_MIN, max = ZOOM_MAX) {
    return Math.min(max, Math.max(min, scale));
}

/**
 * The viewBox a two-finger pinch/pan produces.
 *
 * The svg point that was under the START centre of the fingers ends up under the CURRENT centre, so a pure pan
 * (scale 1) translates the view and a pure pinch zooms around the fingers. The absolute zoom
 * (`rect.width / viewBox.w`) is clamped to [minZoom, maxZoom]; when the clamp bites, the pan still follows.
 *
 * @param {object} p
 * @param {{ x: number, y: number, w: number, h: number }} p.startViewBox viewBox when the pinch began
 * @param {{ left: number, top: number, width: number, height: number }} p.rect the svg's client rect
 * @param {{ x: number, y: number }} p.startCenter finger centre (client px) when the pinch began
 * @param {{ x: number, y: number }} p.center finger centre (client px) now
 * @param {number} p.scale cumulative pinch scale from pointer-gesture.js (>1 = fingers apart = zoom in)
 * @returns {{ x: number, y: number, w: number, h: number, zoom: number }}
 */
export function pinchViewBox({ startViewBox, rect, startCenter, center, scale, minZoom = ZOOM_MIN, maxZoom = ZOOM_MAX }) {
    const start = startViewBox;
    const startZoom = rect.width / start.w;
    const factor = scale > 0 && Number.isFinite(scale) ? scale : 1;
    const zoom = clampZoom(startZoom * factor, minZoom, maxZoom);
    const w = rect.width / zoom;
    const h = start.h * (w / start.w);

    // The svg point under the start centre (proportional mapping, as the wheel zoom uses).
    const px = start.x + (startCenter.x - rect.left) * (start.w / rect.width);
    const py = start.y + (startCenter.y - rect.top) * (start.h / rect.height);
    const x = px - (center.x - rect.left) * (w / rect.width);
    const y = py - (center.y - rect.top) * (h / rect.height);
    return { x, y, w, h, zoom };
}

/**
 * Double-tap detection for touch/pen (a mouse has a real dblclick). Feed it every tap; it answers `true` on
 * the second tap of a pair. A third tap starts a new pair.
 *
 * @param {{ windowMs?: number, slop?: number }} [options]
 * @returns {{ tap: (tap: { x: number, y: number, time: number }) => boolean, reset: () => void }}
 */
export function createDoubleTapTracker({ windowMs = DOUBLE_TAP_MS, slop = DOUBLE_TAP_SLOP } = {}) {
    let last = null;
    return {
        tap({ x, y, time }) {
            const previous = last;
            if (previous && time >= previous.time && time - previous.time <= windowMs
                && Math.hypot(x - previous.x, y - previous.y) <= slop) {
                last = null;
                return true;
            }
            last = { x, y, time };
            return false;
        },
        reset() { last = null; },
    };
}

/**
 * The arrow-key nudge step in svg units (the non-drag alternative to moving an element, WCAG 2.5.7):
 * 1 for a plain arrow, one grid cell with Shift (10 when snapping is off).
 */
export function nudgeStep({ shift = false, gridSize = 0 } = {}) {
    if (!shift) return 1;
    return gridSize > 0 ? gridSize : DEFAULT_SHIFT_STEP;
}
