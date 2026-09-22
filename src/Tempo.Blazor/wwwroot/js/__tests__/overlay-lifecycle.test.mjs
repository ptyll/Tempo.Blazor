// Lifecycle tests for overlay.js — DOM-adjacent paths (open/close/dismiss) exercised with hand
// stubs instead of jsdom (per the phase spec: no new test dependency). In Node, HTMLElement is
// undefined so the module's supportsPopover constant is false — showPanel/hidePanel then never
// touch .matches()/showPopover(), which the stubs deliberately do not implement.
import test from 'node:test';
import assert from 'node:assert/strict';
import { open, close, dismiss } from '../overlay.js';

function stubWindow() {
    return { innerWidth: 1280, innerHeight: 800, addEventListener() {}, removeEventListener() {} };
}
function stubDocument() {
    return { addEventListener() {}, removeEventListener() {} };
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

function installDomStubs({ captureKeydown = null } = {}) {
    globalThis.window = stubWindow();
    if (captureKeydown) {
        globalThis.window.addEventListener = (type, fn) => {
            if (type === 'keydown') {
                captureKeydown(fn);
            }
        };
    }
    globalThis.document = stubDocument();
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
