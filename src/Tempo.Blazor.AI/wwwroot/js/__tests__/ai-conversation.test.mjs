// ai-conversation.js tests — the transcript scroll interop.
//
// The module performs one action of its own: following the newest turn with a smooth scroll. That scroll
// fires `scroll` events whose first frames are still far from the bottom (`scrollToBottom` starts at the
// reader's old position while the content already grew), and those events used to be reported as the
// reader crossing the near-bottom band. `.NET` believed it, dropped `_stickToBottom` and popped the "New
// messages" pill at the reader who never scrolled — so the module now marks its own scrolling and stays
// quiet until it lands, while a real reader gesture still releases the mark immediately.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attach, detach, isNearBottom, scrollToBottom } from '../ai-conversation.js';

/** A scripted stand-in for the transcript element: geometry plus a listener registry. */
class FakeRoot {
    constructor({ scrollHeight = 1000, clientHeight = 400, scrollTop = 600 } = {}) {
        this.scrollHeight = scrollHeight;
        this.clientHeight = clientHeight;
        this.scrollTop = scrollTop;
        this.listeners = new Map();
        this.scrollCalls = [];
    }

    addEventListener(type, handler, options) {
        const entries = this.listeners.get(type) ?? [];
        entries.push({ handler, options });
        this.listeners.set(type, entries);
    }

    removeEventListener(type, handler) {
        const entries = this.listeners.get(type);
        if (!entries) {
            return;
        }

        const remaining = entries.filter((entry) => entry.handler !== handler);
        if (remaining.length > 0) {
            this.listeners.set(type, remaining);
        } else {
            this.listeners.delete(type);
        }
    }

    /** Registered event types, so a test can see what `detach` released. */
    listenerTypes() {
        return [...this.listeners.keys()].sort();
    }

    emit(type) {
        for (const entry of [...(this.listeners.get(type) ?? [])]) {
            entry.handler({ type, target: this });
            if (entry.options === true || entry.options?.once === true) {
                this.removeEventListener(type, entry.handler);
            }
        }
    }

    /** Records the request; the test drives the animated positions itself. */
    scrollTo({ top, behavior }) {
        this.scrollCalls.push({ top, behavior });
    }
}

function fakeDotNet() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(method, value) {
            calls.push({ method, value });
            return Promise.resolve();
        }
    };
}

/** A transcript whose content just grew by `growth` pixels, still scrolled to its old bottom. */
function grownTranscript(growth = 2000) {
    const root = new FakeRoot();
    const dotNet = fakeDotNet();
    attach(root, null, dotNet);
    root.scrollHeight = 1000 + growth;
    return { root, dotNet };
}

test('a smooth scrollToBottom does not report its own intermediate scrolls as a reader detaching', () => {
    const { root, dotNet } = grownTranscript();

    scrollToBottom(root, true);
    assert.deepEqual(root.scrollCalls, [{ top: 3000, behavior: 'smooth' }],
        'the new turn is followed with a smooth scroll');

    // The browser animates from the old position: every one of these frames is still below the band.
    for (const scrollTop of [700, 1100, 1700, 2300]) {
        root.scrollTop = scrollTop;
        root.emit('scroll');
    }

    assert.deepEqual(dotNet.calls, [],
        'the module never reports a detachment caused by its own scroll — the reader never scrolled');

    // The animation lands. The module clears its mark and still invents no crossing.
    root.scrollTop = 2600;
    root.emit('scroll');
    assert.deepEqual(dotNet.calls, [], 'landing is not news either');

    // From now on the reader's own scrolling is reported again.
    root.scrollTop = 600;
    root.emit('scroll');
    assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: false }],
        'a genuine crossing after the programmatic scroll is still reported');

    detach(root);
});

for (const gesture of ['wheel', 'touchstart', 'pointerdown', 'keydown']) {
    test(`a ${gesture} gesture during a programmatic scroll releases it and the reader's crossing is reported`, () => {
        const { root, dotNet } = grownTranscript();

        scrollToBottom(root, true);
        root.emit(gesture);
        root.scrollTop = 900;
        root.emit('scroll');

        assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: false }],
            `${gesture} is the reader taking over: their scroll position is news, not the module's`);

        detach(root);
    });
}

test('scrollend releases the programmatic window once the animation is over', () => {
    const { root, dotNet } = grownTranscript();

    scrollToBottom(root, true);
    root.scrollTop = 2600;
    root.emit('scrollend');

    root.scrollTop = 400;
    root.emit('scroll');

    assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: false }],
        'after scrollend the transcript reports crossings normally again');

    detach(root);
});

test('a fallback timer releases the programmatic window in engines without scrollend', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { root, dotNet } = grownTranscript();

    scrollToBottom(root, true);
    assert.ok(root.listenerTypes().includes('scrollend'), 'the module asks for the one-shot scrollend');

    // Still mid-animation, still below the band, and no scrollend will ever come.
    root.scrollTop = 1200;
    t.mock.timers.tick(500);
    assert.deepEqual(dotNet.calls, [], 'the fallback release does not report the animation either');

    // The animation now lands; the reader ends up at the bottom, so the report can only be "near".
    root.scrollTop = 2600;
    root.emit('scroll');
    assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: true }],
        'a release never reports a detachment the reader did not cause');

    detach(root);
});

test('detach releases every listener it registered', () => {
    const { root } = grownTranscript();

    scrollToBottom(root, true);
    root.emit('wheel');
    assert.ok(root.listenerTypes().includes('scroll'));
    assert.ok(root.listenerTypes().includes('wheel'));

    detach(root);

    assert.deepEqual(root.listenerTypes(), [],
        'a detached transcript keeps no scroll, gesture or scrollend listener alive');
});

test('isNearBottom reads the same band the reports use', () => {
    const root = new FakeRoot({ scrollHeight: 1000, clientHeight: 400, scrollTop: 560 });
    assert.equal(isNearBottom(root), true, '48 px of slack absorbs sub-pixel rounding');

    root.scrollTop = 500;
    assert.equal(isNearBottom(root), false);
    assert.equal(isNearBottom(null), true, 'a transcript without a root cannot be detached');
});

test('scrollToBottom honours reduced motion and survives a root without a scrollTo', () => {
    const root = new FakeRoot();
    const original = globalThis.matchMedia;
    globalThis.matchMedia = (query) => ({ matches: query === '(prefers-reduced-motion: reduce)' });
    try {
        scrollToBottom(root, true);
        assert.deepEqual(root.scrollCalls, [{ top: 1000, behavior: 'auto' }],
            'a reader who asked for less motion gets the jump, never the animation');
    } finally {
        globalThis.matchMedia = original;
    }

    const plain = { scrollHeight: 1200, scrollTop: 0 };
    assert.equal(scrollToBottom(plain, true), true);
    assert.equal(plain.scrollTop, 1200, 'a root without scrollTo still reaches its end');
    assert.equal(scrollToBottom(null, true), false);
});
