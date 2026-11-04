// TmSortableList module tests with hand-rolled DOM stubs (no jsdom): the handle-mode options, the pure
// landing-slot resolver, the ONE commit reported on release, the sibling shift while a row is dragged, the
// verbatim rollback on Escape / pointercancel, the disabled list, the handle-mode veto for a press on the
// row body, the touch long-press gate of whole-item mode, the edge auto-scroll wiring and detach safety.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attachSortable,
    detachSortable,
    optionsFor,
    resolveTargetIndex,
    shouldPreventGrabbedKey,
    __resetForTests,
} from '../tm-sortable.js';

// The shared auto-scroller owns the requestAnimationFrame loop, so the stub is installed once: the
// module must never need a real frame clock to be testable.
globalThis.requestAnimationFrame = () => 0;
globalThis.cancelAnimationFrame = () => {};

test.beforeEach(() => {
    __resetForTests();
    delete globalThis.document;
});

test.afterEach(() => {
    delete globalThis.document;
});

// ── optionsFor: which grip starts a drag, and whether touch has to hold ──────────────────────

test('optionsFor: the handle drags immediately, the whole row needs a 400 ms hold', () => {
    assert.deepEqual(optionsFor('handle'), {
        gripSelector: '[data-tm-sortable-handle]',
        longPressMs: 0,
    });
    assert.deepEqual(optionsFor('whole-item'), {
        gripSelector: '[data-tm-sortable-item]',
        longPressMs: 400,
    });
});

// ── resolveTargetIndex: the landing slot is a pure decision ─────────────────────────────────

const boxesOf = (...tops) => tops.map(top => ({ top, height: 40 }));

test('resolveTargetIndex: staying inside the original row keeps it in place', () => {
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), 10, 0), 0);
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), 82, 1), 1, 'row 1 runs 40..80, 82 is above its centre');
});

test('resolveTargetIndex: crossing a centre trades places with that row', () => {
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), 70, 0), 1);
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), 110, 0), 2);
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), 10, 2), 0);
});

test('resolveTargetIndex: never leaves the list', () => {
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), -500, 1), 0);
    assert.equal(resolveTargetIndex(boxesOf(0, 40, 80), 5000, 1), 2);
    assert.equal(resolveTargetIndex([], 10, 0), 0, 'an empty list cannot resolve to a negative index');
    assert.equal(resolveTargetIndex(null, 10, 3), 3);
});

// ── the grabbed-row key guard: arrows are stopped, Tab and template controls are not ─────────

function keyEvent(key, target) {
    return { key, target, preventDefault() { this.defaultPrevented = true; } };
}

function fakeRow(grabbed) {
    const row = {
        getAttribute: name => (name === 'data-grabbed' ? String(grabbed) : null),
        closest: selector => (selector === '[data-tm-sortable-item]' ? row : null),
    };
    return row;
}

test('shouldPreventGrabbedKey: the reorder keys of a grabbed row lose their default action', () => {
    for (const key of ['ArrowUp', 'ArrowDown', 'Home', 'End', 'Enter', ' ', 'Spacebar', 'Escape']) {
        assert.equal(shouldPreventGrabbedKey(keyEvent(key, fakeRow(true))), true,
            `${key} must neither scroll the page nor re-activate the row`);
    }
});

test('shouldPreventGrabbedKey: Tab is NEVER prevented, so focus can leave the grabbed row', () => {
    assert.equal(shouldPreventGrabbedKey(keyEvent('Tab', fakeRow(true))), false,
        'a swallowed Tab would be the keyboard trap WCAG 2.1.2 forbids');
});

test('shouldPreventGrabbedKey: an ungrabbed row and a key from template content are left alone', () => {
    assert.equal(shouldPreventGrabbedKey(keyEvent('ArrowDown', fakeRow(false))), false);

    const row = fakeRow(true);
    const input = { closest: () => row };
    assert.equal(shouldPreventGrabbedKey(keyEvent('Enter', input)), false,
        'a control inside ItemTemplate keeps its own keys');

    assert.equal(shouldPreventGrabbedKey(keyEvent('ArrowDown', null)), false);
});

// ── DOM stubs ───────────────────────────────────────────────────────────────────────────────

function makeClassList() {
    const set = new Set();
    return {
        add: name => set.add(name),
        remove: name => set.delete(name),
        contains: name => set.has(name),
        values: () => [...set],
    };
}

function makeEventTarget() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of map.get(type) ?? []) fn(event); },
        count: type => (map.get(type) ?? []).length,
    };
}

/** A list root with three 40 px rows starting at y = 0, 40, 80. */
function stage({ disabled = false } = {}) {
    const rows = [];
    const root = {
        ...makeEventTarget(),
        dataset: disabled ? { disabled: 'true' } : {},
        scrollTop: 0,
        scrollLeft: 0,
        style: {},
        getBoundingClientRect: () => ({ top: 0, left: 0, right: 300, bottom: 400, width: 300, height: 400 }),
        scrollHeight: 400,
        clientHeight: 400,
        classList: makeClassList(),
        contains: element => element != null,
        querySelectorAll: selector => (selector === '[data-tm-sortable-item]' ? rows : rows.map(row => row.handle)),
    };

    for (let index = 0; index < 3; index++) {
        const top = index * 40;
        const row = {
            style: { transform: '', zIndex: '' },
            classList: makeClassList(),
            attributes: new Map([['data-sortable-source-index', String(index)]]),
            getAttribute(name) { return this.attributes.get(name) ?? null; },
            // The rect reflects the inline transform, exactly like a real DOM: that is what makes a
            // re-MEASURED box useless as a landing-slot input once the siblings have shifted.
            getBoundingClientRect() {
                const shift = /translateY\((-?[\d.]+)px\)/.exec(this.style.transform ?? '');
                const moved = shift ? Number.parseFloat(shift[1]) : 0;
                return { top: top + moved, left: 0, right: 300, bottom: top + moved + 40, width: 300, height: 40 };
            },
            setPointerCapture(id) { this.captured = id; },
            releasePointerCapture(id) { this.captured = null; this.released = id; },
            querySelectorAll: () => [],
        };
        const handle = {
            style: { touchAction: '' },
            classList: makeClassList(),
            setPointerCapture(id) { this.captured = id; },
            releasePointerCapture(id) { this.captured = null; this.released = id; },
        };
        row.handle = handle;
        row.closest = selector => (selector === '[data-tm-sortable-item]' ? row : null);
        handle.closest = selector => {
            if (selector === '[data-tm-sortable-handle]') return handle;
            if (selector === '[data-tm-sortable-item]') return row;
            return null;
        };
        rows.push(row);
    }

    root.querySelectorAll = selector => (selector === '[data-tm-sortable-item]' ? rows : rows.map(row => row.handle));
    return { root, rows };
}

function installDocument() {
    const doc = makeEventTarget();
    globalThis.document = doc;
    return doc;
}

function installFrames() {
    const frames = [];
    globalThis.requestAnimationFrame = fn => { frames.push(fn); return frames.length; };
    globalThis.cancelAnimationFrame = () => {};
    return frames;
}

const press = (target, { x = 10, y = 10, pointerType = 'mouse', id = 1 } = {}) => ({
    target,
    pointerId: id,
    pointerType,
    button: 0,
    buttons: 1,
    isPrimary: true,
    clientX: x,
    clientY: y,
    preventDefault() { this.defaultPrevented = true; },
    stopPropagation() { this.stopped = true; },
});

const move = ({ x = 10, y = 70, id = 1 } = {}) => ({ pointerId: id, clientX: x, clientY: y, buttons: 1 });
const up = ({ x = 10, y = 70, id = 1 } = {}) => ({ pointerId: id, clientX: x, clientY: y, buttons: 0 });

function install({ mode = 'handle', disabled = false } = {}) {
    const state = stage({ disabled });
    const doc = installDocument();
    const frames = installFrames();
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    attachSortable(state.root, dotnet, 'list-1', mode);
    return { ...state, doc, frames, calls, dotnet };
}

// ── the commit contract ─────────────────────────────────────────────────────────────────────

test('attachSortable: a drag past the next centre commits ONCE with the source indexes', () => {
    const { root, rows, calls } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 10 }));
    assert.equal(calls.length, 0, 'nothing is committed while the pointer is still moving');

    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    assert.deepEqual(calls, [['OnSortableReorderCommitted', 0, 1]]);
});

test('attachSortable: a drop on the original slot commits nothing', () => {
    const { root, rows, calls } = install();

    root.dispatch('pointerdown', press(rows[1].handle, { y: 50 }));
    root.dispatch('pointermove', move({ y: 55 }));
    root.dispatch('pointerup', up({ y: 55 }));

    assert.deepEqual(calls, []);
});

test('attachSortable: the rows shift out of the way while the drag is live', () => {
    const { root, rows } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));

    assert.match(rows[0].style.transform, /translateY\(60px\)/, 'the dragged row follows the pointer');
    assert.equal(rows[0].classList.contains('tm-sortable-list__item--dragging'), true);
    assert.equal(rows[1].style.transform, 'translateY(-40px)', 'the row it passed makes room above');
    assert.equal(rows[2].style.transform, '', 'the row it never reached does not move');
});

test('attachSortable: a second move resolves against the PRE-DRAG geometry, not the shifted rows', () => {
    const { root, rows, calls } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));      // past row 1's centre → it trades places
    assert.equal(rows[1].style.transform, 'translateY(-40px)', 'row 1 has moved up by one row');

    // Back near the top: the row must land at index 0 again. A re-MEASURED box would read row 1 at
    // y = 0 (its own shift) and count the pointer as past its centre, reporting a drop at index 1.
    root.dispatch('pointermove', move({ y: 30 }));
    root.dispatch('pointerup', up({ y: 30 }));

    assert.deepEqual(calls, [], 'the row came back to where it started, so nothing is committed');
});

test('attachSortable: the inline styles are restored after a commit', () => {
    const { root, rows } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    for (const row of rows) {
        assert.equal(row.style.transform, '', 'no leftover transform: the component re-renders the real order');
        assert.equal(row.classList.contains('tm-sortable-list__item--dragging'), false);
    }
});

// ── every cancel path rolls back and stays silent ───────────────────────────────────────────

test('attachSortable: Escape rolls the pending move back without committing', () => {
    const { root, rows, doc, calls } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() { this.stopped = true; } });

    assert.deepEqual(calls, []);
    assert.equal(rows[0].style.transform, '');
    assert.equal(rows[1].style.transform, '');
    assert.equal(rows[0].classList.contains('tm-sortable-list__item--dragging'), false);
});

test('attachSortable: pointercancel rolls back without committing', () => {
    const { root, rows, calls } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointercancel', { pointerId: 1 });

    assert.deepEqual(calls, []);
    assert.equal(rows[0].style.transform, '');
});

// ── what may start a drag ──────────────────────────────────────────────────────────────────

test('attachSortable: a press on the row body is vetoed in handle mode', () => {
    const { root, rows, calls } = install();

    // The row is not the grip in handle mode, so pointer-drag never matches a target at all.
    root.dispatch('pointerdown', press(rows[0]));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    assert.deepEqual(calls, []);
    assert.equal(rows[0].style.transform, '');
});

test('attachSortable: a disabled list starts no drag', () => {
    const { root, rows, calls } = install({ disabled: true });

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    assert.deepEqual(calls, []);
    assert.equal(rows[0].style.transform, '');
});

test('attachSortable: whole-item mode drags from the row itself', () => {
    const { root, rows, calls } = install({ mode: 'whole-item' });

    root.dispatch('pointerdown', press(rows[0]));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    assert.deepEqual(calls, [['OnSortableReorderCommitted', 0, 1]]);
});

test('attachSortable: a touch press in whole-item mode is held, so a swipe still scrolls', () => {
    const { root, rows, calls } = install({ mode: 'whole-item' });

    root.dispatch('pointerdown', press(rows[0], { pointerType: 'touch' }));
    // Moving before the 400 ms hold completes is a scroll, not a drag: pointer-drag aborts it.
    root.dispatch('pointermove', move({ y: 90 }));
    root.dispatch('pointerup', up({ y: 90 }));

    assert.deepEqual(calls, []);
    assert.equal(rows[0].style.transform, '');
});

// ── auto-scroll and lifecycle ──────────────────────────────────────────────────────────────

test('attachSortable: the edge band scrolls the list and re-hit-tests the landing slot', () => {
    const { root, rows, frames, calls } = install();

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 390 }));

    // The scroller schedules one frame at a time; run the pending frames as the browser would.
    for (let index = 0; index < 3 && frames.length > 0; index++) {
        const pending = frames.splice(0, frames.length);
        for (const frame of pending) frame();
    }

    assert.ok(root.scrollTop > 0, 'a pointer in the bottom edge band scrolls the list');
    assert.deepEqual(calls, [], 'scrolling alone never commits');
    assert.equal(rows[0].classList.contains('tm-sortable-list__item--dragging'), true,
        'the row stays lifted while the list scrolls under it');
});

test('attachSortable: the grabbed-row key guard is wired to the root and detached with it', () => {
    const { root, rows } = install();
    const row = rows[0];
    row.attributes.set('data-grabbed', 'true');

    const arrow = keyEvent('ArrowDown', row);
    root.dispatch('keydown', arrow);
    assert.equal(arrow.defaultPrevented, true, 'the page must not scroll under a grabbed row');

    const tab = keyEvent('Tab', row);
    root.dispatch('keydown', tab);
    assert.notEqual(tab.defaultPrevented, true, 'Tab keeps its default action');

    detachSortable('list-1');
    assert.equal(root.count('keydown'), 0, 'the guard listener is removed with the registration');
});

test('detachSortable: removes every listener and is safe to call twice', () => {
    const { root, rows, calls } = install();

    detachSortable('list-1');
    detachSortable('list-1');
    detachSortable('unknown-id');

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    assert.deepEqual(calls, []);
    assert.equal(root.count('pointerdown'), 0, 'the delegated pointerdown listener is gone');
});

test('attachSortable: re-attaching the same id replaces the previous session', () => {
    const { root, rows, calls } = install();

    // A second attach with the same id (the component changed its handle mode) must not stack listeners.
    attachSortable(root, { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } }, 'list-1', 'handle');

    root.dispatch('pointerdown', press(rows[0].handle));
    root.dispatch('pointermove', move({ y: 70 }));
    root.dispatch('pointerup', up({ y: 70 }));

    assert.equal(calls.length, 1, 'exactly one registration commits, never two');
});
