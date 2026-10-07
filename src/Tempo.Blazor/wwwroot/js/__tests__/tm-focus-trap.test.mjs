// Focus-trap tests with hand-rolled DOM stubs, the same approach as overlay-lifecycle.test.mjs:
// no jsdom. The stubs only implement what the module touches.
import test from 'node:test';
import assert from 'node:assert/strict';
import { activate, deactivate, isInnermost, isTopmost, __resetForTests } from '../tm-focus-trap.js';

test.beforeEach(() => __resetForTests());

function element(parent = null) {
    const listeners = new Map();
    const el = {
        parent,
        // The module walks parentElement, the real DOM property; the stub stores the link as parent.
        get parentElement() { return parent; },
        _children: [],
        attributes: new Set(),
        offsetParent: {},
        focusCalls: 0,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        contains(other) {
            for (let node = other; node; node = node.parent) if (node === el) return true;
            return false;
        },
        querySelectorAll() { return []; },
        hasAttribute(name) { return el.attributes.has(name); },
        toggleAttribute(name, on) { if (on) el.attributes.add(name); else el.attributes.delete(name); },
        get children() { return el._children; },
        set children(value) { el._children = value; },
        focus() { el.focusCalls++; },
        dispatch(type, event) { listeners.get(type)?.(event); },
    };
    if (parent) parent.children.push(el);
    return el;
}

function installDom(body) {
    body.children = body.children ?? [];
    // A list per type: nested traps each register their own listener, and the real document keeps
    // all of them.
    const documentListeners = new Map();
    globalThis.document = {
        activeElement: body,
        body,
        addEventListener(type, fn) {
            const list = documentListeners.get(type) ?? [];
            list.push(fn);
            documentListeners.set(type, list);
        },
        removeEventListener(type, fn) {
            const list = documentListeners.get(type) ?? [];
            documentListeners.set(type, list.filter(registered => registered !== fn));
        },
        dispatch(type, event) {
            for (const fn of documentListeners.get(type) ?? []) fn(event);
        },
    };
}

function handler() {
    const calls = [];
    return { calls, invokeMethodAsync(name) { calls.push(name); return Promise.resolve(); } };
}

test('a modal trap marks the background inert and leaves its own ancestors alone', () => {
    const body = element();
    const page = element(body);
    const overlay = element(body);
    const dialog = element(overlay);
    installDom(body);

    activate(dialog, 'sheet', null, false, null, true);

    assert.equal(page.hasAttribute('inert'), true, 'the page behind the sheet must be unreachable');
    assert.equal(overlay.hasAttribute('inert'), false, 'the sheet must not inert its own container');
    assert.equal(dialog.hasAttribute('inert'), false);
});

test('a non-modal trap leaves the background usable', () => {
    const body = element();
    const page = element(body);
    const drawer = element(body);
    installDom(body);

    activate(drawer, 'inline', null, false, null, false);

    assert.equal(page.hasAttribute('inert'), false);
});

test('only the innermost trap cycles Tab and only the topmost handles Escape', () => {
    const body = element();
    const outer = element(body);
    const inner = element(outer);
    installDom(body);
    const outerEscape = handler();
    const innerEscape = handler();

    activate(outer, 'outer', outerEscape, true, null, true);
    activate(inner, 'inner', innerEscape, true, null, true);

    assert.equal(isInnermost('outer'), false);
    assert.equal(isInnermost('inner'), true);
    assert.equal(isTopmost('inner'), true);

    const tab = { key: 'Tab', preventDefault() { tab.prevented = true; } };
    outer.dispatch('keydown', tab);
    assert.equal(tab.prevented, undefined, 'the outer trap must not cycle Tab while a dialog is nested in it');

    document.dispatch('keydown', { key: 'Escape' });
    assert.deepEqual(innerEscape.calls, ['HandleFocusTrapEscapeAsync']);
    assert.deepEqual(outerEscape.calls, [], 'the sheet behind must not close on the same Escape');
});

test('closing the inner trap hands Escape back to the outer one and keeps the background inert', () => {
    const body = element();
    const page = element(body);
    const outer = element(body);
    const inner = element(outer);
    installDom(body);
    const outerEscape = handler();

    activate(outer, 'outer', outerEscape, true, null, true);
    activate(inner, 'inner', handler(), true, null, true);
    deactivate('inner');

    document.dispatch('keydown', { key: 'Escape' });
    assert.deepEqual(outerEscape.calls, ['HandleFocusTrapEscapeAsync']);
    assert.equal(page.hasAttribute('inert'), true, 'the page stays inert while the sheet is still open');

    deactivate('outer');
    assert.equal(page.hasAttribute('inert'), false);
});

test('deactivation restores focus to the named target', () => {
    const body = element();
    const dialog = element(body);
    const canvas = element(body);
    installDom(body);

    activate(dialog, 'scope', null, false, canvas, true);
    deactivate('scope');

    assert.equal(canvas.focusCalls, 1);
});
