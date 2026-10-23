// wireframe-gestures.mjs: the pure, wireframe-specific gesture maths (double-tap, coarse handle sizes, the
// pinch -> viewBox maths with the 0.15x-5x zoom clamp, the keyboard nudge step). Pinch DETECTION and the
// long-press are the core's (pointer-gesture.js / pointer-drag.js) and are covered by their own tests.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    LONG_PRESS_MS, ZOOM_MIN, ZOOM_MAX, COARSE_HANDLE_SCALE, DOUBLE_TAP_MS,
    isCoarsePointer, handleScale, handleSizes, clampZoom, pinchViewBox, createDoubleTapTracker, nudgeStep, matchAspect, effectiveScale,
} from '../wireframe-gestures.mjs';

const near = (actual, expected, epsilon = 1e-9) =>
    assert.ok(Math.abs(actual - expected) <= epsilon, `${actual} is not within ${epsilon} of ${expected}`);

// ── constants ──────────────────────────────────────────────────────────────────

test('the long-press is the 500 ms the plan names and the zoom clamp is 0.15x-5x', () => {
    assert.equal(LONG_PRESS_MS, 500);
    assert.equal(ZOOM_MIN, 0.15);
    assert.equal(ZOOM_MAX, 5);
    assert.equal(COARSE_HANDLE_SCALE, 1.5);
});

// ── coarse pointer / handle hit areas ───────────────────────────────────────────

test('isCoarsePointer reads (pointer: coarse) and never throws', () => {
    assert.equal(isCoarsePointer({ matchMedia: q => ({ matches: q === '(pointer: coarse)' }) }), true);
    assert.equal(isCoarsePointer({ matchMedia: () => ({ matches: false }) }), false);
    assert.equal(isCoarsePointer({}), false, 'no matchMedia (SSR / node)');
    assert.equal(isCoarsePointer({ matchMedia() { throw new Error('boom'); } }), false);
});

test('handle hit-area is x1.5 on a coarse pointer and unchanged on a fine one', () => {
    assert.equal(handleScale(false), 1);
    assert.equal(handleScale(true), 1.5);
    assert.deepEqual(handleSizes({ coarse: false }), { visual: 8, hit: 8 });
    const coarse = handleSizes({ coarse: true });
    assert.equal(coarse.visual, 12, '8 * 1.5');
    assert.ok(coarse.hit >= coarse.visual, 'the hit area is never smaller than what is painted');
});

test('on a coarse pointer the invisible hit area reaches 24 screen px at the current zoom', () => {
    // zoom 0.5: 24 screen px = 48 svg units
    assert.equal(handleSizes({ coarse: true, zoom: 0.5 }).hit, 48);
    // zoom 2: 24 screen px = 12 svg units == the painted 12, no growth beyond the paint
    assert.equal(handleSizes({ coarse: true, zoom: 2 }).hit, 12);
    // a fine pointer ignores the zoom
    assert.equal(handleSizes({ coarse: false, zoom: 0.25 }).hit, 8);
});

test('the hit area is capped so the handles of a small element do not overlap', () => {
    // zoom 1: the hit rect would like 24 units; a 20-unit cap (a small element) wins, the painted 12 stays
    assert.equal(handleSizes({ coarse: true, zoom: 1, maxHit: 20 }).hit, 20);
    assert.equal(handleSizes({ coarse: true, zoom: 0.25, maxHit: 4 }).hit, 48, 'never below the painted size (12 screen px = 48 units at zoom 0.25)');
});

test('on a coarse pointer the PAINTED handle stays at least 12 screen px when the page is fitted far out', () => {
    assert.equal(handleSizes({ coarse: true, zoom: 1 }).visual, 12, 'x1.5 at zoom 1');
    assert.equal(handleSizes({ coarse: true, zoom: 0.3 }).visual, 40, '12 screen px = 40 units at zoom 0.3');
    assert.equal(handleSizes({ coarse: false, zoom: 0.3 }).visual, 8, 'a fine pointer is unchanged by zoom');
    assert.equal(handleSizes({ coarse: true, zoom: 0.3, maxVisual: 18 }).visual, 18, 'a small element caps its own handles');
    assert.equal(handleSizes({ coarse: true, zoom: 0.3, maxVisual: 5 }).visual, 12, 'but never below x1.5');
});

// ── zoom clamp ──────────────────────────────────────────────────────────────────

test('clampZoom keeps the absolute zoom within 0.15x-5x', () => {
    assert.equal(clampZoom(0.01), 0.15);
    assert.equal(clampZoom(9), 5);
    assert.equal(clampZoom(1.25), 1.25);
});

// ── pinch -> viewBox ────────────────────────────────────────────────────────────

const rect = { left: 100, top: 50, width: 800, height: 600 };
const startViewBox = { x: 0, y: 0, w: 800, h: 600 };   // zoom 1

test('a pinch with the fingers twice as far apart zooms in 2x around the finger centre', () => {
    const center = { x: 500, y: 350 };   // the svg point (400, 300)
    const vb = pinchViewBox({ startViewBox, rect, startCenter: center, center, scale: 2 });
    near(vb.zoom, 2);
    near(vb.w, 400);
    near(vb.h, 300);
    // the svg point under the fingers (400, 300) is still under them
    near(vb.x + (center.x - rect.left) * (vb.w / rect.width), 400);
    near(vb.y + (center.y - rect.top) * (vb.h / rect.height), 300);
});

test('a pinch out (scale < 1) zooms out', () => {
    const c = { x: 500, y: 350 };
    const vb = pinchViewBox({ startViewBox, rect, startCenter: c, center: c, scale: 0.5 });
    near(vb.zoom, 0.5);
    near(vb.w, 1600);
});

test('two-finger pan at scale 1 translates the view and never changes the zoom', () => {
    const vb = pinchViewBox({
        startViewBox, rect,
        startCenter: { x: 300, y: 200 }, center: { x: 400, y: 260 }, scale: 1,
    });
    near(vb.zoom, 1);
    near(vb.w, 800);
    near(vb.x, -100, 1e-9);   // fingers moved right by 100px -> the view shifts left by 100 svg units
    near(vb.y, -60, 1e-9);
});

test('the zoom is clamped to 0.15x and 5x whatever the fingers do', () => {
    const c = { x: 500, y: 350 };
    const zoomedIn = pinchViewBox({ startViewBox, rect, startCenter: c, center: c, scale: 40 });
    near(zoomedIn.zoom, 5);
    near(zoomedIn.w, 160);
    const zoomedOut = pinchViewBox({ startViewBox, rect, startCenter: c, center: c, scale: 0.001 });
    near(zoomedOut.zoom, 0.15);
    near(zoomedOut.w, 800 / 0.15);
});

test('a degenerate scale (0, NaN, negative) leaves the zoom unchanged', () => {
    const c = { x: 500, y: 350 };
    for (const scale of [0, NaN, -2, Infinity]) {
        const vb = pinchViewBox({ startViewBox, rect, startCenter: c, center: c, scale });
        assert.equal(Number.isFinite(vb.w), true, `scale ${scale}`);
    }
    near(pinchViewBox({ startViewBox, rect, startCenter: c, center: c, scale: NaN }).zoom, 1);
});

test('the zoom is measured from the start viewBox, not from 1 (an already zoomed canvas)', () => {
    const zoomed = { x: 100, y: 100, w: 400, h: 300 };   // zoom 2
    const c = { x: 500, y: 350 };
    const vb = pinchViewBox({ startViewBox: zoomed, rect, startCenter: c, center: c, scale: 2 });
    near(vb.zoom, 4);
    near(vb.w, 200);
});

// ── double-tap ──────────────────────────────────────────────────────────────────

test('a second tap in the window and radius is a double-tap; the third starts over', () => {
    const t = createDoubleTapTracker();
    assert.equal(t.tap({ x: 10, y: 10, time: 1000 }), false);
    assert.equal(t.tap({ x: 14, y: 12, time: 1000 + 200 }), true, 'second tap');
    assert.equal(t.tap({ x: 14, y: 12, time: 1000 + 300 }), false, 'a triple tap is not a second double-tap');
    assert.equal(t.tap({ x: 14, y: 12, time: 1000 + 400 }), true, 'but the next pair is');
});

test('taps too slow or too far apart are not a double-tap', () => {
    const t = createDoubleTapTracker();
    t.tap({ x: 0, y: 0, time: 0 });
    assert.equal(t.tap({ x: 0, y: 0, time: DOUBLE_TAP_MS + 1 }), false, 'too slow');
    assert.equal(t.tap({ x: 200, y: 0, time: DOUBLE_TAP_MS + 100 }), false, 'too far');
});

test('reset forgets the pending first tap', () => {
    const t = createDoubleTapTracker();
    t.tap({ x: 0, y: 0, time: 0 });
    t.reset();
    assert.equal(t.tap({ x: 0, y: 0, time: 100 }), false);
});

test('a tap that arrives out of order (clock skew) never counts', () => {
    const t = createDoubleTapTracker();
    t.tap({ x: 0, y: 0, time: 500 });
    assert.equal(t.tap({ x: 0, y: 0, time: 400 }), false);
});

// ── nudge ───────────────────────────────────────────────────────────────────────

test('arrow nudge: 1 unit, Shift = one grid cell (10 when snapping is off)', () => {
    assert.equal(nudgeStep({ shift: false, gridSize: 8 }), 1);
    assert.equal(nudgeStep({ shift: true, gridSize: 8 }), 8);
    assert.equal(nudgeStep({ shift: true, gridSize: 0 }), 10);
    assert.equal(nudgeStep({ shift: true }), 10);
});

// ── aspect matching / effective scale ───────────────────────────────────────────

test('matchAspect widens the viewBox to the rect aspect around its centre and never moves what is visible', () => {
    // a 1440x900 page in an 800x600 box: meet fits the width (scale 0.5555), letterboxed vertically
    const vb = matchAspect({ x: 0, y: 0, w: 1440, h: 900 }, { width: 800, height: 600 });
    near(vb.w, 1440);
    near(vb.h, 1080, 1e-6);          // 600 / (800/1440)
    near(vb.x, 0);
    near(vb.y, -90, 1e-6);           // centred: (900 - 1080) / 2
    near(800 / vb.w, 600 / vb.h);    // one scale on both axes now
});

test('matchAspect of an already matching viewBox is the identity', () => {
    assert.deepEqual(matchAspect({ x: 10, y: 20, w: 400, h: 300 }, { width: 800, height: 600 }), { x: 10, y: 20, w: 400, h: 300 });
});

test('matchAspect and effectiveScale tolerate a collapsed rect (a hidden canvas)', () => {
    assert.deepEqual(matchAspect({ x: 0, y: 0, w: 100, h: 50 }, { width: 0, height: 0 }), { x: 0, y: 0, w: 100, h: 50 });
    assert.equal(effectiveScale({ x: 0, y: 0, w: 100, h: 50 }, { width: 0, height: 0 }), 1);
});

test('effectiveScale is the smaller axis scale (what meet renders with)', () => {
    near(effectiveScale({ x: 0, y: 0, w: 1440, h: 900 }, { width: 800, height: 600 }), 800 / 1440);
    near(effectiveScale({ x: 0, y: 0, w: 400, h: 1000 }, { width: 800, height: 600 }), 0.6);
});
