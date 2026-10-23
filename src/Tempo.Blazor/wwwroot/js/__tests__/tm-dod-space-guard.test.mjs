// Space scroll guard + roving focus of TmDocumentOpenDialog (colocated module). Hand-rolled DOM stubs, no jsdom.
import test from 'node:test';
import assert from 'node:assert/strict';
import { shouldSuppressSpaceScroll, shouldSuppressNavigationScroll, attachSpaceGuard, detachSpaceGuard, findRovingTarget, focusRoving, getGridColumns, keyboardLift, attachKeyboardLift, detachKeyboardLift } from '../../../Components/Files/TmDocumentOpenDialog.razor.js';

const element = classes => ({ matches: selector => selector.split(',').map(s => s.trim()).some(s => classes.includes(s.replace('.', ''))) });

test('Space on a list row, a grid card or a tree item is suppressed (no page scroll)', () => {
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-row']) }), true);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-card']) }), true);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-tree-node']) }), true);
});

test('Space bubbling off an input / button inside the dialog is never suppressed', () => {
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-search']) }), false);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-new-folder-input']) }), false);
});

test('Tab, Enter and arrows are never suppressed (a blanket preventDefault would trap focus)', () => {
    for (const key of ['Tab', 'Enter', 'ArrowDown', 'Escape', 'a'])
        assert.equal(shouldSuppressSpaceScroll({ key, target: element(['tm-dod-row']) }), false, key);
});

test('a missing target or event is tolerated', () => {
    assert.equal(shouldSuppressSpaceScroll(null), false);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ' }), false);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: {} }), false);
});

test('attach registers one keydown listener per root, detach removes it, attach is idempotent', () => {
    const added = [], removed = [];
    const root = {
        addEventListener: (type, handler) => added.push([type, handler]),
        removeEventListener: (type, handler) => removed.push([type, handler]),
    };
    attachSpaceGuard(root);
    attachSpaceGuard(root);
    assert.equal(added.length, 1);
    assert.equal(added[0][0], 'keydown');

    let prevented = 0;
    added[0][1]({ key: ' ', target: element(['tm-dod-row']), preventDefault: () => prevented++ });
    added[0][1]({ key: ' ', target: element(['tm-dod-search']), preventDefault: () => prevented++ });
    assert.equal(prevented, 1);

    detachSpaceGuard(root);
    detachSpaceGuard(root);
    assert.equal(removed.length, 1);
    assert.equal(removed[0][1], added[0][1]);
});

test('attach / detach tolerate a null root', () => {
    assert.doesNotThrow(() => attachSpaceGuard(null));
    assert.doesNotThrow(() => detachSpaceGuard(null));
});

// ── Roving focus ─────────────────────────────────────────────────────────────────────

const node = (attrs, extra = {}) => ({ getAttribute: name => attrs[name] ?? null, focus() { this.focused = true; }, ...extra });
const rootOf = (bySelector) => ({ querySelectorAll: selector => bySelector[selector] ?? [] });

test('findRovingTarget matches a tree item by data-path and a row/card by data-id', () => {
    const tree = [node({ 'data-path': '/' }), node({ 'data-path': '/Designs' })];
    const items = [node({ 'data-id': 'a' }), node({ 'data-id': 'b' })];
    const root = rootOf({ '.tm-dod-tree-node': tree, '.tm-dod-row, .tm-dod-card': items });
    assert.equal(findRovingTarget(root, 'tree', '/Designs'), tree[1]);
    assert.equal(findRovingTarget(root, 'item', 'a'), items[0]);
    assert.equal(findRovingTarget(root, 'item', 'zzz'), null);
    assert.equal(findRovingTarget(root, 'tree', 'a'), null, 'a document id is not a folder path');
});

test('focusRoving focuses the target and tolerates a missing target / root', () => {
    const target = node({ 'data-id': 'a' });
    focusRoving(rootOf({ '.tm-dod-row, .tm-dod-card': [target] }), 'item', 'a');
    assert.equal(target.focused, true);
    assert.doesNotThrow(() => focusRoving(rootOf({}), 'item', 'a'));
    assert.doesNotThrow(() => focusRoving(null, 'item', 'a'));
});

test('getGridColumns counts the cards that share the first row', () => {
    const card = top => ({ offsetTop: top });
    assert.equal(getGridColumns(rootOf({ '.tm-dod-card': [card(0), card(0), card(0), card(120), card(120)] })), 3);
    assert.equal(getGridColumns(rootOf({ '.tm-dod-card': [card(0), card(120), card(240)] })), 1, 'one column');
    assert.equal(getGridColumns(rootOf({ '.tm-dod-card': [card(0), card(0)] })), 2, 'a single partial row');
    assert.equal(getGridColumns(rootOf({})), 1, 'no cards falls back to one');
    assert.equal(getGridColumns(null), 1);
});

test('arrows / Home / End on a tree item, row or card are suppressed (focus moves, the container must not scroll)', () => {
    for (const key of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End'])
        for (const cls of ['tm-dod-tree-node', 'tm-dod-row', 'tm-dod-card'])
            assert.equal(shouldSuppressNavigationScroll({ key, target: element([cls]) }), true, `${key} on ${cls}`);
});

test('navigation keys off an input / button, and other keys on an item, are never suppressed', () => {
    assert.equal(shouldSuppressNavigationScroll({ key: 'ArrowLeft', target: element(['tm-dod-search']) }), false, 'caret movement in the search box');
    assert.equal(shouldSuppressNavigationScroll({ key: 'ArrowDown', target: element(['tm-dod-tree-toggle']) }), false);
    for (const key of ['Tab', 'Enter', 'Escape', ' ', 'a'])
        assert.equal(shouldSuppressNavigationScroll({ key, target: element(['tm-dod-row']) }), false, key);
    for (const mod of ['altKey', 'ctrlKey', 'metaKey', 'shiftKey'])
        assert.equal(shouldSuppressNavigationScroll({ key: 'ArrowLeft', [mod]: true, target: element(['tm-dod-row']) }), false, mod);
    assert.equal(shouldSuppressNavigationScroll(null), false);
    assert.equal(shouldSuppressNavigationScroll({ key: 'ArrowDown' }), false);
});

test('the attached listener cancels an arrow on a row but not in the search input', () => {
    const added = [];
    const root = { addEventListener: (type, handler) => added.push(handler), removeEventListener() {} };
    attachSpaceGuard(root);
    let prevented = 0;
    added[0]({ key: 'ArrowDown', target: element(['tm-dod-row']), preventDefault: () => prevented++ });
    added[0]({ key: 'ArrowDown', target: element(['tm-dod-search']), preventDefault: () => prevented++ });
    assert.equal(prevented, 1);
});

// ── Phase 9 (mobile): focus targets beyond the roving stops ───────────────────────────────────────────────────────────

const fakeRoot = elements => ({ querySelectorAll: selector => elements.filter(e => selector.split(',').map(s => s.trim()).some(s => e.classes.includes(s.replace('.', '')) || (s.startsWith('[') && e.attrs && Object.keys(e.attrs).some(a => s.includes(a))))) });
const fakeNode = (classes, attrs) => ({ classes, attrs, getAttribute: name => attrs[name] ?? null, focus() { this.focused = true; } });

test('findRovingTarget resolves a mobile folder row by its data-path', () => {
    const designs = fakeNode(['tm-dod-folder-row'], { 'data-path': '/Designs' });
    const archive = fakeNode(['tm-dod-folder-row'], { 'data-path': '/Archive' });
    const treeNode = fakeNode(['tm-dod-tree-node'], { 'data-path': '/Designs' });
    assert.equal(findRovingTarget(fakeRoot([treeNode, archive, designs]), 'folder', '/Designs'), designs);
    assert.equal(findRovingTarget(fakeRoot([treeNode, archive, designs]), 'tree', '/Designs'), treeNode);
    assert.equal(findRovingTarget(fakeRoot([archive]), 'folder', '/Missing'), null);
});

test('findRovingTarget resolves the mobile head controls (back button, search) by data-dod-control', () => {
    const back = fakeNode(['tm-dod-back'], { 'data-dod-control': 'back' });
    const search = fakeNode(['tm-dod-search'], { 'data-dod-control': 'search' });
    const r = fakeRoot([back, search]);
    assert.equal(findRovingTarget(r, 'control', 'back'), back);
    assert.equal(findRovingTarget(r, 'control', 'search'), search);
    assert.equal(findRovingTarget(r, 'control', 'nope'), null);
});

test('focusRoving focuses a folder row and a control, and tolerates a missing target', () => {
    const folder = fakeNode(['tm-dod-folder-row'], { 'data-path': '/Designs' });
    const back = fakeNode(['tm-dod-back'], { 'data-dod-control': 'back' });
    focusRoving(fakeRoot([folder, back]), 'folder', '/Designs');
    focusRoving(fakeRoot([folder, back]), 'control', 'back');
    assert.equal(folder.focused, true);
    assert.equal(back.focused, true);
    assert.doesNotThrow(() => focusRoving(fakeRoot([]), 'folder', '/x'));
});

test('a drill-down folder row is a native button: the Space / arrow scroll guard never touches it', () => {
    const folderRow = element(['tm-dod-folder-row']);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: folderRow }), false);
    assert.equal(shouldSuppressNavigationScroll({ key: 'ArrowDown', target: folderRow }), false);
});

test('the row menu trigger inside a mobile row never has its keys cancelled (only the row itself does)', () => {
    const trigger = element(['tm-dropdown-trigger']);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: trigger }), false);
    assert.equal(shouldSuppressNavigationScroll({ key: 'ArrowDown', target: trigger }), false);
});
test('keyboardLift pads by the hidden gap only while the keyboard is up', () => {
    assert.deepEqual(keyboardLift(844, { height: 844, offsetTop: 0 }), { padding: '', open: false });
    assert.deepEqual(keyboardLift(844, { height: 430, offsetTop: 0 }), { padding: '414px', open: true });
    // iOS scrolls the visual viewport instead of shrinking it: the offsetTop counts toward what is covered.
    assert.deepEqual(keyboardLift(844, { height: 500, offsetTop: 100 }), { padding: '244px', open: true });
    assert.deepEqual(keyboardLift(844, null), { padding: '', open: false });
});

test('attachKeyboardLift pads the overlay, flags it, and detach restores it (idempotent attach)', () => {
    const listeners = { vv: [], win: [] };
    const classes = new Set();
    const overlay = { style: { paddingBottom: '' }, classList: { toggle: (c, on) => on ? classes.add(c) : classes.delete(c), remove: c => classes.delete(c) } };
    const rootEl = { closest: selector => selector === '.tm-modal-overlay' ? overlay : null };
    const viewport = { height: 430, offsetTop: 0, addEventListener: (t, f) => listeners.vv.push([t, f]), removeEventListener: (t, f) => { listeners.vv = listeners.vv.filter(l => l[1] !== f); } };
    const g = globalThis;
    const had = { window: g.window };
    g.window = { innerHeight: 844, visualViewport: viewport, addEventListener: (t, f) => listeners.win.push([t, f]), removeEventListener: (t, f) => { listeners.win = listeners.win.filter(l => l[1] !== f); } };
    try {
        attachKeyboardLift(rootEl);
        attachKeyboardLift(rootEl);
        assert.equal(overlay.style.paddingBottom, '414px');
        assert.equal(classes.has('tm-dod-keyboard'), true);
        assert.equal(listeners.vv.length, 2, 'resize + scroll, registered once');

        viewport.height = 844;
        listeners.vv[0][1]();
        assert.equal(overlay.style.paddingBottom, '');
        assert.equal(classes.has('tm-dod-keyboard'), false);

        detachKeyboardLift(rootEl);
        assert.equal(listeners.vv.length, 0);
        assert.equal(listeners.win.length, 0);
        assert.doesNotThrow(() => detachKeyboardLift(rootEl));
    } finally {
        g.window = had.window;
    }
});

test('attachKeyboardLift tolerates a root outside a modal overlay', () => {
    assert.doesNotThrow(() => attachKeyboardLift({ closest: () => null }));
    assert.doesNotThrow(() => attachKeyboardLift(null));
});

// ── Listener lifecycle: a stable key releases everything, even when the root element was replaced ────────────────────────────

function liftHarness() {
    const listeners = { vv: [], win: [] };
    const classes = new Set();
    const makeOverlay = () => ({ style: { paddingBottom: '' }, classList: { toggle: (c, on) => on ? classes.add(c) : classes.delete(c), remove: c => classes.delete(c) } });
    const viewport = {
        height: 430, offsetTop: 0,
        addEventListener: (t, f) => listeners.vv.push([t, f]),
        removeEventListener: (t, f) => { listeners.vv = listeners.vv.filter(l => l[0] !== t || l[1] !== f); }
    };
    const win = {
        innerHeight: 844, visualViewport: viewport,
        addEventListener: (t, f) => listeners.win.push([t, f]),
        removeEventListener: (t, f) => { listeners.win = listeners.win.filter(l => l[0] !== t || l[1] !== f); }
    };
    return { listeners, classes, makeOverlay, win };
}

test('attachKeyboardLift returns true once attached; detach by key removes window resize and visualViewport resize + scroll', () => {
    const h = liftHarness();
    const overlay = h.makeOverlay();
    const rootEl = { closest: () => overlay };
    const had = globalThis.window;
    globalThis.window = h.win;
    try {
        assert.equal(attachKeyboardLift(rootEl, 'dlg-1'), true);
        assert.equal(attachKeyboardLift(rootEl, 'dlg-1'), true, 'idempotent');
        assert.deepEqual(h.listeners.vv.map(l => l[0]).sort(), ['resize', 'scroll']);
        assert.deepEqual(h.listeners.win.map(l => l[0]), ['resize']);

        detachKeyboardLift('dlg-1');
        assert.equal(h.listeners.vv.length, 0, 'visualViewport resize + scroll removed');
        assert.equal(h.listeners.win.length, 0, 'window resize removed');
        assert.equal(overlay.style.paddingBottom, '');
        assert.equal(h.classes.has('tm-dod-keyboard'), false);
    } finally {
        globalThis.window = had;
    }
});

test('detach by key still releases the listeners and the CAPTURED overlay when the root was detached from the DOM', () => {
    const h = liftHarness();
    const overlay = h.makeOverlay();
    let attached = true;
    const rootEl = { closest: () => attached ? overlay : null };
    const had = globalThis.window;
    globalThis.window = h.win;
    try {
        attachKeyboardLift(rootEl, 'dlg-2');
        assert.equal(overlay.style.paddingBottom, '414px');
        attached = false; // Blazor removed the node: closest() finds nothing any more
        detachKeyboardLift('dlg-2');
        assert.equal(h.listeners.vv.length + h.listeners.win.length, 0);
        assert.equal(overlay.style.paddingBottom, '', 'the overlay captured at attach time is restored');
        assert.equal(h.classes.has('tm-dod-keyboard'), false);
    } finally {
        globalThis.window = had;
    }
});

test('a replaced root under the same key moves the lift: the old listeners never pile up', () => {
    const h = liftHarness();
    const rootA = { closest: () => h.makeOverlay() };
    const rootB = { closest: () => h.makeOverlay() };
    const had = globalThis.window;
    globalThis.window = h.win;
    try {
        attachKeyboardLift(rootA, 'dlg-3');
        attachKeyboardLift(rootB, 'dlg-3');
        assert.equal(h.listeners.vv.length, 2, 'one resize + one scroll, not four');
        assert.equal(h.listeners.win.length, 1);
        detachKeyboardLift('dlg-3');
        assert.equal(h.listeners.vv.length + h.listeners.win.length, 0);
    } finally {
        globalThis.window = had;
    }
});

test('attachKeyboardLift reports false while the overlay is missing and retries on animation frames until it appears', () => {
    const h = liftHarness();
    const overlay = h.makeOverlay();
    let present = false;
    const rootEl = { closest: () => present ? overlay : null };
    const frames = [];
    const had = { window: globalThis.window, raf: globalThis.requestAnimationFrame, caf: globalThis.cancelAnimationFrame };
    globalThis.window = h.win;
    globalThis.requestAnimationFrame = cb => { frames.push(cb); return frames.length; };
    globalThis.cancelAnimationFrame = () => {};
    try {
        assert.equal(attachKeyboardLift(rootEl, 'dlg-4'), false);
        assert.equal(h.listeners.vv.length, 0);
        assert.equal(frames.length, 1, 'a retry is scheduled');

        present = true;
        frames.shift()();
        assert.equal(h.listeners.vv.length, 2, 'the retry attached once the overlay existed');
        assert.equal(overlay.style.paddingBottom, '414px');
        assert.equal(frames.length, 0);

        detachKeyboardLift('dlg-4');
        assert.equal(h.listeners.vv.length + h.listeners.win.length, 0);
    } finally {
        globalThis.window = had.window;
        globalThis.requestAnimationFrame = had.raf;
        globalThis.cancelAnimationFrame = had.caf;
    }
});

test('detach cancels a pending retry (no listener appears after the dialog closed)', () => {
    const h = liftHarness();
    const overlay = h.makeOverlay();
    let present = false;
    const rootEl = { closest: () => present ? overlay : null };
    const frames = new Map();
    let next = 1;
    const had = { window: globalThis.window, raf: globalThis.requestAnimationFrame, caf: globalThis.cancelAnimationFrame };
    globalThis.window = h.win;
    globalThis.requestAnimationFrame = cb => { frames.set(next, cb); return next++; };
    globalThis.cancelAnimationFrame = id => { frames.delete(id); };
    try {
        attachKeyboardLift(rootEl, 'dlg-5');
        detachKeyboardLift('dlg-5');
        assert.equal(frames.size, 0, 'the scheduled frame was cancelled');
        present = true;
        assert.equal(h.listeners.vv.length + h.listeners.win.length, 0);
    } finally {
        globalThis.window = had.window;
        globalThis.requestAnimationFrame = had.raf;
        globalThis.cancelAnimationFrame = had.caf;
    }
});

test('space guard: detach by key removes the keydown listener from the element it was ADDED to, even after the root was replaced', () => {
    const added = [];
    const removed = [];
    const makeRoot = name => ({ name, addEventListener: (t, f) => added.push([name, t, f]), removeEventListener: (t, f) => removed.push([name, t, f]) });
    const rootA = makeRoot('A');
    const rootB = makeRoot('B');

    attachSpaceGuard(rootA, 'dlg-6');
    attachSpaceGuard(rootA, 'dlg-6');
    assert.equal(added.length, 1, 'idempotent for the same root');

    attachSpaceGuard(rootB, 'dlg-6');
    assert.deepEqual(removed.map(r => r[0]), ['A'], 'the replaced root is released');
    assert.equal(added.length, 2);

    detachSpaceGuard('dlg-6');
    assert.deepEqual(removed.map(r => r[0]), ['A', 'B']);
    assert.equal(removed[1][2], added[1][2], 'the same handler function that was added');
    assert.doesNotThrow(() => detachSpaceGuard('dlg-6'));
});