import assert from 'node:assert/strict';
import { test } from 'node:test';
import { selectionViewportRect } from './interop.mjs';

// Phase 5 (DE-1.6): the form-field popover anchors to the selected field through a viewport rectangle: the page-local rect the selection
// controller reports plus the page element's client rect.
const engineWithPage = (bounds, zoomScale) => ({
    canvasStack: {
        pages: new Map([['0', {
            pageElement: {
                getBoundingClientRect: () => bounds,
                getAttribute: (name) => (name === 'data-canvas-page-zoom-scale' && zoomScale !== undefined ? String(zoomScale) : null),
            },
        }]]),
    },
});

test('a collapsed caret becomes a viewport rectangle (page origin + caret rect)', () => {
    const rect = selectionViewportRect(
        engineWithPage({ left: 100, top: 40 }),
        { caretRect: { x: 12, y: 30, width: 2, height: 16 }, pageIndex: 0 });
    assert.deepEqual(rect, { left: 112, top: 70, width: 2, height: 16 });
});

test('a range uses the bounding rect and its own page', () => {
    const rect = selectionViewportRect(
        engineWithPage({ left: 10, top: 5 }),
        { boundingRect: { pageIndex: 0, x: 50, y: 60, width: 80, height: 18 }, caretRect: { x: 1, y: 1, width: 1, height: 1 }, pageIndex: 3 });
    assert.deepEqual(rect, { left: 60, top: 65, width: 80, height: 18 });
});

test('a zoomed page scales the page-local rect (data-canvas-page-zoom-scale)', () => {
    const rect = selectionViewportRect(
        engineWithPage({ left: 100, top: 40 }, 1.5),
        { caretRect: { x: 12, y: 30, width: 2, height: 16 }, pageIndex: 0 });
    assert.deepEqual(rect, { left: 118, top: 85, width: 3, height: 24 });
});

test('a missing scale attribute means 100 %', () => {
    const rect = selectionViewportRect(
        engineWithPage({ left: 0, top: 0 }, undefined),
        { caretRect: { x: 10, y: 10, width: 4, height: 4 }, pageIndex: 0 });
    assert.deepEqual(rect, { left: 10, top: 10, width: 4, height: 4 });
});

test('a rectangle is never smaller than one pixel', () => {
    const rect = selectionViewportRect(engineWithPage({ left: 0, top: 0 }), { caretRect: { x: 5, y: 5, width: 0, height: 0 }, pageIndex: 0 });
    assert.equal(rect.width, 1);
    assert.equal(rect.height, 1);
});

test('no rect, or a page that is not mounted, gives null', () => {
    assert.equal(selectionViewportRect(engineWithPage({ left: 0, top: 0 }), { pageIndex: 0 }), null);
    assert.equal(selectionViewportRect({ canvasStack: { pages: new Map() } }, { caretRect: { x: 1, y: 1, width: 1, height: 1 }, pageIndex: 2 }), null);
    assert.equal(selectionViewportRect(undefined, undefined), null);
});
