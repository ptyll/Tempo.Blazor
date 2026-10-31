// ai-composer.js tests — the composer interop module.
//
// Three behaviours live in this module because a net8 Blazor app cannot express them: the IME guard on Enter
// (`KeyboardEventArgs.IsComposing` exists only on net9+, and Safari reports a commit-Enter as keyCode 229), the
// autosize of the textarea, and the Web Speech wrapper. The module ALSO decides whether Enter is a submit or a
// combobox activation: while the suggestion listbox is expanded (`aria-expanded="true"`) the C# handler picks
// the highlighted option, so the module must suppress the native newline and stay out of the way.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attach,
    detach,
    resize,
    nextTextareaHeight,
    shouldSubmit,
    computeCaretPlacement,
    placeSuggestions,
    speechSupported,
    startSpeech,
    stopSpeech,
    attachViewportTracker,
    detachViewportTracker,
    isFocusWithin,
    focusFirst,
} from '../ai-composer.js';

/** A scripted stand-in for the composer textarea: listener registry plus the few DOM bits the module reads. */
class FakeTextarea {
    constructor({ value = '', selectionStart = 0, scrollHeight = 60, expanded = false } = {}) {
        this.value = value;
        this.selectionStart = selectionStart;
        this.scrollHeight = scrollHeight;
        this.style = {
            values: new Map(),
            setProperty(name, value) { this.values.set(name, value); },
            removeProperty(name) { this.values.delete(name); },
        };
        this.listeners = new Map();
        this.attributes = new Map();
        this.height = '';
        this.overflowY = '';
        this.style.height = '';
        if (expanded) {
            this.attributes.set('aria-expanded', 'true');
        }
    }

    set heightInternal(value) {
        this.style.height = value;
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

    getAttribute(name) {
        return this.attributes.has(name) ? this.attributes.get(name) : null;
    }

    listenerTypes() {
        return [...this.listeners.keys()].sort();
    }

    emit(type, event) {
        const payload = event ?? {};
        payload.type = type;
        payload.target = this;
        if (typeof payload.preventDefault !== 'function') {
            payload.preventDefault = function preventDefault() { this.defaultPrevented = true; };
        }

        for (const entry of [...(this.listeners.get(type) ?? [])]) {
            entry.handler(payload);
        }

        return payload;
    }
}

function fakeDotNet() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(method, ...values) {
            calls.push({ method, values });
            return Promise.resolve();
        }
    };
}

function enterEvent(overrides = {}) {
    let prevented = false;
    return {
        key: 'Enter',
        preventDefault() { prevented = true; },
        get defaultPrevented() { return prevented; },
        ...overrides,
    };
}

// ── Enter / IME guard ──────────────────────────────────────────────────────────────────────────

test('Enter submits through the .NET entry point and suppresses the native newline', () => {
    const textarea = new FakeTextarea();
    const dotNet = fakeDotNet();
    attach(textarea, dotNet, { maxRows: 4 });

    const event = enterEvent();
    textarea.emit('keydown', event);

    assert.equal(event.defaultPrevented, true, 'the textarea must not insert a newline');
    assert.deepEqual(dotNet.calls, [{ method: 'OnComposerSubmit', values: [] }]);
});

test('Shift+Enter, Alt/Ctrl/Meta+Enter stay native', () => {
    const textarea = new FakeTextarea();
    const dotNet = fakeDotNet();
    attach(textarea, dotNet, { maxRows: 4 });

    for (const modifiers of [{ shiftKey: true }, { altKey: true }, { ctrlKey: true }, { metaKey: true }]) {
        const event = enterEvent(modifiers);
        textarea.emit('keydown', event);
        assert.equal(event.defaultPrevented, false, `${JSON.stringify(modifiers)} keeps the browser behaviour`);
    }

    assert.deepEqual(dotNet.calls, []);
});

test('an IME composition never submits — neither isComposing nor the legacy keyCode 229', () => {
    const textarea = new FakeTextarea();
    const dotNet = fakeDotNet();
    attach(textarea, dotNet, { maxRows: 4 });

    for (const composition of [{ isComposing: true }, { keyCode: 229 }]) {
        const event = enterEvent(composition);
        textarea.emit('keydown', event);
        assert.equal(event.defaultPrevented, false, 'the composition keeps the default behaviour');
    }

    assert.deepEqual(dotNet.calls, [], 'the commit-Enter of an IME is not a send');
});

test('an expanded combobox owns Enter: the module stays quiet and suppresses the newline', () => {
    const textarea = new FakeTextarea({ expanded: true });
    const dotNet = fakeDotNet();
    attach(textarea, dotNet, { maxRows: 4 });

    const event = enterEvent();
    textarea.emit('keydown', event);

    assert.equal(event.defaultPrevented, true, 'the C# handler picks the option — a newline would corrupt the draft');
    assert.deepEqual(dotNet.calls, [], 'the C# keydown handler is the single owner of the combobox Enter');
});

test('the arrow keys are suppressed only while the listbox is expanded', () => {
    const collapsed = new FakeTextarea();
    attach(collapsed, fakeDotNet(), { maxRows: 4 });
    const native = { key: 'ArrowDown', preventDefault() { this.defaultPrevented = true; } };
    collapsed.emit('keydown', native);
    assert.equal(native.defaultPrevented, undefined, 'without a listbox the caret still moves');

    const expanded = new FakeTextarea({ expanded: true });
    attach(expanded, fakeDotNet(), { maxRows: 4 });
    for (const key of ['ArrowDown', 'ArrowUp']) {
        const event = { key, preventDefault() { this.defaultPrevented = true; } };
        expanded.emit('keydown', event);
        assert.equal(event.defaultPrevented, true, `${key} moves the active option, not the caret`);
    }
});

test('shouldSubmit mirrors the keydown rules for a caller that already classified the event', () => {
    assert.equal(shouldSubmit({ key: 'Enter' }, false), true);
    assert.equal(shouldSubmit({ key: 'Enter', shiftKey: true }, false), false);
    assert.equal(shouldSubmit({ key: 'Enter', isComposing: true }, false), false);
    assert.equal(shouldSubmit({ key: 'Enter', keyCode: 229 }, false), false);
    assert.equal(shouldSubmit({ key: 'Enter' }, true), false, 'an open listbox owns Enter');
    assert.equal(shouldSubmit({ key: 'a' }, false), false);
});

// ── Autosize ──────────────────────────────────────────────────────────────────────────────────

test('nextTextareaHeight grows with the content and stops at the row cap', () => {
    const metrics = { lineHeight: 20, paddingY: 10, borderY: 2, boxSizing: 'border-box' };

    assert.equal(nextTextareaHeight({ ...metrics, scrollHeight: 40, maxRows: 3 }), 40);
    assert.equal(nextTextareaHeight({ ...metrics, scrollHeight: 100, maxRows: 3 }), 72,
        'the cap is maxRows * line-height + padding + border');
    assert.equal(nextTextareaHeight({ ...metrics, scrollHeight: 100, maxRows: 0 }), 100,
        'maxRows 0 means no cap');
});

test('resize writes the clamped height and lets the textarea scroll once it is capped', () => {
    const original = globalThis.getComputedStyle;
    globalThis.getComputedStyle = () => ({
        lineHeight: '20px',
        fontSize: '14px',
        paddingTop: '4px',
        paddingBottom: '6px',
        borderTopWidth: '1px',
        borderBottomWidth: '1px',
        boxSizing: 'border-box',
    });
    try {
        const textarea = new FakeTextarea({ scrollHeight: 200 });
        attach(textarea, fakeDotNet(), { maxRows: 2 });
        textarea.style.height = '';

        resize(textarea, 2);

        assert.equal(textarea.style.height, '52px', '2 rows * 20px + 10px padding + 2px border');
        assert.equal(textarea.style.overflowY, 'auto', 'a capped textarea scrolls instead of growing');

        textarea.scrollHeight = 30;
        resize(textarea, 2);
        assert.equal(textarea.style.height, '30px');
        assert.equal(textarea.style.overflowY, 'hidden');
    } finally {
        globalThis.getComputedStyle = original;
    }
});

// ── Suggestion placement ─────────────────────────────────────────────────────────────────────

test('computeCaretPlacement anchors the list above the caret and clamps it into the viewport', () => {
    const anchor = { left: 120, top: 300, bottom: 320 };
    const root = { left: 100, bottom: 420 };
    const viewport = { top: 0, height: 800 };

    const placement = computeCaretPlacement(anchor, root, { maxHeight: 240, viewport });
    assert.equal(placement.inlineStart, 20, 'the list starts at the caret, relative to the composer root');
    assert.equal(placement.blockEnd, 120, 'the list bottom sits at the composer bottom minus the caret top');
    assert.equal(placement.maxBlockSize, 240, 'there is room for the full list above the caret');

    const high = computeCaretPlacement({ left: 0, top: 60, bottom: 80 }, { left: 0, bottom: 420 }, { maxHeight: 240, viewport });
    assert.equal(high.maxBlockSize, 52, 'a caret near the top gets a shorter list instead of an off-screen one');
});

test('placeSuggestions writes the caret knobs onto the list and survives a missing DOM', () => {
    const textarea = new FakeTextarea();
    textarea.getBoundingClientRect = () => ({ left: 110, top: 300, bottom: 320 });
    const list = { style: { values: new Map(), setProperty(name, value) { this.values.set(name, value); } } };
    const root = { getBoundingClientRect: () => ({ left: 100, bottom: 420 }) };
    textarea.closest = (selector) => (selector === '.tm-ai-composer' ? root : null);

    placeSuggestions(textarea, list);

    assert.equal(list.style.values.get('--tm-ai-composer-caret-inline-start'), '10px');
    assert.equal(list.style.values.get('--tm-ai-composer-caret-block-end'), '100px',
        'without a caret measurement the list anchors above the textarea itself');

    assert.doesNotThrow(() => placeSuggestions(null, list), 'a detached composer never throws');
    assert.doesNotThrow(() => placeSuggestions(textarea, null));
});

// ── Speech ────────────────────────────────────────────────────────────────────────────────────

class FakeRecognition {
    constructor() {
        FakeRecognition.instances.push(this);
        this.handlers = new Map();
        this.startCalls = 0;
        this.stopCalls = 0;
    }

    addEventListener(type, handler) {
        this.handlers.set(type, handler);
    }

    start() {
        this.startCalls++;
    }

    stop() {
        this.stopCalls++;
        this.handlers.get('end')?.();
    }

    abort() {
        this.handlers.get('end')?.();
    }
}

FakeRecognition.instances = [];

function withRecognition(callback) {
    const originalSpeech = globalThis.SpeechRecognition;
    const originalWebkit = globalThis.webkitSpeechRecognition;
    FakeRecognition.instances = [];
    globalThis.SpeechRecognition = FakeRecognition;
    try {
        callback();
    } finally {
        globalThis.SpeechRecognition = originalSpeech;
        if (originalWebkit === undefined) {
            delete globalThis.webkitSpeechRecognition;
        } else {
            globalThis.webkitSpeechRecognition = originalWebkit;
        }
    }
}

test('speechSupported reports whether the browser exposes the Web Speech API', () => {
    const originalSpeech = globalThis.SpeechRecognition;
    const originalWebkit = globalThis.webkitSpeechRecognition;
    try {
        delete globalThis.SpeechRecognition;
        delete globalThis.webkitSpeechRecognition;
        assert.equal(speechSupported(), false, 'no constructor means the control is not offered');

        globalThis.webkitSpeechRecognition = FakeRecognition;
        assert.equal(speechSupported(), true, 'the prefixed constructor counts');
    } finally {
        globalThis.SpeechRecognition = originalSpeech;
        if (originalWebkit === undefined) {
            delete globalThis.webkitSpeechRecognition;
        } else {
            globalThis.webkitSpeechRecognition = originalWebkit;
        }
    }
});

test('startSpeech wires the results, the error and the end back to .NET', () => {
    withRecognition(() => {
        const textarea = new FakeTextarea();
        const dotNet = fakeDotNet();

        const started = startSpeech(textarea, dotNet, { language: 'cs-CZ' });
        assert.equal(started, true);

        const recognition = FakeRecognition.instances[0];
        assert.equal(recognition.lang, 'cs-CZ', 'the host language reaches the recognizer');
        assert.equal(recognition.startCalls, 1);

        recognition.handlers.get('result')({ results: [[{ transcript: 'Ahoj svete' }]] });
        recognition.handlers.get('error')({ error: 'not-allowed' });
        recognition.handlers.get('end')();

        assert.deepEqual(dotNet.calls, [
            { method: 'OnSpeechResult', values: ['Ahoj svete', true] },
            { method: 'OnSpeechError', values: ['not-allowed'] },
            { method: 'OnSpeechEnded', values: [] },
        ]);
    });
});

test('startSpeech is a no-op without the API and stopSpeech releases the session', () => {
    const originalSpeech = globalThis.SpeechRecognition;
    try {
        delete globalThis.SpeechRecognition;
        assert.equal(startSpeech(new FakeTextarea(), fakeDotNet()), false,
            'a browser without speech recognition must not throw');
    } finally {
        globalThis.SpeechRecognition = originalSpeech;
    }

    withRecognition(() => {
        const textarea = new FakeTextarea();
        startSpeech(textarea, fakeDotNet());
        const recognition = FakeRecognition.instances[0];

        assert.equal(stopSpeech(textarea), true);
        assert.equal(recognition.stopCalls, 1);
        assert.equal(stopSpeech(textarea), false, 'stopping a finished session is a no-op');
    });
});

test('detach releases the listeners and an in-flight speech session', () => {
    withRecognition(() => {
        const textarea = new FakeTextarea();
        const dotNet = fakeDotNet();
        attach(textarea, dotNet, { maxRows: 4 });
        startSpeech(textarea, dotNet);
        const recognition = FakeRecognition.instances[0];
        assert.ok(textarea.listenerTypes().includes('keydown'));

        detach(textarea);

        assert.deepEqual(textarea.listenerTypes(), [], 'a detached composer keeps no listener alive');
        assert.equal(recognition.stopCalls, 1, 'the recognizer must not outlive the component');
        assert.equal(stopSpeech(textarea), false, 'the session was already released');
    });
});

// ── Virtual keyboard tracker ─────────────────────────────────────────────────────────────────

class FakeViewportRoot {
    constructor() {
        this.style = {
            values: new Map(),
            setProperty(name, value) { this.values.set(name, value); },
            removeProperty(name) { this.values.delete(name); },
        };
        this.attributes = new Map();
        this.disposed = false;
    }

    setAttribute(name) { this.attributes.set(name, ''); }
    removeAttribute(name) { this.attributes.delete(name); }
    hasAttribute(name) { return this.attributes.has(name); }
}

function withVisualViewport(state, callback) {
    const original = globalThis.visualViewport;
    const originalHeight = globalThis.innerHeight;
    const listeners = new Map();
    globalThis.innerHeight = 800;
    globalThis.visualViewport = {
        ...state,
        addEventListener(type, handler) { listeners.set(type, handler); },
        removeEventListener(type) { listeners.delete(type); },
        emit(type, patch = {}) {
            Object.assign(globalThis.visualViewport, patch);
            listeners.get(type)?.();
        },
        listenerTypes() { return [...listeners.keys()].sort(); },
    };
    try {
        callback(globalThis.visualViewport);
    } finally {
        globalThis.visualViewport = original;
        globalThis.innerHeight = originalHeight;
    }
}

test('attachViewportTracker writes the keyboard gap and removes it when the keyboard closes', () => {
    withVisualViewport({ height: 800, width: 400, offsetTop: 0, scale: 1 }, (viewport) => {
        const root = new FakeViewportRoot();
        attachViewportTracker(root, { minInset: 100 });

        assert.equal(root.style.values.has('--tm-ai-composer-keyboard-inset'), false,
            'a closed keyboard writes nothing');

        viewport.emit('resize', { height: 500 });
        assert.equal(root.style.values.get('--tm-ai-composer-keyboard-inset'), '300px');
        assert.equal(root.hasAttribute('data-tm-keyboard-open'), true);

        viewport.emit('resize', { height: 800 });
        assert.equal(root.style.values.has('--tm-ai-composer-keyboard-inset'), false);
        assert.equal(root.hasAttribute('data-tm-keyboard-open'), false);

        detachViewportTracker(root);
        assert.deepEqual(viewport.listenerTypes(), [], 'the tracker releases its viewport listeners');
    });
});

test('attachViewportTracker ignores browser chrome, zoom and a degenerate viewport', () => {
    withVisualViewport({ height: 800, width: 400, offsetTop: 0, scale: 1 }, (viewport) => {
        const root = new FakeViewportRoot();
        attachViewportTracker(root, { minInset: 100 });

        viewport.emit('resize', { height: 740 });
        assert.equal(root.style.values.has('--tm-ai-composer-keyboard-inset'), false,
            'a 60px shrink is browser chrome, not a keyboard');

        viewport.emit('resize', { height: 400, scale: 2 });
        assert.equal(root.style.values.has('--tm-ai-composer-keyboard-inset'), false,
            'a zoomed page is the reader scaling, not a keyboard');

        viewport.emit('resize', { height: 1, scale: 1, width: 1 });
        assert.equal(root.style.values.has('--tm-ai-composer-keyboard-inset'), false,
            'a degenerate 1x1 viewport measures nothing');

        detachViewportTracker(root);
    });
});

test('detachViewportTracker is idempotent and clears the runtime knob', () => {
    withVisualViewport({ height: 800, width: 400, offsetTop: 0, scale: 1 }, (viewport) => {
        const root = new FakeViewportRoot();
        attachViewportTracker(root, { minInset: 100 });
        viewport.emit('resize', { height: 400 });
        assert.equal(root.style.values.get('--tm-ai-composer-keyboard-inset'), '400px');

        detachViewportTracker(root);
        assert.equal(root.style.values.has('--tm-ai-composer-keyboard-inset'), false);
        assert.doesNotThrow(() => detachViewportTracker(root), 'detaching twice is safe');
    });
});

// ── Focus order across the send/stop swap ────────────────────────────────────────────────────

/** A stand-in for the send/stop slot: one queryable control plus a document with an active element. */
function fakeSlot({ control = null, active = null } = {}) {
    const slot = {
        contains(candidate) {
            return candidate === control || candidate === slot;
        },
        querySelector(selector) {
            return selector === 'button:not([disabled])' ? control : null;
        },
    };
    slot.ownerDocument = { activeElement: active };
    return slot;
}

test('isFocusWithin reports whether the active element is inside the slot', () => {
    const button = { focus() {} };
    assert.equal(isFocusWithin(fakeSlot({ control: button, active: button })), true);
    assert.equal(isFocusWithin(fakeSlot({ control: button, active: { tag: 'body' } })), false);
    assert.equal(isFocusWithin(fakeSlot({ control: button, active: null })), false,
        'no active element is not "inside"');
    assert.equal(isFocusWithin(null), false, 'a detached slot never throws');
});

test('focusFirst moves the focus to the slot control and reports when it cannot', () => {
    let focused = 0;
    const button = { focus() { focused++; } };
    assert.equal(focusFirst(fakeSlot({ control: button })), true);
    assert.equal(focused, 1, 'the replacement control in the slot takes the focus');

    assert.equal(focusFirst(fakeSlot({ control: null })), false,
        'a disabled replacement (the query matches nothing) leaves the caller to pick the fallback');
    assert.equal(focusFirst(null), false, 'a detached slot never throws');
    assert.doesNotThrow(() => focusFirst({ querySelector() { throw new Error('gone'); } }),
        'a selector that throws on a stale DOM is a quiet no-op');
});
