// Menu keyboard module tests with hand-rolled DOM stubs (no jsdom): the pure index/typeahead
// rules plus attach() against a stub menu, the way tm-editor-shell.test.mjs does it.
import test from 'node:test';
import assert from 'node:assert/strict';
import { nextIndex, typeaheadMatch, attach, detach, enabledItems, isAttached, focusFirst, __resetForTests } from '../tm-menu-nav.js';

test.beforeEach(() => __resetForTests());

// ── nextIndex: ArrowDown/ArrowUp wrap, Home/End jump ────────────────────────────────────────

test('nextIndex moves down and wraps to the first item', () => {
    assert.equal(nextIndex(4, 0, 'ArrowDown'), 1);
    assert.equal(nextIndex(4, 3, 'ArrowDown'), 0);
});

test('nextIndex moves up and wraps to the last item', () => {
    assert.equal(nextIndex(4, 2, 'ArrowUp'), 1);
    assert.equal(nextIndex(4, 0, 'ArrowUp'), 3);
});

test('nextIndex Home and End jump to the ends', () => {
    assert.equal(nextIndex(4, 2, 'Home'), 0);
    assert.equal(nextIndex(4, 1, 'End'), 3);
});

test('nextIndex with no current item starts at the first (down/home) or last (up/end)', () => {
    assert.equal(nextIndex(3, -1, 'ArrowDown'), 0);
    assert.equal(nextIndex(3, -1, 'ArrowUp'), 2);
});

test('nextIndex ignores other keys and empty menus', () => {
    assert.equal(nextIndex(3, 1, 'a'), -1);
    assert.equal(nextIndex(0, -1, 'ArrowDown'), -1);
});

// ── typeaheadMatch ──────────────────────────────────────────────────────────────────────────

test('typeaheadMatch finds the next label that starts with the buffer, case-insensitively', () => {
    const labels = ['Archive', 'Delete', 'Duplicate', 'Export'];
    assert.equal(typeaheadMatch(labels, 0, 'd'), 1);
    assert.equal(typeaheadMatch(labels, 1, 'd'), 2, 'a repeated single character cycles to the next match');
    assert.equal(typeaheadMatch(labels, 2, 'd'), 1, 'and wraps');
    assert.equal(typeaheadMatch(labels, 0, 'DU'), 2, 'a longer buffer matches the prefix');
    assert.equal(typeaheadMatch(labels, 0, 'zz'), -1);
});

test('typeaheadMatch starts after the current item and can return the current one when it is the only match', () => {
    assert.equal(typeaheadMatch(['One', 'Two'], 0, 'o'), 0);
});

test('typeaheadMatch ignores diacritics and locale case (cs/fr labels)', () => {
    assert.equal(typeaheadMatch(['Zrušit', 'Šablony', 'Éditer'], 0, 's'), 1);
    assert.equal(typeaheadMatch(['Zrušit', 'Šablony', 'Éditer'], 0, 'e'), 2);
});

// ── attach(): the keydown delegate on the menu root ─────────────────────────────────────────

function item(label, { disabled = false, ariaDisabled = false, hidden = false } = {}) {
    const attrs = new Map();
    if (ariaDisabled) attrs.set('aria-disabled', 'true');
    return {
        textContent: label,
        disabled,
        hidden,
        tabIndex: 0,
        focusCalls: 0,
        getAttribute: name => attrs.get(name) ?? null,
        setAttribute: (name, value) => attrs.set(name, String(value)),
        hasAttribute: name => name === 'hidden' ? hidden : attrs.has(name),
        focus() { this.focusCalls++; menu.activeElement = this; },
        closest: () => null,
    };
}

let menu;
function stubMenu(items) {
    const listeners = new Map();
    menu = {
        activeElement: null,
        isConnected: true,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        querySelectorAll() { return items; },
        contains: el => items.includes(el),
        press(key, extra = {}) {
            const event = {
                key, target: menu.activeElement, defaultPrevented: false,
                ctrlKey: false, metaKey: false, altKey: false, ...extra,
                preventDefault() { this.defaultPrevented = true; },
            };
            listeners.get('keydown')?.(event);
            return event;
        },
        listeners,
    };
    return menu;
}

test('attach is idempotent per element', () => {
    const m = stubMenu([item('A')]);
    attach(m);
    attach(m);
    assert.equal(isAttached(m), true);
    assert.equal(m.listeners.size, 2, 'one keydown + one focusin listener, never doubled');
});

test('ArrowDown/ArrowUp rove focus and prevent the page scroll', () => {
    const items = [item('A'), item('B'), item('C')];
    const m = stubMenu(items);
    attach(m);
    m.activeElement = items[0];

    const down = m.press('ArrowDown');
    assert.equal(m.activeElement, items[1]);
    assert.equal(down.defaultPrevented, true);

    m.press('ArrowUp');
    assert.equal(m.activeElement, items[0]);
    m.press('ArrowUp');
    assert.equal(m.activeElement, items[2], 'wraps');
});

test('arrow keys skip disabled and hidden items', () => {
    const items = [item('A'), item('B', { disabled: true }), item('C', { ariaDisabled: true }), item('D', { hidden: true }), item('E')];
    const m = stubMenu(items);
    attach(m);
    m.activeElement = items[0];

    m.press('ArrowDown');
    assert.equal(m.activeElement, items[4], 'B (disabled), C (aria-disabled) and D (hidden) are skipped');
    m.press('ArrowDown');
    assert.equal(m.activeElement, items[0]);
});

test('Home and End focus the first and the last enabled item', () => {
    const items = [item('A', { disabled: true }), item('B'), item('C'), item('D', { disabled: true })];
    const m = stubMenu(items);
    attach(m);
    m.activeElement = items[2];

    m.press('Home');
    assert.equal(m.activeElement, items[1]);
    m.press('End');
    assert.equal(m.activeElement, items[2]);
});

test('typeahead focuses the next item whose label starts with the typed characters', () => {
    const items = [item('Archive'), item('Delete'), item('Duplicate')];
    const m = stubMenu(items);
    attach(m);
    m.activeElement = items[0];

    m.press('d');
    assert.equal(m.activeElement, items[1]);
    m.press('d');
    assert.equal(m.activeElement, items[2], 'the same character again cycles');
});

test('typeahead is ignored with a modifier key and for keystrokes inside an input', () => {
    const items = [item('Archive'), item('Delete')];
    const m = stubMenu(items);
    attach(m);
    m.activeElement = items[0];

    m.press('d', { ctrlKey: true });
    assert.equal(m.activeElement, items[0], 'Ctrl+D is not typeahead');

    const input = { tagName: 'INPUT', isContentEditable: false };
    const event = m.press('d', { target: input });
    assert.equal(event.defaultPrevented, false);
    assert.equal(m.activeElement, items[0], 'typing in a filter input must not move focus');
});

test('enabledItems returns only focusable menu items in DOM order', () => {
    const items = [item('A'), item('B', { disabled: true }), item('C')];
    const m = stubMenu(items);
    assert.deepEqual(enabledItems(m).map(i => i.textContent), ['A', 'C']);
});

test('the roving tabindex follows focus: the focused item is tabbable, the others are not', () => {
    const items = [item('A'), item('B')];
    const m = stubMenu(items);
    attach(m);
    m.listeners.get('focusin')({ target: items[1] });
    assert.equal(items[1].getAttribute('tabindex'), '0');
    assert.equal(items[0].getAttribute('tabindex'), '-1');
});

// ── focusFirst: initial focus of a dropdown opened from the keyboard ───────────────────────────

test('focusFirst focuses the first ENABLED item of the open menu inside the host', () => {
    const items = [item('A', { disabled: true }), item('B'), item('C')];
    const menuEl = { querySelectorAll: () => items };
    const host = { querySelector: selector => (selector === '[role="menu"]' ? menuEl : null) };

    assert.equal(focusFirst(host), true);
    assert.equal(items[1].focusCalls, 1);
    assert.equal(items[0].focusCalls, 0);
});

test('focusFirst does nothing when the menu is not open or has no enabled item', () => {
    assert.equal(focusFirst({ querySelector: () => null }), false);
    assert.equal(focusFirst(null), false);
    const menuEl = { querySelectorAll: () => [item('A', { disabled: true })] };
    assert.equal(focusFirst({ querySelector: () => menuEl }), false);
});

// ── H16c: Tab out of an open popover menu closes it ──────────────────────────────────────────

function dismissEnv() {
    const calls = [];
    const dotNet = { invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(true); } };
    const inside = item('A');
    const m = stubMenu([inside]);
    m.contains = el => el === inside || el === m;
    const anchorListeners = new Map();
    const anchor = {
        contains: el => el === anchor,
        addEventListener(type, fn) { anchorListeners.set(type, fn); },
        removeEventListener(type) { anchorListeners.delete(type); },
    };
    return { calls, dotNet, m, inside, anchor, anchorListeners, outside: { tagName: 'INPUT' } };
}

function tab(target) { return { key: 'Tab', target, defaultPrevented: false, ctrlKey: false, metaKey: false, altKey: false, preventDefault() {} }; }

test('H16c Tab moving focus out of both the menu and its trigger dismisses the popover (aria-expanded follows via IsOpenChanged)', () => {
    const e = dismissEnv();
    attach(e.m, e.dotNet, e.anchor);
    e.m.listeners.get('keydown')(tab(e.inside));
    e.m.listeners.get('focusout')({ target: e.inside, relatedTarget: e.outside });
    assert.deepEqual(e.calls, [['NotifyDismissedAsync', 'focus-out']]);
});

test('H16c focus leaving WITHOUT Tab (a confirm dialog opened by a KeepMenuOpen item) never dismisses the menu', () => {
    const e = dismissEnv();
    attach(e.m, e.dotNet, e.anchor);
    e.m.listeners.get('focusout')({ target: e.inside, relatedTarget: e.outside });
    assert.deepEqual(e.calls, [], 'only Tab dismisses - programmatic focus moves keep the menu');
});

test('H16c Tab that stays inside the menu, goes to the trigger, or lands nowhere does not dismiss', () => {
    const e = dismissEnv();
    attach(e.m, e.dotNet, e.anchor);
    const keydown = e.m.listeners.get('keydown');
    const focusout = e.m.listeners.get('focusout');
    keydown(tab(e.inside)); focusout({ target: e.inside, relatedTarget: e.inside });
    keydown(tab(e.inside)); focusout({ target: e.inside, relatedTarget: e.anchor });
    keydown(tab(e.inside)); focusout({ target: e.inside, relatedTarget: null });
    assert.deepEqual(e.calls, []);
});

test('H16c Tab from the trigger to something outside both dismisses; Tab from the trigger into the menu does not', () => {
    const e = dismissEnv();
    attach(e.m, e.dotNet, e.anchor);
    const keydown = e.anchorListeners.get('keydown');
    const focusout = e.anchorListeners.get('focusout');
    keydown(tab(e.anchor)); focusout({ target: e.anchor, relatedTarget: e.inside });
    assert.deepEqual(e.calls, []);
    keydown(tab(e.anchor)); focusout({ target: e.anchor, relatedTarget: e.outside });
    assert.deepEqual(e.calls, [['NotifyDismissedAsync', 'focus-out']]);
});
test('H16c without a dotNetRef (sheet / trap) nothing is wired, and detach removes the trigger listener', () => {
    const e = dismissEnv();
    attach(e.m);
    assert.equal(e.m.listeners.has('focusout'), false);
    const e2 = dismissEnv();
    attach(e2.m, e2.dotNet, e2.anchor);
    detach(e2.m);
    assert.equal(e2.anchorListeners.size, 0);
});

// ── Review round 2 (I2): re-opening on the same trigger must not stack listeners ─────────────

function multiAnchor() {
    const listeners = [];
    const anchor = {
        contains: el => el === anchor,
        addEventListener(type, fn, capture) { listeners.push({ type, fn, capture: Boolean(capture) }); },
        removeEventListener(type, fn, capture) {
            const index = listeners.findIndex(l => l.type === type && l.fn === fn && l.capture === Boolean(capture));
            if (index >= 0) listeners.splice(index, 1);
        },
        fire(type, event) { for (const l of [...listeners]) if (l.type === type) l.fn(event); },
        count: type => listeners.filter(l => l.type === type).length,
    };
    return anchor;
}

function openMenu(anchor, dotNet) {
    const inside = item('A');
    const m = stubMenu([inside]);
    m.contains = el => el === inside || el === m;
    attach(m, dotNet, anchor);
    return { m, inside };
}

test('I2 re-opening on the same trigger leaves exactly one keydown and one focusout listener on it', () => {
    const calls = [];
    const dotNet = { invokeMethodAsync: (...a) => { calls.push(a); return Promise.resolve(true); } };
    const anchor = multiAnchor();
    openMenu(anchor, dotNet);
    openMenu(anchor, dotNet); // the first menu unmounted without a detach call
    openMenu(anchor, dotNet);
    assert.equal(anchor.count('keydown'), 1);
    assert.equal(anchor.count('focusout'), 1);
});

test('I2 after a re-open, Tab from the trigger INTO the new menu does not dismiss it', () => {
    const calls = [];
    const dotNet = { invokeMethodAsync: (...a) => { calls.push(a); return Promise.resolve(true); } };
    const anchor = multiAnchor();
    openMenu(anchor, dotNet);
    const second = openMenu(anchor, dotNet);
    anchor.fire('keydown', tab(anchor));
    anchor.fire('focusout', { target: anchor, relatedTarget: second.inside });
    assert.deepEqual(calls, []);
});

test('I2 a Tab from the trigger to the outside still dismisses the CURRENT menu after re-opens', () => {
    const calls = [];
    const dotNet = { invokeMethodAsync: (...a) => { calls.push(a); return Promise.resolve(true); } };
    const anchor = multiAnchor();
    openMenu(anchor, dotNet);
    openMenu(anchor, dotNet);
    anchor.fire('keydown', tab(anchor));
    anchor.fire('focusout', { target: anchor, relatedTarget: { tagName: 'INPUT' } });
    assert.deepEqual(calls, [['NotifyDismissedAsync', 'focus-out']]);
});

test('I2 a registration whose menu is gone dismisses nothing and removes its own trigger listeners', () => {
    const calls = [];
    const dotNet = { invokeMethodAsync: (...a) => { calls.push(a); return Promise.resolve(true); } };
    const anchor = multiAnchor();
    const { m } = openMenu(anchor, dotNet);
    m.isConnected = false; // the popover closed and its element was removed
    anchor.fire('keydown', tab(anchor));
    anchor.fire('focusout', { target: anchor, relatedTarget: { tagName: 'INPUT' } });
    assert.deepEqual(calls, [], 'a closed menu must not be dismissed again');
    assert.equal(anchor.count('keydown'), 0);
    assert.equal(anchor.count('focusout'), 0);
});