import test from 'node:test';
import assert from 'node:assert/strict';
import { focusElement, insertAtCursor } from '../signing-editor.js';

// ── focusElement ────────────────────────────────────────────────

/** A focusable element double: `focus` moves the document's `activeElement`, so the
    "does the focus really land" check follows the real contract. */
function makeDoc(elements, selectorToElement) {
    const doc = {
        activeElement: null,
        querySelector(selector) {
            if (selector === '###') {
                throw new Error('malformed selector');
            }
            return selectorToElement(selector) ?? null;
        }
    };
    for (const element of elements) {
        element.focus = function () {
            doc.activeElement = this;
        };
    }
    return doc;
}

test('focusElement focuses the match and reports success', () => {
    const button = { focusable: true };
    const doc = makeDoc([button], (selector) => selector === '.row .remove' ? button : null);

    assert.equal(focusElement('.row .remove', { document: doc }), true);
    assert.equal(doc.activeElement, button);
});

test('focusElement without a match reports false', () => {
    const doc = makeDoc([], () => null);

    assert.equal(focusElement('.missing', { document: doc }), false);
});

test('focusElement with a malformed selector reports false instead of throwing', () => {
    const doc = makeDoc([], () => null);

    assert.equal(focusElement('###', { document: doc }), false);
});

test('focusElement with an empty selector reports false', () => {
    const doc = makeDoc([], () => {
        throw new Error('never queried');
    });

    assert.equal(focusElement('', { document: doc }), false);
});

test('focusElement without a document reports false', () => {
    assert.equal(focusElement('.anything', { document: null }), false);
});

test('focusElement without options falls back to the global document', () => {
    // The node lane has no DOM — the module must take the injectable route and report false.
    assert.equal(focusElement('.anything'), false);
});

// ── insertAtCursor ──────────────────────────────────────────────

// A minimal <textarea> double: the real element's value/selection contract, without a DOM.
const makeTextarea = (value, selectionStart = value.length, selectionEnd = selectionStart) => ({
    value,
    selectionStart,
    selectionEnd,
    setSelectionRange(start, end) {
        this.selectionStart = start;
        this.selectionEnd = end;
    }
});

test('inserts the fragment at the caret inside the text', () => {
    const textarea = makeTextarea('ABCD', 2);
    const next = insertAtCursor(textarea, '{{Tax}}');

    assert.equal(next, 'AB{{Tax}}CD');
    assert.equal(textarea.value, 'AB{{Tax}}CD');
});

test('inserts into an empty textarea', () => {
    const textarea = makeTextarea('');
    const next = insertAtCursor(textarea, '1 + 2');

    assert.equal(next, '1 + 2');
});

test('replaces the active selection', () => {
    const textarea = makeTextarea('ABxCD', 2, 4);
    const next = insertAtCursor(textarea, '*');

    assert.equal(next, 'AB*D');
});

test('moves the caret past the inserted fragment', () => {
    const textarea = makeTextarea('AB', 2);
    insertAtCursor(textarea, '{{Tax}}');

    assert.equal(textarea.selectionStart, 2 + '{{Tax}}'.length);
    assert.equal(textarea.selectionEnd, textarea.selectionStart);
});

test('consecutive insertions keep appending in order', () => {
    const textarea = makeTextarea('', 0);
    insertAtCursor(textarea, '1');
    insertAtCursor(textarea, ' + ');
    insertAtCursor(textarea, '2');

    assert.equal(textarea.value, '1 + 2');
});

test('a missing textarea reports null (the caller appends instead)', () => {
    assert.equal(insertAtCursor(null, 'x'), null);
    assert.equal(insertAtCursor(undefined, 'x'), null);
    assert.equal(insertAtCursor({}, 'x'), null, 'a host without a querySelector is not a wrapper');
    assert.equal(insertAtCursor({ querySelector: () => null }, 'x'), null, 'a wrapper without a textarea');
});

test('a wrapper element hands over its textarea', () => {
    const textarea = makeTextarea('AB', 1);
    const host = {
        querySelector(selector) {
            assert.equal(selector, 'textarea');
            return textarea;
        }
    };

    assert.equal(insertAtCursor(host, 'X'), 'AXB');
});
