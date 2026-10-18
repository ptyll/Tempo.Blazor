// Engine (classic script) unit tests: diagram-editor.js evaluated in a vm context.
// Phase 1 (read-only): setReadOnly mid-gesture rolls the node drag back and reports
// OnDragCancelled, cancels the touch editing sessions and the rotation; the mouse down path
// inspects (selects) in read-only while the pan tool still pans.
import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm'; import fs from 'node:fs'; import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const SRC_PATH = path.resolve(here, '../diagram-editor.js');
const SRC = fs.readFileSync(SRC_PATH, 'utf8');
/** Evaluates the classic script in a fresh context; returns window.tmDiagramEditor. */
function loadEngine({ document = {}, window: win = {}, extra = {} } = {}) {
    const window = Object.assign({ innerWidth: 1280, innerHeight: 800 }, win);
    const ctx = vm.createContext({ window, document: { addEventListener() {}, removeEventListener() {}, ...document },
        URL, console, setTimeout, clearTimeout, performance, ...extra });
    vm.runInContext(SRC, ctx, { filename: SRC_PATH });
    return window.tmDiagramEditor;
}

function fakeInst(overrides = {}) {
    const log = [];
    return {
        inst: Object.assign({
            id: 'c1',
            container: { id: 'c1', style: {}, focus() {}, contains: () => true, classList: { toggle() {} } },
            readOnly: false,
            isDragging: false,
            dragNodeIds: [],
            dragStartPositions: {},
            selectedIds: new Set(),
            spaceHeld: false,
            toolMode: 'select',
            isRotating: false,
            isPanning: false,
            dotNetRef: { invokeMethodAsync: (name, ...args) => { log.push([name, ...args]); return Promise.resolve(); } },
        }, overrides),
        log,
    };
}

/** The engine runs in a vm realm: its Array prototype differs, so deep compares go through JSON. */
const plain = (value) => JSON.parse(JSON.stringify(value));

test('touchCancelNodeDrag after OnDragStarted reports OnDragCancelled', () => {
    const engine = loadEngine();
    const calls = [];
    const inst = {
        isDragging: false,
        selectedIds: new Set(['n1']),
        dotNetRef: { invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); } },
        scenePane: null,
    };
    engine._nodeRect = () => ({ x: 1, y: 2, w: 3, h: 4 });
    engine._includeGroupNodes = (_inst, ids) => ids;
    engine._setNodeTranslate = () => {};
    engine._clearGuideLines = () => {};
    engine._updateSelectionTransforms = () => {};

    const nodeEl = { getAttribute: n => n === 'data-node-id' ? 'n1' : 'false' };
    engine.touchBeginNodeDrag(inst, nodeEl, 0, 0);
    assert.equal(inst.isDragging, true, 'the drag armed');
    assert.ok(inst.dragStartPositions.n1 && inst.dragStartPositions.n1.w === 3 && inst.dragStartPositions.n1.h === 4,
        'the touch start snapshot carries w/h (the snap guides counted the node 0x0 before)');

    engine.touchCancelNodeDrag(inst);
    assert.equal(inst.isDragging, false, 'the drag rolled back');
    assert.deepEqual(calls.map(c => c[0]), ['OnDragStarted', 'OnDragCancelled'],
        'C# must learn the drag died so ShouldRender() resumes');
});

test('setReadOnly(true) mid node drag restores positions and reports OnDragCancelled', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({
        isDragging: true,
        dragNodeIds: ['n1'],
        dragStartPositions: { n1: { x: 10, y: 20 } },
    });
    const translates = [];
    engine._setNodeTranslate = (_i, id, x, y) => translates.push([id, x, y]);
    engine._clearGuideLines = () => {};
    engine._updateSelectionTransforms = () => {};
    engine.instances.set('c1', inst);

    engine.setReadOnly({ id: 'c1' }, true);

    assert.deepEqual(plain(translates), [['n1', 10, 20]], 'the dragged node returned to its start position');
    assert.ok(log.some(entry => entry[0] === 'OnDragCancelled'), 'C# learns the drag was cancelled');
    assert.equal(inst.isDragging, false);
});

test('setReadOnly(true) calls the touch cancelEditing hook', () => {
    const engine = loadEngine();
    const reasons = [];
    const spy = (reason) => reasons.push(reason);
    const { inst } = fakeInst({
        __touchDetach: Object.assign(() => {}, { cancelEditing: spy }),
    });
    engine.instances.set('c1', inst);

    engine.setReadOnly({ id: 'c1' }, true);

    assert.deepEqual(reasons, ['read-only'], 'the touch editing sessions are cancelled with the reason');
});

test('setReadOnly(true) mid rotation restores the start angle', () => {
    const engine = loadEngine();
    const rotations = [];
    const { inst } = fakeInst({
        isRotating: true,
        rotateNodeId: 'n1',
        rotateStartNodeRotation: 15,
    });
    engine._applyNodeRotation = (_i, id, angle) => rotations.push([id, angle]);
    engine.instances.set('c1', inst);

    engine.setReadOnly({ id: 'c1' }, true);

    assert.deepEqual(rotations, [['n1', 15]], 'the rotation rolled back to its start angle');
    assert.equal(inst.isRotating, false);
});

test('setReadOnly(false) cancels nothing', () => {
    const engine = loadEngine();
    let cancelEditingCalls = 0;
    const { inst, log } = fakeInst({
        __touchDetach: Object.assign(() => {}, { cancelEditing: () => { cancelEditingCalls++; } }),
    });
    engine.instances.set('c1', inst);

    engine.setReadOnly({ id: 'c1' }, false);

    assert.equal(cancelEditingCalls, 0, 'unlocking never cancels sessions');
    assert.equal(log.filter(entry => entry[0] === 'OnDragCancelled').length, 0);
});

test('_onMouseDown in read-only selects the clicked node without arming a drag', () => {
    const engine = loadEngine();
    const nodeEl = { getAttribute: n => n === 'data-node-id' ? 'n1' : null };
    const { inst, log } = fakeInst({ readOnly: true });
    engine._isNodeInActiveGroup = () => true;
    engine._updateSelection = () => {};

    const e = {
        button: 0,
        target: { closest: s => s === '[data-node-id]' ? nodeEl : null },
        preventDefault() {},
    };
    engine._onMouseDown(e, inst);

    assert.deepEqual(plain(log), [['OnSelectionChanged', ['n1']]], 'the node is selected for inspection');
    assert.equal(inst.isDragging, false, 'no drag is armed');
    assert.equal(log.some(entry => entry[0] === 'OnDragStarted'), false);
});

test('_onMouseDown in read-only with the Pan tool pans', () => {
    const engine = loadEngine();
    const { inst } = fakeInst({ readOnly: true, toolMode: 'pan' });
    engine._getViewBox = () => ({ x: 0, y: 0, w: 800, h: 500 });

    const e = {
        button: 0,
        clientX: 10,
        clientY: 10,
        target: { closest: () => null },
        preventDefault() {},
    };
    engine._onMouseDown(e, inst);

    assert.equal(inst.isPanning, true, 'the left button pans in read-only with the Pan tool');
});

test('_onMouseDown in read-only clears the selection on the empty surface', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ readOnly: true, selectedIds: new Set(['n1']) });
    engine._updateSelection = () => {};

    const e = {
        button: 0,
        target: { closest: () => null },
        preventDefault() {},
    };
    engine._onMouseDown(e, inst);

    assert.deepEqual(plain(log), [['OnSelectionChanged', []]], 'an empty-surface click deselects');
});

test('selectNodeForInspection toggles with additive and keeps a single pick stable', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst();
    const n1 = { getAttribute: n => n === 'data-node-id' ? 'n1' : null };
    const n2 = { getAttribute: n => n === 'data-node-id' ? 'n2' : null };
    engine._isNodeInActiveGroup = () => true;
    engine._updateSelection = () => {};

    assert.equal(engine.selectNodeForInspection(inst, n1, false), true);
    assert.deepEqual(plain([...inst.selectedIds]), ['n1']);

    // Clicking the only selected node again is a no-op (the selection stays stable).
    engine.selectNodeForInspection(inst, n1, false);
    assert.deepEqual(plain([...inst.selectedIds]), ['n1']);

    // Ctrl+click adds.
    engine.selectNodeForInspection(inst, n2, true);
    assert.deepEqual([...inst.selectedIds].sort(), ['n1', 'n2']);

    // Ctrl+click again removes.
    engine.selectNodeForInspection(inst, n2, true);
    assert.deepEqual(plain([...inst.selectedIds]), ['n1']);

    assert.equal(log.filter(entry => entry[0] === 'OnSelectionChanged').length, 3,
        'the no-op pick did not report a selection change');
});

test('selectNodeForInspection refuses nodes outside the active group', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst();
    const n1 = { getAttribute: n => n === 'data-node-id' ? 'n1' : null };
    engine._isNodeInActiveGroup = () => false;

    assert.equal(engine.selectNodeForInspection(inst, n1, false), false);
    assert.equal(inst.selectedIds.size, 0);
    assert.equal(log.length, 0);
});

test('selectEdgeForInspection selects the tapped edge once', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst();
    engine._updateSelection = () => {};
    const edgeEl = {
        closest: s => s === '[data-edge-id]' ? edgeEl : null,
        getAttribute: n => n === 'data-edge-id' ? 'e1' : null,
    };

    assert.equal(engine.selectEdgeForInspection(inst, edgeEl), true);
    assert.deepEqual(plain([...inst.selectedIds]), ['e1']);
    assert.deepEqual(plain(log), [['OnSelectionChanged', ['e1']]]);

    // Tapping the already-selected edge is a no-op (no duplicate notification).
    engine.selectEdgeForInspection(inst, edgeEl);
    assert.equal(log.length, 1);
});

test('selectEdgeForInspection refuses an element without an edge id', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst();
    const stray = { closest: () => null, getAttribute: () => null };

    assert.equal(engine.selectEdgeForInspection(inst, stray), false);
    assert.equal(log.length, 0);
});

// R1 review: read-only keeps the Select tool reachable — the toolbar button and the mobile
// tile are enabled again, and the KeyV shortcut still switches to select.
test('KeyV stays live in read-only and switches to the select tool', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ readOnly: true });
    inst.container.contains = () => true;

    engine._onKeyDown({
        key: 'v', code: 'KeyV', ctrlKey: false, metaKey: false,
        preventDefault() {}, target: { matches: () => false },
    }, inst);

    assert.equal(inst.toolMode, 'select', 'V selects the inspection tool in read-only');
    assert.ok(log.some(entry => entry[0] === 'OnToolModeChanged' && entry[1] === 'select'));
});

test('KeyL in read-only does not arm the edge tool', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ readOnly: true });
    inst.container.contains = () => true;

    engine._onKeyDown({
        key: 'l', code: 'KeyL', ctrlKey: false, metaKey: false,
        preventDefault() {}, target: { matches: () => false },
    }, inst);

    assert.equal(inst.toolMode, 'select', 'the edge tool is an editing path');
    assert.equal(log.some(entry => entry[0] === 'OnToolModeChanged'), false);
});

test('_onMouseDown in read-only focuses the canvas container', () => {
    const engine = loadEngine();
    const focuses = [];
    const nodeEl = { getAttribute: n => n === 'data-node-id' ? 'n1' : null };
    const { inst, log } = fakeInst({ readOnly: true });
    inst.container.focus = (opts) => focuses.push(opts);
    engine._isNodeInActiveGroup = () => true;
    engine._updateSelection = () => {};

    const e = {
        button: 0,
        target: { closest: s => s === '[data-node-id]' ? nodeEl : null },
        preventDefault() {},
    };
    engine._onMouseDown(e, inst);

    assert.deepEqual(plain(focuses), [{ preventScroll: true }],
        'keyboard panning/Escape need the canvas focus even while read-only');
    assert.deepEqual(plain(log), [['OnSelectionChanged', ['n1']]]);
});
