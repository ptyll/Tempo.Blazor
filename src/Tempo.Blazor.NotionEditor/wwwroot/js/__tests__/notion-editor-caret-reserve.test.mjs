// notion-editor.js: the caret-block viewport reserve (CF3/B2, plan 98c80bc3).
//
// Enter on the last line lands the fresh caret block flush with the fold, and a '/'
// anchor right after reads as out of view for overlay.js — which then PARKS the slash
// panel invisibly. The editor keeps a reserve band under the landed block: a minimal
// downward shift of the nearest scroller (never a centering scrollIntoView — that walked
// every ancestor and displaced the host page). The decision is pure and node-tested;
// the DOM reads stay in notion-editor.js.
import test from 'node:test';
import assert from 'node:assert/strict';

globalThis.window = { addEventListener() {}, removeEventListener() {} };
globalThis.document = { addEventListener() {}, removeEventListener() {} };

const { caretReserveDelta, caretReserve } = await import('../notion-editor.js').then(
    () => globalThis.window.tmNotionEditor);

test('caretReserveDelta: a caret block in the viewport middle needs no shift', () => {
    // 900px viewport, 200px reserve → the band bottom is 700; a block ending at 450 sits well above it.
    const delta = caretReserveDelta({ top: 380, bottom: 450 }, 900, 200);
    assert.equal(delta, 0, 'a comfortably visible block must not scroll at all');
});

test('caretReserveDelta: a caret block at the top edge needs no shift', () => {
    // Only the BOTTOM band is handled — the top edge never scrolls (no centering, B2/M1).
    const delta = caretReserveDelta({ top: 0, bottom: 28 }, 900, 200);
    assert.equal(delta, 0, 'a block at the top edge must not scroll');
});

test('caretReserveDelta: a block below the band returns the exact shift', () => {
    // bottom 898, band bottom 700 → the scroller moves down by exactly 198px.
    const delta = caretReserveDelta({ top: 850, bottom: 898 }, 900, 200);
    assert.equal(delta, 198, 'the shift is rect.bottom − (viewBottom − reserve), exact');
});

test('caretReserveDelta: a block flush with the fold returns its overshoot', () => {
    const delta = caretReserveDelta({ top: 880, bottom: 902 }, 900, 200);
    assert.equal(delta, 202, 'even an anchor past the fold resolves to the exact shift');
});

test('caretReserveDelta: tolerates missing geometry', () => {
    assert.equal(caretReserveDelta(null, 900, 200), 0);
    assert.equal(caretReserveDelta(undefined, 900, 200), 0);
    assert.equal(caretReserveDelta({}, undefined, undefined), 0, 'no view → nothing to decide');
});

test('caretReserveDelta: without a bandTop the shift stays unbounded (legacy callers)', () => {
    const delta = caretReserveDelta({ top: 850, bottom: 898 }, 900, 200);
    assert.equal(delta, 198, 'the default bandTop (-Infinity) keeps the exact raw shift');
});

// ── B1: the band is bounded by the scroller's strip, not the whole viewport ──

test('caretReserveDelta: a small scroller never rolls the block above its own top edge', () => {
    // Viewport 900 but the band is the scroller's 150px strip (bandTop 0): a block ending
    // at the strip's bottom edge would otherwise shift by the full reserve (min(200, 0.25
    // × 150) = 37.5) and its TOP (10) would pass 27.5px ABOVE the scroller's top edge —
    // the landed block rolled out of its own visible strip. The shift is clamped to
    // rect.top − bandTop = 10.
    const delta = caretReserveDelta({ top: 10, bottom: 150 }, 150, 37.5, 0);
    assert.ok(delta <= 10 - 0, `the shift (${delta}) must not exceed rect.top − bandTop`);
    assert.equal(delta, 10, 'the shift is exactly rect.top − bandTop — the block top stays visible');
});

test('caretReserveDelta: the band-top clamp never binds for a roomy band', () => {
    // Same clamp with bandTop 0 — a mid-band block is untouched (raw 0) and a genuinely
    // hanging block still gets its exact raw shift (the clamp is far away).
    assert.equal(caretReserveDelta({ top: 380, bottom: 450 }, 900, 200, 0), 0);
    assert.equal(caretReserveDelta({ top: 850, bottom: 898 }, 900, 200, 0), 198);
});

test('caretReserveDelta: a block already above the band top never shifts (no upward/negative shift)', () => {
    // The block hangs 37.5 past the band but starts ABOVE it — no downward shift can
    // satisfy the reserve without rolling it further out; the clamp floors the shift at 0.
    const delta = caretReserveDelta({ top: -10, bottom: 150 }, 150, 37.5, 0);
    assert.equal(delta, 0, 'the clamp never produces a negative (upward) shift');
});

test('caretReserve: a large viewport reserves the 200px cap', () => {
    assert.equal(caretReserve(900), 200, 'min(200, 0.25 × 900) = 200 — the cap wins');
    assert.equal(caretReserve(2000), 200);
});

test('caretReserve: a small viewport reserves a quarter of it (smaller than the cap)', () => {
    assert.equal(caretReserve(400), 100, 'min(200, 0.25 × 400) = 100 — a small viewport reserves less');
    assert.equal(caretReserve(120), 30, 'a phone-with-keyboard viewport shrinks the reserve further');
});
