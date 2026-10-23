// pdf-annotator.js history-shortcut tests — the FZE-15 review moved the undo/redo shortcut
// FILTERING out of the Blazor render tree into ONE keydown listener per annotator root
// (attachHistoryShortcuts / detachHistoryShortcuts). Blazor is invoked only for an actual
// shortcut press: Ctrl xor ⌘ + z/y, no held-key repeat, no editable focus, preventDefault.
//
// pdf-annotator.js is a classic script installing window.tmPdfAnnotator; it is evaluated
// with window/document stubs (only document.currentScript is read at load time).

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const ANNOTATOR_JS = new URL('../pdf-annotator.js', import.meta.url);

async function load() {
    const src = await readFile(ANNOTATOR_JS, 'utf8');
    const window = {};
    const document = { currentScript: { src: 'https://example.test/_content/js/pdf-annotator.js' } };
    new Function('window', 'document', src)(window, document);
    return window.tmPdfAnnotator;
}

function fakeRoot() {
    const listeners = {};
    return {
        listeners,
        addEventListener(type, fn, options) { (listeners[type] ??= []).push({ fn, options }); },
        removeEventListener() { },
        contains() { return false; },
    };
}

// A real DOM removes a listener automatically once its AbortSignal aborts — the fake
// mirrors that contract: an aborted listener is never handed back.
const handler = (root) => {
    const live = (root.listeners.keydown ?? []).filter(l => !l.options.signal.aborted);
    return live.length ? live[0].fn : () => { };
};

function keydown(overrides = {}) {
    return {
        key: 'z', ctrlKey: true, metaKey: false, altKey: false, shiftKey: false, repeat: false,
        prevented: false,
        preventDefault() { this.prevented = true; },
        ...overrides,
    };
}

function dotnetRef() {
    return { calls: [], invokeMethodAsync(name, arg) { this.calls.push([name, arg]); return Promise.resolve(); } };
}

test('attachHistoryShortcuts installs one keydown listener with an AbortController signal', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();

    assert.equal(tm.attachHistoryShortcuts(root, ref), true);
    const attached = root.listeners.keydown;
    assert.equal(attached.length, 1);
    assert.equal(typeof attached[0].options.signal.aborted, 'boolean', 'the listener carries a signal');
});

test('Ctrl+Z invokes OnHistoryShortcut(false) and preventDefaults', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    tm.attachHistoryShortcuts(root, ref);

    const e = keydown();
    handler(root)(e);

    assert.deepEqual(ref.calls, [['OnHistoryShortcut', false]]);
    assert.equal(e.prevented, true, 'the browser text undo must not run');
});

test('Ctrl+Shift+Z and Ctrl+Y invoke OnHistoryShortcut(true)', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    tm.attachHistoryShortcuts(root, ref);

    handler(root)(keydown({ shiftKey: true }));
    handler(root)(keydown({ key: 'Y' }));

    assert.deepEqual(ref.calls, [['OnHistoryShortcut', true], ['OnHistoryShortcut', true]]);
});

test('⌘+Z on macOS invokes, Alt never participates, Ctrl+⌘ together is ignored', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    tm.attachHistoryShortcuts(root, ref);

    handler(root)(keydown({ ctrlKey: false, metaKey: true })); // ⌘+Z
    handler(root)(keydown({ altKey: true }));                  // Alt+Z → no
    handler(root)(keydown({ metaKey: true }));                 // Ctrl+⌘+Z → no
    handler(root)(keydown({ ctrlKey: false }));                // plain z → no

    assert.deepEqual(ref.calls, [['OnHistoryShortcut', false]],
        'only the plain ⌘+Z press reaches Blazor');
});

test('non-z/y keys and held-key repeats are filtered locally', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    tm.attachHistoryShortcuts(root, ref);

    handler(root)(keydown({ key: 'a' }));
    handler(root)(keydown({ repeat: true }));

    assert.deepEqual(ref.calls, []);
});

test('an editable focus inside the root keeps the NATIVE text undo', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    const document = { activeElement: { tagName: 'TEXTAREA' } };

    // Re-evaluate the script with an editable active element — isEditableFocused reads
    // document.activeElement and root.contains(active) must be true.
    const src = await readFile(ANNOTATOR_JS, 'utf8');
    const window = {};
    new Function('window', 'document', src)(window, {
        currentScript: { src: '' },
        activeElement: { tagName: 'TEXTAREA' },
        ...{},
    });
    const tm2 = window.tmPdfAnnotator;
    // contains() on the fake root reports true so the focus IS inside the root.
    const root2 = fakeRoot();
    root2.contains = () => true;
    tm2.attachHistoryShortcuts(root2, ref);

    const e = keydown();
    handler(root2)(e);

    assert.deepEqual(ref.calls, [], 'the shortcut never fires in an editable');
    assert.equal(e.prevented, false, 'the native text undo keeps the event');
    void document;
});

test('detachHistoryShortcuts unregisters the listener (dispose/destroy per instance)', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    tm.attachHistoryShortcuts(root, ref);

    assert.equal(tm.detachHistoryShortcuts(root), true);
    assert.equal(root.listeners.keydown[0].options.signal.aborted, true,
        'the per-instance AbortController is aborted');
    assert.equal(tm.detachHistoryShortcuts(root), false, 'a second detach is a no-op');

    const e = keydown();
    handler(root)(e);
    assert.deepEqual(ref.calls, [], 'the aborted listener never fires');
});

test('a re-attach replaces the previous listener (no double invocation)', async () => {
    const tm = await load();
    const root = fakeRoot();
    const ref = dotnetRef();
    tm.attachHistoryShortcuts(root, ref);
    tm.attachHistoryShortcuts(root, ref);

    // The fake keeps dead entries (the real DOM removes them); count the live ones.
    assert.equal(root.listeners.keydown.filter(l => !l.options.signal.aborted).length, 1,
        'the re-attach detached the old listener');
    handler(root)(keydown());
    assert.deepEqual(ref.calls, [['OnHistoryShortcut', false]], 'exactly one invocation per press');
});

test('attach returns false for missing arguments and non-element roots', async () => {
    const tm = await load();
    assert.equal(tm.attachHistoryShortcuts(null, dotnetRef()), false);
    assert.equal(tm.attachHistoryShortcuts(fakeRoot(), null), false);
    assert.equal(tm.attachHistoryShortcuts({}, dotnetRef()), false);
    assert.equal(tm.detachHistoryShortcuts(null), false);
});
