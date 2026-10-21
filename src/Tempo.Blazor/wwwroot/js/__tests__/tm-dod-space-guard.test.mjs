// Space scroll guard of TmDocumentOpenDialog (colocated module). Hand-rolled DOM stubs, no jsdom.
import test from 'node:test';
import assert from 'node:assert/strict';
import { shouldSuppressSpaceScroll, attachSpaceGuard, detachSpaceGuard } from '../../../Components/Files/TmDocumentOpenDialog.razor.js';

const element = classes => ({ matches: selector => selector.split(',').map(s => s.trim()).some(s => classes.includes(s.replace('.', ''))) });

test('Space on a list row or a grid card is suppressed (no page scroll)', () => {
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-row']) }), true);
    assert.equal(shouldSuppressSpaceScroll({ key: ' ', target: element(['tm-dod-card']) }), true);
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