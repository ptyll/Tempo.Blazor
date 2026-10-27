// chat.js (TmChat composer) tests with hand-rolled DOM stubs (no jsdom): Enter submit +
// preventDefault, Shift+Enter new line, the IME guard (isComposing / keyCode 229), modifier
// chords, Ctrl/⌘+Enter submit when enterToSend is false, per-call submit routing, auto-resize
// up to maxRows (input events AND a ResizeObserver on width changes), dispose, per-composer
// isolation and the focus/focusIfNeeded helpers.
//
// The second half covers the SCROLLER half of the module (FZE-6): attachScroller's at-bottom
// detection with a threshold tolerance, the report-on-change scroll contract (the away report
// suppressed while a programmatic scroll is in flight, the near-top report skipped while the
// load-older button holds focus), the scroller's OWN pre-update snapshot (refreshed on every
// scroll event and at the end of every afterUpdate — there is no beforeUpdate round trip) with
// the pinned flag (append scrolls only when the viewport WAS at the bottom — including two
// appends inside ONE smooth scroll; away from the bottom it stays put and reports `unread`;
// `none` re-pins a pinned viewport whose content grew), the ANCHOR-based prepend restore
// (falls back to scrollTop += deltaHeight when the anchor row is gone), scrollToBottom,
// scrollToMessage(id), focusMessage(id), reduced-motion aware scrolling and dispose.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachComposer, attachComposerById, attachScroller, focus, focusIfNeeded, restoreFocus } from '../chat.js';

// ── fakes ───────────────────────────────────────────────────────────────────────────────────

function listenerBag() {
    const map = new Map();
    return {
        addEventListener(type, fn) {
            map.set(type, [...(map.get(type) ?? []), fn]);
        },
        removeEventListener(type, fn) {
            const list = map.get(type) ?? [];
            const index = list.indexOf(fn);
            if (index >= 0) list.splice(index, 1);
            map.set(type, list);
        },
        dispatch(type, event = {}) {
            for (const fn of [...(map.get(type) ?? [])]) fn(event);
            return event;
        },
        count(type) { return (map.get(type) ?? []).length; },
    };
}

function fakeTextarea(overrides = {}) {
    const { scrollHeight: initialScrollHeight = 41, ...rest } = overrides;
    const el = {
        ...listenerBag(),
        style: {},
        ...rest,
    };
    // A browser reports the natural content height through `scrollHeight` only while the inline
    // height is 'auto' (resize() resets the height before measuring); once a fixed height is
    // applied the element is at least as tall as its content and `scrollHeight` follows it.
    let contentHeight = initialScrollHeight;
    Object.defineProperty(el, 'scrollHeight', {
        configurable: true,
        get() {
            if (el.style.height === 'auto') return contentHeight;
            const applied = Number.parseFloat(el.style.height);
            return Number.isFinite(applied) ? applied : contentHeight;
        },
        set(value) { contentHeight = value; },
    });
    return el;
}

function dotNet() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(name, ...args) {
            calls.push({ name, args });
            return Promise.resolve();
        },
    };
}

function keyEvent(overrides = {}) {
    return {
        key: 'Enter',
        shiftKey: false,
        altKey: false,
        ctrlKey: false,
        metaKey: false,
        isComposing: false,
        keyCode: 0,
        prevented: false,
        preventDefault() { this.prevented = true; },
        ...overrides,
    };
}

// Computed style the resize maths read in a browser (TmChat.razor.css: padding space-2-5, 1px
// borders, line-height 1.5 over a 14px font → 21px line, and a 120px CSS max-height cap).
const BROWSER_STYLE = {
    lineHeight: '21px',
    paddingTop: '10px',
    paddingBottom: '10px',
    borderTopWidth: '1px',
    borderBottomWidth: '1px',
    boxSizing: 'border-box',
    maxHeight: '120px',
    fontSize: '14px',
};

function installComputedStyle(style = BROWSER_STYLE) {
    const previous = globalThis.getComputedStyle;
    globalThis.getComputedStyle = () => style;
    return () => { globalThis.getComputedStyle = previous; };
}

// ── argument validation ─────────────────────────────────────────────────────────────────────

test('attachComposer requires an element and a dotnet reference', () => {
    const restore = installComputedStyle();
    try {
        assert.throws(() => attachComposer(null, dotNet()), /element/i);
        assert.throws(() => attachComposer(fakeTextarea(), null), /DotNetObjectReference/i);
    } finally {
        restore();
    }
});

// ── Enter / Shift+Enter / IME guard ─────────────────────────────────────────────────────────

test('Enter submits once and calls preventDefault', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet);

        const event = textarea.dispatch('keydown', keyEvent());

        assert.equal(event.prevented, true, 'Enter must not insert a new line when it submits');
        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);
    } finally {
        restore();
    }
});

test('Shift+Enter does not submit and does not preventDefault (native new line)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet);

        const event = textarea.dispatch('keydown', keyEvent({ shiftKey: true }));

        assert.equal(event.prevented, false, 'Shift+Enter stays native so the textarea inserts a new line');
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('isComposing=true never submits (IME composition in progress)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet);

        const event = textarea.dispatch('keydown', keyEvent({ isComposing: true }));

        assert.equal(event.prevented, false, 'the composition must keep the default so the IME can finish');
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('keyCode 229 never submits (legacy IME key code)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet);

        const event = textarea.dispatch('keydown', keyEvent({ keyCode: 229 }));

        assert.equal(event.prevented, false);
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('Alt/Ctrl/Meta+Enter stays native (host shortcuts, not a submit)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet);

        textarea.dispatch('keydown', keyEvent({ altKey: true }));
        textarea.dispatch('keydown', keyEvent({ ctrlKey: true }));
        textarea.dispatch('keydown', keyEvent({ metaKey: true }));

        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('enterToSend:false leaves Enter as a native new line (send only via the button)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet, { enterToSend: false });

        const event = textarea.dispatch('keydown', keyEvent());

        assert.equal(event.prevented, false);
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('enterToSend:false still submits on Ctrl+Enter with preventDefault', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet, { enterToSend: false });

        const event = textarea.dispatch('keydown', keyEvent({ ctrlKey: true }));

        assert.equal(event.prevented, true, 'Ctrl+Enter is a submit, not a new line');
        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);
    } finally {
        restore();
    }
});

test('enterToSend:false still submits on Meta+Enter (⌘+Enter)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet, { enterToSend: false });

        textarea.dispatch('keydown', keyEvent({ metaKey: true }));

        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);
    } finally {
        restore();
    }
});

test('enterToSend:false keeps Shift+Enter and Alt+Enter native (no submit)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet, { enterToSend: false });

        const shift = textarea.dispatch('keydown', keyEvent({ shiftKey: true }));
        const alt = textarea.dispatch('keydown', keyEvent({ altKey: true }));

        assert.equal(shift.prevented, false);
        assert.equal(alt.prevented, false);
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('enterToSend:false never submits inside an IME composition (Ctrl+Enter or not)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet, { enterToSend: false });

        const plain = textarea.dispatch('keydown', keyEvent({ isComposing: true }));
        const ctrl = textarea.dispatch('keydown', keyEvent({ ctrlKey: true, isComposing: true }));
        const legacy = textarea.dispatch('keydown', keyEvent({ ctrlKey: true, keyCode: 229 }));

        assert.equal(plain.prevented, false);
        assert.equal(ctrl.prevented, false);
        assert.equal(legacy.prevented, false);
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

test('enterToSend:true keeps Ctrl+Enter native (host shortcuts win)', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet);

        const event = textarea.dispatch('keydown', keyEvent({ ctrlKey: true }));

        assert.equal(event.prevented, false);
        assert.deepEqual(dotnet.calls, []);
    } finally {
        restore();
    }
});

// ── submit routing (thread input / edit input) ──────────────────────────────────────────────

test('submitMethod option routes the submit to a custom JSInvokable method', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        attachComposer(textarea, dotnet, { submitMethod: 'OnThreadComposerSubmit' });

        textarea.dispatch('keydown', keyEvent());

        assert.deepEqual(dotnet.calls, [{ name: 'OnThreadComposerSubmit', args: [] }]);
    } finally {
        restore();
    }
});

test('a rejected invokeMethodAsync does not throw out of the keydown handler', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const failing = {
            invokeMethodAsync() { return Promise.reject(new Error('circuit gone')); },
        };
        attachComposer(textarea, failing, { maxRows: 0 });

        assert.doesNotThrow(() => textarea.dispatch('keydown', keyEvent()));
    } finally {
        restore();
    }
});

// ── dispose / isolation ─────────────────────────────────────────────────────────────────────

test('dispose detaches the listeners and is idempotent', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        const dotnet = dotNet();
        const handle = attachComposer(textarea, dotnet, { maxRows: 3 });

        assert.equal(textarea.count('keydown'), 1);
        assert.equal(textarea.count('input'), 1, 'auto-resize listens for input');

        handle.dispose();
        handle.dispose();

        assert.equal(textarea.count('keydown'), 0);
        assert.equal(textarea.count('input'), 0);

        textarea.dispatch('keydown', keyEvent());
        assert.deepEqual(dotnet.calls, [], 'a disposed composer must not submit');
    } finally {
        restore();
    }
});

test('two composers on one page work independently (no module-level state)', () => {
    const restore = installComputedStyle();
    try {
        const first = fakeTextarea();
        const second = fakeTextarea();
        const firstDotnet = dotNet();
        const secondDotnet = dotNet();
        const firstHandle = attachComposer(first, firstDotnet, { maxRows: 0 });
        const secondHandle = attachComposer(second, secondDotnet);

        first.dispatch('keydown', keyEvent());

        assert.deepEqual(firstDotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);
        assert.deepEqual(secondDotnet.calls, [], 'an event on one composer must not submit another');

        firstHandle.dispose();
        second.dispatch('keydown', keyEvent());
        assert.deepEqual(secondDotnet.calls, [{ name: 'OnComposerSubmit', args: [] }],
            'disposing one composer must not detach another');
        secondHandle.dispose();
    } finally {
        restore();
    }
});

// ── auto-resize ─────────────────────────────────────────────────────────────────────────────

test('auto-resize grows the textarea with its content and starts at one line', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea({ scrollHeight: 41 });
        const handle = attachComposer(textarea, dotNet(), { maxRows: 5 });

        assert.equal(textarea.style.height, '43px', 'scrollHeight 41 + 2px borders under border-box');
        assert.equal(textarea.style.overflowY, '', 'no inline overflow while the content fits');

        textarea.scrollHeight = 83; // 3 lines: 3*21 content + 20 padding (borders come on top)
        textarea.dispatch('input', {});
        assert.equal(textarea.style.height, '85px');

        assert.equal(typeof handle.resize, 'function', 'the handle exposes resize for a programmatic clear');
        handle.resize();
    } finally {
        restore();
    }
});

test('auto-resize shrinks back to one line when the text is cleared', () => {
    const restore = installComputedStyle({ ...BROWSER_STYLE, maxHeight: '300px' });
    try {
        const textarea = fakeTextarea({ scrollHeight: 41 });
        const handle = attachComposer(textarea, dotNet(), { maxRows: 12 });

        textarea.scrollHeight = 200; // a long draft
        textarea.dispatch('input', {});
        assert.equal(textarea.style.height, '202px', 'the textarea grew with the draft');

        textarea.scrollHeight = 41; // the text was removed
        handle.resize(); // a programmatic clear raises no `input` event

        assert.equal(textarea.style.height, '43px', 'the textarea must shrink back to the minimum');
    } finally {
        restore();
    }
});

test('auto-resize caps at maxRows and marks the overflow scrollable', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea({ scrollHeight: 440 }); // 20 lines
        attachComposer(textarea, dotNet(), { maxRows: 5 });

        // 5 rows of 21px + 20px padding + 2px border = 127, but the computed max-height (120px)
        // of .tm-chat__input wins — the CSS cap is the real limit (TmChat passes maxRows: 5 to
        // agree with that cap).
        assert.equal(textarea.style.height, '120px');
        assert.equal(textarea.style.overflowY, 'auto');
    } finally {
        restore();
    }
});

test('a ResizeObserver re-measures when the width changes and is disconnected on dispose', () => {
    const restore = installComputedStyle();
    const previousRO = globalThis.ResizeObserver;
    try {
        const observed = [];
        const callbacks = [];
        let disconnected = 0;
        class FakeResizeObserver {
            constructor(cb) { callbacks.push(cb); }
            observe(el) { observed.push(el); }
            disconnect() { disconnected++; }
        }
        globalThis.ResizeObserver = FakeResizeObserver;

        const textarea = fakeTextarea({ scrollHeight: 41 });
        const handle = attachComposer(textarea, dotNet(), { maxRows: 5 });

        assert.equal(observed.length, 1, 'the textarea is observed');
        assert.equal(textarea.style.height, '43px');

        // Same width → no re-measure; a different width → the height is recomputed.
        callbacks[0]([{ contentRect: { width: 320 } }]);
        assert.equal(textarea.style.height, '43px', 'an unchanged width must not re-measure');

        textarea.scrollHeight = 200;
        callbacks[0]([{ contentRect: { width: 200 } }]);
        assert.equal(textarea.style.height, '120px', 'a narrower container re-wraps and re-measures');

        handle.dispose();
        assert.equal(disconnected, 1, 'dispose disconnects the observer');
    } finally {
        if (previousRO === undefined) delete globalThis.ResizeObserver;
        else globalThis.ResizeObserver = previousRO;
        restore();
    }
});

test('no ResizeObserver is created when the host does not implement it', () => {
    const restore = installComputedStyle();
    const previousRO = globalThis.ResizeObserver;
    assert.equal(previousRO, undefined, 'the Node test host has no ResizeObserver by default');
    try {
        const textarea = fakeTextarea();
        assert.doesNotThrow(() => attachComposer(textarea, dotNet(), { maxRows: 5 }));
        assert.equal(textarea.style.height, '43px', 'the initial measure still ran');
    } finally {
        if (previousRO !== undefined) globalThis.ResizeObserver = previousRO;
        restore();
    }
});

test('auto-resize is off without maxRows', () => {
    const restore = installComputedStyle();
    try {
        const textarea = fakeTextarea();
        attachComposer(textarea, dotNet());

        assert.equal(textarea.style.height, undefined);
        assert.equal(textarea.count('input'), 0);
    } finally {
        restore();
    }
});

// ── focus helpers ───────────────────────────────────────────────────────────────────────────

test('focus focuses the element and tolerates missing ones', () => {
    let focused = 0;
    focus({ focus() { focused++; } });
    assert.equal(focused, 1);

    assert.doesNotThrow(() => focus(null));
    assert.doesNotThrow(() => focus({}));
});

test('focusIfNeeded focuses only when the element does not already hold focus', () => {
    const focused = { focusCount: 0, focus() { this.focusCount++; } };
    focused.ownerDocument = { activeElement: null };

    focusIfNeeded(focused);
    assert.equal(focused.focusCount, 1, 'an unfocused element gets the focus');

    focused.ownerDocument.activeElement = focused;
    focusIfNeeded(focused);
    assert.equal(focused.focusCount, 1, 'an already-focused element is left alone (no keyboard close)');

    assert.doesNotThrow(() => focusIfNeeded(null));
    assert.doesNotThrow(() => focusIfNeeded({}));
    assert.doesNotThrow(() => focusIfNeeded({ ownerDocument: { activeElement: null } }));
});

// ── attachComposerById (FZE-12: auto-attach by id inside the chat root) ──────────────────────

/** A chat root stub: `querySelector` resolves the id selector against the CURRENT input. */
function fakeRoot(current) {
    const root = {
        queries: [],
        querySelector(selector) {
            root.queries.push(selector);
            return typeof current === 'function' ? current(selector) : current;
        },
    };
    return root;
}

/** A focusable input stub (records focus moves, exposes an ownerDocument for focusIfNeeded). */
function fakeFocusableInput() {
    const el = { ...listenerBag(), focusCount: 0, focus() { this.focusCount++; } };
    el.ownerDocument = { activeElement: null };
    return el;
}

test('attachComposerById requires a root, an input id and a dotnet reference', () => {
    assert.throws(() => attachComposerById(null, 'composer', dotNet()), /root/i);
    assert.throws(() => attachComposerById(fakeRoot(null), '', dotNet()), /input id/i);
    assert.throws(() => attachComposerById(fakeRoot(null), 'composer', null), /DotNetObjectReference/i);
});

test('attachComposerById scopes the id lookup to the root and reuses attachComposer', () => {
    const restore = installComputedStyle();
    try {
        const inside = fakeTextarea();
        const outside = fakeTextarea(); // the SAME id on another element of the page
        const root = fakeRoot((selector) => (selector === '#composer-id' ? inside : outside));
        const dotnet = dotNet();

        const handle = attachComposerById(root, 'composer-id', dotnet);

        // The lookup is an id query ON THE ROOT — never a page-wide search.
        assert.ok(root.queries.includes('#composer-id'),
            'the input is resolved through root.querySelector(#id), not a global query');

        // The behavior is attachComposer's (no second Enter/IME implementation): Enter on the
        // element resolved INSIDE the root submits with preventDefault…
        const event = inside.dispatch('keydown', keyEvent());
        assert.equal(event.prevented, true);
        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);

        // …and an element that merely shares the id OUTSIDE the root gets nothing.
        const stray = outside.dispatch('keydown', keyEvent());
        assert.equal(stray.prevented, false, 'the lookup must stay scoped to the chat root');
        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }],
            'no second submit from outside the root');

        assert.equal(typeof handle.sync, 'function');
        assert.equal(typeof handle.focusIfNeeded, 'function');
        assert.equal(typeof handle.resize, 'function');
        assert.equal(typeof handle.dispose, 'function');
    } finally {
        restore();
    }
});

test('attachComposerById falls back to an unescaped selector when CSS.escape is missing', () => {
    const restore = installComputedStyle();
    try {
        const input = fakeTextarea();
        const root = fakeRoot((selector) => (selector === '#tm-chat-composer-1' ? input : null));
        attachComposerById(root, 'tm-chat-composer-1', dotNet());
        assert.ok(root.queries.includes('#tm-chat-composer-1'),
            'without CSS.escape (fake DOM) the plain #id selector is used');
        assert.equal(input.count('keydown'), 1, 'the input is attached through the fallback too');
    } finally {
        restore();
    }
});

test('sync(): the same node is a no-op, a replaced node re-attaches, a missing node detaches', () => {
    const restore = installComputedStyle();
    try {
        let input = fakeTextarea();
        const root = fakeRoot((selector) => (selector === '#composer-id' ? input : null));
        const handle = attachComposerById(root, 'composer-id', dotNet());
        assert.equal(input.count('keydown'), 1);

        // Same node: sync() re-queries, finds the SAME element and does nothing.
        assert.equal(handle.sync(), true, 'the input is still found');
        assert.equal(input.count('keydown'), 1, 'the same node must not be attached twice');

        // Replaced node: the old element is disposed (listeners gone), the new one attached.
        const replaced = input;
        input = fakeTextarea();
        assert.equal(handle.sync(), true, 'the replaced node is found');
        assert.equal(replaced.count('keydown'), 0, 'the old element was detached');
        assert.equal(input.count('keydown'), 1, 'the new element was attached');

        // Missing node: everything is detached and the sync reports not-found.
        input = null;
        assert.equal(handle.sync(), false, 'a missing input is reported as not found');
        // (the replaced element was detached in this step — asserted via the next test's focus)
    } finally {
        restore();
    }
});

test('focusIfNeeded() targets the current input and tolerates a missing one', () => {
    let input = fakeFocusableInput();
    const root = fakeRoot((selector) => (selector === '#composer-id' ? input : null));
    const handle = attachComposerById(root, 'composer-id', dotNet());

    handle.focusIfNeeded();
    assert.equal(input.focusCount, 1, 'the auto-attached input is the focus target');

    input.ownerDocument.activeElement = input;
    handle.focusIfNeeded();
    assert.equal(input.focusCount, 1, 'an input that already holds focus is left alone');

    input = null; // the input disappeared from the DOM
    handle.sync();
    assert.doesNotThrow(() => handle.focusIfNeeded(), 'no attached input — a silent no-op');
});

test('focusIfNeeded() re-resolves first — a swapped node gets the focus', () => {
    let input = fakeFocusableInput();
    const root = fakeRoot((selector) => (selector === '#composer-id' ? input : null));
    const handle = attachComposerById(root, 'composer-id', dotNet());

    // The host swapped the element WITHOUT a TmChat render — no sync() has run yet, the
    // handle still holds the old node.
    const replaced = input;
    input = fakeFocusableInput();

    handle.focusIfNeeded();

    assert.equal(replaced.focusCount, 0, 'the detached predecessor is never focused');
    assert.equal(input.focusCount, 1, 'the NEW node is the focus target');
});

test('resize() re-measures the auto-attached input after a cleared value', () => {
    const restore = installComputedStyle({ ...BROWSER_STYLE, maxHeight: '300px' });
    try {
        const input = fakeTextarea({ scrollHeight: 41 });
        const handle = attachComposerById(fakeRoot(input), 'composer-id', dotNet(), { maxRows: 12 });

        input.scrollHeight = 200; // a long draft grew the input
        input.dispatch('input', {});
        assert.equal(input.style.height, '202px', 'the draft grew the input');

        input.scrollHeight = 41; // SendAsync cleared the value — no `input` event fires
        handle.resize(); // TmChat's ResizeAutoComposerAfterClearAsync path

        assert.equal(input.style.height, '43px', 'the cleared input shrinks back to one line');
    } finally {
        restore();
    }
});

test('resize() is a silent no-op after dispose and while no input is found', () => {
    const restore = installComputedStyle();
    try {
        const input = fakeTextarea({ scrollHeight: 41 });
        const handle = attachComposerById(fakeRoot(input), 'composer-id', dotNet(), { maxRows: 5 });
        assert.equal(input.style.height, '43px', 'the initial measure ran');

        handle.dispose();
        input.scrollHeight = 200; // nothing may re-measure this element anymore
        assert.doesNotThrow(() => handle.resize());
        assert.equal(input.style.height, '43px', 'a disposed handle must not re-measure');

        // An input that is gone (never found, or detached by a sync() after removal).
        let missing = fakeTextarea({ scrollHeight: 41 });
        const root = fakeRoot((selector) => (selector === '#composer-id' ? missing : null));
        const gone = attachComposerById(root, 'composer-id', dotNet(), { maxRows: 5 });
        missing = null;
        gone.sync(); // reports not-found and detaches
        assert.doesNotThrow(() => gone.resize(), 'no attached input — a silent no-op');
    } finally {
        restore();
    }
});

test('dispose() is idempotent and a disposed handle stops working', () => {
    const input = fakeTextarea();
    const root = fakeRoot(input);
    const dotnet = dotNet();
    const handle = attachComposerById(root, 'composer-id', dotnet);

    handle.dispose();
    handle.dispose(); // idempotent

    assert.equal(input.count('keydown'), 0, 'the listeners are gone after dispose');
    input.dispatch('keydown', keyEvent());
    assert.deepEqual(dotnet.calls, [], 'a disposed handle no longer submits');
    assert.equal(handle.sync(), false, 'a disposed handle no longer re-queries');
    assert.doesNotThrow(() => handle.focusIfNeeded());
});

test('two auto-attached chats are independent (no module-level state)', () => {
    const restore = installComputedStyle();
    try {
        const a = fakeTextarea();
        const b = fakeTextarea();
        const dotnet = dotNet();
        const handleA = attachComposerById(fakeRoot(a), 'composer-a', dotnet);
        const handleB = attachComposerById(fakeRoot(b), 'composer-b', dotnet);

        handleA.dispose();

        const event = b.dispatch('keydown', keyEvent());
        assert.equal(event.prevented, true, 'the sibling chat is untouched by A\u2019s dispose');
        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);
        handleB.dispose();
    } finally {
        restore();
    }
});

test('attachComposerById forwards the options (enterToSend, submitMethod) to attachComposer', () => {
    const restore = installComputedStyle();
    try {
        const input = fakeTextarea();
        const dotnet = dotNet();
        const handle = attachComposerById(fakeRoot(input), 'composer-id', dotnet,
            { enterToSend: false, maxRows: 3, submitMethod: 'OnComposerSubmit' });

        // With enterToSend:false the plain Enter stays native; Ctrl+Enter submits.
        input.dispatch('keydown', keyEvent());
        assert.deepEqual(dotnet.calls, [], 'plain Enter is a new line');
        const ctrl = input.dispatch('keydown', keyEvent({ ctrlKey: true }));
        assert.equal(ctrl.prevented, true);
        assert.deepEqual(dotnet.calls, [{ name: 'OnComposerSubmit', args: [] }]);

        handle.dispose();
        assert.equal(input.style.height, '43px',
            'maxRows was forwarded — the one-line content measures 41 + 2 border = 43px, under the 3-row cap');
    } finally {
        restore();
    }
});

// ── restoreFocus (thread close) ──────────────────────────────────────────────────────────────

/** A focusable element stub with a connected flag and an optional shared document. */
function focusable(doc, { connected = true } = {}) {
    const el = {
        isConnected: connected,
        focusCalls: 0,
        focus() { this.focusCalls++; },
    };
    if (doc) el.ownerDocument = doc;
    return el;
}

test('restoreFocus focuses the target when the restore owns the focus (body active)', () => {
    const doc = { body: { tag: 'body' }, activeElement: null };
    doc.activeElement = doc.body;
    const opener = focusable(doc);
    const fallback = focusable(doc);

    assert.equal(restoreFocus(opener, fallback), true, 'focus moved onto the opener');
    assert.equal(opener.focusCalls, 1);
    assert.equal(fallback.focusCalls, 0, 'the fallback stays untouched while the target lives');
});

test('restoreFocus proceeds when activeElement is null (no focus anywhere yet)', () => {
    const doc = { body: { tag: 'body' }, activeElement: null };
    const opener = focusable(doc);

    assert.equal(restoreFocus(opener, null), true);
    assert.equal(opener.focusCalls, 1);
});

test('restoreFocus does not steal focus while the composer has it', () => {
    const doc = { body: { tag: 'body' } };
    const composer = focusable(doc);
    doc.activeElement = composer; // the user is typing — the restore must not move focus
    const opener = focusable(doc);
    const fallback = focusable(doc);

    assert.equal(restoreFocus(opener, fallback), false,
        'a live input keeps the focus (no on-screen-keyboard close, no IME reset)');
    assert.equal(opener.focusCalls, 0);
    assert.equal(fallback.focusCalls, 0);
});

test('restoreFocus falls back when the target is detached, else reports false', () => {
    const doc = { body: { tag: 'body' }, activeElement: null };
    const detached = focusable(doc, { connected: false });
    const fallback = focusable(doc);

    assert.equal(restoreFocus(detached, fallback), true);
    assert.equal(fallback.focusCalls, 1, 'the fallback receives the focus');
    assert.equal(detached.focusCalls, 0, 'the detached target is never focused');

    const unreachableFallback = focusable(doc, { connected: false });
    assert.equal(restoreFocus(detached, unreachableFallback), false,
        'neither candidate is connected — nothing to restore onto');
    assert.equal(unreachableFallback.focusCalls, 0);
});

test('restoreFocus tolerates null arguments', () => {
    assert.doesNotThrow(() => restoreFocus(null, null));
    assert.equal(restoreFocus(null, null), false);

    const doc = { body: { tag: 'body' }, activeElement: null };
    const fallback = focusable(doc);
    assert.equal(restoreFocus(null, fallback), true);
    assert.equal(fallback.focusCalls, 1);
});

// ══ attachScroller (FZE-6) ══════════════════════════════════════════════════════════════════

/** A scroll container stub: scrollTop/scrollHeight/clientHeight are plain fields the test mutates. */
function fakeContainer(overrides = {}) {
    const messages = overrides.__messages ?? [];
    const el = {
        ...listenerBag(),
        scrollTop: 0,
        scrollHeight: 1000,
        clientHeight: 400,
        style: {},
        scrollToCalls: [],
        scrollTo(opts) {
            this.scrollToCalls.push(opts);
            this.scrollTop = opts.top;
        },
        querySelectorAll(selector) {
            return selector === '[data-message-id]' ? [...this.__messages] : [];
        },
        querySelector(selector) {
            return selector === '.tm-chat__load-older' ? this.__loadOlder ?? null : null;
        },
        __messages: messages,
        ...overrides,
    };
    // `__messages` may come through overrides — keep the same array object the test holds.
    if (overrides.__messages) el.__messages = overrides.__messages;
    return el;
}

function fakeMessage(id, { offsetTop = 0, offsetHeight = 40 } = {}) {
    return {
        id,
        offsetTop,
        offsetHeight,
        scrollIntoViewCalls: [],
        scrollIntoView(opts) { this.scrollIntoViewCalls.push(opts); },
        focusCalls: [],
        focus(opts) { this.focusCalls.push(opts); },
        getAttribute(name) {
            if (name === 'data-message-id') return this.id;
            return Object.prototype.hasOwnProperty.call(this, `__attr_${name}`) ? this[`__attr_${name}`] : null;
        },
        setAttribute(name, value) { this[`__attr_${name}`] = value; },
    };
}

/** The DotNetObjectReference double: records OnScrollStateChanged / OnScrollNearTop reports. */
function scrollerDotNet() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(name, ...args) {
            calls.push({ name, args });
            return Promise.resolve();
        },
    };
}

/** matchMedia stub; pass null to remove it again (Node has none by default). */
function installMatchMedia(matches) {
    const previous = globalThis.matchMedia;
    globalThis.matchMedia = matches === null ? undefined : () => ({ matches });
    return () => {
        if (previous === undefined) delete globalThis.matchMedia;
        else globalThis.matchMedia = previous;
    };
}

test('attachScroller requires a container and a dotnet reference', () => {
    assert.throws(() => attachScroller(null, scrollerDotNet()), /container/i);
    assert.throws(() => attachScroller(fakeContainer(), null), /DotNetObjectReference/i);
});

test('at-bottom detection honors the threshold tolerance and reports on CHANGE only', async () => {
    const container = fakeContainer({ scrollHeight: 1000, clientHeight: 400 });
    const dotnet = scrollerDotNet();
    const handle = attachScroller(container, dotnet);

    // Before any scroll event nothing has been reported yet.
    assert.deepEqual(dotnet.calls, []);

    // distance from bottom = 1000 - 500 - 400 = 100 > 40 → NOT at bottom.
    container.scrollTop = 500;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls, [
        { name: 'OnScrollStateChanged', args: [false] },
        { name: 'OnScrollNearTop', args: [false] },
    ], 'the first measurement reports both states once');

    // Same position again → no second report (state unchanged).
    container.dispatch('scroll');
    await Promise.resolve();
    assert.equal(dotnet.calls.length, 2, 'a scroll event without a state change must not report');

    // 1000 - 590 - 400 = 10 <= 40 → at bottom (the threshold tolerance).
    container.scrollTop = 590;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls[2], { name: 'OnScrollStateChanged', args: [true] });
    assert.equal(dotnet.calls.length, 3, 'the near-top state did not change, so it is not re-reported');

    // A tiny wobble WITHIN the tolerance must not flap the state (still ≤ 40 away).
    container.scrollTop = 585;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.equal(dotnet.calls.length, 3, 'movement inside the threshold is not a state change');

    assert.equal(typeof handle.dispose, 'function');
    handle.dispose();
});

test('a custom threshold tightens the tolerance', async () => {
    const container = fakeContainer({ scrollHeight: 1000, clientHeight: 400 });
    const dotnet = scrollerDotNet();
    attachScroller(container, dotnet, { threshold: 10 });

    // 30 away > 10 → not at bottom (the default 40 would have said "at bottom").
    container.scrollTop = 570;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls[0], { name: 'OnScrollStateChanged', args: [false] });
});

test('the near-top report fires OnScrollNearTop on state change only', async () => {
    const container = fakeContainer();
    const dotnet = scrollerDotNet();
    const handle = attachScroller(container, dotnet, { topThreshold: 80 });

    container.scrollTop = 30;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls, [
        { name: 'OnScrollStateChanged', args: [false] },
        { name: 'OnScrollNearTop', args: [true] },
    ], 'both states are measured and reported on the same event');

    container.scrollTop = 50; // still near top
    container.dispatch('scroll');
    await Promise.resolve();
    assert.equal(dotnet.calls.length, 2, 'no re-report inside the near-top band');

    container.scrollTop = 500; // far from top AND bottom
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls[2], { name: 'OnScrollNearTop', args: [false] });

    handle.dispose();
});

test('afterUpdate("append") at the bottom scrolls down (the scroller keeps its own snapshot)', () => {
    const container = fakeContainer({ scrollTop: 600, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    // The user is at the bottom — the last scroll event left the PRE-UPDATE snapshot behind
    // (there is no beforeUpdate round trip: TmChat only calls afterUpdate after the render).
    container.dispatch('scroll');
    // The render appended content: the container grew to 1600 while scrollTop stayed.
    container.scrollHeight = 1600;

    const status = handle.afterUpdate('append');

    assert.equal(status, 'scrolled', 'an append while the viewport WAS at the bottom scrolls to the bottom');
    assert.equal(container.scrollTop, 1600, 'scrollTop must land at the (new) bottom');
});

test('afterUpdate("append") away from the bottom does NOT scroll and reports unread', () => {
    const container = fakeContainer({ scrollTop: 600, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // pinned at the bottom
    // The user wheels up to read older messages — the gesture releases the pin, then the
    // scroll event records the away snapshot.
    container.dispatch('wheel', { deltaY: -120 });
    container.scrollTop = 200;
    container.dispatch('scroll');

    container.scrollHeight = 1600; // an append happened while the user read older messages

    const status = handle.afterUpdate('append');

    assert.equal(status, 'unread', 'C# shows the jump-to-new button and counts the new messages');
    assert.equal(container.scrollTop, 200, 'the reading position must not be disturbed');
    assert.equal(container.scrollToCalls.length, 0, 'no scroll call at all');
});

test('a manual scroll back to the bottom re-pins after a wheel-up (none growth, then append scrolls)', () => {
    const container = fakeContainer({ scrollTop: 600, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // pinned at the bottom
    // The user wheels up (the pin is released), then scrolls DOWN BY HAND to the bottom —
    // the scroller must re-pin on its own, or the next append would count as unread forever.
    container.dispatch('wheel', { deltaY: -120 });
    container.scrollTop = 600;
    container.dispatch('scroll');

    // A 'none' change grows the content (e.g. the typing indicator): the re-pinned viewport
    // follows it — and the refreshed snapshot records wasAtBottom for the NEXT change.
    container.scrollHeight = 1600;
    assert.equal(handle.afterUpdate('none'), 'none');
    assert.deepEqual(container.scrollToCalls[0], { top: 1600, behavior: 'smooth' },
        'the re-pinned viewport follows a grown content even on a no-change render');

    // The append that follows must SCROLL (not count unread) — the re-pin survived the 'none'.
    container.scrollHeight = 2200;
    assert.equal(handle.afterUpdate('append'), 'scrolled',
        'a user who scrolled back to the bottom by hand is reading at the bottom again');
    assert.deepEqual(container.scrollToCalls[1], { top: 2200, behavior: 'smooth' });
});

test('two appends inside ONE smooth scroll both report scrolled (the pin survives the animation)', () => {
    const container = fakeContainer({ scrollTop: 600, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // the user is at the bottom — snapshot recorded

    container.scrollHeight = 1600;
    assert.equal(handle.afterUpdate('append'), 'scrolled');
    assert.deepEqual(container.scrollToCalls[0], { top: 1600, behavior: 'smooth' });

    // The SECOND append lands while the smooth scroll is still in flight (the scrollTop is
    // somewhere mid-track and NOT within the bottom threshold): the pin keeps it scrolling.
    container.scrollTop = 1200;
    container.scrollHeight = 2200;
    assert.equal(handle.afterUpdate('append'), 'scrolled',
        'the pinned viewport follows the second append instead of counting it as unread');
    assert.deepEqual(container.scrollToCalls[1], { top: 2200, behavior: 'smooth' });
});

test('the away report is suppressed while a programmatic scroll is in flight', async () => {
    const container = fakeContainer({ scrollTop: 0, scrollHeight: 1000, clientHeight: 400 });
    const dotnet = scrollerDotNet();
    const handle = attachScroller(container, dotnet);

    handle.scrollToBottom(); // smooth — in flight

    container.scrollTop = 500; // an intermediate position of the animation
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls.filter(c => c.name === 'OnScrollStateChanged'), [],
        'no away report from the animation itself — C# must not flash the jump button');

    // The target is reached: the arrival IS reported (once).
    container.scrollTop = 1000;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls, [
        { name: 'OnScrollNearTop', args: [false] },
        { name: 'OnScrollStateChanged', args: [true] },
    ], 'the first scroll reported near-top=false; the arrival reports at-bottom once');
});

test('a user scroll upward during a programmatic scroll unpins and reports away', async () => {
    const container = fakeContainer({ scrollTop: 0, scrollHeight: 1000, clientHeight: 400 });
    const dotnet = scrollerDotNet();
    const handle = attachScroller(container, dotnet);

    handle.scrollToBottom(); // smooth — in flight
    container.dispatch('wheel', { deltaY: -120 }); // the user takes over mid-flight
    container.scrollTop = 300;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls.filter(c => c.name === 'OnScrollStateChanged'), [{ name: 'OnScrollStateChanged', args: [false] }],
        'the user gesture ends the flight: the away report goes out');

    // ... and the next append counts as unread instead of scrolling.
    container.scrollHeight = 1600;
    assert.equal(handle.afterUpdate('append'), 'unread');
});

test('afterUpdate("prepend") restores the position on the anchor row', () => {
    const top = fakeMessage('m-1', { offsetTop: 0, offsetHeight: 40 });
    const reading = fakeMessage('m-2', { offsetTop: 600, offsetHeight: 40 });
    const container = fakeContainer({ __messages: [top, reading], scrollTop: 500, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // the anchor is m-2 (600 + 40 > 500): offset 600 - 500 = 100

    // Older messages were PREPENDED: m-2 slid down by the prepended 600px.
    reading.offsetTop = 1200;
    container.scrollHeight = 1600;

    const status = handle.afterUpdate('prepend');

    assert.equal(status, 'preserved');
    assert.equal(container.scrollTop, 1100, '1200 (the anchor position) - 100 (the offset): the same row under the viewport top');
});

test('afterUpdate("prepend") ignores height changes BELOW the anchor (the delta would over-scroll)', () => {
    const top = fakeMessage('m-1', { offsetTop: 0, offsetHeight: 40 });
    const reading = fakeMessage('m-2', { offsetTop: 600, offsetHeight: 40 });
    const last = fakeMessage('m-3', { offsetTop: 900, offsetHeight: 100 });
    const container = fakeContainer({ __messages: [top, reading, last], scrollTop: 500, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // anchor m-2, offset 100

    // 600px prepended ABOVE the anchor + 300px of content grew BELOW it (e.g. an image loaded):
    reading.offsetTop = 1200;
    last.offsetTop = 1800;
    container.scrollHeight = 1900;

    assert.equal(handle.afterUpdate('prepend'), 'preserved');
    assert.equal(container.scrollTop, 1100,
        'the anchor decides: 1200 - 100 — NOT 500 + 900, which the delta fallback would produce');
});

test('afterUpdate("prepend") falls back to the delta when the anchor row is gone', () => {
    const container = fakeContainer({ scrollTop: 500, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // no rows → no anchor

    container.scrollHeight = 1600; // +600px of content above
    assert.equal(handle.afterUpdate('prepend'), 'preserved');
    assert.equal(container.scrollTop, 1100, '500 + (1600 - 1000): the viewport shows the same messages');
});

test('afterUpdate("prepend") with no growth keeps the position', () => {
    const container = fakeContainer({ scrollTop: 500, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll');
    container.scrollHeight = 1000; // nothing grew (e.g. a height-neutral change)
    assert.equal(handle.afterUpdate('prepend'), 'preserved');
    assert.equal(container.scrollTop, 500);
});

test('afterUpdate("none") re-pins a pinned viewport whose content grew (e.g. the typing indicator)', () => {
    const container = fakeContainer({ scrollTop: 600, scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    container.dispatch('scroll'); // pinned at the bottom
    container.scrollHeight = 1060; // the typing indicator appeared: the content grew under the pin

    assert.equal(handle.afterUpdate('none'), 'none');
    assert.deepEqual(container.scrollToCalls, [{ top: 1060, behavior: 'smooth' }],
        'the pinned viewport follows the growth instead of leaving the bottom below the fold');

    // NOT pinned (the user wheeled up): the same growth must NOT scroll.
    container.dispatch('wheel', { deltaY: -120 });
    container.scrollTop = 100;
    container.dispatch('scroll');
    container.scrollHeight = 1160;
    assert.equal(handle.afterUpdate('none'), 'none');
    assert.equal(container.scrollToCalls.length, 1, 'no second scroll — an away viewport stays put');
});

test('focusMessage focuses the row with tabindex=-1 and preventScroll (no scroll fight)', () => {
    const target = fakeMessage('m-2', { offsetTop: 40, offsetHeight: 40 });
    const container = fakeContainer({ __messages: [fakeMessage('m-1'), target] });
    const handle = attachScroller(container, scrollerDotNet());

    assert.equal(handle.focusMessage('m-2'), true);
    assert.equal(target.getAttribute('tabindex'), '-1', 'the row becomes programmatically focusable');
    assert.deepEqual(target.focusCalls, [{ preventScroll: true }]);
    assert.equal(container.scrollToCalls.length, 0, 'no scroll — the caller owns the position');
    assert.equal(handle.focusMessage('missing'), false, 'an unknown id is a no-op, not an error');
});

test('focusMessageVisible scrolls the row into view (nearest) before the preventScroll focus', () => {
    const target = fakeMessage('m-2', { offsetTop: 400, offsetHeight: 40 });
    const container = fakeContainer({ __messages: [fakeMessage('m-1'), target] });
    const handle = attachScroller(container, scrollerDotNet());

    assert.equal(handle.focusMessageVisible('m-2'), true);
    assert.deepEqual(target.scrollIntoViewCalls, [{ block: 'nearest', behavior: 'smooth' }],
        'the row is scrolled into view first — nearest never jumps the viewport');
    assert.deepEqual(target.focusCalls, [{ preventScroll: true }],
        'the focus itself does not fight the scroll (preventScroll)');
    assert.equal(target.getAttribute('tabindex'), '-1');
    assert.equal(handle.focusMessageVisible('missing'), false, 'an unknown id is a no-op, not an error');
});

test('focusMessageVisible honours reduced motion with an instant scroll', () => {
    const restore = installMatchMedia(true); // prefers-reduced-motion: reduce
    try {
        const target = fakeMessage('m-2', { offsetTop: 400, offsetHeight: 40 });
        const container = fakeContainer({ __messages: [fakeMessage('m-1'), target] });
        const handle = attachScroller(container, scrollerDotNet());

        assert.equal(handle.focusMessageVisible('m-2'), true);
        assert.deepEqual(target.scrollIntoViewCalls, [{ block: 'nearest', behavior: 'auto' }]);
    } finally {
        restore();
    }
});

test('the near-top report is skipped while the load-older button holds focus', async () => {
    const button = { getAttribute: () => null };
    const container = fakeContainer();
    container.__loadOlder = button;
    container.ownerDocument = { activeElement: button };
    const dotnet = scrollerDotNet();
    const handle = attachScroller(container, dotnet, { topThreshold: 80 });

    container.scrollTop = 30; // inside the near-top band — but the focus is on the button
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls.filter(c => c.name === 'OnScrollNearTop'), [],
        'a keyboard user tabbing through the button must not trigger a page load');

    // Focus moves away → the next scroll event reports the states once.
    container.ownerDocument = { activeElement: null };
    container.scrollTop = 500;
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls, [
        { name: 'OnScrollStateChanged', args: [false] },
        { name: 'OnScrollNearTop', args: [false] },
    ]);
});

test('scrollToBottom lands at the bottom and respects prefers-reduced-motion', () => {
    const container = fakeContainer({ scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    handle.scrollToBottom();
    assert.equal(container.scrollTop, 1000, 'the fake has no clamping: scrollTop is set to scrollHeight');
    assert.deepEqual(container.scrollToCalls, [{ top: 1000, behavior: 'smooth' }],
        'smooth is the default (the browser clamps to the real bottom)');

    const restore = installMatchMedia(true); // prefers-reduced-motion: reduce
    try {
        handle.scrollToBottom();
        assert.deepEqual(container.scrollToCalls[1], { top: 1000, behavior: 'auto' },
            'reduced motion must never get a smooth scroll');
    } finally {
        restore();
    }
});

test('scrollToBottom(instant) skips the animation (no spurious near-top reports on open)', () => {
    const container = fakeContainer({ scrollHeight: 1000, clientHeight: 400 });
    const handle = attachScroller(container, scrollerDotNet());

    handle.scrollToBottom(true);
    assert.deepEqual(container.scrollToCalls, [{ top: 1000, behavior: 'auto' }],
        'the first render jumps instantly — an animated initial scroll would fire events along the whole track');
});

test('scrollToBottom falls back to the scrollTop assignment when scrollTo is missing', () => {
    const container = fakeContainer({ scrollHeight: 1000 });
    delete container.scrollTo;
    const handle = attachScroller(container, scrollerDotNet());

    handle.scrollToBottom();
    assert.equal(container.scrollTop, 1000);
});

test('scrollToMessage scrolls the anchor into view and reports unknown ids', () => {
    const first = fakeMessage('m-1', { offsetTop: 0, offsetHeight: 60 });
    const second = fakeMessage('m-2', { offsetTop: 900, offsetHeight: 60 });
    const container = fakeContainer({ __messages: [first, second] });
    const handle = attachScroller(container, scrollerDotNet());

    assert.equal(handle.scrollToMessage('m-2'), true);
    assert.deepEqual(second.scrollIntoViewCalls, [{ block: 'center', behavior: 'smooth' }],
        'the anchor is centered, smoothly (unless reduced motion)');
    assert.equal(handle.scrollToMessage('missing'), false, 'an unknown id is a no-op, not an error');

    const restore = installMatchMedia(true);
    try {
        handle.scrollToMessage('m-1');
        assert.deepEqual(first.scrollIntoViewCalls[0], { block: 'center', behavior: 'auto' });
    } finally {
        restore();
    }
});

test('scrollToMessage falls back to offsetTop when scrollIntoView is unavailable', () => {
    const target = fakeMessage('m-9', { offsetTop: 777 });
    delete target.scrollIntoView;
    const container = fakeContainer({ __messages: [target] });
    const handle = attachScroller(container, scrollerDotNet());

    assert.equal(handle.scrollToMessage('m-9'), true);
    assert.equal(container.scrollTop, 777);
});

test('dispose detaches the listeners and disables the handle', async () => {
    const container = fakeContainer();
    const dotnet = scrollerDotNet();
    const handle = attachScroller(container, dotnet);

    assert.equal(container.count('scroll'), 1);

    handle.dispose();
    handle.dispose(); // idempotent

    assert.equal(container.count('scroll'), 0);
    container.dispatch('scroll');
    await Promise.resolve();
    assert.deepEqual(dotnet.calls, [], 'a disposed scroller must not report');
    assert.doesNotThrow(() => handle.afterUpdate('append'));
    assert.doesNotThrow(() => handle.scrollToBottom());
    assert.doesNotThrow(() => handle.scrollToMessage('m-1'));
    assert.doesNotThrow(() => handle.focusMessage('m-1'));
});

test('a rejected invokeMethodAsync does not throw out of the scroll handler', async () => {
    const container = fakeContainer();
    const failing = {
        invokeMethodAsync() { return Promise.reject(new Error('circuit gone')); },
    };
    const handle = attachScroller(container, failing);

    container.scrollTop = 10;
    assert.doesNotThrow(() => container.dispatch('scroll'));
    await Promise.resolve();
    handle.dispose();
});
