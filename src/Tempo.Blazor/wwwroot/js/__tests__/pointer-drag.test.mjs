// pointer-drag.js tests with hand-rolled DOM stubs (no jsdom): threshold, pointer capture by
// pointerId, long-press for touch, every cancel path, touch-action guidance, delegation,
// reduced motion and multi-instance safety.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attach, attachKeyboardMove, prefersReducedMotion } from '../pointer-drag.js';

// ── fakes ───────────────────────────────────────────────────────────────────────────────────

function listenerBag() {
    const map = new Map();
    return {
        addEventListener(type, fn, options) {
            const capture = options === true || options?.capture === true;
            map.set(type, [...(map.get(type) ?? []), { fn, capture, passive: options?.passive }]);
        },
        removeEventListener(type, fn) {
            const list = map.get(type) ?? [];
            const index = list.findIndex(entry => entry.fn === fn);
            if (index >= 0) list.splice(index, 1);
            map.set(type, list);
        },
        dispatch(type, event = {}) {
            for (const entry of [...(map.get(type) ?? [])]) entry.fn(event);
            return event;
        },
        count(type) { return (map.get(type) ?? []).length; },
        total() { let n = 0; for (const list of map.values()) n += list.length; return n; },
        options(type) { return (map.get(type) ?? []).map(entry => entry.passive); },
    };
}

function fakeElement(selectors = []) {
    const classes = new Set();
    const el = {
        ...listenerBag(),
        style: {},
        selectors: new Set(selectors),
        children: [],
        captured: new Set(),
        released: [],
        classes,
        classList: {
            add: name => classes.add(name),
            remove: name => classes.delete(name),
            contains: name => classes.has(name),
        },
        setPointerCapture(id) { el.captured.add(id); },
        releasePointerCapture(id) { el.captured.delete(id); el.released.push(id); },
        contains(node) { return node === el || el.children.includes(node); },
        closest(selector) { return el.selectors.has(selector) ? el : (el.parent?.closest?.(selector) ?? null); },
    };
    return el;
}

function child(parent, selectors) {
    const node = fakeElement(selectors);
    node.parent = parent;
    parent.children.push(node);
    return node;
}

let doc;
test.beforeEach(() => {
    doc = listenerBag();
    globalThis.document = doc;
    delete globalThis.matchMedia;
});
test.afterEach(() => {
    delete globalThis.document;
    delete globalThis.matchMedia;
});

const ptr = (over = {}) => ({
    pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; },
    stopPropagation() { this.propagationStopped = true; },
    ...over,
});

function recorder(extra = {}) {
    const log = [];
    const push = name => info => { log.push([name, info]); };
    return {
        log,
        names: () => log.map(([name]) => name),
        last: name => [...log].reverse().find(([n]) => n === name)?.[1],
        options: {
            onPress: push('press'), onStart: push('start'), onMove: push('move'),
            onEnd: push('end'), onCancel: push('cancel'), onRelease: push('release'), ...extra,
        },
    };
}

// ── threshold ───────────────────────────────────────────────────────────────────────────────

test('a press that never leaves the threshold is a tap: no start, no end, one release', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 5, ...r.options });

    el.dispatch('pointerdown', ptr({ clientX: 10, clientY: 10 }));
    el.dispatch('pointermove', ptr({ clientX: 12, clientY: 13 }));   // distance sqrt(13) < 5
    el.dispatch('pointerup', ptr({ clientX: 12, clientY: 13 }));

    assert.deepEqual(r.names(), ['press', 'release']);
    assert.equal(r.last('release').outcome, 'tap');
    assert.equal(r.last('release').started, false);
});

test('the drag starts exactly at the threshold, reports the start origin and continues until release', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 5, ...r.options });

    el.dispatch('pointerdown', ptr({ clientX: 100, clientY: 50 }));
    el.dispatch('pointermove', ptr({ clientX: 103, clientY: 54 }));   // distance exactly 5
    assert.deepEqual(r.names(), ['press', 'start', 'move'], 'the crossing move is reported as the first move');
    assert.equal(r.last('start').startX, 100);
    assert.equal(r.last('start').origin, 'threshold');
    assert.equal(r.last('move').dx, 3);
    assert.equal(r.last('move').dy, 4);

    el.dispatch('pointermove', ptr({ clientX: 140, clientY: 50 }));
    el.dispatch('pointerup', ptr({ clientX: 140, clientY: 50 }));

    assert.deepEqual(r.names(), ['press', 'start', 'move', 'move', 'end', 'release']);
    assert.equal(r.last('end').dx, 40);
    assert.equal(r.last('release').outcome, 'end');
    assert.equal(r.last('release').started, true);
});

test('threshold 0 starts on the first move', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 1 }));

    assert.equal(r.names().includes('start'), true);
});

test('a throwing or invalid threshold falls back to the default', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 'banana', ...r.options });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 3 }));
    assert.equal(r.names().includes('start'), false, 'default threshold is 4px');
    el.dispatch('pointermove', ptr({ clientX: 4 }));
    assert.equal(r.names().includes('start'), true);
});

// ── pointer capture by pointerId ────────────────────────────────────────────────────────────

test('the pointer is captured on press by its id and released with the same id', () => {
    const el = fakeElement();
    attach(el, { threshold: 0 });

    const down = ptr({ pointerId: 7 });
    el.dispatch('pointerdown', down);
    assert.deepEqual([...el.captured], [7]);
    assert.equal(down.defaultPrevented, true, 'a mouse drag must not start a text selection');

    el.dispatch('pointermove', ptr({ pointerId: 7, clientX: 5 }));
    el.dispatch('pointerup', ptr({ pointerId: 7, clientX: 5 }));

    assert.deepEqual(el.released, [7], 'releasePointerCapture(pointerId) - the argument is required by the real DOM');
    assert.equal(el.captured.size, 0);
});

test('a new primary pointerdown cancels the stale session before starting its own', () => {
    // J7: a pointer that vanished without pointerup/pointercancel leaves a session nothing can end.
    // A new primary pointer must cancel it first (onCancel, never onEnd), then start cleanly.
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerId: 1 }));
    el.dispatch('pointermove', ptr({ pointerId: 1, clientX: 20 }));
    el.dispatch('pointerdown', ptr({ pointerId: 2 }));

    assert.equal(r.last('cancel').reason, 'superseded', 'the stale session is cancelled, not committed');
    assert.equal(r.names().filter(n => n === 'end').length, 0);
    assert.deepEqual([...el.captured], [2], 'the new pointer owns the gesture');
});

test('Escape that cancels a drag stops propagation so a dialog does not also close', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });
    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));

    const escape = { key: 'Escape', stopPropagation() { this.stopped = true; } };
    doc.dispatch('keydown', escape);

    assert.equal(r.last('cancel').reason, 'escape');
    assert.equal(escape.stopped, true, 'the surrounding dialog/sheet must not see this Escape');
});

test('secondary buttons and non-primary pointers do not start a gesture', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr({ button: 2 }));
    el.dispatch('pointerdown', ptr({ isPrimary: false }));

    assert.equal(r.log.length, 0);
    assert.equal(el.captured.size, 0);
});

test('pointerTypes restricts which pointers may start', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, pointerTypes: ['mouse', 'pen'], ...r.options });

    el.dispatch('pointerdown', ptr({ pointerType: 'touch' }));
    assert.equal(r.log.length, 0);
    el.dispatch('pointerdown', ptr({ pointerType: 'pen', pointerId: 3 }));
    assert.deepEqual(r.names(), ['press']);
});

test('captureOn start leaves a tap\'s click alone and captures only once the drag begins', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 4, captureOn: 'start', ...r.options });

    el.dispatch('pointerdown', ptr({ pointerId: 4 }));
    assert.equal(el.captured.size, 0, 'no capture while it is still a possible click on a child');
    el.dispatch('pointerup', ptr({ pointerId: 4 }));
    assert.equal(el.released.length, 0);

    el.dispatch('pointerdown', ptr({ pointerId: 5 }));
    el.dispatch('pointermove', ptr({ pointerId: 5, clientX: 10 }));
    assert.deepEqual([...el.captured], [5]);
    el.dispatch('pointerup', ptr({ pointerId: 5, clientX: 10 }));
    assert.deepEqual(el.released, [5]);
});

// ── click suppression ───────────────────────────────────────────────────────────────────────

test('the click that follows a real drag is swallowed once; a tap\'s click is not', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    attach(el, { threshold: 2 });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointerup', ptr());
    assert.equal(el.count('click'), 0, 'a tap arms no suppression');

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 20 }));
    el.dispatch('pointerup', ptr({ clientX: 20 }));
    assert.equal(el.count('click'), 1);
    const click = el.dispatch('click', { preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } });
    assert.equal(click.prevented, true);
    assert.equal(click.stopped, true);
    assert.equal(el.count('click'), 0, 'one-shot');
});

test('the click suppression expires when no click follows', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    attach(el, { threshold: 0 });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    el.dispatch('pointerup', ptr({ clientX: 9 }));
    assert.equal(el.count('click'), 1);
    t.mock.timers.tick(10);
    assert.equal(el.count('click'), 0, 'a later, unrelated click is never swallowed');
});

test('suppressClick false arms nothing', () => {
    const el = fakeElement();
    attach(el, { threshold: 0, suppressClick: false });
    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    el.dispatch('pointerup', ptr({ clientX: 9 }));
    assert.equal(el.count('click'), 0);
});

// ── long press ──────────────────────────────────────────────────────────────────────────────

test('touch long-press starts the drag after the delay, then follows the finger', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    const r = recorder();
    attach(el, { longPressMs: 400, ...r.options });

    const down = ptr({ pointerType: 'touch', clientX: 20, clientY: 20 });
    el.dispatch('pointerdown', down);
    assert.equal(down.defaultPrevented, undefined, 'a long-press candidate keeps native scroll and selection decisions');
    assert.equal(el.captured.size, 0, 'capturing on press suppresses the scroll the long-press is meant to allow');
    assert.deepEqual(r.names(), ['press']);

    t.mock.timers.tick(399);
    assert.equal(r.names().includes('start'), false);
    t.mock.timers.tick(1);
    assert.equal(r.last('start').origin, 'long-press');
    assert.equal(r.last('start').pointerType, 'touch');

    el.dispatch('pointermove', ptr({ pointerType: 'touch', clientX: 60, clientY: 20 }));
    assert.equal(r.last('move').dx, 40);
    el.dispatch('pointerup', ptr({ pointerType: 'touch', clientX: 60, clientY: 20 }));
    assert.equal(r.last('end').dx, 40);
    assert.equal(r.last('release').outcome, 'end');
});

test('touch movement beyond the tolerance before the delay is a scroll: the long-press is abandoned', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    const r = recorder();
    attach(el, { longPressMs: 400, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerType: 'touch' }));
    el.dispatch('pointermove', ptr({ pointerType: 'touch', clientY: 40 }));
    t.mock.timers.tick(1000);

    assert.equal(r.names().includes('start'), false, 'the timer must have been cleared');
    assert.equal(r.last('release').outcome, 'aborted');
});

test('small finger jitter inside the tolerance does not abandon the long-press', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    const r = recorder();
    attach(el, { longPressMs: 400, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerType: 'touch' }));
    el.dispatch('pointermove', ptr({ pointerType: 'touch', clientX: 3, clientY: 4 }));
    t.mock.timers.tick(400);

    assert.equal(r.names().includes('start'), true);
});

test('lifting the finger before the delay is a tap and clears the timer', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    const r = recorder();
    attach(el, { longPressMs: 400, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerType: 'touch' }));
    el.dispatch('pointerup', ptr({ pointerType: 'touch' }));
    t.mock.timers.tick(1000);

    assert.deepEqual(r.names(), ['press', 'release']);
    assert.equal(r.last('release').outcome, 'tap');
});

test('a mouse ignores the long-press and starts by threshold', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    const r = recorder();
    attach(el, { longPressMs: 400, threshold: 3, ...r.options });

    el.dispatch('pointerdown', ptr());
    t.mock.timers.tick(1000);
    assert.equal(r.names().includes('start'), false, 'a held mouse button does not start a drag by time');
    el.dispatch('pointermove', ptr({ clientX: 5 }));
    assert.equal(r.last('start').origin, 'threshold');
});

test('the contextmenu a touch long-press raises is suppressed while a gesture is active', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    attach(el, { longPressMs: 400 });

    const idle = el.dispatch('contextmenu', { preventDefault() { this.prevented = true; } });
    assert.equal(idle.prevented, undefined, 'outside a gesture the browser menu is untouched');

    el.dispatch('pointerdown', ptr({ pointerType: 'touch' }));
    const during = el.dispatch('contextmenu', { preventDefault() { this.prevented = true; } });
    assert.equal(during.prevented, true);
});

// ── cancel paths ────────────────────────────────────────────────────────────────────────────

test('Escape cancels an active drag: onCancel with the reason, no onEnd, capture released, listener gone', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerId: 9 }));
    el.dispatch('pointermove', ptr({ pointerId: 9, clientX: 30 }));
    assert.equal(doc.count('keydown') > 0, true, 'Escape is listened for only while a gesture is active');

    doc.dispatch('keydown', { key: 'Escape' });

    assert.equal(r.last('cancel').reason, 'escape');
    assert.equal(r.names().includes('end'), false);
    assert.deepEqual(el.released, [9]);
    assert.equal(doc.total(), 0, 'every document listener is removed');

    el.dispatch('pointerup', ptr({ pointerId: 9, clientX: 30 }));
    assert.equal(r.names().includes('end'), false, 'the pointerup after a cancel finishes nothing twice');
});

test('keys other than Escape do not cancel', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });
    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));

    doc.dispatch('keydown', { key: 'a' });

    assert.equal(r.names().includes('cancel'), false);
});

test('cancelOnEscape false never listens for the key', () => {
    const el = fakeElement();
    attach(el, { threshold: 0, cancelOnEscape: false });
    el.dispatch('pointerdown', ptr());
    assert.equal(doc.count('keydown'), 0);
});

test('pointercancel cancels an active drag with its reason', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    el.dispatch('pointercancel', ptr({ clientX: 9 }));

    assert.equal(r.last('cancel').reason, 'pointercancel');
    assert.equal(r.last('release').outcome, 'cancel');
});

test('pointercancel before the drag started aborts the press without onCancel', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 10, ...r.options });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointercancel', ptr());

    assert.deepEqual(r.names(), ['press', 'release']);
    assert.equal(r.last('release').outcome, 'aborted');
});

test('losing the pointer capture mid-drag cancels it; the capture lost after our own end does nothing', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerId: 3 }));
    el.dispatch('pointermove', ptr({ pointerId: 3, clientX: 9 }));
    el.dispatch('lostpointercapture', ptr({ pointerId: 3 }));
    assert.equal(r.last('cancel').reason, 'lostpointercapture');

    r.log.length = 0;
    el.dispatch('pointerdown', ptr({ pointerId: 4 }));
    el.dispatch('pointermove', ptr({ pointerId: 4, clientX: 9 }));
    el.dispatch('pointerup', ptr({ pointerId: 4, clientX: 9 }));
    el.dispatch('lostpointercapture', ptr({ pointerId: 4 }));   // the browser's release after pointerup
    assert.equal(r.names().includes('cancel'), false);
    assert.equal(r.names().filter(name => name === 'release').length, 1);
});

test('a mouse move with no button held means the pointerup was missed: the drag is cancelled', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    el.dispatch('pointermove', ptr({ clientX: 12, buttons: 0 }));

    assert.equal(r.last('cancel').reason, 'buttons-released');
});

test('handle.cancel() cancels programmatically and is a no-op when idle', () => {
    const el = fakeElement();
    const r = recorder();
    const handle = attach(el, { threshold: 0, ...r.options });

    assert.doesNotThrow(() => handle.cancel());
    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    assert.equal(handle.dragging, true);
    handle.cancel('route-change');

    assert.equal(r.last('cancel').reason, 'route-change');
    assert.equal(handle.dragging, false);
});

// ── vetoes ──────────────────────────────────────────────────────────────────────────────────

test('onPress returning false vetoes the gesture before any capture or preventDefault', () => {
    const el = fakeElement();
    const r = recorder({ onPress: () => false });
    attach(el, { threshold: 0, ...r.options });

    const down = ptr();
    el.dispatch('pointerdown', down);
    el.dispatch('pointermove', ptr({ clientX: 9 }));

    assert.equal(el.captured.size, 0);
    assert.equal(down.defaultPrevented, undefined);
    assert.equal(r.names().includes('start'), false);
    assert.equal(doc.total(), 0);
});

test('onStart returning false vetoes the drag and releases the capture', () => {
    const el = fakeElement();
    const r = recorder({ onStart: () => false });
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerId: 6 }));
    el.dispatch('pointermove', ptr({ pointerId: 6, clientX: 9 }));

    assert.deepEqual(el.released, [6]);
    assert.equal(r.names().includes('move'), false);
    assert.equal(r.last('release').outcome, 'aborted');
    assert.equal(doc.total(), 0);
});

// ── touch-action guidance ───────────────────────────────────────────────────────────────────

test('without a long-press the element gets touch-action none and gets it back on detach', () => {
    const el = fakeElement();
    el.style.touchAction = 'pan-y';
    const handle = attach(el, { threshold: 0 });
    assert.equal(el.style.touchAction, 'none', 'an immediate touch drag needs the browser not to pan');

    handle.detach();
    assert.equal(el.style.touchAction, 'pan-y');
});

test('a long-press or delegated drag leaves the scroll behaviour of the element alone', () => {
    const longPress = fakeElement();
    attach(longPress, { longPressMs: 400 });
    assert.equal(longPress.style.touchAction, undefined, 'scrolling must keep working until the long-press fires');

    const delegated = fakeElement();
    attach(delegated, { threshold: 0, selector: '.handle' });
    assert.equal(delegated.style.touchAction, undefined, 'the container scrolls; only the handle may be touch-action none (CSS)');
});

test('an explicit touchAction wins and null opts out', () => {
    const a = fakeElement();
    attach(a, { threshold: 0, touchAction: 'pan-y' });
    assert.equal(a.style.touchAction, 'pan-y');

    const b = fakeElement();
    attach(b, { threshold: 0, touchAction: null });
    assert.equal(b.style.touchAction, undefined);
});

test('the touchmove default is prevented only while a drag is active (non-passive listener)', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const el = fakeElement();
    attach(el, { longPressMs: 400 });
    // J4: a permanent non-passive touchmove on document forces Chrome to wait for the listener on
    // every scroll frame. It must not exist until a long-press drag has actually started.
    assert.deepEqual(el.options('touchmove'), [], 'no touchmove listener while idle');

    const idle = el.dispatch('touchmove', { cancelable: true, preventDefault() { this.prevented = true; } });
    assert.equal(idle.prevented, undefined);

    el.dispatch('pointerdown', ptr({ pointerType: 'touch' }));
    assert.deepEqual(el.options('touchmove'), [], 'a pending long-press still has no touchmove listener');

    t.mock.timers.tick(400);
    assert.deepEqual(el.options('touchmove'), [false], 'the listener is added, non-passive, only once the drag starts');
    const dragging = el.dispatch('touchmove', { cancelable: true, preventDefault() { this.prevented = true; } });
    assert.equal(dragging.prevented, true);

    el.dispatch('pointerup', ptr({ pointerType: 'touch' }));
    assert.deepEqual(el.options('touchmove'), [], 'removed again when the gesture ends');

    const immediate = fakeElement();
    attach(immediate, { threshold: 0 });
    assert.deepEqual(immediate.options('touchmove'), [false], 'an immediate drag keeps the listener for the attachment');
});

// ── drag class, reduced motion ──────────────────────────────────────────────────────────────

test('the dragging class is on the target for the drag only', () => {
    const el = fakeElement();
    attach(el, { threshold: 0 });

    el.dispatch('pointerdown', ptr());
    assert.equal(el.classes.has('tm-pointer-dragging'), false, 'a press is not a drag yet');
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    assert.equal(el.classes.has('tm-pointer-dragging'), true);
    el.dispatch('pointerup', ptr({ clientX: 9 }));
    assert.equal(el.classes.has('tm-pointer-dragging'), false);
});

test('dragClass overrides or disables the class', () => {
    const el = fakeElement();
    attach(el, { threshold: 0, dragClass: 'my-drag' });
    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));
    assert.deepEqual([...el.classes], ['my-drag']);

    const none = fakeElement();
    attach(none, { threshold: 0, dragClass: null });
    none.dispatch('pointerdown', ptr());
    none.dispatch('pointermove', ptr({ clientX: 9 }));
    assert.equal(none.classes.size, 0);
});

test('prefersReducedMotion reads the media query and never throws', () => {
    assert.equal(prefersReducedMotion(), false, 'no matchMedia (SSR, Node)');

    globalThis.matchMedia = query => ({ matches: query.includes('reduce') });
    assert.equal(prefersReducedMotion(), true);

    globalThis.matchMedia = () => { throw new Error('boom'); };
    assert.equal(prefersReducedMotion(), false);
});

test('callbacks learn about reduced motion so they can skip animation', () => {
    globalThis.matchMedia = () => ({ matches: true });
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr());
    el.dispatch('pointermove', ptr({ clientX: 9 }));

    assert.equal(r.last('start').reducedMotion, true);
    assert.equal(r.last('move').reducedMotion, true);
});

// ── delegation ──────────────────────────────────────────────────────────────────────────────

test('a delegated selector only starts from matching descendants and captures on the match', () => {
    const container = fakeElement();
    const handle = child(container, ['.handle']);
    const other = child(container, []);
    const r = recorder();
    attach(container, { threshold: 0, selector: '.handle', ...r.options });

    container.dispatch('pointerdown', ptr({ target: other }));
    assert.equal(r.log.length, 0, 'a press elsewhere in the container is ignored');

    container.dispatch('pointerdown', ptr({ target: handle, pointerId: 8 }));
    assert.deepEqual([...handle.captured], [8], 'capture lives on the handle, not the container');
    assert.equal(container.captured.size, 0);

    container.dispatch('pointermove', ptr({ target: handle, pointerId: 8, clientX: 9 }));
    assert.equal(r.last('start').target, handle);
    assert.equal(handle.classes.has('tm-pointer-dragging'), true);

    container.dispatch('pointerup', ptr({ target: handle, pointerId: 8, clientX: 9 }));
    assert.deepEqual(handle.released, [8]);
    assert.equal(handle.classes.has('tm-pointer-dragging'), false);
});

// ── lifecycle / multi-instance ──────────────────────────────────────────────────────────────

test('detach mid-drag cancels with a reason, removes every listener and is idempotent', () => {
    const el = fakeElement();
    const r = recorder();
    const handle = attach(el, { threshold: 0, ...r.options });

    el.dispatch('pointerdown', ptr({ pointerId: 5 }));
    el.dispatch('pointermove', ptr({ pointerId: 5, clientX: 9 }));
    handle.detach();

    assert.equal(r.last('cancel').reason, 'detached');
    assert.deepEqual(el.released, [5]);
    assert.equal(el.total(), 0, 'no listener is left on the element');
    assert.equal(doc.total(), 0, 'no listener is left on the document');
    assert.doesNotThrow(() => handle.detach());
    assert.equal(r.names().filter(name => name === 'cancel').length, 1);
});

test('after detach the element no longer reacts', () => {
    const el = fakeElement();
    const r = recorder();
    attach(el, { threshold: 0, ...r.options }).detach();

    el.dispatch('pointerdown', ptr());
    assert.equal(r.log.length, 0);
});

test('detach with no gesture touches nothing and restores nothing it did not change', () => {
    const el = fakeElement();
    const handle = attach(el, { threshold: 0, touchAction: null });
    assert.doesNotThrow(() => handle.detach());
    assert.equal(el.style.touchAction, undefined);
});

test('two instances drag at once, independently; detaching one leaves the other working', () => {
    const a = fakeElement();
    const b = fakeElement();
    const ra = recorder();
    const rb = recorder();
    const handleA = attach(a, { threshold: 0, ...ra.options });
    attach(b, { threshold: 0, ...rb.options });

    a.dispatch('pointerdown', ptr({ pointerId: 1 }));
    b.dispatch('pointerdown', ptr({ pointerId: 2, pointerType: 'touch' }));
    a.dispatch('pointermove', ptr({ pointerId: 1, clientX: 10 }));
    b.dispatch('pointermove', ptr({ pointerId: 2, pointerType: 'touch', clientX: 20 }));

    assert.equal(ra.last('move').dx, 10);
    assert.equal(rb.last('move').dx, 20);

    handleA.detach();
    b.dispatch('pointermove', ptr({ pointerId: 2, pointerType: 'touch', clientX: 30 }));
    b.dispatch('pointerup', ptr({ pointerId: 2, pointerType: 'touch', clientX: 30 }));
    assert.equal(rb.last('end').dx, 30);
    assert.equal(ra.last('cancel').reason, 'detached');
});

test('Escape cancels every active drag, each exactly once', () => {
    const a = fakeElement();
    const b = fakeElement();
    const ra = recorder();
    const rb = recorder();
    attach(a, { threshold: 0, ...ra.options });
    attach(b, { threshold: 0, ...rb.options });

    a.dispatch('pointerdown', ptr({ pointerId: 1 }));
    a.dispatch('pointermove', ptr({ pointerId: 1, clientX: 5 }));
    b.dispatch('pointerdown', ptr({ pointerId: 2 }));
    b.dispatch('pointermove', ptr({ pointerId: 2, clientX: 5 }));
    doc.dispatch('keydown', { key: 'Escape' });

    assert.equal(ra.names().filter(n => n === 'cancel').length, 1);
    assert.equal(rb.names().filter(n => n === 'cancel').length, 1);
    assert.equal(doc.total(), 0);
});

test('attach with a missing element is an inert handle', () => {
    const handle = attach(null, { threshold: 0 });
    assert.equal(handle.dragging, false);
    assert.doesNotThrow(() => { handle.cancel(); handle.detach(); });
});

test('a throwing onEnd still leaves the helper idle with everything released', () => {
    const el = fakeElement();
    const handle = attach(el, { threshold: 0, onEnd() { throw new Error('host bug'); } });

    el.dispatch('pointerdown', ptr({ pointerId: 2 }));
    el.dispatch('pointermove', ptr({ pointerId: 2, clientX: 9 }));
    assert.throws(() => el.dispatch('pointerup', ptr({ pointerId: 2, clientX: 9 })), /host bug/);

    assert.equal(handle.dragging, false);
    assert.equal(el.captured.size, 0);
    assert.equal(doc.total(), 0);
    assert.equal(el.classes.size, 0);
});

// ── keyboard alternative (WCAG 2.5.7) ────────────────────────────────────────────────────────

test('attachKeyboardMove preventDefaults only the Alt+Arrow chord and reports the step', () => {
    const el = fakeElement();
    const steps = [];
    const handle = attachKeyboardMove(el, { onStep: (step) => steps.push(step) });

    const chord = { key: 'ArrowLeft', altKey: true, preventDefault() { this.prevented = true; } };
    el.dispatch('keydown', chord);
    assert.equal(chord.prevented, true, 'Alt+ArrowLeft must be cancelled or the browser navigates Back');
    assert.equal(steps.length, 1);
    assert.equal(steps[0].key, 'ArrowLeft');
    assert.deepEqual({ x: steps[0].x, y: steps[0].y }, { x: -1, y: 0 });

    const plain = { key: 'ArrowLeft', altKey: false, preventDefault() { this.prevented = true; } };
    el.dispatch('keydown', plain);
    assert.equal(plain.prevented, undefined, 'a plain arrow is the browser\'s, not the helper\'s');

    const tab = { key: 'Tab', altKey: true, preventDefault() { this.prevented = true; } };
    el.dispatch('keydown', tab);
    assert.equal(tab.prevented, undefined, 'only the arrow chord is cancelled; Tab must still move focus');

    handle.detach();
    const after = { key: 'ArrowRight', altKey: true, preventDefault() { this.prevented = true; } };
    el.dispatch('keydown', after);
    assert.equal(after.prevented, undefined, 'detach removes the listener');
});
