// tm-agent-run-tree.js tests — the scroll-key guard of the agent run treegrid.
//
// The guard exists because Blazor cannot decide preventDefault per key: a render-time
// @onkeydown:preventDefault is ONE bool for every keydown on the element, so it would also swallow Tab
// and trap the focus inside the grid. The module cancels exactly the navigation keys of a row of this
// grid — never Tab, never a key aimed at a control inside the row, never a browser shortcut — and it
// must not leak a listener when the component is disposed.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachScrollKeyGuard, detachScrollKeyGuard } from '../tm-agent-run-tree.js';

/** A scripted stand-in for the grid: an element plus a listener registry. */
class FakeElement {
    constructor({ matches = [], child = null } = {}) {
        this.listeners = new Map();
        this.matches = matches;
        this.child = child;
    }

    addEventListener(type, handler) {
        const entries = this.listeners.get(type) ?? [];
        entries.push(handler);
        this.listeners.set(type, entries);
    }

    removeEventListener(type, handler) {
        const entries = this.listeners.get(type) ?? [];
        const index = entries.indexOf(handler);
        if (index >= 0) entries.splice(index, 1);
    }

    dispatch(event) {
        for (const handler of this.listeners.get('keydown') ?? []) handler(event);
    }

    closest(selector) {
        if (selector === this.child?.controlSelector) return this.child.control;
        return this.matches.includes(selector) ? this : null;
    }
}

/** A keyboard event whose target is a row (or a control inside it). */
function keyEvent(key, { target, ctrlKey = false, altKey = false, metaKey = false } = {}) {
    const row = new FakeElement({ matches: ['.tm-ai-agent-run-tree__row'] });
    return {
        key,
        ctrlKey,
        altKey,
        metaKey,
        defaultPrevented: false,
        target: target ?? row,
        prevented: 0,
        preventDefault() {
            this.prevented += 1;
            this.defaultPrevented = true;
        },
    };
}

function guard() {
    const grid = new FakeElement();
    attachScrollKeyGuard(grid);
    return grid;
}

test('cancels the scroll default of every navigation key on a row', () => {
    const grid = guard();

    for (const key of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', ' ', 'Spacebar']) {
        const event = keyEvent(key);
        grid.dispatch(event);
        assert.equal(event.prevented, 1, `${key} must not scroll the page`);
    }
});

test('leaves Tab, Enter, Escape and typing alone', () => {
    const grid = guard();

    for (const key of ['Tab', 'Enter', 'Escape', 'a', 'PageDown', '+']) {
        const event = keyEvent(key);
        grid.dispatch(event);
        assert.equal(event.prevented, 0, `${key} keeps its browser behaviour`);
    }
});

test('ignores a key aimed outside a row of this grid', () => {
    const grid = guard();

    const elsewhere = new FakeElement({ matches: [] });
    const event = keyEvent('ArrowDown', { target: elsewhere });
    grid.dispatch(event);

    assert.equal(event.prevented, 0, 'the header and the host controls keep their keys');
});

test('ignores a key aimed at a control inside a row', () => {
    const grid = guard();

    const control = new FakeElement({ matches: ['.tm-ai-agent-run-tree__row'] });
    // The event target is both a row descendant and a control: `closest` returns it for both
    // selectors, so the control check is what has to win.
    const target = {
        closest: (selector) =>
            selector === 'button, input, textarea, select, a[href], [contenteditable="true"], [role="button"]'
                ? control
                : new FakeElement({ matches: ['.tm-ai-agent-run-tree__row'] }),
    };

    const event = keyEvent(' ', { target });
    grid.dispatch(event);

    assert.equal(event.prevented, 0, 'a native button inside a row owns its own Space');
});

test('ignores a browser shortcut such as Ctrl+ArrowDown', () => {
    const grid = guard();

    const event = keyEvent('ArrowDown', { ctrlKey: true });
    grid.dispatch(event);
    assert.equal(event.prevented, 0);

    const alt = keyEvent('ArrowDown', { altKey: true });
    grid.dispatch(alt);
    assert.equal(alt.prevented, 0);

    const meta = keyEvent('ArrowDown', { metaKey: true });
    grid.dispatch(meta);
    assert.equal(meta.prevented, 0);
});

test('never claims an already handled event', () => {
    const grid = guard();

    const event = keyEvent('ArrowDown');
    event.defaultPrevented = true;
    grid.dispatch(event);

    assert.equal(event.prevented, 0);
});

test('attaching twice keeps one listener', () => {
    const grid = guard();
    attachScrollKeyGuard(grid);

    const event = keyEvent('ArrowDown');
    grid.dispatch(event);

    assert.equal(event.prevented, 1);
    assert.equal(grid.listeners.get('keydown').length, 1);
});

test('honours a custom row selector', () => {
    const grid = new FakeElement();
    attachScrollKeyGuard(grid, '.custom-row');

    const own = keyEvent('ArrowDown');
    grid.dispatch(own);
    assert.equal(own.prevented, 0, 'a target matching the DEFAULT selector is not a row of this grid');

    const custom = keyEvent('ArrowDown', { target: new FakeElement({ matches: ['.custom-row'] }) });
    grid.dispatch(custom);
    assert.equal(custom.prevented, 1);

    const empty = keyEvent('ArrowDown');
    const emptyGrid = new FakeElement();
    attachScrollKeyGuard(emptyGrid, '   ');
    emptyGrid.dispatch(empty);
    assert.equal(empty.prevented, 0, 'a blank selector falls back to the component default');
});

test('detaching releases the listener, and is safe twice or on a stranger', () => {
    const grid = guard();
    detachScrollKeyGuard(grid);

    const event = keyEvent('ArrowDown');
    grid.dispatch(event);
    assert.equal(event.prevented, 0);

    detachScrollKeyGuard(grid);
    detachScrollKeyGuard(null);
    detachScrollKeyGuard({});
    attachScrollKeyGuard(null);
    attachScrollKeyGuard({});
});
