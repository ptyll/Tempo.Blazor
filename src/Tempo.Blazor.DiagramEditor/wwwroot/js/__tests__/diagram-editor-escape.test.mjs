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
    const engine = window.tmDiagramEditor;
    // Test hook: the classic script installs its own window.tmDiagramStencilShape registry during
    // evaluation (overwriting anything injected through `win`), so tests reach the LIVE registry
    // through the evaluated window instead of pre-seeding it.
    if (engine) engine.__vmWindow = window;
    return engine;
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

// ── Final review (Modeling redesign): inline label edits, inert hosts and selection clears ──

test('escape_in_edge_label_input_is_consumed_and_cancels_edit', () => {
    const engine = loadEngine();
    // The edge-label foreignObject input: document.activeElement is the .tm-diagram-edge-label-input
    // inside the canvas container; no search panel and no view menu are open.
    const labelInput = {
        tagName: 'INPUT',
        className: 'tm-diagram-edge-label-input',
        closest: (selector) => (typeof selector === 'string' && selector.includes('tm-diagram-edge-label-input')) ? labelInput : null,
    };
    const doc = { activeElement: labelInput };
    const engineDoc = loadEngine({ document: doc });
    const { inst, log } = fakeInst({}, { contains: () => true });
    inst.container = Object.assign(inst.container, { contains: (el) => el === labelInput });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engineDoc._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, true, 'the edit owns the Escape');
    assert.equal(e.tmEscapeHandled, true, 'the host overlay (the dialog) must not close under the caret');
    assert.deepEqual(log, [['OnInlineEditEscape']],
        'the canvas cancels the label edit — value reverted, nothing pushed to the undo stack');
});

test('escape_in_node_inline_input_is_consumed (the owning shape cancels its edit)', () => {
    const shapeLog = [];
    const shapeRef = { invokeMethodAsync: (name) => { shapeLog.push(name); return Promise.resolve(); } };
    const nodeGroup = { getAttribute: (a) => (a === 'data-node-id' ? 'n1' : null) };
    const inlineInput = {
        tagName: 'INPUT',
        className: 'tm-diagram-node__inline-input',
        closest: (selector) => {
            if (typeof selector === 'string' && selector.includes('tm-diagram-node__inline-input')) return inlineInput;
            if (selector === 'g.tm-diagram-node[data-node-id]') return nodeGroup;
            return null;
        },
    };
    const doc = { activeElement: inlineInput };
    const engineDoc = loadEngine({ document: doc });
    engineDoc.__vmWindow.tmDiagramStencilShape.getRef = (id) => (id === 'n1' ? shapeRef : null);
    const { inst, log } = fakeInst({}, { contains: () => true });
    inst.container = Object.assign(inst.container, { contains: (el) => el === inlineInput });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engineDoc._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, true, 'the node inline edit owns the Escape');
    assert.equal(e.tmEscapeHandled, true, 'the dialog stays open');
    assert.deepEqual(shapeLog, ['CancelActiveEditFromJs'], 'the registered shape cancels its inline edit');
    assert.deepEqual(log, [['OnInlineEditEscape']], 'the canvas callback is the fallback owner');
});

test('a TEXTAREA node inline edit (tm-diagram-node__inline-textarea) consumes Escape the same way', () => {
    const labelArea = {
        tagName: 'TEXTAREA',
        className: 'tm-diagram-node__inline-textarea',
        closest: (selector) => (typeof selector === 'string' && selector.includes('tm-diagram-node__inline-textarea')) ? labelArea : null,
    };
    const doc = { activeElement: labelArea };
    const engineDoc = loadEngine({ document: doc });
    const { inst, log } = fakeInst({}, { contains: () => true });
    inst.container = Object.assign(inst.container, { contains: (el) => el === labelArea });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engineDoc._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, true);
    assert.equal(e.tmEscapeHandled, true);
    assert.deepEqual(log, [['OnInlineEditEscape']]);
});

test('handled_escape_skips_the_selection_clear: the canvas handler runs after the inline-edit consumer', () => {
    // Fáze 10 review: the SAME event object flows through both capture listeners. The node
    // inline edit consumes it first (marked), so _onKeyDown must not ALSO clear a non-empty
    // selection on the same keypress.
    const shapeRef = { invokeMethodAsync: () => Promise.resolve() };
    const nodeGroup = { getAttribute: (a) => (a === 'data-node-id' ? 'n1' : null) };
    const inlineInput = {
        tagName: 'INPUT',
        className: 'tm-diagram-node__inline-input',
        closest: (selector) => {
            if (typeof selector === 'string' && selector.includes('tm-diagram-node__inline-input')) return inlineInput;
            if (selector === 'g.tm-diagram-node[data-node-id]') return nodeGroup;
            return null;
        },
    };
    const doc = { activeElement: inlineInput };
    const engineDoc = loadEngine({ document: doc });
    engineDoc.__vmWindow.tmDiagramStencilShape.getRef = (id) => (id === 'n1' ? shapeRef : null);
    const { inst, log } = fakeInst({ selectedIds: new Set(['n1']) }, { contains: () => true });
    inst.container = Object.assign(inst.container, { contains: (el) => el === inlineInput });
    engineDoc._updateSelection = () => {};
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engineDoc._onSearchEscape(e, inst);
    assert.equal(e.tmEscapeHandled, true, 'the inline edit consumed the Escape first');

    engineDoc._onKeyDown(e, inst);

    assert.deepEqual(log, [['OnInlineEditEscape']],
        'only the inline-edit cancel ran — OnClearSelection must NOT be invoked on the same keypress');
    assert.equal(inst.selectedIds.size, 1, 'the selection survives the inline-edit cancel');
});

test('a plain INPUT inside the canvas container that is NOT an inline editor does not consume', () => {
    const engine = loadEngine();
    const elsewhere = { tagName: 'INPUT' };
    const doc = { activeElement: elsewhere };
    const engineDoc = loadEngine({ document: doc });
    const { inst } = fakeInst();
    inst.container = Object.assign(inst.container, { contains: () => false });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engineDoc._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, false, 'an Escape typed outside the editor is not stolen');
    assert.equal(e.tmEscapeHandled, undefined);
});

test('_onKeyDown ignores the document.body fallback when the canvas sits inside an inert subtree', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ selectedIds: new Set(['n1']) });
    // The Modeling preview behind the open-editor dialog: the canvas is inside [inert], focus
    // fell to body — the DIALOG's editor owns the keys, this canvas is inert.
    inst.container = Object.assign(inst.container, {
        contains: () => false,
        closest: (selector) => selector === '[inert]' ? { fake: 'overlay' } : null,
    });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onKeyDown(e, inst);

    assert.equal(e.defaultPrevented, false, 'the inert canvas never consumes');
    assert.deepEqual(log, [], 'the dialog editor owns the keys');
});

test('_onKeyDown keeps the document.body fallback when no inert ancestor exists', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ selectedIds: new Set(['n1']) });
    inst.container = Object.assign(inst.container, { contains: () => false, closest: () => null });
    engine._updateSelection = () => {};
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onKeyDown(e, inst);

    assert.equal(e.defaultPrevented, true, 'the canvas fallback still clears a selection');
    assert.deepEqual(log, [['OnClearSelection']]);
});

test('the selection-clear Escape marks tmEscapeHandled (the host dialog stays open)', () => {
    const engine = loadEngine();
    const { inst } = fakeInst({ selectedIds: new Set(['n1']) });
    engine._updateSelection = () => {};
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onKeyDown(e, inst);

    assert.equal(e.defaultPrevented, true);
    assert.equal(e.tmEscapeHandled, true, 'a consumed Escape never bubbles to the host overlay');
});

// ── Fáze 10 (confirmed ordering): one press closes ONE layer, topmost first ──────────────
// context menu > "⋯" view menu > search panel > inline edit > gesture/selection > dialog.

test('escape_with_open_context_menu_and_search_closes_menu_first: the capture consumer skips while _menuOpen', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({ _menuOpen: true }, {
        querySelector: (selector) => selector === '.tm-diagram-search-panel' ? {} : null,
        contains: () => true,
    });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, false,
        'the open context menu is the topmost layer — its own capture handler owns this Escape');
    assert.equal(e.tmEscapeHandled, undefined, 'nothing is marked: the menu handler consumes it next');
    assert.deepEqual(log, [], 'the search panel must NOT close under an open context menu');
});

test('escape_with_open_context_menu_skips_the_inline_edit_consumer_too', () => {
    const engine = loadEngine();
    const labelInput = {
        tagName: 'INPUT',
        className: 'tm-diagram-edge-label-input',
        closest: (selector) => (typeof selector === 'string' && selector.includes('tm-diagram-edge-label-input')) ? labelInput : null,
    };
    const doc = { activeElement: labelInput };
    const engineDoc = loadEngine({ document: doc });
    const { inst, log } = fakeInst({ _menuOpen: true }, { contains: () => true });
    inst.container = Object.assign(inst.container, { contains: (el) => el === labelInput });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engineDoc._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, false, 'the menu outranks the inline edit');
    assert.deepEqual(log, []);
});

test('second_escape_closes_search: with the menu gone, the panel is the topmost layer again', () => {
    const engine = loadEngine();
    const rootOverrides = {
        querySelector: (selector) => selector === '.tm-diagram-search-panel' ? {} : null,
        contains: () => true,
    };
    const first = fakeInst({ _menuOpen: true }, rootOverrides);
    const second = fakeInst({ _menuOpen: false }, rootOverrides);

    // Press 1: the menu owns the Escape — nothing is consumed here.
    const e1 = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    engine._onSearchEscape(e1, first.inst);
    assert.equal(e1.defaultPrevented, false, 'press 1 belongs to the context menu');
    assert.deepEqual(first.log, []);

    // Press 2: the menu is closed — the search panel is now the topmost layer.
    const e2 = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    engine._onSearchEscape(e2, second.inst);
    assert.equal(e2.defaultPrevented, true, 'press 2 closes the search panel');
    assert.equal(e2.tmEscapeHandled, true);
    assert.deepEqual(second.log, [['OnSearchEscape']], 'one press = one layer: the panel, not the dialog');
});

test('view menu outranks the search panel: both open → the menu closes first', () => {
    const engine = loadEngine();
    const { inst, log } = fakeInst({}, {
        querySelector: (selector) => (selector === '.tm-diagram-search-panel' || selector === '.tm-diagram-editor__view-menu[data-open]') ? {} : null,
        contains: () => true,
    });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.deepEqual(log, [['OnViewMenuEscape']], 'the "⋯" view menu sits above the search panel');
    assert.equal(e.defaultPrevented, true);
});

test('unconsumed_escape_stays_unmarked: no menu, no view menu, no panel, no inline edit', () => {
    const engine = loadEngine();
    const { inst } = fakeInst({}, { querySelector: () => null, contains: () => true });
    const e = { key: 'Escape', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };

    engine._onSearchEscape(e, inst);

    assert.equal(e.defaultPrevented, false, 'an unconsumed Escape must keep bubbling');
    assert.equal(e.tmEscapeHandled, undefined, 'the host dialog closes on it');
});
