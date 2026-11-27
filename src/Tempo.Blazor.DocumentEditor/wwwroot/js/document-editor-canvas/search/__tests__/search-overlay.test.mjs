import assert from 'node:assert/strict';
import test from 'node:test';
import { createCanvasSearchOverlay } from '../search-overlay.mjs';

// A minimal DOM: just enough surface for the overlay (createElement, children, setAttribute, querySelector).
function fakeElement(tag) {
    return {
        tag,
        style: {},
        children: [],
        attributes: new Map(),
        className: '',
        setAttribute(name, value) { this.attributes.set(name, String(value)); },
        getAttribute(name) { return this.attributes.has(name) ? this.attributes.get(name) : null; },
        appendChild(child) { this.children.push(child); child.parent = this; return child; },
        replaceChildren() { this.children.length = 0; },
        remove() { if (this.parent) { this.parent.children = this.parent.children.filter(c => c !== this); } },
        querySelector(selector) {
            const testId = /data-testid="([^"]+)"/.exec(selector)?.[1];
            return this.children.find(child => child.getAttribute('data-testid') === testId) || null;
        },
    };
}

function setup(zoomScale = null) {
    const doc = { createElement: fakeElement };
    const pageElement = fakeElement('div');
    if (zoomScale !== null) {
        pageElement.setAttribute('data-canvas-page-zoom-scale', zoomScale);
    }
    const root = fakeElement('div');
    const canvasStack = { root, pages: new Map([['0', { pageElement }]]) };
    return { overlay: createCanvasSearchOverlay({ document: doc, canvasStack }), pageElement, root };
}

// REAL shapes. The display-list textRun commands carry flat geometry AND start/end offsets (this is what the
// engine produces); the layout's own textRects carry NO offsets and nest the geometry under `.rect`
// ({ id, blockId, runId, pageIndex, rect }) - pagination.mjs.
const displayListRender = {
    displayList: {
        commands: [
            { type: 'textRun', blockId: 'p1', pageIndex: 0, start: 0, end: 10, x: 10, y: 20, width: 100, height: 14 },
        ],
    },
    selectionLayout: { textRects: [{ id: 'r1', blockId: 'p1', runId: 'run-1', pageIndex: 0, rect: { x: 10, y: 20, width: 100, height: 14 } }] },
};

const layoutOnlyRender = {
    selectionLayout: { textRects: [{ id: 'r1', blockId: 'p1', runId: 'run-1', pageIndex: 0, start: 0, end: 10, rect: { x: 10, y: 20, width: 100, height: 14 } }] },
};

test('search overlay maps a match onto the display-list text runs (flat geometry + offsets)', () => {
    const { overlay, pageElement, root } = setup();
    overlay.update({ activeIndex: 1, matches: [
        { index: 0, blockId: 'p1', start: 0, end: 4 },
        { index: 1, blockId: 'p1', start: 5, end: 9 },
    ] }, displayListRender);

    const layer = pageElement.children[0];
    assert.equal(layer.children.length, 2);
    assert.equal(root.getAttribute('data-canvas-search-highlight-count'), '2');
    const [plain, active] = layer.children;
    assert.equal(plain.style.left, '10px');
    assert.equal(plain.style.width, '40px');
    assert.equal(active.style.left, '60px');
    assert.equal(active.style.width, '40px');
    assert.equal(plain.className, 'tm-document-canvas-search-highlight');
    assert.equal(active.className, 'tm-document-canvas-search-highlight tm-document-canvas-search-highlight--active');
    assert.equal(active.getAttribute('data-testid'), 'document-canvas-search-active');
});

test('search overlay falls back to the layout rects that nest their geometry under .rect', () => {
    const { overlay, pageElement } = setup();
    overlay.update({ activeIndex: 0, matches: [{ index: 0, blockId: 'p1', start: 0, end: 5 }] }, layoutOnlyRender);

    const [element] = pageElement.children[0].children;
    assert.equal(element.style.left, '10px');
    assert.equal(element.style.top, '20px');
    assert.equal(element.style.width, '50px');
    assert.equal(element.style.height, '14px');
});

test('a layout rect without offsets produces no highlight instead of a wrong one', () => {
    const { overlay, pageElement } = setup();
    overlay.update({ activeIndex: 0, matches: [{ index: 0, blockId: 'p1', start: 0, end: 5 }] }, {
        selectionLayout: { textRects: [{ id: 'r1', blockId: 'p1', pageIndex: 0, rect: { x: 1, y: 2, width: 3, height: 4 } }] },
    });

    assert.equal(pageElement.children.length === 0 || pageElement.children[0].children.length === 0, true);
});

test('geometry is multiplied by the page zoom scale', () => {
    const { overlay, pageElement } = setup('1.5');
    overlay.update({ activeIndex: 0, matches: [{ index: 0, blockId: 'p1', start: 0, end: 5 }] }, displayListRender);

    const [element] = pageElement.children[0].children;
    assert.equal(element.style.left, '15px');
    assert.equal(element.style.top, '30px');
    assert.equal(element.style.width, '75px');
    assert.equal(element.style.height, '21px');
});

test('search overlay sets geometry and classes only - colours come from the stylesheet tokens', () => {
    const { overlay, pageElement } = setup();
    overlay.update({ activeIndex: 0, matches: [{ index: 0, blockId: 'p1', start: 0, end: 4 }] }, displayListRender);

    const [element] = pageElement.children[0].children;
    assert.equal(element.style.background, undefined);
    assert.equal(element.style.outline, undefined);
    assert.equal(element.style.borderRadius, undefined);
});

test('search layer sits above the selection layer (z-index 12)', () => {
    const { overlay, pageElement } = setup();
    overlay.update({ activeIndex: 0, matches: [{ index: 0, blockId: 'p1', start: 0, end: 4 }] }, displayListRender);

    assert.equal(Number(pageElement.children[0].style.zIndex) > 12, true);
});
