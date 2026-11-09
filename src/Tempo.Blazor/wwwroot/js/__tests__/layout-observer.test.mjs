import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyWidth, observe, disconnect, __resetForTests } from '../layout-observer.js';

const breakpoints = { sm: 640, md: 768, lg: 1024 };

// The numbers are the TmBreakpoints contract: Mobile < 640, Tablet < 1024, Desktop otherwise.
// 768 is a named breakpoint for CSS, not a layout-mode boundary.

test('classifyWidth maps the documented boundaries', () => {
    assert.equal(classifyWidth(0, breakpoints), 'mobile');
    assert.equal(classifyWidth(639, breakpoints), 'mobile');
    assert.equal(classifyWidth(640, breakpoints), 'tablet');
    assert.equal(classifyWidth(1023, breakpoints), 'tablet');
    assert.equal(classifyWidth(1024, breakpoints), 'desktop');
    assert.equal(classifyWidth(1440, breakpoints), 'desktop');
});

test('classifyWidth rejects a non-finite width instead of guessing a mode', () => {
    assert.throws(() => classifyWidth(Number.NaN, breakpoints), /finite/);
    assert.throws(() => classifyWidth(-1, breakpoints), /finite/);
});

function installFakeResizeObserver() {
    const instances = [];
    class FakeResizeObserver {
        constructor(callback) {
            this.callback = callback;
            this.observed = [];
            this.unobserved = [];
            this.disconnected = false;
            instances.push(this);
        }

        observe(element) {
            this.observed.push(element);
        }

        unobserve(element) {
            this.unobserved.push(element);
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

    await observe(root, dotnet, 'dash', { initialWidth: 1440, breakpoints });

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
    await observe(element('pane'), dotnet, 'pane', { initialWidth: 1440, breakpoints });

    disconnect('pane');

    assert.deepEqual(instances[0].unobserved.map(item => item.id), ['pane']);
    instances[0].emit(100);
    assert.equal(dotnet.calls.length, 1, 'a disconnected observer must not report');
});

test('observe replaces a registration that reuses the same id', async () => {
    __resetForTests();
    const instances = installFakeResizeObserver();

    await observe(element('pane'), dotNet(), 'pane', { initialWidth: 1440, breakpoints });
    await observe(element('pane'), dotNet(), 'pane', { initialWidth: 390, breakpoints });

    assert.equal(instances[0].unobserved.length, 1, 'the stale registration must be unobserved');
});

test('observe refuses a missing root instead of observing the document', async () => {
    __resetForTests();
    installFakeResizeObserver();

    await assert.rejects(observe(null, dotNet(), 'pane'), /root/);
});

// Per-instance thresholds (F6 round 1, Q3): the observer classifies with whatever pair C# feeds it,
// so an editor shell can go desktop from 1200 and mobile below 768 without a second code path.
test('classifyWidth honours per-instance thresholds', () => {
    const email = { sm: 768, lg: 1200 };
    assert.equal(classifyWidth(767, email), 'mobile');
    assert.equal(classifyWidth(768, email), 'tablet');
    assert.equal(classifyWidth(1199, email), 'tablet');
    assert.equal(classifyWidth(1200, email), 'desktop');
});

test('observe re-registered with new thresholds reclassifies the same width', async () => {
    __resetForTests();
    installFakeResizeObserver();
    const root = element('shell');
    const dotnet = dotNet();

    await observe(root, dotnet, 'shell', { initialWidth: 886, breakpoints });
    await observe(root, dotnet, 'shell', { initialWidth: 886, breakpoints: { sm: 768, lg: 1200 } });
    await observe(root, dotnet, 'shell', { initialWidth: 886, breakpoints: { sm: 900, lg: 1200 } });

    assert.deepEqual(dotnet.calls.map(call => call.args[0]), ['tablet', 'tablet', 'mobile']);
});

// A 0-size content box is transient (viewport re-emulation during a fullPage screenshot, print
// preview, hidden→shown), not a real mobile container: classifying it would remount the editor
// canvas and lose every open piece of UI state.
// Pointer kind is a media-query signal, not a pointermove stream: one report when the query
// matches, and another only when it flips. A missing matchMedia stays silent (fine pointer).
test('observe reports a coarse pointer once and again only when the query flips', async () => {
    __resetForTests();
    installFakeResizeObserver();
    const previousMedia = globalThis.matchMedia;
    const previousWindow = globalThis.window;
    const listeners = new Map();
    const query = {
        matches: true,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
    };
    const moves = [];
    globalThis.matchMedia = (q) => (q === '(pointer: coarse)'
        ? query
        : { matches: false, addEventListener() {}, removeEventListener() {} });
    globalThis.window = {
        addEventListener(type, fn) { if (type === 'pointermove') moves.push(fn); },
        removeEventListener() {},
        innerWidth: 1440,
    };
    try {
        const dotnet = dotNet();
        const root = element('pane');
        await observe(root, dotnet, 'pane', { initialWidth: 1440, breakpoints });

        assert.deepEqual(
            dotnet.calls.filter(call => call.name === 'OnPointerKindChanged'),
            [{ name: 'OnPointerKindChanged', args: [true] }],
            'a coarse primary pointer is reported once, not on every observation');

        query.matches = true;
        listeners.get('change')?.();
        assert.equal(
            dotnet.calls.filter(call => call.name === 'OnPointerKindChanged').length,
            1,
            'an unchanged query must not call .NET');

        query.matches = false;
        listeners.get('change')?.();
        assert.deepEqual(
            dotnet.calls.filter(call => call.name === 'OnPointerKindChanged').at(-1),
            { name: 'OnPointerKindChanged', args: [false] });
        assert.equal(moves.length, 0, 'pointer kind is not sampled from pointermove');

        disconnect('pane');
        query.matches = true;
        listeners.get('change')?.();
        assert.equal(
            dotnet.calls.filter(call => call.name === 'OnPointerKindChanged').length,
            2,
            'disconnect removes the media-query listener');
    } finally {
        globalThis.matchMedia = previousMedia;
        globalThis.window = previousWindow;
    }
});

test('readPointer treats a missing or throwing matchMedia as a fine pointer', async () => {
    const { readPointer } = await import('../layout-observer.js');
    assert.equal(readPointer(undefined), false);
    assert.equal(readPointer(() => { throw new Error('denied'); }), false);
    assert.equal(readPointer(q => ({ matches: q === '(pointer: coarse)' })), true);
});

test('a degenerate 0-width record keeps the last resolved mode', async () => {
    __resetForTests();
    const instances = installFakeResizeObserver();
    const dotnet = dotNet();

    await observe(element('shell'), dotnet, 'shell', { initialWidth: 1440, breakpoints });

    instances[0].emit(0);
    instances[0].emit(0.5);
    assert.deepEqual(dotnet.calls, [
        { name: 'OnLayoutModeChanged', args: ['desktop'] },
    ], 'degenerate widths must not report a mode');

    instances[0].emit(390);
    assert.deepEqual(dotnet.calls.at(-1), { name: 'OnLayoutModeChanged', args: ['mobile'] },
        'a real width still reports after the transient');
});