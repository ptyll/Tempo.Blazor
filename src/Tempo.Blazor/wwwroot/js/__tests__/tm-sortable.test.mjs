// TmSortableList module tests with hand-rolled DOM stubs (no jsdom): the handle-mode options, the pure
// landing-slot resolver, the ONE commit reported on release, the sibling shift while a row is dragged, the
// verbatim rollback on Escape / pointercancel, the disabled list, the handle-mode veto for a press on the
// row body, the touch long-press gate of whole-item mode, the edge auto-scroll wiring and detach safety.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attachSortable,
    createDragOverThrottle,
    detachSortable,
    optionsFor,
    resolveContainerIndex,
    resolveInsertIndex,
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

// ── the cross-list decision: an insertion slot and the list the pointer is over ──────────────

test('resolveInsertIndex: the slot is the first row whose centre the pointer has not passed', () => {
    assert.equal(resolveInsertIndex(boxesOf(0, 40, 80), -10), 0, 'above the first centre inserts at the top');
    assert.equal(resolveInsertIndex(boxesOf(0, 40, 80), 10), 0, 'still above the first centre');
    assert.equal(resolveInsertIndex(boxesOf(0, 40, 80), 30), 1, 'past the first centre, before the second');
    assert.equal(resolveInsertIndex(boxesOf(0, 40, 80), 70), 2);
    assert.equal(resolveInsertIndex(boxesOf(0, 40, 80), 5000), 3, 'below every row appends');
    assert.equal(resolveInsertIndex([], 10), 0, 'an empty target accepts the row at position 0');
    assert.equal(resolveInsertIndex(null, 10), 0);
});

test('resolveContainerIndex: the first list whose rect holds the pointer wins, else -1', () => {
    const rects = [
        { left: 0, top: 0, right: 100, bottom: 400 },
        { left: 120, top: 0, right: 220, bottom: 400 },
    ];

    assert.equal(resolveContainerIndex(rects, 10, 10), 0);
    assert.equal(resolveContainerIndex(rects, 150, 300), 1);
    assert.equal(resolveContainerIndex(rects, 110, 10), -1, 'the gap between two columns invents no target');
    assert.equal(resolveContainerIndex(rects, 10, 900), -1);
    assert.equal(resolveContainerIndex([], 10, 10), -1);
    assert.equal(resolveContainerIndex(null, 10, 10), -1);
});

// ── the OPT-IN drag-over report: a throttled, de-duplicated "which list would it land in" ────

/**
 * A deterministic clock and timer queue: `+now()+setTimeout+clearTimeout` are the three seams the
 * throttle takes, so the whole leading/trailing decision is tested without a real timer.
 */
function fakeClock(start = 0) {
    let time = start;
    let sequence = 0;
    const timers = [];
    return {
        now: () => time,
        setTimeout: (fn, ms) => {
            const id = ++sequence;
            timers.push({ id, at: time + ms, fn });
            return id;
        },
        clearTimeout: (id) => {
            const index = timers.findIndex(timer => timer.id === id);
            if (index >= 0) timers.splice(index, 1);
        },
        advance(ms) {
            time += ms;
            for (const timer of [...timers]) {
                if (timer.at > time) continue;
                timers.splice(timers.indexOf(timer), 1);
                timer.fn();
            }
        },
        pending: () => timers.length,
    };
}

function throttleOf(throttleMs = 100) {
    const clock = fakeClock();
    const reported = [];
    const throttle = createDragOverThrottle((value) => reported.push(value), {
        throttleMs,
        now: clock.now,
        setTimeout: clock.setTimeout,
        clearTimeout: clock.clearTimeout,
    });
    return { clock, reported, throttle };
}

test('createDragOverThrottle: the FIRST hovered list is reported at once', () => {
    const { reported, throttle } = throttleOf();

    throttle.notify('backlog');

    assert.deepEqual(reported, ['backlog'], 'a drag must project into the target without waiting');
});

test('createDragOverThrottle: the same list is never reported twice', () => {
    const { reported, throttle } = throttleOf();

    throttle.notify('backlog');
    throttle.notify('backlog');
    throttle.notify('backlog');

    assert.deepEqual(reported, ['backlog'], 'the module notifies on every pointer frame');
});

test('createDragOverThrottle: a change inside the window is reported ONCE when the window closes', () => {
    const { clock, reported, throttle } = throttleOf();

    throttle.notify('backlog');
    clock.advance(10);
    throttle.notify('planner');
    throttle.notify('writer');

    assert.deepEqual(reported, ['backlog'], 'inside the throttle window nothing else reaches .NET');
    assert.equal(clock.pending(), 1, 'exactly one trailing report is scheduled');

    clock.advance(90);

    assert.deepEqual(reported, ['backlog', 'writer'], 'the LATEST hovered list wins the trailing report');
    assert.equal(clock.pending(), 0);
});

test('createDragOverThrottle: a change back to the reported list schedules nothing to say', () => {
    const { clock, reported, throttle } = throttleOf();

    throttle.notify('backlog');
    clock.advance(10);
    throttle.notify('planner');
    throttle.notify('backlog');

    clock.advance(100);

    assert.deepEqual(reported, ['backlog'], 'the net state never changed, so the drag-over stays silent');
    assert.equal(clock.pending(), 0);
});

test('createDragOverThrottle: cancel drops the pending report and reports the resting state', () => {
    const { clock, reported, throttle } = throttleOf();

    throttle.notify('backlog');
    clock.advance(10);
    throttle.notify('planner');

    throttle.cancel();

    assert.deepEqual(reported, ['backlog', null],
        'a cancelled drag tells .NET there is no target, so the projection returns to rest');
    assert.equal(clock.pending(), 0, 'the scheduled report is dropped, never delivered after the cancel');

    throttle.cancel();
    assert.deepEqual(reported, ['backlog', null], 'a second cancel reports nothing');
});

test('createDragOverThrottle: a notify after a cancel reports at once again', () => {
    const { clock, reported, throttle } = throttleOf();

    throttle.notify('backlog');
    clock.advance(10);
    throttle.notify('planner');
    throttle.cancel();
    reported.length = 0;

    clock.advance(5);
    throttle.notify('writer');

    assert.deepEqual(reported, ['writer'], 'a new gesture is not held back by the previous window');
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

/** A list root with `rowCount` 40 px rows starting at y = 0, 40, 80 (the default three). */
function stage({ disabled = false, rowCount = 3 } = {}) {
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

    for (let index = 0; index < rowCount; index++) {
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

// ── cross-list transfers ───────────────────────────────────────────────────────────────────

/** A second list stage whose rows sit beside the first one, so the pointer can be over either. */
function stageAt({ left, top = 0, id, rows = 3 }) {
    const state = stage({ rowCount: rows });
    state.root.getBoundingClientRect = () => ({
        top, left, right: left + 300, bottom: top + 400, width: 300, height: 400,
    });
    for (let index = 0; index < state.rows.length; index++) {
        const rowTop = top + index * 40;
        state.rows[index].getBoundingClientRect = () => ({
            top: rowTop, left, right: left + 300, bottom: rowTop + 40, width: 300, height: 40,
        });
    }
    state.root.getAttribute = name => (name === 'data-sortable-list-id' ? id : null);
    return state;
}

function installGroup() {
    const first = stageAt({ left: 0, id: 'backlog' });
    const second = stageAt({ left: 400, id: 'agent-1' });
    installDocument();
    installFrames();
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    attachSortable(first.root, dotnet, 'list-a', 'handle', { group: 'board', listId: 'backlog' });
    attachSortable(second.root, dotnet, 'list-b', 'handle', { group: 'board', listId: 'agent-1' });
    return { first, second, calls };
}

test('attachSortable: a row dropped over another list of the group is reported as a TRANSFER', () => {
    const { first, second, calls } = installGroup();

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 420, y: 120 }));
    assert.equal(first.root.classList.contains('tm-sortable-list--drop-target'), false,
        'the SOURCE list is never its own drop target');
    assert.equal(second.root.classList.contains('tm-sortable-list--drop-target'), true,
        'the list the row would land in is marked');

    first.root.dispatch('pointerup', up({ x: 420, y: 120 }));

    assert.deepEqual(calls, [['OnSortableTransferCommitted', 'agent-1', 0, 3]],
        'the commit names the TARGET list and the insertion slot over its rows');
    assert.equal(second.root.classList.contains('tm-sortable-list--drop-target'), false,
        'the mark is gone once the gesture ends');
});

test('attachSortable: dropping above the first row of the target inserts at its position 0', () => {
    const { first, calls } = installGroup();

    first.root.dispatch('pointerdown', press(first.rows[1].handle, { y: 50 }));
    first.root.dispatch('pointermove', move({ x: 420, y: 5 }));
    first.root.dispatch('pointerup', up({ x: 420, y: 5 }));

    assert.deepEqual(calls, [['OnSortableTransferCommitted', 'agent-1', 1, 0]]);
});

test('attachSortable: an EMPTY list of the group accepts the row at position 0', () => {
    // A board column usually starts empty: the target has no rows to insert between, so the only honest slot is
    // 0 — never an index borrowed from the SOURCE list's boxes.
    const source = stageAt({ left: 0, id: 'backlog' });
    const empty = stageAt({ left: 400, id: 'agent-empty', rows: 0 });
    installDocument();
    installFrames();
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    attachSortable(source.root, dotnet, 'list-a', 'handle', { group: 'board', listId: 'backlog' });
    attachSortable(empty.root, dotnet, 'list-b', 'handle', { group: 'board', listId: 'agent-empty' });

    source.root.dispatch('pointerdown', press(source.rows[1].handle, { y: 50 }));
    source.root.dispatch('pointermove', move({ x: 420, y: 50 }));
    assert.equal(empty.root.classList.contains('tm-sortable-list--drop-target'), true,
        'an empty registered list is still a drop target');

    source.root.dispatch('pointerup', up({ x: 420, y: 50 }));

    assert.deepEqual(calls, [['OnSortableTransferCommitted', 'agent-empty', 1, 0]],
        'the row lands at the empty list\'s only slot, not at the source row index');
    assert.equal(empty.root.classList.contains('tm-sortable-list--drop-target'), false);
});

test('attachSortable: the rows of BOTH lists move while the row is over another list', () => {
    const { first, second } = installGroup();

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 420, y: 50 }));

    assert.match(first.rows[0].style.transform, /translate\(410px, 40px\)/,
        'across lists the row follows the pointer in both axes');
    assert.equal(first.rows[1].style.transform, 'translateY(-40px)', 'the source closes the hole');
    assert.equal(second.rows[0].style.transform, '', 'the slot opens below the first target row');
    assert.equal(second.rows[1].style.transform, 'translateY(40px)');
    assert.equal(second.rows[2].style.transform, 'translateY(40px)');
});

test('attachSortable: a cancel restores both lists and reports nothing', () => {
    const { first, second, calls } = installGroup();

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 420, y: 120 }));
    first.root.dispatch('pointercancel', { pointerId: 1 });

    assert.deepEqual(calls, []);
    for (const row of [...first.rows, ...second.rows]) {
        assert.equal(row.style.transform, '');
        assert.equal(row.classList.contains('tm-sortable-list__item--dragging'), false);
    }
    assert.equal(second.root.classList.contains('tm-sortable-list--drop-target'), false);
});

test('attachSortable: a standalone list never transfers — a drop outside it stays a local reorder', () => {
    const first = stageAt({ left: 0, id: 'solo' });
    const second = stageAt({ left: 400, id: 'other' });
    installDocument();
    installFrames();
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    attachSortable(first.root, dotnet, 'solo-a', 'handle', { listId: 'solo' });
    attachSortable(second.root, dotnet, 'solo-b', 'handle', { listId: 'other' });

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 420, y: 120 }));
    first.root.dispatch('pointerup', up({ x: 420, y: 120 }));

    assert.deepEqual(calls, [['OnSortableReorderCommitted', 0, 2]],
        'without a shared group the pointer can only reorder the list it started in');
    assert.equal(second.root.classList.contains('tm-sortable-list--drop-target'), false,
        'a list outside the group is never marked');
});

// ── the opt-in drag-over callback of a real group ─────────────────────────────────────────

/** The same group as {@link installGroup}, with the drag-over report opted in (throttle off for determinism). */
function installGroupTracking() {
    const first = stageAt({ left: 0, id: 'backlog' });
    const second = stageAt({ left: 400, id: 'agent-1' });
    installDocument();
    installFrames();
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    const options = { group: 'board', dragOver: true, dragOverThrottleMs: 0 };
    attachSortable(first.root, dotnet, 'list-a', 'handle', { ...options, listId: 'backlog' });
    attachSortable(second.root, dotnet, 'list-b', 'handle', { ...options, listId: 'agent-1' });
    return { first, second, calls };
}

const dragOverCalls = (calls) => calls.filter(call => call[0] === 'OnSortableDragOver');

test('attachSortable: a list that does not opt in never reports a drag-over', () => {
    const { first, calls } = installGroup();

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 420, y: 120 }));
    first.root.dispatch('pointerup', up({ x: 420, y: 120 }));

    assert.deepEqual(dragOverCalls(calls), [],
        'the drag-over report is opt-in: no other consumer of this module changes behaviour');
    assert.deepEqual(calls, [['OnSortableTransferCommitted', 'agent-1', 0, 3]], 'the commit is unchanged');
});

test('attachSortable: with dragOver the hovered list is reported, and the resting state on release', () => {
    const { first, calls } = installGroupTracking();

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 12, y: 20 }));
    assert.deepEqual(dragOverCalls(calls), [['OnSortableDragOver', 'backlog', 'backlog', 0]],
        'the source list itself is reported, so the host learns WHICH row is being dragged');

    first.root.dispatch('pointermove', move({ x: 420, y: 120 }));
    assert.deepEqual(dragOverCalls(calls).at(-1), ['OnSortableDragOver', 'agent-1', 'backlog', 0],
        'the list the row would land in is named with the source it came from');

    first.root.dispatch('pointerup', up({ x: 420, y: 120 }));
    assert.deepEqual(dragOverCalls(calls).at(-1), ['OnSortableDragOver', null, 'backlog', 0],
        'the release returns every column to its resting projection');
});

test('attachSortable: a cancelled drag-over reports the resting state and transfers nothing', () => {
    const { first, calls } = installGroupTracking();

    first.root.dispatch('pointerdown', press(first.rows[0].handle));
    first.root.dispatch('pointermove', move({ x: 420, y: 120 }));
    first.root.dispatch('pointercancel', { pointerId: 1 });

    assert.deepEqual(dragOverCalls(calls).at(-1), ['OnSortableDragOver', null, 'backlog', 0],
        'Escape / pointercancel return the projection to rest');
    assert.deepEqual(calls.filter(call => call[0] === 'OnSortableTransferCommitted'), []);
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
