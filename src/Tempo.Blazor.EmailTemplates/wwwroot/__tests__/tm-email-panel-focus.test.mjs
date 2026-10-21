import assert from 'node:assert/strict';
import test from 'node:test';

import { focusBySelector, focusRailExpandedToggle } from '../tm-email-panel-focus.js';

function fakeTarget() {
    return {
        isConnected: true,
        focused: false,
        focus() { this.focused = true; },
    };
}

function withDocument(activeElement, body, run) {
    const previous = globalThis.document;
    globalThis.document = { activeElement, body };
    try {
        return run();
    } finally {
        if (previous === undefined) delete globalThis.document;
        else globalThis.document = previous;
    }
}

test('moves focus when the focused element is the body', () => {
    const target = fakeTarget();
    const root = { querySelector: (selector) => (selector.includes('--right') ? target : null) };
    const body = { isConnected: true };
    const moved = withDocument(body, body, () => focusRailExpandedToggle(root, '.tm-editor-shell__panel-toggle--right'));
    assert.equal(moved, true);
    assert.equal(target.focused, true);
});

test('moves focus when the focused element was detached (the unmounted rail button)', () => {
    const target = fakeTarget();
    const root = { querySelector: () => target };
    const moved = withDocument({ isConnected: false }, { isConnected: true }, () =>
        focusRailExpandedToggle(root, '.tm-editor-shell__panel-toggle--right'));
    assert.equal(moved, true);
    assert.equal(target.focused, true);
});

test('moves focus when the focused element still sits inside a rail', () => {
    const target = fakeTarget();
    const root = { querySelector: () => target };
    const active = { isConnected: true, closest: (selector) => (selector === '.tm-editor-shell__rail' ? {} : null) };
    const moved = withDocument(active, { isConnected: true }, () =>
        focusRailExpandedToggle(root, '.tm-editor-shell__panel-toggle--right'));
    assert.equal(moved, true);
});

test('never steals a focus the user placed on another live control', () => {
    const target = fakeTarget();
    const root = { querySelector: () => target };
    const active = { isConnected: true, closest: () => null };
    const moved = withDocument(active, { isConnected: true }, () =>
        focusRailExpandedToggle(root, '.tm-editor-shell__panel-toggle--right'));
    assert.equal(moved, false);
    assert.equal(target.focused, false);
});

test('does nothing when the expanded toggle is not rendered', () => {
    const root = { querySelector: () => null };
    const moved = withDocument(null, { isConnected: true }, () =>
        focusRailExpandedToggle(root, '.tm-editor-shell__panel-toggle--right'));
    assert.equal(moved, false);
});

test('focusBySelector moves the focus onto the matching action of the moved item', () => {
    const target = fakeTarget();
    let asked = null;
    const root = { querySelector: (selector) => { asked = selector; return target; } };
    const moved = focusBySelector(root, "[data-tm-item-down][data-tm-item-index='2']");
    assert.equal(moved, true);
    assert.equal(target.focused, true);
    assert.equal(asked, "[data-tm-item-down][data-tm-item-index='2']");
});

test('focusBySelector never throws when the action is gone', () => {
    assert.equal(focusBySelector({ querySelector: () => null }, '[data-tm-item-up]'), false);
    assert.equal(focusBySelector(null, '[data-tm-item-up]'), false);
    const detached = { isConnected: false, focus() { throw new Error('must not focus'); } };
    assert.equal(focusBySelector({ querySelector: () => detached }, '[data-tm-item-up]'), false);
});
