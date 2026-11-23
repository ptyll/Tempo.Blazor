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
import { attach, detach, isNearBottom, scrollToBottom, scrollToMessage, revealFocusedSuggestion } from '../ai-conversation.js';

/** A scripted stand-in for the transcript element: geometry plus a listener registry. */
class FakeRoot {
    constructor({ scrollHeight = 1000, clientHeight = 400, scrollTop = 600, rectTop = 0 } = {}) {
        this.scrollHeight = scrollHeight;
        this.clientHeight = clientHeight;
        this.scrollTop = scrollTop;
        this.rectTop = rectTop;
        this.listeners = new Map();
        this.scrollCalls = [];
        this.selectors = [];
        this.children = [];
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

    getBoundingClientRect() {
        return { top: this.rectTop };
    }

    /** Transcript rows with `data-message-id`, resolvable by the attribute selector. */
    addRow(messageId, rectTop) {
        const row = { messageId, rectTop, getBoundingClientRect: () => ({ top: rectTop }) };
        this.children.push(row);
        return row;
    }

    querySelector(selector) {
        this.selectors.push(selector);
        const match = /^\[data-message-id="((?:[^"\\]|\\.)*)"\]$/.exec(selector);
        if (!match) {
            return null;
        }

        const id = match[1].replace(/\\(["\\])/g, '$1');
        return this.children.find((child) => child.messageId === id) ?? null;
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

    // Still mid-animation, still below the band, and no scrollend will ever come. The fallback release
    // re-syncs the baseline to the MEASURED position — and because that differs from the baseline .NET
    // was left with (true), it IS reported: a window whose scroll never landed must not leave .NET
    // believing the reader is at the bottom.
    root.scrollTop = 1200;
    t.mock.timers.tick(500);
    assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: false }],
        'the fallback release reports the honest (still detached) position');

    // From there the reader's own landing scroll is a normal crossing again.
    root.scrollTop = 2600;
    root.emit('scroll');
    assert.deepEqual(dotNet.calls, [
        { method: 'OnScrollStateAsync', value: false },
        { method: 'OnScrollStateAsync', value: true },
    ], 'the landing crossing after the fallback release is reported from the re-synced baseline');

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

// ── The log BOX itself: the ResizeObserver watches the root too ─────────────────────────────

/** Installs a spy ResizeObserver; returns the instances and the restore function. */
function spyResizeObserver() {
    const previous = globalThis.ResizeObserver;
    const observers = [];
    globalThis.ResizeObserver = class {
        constructor(callback) {
            this.callback = callback;
            this.targets = [];
            observers.push(this);
        }

        observe(target) {
            this.targets.push(target);
        }

        disconnect() {
            this.disconnected = true;
        }
    };

    return {
        observers,
        restore() {
            if (previous === undefined) {
                delete globalThis.ResizeObserver;
            } else {
                globalThis.ResizeObserver = previous;
            }
        },
    };
}

test('a resize of the log box reports OnContentResizedAsync with the position before the change', () => {
    const spy = spyResizeObserver();
    try {
        const root = new FakeRoot();
        const content = { tag: 'content' };
        const dotNet = fakeDotNet();
        attach(root, content, dotNet);

        const observer = spy.observers[0];
        assert.deepEqual(observer.targets, [content, root],
            'one observer watches the content AND the box itself');

        // The reader is at the bottom (default geometry: 1000 - 600 - 400 = 0) and the HOST shrinks the
        // box. No scroll event needs to fire for the report to be right: it carries lastNear, the
        // position before the change, so .NET keeps the transcript pinned instead of pill-ing the reader.
        root.clientHeight = 250;
        observer.callback([{ target: root, contentRect: { width: 400, height: 250 } }]);

        assert.deepEqual(dotNet.calls, [{ method: 'OnContentResizedAsync', value: true }],
            'a bottom-riding reader follows the box resize');

        // A width-only resize of the SAME box (a scrollbar appears, a panel drags wider) reflows the same
        // number of lines: it must not disturb the reader's position, so it reports nothing.
        observer.callback([{ target: root, contentRect: { width: 384, height: 250 } }]);
        assert.deepEqual(dotNet.calls, [{ method: 'OnContentResizedAsync', value: true }],
            'a width-only box resize is not news');

        detach(root);
        assert.equal(observer.disconnected, true, 'detach releases the box observer too');
    } finally {
        spy.restore();
    }
});

test('a box resize while the reader is scrolled up reports detached, not a crossing', () => {
    const spy = spyResizeObserver();
    try {
        const root = new FakeRoot({ scrollTop: 560 }); // 40 px short: inside the band
        const dotNet = fakeDotNet();
        attach(root, null, dotNet);

        const observer = spy.observers[0];
        root.scrollTop = 300;
        root.emit('scroll');
        assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: false }],
            'the reader scrolled up themselves — a real crossing');

        root.clientHeight = 200;
        observer.callback([{ target: root, contentRect: { width: 400, height: 200 } }]);

        assert.deepEqual(dotNet.calls, [
            { method: 'OnScrollStateAsync', value: false },
            { method: 'OnContentResizedAsync', value: false },
        ], 'the box resize must not invent a bottom position the reader never chose');

        detach(root);
    } finally {
        spy.restore();
    }
});

// ── scrollToMessage ─────────────────────────────────────────────────────────────────────────

test('scrollToMessage moves only the transcript and marks the scroll as its own', () => {
    const { root, dotNet } = grownTranscript();
    root.scrollTop = 200;
    root.rectTop = 100;
    root.addRow('answer-1', 340);

    const moved = scrollToMessage(root, 'answer-1', true);

    assert.equal(moved, true, 'the named turn exists');
    assert.deepEqual(root.scrollCalls, [{ top: 440, behavior: 'smooth' }],
        'the offset is measured against the root itself (target 340 - root 100 = 240)');

    // Frames of the animation are the module's own motion: reported as crossings they would tell .NET
    // the reader detached from the turn the host just scrolled to.
    root.scrollTop = 300;
    root.emit('scroll');
    root.scrollTop = 440;
    root.emit('scroll');
    assert.deepEqual(dotNet.calls, [], 'not one frame of the programmatic scroll is a crossing');

    // The landing re-syncs the baseline: wherever the turn left the reader is the honest position for
    // the next growth (the grown transcript is 3000 tall, so 440 leaves the reader 2160 px from the
    // bottom). Scrolling BACK to the bottom from there is a real crossing again.
    root.emit('scrollend');
    root.scrollTop = 2560;
    root.emit('scroll');

    assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: true }],
        'a crossing after the animation lands is reported from the re-synced baseline');

    detach(root);
});

test('scrollToMessage with an unknown or unusable id scrolls nothing', () => {
    const root = new FakeRoot();
    const dotNet = fakeDotNet();
    attach(root, null, dotNet);
    root.addRow('m-1', 20);

    assert.equal(scrollToMessage(root, 'm-missing', true), false, 'an id outside the transcript is not found');
    assert.equal(scrollToMessage(root, '', true), false, 'an empty id is refused');
    assert.equal(scrollToMessage(root, null, true), false, 'a null id is refused');
    assert.deepEqual(root.scrollCalls, [], 'nothing was scrolled');
    assert.deepEqual(dotNet.calls, [], 'nothing was reported');
    assert.deepEqual(root.selectors, [
        '[data-message-id="m-missing"]',
    ], 'only usable ids reach the query');

    detach(root);
});

test('scrollToMessage escapes the id for the attribute selector', () => {
    const root = new FakeRoot();
    attach(root, null, fakeDotNet());
    root.addRow('weird "quoted" \\id', 10);

    const moved = scrollToMessage(root, 'weird "quoted" \\id', false);

    assert.equal(moved, true, 'an id with quotes and backslashes still resolves to its row');
    assert.ok(root.selectors[0].includes('\\"'), 'the quote was escaped for the selector string');

    detach(root);
});

test('scrollToMessage without the module attached still scrolls a plain root', () => {
    const plain = {
        scrollTop: 50,
        getBoundingClientRect: () => ({ top: 0 }),
        children: [],
        scrollTo({ top }) {
            plain.top = top;
        },
        querySelector(selector) {
            const match = /^\[data-message-id="((?:[^"\\]|\\.)*)"\]$/.exec(selector);
            return match && match[1] === 'm-1' ? { getBoundingClientRect: () => ({ top: 130 }) } : null;
        },
    };

    assert.equal(scrollToMessage(plain, 'm-1', true), true);
    assert.equal(plain.top, 180, 'a root that is not attached still gets the scroll (50 + (130 - 0))');
    assert.equal(scrollToMessage(null, 'm-1', true), false);
});

test('scrollToMessage reports the landing position when it differs from the detached baseline', () => {
    const root = new FakeRoot();
    const dotNet = fakeDotNet();
    attach(root, null, dotNet);
    root.scrollHeight = 3000;
    root.rectTop = 0;
    root.addRow('answer-9', 560);

    const moved = scrollToMessage(root, 'answer-9', false);
    assert.equal(moved, true, 'the named turn exists');
    assert.equal(root.scrollCalls[0].top, 1160, 'the offset is measured against the root itself');

    // The jump detached the baseline before the animation (lastNear = false), so its frames stay silent…
    root.scrollTop = 1200;
    root.emit('scroll');
    assert.deepEqual(dotNet.calls, [], 'not one frame of the jump is a crossing');

    // …but this landing lands INSIDE the bottom band (3000 - 2570 - 400 = 30): the release resync corrects
    // the detached baseline and reports the true position, because a jump to the newest turn must leave
    // .NET following again.
    root.scrollTop = 2570;
    root.emit('scrollend');
    assert.deepEqual(dotNet.calls, [{ method: 'OnScrollStateAsync', value: true }],
        'the landing resync reports the position .NET could not know on its own');

    detach(root);
});

test('scrollToMessage subtracts the log padding-top from the target offset', () => {
    const root = new FakeRoot();
    const original = globalThis.getComputedStyle;
    try {
        globalThis.getComputedStyle = () => ({ paddingTop: '24px' });
        attach(root, null, fakeDotNet());
        root.rectTop = 0;
        root.addRow('m-1', 130);
        root.scrollTop = 50;

        scrollToMessage(root, 'm-1', false);

        assert.equal(root.scrollCalls[0].top, 156,
            '50 + (130 - 0) - 24: the padding above the first scrollable pixel must not push the turn down');
    } finally {
        if (original === undefined) {
            delete globalThis.getComputedStyle;
        } else {
            globalThis.getComputedStyle = original;
        }
    }

    detach(root);
});

test('revealFocusedSuggestion scrolls the row by the overflow, never the log', () => {
    const log = { scrollTop: 80, scrollLeft: 0 };
    const row = {
        scrollLeft: 0,
        getBoundingClientRect: () => ({ left: 10, right: 110, width: 100 }),
        scrollToCalls: [],
        scrollTo({ left, behavior }) {
            this.scrollToCalls.push({ left, behavior });
            this.scrollLeft = left;
        },
    };
    const chip = {
        getBoundingClientRect: () => ({ left: 90, right: 140, width: 50 }),
    };

    const original = globalThis.matchMedia;
    try {
        globalThis.matchMedia = () => ({ matches: false });
        const moved = revealFocusedSuggestion(row, chip, log);
        assert.equal(moved, true);
        assert.equal(row.scrollToCalls.length, 1);
        assert.equal(row.scrollToCalls[0].left, 30, 'right overflow 30px is the only scroll');
        assert.equal(row.scrollToCalls[0].behavior, 'smooth');
        assert.equal(log.scrollTop, 80, 'the transcript never moves');
    } finally {
        if (original === undefined) {
            delete globalThis.matchMedia;
        } else {
            globalThis.matchMedia = original;
        }
    }
});

test('revealFocusedSuggestion uses auto when reduced motion is preferred', () => {
    const row = {
        scrollLeft: 0,
        getBoundingClientRect: () => ({ left: 0, right: 100, width: 100 }),
        scrollToCalls: [],
        scrollTo({ left, behavior }) {
            this.scrollToCalls.push({ left, behavior });
        },
    };
    const chip = {
        getBoundingClientRect: () => ({ left: -20, right: 30, width: 50 }),
    };
    const original = globalThis.matchMedia;
    try {
        globalThis.matchMedia = () => ({ matches: true });
        revealFocusedSuggestion(row, chip, { scrollTop: 12 });
        assert.equal(row.scrollToCalls[0].behavior, 'auto');
        assert.equal(row.scrollToCalls[0].left, -20, 'left overflow is subtracted from scrollLeft');
    } finally {
        if (original === undefined) {
            delete globalThis.matchMedia;
        } else {
            globalThis.matchMedia = original;
        }
    }
});

test('revealFocusedSuggestion is a no-op when the chip is fully visible', () => {
    const row = {
        scrollLeft: 4,
        getBoundingClientRect: () => ({ left: 0, right: 200, width: 200 }),
        scrollToCalls: [],
        scrollTo() { this.scrollToCalls.push('called'); },
    };
    const chip = {
        getBoundingClientRect: () => ({ left: 20, right: 80, width: 60 }),
    };
    assert.equal(revealFocusedSuggestion(row, chip, { scrollTop: 0 }), false);
    assert.equal(row.scrollToCalls.length, 0);
});

function suggestionRow({ className, chip }) {
    return {
        className,
        scrollLeft: 0,
        matches(selector) {
            return selector.split(',').some((part) => this.className.split(/\s+/).includes(part.trim().replace(/^\./, '')));
        },
        contains(node) { return node === chip; },
        getBoundingClientRect: () => ({ left: 10, right: 110, width: 100 }),
        scrollToCalls: [],
        scrollTo({ left, behavior }) {
            this.scrollToCalls.push({ left, behavior });
            this.scrollLeft = left;
        },
    };
}

test('attach reveals an overflowing focused suggestion without moving the log', () => {
    const original = globalThis.matchMedia;
    const log = new FakeRoot({ scrollTop: 80 });
    const chip = {
        getBoundingClientRect: () => ({ left: 90, right: 140, width: 50 }),
    };
    const row = suggestionRow({
        className: 'tm-ai-conversation__suggestions tm-ai-conversation__suggestions--auto',
        chip,
    });
    const originalQuery = log.querySelector.bind(log);
    log.querySelector = (selector) =>
        selector === '.tm-ai-conversation__suggestions' ? row : originalQuery(selector);

    try {
        globalThis.matchMedia = () => ({ matches: false });
        attach(log, null, fakeDotNet());

        const handlers = log.listeners.get('focusin');
        assert.ok(handlers?.length > 0, 'attach registers a focusin listener on the log');
        handlers[0].handler({ type: 'focusin', target: chip, currentTarget: log });

        assert.equal(row.scrollToCalls.length, 1, 'the overflowing chip is revealed');
        assert.equal(row.scrollToCalls[0].left, 30);
        assert.equal(log.scrollTop, 80, 'the transcript never moves');

        detach(log);
        assert.equal(log.listeners.has('focusin'), false, 'detach releases the focusin listener');
    } finally {
        if (original === undefined) {
            delete globalThis.matchMedia;
        } else {
            globalThis.matchMedia = original;
        }
    }
});

test('attach does not scroll a Wrap suggestions row', () => {
    const original = globalThis.matchMedia;
    const log = new FakeRoot({ scrollTop: 80 });
    const chip = {
        getBoundingClientRect: () => ({ left: 90, right: 140, width: 50 }),
    };
    const row = suggestionRow({
        className: 'tm-ai-conversation__suggestions',
        chip,
    });
    const originalQuery = log.querySelector.bind(log);
    log.querySelector = (selector) =>
        selector === '.tm-ai-conversation__suggestions' ? row : originalQuery(selector);

    try {
        globalThis.matchMedia = () => ({ matches: false });
        attach(log, null, fakeDotNet());
        log.listeners.get('focusin')[0].handler({ type: 'focusin', target: chip, currentTarget: log });
        assert.equal(row.scrollToCalls.length, 0, 'Wrap never scrolls on focus');
        detach(log);
    } finally {
        if (original === undefined) {
            delete globalThis.matchMedia;
        } else {
            globalThis.matchMedia = original;
        }
    }
});

