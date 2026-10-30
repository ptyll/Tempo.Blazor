// Node unit tests for the Tempo.Blazor.Spreadsheet canvas TOUCH GESTURE RECOGNIZER
// (redesign phase 7: touch interaction). The recognizer is the pure "state → action" layer the
// browser gesture code asks for a decision; the actual Pointer Events session comes from the shared
// core helpers (pointer-drag.js / pointer-gesture.js), which are covered by `npm run test:overlay`.
//
// Keeping the decision table pure is what makes the touch rules testable without a browser:
//   tap → select, double tap → edit, drag from a selection handle → extend the selection,
//   drag elsewhere → the browser pans, pinch → one clamped zoom step, long-press 500 ms → the
//   cell context menu, and a mouse pointer never takes the touch path at all.
//
// Run: npm run test:spreadsheet
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const CANVAS_SCRIPT = new URL('../spreadsheet-canvas.js', import.meta.url);
const SOURCE = readFileSync(CANVAS_SCRIPT, 'utf8');

/** Loads the shipped canvas script into an isolated sandbox and returns the pure touch helpers. */
function loadTouchInternals() {
    const sandbox = {
        Element: class StubElement {},
        window: {
            matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
        },
        getComputedStyle: () => ({ getPropertyValue: () => '' }),
        performance: { now: () => 0 },
        requestAnimationFrame: () => 0,
        cancelAnimationFrame: () => undefined,
        setTimeout: () => 0,
        clearTimeout: () => undefined,
        setInterval: () => 0,
        clearInterval: () => undefined,
        console,
    };
    vm.createContext(sandbox);
    vm.runInContext(SOURCE, sandbox, { filename: 'spreadsheet-canvas.js' });
    const internals = sandbox.window.tmSpreadsheetCanvas?._internals;
    assert.ok(internals, 'spreadsheet-canvas.js must expose window.tmSpreadsheetCanvas._internals');

    for (const name of [
        'resolvePointerKind',
        'resolveTouchTap',
        'resolveTouchDragAction',
        'resolveLongPressAction',
        'resolvePinchZoom',
        'resolveCellInputMode',
        'resolveAutoFillHitSize',
        'resolveTouchHandleRects',
        'resolveFilterButtonHitRect',
    ]) {
        assert.equal(typeof internals[name], 'function',
            `_internals.${name} must be a function — the gesture recognizer is the node-tested seam`);
    }

    return internals;
}

const touch = loadTouchInternals();

/** The sandbox has its own object realm, so a returned rect is re-created in this one before comparing. */
function plain(rect) {
    return rect ? { x: rect.x, y: rect.y, width: rect.width, height: rect.height } : rect;
}

// ── tap / double tap ─────────────────────────────────────────────────────────────────────────────

test('tap_SelectsCell', () => {
    const first = touch.resolveTouchTap({ pointerType: 'touch', now: 1000, x: 120, y: 200 });
    assert.equal(first.action, 'select-cell', 'a single touch tap selects the cell under the finger');
    assert.equal(first.tapCount, 1);
    assert.ok(first.next, 'a first tap is remembered as the double-tap candidate');
    assert.equal(first.next.time, 1000);

    const restarted = touch.resolveTouchTap({
        pointerType: 'touch', now: 5000, x: 120, y: 200, previous: first.next,
    });
    assert.equal(restarted.action, 'select-cell', 'a second tap long after the first is a new single tap');

    const moved = touch.resolveTouchTap({
        pointerType: 'touch', now: 1100, x: 120, y: 200,
        previous: { time: 1000, x: 120, y: 200 }, moveDistance: 40,
    });
    assert.equal(moved.action, 'none', 'a tap that travelled is a pan, not a tap');
    assert.equal(moved.next, null, 'a pan arms no double-tap candidate');
});

test('doubleTap_StartsEdit', () => {
    const second = touch.resolveTouchTap({
        pointerType: 'touch', now: 1240, x: 122, y: 201,
        previous: { time: 1000, x: 120, y: 200 },
    });
    assert.equal(second.action, 'start-edit', 'the second tap inside the window opens the cell editor');
    assert.equal(second.tapCount, 2);
    assert.equal(second.next, null, 'a completed double tap forgets the candidate (no triple-tap run)');

    const tooLate = touch.resolveTouchTap({
        pointerType: 'touch', now: 1600, x: 122, y: 201,
        previous: { time: 1000, x: 120, y: 200 },
    });
    assert.equal(tooLate.action, 'select-cell', 'a slow second tap must not start editing');

    const tooFar = touch.resolveTouchTap({
        pointerType: 'touch', now: 1200, x: 200, y: 260,
        previous: { time: 1000, x: 120, y: 200 },
    });
    assert.equal(tooFar.action, 'select-cell', 'two taps far apart are two selections, not an edit');
});

test('doubleTap_InputMode_FollowsTheCellType', () => {
    assert.equal(touch.resolveCellInputMode(null), 'text');
    assert.equal(touch.resolveCellInputMode({ Value: 'hello' }), 'text');
    assert.equal(touch.resolveCellInputMode({ Value: 1234 }), 'decimal', 'a numeric value asks for the decimal keypad');
    assert.equal(touch.resolveCellInputMode({ Value: '1234.5' }), 'decimal');
    assert.equal(touch.resolveCellInputMode({ Value: 0.42, Style: { NumberFormat: '0%' } }), 'decimal');
    assert.equal(touch.resolveCellInputMode({ Value: 45200, Style: { NumberFormat: 'yyyy-mm-dd' } }), 'text',
        'a date cell must not get the decimal keypad');
});

// ── drag origin ─────────────────────────────────────────────────────────────────────────────────

test('dragFromSelectionHandle_ExtendsSelection', () => {
    const action = touch.resolveTouchDragAction({ pointerType: 'touch', origin: 'selection-handle' });
    assert.equal(action.action, 'extend-selection');
    assert.equal(action.kind, 'touch');

    const fill = touch.resolveTouchDragAction({ pointerType: 'touch', origin: 'auto-fill' });
    assert.equal(fill.action, 'fill-handle', 'the fill handle keeps its own (pre-existing) fill drag');

    const owned = touch.resolveTouchDragAction({ pointerType: 'touch', origin: 'filter-button' });
    assert.equal(owned.action, 'canvas-owned', 'the header filter button is tapped, never dragged by the pan layer');
});

test('dragElsewhere_IsNativePan', () => {
    for (const origin of ['cell', 'header', 'empty', undefined]) {
        const action = touch.resolveTouchDragAction({ pointerType: 'touch', origin });
        assert.equal(action.action, 'native-pan',
            `a finger drag starting on '${origin}' must stay a native pan — the browser scrolls`);
        assert.equal(action.preventDefault, false, 'a pan gesture must never preventDefault the press');
    }
});

test('mousePointer_PathUnchanged', () => {
    assert.equal(touch.resolvePointerKind(undefined), 'mouse');
    assert.equal(touch.resolvePointerKind('mouse'), 'mouse');
    assert.equal(touch.resolvePointerKind('pen'), 'pen');
    assert.equal(touch.resolvePointerKind('touch'), 'touch');

    for (const origin of ['cell', 'header', 'empty', 'selection-handle', 'auto-fill', 'filter-button']) {
        const action = touch.resolveTouchDragAction({ pointerType: 'mouse', origin });
        assert.equal(action.action, 'native-mouse',
            'the mouse keeps the pre-phase-7 click/drag path for every origin');
    }

    const tap = touch.resolveTouchTap({ pointerType: 'mouse', now: 1000, x: 10, y: 10 });
    assert.equal(tap.action, 'native-mouse', 'a mouse click is never recognized as a touch tap');
    assert.equal(tap.next, null, 'the mouse arms no touch double-tap state');
});

// ── long press ──────────────────────────────────────────────────────────────────────────────────

test('longPress500ms_OpensContextMenu', () => {
    assert.equal(touch.resolveLongPressAction({ pointerType: 'touch', durationMs: 500 }).action, 'open-context-menu',
        'the finger rests 500 ms → the cell context menu opens at the finger');
    assert.equal(touch.resolveLongPressAction({ pointerType: 'touch', durationMs: 499 }).action, 'none',
        'a press shorter than the threshold is not a menu');
    assert.equal(touch.resolveLongPressAction({ pointerType: 'mouse', durationMs: 5000 }).action, 'none',
        'a mouse never long-presses into a menu — the right button owns that');
    assert.equal(touch.resolveLongPressAction({ pointerType: 'pen', durationMs: 900 }).action, 'none',
        'the pen keeps the right-button/Escape path');
    assert.equal(touch.resolveLongPressAction({}).longPressMs, 500, 'the threshold is the documented 500 ms');
});

// ── pinch ───────────────────────────────────────────────────────────────────────────────────────

test('pinch_EmitsZoomStep_Clamped', () => {
    const zoomIn = touch.resolvePinchZoom({ startZoom: 1, scale: 2.5 });
    assert.equal(zoomIn.zoom, 2, 'the pinch zoom is clamped to the component maximum (200 %)');
    assert.equal(zoomIn.direction, 1);
    assert.equal(zoomIn.changed, true);

    const zoomOut = touch.resolvePinchZoom({ startZoom: 0.7, scale: 0.05 });
    assert.equal(zoomOut.zoom, 0.5, 'the pinch zoom is clamped to the component minimum (50 %)');
    assert.equal(zoomOut.direction, -1);

    const jitter = touch.resolvePinchZoom({ startZoom: 1, scale: 1.01 });
    assert.equal(jitter.changed, false, 'a finger tremor inside one 10 % step emits nothing');
    assert.equal(jitter.zoom, 1);

    const step = touch.resolvePinchZoom({ startZoom: 1, scale: 1.35 });
    assert.equal(step.zoom, 1.3, 'the pinch zooms in whole 10 % steps, the same grid the zoom buttons use');
    assert.equal(step.changed, true);

    const clampedStart = touch.resolvePinchZoom({ startZoom: 1.95, scale: 1.5 });
    assert.equal(clampedStart.zoom, 2, 'a zoom already near the ceiling still stops at it');
    assert.equal(clampedStart.clamped, true);
});

// ── hit areas (density / coarse pointer) ────────────────────────────────────────────────────────

test('resolveAutoFillHitSize_MouseIsUnchanged_DensityAndCoarseGrowIt', () => {
    assert.equal(touch.resolveAutoFillHitSize({ handleScale: 1, coarse: false }), 10,
        'the compact fine-pointer fill handle keeps its historic 10px hit box — no mouse regression');
    assert.ok(touch.resolveAutoFillHitSize({ handleScale: 1.4, coarse: false }) > 10,
        'the comfortable density grows the HIT box, not only the painted handle');
    assert.ok(touch.resolveAutoFillHitSize({ handleScale: 1, coarse: true }) >= 24,
        'a coarse pointer gets a finger-sized fill-handle hit box even at the compact density');
    assert.ok(touch.resolveAutoFillHitSize({ handleScale: 1.4, coarse: true })
        > touch.resolveAutoFillHitSize({ handleScale: 1.4, coarse: false }),
        'coarse + comfortable is larger than comfortable alone');
});

test('resolveTouchHandleRects_TwoCornerDropsPlusTheFillHandle', () => {
    const rects = touch.resolveTouchHandleRects({ x: 100, y: 50, width: 200, height: 80, hitSize: 28 });

    // The START drop is centred on the selection's top-left corner.
    assert.deepEqual(plain(rects.start), { x: 86, y: 36, width: 28, height: 28 });
    // The FILL box keeps Excel's corner (the painted square).
    assert.deepEqual(plain(rects.fill), { x: 286, y: 116, width: 28, height: 28 });
    // The END drop sits just OUTSIDE the fill handle so the two never share a centre.
    assert.ok(rects.end.x > rects.fill.x && rects.end.y > rects.fill.y,
        'the bottom-right drop is offset from the fill handle');
    assert.equal(rects.end.width, 28);

    // There are no edge strips any more: they swallowed taps meant for the selected row/column.
    for (const gone of ['top', 'bottom', 'left', 'right']) {
        assert.equal(rects[gone], undefined, `the '${gone}' edge strip must be gone`);
    }

    const coarse = touch.resolveTouchHandleRects({ x: 0, y: 0, width: 40, height: 30, coarse: true });
    assert.equal(coarse.fill.width, 28, 'a coarse pointer gets a finger-sized drop');
    assert.equal(coarse.start.width, 28);

    const hidden = touch.resolveTouchHandleRects({ x: 0, y: 0, width: 0, height: 0 });
    assert.equal(hidden.fill, null, 'no selection → no handle rects (the layer is hidden)');
});

test('clipTouchHandleRect_ClipsAtTheGutters_AndHidesASliver', () => {
    const bounds = { x: 56, y: 28, width: 400, height: 300 };

    const inside = touch.clipTouchHandleRect({
        rect: { x: 100, y: 100, width: 28, height: 28 }, bounds, minSize: 16,
    });
    assert.deepEqual(plain(inside), { x: 100, y: 100, width: 28, height: 28 },
        'a drop fully inside the body is untouched');

    // A drop hanging over the column header is CLIPPED, never shifted onto the selection: the visible
    // sliver stops at the gutter edge.
    const atGutter = touch.clipTouchHandleRect({
        rect: { x: 60, y: 18, width: 28, height: 28 }, bounds, minSize: 16,
    });
    assert.deepEqual(plain(atGutter), { x: 60, y: 28, width: 28, height: 18 },
        'the clipped box starts at the gutter, not at the requested y');

    const sliver = touch.clipTouchHandleRect({
        rect: { x: 60, y: 20, width: 28, height: 20 }, bounds, minSize: 16,
    });
    assert.equal(sliver, null, 'a sliver thinner than minSize is hidden instead of clipped');

    const outside = touch.clipTouchHandleRect({
        rect: { x: 0, y: 0, width: 28, height: 28 }, bounds, minSize: 16,
    });
    assert.equal(outside, null, 'a drop fully over the gutter is hidden');
});

test('isCanvasShortcutCommand_ClaimsTheFillChords_SoTheyNeverReachTheBrowser', () => {
    // Ctrl+D / Ctrl+R must be claimed by the canvas: unclaimed, Ctrl+D opens the browser's bookmark
    // dialog and Ctrl+R RELOADS the page with the grid on it.
    for (const key of ['d', 'r', 'c', 'v', 'x', 'z', 'y', 'b', 'i', 'u', 'a', '1', '5', 'f', 'h', '0']) {
        assert.equal(touch.isCanvasShortcutCommand(key, key), true, `Ctrl+${key.toUpperCase()} must be claimed`);
    }

    assert.equal(touch.isCanvasShortcutCommand('Home', 'Home'), true);
    assert.equal(touch.isCanvasShortcutCommand('End', 'End'), true);
    for (const key of ['s', 'p', 'n', 't', 'w']) {
        assert.equal(touch.isCanvasShortcutCommand(key, key), false,
            `Ctrl+${key.toUpperCase()} is not a canvas command — the browser keeps it`);
    }
});

test('resolveFilterButtonHitRect_CoarseGrowsToTouchTarget_FineIsUntouched', () => {
    const painted = { x: 200, y: 4, w: 16, h: 12 };
    const fine = touch.resolveFilterButtonHitRect({ rect: painted, coarse: false, headerHeight: 20, columnWidth: 64 });
    assert.deepEqual(plain(fine), { x: 200, y: 4, width: 16, height: 12 },
        'a fine pointer keeps the painted filter-button rect — no mouse regression');

    const coarse = touch.resolveFilterButtonHitRect({ rect: painted, coarse: true, headerHeight: 28, columnWidth: 64 });
    assert.ok(coarse.width >= 32, `a coarse pointer grows the filter hit width (was ${coarse.width})`);
    assert.equal(coarse.height, 28, 'the hit box fills the header gutter height');
    assert.ok(coarse.x <= 200 && coarse.x + coarse.width >= 216, 'the painted glyph stays inside the hit box');
    assert.ok(coarse.x >= 200 + 16 - 64,
        'the hit box never leaves the column (it would steal the neighbour header)');
});
