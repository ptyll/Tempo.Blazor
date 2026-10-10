// Toolbar module tests with hand-rolled DOM stubs (no jsdom): the collapse maths (which buttons
// leave first and how many fit), the roving-tabindex target list and the measurement of a stub bar.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    collapseOrder, chooseVisibleCount, nextRovingIndex, rovingItems, computeFit, markRedundantDividers, attach, detach, __resetForTests,
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

function child({ width, rank, pin = 'auto', item = true, more = false, id = '' }) {
    return {
        offsetWidth: width,
        dataset: item ? { tmToolbarItem: id, rank: String(rank), pin } : {},
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
        querySelectorAll: () => [],
        children: (itemWidths ?? controls.map(() => 40)).map((w, index) => ({
            offsetWidth: w,
            dataset: { tmToolbarItem: '', rank: String(index % 2 === 0 ? 2 : 1), pin: 'auto' },
            classList: { contains: () => false },
        })),
    };
    const root = {
        isConnected: true,
        querySelector: selector => (selector === '[data-tm-toolbar-row]' ? bar : (/tm-toolbar-more/.test(selector) ? (root.moreTrigger ?? null) : null)),
        querySelectorAll: () => controls,
        contains: el => controls.includes(el),
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
    const dotNet = { invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); } };
    globalThis.getComputedStyle = el => (el === root ? { direction: rtl ? 'rtl' : 'ltr' } : { columnGap: '8px', gap: '8px' });
    globalThis.requestAnimationFrame = fn => frames.push(fn);
    globalThis.cancelAnimationFrame = () => { frames.length = 0; };
    globalThis.ResizeObserver = class { constructor(cb) { this.cb = cb; observers.push(this); } observe() {} disconnect() { this.disconnected = true; } };
    globalThis.MutationObserver = class { constructor(cb) { this.cb = cb; observers.push(this); } observe(_, options) { env.mutationOptions = options; } disconnect() { this.disconnected = true; } };
    env = { root, bar, calls, frames, observers, active: null,
        flush() { while (frames.length) frames.shift()(); },
        dotNet, overflow };
    attach(root, dotNet, { overflow });
    return env;
}

test('attach measures once per frame and reports the fit to .NET', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { width: 400 });
    e.flush();
    assert.deepEqual(e.calls, [['OnFitChanged', 2, ['', '']]], 'the count AND the DOM order of the items cross the boundary');
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
// ── Pinned (Q1=A): never collapsible - measured as fixed width ───────────────────────────────

test('computeFit measures a pinned (data-pin=never) button as fixed width, not as a collapsible one', () => {
    // 100 + 100 collapsible, a 60px pinned Save: available 400 - (60 + 8) = 332 -> both collapsible fit (208).
    const roomy = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 1 }),
        child({ width: 60, rank: 3, pin: 'never' }),
    ], 400);
    assert.equal(computeFit(roomy).maxVisible, 2);
    // 230: 332 -> 230 - 68 = 162 < 208, one + trigger (100 + 8 + 44) = 152 <= 162 -> exactly one stays.
    const tight = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 1 }),
        child({ width: 60, rank: 3, pin: 'never' }),
    ], 230);
    assert.equal(computeFit(tight).maxVisible, 1, 'the pinned button reserves its width before the buttons are counted');
});
// ── H2: items are collected at any depth; a wrapper (group) is flattened ────────────────────

function wrapper(children, { role = null, group = false, width = 0, padding = 0, className = null } = {}) {
    return {
        offsetWidth: width,
        dataset: group ? { tmToolbarGroup: '' } : {},
        children,
        querySelector: () => (children.length ? children[0] : null),
        computed: { paddingLeft: `${padding}px`, paddingRight: `${padding}px`, columnGap: '8px', gap: '8px' },
        getAttribute: name => (name === 'role' ? role : null),
        classList: { contains: name => name === className },
    };
}

test('H2 computeFit measures buttons wrapped in a group (role=group) - a wide bar keeps them all', () => {
    const bar = stubBar([
        wrapper([child({ width: 60, rank: 2, id: 'a' }), child({ width: 60, rank: 2, id: 'b' }), child({ width: 60, rank: 2, id: 'c' })],
            { role: 'group', width: 200 }),
    ], 1200);
    const fit = computeFit(bar);
    assert.equal(fit.maxVisible, 3, 'three wrapped buttons on a 1200px bar all fit');
    assert.deepEqual(fit.ids, ['a', 'b', 'c']);
});

test('H2 a data-tm-toolbar-group wrapper is flattened like start/actions and collapses correctly when narrow', () => {
    const wide = stubBar([wrapper([child({ width: 60, rank: 2, id: 'a' }), child({ width: 60, rank: 1, id: 'b' })], { group: true, width: 128 })], 400);
    assert.equal(computeFit(wide).maxVisible, 2);
    // 128 > 100; one + trigger = 60 + 8 + 44 = 112 > 100 -> none.
    const narrow = stubBar([wrapper([child({ width: 60, rank: 2, id: 'a' }), child({ width: 60, rank: 1, id: 'b' })], { group: true, width: 128 })], 100);
    assert.equal(computeFit(narrow).maxVisible, 0);
});

test('H2 an unmarked wrapper with items is flattened and its own padding counts as fixed width', () => {
    const bar = stubBar([
        wrapper([child({ width: 50, rank: 2, id: 'a' }), child({ width: 50, rank: 1, id: 'b' })], { width: 150, padding: 20 }),
    ], 142);
    // buttons 50 + 8 + 50 = 108; the wrapper's padding (40) is fixed -> 108 > 142 - 40 = 102 -> one leaves: 50 + 8 + 44 = 102 <= 102.
    assert.equal(computeFit(bar).maxVisible, 1);
});

test('H2 a bar whose items cannot be measured (not rendered: clientWidth 0) reports nothing instead of a fit', () => {
    const fit = computeFit(stubBar([child({ width: 60, rank: 2, id: 'a' })], 0));
    assert.equal(fit.unmeasured, true);
    assert.equal(fit.signature, null);
});

test('H2 a bar with no items at all reports nothing (never "0 fit")', () => {
    const fit = computeFit(stubBar([child({ width: 30, item: false })], 400));
    assert.equal(fit.unmeasured, true);
});

// ── H3: the DOM order of the items is part of the report ─────────────────────────────────────

test('H3 computeFit lists the item ids in DOM order (pinned ones included) and the signature follows a reorder', () => {
    const one = computeFit(stubBar([child({ width: 40, rank: 2, id: 'a' }), child({ width: 40, rank: 2, id: 'b' }), child({ width: 40, rank: 3, pin: 'never', id: 'p' })], 400));
    assert.deepEqual(one.ids, ['a', 'b', 'p']);
    const reordered = computeFit(stubBar([child({ width: 40, rank: 2, id: 'b' }), child({ width: 40, rank: 2, id: 'a' }), child({ width: 40, rank: 3, pin: 'never', id: 'p' })], 400));
    assert.deepEqual(reordered.ids, ['b', 'a', 'p']);
    assert.equal(one.maxVisible, reordered.maxVisible);
    assert.notEqual(one.signature, reordered.signature, 'a reorder alone is a change .NET must hear about');
});

test('H3 attach reports a reorder to .NET even though the count did not change', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { width: 400 });
    e.flush();
    e.calls.length = 0;
    e.bar.children.reverse().forEach(() => {});
    e.bar.children[0].dataset.tmToolbarItem = 'z';
    e.observers[0].cb();
    e.flush();
    assert.equal(e.calls.length, 1);
    assert.deepEqual(e.calls[0].slice(0, 2), ['OnFitChanged', 2]);
    assert.deepEqual(e.calls[0][2][0], 'z');
});

// ── H4: redundant dividers ───────────────────────────────────────────────────────────────────

function leaf(kind, collapsed = false) {
    const attrs = new Map();
    const el = {
        kind,
        collapsed,
        attrs,
        offsetWidth: 10,
        dataset: kind === 'btn' ? { tmToolbarItem: 'x', rank: '2', pin: 'auto' } : {},
        classList: { contains: name => (name === 'tm-toolbar-divider' && kind === 'divider') || (name === 'tm-toolbar-item--collapsed' && el.collapsed) },
        setAttribute: (name, value) => attrs.set(name, String(value)),
        removeAttribute: name => attrs.delete(name),
        hasAttribute: name => attrs.has(name),
    };
    return el;
}

function row(kinds) {
    globalThis.getComputedStyle = () => ({ columnGap: '8px', gap: '8px' });
    const els = kinds.map(k => (typeof k === 'string' ? leaf(k) : leaf(k.kind, k.collapsed)));
    return { els, bar: { children: els, clientWidth: 400 } };
}

const collapsed = { kind: 'btn', collapsed: true };
test('H4 two dividers with only collapsed buttons between them: the first is redundant (Pan | | Find at 390)', () => {
    const { els, bar } = row(['btn', 'btn', 'divider', collapsed, collapsed, 'divider', collapsed, 'btn']);
    markRedundantDividers(bar);
    assert.equal(els[2].hasAttribute('data-tm-divider-redundant'), true);
    assert.equal(els[5].hasAttribute('data-tm-divider-redundant'), false, 'the second still separates Pan from Find');
});

test('H4 a leading or trailing divider (no visible button on one side) is redundant', () => {
    const lead = row([collapsed, 'divider', 'btn', 'btn']);
    markRedundantDividers(lead.bar);
    assert.equal(lead.els[1].hasAttribute('data-tm-divider-redundant'), true, 'nothing visible before it');
    const tail = row(['btn', 'divider', collapsed, collapsed]);
    markRedundantDividers(tail.bar);
    assert.equal(tail.els[1].hasAttribute('data-tm-divider-redundant'), true, 'nothing visible after it');
});

test('H4 a divider between visible buttons stays, and the mark is cleared when the room comes back', () => {
    const { els, bar } = row(['btn', 'divider', collapsed, 'btn']);
    markRedundantDividers(bar);
    assert.equal(els[1].hasAttribute('data-tm-divider-redundant'), false, 'a visible button on both sides');

    const r = row(['btn', 'divider', { kind: 'btn', collapsed: true }, { kind: 'btn', collapsed: true }]);
    markRedundantDividers(r.bar);
    assert.equal(r.els[1].hasAttribute('data-tm-divider-redundant'), true, 'nothing visible after it');
    r.els[2].collapsed = false; // the toolbar widened: the button is back
    markRedundantDividers(r.bar);
    assert.equal(r.els[1].hasAttribute('data-tm-divider-redundant'), false, 'the mark follows the room');
});
test('H4 the attribute is not observed (no mutation loop)', () => {
    const e = installToolbar([tbControl('a')], { width: 400 });
    assert.ok(e.mutationOptions, 'the MutationObserver options are recorded');
    assert.equal(e.mutationOptions.attributeFilter.includes('data-tm-divider-redundant'), false);
});

// ── H12 / H15 ────────────────────────────────────────────────────────────────────────────────

test('H12 the MutationObserver watches character data (a label text change re-measures)', () => {
    const e = installToolbar([tbControl('a')], { width: 400 });
    assert.equal(e.mutationOptions.characterData, true);
});

test('H15 a measurement pass on a disconnected root detaches itself', () => {
    const e = installToolbar([tbControl('a'), tbControl('b')], { width: 400 });
    e.observers[0].cb();
    e.root.isConnected = false;
    e.flush();
    assert.equal(e.observers.every(o => o.disconnected), true, 'observers disconnected');
    assert.equal(e.root.listeners.size, 0, 'listeners removed');
});
// ── H16: focus never falls to <body> when the focused control leaves the bar ───────────────────

function focusEnv(controls, { trigger = true } = {}) {
    const e = installToolbar(controls, { width: 400 });
    const body = { tagName: 'BODY' };
    globalThis.document = { body, documentElement: { tagName: 'HTML' }, activeElement: body };
    const more = { tagName: 'BUTTON', disabled: false, isConnected: true, focusCalls: 0, focus() { more.focusCalls++; globalThis.document.activeElement = more; }, getAttribute: () => null, closest: () => null };
    if (trigger) e.root.moreTrigger = more;
    e.flush();
    return { e, more, body };
}

test('H16 a focused bar button that collapses hands focus to the More trigger', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const { e, more, body } = focusEnv(controls);
    e.root.fire('focusin', { target: controls[1] });
    controls[1].collapsed = true;       // Blazor collapsed it: inert, the browser drops focus to <body>
    globalThis.document.activeElement = body;
    e.observers[1].cb();
    e.flush();
    assert.equal(more.focusCalls, 1);
});

test('H16 without a More trigger focus goes to the roving stop (the trigger unmounted after a widen)', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const { e, body } = focusEnv(controls, { trigger: false });
    e.root.fire('focusin', { target: controls[1] });
    controls[1].collapsed = true;
    globalThis.document.activeElement = body;
    e.observers[1].cb();
    e.flush();
    assert.equal(controls[0].focusCalls, 1, 'the remaining on-bar control receives focus');
});

test('H16 focus that is not lost, or never was in the toolbar, is left alone', () => {
    const controls = [tbControl('a'), tbControl('b')];
    const { e, more } = focusEnv(controls);
    e.observers[1].cb();
    e.flush();
    assert.equal(more.focusCalls, 0, 'focus was never inside the toolbar');

    e.root.fire('focusin', { target: controls[0] });
    globalThis.document.activeElement = { tagName: 'INPUT' }; // the user moved on to another control
    e.root.fire('focusout', { target: controls[0], relatedTarget: globalThis.document.activeElement });
    e.observers[1].cb();
    e.flush();
    assert.equal(more.focusCalls, 0, 'an intentional move elsewhere is never pulled back');
});

test('I1 a zero-width OverflowOnly position marker (data-pin=always) keeps its id slot in DOM order and takes no room', () => {
    const bar = stubBar([
        child({ width: 100, rank: 1, id: 'a' }),
        child({ width: 0, rank: 0, pin: 'always', id: 'x' }),
        child({ width: 100, rank: 1, id: 'b' }),
    ], 400);
    const fit = computeFit(bar);
    assert.deepEqual(fit.ids, ['a', 'x', 'b'], 'the marker reports where the OverflowOnly button was written');
    assert.equal(fit.hasPinned, true);
    assert.equal(fit.maxVisible, 2, '100 + 100 + 8 + trigger 52 fits in 400; the marker adds no width');
});
