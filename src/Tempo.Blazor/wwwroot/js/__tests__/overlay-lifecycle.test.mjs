// Lifecycle tests for overlay.js — DOM-adjacent paths (open/close/dismiss) exercised with hand
// stubs instead of jsdom (per the phase spec: no new test dependency). In Node, HTMLElement is
// undefined so the module's supportsPopover constant is false — showPanel/hidePanel then never
// touch .matches()/showPopover(), which the stubs deliberately do not implement.
import test from 'node:test';
import assert from 'node:assert/strict';
import { open, close, dismiss, update } from '../overlay.js';

function stubWindow() {
    return { innerWidth: 1280, innerHeight: 800, addEventListener() {}, removeEventListener() {} };
}
function stubDocument({ activeElement = null } = {}) {
    const body = { isConnected: true };
    const documentElement = { isConnected: true };
    return {
        addEventListener() {}, removeEventListener() {},
        activeElement, body, documentElement,
    };
}
function stubPanel() {
    return {
        isConnected: true, offsetWidth: 100, offsetHeight: 40, style: {},
        classList: { add() {}, remove() {} }, setAttribute() {}, removeAttribute() {},
    };
}
function stubAnchor() {
    return {
        isConnected: true,
        getBoundingClientRect: () => ({ top: 0, left: 0, right: 100, bottom: 20, width: 100, height: 20 }),
    };
}

function installDomStubs({ captureKeydown = null, activeElement = null } = {}) {
    globalThis.window = stubWindow();
    if (captureKeydown) {
        globalThis.window.addEventListener = (type, fn) => {
            if (type === 'keydown') {
                captureKeydown(fn);
            }
        };
    }
    globalThis.document = stubDocument({ activeElement });
    // Node has no CSSOM; open() reads the stylesheet max-height once per entry (N318), so every
    // test needs a computed-style stub. Default = "no stylesheet cap" (parseFloat('none') is NaN,
    // which the cap math treats as room-only). Tests that need a real cap override this AFTER
    // calling installDomStubs.
    globalThis.getComputedStyle = () => ({ maxHeight: 'none' });
    // maybeRestoreAnchorFocus checks `target instanceof Element` — Node has no DOM globals, so
    // give it a shape the plain stub objects below never satisfy.
    globalThis.Element = class Element {};
    const observed = [];
    const unobserved = [];
    globalThis.ResizeObserver = class {
        observe(el) { observed.push(el); }
        unobserve(el) { unobserved.push(el); }
    };
    return { observed, unobserved };
}

test('reopening an orphaned tracked entry re-observes the new panel and moves it to the end of the stacking order', () => {
    const { observed, unobserved } = installDomStubs();

    const key = 'n164-test-key';
    const panelA = stubPanel();
    open(key, panelA, stubAnchor(), {}, {});
    assert.deepEqual(observed, [panelA]);

    const panelB = stubPanel();
    open(key, panelB, stubAnchor(), {}, {});

    assert.deepEqual(unobserved, [panelA], 'the stale panel must stop being observed');
    assert.deepEqual(observed, [panelA, panelB], 'the new panel must start being observed');

    close(key);
});

test('a reopened entry counts as topmost for Escape — Map reinsertion, not just mutation', async () => {
    let onKeyDown;
    installDomStubs({ captureKeydown: fn => { onKeyDown = fn; } });

    const dismissed = [];
    const mkRef = tag => ({
        invokeMethodAsync: async () => { dismissed.push(tag); return true; },
    });

    const keyA = 'n164-order-a';
    const keyB = 'n164-order-b';
    open(keyA, stubPanel(), stubAnchor(), mkRef('a'), {});
    open(keyB, stubPanel(), stubAnchor(), mkRef('b'), {});
    // Reopen A — as if Blazor had deleted and re-rendered its panel. Without the tracked
    // reinsert the map keeps A in its ORIGINAL slot and Escape peels B instead.
    open(keyA, stubPanel(), stubAnchor(), mkRef('a'), {});

    onKeyDown({ key: 'Escape', preventDefault() {}, stopImmediatePropagation() {} });
    await new Promise(r => setTimeout(r, 0));

    assert.deepEqual(dismissed, ['a'],
        'the most recently (re)opened panel is topmost and must take the Escape');

    close(keyA);
    close(keyB);
});

test('a vetoed dismissal (dotNetRef resolves false) resets entry.dismissed so the next Escape can dismiss it', async () => {
    installDomStubs();
    const entry = {
        key: 'n167-test-key',
        panel: stubPanel(),
        dotNetRef: { invokeMethodAsync: async () => false },
        options: { closeOnEscape: true },
        dismissed: false,
    };

    assert.equal(dismiss(entry, 'escape'), true);
    assert.equal(entry.dismissed, true, 'dismissed is set synchronously while the callback runs');
    await new Promise(r => setTimeout(r, 0));
    assert.equal(entry.dismissed, false, 'a veto must re-arm the entry for the next Escape');
});

test('escape dismiss does NOT steal focus from an element outside the panel (passive anchor)', async () => {
    // TmEntityPicker/TmQueryInput: the anchor is a tabindex="-1" WRAPPER and the focused
    // element is the input INSIDE it — Escape while typing means "close the popup", not
    // "leave the field" (20B carry-forward). Unconditional anchorEl.focus() yanked focus
    // onto the passive div.
    const focusCalls = [];
    const input = { isConnected: true };
    installDomStubs({ activeElement: input });
    const anchor = {
        isConnected: true,
        focus: () => focusCalls.push('anchor'),
    };
    const entry = {
        key: 'cf-passive-anchor',
        panel: { ...stubPanel(), contains: () => false },
        anchor,
        dotNetRef: { invokeMethodAsync: async () => true },
        options: { closeOnEscape: true },
        dismissed: false,
    };

    assert.equal(dismiss(entry, 'escape'), true);
    assert.deepEqual(focusCalls, [],
        'focus was inside the anchor (typing), nowhere near the doomed panel — it must stay put');
    await new Promise(r => setTimeout(r, 0));
});

test('escape dismiss restores focus to the anchor when focus sat INSIDE the panel', async () => {
    // The panel node is about to be destroyed by the .NET re-render — focus inside it would
    // drop to <body>. That is the case the anchor restore exists for.
    const focusCalls = [];
    const insidePanel = { isConnected: true };
    installDomStubs({ activeElement: insidePanel });
    const anchor = {
        isConnected: true,
        focus: () => focusCalls.push('anchor'),
    };
    const entry = {
        key: 'cf-focus-inside-panel',
        panel: { ...stubPanel(), contains: el => el === insidePanel },
        anchor,
        dotNetRef: { invokeMethodAsync: async () => true },
        options: { closeOnEscape: true },
        dismissed: false,
    };

    assert.equal(dismiss(entry, 'escape'), true);
    assert.deepEqual(focusCalls, ['anchor'],
        'focus inside the doomed panel must land on the anchor, not die on <body>');
    await new Promise(r => setTimeout(r, 0));
});

test('escape dismiss restores focus to the anchor when focus is on <body>', async () => {
    const focusCalls = [];
    installDomStubs();
    globalThis.document.activeElement = globalThis.document.body;
    const anchor = {
        isConnected: true,
        focus: () => focusCalls.push('anchor'),
    };
    const entry = {
        key: 'cf-focus-body',
        panel: { ...stubPanel(), contains: () => false },
        anchor,
        dotNetRef: { invokeMethodAsync: async () => true },
        options: { closeOnEscape: true },
        dismissed: false,
    };

    assert.equal(dismiss(entry, 'escape'), true);
    assert.deepEqual(focusCalls, ['anchor'],
        'focus on <body> means it went nowhere — the anchor takes it (N168 contract)');
    await new Promise(r => setTimeout(r, 0));
});

test('an accepted dismissal (dotNetRef resolves true) keeps entry.dismissed', async () => {
    installDomStubs();
    const entry = {
        key: 'n167-accept-key',
        panel: stubPanel(),
        dotNetRef: { invokeMethodAsync: async () => true },
        options: { closeOnEscape: true },
        dismissed: false,
    };

    assert.equal(dismiss(entry, 'outside'), true);
    await new Promise(r => setTimeout(r, 0));
    assert.equal(entry.dismissed, true,
        'an accepted dismissal stays dismissed — the entry is on its way to close()');
});

test('constrainHeight cap grows back when the room grows (N318)', () => {
    installDomStubs();
    const panel = stubPanel(); // offsetHeight 40 — fits on the bottom side in both passes, no flip
    // Stylesheet cap 320px, resolved like the real CSSOM: an inline max-height overrides the
    // stylesheet value. That read-back of our own previous inline cap is the N318 defect — the
    // entry must remember the STYLESHEET cap instead.
    globalThis.getComputedStyle = el =>
        el === panel ? { maxHeight: panel.style.maxHeight || '320px' } : { maxHeight: 'none' };

    // Anchor near the viewport bottom: room below = 800 − 688 − 4 − 8 = 100px.
    const lowAnchor = {
        isConnected: true,
        getBoundingClientRect: () => ({ top: 648, left: 100, right: 220, bottom: 688, width: 120, height: 40 }),
    };
    open('n318-grow', panel, lowAnchor, {}, { constrainHeight: true });
    assert.equal(panel.style.maxHeight, '100px', 'first pass caps to the available room');

    // The anchor scrolled back up: room below is now 800 − 388 − 4 − 8 = 400px, so the panel may
    // grow to the stylesheet cap 320px. Reading getComputedStyle HERE returns 100px (our own
    // inline override) and the buggy code keeps the cap at 100px forever.
    const highAnchor = {
        isConnected: true,
        getBoundingClientRect: () => ({ top: 348, left: 100, right: 220, bottom: 388, width: 120, height: 40 }),
    };
    update('n318-grow', highAnchor, { constrainHeight: true });
    assert.equal(panel.style.maxHeight, '320px',
        'cap must regrow to the stylesheet ceiling once the room returns');

    close('n318-grow');
});

test('panel height is measured without the previous inline cap (N318)', () => {
    installDomStubs();
    const panel = stubPanel();
    // offsetHeight must be read AFTER place() clears the previous pass's inline maxHeight —
    // otherwise the flip/fit decision uses the capped height instead of the natural one.
    const maxHeightAtMeasure = [];
    Object.defineProperty(panel, 'offsetHeight', {
        get() { maxHeightAtMeasure.push(panel.style.maxHeight); return 40; },
    });
    globalThis.getComputedStyle = el =>
        el === panel ? { maxHeight: panel.style.maxHeight || '320px' } : { maxHeight: 'none' };

    const anchor = {
        isConnected: true,
        getBoundingClientRect: () => ({ top: 648, left: 100, right: 220, bottom: 688, width: 120, height: 40 }),
    };
    open('n318-measure', panel, anchor, {}, { constrainHeight: true });
    assert.equal(panel.style.maxHeight, '100px', 'first pass leaves a real inline cap behind');

    update('n318-measure', anchor, { constrainHeight: true }); // second place() pass
    assert.equal(maxHeightAtMeasure.at(-1), '',
        'the second pass must clear the inline cap BEFORE measuring offsetHeight');

    close('n318-measure');
});

test('hiding a panel that holds focus dismisses it and restores anchor focus (N329)', async () => {
    const focusCalls = [];
    const insidePanel = { isConnected: true };
    installDomStubs({ activeElement: insidePanel });
    const panel = { ...stubPanel(), contains: el => el === insidePanel };
    const dismissed = [];
    const anchor = {
        isConnected: true,
        focus: () => focusCalls.push('anchor'),
        // Fully above the viewport — anchorIntersectsViewport goes false on this pass.
        getBoundingClientRect: () => ({ top: -120, left: 0, right: 100, bottom: -100, width: 100, height: 20 }),
    };

    open('n329-focused', panel, anchor, {
        invokeMethodAsync: async (method, reason) => { dismissed.push(reason); return true; },
    }, {});

    assert.equal(panel.style.visibility, 'hidden', 'the doomed panel is still parked invisible');
    await new Promise(r => setTimeout(r, 0));
    assert.deepEqual(dismissed, ['anchor-hidden'],
        'a hidden panel holding focus is dismissed through the same path as outside-dismiss');
    assert.deepEqual(focusCalls, ['anchor'],
        'focus returns to the anchor instead of dropping to <body>');

    close('n329-focused');
});

test('hiding a panel without focus only hides it (N329)', () => {
    installDomStubs({ activeElement: { isConnected: true } });
    const panel = { ...stubPanel(), contains: () => false };
    const dismissed = [];
    const anchor = {
        isConnected: true,
        getBoundingClientRect: () => ({ top: -120, left: 0, right: 100, bottom: -100, width: 100, height: 20 }),
    };

    open('n329-unfocused', panel, anchor, {
        invokeMethodAsync: async (method, reason) => { dismissed.push(reason); return true; },
    }, {});

    assert.equal(panel.style.visibility, 'hidden');
    assert.deepEqual(dismissed, [],
        'no focus inside → the park-and-reappear behaviour is unchanged');
    close('n329-unfocused');
});

test('matchAnchorWidth respects panel min-width when clamping (N324)', () => {
    installDomStubs();

    // TmMultiColumnComboBox contract: a 240px trigger under a panel with min-width 320px.
    // offsetWidth must therefore answer 320 once style.width is written to 240px.
    const panel = stubPanel();
    Object.defineProperty(panel, 'offsetWidth', {
        configurable: true,
        get() {
            const w = parseFloat(panel.style.width);
            return Number.isNaN(w) ? 320 : Math.max(320, w);
        },
    });
    const anchor = {
        isConnected: true,
        getBoundingClientRect: () => ({ top: 0, left: 1040, right: 1280, bottom: 20, width: 240, height: 20 }),
    };

    open('n324-min-width', panel, anchor, {}, { matchAnchorWidth: true, align: 'start' });

    // The inline width still honours the trigger…
    assert.equal(panel.style.width, '240px');
    // …but the shift clamp must have used the RENDERED 320px, so the right edge stays inside
    // the viewport margin. Under the bug the 240px math placed x at 1280-8-240=1032 and the
    // real panel overhung the edge by 72px.
    const x = parseFloat(panel.style.left);
    assert.ok(x + 320 <= 1280 - 8 + 0.5,
        `min-width-clamped panel must stay inside the viewport (left=${x})`);

    close('n324-min-width');
});
