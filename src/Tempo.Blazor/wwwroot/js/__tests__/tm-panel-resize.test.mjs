// Shared panel-resize helper tests with hand-rolled DOM stubs (no jsdom): the width clamp the C#
// side shares, the pointer-drag resize of ONE separator, the localStorage persistence that must
// never throw, and the two hosts' differences (the CSS width variable, the drag class, the region
// selector and the DotNet callback name).
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attachResize, clampWidth, detachResize, loadStored, measureRegion, measureWidth, regionWidth,
    resetPanelResizes, saveStored,
} from '../tm-panel-resize.js';

test.beforeEach(() => {
    resetPanelResizes();
    delete globalThis.localStorage;
    delete globalThis.document;
});

// ── clampWidth: the same vectors as PanelResize.Clamp in C# ─────────────────────────────────

test('clampWidth stays within min and max', () => {
    assert.equal(clampWidth(100, 280, 200, 480, 1000, 400), 200);
    assert.equal(clampWidth(900, 280, 200, 480, 5000, 400), 480);
    assert.equal(clampWidth(300, 280, 200, 480, 5000, 400), 300);
});

test('clampWidth never takes the canvas below its minimum', () => {
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
    assert.equal(clampWidth(299.4, 280, 200, 480, 5000, 400), 299);
});

// ── region measurement ──────────────────────────────────────────────────────────────────────

test('regionWidth measures the region itself when no selector is given', () => {
    assert.equal(regionWidth({ clientWidth: 612 }), 612);
    assert.ok(Number.isNaN(regionWidth({})), 'no clientWidth: unknown, not zero');
    assert.ok(Number.isNaN(regionWidth(null)));
});

test('regionWidth measures the element the selector resolves inside the region', () => {
    const canvas = { clientWidth: 500 };
    const main = { clientWidth: 900, querySelector: selector => (selector === '[data-region="canvas"]' ? canvas : null) };

    assert.equal(regionWidth(main, '[data-region="canvas"]'), 500);
    assert.ok(Number.isNaN(regionWidth(main, '[data-region="missing"]')), 'the selector wins over the region itself');
});

test('measureRegion serialises an unknown width as -1', () => {
    assert.equal(measureRegion({ clientWidth: 480 }), 480);
    assert.equal(measureRegion(null), -1);
    assert.equal(measureRegion({}), -1);
});

test('measureWidth reads the rendered width of an element, -1 when it cannot be measured', () => {
    assert.equal(measureWidth({ getBoundingClientRect: () => ({ width: 319.6 }) }), 320);
    assert.equal(measureWidth(null), -1);
    assert.equal(measureWidth({}), -1);
});

// ── persistence is best-effort ─────────────────────────────────────────────────────────────

test('loadStored returns null when storage is missing, empty or throws', () => {
    assert.equal(loadStored('tempo.tm-app-shell.', 'k'), null, 'no localStorage at all (SSR, disabled)');

    globalThis.localStorage = { getItem() { throw new Error('SecurityError'); } };
    assert.equal(loadStored('tempo.tm-app-shell.', 'k'), null, 'a throwing getItem must not surface');

    globalThis.localStorage = { getItem: () => null };
    assert.equal(loadStored('tempo.tm-app-shell.', 'k'), null);
});

test('loadStored and saveStored share one namespaced key per host prefix', () => {
    const store = new Map();
    globalThis.localStorage = {
        getItem: key => store.get(key) ?? null,
        setItem: (key, value) => store.set(key, value),
        removeItem: key => store.delete(key),
    };

    saveStored('tempo.tm-app-shell.', 'demo', '{"collapsed":true}');

    assert.deepEqual([...store.keys()], ['tempo.tm-app-shell.demo']);
    assert.equal(loadStored('tempo.tm-app-shell.', 'demo'), '{"collapsed":true}');
    assert.equal(loadStored('tempo.tm-editor-shell.', 'demo'), null, 'a prefix never reads another host\'s entry');
});

test('saveStored swallows a throwing setItem and a null payload removes the key', () => {
    globalThis.localStorage = { setItem() { throw new Error('QuotaExceededError'); }, removeItem() { throw new Error('x'); } };
    assert.doesNotThrow(() => saveStored('p.', 'k', '{}'));
    assert.doesNotThrow(() => saveStored('p.', 'k', null));

    const removed = [];
    globalThis.localStorage = { removeItem: key => removed.push(key) };
    saveStored('p.', 'k', null);
    assert.deepEqual(removed, ['p.k']);
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

function stage({ panel = 420, region = 900 } = {}) {
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
    const panelEl = {
        style: { setProperty: (name, value) => styles.set(name, value), removeProperty: name => styles.delete(name) },
        getBoundingClientRect: () => ({ width: panel }),
    };
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    return { handle, panelEl, region: { clientWidth: region }, styles, attrs, calls, dotnet };
}

const OPTIONS = Object.freeze({
    side: 'right', min: 280, max: 640, minCanvas: 480,
    widthVar: '--tm-app-shell-work-area-width',
    dragClass: 'tm-app-shell__resizer--dragging',
    method: 'OnWorkAreaResizeCommitted',
});

const down = (x, id = 7) => ({ button: 0, clientX: x, pointerId: id, preventDefault() { this.prevented = true; } });
const move = (x, id = 7) => ({ clientX: x, pointerId: id });

test('a right panel grows when its separator (on the panel\'s left edge) is dragged left', () => {
    const s = stage({ panel: 420, region: 900 });
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    const pointerdown = down(900);
    s.handle.dispatch('pointerdown', pointerdown);
    assert.equal(pointerdown.prevented, true, 'a drag must not start a text selection');
    assert.equal(s.handle.captured, 7, 'pointer capture keeps the drag alive outside the handle');

    s.handle.dispatch('pointermove', move(860));
    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '460px');
    assert.equal(s.attrs.get('aria-valuenow'), '460');
    assert.equal(s.calls.length, 0, 'no round trip while dragging');

    s.handle.dispatch('pointerup', move(860));
    assert.deepEqual(s.calls, [['OnWorkAreaResizeCommitted', 'right', 460]]);
    assert.equal(s.handle.captured, null);
});

test('a left panel grows when its separator is dragged right', () => {
    const s = stage({ panel: 280, region: 900 });
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'left', {
        ...OPTIONS, side: 'left', method: 'OnResizeCommitted',
    });

    s.handle.dispatch('pointerdown', down(100));
    s.handle.dispatch('pointermove', move(160));
    s.handle.dispatch('pointerup', move(160));

    assert.deepEqual(s.calls, [['OnResizeCommitted', 'left', 340]]);
});

test('a drag is clamped to min, max and the minimum region width', () => {
    const s = stage({ panel: 420, region: 700 });
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    s.handle.dispatch('pointerdown', down(900));
    s.handle.dispatch('pointermove', move(0));
    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '640px', 'never beyond the maximum');
    s.handle.dispatch('pointermove', move(1400));
    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '280px', 'never below the minimum');
    s.handle.dispatch('pointerup', move(1400));
    assert.deepEqual(s.calls, [['OnWorkAreaResizeCommitted', 'right', 280]]);
});

test('the region selector decides what is measured for the canvas rule', () => {
    const canvas = { clientWidth: 400 };
    const main = { clientWidth: 2000, querySelector: selector => (selector === '[data-region="canvas"]' ? canvas : null) };
    const s = stage({ panel: 280 });
    attachResize(s.handle, s.panelEl, main, s.dotnet, 'x', {
        ...OPTIONS, side: 'left', minCanvas: 400, regionSelector: '[data-region="canvas"]',
    });

    s.handle.dispatch('pointerdown', down(0));
    s.handle.dispatch('pointermove', move(300));

    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '280px',
        'the canvas is at its minimum: growth is refused even though the shell has room');
});

test('Escape cancels the drag and restores the start width without committing', () => {
    const s = stage({ panel: 420 });
    const doc = listeners();
    globalThis.document = doc;
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    s.handle.dispatch('pointerdown', down(900));
    s.handle.dispatch('pointermove', move(800));
    doc.dispatch('keydown', { key: 'Escape' });
    s.handle.dispatch('pointerup', move(800));

    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '420px');
    assert.equal(s.attrs.get('aria-valuenow'), '420');
    assert.equal(s.calls.length, 0);
});

test('pointercancel and a click without movement never commit', () => {
    const s = stage();
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    s.handle.dispatch('pointerdown', down(900));
    s.handle.dispatch('pointerup', move(900));
    s.handle.dispatch('pointerdown', down(900));
    s.handle.dispatch('pointermove', move(800));
    s.handle.dispatch('pointercancel', move(800));

    assert.equal(s.calls.length, 0);
});

test('a secondary button does not start a drag, and another pointer is ignored', () => {
    const s = stage();
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    s.handle.dispatch('pointerdown', { ...down(900), button: 2 });
    s.handle.dispatch('pointermove', move(800));
    s.handle.dispatch('pointerup', move(800));
    assert.equal(s.calls.length, 0);

    s.handle.dispatch('pointerdown', down(900, 1));
    s.handle.dispatch('pointermove', move(800, 2));
    s.handle.dispatch('pointerup', move(800, 2));
    assert.equal(s.calls.length, 0, 'a second finger cannot move or release the first one\'s drag');
});

test('keys the separator handles are prevented from scrolling the page; others are left alone', () => {
    const s = stage();
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

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
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);
    assert.equal(s.handle.count('pointerdown'), 1, 'a second attach replaces the first');

    detachResize('wa');
    assert.equal(s.handle.count('pointerdown'), 0);
    assert.equal(s.handle.count('keydown'), 0);
    assert.doesNotThrow(() => detachResize('wa'), 'detach twice is a no-op');
});

test('attachResize with a missing handle or panel is a no-op', () => {
    const s = stage();
    assert.doesNotThrow(() => attachResize(null, s.panelEl, s.region, s.dotnet, 'x', OPTIONS));
    assert.doesNotThrow(() => attachResize(s.handle, null, s.region, s.dotnet, 'x', OPTIONS));
});

test('the default width variable and drag class are the helper\'s own', () => {
    const s = stage({ panel: 280 });
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'plain', { side: 'left', min: 200, max: 480, minCanvas: 400 });

    s.handle.dispatch('pointerdown', down(0));
    s.handle.dispatch('pointermove', move(20));

    assert.equal(s.styles.get('--tm-panel-width'), '300px');
    assert.equal(s.attrs.get('class:tm-panel-resize__handle--dragging'), true);
    s.handle.dispatch('pointerup', move(20));
    assert.deepEqual(s.calls, [['OnResizeCommitted', 'left', 300]], 'the default DotNet callback name');
});

test('a dotnet reference that rejects does not surface an unhandled rejection', async () => {
    const s = stage();
    s.dotnet.invokeMethodAsync = () => Promise.reject(new Error('circuit gone'));
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    s.handle.dispatch('pointerdown', down(900));
    s.handle.dispatch('pointermove', move(800));
    s.handle.dispatch('pointerup', move(800));
    await new Promise(resolve => setTimeout(resolve, 0));
});

test('detaching mid-drag restores the live width and releases the capture', () => {
    const s = stage({ panel: 420 });
    const released = [];
    s.handle.releasePointerCapture = (id) => { released.push(id); };
    attachResize(s.handle, s.panelEl, s.region, s.dotnet, 'wa', OPTIONS);

    s.handle.dispatch('pointerdown', down(900, 5));
    s.handle.dispatch('pointermove', move(800, 5));
    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '520px');

    detachResize('wa');

    assert.equal(s.styles.get('--tm-app-shell-work-area-width'), '420px', 'a panel that closes mid-drag must not keep the dragged width');
    assert.deepEqual(released, [5]);
    assert.equal(s.calls.length, 0, 'a detached drag commits nothing');
    assert.equal(s.attrs.get('aria-valuenow'), '420');
});

test('two hosts on one page keep independent registrations', () => {
    const a = stage();
    const b = stage({ panel: 300 });
    attachResize(a.handle, a.panelEl, a.region, a.dotnet, 'shell-a', OPTIONS);
    attachResize(b.handle, b.panelEl, b.region, b.dotnet, 'shell-b', OPTIONS);

    a.handle.dispatch('pointerup', move(0));
    a.handle.dispatch('pointerdown', down(900));
    a.handle.dispatch('pointermove', move(880));
    a.handle.dispatch('pointerup', move(880));

    assert.deepEqual(a.calls, [['OnWorkAreaResizeCommitted', 'right', 440]]);
    assert.deepEqual(b.calls, [], 'the second host\'s separator was never dragged');

    resetPanelResizes();
    assert.equal(a.handle.count('pointerdown'), 0);
    assert.equal(b.handle.count('pointerdown'), 0);
});
