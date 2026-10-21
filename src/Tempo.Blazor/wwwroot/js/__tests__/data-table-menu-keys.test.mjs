// data-table.js column-menu key shortcut (phase 13). Alt+ArrowDown on a header that announces it
// (aria-keyshortcuts) opens the column menu, but the browser's own default for that chord is to
// scroll the page; the header then leaves the viewport and the overlay dismisses itself as
// "anchor-hidden" ~150 ms after it opened. Blazor cannot make preventDefault depend on the key, so
// the decision lives here. A header that does not announce the chord, and every other key, is left alone.
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

function headerWithShortcuts(shortcuts) {
    const th = {
        getAttribute: name => (name === 'aria-keyshortcuts' ? shortcuts : null),
    };
    const target = {
        closest: selector => (selector.includes('th') ? th : null),
    };
    return target;
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

test('Alt+ArrowDown on a header that announces it is cancelled', async () => {
    await boot();
    const t = table();
    tmDataTable.attachColumnMenuKeys(t);
    const event = keydown(headerWithShortcuts('P Alt+ArrowDown'), { altKey: true });
    t.listeners.get('keydown')(event);
    assert.equal(event.defaultPrevented, true);
});

test('Alt+ArrowDown on a header that does not announce it keeps the browser default', async () => {
    await boot();
    const t = table();
    tmDataTable.attachColumnMenuKeys(t);
    const event = keydown(headerWithShortcuts('P'), { altKey: true });
    t.listeners.get('keydown')(event);
    assert.equal(event.defaultPrevented, false);
});

test('a plain ArrowDown or another Alt chord is left alone', async () => {
    await boot();
    const t = table();
    tmDataTable.attachColumnMenuKeys(t);
    const plain = keydown(headerWithShortcuts('P Alt+ArrowDown'), {});
    const otherChord = keydown(headerWithShortcuts('P Alt+ArrowDown'), { altKey: true, key: 'ArrowUp' });
    t.listeners.get('keydown')(plain);
    t.listeners.get('keydown')(otherChord);
    assert.equal(plain.defaultPrevented, false);
    assert.equal(otherChord.defaultPrevented, false);
});

test('a key outside any header is ignored', async () => {
    await boot();
    const t = table();
    tmDataTable.attachColumnMenuKeys(t);
    const event = keydown({ closest: () => null }, { altKey: true });
    t.listeners.get('keydown')(event);
    assert.equal(event.defaultPrevented, false);
});

test('attaching twice registers one listener, detach removes it', async () => {
    await boot();
    const t = table();
    tmDataTable.attachColumnMenuKeys(t);
    const first = t.listeners.get('keydown');
    tmDataTable.attachColumnMenuKeys(t);
    assert.equal(t.listeners.get('keydown'), first);
    tmDataTable.detachColumnMenuKeys(t);
    assert.equal(t.listeners.has('keydown'), false);
});

function tableWithEditor(selectors) {
    const focused = [];
    const editor = { focus: options => focused.push(options) };
    return {
        focused,
        querySelector: selector => (selectors.some(part => selector.includes(part)) ? editor : null),
    };
}

test('focusRowEditor focuses the first editor control of the editing row', async () => {
    await boot();
    const table = tableWithEditor(['tr.tm-row-editing']);
    tmDataTable.focusRowEditor(table);
    assert.equal(table.focused.length, 1);
    assert.deepEqual(table.focused[0], { preventScroll: true });
});

test('focusRowEditor is a no-op when the row has no editor control or there is no table', async () => {
    await boot();
    const none = { querySelector: () => null };
    assert.doesNotThrow(() => tmDataTable.focusRowEditor(none));
    assert.doesNotThrow(() => tmDataTable.focusRowEditor(null));
});