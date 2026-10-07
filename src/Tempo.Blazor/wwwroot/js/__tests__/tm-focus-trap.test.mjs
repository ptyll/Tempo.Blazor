// Focus-trap tests with hand-rolled DOM stubs, the same approach as overlay-lifecycle.test.mjs:
// no jsdom. The stubs only implement what the module touches.
import test from 'node:test';
import assert from 'node:assert/strict';
import { activate, deactivate, isInnermost, isTopmost, __resetForTests } from '../tm-focus-trap.js';

test.beforeEach(() => __resetForTests());

function element(parent = null) {
    const listeners = new Map();
    const el = {
        parent,
        // The module walks parentElement, the real DOM property; the stub stores the link as parent.
        get parentElement() { return parent; },
        _children: [],
        attributes: new Set(),
        offsetParent: {},
        focusCalls: 0,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        contains(other) {
            for (let node = other; node; node = node.parent) if (node === el) return true;
            return false;
        },
        querySelectorAll() { return []; },
        hasAttribute(name) { return el.attributes.has(name); },
        toggleAttribute(name, on) { if (on) el.attributes.add(name); else el.attributes.delete(name); },
        get children() { return el._children; },
        set children(value) { el._children = value; },
        dataset: {},
        focus() { el.focusCalls++; },
        dispatch(type, event) { listeners.get(type)?.(event); },
    };
    if (parent) parent.children.push(el);
    return el;
}

function classList(initial = []) {
    const classes = new Set(initial);
    return {
        contains: name => classes.has(name),
        add: name => classes.add(name),
        remove: name => classes.delete(name),
    };
}

function installDom(body, documentElement = null) {
    body.children = body.children ?? [];
    // A list per type: nested traps each register their own listener, and the real document keeps
    // all of them.
    const documentListeners = new Map();
    globalThis.document = {
        activeElement: body,
        body,
        addEventListener(type, fn) {
            const list = documentListeners.get(type) ?? [];
            list.push(fn);
            documentListeners.set(type, list);
        },
        removeEventListener(type, fn) {
            const list = documentListeners.get(type) ?? [];
            documentListeners.set(type, list.filter(registered => registered !== fn));
        },
        dispatch(type, event) {
            for (const fn of documentListeners.get(type) ?? []) fn(event);
        },
        documentElement: documentElement ?? { classList: classList() },
    };
}

function handler() {
    const calls = [];
    return { calls, invokeMethodAsync(name) { calls.push(name); return Promise.resolve(); } };
}

test('a modal trap marks the background inert and leaves its own ancestors alone', () => {
    const body = element();
    const page = element(body);
    const overlay = element(body);
    const dialog = element(overlay);
    installDom(body);

    activate(dialog, 'sheet', null, false, null, true);

    assert.equal(page.hasAttribute('inert'), true, 'the page behind the sheet must be unreachable');
    assert.equal(overlay.hasAttribute('inert'), false, 'the sheet must not inert its own container');
    assert.equal(dialog.hasAttribute('inert'), false);
});

test('a modal trap inerts the siblings at every level up to body, the Blazor #app shape', () => {
    // Blazor hosts render body > #app > page, and the overlay is a descendant of #app. Inerting only
    // body's children leaves #app (and the page inside it) reachable.
    const body = element();
    const app = element(body);
    const page = element(app);
    const overlay = element(app);
    const dialog = element(overlay);
    installDom(body);

    activate(dialog, 'sheet', null, false, null, true);

    assert.equal(page.hasAttribute('inert'), true, 'the page inside #app must be inert while the sheet is open');
    assert.equal(app.hasAttribute('inert'), false, 'the overlay host stays reachable, otherwise the trap inerts itself');
    assert.equal(overlay.hasAttribute('inert'), false);

    deactivate('sheet');
    assert.equal(page.hasAttribute('inert'), false, 'closing the last modal restores exactly what it marked');
});

test('a non-modal trap leaves the background usable', () => {
    const body = element();
    const page = element(body);
    const drawer = element(body);
    installDom(body);

    activate(drawer, 'inline', null, false, null, false);

    assert.equal(page.hasAttribute('inert'), false);
});

test('only the innermost trap cycles Tab and only the topmost handles Escape', () => {
    const body = element();
    const outer = element(body);
    const inner = element(outer);
    installDom(body);
    const outerEscape = handler();
    const innerEscape = handler();

    activate(outer, 'outer', outerEscape, true, null, true);
    activate(inner, 'inner', innerEscape, true, null, true);

    assert.equal(isInnermost('outer'), false);
    assert.equal(isInnermost('inner'), true);
    assert.equal(isTopmost('inner'), true);

    const tab = { key: 'Tab', preventDefault() { tab.prevented = true; } };
    outer.dispatch('keydown', tab);
    assert.equal(tab.prevented, undefined, 'the outer trap must not cycle Tab while a dialog is nested in it');

    document.dispatch('keydown', { key: 'Escape' });
    assert.deepEqual(innerEscape.calls, ['HandleFocusTrapEscapeAsync']);
    assert.deepEqual(outerEscape.calls, [], 'the sheet behind must not close on the same Escape');
});

test('closing the inner trap hands Escape back to the outer one and keeps the background inert', () => {
    const body = element();
    const page = element(body);
    const outer = element(body);
    const inner = element(outer);
    installDom(body);
    const outerEscape = handler();

    activate(outer, 'outer', outerEscape, true, null, true);
    activate(inner, 'inner', handler(), true, null, true);
    deactivate('inner');

    document.dispatch('keydown', { key: 'Escape' });
    assert.deepEqual(outerEscape.calls, ['HandleFocusTrapEscapeAsync']);
    assert.equal(page.hasAttribute('inert'), true, 'the page stays inert while the sheet is still open');

    deactivate('outer');
    assert.equal(page.hasAttribute('inert'), false);
});

test('deactivation restores focus to the named target', () => {
    const body = element();
    const dialog = element(body);
    const canvas = element(body);
    installDom(body);

    activate(dialog, 'scope', null, false, canvas, true);
    deactivate('scope');

    assert.equal(canvas.focusCalls, 1);
});

test('a non-modal trap does not cycle Tab and does not move initial focus', () => {
    const body = element();
    const drawer = element(body);
    const behind = element(body);
    behind.offsetParent = {};
    drawer.querySelectorAll = () => [behind];
    installDom(body);
    document.activeElement = behind;

    activate(drawer, 'inline', null, false, null, false);

    const tab = { key: 'Tab', shiftKey: false, preventDefault() { tab.prevented = true; } };
    drawer.dispatch('keydown', tab);
    assert.equal(tab.prevented, undefined, 'a non-modal sheet must let Tab reach the page behind it');
    assert.equal(behind.focusCalls, 0, 'a non-modal sheet must not steal focus on open');
});

test('deactivation resolves a restore target by id when the element reference is gone', () => {
    const body = element();
    const dialog = element(body);
    dialog.dataset = { restoreTarget: 'canvas' };
    const canvas = element(body);
    canvas.id = 'canvas';
    installDom(body);
    document.getElementById = (id) => (id === 'canvas' ? canvas : null);

    // The reference captured at open is disconnected (a re-rendered trigger). The id on the root
    // still names the element focus should return to.
    const gone = element();
    gone.isConnected = false;
    activate(dialog, 'scope', null, false, gone, true);
    deactivate('scope');

    assert.equal(canvas.focusCalls, 1, 'the id recorded on the root must win over a disconnected reference');
});

test('an initial focus element beats the named id', () => {
    const body = element();
    const scope = element(body);
    scope.dataset = { initialFocus: 'named' };
    const named = element(scope);
    const preferred = element(scope);
    installDom(body);
    document.getElementById = (id) => (id === 'named' ? named : null);

    activate(scope, 'scope', null, false, null, true, preferred);

    assert.equal(preferred.focusCalls, 1, 'the element reference is the initial target');
    assert.equal(named.focusCalls, 0, 'the named id is only the fallback');
});

test('a dialog declared in page content becomes reachable when it opens', () => {
    const body = element();
    const app = element(body);
    const content = element(app);
    const dialog = element(content);
    const drawer = element(app);
    installDom(body);

    activate(drawer, 'drawer', { invokeMethodAsync() {} }, true, null, true);
    assert.equal(content.attributes.has('inert'), true, 'the open drawer inerts the page content that holds the dialog');

    activate(dialog, 'dialog', { invokeMethodAsync() {} }, true, null, true);
    assert.equal(content.attributes.has('inert'), false, 'opening the dialog releases the ancestor the drawer inerted');
    assert.equal(dialog.attributes.has('inert'), false, 'the dialog itself stays reachable');
});

test('a backdrop rendered as a sibling of the trap root stays clickable', () => {
    // TmDrawer renders .tm-drawer__overlay as a sibling of the TmFocusScope root. The walk that
    // inerts the page must not inert a backdrop that belongs to the overlay, or a click on it never
    // reaches the close handler.
    const body = element();
    const app = element(body);
    const page = element(app);
    const backdrop = element(app);
    backdrop.dataset = { tmBackdrop: '' };
    const scope = element(app);
    installDom(body);

    activate(scope, 'drawer', null, false, null, true);

    assert.equal(backdrop.hasAttribute('inert'), false, 'the overlay backdrop must stay clickable');
    assert.equal(page.hasAttribute('inert'), true, 'the page behind the overlay is still inert');
});

test('a nested dialog restores the outer drawer and releases the scroll lock only once', () => {
    // The inner trap walks up through the drawer and inerts the drawer's own content. Closing it
    // must hand the page back to the outer trap, and two modal traps must not leak html.tm-scroll-lock.
    const root = { classList: classList() };
    const body = element();
    const app = element(body);
    const page = element(app);
    const drawer = element(app);
    const header = element(drawer);
    const drawerBody = element(drawer);
    const drawerText = element(drawerBody);
    const overlay = element(drawerBody);
    const dialog = element(overlay);
    const opener = element(drawer);
    opener.focus = () => { opener.focusCalls++; };
    installDom(body, root);
    document.activeElement = opener;

    activate(drawer, 'drawer', null, false, null, true);
    activate(dialog, 'dialog', null, false, null, true);
    assert.equal(root.classList.contains('tm-scroll-lock'), true);

    deactivate('dialog');

    assert.equal(header.hasAttribute('inert'), false, 'the outer drawer is usable after the dialog closes');
    assert.equal(drawerText.hasAttribute('inert'), false);
    assert.equal(page.hasAttribute('inert'), true, 'the page stays inert while the drawer is still open');
    assert.equal(root.classList.contains('tm-scroll-lock'), true, 'one modal is still open');
    assert.equal(opener.focusCalls, 1, 'focus returns to the opener inside the outer trap');

    deactivate('drawer');
    assert.equal(page.hasAttribute('inert'), false);
    assert.equal(root.classList.contains('tm-scroll-lock'), false, 'the last modal releases the lock');
});

test('a sibling dialog does not leave the outer drawer inert', () => {
    const body = element();
    const app = element(body);
    const drawer = element(app);
    const overlay = element(app);
    const dialog = element(overlay);
    installDom(body);

    activate(drawer, 'drawer', null, false, null, true);
    activate(dialog, 'dialog', null, false, null, true);
    deactivate('dialog');

    assert.equal(drawer.hasAttribute('inert'), false, 'closing the topmost trap re-inerts for the one that remains');
});

test('an element that was already inert is not cleared by a trap that did not set it', () => {
    const body = element();
    const app = element(body);
    const hostInert = element(app);
    hostInert.toggleAttribute('inert', true);
    const scope = element(app);
    installDom(body);

    activate(scope, 'sheet', null, false, null, true);
    deactivate('sheet');

    assert.equal(hostInert.hasAttribute('inert'), true, 'a host-owned inert survives the trap');
});

test('a named id beats the opener captured at activation', () => {
    const body = element();
    const opener = element(body);
    opener.focus = () => { opener.focusCalls++; };
    const named = element(body);
    named.id = 'canvas';
    named.focus = () => { named.focusCalls++; };
    const dialog = element(body);
    dialog.dataset = { restoreTarget: 'canvas' };
    installDom(body);
    document.activeElement = opener;
    document.getElementById = (id) => (id === 'canvas' ? named : null);

    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');

    assert.equal(named.focusCalls, 1, 'RestoreFocusTargetId wins over the element focused at open');
    assert.equal(opener.focusCalls, 0);
});

test('RestoreFocus=false restores nothing, not even the opener', () => {
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const opener = element(body);
    opener.focus = () => { opener.focusCalls++; };
    const dialog = element(body);
    dialog.dataset = { restoreFocus: 'false' };
    installDom(body);
    document.activeElement = opener;

    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');

    assert.equal(opener.focusCalls, 0);
    assert.equal(body.focusCalls, 0, 'RestoreFocus=false restores nothing at all');
});

test('initial focus lands on the named target, not the first focusable', () => {
    const body = element();
    const dialog = element(body);
    const close = element(dialog);
    const name = element(dialog);
    name.id = 'name';
    dialog.dataset = { initialFocus: 'name' };
    dialog.querySelectorAll = () => [close, name];
    installDom(body);
    document.getElementById = (id) => (id === 'name' ? name : null);

    activate(dialog, 'scope', null, false, null, true);

    assert.equal(name.focusCalls, 1, 'the initial-focus id is forwarded to activate');
    assert.equal(close.focusCalls, 0);
});

test('deactivation falls back when the restore target is disconnected and has no id', () => {
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const dialog = element(body);
    installDom(body);
    document.getElementById = () => null;

    const gone = element();
    gone.isConnected = false;
    gone.focus = () => { throw new Error('disconnected'); };
    activate(dialog, 'scope', null, false, gone, true);

    assert.doesNotThrow(() => deactivate('scope'));
    assert.equal(body.focusCalls, 1, 'a disconnected target with no id falls back to body without throwing');
});
