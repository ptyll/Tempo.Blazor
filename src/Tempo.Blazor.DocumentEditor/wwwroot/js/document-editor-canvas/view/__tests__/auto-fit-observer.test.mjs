import test from 'node:test';
import assert from 'node:assert/strict';
import { createAutoFitObserver } from '../auto-fit-observer.mjs';

// ── the observer wrapper ────────────────────────────────────────────────────

function createHarness() {
    const observers = [];
    class FakeResizeObserver {
        constructor(callback) {
            this.callback = callback;
            this.targets = [];
            this.disconnected = false;
            observers.push(this);
        }

        observe(target) {
            this.targets.push(target);
        }

        disconnect() {
            this.disconnected = true;
            this.targets = [];
        }

        emit(width) {
            this.callback([{ contentRect: { width } }]);
        }
    }

    const frames = [];
    let nextFrame = 1;
    const view = {
        requestAnimationFrame: callback => {
            const id = nextFrame++;
            frames.push({ id, callback });
            return id;
        },
        cancelAnimationFrame: id => {
            const index = frames.findIndex(frame => frame.id === id);
            if (index >= 0) {
                frames.splice(index, 1);
            }
        },
    };
    const flushFrames = () => {
        const pending = frames.splice(0, frames.length);
        pending.forEach(frame => frame.callback());
    };

    return { observers, FakeResizeObserver, view, frames, flushFrames };
}

test('observer reports a width change once per animation frame', () => {
    const h = createHarness();
    const widths = [];
    const target = { clientWidth: 1000 };
    const observer = createAutoFitObserver({
        view: h.view,
        ResizeObserver: h.FakeResizeObserver,
        resolveTarget: () => target,
        onResize: width => widths.push(width),
    });

    assert.equal(h.observers.length, 1);
    assert.deepEqual(h.observers[0].targets, [target]);

    h.observers[0].emit(1000.4);
    h.observers[0].emit(990);
    h.observers[0].emit(900);
    assert.equal(h.frames.length, 1, 'three reports within a frame schedule ONE fit');
    h.flushFrames();
    assert.deepEqual(widths, [900], 'the fit sees the latest width');

    h.observers[0].emit(900.2);
    assert.equal(h.frames.length, 0, 'a sub-pixel change of the rounded width is not a change');
    observer.destroy();
});

test('observer ignores a height-only change and re-fits when the width really changes', () => {
    const h = createHarness();
    const widths = [];
    createAutoFitObserver({
        view: h.view,
        ResizeObserver: h.FakeResizeObserver,
        resolveTarget: () => ({}),
        onResize: width => widths.push(width),
    });

    h.observers[0].emit(800);
    h.flushFrames();
    h.observers[0].emit(800);
    h.flushFrames();
    h.observers[0].emit(780);
    h.flushFrames();
    assert.deepEqual(widths, [800, 780]);
});

test('destroy disconnects the observer and cancels a pending frame', () => {
    const h = createHarness();
    const widths = [];
    const observer = createAutoFitObserver({
        view: h.view,
        ResizeObserver: h.FakeResizeObserver,
        resolveTarget: () => ({}),
        onResize: width => widths.push(width),
    });

    h.observers[0].emit(640);
    assert.equal(h.frames.length, 1);
    observer.destroy();
    assert.equal(h.observers[0].disconnected, true);
    assert.equal(h.frames.length, 0, 'the pending frame is cancelled');
    h.flushFrames();
    assert.deepEqual(widths, []);

    observer.destroy();
});

test('observer without a scroll viewport or without ResizeObserver is an inert no-op', () => {
    const h = createHarness();
    const noTarget = createAutoFitObserver({ view: h.view, ResizeObserver: h.FakeResizeObserver, resolveTarget: () => null, onResize: () => {} });
    assert.equal(h.observers.length, 0, 'the content-sized mount is never observed');
    noTarget.destroy();

    const noApi = createAutoFitObserver({ view: {}, ResizeObserver: null, resolveTarget: () => ({}), onResize: () => {} });
    noApi.destroy();
});

test('the observer exposes the element it watches, an inert one exposes none', () => {
    const h = createHarness();
    const target = {};
    const observer = createAutoFitObserver({ view: h.view, ResizeObserver: h.FakeResizeObserver, resolveTarget: () => target, onResize: () => {} });
    assert.equal(observer.target, target);
    observer.destroy();

    const inert = createAutoFitObserver({ view: h.view, ResizeObserver: h.FakeResizeObserver, resolveTarget: () => null, onResize: () => {} });
    assert.equal(inert.target, null);
});
