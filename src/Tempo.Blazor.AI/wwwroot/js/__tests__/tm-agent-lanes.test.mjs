// tm-agent-lanes.js tests — the zoom gesture bridge of TmAgentLanes.
//
// The component owns the zoom level; the module only reports a gesture's RELATIVE scale and its start/end, because the
// shared pointer-gesture.js helper owns pinch and ctrl+wheel. These tests pin the contract that matters: the scale is
// clamped into the component's window, the gesture is attached with `wheel: true` (so plain wheel keeps scrolling the
// page), every callback reaches .NET exactly once, and detach releases the helper.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attachLanesZoom,
    clampPixelsPerHour,
    loadPinch,
    measureWidth,
    scaleFromGesture,
} from '../tm-agent-lanes.js';

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

test('clampPixelsPerHour keeps the scale inside its window', () => {
    assert.equal(clampPixelsPerHour(120, 24, 2400), 120);
    assert.equal(clampPixelsPerHour(1, 24, 2400), 24);
    assert.equal(clampPixelsPerHour(999999, 24, 2400), 2400);
});

test('clampPixelsPerHour falls back to the floor for nonsense', () => {
    assert.equal(clampPixelsPerHour(0, 24, 2400), 24);
    assert.equal(clampPixelsPerHour(-5, 24, 2400), 24);
    assert.equal(clampPixelsPerHour(Number.NaN, 24, 2400), 24);
    assert.equal(clampPixelsPerHour(undefined, 24, 2400), 24);
});

test('clampPixelsPerHour repairs a broken window', () => {
    assert.equal(clampPixelsPerHour(120, 0, 0), 120, 'a non-positive floor uses the default floor');
    assert.equal(clampPixelsPerHour(120, 100, 10), 120, 'a ceiling below the floor uses the default ceiling');
    assert.equal(clampPixelsPerHour(5000, 100, 10), 2400);
});

test('scaleFromGesture multiplies the level the gesture started from', () => {
    assert.equal(scaleFromGesture(120, 2, 24, 2400), 240);
    assert.equal(scaleFromGesture(120, 0.5, 24, 2400), 60);
    assert.equal(scaleFromGesture(100, 100, 24, 2400), 2400, 'the window still caps the result');
    assert.equal(scaleFromGesture(120, 0, 24, 2400), 120, 'a zero scale is not a zoom');
});

test('attachLanesZoom reports the gesture to .NET exactly once per callback', async () => {
    const pinch = fakePinch();
    const dotNet = new FakeDotNet();

    const handle = await attachLanesZoom({ addEventListener() {} }, dotNet, { pinchModule: pinch.module });

    assert.equal(pinch.state.attachedTo.addEventListener !== undefined, true);
    assert.equal(pinch.state.options.wheel, true, 'ctrl+wheel zooms; a plain wheel must keep scrolling the page');

    pinch.state.options.onStart();
    pinch.state.options.onChange({ scale: 1.5 });
    pinch.state.options.onChange({ scale: Number.NaN });
    pinch.state.options.onEnd();

    assert.deepEqual(dotNet.calls, [
        { name: 'OnLanesZoomGestureStartAsync', args: [] },
        { name: 'OnLanesZoomGestureChangeAsync', args: [1.5] },
        { name: 'OnLanesZoomGestureEndAsync', args: [] },
    ]);

    handle.detach();
    assert.equal(pinch.state.detached, 1);
});

test('attachLanesZoom reports a cancelled gesture as finished', async () => {
    const pinch = fakePinch();
    const dotNet = new FakeDotNet();

    await attachLanesZoom({ addEventListener() {} }, dotNet, { pinchModule: pinch.module });
    pinch.state.options.onCancel({ reason: 'Escape' });

    assert.deepEqual(dotNet.calls, [{ name: 'OnLanesZoomGestureEndAsync', args: [] }]);
});

test('attachLanesZoom survives a dead circuit', async () => {
    const pinch = fakePinch();
    const dotNet = {
        invokeMethodAsync() {
            throw new Error('disposed');
        },
    };

    const handle = await attachLanesZoom({ addEventListener() {} }, dotNet, { pinchModule: pinch.module });

    assert.doesNotThrow(() => pinch.state.options.onChange({ scale: 2 }));
    handle.cancel();
    assert.equal(pinch.state.cancelled, 1);
});

test('attachLanesZoom is inert where it cannot attach', async () => {
    assert.deepEqual(Object.keys(await attachLanesZoom(null, new FakeDotNet())), ['detach', 'cancel', 'active']);
    assert.deepEqual(Object.keys(await attachLanesZoom({}, null)), ['detach', 'cancel', 'active']);

    const inert = await attachLanesZoom({ addEventListener() {} }, new FakeDotNet(), { pinchModule: {} });
    assert.doesNotThrow(() => inert.detach());
});

test('attachLanesZoom tolerates a missing helper at runtime', async () => {
    const handle = await attachLanesZoom(
        { addEventListener() {} },
        new FakeDotNet(),
        {
            pinchModule: () => {
                throw new Error('no pinch');
            },
        });

    assert.doesNotThrow(() => handle.detach());
});

test('measureWidth prefers the CONTENT box of the scrollport', () => {
    assert.equal(measureWidth(null), 0);
    assert.equal(measureWidth({}), 0);
    assert.equal(measureWidth({ getBoundingClientRect: () => ({ width: 0 }) }), 0);
    assert.equal(
        measureWidth({ clientWidth: 638, getBoundingClientRect: () => ({ width: 640 }) }),
        638,
        'the border is not room for content: a rect width leaves a scrollbar of overflow');
    assert.equal(
        measureWidth({ getBoundingClientRect: () => ({ width: 640 }) }),
        640,
        'a detached element with no client box falls back to the rect');
});

test('loadPinch targets the core helper across the package boundary', async () => {
    // Node does not serve the package's static assets, so the import rejects here; what matters is the URL it tries
    // — the CORE `_content/Tempo.Blazor/js/pointer-gesture.js`, never a package-local second implementation.
    await assert.rejects(() => loadPinch(), /pointer-gesture\.js/);
});

test('attachLanesZoom stays inert when the shared helper cannot be loaded', async () => {
    const handle = await attachLanesZoom({ addEventListener() {} }, new FakeDotNet());

    assert.doesNotThrow(() => handle.detach());
    assert.equal(handle.active, false);
});
