// Node tests of the pinned-on-top registry in tm-top-layer.js (plan 4ce1b994, decision
// TOP-LAYER-PIN, docs/top-layer-pin.md §4.2–§4.3): explicit priority bands, the native modal
// <dialog> deferral and the close re-sync. The 2.11 behaviours (re-raise after promote, skip a
// closed root, drop a disconnected root, pusher lands last) stay pinned in tm-sheet.test.mjs.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    pinRoot, unpinRoot, promote, demote, raise, isNativeModalOpen, syncPinnedRoots, __resetForTests,
} from '../tm-top-layer.js';

// One shared paint log: the ORDER of show calls across roots is the top-layer order (the last
// shown paints topmost).
function stage() {
    const log = [];
    const root = (name, open = false) => {
        const el = {
            name,
            isConnected: true,
            _open: open,
            matches: (selector) => selector === ':popover-open' ? el._open : false,
            showPopover() { el._open = true; log.push(`${name}:show`); },
            hidePopover() { el._open = false; log.push(`${name}:hide`); },
        };
        return el;
    };
    // The topmost painted order: replay the log and keep every open root by its last show.
    const order = () => {
        const shown = [];
        for (const entry of log) {
            const [name, op] = entry.split(':');
            const at = shown.indexOf(name);
            if (at >= 0) shown.splice(at, 1);
            if (op === 'show') shown.push(name);
        }
        return shown;
    };
    return { log, root, order };
}

// A minimal document: the registry asks it for `dialog:modal` and installs ONE capture-phase
// close listener on it.
function installDocument() {
    const listeners = [];
    const doc = {
        modalOpen: false,
        querySelector(selector) {
            if (selector === 'dialog:modal') return doc.modalOpen ? { tagName: 'DIALOG' } : null;
            return null;
        },
        addEventListener(type, fn, capture) { listeners.push({ type, fn, capture }); },
        removeEventListener() { },
        listeners,
        fireClose() {
            for (const l of listeners.filter(entry => entry.type === 'close')) l.fn({ type: 'close' });
        },
    };
    globalThis.document = doc;
    return doc;
}

function cleanup() {
    __resetForTests();
    delete globalThis.document;
}

test('pinned roots re-raise in ascending priority, so the highest priority paints topmost', () => {
    const { root, order } = stage();
    const banner = root('banner', true);
    const toast = root('toast', true);
    try {
        // The banner registers FIRST but carries the higher priority: registration order alone
        // would leave it under the toasts.
        pinRoot(banner, { priority: 100 });
        pinRoot(toast);
        promote(root('modal'));
        assert.deepEqual(order(), ['modal', 'toast', 'banner']);
    }
    finally { cleanup(); }
});

test('equal priorities keep registration order', () => {
    const { root, order } = stage();
    const a = root('a', true);
    const b = root('b', true);
    try {
        pinRoot(a);
        pinRoot(b);
        promote(root('sheet'));
        assert.deepEqual(order(), ['sheet', 'a', 'b']);
    }
    finally { cleanup(); }
});

test('re-pinning a root updates its priority but keeps its registration slot', () => {
    const { root, order } = stage();
    const a = root('a', true);
    const b = root('b', true);
    const c = root('c', true);
    try {
        pinRoot(a);
        pinRoot(b);
        pinRoot(c);
        pinRoot(a, { priority: 5 });   // a jumps above b and c
        pinRoot(a, { priority: 0 });   // and back: its original (first) slot, not the last one
        promote(root('dialog'));
        assert.deepEqual(order(), ['dialog', 'a', 'b', 'c']);
    }
    finally { cleanup(); }
});

test('raise lands the pusher on top of its own band but never above a higher band', () => {
    const { root, order } = stage();
    const banner = root('banner', true);
    const toast = root('toast', true);
    const notifications = root('notifications', true);
    try {
        pinRoot(banner, { priority: 100 });
        pinRoot(toast);
        pinRoot(notifications);
        raise(toast);
        assert.deepEqual(order(), ['notifications', 'toast', 'banner'],
            'the pushed toast tops the priority-0 band, the priority-100 banner stays above it');
    }
    finally { cleanup(); }
});

test('promoting a pinned root ranks it inside its band instead of under every other pin', () => {
    const { root, order } = stage();
    const banner = root('banner');
    const toast = root('toast');
    const late = root('late');
    try {
        pinRoot(banner, { priority: 100 });
        pinRoot(toast);
        pinRoot(late);
        toast.showPopover();
        promote(banner);
        assert.deepEqual(order(), ['toast', 'banner'], 'a higher-band pin lands above the open lower band');
        promote(late);
        assert.deepEqual(order(), ['toast', 'late', 'banner'],
            'a lower-band pin promoted later stays under the higher band (the banner re-raises above it)');
    }
    finally { cleanup(); }
});

test('a native modal dialog stops every re-raise: pins are inert outside it, so they must not cover it', () => {
    const doc = installDocument();
    const { root, log } = stage();
    const toast = root('toast', true);
    pinRoot(toast);
    try {
        doc.modalOpen = true;
        assert.equal(isNativeModalOpen(), true);
        promote(root('drawer'));
        raise(toast);
        assert.deepEqual(log.filter(entry => entry.startsWith('toast')), [],
            'neither a later promotion nor a push may lift the toast above the open dialog');
    }
    finally { cleanup(); }
});

test('a raise while a native modal dialog is open is deferred and lands when the dialog closes', () => {
    const doc = installDocument();
    const { root, order } = stage();
    const toast = root('toast', false);
    pinRoot(toast);
    try {
        doc.modalOpen = true;
        raise(toast);
        assert.equal(toast._open, false, 'a container that was not up yet stays hidden while the dialog is open');

        doc.modalOpen = false;
        doc.fireClose();
        assert.equal(toast._open, true, 'the close re-sync shows the deferred container');
        assert.deepEqual(order(), ['toast']);
    }
    finally { cleanup(); }
});

test('a deferred show dispatches tm-pin-shown once, so the owner can re-announce', () => {
    const doc = installDocument();
    const { root } = stage();
    const toast = root('toast', false);
    const open = root('open', true);
    const events = [];
    toast.dispatchEvent = (event) => events.push(`toast:${event.type}`);
    open.dispatchEvent = (event) => events.push(`open:${event.type}`);
    pinRoot(toast);
    pinRoot(open);
    try {
        doc.modalOpen = true;
        raise(toast);
        doc.modalOpen = false;
        doc.fireClose();
        doc.fireClose();
        assert.deepEqual(events, ['toast:tm-pin-shown'],
            'only the root whose show was DEFERRED is told, and only once; a root that stayed open was never hidden');
    }
    finally { cleanup(); }
});

test('demote while deferred cancels the pending show', () => {
    const doc = installDocument();
    const { root } = stage();
    const toast = root('toast', false);
    pinRoot(toast);
    try {
        doc.modalOpen = true;
        raise(toast);
        demote(toast);   // the last toast left while the dialog was still open
        doc.modalOpen = false;
        doc.fireClose();
        assert.equal(toast._open, false, 'an emptied container must not pop up after the dialog closes');
    }
    finally { cleanup(); }
});

test('the close re-sync re-raises open pins above every surface promoted during the dialog', () => {
    const doc = installDocument();
    const { root, order } = stage();
    const toast = root('toast', true);
    pinRoot(toast);
    try {
        doc.modalOpen = true;
        promote(root('sheet'));   // a Tempo surface opened from inside the native dialog
        doc.modalOpen = false;
        doc.fireClose();
        assert.deepEqual(order(), ['sheet', 'toast'],
            'the pin must not stay lost under a surface opened while the dialog blocked the re-raise');
    }
    finally { cleanup(); }
});

test('a close event while ANOTHER native modal dialog is still open does not re-sync', () => {
    const doc = installDocument();
    const { root } = stage();
    const toast = root('toast', false);
    pinRoot(toast);
    try {
        doc.modalOpen = true;
        raise(toast);
        doc.fireClose();   // the inner dialog closed, the outer one is still modal
        assert.equal(toast._open, false);
    }
    finally { cleanup(); }
});

test('the registry installs exactly one capture-phase close listener', () => {
    const doc = installDocument();
    const { root } = stage();
    try {
        pinRoot(root('a'));
        pinRoot(root('b'));
        unpinRoot(root('c'));
        const close = doc.listeners.filter(entry => entry.type === 'close');
        assert.equal(close.length, 1);
        assert.equal(close[0].capture, true, 'close does not bubble, only a capture listener on document sees it');
    }
    finally { cleanup(); }
});

test('syncPinnedRoots without a document and without the Popover API is a silent no-op', () => {
    const inert = { isConnected: true, matches: () => false };
    pinRoot(inert);
    try {
        assert.equal(isNativeModalOpen(), false);
        assert.doesNotThrow(() => syncPinnedRoots());
        assert.doesNotThrow(() => promote(inert));
        assert.doesNotThrow(() => raise(inert));
    }
    finally { cleanup(); }
});

test('a document without :modal support reads as no native modal', () => {
    globalThis.document = { querySelector() { throw new SyntaxError("':modal' is not a valid selector"); }, addEventListener() { } };
    try {
        assert.equal(isNativeModalOpen(), false);
    }
    finally { cleanup(); }
});

// A host can end up with TWO instances of this module: a <script type="module"> tag with the plain
// URL next to an IJSRuntime import that an import map rewrites to the fingerprinted URL. Two
// registries would split the contract (a toast pinned through one copy is never re-raised by a
// promote through the other), so the registry state lives on globalThis and every copy shares it.
test('a second copy of the module shares the one registry', async () => {
    const copy = await import('../tm-top-layer.js?second-instance');
    const { root, order } = stage();
    const toast = root('toast', true);
    try {
        pinRoot(toast);
        copy.promote(root('drawer'));
        assert.deepEqual(order(), ['drawer', 'toast'],
            'a promote through the second copy must re-raise a root pinned through the first');
    }
    finally { cleanup(); }
});
