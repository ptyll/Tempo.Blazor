// pdf-viewer-size.mjs tests — the pure canvas-size math used by renderPage,
// renderAllPages and the thumbnails (HiDPI backing store with a DPR cap of 2).
import test from 'node:test';
import assert from 'node:assert/strict';
import { canvasSize, MAX_DEVICE_PIXEL_RATIO } from '../pdf-viewer-size.mjs';

test('MAX_DEVICE_PIXEL_RATIO is 2', () => {
    assert.equal(MAX_DEVICE_PIXEL_RATIO, 2);
});

test('canvasSize at dpr 1 keeps the viewport size as the backing store', () => {
    assert.deepEqual(canvasSize(612, 792, 1), { width: 612, height: 792, cssWidth: 612, cssHeight: 792, dpr: 1 });
});

test('canvasSize doubles the backing store at dpr 2 while CSS size stays the viewport', () => {
    const size = canvasSize(612, 792, 2);
    assert.equal(size.width, 1224);
    assert.equal(size.height, 1584);
    assert.equal(size.cssWidth, 612);
    assert.equal(size.cssHeight, 792);
    assert.equal(size.dpr, 2);
});

test('canvasSize caps the device pixel ratio at 2', () => {
    const size = canvasSize(100, 100, 3);
    assert.equal(size.width, 200);
    assert.equal(size.height, 200);
    assert.equal(size.dpr, 2);
});

test('canvasSize rounds fractional backing sizes', () => {
    const size = canvasSize(612.3, 792.7, 1.5);
    assert.equal(size.width, Math.round(612.3 * 1.5));
    assert.equal(size.height, Math.round(792.7 * 1.5));
    assert.equal(size.cssWidth, 612.3);
    assert.equal(size.cssHeight, 792.7);
});

test('canvasSize treats non-finite or non-positive dpr as 1', () => {
    for (const dpr of [0, -1, NaN, undefined]) {
        const size = canvasSize(100, 100, dpr);
        assert.equal(size.dpr, 1);
        assert.equal(size.width, 100);
        assert.equal(size.height, 100);
    }
});

test('canvasSize never returns a zero-dimension backing store', () => {
    const size = canvasSize(0, 0, 2);
    assert.equal(size.width, 1);
    assert.equal(size.height, 1);
});
