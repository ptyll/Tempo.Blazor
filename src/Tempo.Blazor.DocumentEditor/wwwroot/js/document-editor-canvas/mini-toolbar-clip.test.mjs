import assert from 'node:assert/strict';
import { afterEach, beforeEach, test } from 'node:test';
import { buildMiniToolbarPayload, miniToolbarClipRect } from './entry.mjs';

// Phase 13 (carry-forward of phase 12): the floating mini / object toolbar is fixed to the viewport, so a bounded frame's doc-region scroller must
// clip it. A selection scrolled out of that scroller reports isVisible:false ('canvas-selection-scrolled-out'); a partly visible one is clamped
// into the clip rect instead of floating over the ribbon.

const saved = { innerWidth: globalThis.innerWidth, innerHeight: globalThis.innerHeight };

beforeEach(() => {
    globalThis.innerWidth = 1200;
    globalThis.innerHeight = 800;
});

afterEach(() => {
    globalThis.innerWidth = saved.innerWidth;
    globalThis.innerHeight = saved.innerHeight;
});

const stack = (pageTop, pageLeft = 100) => ({
    pages: new Map([['0', {
        pageElement: {
            getBoundingClientRect: () => ({ left: pageLeft, top: pageTop, width: 600, height: 800 }),
            getAttribute: () => null,
        },
    }]]),
});

const range = (y = 100, height = 18) => ({
    anchor: { blockId: 'p1', offset: 0 },
    focus: { blockId: 'p1', offset: 5 },
    isCollapsed: false,
    pageIndex: 0,
    boundingRect: { pageIndex: 0, x: 40, y, width: 120, height },
});

const objectState = (y = 100, source = 'pointer') => ({
    anchor: { blockId: 'p1', offset: 0 },
    focus: { blockId: 'p1', offset: 0 },
    isCollapsed: true,
    pageIndex: 0,
    objectSelectionSource: source,
    object: { objectId: 'img1', blockId: 'p1', rect: { x: 40, y, width: 200, height: 120 } },
});

const clip = { left: 50, top: 200, right: 750, bottom: 600 };

test('a range inside the clip rect stays visible', () => {
    const payload = buildMiniToolbarPayload(stack(150), range(200), clip);
    assert.equal(payload.isVisible, true);
    assert.equal(payload.reason, 'canvas-selection-range');
    assert.ok(payload.top >= clip.top + 8 && payload.top + payload.height <= clip.bottom - 8);
    assert.ok(payload.left >= clip.left + 8 && payload.left + payload.width <= clip.right - 8);
});

test('a range scrolled above the clip rect is hidden as scrolled out', () => {
    const payload = buildMiniToolbarPayload(stack(-400), range(100), clip);
    assert.equal(payload.isVisible, false);
    assert.equal(payload.reason, 'canvas-selection-scrolled-out');
    assert.equal(payload.selection.isCollapsed, false, 'the selection snapshot still rides along');
});

test('a range scrolled below the clip rect is hidden as scrolled out', () => {
    const payload = buildMiniToolbarPayload(stack(700), range(100), clip);
    assert.equal(payload.isVisible, false);
    assert.equal(payload.reason, 'canvas-selection-scrolled-out');
});

test('a range with no room above it (it touches the clip top) puts the toolbar BELOW the selection instead of covering it', () => {
    // selection top = 190 + 20 = 210, bottom 228 -> above it the toolbar would sit at 156, outside the clip (top 200).
    const payload = buildMiniToolbarPayload(stack(190), range(20), clip);
    assert.equal(payload.isVisible, true);
    assert.equal(payload.top, 228 + 10, 'under the selection, never over it');
    assert.ok(payload.top >= clip.top + 8);
});

test('a range with no room above AND below stays inside the clip rect', () => {
    // selection spans nearly the whole clip: the toolbar is clamped into the rect.
    const payload = buildMiniToolbarPayload(stack(190), range(20, 400), clip);
    assert.equal(payload.isVisible, true);
    assert.ok(payload.top >= clip.top + 8 && payload.top + payload.height <= clip.bottom - 8);
});

test('the toolbar is clamped to the clip rect horizontally', () => {
    const payload = buildMiniToolbarPayload(stack(150, -100), range(200), clip);
    assert.equal(payload.isVisible, true);
    assert.equal(payload.left, clip.left + 8);
});

test('without a marked scroller the window is the clip: a selection below the fold is hidden', () => {
    const payload = buildMiniToolbarPayload(stack(5000), range(100), null);
    assert.equal(payload.isVisible, false);
    assert.equal(payload.reason, 'canvas-selection-scrolled-out');
});

test('without a marked scroller a visible selection keeps today\'s payload', () => {
    const payload = buildMiniToolbarPayload(stack(150), range(200), null);
    assert.equal(payload.isVisible, true);
    assert.equal(payload.reason, 'canvas-selection-range');
    assert.equal(payload.viewportWidth, 1200);
    assert.equal(payload.viewportHeight, 800);
});

test('an object selected inside the clip keeps the object reason and its source', () => {
    const payload = buildMiniToolbarPayload(stack(150), objectState(100, 'keyboard'), clip);
    assert.equal(payload.isVisible, true);
    assert.equal(payload.reason, 'canvas-object-selection');
    assert.equal(payload.objectSelectionSource, 'keyboard');
});

test('the object panel prefers below only when the CLIP rect has the room, not the window', () => {
    // object bottom = 150 + 100 + 120 = 370; room in the clip below = 600 - 370 = 230 (< 280) although the window has 430.
    const bounded = buildMiniToolbarPayload(stack(150), objectState(100), clip);
    assert.equal(bounded.placement, 'above');
    const flowing = buildMiniToolbarPayload(stack(150), objectState(100), null);
    assert.equal(flowing.placement, 'below');
});

test('an object scrolled out of the clip is hidden but keeps its source and the object selection', () => {
    const payload = buildMiniToolbarPayload(stack(-600), objectState(100, 'pointer'), clip);
    assert.equal(payload.isVisible, false);
    assert.equal(payload.reason, 'canvas-selection-scrolled-out');
    assert.equal(payload.objectSelectionSource, 'pointer');
    assert.equal(payload.selection.objectSelection.objectId, 'img1');
});

test('miniToolbarClipRect is the marked scroller\'s rect, or null without one', () => {
    const scroller = {
        getAttribute: name => (name === 'data-scroll-viewport' ? 'true' : null),
        getBoundingClientRect: () => ({ left: 10, top: 60, right: 710, bottom: 560, width: 700, height: 500 }),
        contains: () => true,
        isConnected: true,
        parentElement: null,
    };
    const host = { parentElement: scroller };
    assert.deepEqual(miniToolbarClipRect(host), { left: 10, top: 60, right: 710, bottom: 560 });
    assert.equal(miniToolbarClipRect({ parentElement: null }), null);
});

// A scroll frame that extends the progressive first layout takes the early-return path: the toolbar clip is still re-evaluated there.
import { CanvasDocumentEngine } from './entry.mjs';

test('handleScroll re-evaluates the toolbar clip on the progressive-layout path too', () => {
    const calls = [];
    const fake = {
        scrollRenderPending: false,
        mounted: true,
        document: { defaultView: { requestAnimationFrame: callback => callback() } },
        performanceMetrics: { recordScrollFrame() {} },
        engineOptions: { progressiveFirstLayout: true },
        progressiveLayout: { complete: false, laidPages: 1 },
        progressivePagesForViewport: () => 5,
        render: options => calls.push(['render', options.progressiveTargetPages]),
        repaint: () => calls.push(['repaint']),
        refreshMiniToolbarClip: () => calls.push(['refresh']),
    };

    CanvasDocumentEngine.prototype.handleScroll.call(fake);

    assert.deepEqual(calls.map(call => call[0]), ['render', 'refresh']);
});

test('handleScroll re-evaluates the toolbar clip after a plain repaint', () => {
    const calls = [];
    const fake = {
        scrollRenderPending: false,
        mounted: true,
        document: { defaultView: { requestAnimationFrame: callback => callback() } },
        performanceMetrics: { recordScrollFrame() {} },
        engineOptions: { progressiveFirstLayout: false },
        progressiveLayout: { complete: true },
        render: () => calls.push(['render']),
        repaint: () => calls.push(['repaint']),
        refreshMiniToolbarClip: () => calls.push(['refresh']),
    };

    CanvasDocumentEngine.prototype.handleScroll.call(fake);

    assert.deepEqual(calls.map(call => call[0]), ['repaint', 'refresh']);
});
