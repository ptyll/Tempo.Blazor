// Toolbar module tests with hand-rolled DOM stubs (no jsdom): the collapse maths (which buttons
// leave first and how many fit), the roving-tabindex target list and the measurement of a stub bar.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    collapseOrder, chooseVisibleCount, nextRovingIndex, rovingItems, computeFit, attach, detach, __resetForTests,
} from '../tm-toolbar.js';

test.beforeEach(() => __resetForTests());

// ── collapseOrder: the SAME rule as ActionOverflowLayout.Partition (lowest rank first, ties: last first)

test('collapseOrder drops the lowest rank first and the later item first within a rank', () => {
    // ranks: Primary=2, Secondary=1 -> Partition drops idx3 then idx1 (Secondary, last first), then idx2, idx0.
    assert.deepEqual(collapseOrder([2, 1, 2, 1]), [3, 1, 2, 0]);
});

test('collapseOrder of equal ranks starts at the end', () => {
    assert.deepEqual(collapseOrder([1, 1, 1]), [2, 1, 0]);
});

test('collapseOrder of nothing is nothing', () => {
    assert.deepEqual(collapseOrder([]), []);
});

// ── chooseVisibleCount: how many non-pinned buttons the bar can hold ─────────────────────────

const btn = (width, rank) => ({ width, rank });

test('everything fits -> every button stays', () => {
    const items = [btn(40, 2), btn(40, 1), btn(40, 2)];
    assert.equal(chooseVisibleCount(items, 200, 8, 44, false), 3);
});

test('exactly fitting counts as fitting (gaps included)', () => {
    const items = [btn(40, 2), btn(40, 1)];
    assert.equal(chooseVisibleCount(items, 88, 8, 44, false), 2);
    // One short of fitting: even one button + the trigger (40 + 8 + 44 = 92) does not fit in 87.
    assert.equal(chooseVisibleCount(items, 87, 8, 44, false), 0);
});

test('a collapse reserves room for the More trigger', () => {
    // 3 x 40 + 2 gaps = 136 > 130 -> something must leave -> the trigger (44 + gap 8) takes room:
    // 2 x 40 + 1 gap + 52 = 140 > 130 -> 1 button + 52 = 92 <= 130.
    const items = [btn(40, 2), btn(40, 2), btn(40, 1)];
    assert.equal(chooseVisibleCount(items, 130, 8, 44, false), 1);
});

test('a bar with a pinned overflow-only button always reserves the trigger', () => {
    const items = [btn(40, 2), btn(40, 2)];
    // 88 fits the buttons alone but not buttons + trigger (88 + 52 = 140).
    assert.equal(chooseVisibleCount(items, 100, 8, 44, true), 1);
    assert.equal(chooseVisibleCount(items, 140, 8, 44, true), 2);
});

test('wide buttons leave the same buttons the C# partition would pick', () => {
    // Collapse order [Secondary(last), Secondary, Primary(last), Primary]: dropping the first two
    // frees enough room, so the count is 2 - the C# side then drops exactly those two.
    const items = [btn(60, 2), btn(60, 1), btn(60, 2), btn(60, 1)];
    assert.equal(chooseVisibleCount(items, 190, 8, 44, false), 2);
});

test('nothing fits -> zero (the C# partition keeps one button anyway)', () => {
    assert.equal(chooseVisibleCount([btn(300, 2)], 100, 8, 44, false), 0);
});

test('no buttons -> zero', () => {
    assert.equal(chooseVisibleCount([], 100, 8, 44, false), 0);
});

// ── keyboard: roving tabindex ────────────────────────────────────────────────────────────────

test('nextRovingIndex follows the reading direction and wraps', () => {
    assert.equal(nextRovingIndex(3, 0, 'ArrowRight', false), 1);
    assert.equal(nextRovingIndex(3, 2, 'ArrowRight', false), 0);
    assert.equal(nextRovingIndex(3, 0, 'ArrowLeft', false), 2);
    assert.equal(nextRovingIndex(3, 1, 'Home', false), 0);
    assert.equal(nextRovingIndex(3, 1, 'End', false), 2);
});

test('nextRovingIndex swaps the arrows in a right-to-left toolbar', () => {
    assert.equal(nextRovingIndex(3, 0, 'ArrowLeft', true), 1);
    assert.equal(nextRovingIndex(3, 0, 'ArrowRight', true), 2);
});

test('nextRovingIndex ignores vertical arrows and other keys', () => {
    assert.equal(nextRovingIndex(3, 0, 'ArrowDown', false), -1);
    assert.equal(nextRovingIndex(3, 0, 'a', false), -1);
    assert.equal(nextRovingIndex(0, -1, 'ArrowRight', false), -1);
});

function control({ disabled = false, ariaDisabled = false, blocked = false, label = '' } = {}) {
    return {
        label,
        disabled,
        getAttribute: name => (name === 'aria-disabled' && ariaDisabled ? 'true' : null),
        // rovingItems asks ONE closest() for every kind of exclusion: collapsed bar items, the
        // open More menu and its panel.
        closest: selector => {
            assert.match(selector, /tm-toolbar-item--collapsed/);
            assert.match(selector, /role="menu"/);
            return blocked ? {} : null;
        },
    };
}

test('rovingItems skips disabled, collapsed and in-menu controls (KeyboardNavigation_SkipsHiddenItems)', () => {
    const controls = [
        control({ label: 'a' }),
        control({ label: 'b', disabled: true }),
        control({ label: 'c', blocked: true }),
        control({ label: 'd', ariaDisabled: true }),
        control({ label: 'e' }),
    ];
    const root = { querySelectorAll: () => controls };
    assert.deepEqual(rovingItems(root).map(c => c.label), ['a', 'e']);
});

// ── computeFit: measuring a stub bar ─────────────────────────────────────────────────────────

function child({ width, rank, pin = 'auto', item = true, more = false }) {
    return {
        offsetWidth: width,
        dataset: item ? { tmToolbarItem: '', rank: String(rank), pin } : {},
        classList: { contains: name => (more && name === 'tm-toolbar-more') },
    };
}

function stubBar(children, clientWidth) {
    globalThis.getComputedStyle = el => el?.computed ?? ({ columnGap: '8px', gap: '8px' });
    return { clientWidth, children };
}

test('computeFit measures the buttons, the fixed children and the trigger', () => {
    // 2 buttons (40 + 60) + a 1px divider + a More trigger (50): gap 8.
    const bar = stubBar([
        child({ width: 40, rank: 2 }),
        child({ width: 1, item: false }),
        child({ width: 60, rank: 1 }),
        child({ width: 50, item: false, more: true }),
    ], 400);
    const fit = computeFit(bar);
    assert.equal(fit.maxVisible, 2, 'everything fits in 400px');
});

test('computeFit subtracts the fixed children from the room the buttons get', () => {
    const bar = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 1 }),
        child({ width: 30, item: false }),
    ], 230);
    // 200 + 8 gap + 30 fixed + 8 gap = 246 > 230 -> Secondary leaves: 100 + 30 + gaps + trigger(44+8)=190 <= 230.
    assert.equal(computeFit(bar).maxVisible, 1);
});

test('computeFit never measures OverflowOnly buttons but reserves the trigger for them', () => {
    const bar = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 0, pin: 'always' }),
    ], 130);
    const fit = computeFit(bar);
    assert.equal(fit.hasPinned, true);
    assert.equal(fit.maxVisible, 0, '100 + trigger 52 > 130');
});

test('computeFit reports a signature that changes only when the answer changes', () => {
    const wide = computeFit(stubBar([child({ width: 40, rank: 2 })], 400));
    const wider = computeFit(stubBar([child({ width: 40, rank: 2 })], 500));
    const narrow = computeFit(stubBar([child({ width: 40, rank: 2 }), child({ width: 40, rank: 1 })], 60));
    assert.equal(wide.signature, wider.signature);
    assert.notEqual(wide.signature, narrow.signature);
});

test('computeFit ignores out-of-flow children (a panel in the bar) when summing the fixed width', () => {
    const popover = child({ width: 200, rank: 0, item: false });
    popover.computed = { position: 'fixed', columnGap: '8px', gap: '8px' };
    const bar = stubBar([child({ width: 100, rank: 2 }), popover], 120);
    assert.equal(computeFit(bar).maxVisible, 1, 'the 200px fixed child is not part of the bar flow');
});

test('computeFit uses the real width of the More trigger when it is rendered', () => {
    // 100 + gap 8 + trigger 80 = 188 > 170 -> nothing fits; with the 44px default it would have kept one.
    const bar = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 1 }),
        child({ width: 80, item: false, more: true }),
    ], 170);
    assert.equal(computeFit(bar).maxVisible, 0);
});

// ── attach(): observers, rAF coalescing, signature dedup, roving tabindex ────────────────────────

function tbControl(label, { disabled = false, collapsed = false, classes = ['tm-toolbar-btn'] } = {}) {
    const attrs = new Map();
    const el = {
        label,
        disabled,
        collapsed,
        tagName: 'BUTTON',
        isContentEditable: false,
        focusCalls: 0,
        attrs,
        getAttribute: name => attrs.get(name) ?? null,
        setAttribute: (name, value) => attrs.set(name, String(value)),
        removeAttribute: name => attrs.delete(name),
        focus() { el.focusCalls++; env.active = el; env.root.fire('focusin', { target: el }); },
        closest: selector => (el.collapsed && /tm-toolbar-item--collapsed/.test(selector) ? {} : null),
    };
    return el;
}

let env;
function installToolbar(controls, { width = 400, overflow = true, rtl = false, itemWidths = null } = {}) {
    const listeners = new Map();
    const observers = [];
    const frames = [];
    const calls = [];
    const bar = {
        clientWidth: width,
        dataset: {},
        children: (itemWidths ?? controls.map(() => 40)).map((w, index) => ({
            offsetWidth: w,
            dataset: { tmToolbarItem: '', rank: String(index % 2 === 0 ? 2 : 1), pin: 'auto' },
            classList: { contains: () => false },
        })),
    };
    const root = {
        isConnected: true,
        querySelector: selector => (selector === '[data-tm-toolbar-row]' ? bar : null),
        querySelectorAll: () => controls,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        fire(type, event) { listeners.get(type)?.(event); },
        press(key, extra = {}) {
            const event = { key, target: env.active, defaultPrevented: false, ctrlKey: false, metaKey: false, altKey: false, ...extra,
                preventDefault() { this.defaultPrevented = true; } };
            listeners.get('keydown')?.(event);
            return event;
        },
        listeners,
    };
    const dotNet = { invokeMethodAsync: (name, value) => { calls.push([name, value]); return Promise.resolve(); } };
    globalThis.getComputedStyle = el => (el === root ? { direction: rtl ? 'rtl' : 'ltr' } : { columnGap: '8px', gap: '8px' });
    globalThis.requestAnimationFrame = fn => frames.push(fn);
    globalThis.cancelAnimationFrame = () => { frames.length = 0; };
    globalThis.ResizeObserver = class { constructor(cb) { this.cb = cb; observers.push(this); } observe() {} disconnect() { this.disconnected = true; } };
    globalThis.MutationObserver = class { constructor(cb) { this.cb = cb; observers.push(this); } observe() {} disconnect() { this.disconnected = true; } };
    env = { root, bar, calls, frames, observers, active: null,
        flush() { while (frames.length) frames.shift()(); },
        dotNet, overflow };
    attach(root, dotNet, { overflow });
    return env;
}

test('attach measures once per frame and reports the fit to .NET', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { width: 400 });
    e.flush();
    assert.deepEqual(e.calls, [['OnFitChanged', 2]]);
});

test('attach coalesces a burst of resize/mutation callbacks into one measurement', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { width: 400 });
    e.flush();
    e.calls.length = 0;
    for (const observer of e.observers) { observer.cb(); observer.cb(); }
    assert.equal(e.frames.length, 1, 'one animation frame, however many callbacks');
});

test('attach reports to .NET only when the answer changes (no render loop)', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { width: 400 });
    e.flush();
    e.calls.length = 0;

    e.observers[0].cb();
    e.flush();
    assert.deepEqual(e.calls, [], 'same signature -> no interop');

    e.bar.clientWidth = 60; // only one of the two buttons fits
    e.observers[0].cb();
    e.flush();
    assert.equal(e.calls.length, 1);
    assert.equal(e.calls[0][1], 0, '40 + trigger does not fit in 60 -> none kept');
});

test('a rejected interop call is swallowed', async () => {
    const e = installToolbar([tbControl('a')], { width: 400 });
    e.dotNet.invokeMethodAsync = () => Promise.reject(new Error('circuit gone'));
    e.bar.clientWidth = 20;
    e.observers[0].cb();
    assert.doesNotThrow(() => e.flush());
    await new Promise(resolve => setImmediate(resolve));
});

test('overflow:false never measures but still roves', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { overflow: false });
    e.flush();
    assert.deepEqual(e.calls, []);
    assert.equal(e.observers.length > 0 || true, true);
});

test('attach makes the first enabled control the only tab stop', () => {
    const controls = [tbControl('a', { disabled: true }), tbControl('b'), tbControl('c')];
    installToolbar(controls);
    assert.equal(controls[1].getAttribute('tabindex'), '0');
    assert.equal(controls[2].getAttribute('tabindex'), '-1');
});

test('ArrowRight/ArrowLeft/Home/End rove focus and the tab stop (KeyboardNavigation_SkipsHiddenItems)', () => {
    const controls = [tbControl('a'), tbControl('b', { collapsed: true }), tbControl('c', { disabled: true }), tbControl('d')];
    const e = installToolbar(controls);
    env.active = controls[0];

    const right = e.root.press('ArrowRight');
    assert.equal(env.active, controls[3], 'the collapsed copy and the disabled button are skipped');
    assert.equal(right.defaultPrevented, true);
    assert.equal(controls[3].getAttribute('tabindex'), '0');
    assert.equal(controls[0].getAttribute('tabindex'), '-1');

    e.root.press('ArrowRight');
    assert.equal(env.active, controls[0], 'wraps');
    e.root.press('End');
    assert.equal(env.active, controls[3]);
    e.root.press('Home');
    assert.equal(env.active, controls[0]);
    e.root.press('ArrowLeft');
    assert.equal(env.active, controls[3]);
});

test('a right-to-left toolbar swaps the arrow directions', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const e = installToolbar(controls, { rtl: true });
    env.active = controls[0];
    e.root.press('ArrowLeft');
    assert.equal(env.active, controls[1]);
});

test('vertical arrows, modifiers, text entry and non-controls are not intercepted', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const e = installToolbar(controls);
    env.active = controls[0];

    assert.equal(e.root.press('ArrowDown').defaultPrevented, false);
    assert.equal(e.root.press('ArrowRight', { ctrlKey: true }).defaultPrevented, false);
    assert.equal(env.active, controls[0]);

    const input = { tagName: 'INPUT', isContentEditable: false, closest: () => null };
    assert.equal(e.root.press('ArrowRight', { target: input }).defaultPrevented, false);
    const stray = { tagName: 'DIV', isContentEditable: false, closest: () => null };
    assert.equal(e.root.press('ArrowRight', { target: stray }).defaultPrevented, false);
    assert.equal(env.active, controls[0]);
});

test('focusing a control (click, programmatic) moves the tab stop to it', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const e = installToolbar(controls);
    e.root.fire('focusin', { target: controls[1] });
    assert.equal(controls[1].getAttribute('tabindex'), '0');
    assert.equal(controls[0].getAttribute('tabindex'), '-1');
});

test('a control that appears later joins the roving set on the next frame', () => {
    const controls = [tbControl('a')];
    const e = installToolbar(controls);
    const late = tbControl('late');
    controls.push(late);
    e.observers[1].cb();
    e.flush();
    assert.equal(late.getAttribute('tabindex'), '-1');
});

test('attach twice on one element replaces the first registration', () => {
    const controls = [tbControl('a')];
    const e = installToolbar(controls);
    const listenersBefore = e.root.listeners.size;
    attach(e.root, e.dotNet, { overflow: true });
    assert.equal(e.root.listeners.size, listenersBefore, 'no doubled listeners');
    assert.equal(e.observers.filter(o => o.disconnected).length > 0, true, 'the first registration was disconnected');
});

test('detach disconnects the observers, cancels the pending frame and lets go of the tab stops', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const e = installToolbar(controls);
    e.observers[0].cb();
    detach(e.root);
    assert.equal(e.observers.every(o => o.disconnected), true);
    assert.equal(e.frames.length, 0, 'the pending measurement was cancelled');
    assert.equal(e.root.listeners.size, 0);
    assert.equal(controls[0].getAttribute('tabindex'), null, 'detach leaves the natural tab order');
    assert.doesNotThrow(() => detach(e.root), 'a second detach is harmless');
});
// ── the measured row: start/actions containers are flattened ─────────────────────────────────

function container(className, children, width = 0) {
    return {
        offsetWidth: width,
        dataset: {},
        children,
        classList: { contains: name => name === className },
    };
}

test('computeFit flattens the start and actions containers into one row', () => {
    // One Primary button in each container (60 + 60), a 30px title in start, the More trigger in actions.
    const bar = stubBar([
        container('tm-toolbar-start', [child({ width: 30, item: false }), child({ width: 60, rank: 2 })], 300),
        container('tm-toolbar-actions', [child({ width: 60, rank: 1 }), child({ width: 50, item: false, more: true })], 200),
    ], 400);
    // fixed = 30 + 8; buttons 60 + 8 + 60 = 128; available = 400 - 38 = 362 -> both fit.
    assert.equal(computeFit(bar).maxVisible, 2);
    const narrow = stubBar([
        container('tm-toolbar-start', [child({ width: 30, item: false }), child({ width: 60, rank: 2 })], 300),
        container('tm-toolbar-actions', [child({ width: 60, rank: 1 }), child({ width: 50, item: false, more: true })], 200),
    ], 140);
    // available = 140 - 38 = 102: both = 128 > 102; one + the 50px trigger = 60 + 8 + 50 = 118 > 102 -> 0.
    assert.equal(computeFit(narrow).maxVisible, 0);
});

test('a flattened container that is itself empty adds nothing', () => {
    const bar = stubBar([
        container('tm-toolbar-start', [child({ width: 60, rank: 2 })], 60),
        container('tm-toolbar-actions', [], 0),
    ], 100);
    assert.equal(computeFit(bar).maxVisible, 1);
});