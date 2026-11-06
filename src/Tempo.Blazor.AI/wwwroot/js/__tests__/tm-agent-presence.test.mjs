// tm-agent-presence.js tests — where an anchored presence cursor sits.
//
// The component owns the cursor's words, colours and the roster; this module only measures the anchor inside the content
// and writes two custom properties. These tests pin the contract that matters: the offsets are relative to the content
// box, a marker whose anchor disappeared is HIDDEN instead of left at a stale position, a burst of mutations costs one
// measurement, the observers watch the content and every anchor, and detach releases everything it attached.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    ANCHOR_ATTRIBUTE,
    FLIP_ATTRIBUTE,
    MARKER_ATTRIBUTE,
    PLACED_ATTRIBUTE,
    X_PROPERTY,
    Y_PROPERTY,
    anchorSelector,
    attachPresence,
    flipMarker,
    offsetValue,
    placeMarker,
    resolveAnchorPosition,
    shouldFlip,
} from '../tm-agent-presence.js';

/** A stand-in for an element's inline style. */
class FakeStyle {
    constructor() {
        this.props = {};
    }

    setProperty(name, value) {
        this.props[name] = value;
    }
}

/** A stand-in for an element with a measured box and attributes. */
class FakeElement {
    constructor({ rect = null, attrs = {} } = {}) {
        this.rect = rect;
        this.attrs = { ...attrs };
        this.style = new FakeStyle();
        this.lookup = new Map();
        this.rectReads = 0;
    }

    getBoundingClientRect() {
        this.rectReads += 1;
        return this.rect;
    }

    getAttribute(name) {
        return Object.prototype.hasOwnProperty.call(this.attrs, name) ? this.attrs[name] : null;
    }

    setAttribute(name, value) {
        this.attrs[name] = String(value);
    }

    removeAttribute(name) {
        delete this.attrs[name];
    }

    querySelector(selector) {
        return this.lookup.get(selector) ?? null;
    }
}

/** A content element: anchors are looked up by the selector the module builds. */
function fakeContent(rect, anchors = {}) {
    const content = new FakeElement({ rect });
    for (const [id, anchor] of Object.entries(anchors)) {
        content.lookup.set(anchorSelector(id), anchor);
    }

    return content;
}

/** A root element holding the markers the component rendered. */
function fakeRoot(markers) {
    const root = new FakeElement();
    root.querySelectorAll = selector => selector === `[${MARKER_ATTRIBUTE}]` ? markers : [];
    return root;
}

/** A marker element, anchored when `anchorId` is given. */
function fakeMarker(id, anchorId, attrs = {}) {
    const marker = new FakeElement({ attrs: { [MARKER_ATTRIBUTE]: id, ...attrs } });
    if (anchorId) {
        marker.setAttribute(ANCHOR_ATTRIBUTE, anchorId);
    }

    return marker;
}

/** A window stand-in with countdown-free timers, a controllable clock and recording observers. */
function fakeWindow() {
    const state = {
        clock: 0,
        timers: [],
        nextId: 1,
        frames: [],
        listeners: [],
        removedListeners: [],
        resizeObservers: [],
        mutationObservers: [],
    };

    state.window = {
        performance: { now: () => state.clock },
        setTimeout(fn, delay) {
            const entry = { id: state.nextId++, fn, delay };
            state.timers.push(entry);
            return entry.id;
        },
        clearTimeout(id) {
            state.timers = state.timers.filter(entry => entry.id !== id);
        },
        requestAnimationFrame(fn) {
            const entry = { id: state.nextId++, fn };
            state.frames.push(entry);
            return entry.id;
        },
        cancelAnimationFrame(id) {
            state.frames = state.frames.filter(entry => entry.id !== id);
        },
        addEventListener(type, handler, options) {
            state.listeners.push({ type, handler, options });
        },
        removeEventListener(type, handler, options) {
            state.removedListeners.push({ type, handler, options });
        },
        ResizeObserver: class {
            constructor(handler) {
                this.handler = handler;
                this.observed = [];
                this.disconnected = 0;
                state.resizeObservers.push(this);
            }

            observe(element) {
                this.observed.push(element);
            }

            disconnect() {
                this.disconnected += 1;
            }
        },
        MutationObserver: class {
            constructor(handler) {
                this.handler = handler;
                this.observed = [];
                this.options = null;
                this.disconnected = 0;
                state.mutationObservers.push(this);
            }

            observe(element, options) {
                this.observed.push(element);
                this.options = options;
            }

            disconnect() {
                this.disconnected += 1;
            }
        },
    };

    state.flushTimers = () => {
        const pending = [...state.timers];
        state.timers = [];
        for (const entry of pending) {
            entry.fn();
        }
    };

    state.flushFrames = () => {
        const pending = [...state.frames];
        state.frames = [];
        for (const entry of pending) {
            entry.fn();
        }
    };

    state.flushAll = () => {
        state.flushTimers();
        state.flushFrames();
        state.flushTimers();
    };

    return state;
}

test('anchorSelector escapes quotes and backslashes instead of ending the selector early', () => {
    assert.equal(anchorSelector('block-1'), '[data-presence-anchor="block-1"]');
    assert.equal(anchorSelector('a"b'), '[data-presence-anchor="a\\"b"]');
    assert.equal(anchorSelector('a\\b'), '[data-presence-anchor="a\\\\b"]');
    assert.equal(anchorSelector(null), '[data-presence-anchor=""]');
});

test('resolveAnchorPosition subtracts the content box and keeps the anchor top-left', () => {
    const position = resolveAnchorPosition(
        { left: 20, top: 40, width: 400, height: 300 },
        { left: 120, top: 90, width: 10, height: 10 });

    assert.deepEqual(position, { x: 100, y: 50 });
});

test('resolveAnchorPosition refuses a box it cannot trust', () => {
    assert.equal(resolveAnchorPosition(null, { left: 0, top: 0 }), null);
    assert.equal(resolveAnchorPosition({ left: 0, top: 0, width: 0, height: 300 }, { left: 0, top: 0 }), null);
    assert.equal(resolveAnchorPosition({ left: 0, top: 0, width: 400, height: 0 }, { left: 0, top: 0 }), null);
    assert.equal(
        resolveAnchorPosition({ left: 0, top: 0, width: Number.NaN, height: 10 }, { left: 0, top: 0 }),
        null);
    assert.equal(
        resolveAnchorPosition({ left: 0, top: 0, width: 10, height: 10 }, { left: Number.NaN, top: 0 }),
        null);
    assert.equal(resolveAnchorPosition({ left: 0, top: 0, width: 10, height: 10 }, null), null);
});

test('offsetValue rounds to two decimals and refuses nonsense', () => {
    assert.equal(offsetValue(12), '12px');
    assert.equal(offsetValue(12.345), '12.35px');
    assert.equal(offsetValue(-3.2), '-3.2px');
    assert.equal(offsetValue(Number.NaN), null);
    assert.equal(offsetValue(undefined), null);
});

test('placeMarker writes both knobs and marks the marker placed', () => {
    const marker = fakeMarker('m1');

    assert.equal(placeMarker(marker, { x: 10.5, y: 4 }), true);
    assert.equal(marker.style.props[X_PROPERTY], '10.5px');
    assert.equal(marker.style.props[Y_PROPERTY], '4px');
    assert.equal(marker.getAttribute(PLACED_ATTRIBUTE), 'true');
});

test('placeMarker refuses a missing marker or position', () => {
    const marker = fakeMarker('m1');

    assert.equal(placeMarker(null, { x: 1, y: 1 }), false);
    assert.equal(placeMarker(marker, null), false);
    assert.equal(placeMarker(marker, { x: Number.NaN, y: 0 }), false);
    assert.equal(marker.getAttribute(PLACED_ATTRIBUTE), null);
});

test('shouldFlip mirrors a label that would run past the content edge', () => {
    assert.equal(shouldFlip({ x: 40 }, 120, 400), false, 'plenty of room to the right');
    assert.equal(shouldFlip({ x: 300 }, 120, 400), true, 'label would end past the content');
    assert.equal(shouldFlip({ x: 270 }, 120, 400, 16), true, 'the gutter is part of the test');
    assert.equal(shouldFlip({ x: 270 }, 120, 400, 0), false, 'without a gutter it just fits');
    assert.equal(shouldFlip(null, 120, 400), false);
    assert.equal(shouldFlip({ x: 300 }, Number.NaN, 400), false);
    assert.equal(shouldFlip({ x: 300 }, 120, 0), false);
});

test('flipMarker writes the attribute only when the answer changes', () => {
    const marker = fakeMarker('m1');

    flipMarker(marker, true);
    assert.equal(marker.getAttribute(FLIP_ATTRIBUTE), 'true');
    marker.attrs.reads = 0;
    flipMarker(marker, true);
    assert.equal(marker.getAttribute(FLIP_ATTRIBUTE), 'true');

    flipMarker(marker, false);
    assert.equal(marker.getAttribute(FLIP_ATTRIBUTE), null);
    flipMarker(null, true);
    assert.ok(true, 'a missing marker is tolerated');
});

test('a marker near the content edge is mirrored by the measurement', () => {
    const win = fakeWindow();
    const content = fakeContent(
        { left: 0, top: 0, width: 300, height: 200 },
        { 'block-a': new FakeElement({ rect: { left: 250, top: 20 } }) });
    const marker = fakeMarker('m1', 'block-a');
    marker.getBoundingClientRect = () => ({ width: 140 });

    attachPresence(fakeRoot([marker]), content, { window: win.window });

    assert.equal(marker.getAttribute(FLIP_ATTRIBUTE), 'true',
        'a label starting at 250 px in a 300 px box would widen the page');
});

test('attachPresence places every anchored marker relative to the content', () => {
    const win = fakeWindow();
    const content = fakeContent(
        { left: 10, top: 20, width: 400, height: 300 },
        { 'block-a': new FakeElement({ rect: { left: 60, top: 70, width: 12, height: 12 } }) });
    const markers = [fakeMarker('m1', 'block-a')];

    attachPresence(fakeRoot(markers), content, { window: win.window });

    assert.equal(markers[0].style.props[X_PROPERTY], '50px');
    assert.equal(markers[0].style.props[Y_PROPERTY], '50px');
    assert.equal(markers[0].getAttribute(PLACED_ATTRIBUTE), 'true');
});

test('a marker whose anchor is gone is hidden instead of painted at a stale position', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    const marker = fakeMarker('m1', 'missing');
    marker.setAttribute(PLACED_ATTRIBUTE, 'true');

    attachPresence(fakeRoot([marker]), content, { window: win.window });

    assert.equal(marker.getAttribute(PLACED_ATTRIBUTE), null);
    assert.equal(marker.style.props[X_PROPERTY], undefined);
});

test('point markers are left alone: the component paints them from its own fractions', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    const marker = fakeMarker('m1');

    attachPresence(fakeRoot([marker]), content, { window: win.window });

    assert.equal(marker.style.props[X_PROPERTY], undefined);
    assert.equal(marker.getAttribute(PLACED_ATTRIBUTE), null);
});

test('a burst of refreshes costs one measurement', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    const handle = attachPresence(fakeRoot([fakeMarker('m1')]), content, { window: win.window });

    assert.equal(content.rectReads, 1, 'the attach measures once');

    handle.refresh();
    handle.refresh();
    handle.refresh();

    assert.equal(win.timers.length, 1, 'the burst is coalesced into one pending measurement');
    assert.equal(content.rectReads, 1, 'nothing is measured before the throttle elapses');

    win.flushTimers();

    assert.equal(content.rectReads, 2);
});

test('the throttle does not delay a measurement that is already due', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    const handle = attachPresence(fakeRoot([fakeMarker('m1')]), content, { window: win.window });

    win.clock = 500;
    handle.refresh();

    assert.equal(win.timers.length, 0);
    assert.equal(win.frames.length, 1, 'a due measurement waits for the next frame only');
    win.frames[0].fn();
    assert.equal(content.rectReads, 2);
});

test('a refresh picks up an anchor the content gained after the attach', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    const marker = fakeMarker('m1', 'block-late');
    const handle = attachPresence(fakeRoot([marker]), content, { window: win.window });

    assert.equal(marker.getAttribute(PLACED_ATTRIBUTE), null, 'no anchor yet, so the cursor stays hidden');

    content.lookup.set(anchorSelector('block-late'), new FakeElement({ rect: { left: 30, top: 40 } }));
    win.clock = 500;
    handle.refresh();
    win.frames[0].fn();

    assert.equal(marker.getAttribute(PLACED_ATTRIBUTE), 'true');
    assert.equal(marker.style.props[X_PROPERTY], '30px');
    assert.equal(marker.style.props[Y_PROPERTY], '40px');
});

test('the content and every anchor are observed for reflow', () => {
    const win = fakeWindow();
    const anchor = new FakeElement({ rect: { left: 5, top: 5 } });
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 }, { 'block-a': anchor });

    attachPresence(fakeRoot([fakeMarker('m1', 'block-a')]), content, { window: win.window });

    assert.equal(win.resizeObservers.length, 1);
    assert.deepEqual(win.resizeObservers[0].observed, [content, anchor]);
    assert.equal(win.mutationObservers.length, 1);
    assert.deepEqual(win.mutationObservers[0].observed, [content]);
    assert.equal(win.mutationObservers[0].options.attributes, true);
    assert.deepEqual(win.mutationObservers[0].options.attributeFilter, [ANCHOR_ATTRIBUTE]);
});

test('a mutation re-measures once the throttle allows it', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    attachPresence(fakeRoot([fakeMarker('m1')]), content, { window: win.window });

    win.clock = 200;
    win.mutationObservers[0].handler();
    win.flushAll();

    assert.equal(content.rectReads, 2);
});

test('a window scroll and resize ask for a measurement', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    attachPresence(fakeRoot([fakeMarker('m1')]), content, { window: win.window });

    const types = win.listeners.map(listener => listener.type).sort();
    assert.deepEqual(types, ['resize', 'scroll']);
    assert.equal(win.listeners.find(listener => listener.type === 'scroll').options.capture, true,
        'a scroll inside the content must be seen too');

    win.clock = 400;
    win.listeners.find(listener => listener.type === 'resize').handler();
    win.flushAll();

    assert.equal(content.rectReads, 2);
});

test('detach releases the observers, the listeners and every pending measurement', () => {
    const win = fakeWindow();
    const content = fakeContent({ left: 0, top: 0, width: 400, height: 300 });
    const handle = attachPresence(fakeRoot([fakeMarker('m1')]), content, { window: win.window });

    handle.refresh();
    handle.detach();
    handle.detach();

    assert.equal(win.resizeObservers[0].disconnected, 1);
    assert.equal(win.mutationObservers[0].disconnected, 1);
    assert.deepEqual(win.removedListeners.map(listener => listener.type).sort(), ['resize', 'scroll']);
    assert.equal(win.timers.length, 0, 'the pending measurement is cancelled');

    win.clock = 900;
    handle.refresh();
    win.flushTimers();
    assert.equal(content.rectReads, 1, 'a detached handle measures nothing');
});

test('attachPresence tolerates a missing root, content or browser environment', () => {
    const handle = attachPresence(null, null, { window: null });

    handle.refresh();
    assert.equal(handle.placed(), 0);
    handle.detach();
});
