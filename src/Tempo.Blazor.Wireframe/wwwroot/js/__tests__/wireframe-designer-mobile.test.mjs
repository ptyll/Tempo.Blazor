// Mobile helpers of wireframe-designer.js (plan 643bba06, wireframe G2 review debt): the focus return to the action-bar tile that
// opened the sheet (and its fallbacks), the focus after Delete / Lock, and the download helper's Blob URL life cycle (created, used,
// revoked late - never before the browser got the file - and the base64 data: URL only as the fallback).
import test, { mock } from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const SRC_PATH = path.resolve(here, '../wireframe-designer.js');
const SRC = fs.readFileSync(SRC_PATH, 'utf8');

function loadEngine(globals = {}) {
    const win = {};
    const ctx = vm.createContext({ window: win, document: {}, console, setTimeout, clearTimeout, performance, ...globals });
    vm.runInContext(SRC, ctx, { filename: SRC_PATH });
    return { engine: win.tmWireframeDesigner, ctx };
}

// ── a tiny DOM for the action bar ─────────────────────────────────────────────

function tile(id, { disabled = false, more = false } = {}) {
    return {
        id, disabled, more, focused: false,
        getAttribute: name => (name === 'data-action-id' ? id : null),
        focus(options) { this.focused = true; this.options = options; },
    };
}

/** A root whose querySelector(All) understands the three selectors the helpers use. */
function barRoot(tiles, extra = {}) {
    const actions = tiles.filter(t => !t.more && !t.disabled);
    const more = tiles.filter(t => t.more && !t.disabled);
    const canvas = extra.canvas ?? null;
    return {
        querySelectorAll(selector) {
            if (selector === '.tm-mobile-action-bar__action:not([disabled])') return actions;
            return [];
        },
        querySelector(selector) {
            if (selector === '.tm-mobile-action-bar__action:not([disabled]), .tm-mobile-action-bar__more') return actions[0] ?? more[0] ?? null;
            if (selector === '.tm-mobile-action-bar__more:not([disabled])') return more[0] ?? null;
            if (selector === '.tm-wd-canvas-wrap') return canvas;
            return null;
        },
    };
}

function documentWith(active, { contains = true } = {}) {
    const body = {};
    return { body, activeElement: active === 'body' ? body : active, contains: () => contains };
}

// ── focusMobileAction: back to the opener ─────────────────────────────────────

test('focusMobileAction returns the focus to the tile that opened the sheet, not to the first tile', () => {
    const add = tile('components'), pages = tile('pages-layers'), exp = tile('export');
    const { engine } = loadEngine({ document: documentWith('body') });

    assert.equal(engine.focusMobileAction(barRoot([add, pages, exp]), 'pages-layers'), true);

    assert.equal(pages.focused, true);
    assert.equal(add.focused, false);
    assert.deepEqual({ ...pages.options }, { preventScroll: true });
});

test('focusMobileAction falls back to the first tile when no tile opened the sheet or the opener left the bar', () => {
    const add = tile('components'), more = tile('more', { more: true });
    const first = loadEngine({ document: documentWith('body') });
    assert.equal(first.engine.focusMobileAction(barRoot([add, more]), null), true);
    assert.equal(add.focused, true, 'a selection opened the sheet: no opener id, the first tile');

    const add2 = tile('components');
    const second = loadEngine({ document: documentWith('body') });
    assert.equal(second.engine.focusMobileAction(barRoot([add2]), 'pages-layers'), true);
    assert.equal(add2.focused, true, 'the opener folded into "More" (not a bar tile any more): the first tile');
});

test('focusMobileAction never takes a focus the user placed elsewhere, and skips a disabled opener', () => {
    const add = tile('components');
    const elsewhere = { id: 'input' };
    const kept = loadEngine({ document: documentWith(elsewhere) });
    assert.equal(kept.engine.focusMobileAction(barRoot([add]), 'components'), false);
    assert.equal(add.focused, false, 'focus was not lost: nothing moved');

    const disabled = tile('pages-layers', { disabled: true }), first = tile('components');
    const skip = loadEngine({ document: documentWith('body') });
    assert.equal(skip.engine.focusMobileAction(barRoot([first, disabled]), 'pages-layers'), true);
    assert.equal(first.focused, true, 'a disabled tile cannot hold focus: the first enabled tile does');
    assert.equal(disabled.focused, false);
});

test('focusMobileAction treats a detached active element as lost focus, and tolerates a missing root', () => {
    const add = tile('components');
    const detached = loadEngine({ document: documentWith({ id: 'gone' }, { contains: false }) });
    assert.equal(detached.engine.focusMobileAction(barRoot([add]), 'components'), true);
    assert.equal(add.focused, true);

    assert.equal(detached.engine.focusMobileAction(null, 'components'), false);
    assert.equal(detached.engine.focusMobileAction({}, 'components'), false);
    assert.equal(detached.engine.focusMobileAction(barRoot([]), 'components'), false, 'an empty bar has nothing to focus');
});

test('the sheet scroll reset moved out of the wireframe script into TmEditorShell', () => {
    const { engine } = loadEngine();
    assert.equal(engine.resetMobileSheetScroll, undefined);
    assert.equal(engine.focusFirstMobileAction, undefined, 'replaced by focusMobileAction(root, actionId)');
});

// ── focusAfterMobileAction: after Delete / Lock ───────────────────────────────

test('focusAfterMobileAction prefers the More tile (never Add), then the canvas, and only when the focus was lost', () => {
    const add = tile('components'), more = tile('more', { more: true });
    const a = loadEngine({ document: documentWith('body') });
    assert.equal(a.engine.focusAfterMobileAction(barRoot([add, more])), true);
    assert.equal(more.focused, true);
    assert.equal(add.focused, false, 'focusing Add right after a delete reads as an invitation to insert');

    const canvas = tile('canvas');
    const b = loadEngine({ document: documentWith('body') });
    assert.equal(b.engine.focusAfterMobileAction(barRoot([add], { canvas })), true);
    assert.equal(canvas.focused, true, 'no More tile: the canvas takes the focus');

    const c = loadEngine({ document: documentWith({ id: 'user' }) });
    assert.equal(c.engine.focusAfterMobileAction(barRoot([add, more])), false);
});

// ── downloadFile: the Blob URL life cycle ─────────────────────────────────────

function anchor() {
    return {
        href: '', download: '', clicks: [],
        click() { this.clicks.push({ href: this.href, download: this.download }); },
    };
}

test('downloadFile saves through a Blob URL, clicks the anchor with it, and revokes it only after the late timer', () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    try {
        const created = [], revoked = [];
        const blobs = [];
        class FakeBlob { constructor(parts, options) { this.parts = parts; this.type = options.type; blobs.push(this); } }
        const { engine } = loadEngine({
            Blob: FakeBlob,
            URL: { createObjectURL: blob => { created.push(blob); return 'blob:https://app/abc'; }, revokeObjectURL: url => revoked.push(url) },
            atob: s => Buffer.from(s, 'base64').toString('binary'),
            Uint8Array,
            setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
        });
        const a = anchor();

        engine.downloadFile(a, 'plan.json', 'application/json', Buffer.from('{"a":1}').toString('base64'));

        assert.equal(created.length, 1);
        assert.equal(blobs[0].type, 'application/json');
        assert.deepEqual([...blobs[0].parts[0]], [...Buffer.from('{"a":1}')], 'the bytes survived the base64 round trip');
        assert.deepEqual(a.clicks, [{ href: 'blob:https://app/abc', download: 'plan.json' }]);
        assert.deepEqual(revoked, [], 'not revoked synchronously: iOS hands the blob to a new tab / the download manager asynchronously');

        mock.timers.tick(59_999);
        assert.deepEqual(revoked, [], 'still alive just before the 60 s release');
        assert.equal(a.href, 'blob:https://app/abc');

        mock.timers.tick(1);
        assert.deepEqual(revoked, ['blob:https://app/abc'], 'revoked exactly once, after the delay');
        assert.equal(a.href, '', 'the anchor is released with it');
    } finally {
        mock.timers.reset();
    }
});

test('downloadFile falls back to the base64 data: URL where Blob / createObjectURL is missing, and then revokes nothing', () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    try {
        const revoked = [];
        const { engine } = loadEngine({
            URL: { revokeObjectURL: url => revoked.push(url) },   // no createObjectURL
            setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
        });
        const a = anchor();

        engine.downloadFile(a, 'p.svg', 'image/svg+xml', 'PHN2Zy8+');
        mock.timers.tick(60_000);

        assert.deepEqual(a.clicks, [{ href: 'data:image/svg+xml;base64,PHN2Zy8+', download: 'p.svg' }]);
        assert.deepEqual(revoked, [], 'a data: URL is not an object URL: there is nothing to revoke');
        assert.equal(a.href, '');
    } finally {
        mock.timers.reset();
    }
});

test('downloadFile falls back to the data: URL when building the Blob throws, and never leaks a half-created URL', () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    try {
        const revoked = [];
        const { engine } = loadEngine({
            Blob: class { constructor() { throw new Error('quota'); } },
            URL: { createObjectURL: () => 'blob:never', revokeObjectURL: url => revoked.push(url) },
            atob: s => Buffer.from(s, 'base64').toString('binary'),
            Uint8Array,
            setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
        });
        const a = anchor();

        engine.downloadFile(a, 'p.png', 'image/png', 'AAAA');
        mock.timers.tick(60_000);

        assert.equal(a.clicks[0].href, 'data:image/png;base64,AAAA');
        assert.deepEqual(revoked, []);
    } finally {
        mock.timers.reset();
    }
});

test('downloadFile survives a revokeObjectURL that throws (the URL is already gone)', () => {
    mock.timers.enable({ apis: ['setTimeout'] });
    try {
        class FakeBlob { constructor() {} }
        const { engine } = loadEngine({
            Blob: FakeBlob,
            URL: { createObjectURL: () => 'blob:x', revokeObjectURL: () => { throw new Error('gone'); } },
            atob: s => Buffer.from(s, 'base64').toString('binary'),
            Uint8Array,
            setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
        });
        const a = anchor();
        engine.downloadFile(a, 'p.json', 'application/json', 'e30=');

        assert.doesNotThrow(() => mock.timers.tick(60_000));
        assert.equal(a.href, '');
    } finally {
        mock.timers.reset();
    }
});
