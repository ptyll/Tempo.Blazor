// dashboard.js widget move + resize on the shared pointer-drag helper (dashboard redesign, F5).
// The real dashboard.js and the real pointer-drag.js run against a hand-rolled DOM: a mouse and a
// finger move a widget through Pointer Events (HTML5 drag-and-drop and mouse events are gone),
// the grid metrics come from the grid's CSS custom properties, the resize clamps through the
// widget's data-min-*/data-max-* attributes, and every cancel path (Escape, pointercancel,
// destroy) commits nothing.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const helperUrl = pathToFileURL(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../pointer-drag.js')).href;

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

/** A Map with the CSSStyleDeclaration surface the code reads (getPropertyValue). */
class StyleMap extends Map {
    getPropertyValue(name) { return this.get(name); }
    setProperty(name, value) { this.set(name, value); }
}

function el(classes = [], parent = null, rect = { left: 0, top: 0, width: 0, height: 0 }) {
    const set = new Set(classes);
    const node = {
        ...bag(),
        parent, dataset: {}, style: new StyleMap(), attrs: new Map(), rect,
        captured: new Set(), released: [], children: [],
        classList: {
            add: n => set.add(n), remove: n => set.delete(n), contains: n => set.has(n),
        },
        setAttribute(name, value) { node.attrs.set(name, String(value)); },
        removeAttribute(name) { node.attrs.delete(name); },
        appendChild(child) { node.children.push(child); child.parent = node; },
        remove() {
            const p = node.parent;
            if (p) p.children = p.children.filter(c => c !== node);
        },
        getBoundingClientRect: () => node.rect,
        contains(target) { for (let n = target; n; n = n.parent) { if (n === node) return true; } return false; },
        closest(selector) {
            for (let n = node; n; n = n.parent) {
                const wanted = selector.split(',').map(s => s.trim());
                if (wanted.some(s => s.startsWith('.') && n.classList.contains(s.slice(1)))) return n;
            }
            return null;
        },
    };
    // Pointer events bubble: a captured pointer's events fire on the handle and reach the grid.
    const own = node.dispatch;
    node.dispatch = (type, event = {}) => {
        own(type, event);
        (node.parent ?? doc)?.dispatch(type, event);
        return event;
    };
    return node;
}

/** A 1200x676 desktop grid: 12 columns of 64px + 16px gap, rows of 60px + 16px gap. */
function board() {
    const root = el(['tm-dashboard']);
    const grid = el(['tm-dashboard-grid'], root, { left: 0, top: 0, width: 1200, height: 676 });
    root.querySelector = (selector) => selector === '.tm-dashboard-grid' ? grid : null;
    grid.querySelector = (selector) =>
        grid.children.find(c => c.classList.contains(selector.replace(/^\./, '').split(/[\s.]+/)[0])) ?? null;
    const widget = el(['tm-widget'], grid, { left: 0, top: 0, width: 296, height: 136 });
    widget.dataset.instanceId = 'a';
    // the min/max the component renders from the WidgetDefinition (grid cells; 0 = unlimited)
    widget.dataset.minW = '2'; widget.dataset.maxW = '6';
    widget.dataset.minH = '2'; widget.dataset.maxH = '5';
    widget.style.set('--tm-w-span', '4');
    widget.style.set('--tm-w-rows', '2');
    const dragHandle = el(['tm-widget-drag-handle'], widget);
    const seHandle = el(['tm-widget-resize-handle', 'tm-widget-resize-se'], widget);
    return { root, grid, widget, dragHandle, seHandle };
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
}

let doc;
let dashboard;

async function boot() {
    doc = { ...bag(), readyState: 'complete', currentScript: { src: helperUrl.replace('pointer-drag.js', 'dashboard.js') }, contains: () => true, createElement: () => el(['tm-widget-drop-preview']) };
    globalThis.document = doc;
    globalThis.window = { tempoDashboard: undefined };
    globalThis.matchMedia = undefined;
    // The grid publishes its metrics as custom properties on the grid root.
    globalThis.getComputedStyle = () => ({
        getPropertyValue(name) {
            if (name === '--grid-columns') return '12';
            if (name === '--row-height') return '60';
            if (name === '--grid-gap') return '16';
            return '';
        },
    });
    // A fresh module instance per test: the IIFE attaches to the globals above.
    await import(`../dashboard.js?${Math.random()}`);
    dashboard = globalThis.window.tempoDashboard;
}

test.beforeEach(boot);
test.afterEach(() => {
    dashboard?.destroy?.({ id: 'board' });
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.getComputedStyle;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...over,
});

// ── contract kept ───────────────────────────────────────────────────────────────────────────

test('window.tempoDashboard keeps init, destroy and scrollWidgetIntoView', async () => {
    const b = board();
    const id = dashboard.init(b.root, dotnet());
    await dashboard.dashboards.get(id).ready;
    for (const name of ['init', 'destroy', 'scrollWidgetIntoView']) {
        assert.equal(typeof dashboard[name], 'function', name);
    }
});

test('no HTML5 drag-and-drop or mouse listener is registered any more - the gestures are Pointer Events on the grid', async () => {
    const b = board();
    const id = dashboard.init(b.root, dotnet());
    await dashboard.dashboards.get(id).ready;

    assert.equal(b.grid.count('dragstart'), 0, 'HTML5 DnD is gone');
    assert.equal(b.grid.count('dragover'), 0);
    assert.equal(b.grid.count('drop'), 0);
    assert.equal(b.grid.count('dragend'), 0);
    assert.equal(b.grid.count('mousedown'), 0, 'mouse events are gone');
    assert.equal(doc.count('mousemove'), 0);
    assert.equal(doc.count('mouseup'), 0);
    assert.equal(b.grid.count('pointerdown'), 2, 'two delegated pointer attachments: move + resize');
});

// ── cell maths ──────────────────────────────────────────────────────────────────────────────

test('cellFromPointer snaps the pointer to a cell, using the metrics read from the CSS custom properties', () => {
    const metrics = { columns: 12, rowHeight: 60, gap: 16 };
    const rect = { left: 0, top: 0, width: 1200, height: 676 };
    // cell width = (1200 - 11*16)/12 = 85.33; step = 101.33; row step = 76
    assert.deepEqual(dashboard.cellFromPointer(rect, 40, 20, metrics), { col: 0, row: 0 });
    assert.deepEqual(dashboard.cellFromPointer(rect, 110, 70, metrics), { col: 1, row: 1 });
    assert.deepEqual(dashboard.cellFromPointer(rect, 1000, 300, metrics), { col: 10, row: 4 });
    assert.deepEqual(dashboard.cellFromPointer(rect, -50, -50, metrics), { col: 0, row: 0 }, 'never left of the grid');
    assert.deepEqual(dashboard.cellFromPointer(rect, 5000, 0, metrics), { col: 11, row: 0 }, 'never right of the grid');
});

test('readGridMetrics falls back to the previous values when the properties are missing', async () => {
    const b = board();
    const id = dashboard.init(b.root, dotnet());
    const state = dashboard.dashboards.get(id);
    await state.ready;
    assert.deepEqual(
        { c: state.gridColumns, r: state.rowHeight, g: state.gap },
        { c: 12, r: 60, g: 16 },
        'the computed-style stub published 12/60/16');
    globalThis.getComputedStyle = () => ({ getPropertyValue: () => 'banana' });
    dashboard.readGridMetrics(state);
    assert.equal(state.gridColumns, 12, 'a non-numeric value never zeroes the grid');
});

// ── move ────────────────────────────────────────────────────────────────────────────────────

test('a mouse drag from the handle moves the widget: live preview, one commit on drop', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.dragHandle.dispatch('pointerdown', ev(b.dragHandle, { clientX: 10, clientY: 10 }));

    b.dragHandle.dispatch('pointermove', ev(b.dragHandle, { clientX: 313, clientY: 176 }));
    assert.equal(b.widget.classList.contains('tm-widget--dragging'), true, 'the widget shows the dragging state');

    const preview = b.grid.children.find(c => c.classList.contains('tm-widget-drop-preview'));
    assert.ok(preview, 'the drop preview follows the pointer');
    assert.equal(preview.style.get('grid-column'), '4 / span 4', 'the preview snaps to the hovered cell (x=313 -> col 3)');

    b.dragHandle.dispatch('pointerup', ev(b.dragHandle, { clientX: 313, clientY: 176 }));

    assert.deepEqual(ref.calls, [['OnGridPositionChanged', 'a', 3, 2]], 'the drop commits once through the unchanged JSInvokable');
    assert.equal(b.widget.classList.contains('tm-widget--dragging'), false);
    assert.ok(!b.grid.children.some(c => c.classList.contains('tm-widget-drop-preview')), 'the preview is cleared');
});

test('a FINGER drags the widget the same way (no long-press gate: touch-action:none lives on the handle)', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.dragHandle.dispatch('pointerdown', ev(b.dragHandle, { pointerType: 'touch', pointerId: 4, clientX: 10, clientY: 10 }));
    b.dragHandle.dispatch('pointermove', ev(b.dragHandle, { pointerType: 'touch', pointerId: 4, clientX: 214, clientY: 10 }));
    b.dragHandle.dispatch('pointerup', ev(b.dragHandle, { pointerType: 'touch', pointerId: 4, clientX: 214, clientY: 10 }));

    assert.deepEqual(ref.calls, [['OnGridPositionChanged', 'a', 2, 0]]);
});

test('a movement below the threshold is a tap: the keyboard-grab click survives and nothing commits', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.dragHandle.dispatch('pointerdown', ev(b.dragHandle, { clientX: 10, clientY: 10 }));
    b.dragHandle.dispatch('pointerup', ev(b.dragHandle, { clientX: 12, clientY: 12 }));

    assert.deepEqual(ref.calls, []);
    assert.equal(b.widget.classList.contains('tm-widget--dragging'), false);
});

test('Escape and pointercancel roll the widget back and notify nothing', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.dragHandle.dispatch('pointerdown', ev(b.dragHandle, { clientX: 10, clientY: 10 }));
    b.dragHandle.dispatch('pointermove', ev(b.dragHandle, { clientX: 313, clientY: 176 }));
    doc.dispatch('keydown', { key: 'Escape' });
    b.dragHandle.dispatch('pointerup', ev(b.dragHandle, { clientX: 313, clientY: 176 }));
    assert.deepEqual(ref.calls, [], 'Escape cancels without a .NET call');
    assert.equal(b.widget.classList.contains('tm-widget--dragging'), false);
    assert.ok(!b.grid.children.some(c => c.classList.contains('tm-widget-drop-preview')));

    b.dragHandle.dispatch('pointerdown', ev(b.dragHandle, { pointerId: 2, clientX: 10, clientY: 10 }));
    b.dragHandle.dispatch('pointermove', ev(b.dragHandle, { pointerId: 2, clientX: 313, clientY: 176 }));
    b.dragHandle.dispatch('pointercancel', ev(b.dragHandle, { pointerId: 2 }));
    assert.deepEqual(ref.calls, [], 'pointercancel cancels too');
});

// ── resize ──────────────────────────────────────────────────────────────────────────────────

test('a mouse drag on the SE handle resizes live and commits the data-* clamped cell count', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.seHandle.dispatch('pointerdown', ev(b.seHandle, { clientX: 296, clientY: 136 }));
    // +419px over a 101.33px step -> 6 columns (the data-max-w clamp), +284px over 76px -> 5 rows (data-max-h)
    b.seHandle.dispatch('pointermove', ev(b.seHandle, { clientX: 296 + 419, clientY: 136 + 284 }));
    b.seHandle.dispatch('pointerup', ev(b.seHandle, { clientX: 296 + 419, clientY: 136 + 284 }));

    assert.deepEqual(ref.calls, [['OnWidgetResized', 'a', 6, 5]], 'the resize clamps at the widget definition maxima');
    assert.equal(b.widget.classList.contains('tm-widget--resizing'), false);
    assert.equal([...b.widget.style.keys()].length, 2, 'only the Blazor-rendered --tm-* variables remain');
});

test('the resize honours the data-* minima as well', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.seHandle.dispatch('pointerdown', ev(b.seHandle, { clientX: 296, clientY: 136 }));
    b.seHandle.dispatch('pointermove', ev(b.seHandle, { clientX: 40, clientY: 40 }));
    b.seHandle.dispatch('pointerup', ev(b.seHandle, { clientX: 40, clientY: 40 }));

    assert.deepEqual(ref.calls, [['OnWidgetResized', 'a', 2, 2]], 'the widget never shrinks below the definition minima');
});

test('the width clamp also honours the grid: a max of 0 (unlimited) stops at the last column', async () => {
    const b = board();
    b.widget.dataset.maxW = '0';
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.seHandle.dispatch('pointerdown', ev(b.seHandle, { clientX: 296, clientY: 136 }));
    b.seHandle.dispatch('pointermove', ev(b.seHandle, { clientX: 3000, clientY: 136 }));
    b.seHandle.dispatch('pointerup', ev(b.seHandle, { clientX: 3000, clientY: 136 }));

    assert.deepEqual(ref.calls, [['OnWidgetResized', 'a', 12, 2]], 'unlimited width stops at the grid edge');
});

test('a resize that never moves commits nothing', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    b.seHandle.dispatch('pointerdown', ev(b.seHandle, { clientX: 296, clientY: 136 }));
    b.seHandle.dispatch('pointerup', ev(b.seHandle, { clientX: 296, clientY: 136 }));
    assert.deepEqual(ref.calls, []);
});

// ── resize ceilings (regression: rows were capped at the COLUMN count) ──────────────────────

test('an e/w resize keeps the rendered row count: a 15-row widget stays 15 rows tall', async () => {
    const b = board();
    b.widget.dataset.maxH = '20';
    b.widget.style.set('--tm-w-rows', '15');
    b.widget.rect.height = 1144; // 15 rows * 76 - 16
    const eHandle = el(['tm-widget-resize-handle', 'tm-widget-resize-e'], b.widget);
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    eHandle.dispatch('pointerdown', ev(eHandle, { clientX: 296, clientY: 500 }));
    eHandle.dispatch('pointermove', ev(eHandle, { clientX: 296 + 419, clientY: 500 }));
    eHandle.dispatch('pointerup', ev(eHandle, { clientX: 296 + 419, clientY: 500 }));

    assert.deepEqual(ref.calls, [['OnWidgetResized', 'a', 6, 15]],
        'the width follows the pointer but the rows are committed as rendered, not recomputed from pixels');
});

test('an unlimited maxH lets rows exceed the column count (12)', async () => {
    const b = board();
    b.widget.dataset.maxH = '0'; // unlimited, as rendered by the razor for MaxHeight = 0
    const sHandle = el(['tm-widget-resize-handle', 'tm-widget-resize-s'], b.widget);
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    await dashboard.dashboards.get(id).ready;

    sHandle.dispatch('pointerdown', ev(sHandle, { clientX: 296, clientY: 136 }));
    // (136 + 1000) / 76 = 14.9 -> 15 rows: above the old 12-row cap
    sHandle.dispatch('pointermove', ev(sHandle, { clientX: 296, clientY: 136 + 1000 }));
    sHandle.dispatch('pointerup', ev(sHandle, { clientX: 296, clientY: 136 + 1000 }));

    assert.deepEqual(ref.calls, [['OnWidgetResized', 'a', 4, 15]],
        'unlimited height exceeds 12 and the untouched width is committed as rendered');
});

// ── lifecycle ───────────────────────────────────────────────────────────────────────────────

test('destroy detaches every pointer listener (grid and document)', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    const state = dashboard.dashboards.get(id);
    await state.ready;
    assert.ok(b.grid.total() > 0, 'precondition: the helper attached to the grid');

    dashboard.destroy(b.root);

    assert.equal(b.grid.total(), 0, 'no listener survives on the grid');
    assert.equal(doc.total(), 0, 'no listener survives on the document');
    assert.equal(dashboard.dashboards.size, 0, 'the dashboard state is dropped');
});

test('destroy mid-drag rolls the widget back and commits nothing', async () => {
    const b = board();
    const ref = dotnet();
    const id = dashboard.init(b.root, ref);
    const state = dashboard.dashboards.get(id);
    await state.ready;

    b.dragHandle.dispatch('pointerdown', ev(b.dragHandle, { clientX: 10, clientY: 10 }));
    b.dragHandle.dispatch('pointermove', ev(b.dragHandle, { clientX: 313, clientY: 176 }));
    dashboard.destroy(b.root);

    assert.deepEqual(ref.calls, []);
    assert.equal(b.widget.classList.contains('tm-widget--dragging'), false);
    assert.ok(!b.grid.children.some(c => c.classList.contains('tm-widget-drop-preview')));
    assert.equal(b.grid.total(), 0);
});

test('init after destroy works again', async () => {
    const b = board();
    const root = b.root;
    dashboard.init(root, dotnet());
    dashboard.destroy(root);

    const id = dashboard.init(root, dotnet());
    const state = dashboard.dashboards.get(id);
    await state.ready;
    assert.equal(b.grid.count('pointerdown'), 2);
    assert.ok(dashboard.dashboards.has(id));
});
