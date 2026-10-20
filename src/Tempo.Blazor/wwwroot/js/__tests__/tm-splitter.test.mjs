// TmSplitterPane module tests with hand-rolled DOM stubs (no jsdom): the size clamp the pointer
// drag and the C# keyboard path share, the live inline flex with ONE commit on release, the vetoed
// press on the collapse button, the verbatim start-style restore on every cancel path, RTL
// inversion of the horizontal delta, and the axis-only key prevention.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachResize, clampSize, detachResize, directionOf, measureSize, __resetForTests } from '../tm-splitter.js';

test.beforeEach(() => {
    __resetForTests();
    delete globalThis.document;
    delete globalThis.getComputedStyle;
});

// ── clampSize: the same vectors as the C# keyboard clamp ────────────────────────────────────

test('clampSize rounds and applies min/max only when finite', () => {
    assert.equal(clampSize(300.4, 0, Infinity), 300, 'whole pixels');
    assert.equal(clampSize(50, 100, 400), 100, 'never below the minimum');
    assert.equal(clampSize(900, 100, 400), 400, 'never above the maximum');
    assert.equal(clampSize(-20, 0, Infinity), 0, 'never negative');
    assert.equal(clampSize(500, 0, Infinity), 500, 'no maximum: unbounded growth');
});

// ── the pointer drag ────────────────────────────────────────────────────────────────────────

function listeners() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of map.get(type) ?? []) fn(event); },
        count(type) { return (map.get(type) ?? []).length; },
    };
}

/** A tiny inline-style parser so a restored style attribute is observable in the stub. */
function parseStyle(text) {
    const style = {};
    for (const part of String(text).split(';')) {
        const separator = part.indexOf(':');
        if (separator > 0) style[part.slice(0, separator).trim()] = part.slice(separator + 1).trim();
    }
    return style;
}

function stage({ width = 300, height = 200, style = null } = {}) {
    const attrs = new Map();
    const handle = {
        ...listeners(),
        captured: null,
        setAttribute: (name, value) => attrs.set(name, value),
        getAttribute: name => attrs.get(name) ?? null,
        classList: { add: name => attrs.set(`class:${name}`, true), remove: name => attrs.delete(`class:${name}`) },
        setPointerCapture(id) { handle.captured = id; },
        releasePointerCapture(id) { handle.captured = null; handle.released = id; },
    };
    const pane = {
        style: {},
        getBoundingClientRect: () => ({ width, height }),
        getAttribute: name => attrs.get(name) ?? null,
        setAttribute(name, value) {
            attrs.set(name, value);
            if (name === 'style') pane.style = parseStyle(value);
        },
        removeAttribute(name) {
            attrs.delete(name);
            if (name === 'style') pane.style = {};
        },
    };
    if (style !== null) pane.setAttribute('style', style);
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    return { handle, pane, attrs, calls, dotnet };
}

const down = (x, y, id = 7) => ({
    button: 0, clientX: x, clientY: y, pointerId: id, isPrimary: true, pointerType: 'mouse',
    preventDefault() { this.prevented = true; },
});
const move = (x, y, id = 7) => ({ clientX: x, clientY: y, pointerId: id });

test('a horizontal drag grows the pane live and commits once', () => {
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 'a', { orientation: 'horizontal', min: 100, max: 480 });

    s.handle.dispatch('pointerdown', down(300, 200));
    assert.equal(s.handle.captured, 7, 'pointer capture keeps the drag alive outside the strip');

    s.handle.dispatch('pointermove', move(340, 200));
    assert.equal(s.pane.style.flex, '0 0 340px', 'the live size is an inline style, not a re-render');
    assert.equal(s.attrs.get('aria-valuenow'), '340');
    assert.equal(s.calls.length, 0, 'no .NET round trip while dragging');

    s.handle.dispatch('pointerup', move(340, 200));
    assert.deepEqual(s.calls, [['OnSplitterResizeCommitted', 340]], 'committed exactly once');
    assert.equal(s.handle.captured, null);
});

test('a vertical drag uses dy', () => {
    const s = stage({ height: 200 });
    attachResize(s.handle, s.pane, s.dotnet, 'v', { orientation: 'vertical', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(100, 300));
    s.handle.dispatch('pointermove', move(100, 260));
    assert.equal(s.pane.style.flex, '0 0 160px');

    s.handle.dispatch('pointerup', move(100, 260));
    assert.deepEqual(s.calls, [['OnSplitterResizeCommitted', 160]]);
});

test('the drag clamps to min and max', () => {
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 'c', { orientation: 'horizontal', min: 100, max: 400 });

    s.handle.dispatch('pointerdown', down(0, 0));
    s.handle.dispatch('pointermove', move(500, 0));
    assert.equal(s.pane.style.flex, '0 0 400px', 'never above the maximum');
    s.handle.dispatch('pointermove', move(-500, 0));
    assert.equal(s.pane.style.flex, '0 0 100px', 'never below the minimum');
    s.handle.dispatch('pointerup', move(-500, 0));
    assert.deepEqual(s.calls, [['OnSplitterResizeCommitted', 100]]);
});

// ── RTL: the horizontal delta inverts with the direction ────────────────────────────────────

test('an RTL horizontal drag inverts dx', () => {
    globalThis.getComputedStyle = () => ({ direction: 'rtl' });
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 'r', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(300, 0));
    s.handle.dispatch('pointermove', move(340, 0));
    assert.equal(s.pane.style.flex, '0 0 260px', 'in RTL a rightward pointer shrinks the start pane');
    s.handle.dispatch('pointerup', move(340, 0));
    assert.deepEqual(s.calls, [['OnSplitterResizeCommitted', 260]]);
});

test('a vertical drag ignores the direction', () => {
    globalThis.getComputedStyle = () => ({ direction: 'rtl' });
    const s = stage({ height: 200 });
    attachResize(s.handle, s.pane, s.dotnet, 'rv', { orientation: 'vertical', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(100, 300));
    s.handle.dispatch('pointermove', move(100, 260));
    assert.equal(s.pane.style.flex, '0 0 160px', 'the vertical delta never inverts');
    s.handle.dispatch('pointerup', move(100, 260));
    assert.deepEqual(s.calls, [['OnSplitterResizeCommitted', 160]]);
});

test('directionOf reads the computed direction and falls back to ltr', () => {
    assert.equal(directionOf({}), 'ltr', 'no getComputedStyle (Node, SSR): ltr');
    globalThis.getComputedStyle = () => ({ direction: 'rtl' });
    assert.equal(directionOf({}), 'rtl');
    globalThis.getComputedStyle = () => { throw new Error('boom'); };
    assert.equal(directionOf({}), 'ltr', 'a throwing probe must never break a press');
});

// ── the start style survives every cancel path verbatim ─────────────────────────────────────

const PRESET = 'flex-basis: 30%; min-width: 100px';

test('Escape restores the start style verbatim and commits nothing', () => {
    const s = stage({ width: 300, style: PRESET });
    const doc = listeners();
    globalThis.document = doc;
    attachResize(s.handle, s.pane, s.dotnet, 'e', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(300, 0));
    s.handle.dispatch('pointermove', move(360, 0));
    assert.equal(s.pane.style.flex, '0 0 360px');

    doc.dispatch('keydown', { key: 'Escape' });
    assert.equal(s.pane.getAttribute('style'), PRESET,
        'the exact start style comes back — clearing `flex` alone would keep the shorthand having destroyed flex-basis');
    assert.equal(s.attrs.get('aria-valuenow'), '300');

    s.handle.dispatch('pointerup', move(360, 0));
    assert.equal(s.calls.length, 0);
});

test('a cancel with no prior style attribute removes the style instead of leaving an empty one', () => {
    const s = stage({ width: 300 });
    const doc = listeners();
    globalThis.document = doc;
    attachResize(s.handle, s.pane, s.dotnet, 'e2', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(300, 0));
    s.handle.dispatch('pointermove', move(360, 0));
    doc.dispatch('keydown', { key: 'Escape' });

    assert.equal(s.pane.getAttribute('style'), null, 'no style attribute existed, so none is left behind');
    assert.ok(!('flex' in s.pane.style), 'the dragged flex is gone');
});

test('pointercancel restores the start style verbatim', () => {
    const s = stage({ width: 300, style: PRESET });
    attachResize(s.handle, s.pane, s.dotnet, 'p', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(300, 0));
    s.handle.dispatch('pointermove', move(340, 0));
    s.handle.dispatch('pointercancel', move(340, 0));

    assert.equal(s.pane.getAttribute('style'), PRESET, 'a lost gesture restores the authored style');
    assert.equal(s.attrs.get('aria-valuenow'), '300');
    assert.equal(s.calls.length, 0);
});

test('detaching mid-drag restores the start style verbatim and commits nothing', () => {
    const s = stage({ width: 300, style: PRESET });
    attachResize(s.handle, s.pane, s.dotnet, 'm', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(300, 0));
    s.handle.dispatch('pointermove', move(380, 0));
    assert.equal(s.pane.style.flex, '0 0 380px');

    detachResize('m');

    assert.equal(s.pane.getAttribute('style'), PRESET, 'a pane that unmounts mid-drag gets its style back');
    assert.equal(s.attrs.get('aria-valuenow'), '300');
    assert.equal(s.calls.length, 0, 'a detached drag commits nothing');
});

test('a release back at the start size restores the start style verbatim and commits nothing', () => {
    const s = stage({ width: 300, style: PRESET });
    attachResize(s.handle, s.pane, s.dotnet, 'z', { orientation: 'horizontal', min: 100, max: 480 });

    s.handle.dispatch('pointerdown', down(0, 0));
    s.handle.dispatch('pointermove', move(60, 0));
    assert.equal(s.pane.style.flex, '0 0 360px');
    s.handle.dispatch('pointermove', move(0, 0));
    assert.equal(s.pane.style.flex, '0 0 300px', 'back at the start size');
    s.handle.dispatch('pointerup', move(0, 0));

    assert.equal(s.pane.getAttribute('style'), PRESET, 'no px flex is left behind when nothing changed');
    assert.equal(s.calls.length, 0, 'no commit when nothing changed');
});

test('a real commit leaves the size in place for the one Blazor re-render', () => {
    const s = stage({ width: 300, style: PRESET });
    attachResize(s.handle, s.pane, s.dotnet, 'k', { orientation: 'horizontal', min: 100, max: 480 });

    s.handle.dispatch('pointerdown', down(0, 0));
    s.handle.dispatch('pointermove', move(60, 0));
    s.handle.dispatch('pointerup', move(60, 0));

    assert.deepEqual(s.calls, [['OnSplitterResizeCommitted', 360]]);
    assert.equal(s.pane.style.flex, '0 0 360px', 'the committed flex survives until Blazor re-renders');
});

// ── vetoes and multi-pointer ────────────────────────────────────────────────────────────────

test('a press on the collapse button is vetoed: no capture, no drag, the click is untouched', () => {
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 'b', { orientation: 'horizontal', min: 0, max: Infinity });

    const press = down(300, 0);
    press.target = { closest: selector => (selector === '.tm-splitter__collapse-btn' ? { classList: 'btn' } : null) };
    s.handle.dispatch('pointerdown', press);

    assert.equal(s.handle.captured, null, 'the veto happens before the capture');
    assert.equal(press.prevented, undefined, 'the press is not prevented: the click must reach the button');

    s.handle.dispatch('pointermove', move(340, 0));
    assert.equal('flex' in s.pane.style, false, 'no drag follows a vetoed press');
    s.handle.dispatch('pointerup', move(340, 0));
    assert.equal(s.calls.length, 0);
});

test('a secondary button and a second pointer never drive the drag', () => {
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 's', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', { ...down(300, 0), button: 2 });
    s.handle.dispatch('pointermove', move(340, 0));
    s.handle.dispatch('pointerup', move(340, 0));
    assert.equal(s.calls.length, 0);

    s.handle.dispatch('pointerdown', { ...down(300, 1), isPrimary: false });
    s.handle.dispatch('pointermove', move(340, 1));
    s.handle.dispatch('pointerup', move(340, 1));
    assert.equal(s.calls.length, 0, "a second contact cannot move or release the first one's drag");
});

// ── attach/detach plumbing ──────────────────────────────────────────────────────────────────

test('detachResize removes every listener; re-attaching the same id replaces the first', () => {
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 'd', { orientation: 'horizontal', min: 0, max: Infinity });
    attachResize(s.handle, s.pane, s.dotnet, 'd', { orientation: 'horizontal', min: 0, max: Infinity });
    assert.equal(s.handle.count('pointerdown'), 1, 'a second attach replaces the first');

    detachResize('d');
    assert.equal(s.handle.count('pointerdown'), 0);
    assert.equal(s.handle.count('keydown'), 0);
    assert.doesNotThrow(() => detachResize('d'), 'detach twice is a no-op');
});

test('two registrations are independent', () => {
    const a = stage({ width: 300 });
    const b = stage({ width: 500 });
    attachResize(a.handle, a.pane, a.dotnet, 'x1', { orientation: 'horizontal', min: 0, max: Infinity });
    attachResize(b.handle, b.pane, b.dotnet, 'x2', { orientation: 'horizontal', min: 0, max: Infinity });

    a.handle.dispatch('pointerdown', down(0, 0));
    a.handle.dispatch('pointermove', move(60, 0));
    a.handle.dispatch('pointerup', move(60, 0));

    assert.deepEqual(a.calls, [['OnSplitterResizeCommitted', 360]]);
    assert.deepEqual(b.pane.style, {}, "the other splitter's pane is untouched");

    detachResize('x1');
    b.handle.dispatch('pointerdown', down(0, 0));
    b.handle.dispatch('pointermove', move(30, 0));
    b.handle.dispatch('pointerup', move(30, 0));
    assert.deepEqual(b.calls, [['OnSplitterResizeCommitted', 530]], 'detach leaves the other registration alive');
});

// ── key prevention: only the axis keys and Home/End ─────────────────────────────────────────

test('the axis keys and Home/End must not scroll the page; the other arrows are left alone', () => {
    const s = stage({ width: 300 });
    attachResize(s.handle, s.pane, s.dotnet, 'k', { orientation: 'horizontal', min: 0, max: Infinity });

    for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) {
        const event = { key, preventDefault() { this.prevented = true; } };
        s.handle.dispatch('keydown', event);
        assert.equal(event.prevented, true, `${key} is a horizontal resize key`);
    }
    for (const key of ['ArrowUp', 'ArrowDown', 'Tab', 'Enter']) {
        const event = { key, preventDefault() { this.prevented = true; } };
        s.handle.dispatch('keydown', event);
        assert.equal(event.prevented, undefined, `${key} keeps its browser behaviour on a horizontal splitter`);
    }
});

test('a vertical splitter prevents only the vertical axis keys', () => {
    const s = stage({ height: 200 });
    attachResize(s.handle, s.pane, s.dotnet, 'kv', { orientation: 'vertical', min: 0, max: Infinity });

    for (const key of ['ArrowUp', 'ArrowDown', 'Home', 'End']) {
        const event = { key, preventDefault() { this.prevented = true; } };
        s.handle.dispatch('keydown', event);
        assert.equal(event.prevented, true, `${key} is a vertical resize key`);
    }
    for (const key of ['ArrowLeft', 'ArrowRight']) {
        const event = { key, preventDefault() { this.prevented = true; } };
        s.handle.dispatch('keydown', event);
        assert.equal(event.prevented, undefined, `${key} keeps its browser behaviour on a vertical splitter`);
    }
});

// ── plumbing edge cases ─────────────────────────────────────────────────────────────────────

test('attachResize with a missing handle or pane is a no-op', () => {
    const s = stage({ width: 300 });
    assert.doesNotThrow(() => attachResize(null, s.pane, s.dotnet, 'n', { orientation: 'horizontal', min: 0, max: Infinity }));
    assert.doesNotThrow(() => attachResize(s.handle, null, s.dotnet, 'n', { orientation: 'horizontal', min: 0, max: Infinity }));
});

test('a dotnet reference that rejects does not surface an unhandled rejection', async () => {
    const s = stage({ width: 300 });
    s.dotnet.invokeMethodAsync = () => Promise.reject(new Error('circuit gone'));
    attachResize(s.handle, s.pane, s.dotnet, 'r', { orientation: 'horizontal', min: 0, max: Infinity });

    s.handle.dispatch('pointerdown', down(300, 0));
    s.handle.dispatch('pointermove', move(340, 0));
    s.handle.dispatch('pointerup', move(340, 0));
    await new Promise(resolve => setTimeout(resolve, 0));
});

// ── measureSize: the keyboard path's start value ────────────────────────────────────────────

test('measureSize reads the rendered size of the pane, -1 when it cannot be measured', () => {
    assert.equal(measureSize({ getBoundingClientRect: () => ({ width: 319.6, height: 220 }) }, 'horizontal'), 320);
    assert.equal(measureSize({ getBoundingClientRect: () => ({ width: 319.6, height: 220 }) }, 'vertical'), 220);
    assert.equal(measureSize(null, 'horizontal'), -1);
    assert.equal(measureSize({}, 'vertical'), -1);
});
