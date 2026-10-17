// diagram-editor-touch.js on the shared helpers (DiagramEditor redesign, phase 6). The real
// pointer-drag.js and pointer-gesture.js run against a hand-rolled DOM: a finger drag on a node
// selects and moves it, a finger on a resize/rotate handle resizes/rotates (never drags the
// node), a 600ms hold opens the context menu instead of dragging, two fingers pinch/pan, and
// read-only keeps the navigation gestures while vetoing every editing gesture.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
// The real helpers, from the CORE source tree (four levels up from this folder: js/__tests__
// → js → wwwroot → package → src), with the trailing slash a URL base needs.
const coreUrl = pathToFileURL(path.resolve(here, '../../../../Tempo.Blazor/wwwroot/js/') + path.sep).href;
const dragHelpers = await import(new URL('pointer-drag.js', coreUrl).href);
const gestureHelpers = await import(new URL('pointer-gesture.js', coreUrl).href);
// The module under test resolves its own helpers from import.meta.url; the test injects the
// same pair explicitly so the run never depends on the deployed _content URL layout.
const touch = await import('../diagram-editor-touch.js');

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
    };

    const container = el({ tag: 'div', attrs: { id: 'tmd-test' } });
    const svg = el({ tag: 'svg', attrs: { class: 'tm-diagram-canvas__svg' }, parent: container });
    const node = el({ tag: 'g', attrs: { 'data-node-id': 'n1', 'data-locked': 'false' }, parent: svg });
    const handlesG = el({ tag: 'g', attrs: { class: 'tm-diagram-node-handles', 'data-node-id': 'n1' }, parent: svg });
    const hit = el({ tag: 'rect', attrs: { class: 'tm-diagram-resize-handle-hit', 'data-resize-handle': 'se' }, parent: handlesG });
    const rotateHit = el({ tag: 'rect', attrs: { class: 'tm-diagram-rotate-handle-hit' }, parent: handlesG });
    const edgeHit = el({ tag: 'path', attrs: { class: 'tm-diagram-edge-hit-path', 'data-edge-id': 'e1' }, parent: svg });

    const inst = {
        container,
        svg,
        dotNetRef,
        readOnly,
        selectedIds: new Set(),
        __touchCalls: calls,
    };

    const detach = await touch.attachTouch(engine, inst);
    return { inst, calls, container, node, hit, rotateHit, edgeHit, handlesG, detach };
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

    const hitDown = pointer('pointerdown', 2, 200, 200);
    hitDown.target = hit;
    hit.dispatch('pointerdown', hitDown);
    hit.dispatch('pointerup', { ...pointer('pointerup', 2, 200, 200), target: hit });
    assert.equal(calls.some(c => c[0] === 'OnResizeStarted'), false, 'read-only never resizes');
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
