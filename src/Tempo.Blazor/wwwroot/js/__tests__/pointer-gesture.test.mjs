// pointer-gesture.js tests with hand-rolled DOM stubs (no jsdom): the two-pointer pinch session
// (start on the second finger, end when it drops below two), every cancel path, the ctrl+wheel
// zoom with its debounced end, and the edge auto-scroller ported from spreadsheet-canvas.js.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachPinch, createAutoScroller } from '../pointer-gesture.js';
import { attach } from '../pointer-drag.js';

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
        // Capture listeners fire before bubble listeners, the way a real target does.
        dispatchPhased(type, event = {}) {
            const list = [...(map.get(type) ?? [])];
            let stopped = false;
            const stop = event.stopPropagation?.bind(event);
            event.stopPropagation = () => { stopped = true; stop?.(); };
            for (const entry of list.filter(item => item.capture)) entry.fn(event);
            if (stopped) return event;
            for (const entry of list.filter(item => !item.capture)) entry.fn(event);
            return event;
        },
        captures(type) { return (map.get(type) ?? []).some(entry => entry.capture); },
        count(type) { return (map.get(type) ?? []).length; },
        total() { let n = 0; for (const list of map.values()) n += list.length; return n; },
        options(type) { return (map.get(type) ?? []).map(entry => entry.passive); },
    };
}

function fakeElement() {
    const el = {
        ...listenerBag(),
        style: {},
        captured: new Set(),
        released: [],
        setPointerCapture(id) { el.captured.add(id); },
        releasePointerCapture(id) { el.captured.delete(id); el.released.push(id); },
    };
    return el;
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
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
});

const touch = (pointerId, clientX, clientY, over = {}) => ({
    pointerId, pointerType: 'touch', isPrimary: pointerId === 1, clientX, clientY,
    preventDefault() { this.defaultPrevented = true; },
    stopPropagation() { this.propagationStopped = true; },
    ...over,
});

const wheelEvent = (over = {}) => ({
    ctrlKey: false, deltaY: 0, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; },
    ...over,
});

function pinchRecorder(extra = {}) {
    const log = [];
    const push = name => info => { log.push([name, info]); };
    return {
        log,
        names: () => log.map(([name]) => name),
        last: name => [...log].reverse().find(([n]) => n === name)?.[1],
        all: name => log.filter(([n]) => n === name).map(([, info]) => info),
        options: {
            onStart: push('start'), onChange: push('change'), onEnd: push('end'), onCancel: push('cancel'),
            ...extra,
        },
    };
}

/** A rAF queue the test flushes by hand: deterministic frames, no timers. */
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

/** A scroll container a real browser would recognise: the scroll position CLAMPS to 0..max. */
function fakeScrollContainer(rect, maxScrollLeft = 1000, maxScrollTop = 800) {
    const container = { getBoundingClientRect: () => ({ ...rect }) };
    let scrollLeft = 0;
    let scrollTop = 0;
    Object.defineProperty(container, 'scrollLeft', {
        get: () => scrollLeft,
        set(value) { scrollLeft = Math.max(0, Math.min(maxScrollLeft, value)); },
    });
    Object.defineProperty(container, 'scrollTop', {
        get: () => scrollTop,
        set(value) { scrollTop = Math.max(0, Math.min(maxScrollTop, value)); },
    });
    return container;
}

// ── attachPinch: the two-finger session ─────────────────────────────────────────────────────

test('two touch fingers spreading apart report start, change with scale > 1 at the centre, and one end', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    const pinch = attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    assert.deepEqual(r.names(), [], 'one finger is not a pinch yet');

    el.dispatch('pointerdown', touch(2, 200, 100));
    assert.deepEqual(r.names(), ['start']);
    assert.equal(r.last('start').centerX, 150);
    assert.equal(r.last('start').centerY, 100);
    assert.equal(r.last('start').distance, 100);
    assert.equal(pinch.active, true);
    assert.deepEqual([...el.captured].sort(), [1, 2], 'both fingers are captured by id');

    el.dispatch('pointermove', touch(1, 50, 100));
    el.dispatch('pointermove', touch(2, 250, 100));
    const change = r.last('change');
    assert.equal(change.scale, 2);                       // 200px apart / 100px at the start
    assert.equal(change.centerX, 150);
    assert.equal(change.centerY, 100);
    assert.equal(change.dx, 0);
    assert.equal(change.dy, 0);

    el.dispatch('pointermove', touch(1, 50, 140));
    el.dispatch('pointermove', touch(2, 250, 140));
    assert.ok(r.last('change').scale > 1);
    assert.equal(r.last('change').centerY, 140, 'the centre follows the fingers');

    el.dispatch('pointerup', touch(1, 50, 140));
    assert.deepEqual(r.names().filter(name => name === 'end'), ['end'], 'committed exactly once');
    assert.equal(r.last('end').scale, 2);
    assert.equal(pinch.active, false);
    assert.deepEqual(r.names().filter(name => name === 'cancel'), []);
});

test('a single finger never starts a pinch', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointermove', touch(1, 140, 160));
    el.dispatch('pointermove', touch(1, 180, 220));
    el.dispatch('pointerup', touch(1, 180, 220));

    assert.deepEqual(r.names(), []);
});

test('a third finger is ignored while the pinch continues', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100));
    el.dispatch('pointerdown', touch(3, 300, 100));
    assert.deepEqual(r.names(), ['start'], 'the extra finger starts nothing');
    assert.equal(el.captured.has(3), false, 'the extra finger is not captured');

    el.dispatch('pointermove', touch(3, 380, 100));
    assert.deepEqual(r.names(), ['start'], 'moving the extra finger reports nothing');

    el.dispatch('pointerup', touch(3, 380, 100));
    assert.deepEqual(r.names(), ['start'], 'lifting the extra finger does not end the pinch');

    el.dispatch('pointermove', touch(2, 260, 100));
    assert.ok(r.last('change').scale > 1, 'the pinch keeps reporting with two fingers');
});

test('pointercancel of either finger cancels the pinch and never commits', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100));
    el.dispatch('pointercancel', touch(2, 200, 100));

    assert.deepEqual(r.names(), ['start', 'cancel']);
    assert.equal(r.last('cancel').reason, 'pointercancel');
    assert.deepEqual(r.names().filter(name => name === 'end'), [], 'a cancelled pinch never commits');
    assert.deepEqual([...el.captured].sort(), [], 'both captures are released');
    assert.equal(doc.count('keydown'), 0, 'the Escape listener goes with the session');

    el.dispatch('pointermove', touch(1, 140, 100));
    assert.deepEqual(r.names(), ['start', 'cancel'], 'nothing reports after the cancel');
});

test('pointercancel of the FIRST finger cancels the pinch the same way', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100));
    el.dispatch('pointercancel', touch(1, 100, 100));

    assert.deepEqual(r.names(), ['start', 'cancel'], 'either finger cancels: the pinch is gone either way');
    assert.equal(r.last('cancel').reason, 'pointercancel');
    assert.deepEqual(r.names().filter(name => name === 'end'), []);
    assert.deepEqual([...el.captured].sort(), [], 'both captures are released');
});

test('lostpointercapture of a tracked finger mid-pinch cancels with reason lostpointercapture', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    const pinch = attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100));
    el.dispatch('lostpointercapture', { pointerId: 1 });

    assert.deepEqual(r.names(), ['start', 'cancel']);
    assert.equal(r.last('cancel').reason, 'lostpointercapture');
    assert.deepEqual(r.names().filter(name => name === 'end'), [], 'a half-seen gesture never commits');
    assert.deepEqual([...el.captured].sort(), [], 'both captures are gone');
    assert.equal(doc.count('keydown'), 0, 'the Escape listener goes with the session');
    assert.equal(pinch.active, false);

    el.dispatch('pointermove', touch(2, 260, 100));
    assert.deepEqual(r.names(), ['start', 'cancel'], 'nothing reports after the cancel');
});

test('lostpointercapture with a lone finger forgets the finger and unbinds Escape', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('lostpointercapture', { pointerId: 1 });
    assert.deepEqual(r.names(), [], 'no pinch: nothing to cancel');
    assert.equal(doc.count('keydown'), 0, 'no tracked finger left: the Escape listener is unbound');

    el.dispatch('pointermove', touch(1, 140, 100));
    el.dispatch('pointerup', touch(1, 140, 100));
    assert.deepEqual(r.names(), [], 'the forgotten finger reports nothing');
});

test('minScale/maxScale clamp the reported scale in onChange and onEnd', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, { ...r.options, minScale: 0.5, maxScale: 1.5 });

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100));
    el.dispatch('pointermove', touch(1, 50, 100));
    el.dispatch('pointermove', touch(2, 250, 100));      // 200px / 100px = 2x spread
    assert.equal(r.last('change').scale, 1.5, 'onChange: 2x clamped down to maxScale');

    el.dispatch('pointermove', touch(1, 90, 100));
    el.dispatch('pointermove', touch(2, 160, 100));      // 70px / 100px = 0.7, inside the clamp
    assert.ok(Math.abs(r.last('change').scale - 0.7) < 1e-12, 'inside the clamp the raw scale is reported');

    el.dispatch('pointermove', touch(1, 95, 100));
    el.dispatch('pointermove', touch(2, 145, 100));      // 50px / 100px = 0.5 → minScale
    el.dispatch('pointerup', touch(1, 95, 100));
    assert.equal(r.last('end').scale, 0.5, 'onEnd reports the clamped scale too');
});

test('pointerTypes: [] allows every pointer type', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, { ...r.options, pointerTypes: [] });

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100, { pointerType: 'pen' }));
    assert.deepEqual(r.names(), ['start'], 'an empty type list accepts touch and pen alike');
});

test('Escape cancels the active pinch, stops propagation, and a one-finger press leaves Escape alone', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    assert.equal(doc.count('keydown'), 1, 'the Escape listener is bound in the capture phase while fingers are down');
    assert.equal(doc.captures('keydown'), true, 'the Escape listener is registered in the CAPTURE phase, as pointer-drag.js does');
    const lonePressEscape = doc.dispatchPhased('keydown', { key: 'Escape', stopPropagation() { this.propagationStopped = true; } });
    assert.ok(!lonePressEscape.propagationStopped, 'no pinch: the dialog around it keeps the key');
    assert.deepEqual(r.names(), []);

    el.dispatch('pointerdown', touch(2, 200, 100));
    const escape = doc.dispatchPhased('keydown', { key: 'Escape', stopPropagation() { this.propagationStopped = true; } });
    assert.equal(escape.propagationStopped, true, 'an active pinch consumes the Escape');
    assert.deepEqual(r.names(), ['start', 'cancel']);
    assert.equal(r.last('cancel').reason, 'escape');
    assert.deepEqual(r.names().filter(name => name === 'end'), []);
});

test('mouse pointers are ignored by default and honoured with pointerTypes', () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100, { pointerType: 'mouse' }));
    el.dispatch('pointerdown', touch(2, 200, 100, { pointerType: 'mouse' }));
    assert.deepEqual(r.names(), [], 'mouse cannot pinch by default');

    const r2 = pinchRecorder();
    attachPinch(el, { ...r2.options, pointerTypes: ['mouse'] });
    el.dispatch('pointerdown', touch(1, 100, 100, { pointerType: 'mouse' }));
    el.dispatch('pointerdown', touch(2, 200, 100, { pointerType: 'mouse' }));
    assert.deepEqual(r2.names(), ['start'], 'pointerTypes opens the gesture to other types');
});

test('ctrl+wheel zooms by exp(-deltaY/100) with preventDefault only when ctrl is held, and onEnd is debounced', async () => {
    const el = fakeElement();
    const r = pinchRecorder();
    attachPinch(el, r.options);
    assert.deepEqual(el.options('wheel'), [false], 'the wheel listener must be non-passive or preventDefault is ignored');

    const zoomTick = wheelEvent({ ctrlKey: true, deltaY: -100, clientX: 60, clientY: 70 });
    el.dispatch('wheel', zoomTick);
    assert.deepEqual(r.names(), ['start', 'change']);
    assert.ok(Math.abs(r.last('change').scale - Math.exp(1)) < 1e-9, 'one tick: exp(-(-100)/100) = e');
    assert.equal(zoomTick.defaultPrevented, true, 'ctrl+wheel is prevented — it would zoom the browser');
    assert.equal(r.last('change').centerX, 60);
    assert.equal(r.last('change').centerY, 70);

    el.dispatch('wheel', wheelEvent({ ctrlKey: true, deltaY: -100, clientX: 60, clientY: 70 }));
    assert.deepEqual(r.names(), ['start', 'change', 'change'], 'a burst is one gesture: no second start');
    assert.ok(Math.abs(r.last('change').scale - Math.exp(2)) < 1e-9, 'the burst scale is cumulative');

    await new Promise(resolve => setTimeout(resolve, 220));
    assert.deepEqual(r.names(), ['start', 'change', 'change', 'end'], 'onEnd comes once, 150 ms after the last tick');
    assert.ok(Math.abs(r.last('end').scale - Math.exp(2)) < 1e-9);

    const plain = wheelEvent({ deltaY: -120 });
    el.dispatch('wheel', plain);
    assert.equal(plain.defaultPrevented, undefined, 'plain wheel is left to the page scroll');
    assert.deepEqual(r.names(), ['start', 'change', 'change', 'end']);
});

test('detach restores touch-action, removes listeners, cancels an active pinch once and is safe twice', () => {
    const el = fakeElement();
    el.style.touchAction = 'pan-y';
    const r = pinchRecorder();
    const pinch = attachPinch(el, r.options);
    assert.equal(el.style.touchAction, 'none', 'the default sets touch-action: none');

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointerdown', touch(2, 200, 100));
    const listenersBefore = el.total() + doc.total();
    assert.ok(listenersBefore > 0);

    pinch.detach();
    assert.equal(el.style.touchAction, 'pan-y', 'the previous touch-action is restored verbatim');
    assert.equal(el.total(), 0, 'every element listener is removed');
    assert.equal(doc.total(), 0, 'the document Escape listener is removed');
    assert.deepEqual(r.names(), ['start', 'cancel']);
    assert.equal(r.last('cancel').reason, 'detached');
    assert.deepEqual([...el.captured].sort(), [], 'the captures are released');

    pinch.detach();
    assert.equal(r.names().filter(name => name === 'cancel').length, 1, 'a second detach is a no-op');

    el.dispatch('pointerdown', touch(1, 100, 100));
    assert.deepEqual(r.names(), ['start', 'cancel'], 'a detached pinch reports nothing');

    const fresh = fakeElement();
    const freshPinch = attachPinch(fresh, pinchRecorder().options);
    assert.equal(fresh.style.touchAction, 'none', 'the default claims a fresh element too');
    freshPinch.detach();
    assert.equal(fresh.style.touchAction, undefined, 'a fresh element restores its untouched value');
});

// ── createAutoScroller: the spreadsheet-canvas port ─────────────────────────────────────────

test('createAutoScroller scrolls round(power * maxStep) at the edges, nothing in the middle', () => {
    const raf = fakeRaf();
    const container = fakeScrollContainer({ left: 0, top: 0, right: 400, bottom: 300 });
    container.scrollLeft = 500;           // pre-scrolled: a leftward scroll must have room
    container.scrollTop = 300;
    const scrolled = [];
    const scroller = createAutoScroller(container, { onScroll: info => scrolled.push(info) });

    scroller.update(200, 150);            // the middle: no power on any edge
    assert.equal(raf.flush(), 1);
    assert.equal(container.scrollLeft, 500);
    assert.equal(container.scrollTop, 300);
    assert.deepEqual(scrolled, []);

    scroller.update(10, 150);             // 10px from the left edge: (36-10)/36 * 28 → round(-20.2) = -20
    raf.flush();
    assert.equal(container.scrollLeft, 480);
    assert.deepEqual(scrolled, [{ dx: -20, dy: 0 }]);

    scroller.update(390, 150);            // 10px from the right edge
    raf.flush();
    assert.equal(container.scrollLeft, 500);
    assert.deepEqual(scrolled.at(-1), { dx: 20, dy: 0 });

    scroller.update(200, 10);             // 10px from the top edge
    raf.flush();
    assert.equal(container.scrollTop, 280);
    assert.deepEqual(scrolled.at(-1), { dx: 0, dy: -20 });

    raf.flush();                          // the pointer has not moved: the loop keeps scrolling
    assert.equal(container.scrollTop, 260);
    assert.equal(scroller.scrolledY, -40, 'scrolledY accumulates the APPLIED deltas over the frames');
    assert.equal(scroller.scrolledX, 0, 'x scrolled -20 then +20: scrolledX nets 0');
    assert.equal(raf.pending(), 1, 'the loop re-arms itself until stop()');

    scroller.stop();
    assert.equal(raf.pending(), 0, 'stop() cancels the pending frame');
    raf.flush();
    assert.equal(container.scrollTop, 260, 'nothing scrolls after stop()');

    scroller.update(200, 10);             // reusable across gestures: update starts a new loop
    assert.equal(raf.pending(), 1, 'a stopped scroller resumes on the next update()');
    raf.flush();
    assert.equal(container.scrollTop, 240, 'the accumulation continues across gestures');
    assert.equal(scroller.scrolledY, -60);
});

test('axis y ignores the horizontal edge and stop() ends the loop', () => {
    const raf = fakeRaf();
    const container = fakeScrollContainer({ left: 0, top: 0, right: 400, bottom: 300 });
    container.scrollTop = 300;            // room to scroll up
    const scroller = createAutoScroller(container, { axis: 'y' });

    scroller.update(10, 10);              // near BOTH edges: only y may move
    raf.flush();
    assert.equal(container.scrollLeft, 0);
    assert.equal(container.scrollTop, 280);
    assert.equal(scroller.scrolledX, 0);

    scroller.stop();
    assert.equal(raf.pending(), 0);
});

test('at a scroll limit the applied delta is zero: scrolledX/Y stay put and onScroll stays silent', () => {
    const raf = fakeRaf();
    const container = fakeScrollContainer({ left: 0, top: 0, right: 400, bottom: 300 });
    const scrolled = [];
    const scroller = createAutoScroller(container, { onScroll: info => scrolled.push(info) });

    scroller.update(200, 10);             // the top edge, but scrollTop is already 0
    raf.flush();
    raf.flush();
    assert.equal(container.scrollTop, 0, 'the browser clamped: it never scrolled negative');
    assert.equal(scroller.scrolledY, 0, 'only the APPLIED delta accumulates');
    assert.deepEqual(scrolled, [], 'no applied delta → no onScroll');
    assert.equal(raf.pending(), 1, 'the loop stays armed: the pointer may move back into range');

    scroller.update(10, 150);             // the left edge with scrollLeft 0: the same silence on x
    raf.flush();
    assert.equal(container.scrollLeft, 0);
    assert.equal(scroller.scrolledX, 0);
    assert.deepEqual(scrolled, []);
});

test('the edge power is capped at 1 past the edge: exactly maxStep per frame, never more', () => {
    const raf = fakeRaf();
    const container = fakeScrollContainer({ left: 0, top: 0, right: 400, bottom: 300 });
    container.scrollLeft = 500;
    const scroller = createAutoScroller(container);

    scroller.update(-50, 150);            // 50px PAST the left edge
    raf.flush();
    assert.equal(container.scrollLeft, 472, '50px past the edge: full power (28), not (36+50)/36 * 28 ≈ 67');
});

test('container = null scrolls the page and edge-tests against the viewport rect', () => {
    const raf = fakeRaf();
    const page = fakeScrollContainer(
        { left: 0, top: 0, right: 20000, bottom: 20000 },   // the document box, NOT the viewport
        5000, 4000);
    page.scrollLeft = 500;
    page.scrollTop = 300;
    doc.scrollingElement = page;
    doc.documentElement = { clientWidth: 1024, clientHeight: 768 };
    const scrolled = [];
    const scroller = createAutoScroller(null, { onScroll: info => scrolled.push(info) });

    scroller.update(1010, 384);           // 14px from the right VIEWPORT edge (1024)
    raf.flush();
    assert.equal(page.scrollLeft, 517, 'round((36-14)/36 * 28) = 17: measured against clientWidth');
    assert.deepEqual(scrolled.at(-1), { dx: 17, dy: 0 });

    scroller.update(512, 758);            // 10px from the bottom VIEWPORT edge (768)
    raf.flush();
    assert.equal(page.scrollTop, 320, 'round((36-10)/36 * 28) = 20 against clientHeight');
    assert.deepEqual(scrolled.at(-1), { dx: 0, dy: 20 });
});

// ── coexistence with the single-pointer helper ──────────────────────────────────────────────

test('a pinch and a single-finger drag coexist: the host cancels the drag from the pinch onStart', () => {
    const el = fakeElement();
    const dragLog = [];
    const drag = attach(el, {
        threshold: 0,
        onStart: () => dragLog.push('drag:start'),
        onMove: () => dragLog.push('drag:move'),
        onEnd: () => dragLog.push('drag:end'),
        onCancel: info => dragLog.push(`drag:cancel:${info.reason}`),
    });
    const r = pinchRecorder();
    r.options.onStart = info => {
        r.log.push(['start', info]);          // keep the recorder honest: log AND act
        dragLog.push('pinch:start');
        drag.cancel('pinch');
    };
    attachPinch(el, r.options);

    el.dispatch('pointerdown', touch(1, 100, 100));
    el.dispatch('pointermove', touch(1, 110, 100));
    assert.deepEqual(dragLog, ['drag:start', 'drag:move'], 'finger one drives the single-pointer drag');

    el.dispatch('pointerdown', touch(2, 200, 100));
    assert.deepEqual(dragLog, ['drag:start', 'drag:move', 'pinch:start', 'drag:cancel:pinch'],
        'the second finger never cancels the drag by itself — the host does it from pinch onStart');
    assert.deepEqual(r.names(), ['start']);

    el.dispatch('pointermove', touch(1, 50, 100));
    el.dispatch('pointermove', touch(2, 250, 100));
    // The pinch starts from where the fingers ARE when the second lands: the drag already moved
    // finger one to 110, so the start distance is 90 (110|200) and the start centre is 155.
    assert.ok(Math.abs(r.last('change').scale - 200 / 90) < 1e-9, 'scale = distance / startDistance');
    assert.equal(r.last('change').centerX, 150);
    assert.equal(r.last('change').dx, -5);
    assert.deepEqual(dragLog.filter(entry => entry.startsWith('drag:move')).length, 1, 'the drag no longer follows finger one');

    el.dispatch('pointerup', touch(1, 50, 100));
    assert.deepEqual(r.names(), ['start', 'change', 'change', 'end'], 'two moves report twice, the end comes once');
    assert.deepEqual(dragLog.at(-1), 'drag:cancel:pinch', 'a cancelled drag never commits');

    drag.detach();
});
