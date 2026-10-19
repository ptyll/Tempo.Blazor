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
/** Evaluates the classic script in a fresh context; returns window.tmDiagramEditor.
 *  The caller's `document` object is used BY IDENTITY (its live state — activeElement —
 *  stays observable); only the listener methods are defaulted in when missing. */
function loadEngine({ document = null, window: win = {}, extra = {} } = {}) {
    const window = Object.assign({ innerWidth: 1280, innerHeight: 800 }, win);
    const doc = document ?? {};
    if (typeof doc.addEventListener !== 'function') doc.addEventListener = () => {};
    if (typeof doc.removeEventListener !== 'function') doc.removeEventListener = () => {};
    const ctx = vm.createContext({ window, document: doc,
        URL, console, setTimeout, clearTimeout, performance, ...extra });
    vm.runInContext(SRC, ctx, { filename: SRC_PATH });
    return window.tmDiagramEditor;
}

function fakeInst(overrides = {}) {
    const log = [];
    return {
        inst: Object.assign({
            id: 'c1',
            container: { id: 'c1', style: {}, focus() {}, contains: () => true, closest: () => null, classList: { toggle() {} } },
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

// J2: the touch module URL must resolve even when document.currentScript is null
// (type="module", a loader or an injected script) — a bare '_content/...' string is not a
// module specifier: import() would throw TypeError and touch would never work.
test('the touch module URL comes from document.currentScript for a classic script', () => {
    const engine = loadEngine({
        document: { currentScript: { src: 'https://h/app/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js' } },
    });
    assert.equal(engine._touchModuleUrl,
        'https://h/app/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor-touch.js');
});

test('without currentScript the URL is resolved from our own <script> tag (type=module, fingerprinted)', () => {
    const engine = loadEngine({
        document: {
            currentScript: null,
            querySelectorAll: () => [{ src: 'https://h/x/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.ab12cd.js' }, { src: 'https://h/x/other.js' }],
        },
    });
    assert.equal(engine._touchModuleUrl,
        'https://h/x/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor-touch.js');
});

test('without currentScript the URL never matches the -touch module itself or an unrelated tag', () => {
    const engine = loadEngine({
        document: {
            currentScript: null,
            querySelectorAll: () => [{ src: 'https://h/x/other.js' }, { src: 'https://h/x/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor-touch.js' }],
            baseURI: 'https://h/x/',
        },
    });
    assert.equal(engine._touchModuleUrl,
        'https://h/x/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor-touch.js',
        'the -touch.js tag itself is skipped; an earlier real tag (or the fallback) wins');
});

test('the last resort is absolute against document.baseURI (never a bare specifier)', () => {
    const engine = loadEngine({
        document: { currentScript: null, querySelectorAll: () => [], baseURI: 'https://h/sub/' },
    });
    assert.equal(engine._touchModuleUrl,
        'https://h/sub/_content/Tempo.Blazor.DiagramEditor/js/diagram-editor-touch.js');
    assert.doesNotThrow(() => new URL(engine._touchModuleUrl), 'import() needs an absolute URL');
});

// J7: destroy() while the touch module is loading must not attach sessions onto the dead
// canvas, a late-resolving attach must detach immediately, and a rejected attach must not
// surface as an unhandled rejection. The tests stub the _importTouchModule seam.
function touchLoadInst() {
    return {
        id: 'c1',
        container: { id: 'c1', style: {}, addEventListener() {}, removeEventListener() {} },
        __touchDetach: null,
        selectionOutlines: [],
    };
}

/** Microtask flush: the _loadTouchModule chain defers its import behind Promise.resolve().then. */
const flushTicks = async (n = 6) => { for (let i = 0; i < n; i++) await Promise.resolve(); };

test('destroy while the touch module is loading attaches nothing', async () => {
    const engine = loadEngine();
    let resolveImport;
    engine._importTouchModule = () => new Promise(res => { resolveImport = res; });
    let attaches = 0;
    const inst = touchLoadInst();
    engine.instances.set('c1', inst);
    const loading = engine._loadTouchModule(inst);
    await flushTicks(1);                       // the deferred _importTouchModule call runs
    engine.destroy({ id: 'c1' });
    resolveImport({ attachTouch: () => { attaches++; return Promise.resolve(() => {}); } });
    await loading;
    assert.equal(attaches, 0, 'a destroy() during the import must prevent the attach');
    assert.equal(inst.__touchDetach, null);
});

test('destroy after attachTouch started but before it resolved detaches the late sessions', async () => {
    const engine = loadEngine();
    let resolveAttach;
    engine._importTouchModule = () => Promise.resolve({
        attachTouch: () => new Promise(res => { resolveAttach = res; }),
    });
    let detached = false;
    const inst = touchLoadInst();
    engine.instances.set('c1', inst);
    const loading = engine._loadTouchModule(inst);
    await flushTicks();                        // the import resolves and attachTouch starts
    engine.destroy({ id: 'c1' });
    resolveAttach(() => { detached = true; });
    await loading;
    assert.equal(detached, true, 'the late detach ran immediately instead of parking on the dead instance');
    assert.equal(inst.__touchDetach, null);
});

test('a rejected attachTouch is caught (no unhandled rejection) and the promise resolves', async () => {
    const engine = loadEngine();
    engine._importTouchModule = () => Promise.resolve({
        attachTouch: () => Promise.reject(new Error('boom')),
    });
    const inst = touchLoadInst();
    engine.instances.set('c1', inst);
    await assert.doesNotReject(() => engine._loadTouchModule(inst));
    assert.equal(inst.__touchLoading ?? null, null, 'the loading marker is cleared in every path');
});

test('_loadTouchModule twice while loading returns the same promise and attaches once', async () => {
    const engine = loadEngine();
    let imports = 0;
    engine._importTouchModule = () => {
        imports++;
        return Promise.resolve({ attachTouch: () => Promise.resolve(() => {}) });
    };
    const inst = touchLoadInst();
    engine.instances.set('c1', inst);
    const first = engine._loadTouchModule(inst);
    const second = engine._loadTouchModule(inst);
    assert.equal(first, second, 'a concurrent second call returns the in-flight promise');
    await second;
    assert.equal(imports, 1, 'the module is imported once');
    assert.equal(typeof inst.__touchDetach, 'function', 'the detach is parked on the instance');
    const third = engine._loadTouchModule(inst);
    assert.equal(imports, 1, 'an attached instance never reloads');
    await third;
});

// ── J8: one startRotate (no duplicate keys), guarded re-entry, no listener leak ─────────────

test('tmDiagramEditor has no duplicate method keys', () => {
    const start = SRC.indexOf('window.tmDiagramEditor = {');
    const body = SRC.slice(start, SRC.indexOf('\n};', start));
    const keys = [...body.matchAll(/^    ([A-Za-z_$][\w$]*)\s*:/gm)].map(m => m[1]);
    assert.ok(keys.length > 100, 'the scan must see the engine object');
    assert.deepEqual(keys.filter((k, i) => keys.indexOf(k) !== i), []);
});

/** A document stub whose add/removeEventListener calls are observable. */
function listenerDocument() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, e = {}) { for (const fn of [...(map.get(type) ?? [])]) fn(e); },
        count(type) { return (map.get(type) ?? []).length; },
    };
}

/** Engine with the geometry stubs a mouse rotation session needs. */
function rotateEngine(doc) {
    const engine = loadEngine({ document: doc });
    const { inst, log } = fakeInst();
    engine.instances.set('c1', inst);
    engine._nodeRect = () => ({ x: 0, y: 0, w: 100, h: 50 });
    engine._screenToDoc = (_i, x, y) => ({ x, y });
    engine._nodeEl = () => ({ getAttribute: n => n === 'data-stencil-id' ? 'rect' : null });
    engine._getNodeRotation = () => 45;
    engine._applyNodeRotation = (_i, id, angle) => log.push(['_applyNodeRotation', id, angle]);
    return { engine, inst, log };
}

test('startRotate mouse: move rotates, up commits OnRotateEnded once and removes both listeners', () => {
    const doc = listenerDocument();
    const { engine, inst, log } = rotateEngine(doc);

    engine.startRotate({ id: 'c1' }, 'n1', 10, 10, 15, 0);
    assert.equal(inst.isRotating, true, 'the rotation session started');
    assert.equal(doc.count('mousemove'), 1);
    assert.equal(doc.count('mouseup'), 1);

    doc.dispatch('mousemove', { clientX: 30, clientY: 10, preventDefault() {} });
    assert.ok(log.some(entry => entry[0] === '_applyNodeRotation'), 'the move rotates the node');

    doc.dispatch('mouseup', {});
    assert.equal(inst.isRotating, false);
    assert.equal(log.filter(entry => entry[0] === 'OnRotateEnded').length, 1, 'exactly one OnRotateEnded');
    assert.equal(doc.count('mousemove'), 0, 'the mousemove listener is gone');
    assert.equal(doc.count('mouseup'), 0, 'the mouseup listener is gone');
});

test('a second startRotate during a rotation adds no listeners', () => {
    const doc = listenerDocument();
    const { engine } = rotateEngine(doc);

    engine.startRotate({ id: 'c1' }, 'n1', 10, 10, 15, 0);
    engine.startRotate({ id: 'c1' }, 'n2', 10, 10, 0, 0);
    assert.equal(doc.count('mousemove'), 1, 'the re-entrant press was ignored');
    assert.equal(doc.count('mouseup'), 1);
});

test('_cancelRotateSession during a mouse rotation restores the angle and removes the listeners; a later mouseup commits nothing', () => {
    const doc = listenerDocument();
    const { engine, inst, log } = rotateEngine(doc);

    engine.startRotate({ id: 'c1' }, 'n1', 10, 10, 15, 0);
    engine._cancelRotateSession(inst);
    assert.deepEqual(
        log.filter(entry => entry[0] === '_applyNodeRotation').map(entry => entry[2]),
        [15],
        'the start rotation was restored');
    assert.equal(doc.count('mousemove'), 0, 'cancel removes the mousemove listener');
    assert.equal(doc.count('mouseup'), 0, 'cancel removes the mouseup listener');

    doc.dispatch('mouseup', {});
    assert.equal(log.filter(entry => entry[0] === 'OnRotateEnded').length, 0,
        'a cancelled session never commits a rotate end');
});

test('destroy during a mouse rotation cancels the session and removes the listeners', () => {
    const doc = listenerDocument();
    const { engine, inst } = rotateEngine(doc);
    inst.container.addEventListener = () => {};
    inst.container.removeEventListener = () => {};
    inst.rubberEl = null;
    inst.selectionOutlines = [];
    engine._clearSelectionOutlines = () => {};

    engine.startRotate({ id: 'c1' }, 'n1', 10, 10, 15, 0);
    engine.destroy({ id: 'c1' });
    assert.equal(inst.isRotating, false, 'destroy cancels the rotation');
    assert.equal(doc.count('mousemove'), 0, 'destroy removes the mousemove listener');
    assert.equal(doc.count('mouseup'), 0, 'destroy removes the mouseup listener');
});

// ── J9/R3: shared zoom limits 0.15–5.0 — clamp, never freeze ────────────────────────────────

/** An svg stub whose viewBox attribute is read back through baseVal, like a real SVGElement. */
function fakeSvg() {
    let vb = '0 0 800 500';
    return {
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 500 }),
        setAttribute(name, value) { if (name === 'viewBox') vb = value; },
        get viewBox() {
            const self = this;
            const [x, y, w, h] = vb.split(' ').map(Number);
            return { baseVal: { x, y, width: w, height: h, toString: () => vb, self } };
        },
    };
}

function zoomInst(vb = '0 0 800 500') {
    const calls = [];
    const inst = {
        id: 'z1',
        svg: fakeSvg(),
        container: { id: 'z1', style: {} },
        canvasW: 3000,
        canvasH: 2000,
        scenePane: null,
        dotNetRef: { invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); } },
    };
    if (vb !== '0 0 800 500') inst.svg.setAttribute('viewBox', vb);
    return { inst, calls };
}

const readVb = inst => inst.svg.viewBox.baseVal;

test('a pinch past MAX_SCALE clamps to exactly 5 and keeps panning', () => {
    const engine = loadEngine();
    const { inst } = zoomInst();
    engine.instances.set('z1', inst);

    engine.touchPinchChange(inst, { x: 0, y: 0, w: 800, h: 500 }, 8, 400, 250, 0, 0);
    assert.equal(readVb(inst).width, 160, 'scale 8 clamps to 5 (800/160)');

    engine.touchPinchChange(inst, { x: 0, y: 0, w: 800, h: 500 }, 8, 400, 250, 30, 0);
    assert.equal(readVb(inst).width, 160, 'the width stays at the limit');
    assert.ok(Math.abs(readVb(inst).x - 290) < 1e-9, 'the two-finger pan still moves x by dx*scale = 6');
});

test('a pinch past MIN_SCALE clamps to exactly 0.15', () => {
    const engine = loadEngine();
    const { inst } = zoomInst();
    engine.instances.set('z1', inst);

    engine.touchPinchChange(inst, { x: 0, y: 0, w: 800, h: 500 }, 0.001, 400, 250, 0, 0);
    const vb = readVb(inst);
    assert.ok(Math.abs(vb.width - 800 / 0.15) < 1e-6, 'scale 0.001 clamps to 0.15');
});

test('wheel at MAX_SCALE is a no-op; one tick before the limit lands exactly on it', () => {
    const engine = loadEngine();
    engine._screenToDoc = (_i, x, y) => ({ x, y });
    const { inst } = zoomInst('0 0 160 100');       // curScale = 800/160 = 5 = MAX
    engine.instances.set('z1', inst);

    const wheelIn = { clientX: 400, clientY: 250, deltaY: -100, preventDefault() {}, target: { closest: () => null } };
    engine._onWheel(wheelIn, inst);
    assert.equal(readVb(inst).width, 160, 'a wheel tick at the limit changes nothing');

    inst.svg.setAttribute('viewBox', '0 0 177.77777777777777 111.11111111111111');   // curScale = 4.5
    engine._onWheel({ ...wheelIn, deltaY: 0 }, inst);
    assert.ok(Math.abs(readVb(inst).width - 160) < 1e-9, 'the tick lands exactly on the limit');
});

test('wheel at MIN_SCALE is a no-op; one tick before the limit lands exactly on it', () => {
    const engine = loadEngine();
    engine._screenToDoc = (_i, x, y) => ({ x, y });
    const { inst } = zoomInst('0 0 5333.333333333333 3333.3333333333335');   // curScale = 0.15 = MIN
    engine.instances.set('z1', inst);

    const wheelOut = { clientX: 400, clientY: 250, deltaY: 100, preventDefault() {}, target: { closest: () => null } };
    engine._onWheel(wheelOut, inst);
    assert.ok(Math.abs(readVb(inst).width - 5333.333333333333) < 1e-6, 'a wheel tick at the minimum changes nothing');

    inst.svg.setAttribute('viewBox', '0 0 5083.333333333333 3177.0833333333335');   // curScale ≈ 0.157
    engine._onWheel(wheelOut, inst);
    assert.ok(Math.abs(readVb(inst).width - 800 / 0.15) < 1e-6, 'the tick lands exactly on the minimum');
});

test('zoomTo clamps its argument', () => {
    const engine = loadEngine();
    const { inst } = zoomInst();
    engine.instances.set('z1', inst);

    engine.zoomTo({ id: 'z1' }, 10);
    assert.equal(readVb(inst).width, 160, 'zoomTo(10) clamps to 5');
});

test('fitToView clamps its fit scale', () => {
    const engine = loadEngine();
    const { inst } = zoomInst();
    engine.instances.set('z1', inst);
    // A tiny content box would fit at scale > 5 without the clamp: content 20x20 + padding 80 →
    // 800/180 ≈ 4.4 — use an even smaller box to exceed 5 and assert the clamp.
    inst.scenePane = { querySelectorAll: () => [{
        getAttribute: n => n === 'data-node-id' ? 'n1' : null,
    }] };
    engine._nodeRect = () => ({ x: 0, y: 0, w: 4, h: 4 });

    const fit = engine.fitToView({ id: 'z1' }, 40);
    assert.ok(fit <= 5.0, 'fitToView never exceeds MAX_SCALE');
});

// ── J10: one measured containing block for the fixed popovers ───────────────────────────────

/** A fake editor root: querySelector returns the anchor, getBoundingClientRect the editor box,
 *  appendChild keeps the probe so the test can choose what it measures. */
function fakeRoot({ editorRect = { left: 300, top: 120, width: 980, height: 680 }, anchorRect = { left: 400, top: 160, width: 120, height: 40, bottom: 200, right: 520 } } = {}) {
    let probe = null;
    const anchor = {
        getBoundingClientRect: () => ({ ...anchorRect }),
        closest: () => null,
    };
    return {
        querySelector: () => anchor,
        getBoundingClientRect: () => ({ ...editorRect }),
        appendChild: child => { probe = child; },
        get probe() { return probe; },
    };
}

/** A document stub whose createElement returns a probe with a controllable rect. */
function probeDocument(probeRect = { left: 0, top: 0, width: 0, height: 0 }) {
    return {
        createElement: () => {
            const attrs = new Map();
            return {
                style: {},
                setAttribute(name, value) { attrs.set(name, value); },
                getAttribute: name => (attrs.has(name) ? attrs.get(name) : null),
                getBoundingClientRect: () => ({ ...probeRect }),
                remove() {},
            };
        },
    };
}

test('_fixedContainingBlock returns the probe rect, or the viewport when the probe measures nothing', () => {
    const engine = loadEngine({ document: probeDocument({ left: 300, top: 120, width: 980, height: 680 }) });
    const root = fakeRoot();

    const cb = engine._fixedContainingBlock(root);
    assert.deepEqual(plain(cb), { left: 300, top: 120, width: 980, height: 680 }, 'the probe rect IS the containing block');
    assert.equal(root.probe.getAttribute('aria-hidden'), 'true', 'the probe is hidden from a11y');
    assert.ok(!root.probe.parentNode, 'the probe is removed after measuring');

    const zeroEngine = loadEngine({ document: probeDocument({ left: 0, top: 0, width: 0, height: 0 }) });
    assert.deepEqual(plain(zeroEngine._fixedContainingBlock(fakeRoot())),
        { left: 0, top: 0, width: 1280, height: 800 }, 'an unmeasurable probe falls back to the viewport');
});

test('tableInserterAnchor is NOT translated when the viewport is the containing block', () => {
    // The editor sits at (300,120) but the probe proves the viewport still resolves the popover.
    const engine = loadEngine({ document: probeDocument({ left: 0, top: 0, width: 1280, height: 800 }) });
    const root = fakeRoot();

    assert.deepEqual(plain(engine.tableInserterAnchor(root)), [400, 206],
        'a viewport containing block must not translate the anchor by the editor offset');
});

test('tableInserterAnchor is translated into the editor when the editor is the containing block', () => {
    const engine = loadEngine({ document: probeDocument({ left: 300, top: 120, width: 980, height: 680 }) });
    const root = fakeRoot();

    assert.deepEqual(plain(engine.tableInserterAnchor(root)), [100, 86],
        'the anchor is expressed in editor-box coordinates when the editor is the containing block');
});

test('_placeFixed clamps into cb ∩ viewport', () => {
    const engine = loadEngine();
    const pos = engine._placeFixed(
        { left: -50, top: -40, width: 1000, height: 900 },
        5000, -100, 200, 300, 8);
    assert.equal(pos.left, 792, 'clamped right edge inside cb ∩ viewport, expressed cb-locally');
    assert.equal(pos.top, 48, 'clamped top edge inside cb ∩ viewport, expressed cb-locally');
    assert.equal(pos.availHeight, 784, 'the visible region height feeds the maxHeight cap');
});

// ── J4: the context menu positions from the invariant data-screen-x/y, never its own style ──

function fakeMenuEl({ w = 200, h = 300, x = 300, y = 200, screenX = null, screenY = null, anchor = null } = {}) {
    const attrs = new Map();
    if (screenX !== null) attrs.set('data-screen-x', String(screenX));
    if (screenY !== null) attrs.set('data-screen-y', String(screenY));
    if (anchor !== null) attrs.set('data-anchor', anchor);
    return {
        style: { left: x + 'px', top: y + 'px' },
        _rect: { left: x, top: y, width: w, height: h },
        getBoundingClientRect() { return { ...this._rect }; },
        getAttribute: name => (attrs.has(name) ? attrs.get(name) : null),
    };
}

function menuInst() {
    const inst = {
        id: 'm1',
        container: { id: 'm1', style: {}, closest: () => null },
        dotNetRef: null, _menuDismissHandler: null, _menuKeyHandler: null,
    };
    return inst;
}

const SYNC_TIMEOUTS = { setTimeout: f => f() };

test('openContextMenu twice at the same point does not translate twice (editor is the containing block)', () => {
    // The editor box is the containing block and sits at (100,50).
    const engine = loadEngine({ document: probeDocument({ left: 100, top: 50, width: 1000, height: 700 }), extra: SYNC_TIMEOUTS });
    const editorEl = {
        getBoundingClientRect: () => ({ left: 100, top: 50, width: 1000, height: 700 }),
        appendChild: () => {},
    };
    const inst = menuInst();
    inst.container.closest = () => editorEl;
    engine.instances.set('m1', inst);
    const menuEl = fakeMenuEl({ screenX: 300, screenY: 200 });

    engine.openContextMenu(inst.container, menuEl);
    assert.equal(menuEl.style.left, '200px', 'the viewport point is expressed in editor-box coordinates');
    assert.equal(menuEl.style.top, '150px');

    engine.openContextMenu(inst.container, menuEl);
    assert.equal(menuEl.style.left, '200px', 'the second call re-reads data-screen-x, not its own output');
    assert.equal(menuEl.style.top, '150px');
});

test('viewport containing block: the menu stays at the viewport point', () => {
    const engine = loadEngine({ document: probeDocument({ left: 0, top: 0, width: 1280, height: 800 }), extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    engine.instances.set('m1', inst);
    const menuEl = fakeMenuEl({ screenX: 300, screenY: 200 });

    engine.openContextMenu(inst.container, menuEl);
    assert.equal(menuEl.style.left, '300px');
    assert.equal(menuEl.style.top, '200px');
});

test('a menu near the right/bottom edge is clamped inside the visible part of the containing block', () => {
    const engine = loadEngine({ document: probeDocument({ left: 0, top: 0, width: 1280, height: 800 }), extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    engine.instances.set('m1', inst);
    const menuEl = fakeMenuEl({ w: 200, h: 300, x: 1200, y: 700, screenX: 1200, screenY: 700 });

    engine.openContextMenu(inst.container, menuEl);
    assert.equal(menuEl.style.left, '1072px', 'right edge clamped to viewport − margin − width');
    assert.equal(menuEl.style.top, '492px', 'bottom edge clamped to viewport − margin − height');
});

test('maxHeight from a tall menu is reset before the next measure', () => {
    const engine = loadEngine({ document: probeDocument({ left: 0, top: 0, width: 1280, height: 800 }), extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    engine.instances.set('m1', inst);
    const tall = fakeMenuEl({ h: 900, screenX: 300, screenY: 200 });
    engine.openContextMenu(inst.container, tall);
    assert.equal(tall.style.maxHeight, '784px', 'a tall menu is capped to the visible region');

    const short = fakeMenuEl({ h: 300, screenX: 300, screenY: 200 });
    short.style.maxHeight = '784px';   // leftovers from a previous open of the same element
    engine.openContextMenu(inst.container, short);
    assert.equal(short.style.maxHeight, '', 'the cap is reset before the next measure');
});

// ── J5/R4: the context menu routing and the native touch contextmenu guard ──────────────────

/** A contextmenu target chain: closest walks the given answers by selector. */
function targetChain(map) {
    return { closest: sel => (Object.prototype.hasOwnProperty.call(map, sel) ? map[sel] : null) };
}

function ctxInst() {
    const log = [];
    return {
        inst: {
            id: 'cm1',
            container: { id: 'cm1', style: {}, closest: () => null },
            readOnly: false,
            __touchDetach: () => {},   // the touch stack is attached: touch contextmenus belong to the long-press session
            __touchContacts: new Set(),
            __lastTouchUpAt: 0,
            svg: {
                createSVGPoint: () => ({ x: 0, y: 0, matrixTransform: () => ({ x: 5, y: 6 }) }),
                getScreenCTM: () => ({ inverse: () => ({}) }),
            },
            dotNetRef: { invokeMethodAsync: (name, ...args) => { log.push([name, ...args]); return Promise.resolve(); } },
        },
        log,
    };
}

test('a touch contextmenu on a node, an edge or the canvas is swallowed (the long-press session owns it)', () => {
    const engine = loadEngine();
    const { inst, log } = ctxInst();
    inst.__touchContacts.add(1);   // the finger is down (the diagram-editor-touch.js tracker)

    engine._onContextMenu({ pointerType: 'touch', clientX: 10, clientY: 10, preventDefault() {}, target: targetChain({}) }, inst);
    assert.deepEqual(plain(log), [], 'pointerType touch is swallowed');

    const { inst: inst2, log: log2 } = ctxInst();
    inst2.__touchContacts.add(1);
    engine._onContextMenu({ clientX: 10, clientY: 10, preventDefault() {}, target: targetChain({}) }, inst2);
    assert.deepEqual(plain(log2), [], 'a contextmenu during the hold is swallowed');

    const { inst: inst3, log: log3 } = ctxInst();
    inst3.__lastTouchUpAt = Date.now() - 300;
    engine._onContextMenu({ clientX: 10, clientY: 10, preventDefault() {}, target: targetChain({}) }, inst3);
    assert.deepEqual(plain(log3), [], 'a contextmenu right after a touch lift is swallowed');
});

test('a contextmenu fired 300 ms after a touch lift is treated as touch', () => {
    const engine = loadEngine();
    const { inst, log } = ctxInst();
    inst.__lastTouchUpAt = Date.now() - 300;
    engine._onContextMenu({ clientX: 10, clientY: 10, preventDefault() {}, target: targetChain({}) }, inst);
    assert.deepEqual(plain(log), [], 'within the 800 ms window the contextmenu belongs to the touch');
});

test('a contextmenu long after the touch lift opens the menu again (a real mouse right-click)', () => {
    const engine = loadEngine();
    const { inst, log } = ctxInst();
    inst.__lastTouchUpAt = Date.now() - 5000;
    const nodeEl = { getAttribute: n => n === 'data-node-id' ? 'n1' : null, closest: () => null };
    engine._onContextMenu({
        clientX: 30, clientY: 40, preventDefault() {},
        target: targetChain({
            '.tm-diagram-editor__context-menu': null,
            '.tm-diagram-edge-label-group': null,
            '.tm-diagram-node__table-cell': null,
            '.tm-diagram-node': nodeEl,
            '.tm-diagram-edge-hit-path': null,
        }),
    }, inst);
    assert.deepEqual(plain(log), [['OnNodeContextMenu', 'n1', 30, 40, 'mouse']], 'a mouse right-click still opens the node menu');
});

test('a mouse right-click on a node still opens the menu', () => {
    const engine = loadEngine();
    const { inst, log } = ctxInst();
    const nodeEl = { getAttribute: n => n === 'data-node-id' ? 'n1' : null, closest: () => null };
    engine._onContextMenu({
        clientX: 30, clientY: 40, preventDefault() {},
        target: targetChain({
            '.tm-diagram-node': nodeEl,
        }),
    }, inst);
    assert.deepEqual(plain(log), [['OnNodeContextMenu', 'n1', 30, 40, 'mouse']]);
});

test('without the touch stack a touch contextmenu is NOT swallowed (nothing owns the long-press)', () => {
    const engine = loadEngine();
    const { inst, log } = ctxInst();
    inst.__touchDetach = null;   // the touch module never attached on this canvas
    const nodeEl = { getAttribute: n => n === 'data-node-id' ? 'n1' : null, closest: () => null };
    engine._onContextMenu({
        pointerType: 'touch', clientX: 30, clientY: 40, preventDefault() {},
        target: targetChain({ '.tm-diagram-node': nodeEl }),
    }, inst);
    assert.deepEqual(plain(log), [['OnNodeContextMenu', 'n1', 30, 40, 'mouse']],
        'no __touchDetach: even a touch-typed contextmenu opens the menu');
});

test('_contextMenuTarget resolves label group, table cell, node, edge and canvas like the old _onContextMenu', () => {
    const engine = loadEngine();

    // label group: the [data-label-handle] carries the edge id
    const labelHandle = { getAttribute: n => n === 'data-edge-id' ? 'e9' : null };
    const labelGroup = {
        querySelector: sel => sel === '[data-label-handle]' ? labelHandle : null,
        getAttribute: () => null,
        closest: () => null,
    };
    assert.deepEqual(plain(engine._contextMenuTarget(targetChain({
        '.tm-diagram-edge-label-group': labelGroup,
    }))), { kind: 'edge', id: 'e9' });

    // label group without a handle id falls back to the group attribute
    const bareGroup = {
        querySelector: () => null,
        getAttribute: n => n === 'data-edge-id' ? 'e8' : null,
        closest: () => null,
    };
    assert.deepEqual(plain(engine._contextMenuTarget(targetChain({
        '.tm-diagram-edge-label-group': bareGroup,
    }))), { kind: 'edge', id: 'e8' });

    // table cell
    const cell = { getAttribute: n => n === 'data-row' ? '2' : n === 'data-col' ? '3' : null, closest: () => null };
    const cellNode = { getAttribute: n => n === 'data-node-id' ? 'n7' : null, closest: () => null };
    assert.deepEqual(plain(engine._contextMenuTarget(targetChain({
        '.tm-diagram-node__table-cell': cell,
        '.tm-diagram-node': cellNode,
    }))), { kind: 'cell', id: 'n7', row: 2, col: 3 });

    // node
    const node = { getAttribute: n => n === 'data-node-id' ? 'n1' : null, closest: () => null };
    assert.deepEqual(plain(engine._contextMenuTarget(targetChain({ '.tm-diagram-node': node }))), { kind: 'node', id: 'n1' });

    // edge
    const edge = { getAttribute: n => n === 'data-edge-id' ? 'e1' : null, closest: () => null };
    assert.deepEqual(plain(engine._contextMenuTarget(targetChain({ '.tm-diagram-edge-hit-path': edge }))), { kind: 'edge', id: 'e1' });

    // canvas
    assert.deepEqual(plain(engine._contextMenuTarget(targetChain({}))), { kind: 'canvas' });

    // null target / no closest
    assert.deepEqual(plain(engine._contextMenuTarget(null)), { kind: 'canvas' });
});

test('touchContextMenuTarget refuses a press outside the svg and everything in read-only', () => {
    const engine = loadEngine();
    const { inst } = ctxInst();
    const svgMarked = { closest: sel => sel === '.tm-diagram-canvas__svg' ? svgMarked : null };
    assert.equal(engine.touchContextMenuTarget(inst, svgMarked) !== null, true, 'a press on the svg resolves');
    const stray = { closest: () => null };
    assert.equal(engine.touchContextMenuTarget(inst, stray), null, 'a press outside the svg never menus');
    inst.readOnly = true;
    assert.equal(engine.touchContextMenuTarget(inst, svgMarked), null, 'read-only never opens the menu');
});

// ── J6: a port / connection point under an edge corridor wins the press ─────────────────────

/** Fake SVG-ish elements for the _onMouseDown hit-stack tests. */
function hitStackElements() {
    const nodeG = {
        tag: 'g',
        classList: { contains: c => c === 'tm-diagram-node' },
        getAttribute: n => n === 'data-node-id' ? 'n1' : n === 'data-locked' ? 'false' : null,
        closest: () => null,
    };
    const edgeHitPath = {
        tag: 'path',
        classList: { contains: c => c === 'tm-diagram-edge-hit-path' },
        getAttribute: n => n === 'data-edge-id' ? 'e1' : null,
        closest: sel => (sel === '.tm-diagram-edge-hit-path' || sel === '.tm-diagram-edge-hit-path, .tm-diagram-edge-label-group') ? edgeHitPath : null,
    };
    const cpCircle = {
        tag: 'circle',
        classList: { contains: c => c === 'tm-diagram-connection-point' },
        getAttribute: n => n === 'data-cp-rx' ? '0.5' : n === 'data-cp-ry' ? '0.25' : n === 'data-cp-perimeter' ? 'true' : null,
        closest: sel => sel === '[data-node-id]' ? nodeG : null,
    };
    const portCircle = {
        tag: 'circle',
        classList: { contains: c => c === 'tm-diagram-port' },
        getAttribute: n => n === 'data-port-id' ? 'p1' : null,
        closest: sel => sel === '[data-node-id]' ? nodeG : null,
    };
    return { nodeG, edgeHitPath, cpCircle, portCircle };
}

function corridorInst() {
    const log = [];
    const inst = {
        id: 'c1',
        container: { id: 'c1', style: {}, focus() {}, contains: () => true, closest: () => null, classList: { toggle() {} } },
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
    };
    return { inst, log };
}

const noHitStubs = engine => {
    engine._updateSelection = () => {};
    engine._isNodeInActiveGroup = () => true;
};

test('a press on a connection point under an edge corridor starts an edge draw, not a whole-edge drag', () => {
    const { edgeHitPath, cpCircle, nodeG } = hitStackElements();
    const engine = loadEngine({ document: { elementsFromPoint: () => [edgeHitPath, cpCircle, nodeG] } });
    noHitStubs(engine);
    const { inst, log } = corridorInst();
    engine.instances.set('c1', inst);
    const edgeDraws = [];
    engine._startEdgeDraw = (...args) => edgeDraws.push(args);

    engine._onMouseDown({
        button: 0, clientX: 50, clientY: 60,
        preventDefault() {}, stopPropagation() {},
        target: edgeHitPath,
    }, inst);

    assert.equal(edgeDraws.length, 1, 'the connection point starts an edge draw through the corridor');
    assert.equal(edgeDraws[0][1], 'n1', 'the draw sources the node under the connection point');
    assert.equal(edgeDraws[0][2], null, 'no port id — a connection point draw');
    assert.equal(edgeDraws[0][7], 0.5, 'the cp rx reaches _startEdgeDraw');
    assert.equal(edgeDraws[0][8], 0.25, 'the cp ry reaches _startEdgeDraw');
    assert.equal(edgeDraws[0][9], true, 'the cp perimeter flag reaches _startEdgeDraw');
    assert.notEqual(inst.isDraggingWholeEdge, true, 'the press is not a whole-edge drag');
    assert.equal(log.filter(entry => entry[0] === 'OnSelectionChanged').length, 0,
        'the edge is not selected by this press');
});

test('a port under the corridor still wins', () => {
    const { edgeHitPath, portCircle, nodeG } = hitStackElements();
    const engine = loadEngine({ document: { elementsFromPoint: () => [edgeHitPath, portCircle, nodeG] } });
    noHitStubs(engine);
    const { inst } = corridorInst();
    engine.instances.set('c1', inst);
    const edgeDraws = [];
    engine._startEdgeDraw = (...args) => edgeDraws.push(args);

    engine._onMouseDown({
        button: 0, clientX: 50, clientY: 60,
        preventDefault() {}, stopPropagation() {},
        target: edgeHitPath,
    }, inst);

    assert.equal(edgeDraws.length, 1, 'the port starts an edge draw');
    assert.equal(edgeDraws[0][1], 'n1');
    assert.equal(edgeDraws[0][2], 'p1', 'the port id wins over the corridor');
    assert.notEqual(inst.isDraggingWholeEdge, true, 'the press is not a whole-edge drag');
});

test('a press on a bare edge corridor still drags the whole edge', () => {
    const { edgeHitPath, nodeG } = hitStackElements();
    const engine = loadEngine({ document: { elementsFromPoint: () => [edgeHitPath, nodeG] } });
    noHitStubs(engine);
    const { inst, log } = corridorInst();
    engine.instances.set('c1', inst);
    inst.svg = {
        querySelector: () => null,
        getBoundingClientRect: () => ({ width: 800 }),
        createSVGPoint: () => ({ x: 0, y: 0, matrixTransform: () => ({ x: 5, y: 6 }) }),
        getScreenCTM: () => ({ inverse: () => ({}) }),
        viewBox: { baseVal: { x: 0, y: 0, width: 800, height: 500 } },
    };
    const edgeDraws = [];
    engine._startEdgeDraw = (...args) => edgeDraws.push(args);

    engine._onMouseDown({
        button: 0, clientX: 50, clientY: 60,
        preventDefault() {}, stopPropagation() {},
        target: edgeHitPath,
    }, inst);

    assert.equal(edgeDraws.length, 0, 'no port, no cp — the corridor drag arms');
    assert.equal(inst.isDraggingWholeEdge, true, 'the whole-edge drag arms');
    assert.equal(log.filter(entry => entry[0] === 'OnSelectionChanged').length, 1,
        'the edge is selected for the drag');
});

// ── J12a: the mouse edge-draw commit is a reusable method (the touch path shares it) ─────────

test('_commitEdgeDraw is the mouseup branch', () => {
    const log = [];
    const targetNode = { getAttribute: n => n === 'data-node-id' ? 'n2' : null };
    const portEl = {
        classList: { contains: c => c === 'tm-diagram-port' },
        getAttribute: n => n === 'data-port-id' ? 'p2' : null,
        closest: sel => sel === '[data-node-id]' ? targetNode
            : sel === '.tm-diagram-port' ? portEl : null,
    };
    const inst = {
        id: 'c1',
        container: { id: 'c1', style: {} },
        isDrawingEdge: true,
        drawSource: {
            nodeId: 'n1', portId: 'p1', side: 'right', offset: 0.5,
            constraintRx: null, constraintRy: null, constraintPerimeter: false,
        },
        drawHoverEdgeId: null,
        drawHoverEdgeT: null,
        scale: 1,
        gridSize: 0,
        svg: { getBoundingClientRect: () => ({ width: 800 }) },
        dotNetRef: { invokeMethodAsync: (name, ...args) => { log.push([name, ...args]); return Promise.resolve(); } },
    };
    const engine = loadEngine({ document: { elementFromPoint: () => portEl } });
    engine.instances.set('c1', inst);
    engine._screenToDoc = (_i, x, y) => ({ x, y });
    engine._snap = (_i, v) => v;
    engine._cancelEdgeDraw = (i) => { i.isDrawingEdge = false; };
    engine._findNearestPortOnNode = () => null;

    engine._onMouseUp({ clientX: 200, clientY: 120, target: { tagName: 'DIV' } }, inst);

    const created = log.filter(entry => entry[0] === 'JsOnEdgeCreated');
    assert.equal(created.length, 1, 'the mouse-up commit reports the edge once');
    assert.deepEqual(plain(created[0].slice(1, 4)), ['n1', 'p1', 'n2'],
        'source node/port and the target node reach JsOnEdgeCreated');
    assert.equal(created[0][4], 'p2', 'the target port id reaches JsOnEdgeCreated');
    assert.equal(inst.isDrawingEdge, false, 'the draw state is cleaned up');

    // The extracted method itself commits identically.
    inst.isDrawingEdge = true;
    log.length = 0;
    engine._commitEdgeDraw(inst, 200, 120);
    assert.equal(log.filter(entry => entry[0] === 'JsOnEdgeCreated').length, 1,
        '_commitEdgeDraw is the same commit path');
});

// ── J12a review fix: occlusion — a port buried under another control is not pressed ─────────

test('touchPortAt refuses a port buried under a resize handle (occlusion)', () => {
    const { edgeHitPath, portCircle, nodeG } = hitStackElements();
    const engine = loadEngine({ document: { elementsFromPoint: () => [edgeHitPath, portCircle, nodeG] } });
    const { inst } = corridorInst();

    const resizeHit = {
        classList: { contains: c => c === 'tm-diagram-resize-handle-hit' },
        getAttribute: () => null,
        closest: sel => sel.startsWith('.tm-diagram-resize-handle') || sel.startsWith('.tm-diagram-rotate-handle') ? resizeHit : null,
    };
    assert.equal(engine.touchPortAt(inst, { clientX: 50, clientY: 60, target: resizeHit }), null,
        'a press on a resize handle never starts a port edge draw, even when a port sits under it');
});

test('touchPortAt still resolves a port under an edge corridor (J6 semantics)', () => {
    const { edgeHitPath, portCircle, nodeG } = hitStackElements();
    const engine = loadEngine({ document: { elementsFromPoint: () => [edgeHitPath, portCircle, nodeG] } });
    const { inst } = corridorInst();

    const resolved = engine.touchPortAt(inst, { clientX: 50, clientY: 60, target: edgeHitPath });
    assert.equal(resolved, portCircle, 'the corridor press resolves the port it paints over');
});

test('_onMouseDown on a resize hit over a buried port does not draw an edge', () => {
    const { portCircle, nodeG } = hitStackElements();
    const engine = loadEngine({ document: { elementsFromPoint: () => [portCircle, nodeG] } });
    noHitStubs(engine);
    const { inst } = corridorInst();
    engine.instances.set('c1', inst);
    const edgeDraws = [];
    engine._startEdgeDraw = (...args) => edgeDraws.push(args);

    const resizeHit = {
        classList: { contains: c => c === 'tm-diagram-resize-handle-hit' },
        getAttribute: () => null,
        closest: sel => sel.startsWith('.tm-diagram-resize-handle') || sel.startsWith('.tm-diagram-rotate-handle')
            || sel === '.tm-diagram-resize-handle-hit, .tm-diagram-rotate-handle-hit, .tm-diagram-connect-arrow'
            || sel.includes('resize-handle') || sel.includes('rotate-handle') || sel.includes('connect-arrow')
            ? resizeHit : null,
    };
    engine._onMouseDown({
        button: 0, clientX: 50, clientY: 60,
        preventDefault() {}, stopPropagation() {},
        target: resizeHit,
    }, inst);

    assert.equal(edgeDraws.length, 0, 'the occluded port never claims the press');
    assert.notEqual(inst.isDraggingWholeEdge, true, 'and the whole-edge drag does not arm either');
});
// ── Review fixes: zoom limits on every path, menu dismissal, a11y, re-init ────────────────────

test('wheel-out below MIN is a no-op; wheel-in moves toward the range and lands exactly on MIN', () => {
    const engine = loadEngine();
    engine._screenToDoc = (_i, x, y) => ({ x, y });
    const { inst } = zoomInst('0 0 16000 10000');      // curScale = 800/16000 = 0.05, BELOW MIN
    engine.instances.set('z1', inst);

    const wheelOut = { clientX: 400, clientY: 250, deltaY: 100, preventDefault() {}, target: { closest: () => null } };
    engine._onWheel(wheelOut, inst);
    assert.equal(readVb(inst).width, 16000, 'zooming further out below MIN changes nothing (no jump)');

    engine._onWheel({ ...wheelOut, deltaY: -100 }, inst);   // zoom in: toward the range, NO jump
    assert.equal(readVb(inst).width, 14400, 'one tick moves one step toward the range (16000 × 0.9)');
    for (let i = 0; i < 20 && readVb(inst).width > 800 / 0.15 + 0.5; i++)
        engine._onWheel({ ...wheelOut, deltaY: -100 }, inst);
    assert.ok(Math.abs(readVb(inst).width - 800 / 0.15) < 0.01,
        'the crossing tick lands exactly on MIN, never jumps past it');
    engine._onWheel(wheelOut, inst);   // away from the range at the boundary: a no-op
    assert.ok(Math.abs(readVb(inst).width - 800 / 0.15) < 0.01,
        'zooming out at MIN stays put');
});

test('wheel-in above MAX is a no-op; wheel-out moves toward the range and lands exactly on MAX', () => {
    const engine = loadEngine();
    engine._screenToDoc = (_i, x, y) => ({ x, y });
    const { inst } = zoomInst('0 0 100 62.5');         // curScale = 8, ABOVE MAX
    engine.instances.set('z1', inst);

    const wheelIn = { clientX: 400, clientY: 250, deltaY: -100, preventDefault() {}, target: { closest: () => null } };
    engine._onWheel(wheelIn, inst);
    assert.equal(readVb(inst).width, 100, 'zooming further in above MAX changes nothing');

    engine._onWheel({ ...wheelIn, deltaY: 100 }, inst);
    assert.ok(Math.abs(readVb(inst).width - 110) < 1e-9, 'one tick moves one step toward the range (100 × 1.1)');
    for (let i = 0; i < 20 && readVb(inst).width < 800 / 5 - 0.5; i++)
        engine._onWheel({ ...wheelIn, deltaY: 100 }, inst);
    assert.ok(Math.abs(readVb(inst).width - 800 / 5) < 0.01,
        'the crossing tick lands exactly on MAX, never jumps past it');
    engine._onWheel(wheelIn, inst);    // away from the range at the boundary: a no-op
    assert.ok(Math.abs(readVb(inst).width - 800 / 5) < 0.01,
        'zooming in at MAX stays put');
});

test('zoomToRect clamps into the shared limits and reports inst.scale', () => {
    const engine = loadEngine();
    const { inst, calls } = zoomInst();
    engine.instances.set('z1', inst);

    engine.zoomToRect({ id: 'z1' }, 0, 0, 10, 10, 40);   // content 90x90: fit scale capped by FIT_MAX_SCALE
    assert.equal(readVb(inst).width, 400, 'zoomToRect caps at FIT_MAX_SCALE (2.0)');
    const zoomReported = calls.filter(c => c[0] === 'OnZoomChanged').map(c => c[1]);
    assert.deepEqual(zoomReported, [inst.scale], 'zoomToRect reports the settled inst.scale');

    engine.zoomToRect({ id: 'z1' }, 0, 0, 100000, 100000, 40);
    assert.ok(Math.abs(readVb(inst).width - 800 / 0.15) < 1e-6,
        'a huge rect clamps to MIN_SCALE — the old 0.25 floor is gone');
});

test('fitToView on an empty document clamps the scale into the shared limits', () => {
    const engine = loadEngine();
    const { inst } = zoomInst();
    inst.canvasW = 100;                                   // 800/100 = 8 raw: past MAX
    engine.instances.set('z1', inst);

    const settled = engine.fitToView({ id: 'z1' }, 40);
    assert.ok(Math.abs(settled - 5.0) < 1e-9, 'the empty-document branch clamps to MAX_SCALE');
    assert.ok(Math.abs(readVb(inst).width - 160) < 1e-9, 'the viewBox matches the clamped scale');
});

test('touchBeginPan / touchPinchBegin / touchBeginEdgeDraw close an open context menu', () => {
    const engine = loadEngine();
    const { portCircle } = hitStackElements();

    // Only when a menu is open: no flag, no CloseContextMenu.
    const { inst: idle, log: idleLog } = fakeInst({ svg: fakeSvg() });
    engine.touchBeginPan(idle, 10, 10);
    assert.equal(idleLog.some(c => c[0] === 'CloseContextMenu'), false, 'no menu open: the pan closes nothing');

    const { inst, log } = fakeInst({ svg: fakeSvg(), _menuOpen: true });
    engine.touchBeginPan(inst, 10, 10);
    assert.deepEqual(log.map(c => c[0]), ['CloseContextMenu'], 'a pan start dismisses the open menu');
    assert.equal(inst.isPanning, true, 'the pan still arms');

    const { inst: inst2, log: log2 } = fakeInst({ svg: fakeSvg(), _menuOpen: true });
    engine.touchPinchBegin(inst2);
    assert.deepEqual(log2.map(c => c[0]), ['CloseContextMenu'], 'a pinch start dismisses the open menu');

    const { inst: inst3, log: log3 } = fakeInst({ _menuOpen: true });
    engine._startEdgeDraw = () => {};
    engine.touchBeginEdgeDraw(inst3, portCircle, 50, 60);
    assert.deepEqual(log3.map(c => c[0]), ['CloseContextMenu'], 'an edge draw start dismisses the open menu');
});

// ── Review fixes: touch dismissal, finger offset, roving focus ────────────────────────────────

test('a primary pointerdown outside dismisses the menu (capture phase, touch button 0 included)', () => {
    const doc = listenerDocument();
    const engine = loadEngine({ document: doc, extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    const closeCalls = [];
    inst.dotNetRef = { invokeMethodAsync: n => { closeCalls.push(n); return Promise.resolve(); } };
    engine.instances.set('m1', inst);

    engine.openContextMenu(inst.container, fakeMenuEl({ screenX: 300, screenY: 200 }));
    assert.equal(doc.count('pointerdown'), 1, 'the dismiss handler listens on pointerdown (capture)');
    assert.equal(doc.captures?.('pointerdown') ?? true, true);

    doc.dispatch('pointerdown', { button: 0, target: { closest: () => null } });
    assert.deepEqual(closeCalls, ['CloseContextMenu'], 'a press outside closes the menu');
    assert.equal(doc.count('pointerdown'), 0, 'the handler removes itself');

    // A press INSIDE the menu keeps it open; a non-primary button does not dismiss either.
    closeCalls.length = 0;
    engine.openContextMenu(inst.container, fakeMenuEl({ screenX: 300, screenY: 200 }));
    doc.dispatch('pointerdown', { button: 0, target: { closest: s => s === '.tm-diagram-editor__context-menu' ? {} : null } });
    doc.dispatch('pointerdown', { button: 2, target: { closest: () => null } });
    assert.deepEqual(closeCalls, [], 'inside-menu and non-primary presses leave the menu alone');
});

test('a touch-anchored menu parks above the finger, offset right, flipping below when no room', () => {
    const engine = loadEngine({ document: probeDocument({ left: 0, top: 0, width: 1280, height: 800 }), extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    engine.instances.set('m1', inst);

    const el = fakeMenuEl({ w: 200, h: 300, screenX: 300, screenY: 400, anchor: 'touch' });
    engine.openContextMenu(inst.container, el);
    assert.equal(el.style.top, '84px', 'y − height − 16 parks the menu above the finger');
    assert.equal(el.style.left, '316px', 'x + 16 offsets it right of the finger');

    const flip = fakeMenuEl({ w: 200, h: 300, screenX: 300, screenY: 60, anchor: 'touch' });
    engine.openContextMenu(inst.container, flip);
    assert.equal(flip.style.top, '76px', 'no room above: the menu flips below the finger (y + 16)');

    const clamped = fakeMenuEl({ w: 200, h: 300, screenX: 1250, screenY: 400, anchor: 'touch' });
    engine.openContextMenu(inst.container, clamped);
    assert.equal(clamped.style.left, '1072px', 'the offset menu is clamped into the containing block');
});

test('context menu a11y: the first enabled item is focused, arrows/Home/End rove, Escape closes', () => {
    const doc = listenerDocument();
    const engine = loadEngine({ document: doc, extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    const closeCalls = [];
    inst.dotNetRef = { invokeMethodAsync: n => { closeCalls.push(n); return Promise.resolve(); } };
    engine.instances.set('m1', inst);

    const makeItem = (name, disabled = false) => ({
        name, disabled, tabIndex: -1,
        getAttribute: () => null,
        focus(opts) { doc.activeElement = this; this.focusedWith = opts; },
    });
    const cut = makeItem('cut'), copy = makeItem('copy'), paste = makeItem('paste', true);
    const menuEl = { ...fakeMenuEl({ screenX: 300, screenY: 200 }), querySelectorAll: () => [cut, copy, paste] };

    engine.openContextMenu(inst.container, menuEl);
    assert.equal(doc.activeElement, cut, 'the first ENABLED item owns the focus after placement');
    assert.equal(cut.focusedWith?.preventScroll, true, 'the focus does not scroll the page');
    assert.equal(cut.tabIndex, 0, 'the roving tab stop sits on the focused item');
    assert.equal(copy.tabIndex, -1);

    doc.dispatch('keydown', { key: 'ArrowDown', preventDefault() {} });
    assert.equal(doc.activeElement, copy, 'ArrowDown moves to the next enabled item');
    doc.dispatch('keydown', { key: 'ArrowUp', preventDefault() {} });
    assert.equal(doc.activeElement, cut, 'ArrowUp moves back');
    doc.dispatch('keydown', { key: 'End', preventDefault() {} });
    assert.equal(doc.activeElement, copy, 'End lands on the last ENABLED item (paste is disabled)');
    doc.dispatch('keydown', { key: 'Home', preventDefault() {} });
    assert.equal(doc.activeElement, cut, 'Home returns to the first');

    doc.dispatch('keydown', { key: 'Escape' });
    assert.deepEqual(closeCalls, ['CloseContextMenu'], 'Escape closes the menu');
    assert.equal(doc.count('keydown'), 0, 'the key handler is removed with the menu');
});

test('closing the menu returns the focus to the canvas', () => {
    const doc = listenerDocument();
    const engine = loadEngine({ document: doc, extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    inst.container.focus = (opts) => { doc.canvasFocused = opts; };
    engine.instances.set('m1', inst);

    const item = {
        disabled: false, tabIndex: -1, getAttribute: () => null,
        closest: sel => sel === '.tm-diagram-editor__context-menu' ? item : null,
        focus() { doc.activeElement = this; },
    };
    const menuEl = { ...fakeMenuEl({ screenX: 300, screenY: 200 }), querySelectorAll: () => [item] };
    engine.openContextMenu(inst.container, menuEl);
    assert.equal(doc.activeElement, item, 'the menu owns the focus');

    engine.closeContextMenu(inst.container);
    assert.equal(doc.canvasFocused?.preventScroll, true, 'the canvas gets the focus back');
});

test('init() destroys a previous instance registered for the same container id', () => {
    const engine = loadEngine();
    const events = [];
    const panes = { querySelectorAll: () => [] };
    const svg = {
        setAttribute(n, v) { if (n === 'viewBox') this._vb = v; },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 500 }),
        querySelector(sel) {
            if (sel === '.tm-diagram-bg-pane' || sel === '.tm-diagram-scene-pane'
                || sel === '.tm-diagram-overlay-pane' || sel === '.tm-diagram-decorator-pane') return panes;
            return null;
        },
        get viewBox() {
            const [x, y, w, h] = String(this._vb || '0 0 800 500').split(' ').map(Number);
            return { baseVal: { x, y, width: w, height: h } };
        },
    };
    const container = {
        id: 're1',
        style: {},
        querySelector(sel) { return sel === '.tm-diagram-canvas__svg' ? svg : null; },
        addEventListener() {}, removeEventListener() {},
        contains: () => true, focus() {},
    };
    const dotNetRef = {
        invokeMethodAsync: () => Promise.resolve(),
        dispose: () => events.push('disposed'),
    };
    // A stale instance from a previous render, still registered for the same id.
    engine.instances.set('re1', {
        container,
        svg, bgPane: panes, scenePane: panes, overlayPane: panes, decoratorPane: panes,
        dotNetRef,
        __touchDetach: () => events.push('touch-detached'),
        selectionOutlines: [],
        selectedIds: new Set(),
    });

    engine.init(container, dotNetRef, { autoFit: false });
    assert.deepEqual(events, ['touch-detached', 'disposed'], 'the stale instance is torn down first');
    const fresh = engine.instances.get('re1');
    assert.ok(fresh && fresh.dotNetRef === dotNetRef, 'the fresh instance owns the id now');

    engine.init(container, dotNetRef, { autoFit: false });
    const count = [...engine.instances.keys()].filter(k => k === 're1').length;
    assert.equal(count, 1, 'still exactly one instance for the id after a re-init');
});

// ── Re-review fixes: port-hit transform parity, resize hiding, Tab dismissal ─────────────────

/** A decoratorPane stub whose per-node groups (handles, port hits, connect arrows) record the
 *  transforms they receive through the generic [data-node-id] selector. */
function decoratorWithGroups(nodeId) {
    const make = (cls) => {
        const attrs = new Map();
        return {
            attrs,
            className: cls,
            setAttribute(n, v) { attrs.set(n, String(v)); },
            getAttribute(n) { return attrs.has(n) ? attrs.get(n) : null; },
        };
    };
    const handles = make('tm-diagram-node-handles');
    const portHits = make('tm-diagram-port-hits');
    const connectArrows = make('tm-diagram-connect-arrows');
    const groups = [handles, portHits, connectArrows];
    return {
        handles, portHits, connectArrows,
        querySelectorAll(sel) {
            return sel === '[data-node-id="' + nodeId + '"]' ? groups : [];
        },
    };
}

test('_updateSelectionTransforms keeps every per-node decorator group in sync', () => {
    const engine = loadEngine();
    const { inst } = fakeInst({ selectedIds: new Set(['n1']) });
    const nodeEl = {
        getAttribute: n => ({ transform: 'translate(10,20) rotate(15 50 25)', 'data-w': '100', 'data-h': '50' })[n] ?? null,
    };
    engine._nodeEl = () => nodeEl;
    const dec = decoratorWithGroups('n1');
    inst.decoratorPane = dec;

    engine._updateSelectionTransforms(inst);

    const expected = 'translate(10,20) rotate(15 50 25)';
    assert.equal(dec.handles.getAttribute('transform'), expected);
    assert.equal(dec.portHits.getAttribute('transform'), expected,
        'the finger-sized port hit circles follow the same transform as the handles during a drag');
    assert.equal(dec.connectArrows.getAttribute('transform'), expected,
        'the quick-connect arrows live in their own per-node group and follow the same transform');
});

test('_applyNodeRotation rotates the port-hit group together with the handles group', () => {
    const engine = loadEngine();
    const { inst } = fakeInst();
    const nodeEl = {
        attrs: new Map([['transform', 'translate(10,20)'], ['data-w', '100'], ['data-h', '50']]),
        getAttribute(n) { return this.attrs.has(n) ? this.attrs.get(n) : null; },
        setAttribute(n, v) { this.attrs.set(n, String(v)); },
    };
    engine._nodeEl = () => nodeEl;
    const dec = decoratorWithGroups('n1');
    inst.decoratorPane = dec;

    engine._applyNodeRotation(inst, 'n1', 30);

    const expected = 'translate(10,20) rotate(30 50 25)';
    assert.equal(dec.handles.getAttribute('transform'), expected);
    assert.equal(dec.portHits.getAttribute('transform'), expected,
        'a live rotation moves the port hits with the handles so a finger press lands on the rotated port');
    assert.equal(dec.connectArrows.getAttribute('transform'), expected,
        'a live rotation moves the quick-connect arrows with the node');
});

test('setResizing toggles tm-diagram-canvas--resizing on the canvas (the CSS hides the port hits)', () => {
    const engine = loadEngine();
    const toggles = [];
    const { inst } = fakeInst({
        container: {
            id: 'c1', style: {}, focus() {}, contains: () => true, closest: () => null,
            classList: { toggle: (cls, on) => toggles.push([cls, on]) },
        },
    });
    engine.instances.set('c1', inst);

    engine.setResizing({ id: 'c1' }, true);
    engine.setResizing({ id: 'c1' }, false);

    assert.deepEqual(plain(toggles), [
        ['tm-diagram-canvas--resizing', true],
        ['tm-diagram-canvas--resizing', false],
    ]);
});

test('Tab closes the context menu and lets the default Tab proceed', () => {
    const doc = listenerDocument();
    const engine = loadEngine({ document: doc, extra: SYNC_TIMEOUTS });
    const inst = menuInst();
    const closeCalls = [];
    inst.dotNetRef = { invokeMethodAsync: n => { closeCalls.push(n); return Promise.resolve(); } };
    engine.instances.set('m1', inst);

    const item = { disabled: false, tabIndex: -1, getAttribute: () => null, focus() { doc.activeElement = this; } };
    const menuEl = { ...fakeMenuEl({ screenX: 300, screenY: 200 }), querySelectorAll: () => [item] };
    engine.openContextMenu(inst.container, menuEl);
    assert.equal(doc.count('keydown'), 1, 'the key handler listens while the menu is open');

    let prevented = false;
    doc.dispatch('keydown', { key: 'Tab', preventDefault() { prevented = true; } });

    assert.deepEqual(closeCalls, ['CloseContextMenu'], 'Tab closes the menu like Escape');
    assert.equal(doc.count('keydown'), 0, 'the key handler is removed with the menu');
    assert.equal(prevented, false, 'default Tab is NOT prevented — focus leaves the closed menu in tab order');
});
test('visibleBandCentre returns a [x, y] array (the C# side reads double[])', () => {
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const inst = {
        id: 'c1',
        scale: 1,
        svg: {
            viewBox: { baseVal: { x: 0, y: 0, width: 400, height: 600 } },
            getBoundingClientRect: () => ({ left: 0, top: 0, width: 400, height: 600, right: 400, bottom: 600 }),
            createSVGPoint: () => ({ x: 0, y: 0, matrixTransform: () => ({ x: 7, y: 9 }) }),
            getScreenCTM: () => ({ inverse: () => ({}) }),
        },
    };
    const root = {
        querySelector: (sel) => sel === '.tm-diagram-canvas' ? { id: 'c1' } : null,
    };
    engine.instances.set('c1', inst);

    const result = engine.visibleBandCentre(root);

    assert.ok(Array.isArray(result), 'the helper returns an array, not an object');
    assert.deepEqual(plain(result), [7, 9], 'the doc point is the array the .NET double[] read expects');
    assert.equal(engine.visibleBandCentre(null), null);
});

test('revealRectAboveSheet pans the rect above the sheet panel', async () => {
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    const calls = [];
    const inst = {
        id: 'c1',
        scale: 1,
        dotNetRef: { invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); } },
        svg: {
            viewBox: { baseVal: { x: 0, y: 0, width: 400, height: 600 } },
            getBoundingClientRect: () => ({ left: 0, top: 0, width: 400, height: 600, right: 400, bottom: 600 }),
            setAttribute: (name, value) => sets.push([name, value]),
        },
    };
    const root = {
        querySelector: (sel) => {
            if (sel === '.tm-diagram-canvas') return { id: 'c1' };
            if (sel === '.tm-editor-shell__sheet .tm-drawer__panel')
                return { getBoundingClientRect: () => ({ top: 400, height: 200 }) };
            return null;
        },
    };
    engine.instances.set('c1', inst);

    const panned = await engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16);

    assert.equal(panned, true, 'the covered rect panned the canvas');
    assert.deepEqual(sets, [['viewBox', '0 116 400 600']]);
    assert.deepEqual(calls[calls.length - 1], ['OnViewportChanged', 0, 116, 400, 600],
        'the Blazor side learns the new viewBox');
});

test('revealRectAboveSheet does nothing when the rect is already visible', async () => {
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    const inst = {
        id: 'c1',
        scale: 1,
        svg: {
            viewBox: { baseVal: { x: 0, y: 0, width: 400, height: 600 } },
            getBoundingClientRect: () => ({ left: 0, top: 0, width: 400, height: 600, right: 400, bottom: 600 }),
            setAttribute: (name, value) => sets.push([name, value]),
        },
    };
    const root = {
        querySelector: (sel) => {
            if (sel === '.tm-diagram-canvas') return { id: 'c1' };
            if (sel === '.tm-editor-shell__sheet .tm-drawer__panel')
                return { getBoundingClientRect: () => ({ top: 400, height: 200 }) };
            return null;
        },
    };
    engine.instances.set('c1', inst);

    const panned = await engine.revealRectAboveSheet(root, 100, 100, 50, 50, 16);

    assert.equal(panned, false, 'an already-visible rect does not pan');
    assert.deepEqual(sets, [], 'the viewBox stays untouched');
});

// ── UX review (fze-6): gesture guard, animation wait, timeout fallback ──────

function revealInst(sets, flags = {}) {
    return Object.assign({
        id: 'c1',
        scale: 1,
        svg: {
            viewBox: { baseVal: { x: 0, y: 0, width: 400, height: 600 } },
            getBoundingClientRect: () => ({ left: 0, top: 0, width: 400, height: 600, right: 400, bottom: 600 }),
            setAttribute: (name, value) => sets.push([name, value]),
        },
    }, flags);
}

function revealRoot(panel) {
    return {
        querySelector: (sel) => {
            if (sel === '.tm-diagram-canvas') return { id: 'c1' };
            if (sel === '.tm-editor-shell__sheet .tm-drawer__panel') return panel;
            return null;
        },
    };
}

test('revealRectAboveSheet refuses to pan while a gesture owns the viewBox', async () => {
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    const panel = { getBoundingClientRect: () => ({ top: 400, height: 200 }) };
    const root = revealRoot(panel);

    for (const flag of ['isDragging', 'isPanning', 'isRubberBand', 'isPinching']) {
        sets.length = 0;
        engine.instances.set('c1', revealInst(sets, { [flag]: true }));
        const panned = await engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16);
        assert.equal(panned, false, `${flag} refuses the pan`);
        assert.deepEqual(sets, [], `${flag}: the viewBox stays untouched`);
    }
});

test('revealRectAboveSheet waits for the sheet panel animations before measuring', async () => {
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    let resolveAnim;
    const anim = { finished: new Promise(r => { resolveAnim = r; }) };
    const panel = {
        getBoundingClientRect: () => ({ top: 400, height: 200 }),
        getAnimations: () => [anim],
    };
    engine.instances.set('c1', revealInst(sets));
    const root = revealRoot(panel);

    let done = null;
    const pending = engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16)
        .then(v => { done = v; });
    await Promise.resolve();

    assert.equal(done, null, 'the pan waits for the animation to settle');
    assert.deepEqual(sets, [], 'nothing panned before the animation finished');

    resolveAnim();
    await pending;

    assert.equal(done, true, 'after the animation the pan runs');
    assert.deepEqual(sets, [['viewBox', '0 116 400 600']]);
});

test('revealRectAboveSheet computes the settled band from the stage bottom and the panel target height', async () => {
    // No animations observable: mid-flight geometry (the panel reporting top 999 while the
    // stage is 600 tall) must not be trusted — the settled top is stage bottom − target height.
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    const panel = {
        offsetHeight: 200,
        getBoundingClientRect: () => ({ top: 999, height: 1 }),
        closest: (sel) => sel === '.tm-editor-shell__stage'
            ? { getBoundingClientRect: () => ({ bottom: 590 }) } : null,
        getAnimations: () => [],
    };
    engine.instances.set('c1', revealInst(sets));
    const root = revealRoot(panel);

    const panned = await engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16);

    assert.equal(panned, true, 'the rect below the settled band pans the canvas');
    assert.deepEqual(sets, [['viewBox', '0 126 400 600']],
        'the band top is the stage bottom (590) minus the panel target height (200), plus the margin');
});

test('revealRectAboveSheet resolves false on the animation timeout fallback', async () => {
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    const panel = {
        getBoundingClientRect: () => ({ top: 400, height: 200 }),
        getAnimations: () => [{ finished: new Promise(() => {}) }], // never settles
    };
    engine.instances.set('c1', revealInst(sets));
    const root = revealRoot(panel);

    const start = Date.now();
    const panned = await engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16);

    assert.equal(panned, false, 'a stalled animation resolves false, never hangs');
    assert.ok(Date.now() - start >= 400, 'the fallback actually waited (~500 ms)');
    assert.ok(Date.now() - start < 3000, 'and it is only a fallback');
    assert.deepEqual(sets, [], 'the viewBox stays untouched');
});

test('revealRectAboveSheet ignores an animation that resolves after the timeout fallback', async () => {
    // The fallback resolved the promise false while the animation was still pending; when the
    // animation finally settles, the late pan must NOT run — the viewBox stays untouched.
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    let resolveAnim;
    const panel = {
        getBoundingClientRect: () => ({ top: 400, height: 200 }),
        getAnimations: () => [{ finished: new Promise(r => { resolveAnim = r; }) }],
    };
    engine.instances.set('c1', revealInst(sets));
    const root = revealRoot(panel);

    const panned = await engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16);
    assert.equal(panned, false, 'the timeout fallback resolved first');
    assert.deepEqual(sets, [], 'nothing panned before the animation finished');

    resolveAnim();
    await Promise.resolve(); // let the animation .then(pan, pan) run its rAF chain

    assert.deepEqual(sets, [], 'the late animation does not pan a settled (timed-out) call');
});

test('revealRectAboveSheet refuses to pan when a pinch starts during the animation wait', async () => {
    // The gesture guard at call time saw a free viewBox; a pinch starts while the snap animation
    // plays — the deferred pan must re-check and stand down.
    const engine = loadEngine({ extra: { requestAnimationFrame: cb => cb() } });
    const sets = [];
    let resolveAnim;
    const panel = {
        getBoundingClientRect: () => ({ top: 400, height: 200 }),
        getAnimations: () => [{ finished: new Promise(r => { resolveAnim = r; }) }],
    };
    const inst = revealInst(sets);
    engine.instances.set('c1', inst);
    const root = revealRoot(panel);

    let done = null;
    const pending = engine.revealRectAboveSheet(root, 100, 450, 50, 50, 16)
        .then(v => { done = v; });
    await Promise.resolve();
    assert.equal(done, null, 'the pan waits for the animation to settle');

    inst.isPinching = true; // a second finger lands during the wait
    resolveAnim();
    await pending;

    assert.equal(done, false, 'the deferred pan stands down for the live pinch');
    assert.deepEqual(sets, [], 'the viewBox stays untouched');
});

test('focusFirstAction focuses the first mobile action bar tile', () => {
    const engine = loadEngine();
    const focused = [];
    const tile = { focus: (opts) => focused.push(opts) };
    const root = { querySelector: (sel) => sel === '.tm-mobile-action-bar__action' ? tile : null };

    assert.equal(engine.focusFirstAction(root), true);
    assert.deepEqual(plain(focused), [{ preventScroll: true }], 'the tile is focused without scrolling');
    assert.equal(engine.focusFirstAction(null), false);
    assert.equal(engine.focusFirstAction({ querySelector: () => null }), false,
        'no bar — nothing to focus, no crash');
});
