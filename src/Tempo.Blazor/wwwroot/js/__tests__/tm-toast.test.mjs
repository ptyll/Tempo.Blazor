// tm-toast.js tests with hand-rolled DOM stubs (same approach as tm-toast-visibility.test.mjs):
// no jsdom. Covers the contract the bUnit tests cannot reach: Escape on a card dismisses it via
// DismissToast with tmEscapeHandled/preventDefault/stopPropagation, Escape outside a card is
// ignored, focusin relatedTarget is remembered and restored on card removal (only while
// connected), the Alt+T hotkey focuses the NEWEST action button, and attach/detach are clean.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attach, detach } from '../tm-toast.js';

function makeDoc() {
    const listeners = new Map();
    return {
        activeElement: null,
        body: { isConnected: true },
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type, fn) { listeners.delete(type, fn); },
        has(type) { return listeners.has(type); },
        keydown(event) { listeners.get('keydown')?.(event); },
    };
}

function makeElement(overrides = {}) {
    return {
        isConnected: true,
        focused: false,
        focus() { this.focused = true; },
        ...overrides,
    };
}

function makeCard(id) {
    return {
        nodeType: 1,
        isConnected: true,
        dataset: { toastId: id },
        classList: { contains: c => c === 'tm-toast' },
        closest(selector) { return selector === '.tm-toast' ? this : null; },
        querySelector() { return null; },
    };
}

function makeContainer(doc) {
    const listeners = new Map();
    const cards = [];
    return {
        cards,
        ownerDocument: doc,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type, fn) { listeners.delete(type, fn); },
        contains(node) { return cards.includes(node); },
        querySelectorAll(selector) {
            return selector === '.tm-toast-action' ? this.actionButtons : [];
        },
        actionButtons: [],
        appendCard(card) { cards.push(card); },
        removeCard(card) {
            const index = cards.indexOf(card);
            if (index >= 0) cards.splice(index, 1);
            this.lastMutations = [{ removedNodes: [card] }];
        },
        keydown(event) { listeners.get('keydown')?.(event); },
        focusin(event) { listeners.get('focusin')?.(event); },
    };
}

class CapturingObserver {
    static last = null;
    constructor(cb) { this.cb = cb; CapturingObserver.last = this; }
    observe() { }
    disconnect() { this.disconnected = true; }
    trigger(mutations) { this.cb(mutations); }
}

function dotnetStub() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(method, ...args) {
            calls.push({ method, args });
            return Promise.resolve();
        },
    };
}

function escapeEvent(target) {
    return {
        key: 'Escape',
        target,
        prevented: false,
        stopped: false,
        preventDefault() { this.prevented = true; },
        stopPropagation() { this.stopped = true; },
    };
}

test('Escape on a toast card dismisses it: tmEscapeHandled, preventDefault, stopPropagation, DismissToast', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const card = makeCard('abc123');
    container.appendCard(card);
    const dotNet = dotnetStub();
    attach(container, dotNet, doc, CapturingObserver);

    const e = escapeEvent(card);
    container.keydown(e);

    assert.equal(e.tmEscapeHandled, true, 'capture-phase consumers must see the flag and stand down');
    assert.equal(e.prevented, true);
    assert.equal(e.stopped, true);
    assert.deepEqual(dotNet.calls, [{ method: 'DismissToast', args: ['abc123'] }]);
});

test('Escape outside a toast card is ignored', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const dotNet = dotnetStub();
    attach(container, dotNet, doc, CapturingObserver);

    const outside = { closest: () => null };
    const e = escapeEvent(outside);
    container.keydown(e);

    assert.equal(e.tmEscapeHandled, undefined);
    assert.equal(e.prevented, false);
    assert.deepEqual(dotNet.calls, []);
});

test('focusin remembers relatedTarget; removing its card returns focus while connected', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const card = makeCard('x');
    container.appendCard(card);
    attach(container, dotnetStub(), doc, CapturingObserver);

    const origin = makeElement();
    container.focusin({ relatedTarget: origin });
    doc.activeElement = doc.body; // the removed card dropped focus back to <body>

    container.removeCard(card);
    CapturingObserver.last.trigger(container.lastMutations);

    assert.equal(origin.focused, true, 'focus returns to the element that was focused before the card');
    assert.equal(CapturingObserver.last.disconnected, undefined, 'the observer keeps watching');
});

test('focus is NOT restored when the remembered element is disconnected', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const card = makeCard('x');
    container.appendCard(card);
    attach(container, dotnetStub(), doc, CapturingObserver);

    const gone = makeElement({ isConnected: false });
    container.focusin({ relatedTarget: gone });
    doc.activeElement = doc.body;

    container.removeCard(card);
    CapturingObserver.last.trigger(container.lastMutations);

    assert.equal(gone.focused, false, 'a detached element can never take focus back');
});

test('focus is NOT restored while focus already landed inside the container', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const card = makeCard('x');
    const other = makeCard('y');
    container.appendCard(card);
    container.appendCard(other);
    attach(container, dotnetStub(), doc, CapturingObserver);

    const origin = makeElement();
    container.focusin({ relatedTarget: origin });
    doc.activeElement = other; // focus moved to another toast card

    container.removeCard(card);
    CapturingObserver.last.trigger(container.lastMutations);

    assert.equal(origin.focused, false, 'focus already lives inside the container — do not hijack it');
});

test('Alt+T focuses the NEWEST action button and skips editable targets', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const first = makeElement();
    const newest = makeElement();
    container.actionButtons = [first, newest];
    attach(container, dotnetStub(), doc, CapturingObserver);

    const e = {
        key: 't', altKey: true, target: { closest: () => null }, prevented: false,
        preventDefault() { this.prevented = true; },
    };
    doc.keydown(e);

    assert.equal(e.prevented, true);
    assert.equal(newest.focused, true, 'the newest action button wins');
    assert.equal(first.focused, false);

    const typing = {
        key: 'T', altKey: true, target: { closest: sel => sel.includes('input') ? {} : null }, prevented: false,
        preventDefault() { this.prevented = true; },
    };
    doc.keydown(typing);
    assert.equal(typing.prevented, false, 'Alt+T never hijacks typing in a field');
});

test('Alt+T without any action button is a no-op', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    attach(container, dotnetStub(), doc, CapturingObserver);

    const e = { key: 't', altKey: true, target: { closest: () => null }, prevented: false, preventDefault() { this.prevented = true; } };
    doc.keydown(e);
    assert.equal(e.prevented, false);
});

test('detach removes every listener and the observer; re-attach replaces wiring', () => {
    const doc = makeDoc();
    const container = makeContainer(doc);
    const card = makeCard('x');
    container.appendCard(card);

    attach(container, dotnetStub(), doc, CapturingObserver);
    const firstObserver = CapturingObserver.last;
    attach(container, dotnetStub(), doc, CapturingObserver);
    assert.equal(firstObserver.disconnected, true, 're-attach disconnects the previous observer');

    detach(container, doc);
    assert.equal(CapturingObserver.last.disconnected, true);
    assert.equal(doc.has('keydown'), false, 'the document hotkey listener is gone');

    const e = escapeEvent(card);
    container.keydown(e);
    assert.equal(e.prevented, false, 'nothing fires after detach');
});
