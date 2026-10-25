// scheduler.js resize on the shared pointer-drag helper (F7 pilot, B13). The real scheduler.js and the
// real pointer-drag.js run against a hand-rolled DOM: a mouse, a finger and a pen resize an event
// through Pointer Events, the registerInstance/unregisterInstance + NotifyResizeComplete contract is
// unchanged, and every cancel path restores the event without notifying .NET.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const helperUrl = pathToFileURL(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../pointer-drag.js')).href;

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

function el(classes = [], parent = null, rect = { left: 0, top: 0, width: 0, height: 0 }) {
    const set = new Set(classes);
    const node = {
        ...bag(),
        parent, dataset: {}, style: {}, attrs: new Map(), draggable: true, rect,
        captured: new Set(), released: [],
        classList: {
            add: n => set.add(n), remove: n => set.delete(n), contains: n => set.has(n),
        },
        setAttribute(name, value) { node.attrs.set(name, String(value)); },
        removeAttribute(name) { node.attrs.delete(name); },
        getBoundingClientRect: () => node.rect,
        setPointerCapture(id) { node.captured.add(id); },
        releasePointerCapture(id) { node.captured.delete(id); node.released.push(id); },
        closest(selector) {
            for (let n = node; n; n = n.parent) {
                const wanted = selector.split(',').map(s => s.trim());
                if (wanted.some(s => s.startsWith('.')
                    ? n.classList.contains(s.slice(1))
                    : s.startsWith('[') && n.attrs.has(s.slice(1, s.indexOf('='))))) return n;
            }
            return null;
        },
    };
    // Pointer events bubble: a captured pointer's events fire on the handle and reach the document.
    const own = node.dispatch;
    node.dispatch = (type, event = {}) => {
        own(type, event);
        (node.parent ?? doc)?.dispatch(type, event);
        return event;
    };
    return node;
}

/** A timegrid with one event (top 100, height 60 of a 1440px-tall body) and both resize handles. */
function timegrid({ eventId = 'e1', slot = 30 } = {}) {
    const grid = el(['tm-scheduler-timegrid']);
    grid.dataset.slotDuration = String(slot);
    const body = el(['tm-scheduler-timegrid-body'], grid, { left: 0, top: 0, width: 700, height: 1440 });
    const event = el(['tm-scheduler-timegrid-event'], body, { left: 10, top: 100, width: 100, height: 60 });
    event.dataset.eventId = eventId;
    const top = el(['tm-scheduler-event-resize-handle', 'tm-scheduler-event-resize-handle--top'], event);
    top.dataset.resizeEdge = 'top';
    const bottom = el(['tm-scheduler-event-resize-handle', 'tm-scheduler-event-resize-handle--bottom'], event);
    bottom.dataset.resizeEdge = 'bottom';
    return { grid, body, event, top, bottom };
}

/** A timeline row (1440px wide = one minute per pixel) with one event. */
function timeline({ eventId = 't1', slot = 15 } = {}) {
    const root = el(['tm-scheduler-timeline']);
    root.dataset.slotDuration = String(slot);
    const body = el(['tm-scheduler-timeline-row-body'], root, { left: 0, top: 0, width: 1440, height: 40 });
    const event = el(['tm-scheduler-timeline-event'], body, { left: 300, top: 0, width: 120, height: 30 });
    event.dataset.eventId = eventId;
    const left = el(['tm-scheduler-event-resize-handle'], event);
    left.dataset.resizeEdge = 'left';
    const right = el(['tm-scheduler-event-resize-handle'], event);
    right.dataset.resizeEdge = 'right';
    return { root, body, event, left, right };
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
}

let doc;
let scheduler;

async function boot() {
    doc = { ...bag(), readyState: 'complete', currentScript: { src: helperUrl.replace('pointer-drag.js', 'scheduler.js') }, contains: () => true };
    globalThis.document = doc;
    globalThis.window = { TempoBlazor: undefined };
    globalThis.matchMedia = undefined;
    // A fresh module instance per test: the IIFE attaches to the globals above.
    await import(`../scheduler.js?${Math.random()}`);
    scheduler = globalThis.window.TempoBlazor.Scheduler;
    await scheduler.ready;
}

test.beforeEach(boot);
test.afterEach(() => {
    scheduler?.destroy?.();
    delete globalThis.document;
    delete globalThis.window;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...over,
});

// ── contract kept ───────────────────────────────────────────────────────────────────────────

test('window.TempoBlazor.Scheduler keeps init, registerInstance, unregisterInstance, destroy', () => {
    for (const name of ['init', 'registerInstance', 'unregisterInstance', 'destroy', 'cancelResize', 'focusRovingStop', 'attachScrollGuard', 'detachScrollGuard', 'elementPoint']) {
        assert.equal(typeof scheduler[name], 'function', name);
    }
});

test('focusRovingStop moves the DOM focus onto the one roving stop', () => {
    const root = { querySelector: () => null };
    scheduler.focusRovingStop(null, "[tabindex='0']");
    scheduler.focusRovingStop(root, "[tabindex='0']");

    const focused = [];
    const stop = { focus: options => focused.push(options) };
    root.querySelector = selector => selector === "[role='gridcell'][tabindex='0']" ? stop : null;
    scheduler.focusRovingStop(root, "[role='gridcell'][tabindex='0']");
    assert.deepEqual(focused, [{ preventScroll: true }]);
});

test('the scroll guard prevents default only for an unmodified roving key on a roving stop', () => {
    const listeners = [];
    const root = {
        contains: target => target && target.inside !== false,
        addEventListener: (type, handler, capture) => listeners.push({ type, handler, capture }),
        removeEventListener: (type, handler, capture) => {
            const at = listeners.findIndex(l => l.type === type && l.handler === handler);
            if (at >= 0) listeners.splice(at, 1);
        },
    };
    const stop = role => ({
        getAttribute: name => name === 'role' ? role : null,
        closest: selector => selector === '[data-tm-roving]' ? stop(role) : null,
    });
    const press = (key, target, modifiers = {}) => {
        const event = { key, target, preventDefault() { event.prevented = true; }, ...modifiers };
        for (const listener of listeners) listener.handler(event);
        return event.prevented === true;
    };

    scheduler.attachScrollGuard(root);
    scheduler.attachScrollGuard(root);
    assert.equal(listeners.length, 1, 'a second attach on the same root adds nothing');
    assert.equal(listeners[0].capture, true, 'capture on the root, because the cell stops the bubbling keydown');

    assert.equal(press('ArrowDown', stop('gridcell')), true);
    assert.equal(press('PageDown', stop('gridcell')), true);
    assert.equal(press(' ', stop('button')), true);
    assert.equal(press(' ', stop('gridcell')), true);

    assert.equal(press('ArrowDown', stop('gridcell'), { ctrlKey: true }), false, 'a modified chord is the browser\'s');
    assert.equal(press(' ', stop('radio')), false, 'space on a radio keeps its native behaviour');
    assert.equal(press('ArrowDown', { closest: () => null }), false, 'a key outside a roving stop scrolls');
    assert.equal(press('ArrowDown', { ...stop('gridcell'), inside: false }), false, 'a key outside this scheduler scrolls');
    assert.equal(press('a', stop('gridcell')), false);

    scheduler.detachScrollGuard(root);
    assert.equal(listeners.length, 0, 'detach removes the listener from the element it was added to');
});

test('no mouse listener is registered any more - the gesture is Pointer Events on the document', () => {
    assert.equal(doc.count('mousedown'), 0);
    assert.equal(doc.count('pointerdown'), 1);
});

test('touch resize is gated behind a 400ms long-press; a quick swipe is not a resize', async (t) => {
    // F7 review J3: threshold 2 with no long-press turns a scroll swipe that lands on a handle
    // (Chromium snaps the finger onto it) into a committed resize. The helper applies longPressMs
    // to touch only, so mouse and pen keep the 2px threshold.
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { pointerType: 'touch', clientY: 160 }));
    doc.dispatch('pointermove', ev(g.bottom, { pointerType: 'touch', clientY: 160 + 40 }));
    assert.equal(g.event.style.height ?? '', '', 'a swipe that moves before the hold is a scroll, not a resize');
    assert.equal(ref.calls.length, 0);

    doc.dispatch('pointerdown', ev(g.bottom, { pointerType: 'touch', clientY: 160 }));
    t.mock.timers.tick(400);
    doc.dispatch('pointermove', ev(g.bottom, { pointerType: 'touch', clientY: 160 + 40 }));
    assert.equal(g.event.style.height, '100px', 'holding 400ms, then dragging, resizes');

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    doc.dispatch('pointermove', ev(g.bottom, { clientY: 160 + 40 }));
    assert.equal(g.event.style.height, '100px', 'a mouse keeps the immediate 2px threshold');
});

// ── timegrid resize ─────────────────────────────────────────────────────────────────────────

test('a mouse drag on the bottom handle resizes live and notifies .NET once with the slot-snapped delta', () => {
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    const down = ev(g.bottom, { clientY: 160 });
    doc.dispatch('pointerdown', down);
    assert.equal(down.defaultPrevented, true, 'no text selection while resizing');
    assert.equal(g.event.draggable, false, 'HTML5 drag is off while resizing');
    assert.equal(g.event.attrs.get('data-resizing'), 'true');
    assert.deepEqual([...g.bottom.captured], [1], 'capture on the handle, by pointer id');

    // 1440px body = 1440 minutes: 1px = 1 minute. +45px = 45 min -> snaps to 30 (round(1.5)=2 -> 60)
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 160 + 40 }));
    assert.equal(g.event.style.height, '100px', 'the live height follows the pointer');
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 160 + 60 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 160 + 60 }));

    assert.deepEqual(ref.calls, [['NotifyResizeComplete', 'e1', 60, 'bottom']]);
    assert.equal(g.event.draggable, true);
    assert.equal(g.event.attrs.has('data-resizing'), false);
    assert.equal(g.event.style.height, '', 'Blazor re-renders the final size');
    assert.deepEqual(g.bottom.released, [1]);
});

test('the top handle moves the start: negative delta, top and height follow', () => {
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.top, { clientY: 100 }));
    g.top.dispatch('pointermove', ev(g.top, { clientY: 70 }));
    assert.equal(g.event.style.top, '70px');
    assert.equal(g.event.style.height, '90px');
    g.top.dispatch('pointerup', ev(g.top, { clientY: 70 }));

    assert.deepEqual(ref.calls, [['NotifyResizeComplete', 'e1', -30, 'top']]);
    assert.equal(g.event.style.top, '');
    assert.equal(g.event.style.height, '');
});

test('a FINGER resizes the same way (the B13 bug: touch did nothing)', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { pointerType: 'touch', pointerId: 4, clientY: 160 }));
    t.mock.timers.tick(400); // touch resize waits out the long-press; a swipe before it scrolls
    g.bottom.dispatch('pointermove', ev(g.bottom, { pointerType: 'touch', pointerId: 4, clientY: 220 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { pointerType: 'touch', pointerId: 4, clientY: 220 }));

    assert.deepEqual(ref.calls, [['NotifyResizeComplete', 'e1', 60, 'bottom']]);
});

test('a pen resizes the same way', () => {
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { pointerType: 'pen', pointerId: 5, clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { pointerType: 'pen', pointerId: 5, clientY: 190 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { pointerType: 'pen', pointerId: 5, clientY: 190 }));

    assert.deepEqual(ref.calls, [['NotifyResizeComplete', 'e1', 30, 'bottom']]);
});

test('a press on a handle that never moves (a tap) notifies nothing and leaves no residue', () => {
    const g = timegrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 160 }));

    assert.equal(ref.calls.length, 0);
    assert.equal(g.event.draggable, true, 'draggable is restored on EVERY path, not just a completed drag');
    assert.equal(g.event.attrs.has('data-resizing'), false);
});

test('a movement smaller than one slot snaps to zero and notifies nothing', () => {
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 170 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 170 }));

    assert.equal(ref.calls.length, 0);
});

test('Escape restores the event and notifies nothing', () => {
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 260 }));
    assert.equal(g.event.style.height, '160px');
    doc.dispatch('keydown', { key: 'Escape' });
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 260 }));

    assert.equal(ref.calls.length, 0);
    assert.equal(g.event.style.height, '', 'the live size is rolled back');
    assert.equal(g.event.draggable, true);
    assert.equal(g.event.attrs.has('data-resizing'), false);
});

test('pointercancel (the browser takes over for a scroll or a call) restores the event', () => {
    const g = timegrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { pointerType: 'touch', clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { pointerType: 'touch', clientY: 260 }));
    g.bottom.dispatch('pointercancel', ev(g.bottom, { pointerType: 'touch' }));

    assert.equal(ref.calls.length, 0);
    assert.equal(g.event.style.height, '');
});

test('the minimum size guard still holds (10px)', () => {
    const g = timegrid({ slot: 30 });
    scheduler.registerInstance(g.grid, dotnet());

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 100 }));   // newHeight = 60 - 60 = 0
    assert.equal(g.event.style.height, undefined, 'a height below the guard is never painted');
});

test('a read-only host without a registered instance resizes visually and notifies nobody', () => {
    const g = timegrid({ slot: 30 });
    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 220 }));
    assert.doesNotThrow(() => g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 220 })));
});

// ── timeline resize ─────────────────────────────────────────────────────────────────────────

test('the timeline right handle widens the event; the left handle moves the start', () => {
    const t = timeline({ slot: 15 });
    const ref = dotnet();
    scheduler.registerInstance(t.root, ref);

    doc.dispatch('pointerdown', ev(t.right, { clientX: 420 }));
    t.right.dispatch('pointermove', ev(t.right, { clientX: 465 }));
    assert.equal(t.event.style.width, '165px');
    t.right.dispatch('pointerup', ev(t.right, { clientX: 465 }));

    doc.dispatch('pointerdown', ev(t.left, { clientX: 300, pointerId: 2 }));
    t.left.dispatch('pointermove', ev(t.left, { clientX: 270, pointerId: 2 }));
    assert.equal(t.event.style.left, '270px', 'left is relative to the row body');
    assert.equal(t.event.style.width, '150px');
    t.left.dispatch('pointerup', ev(t.left, { clientX: 270, pointerId: 2 }));

    assert.deepEqual(ref.calls, [
        ['NotifyResizeComplete', 't1', 45, 'right'],
        ['NotifyResizeComplete', 't1', -30, 'left'],
    ]);
});

// ── things that are not a resize ────────────────────────────────────────────────────────────

test('a press anywhere else on the page starts nothing and prevents nothing', () => {
    const g = timegrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    const down = ev(g.event, { clientY: 130 });
    doc.dispatch('pointerdown', down);

    assert.equal(down.defaultPrevented, undefined, 'the event body keeps HTML5 drag-and-drop for the mouse');
    assert.equal(g.event.draggable, true);
    assert.equal(g.event.captured.size, 0);
});

test('two schedulers on one page: each resize reports to its own instance', () => {
    const a = timegrid({ eventId: 'a' });
    const b = timegrid({ eventId: 'b' });
    const refA = dotnet();
    const refB = dotnet();
    scheduler.registerInstance(a.grid, refA);
    scheduler.registerInstance(b.grid, refB);

    doc.dispatch('pointerdown', ev(b.bottom, { clientY: 160 }));
    b.bottom.dispatch('pointermove', ev(b.bottom, { clientY: 190 }));
    b.bottom.dispatch('pointerup', ev(b.bottom, { clientY: 190 }));

    assert.equal(refA.calls.length, 0);
    assert.deepEqual(refB.calls, [['NotifyResizeComplete', 'b', 30, 'bottom']]);
});

test('unregisterInstance stops notifications for that scheduler', () => {
    const g = timegrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);
    scheduler.unregisterInstance(g.grid);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 220 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 220 }));

    assert.equal(ref.calls.length, 0);
});

// ── lifecycle ───────────────────────────────────────────────────────────────────────────────

test('init twice never stacks a second listener', async () => {
    scheduler.init();
    await scheduler.ready;
    scheduler.init();
    await scheduler.ready;
    assert.equal(doc.count('pointerdown'), 1);
});

test('destroy mid-resize restores the event, releases the capture and removes every listener', () => {
    const g = timegrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 260 }));
    scheduler.destroy();

    assert.equal(ref.calls.length, 0);
    assert.equal(g.event.style.height, '');
    assert.equal(g.event.draggable, true);
    assert.deepEqual(g.bottom.released, [1]);
    assert.equal(doc.total(), 0);
});

test('init after destroy works again', async () => {
    scheduler.destroy();
    scheduler.init();
    await scheduler.ready;
    assert.equal(doc.count('pointerdown'), 1);
});

// ── a cancelled resize puts back the INLINE geometry Blazor rendered (found by the real-browser E2E) ──

test('Escape restores the inline height/top Blazor rendered instead of clearing them (no collapse to min-height)', () => {
    const g = timegrid({ slot: 30 });
    g.event.style.height = '4.1667%';
    g.event.style.top = '58.3333%';
    scheduler.registerInstance(g.grid, dotnet());

    doc.dispatch('pointerdown', ev(g.top, { clientY: 100 }));
    g.top.dispatch('pointermove', ev(g.top, { clientY: 60 }));
    assert.notEqual(g.event.style.height, '4.1667%', 'precondition: the live resize changed it');
    doc.dispatch('keydown', { key: 'Escape' });

    assert.equal(g.event.style.height, '4.1667%');
    assert.equal(g.event.style.top, '58.3333%');
});

test('pointercancel and a tap restore the inline geometry too; a timeline restores left/width', () => {
    const g = timegrid();
    g.event.style.height = '4.1667%';
    scheduler.registerInstance(g.grid, dotnet());
    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 220 }));
    g.bottom.dispatch('pointercancel', ev(g.bottom));
    assert.equal(g.event.style.height, '4.1667%');

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160, pointerId: 2 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 160, pointerId: 2 }));
    assert.equal(g.event.style.height, '4.1667%', 'a tap leaves the geometry alone');

    const t = timeline();
    t.event.style.left = '20.8333%';
    t.event.style.width = '8.3333%';
    scheduler.registerInstance(t.root, dotnet());
    doc.dispatch('pointerdown', ev(t.left, { clientX: 300, pointerId: 3 }));
    t.left.dispatch('pointermove', ev(t.left, { clientX: 250, pointerId: 3 }));
    doc.dispatch('keydown', { key: 'Escape' });
    assert.equal(t.event.style.left, '20.8333%');
    assert.equal(t.event.style.width, '8.3333%');
});

test('a committed resize also restores the inline geometry first; Blazor then renders the new one', () => {
    const g = timegrid({ slot: 30 });
    g.event.style.height = '4.1667%';
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 220 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 220 }));

    assert.equal(ref.calls.length, 1);
    assert.equal(g.event.style.height, '4.1667%', 'never left at min-height while .NET catches up');
});
test('the old geometry is restored BEFORE .NET is told, so a synchronous Blazor render is never overwritten (E2E: the committed size did not show)', () => {
    const g = timegrid({ slot: 30 });
    g.event.style.height = '4.1667%';
    // Blazor WebAssembly renders inside invokeMethodAsync: simulate that render patching the DOM.
    const ref = { calls: [], invokeMethodAsync(...args) { this.calls.push(args); g.event.style.height = '8.3333%'; return Promise.resolve(); } };
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 220 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 220 }));

    assert.equal(ref.calls.length, 1);
    assert.equal(g.event.style.height, '8.3333%', 'what Blazor rendered during the call survives the cleanup');
    assert.equal(g.event.draggable, true);
    assert.equal(g.event.attrs.has('data-resizing'), false);
});

test('the same ordering holds for the timeline left handle', () => {
    const t = timeline({ slot: 15 });
    t.event.style.left = '20.8333%';
    t.event.style.width = '8.3333%';
    const ref = { calls: [], invokeMethodAsync(...args) { this.calls.push(args); t.event.style.left = '19%'; t.event.style.width = '10%'; return Promise.resolve(); } };
    scheduler.registerInstance(t.root, ref);

    doc.dispatch('pointerdown', ev(t.left, { clientX: 300 }));
    t.left.dispatch('pointermove', ev(t.left, { clientX: 270 }));
    t.left.dispatch('pointerup', ev(t.left, { clientX: 270 }));

    assert.equal(t.event.style.left, '19%');
    assert.equal(t.event.style.width, '10%');
});

test('focusRovingStop scrolls the grid so the slot clears the sticky head', () => {
    const grid = el(['tm-scheduler-timegrid']);
    const scroller = el(['tm-scheduler-timegrid-scroll'], grid, { left: 0, top: 100, width: 700, height: 400 });
    scroller.attrs.set('data-tm-scroll', 'timegrid');
    scroller.scrollTop = 0;
    const head = el(['tm-scheduler-timegrid-head'], scroller, { left: 0, top: 100, width: 700, height: 60 });
    const slot = el(['tm-scheduler-timegrid-slot'], scroller, { left: 0, top: 120, width: 100, height: 30 });
    slot.dataset.start = '2026-05-12T09:00';
    slot.focus = () => { slot.focused = true; };
    grid.querySelector = selector => selector.includes('09:00') ? slot : null;
    scroller.querySelector = selector => selector.includes('timegrid-head') ? head : null;

    scheduler.focusRovingStop(grid, '[data-start="2026-05-12T09:00"]');

    assert.equal(slot.focused, true);
    assert.equal(scroller.scrollTop, -40, 'the slot moves below the 60px sticky head');
});

test('slotPoint finds the slot by its start and resource', () => {
    const grid = el(['tm-scheduler-timegrid']);
    const slot = el(['tm-scheduler-timegrid-slot'], grid, { left: 140, top: 260, width: 100, height: 30 });
    slot.dataset.start = '2026-05-12T08:00';
    slot.dataset.resource = 'room-a';
    grid.querySelector = selector => selector.includes('2026-05-12T08:00') && selector.includes('room-a') ? slot : null;

    assert.deepEqual(scheduler.slotPoint(grid, '2026-05-12T08:00', 'room-a'), [140, 260]);
    assert.deepEqual(scheduler.slotPoint(grid, '2026-05-12T08:00', 'room-b'), [0, 0]);
});