import assert from 'node:assert/strict';
import { test } from 'node:test';
import { GRACE_MS, isInsidePanel, unwatch, watch } from '../field-popover-guard.mjs';

// A minimal host: document/window listeners recorded by type, a manual animation frame queue and a manual clock.
function makeHost() {
    const listeners = new Map();
    const frames = [];
    const add = (type, fn) => { listeners.set(type, [...(listeners.get(type) ?? []), fn]); };
    const remove = (type, fn) => { listeners.set(type, (listeners.get(type) ?? []).filter(l => l !== fn)); };
    return {
        listeners,
        frames,
        document: { addEventListener: add, removeEventListener: remove },
        addEventListener: (type, fn) => add(`window:${type}`, fn),
        removeEventListener: (type, fn) => remove(`window:${type}`, fn),
        requestAnimationFrame: (fn) => { frames.push(fn); return frames.length; },
        cancelAnimationFrame: () => { frames.length = 0; },
        fire: (type, event = {}) => { for (const fn of listeners.get(type) ?? []) fn(event); },
    };
}

test('a scroll after the grace period is reported once per animation frame', () => {
    const host = makeHost();
    let t = 1000;
    let reports = 0;
    watch('a', () => { reports += 1; }, host, () => t);
    t += GRACE_MS + 1;

    host.fire('scroll', { target: {} });
    host.fire('scroll', { target: {} });
    host.fire('scroll', { target: {} });
    assert.equal(host.frames.length, 1, 'three scrolls in one frame schedule one report');
    host.frames.shift()();
    assert.equal(reports, 1);
    unwatch('a');
});

test('the scroll the engine performs right after the selection (grace period) is ignored', () => {
    const host = makeHost();
    let t = 1000;
    let reports = 0;
    watch('b', () => { reports += 1; }, host, () => t);
    t += 50;
    host.fire('scroll', { target: {} });
    assert.equal(host.frames.length, 0);
    assert.equal(reports, 0);
    unwatch('b');
});

test('a scroll inside the popover itself never counts', () => {
    const host = makeHost();
    let t = 1000;
    watch('c', () => assert.fail('must not report'), host, () => t);
    t += GRACE_MS + 1;
    const inside = { closest: (selector) => (selector.includes('.tm-document-floating-panel') ? {} : null) };
    assert.equal(isInsidePanel(inside), true);
    host.fire('scroll', { target: inside });
    assert.equal(host.frames.length, 0);
    unwatch('c');
});

test('a window resize is reported too', () => {
    const host = makeHost();
    let t = 1000;
    let reports = 0;
    watch('d', () => { reports += 1; }, host, () => t);
    t += GRACE_MS + 1;
    host.fire('window:resize');
    host.frames.shift()();
    assert.equal(reports, 1);
    unwatch('d');
});

test('watch is idempotent per key and unwatch removes every listener and the pending frame', () => {
    const host = makeHost();
    let t = 1000;
    assert.equal(watch('e', () => {}, host, () => t), true);
    assert.equal(watch('e', () => {}, host, () => t), false);
    t += GRACE_MS + 1;
    host.fire('scroll', { target: {} });
    assert.equal(unwatch('e'), true);
    assert.equal(host.frames.length, 0, 'the pending frame was cancelled');
    assert.equal((host.listeners.get('scroll') ?? []).length, 0);
    assert.equal((host.listeners.get('window:resize') ?? []).length, 0);
    assert.equal(unwatch('e'), false);
});
