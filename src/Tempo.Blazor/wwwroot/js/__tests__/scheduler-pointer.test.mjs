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
        getAttribute(name) { return node.attrs.has(name) ? node.attrs.get(name) : null; },
        removeAttribute(name) { node.attrs.delete(name); },
        getBoundingClientRect: () => node.rect,
        setPointerCapture(id) { node.captured.add(id); },
        releasePointerCapture(id) { node.captured.delete(id); node.released.push(id); },
        appendChild(child) { node.children.push(child); child.parent = node; return child; },
        remove() { if (node.parent) node.parent.children = node.parent.children.filter(c => c !== node); },
        children: [],
        querySelector(selector) {
            const wanted = selector.startsWith('.') ? selector.slice(1) : null;
            const walk = list => {
                for (const child of list) {
                    if (wanted && child.classList.contains(wanted)) return child;
                    const found = walk(child.children ?? []);
                    if (found) return found;
                }
                return null;
            };
            return walk(node.children);
        },
        closest(selector) {
            const matches = (n, raw) => {
                const compound = raw.trim();
                const parts = compound.split(/(?=[.\[])/).filter(Boolean);
                return parts.every(part => {
                    if (part.startsWith('.')) return n.classList.contains(part.slice(1));
                    if (part.startsWith('[')) {
                        const name = part.slice(1, part.indexOf('='));
                        const key = name.replace(/^data-/, '').replace(/-([a-z])/g, (_, c) => c.toUpperCase());
                        return n.attrs.has(name) || (n.dataset && n.dataset[key] !== undefined);
                    }
                    return false;
                });
            };
            for (let n = node; n; n = n.parent) {
                if (selector.split(',').some(s => matches(n, s))) return n;
            }
            return null;
        },
        // closest() starts at the node itself, so a handle must answer for its own selector.
        matches(selector) {
            return node.closest(selector) === node;
        },
    };
    // Pointer events bubble: a captured pointer's events fire on the handle and reach the document.
    const own = node.dispatch;
    node.dispatch = (type, event = {}) => {
        own(type, event);
        (node.parent ?? doc)?.dispatch(type, event);
        return event;
    };
    if (parent) parent.children.push(node);
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
    doc = {
        ...bag(), readyState: 'complete',
        currentScript: { src: helperUrl.replace('pointer-drag.js', 'scheduler.js') },
        contains: () => true,
        createElement() {
            const node = el();
            // className is how the live time label is marked; querySelector matches classList.
            Object.defineProperty(node, 'className', {
                set(value) { String(value).split(/\s+/).filter(Boolean).forEach(c => node.classList.add(c)); },
                get() { return ''; },
            });
            return node;
        },
        elementFromPoint: () => null,
    };
    globalThis.document = doc;
    globalThis.window = { TempoBlazor: undefined };
    globalThis.matchMedia = undefined;
    // pointer-gesture.js schedules its auto-scroller on a frame. The stub never paints, so the
    // callback is recorded and never run.
    globalThis.requestAnimationFrame = () => 1;
    globalThis.cancelAnimationFrame = () => {};
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
    for (const name of ['init', 'registerInstance', 'unregisterInstance', 'destroy', 'cancelResize', 'focusRovingStop', 'attachScrollGuard', 'detachScrollGuard', 'elementPoint', 'scrollToTime']) {
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
    const keydowns = listeners.filter(l => l.type === 'keydown');
    assert.equal(keydowns.length, 2, 'one guard and one position memory, and a second attach adds neither');
    assert.equal(listeners.filter(l => l.type === 'focusin').length, 1);
    assert.equal(keydowns[0].capture, true, 'capture on the root, because the cell stops the bubbling keydown');

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
    assert.equal(listeners.filter(l => l.type === 'keydown' || l.type === 'focusin').length, 0,
        'detach removes the listeners from the element they were added to');
});

test('no mouse listener is registered any more - the gesture is Pointer Events on the document', () => {
    assert.equal(doc.count('mousedown'), 0);
    // Two delegated listeners: the resize handles, and the touch move of the event body.
    assert.equal(doc.count('pointerdown'), 2);
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
    assert.equal(doc.count('pointerdown'), 2, 'resize and move, still one of each');
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
    assert.equal(doc.count('pointerdown'), 2);
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

test('a guarded key puts the page position back on the frame the browser scrolls', () => {
    const listeners = [];
    const root = {
        contains: () => true,
        addEventListener: (type, handler) => listeners.push({ type, handler }),
        removeEventListener() {},
    };
    const stop = { getAttribute: () => 'gridcell', closest: () => stop };
    let y = 0;
    const scrolls = [];
    const frames = [];
    const windowListeners = [];
    globalThis.window.scrollX = 0;
    Object.defineProperty(globalThis.window, 'scrollY', { configurable: true, get: () => y });
    globalThis.window.scrollTo = options => { scrolls.push(options); y = options.top; };
    globalThis.window.requestAnimationFrame = fn => frames.push(fn);
    globalThis.window.addEventListener = (type, fn) => windowListeners.push({ type, fn });
    globalThis.window.removeEventListener = (type, fn) => {
        const at = windowListeners.findIndex(l => l.type === type && l.fn === fn);
        if (at >= 0) windowListeners.splice(at, 1);
    };

    scheduler.attachScrollGuard(root);
    const fire = (type, target = stop) => listeners.filter(l => l.type === type).forEach(l => l.handler(
        type === 'keydown' ? { key: 'ArrowDown', target, preventDefault() {} } : { target }));
    fire('focusin');
    y = 40;
    windowListeners.filter(l => l.type === 'scroll').forEach(l => l.fn());
    assert.equal(y, 0, 'focusing a roving stop pins the page before any key');

    // The page has not moved yet: the smooth scroll-into-view starts after focusin.
    assert.deepEqual(scrolls[0], { left: 0, top: 0, behavior: 'auto' },
        'focus must re-apply the remembered position even though the page has not moved yet');

    // A scroll between gestures is the new position. The next key must keep it, and the focus
    // scroll that follows that key is pinned to it rather than to the earlier focus.
    y = 18;
    fire('keydown');
    fire('focusin');
    y = 40;
    windowListeners.filter(l => l.type === 'scroll').forEach(l => l.fn());
    assert.equal(y, 18, 'a scroll the browser starts during the correction is pinned back');
    for (const frame of frames) frame();
    assert.equal(y, 18, 'a key re-measures, and the focus scroll that follows is pinned to that');
    scheduler.detachScrollGuard(root);
    assert.equal(windowListeners.length, 0, 'detach removes the window scroll pin');
});

test('a focus that lands before any guarded key does not scroll the page', () => {
    const root = el(['tm-scheduler']);
    let y = 420;
    const scrolls = [];
    globalThis.window.scrollX = 0;
    Object.defineProperty(globalThis.window, 'scrollY', { configurable: true, get: () => y, set: value => { y = value; } });
    globalThis.window.scrollTo = (x, next) => { scrolls.push(next); y = next; };

    scheduler.attachScrollGuard(root);
    root.dispatch('focusin', {});
    assert.deepEqual(scrolls, [], 'an unset remembered position must not scroll the page to the top');
    assert.equal(y, 420);
    scheduler.detachScrollGuard(root);
});

test('focusCalendarDay waits out the opening key, clears it on blur, and never queries the document', async () => {
    const listeners = bag();
    const win = {
        ...bag(),
        TempoBlazor: undefined,
        setTimeout,
        clearTimeout,
    };
    win.addEventListener = listeners.addEventListener.bind(listeners);
    win.removeEventListener = listeners.removeEventListener.bind(listeners);
    globalThis.window = win;
    globalThis.document = {
        ...bag(),
        readyState: 'complete',
        currentScript: { src: helperUrl.replace('pointer-drag.js', 'scheduler.js') },
        contains: () => true,
        querySelector() { throw new Error('document query is not allowed'); },
    };
    await import(`../scheduler.js?focus-${Math.random()}`);
    const api = globalThis.window.TempoBlazor.Scheduler;

    const day = { isConnected: true, focused: 0, focus() { this.focused += 1; } };
    const calendar = { querySelector: selector => selector.includes('tm-cal-day') ? day : null };
    const root = { querySelector: selector => selector === '.tm-calendar-view' ? calendar : null };

    listeners.dispatch('keydown', { key: 'Enter' });
    api.focusCalendarDay(root);
    await new Promise(r => setTimeout(r, 20));
    assert.equal(day.focused, 0, 'Enter is still down, so the day must not take the opening key');

    listeners.dispatch('keyup', { key: 'Enter' });
    await new Promise(r => setTimeout(r, 20));
    assert.equal(day.focused, 1, 'the day is focused once the opening key is released');

    listeners.dispatch('keydown', { key: ' ' });
    listeners.dispatch('blur');
    api.focusCalendarDay(root);
    await new Promise(r => setTimeout(r, 20));
    assert.equal(day.focused, 2, 'losing the window clears a key whose keyup never arrives');

    api.focusCalendarDay(null);
    assert.equal(day.focused, 2, 'no root means no document-wide query, including :popover-open');
});

test('a clipped hour range sizes the minute from data-range-minutes, not a hard-coded day', () => {
    const g = timegrid({ slot: 30 });
    // 08:00-17:00 is 540 minutes. A 540px body is one minute per pixel, so +60px snaps to 60.
    g.grid.dataset.rangeMinutes = '540';
    g.body.rect = { left: 0, top: 0, width: 700, height: 540 };
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    g.bottom.dispatch('pointerdown', ev(g.bottom, { clientY: 160 }));
    g.bottom.dispatch('pointermove', ev(g.bottom, { clientY: 160 + 60 }));
    g.bottom.dispatch('pointerup', ev(g.bottom, { clientY: 160 + 60 }));

    assert.deepEqual(ref.calls, [['NotifyResizeComplete', 'e1', 60, 'bottom']],
        '540 visible minutes over 540px is one minute per pixel, not 1440/540');
});

test('scrollToTime scrolls the grid scroller by the visible range, under the sticky head', () => {
    const scroller = { scrollTop: 0, scrollHeight: 960 };
    const head = { offsetHeight: 48 };
    const root = {
        dataset: { rangeStart: '480', rangeMinutes: '540' },
        querySelector(selector) {
            if (selector === '[data-tm-scroll]') return scroller;
            if (selector.includes('tm-scheduler-timegrid-head')) return head;
            return null;
        },
    };

    scheduler.scrollToTime(root, 8 * 60 + 30);
    // 30 minutes into a 540 minute range, over a 912px track, is 50.666px.
    assert.ok(Math.abs(scroller.scrollTop - (912 * 30 / 540)) < 0.01, scroller.scrollTop);

    scheduler.scrollToTime(root, 7 * 60);
    assert.ok(Math.abs(scroller.scrollTop - (912 * 30 / 540)) < 0.01, 'a time before the range does not scroll');

    scheduler.scrollToTime(null, 600);
    assert.equal(scroller.scrollTop > 0, true);
});

// ── touch move (B13) ────────────────────────────────────────────────────────────────────────
// The event body moves only for a touch long-press. A mouse or a pen press on the body must not
// start a pointer gesture: that stays HTML5 drag-and-drop, so the desktop behaviour is unchanged.

/** Two day columns (1440px tall = one minute per pixel) and one draggable event in the first. */
function moveGrid({ eventId = 'e1', slot = 30, draggable = true } = {}) {
    const grid = el(['tm-scheduler-timegrid']);
    grid.dataset.slotDuration = String(slot);
    grid.dataset.rangeStart = '0';
    grid.dataset.rangeMinutes = '1440';
    const body = el(['tm-scheduler-timegrid-body'], grid, { left: 0, top: 0, width: 200, height: 1440 });
    const col1 = el(['tm-scheduler-timegrid-col'], body, { left: 0, top: 0, width: 100, height: 1440 });
    col1.dataset.date = '2026-10-13';
    const col2 = el(['tm-scheduler-timegrid-col'], body, { left: 100, top: 0, width: 100, height: 1440 });
    col2.dataset.date = '2026-10-14';
    const event = el(['tm-scheduler-timegrid-event'], col1, { left: 0, top: 600, width: 100, height: 60 });
    // A real rect already includes the live translate. The mock has to do the same, or a snap that
    // re-reads the origin after writing the transform looks stable when it is not.
    const base = event.rect;
    event.getBoundingClientRect = () => {
        const match = /translate\(([-\d.]+)px,\s*([-\d.]+)px\)/.exec(event.style.transform || '');
        const dx = match ? Number(match[1]) : 0;
        const dy = match ? Number(match[2]) : 0;
        return { left: base.left + dx, top: base.top + dy, width: base.width, height: base.height };
    };
    event.dataset.eventId = eventId;
    event.draggable = draggable;
    event.attrs.set('draggable', draggable ? 'true' : 'false');
    return { grid, body, col1, col2, event };
}

/** A month grid with two day cells and one draggable event in the first. */
function monthGrid({ eventId = 'm1' } = {}) {
    const root = el(['tm-scheduler-month']);
    const day1 = el(['tm-scheduler-month-day'], root, { left: 0, top: 0, width: 100, height: 80 });
    day1.dataset.date = '2026-10-13';
    const event = el(['tm-scheduler-month-event'], day1, { left: 4, top: 20, width: 90, height: 20 });
    event.dataset.eventId = eventId;
    event.attrs.set('draggable', 'true');
    const day2 = el(['tm-scheduler-month-day'], root, { left: 100, top: 0, width: 100, height: 80 });
    day2.dataset.date = '2026-10-20';
    return { root, day1, day2, event };
}

/** Two timeline rows (1440px wide = one minute per pixel) and one draggable event in the first. */
function moveTimeline({ eventId = 't1', slot = 30 } = {}) {
    const root = el(['tm-scheduler-timeline']);
    root.dataset.slotDuration = String(slot);
    root.dataset.rangeStart = '0';
    root.dataset.rangeMinutes = '1440';
    root.dataset.date = '2026-10-13';
    const row1 = el(['tm-scheduler-timeline-row'], root);
    row1.dataset.resource = 'room-a';
    const body1 = el(['tm-scheduler-timeline-row-body'], row1, { left: 0, top: 0, width: 1440, height: 40 });
    const event = el(['tm-scheduler-timeline-event'], body1, { left: 600, top: 0, width: 60, height: 30 });
    event.dataset.eventId = eventId;
    event.attrs.set('draggable', 'true');
    const row2 = el(['tm-scheduler-timeline-row'], root);
    row2.dataset.resource = 'room-b';
    const body2 = el(['tm-scheduler-timeline-row-body'], row2, { left: 0, top: 40, width: 1440, height: 40 });
    return { root, row1, row2, body1, body2, event };
}

/** Hold a touch for the long-press, then drag it. Returns the pointerup event. */
function longPressDrag(t, target, from, to) {
    // setTimeout is read when the hold is armed, so the mock has to be on before the pointerdown.
    // A second gesture in the same test finds it already enabled.
    try { t.mock.timers.enable({ apis: ['setTimeout'] }); } catch { /* already on */ }
    doc.dispatch('pointerdown', ev(target, { pointerType: 'touch', pointerId: 7, clientX: from.x, clientY: from.y }));
    t.mock.timers.tick(400);
    target.dispatch('pointermove', ev(target, { pointerType: 'touch', pointerId: 7, clientX: to.x, clientY: to.y }));
    return ev(target, { pointerType: 'touch', pointerId: 7, clientX: to.x, clientY: to.y });
}

test('a 400ms touch hold on the event body starts a move; a quick swipe sends nothing', async (t) => {
    const g = moveGrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);
    doc.elementFromPoint = () => g.col2;

    doc.dispatch('pointerdown', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 40, clientY: 630 }));
    doc.dispatch('pointermove', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 140, clientY: 690 }));
    doc.dispatch('pointerup', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 140, clientY: 690 }));
    assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), false, 'a swipe before the hold is a scroll');
    assert.equal(ref.calls.length, 0, 'a swipe notifies nothing');

    const up = longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 140, y: 690 });
    assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), true, 'the hold starts the move');
    assert.equal(g.event.draggable, false, 'HTML5 drag is off while the finger moves the event');
    g.event.dispatch('pointerup', up);

    assert.equal(ref.calls.filter(c => c[0] === 'NotifyMoveComplete').length, 1, 'the release notifies .NET exactly once');
});

test('a mouse or a pen press on the event body does not start a pointer move', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const g = moveGrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    for (const pointerType of ['mouse', 'pen']) {
        doc.dispatch('pointerdown', ev(g.event, { pointerType, clientX: 40, clientY: 630 }));
        t.mock.timers.tick(400);
        g.event.dispatch('pointermove', ev(g.event, { pointerType, clientX: 140, clientY: 690 }));
        g.event.dispatch('pointerup', ev(g.event, { pointerType, clientX: 140, clientY: 690 }));
        assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), false, pointerType);
    }
    assert.deepEqual(ref.calls, [], 'mouse and pen keep HTML5 drag-and-drop');
    assert.equal(g.event.draggable, true);
});

test('a touch on a resize handle stays a resize, and a read-only event does not move', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const g = timegrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    doc.dispatch('pointerdown', ev(g.bottom, { pointerType: 'touch', pointerId: 7, clientY: 160 }));
    t.mock.timers.tick(400);
    g.bottom.dispatch('pointermove', ev(g.bottom, { pointerType: 'touch', pointerId: 7, clientY: 220 }));
    assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), false, 'the handle is a resize, not a move');
    // 60px at one minute per pixel, from a 60px event: the edge moved, the body did not.
    assert.equal(g.event.style.height, '120px');
    g.bottom.dispatch('pointerup', ev(g.bottom, { pointerType: 'touch', pointerId: 7, clientY: 220 }));
    assert.deepEqual(ref.calls, [['NotifyResizeComplete', 'e1', 60, 'bottom']]);

    const locked = moveGrid({ eventId: 'locked', draggable: false });
    scheduler.registerInstance(locked.grid, ref);
    doc.dispatch('pointerdown', ev(locked.event, { pointerType: 'touch', pointerId: 8, clientX: 40, clientY: 630 }));
    t.mock.timers.tick(400);
    locked.event.dispatch('pointermove', ev(locked.event, { pointerType: 'touch', pointerId: 8, clientX: 140, clientY: 690 }));
    assert.equal(locked.event.classList.contains('tm-scheduler-event--moving'), false, 'draggable is not true');
    assert.equal(ref.calls.filter(c => c[0] === 'NotifyMoveComplete').length, 0);
});

test('the drop target resolves the day, the snapped minute and the resource from the element under the finger', async (t) => {
    const g = moveGrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);
    // The press is 30px into a 60px event whose top is minute 600. The finger ends at 690, so the
    // grabbed point (finger minus that 30px offset) is minute 660 — not the finger's own 690.
    doc.elementFromPoint = () => g.col2;

    g.event.dispatch('pointerup', longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 140, y: 690 }));
    assert.deepEqual(ref.calls, [['NotifyMoveComplete', 'e1', '2026-10-14', 660, null]]);

    const tl = moveTimeline({ slot: 30 });
    const tlRef = dotnet();
    scheduler.registerInstance(tl.root, tlRef);
    doc.elementFromPoint = () => tl.body2;
    tl.event.dispatch('pointerup', longPressDrag(t, tl.event, { x: 630, y: 10 }, { x: 750, y: 50 }));
    // The press is 30px into a 60px event whose left edge is minute 600, so the grabbed point at
    // finger 750 is minute 720, and the row under the finger carries the resource.
    assert.deepEqual(tlRef.calls, [['NotifyMoveComplete', 't1', '2026-10-13', 720, 'room-b']]);
    assert.equal(tl.body2.classList.contains('tm-scheduler-timeline-slot--drop-target'), false);

    const month = monthGrid();
    const monthRef = dotnet();
    scheduler.registerInstance(month.root, monthRef);
    doc.elementFromPoint = () => month.day2;
    month.event.dispatch('pointerup', longPressDrag(t, month.event, { x: 40, y: 30 }, { x: 140, y: 30 }));
    // A month has no minute axis: -1 tells .NET to keep the time of day.
    assert.deepEqual(monthRef.calls, [['NotifyMoveComplete', 'm1', '2026-10-20', -1, null]]);
    assert.equal(month.day2.classList.contains('tm-scheduler-month-day--drop-target'), false,
        'the drop target highlight is gone once the finger lifts');
});

test('two moves that snap to the same minute write the same transform', async (t) => {
    const g = moveGrid({ slot: 30 });
    scheduler.registerInstance(g.grid, dotnet());
    doc.elementFromPoint = () => g.col1;

    longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 40, y: 645 });
    const first = g.event.style.transform;
    g.event.dispatch('pointermove', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 40, clientY: 650 }));
    assert.equal(g.event.style.transform, first,
        'a second move that snaps to the same minute must not re-measure the translated rect');
});

test('a hold without a move shows the grabbed minute, not the offset as a coordinate', async (t) => {
    const g = moveGrid({ slot: 30 });
    scheduler.registerInstance(g.grid, dotnet());
    try { t.mock.timers.enable({ apis: ['setTimeout'] }); } catch { /* already on */ }
    doc.dispatch('pointerdown', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 40, clientY: 630 }));
    t.mock.timers.tick(400);
    assert.equal(g.event.querySelector('.tm-scheduler-event-drag-time')?.textContent, '10:00 – 11:00',
        'the press is 30px into a 10:00 event, so the label stays 10:00 until the finger moves');
});

test('a timeline label that starts under the resource column shifts into the row', async (t) => {
    const tl = moveTimeline({ slot: 30 });
    const resource = el(['tm-scheduler-timeline-resource-label--sticky'], tl.root, { left: 0, top: 0, width: 48, height: 40, right: 48, bottom: 40 });
    tl.root.querySelectorAll = () => [resource];
    scheduler.registerInstance(tl.root, dotnet());
    const created = [];
    const real = doc.createElement.bind(doc);
    doc.createElement = () => {
        const node = real();
        created.push(node);
        node.getBoundingClientRect = () => ({ left: 10, top: 0, width: 80, height: 20, right: 90, bottom: 20 });
        return node;
    };
    try { t.mock.timers.enable({ apis: ['setTimeout'] }); } catch { /* already on */ }
    doc.dispatch('pointerdown', ev(tl.event, { pointerType: 'touch', pointerId: 7, clientX: 630, clientY: 10 }));
    t.mock.timers.tick(400);
    assert.equal(created[0]?.style.marginLeft, '42px', 'the label moves right of the resource column');
});

test('the visual translate snaps to the same minute the release commits, honouring the grab offset', async (t) => {
    const g = moveGrid({ slot: 30 });
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);
    doc.elementFromPoint = () => g.col1;

    // Press 30px into the event (top 600). A finger at 645 would be 10:45 raw, but the grabbed
    // point is 615, which snaps to 10:30 — and the translate must show that same 30px, not the raw 45.
    const up = longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 40, y: 645 });
    assert.equal(g.event.style.transform, 'translate(0px, 30px)',
        'the preview snaps to the committed minute instead of following the finger raw');
    g.event.dispatch('pointerup', up);
    assert.deepEqual(ref.calls, [['NotifyMoveComplete', 'e1', '2026-10-13', 630, null]]);
});

test('a month day and a grid column under the finger get the drop-target class, removed on release', async (t) => {
    const month = monthGrid();
    scheduler.registerInstance(month.root, dotnet());
    doc.elementFromPoint = () => month.day2;
    const up = longPressDrag(t, month.event, { x: 40, y: 30 }, { x: 140, y: 30 });
    assert.equal(month.day2.classList.contains('tm-scheduler-month-day--drop-target'), true);
    assert.equal(month.day1.classList.contains('tm-scheduler-month-day--drop-target'), false);
    month.event.dispatch('pointerup', up);
    assert.equal(month.day2.classList.contains('tm-scheduler-month-day--drop-target'), false);

    const g = moveGrid({ slot: 30 });
    scheduler.registerInstance(g.grid, dotnet());
    doc.elementFromPoint = () => g.col2;
    const gridUp = longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 140, y: 690 });
    assert.equal(g.col2.classList.contains('tm-scheduler-timegrid-slot--drop-target'), true);
    g.event.dispatch('pointerup', gridUp);
    assert.equal(g.col2.classList.contains('tm-scheduler-timegrid-slot--drop-target'), false);
});

test('the live time label follows the pointer and a resize shows it too', async (t) => {
    const g = moveGrid({ slot: 30 });
    scheduler.registerInstance(g.grid, dotnet());
    doc.elementFromPoint = () => g.col2;

    longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 140, y: 690 });

    const label = g.event.querySelector('.tm-scheduler-event-drag-time');
    // The press is 30px into the event, so the grabbed point at finger 690 is 11:00, and the label
    // joins the two times with a spaced en dash.
    assert.equal(label?.textContent, '11:00 – 12:00', 'the one hour event lands at 11:00');
    assert.equal(g.event.style.transform.includes('translate'), true, 'the event follows the finger');
    g.event.dispatch('pointerup', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 140, clientY: 690 }));
    assert.equal(g.event.querySelector('.tm-scheduler-event-drag-time'), null, 'the label goes away with the gesture');

    const resized = timegrid({ slot: 30 });
    scheduler.registerInstance(resized.grid, dotnet());
    doc.dispatch('pointerdown', ev(resized.bottom, { clientY: 160 }));
    resized.bottom.dispatch('pointermove', ev(resized.bottom, { clientY: 220 }));
    assert.equal(resized.event.querySelector('.tm-scheduler-event-drag-time')?.textContent?.endsWith('–'), false);
    assert.match(resized.event.querySelector('.tm-scheduler-event-drag-time')?.textContent ?? '', /^\d{2}:\d{2} – \d{2}:\d{2}$/);
});

test('Escape and pointercancel roll the move back and notify nothing', async (t) => {
    const g = moveGrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);
    doc.elementFromPoint = () => g.col2;

    const start = longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 140, y: 690 });
    assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), true);
    doc.dispatch('keydown', { ...start, key: 'Escape' });
    assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), false, 'Escape restores the event');
    assert.equal(g.event.style.transform ?? '', '', 'the live translate is gone');
    assert.equal(g.event.draggable, true);
    assert.deepEqual(ref.calls, []);

    // A cancel instead of a release: pointerup would be the commit.
    longPressDrag(t, g.event, { x: 40, y: 630 }, { x: 140, y: 690 });
    doc.dispatch('pointercancel', ev(g.event, { pointerType: 'touch', pointerId: 7, clientX: 140, clientY: 690 }));
    assert.equal(g.event.classList.contains('tm-scheduler-event--moving'), false, 'pointercancel restores too');
    assert.deepEqual(ref.calls, [], 'a cancel commits nothing either');
});

test('Alt+Arrow reports a move step and Alt+Shift+Arrow a resize step, once each', () => {
    const g = moveGrid();
    const ref = dotnet();
    scheduler.registerInstance(g.grid, ref);

    const chord = (key, shiftKey) => ({
        key, altKey: true, shiftKey, ctrlKey: false, metaKey: false, target: g.event,
        preventDefault() { this.defaultPrevented = true; },
    });

    const plain = chord('ArrowDown', false);
    doc.dispatch('keydown', plain);
    assert.equal(plain.defaultPrevented, true, 'Alt+Arrow is the move alternative, so the browser must not navigate');
    assert.deepEqual(ref.calls, [['NotifyKeyboardStep', 'e1', 0, 1, false]]);

    const shifted = chord('ArrowDown', true);
    doc.dispatch('keydown', shifted);
    assert.equal(shifted.defaultPrevented, true, 'Alt+Shift+Arrow is the resize alternative');
    assert.deepEqual(ref.calls[1], ['NotifyKeyboardStep', 'e1', 0, 1, true]);

    const roving = chord('ArrowDown', false);
    roving.altKey = false;
    doc.dispatch('keydown', roving);
    assert.equal(ref.calls.length, 2, 'a plain arrow stays the roving key and is not prevented here');
    assert.equal(roving.defaultPrevented, undefined);
});

test('scrollToTime scrolls a timeline root horizontally, past its label column', () => {
    const label = { offsetWidth: 192 };
    const root = {
        dataset: { rangeStart: '0', rangeMinutes: '1440', tmScroll: 'timeline' },
        scrollLeft: 0,
        scrollWidth: 1392,
        matches(selector) { return selector === '[data-tm-scroll]'; },
        querySelector(selector) {
            if (selector === '.tm-scheduler-timeline-resource-header') return label;
            return null;
        },
    };

    scheduler.scrollToTime(root, 8 * 60);
    // 8 hours of 24, over the 1200px track that follows the 192px label.
    assert.ok(Math.abs(root.scrollLeft - (1200 * 8 / 24)) < 0.01, root.scrollLeft);
});