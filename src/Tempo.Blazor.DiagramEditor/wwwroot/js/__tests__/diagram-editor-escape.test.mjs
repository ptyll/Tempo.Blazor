// Escape ordering (review fix, Modeling phase 4): every consumer this editor owns runs in the
// CAPTURE phase on `document` and marks an Escape it actually handles (preventDefault +
// tmEscapeHandled), so the core tm-focus-trap module — a document BUBBLE keydown listener — can
// never race it. An Escape NOTHING here consumes stays unmarked and keeps bubbling, which is what
// lets a host overlay (a TmModal dialog) close.
import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm'; import fs from 'node:fs'; import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const SRC_PATH = path.resolve(here, '../diagram-editor.js');
const SRC = fs.readFileSync(SRC_PATH, 'utf8');

/** Evaluates the classic script in a fresh context; returns window.tmDiagramEditor. */
function loadEngine({ document: doc = {}, window: win = {}, extra = {} } = {}) {
    const window = Object.assign({ innerWidth: 1280, innerHeight: 800 }, win);
    const document = doc;
    if (typeof document.addEventListener !== 'function') document.addEventListener = () => {};
    if (typeof document.removeEventListener !== 'function') document.removeEventListener = () => {};
    const ctx = vm.createContext({ window, document, URL, console, setTimeout, clearTimeout, performance, ...extra });
    vm.runInContext(SRC, ctx, { filename: SRC_PATH });
    return window.tmDiagramEditor;
}

/** A document that records keydown listener registrations (capture flag included). */
function recordingDocument() {
    const added = [];
    const removed = [];
    return {
        added, removed,
        activeElement: null,
        addEventListener(type, fn, options) { if (type === 'keydown') added.push({ fn, capture: options === true || (options && options.capture) === true }); },
        removeEventListener(type, fn, options) { if (type === 'keydown') removed.push({ fn, capture: options === true || (options && options.capture) === true }); },
    };
}

function fakeInst(overrides = {}, rootOverrides = {}) {
    const log = [];
    const root = Object.assign({
        // The fake editor root: the search panel + view menu are looked up INSIDE it and the
        // focus check is root.contains(document.activeElement).
        querySelector: () => null,
        contains: () => false,
    }, rootOverrides);
    const container = {
        id: 'c1',
        style: {},
        closest: (selector) => selector === '.tm-diagram-editor' ? root : null,
        contains: () => true,
        focus() {},
        addEventListener() {},
        removeEventListener() {},
    };
    const inst = Object.assign({
        id: 'c1',
        container,
        readOnly: false,
        selectedIds: new Set(),
        spaceHeld: false,
        toolMode: 'select',
        isRotating: false,
        isPanning: false,
        isPendingEdgeDraw: false,
        isDrawingEdge: false,
        isDraggingWaypoint: false,
        isDraggingWholeEdge: false,
        svg: { querySelector: () => null },
        dotNetRef: { invokeMethodAsync: (name, ...args) => { log.push([name, ...args]); return Promise.resolve(); } },
    }, overrides);
    return { inst, log, root };
}

test('_attachEvents registers both keydown consumers in the CAPTURE phase', () => {
    const doc = recordingDocument();
    const engine = loadEngine({ document: doc });
    const { inst } = fakeInst();
    engine._attachEvents(inst);

    const keydowns = doc.added;
    assert.equal(keydowns.length, 2, 'the search-escape consumer and the canvas shortcut handler register');
    assert.ok(keydowns.every(k => k.capture === true), 'both run before the bubble-phase focus trap');
    assert.ok(keydowns.some(k => k.fn === inst._onSearchEscape), 'the search/view-menu escape consumer is registered');
    assert.ok(keydowns.some(k => k.fn === inst._onKeyDown), 'the canvas shortcut handler is registered');
});

test('_detachEvents removes the search-escape consumer with capture', () => {
    const doc = recordingDocument();
    const engine = loadEngine({ document: doc });
    const { inst } = fakeInst();
    engine._attachEvents(inst);
    const searchEscape = inst._onSearchEscape;
    const keyHandler = inst._onKeyDown;
    engine._detachEvents(inst);
    assert.ok(doc.removed.some(r => r.fn === searchEscape && r.capture === true),
        'capture registration must be matched by a capture removal');
    assert.ok(doc.removed.some(r => r.fn === keyHandler && r.capture === true));
});

test('search panel open + focus inside the editor: Escape is consumed and raises OnSearchEscape', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({}, {
        querySelector: (selector) => selector === '.tm-diagram-search-panel' ? { fake: 'panel' } : null,
        contains: () => true,
    });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, true, 'the consumer marks the event so the bubble-phase trap skips it');
    assert.equal(e.tmEscapeHandled, true, 'the trap honors the tmEscapeHandled flag too');
    assert.deepEqual(log, [['OnSearchEscape']], 'the canvas raises OnSearchEscape → OnCloseSearch');
});

test('Escape from the search INPUT: the search surface consumes it (not the host dialog)', () => {
    // The panel's own input: document.activeElement is inside the editor ROOT (the panel lives
    // there), which is exactly the condition the capture consumer requires.
    const doc = recordingDocument();
    const engine = loadEngine({ document: doc });
    const { inst, log } = fakeInst({}, {
        querySelector: (selector) => selector === '.tm-diagram-search-panel' ? {} : null,
        contains: (el) => el === doc.activeElement,
    });
    doc.activeElement = { tagName: 'INPUT' };

    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, true, 'Escape typed in the search input closes the panel');
    assert.equal(e.tmEscapeHandled, true, 'and never reaches the dialog behind the editor');
    assert.deepEqual(log, [['OnSearchEscape']]);
});

test('no search panel, no view menu: Escape stays unmarked (a host dialog may close)', () => {
    const engine = loadEngine();
    const { inst } = fakeInst({}, { querySelector: () => null, contains: () => true });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, false, 'an unconsumed Escape must keep bubbling');
    assert.equal(e.tmEscapeHandled, undefined);
});

test('search panel open but focus OUTSIDE the editor: the Escape is not stolen', () => {
    const engine = loadEngine();
    const { inst } = fakeInst({}, {
        querySelector: (selector) => selector === '.tm-diagram-search-panel' ? {} : null,
        contains: () => false,
    });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, false);
});

test('view menu open (data-open): Escape is consumed and raises OnViewMenuEscape', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({}, {
        querySelector: (selector) => selector === '.tm-diagram-editor__view-menu[data-open]' ? {} : null,
        contains: () => true,
    });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, true);
    assert.equal(e.tmEscapeHandled, true);
    assert.deepEqual(log, [['OnViewMenuEscape']], 'the canvas raises OnViewMenuEscape → OnCloseViewMenu');
});

test('already-consumed Escape (defaultPrevented / tmEscapeHandled) is skipped by the consumer', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({}, { querySelector: () => ({}), contains: () => true });

    const prevented = { key: 'Escape', defaultPrevented: true };
    engine._onSearchEscape(prevented, inst);
    const flagged = { key: 'Escape', defaultPrevented: false, tmEscapeHandled: true };
    engine._onSearchEscape(flagged, inst);

    assert.deepEqual(log, [], 'a handled Escape is never re-consumed');
});

test('empty canvas Escape: not prevented — it bubbles to a host overlay (the dialog closes)', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst();
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onKeyDown(e, inst);

    assert.equal(e.defaultPrevented, false, 'nothing to consume → the dialog layer may close');
    assert.deepEqual(log, [], 'no selection, no session → nothing is invoked');
});

test('Escape with a non-empty selection: consumed (preventDefault + OnClearSelection)', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ selectedIds: new Set(['n1']) });
    engine._updateSelection = () => {};
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onKeyDown(e, inst);

    assert.equal(e.defaultPrevented, true);
    assert.deepEqual(log, [['OnClearSelection']]);
});

test('Escape during an edge draw: consumed (the gesture wins over the dialog)', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ isDrawingEdge: true });
    engine._cancelEdgeDraw = () => {};
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onKeyDown(e, inst);

    assert.equal(e.defaultPrevented, true);
    assert.equal(log.length, 0, 'cancelEdgeDraw is the consumer; nothing else is invoked');
});
