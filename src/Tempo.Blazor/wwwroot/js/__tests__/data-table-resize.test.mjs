// data-table.js column resize on the shared pointer-drag helper (FZE-21, cluster-E 2.3).
// The real data-table.js and the real pointer-drag.js run against a hand-rolled DOM: a drag on
// a .tm-col-resize-handle measures the <th> (never the fixed 160 default), reports the live width
// and commits ONCE, while pointercancel, Escape and a mid-drag detach restore the start width and
// commit NOTHING (the bug this migration fixes: pointercancel used to persist the width).
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const dataTableCellUrl = pathToFileURL(path.resolve(here, '../data-table.js')).href;

function bag() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of [...(map.get(type) ?? [])]) fn(event); return event; },
        count(type) { return (map.get(type) ?? []).length; },
        total() { let n = 0; for (const list of map.values()) n += list.length; return n; },
    };
}

let frames = [];
function installFrames() {
    frames = [];
    globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
    globalThis.cancelAnimationFrame = () => {};
}
function flushFrames() {
    const pending = frames;
    frames = [];
    for (const fn of pending) fn();
}

function el(classes = [], parent = null, rect = { left: 0, top: 0, width: 0, height: 0 }, tag = 'div') {
    const set = new Set(classes);
    const node = {
        ...bag(),
        tag: tag.toLowerCase(),
        parent, dataset: {}, style: {}, attrs: new Map(), rect,
        captured: new Set(),
        classList: {
            add: n => set.add(n), remove: n => set.delete(n), contains: n => set.has(n),
        },
        setAttribute(name, value) { node.attrs.set(name, String(value)); },
        removeAttribute(name) { node.attrs.delete(name); },
        getAttribute(name) { return node.attrs.has(name) ? node.attrs.get(name) : null; },
        getBoundingClientRect: () => node.rect,
        setPointerCapture(id) { node.captured.add(id); },
        releasePointerCapture(id) { node.captured.delete(id); },
        contains: () => true,
        closest(selector) {
            for (let n = node; n; n = n.parent) {
                for (const s of selector.split(',').map(s => s.trim())) {
                    if (s.startsWith('.') && n.classList.contains(s.slice(1))) return n;
                    if (s.startsWith('#') && n.attrs.get('id') === s.slice(1)) return n;
                    if (/^[a-z]+$/i.test(s) && n.tag === s.toLowerCase()) return n;
                }
            }
            return null;
        },
    };
    // Pointer events bubble: a captured pointer's events fire on the handle and reach the root.
    const own = node.dispatch;
    node.dispatch = (type, event = {}) => {
        own(type, event);
        (node.parent ?? doc)?.dispatch(type, event);
        return event;
    };
    return node;
}

/** A table whose first header cell carries a resize handle (180px measured width). */
function table({ minWidth = '60', width = 180 } = {}) {
    const root = el(['tm-data-table'], null, { left: 0, top: 0, width: 600, height: 200 }, 'table');
    const th = el(['tm-th'], root, { left: 0, top: 0, width, height: 40 }, 'th');
    const handle = el(['tm-col-resize-handle'], th, { left: width - 8, top: 0, width: 8, height: 40 });
    handle.dataset.colKey = 'Name';
    handle.dataset.minWidth = minWidth;
    // The column measurement looks a handle up by its data-col-key.
    root.querySelector = (selector) => {
        const match = /^\[data-col-key="([^"]+)"\]$/.exec(selector);
        return match && match[1] === handle.dataset.colKey ? handle : null;
    };
    return { root, th, handle };
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
}

let doc;
let tmDataTable;

async function boot() {
    delete globalThis.getComputedStyle;
    installFrames();
    doc = { ...bag(), readyState: 'complete', currentScript: { src: dataTableCellUrl }, contains: () => true };
    globalThis.document = doc;
    globalThis.window = {};
    // A fresh module instance per test: the IIFE attaches to the globals above.
    await import(`../data-table.js?${Math.random()}`);
    tmDataTable = globalThis.window.tmDataTable;
}

test.beforeEach(boot);
test.afterEach(() => {
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
    delete globalThis.getComputedStyle;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...over,
});

// The release flush chains its commit on the previous invokeMethodAsync promise: a microtask tick
// must pass before the commit call is on the record.
const tick = () => new Promise(resolve => setImmediate(resolve));

// ── contract kept ───────────────────────────────────────────────────────────────────────────

test('startColumnResize stays exported for hosts that still call it', () => {
    assert.equal(typeof tmDataTable.startColumnResize, 'function');
    assert.equal(typeof tmDataTable.downloadFile, 'function');
});

// ── the drag ────────────────────────────────────────────────────────────────────────────────

test('a drag measures the th, reports the live width and commits once', async () => {
    const t = table();
    const ref = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 50 }));
    flushFrames();
    assert.deepEqual(ref.calls, [['OnColumnResized', 'Name', 230]], 'the live width is measured from the th, not the 160 default');

    t.handle.dispatch('pointerup', ev(t.handle, { clientX: 222 }));
    await tick();
    assert.deepEqual(ref.calls, [
        ['OnColumnResized', 'Name', 230],
        ['OnColumnResized', 'Name', 230], // the final flush before the single commit
        ['OnColumnResizeCommitted'],
    ], 'the release flushes the last width, then commits exactly once');
});

test('pointercancel reports OnColumnResizeCancelled and never commits', async () => {
    const t = table();
    const ref = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 40 }));
    flushFrames();
    assert.equal(ref.calls.at(-1)[2], 220, 'precondition: the drag moved the width');

    t.handle.dispatch('pointercancel', ev(t.handle, { clientX: 212 }));
    assert.deepEqual(ref.calls.at(-1), ['OnColumnResizeCancelled'],
        'the cancel goes to the snapshot-restoring Blazor path, not a width call');
    assert.equal(ref.calls.filter(c => c[0] === 'OnColumnResizeCommitted').length, 0, 'a cancelled drag commits nothing');
    assert.equal(ref.calls.filter(c => c[0] === 'OnColumnResized').length, 1,
        'the cancel does NOT report the start width through OnColumnResized');
});

test('Escape mid-drag reports OnColumnResizeCancelled and never commits', async () => {
    const t = table();
    const ref = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 - 30 }));
    flushFrames();
    assert.equal(ref.calls.at(-1)[2], 150, 'precondition: the drag moved the width');

    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });
    assert.deepEqual(ref.calls.at(-1), ['OnColumnResizeCancelled'], 'Escape reports the cancelled gesture');
    assert.equal(ref.calls.some(c => c[0] === 'OnColumnResizeCommitted'), false, 'Escape commits nothing');

    // The gesture is over: the pointer that is still down must not drag again.
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 80 }));
    flushFrames();
    t.handle.dispatch('pointerup', ev(t.handle, { clientX: 252 }));
    assert.equal(ref.calls.some(c => c[0] === 'OnColumnResizeCommitted'), false, 'the cancelled session stays dead');
});

test('measureColumn returns the real th width and null for an unknown column', async () => {
    const t = table({ width: 180 });
    assert.equal(tmDataTable.measureColumn(t.root, 'Name'), 180, 'the live <th> width, not the 160 default');
    assert.equal(tmDataTable.measureColumn(t.root, 'Missing'), null);
    assert.equal(tmDataTable.measureColumn(null, 'Name'), null);
});

test('the width clamps to the handle data-min-width', async () => {
    const t = table({ minWidth: '80' });
    const ref = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 - 500 }));
    flushFrames();
    t.handle.dispatch('pointerup', ev(t.handle, { clientX: -328 }));
    await tick();

    assert.equal(ref.calls.filter(c => c[0] === 'OnColumnResized').at(-1)[2], 80);
    assert.equal(ref.calls.at(-1)[0], 'OnColumnResizeCommitted');
});

test('a press outside a handle does nothing', async () => {
    const t = table();
    const ref = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref);

    t.th.dispatch('pointerdown', ev(t.th, { clientX: 10 }));
    t.th.dispatch('pointermove', ev(t.th, { clientX: 200 }));
    t.th.dispatch('pointerup', ev(t.th, { clientX: 200 }));
    flushFrames();

    assert.deepEqual(ref.calls, []);
});

test('detach removes the listeners and cancels a drag in progress', async () => {
    const t = table();
    const ref = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 10 }));
    flushFrames();
    tmDataTable.detachColumnResize(t.root);

    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 100 }));
    t.handle.dispatch('pointerup', ev(t.handle, { clientX: 272 }));
    flushFrames();

    assert.equal(ref.calls.filter(c => c[0] === 'OnColumnResizeCommitted').length, 0, 'detach cancels, never commits');
    assert.deepEqual(ref.calls.filter(c => c[0] === 'OnColumnResizeCancelled').length, 1,
        'the in-flight drag reported the cancelled gesture');
});

test('two tables are independent', async () => {
    const a = table();
    const b = table();
    const refA = dotnet();
    const refB = dotnet();
    await tmDataTable.attachColumnResize(a.root, refA);
    await tmDataTable.attachColumnResize(b.root, refB);

    a.handle.dispatch('pointerdown', ev(a.handle, { clientX: 172 }));
    a.handle.dispatch('pointermove', ev(a.handle, { clientX: 172 + 20 }));
    flushFrames();
    a.handle.dispatch('pointerup', ev(a.handle, { clientX: 192 }));
    await tick();

    assert.equal(refB.calls.length, 0, 'a drag on one table never touches the other');
    assert.equal(refA.calls.at(-1)[0], 'OnColumnResizeCommitted');
});

test('re-attaching the same root replaces the previous attachment', async () => {
    const t = table();
    const ref1 = dotnet();
    const ref2 = dotnet();
    await tmDataTable.attachColumnResize(t.root, ref1);
    await tmDataTable.attachColumnResize(t.root, ref2);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 20 }));
    flushFrames();
    t.handle.dispatch('pointerup', ev(t.handle, { clientX: 192 }));
    await tick();

    assert.equal(ref1.calls.length, 0, 'the replaced attachment is dead');
    assert.equal(ref2.calls.at(-1)[0], 'OnColumnResizeCommitted');
});

// ── RTL ─────────────────────────────────────────────────────────────────────────────────────

test('a right-to-left table inverts the horizontal delta', async () => {
    const t = table();
    const ref = dotnet();
    globalThis.getComputedStyle = () => ({ direction: 'rtl' });
    await tmDataTable.attachColumnResize(t.root, ref);

    t.handle.dispatch('pointerdown', ev(t.handle, { clientX: 172 }));
    t.handle.dispatch('pointermove', ev(t.handle, { clientX: 172 + 50 }));
    flushFrames();

    assert.equal(ref.calls.at(-1)[2], 130, 'moving the pointer right shrinks an RTL column');
    t.handle.dispatch('pointerup', ev(t.handle, { clientX: 222 }));
    await tick();
    assert.equal(ref.calls.at(-1)[0], 'OnColumnResizeCommitted');
});

// ── keyboard: the axis keys must not scroll the table ───────────────────────────────────────

test('arrows, Home and End on a focused handle are prevented, other keys are not', async () => {
    const t = table();
    await tmDataTable.attachColumnResize(t.root, dotnet());

    const prevented = [];
    const key = (k) => {
        const e = { key: k, target: t.handle, preventDefault() { e.defaultPrevented = true; } };
        t.root.dispatch('keydown', e);
        return e.defaultPrevented === true;
    };

    assert.equal(key('ArrowLeft'), true);
    assert.equal(key('ArrowRight'), true);
    assert.equal(key('Home'), true);
    assert.equal(key('End'), true);
    assert.equal(key('ArrowUp'), false, 'the perpendicular arrows keep the browser behaviour');
    assert.equal(key('Tab'), false);
    assert.equal(key('Enter'), false);

    assert.equal(key('ArrowRight') && true, true);
    // A keydown on something that is not a handle is never ours.
    const thKey = { key: 'ArrowRight', target: t.th, preventDefault() { thKey.defaultPrevented = true; } };
    t.root.dispatch('keydown', thKey);
    assert.notEqual(thKey.defaultPrevented, true, 'the <th> keeps its own key handling');
});

test('attachArrowKeyPrevent cancels arrows on the matching child and leaves Tab alone', () => {
    const root = el(['tm-mvl-switcher']);
    const radio = el([], root);
    radio.matches = (selector) => selector === "[role='radio']";
    radio.closest = (selector) => (selector === "[role='radio']" ? radio : null);
    const other = el([], root);
    other.closest = () => null;

    tmDataTable.attachArrowKeyPrevent(root, "[role='radio']");
    tmDataTable.attachArrowKeyPrevent(root, "[role='radio']");

    const arrow = { key: 'ArrowRight', target: radio, preventDefault() { arrow.defaultPrevented = true; } };
    root.dispatch('keydown', arrow);
    assert.equal(arrow.defaultPrevented, true, 'an arrow on a radio must not scroll the page');

    const tab = { key: 'Tab', target: radio, preventDefault() { tab.defaultPrevented = true; } };
    root.dispatch('keydown', tab);
    assert.notEqual(tab.defaultPrevented, true, 'Tab must leave the group');

    const outside = { key: 'ArrowDown', target: other, preventDefault() { outside.defaultPrevented = true; } };
    root.dispatch('keydown', outside);
    assert.notEqual(outside.defaultPrevented, true, 'a non-matching target keeps the browser default');
});

test('detachArrowKeyPrevent stops cancelling arrows and forgets the element', () => {
    const root = el(['tm-mvl-switcher']);
    const radio = el([], root);
    radio.closest = (selector) => (selector === "[role='radio']" ? radio : null);

    tmDataTable.attachArrowKeyPrevent(root, "[role='radio']");
    tmDataTable.detachArrowKeyPrevent(root);

    const arrow = { key: 'ArrowRight', target: radio, preventDefault() { arrow.defaultPrevented = true; } };
    root.dispatch('keydown', arrow);
    assert.notEqual(arrow.defaultPrevented, true, 'after a detach an arrow keeps the browser default');

    // A second detach (or a detach of an unknown element) is a safe no-op.
    tmDataTable.detachArrowKeyPrevent(root);
    tmDataTable.detachArrowKeyPrevent(null);

    // Re-attach works again after a detach.
    tmDataTable.attachArrowKeyPrevent(root, "[role='radio']");
    const arrow2 = { key: 'ArrowLeft', target: radio, preventDefault() { arrow2.defaultPrevented = true; } };
    root.dispatch('keydown', arrow2);
    assert.equal(arrow2.defaultPrevented, true, 'a re-attached listener cancels arrows again');
});
