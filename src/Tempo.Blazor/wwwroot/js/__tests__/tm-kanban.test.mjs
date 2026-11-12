// TmKanbanBoard touch module tests (no jsdom): the pure zone/insertion/indicator resolvers, the
// long-press gate, the single commit on release (drop AND rollback-on-release-outside) reporting
// the FULL-LIST beforeIndex and the source key, the cancel report (Escape / pointercancel / a
// second finger) and the verbatim DOM cleanup, the ghost contract (inert, anonymous, held until the
// commit resolves), the native dragstart block during a touch session, the edge auto-scroll (board
// root horizontal, the hovered cell's card list vertical, swapping on zone change), the disabled-
// less module contract, and the public lane encoding ("" = classic layout, the NoSwimlaneId
// sentinel).
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    TOUCH_LONG_PRESS_MS,
    __resetForTests,
    attachKanbanBoard,
    detachKanbanBoard,
    resolveIndicatorPosition,
    resolveInsertIndex,
    resolveZoneIndex,
} from '../tm-kanban.js';

// ── Pure resolvers ────────────────────────────────────────────────────────────────────────────

const zones = [
    { top: 0, left: 0, right: 200, bottom: 400 },     // todo
    { top: 0, left: 216, right: 416, bottom: 400 },   // doing
    { top: 0, left: 432, right: 632, bottom: 400 },   // done
];

test('resolveZoneIndex: the pointer inside a zone resolves to that zone', () => {
    assert.equal(resolveZoneIndex(zones, 100, 200), 0);
    assert.equal(resolveZoneIndex(zones, 300, 10), 1);
    assert.equal(resolveZoneIndex(zones, 500, 399), 2);
});

test('resolveZoneIndex: the gap between zones and the empty list resolve to nothing', () => {
    assert.equal(resolveZoneIndex(zones, 208, 200), -1, 'the column gap must not invent a target');
    assert.equal(resolveZoneIndex(zones, 700, 200), -1);
    assert.equal(resolveZoneIndex([], 100, 100), -1);
    assert.equal(resolveZoneIndex(null, 100, 100), -1);
});

const cards = (...tops) => tops.map(top => ({ top, height: 40 }));

test('resolveInsertIndex: the midpoint rule decides before/after, the count appends', () => {
    assert.equal(resolveInsertIndex(cards(0, 40, 80), 10), 0);
    assert.equal(resolveInsertIndex(cards(0, 40, 80), 30), 1, 'below the first centre, above the second');
    assert.equal(resolveInsertIndex(cards(0, 40, 80), 150), 3, 'past every centre appends');
    assert.equal(resolveInsertIndex(cards(0, 40, 80), 200), 3);
});

test('resolveInsertIndex: an empty cell has exactly one slot — 0', () => {
    assert.equal(resolveInsertIndex([], 50), 0);
    assert.equal(resolveInsertIndex(null, 50), 0);
});

const cardRows = (...tops) => tops.map(top => ({ top, height: 40, left: 8, right: 192 }));

test('resolveIndicatorPosition: the line sits on the top edge of the card before the slot', () => {
    const zone = zones[0];
    assert.deepEqual(resolveIndicatorPosition(zone, cardRows(10, 50, 90), 1), { top: 50, left: 8, width: 184 });
    assert.deepEqual(resolveIndicatorPosition(zone, cardRows(10, 50, 90), 0), { top: 10, left: 8, width: 184 });
});

test('resolveIndicatorPosition: appending puts the line on the last card bottom edge', () => {
    const zone = zones[0];
    assert.deepEqual(resolveIndicatorPosition(zone, cardRows(10, 50, 90), 3), { top: 130, left: 8, width: 184 });
});

test('resolveIndicatorPosition: a zone without cards carries no line (the outline is the feedback)', () => {
    assert.equal(resolveIndicatorPosition(zones[0], [], 0), null);
    assert.equal(resolveIndicatorPosition(zones[0], null, 0), null);
    assert.equal(resolveIndicatorPosition(null, cards(10), 0), null);
});

test('the touch drag is a 400 ms hold — a tap and a scroll keep working', () => {
    assert.equal(TOUCH_LONG_PRESS_MS, 400);
});

// ── Gesture over a hand-rolled DOM ────────────────────────────────────────────────────────────

function fakeClassList(owner) {
    const classes = new Set();
    return {
        add: (...names) => names.forEach(n => classes.add(n)),
        remove: (...names) => names.forEach(n => classes.delete(n)),
        contains: (name) => classes.has(name),
        _set: classes,
        _owner: owner,
    };
}

function fakeElement(tag, attrs = {}) {
    const el = {
        tagName: tag,
        children: [],
        style: {},
        dataset: {},
        _attrs: { ...attrs },
        _listeners: {},
        parent: null,
    };
    el.getAttribute = (name) => (name in el._attrs ? el._attrs[name] : null);
    el.setAttribute = (name, value) => { el._attrs[name] = String(value); };
    el.removeAttribute = (name) => { delete el._attrs[name]; };
    el.classList = fakeClassList(el);
    el.addEventListener = (type, fn) => { (el._listeners[type] ??= []).push(fn); };
    el.removeEventListener = (type, fn) => {
        el._listeners[type] = (el._listeners[type] ?? []).filter(f => f !== fn);
    };
    el.dispatch = (type, event = {}) => {
        event.type = type;
        event.target ??= el;
        event.preventDefault ??= () => { event.defaultPrevented = true; };
        event.stopPropagation ??= () => { event.propagationStopped = true; };
        // Bubble like the real DOM: the module and the shared helper listen on the ROOT, the
        // synthetic events are dispatched on the card.
        for (let node = el; node; node = node.parent) {
            for (const fn of node._listeners[type] ?? []) fn(event);
        }
        return event;
    };
    el.appendChild = (child) => { child.parent = el; el.children.push(child); return child; };
    el.remove = () => {
        if (!el.parent) return;
        el.parent.children = el.parent.children.filter(c => c !== el);
        el.parent = null;
    };
    el.cloneNode = () => {
        const copy = fakeElement(el.tagName, { ...el._attrs });
        copy._rect = el._rect;
        for (const name of el.classList._set) copy.classList.add(name);
        for (const child of el.children) copy.appendChild(child.cloneNode());
        return copy;
    };
    el.contains = (node) => {
        for (let p = node; p; p = p.parent) if (p === el) return true;
        return false;
    };
    el.closest = (selector) => {
        if (selector.startsWith('.')) {
            const name = selector.slice(1);
            for (let p = el; p; p = p.parent) if (p.classList?.contains(name)) return p;
        }
        for (let p = el; p; p = p.parent) {
            if (matchesSelector(p, selector)) return p;
        }
        return null;
    };
    el.querySelector = (selector) => el.querySelectorAll(selector)[0] ?? null;
    el.querySelectorAll = (selector) => {
        const out = [];
        const walk = (node) => {
            for (const child of node.children) {
                if (matchesSelector(child, selector)) out.push(child);
                walk(child);
            }
        };
        walk(el);
        return out;
    };
    el.getBoundingClientRect = () => el._rect ?? { top: 0, left: 0, right: 0, bottom: 0, height: 0 };
    return el;
}

function matchesSelector(el, selector) {
    if (selector.includes(',')) return selector.split(',').some(s => matchesSelector(el, s.trim()));
    if (selector.startsWith('[')) {
        const name = selector.slice(1, -1);
        if (name.endsWith(']')) return false;
        return el._attrs[name] !== undefined;
    }
    if (selector.startsWith('.')) return el.classList.contains(selector.slice(1));
    return el.tagName === selector;
}

/** A scroll container the auto-scroller can drive: the position clamps to 0..max like a browser. */
function makeScrollable(el, maxScrollLeft = 1000, maxScrollTop = 1000) {
    let scrollLeft = 0;
    let scrollTop = 0;
    Object.defineProperty(el, 'scrollLeft', {
        get: () => scrollLeft,
        set(value) { scrollLeft = Math.max(0, Math.min(maxScrollLeft, value)); },
    });
    Object.defineProperty(el, 'scrollTop', {
        get: () => scrollTop,
        set(value) { scrollTop = Math.max(0, Math.min(maxScrollTop, value)); },
    });
    return el;
}

/**
 * One board: two classic columns, one card per column, with the module's data attributes.
 * @param {boolean} withCards when true, each column holds a `.tm-kanban__cards` scroll container
 *   (a vertical edge-scroll target) around its card.
 */
function fakeBoard(withCards = false) {
    const root = makeScrollable(fakeElement('div'));
    root._rect = { top: 0, left: 0, right: 416, bottom: 400 };
    const zonesByColumn = {};
    const cardsByColumn = {};

    for (const [index, column] of ['todo', 'doing'].entries()) {
        const zone = fakeElement('div', {
            'data-tm-kanban-column': column,
            'data-tm-kanban-lane': '',
        });
        zone._rect = { top: 0, left: index * 216, right: index * 216 + 200, bottom: 400 };
        zone.classList.add('tm-kanban__column');
        const card = fakeElement('div', {
            'data-tm-kanban-card': 'true',
            'data-tm-kanban-index': '0',
            'data-tm-kanban-key': String(index),
        });
        card._rect = { top: 10, left: index * 216 + 8, right: index * 216 + 192, bottom: 90, height: 80 };
        let container = zone;
        if (withCards) {
            const cards = makeScrollable(fakeElement('div'));
            cards._rect = { top: 0, left: index * 216, right: index * 216 + 200, bottom: 300 };
            cards.classList.add('tm-kanban__cards');
            zone.appendChild(cards);
            cardsByColumn[column] = cards;
            container = cards;
        }
        container.appendChild(card);
        root.appendChild(zone);
        zonesByColumn[column] = zone;
    }

    return { root, zonesByColumn, cardsByColumn };
}

function fakeDocument(root, zonesForHitTest) {
    globalThis.document = {
        elementFromPoint: (x, y) => {
            for (const zone of zonesForHitTest()) {
                const r = zone._rect;
                if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) {
                    return zone.querySelector?.('[data-tm-kanban-card]') ?? zone.children[0] ?? zone;
                }
            }
            return null;
        },
        createElement: (tag) => fakeElement(tag),
        body: fakeElement('body'),
        addEventListener: () => {},
        removeEventListener: () => {},
    };
}

function collectDotnet() {
    const calls = [];
    return {
        calls,
        ref: {
            invokeMethodAsync: async (name, ...args) => { calls.push({ name, args }); },
        },
    };
}

/** A rAF queue the auto-scroll tests flush by hand: deterministic frames, no timers. */
function fakeRaf() {
    const queue = new Map();
    let next = 1;
    globalThis.requestAnimationFrame = callback => {
        const id = next++;
        queue.set(id, callback);
        return id;
    };
    globalThis.cancelAnimationFrame = id => { queue.delete(id); };
    return {
        flush() {
            const entries = [...queue.entries()];
            queue.clear();
            for (const [, callback] of entries) callback(0);
            return entries.length;
        },
        pending: () => queue.size,
    };
}

test.beforeEach(() => {
    __resetForTests();
    delete globalThis.document;
    // The auto-scrollers schedule rAF frames; a no-op scheduler keeps the gesture tests honest
    // (frames never run unless a test flushes them through fakeRaf()).
    globalThis.requestAnimationFrame = () => 0;
    globalThis.cancelAnimationFrame = () => {};
});

test.afterEach(() => {
    detachKanbanBoard('board-1');
    __resetForTests();
    delete globalThis.document;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
});

const nextFrame = () => new Promise(resolve => setTimeout(resolve, 0));
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

test('a held press starts the drag; release over another zone commits the ONE drop', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    // The hold has not elapsed: a move is a scroll, not a drag. A small drift inside the tolerance
    // keeps the hold alive; beyond it the helper would abort the gesture as a swipe.
    card.dispatch('pointermove', { pointerId: 1, clientX: 42, clientY: 41 });
    assert.equal(globalThis.document.body.children.length, 0, 'no ghost before the hold completes');
    assert.ok(!card.style.visibility, 'the source card is untouched before the drag starts');

    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 120, clientY: 60 });
    const ghost = globalThis.document.body.children[0];
    assert.ok(ghost, 'the ghost lifts once the hold completes');
    assert.equal(ghost.style.left, '88px', 'the ghost starts at the card box (8) and follows the pointer (+80)');
    assert.equal(ghost.style.top, '30px');
    assert.equal(card.style.visibility, 'hidden', 'the source stays in the flow, invisibly — no sibling jump');
    assert.ok(ghost.classList.contains('tm-kanban__card--touch-dragging'));

    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 100 });
    assert.ok(zonesByColumn.doing.classList.contains('tm-kanban__cell--touch-target'),
        'the expanded hovered zone is highlighted');

    card.dispatch('pointerup', { pointerId: 1, clientX: 300, clientY: 100 });

    assert.deepEqual(dotnet.calls.map(c => c.name), ['OnKanbanTouchDrop'], 'exactly one commit');
    assert.deepEqual(dotnet.calls[0].args, ['todo', null, 0, 0, 'doing', null, -1],
        'release over the doing column appends (beforeIndex -1), carrying the source key');

    // The ghost is held until the commit report settles — one macrotask later here.
    await nextFrame();
    assert.equal(globalThis.document.body.children.length, 0, 'the ghost and the indicator are dropped');
    assert.equal(card.style.visibility, '', 'the source card is visible again');
    assert.ok(!zonesByColumn.doing.classList.contains('tm-kanban__cell--touch-target'));
    assert.ok(!root.classList.contains('tm-kanban--dragging'));
});

test('pointercancel cancels: every paint is rolled back and the cancel is announced', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 100 });

    globalThis.document._listeners = {};
    // The browser turns an Escape / a system gesture into pointercancel — drive the cancel the
    // same way.
    card.dispatch('pointercancel', { pointerId: 1 });

    assert.deepEqual(dotnet.calls.map(c => c.name), ['OnKanbanTouchDragCancelled']);
    assert.deepEqual(dotnet.calls[0].args, ['todo', null, 0, 0]);
    assert.equal(globalThis.document.body.children.length, 0, 'the ghost is dropped on cancel');
    assert.equal(card.style.visibility, '', 'the source card is restored');
    await nextFrame();
});

test('releasing outside every zone rolls back and announces the cancel', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 900, clientY: 600 });

    card.dispatch('pointerup', { pointerId: 1, clientX: 900, clientY: 600 });

    assert.deepEqual(dotnet.calls.map(c => c.name), ['OnKanbanTouchDragCancelled'],
        'a drop on no zone is a cancel, never a silent loss');
    await nextFrame();
    assert.equal(globalThis.document.body.children.length, 0);
    assert.equal(card.style.visibility, '');
});

test('a second finger cancels the held gesture (superseded) and announces it', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 100, clientY: 50 });

    // The second finger lands; the browser takes the gesture over from the first pointer, which
    // receives pointercancel — the shared helper rolls the drag back on that cancel.
    card.dispatch('pointerdown', {
        pointerId: 2, pointerType: 'touch', isPrimary: false, button: 0, clientX: 60, clientY: 60,
    });
    card.dispatch('pointercancel', { pointerId: 1 });

    assert.deepEqual(dotnet.calls.map(c => c.name), ['OnKanbanTouchDragCancelled']);
    assert.deepEqual(dotnet.calls[0].args, ['todo', null, 0, 0]);
    assert.ok(!card.style.visibility, 'a second finger rolls the drag back and restores the source');
});

test('a collapsed lane slot reports the lane sentinel; a mouse press never starts a touch drag', async () => {
    const { root, zonesByColumn } = fakeBoard();
    const slot = fakeElement('div', {
        'data-tm-kanban-column': 'todo',
        'data-tm-kanban-lane': '__tm_kanban_no_swimlane__',
    });
    // The slot sits BELOW the columns (a collapsed lane's strip), so the hit-test reaches it.
    slot._rect = { top: 420, left: 0, right: 200, bottom: 460 };
    slot.classList.add('tm-kanban__lane-slot');
    root.appendChild(slot);
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing, slot]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    // Mouse is the HTML5 path: the module must not even start a session.
    card.dispatch('pointerdown', {
        pointerId: 7, pointerType: 'mouse', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 7, clientX: 300, clientY: 100 });
    card.dispatch('pointerup', { pointerId: 7, clientX: 300, clientY: 100 });
    assert.deepEqual(dotnet.calls, [], 'a mouse press is left to the HTML5 drag entirely');

    // Touch onto the collapsed lane slot carries the sentinel through the drop.
    card.dispatch('pointerdown', {
        pointerId: 8, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 8, clientX: 20, clientY: 430 });
    assert.ok(slot.classList.contains('tm-kanban__lane-slot--drop-target'),
        'the slot highlights with its own modifier');
    card.dispatch('pointerup', { pointerId: 8, clientX: 20, clientY: 430 });

    assert.deepEqual(dotnet.calls.map(c => c.name), ['OnKanbanTouchDrop']);
    assert.deepEqual(dotnet.calls[0].args,
        ['todo', null, 0, 0, 'todo', '__tm_kanban_no_swimlane__', -1]);
});

test('detach cancels a live gesture without committing anything', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');
    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 100 });

    detachKanbanBoard('board-1');

    // The cancel report races disposal by design; the DOM must be clean either way.
    assert.equal(card.style.visibility, '');
    assert.ok(!root.classList.contains('tm-kanban--dragging'));
});

// ── The review fixes ──────────────────────────────────────────────────────────────────────────

test('B1: the drop reports the FULL-LIST index of the hovered card, not the visible-window position', async () => {
    // A virtualized cell renders only a WINDOW of its cards: the DOM holds cards whose
    // data-tm-kanban-index values are a slice of the full list (10..12 of a 40-card cell). The
    // beforeIndex must be the hovered card's own full-list index, so the server reads the same slot
    // it would read on a fully-rendered cell.
    const { root, zonesByColumn } = fakeBoard();
    const target = zonesByColumn.doing;
    const targetCard = target.querySelector('[data-tm-kanban-card]');
    targetCard.remove();
    const windows = [];
    for (const [i, fullIndex] of [10, 11, 12].entries()) {
        const card = fakeElement('div', {
            'data-tm-kanban-card': 'true',
            'data-tm-kanban-index': String(fullIndex),
            'data-tm-kanban-key': String(100 + fullIndex),
        });
        card._rect = { top: 100 + i * 80, left: 216 + 8, right: 216 + 192, bottom: 180 + i * 80, height: 80 };
        target.appendChild(card);
        windows.push(card);
    }
    fakeDocument(root, () => [zonesByColumn.todo, target]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    // Between the first centre (140) and the second (220): insert before the SECOND window card.
    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 160 });
    card.dispatch('pointerup', { pointerId: 1, clientX: 300, clientY: 160 });

    assert.deepEqual(dotnet.calls.map(c => c.name), ['OnKanbanTouchDrop']);
    assert.deepEqual(dotnet.calls[0].args, ['todo', null, 0, 0, 'doing', null, 11],
        'beforeIndex is the hovered card FULL-LIST index (11), not its visible-window position (1)');
});

test('B2: native dragstart is blocked during a touch session; the mouse path is untouched', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    // No touch yet: a (mouse) dragstart is the HTML5 path and must reach the card untouched.
    let event = card.dispatch('dragstart');
    assert.ok(!event.defaultPrevented, 'before any touch the HTML5 dragstart is left alone');

    // A touch press (even before the hold completes) makes the touch session active.
    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    event = card.dispatch('dragstart');
    assert.ok(event.defaultPrevented && event.propagationStopped,
        'dragstart is blocked while the touch session owns the gesture');

    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 100 });
    event = card.dispatch('dragstart');
    assert.ok(event.defaultPrevented, 'the block holds for the whole drag');

    card.dispatch('pointerup', { pointerId: 1, clientX: 300, clientY: 100 });
    await nextFrame();

    // A mouse press afterwards: the HTML5 path is back.
    card.dispatch('pointerdown', {
        pointerId: 2, pointerType: 'mouse', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    event = card.dispatch('dragstart');
    assert.ok(!event.defaultPrevented, 'a mouse dragstart is never blocked');
});

test('the ghost is inert and anonymous: ids and test ids are stripped from the clone', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');
    card.setAttribute('id', 'card-source-id');
    card.setAttribute('data-testid', 'card-source-testid');
    const inner = fakeElement('span', { id: 'card-inner-id', 'data-testid': 'card-inner-testid' });
    card.appendChild(inner);

    attachKanbanBoard(root, dotnet.ref, 'board-1');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 120, clientY: 60 });

    const ghost = globalThis.document.body.children[0];
    assert.ok(ghost, 'the ghost lifted');
    assert.equal(ghost.inert, true, 'the clone is inert — no focus, no pointer, no assistive tech');
    assert.equal(ghost.getAttribute('id'), null, 'the clone root id is stripped');
    assert.equal(ghost.getAttribute('data-testid'), null, 'the clone root test id is stripped');
    assert.equal(ghost.querySelector('[id]'), null, 'no id survives anywhere in the clone subtree');
    assert.equal(ghost.querySelector('[data-testid]'), null, 'no test id survives anywhere in the clone subtree');
    assert.equal(card.getAttribute('id'), 'card-source-id', 'the SOURCE card keeps its identity');

    card.dispatch('pointerup', { pointerId: 1, clientX: 120, clientY: 60 });
    await nextFrame();
});

test('the ghost stays under the finger until the commit report resolves (no snap-back on Server)', async () => {
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    // A .NET round trip takes a while (Blazor Server): the invoke resolves 40 ms after the release.
    const calls = [];
    const ref = {
        invokeMethodAsync: (name, ...args) => new Promise(resolve => setTimeout(() => {
            calls.push({ name, args });
            resolve();
        }, 40)),
    };

    attachKanbanBoard(root, ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));
    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 100 });
    card.dispatch('pointerup', { pointerId: 1, clientX: 300, clientY: 100 });

    await sleep(10);
    assert.equal(
        globalThis.document.body.querySelectorAll('.tm-kanban__card--touch-dragging').length, 1,
        'the ghost is held while the commit is still in flight');
    assert.equal(card.style.visibility, 'hidden', 'the source stays lifted until the commit lands');

    await sleep(80);
    assert.equal(calls.length, 1, 'the commit arrived');
    assert.equal(
        globalThis.document.body.querySelectorAll('.tm-kanban__card--touch-dragging').length, 0,
        'the ghost releases only after the commit resolved');
    assert.equal(card.style.visibility, '');
});

test('B3: edge auto-scroll — the board root scrolls horizontally near its edge and stays still away from it', async () => {
    const raf = fakeRaf();
    const { root, zonesByColumn } = fakeBoard();
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));

    // The middle of the board: no edge power on any side.
    card.dispatch('pointermove', { pointerId: 1, clientX: 208, clientY: 200 });
    raf.flush();
    assert.equal(root.scrollLeft, 0, 'away from the edge the board stays still');
    assert.equal(root.scrollTop, 0, 'the root scroller is horizontal only');

    // 10 px from the board's right edge (rect.right 416): power (36-10)/36, dx = round(26/36*28) = 20.
    card.dispatch('pointermove', { pointerId: 1, clientX: 406, clientY: 200 });
    raf.flush();
    assert.equal(root.scrollLeft, 20, 'near the edge the board scrolls towards the hidden columns');

    // The loop keeps scrolling while the finger rests in the band; the zone highlight is re-hit per
    // scrolled frame (the content moved under the stationary pointer).
    card.dispatch('pointermove', { pointerId: 1, clientX: 406, clientY: 200 });
    raf.flush();
    assert.equal(root.scrollLeft, 40, 'a resting finger keeps scrolling');
    assert.ok(zonesByColumn.doing.classList.contains('tm-kanban__cell--touch-target'),
        'the zone under the stationary pointer is re-resolved on the scrolled frame');

    card.dispatch('pointerup', { pointerId: 1, clientX: 406, clientY: 200 });
    await nextFrame();
    assert.equal(raf.pending(), 0, 'the scroll loop stops on release');
    assert.equal(root.scrollLeft, 40, 'and nothing scrolls after it stopped');
});

test('B3: the hovered cell\'s card list scrolls vertically and the scroller swaps on zone change', async () => {
    const raf = fakeRaf();
    const { root, zonesByColumn, cardsByColumn } = fakeBoard(true);
    fakeDocument(root, () => [zonesByColumn.todo, zonesByColumn.doing]);
    const dotnet = collectDotnet();

    attachKanbanBoard(root, dotnet.ref, 'board-1');
    const card = zonesByColumn.todo.querySelector('[data-tm-kanban-card]');

    card.dispatch('pointerdown', {
        pointerId: 1, pointerType: 'touch', isPrimary: true, button: 0, clientX: 40, clientY: 40,
    });
    await new Promise(resolve => setTimeout(resolve, 450));

    // 10 px above the todo card list's bottom edge (rect.bottom 300): dy = round(26/36*28) = 20.
    card.dispatch('pointermove', { pointerId: 1, clientX: 100, clientY: 290 });
    raf.flush();
    assert.equal(cardsByColumn.todo.scrollTop, 20, 'the hovered cell\'s card list scrolls vertically');
    assert.equal(cardsByColumn.doing.scrollTop, 0, 'the other cell\'s list is untouched');

    // Swap zones: the pointer crosses into the doing column, near ITS list's bottom edge.
    card.dispatch('pointermove', { pointerId: 1, clientX: 300, clientY: 290 });
    raf.flush();
    assert.equal(cardsByColumn.todo.scrollTop, 20, 'the source list keeps its scroll position');
    assert.equal(cardsByColumn.doing.scrollTop, 20, 'the vertical scroller swapped to the hovered cell');

    card.dispatch('pointerup', { pointerId: 1, clientX: 300, clientY: 290 });
    await nextFrame();
    assert.equal(raf.pending(), 0, 'both scroll loops stop on release');
});
