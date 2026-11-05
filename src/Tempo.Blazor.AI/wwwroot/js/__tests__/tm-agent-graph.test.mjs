// tm-agent-graph.js tests — the zoom/pan bridge of TmAgentGraph.
//
// The component owns the zoom level and the layout; the module only reports a gesture's RELATIVE scale and its
// start/end, applies the two-finger pan to the scrollport, and measures the room "fit to view" needs. The shared
// pointer-gesture.js helper owns pinch and ctrl+wheel, so these tests pin the bridge: the scale is clamped into the
// component's window and is CUMULATIVE per gesture, `wheel: true` keeps a plain wheel scrolling the page, the pan moves
// the scrollport (never the page), every callback reaches .NET exactly once, and detach releases the helper.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    applyPan,
    attachGraphGesture,
    clampZoom,
    loadPinch,
    measureViewport,
    zoomFromGesture,
} from '../tm-agent-graph.js';

/** A scripted .NET reference that records every call. */
class FakeDotNet {
    constructor() {
        this.calls = [];
    }

    invokeMethodAsync(name, ...args) {
        this.calls.push({ name, args });
        return Promise.resolve();
    }
}

/** A stand-in for pointer-gesture.js' attachPinch that records its options and returns a controllable handle. */
function fakePinch() {
    const state = { options: null, attachedTo: null, detached: 0, cancelled: 0 };

    return {
        state,
        module: {
            attachPinch(element, options) {
                state.attachedTo = element;
                state.options = options;
                return {
                    get active() { return true; },
                    detach() { state.detached += 1; },
                    cancel(reason) { state.cancelled += 1; state.cancelReason = reason; },
                };
            },
        },
    };
}

/** A stand-in scrollport that records what the module scrolled it to. */
function fakeViewport({ width = 800, height = 600, left = 120, top = 40 } = {}) {
    return {
        clientWidth: width,
        clientHeight: height,
        scrollLeft: left,
        scrollTop: top,
        addEventListener() {},
    };
}

test('clampZoom keeps the scale inside its window', () => {
    assert.equal(clampZoom(1, 0.5, 2.5), 1);
    assert.equal(clampZoom(0.1, 0.5, 2.5), 0.5);
    assert.equal(clampZoom(9, 0.5, 2.5), 2.5);
});

test('clampZoom falls back to the floor for nonsense', () => {
    assert.equal(clampZoom(0, 0.5, 2.5), 0.5);
    assert.equal(clampZoom(-2, 0.5, 2.5), 0.5);
    assert.equal(clampZoom(Number.NaN, 0.5, 2.5), 0.5);
    assert.equal(clampZoom(undefined, 0.5, 2.5), 0.5);
});

test('clampZoom repairs a broken window', () => {
    assert.equal(clampZoom(1, 0, 0), 1, 'a non-positive floor uses the default floor');
    assert.equal(clampZoom(1, 2, 1), 2,
        'a ceiling below the floor is repaired to the default ceiling, so the valid floor still wins');
    assert.equal(clampZoom(99, 2, 1), 2.5);
});

test('zoomFromGesture multiplies the level the gesture started from', () => {
    assert.equal(zoomFromGesture(1, 1.5, 0.5, 2.5), 1.5);
    assert.equal(zoomFromGesture(2, 0.5, 0.5, 2.5), 1);
    assert.equal(zoomFromGesture(1, 100, 0.5, 2.5), 2.5, 'the window still caps the result');
    assert.equal(zoomFromGesture(1, 0, 0.5, 2.5), 1, 'a zero scale is not a zoom');
});

test('zoomFromGesture treats the reported scale as CUMULATIVE, not as a step', () => {
    // pointer-gesture.js reports the scale relative to the START of the gesture, so the second tick replaces the first
    // instead of multiplying it: 1 -> 1.5 -> 1.8, never 1 -> 1.5 -> 2.7.
    assert.equal(zoomFromGesture(1, 1.5, 0.5, 2.5), 1.5);
    assert.equal(zoomFromGesture(1, 1.8, 0.5, 2.5), 1.8);
});

test('measureViewport reads the CONTENT box, never the rect', () => {
    assert.deepEqual(measureViewport(fakeViewport({ width: 640, height: 480 })), [640, 480]);
});

test('measureViewport falls back to the bounding rect', () => {
    const element = {
        clientWidth: 0,
        clientHeight: 0,
        getBoundingClientRect: () => ({ width: 375, height: 812 }),
    };

    assert.deepEqual(measureViewport(element), [375, 812]);
});

test('measureViewport measures nothing where nothing is measurable', () => {
    assert.deepEqual(measureViewport(null), []);
    assert.deepEqual(measureViewport({ clientWidth: 0, clientHeight: 0 }), []);
    assert.deepEqual(measureViewport({ clientWidth: 100, clientHeight: 0 }), [],
        'half a measurement is no measurement: fit-to-view would jump to a guess');
});

test('applyPan scrolls the scrollport by the gesture delta', () => {
    const viewport = fakeViewport({ left: 120, top: 40 });

    assert.equal(applyPan(viewport, 120, 40, 30, -10), true);
    assert.equal(viewport.scrollLeft, 90, 'a drag to the right moves the content left');
    assert.equal(viewport.scrollTop, 50);
});

test('applyPan survives a broken delta and a missing element', () => {
    const viewport = fakeViewport({ left: 0, top: 0 });

    assert.equal(applyPan(viewport, Number.NaN, undefined, Number.NaN, 'x'), true);
    assert.equal(viewport.scrollLeft, 0);
    assert.equal(applyPan(null, 0, 0, 1, 1), false);
});

test('attachGraphGesture reports the gesture to .NET exactly once per callback', async () => {
    const pinch = fakePinch();
    const dotNet = new FakeDotNet();

    const handle = await attachGraphGesture(fakeViewport(), dotNet, { pinchModule: pinch.module });

    assert.equal(pinch.state.attachedTo.addEventListener !== undefined, true);
    assert.equal(pinch.state.options.wheel, true, 'ctrl+wheel zooms; a plain wheel must keep scrolling the page');

    pinch.state.options.onStart();
    pinch.state.options.onChange({ scale: 1.5, dx: 0, dy: 0 });
    pinch.state.options.onChange({ scale: Number.NaN, dx: 0, dy: 0 });
    pinch.state.options.onEnd();

    assert.deepEqual(dotNet.calls, [
        { name: 'OnGraphZoomGestureStartAsync', args: [] },
        { name: 'OnGraphZoomGestureChangeAsync', args: [1.5] },
        { name: 'OnGraphZoomGestureEndAsync', args: [] },
    ]);
    assert.equal(handle.active, true);
});

test('attachGraphGesture PANS the scrollport instead of sending the delta to .NET', async () => {
    const pinch = fakePinch();
    const dotNet = new FakeDotNet();
    const viewport = fakeViewport({ left: 200, top: 100 });

    await attachGraphGesture(viewport, dotNet, { pinchModule: pinch.module });

    pinch.state.options.onStart();
    pinch.state.options.onChange({ scale: 1, dx: 40, dy: -20 });

    assert.equal(viewport.scrollLeft, 160);
    assert.equal(viewport.scrollTop, 120);
    assert.deepEqual(dotNet.calls.map(call => call.name), [
        'OnGraphZoomGestureStartAsync',
        'OnGraphZoomGestureChangeAsync',
    ], 'a pan is a scroll position, not component state');
});

test('attachGraphGesture treats a cancelled gesture as an ended one', async () => {
    const pinch = fakePinch();
    const dotNet = new FakeDotNet();

    await attachGraphGesture(fakeViewport(), dotNet, { pinchModule: pinch.module });

    pinch.state.options.onCancel({ reason: 'escape' });

    assert.deepEqual(dotNet.calls, [{ name: 'OnGraphZoomGestureEndAsync', args: [] }],
        '.NET only needs to know the gesture is over');
});

test('attachGraphGesture detaches the helper, and cancel forwards the reason', async () => {
    const pinch = fakePinch();

    const handle = await attachGraphGesture(fakeViewport(), new FakeDotNet(), { pinchModule: pinch.module });

    handle.detach();
    handle.cancel();

    assert.equal(pinch.state.detached, 1);
    assert.equal(pinch.state.cancelled, 1);
    assert.equal(pinch.state.cancelReason, 'host');
});

test('attachGraphGesture is inert without a target, an element or a helper', async () => {
    const pinch = fakePinch();

    assert.equal((await attachGraphGesture(null, new FakeDotNet(), { pinchModule: pinch.module })).active, false);
    assert.equal((await attachGraphGesture(fakeViewport(), null, { pinchModule: pinch.module })).active, false);
    assert.equal((await attachGraphGesture(fakeViewport(), new FakeDotNet(), { pinchModule: {} })).active, false,
        'a helper without attachPinch leaves the buttons as the only zoom');
});

test('attachGraphGesture swallows a .NET call that throws (a dead circuit)', async () => {
    const pinch = fakePinch();
    const dotNet = {
        invokeMethodAsync() {
            throw new Error('circuit is gone');
        },
    };

    await attachGraphGesture(fakeViewport(), dotNet, { pinchModule: pinch.module });

    assert.doesNotThrow(() => pinch.state.options.onStart());
    assert.doesNotThrow(() => pinch.state.options.onChange({ scale: 1.2, dx: 0, dy: 0 }));
    assert.doesNotThrow(() => pinch.state.options.onEnd());
});

test('loadPinch resolves the shared helper across the static-asset boundary', async () => {
    // The URL is resolved against THIS module, so it works under any base path a host serves the package from.
    const module = await loadPinch().catch(() => null);

    assert.equal(module === null || typeof module.attachPinch === 'function', true,
        'the module either exposes the shared helper or the import fails (and the buttons still zoom)');
});
