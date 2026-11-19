// TmChart.razor.js — the plot-box observer of the opt-in Responsive TmChart (plan 98c80bc3, phase 10b).
// The REAL ES module is imported with a fake ResizeObserver; the module's only shared state is a WeakMap
// keyed by the element, so fresh elements keep the tests isolated.
import test from 'node:test';
import assert from 'node:assert/strict';
import { pathToFileURL, fileURLToPath } from 'node:url';
import path from 'node:path';

const observers = [];
class FakeResizeObserver {
    constructor(callback) {
        this.callback = callback;
        this.observed = [];
        this.disconnected = false;
        observers.push(this);
    }
    observe(element) { this.observed.push(element); }
    unobserve(element) { this.observed = this.observed.filter((e) => e !== element); }
    disconnect() { this.disconnected = true; this.observed = []; }
    emit(element, width, height) {
        this.callback([{ target: element, contentRect: { width, height } }]);
    }
}
globalThis.ResizeObserver = FakeResizeObserver;

const here = path.dirname(fileURLToPath(import.meta.url));
const mod = await import(pathToFileURL(path.join(here, '..', 'TmChart.razor.js')).href);

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync(name, ...args) { calls.push({ name, args }); return Promise.resolve(); } };
}
const element = () => ({ clientWidth: 0 });

test('observe reports the plot box through OnPlotResized, rounded to whole pixels', () => {
    const root = element();
    const host = dotnet();
    mod.observe(root, host);

    const ro = observers.at(-1);
    assert.deepEqual(ro.observed, [root]);
    ro.emit(root, 812.4, 301.6);
    assert.deepEqual(host.calls, [{ name: 'OnPlotResized', args: [812, 302] }]);
    mod.release(root);
});

test('an unchanged size is not reported again (sub-pixel jitter cannot re-render the chart)', () => {
    const root = element();
    const host = dotnet();
    mod.observe(root, host);
    const ro = observers.at(-1);

    ro.emit(root, 800, 300);
    ro.emit(root, 800.3, 299.8);
    ro.emit(root, 801, 300);

    assert.deepEqual(host.calls.map((c) => c.args), [[800, 300], [801, 300]]);
    mod.release(root);
});

test('a degenerate box (hidden tab, display:none, mid-emulation 0-size) keeps the last good size', () => {
    const root = element();
    const host = dotnet();
    mod.observe(root, host);
    const ro = observers.at(-1);

    ro.emit(root, 700, 260);
    ro.emit(root, 0, 0);
    ro.emit(root, 0, 260);
    ro.emit(root, Number.NaN, 260);

    assert.equal(host.calls.length, 1);
    mod.release(root);
});

test('release disconnects the observer and ignores later resizes', () => {
    const root = element();
    const host = dotnet();
    mod.observe(root, host);
    const ro = observers.at(-1);

    mod.release(root);
    assert.equal(ro.disconnected, true);
    ro.emit(root, 900, 300);
    assert.equal(host.calls.length, 0);
});

test('release is idempotent and tolerates an element that was never observed', () => {
    const root = element();
    assert.doesNotThrow(() => mod.release(root));
    assert.doesNotThrow(() => mod.release(null));
    mod.observe(root, dotnet());
    mod.release(root);
    assert.doesNotThrow(() => mod.release(root));
});

test('observing the same element again replaces the previous registration', () => {
    const root = element();
    const first = dotnet();
    const second = dotnet();
    mod.observe(root, first);
    const firstObserver = observers.at(-1);
    mod.observe(root, second);
    const secondObserver = observers.at(-1);

    assert.equal(firstObserver.disconnected, true);
    secondObserver.emit(root, 640, 240);
    assert.equal(first.calls.length, 0);
    assert.equal(second.calls.length, 1);
    mod.release(root);
});

test('observe without ResizeObserver or without an element is a quiet no-op', () => {
    assert.doesNotThrow(() => mod.observe(null, dotnet()));
    const saved = globalThis.ResizeObserver;
    globalThis.ResizeObserver = undefined;
    try {
        const host = dotnet();
        assert.doesNotThrow(() => mod.observe(element(), host));
        assert.equal(host.calls.length, 0);
    } finally {
        globalThis.ResizeObserver = saved;
    }
});

test('a rejected invoke (circuit already gone) never becomes an unhandled rejection', async () => {
    const root = element();
    const host = { invokeMethodAsync: () => Promise.reject(new Error('disposed')) };
    mod.observe(root, host);
    observers.at(-1).emit(root, 500, 200);
    await new Promise((resolve) => setTimeout(resolve, 0));
    mod.release(root);
});
