// workflow-designer.js on the shared helpers (F23). The real workflow-designer.js, pointer-drag.js
// and pointer-gesture.js run against a hand-rolled SVG DOM: a mouse and a finger drag a state,
// a port drag creates a transition, the empty canvas pans, two fingers pinch-zoom, Alt+arrows and
// Delete reach .NET through the keyboard path, and every cancel path (Escape, pointercancel,
// destroy) restores the start geometry VERBATIM and commits nothing.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const selfUrl = pathToFileURL(path.resolve(here, '../workflow-designer.js')).href;

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

function attrMatch(el, part) {
    const m = part.trim().match(/^\[([a-zA-Z-]+)(?:="([^"]*)")?\]$/);
    if (!m || !el.attrs) return false;
    const value = el.attrs.get(m[1]);
    return m[2] === undefined ? value !== undefined && value !== null : value === m[2];
}

function svgNode({ ctmScale = 1, rect = { left: 0, top: 0, width: 800, height: 500 } } = {}) {
    const children = [];
    const svg = {
        ...bag(),
        attrs: new Map(), style: {}, children,
        captured: new Set(), released: [],
        classList: { add() {}, remove() {}, contains: () => false },
        parent: null,
        get parentNode() { return svg.parent; },
        setAttribute(name, value) {
            svg.attrs.set(name, String(value));
            if (name === 'viewBox') {
                const [x, y, w, h] = String(value).split(/\s+/).map(Number);
                Object.assign(svg.viewBox.baseVal, { x, y, width: w, height: h });
            }
        },
        getAttribute(name) { return svg.attrs.has(name) ? svg.attrs.get(name) : null; },
        removeAttribute(name) {
            svg.attrs.delete(name);
            if (name === 'viewBox') Object.assign(svg.viewBox.baseVal, { x: 0, y: 0, width: 800, height: 500 });
        },
        viewBox: { baseVal: { x: 0, y: 0, width: 800, height: 500 } },
        getBoundingClientRect: () => rect,
        get parentElement() { return svg.parent; },
        focus() { svg.focused = true; },
        setPointerCapture(id) { svg.captured.add(id); },
        releasePointerCapture(id) { svg.captured.delete(id); svg.released.push(id); },
        appendChild(child) { child.parent = svg; children.push(child); return child; },
        removeChild(child) { const i = children.indexOf(child); if (i >= 0) children.splice(i, 1); child.parent = null; },
        contains(el) { for (let n = el; n; n = n.parent) if (n === svg) return true; return false; },
        querySelector(selector) {
            const m = selector.match(/^\[data-state-id="([^"]+)"\]$/);
            return m ? (children.find(c => c.attrs.get('data-state-id') === m[1]) ?? null) : null;
        },
        querySelectorAll(selector) {
            return selector === '[data-state-id]' ? children.filter(c => c.attrs.has('data-state-id')) : [];
        },
        createSVGPoint: () => ({
            x: 0, y: 0,
            matrixTransform(m) { return { x: this.x * m.a + this.y * m.c + m.e, y: this.x * m.b + this.y * m.d + m.f }; },
        }),
        getScreenCTM: () => ({
            a: ctmScale, b: 0, c: 0, d: ctmScale, e: 0, f: 0,
            inverse() {
                const inv = { a: 1 / this.a, b: 0, c: 0, d: 1 / this.d, e: 0, f: 0 };
                inv.inverse = () => this;
                return inv;
            },
        }),
    };
    return svg;
}

function shapeNode(attrs, parent) {
    const n = {
        ...bag(),
        attrs: new Map(Object.entries(attrs).map(([k, v]) => [k, String(v)])),
        style: {}, parent, captured: new Set(), released: [],
        classList: { add() {}, remove() {}, contains: () => false },
        get parentNode() { return n.parent; },
        setAttribute(name, value) { n.attrs.set(name, String(value)); },
        getAttribute(name) { return n.attrs.has(name) ? n.attrs.get(name) : null; },
        removeAttribute(name) { n.attrs.delete(name); },
        setPointerCapture(id) { n.captured.add(id); },
        releasePointerCapture(id) { n.captured.delete(id); n.released.push(id); },
        closest(selector) {
            for (let p = n; p; p = p.parent) {
                if (selector.split(',').some(part => attrMatch(p, part))) return p;
            }
            return null;
        },
    };
    // Pointer events bubble: dispatching on a state/port reaches the sessions attached on the svg.
    const own = n.dispatch;
    n.dispatch = (type, event = {}) => {
        own(type, event);
        if (n.parent) n.parent.dispatch(type, event);
        return event;
    };
    return n;
}

/** An svg with two states; s2 carries the connection port. */
function designer({ ctmScale = 1 } = {}) {
    const svg = svgNode({ ctmScale });
    const s1 = shapeNode({ 'data-state-id': 's1', transform: 'translate(30, 200)' }, svg);
    const s2 = shapeNode({ 'data-state-id': 's2', transform: 'translate(250, 100)' }, svg);
    const port = shapeNode({ 'data-port': 's2' }, s2);
    svg.children.push(s1, s2);
    return { svg, s1, s2, port };
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
}

let doc;
let module_;

async function boot() {
    doc = {
        ...bag(),
        readyState: 'complete',
        baseURI: 'https://example.test/app/',
        currentScript: { src: selfUrl },
        contains: () => true,
        documentElement: { clientWidth: 800, clientHeight: 600 },
        scrollingElement: { scrollLeft: 0, scrollTop: 0 },
        elementFromPoint: () => null,
        createElementNS: () => shapeNode({}, null),
    };
    globalThis.document = doc;
    globalThis.window = {};
    globalThis.matchMedia = undefined;
    globalThis.__rafQueue = [];
    globalThis.requestAnimationFrame = (fn) => { globalThis.__rafQueue.push(fn); return globalThis.__rafQueue.length; };
    globalThis.cancelAnimationFrame = () => { globalThis.__rafQueue.length = 0; };
    await import(`${selfUrl}?${Math.random()}`);
    module_ = globalThis.window.tmWorkflowDesigner;
    return module_;
}

async function designerInit({ readOnly = false, gridSize = 20, ctmScale = 1 } = {}) {
    const g = designer({ ctmScale });
    const ref = dotnet();
    const id = module_.init(g.svg, ref, readOnly, { gridSize });
    await module_.instances.get(id).ready;   // the helper sessions attach asynchronously
    return { ...g, ref, id, inst: () => module_.instances.get(id) };
}

test.beforeEach(boot);
test.afterEach(() => {
    module_?.instances.forEach((_v, id) => module_.destroy({ id }));
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.matchMedia;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
    delete globalThis.__rafQueue;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...over,
});

// ── the old gesture is gone ─────────────────────────────────────────────────────────────────

test('no mousedown listener remains anywhere - the gesture is Pointer Events', async () => {
    const g = await designerInit();
    assert.equal(doc.count('mousedown'), 0);
    assert.equal(g.svg.count('mousedown'), 0);
    assert.ok(g.svg.count('pointerdown') >= 3, 'the port, node and pan sessions listen on the svg');
});

test('destroy during the helper import wins: no listener survives', async () => {
    const g = await designerInit();
    const inst = g.inst();
    module_.destroy(g.svg);
    await inst.ready;
    assert.equal(g.svg.total(), 0);
    assert.equal(doc.total(), 0);
});

// ── node drag ───────────────────────────────────────────────────────────────────────────────

test('a mouse drag on a state follows the pointer live and notifies OnNodeDragged once', async () => {
    const g = await designerInit();

    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 100, clientY: 250 }));
    assert.equal(g.s1.attrs.get('transform'), 'translate(90, 240)', 'the live preview moves the group');

    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 100, clientY: 250 }));
    assert.deepEqual(g.ref.calls, [['OnNodeDragged', 's1', 90, 240]]);
});

test('node drag coordinates are svg-scaled (zoomed canvas)', async () => {
    const g = await designerInit({ ctmScale: 2 });

    // A 2x CTM: 40 client px = 20 svg units; s1 starts at (30, 200).
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 80, clientY: 210 }));
    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 80, clientY: 210 }));

    assert.deepEqual(g.ref.calls, [['OnNodeDragged', 's1', 50, 200]]);
});

test('a FINGER drags a state the same way (touch did nothing before)', async () => {
    const g = await designerInit();

    g.s2.dispatch('pointerdown', ev(g.s2, { pointerType: 'touch', pointerId: 7, clientX: 260, clientY: 110 }));
    g.s2.dispatch('pointermove', ev(g.s2, { pointerType: 'touch', pointerId: 7, clientX: 300, clientY: 160 }));
    g.s2.dispatch('pointerup', ev(g.s2, { pointerType: 'touch', pointerId: 7, clientX: 300, clientY: 160 }));

    assert.deepEqual(g.ref.calls, [['OnNodeDragged', 's2', 290, 150]]);
});

test('a tap on a state does not notify - the click keeps selecting', async () => {
    const g = await designerInit();
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 41, clientY: 211 }));
    assert.equal(g.ref.calls.length, 0);
});

test('Escape restores the start transform VERBATIM and notifies nothing', async () => {
    const g = await designerInit();
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 140, clientY: 310 }));
    assert.notEqual(g.s1.attrs.get('transform'), 'translate(30, 200)');

    doc.dispatch('keydown', { key: 'Escape' });
    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 140, clientY: 310 }));

    assert.equal(g.s1.attrs.get('transform'), 'translate(30, 200)', 'the exact Blazor-rendered transform is back');
    assert.equal(g.ref.calls.length, 0);
});

test('pointercancel restores the start transform too', async () => {
    const g = await designerInit();
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 140, clientY: 310 }));
    g.s1.dispatch('pointercancel', ev(g.s1));
    assert.equal(g.s1.attrs.get('transform'), 'translate(30, 200)');
    assert.equal(g.ref.calls.length, 0);
});

test('a read-only canvas vetoes node gestures', async () => {
    const g = await designerInit({ readOnly: true });

    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 140, clientY: 310 }));
    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 140, clientY: 310 }));

    assert.equal(g.s1.attrs.get('transform'), 'translate(30, 200)');
    assert.equal(g.ref.calls.length, 0);
});

// ── transition creation from a port ─────────────────────────────────────────────────────────

test('a port drag onto another state notifies OnTransitionCreated once; the temp line is removed', async () => {
    const g = await designerInit();
    const s1fake = shapeNode({ 'data-state-id': 's1' }, g.svg);
    doc.elementFromPoint = () => s1fake;

    g.port.dispatch('pointerdown', ev(g.port, { clientX: 390, clientY: 125 }));
    g.port.dispatch('pointermove', ev(g.port, { clientX: 60, clientY: 220 }));
    assert.ok(g.inst().tempLine, 'the temp line follows the pointer');
    g.port.dispatch('pointerup', ev(g.port, { clientX: 60, clientY: 220 }));

    assert.deepEqual(g.ref.calls, [['OnTransitionCreated', 's2', 's1']]);
    assert.equal(g.inst().tempLine, null, 'the temp line is gone');
});

test('a port drag onto the empty canvas notifies nothing; Escape removes the temp line', async () => {
    const g = await designerInit();
    doc.elementFromPoint = () => null;

    g.port.dispatch('pointerdown', ev(g.port, { clientX: 390, clientY: 125 }));
    g.port.dispatch('pointermove', ev(g.port, { clientX: 500, clientY: 400 }));
    doc.dispatch('keydown', { key: 'Escape' });
    g.port.dispatch('pointerup', ev(g.port, { clientX: 500, clientY: 400 }));

    assert.deepEqual(g.ref.calls, []);
    assert.equal(g.inst().tempLine, null);
});

test('a read-only canvas vetoes port gestures', async () => {
    const g = await designerInit({ readOnly: true });

    g.port.dispatch('pointerdown', ev(g.port, { clientX: 390, clientY: 125 }));
    g.port.dispatch('pointermove', ev(g.port, { clientX: 60, clientY: 220 }));
    g.port.dispatch('pointerup', ev(g.port, { clientX: 60, clientY: 220 }));
    assert.equal(g.inst().tempLine, null);
    assert.equal(g.ref.calls.length, 0);
});

// ── pan on the empty canvas ─────────────────────────────────────────────────────────────────

test('an empty-canvas drag pans the viewBox and notifies OnViewBoxChanged once', async () => {
    const g = await designerInit();
    const canvas = shapeNode({}, g.svg);

    canvas.dispatch('pointerdown', ev(canvas, { clientX: 400, clientY: 250 }));
    canvas.dispatch('pointermove', ev(canvas, { clientX: 300, clientY: 200 }));
    const vb = g.svg.viewBox.baseVal;
    assert.equal(vb.x, 100, 'the viewBox pans opposite to the finger (1:1 at default zoom)');
    assert.equal(vb.y, 50);
    canvas.dispatch('pointerup', ev(canvas, { clientX: 300, clientY: 200 }));

    assert.deepEqual(g.ref.calls, [['OnViewBoxChanged', 100, 50, 800, 500]]);
});

test('a press on a state never pans, and Escape restores the start viewBox', async () => {
    const g = await designerInit();

    // A state press + move must not pan; Escape cancels the node drag.
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 300, clientY: 400 }));
    assert.equal(g.svg.viewBox.baseVal.x, 0);
    doc.dispatch('keydown', { key: 'Escape' });
    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 300, clientY: 400 }));
    assert.deepEqual(g.ref.calls, []);

    // A pan + Escape: the untouched viewBox attribute is not rewritten.
    const canvas = shapeNode({}, g.svg);
    canvas.dispatch('pointerdown', ev(canvas, { clientX: 400, clientY: 250 }));
    canvas.dispatch('pointermove', ev(canvas, { clientX: 200, clientY: 150 }));
    doc.dispatch('keydown', { key: 'Escape' });
    canvas.dispatch('pointerup', ev(canvas, { clientX: 200, clientY: 150 }));

    assert.equal(g.svg.getAttribute('viewBox'), null);
    assert.equal(g.svg.viewBox.baseVal.width, 800);
});

// ── pinch zoom (two fingers) + the drag hand-over ───────────────────────────────────────────

function pinchStart(g) {
    g.s1.dispatch('pointerdown', ev(g.s1, { pointerType: 'touch', pointerId: 1, isPrimary: true, clientX: 100, clientY: 100 }));
    g.s1.dispatch('pointermove', ev(g.s1, { pointerType: 'touch', pointerId: 1, isPrimary: true, clientX: 120, clientY: 100 }));
    assert.equal(g.s1.attrs.get('transform'), 'translate(50, 200)', 'precondition: the node drag is live');
    // The pinch starts when the second finger lands: its start distance is (120,100)-(200,100) = 80.
    g.svg.dispatch('pointerdown', ev(g.svg, { pointerType: 'touch', pointerId: 2, isPrimary: false, clientX: 200, clientY: 100 }));
}

test('two fingers cancel the node drag without committing, then pinch-zoom commits once', async () => {
    const g = await designerInit();
    pinchStart(g);

    // The hand-over: the drag rolled back to the Blazor-rendered transform and never committed.
    assert.equal(g.s1.attrs.get('transform'), 'translate(30, 200)');
    assert.deepEqual(g.ref.calls, []);

    // Spread to (50,100)-(250,100): distance 200 from the 80 start = scale 2.5, centre (150,100).
    g.s1.dispatch('pointermove', ev(g.s1, { pointerType: 'touch', pointerId: 1, isPrimary: true, clientX: 50, clientY: 100 }));
    g.svg.dispatch('pointermove', ev(g.svg, { pointerType: 'touch', pointerId: 2, isPrimary: false, clientX: 250, clientY: 100 }));
    const vb = g.svg.viewBox.baseVal;
    assert.equal(vb.width, 320, 'scale 2.5 shrinks the viewBox width');
    assert.ok(Math.abs(vb.x - 90) < 1e-9 && Math.abs(vb.y - 60) < 1e-9, 'the zoom keeps the pinch centre stationary');

    g.s1.dispatch('pointerup', ev(g.s1, { pointerType: 'touch', pointerId: 1, isPrimary: true, clientX: 50, clientY: 100 }));
    g.svg.dispatch('pointerup', ev(g.svg, { pointerType: 'touch', pointerId: 2, isPrimary: false, clientX: 250, clientY: 100 }));

    assert.deepEqual(
        g.ref.calls.filter(c => c[0] === 'OnZoomChanged'),
        [['OnZoomChanged', 2.5]],
        'one commit per gesture');
    assert.deepEqual(
        g.ref.calls.map(c => c[0]),
        ['OnViewBoxChanged', 'OnZoomChanged'],
        'the viewBox is committed BEFORE the zoom level, so a render in between sees both');
    assert.equal(g.ref.calls.filter(c => c[0] === 'OnNodeDragged').length, 0, 'the superseded node drag never committed');
});

test('Escape during a pinch restores the start viewBox and commits nothing', async () => {
    const g = await designerInit();
    pinchStart(g);
    g.s1.dispatch('pointermove', ev(g.s1, { pointerType: 'touch', pointerId: 1, isPrimary: true, clientX: 50, clientY: 100 }));
    g.svg.dispatch('pointermove', ev(g.svg, { pointerType: 'touch', pointerId: 2, isPrimary: false, clientX: 250, clientY: 100 }));
    assert.notEqual(g.svg.viewBox.baseVal.width, 800);

    doc.dispatch('keydown', { key: 'Escape' });
    g.s1.dispatch('pointerup', ev(g.s1, { pointerType: 'touch', pointerId: 1, isPrimary: true, clientX: 50, clientY: 100 }));
    g.svg.dispatch('pointerup', ev(g.svg, { pointerType: 'touch', pointerId: 2, isPrimary: false, clientX: 250, clientY: 100 }));

    assert.equal(g.svg.viewBox.baseVal.width, 800);
    assert.deepEqual(g.ref.calls, []);
});

test('ctrl+wheel zooms around the cursor; plain wheel is left to the page', async () => {
    const g = await designerInit();

    const plain = { ctrlKey: false, deltaY: -100, clientX: 400, clientY: 250, preventDefault() { this.defaultPrevented = true; } };
    g.svg.dispatch('wheel', plain);
    assert.equal(g.svg.viewBox.baseVal.width, 800, 'plain wheel never zooms');
    assert.equal(plain.defaultPrevented, undefined, 'plain wheel keeps scrolling the page');

    g.svg.dispatch('wheel', { ctrlKey: true, deltaY: -100 * Math.LN2, clientX: 400, clientY: 250, preventDefault() {} });
    assert.equal(g.svg.viewBox.baseVal.width, 400, 'ctrl+wheel zooms in');
    assert.equal(g.ref.calls.length, 0, 'the burst commits once, after it ends');
});

// ── auto-scroll while a node is dragged near the viewport edge ──────────────────────────────

test('a node drag near the bottom viewport edge scrolls the page and stops on release', async () => {
    const g = await designerInit();

    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 400, clientY: 590 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 400, clientY: 600 }));
    // Flush the rAF loop: the pointer rests ON the bottom edge -> full-speed scroll.
    const frames = globalThis.__rafQueue.splice(0);
    assert.ok(frames.length > 0, 'the scroller runs a frame loop during the drag');
    for (const fn of frames) fn();
    assert.equal(doc.scrollingElement.scrollTop, 28, 'one frame at full edge power = maxStep px');

    g.s1.dispatch('pointerup', ev(g.s1, { clientX: 400, clientY: 600 }));
    const framesAfter = globalThis.__rafQueue.splice(0);
    for (const fn of framesAfter) fn();
    assert.equal(framesAfter.length, 0, 'the loop stops on release');
});

// ── keyboard alternative (WCAG 2.5.7) ───────────────────────────────────────────────────────

test('Alt+ArrowRight on a focused state moves it one grid step through OnNodeKeyboardMoved', async () => {
    const g = await designerInit();

    g.s1.dispatch('keydown', { key: 'ArrowRight', altKey: true, target: g.s1, preventDefault() { this.defaultPrevented = true; } });

    assert.equal(g.s1.attrs.get('transform'), 'translate(50, 200)');
    assert.deepEqual(g.ref.calls, [['OnNodeKeyboardMoved', 's1', 50, 200]]);
});

test('a plain Arrow is cancelled (roving focus owns it, Blazor moves the focus), Delete deletes', async () => {
    const g = await designerInit();

    const arrow = { key: 'ArrowRight', altKey: false, target: g.s1, preventDefault() { this.defaultPrevented = true; } };
    g.s1.dispatch('keydown', arrow);
    assert.deepEqual(g.ref.calls, [], 'plain arrows never move the node');
    assert.equal(arrow.defaultPrevented, true, 'the page scroll default is cancelled in JS');

    g.s1.dispatch('keydown', { key: 'Delete', target: g.s1 });
    assert.deepEqual(g.ref.calls, [['OnNodeKeyboardDeleted', 's1']]);

    const ro = await designerInit({ readOnly: true });
    ro.s1.dispatch('keydown', { key: 'Delete', target: ro.s1 });
    assert.deepEqual(ro.ref.calls, [], 'read-only never deletes');
});

test('focusState focuses the state node the roving tabindex moved to', async () => {
    const g = await designerInit();
    let focused = null;
    g.s1.focus = () => { focused = 's1'; };
    g.s2.focus = () => { focused = 's2'; };

    module_.focusState(g.svg, 's2');
    assert.equal(focused, 's2');
    module_.focusState(g.svg, 'nope');
    assert.equal(focused, 's2', 'an unknown id is a no-op');
});

// ── lifecycle ───────────────────────────────────────────────────────────────────────────────

test('destroy mid-gesture cancels the drag and removes every listener', async () => {
    const g = await designerInit();
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 140, clientY: 310 }));
    module_.destroy(g.svg);

    assert.equal(g.s1.attrs.get('transform'), 'translate(30, 200)', 'the in-flight drag rolled back');
    assert.deepEqual(g.ref.calls, []);
    assert.equal(g.svg.total(), 0, 'no listener survives on the svg');
    assert.equal(doc.total(), 0, 'no listener survives on the document');
});

test('two canvases on one page never share a gesture', async () => {
    const a = await designerInit();
    const b = await designerInit();

    b.s1.dispatch('pointerdown', ev(b.s1, { clientX: 40, clientY: 210 }));
    b.s1.dispatch('pointermove', ev(b.s1, { clientX: 140, clientY: 310 }));
    b.s1.dispatch('pointerup', ev(b.s1, { clientX: 140, clientY: 310 }));

    assert.deepEqual(a.ref.calls, []);
    assert.deepEqual(b.ref.calls, [['OnNodeDragged', 's1', 130, 300]]);
});

// ── context menu (the svg contextmenu listener) ─────────────────────────────────────────────

function hostAt(left, top) {
    const host = shapeNode({}, null);
    host.getBoundingClientRect = () => ({ left, top, width: 800, height: 500 });
    return host;
}

test('a right-click on a state prevents the native menu and calls OnContextMenu with the container offset', async () => {
    const g = await designerInit();
    g.svg.parent = hostAt(12, 34);

    const e = ev(g.s1, { clientX: 112, clientY: 154, button: 2 });
    g.s1.dispatch('contextmenu', e);

    assert.equal(e.defaultPrevented, true, 'the native browser menu is cancelled');
    assert.deepEqual(g.ref.calls,
        [['OnContextMenu', 112, 154, 100, 120, 's1', null]],
        'svg point (x, y) + offset relative to the svg container rect (offX, offY) + the state id');
});

test('a right-click on a transition names the transition, not a state', async () => {
    const g = await designerInit();
    const t1 = shapeNode({ 'data-transition-id': 't1' }, g.svg);
    g.svg.children.push(t1);

    const e = ev(t1, { clientX: 200, clientY: 100, button: 2 });
    t1.dispatch('contextmenu', e);

    assert.equal(e.defaultPrevented, true);
    assert.deepEqual(g.ref.calls, [['OnContextMenu', 200, 100, 200, 100, null, 't1']]);
});

test('a read-only canvas leaves the right-click to the browser', async () => {
    const g = await designerInit({ readOnly: true });

    const e = ev(g.s1, { clientX: 100, clientY: 120, button: 2 });
    g.s1.dispatch('contextmenu', e);

    assert.equal(e.defaultPrevented, undefined, 'the native menu stays');
    assert.deepEqual(g.ref.calls, []);
});

test('a right-click mid-gesture cancels the gestures first (the menu wins)', async () => {
    const g = await designerInit();
    g.s1.dispatch('pointerdown', ev(g.s1, { clientX: 40, clientY: 210 }));
    g.s1.dispatch('pointermove', ev(g.s1, { clientX: 140, clientY: 310 }));
    assert.notEqual(g.s1.attrs.get('transform'), 'translate(30, 200)', 'precondition: dragging');

    g.s1.dispatch('contextmenu', ev(g.s1, { clientX: 140, clientY: 310, button: 2 }));

    assert.equal(g.s1.attrs.get('transform'), 'translate(30, 200)', 'the drag rolled back');
});

test('destroy removes the contextmenu listener', async () => {
    const g = await designerInit();
    module_.destroy(g.svg);

    assert.equal(g.svg.count('contextmenu'), 0);
    g.s1.dispatch('contextmenu', ev(g.s1, { clientX: 5, clientY: 5 }));
    assert.deepEqual(g.ref.calls, []);
});

// ── keyboard: Space is cancelled (Blazor activates on keyup) ────────────────────────────────

test('Space on a focused state is cancelled (activation stays on keyup in Blazor)', async () => {
    const g = await designerInit();

    const e = { key: ' ', altKey: false, target: g.s1, preventDefault() { this.defaultPrevented = true; } };
    g.s1.dispatch('keydown', e);

    assert.equal(e.defaultPrevented, true, 'the page-scroll default is cancelled');
    assert.deepEqual(g.ref.calls, []);
});

// ── programmatic zoom reports the new geometry back ─────────────────────────────────────────

test('zoomTo reports the new viewBox back to .NET as [x, y, w, h]', async () => {
    const g = await designerInit();

    const vb = module_.zoomTo(g.svg, 2);

    assert.deepEqual(vb, [200, 125, 400, 250], '.NET assigns its _viewBox fields from this');
});

test('fitToView reports the fit scale and the new viewBox as one object', async () => {
    const g = await designerInit();
    g.s1.getBoundingClientRect = () => ({ left: 0, top: 0, width: 140, height: 50 });

    const fit = module_.fitToView(g.svg, 40);

    assert.equal(typeof fit, 'object', '.NET deserializes { scale, x, y, w, h }');
    assert.ok(fit.scale > 1 && fit.scale <= 2, 'the two states fit inside the canvas, capped at 2');
    assert.deepEqual([fit.x + 40, fit.y + 40], [30, 100], 'the padding-inset origin is the content bound');
    assert.equal(fit.w, 800 / fit.scale);
    assert.equal(fit.h, 500 / fit.scale);
});

// ── focus helpers the context menu / roving focus use ───────────────────────────────────────

test('focusMenu focuses the first menu item and attaches the shared menu keyboard', async () => {
    const g = await designerInit();
    const first = { focus() { first.focused = true; }, addEventListener() {}, removeEventListener() {} };
    const menu = {
        querySelector: sel => (sel === '.tm-wf-context-menu__item' ? first : null),
        addEventListener() {}, removeEventListener() {},
    };

    module_.focusMenu(menu);

    assert.equal(first.focused, true, 'the first item owns the focus when the menu opens');
});

test('focusZoom focuses the zoom button, falling back to the svg itself', async () => {
    const g = await designerInit();
    const host = shapeNode({}, null);
    let hostFocused = false;
    host.querySelector = sel => (sel === '.tm-wf-zoom-overlay__btn'
        ? { focus() { hostFocused = true; } } : null);
    g.svg.parent = host;
    module_.focusZoom(g.svg);
    assert.equal(hostFocused, true);

    const bare = await designerInit();
    delete bare.svg.parent;              // no canvas container -> the svg takes the focus
    bare.svg.focus = () => { bare.svg.focused = true; };
    module_.focusZoom(bare.svg);
    assert.equal(bare.svg.focused, true, 'the svg is the last-resort focus target');
});

test('stateMenuPoint measures the state rect against the canvas container rect', async () => {
    const g = await designerInit();
    g.svg.parent = hostAt(10, 20);
    g.s1.getBoundingClientRect = () => ({ left: 43, top: 140, width: 140, height: 50 });

    assert.deepEqual(module_.stateMenuPoint(g.svg, 's1'), [33, 170],
        'the menu anchors at the state bottom-left, correct under zoom and pan');
    assert.equal(module_.stateMenuPoint(g.svg, 'nope'), null, 'an unknown state reports nothing');
});
