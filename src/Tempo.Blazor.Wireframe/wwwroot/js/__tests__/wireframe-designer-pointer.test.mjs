// wireframe-designer.js on the shared helpers (wireframe redesign plan ce19aa8c, phase 5). The REAL
// wireframe-designer.js, pointer-drag.js, pointer-gesture.js and wireframe-gestures.mjs run against a
// hand-rolled SVG DOM: a mouse, a finger and a pen select / drag / resize / rotate / rubber-band,
// a 500 ms touch hold opens the context menu, two fingers pinch-zoom and pan within the 0.15x-5x clamp,
// a double-tap is a double-click, and every cancel path (Escape, pointercancel, the second finger, destroy)
// restores the start geometry VERBATIM and commits nothing.
import test, { mock } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const selfUrl = pathToFileURL(path.resolve(here, '../wireframe-designer.js')).href;
const coreJs = path.resolve(here, '../../../../Tempo.Blazor/wwwroot/js');
const dragUrl = pathToFileURL(path.join(coreJs, 'pointer-drag.js')).href;
const gestureUrl = pathToFileURL(path.join(coreJs, 'pointer-gesture.js')).href;
const gesturesUrl = pathToFileURL(path.resolve(here, '../wireframe-gestures.mjs')).href;

// ── a small DOM ─────────────────────────────────────────────────────────────────────────────

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

function matchesOne(el, part) {
    part = part.trim();
    if (part.startsWith('.')) return el.classList.contains(part.slice(1));
    const attr = part.match(/^\[([\w-]+)(?:="([^"]*)")?\]$/);
    if (attr) {
        const value = el.attrs.get(attr[1]);
        return attr[2] === undefined ? value !== undefined : value === attr[2];
    }
    return el.tag === part;
}
const matches = (el, selector) => selector.split(',').some(part => matchesOne(el, part));

function node(tag, attrs = {}, parent = null) {
    const classes = new Set();
    const n = {
        ...bag(), tag, attrs: new Map(), children: [], parent, style: {}, captured: new Set(), released: [],
        classList: {
            add: (...c) => c.forEach(x => classes.add(x)),
            remove: (...c) => c.forEach(x => classes.delete(x)),
            contains: c => classes.has(c),
        },
        setAttribute(name, value) { n.attrs.set(name, String(value)); if (name === 'class') String(value).split(/\s+/).forEach(c => c && classes.add(c)); },
        getAttribute(name) { return n.attrs.has(name) ? n.attrs.get(name) : null; },
        hasAttribute(name) { return n.attrs.has(name); },
        removeAttribute(name) { n.attrs.delete(name); },
        appendChild(child) { child.parent = n; n.children.push(child); return child; },
        remove() { if (n.parent) n.parent.children = n.parent.children.filter(c => c !== n); n.parent = null; },
        setPointerCapture(id) { n.captured.add(id); },
        releasePointerCapture(id) { n.captured.delete(id); n.released.push(id); },
        closest(selector) { for (let p = n; p; p = p.parent) if (matches(p, selector)) return p; return null; },
        contains(el) { for (let p = el; p; p = p.parent) if (p === n) return true; return false; },
        querySelectorAll(selector) {
            const out = [];
            const walk = el => { for (const c of el.children) { if (matches(c, selector)) out.push(c); walk(c); } };
            walk(n);
            return out;
        },
        querySelector(selector) { return n.querySelectorAll(selector)[0] ?? null; },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 }),
        focus() { n.focused = true; },
    };
    for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
    // Pointer events bubble: dispatching on a descendant reaches the sessions attached on the svg.
    const own = n.dispatch;
    n.dispatch = (type, event = {}) => {
        own(type, event);
        if (n.parent) n.parent.dispatch(type, event);
        return event;
    };
    if (parent) parent.children.push(n);
    return n;
}

function svgNode() {
    const svg = node('svg');
    svg.viewBox = { baseVal: { x: 0, y: 0, width: 800, height: 600 } };
    const setAttribute = svg.setAttribute;
    svg.setAttribute = (name, value) => {
        setAttribute(name, value);
        if (name === 'viewBox') {
            const [x, y, w, h] = String(value).split(/\s+/).map(Number);
            Object.assign(svg.viewBox.baseVal, { x, y, width: w, height: h });
        }
    };
    svg.id = 'wd-test';
    svg.setAttribute('viewBox', '0 0 800 600');
    svg.createSVGPoint = () => ({
        x: 0, y: 0,
        matrixTransform(m) { return { x: this.x * m.a + this.y * m.c + m.e, y: this.x * m.b + this.y * m.d + m.f }; },
    });
    // Client px -> svg units through the live viewBox (the svg fills 800x600 at the origin).
    svg.getScreenCTM = () => {
        const vb = svg.viewBox.baseVal;
        const s = 800 / vb.width;
        return { a: s, b: 0, c: 0, d: s, e: -vb.x * s, f: -vb.y * s, inverse() {
            const inv = { a: 1 / s, b: 0, c: 0, d: 1 / s, e: vb.x, f: vb.y };
            inv.inverse = () => this;
            return inv;
        } };
    };
    svg.closest = (selector) => (matches(svg, selector) ? svg : (svg.parent ? svg.parent.closest(selector) : null));
    return svg;
}

/** An element group (the way TmWireframeDesignerCanvas renders it) with its inner svg. */
function element(svg, id, { x = 0, y = 0, w = 100, h = 60, locked = false, type = 'TmButton' } = {}) {
    const g = node('g', {
        'data-el-id': id, 'data-type': type, 'data-w': w, 'data-h': h, 'data-rotation': 0,
        'data-locked': locked ? 'true' : 'false', transform: `translate(${x}, ${y}) rotate(0, ${w / 2}, ${h / 2})`,
    }, svg);
    node('svg', { width: w, height: h, viewBox: `0 0 ${w} ${h}` }, g);
    const body = node('rect', {}, g.children[0]);
    return { g, body };
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); },
        named: name => calls.filter(c => c[0] === name) };
}

let doc;
let engine;
let helpers;

async function boot({ coarse = false } = {}) {
    doc = {
        ...bag(),
        readyState: 'complete', baseURI: 'https://example.test/app/', currentScript: { src: selfUrl },
        contains: () => true, activeElement: null, body: node('body'),
        documentElement: { clientWidth: 800, clientHeight: 600 },
        scrollingElement: { scrollLeft: 0, scrollTop: 0 },
        elementFromPoint: () => null,
        createElementNS: (_ns, tag) => node(tag),
    };
    globalThis.document = doc;
    globalThis.window = { matchMedia: q => ({ matches: coarse && q === '(pointer: coarse)' }) };
    globalThis.matchMedia = globalThis.window.matchMedia;
    globalThis.requestAnimationFrame = fn => { fn(); return 1; };
    globalThis.cancelAnimationFrame = () => {};
    await import(`${selfUrl}?${Math.random()}`);
    engine = globalThis.window.tmWireframeDesigner;
    helpers = {
        drag: await import(dragUrl),
        gesture: await import(gestureUrl),
        gestures: await import(gesturesUrl),
    };
    engine._realLoadHelpers = engine._loadHelpers;
    engine._loadHelpers = () => Promise.resolve(helpers);
    return engine;
}

async function canvas({ readOnly = false, gridSize = 8, coarse = false, lightEditing = false, initialZoom, canvasWidth = 800, canvasHeight = 600 } = {}) {
    await boot({ coarse });
    const svg = svgNode();
    const wrap = node('div', { class: 'tm-wd-canvas-wrap' });
    wrap.children.push(svg); svg.parent = wrap;
    const ref = dotnet();
    const id = engine.init(svg, ref, { readOnly, gridSize, showGrid: true, canvasWidth, canvasHeight, lightEditing, initialZoom });
    await engine.instances.get(id).ready;
    return { svg, wrap, ref, id, inst: () => engine.instances.get(id) };
}

test.afterEach(() => {
    mock.timers.reset();
    engine?.instances.forEach((_v, id) => engine.destroy({ id }));
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.matchMedia;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    shiftKey: false, preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...over,
});
const touch = (target, over = {}) => ev(target, { pointerType: 'touch', pointerId: 7, buttons: 1, ...over });

const translateOf = g => g.getAttribute('transform');

// ── the old mouse gesture is gone ──────────────────────────────────────────────────────────────

test('no mouse listener remains: the canvas gesture is Pointer Events', async () => {
    const c = await canvas();
    for (const type of ['mousedown', 'mousemove', 'mouseup']) {
        assert.equal(c.svg.count(type), 0, `svg ${type}`);
        assert.equal(doc.count(type), 0, `document ${type}`);
    }
    assert.ok(c.svg.count('pointerdown') >= 2, 'the drag and the long-press sessions listen on the svg');
});

test('destroy removes every listener it added, including a destroy during the helper import', async () => {
    const c = await canvas();
    engine.destroy(c.svg);
    assert.equal(c.svg.total(), 0);
    assert.equal(doc.total(), 0);
});

test('a failed helper import is reported, does not throw out of init and is not cached for the next editor', async () => {
    await boot({ coarse: false });
    // The REAL loader: under node the deployed-layout URLs (../../Tempo.Blazor/js/...) do not exist, so the import rejects.
    engine._loadHelpers = engine._realLoadHelpers;
    const errors = [];
    const original = console.error;
    console.error = (...args) => errors.push(args.map(String).join(' '));
    try {
        const svg = svgNode();
        const id = engine.init(svg, dotnet(), { gridSize: 8 });
        await engine.instances.get(id).ready;     // resolves: the failure is handled, never an unhandled rejection
        assert.equal(errors.length, 1);
        assert.match(errors[0], /could not be loaded/);
        assert.equal(svg.count('pointerdown'), 1, 'only the middle-button listener; no helper session was attached');
        assert.equal(engine._helpers, null, 'the failed import is not cached');

        const svg2 = svgNode(); svg2.id = 'wd-test-2';
        await engine.instances.get(engine.init(svg2, dotnet(), { gridSize: 8 })).ready;
        assert.equal(errors.length, 2, 'the second editor retried the import (and reported it again)');
    } finally {
        console.error = original;
    }
});
// ── select, drag ───────────────────────────────────────────────────────────────────────────────

test('a mouse press selects the element and a drag moves it live, notifying OnElementMoved once at the end', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    assert.deepEqual(c.ref.named('OnSelectionChanged'), [['OnSelectionChanged', ['a']]]);

    a.body.dispatch('pointermove', ev(a.body, { clientX: 100, clientY: 90 }));
    assert.equal(translateOf(a.g), 'translate(80, 72)', 'live preview, snapped to the 8px grid');

    a.body.dispatch('pointerup', ev(a.body, { clientX: 100, clientY: 90 }));
    assert.deepEqual(c.ref.named('OnDragStarted'), [['OnDragStarted', ['a']]]);
    assert.deepEqual(c.ref.named('OnElementMoved'), [['OnElementMoved', 'a', 80, 72]]);
});

test('a tap selects and commits no move (no empty undo step)', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    assert.deepEqual(c.ref.named('OnSelectionChanged'), [['OnSelectionChanged', ['a']]]);
    assert.equal(c.ref.named('OnElementMoved').length, 0);
    assert.equal(c.ref.named('OnElementsMoved').length, 0);
    assert.equal(translateOf(a.g), 'translate(40, 40) rotate(0, 50, 30)', 'untouched');
});

test('a FINGER drags an element the same way (touch did nothing before)', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', touch(a.body, { clientX: 100, clientY: 90 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 100, clientY: 90 }));

    assert.deepEqual(c.ref.named('OnElementMoved'), [['OnElementMoved', 'a', 80, 72]]);
});

test('a pen drags like a mouse', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 0, y: 0 });
    const pen = over => ev(a.body, { pointerType: 'pen', pointerId: 3, ...over });

    a.body.dispatch('pointerdown', pen({ clientX: 10, clientY: 10 }));
    a.body.dispatch('pointermove', pen({ clientX: 50, clientY: 10 }));
    a.body.dispatch('pointerup', pen({ clientX: 50, clientY: 10 }));

    assert.deepEqual(c.ref.named('OnElementMoved'), [['OnElementMoved', 'a', 40, 0]]);
});

test('dragging a selected group of elements notifies OnElementsMoved once', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 0, y: 0 });
    const b = element(c.svg, 'b', { x: 200, y: 0 });
    engine.setSelection(c.svg, ['a', 'b']);

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 10, clientY: 10 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 50, clientY: 10 }));
    a.body.dispatch('pointerup', ev(a.body, { clientX: 50, clientY: 10 }));

    assert.deepEqual(c.ref.named('OnElementsMoved'), [['OnElementsMoved', [{ id: 'a', x: 40, y: 0 }, { id: 'b', x: 240, y: 0 }]]]);
    assert.equal(translateOf(b.g), 'translate(240, 0)');
});

test('a locked element cannot be selected or dragged', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 0, y: 0, locked: true });

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 10, clientY: 10 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 90, clientY: 10 }));
    a.body.dispatch('pointerup', ev(a.body, { clientX: 90, clientY: 10 }));

    assert.deepEqual(c.ref.calls.filter(call => call[0] !== 'OnCanvasMouseMoved'), []);
    assert.equal(translateOf(a.g), 'translate(0, 0) rotate(0, 50, 30)');
});

test('read-only: a press selects nothing and a drag moves nothing', async () => {
    const c = await canvas({ readOnly: true });
    const a = element(c.svg, 'a', { x: 0, y: 0 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 10, clientY: 10 }));
    a.body.dispatch('pointermove', touch(a.body, { clientX: 60, clientY: 10 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 10 }));

    assert.equal(c.ref.calls.length, 0);
    assert.equal(translateOf(a.g), 'translate(0, 0) rotate(0, 50, 30)');
});

test('setReadOnly(true) in the middle of a drag rolls it back and commits nothing', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 140, clientY: 60 }));
    engine.setReadOnly(c.svg, true);
    a.body.dispatch('pointerup', ev(a.body, { clientX: 140, clientY: 60 }));

    assert.equal(c.ref.named('OnElementMoved').length, 0);
    assert.equal(translateOf(a.g), 'translate(40, 40) rotate(0, 50, 30)');
});

// ── cancel paths ───────────────────────────────────────────────────────────────────────────────

test('Escape during a drag restores the start transform VERBATIM and commits nothing', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    const original = translateOf(a.g);

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 160, clientY: 60 }));
    assert.notEqual(translateOf(a.g), original);
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });

    assert.equal(translateOf(a.g), original);
    assert.equal(c.ref.named('OnElementMoved').length, 0);
});

test('pointercancel during a resize restores the start rect and commits nothing', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');

    handle.dispatch('pointerdown', ev(handle, { clientX: 140, clientY: 100 }));
    c.svg.dispatch('pointermove', ev(c.svg, { clientX: 200, clientY: 160 }));
    assert.equal(a.g.getAttribute('data-w'), '160');
    c.svg.dispatch('pointercancel', ev(c.svg, { clientX: 200, clientY: 160 }));

    assert.equal(a.g.getAttribute('data-w'), '100');
    assert.equal(a.g.getAttribute('data-h'), '60');
    assert.equal(a.g.children[0].getAttribute('width'), '100');
    assert.equal(c.ref.named('OnElementResized').length, 0);
});

// ── resize / rotate / rubber-band ──────────────────────────────────────────────────────────────

test('a FINGER resizes through a handle and OnElementResized fires once', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');

    handle.dispatch('pointerdown', touch(handle, { clientX: 140, clientY: 100 }));
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 200, clientY: 140 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 200, clientY: 140 }));

    assert.deepEqual(c.ref.named('OnElementResized'), [['OnElementResized', 'a', 40, 40, 160, 104]]);
});

test('a drag on the rotate handle rotates and notifies OnElementRotated once', async () => {
    const c = await canvas();
    element(c.svg, 'a', { x: 100, y: 100, w: 100, h: 100 });
    engine.setSelection(c.svg, ['a']);
    const rot = c.svg.querySelector('[data-rotate-handle]');

    // centre (150,150); the handle is straight above (150, 88): drag it to the right of the centre = +90deg
    rot.dispatch('pointerdown', ev(rot, { clientX: 150, clientY: 88 }));
    c.svg.dispatch('pointermove', ev(c.svg, { clientX: 260, clientY: 150 }));
    c.svg.dispatch('pointerup', ev(c.svg, { clientX: 260, clientY: 150 }));

    const calls = c.ref.named('OnElementRotated');
    assert.equal(calls.length, 1);
    assert.equal(calls[0][1], 'a');
    assert.ok(Math.abs(calls[0][2] - 90) < 1, `rotation ${calls[0][2]}`);
});

test('a drag on empty canvas rubber-bands and selects what it fully contains', async () => {
    const c = await canvas();
    element(c.svg, 'a', { x: 20, y: 20, w: 40, h: 40 });
    element(c.svg, 'b', { x: 400, y: 400, w: 40, h: 40 });

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 5, clientY: 5 }));
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 120, clientY: 120 }));
    assert.ok(c.svg.children.some(n => n.classList.contains('tm-wd-rubber')), 'the rubber band is drawn');
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 120, clientY: 120 }));

    assert.deepEqual(c.ref.named('OnMultiSelect'), [['OnMultiSelect', ['a']]]);
    assert.ok(!c.svg.children.some(n => n.classList.contains('tm-wd-rubber')), 'and removed');
});

test('a tap on empty canvas clears the selection and leaves no rubber band behind', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 20, y: 20 });
    engine.setSelection(c.svg, ['a']);

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 700, clientY: 500 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 700, clientY: 500 }));

    assert.deepEqual(c.ref.named('OnSelectionChanged'), [['OnSelectionChanged', []]]);
    assert.ok(!c.svg.children.some(n => n.classList.contains('tm-wd-rubber')));
    assert.equal(a.g.getAttribute('data-w'), '100');
});

test('connector tool: a drag from one element to another creates the connector (target by elementFromPoint)', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 0, y: 0 });
    const b = element(c.svg, 'b', { x: 300, y: 0 });
    engine.setToolMode(c.svg, 'connector');
    doc.elementFromPoint = () => b.body;   // the pointer is captured by the svg: the drop is hit-tested

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 20, clientY: 20 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 320, clientY: 20 }));
    assert.ok(c.svg.children.some(n => n.classList.contains('tm-wd-connector-preview')), 'the preview line follows');
    a.body.dispatch('pointerup', ev(a.body, { clientX: 320, clientY: 20 }));

    assert.deepEqual(c.ref.named('OnConnectorCreated'), [['OnConnectorCreated', 'a', 'b']]);
    assert.ok(!c.svg.children.some(n => n.classList.contains('tm-wd-connector-preview')));
});

test('a connector waypoint is dragged and reported at its new position', async () => {
    const c = await canvas();
    const conn = node('g', { 'data-connector-id': 'c1' }, c.svg);
    const wp = node('circle', { class: 'tm-wd-connector__waypoint', cx: 100, cy: 100, 'data-waypoint-index': 2 }, conn);

    wp.dispatch('pointerdown', touch(wp, { clientX: 100, clientY: 100 }));
    wp.dispatch('pointermove', touch(wp, { clientX: 140, clientY: 130 }));
    wp.dispatch('pointerup', touch(wp, { clientX: 140, clientY: 130 }));

    assert.deepEqual(c.ref.named('OnConnectorWaypointDragged'), [['OnConnectorWaypointDragged', 'c1', 2, 140, 130]]);
});

// ── pan (Space / tool / middle button) ────────────────────────────────────────────────────────

test('the pan tool pans with one finger and commits the viewBox once', async () => {
    const c = await canvas();
    engine.setToolMode(c.svg, 'pan');

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 250, clientY: 280 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 250, clientY: 280 }));

    assert.deepEqual(c.ref.named('OnViewBoxChanged'), [['OnViewBoxChanged', 50, 20, 800, 600]]);
});

test('Escape during a pan restores the start viewBox', async () => {
    const c = await canvas();
    engine.setToolMode(c.svg, 'pan');

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 250, clientY: 280 }));
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });

    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0);
});

test('the middle mouse button still pans (the mouse affordance pointer-drag.js leaves to the host)', async () => {
    const c = await canvas();

    c.svg.dispatch('pointerdown', ev(c.svg, { button: 1, buttons: 4, clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointermove', ev(c.svg, { button: -1, buttons: 4, clientX: 260, clientY: 300 }));
    c.svg.dispatch('pointerup', ev(c.svg, { button: 1, buttons: 0, clientX: 260, clientY: 300 }));

    assert.deepEqual(c.ref.named('OnViewBoxChanged'), [['OnViewBoxChanged', 40, 0, 800, 600]]);
});

// ── two fingers: pinch zoom + pan ─────────────────────────────────────────────────────────────

function twoFingers(c, a, b, { start = [[300, 300], [400, 300]] } = {}) {
    const f1 = over => touch(c.svg, { pointerId: 1, isPrimary: true, ...over });
    const f2 = over => touch(c.svg, { pointerId: 2, isPrimary: false, ...over });
    c.svg.dispatch('pointerdown', f1({ clientX: start[0][0], clientY: start[0][1] }));
    c.svg.dispatch('pointerdown', f2({ clientX: start[1][0], clientY: start[1][1] }));
    return { f1, f2 };
}

test('two fingers moving apart pinch-zoom the canvas around the fingers; the viewBox is committed once at the end', async () => {
    const c = await canvas();
    const { f1, f2 } = twoFingers(c);

    c.svg.dispatch('pointermove', f1({ clientX: 250, clientY: 300 }));
    c.svg.dispatch('pointermove', f2({ clientX: 450, clientY: 300 }));   // distance 100 -> 200: 2x
    const vb = c.svg.viewBox.baseVal;
    assert.ok(Math.abs(vb.width - 400) < 1e-6, `viewBox width ${vb.width}`);
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0, 'live: nothing is committed yet');

    c.svg.dispatch('pointerup', f2({ clientX: 450, clientY: 300 }));
    const committed = c.ref.named('OnViewBoxChanged');
    assert.equal(committed.length, 1);
    assert.ok(Math.abs(committed[0][3] - 400) < 1e-6, 'width is the zoomed one');
    assert.ok(Math.abs(committed[0][4] - 300) < 1e-6, 'height follows');
    const zoom = c.ref.named('OnZoomChanged');
    assert.equal(zoom.length, 1);
    assert.ok(Math.abs(zoom[0][1] - 2) < 1e-6, `zoom ${zoom[0][1]}`);
});

test('the pinch zoom is clamped to 0.15x-5x', async () => {
    const c = await canvas();
    const { f1, f2 } = twoFingers(c);

    c.svg.dispatch('pointermove', f1({ clientX: 100, clientY: 300 }));
    c.svg.dispatch('pointermove', f2({ clientX: 700, clientY: 300 }));   // 6x
    assert.ok(Math.abs(c.svg.viewBox.baseVal.width - 160) < 1e-6, `clamped to 5x (width 160): ${c.svg.viewBox.baseVal.width}`);

    c.svg.dispatch('pointermove', f1({ clientX: 349, clientY: 300 }));
    c.svg.dispatch('pointermove', f2({ clientX: 351, clientY: 300 }));   // ~0.02x
    assert.ok(Math.abs(c.svg.viewBox.baseVal.width - 800 / 0.15) < 1e-3, 'clamped to 0.15x');
});

test('two fingers moving together pan the canvas without zooming (the canvas follows the fingers)', async () => {
    const c = await canvas();
    const { f1, f2 } = twoFingers(c);

    c.svg.dispatch('pointermove', f1({ clientX: 340, clientY: 340 }));
    c.svg.dispatch('pointermove', f2({ clientX: 440, clientY: 340 }));

    const vb = c.svg.viewBox.baseVal;
    assert.ok(Math.abs(vb.width - 800) < 1e-6, 'no zoom');
    assert.ok(Math.abs(vb.x + 40) < 1e-6 && Math.abs(vb.y + 40) < 1e-6, `panned by the finger travel: ${vb.x}, ${vb.y}`);
});

test('the second finger hands over: the one-finger drag rolls back and the pinch commits only the viewBox', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    const original = translateOf(a.g);

    a.body.dispatch('pointerdown', touch(a.body, { pointerId: 1, clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', touch(a.body, { pointerId: 1, clientX: 120, clientY: 60 }));
    assert.notEqual(translateOf(a.g), original, 'the drag is live');
    c.svg.dispatch('pointerdown', touch(c.svg, { pointerId: 2, isPrimary: false, clientX: 300, clientY: 300 }));

    assert.equal(translateOf(a.g), original, 'the drag rolled back');
    assert.equal(c.ref.named('OnElementMoved').length, 0);
});

test('Escape during a pinch restores the start viewBox and commits nothing', async () => {
    const c = await canvas();
    const { f1, f2 } = twoFingers(c);

    c.svg.dispatch('pointermove', f1({ clientX: 250, clientY: 300 }));
    c.svg.dispatch('pointermove', f2({ clientX: 450, clientY: 300 }));
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });

    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0);
});

test('a mouse cannot pinch', async () => {
    const c = await canvas();
    c.svg.dispatch('pointerdown', ev(c.svg, { pointerId: 1, clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointerdown', ev(c.svg, { pointerId: 2, isPrimary: false, clientX: 400, clientY: 300 }));
    c.svg.dispatch('pointermove', ev(c.svg, { pointerId: 2, isPrimary: false, clientX: 700, clientY: 300 }));
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600', 'a mouse pointer never forms a pinch');
});

// ── long-press = context menu ─────────────────────────────────────────────────────────────────

test('a 500 ms touch hold on an element opens the element context menu (offset from the finger)', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(499);
    assert.equal(c.ref.named('OnElementContextMenu').length, 0, 'not yet');
    mock.timers.tick(1);

    const calls = c.ref.named('OnElementContextMenu');
    assert.equal(calls.length, 1);
    assert.equal(calls[0][1], 'a');
    assert.ok(calls[0][2] > 60 && calls[0][3] > 60, 'the menu is parked beside the finger, not under it');
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(c.ref.named('OnElementMoved').length, 0, 'the hold never becomes a move');
});

test('a hold on an element still opens the ELEMENT menu after the press re-rendered (detached) the shape', async () => {
    // The body session selects on the press and Blazor then replaces the markup-rendered shape nodes: the hold session, which
    // looks at the same pointerdown a moment later, must not find a detached node (closest() = null = "the canvas").
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    c.ref.invokeMethodAsync = (...args) => {
        c.ref.calls.push(args);
        if (args[0] === 'OnSelectionChanged') {
            c.svg.children = c.svg.children.filter(n => n !== a.g);   // the re-render: the pressed shape is gone from the document
            a.body.parent = null; a.g.parent = null;
        }
        return Promise.resolve();
    };

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(500);

    assert.equal(c.ref.named('OnCanvasContextMenu').length, 0, 'not the canvas menu');
    const calls = c.ref.named('OnElementContextMenu');
    assert.equal(calls.length, 1);
    assert.equal(calls[0][1], 'a');
});
// The release-click guard of the light-editing hold sheet swallows the POINTER compatibility click of the lift only: a click with
// detail === 0 is a keyboard (Enter / Space) or assistive-technology activation and must reach its target.
test('light editing: the click of the lift after a hold is swallowed once, but a keyboard / AT click (detail 0) passes and keeps the guard armed', async () => {
    mock.timers.enable({ apis: ['setTimeout', 'Date'] });
    const c = await canvas({ lightEditing: true, coarse: true });
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(500);
    assert.equal(c.ref.named('OnElementContextMenu').length, 1, 'the hold opened the sheet');
    assert.equal(c.inst().holdClickArmed, true, 'light editing arms the release-click guard');

    const keyboard = { detail: 0, preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; } };
    doc.dispatch('click', keyboard);
    assert.equal(keyboard.defaultPrevented, undefined, 'a keyboard / screen-reader activation is never swallowed');
    assert.equal(keyboard.stopped, undefined);
    assert.equal(c.inst().holdClickArmed, true, 'and it does not spend the guard that belongs to the finger lift');

    const lift = { detail: 1, preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; } };
    doc.dispatch('click', lift);
    assert.equal(lift.defaultPrevented, true, 'the pointer compatibility click of the lift is swallowed');
    assert.equal(lift.stopped, true);
    assert.equal(c.inst().holdClickArmed, false, 'once');

    const next = { detail: 1, preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; } };
    doc.dispatch('click', next);
    assert.equal(next.defaultPrevented, undefined, 'the second click is a real one');
});
test('a hold on empty canvas opens the canvas context menu', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 500, clientY: 400 }));
    mock.timers.tick(500);

    const calls = c.ref.named('OnCanvasContextMenu');
    assert.equal(calls.length, 1);
    assert.equal(calls[0][1], 500, 'svg x');
    assert.equal(calls[0][2], 400, 'svg y');
});

test('a finger that moves before the hold completes is a drag, not a menu', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', touch(a.body, { clientX: 100, clientY: 60 }));
    mock.timers.tick(600);
    a.body.dispatch('pointerup', touch(a.body, { clientX: 100, clientY: 60 }));

    assert.equal(c.ref.named('OnElementContextMenu').length, 0);
    assert.equal(c.ref.named('OnElementMoved').length, 1);
});

test('a mouse press-and-hold never opens the menu (the right button does, through contextmenu)', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(1000);
    assert.equal(c.ref.named('OnElementContextMenu').length, 0);

    a.body.dispatch('contextmenu', ev(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(c.ref.named('OnElementContextMenu').length, 1, 'native contextmenu still works for the mouse');
});

test('the native contextmenu of a touch long-press (Android) does not open a second menu, even when it is retargeted to the svg', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(500);
    a.body.dispatch('contextmenu', ev(a.body, { pointerType: 'touch', clientX: 60, clientY: 60 }));
    // Chrome fires the contextmenu while the finger is still down and, with the svg holding the pointer capture, at the SVG itself
    // (no pointerType on a plain MouseEvent either): that must open nothing, least of all the canvas menu.
    c.svg.dispatch('contextmenu', { target: c.svg, clientX: 60, clientY: 60, preventDefault() {} });
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    assert.equal(c.ref.named('OnElementContextMenu').length, 1, 'only the hold session opened it');
    assert.equal(c.ref.named('OnCanvasContextMenu').length, 0, 'and it never opened the canvas menu');
});

// ── double-tap = double-click ─────────────────────────────────────────────────────────────────

test('a double-tap on a connector adds a waypoint like a double-click, and a real dblclick right after is deduped', async () => {
    const c = await canvas();
    const conn = node('g', { 'data-connector-id': 'c1' }, c.svg);
    const hit = node('path', { class: 'tm-wd-connector__hit' }, conn);
    const line = node('path', { class: 'tm-wd-connector__path' }, conn);
    line.getTotalLength = () => 100;
    line.getPointAtLength = i => ({ x: i, y: 50 });
    const tap = t => {
        hit.dispatch('pointerdown', touch(hit, { clientX: 40, clientY: 50 }));
        hit.dispatch('pointerup', touch(hit, { clientX: 40, clientY: 50, timeStamp: t }));
    };

    tap(1000);
    assert.equal(c.ref.named('OnConnectorWaypointAdded').length, 0, 'one tap is not a double-tap');
    tap(1200);
    assert.equal(c.ref.named('OnConnectorWaypointAdded').length, 1);

    c.svg.dispatch('dblclick', ev(hit, { clientX: 40, clientY: 50 }));
    assert.equal(c.ref.named('OnConnectorWaypointAdded').length, 1, 'the browser dblclick of the same gesture is ignored');
});

test('a mouse double-click on a connector still adds a waypoint', async () => {
    const c = await canvas();
    const conn = node('g', { 'data-connector-id': 'c1' }, c.svg);
    const hit = node('path', { class: 'tm-wd-connector__hit' }, conn);
    const line = node('path', { class: 'tm-wd-connector__path' }, conn);
    line.getTotalLength = () => 100;
    line.getPointAtLength = i => ({ x: i, y: 50 });

    c.svg.dispatch('dblclick', ev(hit, { clientX: 40, clientY: 50, preventDefault() {}, stopPropagation() {} }));

    assert.equal(c.ref.named('OnConnectorWaypointAdded').length, 1);
});

// ── coarse pointer: bigger handles ────────────────────────────────────────────────────────────

test('fine pointer: 8px resize handles and no extra hit areas', async () => {
    const c = await canvas({ coarse: false });
    element(c.svg, 'a', { x: 100, y: 100, w: 200, h: 120 });
    engine.setSelection(c.svg, ['a']);

    const handle = c.svg.querySelector('[data-handle="se"]');
    assert.equal(handle.getAttribute('width'), '8');
    assert.equal(c.svg.querySelectorAll('[data-handle-hit]').length, 0);
});

test('coarse pointer: handles are painted x1.5 and every handle has a larger invisible hit area', async () => {
    const c = await canvas({ coarse: true });
    element(c.svg, 'a', { x: 100, y: 100, w: 200, h: 120 });
    engine.setSelection(c.svg, ['a']);

    const handle = c.svg.querySelector('[data-handle="se"]');
    assert.equal(handle.getAttribute('width'), '12', '8 x 1.5');
    const hits = c.svg.querySelectorAll('[data-handle-hit]');
    assert.equal(hits.length, 9, '8 resize handles + the rotate handle');
    const se = hits.find(h => h.getAttribute('data-handle-hit') === 'se');
    assert.ok(Number(se.getAttribute('width')) >= 24, `hit area ${se.getAttribute('width')} >= 24`);
    assert.equal(se.getAttribute('fill'), 'transparent');
    assert.equal(se.getAttribute('data-handle'), 'se', 'the hit rect carries the same handle key, so the resize gesture reads it');
});

test('a finger resizes through the larger hit area', async () => {
    const c = await canvas({ coarse: true });
    const a = element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const hit = c.svg.querySelectorAll('[data-handle-hit]').find(h => h.getAttribute('data-handle-hit') === 'se');

    hit.dispatch('pointerdown', touch(hit, { clientX: 150, clientY: 110 }));   // 10px off the visual corner
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 200, clientY: 150 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 200, clientY: 150 }));

    assert.equal(c.ref.named('OnElementResized').length, 1);
    assert.equal(a.g.getAttribute('data-w'), '152', '100 + 50, snapped to the 8px grid');
});

// ── keyboard alternative (WCAG 2.5.7) ─────────────────────────────────────────────────────────

function key(c, name, over = {}) {
    c.svg.closest = (selector) => (selector === ':focus-within' ? c.svg : matches(c.svg, selector) ? c.svg : null);
    // Returns the event so a test can ask whether the canvas prevented it (defaultPrevented stays undefined until preventDefault runs).
    return doc.dispatch('keydown', { key: name, code: name, target: { tagName: 'DIV', closest: () => null, matches: () => false }, preventDefault() { this.defaultPrevented = true; }, ...over });
}

test('arrow keys nudge the selection by 1 and Shift+arrow by one grid cell', async () => {
    const c = await canvas({ gridSize: 8 });
    element(c.svg, 'a', { x: 40, y: 40 });
    engine.setSelection(c.svg, ['a']);

    key(c, 'ArrowRight');
    key(c, 'ArrowDown', { shiftKey: true });

    assert.deepEqual(c.ref.named('OnElementMoved'), [
        ['OnElementMoved', 'a', 41, 40],
        ['OnElementMoved', 'a', 41, 48],
    ]);
});

test('arrow keys do nothing in a read-only canvas and skip locked elements', async () => {
    const c = await canvas({ readOnly: true });
    element(c.svg, 'a', { x: 40, y: 40 });
    engine.setSelection(c.svg, ['a']);
    key(c, 'ArrowRight');
    assert.equal(c.ref.named('OnElementMoved').length, 0);

    engine.setReadOnly(c.svg, false);
    c.svg.querySelector('[data-el-id="a"]').setAttribute('data-locked', 'true');
    key(c, 'ArrowRight');
    assert.equal(c.ref.named('OnElementMoved').length, 0);
});

// ── wheel zoom keeps working and shares the clamp ─────────────────────────────────────────────

test('wheel zoom still works for the mouse and stops at the 5x limit', async () => {
    const c = await canvas();
    const wheel = (deltaY) => c.svg.dispatch('wheel', { deltaY, clientX: 400, clientY: 300, preventDefault() {} });

    wheel(-100);   // factor 0.9
    assert.ok(Math.abs(c.svg.viewBox.baseVal.width - 720) < 1e-6);
    for (let i = 0; i < 40; i++) wheel(-100);
    assert.ok(c.svg.viewBox.baseVal.width >= 160 - 1e-6, 'never beyond 5x');
});

// ── phase 6 carry-forward ──────────────────────────────────────────────────────────────────────

const target = { tagName: 'DIV', closest: () => null, matches: () => false };
// The canvas only handles keys while focus is inside it (:focus-within): the test svg answers that query.
const press = (c, name, over = {}) => {
    const closest = c.svg.closest;
    c.svg.closest = (selector) => (selector === ':focus-within' ? c.svg : closest(selector));
    doc.dispatch('keydown', { key: name, code: name, target, preventDefault() {}, stopPropagation() {}, ...over });
    c.svg.closest = closest;
};

test('Escape in the middle of a middle-button pan restores the viewBox and detaches the move/up listeners', async () => {
    const c = await canvas();
    c.svg.dispatch('pointerdown', ev(c.svg, { button: 1, buttons: 4, clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointermove', ev(c.svg, { button: -1, buttons: 4, clientX: 240, clientY: 300 }));
    assert.equal(c.svg.viewBox.baseVal.x, 60, 'the pan moved the view');
    assert.equal(c.svg.count('pointermove') > 0, true);

    press(c, 'Escape');

    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600', 'restored verbatim');
    assert.equal(c.svg.captured.has(1), false, 'pointer capture released');
    assert.deepEqual(c.svg.released, [1]);
    assert.equal(c.inst().middlePan, null);
    assert.equal(c.svg.style.cursor, '');
    // the late pointerup of the abandoned pan commits nothing
    c.svg.dispatch('pointerup', ev(c.svg, { button: 1, buttons: 0, clientX: 240, clientY: 300 }));
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0);
    c.svg.dispatch('pointermove', ev(c.svg, { button: -1, buttons: 4, clientX: 100, clientY: 300 }));
    assert.equal(c.svg.viewBox.baseVal.x, 0, 'a move after Escape no longer pans');
});

test('destroy in the middle of a middle-button pan restores the viewBox and leaves no listener behind', async () => {
    const c = await canvas();
    c.svg.dispatch('pointerdown', ev(c.svg, { button: 1, buttons: 4, clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointermove', ev(c.svg, { button: -1, buttons: 4, clientX: 240, clientY: 300 }));
    engine.destroy(c.svg);

    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');
    assert.equal(c.svg.total(), 0);
    assert.equal(doc.total(), 0);
    assert.deepEqual(c.svg.released, [1]);
});

test('cancelling a move after destroy does not call .NET (OnDragStarted([]) is skipped) and a rejected call is swallowed', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    c.svg.captured.clear();
    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 160, clientY: 60 }));
    const inst = c.inst();

    // A live cancel resets the .NET start snapshot, and a rejecting promise is not an unhandled rejection.
    inst.dotNetRef = { invokeMethodAsync: (...args) => { c.ref.calls.push(args); return args[0] === 'OnDragStarted' ? Promise.reject(new Error('disposed')) : Promise.resolve(); } };
    press(c, 'Escape');
    assert.deepEqual(c.ref.named('OnDragStarted').at(-1), ['OnDragStarted', []]);

    // After destroy: nothing is sent (the reference is gone).
    const before = c.ref.calls.length;
    inst.destroyed = true;
    inst.body = { kind: 'element', snapshots: { a: translateOf(a.g) } };
    engine._bodyCancel(inst, { reason: 'detached' });
    assert.equal(c.ref.calls.length, before);
    inst.destroyed = false;
});

test('Shift+F10 opens the element menu at the selected element, with no selection the canvas menu at the svg centre; the ContextMenu key too', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    a.g.getBoundingClientRect = () => ({ left: 40, top: 40, width: 100, height: 60 });
    engine.setSelection(c.svg, ['a']);

    let prevented = false;
    press(c, 'F10', { shiftKey: true, preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.deepEqual(c.ref.named('OnElementContextMenu'), [['OnElementContextMenu', 'a', 90, 70]]);

    engine.setSelection(c.svg, []);
    press(c, 'ContextMenu');
    const canvasMenu = c.ref.named('OnCanvasContextMenu');
    assert.equal(canvasMenu.length, 1);
    assert.deepEqual(canvasMenu[0].slice(3), [400, 300], 'screen position = the svg centre');
});

test('Shift+F10 inside an input keeps the native behaviour', async () => {
    const c = await canvas();
    press(c, 'F10', { shiftKey: true, target: { tagName: 'INPUT', closest: () => null, matches: () => true } });
    assert.equal(c.ref.calls.length, 0);
});

test('the menu focuses its first enabled item, Arrow Up/Down rove, Escape closes and returns the focus to the canvas wrap', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const wrap = c.wrap;
    c.svg.closest = (selector) => (selector === '.tm-wd-canvas-wrap' ? wrap : matches(c.svg, selector) ? c.svg : null);
    const menu = node('div', { class: 'tm-wd-editor__context-menu', role: 'menu' });
    const disabled = node('button', { role: 'menuitem' }, menu); disabled.disabled = true;
    const first = node('button', { role: 'menuitem' }, menu);
    const second = node('button', { role: 'menuitem' }, menu);
    for (const item of [disabled, first, second]) item.focus = () => { doc.activeElement = item; };
    menu.style = { left: '10px', top: '10px' };
    globalThis.window.innerWidth = 1000; globalThis.window.innerHeight = 800;
    menu.getBoundingClientRect = () => ({ width: 100, height: 100 });
    const arrow = (name) => { const e = { key: name, preventDefault() {}, stopPropagation() {} }; menu.dispatch('keydown', e); return e; };

    engine.openContextMenu(c.svg, menu);
    mock.timers.tick(0);
    assert.equal(doc.activeElement, first, 'the first NON-disabled item takes the focus');

    arrow('ArrowDown');
    assert.equal(doc.activeElement, second);
    arrow('ArrowDown');
    assert.equal(doc.activeElement, first, 'wraps (the disabled item is skipped)');
    arrow('ArrowUp');
    assert.equal(doc.activeElement, second);

    doc.dispatch('keydown', { key: 'Escape' });
    assert.equal(c.ref.named('CloseContextMenu').length, 1, 'Escape asks .NET to close');
    // .NET closes the menu and calls closeContextMenu; the focus goes back to the wrap
    engine.closeContextMenu(c.svg);
    assert.equal(wrap.focused, true);
});

test('a press outside the menu closes it without stealing the focus back', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const wrap = c.wrap;
    c.svg.closest = (selector) => (selector === '.tm-wd-canvas-wrap' ? wrap : matches(c.svg, selector) ? c.svg : null);
    const menu = node('div', { class: 'tm-wd-editor__context-menu' });
    const item = node('button', { role: 'menuitem' }, menu);
    item.focus = () => { doc.activeElement = item; };
    menu.style = { left: '0px', top: '0px' };
    globalThis.window.innerWidth = 1000; globalThis.window.innerHeight = 800;
    menu.getBoundingClientRect = () => ({ width: 10, height: 10 });
    engine.openContextMenu(c.svg, menu);
    mock.timers.tick(0);

    doc.dispatch('pointerdown', { button: 0, target: { closest: () => null } });
    assert.equal(c.ref.named('CloseContextMenu').length, 1);
    engine.closeContextMenu(c.svg);
    assert.notEqual(wrap.focused, true);
});

test('a move cancelled by Escape, pointercancel or a second finger announces OnGestureCancelled; a normal end and a destroy do not', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    // normal end: silent
    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 100, clientY: 60 }));
    a.body.dispatch('pointerup', ev(a.body, { clientX: 100, clientY: 60 }));
    assert.equal(c.ref.named('OnGestureCancelled').length, 0, 'a normal end is not a rollback');

    // Escape
    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 140, clientY: 60 }));
    press(c, 'Escape');
    assert.equal(c.ref.named('OnGestureCancelled').length, 1);

    // pointercancel
    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 140, clientY: 60 }));
    c.svg.dispatch('pointercancel', ev(c.svg, { clientX: 140, clientY: 60 }));
    assert.equal(c.ref.named('OnGestureCancelled').length, 2);

    // destroy mid-drag: the roll-back is silent
    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 140, clientY: 60 }));
    engine.destroy(c.svg);
    assert.equal(c.ref.named('OnGestureCancelled').length, 2, 'destroy does not announce');
});

test('a resize rolled back by pointercancel announces it, the read-only flip does not', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');

    handle.dispatch('pointerdown', ev(handle, { clientX: 140, clientY: 100 }));
    c.svg.dispatch('pointermove', ev(c.svg, { clientX: 200, clientY: 160 }));
    c.svg.dispatch('pointercancel', ev(c.svg, { clientX: 200, clientY: 160 }));
    assert.equal(c.ref.named('OnGestureCancelled').length, 1);

    const handle2 = c.svg.querySelector('[data-handle="se"]');
    handle2.dispatch('pointerdown', ev(handle2, { clientX: 140, clientY: 100 }));
    c.svg.dispatch('pointermove', ev(c.svg, { clientX: 200, clientY: 160 }));
    engine.setReadOnly(c.svg, true);
    assert.equal(c.ref.named('OnGestureCancelled').length, 1, 'read-only flip is not a user cancel');
});

test('the long-press cue class and ring live exactly while the hold timer is pending', async () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    const cue = () => c.svg.classList.contains('tm-wd-canvas__svg--holding');
    const ring = () => c.svg.querySelectorAll('.tm-wd-hold-cue').length;

    // pending -> cue, lifted before 500 ms -> gone
    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(cue(), true);
    assert.equal(ring(), 1);
    mock.timers.tick(300);
    assert.equal(cue(), true, 'still pending at 300 ms');
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(cue(), false);
    assert.equal(ring(), 0);

    // pending -> the hold fires, the menu replaces the cue
    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(cue(), true);
    mock.timers.tick(500);
    assert.equal(c.ref.named('OnElementContextMenu').length, 1);
    assert.equal(cue(), false, 'removed when the menu opens');
    assert.equal(ring(), 0);
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    // pending -> the finger drifts beyond the tolerance (a scroll / drag) -> gone
    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(cue(), true);
    a.body.dispatch('pointermove', touch(a.body, { clientX: 120, clientY: 60 }));
    assert.equal(cue(), false, 'removed once the finger moves beyond the threshold');
    a.body.dispatch('pointerup', touch(a.body, { clientX: 120, clientY: 60 }));

    // pending -> pointercancel -> gone ; a mouse never shows the cue
    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    c.svg.dispatch('pointercancel', touch(c.svg, { clientX: 60, clientY: 60 }));
    assert.equal(cue(), false);
    a.body.dispatch('pointerdown', ev(a.body, { clientX: 60, clientY: 60 }));
    assert.equal(cue(), false, 'a mouse has no hold');
});

// ── relayout (container resize without a Blazor render) ───────────────────────────────────────

function resizeSvg(svg, width, height) { svg.getBoundingClientRect = () => ({ left: 0, top: 0, width, height }); }

test('relayout re-fits a view the user has not touched and reports viewBox + zoom to .NET', async () => {
    const c = await canvas();
    resizeSvg(c.svg, 400, 300);
    const scale = engine.relayout(c.svg);

    assert.equal(scale, 0.5);
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');
    assert.deepEqual(c.ref.named('OnZoomChanged'), [['OnZoomChanged', 0.5]]);
    assert.equal(c.ref.named('OnViewBoxChanged').length, 1);
    assert.equal(engine.relayout(c.svg), 0, 'an unchanged size is a no-op');
    assert.equal(c.ref.named('OnZoomChanged').length, 1);
});

test('relayout keeps the centre and the zoom of a view the user moved', async () => {
    const c = await canvas();
    engine.zoomTo(c.svg, 2);                 // 400x300 around the centre (400, 300)
    assert.equal(c.inst().userViewChanged, true);
    resizeSvg(c.svg, 400, 300);              // the panel opened: the svg is half as wide

    const scale = engine.relayout(c.svg);

    assert.equal(scale, 2, 'zoom is unchanged');
    const vb = c.svg.viewBox.baseVal;
    assert.equal(vb.width, 200);
    assert.equal(vb.height, 150);
    assert.equal(vb.x + vb.width / 2, 400, 'the centre stays');
    assert.equal(vb.y + vb.height / 2, 300);
});

test('relayout is a no-op while the svg has no size (hidden) and after destroy', async () => {
    const c = await canvas();
    resizeSvg(c.svg, 0, 0);
    assert.equal(engine.relayout(c.svg), 0);
    assert.equal(c.ref.named('OnZoomChanged').length, 0);
    engine.destroy(c.svg);
    assert.equal(engine.relayout(c.svg), 0);
});

test('a ResizeObserver triggers one relayout per frame and is disconnected on destroy; no ResizeObserver is fine', async () => {
    const observers = [];
    globalThis.ResizeObserver = class {
        constructor(fn) { this.fn = fn; this.observed = []; this.disconnected = false; observers.push(this); }
        observe(el) { this.observed.push(el); }
        disconnect() { this.disconnected = true; }
    };
    try {
        const c = await canvas();
        assert.equal(observers.length, 1);
        assert.ok(observers[0].observed.includes(c.svg));
        assert.ok(observers[0].observed.includes(c.wrap));

        resizeSvg(c.svg, 400, 300);
        observers[0].fn([]); observers[0].fn([]);
        assert.equal(c.ref.named('OnZoomChanged').length, 1, 'rAF-debounced + unchanged size dedupes');

        engine.destroy(c.svg);
        assert.equal(observers[0].disconnected, true);
    } finally {
        delete globalThis.ResizeObserver;
    }
    const c2 = await canvas();     // no ResizeObserver in this environment
    assert.equal(c2.inst().resizeObserver, null);
});

// ── InitialZoom (decision WIREFRAME-INITIAL-ZOOM): applyInitialZoom fits the page (padding 40) but never enlarges ──────────────

const near = (actual, expected, message) => assert.ok(Math.abs(actual - expected) < 1e-6, `${message ?? ''} expected ${expected}, got ${actual}`);

test('applyInitialZoom fit: a page larger than the canvas is fitted with a 40 px padding and the real scale is returned', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    resizeSvg(c.svg, 720, 450);

    const scale = engine.applyInitialZoom(c.svg);

    // limiting axis: min(720 / 1520, 450 / 980) = 0.4592...
    const expected = Math.min(720 / 1520, 450 / 980);
    near(scale, expected, 'scale');
    const vb = c.svg.viewBox.baseVal;
    near(vb.width * scale, 720, 'the viewBox spans the svg width at that scale');
    near(vb.height * scale, 450, 'and its height');
    near(vb.y, -40, 'the limiting axis keeps the padding');
    assert.equal(c.inst().userViewChanged, false, 'the fitted view is pristine: a container resize re-fits it');
    assert.equal(c.inst().scale, scale);
    assert.equal(c.ref.named('OnViewBoxChanged').length, 1, 'rulers / minimap learn the new view');
});

test('applyInitialZoom fit never enlarges: a small page stays at 100 % and is centred', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 390, canvasHeight: 844 });
    resizeSvg(c.svg, 1000, 900);

    const scale = engine.applyInitialZoom(c.svg);

    assert.equal(scale, 1, 'no zoom beyond 100 %');
    const vb = c.svg.viewBox.baseVal;
    near(vb.width, 1000);
    near(vb.height, 900);
    near(vb.x, (390 - 1000) / 2, 'centred horizontally');
    near(vb.y, (844 - 900) / 2, 'centred vertically');
});

test('applyInitialZoom actual: 100 %, the page origin padded when the page is larger than the canvas, centred when it is smaller', async () => {
    const big = await canvas({ initialZoom: 'actual', canvasWidth: 1440, canvasHeight: 900 });
    resizeSvg(big.svg, 600, 400);
    assert.equal(engine.applyInitialZoom(big.svg), 1);
    let vb = big.svg.viewBox.baseVal;
    near(vb.width, 600); near(vb.height, 400);
    near(vb.x, -40, 'the page starts at the padded origin'); near(vb.y, -40);

    const small = await canvas({ initialZoom: 'actual', canvasWidth: 300, canvasHeight: 200 });
    resizeSvg(small.svg, 600, 400);
    assert.equal(engine.applyInitialZoom(small.svg), 1);
    vb = small.svg.viewBox.baseVal;
    near(vb.x, (300 - 600) / 2); near(vb.y, (200 - 400) / 2);
});

test('applyInitialZoom takes the mode from its argument and remembers it', async () => {
    const c = await canvas({ canvasWidth: 1440, canvasHeight: 900 });      // no initialZoom option: the legacy meet fit
    resizeSvg(c.svg, 720, 450);

    assert.equal(engine.applyInitialZoom(c.svg, 'actual'), 1);
    assert.equal(c.inst().initialZoom, 'actual');
    near(engine.applyInitialZoom(c.svg, 'fit'), Math.min(720 / 1520, 450 / 980));
    assert.equal(c.inst().initialZoom, 'fit');
    assert.equal(engine.applyInitialZoom(c.svg, 'nonsense') > 0, true, 'an unknown mode keeps the previous one');
    assert.equal(c.inst().initialZoom, 'fit');
});

test('applyInitialZoom without a size (a hidden tab) changes nothing and reports 0; the first real size then lays the view out', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    resizeSvg(c.svg, 0, 0);
    assert.equal(engine.applyInitialZoom(c.svg), 0);
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600', 'the viewBox is untouched');

    resizeSvg(c.svg, 720, 450);
    const scale = engine.relayout(c.svg);
    near(scale, Math.min(720 / 1520, 450 / 980), 'the relayout uses the initial mode');
});

test('applyInitialZoom without a size for a NEW subject drops the old user zoom and measured baseline, so the first real size lays it out', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    resizeSvg(c.svg, 720, 450);
    engine.applyInitialZoom(c.svg);
    engine.zoomTo(c.svg, 2);                           // the user zoomed the previous page
    assert.equal(c.inst().userViewChanged, true);

    resizeSvg(c.svg, 0, 0);                            // the next page is switched to while the canvas is hidden
    assert.equal(engine.applyInitialZoom(c.svg), 0);
    assert.equal(c.inst().userViewChanged, false, 'the new subject starts pristine');
    assert.equal(c.inst().layoutRect, null, 'and without a measured baseline');

    resizeSvg(c.svg, 720, 450);
    near(engine.relayout(c.svg), Math.min(720 / 1520, 450 / 980), 'the first real size lays the new subject out (not the old zoom)');
});

test('relayout of a pristine view re-applies the initial mode; a view the user zoomed keeps its zoom (no automatic overwrite)', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    resizeSvg(c.svg, 720, 450);
    engine.applyInitialZoom(c.svg);
    resizeSvg(c.svg, 1400, 880);                       // the panel closed: the canvas grew
    near(engine.relayout(c.svg), Math.min(1400 / 1520, 880 / 980), 'pristine: refitted with the padding');
    resizeSvg(c.svg, 2000, 1200);                      // a huge canvas: the whole page fits, so it stays at 100 %, never enlarged
    assert.equal(engine.relayout(c.svg), 1, 'never above 100 %');

    engine.zoomTo(c.svg, 2);                           // the user zooms
    resizeSvg(c.svg, 720, 450);
    assert.equal(engine.relayout(c.svg), 2, 'the user zoom survives a resize');
    assert.equal(engine.applyInitialZoom(c.svg) <= 1, true, 'only an explicit apply (document / page change) resets it');
});

test('legacy canvas (no initialZoom option) keeps the plain page meet-fit on relayout', async () => {
    const c = await canvas();
    resizeSvg(c.svg, 400, 300);
    assert.equal(engine.relayout(c.svg), 0.5);
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');
});
test('keys typed inside a side sheet (the tablet drawer / inspector) never act on the canvas selection', async () => {
    const c = await canvas({ gridSize: 8 });
    element(c.svg, 'a', { x: 40, y: 40 });
    engine.setSelection(c.svg, ['a']);
    const inSheet = { tagName: 'BUTTON', closest: (selector) => (selector.includes('.tm-editor-shell__overlay') ? {} : null), matches: () => false };

    key(c, 'ArrowRight', { target: inSheet });
    key(c, 'Delete', { target: inSheet });
    key(c, 'Escape', { target: inSheet });

    assert.equal(c.ref.named('OnElementMoved').length, 0, 'no nudge');
    assert.equal(c.ref.named('OnDeleteSelected').length, 0, 'no delete');
    assert.equal(c.ref.named('OnClearSelection').length, 0, 'the sheet owns Escape (it closes itself)');
});

test('keys typed inside the MOBILE sheet never act on the canvas: Escape reaches the sheet\'s own focus trap un-prevented, so the sheet closes', async () => {
    const c = await canvas({ gridSize: 8 });
    element(c.svg, 'a', { x: 40, y: 40 });
    engine.setSelection(c.svg, ['a']);
    const inSheet = { tagName: 'BUTTON', closest: (selector) => (selector.includes('.tm-editor-shell__sheet') ? {} : null), matches: () => false };

    const escape = key(c, 'Escape', { target: inSheet });
    key(c, 'Delete', { target: inSheet });

    assert.equal(escape.defaultPrevented, undefined, 'the canvas must not preventDefault an Escape typed in the sheet (the focus trap skips a defaultPrevented key)');
    assert.equal(c.ref.named('OnClearSelection').length, 0, 'and must not clear the selection behind the sheet');
    assert.equal(c.ref.named('OnDeleteSelected').length, 0);
});

test('relayout waits while a gesture is active (the sheet opens under a press) and runs when it is released', async () => {
    const c = await canvas();
    c.inst().isDragging = true;               // a press selected the element and the inspector sheet narrowed the canvas under the finger
    resizeSvg(c.svg, 400, 300);

    assert.equal(engine.relayout(c.svg), 0, 'no re-fit mid-gesture');
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600', 'the viewBox is untouched while the finger is down');

    c.inst().isDragging = false;
    engine._flushRelayout(c.inst());

    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');
    assert.deepEqual(c.ref.named('OnZoomChanged'), [['OnZoomChanged', 0.5]], 'the postponed relayout ran once');
});

test('the svg carries data-wd-gesture exactly while a body gesture runs (CSS defers the sheet reservation until the finger is lifted)', async () => {
    const c = await canvas();
    element(c.svg, 'a', { x: 40, y: 40 });
    const g = c.svg.querySelector('[data-el-id="a"]');

    c.svg.dispatch('pointerdown', { pointerId: 1, pointerType: 'touch', button: 0, isPrimary: true, clientX: 50, clientY: 50, target: g, preventDefault() {} });
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), true, 'set from the press');

    c.svg.dispatch('pointerup', { pointerId: 1, pointerType: 'touch', button: 0, isPrimary: true, clientX: 50, clientY: 50, target: g, preventDefault() {} });
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), false, 'removed on every exit');
});


test('a relayout deferred by a pinch runs when the pinch ends, cancels, or a middle-button pan ends/cancels', async () => {
    // pinch end
    let c = await canvas();
    let { f1, f2 } = twoFingers(c);
    resizeSvg(c.svg, 400, 300);
    assert.equal(engine.relayout(c.svg), 0, 'deferred while the pinch owns the viewBox');
    assert.equal(c.inst().relayoutDeferred, true);
    c.svg.dispatch('pointerup', f2({ clientX: 400, clientY: 300 }));
    assert.equal(c.inst().relayoutDeferred, false, 'flushed at the end of the pinch');
    assert.deepEqual(c.inst().layoutRect, { width: 400, height: 300 });
    const vb = c.svg.viewBox.baseVal;
    assert.ok(Math.abs(vb.width - 400) < 1e-6 && Math.abs(vb.height - 300) < 1e-6, 'the zoom is kept (1), so the half-size svg shows half the units');

    // pinch cancel (Escape)
    c = await canvas();
    ({ f1, f2 } = twoFingers(c));
    resizeSvg(c.svg, 400, 300);
    engine.relayout(c.svg);
    assert.equal(c.inst().relayoutDeferred, true);
    c.svg.dispatch('pointercancel', f2({ clientX: 400, clientY: 300 }));
    assert.equal(c.inst().relayoutDeferred, false, 'flushed after the pinch rolled back');
    assert.deepEqual(c.inst().layoutRect, { width: 400, height: 300 });

    // middle pan end
    c = await canvas();
    c.svg.dispatch('pointerdown', ev(c.svg, { button: 1, buttons: 4, clientX: 300, clientY: 300 }));
    resizeSvg(c.svg, 400, 300);
    engine.relayout(c.svg);
    assert.equal(c.inst().relayoutDeferred, true);
    c.svg.dispatch('pointerup', ev(c.svg, { button: 1, buttons: 0, clientX: 300, clientY: 300 }));
    assert.equal(c.inst().relayoutDeferred, false, 'flushed after the middle pan');
    assert.deepEqual(c.inst().layoutRect, { width: 400, height: 300 });

    // middle pan cancel
    c = await canvas();
    c.svg.dispatch('pointerdown', ev(c.svg, { button: 1, buttons: 4, clientX: 300, clientY: 300 }));
    resizeSvg(c.svg, 400, 300);
    engine.relayout(c.svg);
    c.svg.dispatch('pointercancel', ev(c.svg, { button: 1, buttons: 0, clientX: 300, clientY: 300 }));
    assert.equal(c.inst().relayoutDeferred, false, 'flushed after a cancelled middle pan');
    assert.deepEqual(c.inst().layoutRect, { width: 400, height: 300 });
});

test('the svg carries data-wd-gesture while a resize handle gesture runs too', async () => {
    const c = await canvas();
    element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');

    handle.dispatch('pointerdown', touch(handle, { clientX: 140, clientY: 100 }));
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), true, 'set from the handle press');
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 200, clientY: 140 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 200, clientY: 140 }));
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), false, 'removed on release');
});

// Touch_ResizeHandle(1024,768) root cause: with the inspector sheet ALREADY open the canvas region reserves the sheet's width; the
// gesture marker must not make CSS drop that reservation (the svg would widen under the finger and the drag would run in a shifted
// coordinate system). The script records whether the reservation was in effect at the press.
function reserveRegion(c, reserved) {
    const region = node('div', { class: 'tm-wd-editor__canvas-region' });
    const wrap = c.svg.parent;
    region.children.push(wrap); wrap.parent = region;
    // Emulates the stylesheet: the region reserves 300px unless a gesture marker WITHOUT data-wd-reserved sits on the svg.
    globalThis.getComputedStyle = el => {
        const dropped = c.svg.hasAttribute('data-wd-gesture') && !c.svg.hasAttribute('data-wd-reserved');
        return { paddingInlineEnd: el === region && reserved && !dropped ? '300px' : '0px' };
    };
    return region;
}

test('a gesture started while the sheet reservation is already in effect marks it data-wd-reserved, so CSS keeps the canvas width', async () => {
    const c = await canvas();
    reserveRegion(c, true);
    element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');

    handle.dispatch('pointerdown', touch(handle, { clientX: 140, clientY: 100 }));
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), true);
    assert.equal(c.svg.hasAttribute('data-wd-reserved'), true, 'the reservation was in effect at the press');
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 200, clientY: 140 }));
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), false);
    assert.equal(c.svg.hasAttribute('data-wd-reserved'), false, 'both markers are removed on release');
    delete globalThis.getComputedStyle;
});

test('a body press that opens the sheet (no reservation yet) is NOT marked reserved: CSS defers the reservation until the lift', async () => {
    const c = await canvas();
    reserveRegion(c, false);
    element(c.svg, 'a', { x: 40, y: 40 });
    const g = c.svg.querySelector('[data-el-id="a"]');

    c.svg.dispatch('pointerdown', { pointerId: 1, pointerType: 'touch', button: 0, isPrimary: true, clientX: 50, clientY: 50, target: g, preventDefault() {} });
    assert.equal(c.svg.hasAttribute('data-wd-gesture'), true);
    assert.equal(c.svg.hasAttribute('data-wd-reserved'), false);
    c.svg.dispatch('pointerup', { pointerId: 1, pointerType: 'touch', button: 0, isPrimary: true, clientX: 50, clientY: 50, target: g, preventDefault() {} });
    delete globalThis.getComputedStyle;
});

test('a body drag started with the reservation in effect is marked reserved too (an element drag beside an open inspector)', async () => {
    const c = await canvas();
    reserveRegion(c, true);
    element(c.svg, 'a', { x: 40, y: 40 });
    const g = c.svg.querySelector('[data-el-id="a"]');

    c.svg.dispatch('pointerdown', { pointerId: 1, pointerType: 'touch', button: 0, isPrimary: true, clientX: 50, clientY: 50, target: g, preventDefault() {} });
    assert.equal(c.svg.hasAttribute('data-wd-reserved'), true);
    c.svg.dispatch('pointerup', { pointerId: 1, pointerType: 'touch', button: 0, isPrimary: true, clientX: 50, clientY: 50, target: g, preventDefault() {} });
    assert.equal(c.svg.hasAttribute('data-wd-reserved'), false);
    delete globalThis.getComputedStyle;
});

test('a handle press counts as a gesture BEFORE the drag threshold: a relayout in that window is deferred, then flushed on release', async () => {
    const c = await canvas();
    element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');

    handle.dispatch('pointerdown', touch(handle, { clientX: 140, clientY: 100 }));     // pressed, threshold not crossed: isResizing is still false
    assert.equal(c.inst().isResizing, false);
    resizeSvg(c.svg, 400, 300);
    assert.equal(engine.relayout(c.svg), 0, 'deferred from the press, not only from the first move');
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600');

    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 140, clientY: 100 }));
    assert.equal(c.inst().relayoutDeferred, false, 'flushed on release');
    assert.deepEqual(c.inst().layoutRect, { width: 400, height: 300 });
});
test('entering light editing with the connector tool active falls back to select AND tells .NET (the tool row must not show a stale tool)', async () => {
    const c = await canvas();
    engine.setToolMode(c.svg, 'connector');

    engine.setLightEditing(c.svg, true);

    assert.equal(c.inst().toolMode, 'select');
    assert.deepEqual(c.ref.named('OnToolModeChanged'), [['OnToolModeChanged', 'select']]);
});

test('entering light editing with the select tool active reports nothing', async () => {
    const c = await canvas();
    engine.setLightEditing(c.svg, true);
    assert.equal(c.ref.named('OnToolModeChanged').length, 0);
});
// ── mobile viewing + light edits (wireframe redesign plan ce19aa8c, phase 7, decision G2) ────────

test('light editing: a selected element has its outline but NO resize / rotate grips, also on a coarse pointer', async () => {
    const c = await canvas({ lightEditing: true, coarse: true });
    element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });

    engine.setSelection(c.svg, ['a']);

    assert.equal(c.svg.querySelectorAll('.tm-wd-selection').length, 1, 'the dashed outline stays');
    assert.equal(c.svg.querySelectorAll('[data-handle]').length, 0, 'no resize handle and no hit rect');
    assert.equal(c.svg.querySelectorAll('[data-rotate-handle]').length, 0, 'no rotate handle');
});

test('light editing: a finger still selects and drags an element (one OnElementMoved at the end)', async () => {
    const c = await canvas({ lightEditing: true });
    const a = element(c.svg, 'a', { x: 40, y: 40 });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', touch(a.body, { clientX: 100, clientY: 90 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 100, clientY: 90 }));

    assert.deepEqual(c.ref.named('OnSelectionChanged'), [['OnSelectionChanged', ['a']]]);
    assert.deepEqual(c.ref.named('OnElementMoved'), [['OnElementMoved', 'a', 80, 72]]);
});

test('light editing: a one-finger drag on the EMPTY canvas pans (no rubber band) and commits the viewBox once', async () => {
    const c = await canvas({ lightEditing: true });
    element(c.svg, 'a', { x: 400, y: 400, w: 40, h: 40 });

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 300, clientY: 300 }));
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 250, clientY: 280 }));
    assert.ok(!c.svg.children.some(n => n.classList.contains('tm-wd-rubber')), 'no rubber band is drawn');
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 250, clientY: 280 }));

    assert.deepEqual(c.ref.named('OnViewBoxChanged'), [['OnViewBoxChanged', 50, 20, 800, 600]]);
    assert.equal(c.ref.named('OnMultiSelect').length, 0);
});

test('light editing: a TAP on the empty canvas still clears the selection', async () => {
    const c = await canvas({ lightEditing: true });
    element(c.svg, 'a', { x: 20, y: 20 });
    engine.setSelection(c.svg, ['a']);

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 700, clientY: 500 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 700, clientY: 500 }));

    assert.deepEqual(c.ref.named('OnSelectionChanged'), [['OnSelectionChanged', []]]);
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0, 'a tap is no pan');
});

test('light editing: a DRAG on the empty canvas keeps the selection (it only pans)', async () => {
    const c = await canvas({ lightEditing: true });
    element(c.svg, 'a', { x: 20, y: 20 });
    engine.setSelection(c.svg, ['a']);

    c.svg.dispatch('pointerdown', touch(c.svg, { clientX: 700, clientY: 500 }));
    c.svg.dispatch('pointermove', touch(c.svg, { clientX: 650, clientY: 450 }));
    c.svg.dispatch('pointerup', touch(c.svg, { clientX: 650, clientY: 450 }));

    assert.equal(c.ref.named('OnSelectionChanged').length, 0, 'panning does not deselect');
    assert.equal(c.ref.named('OnViewBoxChanged').length, 1);
});

test('light editing: the connector tool draws nothing (edge drawing is tablet+) and waypoints are not grabbed', async () => {
    const c = await canvas({ lightEditing: true });
    const a = element(c.svg, 'a', { x: 0, y: 0 });
    const b = element(c.svg, 'b', { x: 300, y: 0 });
    engine.setToolMode(c.svg, 'connector');
    doc.elementFromPoint = () => b.body;

    a.body.dispatch('pointerdown', ev(a.body, { clientX: 20, clientY: 20 }));
    a.body.dispatch('pointermove', ev(a.body, { clientX: 320, clientY: 20 }));
    assert.ok(!c.svg.children.some(n => n.classList.contains('tm-wd-connector-preview')), 'no preview line');
    a.body.dispatch('pointerup', ev(a.body, { clientX: 320, clientY: 20 }));

    assert.equal(c.ref.named('OnConnectorCreated').length, 0);

    const conn = node('g', { 'data-connector-id': 'c1' }, c.svg);
    const wp = node('circle', { class: 'tm-wd-connector__waypoint', cx: 100, cy: 100, 'data-waypoint-index': 2 }, conn);
    wp.dispatch('pointerdown', touch(wp, { clientX: 100, clientY: 100 }));
    wp.dispatch('pointermove', touch(wp, { clientX: 140, clientY: 130 }));
    wp.dispatch('pointerup', touch(wp, { clientX: 140, clientY: 130 }));
    assert.equal(c.ref.named('OnConnectorWaypointDragged').length, 0);
});

test('light editing: the pan tool and the long-press menu keep working', async () => {
    const c = await canvas({ lightEditing: true });
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    mock.timers.enable({ apis: ['setTimeout', 'Date'] });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(600);
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    assert.equal(c.ref.named('OnElementContextMenu').length, 1, 'the hold opens the element menu');
});

test('setLightEditing(true) rolls a resize in flight back and removes the grips; (false) brings them back', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40, w: 100, h: 60 });
    engine.setSelection(c.svg, ['a']);
    const handle = c.svg.querySelector('[data-handle="se"]');
    handle.dispatch('pointerdown', ev(handle, { clientX: 140, clientY: 100 }));
    c.svg.dispatch('pointermove', ev(c.svg, { clientX: 200, clientY: 160 }));
    assert.equal(a.g.getAttribute('data-w'), '160');

    engine.setLightEditing(c.svg, true);
    c.svg.dispatch('pointerup', ev(c.svg, { clientX: 200, clientY: 160 }));

    assert.equal(a.g.getAttribute('data-w'), '100', 'the live resize is rolled back');
    assert.equal(c.ref.named('OnElementResized').length, 0, 'and nothing is committed');
    assert.equal(c.svg.querySelectorAll('[data-handle]').length, 0);

    engine.setLightEditing(c.svg, false);
    assert.equal(c.svg.querySelectorAll('[data-handle]').length, 8, 'the eight resize handles return');
});

test('setLightEditing(true) leaves the connector tool for the select tool', async () => {
    const c = await canvas();
    engine.setToolMode(c.svg, 'connector');

    engine.setLightEditing(c.svg, true);

    assert.equal(c.inst().toolMode, 'select');
});

test('light editing: a TAP on an already selected element re-announces the selection (the properties sheet comes back); a drag does not', async () => {
    const c = await canvas({ lightEditing: true });
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    engine.setSelection(c.svg, ['a']);

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));
    assert.deepEqual(c.ref.named('OnSelectionChanged'), [['OnSelectionChanged', ['a']]], 'the tap re-announces');

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointermove', touch(a.body, { clientX: 100, clientY: 90 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 100, clientY: 90 }));
    assert.equal(c.ref.named('OnSelectionChanged').length, 1, 'a drag does not re-announce');
    assert.equal(c.ref.named('OnElementMoved').length, 1);
});

test('without light editing a tap on an already selected element announces nothing (tablet / desktop unchanged)', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    engine.setSelection(c.svg, ['a']);

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    assert.equal(c.ref.named('OnSelectionChanged').length, 0);
});

test('light editing: the compatibility click of the finger that opened the long-press sheet is swallowed once; a new press disarms the guard', async () => {
    const c = await canvas({ lightEditing: true });
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    mock.timers.enable({ apis: ['setTimeout', 'Date'] });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(600);
    assert.equal(c.ref.named('OnElementContextMenu').length, 1);
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    const click = () => doc.dispatch('click', { target: a.body, preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } });
    const first = click();
    assert.ok(first.prevented && first.stopped, 'the lift click is swallowed');
    const second = click();
    assert.ok(!second.prevented && !second.stopped, 'only the first click is swallowed');

    // A hold that opened a menu, then a NEW press: the next click is the user\'s own.
    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(600);
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));
    doc.dispatch('pointerdown', { target: a.body });
    const third = click();
    assert.ok(!third.prevented, 'a new press disarms the guard');
});

test('without light editing the hold arms no release-click guard (the tablet menu is offset from the finger)', async () => {
    const c = await canvas();
    const a = element(c.svg, 'a', { x: 40, y: 40 });
    mock.timers.enable({ apis: ['setTimeout', 'Date'] });

    a.body.dispatch('pointerdown', touch(a.body, { clientX: 60, clientY: 60 }));
    mock.timers.tick(600);
    a.body.dispatch('pointerup', touch(a.body, { clientX: 60, clientY: 60 }));

    const e = doc.dispatch('click', { target: a.body, preventDefault() { this.prevented = true; }, stopPropagation() {} });
    assert.ok(!e.prevented);
});
