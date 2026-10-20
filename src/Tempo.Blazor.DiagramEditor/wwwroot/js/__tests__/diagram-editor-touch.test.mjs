// diagram-editor-touch.js on the shared helpers (DiagramEditor redesign, phase 6). The real
// pointer-drag.js and pointer-gesture.js run against a hand-rolled DOM: a finger drag on a node
// selects and moves it, a finger on a resize/rotate handle resizes/rotates (never drags the
// node), a 600ms hold opens the context menu instead of dragging, two fingers pinch/pan, and
// read-only keeps the navigation gestures while vetoing every editing gesture.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import vm from 'node:vm';
import fs from 'node:fs';

const here = path.dirname(fileURLToPath(import.meta.url));
// The real helpers, from the CORE source tree (four levels up from this folder: js/__tests__
// → js → wwwroot → package → src), with the trailing slash a URL base needs.
const coreUrl = pathToFileURL(path.resolve(here, '../../../../Tempo.Blazor/wwwroot/js/') + path.sep).href;
const dragHelpers = await import(new URL('pointer-drag.js', coreUrl).href);
const gestureHelpers = await import(new URL('pointer-gesture.js', coreUrl).href);
// The module under test resolves its own helpers from import.meta.url; the test injects the
// same pair explicitly so the run never depends on the deployed _content URL layout.
const touch = await import('../diagram-editor-touch.js');

// The real engine (diagram-editor.js) for the guards the touch stack relies on
// (_isTouchContextMenu): the long-press tests call the ACTUAL guard, not a copy of it.
const ENGINE_SRC = fs.readFileSync(new URL('../diagram-editor.js', import.meta.url), 'utf8');
function loadEngineGuard() {
    const window = {};
    const ctx = vm.createContext({
        window,
        document: { addEventListener() {}, removeEventListener() {} },
        URL, console, setTimeout, clearTimeout, performance,
    });
    vm.runInContext(ENGINE_SRC, ctx, { filename: 'diagram-editor.js' });
    return window.tmDiagramEditor;
}

/** Spec-faithful capture model: set/release change only the PENDING override; got/lost fire when
 *  it is processed (before and after each dispatched pointer event); events go to the capture target. */
function captureDom() {
    const current = new Map(); const pendingOv = new Map(); const log = [];
    function node(parent, { cls = [], attrs = {} } = {}) {
        const map = new Map();
        const n = { parent, classes: new Set(cls), style: {}, attrs: new Map(Object.entries(attrs)),
            addEventListener(t, f) { map.set(t, [...(map.get(t) ?? []), f]); },
            removeEventListener(t, f) { map.set(t, (map.get(t) ?? []).filter(x => x !== f)); },
            dispatch(t, e) { for (const f of [...(map.get(t) ?? [])]) f(e); if (n.parent) n.parent.dispatch(t, e); },
            classList: { add: c => n.classes.add(c), remove: c => n.classes.delete(c), contains: c => n.classes.has(c) },
            getAttribute: k => n.attrs.get(k) ?? null,
            hasPointerCapture: id => (pendingOv.has(id) ? pendingOv.get(id) : current.get(id)) === n,
            setPointerCapture(id) { pendingOv.set(id, n); },
            releasePointerCapture(id) { if (n.hasPointerCapture(id)) pendingOv.set(id, null); },
            contains(o) { for (let p = o; p; p = p.parent) if (p === n) return true; return false; },
            closest(sel) {
                for (let p = n; p; p = p.parent) for (const part of sel.split(',').map(s => s.trim())) {
                    if (part.startsWith('.') && p.classes.has(part.slice(1))) return p;
                    const m = part.match(/^\[([a-z-]+)\]$/); if (m && p.attrs.has(m[1])) return p;
                }
                return null;
            }, focus() {} };
        return n;
    }
    function process() {
        for (const [id, ov] of [...pendingOv]) {
            pendingOv.delete(id);
            const cur = current.get(id) ?? null;
            if (cur === ov) continue;
            if (cur) { current.delete(id); log.push('lost:' + id); cur.dispatch('lostpointercapture', { pointerId: id, target: cur }); }
            if (ov) { current.set(id, ov); ov.dispatch('gotpointercapture', { pointerId: id, target: ov }); }
        }
    }
    function send(type, e) {
        process();
        (current.get(e.pointerId) ?? e.target).dispatch(type, { ...e, target: e.target });
        process();
        if (type === 'pointerup' || type === 'pointercancel') { pendingOv.set(e.pointerId, null); process(); }
    }
    return { node, send, log };
}

// J11: without injected helpers the module resolves them from the DEPLOYED _content layout
// only — the source-tree fallback is gone, so a plain attachTouch() in Node must reject.
test('attachTouch without injected helpers rejects with the deployed-layout error (no source-tree fallback)', async () => {
    const engine = {};
    const inst = { container: el({ tag: 'div', attrs: { id: 'no-helpers' } }) };
    await assert.rejects(
        () => touch.attachTouch(engine, inst),
        err => /Tempo\.Blazor[\\/]js[\\/]pointer-drag\.js/.test(String(err?.message ?? err)),
    );
});

function bag() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of [...(map.get(type) ?? [])]) fn(event); return event; },
        count(type) { return (map.get(type) ?? []).length; },
    };
}

/** Minimal element: attributes, classList, closest() over the parent chain, bubbling dispatch. */
function el({ attrs = {}, parent = null, tag = 'g' } = {}) {
    const n = {
        ...bag(),
        tag,
        attrs: new Map(),
        classes: new Set(),
        style: {},
        parent,
        captured: new Set(),
        released: [],
        classList: {
            add: (c) => n.classes.add(c),
            remove: (c) => n.classes.delete(c),
            contains: (c) => n.classes.has(c),
        },
        setAttribute(name, value) {
            n.attrs.set(name, String(value));
            // a real DOM keeps the class attribute and the classList in sync
            if (name === 'class') {
                n.classes.clear();
                for (const c of String(value).split(/\s+/)) if (c) n.classes.add(c);
            }
        },
        getAttribute(name) { return n.attrs.has(name) ? n.attrs.get(name) : null; },
        removeAttribute(name) { n.attrs.delete(name); },
        setPointerCapture(id) { n.captured.add(id); },
        releasePointerCapture(id) { n.captured.delete(id); n.released.push(id); },
        focus() { },
        contains(other) {
            for (let p = other; p; p = p.parent) if (p === n) return true;
            return false;
        },
        closest(selector) {
            const parts = selector.split(',').map(s => s.trim());
            for (let p = n; p; p = p.parent) {
                for (const part of parts) {
                    if (part.startsWith('.')) { if (p.classes.has(part.slice(1))) return p; }
                    else if (part.startsWith('[')) {
                        const m = part.match(/^\[([a-zA-Z-]+)(?:="([^"]*)")?\]$/);
                        if (!m) continue;
                        const v = p.attrs.get(m[1]);
                        if (m[2] === undefined ? v !== undefined : v === m[2]) return p;
                    } else {
                        // tag selector, optionally with classes: g.tm-diagram-node-handles
                        const m = part.match(/^([a-zA-Z]+)((?:\.[\w-]+)*)$/);
                        if (!m) continue;
                        if (p.tag !== m[1]) continue;
                        const wanted = m[2] ? m[2].split('.').filter(Boolean) : [];
                        if (wanted.every(c => p.classes.has(c))) return p;
                    }
                }
            }
            return null;
        },
    };
    // Pointer events bubble: dispatching on a child reaches the sessions attached on the container.
    const own = n.dispatch;
    n.dispatch = (type, event = {}) => {
        own(type, event);
        if (n.parent) n.parent.dispatch(type, event);
        return event;
    };
    for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
    return n;
}

const resizeHandles = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];

/** A container with one node (n1) carrying a full handles group and one resize hit rect. */
async function rig({ readOnly = false } = {}) {
    const calls = [];
    const dotNetRef = {
        invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); },
    };

    const engine = {
        // every touch hook is a spy the assertions read. The hooks own the read-only rule —
        // the real touchPressNode selects nothing in read-only — so the fake records nothing.
        touchPressNode: (inst, nodeEl, x, y) => {
            if (inst.readOnly) return false;
            calls.push(['touchPressNode', nodeEl.attrs.get('data-node-id')]);
            return true;
        },
        touchBeginNodeDrag: (inst, nodeEl, x, y) => calls.push(['touchBeginNodeDrag']),
        touchMoveNodeDrag: (inst, x, y) => calls.push(['touchMoveNodeDrag', x, y]),
        touchEndNodeDrag: (inst, x, y) => calls.push(['touchEndNodeDrag', x, y]),
        touchCancelNodeDrag: (inst) => calls.push(['touchCancelNodeDrag']),
        touchPressEdge: (inst, edgeEl) => {
            calls.push(['touchPressEdge', edgeEl?.attrs?.get?.('data-edge-id')]);
            return true;
        },
        selectNodeForInspection: (inst, el) => calls.push(['selectNodeForInspection', el?.attrs?.get?.('data-node-id')]),
        selectEdgeForInspection: (inst, el) => calls.push(['selectEdgeForInspection', el?.closest?.('[data-edge-id]')?.attrs?.get?.('data-edge-id')]),
        clearSelectionForInspection: () => calls.push(['clearSelectionForInspection']),
        _updateSelection: () => {},
        touchBeginPan: (inst, x, y) => calls.push(['touchBeginPan', x, y]),
        touchMovePan: (inst, x, y) => calls.push(['touchMovePan', x, y]),
        touchEndPan: (inst) => calls.push(['touchEndPan']),
        touchCancelPan: (inst) => calls.push(['touchCancelPan']),
        touchBeginRotate: (inst, nodeId, x, y) => calls.push(['touchBeginRotate', nodeId, x, y]),
        touchMoveRotate: (inst, x, y) => calls.push(['touchMoveRotate', x, y]),
        touchEndRotate: (inst) => calls.push(['touchEndRotate']),
        touchCancelRotate: (inst) => calls.push(['touchCancelRotate']),
        touchPinchBegin: (inst) => { calls.push(['touchPinchBegin']); return { x: 0, y: 0, w: 800, h: 500 }; },
        touchPinchChange: (inst, start, scale, cx, cy, dx, dy) => calls.push(['touchPinchChange', scale, dx, dy]),
        touchPinchEnd: (inst, start, scale, dx, dy) => calls.push(['touchPinchEnd', scale, dx, dy]),
        touchPinchCancel: (inst, start) => calls.push(['touchPinchCancel']),
        // R4: the long-press session resolves the target through the engine hooks. The stub
        // mirrors the real touchContextMenuTarget / touchOpenContextMenu routing (origin = the
        // pointer type that opened the menu, C# mirrors it as data-anchor="touch").
        touchContextMenuTarget: (inst, t) => inst.readOnly ? null
            : t?.closest?.('[data-node-id]')
                ? { kind: 'node', id: t.closest('[data-node-id]').attrs.get('data-node-id') }
                : t?.closest?.('[data-edge-id]')
                    ? { kind: 'edge', id: t.closest('[data-edge-id]').attrs.get('data-edge-id') }
                    : { kind: 'canvas' },
        touchOpenContextMenu: (inst, t, x, y, origin) => {
            calls.push(['touchOpenContextMenu', t.kind, origin]);
            return inst.dotNetRef.invokeMethodAsync(
                t.kind === 'node' ? 'OnNodeContextMenu' : t.kind === 'edge' ? 'OnEdgeContextMenu' : 'OnCanvasContextMenu',
                t.id ?? null, x, y);
        },
        // J12a: the port / connection-point resolution and the edge-draw hooks (spies).
        touchPortAt: (inst, e) => (!inst.readOnly && !inst.__mobile && e?.target?.closest?.('.tm-diagram-port')) || null,
        touchBeginEdgeDraw: (inst, portEl, x, y) => calls.push(['touchBeginEdgeDraw', portEl?.attrs?.get?.('data-port-id'), x, y]),
        touchMoveEdgeDraw: (inst, x, y) => calls.push(['touchMoveEdgeDraw', x, y]),
        touchEndEdgeDraw: (inst, x, y) => calls.push(['touchEndEdgeDraw', x, y]),
        touchCancelEdgeDraw: (inst) => calls.push(['touchCancelEdgeDraw']),
    };

    const container = el({ tag: 'div', attrs: { id: 'tmd-test' } });
    const svg = el({ tag: 'svg', attrs: { class: 'tm-diagram-canvas__svg' }, parent: container });
    const node = el({ tag: 'g', attrs: { 'data-node-id': 'n1', 'data-locked': 'false' }, parent: svg });
    const port = el({ tag: 'circle', attrs: { class: 'tm-diagram-port', 'data-port-id': 'p1' }, parent: node });
    const handlesG = el({ tag: 'g', attrs: { class: 'tm-diagram-node-handles', 'data-node-id': 'n1' }, parent: svg });
    const hit = el({ tag: 'rect', attrs: { class: 'tm-diagram-resize-handle-hit', 'data-resize-handle': 'se' }, parent: handlesG });
    const rotateHit = el({ tag: 'rect', attrs: { class: 'tm-diagram-rotate-handle-hit' }, parent: handlesG });
    const edgeHit = el({ tag: 'path', attrs: { class: 'tm-diagram-edge-hit-path', 'data-edge-id': 'e1' }, parent: svg });
    const labelGroup = el({ tag: 'g', attrs: { class: 'tm-diagram-edge-label-group', 'data-edge-id': 'e1' }, parent: svg });
    const labelRect = el({ tag: 'rect', attrs: { class: 'tm-diagram-edge-label-bg' }, parent: labelGroup });

    const inst = {
        container,
        svg,
        dotNetRef,
        readOnly,
        selectedIds: new Set(),
        __touchCalls: calls,
    };

    const detach = await touch.attachTouch(engine, inst, { drag: dragHelpers, gesture: gestureHelpers });
    // The real engine flow parks the detach on the instance (_loadTouchModule); the guard tests
    // (engine._isTouchContextMenu) key off it.
    inst.__touchDetach = detach;
    return { inst, calls, container, node, port, hit, rotateHit, edgeHit, labelGroup, labelRect, handlesG, detach };
}

const pointer = (type, id, x, y) => ({
    type,
    pointerId: id,
    pointerType: 'touch',
    isPrimary: id === 1,
    button: 0,
    buttons: 1,
    clientX: x,
    clientY: y,
    target: null,
    preventDefault() { this.prevented = true; },
    stopPropagation() { },
});

test('a finger on a resize handle resizes the node instead of dragging it', async () => {
    const rigOut = await rig();
    const { node, hit, calls } = rigOut;

    const down = pointer('pointerdown', 1, 200, 200);
    down.target = hit;
    hit.dispatch('pointerdown', down);
    // past the zero threshold of the handle session, under the node-drag threshold
    hit.dispatch('pointermove', { ...pointer('pointermove', 1, 210, 210), target: hit });
    hit.dispatch('pointerup', { ...pointer('pointerup', 1, 210, 210), target: hit });

    const names = calls.map(c => c[0]);
    assert.ok(names.includes('touchBeginNodeDrag') === false, 'a handle press must never arm a node drag');
    assert.deepEqual(names.filter(n => n === 'touchPressNode'), [], 'the node press hook must not run for a handle');
    assert.deepEqual(
        calls.filter(c => c[0] === 'OnResizeStarted').map(c => [c[1], c[2]]),
        [['n1', 'se']],
        'the resize session reports node + handle to .NET');
    assert.ok(calls.some(c => c[0] === 'OnResizeMoved'), 'the drag drives live resize moves');
    assert.ok(calls.some(c => c[0] === 'OnResizeEnded'), 'the resize commits on release');
});

test('a finger drag on a node selects it first and then moves it', async () => {
    const { node, calls } = await rig();

    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 120, 110), target: node });
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 120, 110), target: node });

    const names = calls.map(c => c[0]);
    assert.ok(names.includes('touchPressNode'), 'the press selects the node');
    assert.ok(names.includes('touchBeginNodeDrag'), 'the drag arms');
    assert.ok(calls.some(c => c[0] === 'touchMoveNodeDrag' && c[1] === 120), 'moves report pointer positions');
    assert.ok(names.includes('touchEndNodeDrag'), 'the drag commits');
    assert.equal(names.includes('OnNodeContextMenu'), false, 'a moving finger never opens the menu');
});

test('a tap on an edge hit path selects the edge and a drag pans', async () => {
    const { edgeHit, calls } = await rig();

    const down = pointer('pointerdown', 1, 10, 10);
    down.target = edgeHit;
    edgeHit.dispatch('pointerdown', down);
    edgeHit.dispatch('pointerup', { ...pointer('pointerup', 1, 11, 10), target: edgeHit });

    assert.deepEqual(
        calls.filter(c => c[0] === 'touchPressEdge').map(c => c[1]),
        ['e1'],
        'a tap on an edge selects it');

    // then a drag from the empty surface pans (a new primary press, as after the tap)
    const svg = edgeHit.parent;
    svg.dispatch('pointerdown', { ...pointer('pointerdown', 1, 50, 50), target: svg });
    svg.dispatch('pointermove', { ...pointer('pointermove', 1, 90, 70), target: svg });
    svg.dispatch('pointerup', { ...pointer('pointerup', 1, 90, 70), target: svg });
    assert.ok(calls.some(c => c[0] === 'touchBeginPan' && c[1] === 50), 'a drag from the empty canvas pans');
    assert.ok(calls.some(c => c[0] === 'touchEndPan'), 'the pan commits');
});

test('a 600ms hold on a node opens the context menu and rolls the drag back', async () => {
    const { node, calls } = await rig();

    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    await new Promise(resolve => setTimeout(resolve, 700));
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 100, 100), target: node });

    assert.deepEqual(
        calls.filter(c => c[0] === 'OnNodeContextMenu').map(c => c[1]),
        ['n1'],
        'the long press opens the node context menu through the helper longPressMs');
    assert.ok(calls.some(c => c[0] === 'touchCancelNodeDrag'), 'the armed single-finger drag rolls back');
    assert.equal(calls.filter(c => c[0] === 'touchEndNodeDrag').length, 0, 'a held press never commits a move');
});

test('a finger that moves before the hold never opens the menu', async () => {
    const { node, calls } = await rig();

    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 160, 140), target: node });
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 160, 140), target: node });
    await new Promise(resolve => setTimeout(resolve, 650));

    assert.equal(calls.filter(c => c[0] === 'OnNodeContextMenu').length, 0, 'a moving finger drags, it does not menu');
    assert.ok(calls.some(c => c[0] === 'touchEndNodeDrag'), 'the quick drag commits');
});

test('two fingers pinch and the armed single-finger drag hands over', async () => {
    const { container, node, calls } = await rig();

    const down1 = pointer('pointerdown', 1, 100, 100);
    down1.target = node;
    node.dispatch('pointerdown', down1);
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 120, 120), target: node });

    node.dispatch('pointerdown', { ...pointer('pointerdown', 2, 200, 100), target: node });
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 80, 100), target: node });
    node.dispatch('pointermove', { ...pointer('pointermove', 2, 220, 100), target: node });
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 80, 100), target: node });
    node.dispatch('pointerup', { ...pointer('pointerup', 2, 220, 100), target: node });

    assert.ok(calls.some(c => c[0] === 'touchPinchBegin'), 'the pinch session starts on the second finger');
    assert.ok(calls.some(c => c[0] === 'touchCancelNodeDrag') || calls.some(c => c[0] === 'touchCancelPan'),
        'the host cancels its single-finger gesture from the pinch onStart');
    assert.ok(calls.some(c => c[0] === 'touchPinchChange' && c[1] > 1.1), 'the pinch reports the scale (120→80 px over 100)');
    assert.equal(calls.filter(c => c[0] === 'touchEndNodeDrag').length, 0, 'the handed-over drag commits nothing');
});

test('a finger on the rotate handle rotates and never drags the node', async () => {
    const { rotateHit, calls } = await rig();

    const down = pointer('pointerdown', 1, 160, 20);
    down.target = rotateHit;
    rotateHit.dispatch('pointerdown', down);
    rotateHit.dispatch('pointermove', { ...pointer('pointermove', 1, 180, 40), target: rotateHit });
    rotateHit.dispatch('pointerup', { ...pointer('pointerup', 1, 180, 40), target: rotateHit });

    const names = calls.map(c => c[0]);
    assert.equal(names.includes('touchBeginNodeDrag'), false, 'the rotate handle never arms a node drag');
    assert.ok(calls.some(c => c[0] === 'touchBeginRotate'), 'the rotate session starts');
    assert.ok(calls.some(c => c[0] === 'touchMoveRotate'), 'the rotation tracks the finger');
    assert.ok(calls.some(c => c[0] === 'touchEndRotate'), 'the rotation commits');
});

test('read-only keeps navigation but vetoes every editing gesture', async () => {
    const { node, hit, calls } = await rig({ readOnly: true });

    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 150, 150), target: node });
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 150, 150), target: node });
    await new Promise(resolve => setTimeout(resolve, 650));

    const names = calls.map(c => c[0]);
    assert.equal(names.includes('touchPressNode'), false, 'read-only never selects through the touch press');
    assert.equal(names.includes('touchBeginNodeDrag'), false, 'read-only never drags');
    assert.equal(names.includes('OnNodeContextMenu'), false, 'read-only never opens the context menu');
    assert.ok(calls.some(c => c[0] === 'touchBeginPan' && c[1] === 100),
        'R1: a finger that lands on a node pans');
    assert.ok(calls.some(c => c[0] === 'touchMovePan'), 'the pan keeps moving');
    assert.equal(names.includes('selectNodeForInspection'), false,
        'a DRAG must not inspect — only a tap does');

    const hitDown = pointer('pointerdown', 2, 200, 200);
    hitDown.target = hit;
    hit.dispatch('pointerdown', hitDown);
    hit.dispatch('pointerup', { ...pointer('pointerup', 2, 200, 200), target: hit });
    assert.equal(calls.some(c => c[0] === 'OnResizeStarted'), false, 'read-only never resizes');
});

test('read-only: a tap on a node selects it for inspection', async () => {
    const { node, calls } = await rig({ readOnly: true });

    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 100, 100), target: node });
    await new Promise(resolve => setTimeout(resolve, 700));

    const names = calls.map(c => c[0]);
    assert.deepEqual(
        calls.filter(c => c[0] === 'selectNodeForInspection').map(c => c[1]),
        ['n1'],
        'the tap selects the node for the read-only properties panel');
    assert.equal(names.includes('touchBeginNodeDrag'), false, 'a tap never drags');
    assert.equal(names.includes('touchBeginPan'), false, 'a tap never pans');
});

test('read-only: a finger on an edge pans', async () => {
    const { edgeHit, calls } = await rig({ readOnly: true });

    const down = pointer('pointerdown', 1, 10, 10);
    down.target = edgeHit;
    edgeHit.dispatch('pointerdown', down);
    edgeHit.dispatch('pointermove', { ...pointer('pointermove', 1, 60, 10), target: edgeHit });
    edgeHit.dispatch('pointerup', { ...pointer('pointerup', 1, 60, 10), target: edgeHit });

    assert.ok(calls.some(c => c[0] === 'touchBeginPan' && c[1] === 10),
        'R1: a finger that lands on an edge pans');
    assert.equal(calls.some(c => c[0] === 'touchPressEdge'), false,
        'the read-only pan press never routes through the edit-mode edge selection');
    assert.equal(calls.some(c => c[0] === 'selectEdgeForInspection'), false,
        'a DRAG on an edge must not inspect — only a tap does');
});

test('read-only: a tap on an edge selects it for inspection', async () => {
    const { edgeHit, calls } = await rig({ readOnly: true });

    const down = pointer('pointerdown', 1, 10, 10);
    down.target = edgeHit;
    edgeHit.dispatch('pointerdown', down);
    edgeHit.dispatch('pointerup', { ...pointer('pointerup', 1, 11, 10), target: edgeHit });

    assert.deepEqual(
        calls.filter(c => c[0] === 'selectEdgeForInspection').map(c => c[1]),
        ['e1'],
        'the tap selects the edge for the read-only properties panel');
    assert.equal(calls.some(c => c[0] === 'touchPressEdge'), false,
        'read-only never routes through the edit-mode edge selection');
    assert.equal(calls.some(c => c[0] === 'touchBeginPan'), false, 'a tap never pans');
});

test('a drag that starts on an edge pans; a tap still selects it', async () => {
    const { edgeHit, calls } = await rig();

    // Tap selects the edge (edit mode routes through touchPressEdge).
    const tapDown = pointer('pointerdown', 1, 10, 10);
    tapDown.target = edgeHit;
    edgeHit.dispatch('pointerdown', tapDown);
    edgeHit.dispatch('pointerup', { ...pointer('pointerup', 1, 11, 10), target: edgeHit });
    assert.deepEqual(
        calls.filter(c => c[0] === 'touchPressEdge').map(c => c[1]),
        ['e1'],
        'a tap on an edge selects it');

    // A drag from the edge now pans too (J12d: previously nothing happened).
    const dragDown = pointer('pointerdown', 1, 50, 50);
    dragDown.target = edgeHit;
    edgeHit.dispatch('pointerdown', dragDown);
    edgeHit.dispatch('pointermove', { ...pointer('pointermove', 1, 120, 50), target: edgeHit });
    edgeHit.dispatch('pointerup', { ...pointer('pointerup', 1, 120, 50), target: edgeHit });
    assert.ok(calls.some(c => c[0] === 'touchBeginPan' && c[1] === 50),
        'a drag that starts on an edge pans');
});

test('cancelEditing rolls back an active node drag and resize but keeps a pan running', async () => {
    const rigOut = await rig();
    const { node, hit, calls, detach } = rigOut;
    const svg = node.parent;

    // Node drag in flight (primary pointer).
    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 130, 130), target: node });
    detach.cancelEditing('read-only');
    assert.ok(calls.some(c => c[0] === 'touchCancelNodeDrag'), 'the node drag rolled back');

    // Resize in flight (a fresh primary press after the cancelled drag).
    const hitDown = pointer('pointerdown', 1, 200, 200);
    hitDown.target = hit;
    hit.dispatch('pointerdown', hitDown);
    hit.dispatch('pointermove', { ...pointer('pointermove', 1, 220, 220), target: hit });
    detach.cancelEditing('read-only');
    assert.ok(calls.some(c => c[0] === 'OnResizeCancelled'), 'the resize rolled back');

    // A pan in flight survives the editing cancellation and keeps navigating.
    const panDown = pointer('pointerdown', 1, 300, 300);
    panDown.target = svg;
    svg.dispatch('pointerdown', panDown);
    svg.dispatch('pointermove', { ...pointer('pointermove', 1, 320, 300), target: svg });
    detach.cancelEditing('read-only');
    assert.equal(calls.some(c => c[0] === 'touchCancelPan'), false,
        'navigation (pan) is not an editing gesture');
    svg.dispatch('pointermove', { ...pointer('pointermove', 1, 340, 300), target: svg });
    assert.ok(calls.some(c => c[0] === 'touchMovePan' && c[1] === 340),
        'the pan keeps moving after cancelEditing');
});

test('detach ends every session and cancels an active drag', async () => {
    const { node, calls, detach } = await rig();

    const down = pointer('pointerdown', 1, 100, 100);
    down.target = node;
    node.dispatch('pointerdown', down);
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 130, 130), target: node });
    detach();
    node.dispatch('pointermove', { ...pointer('pointermove', 1, 150, 150), target: node });
    node.dispatch('pointerup', { ...pointer('pointerup', 1, 150, 150), target: node });

    const movesAfterDetach = calls.filter(c => c[0] === 'touchMoveNodeDrag' && c[1] === 150).length;
    assert.equal(movesAfterDetach, 0, 'a detached session no longer drives the editor');
    assert.ok(calls.some(c => c[0] === 'touchCancelNodeDrag'), 'detach cancels the drag in progress');
});

test('detach.cancel cancels navigation too, unlike cancelEditing', async () => {
    const { node, calls, detach } = await rig();
    const svg = node.parent;

    // A pan in flight.
    const panDown = pointer('pointerdown', 1, 50, 50);
    panDown.target = svg;
    svg.dispatch('pointerdown', panDown);
    svg.dispatch('pointermove', { ...pointer('pointermove', 1, 70, 50), target: svg });
    detach.cancel('destroyed');
    assert.ok(calls.some(c => c[0] === 'touchCancelPan'), 'destroy cancels every session, pan included');

    // And a fresh gesture is dead: nothing further reaches the engine.
    const again = pointer('pointerdown', 2, 100, 100);
    again.target = node;
    node.dispatch('pointerdown', again);
    node.dispatch('pointermove', { ...pointer('pointermove', 2, 130, 130), target: node });
    assert.equal(calls.filter(c => c[0] === 'touchBeginNodeDrag').length, 0,
        'a cancelled session never re-arms');
});

// ── J5/R4: the long-press menu covers the edge and the empty canvas, exactly once ───────────

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

test('a 600ms hold on an edge opens the edge context menu', async () => {
    const { edgeHit, calls } = await rig();

    const down = pointer('pointerdown', 1, 10, 10);
    down.target = edgeHit;
    edgeHit.dispatch('pointerdown', down);
    await sleep(700);
    edgeHit.dispatch('pointerup', { ...pointer('pointerup', 1, 10, 10), target: edgeHit });

    assert.deepEqual(
        calls.filter(c => c[0] === 'OnEdgeContextMenu').map(c => c[1]),
        ['e1'],
        'the long-press opens the edge context menu');
    assert.equal(calls.filter(c => c[0] === 'OnNodeContextMenu').length, 0, 'never the node menu');
});

test('a 600ms hold on the empty canvas opens the canvas context menu and cancels the armed pan', async () => {
    const { node, calls } = await rig();
    const svg = node.parent;

    const down = pointer('pointerdown', 1, 300, 300);
    down.target = svg;
    svg.dispatch('pointerdown', down);
    await sleep(700);
    svg.dispatch('pointerup', { ...pointer('pointerup', 1, 300, 300), target: svg });

    assert.ok(calls.some(c => c[0] === 'OnCanvasContextMenu'), 'the long-press opens the canvas menu');
    assert.ok(calls.some(c => c[0] === 'touchCancelPan'), 'the armed pan rolls back');
    assert.equal(calls.filter(c => c[0] === 'touchEndPan').length, 0, 'a held press never commits a pan');
});

test('a long-press reports OnNodeContextMenu exactly once even when the native contextmenu fires', async () => {
    const engine = loadEngineGuard();
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const { node, container, calls, inst } = await rig();

        // The REAL engine guard (diagram-editor.js _isTouchContextMenu): a native contextmenu
        // fired while a touch contact is down — or just after one lifted — is swallowed.
        container.addEventListener('contextmenu', (e) => {
            if (!engine._isTouchContextMenu(e, inst)) calls.push(['OnNodeContextMenu', 'native']);
        });

        const down = pointer('pointerdown', 1, 100, 100);
        down.target = node;
        node.dispatch('pointerdown', down);
        await sleep(300);                       // the hold is running; Android fires the native menu here
        container.dispatch('contextmenu', { pointerType: 'touch', clientX: 100, clientY: 100 });
        await sleep(400);                       // 700 ms: the session's long-press fires
        docBag.dispatch('pointerup', { pointerId: 1, pointerType: 'touch' });   // the lift reaches the document

        assert.equal(inst.__touchContacts.size, 0, 'the contact tracker released the finger');
        assert.equal(calls.filter(c => c[0] === 'OnNodeContextMenu').length, 1,
            'the menu opens exactly once — from the session, the native event was swallowed');
        assert.deepEqual(
            calls.filter(c => c[0] === 'touchOpenContextMenu').map(c => [c[1], c[2]]),
            [['node', 'touch']],
            'the touch origin rides to C# (data-anchor="touch")');
    } finally {
        delete globalThis.document;
    }
});

test('the release click on the just-opened menu is swallowed (R4)', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const { node } = await rig();

        const down = pointer('pointerdown', 1, 100, 100);
        down.target = node;
        node.dispatch('pointerdown', down);
        await sleep(700);                   // the hold opens the menu at the finger

        // The compatibility click of the lifting finger lands on a menu item.
        const item = {
            closest: sel => sel === '.tm-diagram-editor__context-menu' ? item : null,
        };
        let prevented = 0;
        let stopped = 0;
        docBag.dispatch('click', {
            target: item,
            preventDefault() { prevented++; },
            stopPropagation() { stopped++; },
        });
        assert.equal(prevented, 1, 'the accidental menu-item click is prevented');
        assert.equal(stopped, 1, 'the accidental menu-item click never reaches Blazor');

        // A click outside the menu passes through untouched.
        docBag.dispatch('click', {
            target: { closest: () => null },
            preventDefault() { prevented++; },
            stopPropagation() { stopped++; },
        });
        assert.equal(prevented, 1, 'clicks outside the menu are never touched by the guard');
    } finally {
        delete globalThis.document;
    }
});

test('the release-click guard: a LONG hold still swallows the release click; a tap 350 ms after the lift passes', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const { node } = await rig();
        const menuItem = { closest: sel => sel === '.tm-diagram-editor__context-menu' ? menuItem : null };
        let prevented = 0;

        // Long hold: the finger stays down TWO seconds after the menu opened (600 ms).
        const down = pointer('pointerdown', 1, 100, 100);
        down.target = node;
        node.dispatch('pointerdown', down);
        await sleep(700);
        node.dispatch('pointerup', { ...pointer('pointerup', 1, 100, 100), target: node });
        docBag.dispatch('click', { target: menuItem, preventDefault() { prevented++; }, stopPropagation() {} });
        assert.equal(prevented, 1, 'the release click right after a long hold is swallowed');

        // A tap whose lift is more than 350 ms after the hold's lift: its click passes.
        await sleep(400);
        node.dispatch('pointerdown', { ...pointer('pointerdown', 1, 120, 120), target: node });
        node.dispatch('pointerup', { ...pointer('pointerup', 1, 120, 120), target: node });
        docBag.dispatch('click', { target: menuItem, preventDefault() { prevented++; }, stopPropagation() {} });
        assert.equal(prevented, 1, 'a click 400 ms after the lift is not swallowed');
    } finally {
        delete globalThis.document;
    }
});

test('any pointerdown clears the release-click suppression window (review fix)', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const { node } = await rig();
        const menuItem = { closest: sel => sel === '.tm-diagram-editor__context-menu' ? menuItem : null };
        let prevented = 0;

        // Long hold opens the menu; the lift arms the 350 ms suppression window.
        const down = pointer('pointerdown', 1, 100, 100);
        down.target = node;
        node.dispatch('pointerdown', down);
        await sleep(700);
        node.dispatch('pointerup', { ...pointer('pointerup', 1, 100, 100), target: node });

        // The user presses again immediately (inside the window): the press itself cancels
        // the guard — a fast intentional click must not be swallowed by the lift's window.
        docBag.dispatch('pointerdown', { pointerId: 2, pointerType: 'mouse' });
        docBag.dispatch('click', { target: menuItem, preventDefault() { prevented++; }, stopPropagation() {} });
        assert.equal(prevented, 0, 'a real press clears the lift-click window — the next click passes');
    } finally {
        delete globalThis.document;
    }
});

test('the release-click guard is scoped to this editor menu (another editor menu passes)', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const { node, container, inst } = await rig();
        // A host wrapper: this editor's menu resolves INTO the wrapper.
        const editorRoot = { name: 'editor-root' };
        container.closest = sel => sel === '.tm-diagram-editor' ? editorRoot : null;
        const ownItem = {
            closest: sel => sel === '.tm-diagram-editor__context-menu' ? ownItem
                : sel === '.tm-diagram-editor' ? editorRoot : null,
        };
        const foreignItem = {
            closest: sel => sel === '.tm-diagram-editor__context-menu' ? foreignItem
                : sel === '.tm-diagram-editor' ? { name: 'other-editor' } : null,
        };
        let prevented = 0;

        const down = pointer('pointerdown', 1, 100, 100);
        down.target = node;
        node.dispatch('pointerdown', down);
        await sleep(700);                   // the hold opens THIS editor's menu
        node.dispatch('pointerup', { ...pointer('pointerup', 1, 100, 100), target: node });

        docBag.dispatch('click', { target: foreignItem, preventDefault() { prevented++; }, stopPropagation() {} });
        assert.equal(prevented, 0, "another editor's menu is not touched by this editor's guard");
        docBag.dispatch('click', { target: ownItem, preventDefault() { prevented++; }, stopPropagation() {} });
        assert.equal(prevented, 1, 'this editor’s own menu click is swallowed');
        void inst;
    } finally {
        delete globalThis.document;
    }
});

test('a long-press grows a press ring after ~150 ms and removes it on every exit', async () => {
    const created = [];
    const docBag = bag();
    docBag.createElement = () => {
        const ring = { style: {}, className: '', removed: false, remove() { this.removed = true; } };
        created.push(ring);
        return ring;
    };
    docBag.body = { appendChild() {} };
    globalThis.document = docBag;
    try {
        const { node } = await rig();

        const down = pointer('pointerdown', 1, 100, 100);
        down.target = node;
        node.dispatch('pointerdown', down);
        await sleep(250);                   // past the 150 ms ring delay, before the 600 ms hold
        assert.equal(created.length, 1, 'the press ring appears while the hold is running');
        assert.equal(created[0].className, 'tm-diagram-press-ring');
        assert.equal(created[0].style.left, '100px', 'the ring is pinned to the press point');
        assert.equal(created[0].style.top, '100px');

        await sleep(500);                   // the hold completes: the menu opens
        assert.equal(created[0].removed, true, 'the ring is removed when the menu opens');
        docBag.dispatch('pointerup', { pointerId: 1, pointerType: 'touch' });

        // A tap (no hold) still cleans its ring up through the release path.
        node.dispatch('pointerdown', { ...pointer('pointerdown', 1, 140, 140), target: node });
        await sleep(250);
        assert.equal(created.length, 2, 'a fresh hold grows a new ring');
        node.dispatch('pointerup', { ...pointer('pointerup', 1, 140, 140), target: node });
        assert.equal(created[1].removed, true, 'the ring is removed on the tap release');
    } finally {
        delete globalThis.document;
    }
});

test('the contact tracker keeps a Set of touch pointerIds; lifts on the document release them; a mouse press clears', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const engine = loadEngineGuard();
        const { node, container, calls, inst } = await rig();
        container.addEventListener('contextmenu', (e) => {
            if (!engine._isTouchContextMenu(e, inst)) calls.push(['OnNodeContextMenu', 'mouse']);
        });

        node.dispatch('pointerdown', { ...pointer('pointerdown', 1, 100, 100), target: node, pointerType: 'touch' });
        node.dispatch('pointerdown', { ...pointer('pointerdown', 2, 200, 100), target: node, pointerType: 'touch', isPrimary: false });
        assert.equal(inst.__touchContacts.size, 2, 'both touch contacts are tracked by pointerId');

        docBag.dispatch('pointerup', { pointerId: 1, pointerType: 'touch' });   // the lift happens over the document
        assert.equal(inst.__touchContacts.size, 1, 'a document lift releases exactly that finger');
        assert.ok(inst.__lastTouchUpAt > 0, 'the lift time is recorded for the post-lift guard');

        docBag.dispatch('pointercancel', { pointerId: 2, pointerType: 'touch' });
        assert.equal(inst.__touchContacts.size, 0, 'pointercancel releases the other finger');

        // No contact down, the last lift long ago: a mouse contextmenu opens the menu.
        inst.__lastTouchUpAt = Date.now() - 5000;
        container.dispatch('contextmenu', { pointerType: 'mouse', clientX: 5, clientY: 5 });
        assert.deepEqual(calls.filter(c => c[0] === 'OnNodeContextMenu').map(c => c[1]), ['mouse'],
            'with no touch contact a mouse contextmenu is NOT swallowed');

        // During a hold a touch contextmenu IS swallowed by the real guard.
        node.dispatch('pointerdown', { ...pointer('pointerdown', 4, 100, 100), target: node, pointerType: 'touch', isPrimary: false });
        container.dispatch('contextmenu', { pointerType: 'touch', clientX: 5, clientY: 5 });
        assert.equal(calls.filter(c => c[0] === 'OnNodeContextMenu').length, 1,
            'a touch contextmenu during the hold stays swallowed');
        docBag.dispatch('pointerup', { pointerId: 4, pointerType: 'touch' });

        // A pen/mouse press is not a touch contact — and clears the tracker (no touch hold can
        // be running while the mouse/pen is pressing).
        node.dispatch('pointerdown', { ...pointer('pointerdown', 5, 100, 100), target: node, pointerType: 'touch', isPrimary: false });
        assert.equal(inst.__touchContacts.size, 1);
        container.dispatch('pointerdown', { ...pointer('pointerdown', 6, 400, 400), target: container, pointerType: 'pen' });
        assert.equal(inst.__touchContacts.size, 0, 'a pen press clears the touch tracker');
        docBag.dispatch('pointerup', { pointerId: 5, pointerType: 'touch' });
    } finally {
        delete globalThis.document;
    }
});

test('detach removes the document guard and contact listeners', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const { detach } = await rig();
        assert.ok(docBag.count('click') > 0, 'the release-click guard is registered');
        assert.ok(docBag.count('pointerdown') > 0, 'the disarm listener is registered');
        assert.ok(docBag.count('pointerup') > 0 && docBag.count('pointercancel') > 0
            && docBag.count('lostpointercapture') > 0, 'the contact listeners are registered');

        detach();
        assert.equal(docBag.count('click'), 0, 'the click guard is removed');
        assert.equal(docBag.count('pointerdown'), 0, 'the disarm listener is removed');
        assert.equal(docBag.count('pointerup'), 0, 'the document lift listener is removed');
        assert.equal(docBag.count('pointercancel'), 0);
        assert.equal(docBag.count('lostpointercapture'), 0);
    } finally {
        delete globalThis.document;
    }
});

// ── J12a/J12b (R2): a finger drag from a port draws an edge (tablet+) ────────────────────────

test('a finger drag from a port draws an edge (tablet)', async () => {
    const { port, calls } = await rig();

    const down = pointer('pointerdown', 1, 200, 200);
    down.target = port;
    port.dispatch('pointerdown', down);
    port.dispatch('pointermove', { ...pointer('pointermove', 1, 220, 210), target: port });
    port.dispatch('pointermove', { ...pointer('pointermove', 1, 240, 220), target: port });
    port.dispatch('pointerup', { ...pointer('pointerup', 1, 240, 220), target: port });

    const names = calls.map(c => c[0]);
    assert.ok(names.includes('touchBeginEdgeDraw'), 'the port drag begins the edge draw');
    assert.ok(names.includes('touchMoveEdgeDraw'), 'the draw tracks the finger');
    assert.ok(names.includes('touchEndEdgeDraw'), 'the lift commits through the shared path');
    assert.equal(names.indexOf('touchBeginEdgeDraw') < names.indexOf('touchMoveEdgeDraw')
        && names.indexOf('touchMoveEdgeDraw') < names.indexOf('touchEndEdgeDraw'), true,
        'the order is begin → move → end');
    assert.equal(names.includes('touchBeginPan'), false, 'a port press never pans');
    assert.equal(names.includes('touchBeginNodeDrag'), false, 'a port press never drags the node');
});

test('a port press on mobile layout never draws', async () => {
    const rigOut = await rig();
    const { port, calls, inst } = rigOut;
    inst.__mobile = true;   // the real touchPortAt returns null on a mobile layout

    const down = pointer('pointerdown', 1, 200, 200);
    down.target = port;
    port.dispatch('pointerdown', down);
    port.dispatch('pointermove', { ...pointer('pointermove', 1, 240, 220), target: port });
    port.dispatch('pointerup', { ...pointer('pointerup', 1, 240, 220), target: port });

    assert.equal(calls.some(c => c[0] === 'touchBeginEdgeDraw'), false, 'mobile keeps the connect arrow / Connect dialog');
    assert.equal(calls.some(c => c[0] === 'touchBeginPan'), false, 'a port press under the node does not pan either');
    assert.equal(calls.some(c => c[0] === 'touchBeginNodeDrag'), false, 'and it does not drag the node');
});

test('two fingers during an edge draw cancel it', async () => {
    const { port, calls } = await rig();

    const down = pointer('pointerdown', 1, 200, 200);
    down.target = port;
    port.dispatch('pointerdown', down);
    port.dispatch('pointermove', { ...pointer('pointermove', 1, 220, 210), target: port });
    assert.ok(calls.some(c => c[0] === 'touchBeginEdgeDraw'), 'the draw started');

    port.dispatch('pointerdown', { ...pointer('pointerdown', 2, 400, 200), target: port });
    assert.ok(calls.some(c => c[0] === 'touchCancelEdgeDraw'),
        'the pinch hand-over cancels the edge draw');

    port.dispatch('pointerup', { ...pointer('pointerup', 1, 220, 210), target: port });
    port.dispatch('pointerup', { ...pointer('pointerup', 2, 420, 200), target: port });
    assert.equal(calls.some(c => c[0] === 'touchEndEdgeDraw'), false, 'a cancelled draw never commits');
});

test('a press on an edge label rect passes the [data-edge-id] element', async () => {
    const { labelRect, calls } = await rig();

    const down = pointer('pointerdown', 1, 10, 10);
    down.target = labelRect;
    labelRect.dispatch('pointerdown', down);
    labelRect.dispatch('pointerup', { ...pointer('pointerup', 1, 11, 10), target: labelRect });

    assert.deepEqual(
        calls.filter(c => c[0] === 'touchPressEdge').map(c => c[1]),
        ['e1'],
        'the label press resolves the [data-edge-id] ancestor');
});

test('read-only never starts a port edge draw', async () => {
    const { port, calls } = await rig({ readOnly: true });

    const down = pointer('pointerdown', 1, 200, 200);
    down.target = port;
    port.dispatch('pointerdown', down);
    port.dispatch('pointermove', { ...pointer('pointermove', 1, 240, 220), target: port });
    port.dispatch('pointerup', { ...pointer('pointerup', 1, 240, 220), target: port });

    assert.equal(calls.some(c => c[0] === 'touchBeginEdgeDraw'), false, 'read-only never draws');
    assert.equal(calls.some(c => c[0] === 'touchBeginPan'), false,
        'a port press is a control press, not an R1 pan');
    assert.equal(calls.some(c => c[0] === 'touchBeginNodeDrag'), false, 'read-only never drags');
});

// ── J1: a pinch from the empty canvas survives the pan hand-over (real capture model) ────────

const pt = (type, id, x, y) => ({
    type, pointerId: id, pointerType: 'touch', isPrimary: id === 1, button: 0, buttons: 1,
    clientX: x, clientY: y, target: null, preventDefault() {}, stopPropagation() {},
});

test('a pinch from the EMPTY canvas after finger one moved survives the pan hand-over', async () => {
    const dom = captureDom();
    const container = dom.node(null);
    const svg = dom.node(container, { cls: ['tm-diagram-canvas__svg'] });
    const calls = [];
    const engine = new Proxy({}, {
        get: (_, k) => (...a) => {
            calls.push(k);
            return k === 'touchPinchBegin' ? {} : (k === 'touchContextMenuTarget' || k === 'touchPortAt') ? null : true;
        },
    });
    const inst = { container, svg, readOnly: false, selectedIds: new Set(), dotNetRef: { invokeMethodAsync: () => Promise.resolve() } };
    await touch.attachTouch(engine, inst, { drag: dragHelpers, gesture: gestureHelpers });

    dom.send('pointerdown', { ...pt('pointerdown', 1, 300, 300), target: svg });
    dom.send('pointermove', { ...pt('pointermove', 1, 302, 300), target: svg });   // the capture goes live
    dom.send('pointerdown', { ...pt('pointerdown', 2, 400, 300), target: svg });
    dom.send('pointermove', { ...pt('pointermove', 1, 250, 300), target: svg });
    dom.send('pointermove', { ...pt('pointermove', 2, 450, 300), target: svg });
    dom.send('pointerup', { ...pt('pointerup', 1, 250, 300), target: svg });
    dom.send('pointerup', { ...pt('pointerup', 2, 450, 300), target: svg });

    assert.equal(calls.includes('touchPinchCancel'), false,
        'the pan hand-over must not roll the pinch back');
    assert.ok(calls.includes('touchPinchEnd'), 'the pinch commits');
});

test('a pinch after a started pan hands over: touchCancelPan then touchPinchEnd', async () => {
    const dom = captureDom();
    const container = dom.node(null);
    const svg = dom.node(container, { cls: ['tm-diagram-canvas__svg'] });
    const calls = [];
    const engine = new Proxy({}, {
        get: (_, k) => (...a) => {
            calls.push(k);
            return k === 'touchPinchBegin' ? {} : (k === 'touchContextMenuTarget' || k === 'touchPortAt') ? null : true;
        },
    });
    const inst = { container, svg, readOnly: false, selectedIds: new Set(), dotNetRef: { invokeMethodAsync: () => Promise.resolve() } };
    await touch.attachTouch(engine, inst, { drag: dragHelpers, gesture: gestureHelpers });

    dom.send('pointerdown', { ...pt('pointerdown', 1, 300, 300), target: svg });
    dom.send('pointermove', { ...pt('pointermove', 1, 320, 300), target: svg });   // the pan started
    dom.send('pointerdown', { ...pt('pointerdown', 2, 400, 300), target: svg });
    dom.send('pointermove', { ...pt('pointermove', 1, 250, 300), target: svg });
    dom.send('pointermove', { ...pt('pointermove', 2, 450, 300), target: svg });
    dom.send('pointerup', { ...pt('pointerup', 1, 250, 300), target: svg });
    dom.send('pointerup', { ...pt('pointerup', 2, 450, 300), target: svg });

    assert.ok(calls.includes('touchCancelPan'), 'the pan was cancelled by the hand-over');
    assert.ok(calls.indexOf('touchCancelPan') < calls.indexOf('touchPinchEnd'),
        'the cancel precedes the pinch commit');
    assert.equal(calls.includes('touchPinchCancel'), false, 'the pinch itself was never cancelled');
});

// ── Review fix: a tap on a quick-connect arrow clicks the button ────────────
// The arrows are HTML inside an SVG foreignObject; Chromium never delivers the touch's
// compatibility click to the button there, so a dedicated tap session (10px touch slop)
// clicks the button the press landed on.

/** One quick-connect arrow button on the rig's node; returns the arrow plus a click counter. */
function addArrow(rigOut) {
    const clicks = [];
    const arrowsG = el({ tag: 'g', attrs: { class: 'tm-diagram-connect-arrows', 'data-node-id': 'n1' }, parent: rigOut.inst.svg });
    const arrow = el({ tag: 'button', attrs: { class: 'tm-diagram-connect-arrow tm-diagram-connect-arrow--e' }, parent: arrowsG });
    arrow.click = () => clicks.push('click');
    return { arrow, clicks };
}

test('a tap on a quick-connect arrow clicks the button (no move, no drag)', async () => {
    const rigOut = await rig();
    const { arrow, clicks } = addArrow(rigOut);

    const down = pointer('pointerdown', 1, 300, 300);
    down.target = arrow;
    arrow.dispatch('pointerdown', down);
    arrow.dispatch('pointerup', { ...pointer('pointerup', 1, 300, 300), target: arrow });

    assert.deepEqual(clicks, ['click'], 'a tap on the arrow must click the button (the connect dialog opens)');
    assert.ok(down.prevented, 'the claimed press is preventDefaulted so the missing native click cannot double-fire');
});

test('a finger that drifts 3px before the lift still taps the arrow (touch slop)', async () => {
    const rigOut = await rig();
    const { arrow, clicks } = addArrow(rigOut);

    const down = pointer('pointerdown', 1, 300, 300);
    down.target = arrow;
    arrow.dispatch('pointerdown', down);
    arrow.dispatch('pointermove', { ...pointer('pointermove', 1, 303, 301), target: arrow });
    arrow.dispatch('pointerup', { ...pointer('pointerup', 1, 303, 301), target: arrow });

    assert.deepEqual(clicks, ['click'],
        'a 3px drift is touch slop, not a drag — the button must still activate');
});

test('a drag that starts on a quick-connect arrow never clicks the button', async () => {
    const rigOut = await rig();
    const { arrow, clicks } = addArrow(rigOut);

    const down = pointer('pointerdown', 1, 300, 300);
    down.target = arrow;
    arrow.dispatch('pointerdown', down);
    arrow.dispatch('pointermove', { ...pointer('pointermove', 1, 340, 300), target: arrow });
    arrow.dispatch('pointerup', { ...pointer('pointerup', 1, 340, 300), target: arrow });

    assert.deepEqual(clicks, [], 'a press that moved is not a tap — the button must not click (no accidental dialog)');
});

test('the trailing compatibility click after an arrow tap is swallowed for ~350ms, near the tap point only', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const rigOut = await rig();
        const { arrow } = addArrow(rigOut);
        let prevented = 0;
        const clickAt = (x, y) => docBag.dispatch('click', {
            clientX: x, clientY: y,
            preventDefault() { prevented++; },
            stopPropagation() { },
        });

        const down = pointer('pointerdown', 1, 300, 300);
        down.target = arrow;
        arrow.dispatch('pointerdown', down);
        arrow.dispatch('pointerup', { ...pointer('pointerup', 1, 300, 300), target: arrow });

        // 50 ms later, at the lift point (under the freshly opened dialog): swallowed.
        await sleep(50);
        clickAt(300, 300);
        assert.equal(prevented, 1, 'the trailing compatibility click at the tap point is swallowed');

        // A click far away from the tap point is never touched by the guard.
        clickAt(600, 500);
        assert.equal(prevented, 1, 'clicks outside the ~30px radius pass untouched');

        // …and the first swallowed click disarms the guard: exactly ONE click is ever eaten.
        clickAt(305, 300);
        assert.equal(prevented, 1, 'the guard is removed by the first swallowed click — the next click passes');

        await sleep(400);
        clickAt(300, 300);
        assert.equal(prevented, 1, 'long after, with the guard already gone, the click passes');
    } finally {
        delete globalThis.document;
    }
});

test('any new pointerdown disarms the arrow-tap trailing-click swallow', async () => {
    const docBag = bag();
    globalThis.document = docBag;
    try {
        const rigOut = await rig();
        const { arrow } = addArrow(rigOut);
        let prevented = 0;

        const down = pointer('pointerdown', 1, 300, 300);
        down.target = arrow;
        arrow.dispatch('pointerdown', down);
        arrow.dispatch('pointerup', { ...pointer('pointerup', 1, 300, 300), target: arrow });

        // The user presses again immediately (e.g. an intentional dialog click): the press
        // itself disarms the guard — the next click must pass.
        docBag.dispatch('pointerdown', { pointerId: 2, pointerType: 'mouse' });
        docBag.dispatch('click', {
            clientX: 300, clientY: 300,
            preventDefault() { prevented++; },
            stopPropagation() { },
        });
        assert.equal(prevented, 0, 'a real press clears the swallow window — the next click passes');
    } finally {
        delete globalThis.document;
    }
});
