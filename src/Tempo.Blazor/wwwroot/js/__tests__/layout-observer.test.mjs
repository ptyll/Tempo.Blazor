import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyWidth, observe, disconnect, __resetForTests } from '../layout-observer.js';

// The numbers are the TmBreakpoints contract: Mobile < 640, Tablet < 1024, Desktop otherwise.
// 768 is a named breakpoint for CSS, not a layout-mode boundary.

test('classifyWidth maps the documented boundaries', () => {
    assert.equal(classifyWidth(0), 'mobile');
    assert.equal(classifyWidth(639), 'mobile');
    assert.equal(classifyWidth(640), 'tablet');
    assert.equal(classifyWidth(1023), 'tablet');
    assert.equal(classifyWidth(1024), 'desktop');
    assert.equal(classifyWidth(1440), 'desktop');
});

test('classifyWidth rejects a non-finite width instead of guessing a mode', () => {
    assert.throws(() => classifyWidth(Number.NaN), /finite/);
    assert.throws(() => classifyWidth(-1), /finite/);
});

function installFakeResizeObserver() {
    const instances = [];
    class FakeResizeObserver {
        constructor(callback) {
            this.callback = callback;
            this.observed = [];
            this.disconnected = false;
            instances.push(this);
        }

        observe(element) {
            this.observed.push(element);
        }

        disconnect() {
            this.disconnected = true;
        }

        emit(width) {
            this.callback([{ contentRect: { width }, target: this.observed[0] }]);
        }
    }

    globalThis.ResizeObserver = FakeResizeObserver;
    return instances;
}

function element(id = 'root') {
    return { id, dataset: {} };
}

function dotNet() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(name, ...args) {
            calls.push({ name, args });
            return Promise.resolve();
        },
    };
}

test('observe reports the initial width and a later width only when the mode changes', async () => {
    __resetForTests();
    const instances = installFakeResizeObserver();
    const root = element('dash');
    const dotnet = dotNet();

    await observe(root, dotnet, 'dash', { initialWidth: 1440 });

    assert.equal(instances.length, 1);
    assert.deepEqual(instances[0].observed, [root]);
    assert.deepEqual(dotnet.calls, [
        { name: 'OnLayoutModeChanged', args: ['desktop'] },
    ]);

    instances[0].emit(1100);
    instances[0].emit(1024);
    assert.equal(dotnet.calls.length, 1, 'a resize inside the same mode must not call .NET');

    instances[0].emit(800);
    assert.deepEqual(dotnet.calls.at(-1), { name: 'OnLayoutModeChanged', args: ['tablet'] });

    instances[0].emit(390);
    assert.deepEqual(dotnet.calls.at(-1), { name: 'OnLayoutModeChanged', args: ['mobile'] });
    assert.equal(dotnet.calls.length, 3);
});

test('observe does not write the mode onto the element — Blazor owns data-layout', async () => {
    __resetForTests();
    installFakeResizeObserver();
    const root = element('pane');

    await observe(root, dotNet(), 'pane', { initialWidth: 700, breakpoints: { sm: 640, lg: 1024 } });

    assert.equal(root.dataset.layout, undefined);
});

test('observe classifies with the breakpoints the caller passed, not with literals of its own', async () => {
    __resetForTests();
    const instances = installFakeResizeObserver();
    const dotnet = dotNet();

    await observe(element('pane'), dotnet, 'pane', {
        initialWidth: 500,
        breakpoints: { sm: 400, lg: 800 },
    });

    assert.deepEqual(dotnet.calls.at(-1), { name: 'OnLayoutModeChanged', args: ['tablet'] },
        '500 is below the caller lg and at or above the caller sm');
    instances[0].emit(399);
    assert.deepEqual(dotnet.calls.at(-1), { name: 'OnLayoutModeChanged', args: ['mobile'] });
});

test('disconnect releases the observer and ignores a later resize', async () => {
    __resetForTests();
    const instances = installFakeResizeObserver();
    const dotnet = dotNet();
    await observe(element('pane'), dotnet, 'pane', { initialWidth: 1440 });

    disconnect('pane');

    assert.equal(instances[0].disconnected, true);
    instances[0].emit(100);
    assert.equal(dotnet.calls.length, 1, 'a disconnected observer must not report');
});

test('observe replaces a registration that reuses the same id', async () => {
    __resetForTests();
    const instances = installFakeResizeObserver();

    await observe(element('pane'), dotNet(), 'pane', { initialWidth: 1440 });
    await observe(element('pane'), dotNet(), 'pane', { initialWidth: 390 });

    assert.equal(instances[0].disconnected, true, 'the stale observer must be released');
    assert.equal(instances[1].disconnected, false);
});

test('observe refuses a missing root instead of observing the document', async () => {
    __resetForTests();
    installFakeResizeObserver();

    await assert.rejects(observe(null, dotNet(), 'pane'), /root/);
});
