// tm-time-selector.js tests with hand-rolled DOM stubs (same approach as tm-otp-input.test.mjs):
// no jsdom; the stubs implement only what the module touches. Covers keys, typeahead, Tab never
// preventDefault, auto-repeat, fit measurement, the software-keyboard heuristic, and opt-in
// swipe (dy→steps + cancel rollback) over the shared pointer-drag.js helper.
import { describe, test } from 'node:test';
import assert from 'node:assert/strict';
import { attach, detach, update } from '../tm-time-selector.js';

describe('tm-time-selector', { concurrency: false }, () => {

function makeEl(attrs = {}) {
    const listeners = new Map();
    return {
        attrs: { ...attrs },
        tagName: attrs.tagName ?? 'SPAN',
        disabled: false,
        readOnly: false,
        isContentEditable: false,
        focusCalls: 0,
        scrollCalls: [],
        getAttribute(name) { return this.attrs[name] ?? null; },
        setAttribute(name, value) { this.attrs[name] = String(value); },
        addEventListener(type, fn, options) { listeners.set(type, { fn, options }); },
        removeEventListener(type) { listeners.delete(type); },
        dispatch(type, event = {}) {
            event.currentTarget ??= this;
            event.target ??= this;
            event.preventDefault ??= function preventDefault() { this.defaultPrevented = true; };
            event.stopPropagation ??= function stopPropagation() { this.propagationStopped = true; };
            listeners.get(type)?.fn(event);
            return event;
        },
        focus() { this.focusCalls++; },
        scrollIntoView(opts) { this.scrollCalls.push(opts); },
        closest(sel) {
            if (sel === '[data-tm-time-grid-cell]' && this.attrs['data-tm-time-grid-cell'] != null) {
                return this;
            }
            return null;
        },
        getBoundingClientRect() { return { width: this.clientWidth ?? 400, height: 80 }; },
        clientWidth: 400,
        style: {},
        setPointerCapture() {},
        releasePointerCapture() {},
        contains(node) { return node === this; },
        _listeners: listeners,
    };
}

function makeRoot({ cycle = 'h23', seconds = false, width = 400, swipe = false } = {}) {
    const hours = makeEl({ 'data-tm-time-segment': 'hours', role: 'spinbutton' });
    const minutes = makeEl({ 'data-tm-time-segment': 'minutes', role: 'spinbutton' });
    const secondEl = makeEl({ 'data-tm-time-segment': 'seconds', role: 'spinbutton' });
    const plusHours = makeEl({
        tagName: 'BUTTON',
        'data-tm-time-step': 'hours',
        'data-tm-time-delta': '1',
    });
    const minusHours = makeEl({
        tagName: 'BUTTON',
        'data-tm-time-step': 'hours',
        'data-tm-time-delta': '-1',
    });
    const spinList = seconds ? [hours, minutes, secondEl] : [hours, minutes];
    const stepList = [minusHours, plusHours];
    const listeners = new Map();
    const root = {
        dataset: { tmHourCycle: cycle },
        attrs: {
            'data-tm-hour-cycle': cycle,
            ...(swipe ? { 'data-tm-time-swipe': 'true' } : {}),
        },
        hours,
        minutes,
        seconds: secondEl,
        plusHours,
        minusHours,
        clientWidth: width,
        getAttribute(name) { return this.attrs[name] ?? null; },
        setAttribute(name, value) { this.attrs[name] = String(value); },
        querySelectorAll(sel) {
            if (sel.includes('role=spinbutton') && sel.includes('data-tm-time-segment')) return spinList;
            if (sel.includes('data-tm-time-segment') && !sel.includes('role=')) return spinList;
            if (sel.includes('data-tm-time-step')) return stepList;
            return [];
        },
        addEventListener(type, fn, options) { listeners.set(type, { fn, options }); },
        removeEventListener(type) { listeners.delete(type); },
        dispatch(type, event = {}) {
            event.currentTarget ??= this;
            event.target ??= event.target;
            event.preventDefault ??= function preventDefault() { this.defaultPrevented = true; };
            listeners.get(type)?.fn(event);
            return event;
        },
        closest() { return null; },
        getBoundingClientRect() { return { width, height: 80 }; },
        _listeners: listeners,
    };
    return root;
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

function keyEvent(target, key, extras = {}) {
    return {
        key,
        target,
        ctrlKey: false,
        metaKey: false,
        altKey: false,
        defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
        ...extras,
    };
}

function installWindow({ innerHeight = 800, vvHeight = 800, sheetViewport = '' } = {}) {
    const wListeners = new Map();
    const vvListeners = new Map();
    const visualViewport = {
        height: vvHeight,
        addEventListener(type, fn) { vvListeners.set(type, fn); },
        removeEventListener(type) { vvListeners.delete(type); },
        dispatch(type) { vvListeners.get(type)?.(); },
    };
    const documentStub = {
        activeElement: null,
        addEventListener() {},
        removeEventListener() {},
        contains() { return true; },
    };
    const previous = {
        window: globalThis.window,
        document: globalThis.document,
        setTimeout: globalThis.setTimeout,
        clearTimeout: globalThis.clearTimeout,
        setInterval: globalThis.setInterval,
        clearInterval: globalThis.clearInterval,
    };
    globalThis.document = documentStub;
    globalThis.window = {
        innerHeight,
        visualViewport,
        addEventListener(type, fn) { wListeners.set(type, fn); },
        removeEventListener(type) { wListeners.delete(type); },
        getComputedStyle() { return { getPropertyValue: () => sheetViewport }; },
        dispatch(type) { wListeners.get(type)?.(); },
        _wListeners: wListeners,
        _vv: visualViewport,
        document: documentStub,
    };
    return {
        window: globalThis.window,
        document: documentStub,
        restore() {
            globalThis.window = previous.window;
            globalThis.document = previous.document;
            globalThis.setTimeout = previous.setTimeout;
            globalThis.clearTimeout = previous.clearTimeout;
            globalThis.setInterval = previous.setInterval;
            globalThis.clearInterval = previous.clearInterval;
        },
    };
}

function fakeTimers() {
    const timeouts = new Map();
    const intervals = new Map();
    let nextId = 1;
    let now = 0;
    globalThis.setTimeout = (fn, ms) => {
        const id = nextId++;
        timeouts.set(id, { fn, due: now + ms });
        return id;
    };
    globalThis.clearTimeout = (id) => { timeouts.delete(id); };
    globalThis.setInterval = (fn, ms) => {
        const id = nextId++;
        intervals.set(id, { fn, ms, due: now + ms });
        return id;
    };
    globalThis.clearInterval = (id) => { intervals.delete(id); };
    return {
        advance(ms) {
            now += ms;
            for (const [id, t] of [...timeouts]) {
                if (t.due <= now) {
                    timeouts.delete(id);
                    t.fn();
                }
            }
            for (const t of intervals.values()) {
                while (t.due <= now) {
                    t.fn();
                    t.due += t.ms;
                }
            }
        },
    };
}

test('arrow up on hours steps +1 without announce and preventDefaults', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const event = keyEvent(root.hours, 'ArrowUp');
    root.dispatch('keydown', event);
    assert.equal(event.defaultPrevented, true);
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync'), [
        { method: 'OnKeyStepAsync', args: ['hours', 1, false] },
    ]);
    detach(root);
    host.restore();
});

test('Tab is never preventDefaulted', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const event = keyEvent(root.hours, 'Tab');
    root.dispatch('keydown', event);
    assert.equal(event.defaultPrevented, false);
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').length, 0);
    detach(root);
    host.restore();
});

test('PageUp on hours steps +3 hours', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.hours, 'PageUp'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').at(-1),
        { method: 'OnKeyStepAsync', args: ['hours', 3, false] });
    detach(root);
    host.restore();
});

test('PageDown on minutes steps -15', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.minutes, 'PageDown'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').at(-1),
        { method: 'OnKeyStepAsync', args: ['minutes', -15, false] });
    detach(root);
    host.restore();
});

test('Home on hours reports home=true', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.hours, 'Home'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnHomeEndAsync').at(-1),
        { method: 'OnHomeEndAsync', args: ['hours', true] });
    detach(root);
    host.restore();
});

test('24h hours digit 3 auto-advances to minutes', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const event = keyEvent(root.hours, '3');
    root.dispatch('keydown', event);
    assert.equal(event.defaultPrevented, true);
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').at(-1),
        { method: 'OnTypeaheadAsync', args: ['hours', 3, true] });
    assert.equal(root.minutes.focusCalls, 1);
    assert.deepEqual(root.minutes.scrollCalls.at(-1), { block: 'nearest' });
    detach(root);
    host.restore();
});

test('24h hours digit 1 waits then commits without advance', () => {
    const host = installWindow();
    const timers = fakeTimers();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.hours, '1'));
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').length, 0);
    timers.advance(1000);
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').at(-1),
        { method: 'OnTypeaheadAsync', args: ['hours', 1, false] });
    detach(root);
    host.restore();
});

test('two digits commit and advance', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.hours, '1'));
    root.dispatch('keydown', keyEvent(root.hours, '4'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').at(-1),
        { method: 'OnTypeaheadAsync', args: ['hours', 14, true] });
    detach(root);
    host.restore();
});

test('12h hours digit 2 auto-advances', () => {
    const host = installWindow();
    const root = makeRoot({ cycle: 'h12' });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.hours, '2'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').at(-1),
        { method: 'OnTypeaheadAsync', args: ['hours', 2, true] });
    detach(root);
    host.restore();
});

test('colon moves to the next segment', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const event = keyEvent(root.hours, ':');
    root.dispatch('keydown', event);
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.minutes.focusCalls, 1);
    detach(root);
    host.restore();
});

test('p reports period PM', () => {
    const host = installWindow();
    const root = makeRoot({ cycle: 'h12' });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.hours, 'p'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').at(-1),
        { method: 'OnTypeaheadAsync', args: ['period', 1, false] });
    detach(root);
    host.restore();
});

test('grid-cell arrows preventDefault so the page does not scroll, Enter stays native', () => {
    const host = installWindow();
    const root = makeRoot();
    const cell = makeEl({ 'data-tm-time-grid-cell': '', tagName: 'BUTTON' });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const arrow = root.dispatch('keydown', keyEvent(cell, 'ArrowRight'));
    assert.equal(arrow.defaultPrevented, true);
    const enter = root.dispatch('keydown', keyEvent(cell, 'Enter'));
    assert.equal(enter.defaultPrevented, false);
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').length, 0);
    detach(root);
    host.restore();
});

test('Backspace clears the segment to 0', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.dispatch('keydown', keyEvent(root.minutes, 'Backspace'));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnTypeaheadAsync').at(-1),
        { method: 'OnTypeaheadAsync', args: ['minutes', 0, false] });
    detach(root);
    host.restore();
});

test('attach reports measured width and innerHeight, never the root block-size', () => {
    const host = installWindow({ innerHeight: 844, vvHeight: 844 });
    const root = makeRoot({ width: 390 });
    root.getBoundingClientRect = () => ({ width: 390, height: 900 });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const vp = dotNet.calls.filter((c) => c.method === 'OnViewportChanged').at(-1);
    assert.deepEqual(vp, { method: 'OnViewportChanged', args: [390, 844, 390] });
    detach(root);
    host.restore();
});

test('viewport width for hour columns is clientWidth minus padding, not the outer box', () => {
    const host = installWindow({ innerHeight: 306, vvHeight: 306 });
    const root = makeRoot({ width: 399 });
    root.clientWidth = 399;
    root.getBoundingClientRect = () => ({ width: 399, height: 80 });
    host.window.getComputedStyle = () => ({
        getPropertyValue: () => '',
        paddingLeft: '8px',
        paddingRight: '8px',
    });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const vp = dotNet.calls.filter((c) => c.method === 'OnViewportChanged').at(-1);
    assert.equal(vp.args[0], 399, 'fit still uses the outer inline-size');
    assert.equal(vp.args[2], 383, 'HourColumns uses content-box width (399 − 8 − 8)');
    detach(root);
    host.restore();
});

test('sheet height is 85 percent of visualViewport capped by --tm-sheet-viewport', () => {
    const host = installWindow({ innerHeight: 844, vvHeight: 844, sheetViewport: '400px' });
    const root = makeRoot({ width: 399 });
    root.closest = (sel) => (sel === '.tm-sheet' ? {} : null);
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const vp = dotNet.calls.filter((c) => c.method === 'OnViewportChanged').at(-1);
    assert.equal(vp.args[0], 399);
    assert.equal(vp.args[1], 400);
    detach(root);
    host.restore();
});

test('keyboard open requires an editable activeElement and a shrunken visualViewport', () => {
    const host = installWindow({ innerHeight: 844, vvHeight: 844 });
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    host.document.activeElement = { tagName: 'INPUT', disabled: false, readOnly: false };
    host.window.visualViewport.height = 93;
    host.window.visualViewport.dispatch('resize');
    const kb = dotNet.calls.filter((c) => c.method === 'OnKeyboardChanged').at(-1);
    assert.deepEqual(kb, { method: 'OnKeyboardChanged', args: [true] });
    detach(root);
    host.restore();
});

test('keyboard open does not re-report viewport', () => {
    const host = installWindow({ innerHeight: 844, vvHeight: 844 });
    const root = makeRoot({ width: 399 });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    const before = dotNet.calls.filter((c) => c.method === 'OnViewportChanged').length;
    host.document.activeElement = { tagName: 'INPUT', disabled: false, readOnly: false };
    host.window.visualViewport.height = 93;
    host.window.innerHeight = 1280;
    host.window.visualViewport.dispatch('resize');
    const after = dotNet.calls.filter((c) => c.method === 'OnViewportChanged').length;
    assert.equal(after, before, 'fit stays locked while the software keyboard is open');
    detach(root);
    host.restore();
});

test('auto-repeat starts after 400 ms and stops on pointerup', () => {
    const host = installWindow();
    const timers = fakeTimers();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.plusHours.dispatch('pointerdown', { button: 0 });
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').length, 0);
    timers.advance(400);
    const during = dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync');
    assert.ok(during.length >= 1, 'first auto-repeat tick fires at 400 ms');
    assert.equal(during[0].args[2], false, 'ticks during the hold are not announced');
    root.plusHours.dispatch('pointerup', {});
    const announced = dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync' && c.args[2] === true);
    assert.equal(announced.length, 1, 'pointerup announces once at the end of the series');
    const afterStop = dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').length;
    timers.advance(500);
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnKeyStepAsync').length, afterStop);
    detach(root);
    host.restore();
});

test('attach is idempotent and detach removes listeners', () => {
    const host = installWindow();
    const root = makeRoot();
    const first = dotnetStub();
    const second = dotnetStub();
    attach(root, first);
    attach(root, second);
    root.dispatch('keydown', keyEvent(root.hours, 'ArrowUp'));
    assert.equal(first.calls.filter((c) => c.method === 'OnKeyStepAsync').length, 0);
    assert.ok(second.calls.some((c) => c.method === 'OnKeyStepAsync'));
    detach(root);
    const before = second.calls.length;
    root.dispatch('keydown', keyEvent(root.hours, 'ArrowDown'));
    assert.equal(second.calls.length, before);
    host.restore();
});

test('update rebinds without throwing on a live session', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    update(root);
    root.dispatch('keydown', keyEvent(root.hours, 'ArrowUp'));
    assert.ok(dotNet.calls.some((c) => c.method === 'OnKeyStepAsync'));
    detach(root);
    host.restore();
});

test('update keeps max visualViewport so an open keyboard stays open', () => {
    const host = installWindow({ innerHeight: 844, vvHeight: 844 });
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    host.document.activeElement = { tagName: 'INPUT', disabled: false, readOnly: false };
    host.window.visualViewport.height = 450;
    host.window.visualViewport.dispatch('resize');
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnKeyboardChanged').at(-1),
        { method: 'OnKeyboardChanged', args: [true] });
    const before = dotNet.calls.filter((c) => c.method === 'OnKeyboardChanged').length;
    update(root);
    const after = dotNet.calls.filter((c) => c.method === 'OnKeyboardChanged');
    assert.equal(after.length, before, 'update must not re-run the keyboard heuristic');
    assert.ok(!after.some((c) => c.args[0] === false),
        'OnKeyboardChanged(false) must not fire: maxVv is measured since attach, not reset on re-render');
    host.window.visualViewport.dispatch('resize');
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnKeyboardChanged').at(-1),
        { method: 'OnKeyboardChanged', args: [true] });
    detach(root);
    host.restore();
});

function pointer(el, { y = 0, x = 0, pointerId = 1, pointerType = 'touch', buttons = 1 } = {}) {
    return {
        pointerId,
        pointerType,
        clientX: x,
        clientY: y,
        button: 0,
        buttons,
        isPrimary: true,
        target: el,
        defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
        stopPropagation() {},
    };
}

function swipeCalls(dotNet) {
    return dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync' || c.method === 'OnKeyStepAsync');
}

test('without data-tm-time-swipe a vertical drag on hours attaches nothing', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    assert.equal(root.hours._listeners.has('pointerdown'), false);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 0 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: -48 }));
    root.hours.dispatch('pointerup', pointer(root.hours, { y: -48, buttons: 0 }));
    assert.equal(swipeCalls(dotNet).length, 0);
    assert.equal(dotNet.calls.filter((c) => c.method.startsWith('OnSwipe')).length, 0);
    detach(root);
    host.restore();
});

test('swipe up 48 px on hours steps +1 twice (24 px = 1 step, up = +)', () => {
    const host = installWindow();
    const root = makeRoot({ swipe: true });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 100 - 48 }));
    const steps = dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync');
    assert.deepEqual(steps, [
        { method: 'OnSwipeStepAsync', args: ['hours', 1] },
        { method: 'OnSwipeStepAsync', args: ['hours', 1] },
    ]);
    root.hours.dispatch('pointerup', pointer(root.hours, { y: 52, buttons: 0 }));
    detach(root);
    host.restore();
});

test('swipe down 24 px on hours steps -1; movement under 24 px is a no-op after threshold 6', () => {
    const host = installWindow();
    const root = makeRoot({ swipe: true });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 106 }));
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync').length, 0,
        'threshold 6 starts the drag but 6 px is under one 24 px step');
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 124 }));
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync'), [
        { method: 'OnSwipeStepAsync', args: ['hours', -1] },
    ]);
    root.hours.dispatch('pointerup', pointer(root.hours, { y: 124, buttons: 0 }));
    detach(root);
    host.restore();
});

test('pointercancel rolls the swipe back via OnSwipeCancelAsync', () => {
    const host = installWindow();
    const root = makeRoot({ swipe: true });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 52 }));
    assert.ok(dotNet.calls.some((c) => c.method === 'OnSwipeStepAsync'));
    root.hours.dispatch('pointercancel', pointer(root.hours, { y: 52 }));
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnSwipeCancelAsync').length, 1);
    assert.deepEqual(dotNet.calls.filter((c) => c.method === 'OnSwipeCancelAsync').at(-1).args[0], 'hours');
    detach(root);
    host.restore();
});

test('detach releases swipe handles and a second attach is idempotent', () => {
    const host = installWindow();
    const root = makeRoot({ swipe: true });
    const first = dotnetStub();
    const second = dotnetStub();
    attach(root, first);
    attach(root, second);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 76 }));
    assert.equal(first.calls.filter((c) => c.method === 'OnSwipeStepAsync').length, 0);
    assert.ok(second.calls.some((c) => c.method === 'OnSwipeStepAsync'));
    detach(root);
    const before = second.calls.length;
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 52 }));
    assert.equal(second.calls.length, before);
    assert.equal(root.hours._listeners.has('pointerdown'), false);
    host.restore();
});

test('update binds swipe when the attribute is added and unbinds when it is removed', () => {
    const host = installWindow();
    const root = makeRoot();
    const dotNet = dotnetStub();
    attach(root, dotNet);
    assert.equal(root.hours._listeners.has('pointerdown'), false);
    root.setAttribute('data-tm-time-swipe', 'true');
    update(root);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 76 }));
    assert.ok(dotNet.calls.some((c) => c.method === 'OnSwipeStepAsync'));
    root.hours.dispatch('pointerup', pointer(root.hours, { y: 76, buttons: 0 }));
    root.setAttribute('data-tm-time-swipe', 'false');
    update(root);
    const before = dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync').length;
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 52 }));
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync').length, before);
    detach(root);
    host.restore();
});

test('update during an in-progress swipe does not cancel the gesture', () => {
    const host = installWindow();
    const root = makeRoot({ swipe: true });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    root.hours.dispatch('pointerdown', pointer(root.hours, { y: 100 }));
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 76 }));
    update(root);
    root.hours.dispatch('pointermove', pointer(root.hours, { y: 52 }));
    const steps = dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync');
    assert.equal(steps.length, 2, 'the second 24 px still lands after update()');
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnSwipeCancelAsync').length, 0,
        'rebind must not detach() a live pointer-drag (detach cancels and rolls back)');
    detach(root);
    host.restore();
});

test('swipe never starts on a +/− stepper, only on the spinbutton number', () => {
    const host = installWindow();
    const root = makeRoot({ swipe: true });
    const dotNet = dotnetStub();
    attach(root, dotNet);
    assert.equal(root.plusHours._listeners.has('pointerdown'), true, 'auto-repeat still binds the stepper');
    root.plusHours.dispatch('pointerdown', pointer(root.plusHours, { y: 100 }));
    root.plusHours.dispatch('pointermove', pointer(root.plusHours, { y: 52 }));
    root.plusHours.dispatch('pointerup', pointer(root.plusHours, { y: 52, buttons: 0 }));
    assert.equal(dotNet.calls.filter((c) => c.method === 'OnSwipeStepAsync').length, 0);
    detach(root);
    host.restore();
});

});
