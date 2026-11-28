import test from 'node:test';
import assert from 'node:assert/strict';

import { attach, detach, computeInset, isAttached } from '../tm-visual-viewport.js';

const PROP = '--tm-visual-viewport-inset-block-end';

function fakeRoot(bottom) {
    const props = new Map();
    const attrs = new Map();
    return {
        props,
        attrs,
        isConnected: true,
        getBoundingClientRect: () => ({ bottom: bottom ?? globalThis.innerHeight }),
        style: {
            setProperty(name, value) { props.set(name, value); },
            removeProperty(name) { props.delete(name); },
        },
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
    };
}

function fakeEnv(innerHeight = 844, height = 844, offsetTop = 0) {
    const listeners = new Map();
    const viewport = {
        height,
        width: 390,
        offsetTop,
        scale: 1,
        addEventListener(type, fn) { (listeners.get(`vv:${type}`) ?? listeners.set(`vv:${type}`, new Set()).get(`vv:${type}`)).add(fn); },
        removeEventListener(type, fn) { listeners.get(`vv:${type}`)?.delete(fn); },
    };
    const queue = [];
    globalThis.innerHeight = innerHeight;
    globalThis.visualViewport = viewport;
    globalThis.addEventListener = (type, fn) => { (listeners.get(`w:${type}`) ?? listeners.set(`w:${type}`, new Set()).get(`w:${type}`)).add(fn); };
    globalThis.removeEventListener = (type, fn) => { listeners.get(`w:${type}`)?.delete(fn); };
    globalThis.requestAnimationFrame = (fn) => { queue.push(fn); return queue.length; };
    globalThis.cancelAnimationFrame = () => {};
    return {
        viewport,
        count: (key) => listeners.get(key)?.size ?? 0,
        fire: (key) => { for (const fn of [...(listeners.get(key) ?? [])]) fn(); },
        flush: () => { while (queue.length) queue.shift()(); },
    };
}

function cleanup() {
    delete globalThis.innerHeight;
    delete globalThis.visualViewport;
    delete globalThis.addEventListener;
    delete globalThis.removeEventListener;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
}

test('computeInset is the gap the keyboard covers, 0 below the browser-chrome threshold', () => {
    assert.equal(computeInset(844, { height: 844, offsetTop: 0, width: 390, scale: 1 }), 0);
    assert.equal(computeInset(844, { height: 500, offsetTop: 0, width: 390, scale: 1 }), 344);
    assert.equal(computeInset(844, { height: 500, offsetTop: 20, width: 390, scale: 1 }), 324);
    assert.equal(computeInset(844, { height: 800, offsetTop: 0, width: 390, scale: 1 }), 0, 'a 44px shrink is browser chrome, not a keyboard');
    assert.equal(computeInset(844, null), 0);
});

test('attach writes the inset on a visualViewport resize (rAF-coalesced) and marks the keyboard open', () => {
    const env = fakeEnv();
    const root = fakeRoot();
    try {
        attach(root, { id: 'r' });
        assert.equal(root.props.has(PROP), false, 'no keyboard, no knob');
        env.viewport.height = 500;
        env.fire('vv:resize');
        env.fire('vv:scroll');
        assert.equal(root.props.has(PROP), false, 'the write waits for the animation frame');
        env.flush();
        assert.equal(root.props.get(PROP), '344px');
        assert.equal(root.attrs.has('data-tm-keyboard-open'), true);
        env.viewport.height = 844;
        env.fire('vv:resize');
        env.flush();
        assert.equal(root.props.has(PROP), false, 'closing the keyboard removes the knob');
        assert.equal(root.attrs.has('data-tm-keyboard-open'), false);
    } finally {
        detach('r');
        cleanup();
    }
});

test('detach removes every listener and the knob', () => {
    const env = fakeEnv(844, 500);
    const root = fakeRoot();
    try {
        attach(root, { id: 'r' });
        assert.equal(root.props.get(PROP), '344px', 'the first reading is applied at once');
        assert.equal(env.count('vv:resize'), 1);
        assert.equal(env.count('vv:scroll'), 1);
        assert.equal(env.count('w:resize'), 1);
        assert.equal(env.count('w:scroll'), 1);
        detach('r');
        assert.equal(env.count('vv:resize'), 0);
        assert.equal(env.count('vv:scroll'), 0);
        assert.equal(env.count('w:resize'), 0);
        assert.equal(root.props.has(PROP), false);
        assert.equal(isAttached('r'), false);
        detach('r');
    } finally {
        cleanup();
    }
});

test('attach is idempotent: a second attach replaces the first, listeners never stack', () => {
    const env = fakeEnv();
    const root = fakeRoot();
    try {
        attach(root, { id: 'r' });
        attach(root, { id: 'r' });
        attach(root, { id: 'r' });
        assert.equal(env.count('vv:resize'), 1);
        assert.equal(env.count('w:resize'), 1);
        assert.equal(isAttached('r'), true);
    } finally {
        detach('r');
        cleanup();
    }
});

test('a pinch zoom keeps the last reading instead of reporting a keyboard', () => {
    const env = fakeEnv(844, 500);
    const root = fakeRoot();
    try {
        attach(root, { id: 'r' });
        assert.equal(root.props.get(PROP), '344px');
        env.viewport.scale = 2;
        env.viewport.height = 250;
        env.fire('vv:resize');
        env.flush();
        assert.equal(root.props.get(PROP), '344px');
    } finally {
        detach('r');
        cleanup();
    }
});

test('no visualViewport: attach is a no-op that still detaches cleanly', () => {
    const root = fakeRoot();
    delete globalThis.visualViewport;
    attach(root, { id: 'r' });
    assert.equal(root.props.has(PROP), false);
    detach('r');
});

test('attach rejects a missing element', () => {
    assert.throws(() => attach(null, { id: 'x' }), TypeError);
    assert.throws(() => attach(fakeRoot()), TypeError, 'an id is required');
});

test('detach by id still releases a tracker whose element object is gone (nothing else holds it)', () => {
    const env = fakeEnv(844, 500);
    const root = fakeRoot();
    try {
        attach(root, { id: 'gone' });
        assert.equal(root.props.get(PROP), '344px');
        detach('gone');
        assert.equal(env.count('vv:resize'), 0);
        assert.equal(env.count('w:scroll'), 0);
        assert.equal(isAttached('gone'), false);
    } finally {
        cleanup();
    }
});

test('a disconnected element self-detaches on its next tick instead of leaking listeners', () => {
    const env = fakeEnv();
    const root = fakeRoot();
    try {
        attach(root, { id: 'leak' });
        assert.equal(env.count('vv:resize'), 1);
        root.isConnected = false;
        env.viewport.height = 500;
        env.fire('vv:resize');
        env.flush();
        assert.equal(env.count('vv:resize'), 0);
        assert.equal(env.count('vv:scroll'), 0);
        assert.equal(env.count('w:resize'), 0);
        assert.equal(env.count('w:scroll'), 0);
        assert.equal(isAttached('leak'), false);
    } finally {
        detach('leak');
        cleanup();
    }
});

test('two ids track two elements independently', () => {
    const env = fakeEnv(844, 500);
    const a = fakeRoot();
    const b = fakeRoot();
    try {
        attach(a, { id: 'a' });
        attach(b, { id: 'b' });
        assert.equal(env.count('vv:resize'), 2);
        detach('a');
        assert.equal(a.props.has(PROP), false);
        assert.equal(b.props.get(PROP), '344px');
        assert.equal(env.count('vv:resize'), 1);
    } finally {
        detach('a');
        detach('b');
        cleanup();
    }
});

test('the inset is relative to the tracked element: one that ends above the keyboard gets 0, one that ends 60px higher gets 60px less', () => {
    const env = fakeEnv(844, 544);
    const above = fakeRoot(500);
    const higher = fakeRoot(784);
    const full = fakeRoot(844);
    try {
        attach(above, { id: 'above' });
        attach(higher, { id: 'higher' });
        attach(full, { id: 'full' });
        assert.equal(above.props.has(PROP), false, 'the element ends at 500, above the visible bottom 544');
        assert.equal(higher.props.get(PROP), '240px');
        assert.equal(full.props.get(PROP), '300px');
    } finally {
        detach('above');
        detach('higher');
        detach('full');
        cleanup();
    }
});

test('computeInset uses the layout viewport bottom when no element bottom is known, and keeps the browser-chrome threshold on the raw keyboard', () => {
    const vv = { height: 544, offsetTop: 0, width: 390, scale: 1 };
    assert.equal(computeInset(844, vv), 300);
    assert.equal(computeInset(844, vv, 80, null), 300);
    assert.equal(computeInset(844, vv, 80, 700), 156);
    assert.equal(computeInset(844, { ...vv, height: 800 }, 80, 844), 0, 'a 44px shrink is browser chrome');
});

test('options.property writes another consumer\'s own knob and removes it again', () => {
    fakeEnv(844, 544);
    const root = fakeRoot();
    try {
        attach(root, { id: 'composer', property: '--tm-ai-composer-keyboard-inset' });
        assert.equal(root.props.get('--tm-ai-composer-keyboard-inset'), '300px');
        assert.equal(root.props.has(PROP), false);
        detach('composer');
        assert.equal(root.props.has('--tm-ai-composer-keyboard-inset'), false);
    } finally {
        detach('composer');
        cleanup();
    }
});
