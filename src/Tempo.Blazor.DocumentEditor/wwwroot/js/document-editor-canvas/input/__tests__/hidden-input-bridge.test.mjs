import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createHiddenInputBridge } from '../hidden-input-bridge.mjs';

// A minimal DOM: createElement returns an element that records attributes and style.
function fakeDocument() {
    return {
        createElement: () => {
            const attributes = new Map();
            return {
                id: '',
                className: '',
                style: {},
                setAttribute: (name, value) => attributes.set(name, value),
                getAttribute: name => attributes.get(name),
                addEventListener() {},
                removeEventListener() {},
            };
        },
    };
}

// Phase 5 review: a host names the engine's input as the restore target of a dialog (TmModal RestoreFocusTargetId), so the bridge
// carries an id when it is given one.
test('the input carries the id the host asked for', () => {
    const bridge = createHiddenInputBridge({ document: fakeDocument(), id: 'tm-document-canvas-input-abc' });
    assert.equal(bridge.input.id, 'tm-document-canvas-input-abc');
});

test('without an id the input stays anonymous (no id attribute invented)', () => {
    const bridge = createHiddenInputBridge({ document: fakeDocument() });
    assert.equal(bridge.input.id, '');
});

test('the input keeps its test id and role', () => {
    const bridge = createHiddenInputBridge({ document: fakeDocument(), id: 'x' });
    assert.equal(bridge.input.getAttribute('data-testid'), 'document-canvas-hidden-input');
    assert.equal(bridge.input.getAttribute('role'), 'textbox');
});
