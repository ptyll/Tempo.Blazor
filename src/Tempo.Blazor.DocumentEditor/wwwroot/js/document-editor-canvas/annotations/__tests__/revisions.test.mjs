import assert from 'node:assert/strict';
import test from 'node:test';
import { canCreateRestrictedSuggestion, canEditRestrictedSelection } from '../restricted-editing.mjs';
import { applyReviewDecision, applyReviewDecisionAll, buildRevisionMarkers, createCanvasRevisionOverlay } from '../revision-render.mjs';
import { applyDeletionRevision, applyFormattingRevision } from '../track-changes.mjs';

test('revision render emits insertion deletion and formatting markers with review modes', () => {
    const model = createModel();
    const render = createRender();

    const all = buildRevisionMarkers(model, render, { reviewMode: 'allMarkup' });
    const simple = buildRevisionMarkers(model, render, { reviewMode: 'simpleMarkup' });
    const final = buildRevisionMarkers(model, render, { reviewMode: 'noMarkup' });
    const original = buildRevisionMarkers(model, render, { reviewMode: 'original' });

    assert.deepEqual(all.map(marker => marker.type), ['insertion', 'deletion', 'formatting']);
    assert.equal(simple.every(marker => marker.rect.width <= 4), true);
    assert.equal(final.length, 0);
    assert.deepEqual(original.map(marker => marker.type), ['deletion', 'formatting']);
});

test('accept and reject one revision update model content and pending action', () => {
    const rejectedInsertion = applyReviewDecision(createModel(), 'rev-insert', 'rejected');
    const acceptedDeletion = applyReviewDecision(createModel(), 'rev-delete', 'accepted');

    assert.equal(rejectedInsertion.changed, true);
    assert.equal(blockText(rejectedInsertion.model, 'p1').includes('New'), false);
    assert.equal(rejectedInsertion.model.revisions.find(revision => revision.id === 'rev-insert').action, 'rejected');

    assert.equal(acceptedDeletion.changed, true);
    assert.equal(blockText(acceptedDeletion.model, 'p1').includes('Old'), false);
    assert.equal(acceptedDeletion.model.revisions.find(revision => revision.id === 'rev-delete').action, 'accepted');
});

test('reviewing all revisions renders no markers, even when stale revision anchors remain', () => {
    const reviewed = applyReviewDecisionAll(createModel(), 'rejected', {});

    assert.equal(reviewed.changed, true);
    assert.equal(reviewed.model.revisions.every(revision => String(revision.action).toLowerCase() !== 'pending'), true);
    // A marker must only ever represent a PENDING revision. The display list can still carry revisionAnchor
    // commands for the just-reviewed revisions until the next relayout, so building markers against them must
    // not resurrect a marker for a revision that is no longer pending (Phase17 reject-all marker bug).
    const markers = buildRevisionMarkers(reviewed.model, createRender(), { reviewMode: 'allMarkup' });
    assert.equal(markers.length, 0, `expected no markers after reviewing all revisions, got ${markers.length}`);
});

test('track deletion keeps cross-block text visible with deletion revisions', () => {
    const model = createPlainModel();
    const result = applyDeletionRevision(model, {
        anchor: { blockId: 'a', offset: 2 },
        focus: { blockId: 'b', offset: 3 },
    }, { author: { id: 'u1', displayName: 'Author' } });

    assert.equal(result.changed, true);
    assert.equal(result.revisions.length, 2);
    assert.equal(model.body.blocks[0].content.runs.some(run => hasRevision(run, 'Deletion')), true);
    assert.equal(model.body.blocks[1].content.runs.some(run => hasRevision(run, 'Deletion')), true);
});

test('formatting tracked change creates formatting revision on selected range', () => {
    const model = createPlainModel();
    const result = applyFormattingRevision(model, {
        anchor: { blockId: 'a', offset: 1 },
        focus: { blockId: 'a', offset: 4 },
    }, 'bold');

    assert.equal(result.changed, true);
    assert.equal(model.revisions[0].type, 'Formatting');
    assert.equal(model.body.blocks[0].content.runs.some(run => hasRevision(run, 'Formatting')), true);
});

test('restricted editing allows only configured editable regions in protected documents', () => {
    const model = {
        isProtected: true,
        restrictedMarkers: [{
            id: 'region-1',
            startBlockId: 'a',
            startOffset: 2,
            endBlockId: 'a',
            endOffset: 6,
        }],
    };

    assert.equal(canEditRestrictedSelection(model, {
        anchor: { blockId: 'a', offset: 3 },
        focus: { blockId: 'a', offset: 5 },
    }).allowed, true);
    assert.equal(canEditRestrictedSelection(model, {
        anchor: { blockId: 'a', offset: 0 },
        focus: { blockId: 'a', offset: 5 },
    }).allowed, false);
});

test('revision overlay keeps DOM order and element identity when y swaps', () => {
    const document = createFakeDocument();
    const overlay = createCanvasRevisionOverlay({ document });
    const model = {
        revisions: [
            { id: 'rev-a', type: 'Insertion', action: 'Pending', range: { blockId: 'p1', startOffset: 0, endOffset: 3 } },
            { id: 'rev-b', type: 'Insertion', action: 'Pending', range: { blockId: 'p1', startOffset: 3, endOffset: 6 } },
        ],
    };
    const renderAt = (aY, bY) => ({
        displayList: {
            commands: [
                { type: 'revisionAnchor', revisionId: 'rev-a', blockId: 'p1', pageIndex: 0, x: 10, y: aY, width: 20, height: 18 },
                { type: 'revisionAnchor', revisionId: 'rev-b', blockId: 'p1', pageIndex: 0, x: 10, y: bY, width: 20, height: 18 },
            ],
        },
    });

    overlay.update(model, renderAt(563, 1122));
    const first = [...overlay.root.children];
    overlay.update(model, renderAt(1122, 812));
    const second = [...overlay.root.children];

    assert.deepEqual(
        first.map(el => el.getAttribute('data-revision-id')),
        ['rev-a', 'rev-b'],
        'markers must follow revisionsOf(model) order, not rect.y');
    assert.deepEqual(
        second.map(el => el.getAttribute('data-revision-id')),
        ['rev-a', 'rev-b'],
        'a y swap must not reorder the DOM (Accept would hit the neighbour)');
    assert.equal(second[0], first[0], 'rev-a element instance must survive the re-render (===)');
    assert.equal(second[1], first[1], 'rev-b element instance must survive the re-render (===)');
});

test('restricted editing blocks suggestion provider changes outside editable regions', () => {
    const model = {
        isProtected: true,
        restrictedMarkers: [{
            id: 'region-1',
            startBlockId: 'a',
            startOffset: 2,
            endBlockId: 'a',
            endOffset: 6,
        }],
    };

    const allowed = canCreateRestrictedSuggestion(model, {
        range: { blockId: 'a', startOffset: 3, endOffset: 5 },
        operations: [{ target: { blockId: 'a', offset: 3, length: 2 } }],
    });
    const blocked = canCreateRestrictedSuggestion(model, {
        range: { blockId: 'a', startOffset: 0, endOffset: 5 },
        operations: [{ target: { blockId: 'a', offset: 0, length: 5 } }],
    });

    assert.equal(allowed.allowed, true);
    assert.equal(allowed.markerId, 'region-1');
    assert.equal(blocked.allowed, false);
    assert.equal(blocked.reason, 'outsideEditableRegion');
});

function createModel() {
    return {
        revisions: [
            { id: 'rev-insert', type: 'Insertion', action: 'Pending', range: { blockId: 'p1', startOffset: 0, endOffset: 3 } },
            { id: 'rev-delete', type: 'Deletion', action: 'Pending', range: { blockId: 'p1', startOffset: 3, endOffset: 6 } },
            { id: 'rev-format', type: 'Formatting', action: 'Pending', range: { blockId: 'p1', startOffset: 6, endOffset: 10 } },
        ],
        body: {
            blocks: [{
                id: 'p1',
                type: 'paragraph',
                content: {
                    runs: [
                        { id: 'insert', type: 'text', text: 'New', marks: [{ type: 'revision', revisionId: 'rev-insert', value: 'Insertion' }] },
                        { id: 'delete', type: 'text', text: 'Old', marks: [{ type: 'revision', revisionId: 'rev-delete', value: 'Deletion' }] },
                        { id: 'format', type: 'text', text: 'Bold', marks: [{ type: 'bold' }, { type: 'revision', revisionId: 'rev-format', value: 'Formatting' }] },
                    ],
                },
            }],
        },
    };
}

function createPlainModel() {
    return {
        revisions: [],
        body: {
            blocks: [
                { id: 'a', type: 'paragraph', content: { runs: [{ id: 'a-run', type: 'text', text: 'Alpha', marks: [] }] } },
                { id: 'b', type: 'paragraph', content: { runs: [{ id: 'b-run', type: 'text', text: 'Beta', marks: [] }] } },
            ],
        },
    };
}

function createRender() {
    return {
        displayList: {
            commands: [
                { type: 'revisionAnchor', revisionId: 'rev-insert', blockId: 'p1', pageIndex: 0, x: 100, y: 100, width: 28, height: 18 },
                { type: 'revisionAnchor', revisionId: 'rev-delete', blockId: 'p1', pageIndex: 0, x: 128, y: 100, width: 24, height: 18 },
                { type: 'revisionAnchor', revisionId: 'rev-format', blockId: 'p1', pageIndex: 0, x: 152, y: 100, width: 36, height: 18 },
            ],
        },
    };
}

function blockText(model, blockId) {
    return model.body.blocks.find(block => block.id === blockId).content.runs.map(run => run.text || '').join('');
}

function hasRevision(run, value) {
    return run.marks.some(mark => mark.type === 'revision' && mark.value === value);
}

function createFakeDocument() {
    return {
        createElement(tagName) {
            return new FakeElement(String(tagName).toUpperCase());
        },
    };
}

class FakeElement {
    constructor(tagName) {
        this.tagName = tagName;
        this.children = [];
        this.attributes = new Map();
        this.style = {};
        this.parentNode = null;
        this.className = '';
        this.type = '';
    }

    get firstChild() {
        return this.children[0] || null;
    }

    get nextSibling() {
        if (!this.parentNode) return null;
        const siblings = this.parentNode.children;
        const index = siblings.indexOf(this);
        return index >= 0 ? (siblings[index + 1] || null) : null;
    }

    appendChild(child) {
        if (child.parentNode) child.parentNode.removeChild(child);
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    insertBefore(child, reference) {
        if (child.parentNode) child.parentNode.removeChild(child);
        child.parentNode = this;
        const index = reference ? this.children.indexOf(reference) : -1;
        if (index >= 0) this.children.splice(index, 0, child);
        else this.children.push(child);
        return child;
    }

    removeChild(child) {
        const index = this.children.indexOf(child);
        if (index >= 0) this.children.splice(index, 1);
        child.parentNode = null;
        return child;
    }

    replaceChildren(...children) {
        for (const child of [...this.children]) {
            this.removeChild(child);
        }
        for (const child of children) {
            this.appendChild(child);
        }
    }

    setAttribute(name, value) {
        this.attributes.set(String(name), String(value));
    }

    getAttribute(name) {
        return this.attributes.get(String(name)) ?? null;
    }
}
