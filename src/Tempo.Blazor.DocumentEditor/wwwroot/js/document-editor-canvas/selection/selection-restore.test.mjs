import assert from 'node:assert/strict';
import test from 'node:test';
import { modelHasBlock, selectionToRestoreAfterModelRefresh } from './selection-restore.mjs';

const text = (id, value) => ({ id, type: 'paragraph', content: { type: 'paragraph', runs: [{ id: `${id}-r`, type: 'text', text: value }] } });
const withImage = (id, objectId) => ({
    id,
    type: 'paragraph',
    content: { type: 'paragraph', runs: [{ id: `${id}-t`, type: 'text', text: 'ab' }, { id: `${id}-img`, type: 'image', object: { objectId } }] },
});

function model(extra = []) {
    return {
        documentId: 'doc-1',
        body: {
            blocks: [
                text('heading', 'Title'),
                {
                    id: 'table',
                    type: 'table',
                    content: { table: { rows: [{ cells: [{ id: 'c1', blocks: [text('c1-p', 'hello')] }, { id: 'c2', blocks: [text('c2-p', 'x')] }] }] } },
                },
                ...extra,
            ],
        },
    };
}

const preserve = { preserveSelection: true };
const caret = (blockId, offset) => ({ anchor: { blockId, offset }, focus: { blockId, offset } });
const atStart = caret('heading', 0);

test('modelHasBlock finds top-level and table-cell blocks', () => {
    assert.equal(modelHasBlock(model(), 'heading'), true);
    assert.equal(modelHasBlock(model(), 'c2-p'), true);
    assert.equal(modelHasBlock(model(), 'gone'), false);
    assert.equal(modelHasBlock(model(), ''), false);
    assert.equal(modelHasBlock(null, 'heading'), false);
});

test('a mirror refresh of the SAME document gives the caret back (it is not reset to the start)', () => {
    const restored = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('c1-p', 2), model(), atStart, preserve);
    assert.deepEqual(restored, caret('c1-p', 2));
});

test('a push that is not a mirror refresh restores nothing (accept / reject, template, import, version preview)', () => {
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('c1-p', 2), model(), atStart), null);
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('c1-p', 2), model(), atStart, {}), null);
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('c1-p', 2), model(), atStart, { preserveSelection: false }), null);
});

test('offsets are clamped to the text of the NEW model', () => {
    const restored = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, { anchor: { blockId: 'c1-p', offset: 2 }, focus: { blockId: 'c2-p', offset: 40 } }, model(), atStart, preserve);
    assert.equal(restored.anchor.offset, 2);
    assert.equal(restored.focus.offset, 1, 'c2-p now holds one character');
    const shrunk = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('c1-p', 99), model(), atStart, preserve);
    assert.equal(shrunk.focus.offset, 5);
    const negative = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('c1-p', -3), model(), atStart, preserve);
    assert.equal(negative.anchor.offset, 0);
});

test('an inline object counts one offset position, like the clipboard offsets', () => {
    const restored = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('pic', 9), model([withImage('pic', 'img-1')]), atStart, preserve);
    assert.equal(restored.focus.offset, 3, 'two characters plus one object');
});

test('nothing to restore when the selection survived the refresh', () => {
    const same = caret('c1-p', 2);
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, same, model(), caret('c1-p', 2), preserve), null);
});

test('a different document never inherits the previous caret', () => {
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'other' }, caret('c1-p', 1), model(), atStart, preserve), null);
    assert.equal(selectionToRestoreAfterModelRefresh(null, caret('c1-p', 1), model(), atStart, preserve), null);
});

test('a caret in a block that no longer exists is dropped', () => {
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, caret('gone', 0), model(), atStart, preserve), null);
    assert.equal(selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, null, model(), atStart, preserve), null);
});

test('an object selection is kept while its object exists and dropped (caret kept) when it is gone', () => {
    const selected = { ...caret('pic', 2), object: { objectId: 'img-1', blockId: 'pic' } };
    const kept = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, selected, model([withImage('pic', 'img-1')]), atStart, preserve);
    assert.equal(kept.object.objectId, 'img-1');

    const removed = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, selected, model([withImage('pic', 'img-2')]), atStart, preserve);
    assert.equal('object' in removed, false, 'the object id is gone from its block: no object selection');
    assert.equal(removed.focus.blockId, 'pic');

    const blockGone = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, selected, model(), atStart, preserve);
    assert.equal(blockGone, null);
});

test('a math slot is dropped when its math id is gone', () => {
    const selected = { ...caret('heading', 1), math: { mathId: 'm-1', blockId: 'heading', runId: 'x' } };
    const restored = selectionToRestoreAfterModelRefresh({ documentId: 'doc-1' }, selected, model(), atStart, preserve);
    assert.equal(restored.math, undefined);
    assert.equal(restored.focus.offset, 1);
});
