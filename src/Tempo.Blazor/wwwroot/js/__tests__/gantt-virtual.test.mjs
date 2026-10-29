// gantt-virtual.mjs: the pure window virtualization maths of TmGantt (Fáze 12) — which rows the viewport
// shows, when a shift is worth a .NET render (the overscan/2 hysteresis) and where a scrollToRow lands.
import test from 'node:test';
import assert from 'node:assert/strict';

import { computeWindow, shouldNotify, alignScrollTop } from '../gantt-virtual.mjs';

// ── computeWindow ───────────────────────────────────────────────────────────────────────────────

test('computeWindow returns the viewport rows plus the overscan on both sides', () => {
    // 40 px rows, an 800 px viewport = 20 visible rows, overscan 10: scrollTop 4000 = row 100.
    const win = computeWindow(4000, 800, 1000, 40, 10);
    assert.equal(win.first, 90, 'row 100 minus the 10 overscan rows above');
    assert.equal(win.last, 130, '20 visible rows + 10 overscan rows below, exclusive');
});

test('computeWindow clamps to the data on both edges', () => {
    // scrollTop 0: the 10 overscan rows above are clamped away, 20 visible + 10 below remain.
    assert.deepEqual(computeWindow(0, 800, 1000, 40, 10), { first: 0, last: 30 });
    const tail = computeWindow(999999, 800, 1000, 40, 10);
    assert.equal(tail.first, 999, 'an overscroll past the end keeps the last row painted');
    assert.equal(tail.last, 1000, 'never past the last row');
});

test('computeWindow without rows or a row height reports an empty window', () => {
    assert.deepEqual(computeWindow(0, 800, 0, 40, 10), { first: 0, last: 0 });
    assert.deepEqual(computeWindow(100, 800, 50, 0, 10), { first: 0, last: 0 });
});

test('computeWindow treats a negative scroll and overscan as zero', () => {
    assert.deepEqual(computeWindow(-50, 800, 1000, 40, -5), { first: 0, last: 20 });
});

test('computeWindow rounds a partial viewport row up', () => {
    // 850 px over 40 px rows = 21.25 visible rows → 22, so the last row is never clipped mid-paint.
    const win = computeWindow(0, 850, 1000, 40, 0);
    assert.equal(win.last, 22);
});

test('computeWindow with overscan 0 renders exactly the visible rows', () => {
    // scrollTop 4000 = row 100, viewport 800 px = rows 100..119: nothing above, nothing below.
    assert.deepEqual(computeWindow(4000, 800, 1000, 40, 0), { first: 100, last: 120 });
});

test('computeWindow keeps the partially visible edge rows at a mid-row scrollTop', () => {
    // scrollTop 4020: row 100 is half scrolled away at the top edge, row 121 peeks in at the bottom —
    // floor/ceil of the real pixel range keeps both painted.
    assert.deepEqual(computeWindow(4020, 800, 1000, 40, 0), { first: 100, last: 121 });
    const win = computeWindow(4020, 800, 1000, 40, 10);
    assert.equal(win.first, 90, 'overscan above');
    assert.equal(win.last, 131, 'overscan below');
});

// ── shouldNotify (the hysteresis) ───────────────────────────────────────────────────────────────

test('shouldNotify always reports the first measurement', () => {
    assert.equal(shouldNotify(null, { first: 0, last: 30 }, 10), true);
});

test('shouldNotify stays silent for a shift below overscan/2 rows', () => {
    const prev = { first: 100, last: 140 };
    assert.equal(shouldNotify(prev, { first: 104, last: 144 }, 10), false, '4 < 10/2 = 5');
    assert.equal(shouldNotify(prev, { first: 105, last: 145 }, 10), true, '5 ≥ 10/2');
});

test('shouldNotify is silent when the window did not move', () => {
    assert.equal(shouldNotify({ first: 100, last: 140 }, { first: 100, last: 140 }, 10), false);
});

test('shouldNotify reports when either edge moved past the threshold', () => {
    assert.equal(shouldNotify({ first: 100, last: 140 }, { first: 100, last: 146 }, 10), true, 'the lower edge alone');
    assert.equal(shouldNotify({ first: 100, last: 140 }, { first: 94, last: 140 }, 10), true, 'the upper edge alone');
});

test('shouldNotify with overscan 0 reports every move', () => {
    assert.equal(shouldNotify({ first: 10, last: 20 }, { first: 10, last: 21 }, 0), true);
    assert.equal(shouldNotify({ first: 10, last: 20 }, { first: 10, last: 20 }, 0), false);
});

// ── alignScrollTop ──────────────────────────────────────────────────────────────────────────────

test('alignScrollTop start puts the row at the top edge', () => {
    assert.equal(alignScrollTop(25, 40, 800, 'start', 0), 1000);
});

test('alignScrollTop end puts the row at the bottom edge', () => {
    assert.equal(alignScrollTop(25, 40, 800, 'end', 0), 1040 - 800);
    assert.equal(alignScrollTop(2, 40, 800, 'end', 0), 0, 'never negative');
});

test('alignScrollTop nearest scrolls a row below the viewport into view', () => {
    // Viewport rows 0..19 (scrollTop 0); row 30 is below.
    assert.equal(alignScrollTop(30, 40, 800, 'nearest', 0), 1200);
});

test('alignScrollTop nearest scrolls a row above the viewport into view', () => {
    // Viewport rows 20..39 (scrollTop 800); row 5 is above.
    assert.equal(alignScrollTop(5, 40, 800, 'nearest', 800), 200);
});

test('alignScrollTop nearest keeps a fully visible row where it is', () => {
    assert.equal(alignScrollTop(25, 40, 800, 'nearest', 800), 800);
});

test('alignScrollTop nearest pulls a row straddling the bottom edge up', () => {
    // Viewport rows 20..39 (scrollTop 800); row 39 straddles nothing, row 40 is just below — but a row that
    // ends past the bottom edge is pulled up.
    assert.equal(alignScrollTop(39, 40, 800, 'nearest', 770), 800, 'the row bottom 1600 - 800');
});
