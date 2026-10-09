// Editor-shell module tests with hand-rolled DOM stubs (no jsdom): the width clamp the resizer
// shares with C#, the pointer-drag resize, and the localStorage persistence that must never throw.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attachResize, canvasWidth, clampWidth, detachResize, measureWidth, loadPanelWidths, savePanelWidths, __resetForTests,
} from '../tm-editor-shell.js';

test.beforeEach(() => {
    __resetForTests();
    delete globalThis.localStorage;
    delete globalThis.document;
});

// ── clampWidth: the same vectors as EditorShellResize.Clamp in C# ───────────────────────────

test('clampWidth stays within min and max', () => {
    assert.equal(clampWidth(100, 280, 200, 480, 1000, 400), 200);
    assert.equal(clampWidth(900, 280, 200, 480, 5000, 400), 480);
    assert.equal(clampWidth(300, 280, 200, 480, 5000, 400), 300);
});

test('clampWidth never takes the canvas below its minimum', () => {
    // 410px canvas, 400px minimum: the panel may grow by at most 10px.
    assert.equal(clampWidth(380, 280, 200, 480, 410, 400), 290);
});

test('clampWidth never grows a panel while the canvas is already too small', () => {
    assert.equal(clampWidth(296, 280, 200, 480, 300, 400), 280, 'no room: growth is refused, not reversed');
    assert.equal(clampWidth(264, 280, 200, 480, 300, 400), 264, 'shrinking is always allowed');
});

test('clampWidth without a measured canvas only applies min and max', () => {
    assert.equal(clampWidth(470, 280, 200, 480, NaN, 400), 470);
    assert.equal(clampWidth(500, 280, 200, 480, undefined, 400), 480);
});

test('clampWidth rounds to whole pixels', () => {
    assert.equal(clampWidth(300.6, 280, 200, 480, 5000, 400), 301);
});

// ── persistence is best-effort ──────────────────────────────────────────────────────────────

test('loadPanelWidths returns null when storage is missing, empty or throws', () => {
    assert.equal(loadPanelWidths('k'), null, 'no localStorage at all (SSR, disabled)');

    globalThis.localStorage = { getItem() { throw new Error('SecurityError'); } };
    assert.equal(loadPanelWidths('k'), null, 'a throwing getItem must not surface');

    globalThis.localStorage = { getItem: () => null };
    assert.equal(loadPanelWidths('k'), null);
});

test('loadPanelWidths and savePanelWidths share one namespaced key', () => {
    const store = new Map();
    globalThis.localStorage = {
        getItem: key => store.get(key) ?? null,
        setItem: (key, value) => store.set(key, value),
        removeItem: key => store.delete(key),
    };

    savePanelWidths('demo', '{"left":{"w":300,"base":"280px"}}');

    assert.deepEqual([...store.keys()], ['tempo.tm-editor-shell.demo']);
    assert.equal(loadPanelWidths('demo'), '{"left":{"w":300,"base":"280px"}}');
});

test('savePanelWidths swallows a throwing setItem and a null payload removes the key', () => {
    globalThis.localStorage = { setItem() { throw new Error('QuotaExceededError'); }, removeItem() { throw new Error('x'); } };
    assert.doesNotThrow(() => savePanelWidths('k', '{}'));
    assert.doesNotThrow(() => savePanelWidths('k', null));

    const removed = [];
    globalThis.localStorage = { removeItem: key => removed.push(key) };
    savePanelWidths('k', null);
    assert.deepEqual(removed, ['tempo.tm-editor-shell.k']);
});

// ── pointer-drag resize ─────────────────────────────────────────────────────────────────────

function listeners() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of map.get(type) ?? []) fn(event); },
        count(type) { return (map.get(type) ?? []).length; },
    };
}

function stage({ aside = 280, canvas = 700 } = {}) {
    const attrs = new Map();
    const handle = {
        ...listeners(),
        captured: null,
        setAttribute: (name, value) => attrs.set(name, value),
        getAttribute: name => attrs.get(name) ?? null,
        classList: { add: name => attrs.set(`class:${name}`, true), remove: name => attrs.delete(`class:${name}`) },
        setPointerCapture(id) { handle.captured = id; },
        releasePointerCapture() { handle.captured = null; },
    };
    const styles = new Map();
    const asideEl = {
        style: { setProperty: (name, value) => styles.set(name, value), removeProperty: name => styles.delete(name) },
        getBoundingClientRect: () => ({ width: aside }),
    };
    const canvasEl = { clientWidth: canvas };
    const main = { querySelector: selector => (selector.includes('canvas') ? canvasEl : null) };
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    return { handle, asideEl, main, styles, attrs, calls, dotnet, canvasEl };
}

const down = (x, id = 7) => ({ button: 0, clientX: x, pointerId: id, preventDefault() { this.prevented = true; } });
const move = (x, id = 7) => ({ clientX: x, pointerId: id });

test('canvasWidth reads the canvas region of the main element', () => {
    const { main } = stage({ canvas: 612 });
    assert.equal(canvasWidth(main), 612);
    assert.ok(Number.isNaN(canvasWidth({ querySelector: () => null })), 'no canvas: unknown, not zero');
    assert.ok(Number.isNaN(canvasWidth(null)));
});

test('dragging the left separator right grows the panel live and commits the clamped width once', () => {
    const s = stage({ aside: 280, canvas: 700 });
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    const pointerdown = down(300);
    s.handle.dispatch('pointerdown', pointerdown);
    assert.equal(pointerdown.prevented, true, 'a drag must not start a text selection');
    assert.equal(s.handle.captured, 7, 'pointer capture keeps the drag alive outside the 6px handle');

    s.handle.dispatch('pointermove', move(340));
    assert.equal(s.styles.get('--tm-editor-shell-panel-width'), '320px');
    assert.equal(s.attrs.get('aria-valuenow'), '320');
    assert.equal(s.calls.length, 0, 'no round trip while dragging');

    s.handle.dispatch('pointerup', move(340));
    assert.deepEqual(s.calls, [['OnResizeCommitted', 'left', 320]]);
    assert.equal(s.handle.captured, null);
});

test('dragging the right separator LEFT grows the right panel', () => {
    const s = stage({ aside: 320, canvas: 700 });
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'right', { side: 'right', min: 240, max: 560, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(900));
    s.handle.dispatch('pointermove', move(860));
    s.handle.dispatch('pointerup', move(860));

    assert.deepEqual(s.calls, [['OnResizeCommitted', 'right', 360]]);
});

test('a drag is clamped to min, max and the minimum canvas', () => {
    const s = stage({ aside: 280, canvas: 450 });
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(0));
    s.handle.dispatch('pointermove', move(300));
    assert.equal(s.styles.get('--tm-editor-shell-panel-width'), '330px', '450 - 400 = 50px of room');
    s.handle.dispatch('pointermove', move(-500));
    assert.equal(s.styles.get('--tm-editor-shell-panel-width'), '200px', 'never below the minimum width');
    s.handle.dispatch('pointerup', move(-500));
    assert.deepEqual(s.calls, [['OnResizeCommitted', 'left', 200]]);
});

test('Escape cancels the drag and restores the start width without committing', () => {
    const s = stage({ aside: 280 });
    const doc = listeners();
    globalThis.document = doc;
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(300));
    s.handle.dispatch('pointermove', move(360));
    doc.dispatch('keydown', { key: 'Escape' });
    s.handle.dispatch('pointerup', move(360));

    assert.equal(s.styles.get('--tm-editor-shell-panel-width'), '280px');
    assert.equal(s.attrs.get('aria-valuenow'), '280');
    assert.equal(s.calls.length, 0);
});

test('pointercancel and a click without movement never commit', () => {
    const s = stage();
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(300));
    s.handle.dispatch('pointerup', move(300));
    s.handle.dispatch('pointerdown', down(300));
    s.handle.dispatch('pointermove', move(340));
    s.handle.dispatch('pointercancel', move(340));

    assert.equal(s.calls.length, 0);
});

test('a secondary button does not start a drag, and another pointer is ignored', () => {
    const s = stage();
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', { ...down(300), button: 2 });
    s.handle.dispatch('pointermove', move(340));
    s.handle.dispatch('pointerup', move(340));
    assert.equal(s.calls.length, 0);

    s.handle.dispatch('pointerdown', down(300, 1));
    s.handle.dispatch('pointermove', move(340, 2));
    s.handle.dispatch('pointerup', move(340, 2));
    assert.equal(s.calls.length, 0, 'a second finger cannot move or release the first one\'s drag');
});

test('keys the resizer handles are prevented from scrolling the page; others are left alone', () => {
    const s = stage();
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) {
        const event = { key, preventDefault() { this.prevented = true; } };
        s.handle.dispatch('keydown', event);
        assert.equal(event.prevented, true, `${key} must not scroll the page`);
    }
    const tab = { key: 'Tab', preventDefault() { this.prevented = true; } };
    s.handle.dispatch('keydown', tab);
    assert.equal(tab.prevented, undefined, 'Tab keeps moving focus');
});

test('detachResize removes every listener; re-attaching the same id replaces the first', () => {
    const s = stage();
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });
    assert.equal(s.handle.count('pointerdown'), 1, 'a second attach replaces the first');

    detachResize('left');
    assert.equal(s.handle.count('pointerdown'), 0);
    assert.equal(s.handle.count('keydown'), 0);
    assert.doesNotThrow(() => detachResize('left'), 'detach twice is a no-op');
});

test('attachResize with a missing handle or panel is a no-op', () => {
    const s = stage();
    assert.doesNotThrow(() => attachResize(null, s.asideEl, s.main, s.dotnet, 'x', { side: 'left', min: 1, max: 2, minCanvas: 0 }));
    assert.doesNotThrow(() => attachResize(s.handle, null, s.main, s.dotnet, 'x', { side: 'left', min: 1, max: 2, minCanvas: 0 }));
});

test('a dotnet reference that rejects does not surface an unhandled rejection', async () => {
    const s = stage();
    s.dotnet.invokeMethodAsync = () => Promise.reject(new Error('circuit gone'));
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(300));
    s.handle.dispatch('pointermove', move(340));
    s.handle.dispatch('pointerup', move(340));
    await new Promise(resolve => setTimeout(resolve, 0));
});

// ── F6 r2 G7: pointer-capture release and mid-drag detach ───────────────────────────────────

test('the drag releases the pointer capture of ITS pointer id (the argument is required)', () => {
    const s = stage();
    const released = [];
    s.handle.releasePointerCapture = (id) => { released.push(id); s.handle.captured = null; };
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(300, 11));
    s.handle.dispatch('pointermove', move(340, 11));
    s.handle.dispatch('pointerup', move(340, 11));

    assert.deepEqual(released, [11], 'releasePointerCapture(pointerId) - without the id the real DOM throws and the capture leaks');
});

test('detaching mid-drag restores the live width and releases the capture', () => {
    const s = stage({ aside: 280 });
    const released = [];
    s.handle.releasePointerCapture = (id) => { released.push(id); };
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(300, 5));
    s.handle.dispatch('pointermove', move(380, 5));
    assert.equal(s.styles.get('--tm-editor-shell-panel-width'), '360px');

    detachResize('left');

    assert.equal(s.styles.get('--tm-editor-shell-panel-width'), '280px', 'a panel that collapses mid-drag must not keep the dragged width');
    assert.deepEqual(released, [5]);
    assert.equal(s.calls.length, 0, 'a detached drag commits nothing');
    assert.equal(s.attrs.get('aria-valuenow'), '280');
});

test('detaching with no drag in progress touches nothing', () => {
    const s = stage({ aside: 280 });
    attachResize(s.handle, s.asideEl, s.main, s.dotnet, 'left', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    detachResize('left');

    assert.equal(s.styles.size, 0);
});

test('measureWidth reads the rendered width of an element, -1 when it cannot be measured', () => {
    assert.equal(measureWidth({ getBoundingClientRect: () => ({ width: 319.6 }) }), 320);
    assert.equal(measureWidth(null), -1);
    assert.equal(measureWidth({}), -1);
});