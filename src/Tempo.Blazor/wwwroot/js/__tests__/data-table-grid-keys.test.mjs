// data-table.js grid keyboard helpers. Two contracts live here:
// 1. attachGridKeys cancels the browser default for Ctrl+Home / Ctrl+End — C# treats the chords as
//    grid navigation (first / last row), but the generic Ctrl guard would otherwise let the
//    document scroll to the very top or bottom before the nearest-edge scroll pulls it back.
// 2. scrollGridToIndex focuses the target cell itself: <Virtualize> paints the rows a few frames
//    after the scroll, so the lookup retries inside requestAnimationFrame until the cell exists.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const dataTableUrl = pathToFileURL(path.resolve(here, '../data-table.js')).href;

let tmDataTable;

async function boot() {
    globalThis.document = { currentScript: { src: dataTableUrl }, readyState: 'complete' };
    globalThis.window = {};
    await import(`../data-table.js?${Math.random()}`);
    tmDataTable = globalThis.window.tmDataTable;
}

function table() {
    const listeners = new Map();
    return {
        listeners,
        addEventListener: (type, handler) => listeners.set(type, handler),
        removeEventListener: type => listeners.delete(type),
    };
}

function keydown(target, init) {
    const event = { key: 'ArrowDown', altKey: false, ctrlKey: false, metaKey: false, target, defaultPrevented: false };
    Object.assign(event, init);
    event.preventDefault = () => { event.defaultPrevented = true; };
    return event;
}

function gridCellTarget() {
    return { closest: selector => (selector.includes('gridcell') ? {} : null) };
}

test('Ctrl+Home and Ctrl+End on a grid cell are cancelled', async () => {
    await boot();
    const t = table();
    tmDataTable.attachGridKeys(t);
    const home = keydown(gridCellTarget(), { key: 'Home', ctrlKey: true });
    const end = keydown(gridCellTarget(), { key: 'End', ctrlKey: true });
    t.listeners.get('keydown')(home);
    t.listeners.get('keydown')(end);
    assert.equal(home.defaultPrevented, true);
    assert.equal(end.defaultPrevented, true);
});

test('Ctrl+ArrowDown, Alt+Home and Meta+Home keep the browser default', async () => {
    await boot();
    const t = table();
    tmDataTable.attachGridKeys(t);
    const cases = [
        keydown(gridCellTarget(), { key: 'ArrowDown', ctrlKey: true }),
        keydown(gridCellTarget(), { key: 'Home', altKey: true }),
        keydown(gridCellTarget(), { key: 'Home', metaKey: true, ctrlKey: true }),
    ];
    for (const event of cases) {
        t.listeners.get('keydown')(event);
        assert.equal(event.defaultPrevented, false, event.key);
    }
});

test('plain Home and End are still cancelled as before', async () => {
    await boot();
    const t = table();
    tmDataTable.attachGridKeys(t);
    const home = keydown(gridCellTarget(), { key: 'Home' });
    const end = keydown(gridCellTarget(), { key: 'End' });
    t.listeners.get('keydown')(home);
    t.listeners.get('keydown')(end);
    assert.equal(home.defaultPrevented, true);
    assert.equal(end.defaultPrevented, true);
});

function scrollerTable(cellAfterFrames) {
    const focused = [];
    const scrolledIntoView = [];
    let frames = 0;
    const cell = {
        focus: options => focused.push(options),
        scrollIntoView: options => scrolledIntoView.push(options),
    };
    const el = {
        focused,
        scrolledIntoView,
        closest: selector => (selector === '.tm-data-table-scroll' ? {
            scrollTo: () => { },
        } : null),
        querySelector: selector => {
            if (!selector.includes('data-grid-cell')) return null;
            frames += 1;
            return frames >= cellAfterFrames ? cell : null;
        },
    };
    return el;
}

test('scrollGridToIndex retries until the virtualized cell renders, then focuses it', async () => {
    await boot();
    const rafCallbacks = [];
    globalThis.requestAnimationFrame = callback => { rafCallbacks.push(callback); return rafCallbacks.length; };
    try {
        const el = scrollerTable(3);
        tmDataTable.scrollGridToIndex(el, 12, 48, 12, 1);
        assert.equal(el.focused.length, 0, 'no focus before the cell exists');
        rafCallbacks.splice(0)[0]();
        rafCallbacks.splice(0)[0]();
        assert.equal(el.focused.length, 0, 'still waiting after two frames');
        rafCallbacks.splice(0)[0]();
        assert.deepEqual(el.focused, [{ preventScroll: true }]);
        assert.deepEqual(el.scrolledIntoView, [{ block: 'nearest' }]);
    } finally {
        delete globalThis.requestAnimationFrame;
    }
});

test('scrollGridToIndex gives up after the bounded retry without throwing', async () => {
    await boot();
    const rafCallbacks = [];
    globalThis.requestAnimationFrame = callback => { rafCallbacks.push(callback); return rafCallbacks.length; };
    try {
        const el = scrollerTable(Number.POSITIVE_INFINITY);
        tmDataTable.scrollGridToIndex(el, 5, 48, 5, 0);
        for (let i = 0; i < 20 && rafCallbacks.length; i++)
            rafCallbacks.splice(0)[0]();
        assert.equal(rafCallbacks.length, 0, 'the retry is bounded');
        assert.equal(el.focused.length, 0);
    } finally {
        delete globalThis.requestAnimationFrame;
    }
});

test('scrollGridToIndex without a target cell only scrolls', async () => {
    await boot();
    let scrolled = 0;
    const el = {
        closest: () => ({ scrollTo: () => { scrolled += 1; } }),
        querySelector: () => { throw new Error('must not query'); },
    };
    assert.doesNotThrow(() => tmDataTable.scrollGridToIndex(el, 2, 48));
    assert.equal(scrolled, 1);
    assert.doesNotThrow(() => tmDataTable.scrollGridToIndex(null, 2, 48));
});
