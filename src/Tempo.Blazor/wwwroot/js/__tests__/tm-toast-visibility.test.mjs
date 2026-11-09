// tm-toast-visibility.js tests with hand-rolled DOM stubs (same approach as tm-otp-input.test.mjs):
// no jsdom; the stub implements only addEventListener/removeEventListener/hidden. Covers the
// contract the bUnit tests cannot reach: the visibilitychange listener reports document.hidden to
// Blazor, watch() is per-instance (multi-container, re-watch replaces, unwatch detaches one),
// a rejected invokeMethodAsync never surfaces as an unhandled rejection, watch() RETURNS the
// current hidden flag and reports synchronously when the document starts hidden.
import test from 'node:test';
import assert from 'node:assert/strict';
import { watch, unwatch } from '../tm-toast-visibility.js';

function makeDoc(hidden = false) {
    const listeners = new Map(); // type -> Set<fn> (a Map<string, fn> would hide stacked listeners)
    const ensure = type => {
        if (!listeners.has(type)) listeners.set(type, new Set());
        return listeners.get(type);
    };
    return {
        hidden,
        addEventListener(type, fn) { ensure(type).add(fn); },
        removeEventListener(type, fn) { ensure(type).delete(fn); },
        listenerCount() { return [...listeners.values()].reduce((n, s) => n + s.size, 0); },
        flip(value) {
            this.hidden = value;
            for (const fn of ensure('visibilitychange')) fn({ type: 'visibilitychange' });
        },
    };
}

function dotnetStub({ reject = false } = {}) {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(method, ...args) {
            calls.push({ method, args });
            return reject ? Promise.reject(new Error('circuit gone')) : Promise.resolve();
        },
    };
}

test('watch reports the hidden flag on every visibility flip', () => {
    const doc = makeDoc(false);
    const dotNet = dotnetStub();

    watch(dotNet, doc);
    doc.flip(true);
    doc.flip(false);
    doc.flip(true);

    assert.deepEqual(dotNet.calls, [
        { method: 'OnDocumentVisibilityChanged', args: [true] },
        { method: 'OnDocumentVisibilityChanged', args: [false] },
        { method: 'OnDocumentVisibilityChanged', args: [true] },
    ]);
});

test('re-watch of the same handle replaces the listener instead of stacking callbacks', () => {
    const doc = makeDoc(false);
    const first = dotnetStub();
    const second = dotnetStub();

    watch(first, doc);
    watch(first, doc); // same handle: the first wiring must be gone
    assert.equal(doc.listenerCount(), 1, 'exactly one listener is attached per handle');
    doc.flip(true);

    assert.equal(first.calls.length, 1);
    assert.deepEqual(second.calls, [], 'the superseded reference was never watched');
});

test('two handles on one document watch independently; unwatch detaches exactly one', () => {
    const doc = makeDoc(false);
    const a = dotnetStub();
    const b = dotnetStub();

    watch(a, doc);
    watch(b, doc);
    assert.equal(doc.listenerCount(), 2, 'per-instance state: two watches, two listeners');

    unwatch(a);
    assert.equal(doc.listenerCount(), 1);
    doc.flip(true);

    assert.deepEqual(a.calls, [], 'the unwatched instance is silent');
    assert.equal(b.calls.length, 1, 'the remaining instance still reports');
});

test('unwatch without a prior watch is a no-op', () => {
    const doc = makeDoc(false);
    unwatch({});
    assert.equal(doc.listenerCount(), 0);
});

test('a rejected invokeMethodAsync never surfaces as an unhandled rejection', async () => {
    const doc = makeDoc(false);
    const dotNet = dotnetStub({ reject: true });

    watch(dotNet, doc);
    doc.flip(true);
    await new Promise(resolve => setTimeout(resolve, 0)); // let the .catch(() => {}) run

    assert.equal(dotNet.calls.length, 1, 'the report was attempted and swallowed');
});

test('watch returns the current hidden flag and reports immediately when starting hidden', () => {
    const doc = makeDoc(true);
    const dotNet = dotnetStub();

    const hidden = watch(dotNet, doc);

    assert.equal(hidden, true, 'watch returns document.hidden');
    assert.deepEqual(dotNet.calls, [
        { method: 'OnDocumentVisibilityChanged', args: [true] },
    ], 'no flip event exists for an already-hidden document — PauseAll must happen immediately');
});

test('watch of a visible document returns false without an immediate report', () => {
    const doc = makeDoc(false);
    const dotNet = dotnetStub();

    const hidden = watch(dotNet, doc);

    assert.equal(hidden, false);
    assert.deepEqual(dotNet.calls, [], 'nothing changed: no report until the first flip');
});
