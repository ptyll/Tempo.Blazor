// Space scroll guard + roving focus of TmDocumentOpenDialog (colocated module). Hand-rolled DOM stubs, no jsdom.
import test from 'node:test';
import assert from 'node:assert/strict';
import { shouldSuppressSpaceScroll, shouldSuppressNavigationScroll, attachSpaceGuard, detachSpaceGuard, findRovingTarget, focusRoving, getGridColumns } from '../../../Components/Files/TmDocumentOpenDialog.razor.js';

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
