// chat.js (TmChat composer) tests with hand-rolled DOM stubs (no jsdom): Enter submit +
// preventDefault, Shift+Enter new line, the IME guard (isComposing / keyCode 229), modifier
// chords, Ctrl/⌘+Enter submit when enterToSend is false, per-call submit routing, auto-resize
// up to maxRows (input events AND a ResizeObserver on width changes), dispose, per-composer
// isolation and the focus/focusIfNeeded helpers.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachComposer, focus, focusIfNeeded } from '../chat.js';

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
