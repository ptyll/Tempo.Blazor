// Focus-trap tests with hand-rolled DOM stubs, the same approach as overlay-lifecycle.test.mjs:
// no jsdom. The stubs only implement what the module touches.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    activate, deactivate, isInnermost, isTopmost, syncScrollRegion, stopScrollRegion, focusIfLost, focusWithin, __resetForTests,
} from '../tm-focus-trap.js';

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
        toggleAttribute(name, force) {
            const on = force === undefined ? !el.attributes.has(name) : force;
            if (on) el.attributes.add(name); else el.attributes.delete(name);
            return on;
        },
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

test('initial focus falls back to a data-tm-id marker when no real id is rendered', () => {
    const body = element();
    const scope = element(body);
    scope.dataset = { initialFocus: 'view-name-abc123' };
    const field = element(scope);
    const first = element(scope);
    installDom(body);
    // The named field is deliberately NOT the first focusable: with no marker fallback the trap
    // lands on the close button instead, which is the reported regression.
    scope.querySelectorAll = () => [first, field];
    document.querySelector = (selector) => (
        selector === '[data-tm-id="view-name-abc123"]' ? field : null);

    activate(scope, 'scope', null, false, null, true);

    assert.equal(field.focusCalls, 1, 'the data-tm-id marker resolves the initial target when there is no id');
    assert.equal(first.focusCalls, 0, 'the first focusable is only the last resort');
});

test('the data-tm-id marker value cannot break out of the attribute selector', () => {
    const body = element();
    const scope = element(body);
    scope.dataset = { initialFocus: 'name"injected' };
    const field = element(scope);
    installDom(body);
    scope.querySelectorAll = () => [field];
    let seen = null;
    document.querySelector = (selector) => { seen = selector; return field; };

    activate(scope, 'scope', null, false, null, true);

    assert.ok(seen?.includes('\\"'), `the quote in the marker value must be escaped, got ${seen}`);
    assert.equal(field.focusCalls, 1);
});

test('an initial-focus selector resolves the first match inside the trap', () => {
    // T3: a menu/listbox sheet wants the FIRST menuitem/option focused, not the first focusable
    // (the header Done). The selector is scoped to the trap element.
    const body = element();
    const scope = element(body);
    scope.dataset = { initialFocusSelector: '[role="menuitem"]' };
    const done = element(scope);
    const item = element(scope);
    installDom(body);
    scope.querySelectorAll = () => [done, item];
    let seen = null;
    scope.querySelector = (selector) => { seen = selector; return item; };

    activate(scope, 'scope', null, false, null, true);

    assert.equal(seen, '[role="menuitem"]', 'the selector is forwarded scoped to the trap');
    assert.equal(item.focusCalls, 1, 'the selector match takes initial focus');
    assert.equal(done.focusCalls, 0);
});

test('an explicit initial target beats the selector', () => {
    const body = element();
    const scope = element(body);
    scope.dataset = { initialFocusSelector: '[role="menuitem"]' };
    const item = element(scope);
    const preferred = element(scope);
    installDom(body);
    scope.querySelectorAll = () => [item, preferred];
    scope.querySelector = () => { throw new Error('the selector must not even run'); };

    activate(scope, 'scope', null, false, null, true, preferred);

    assert.equal(preferred.focusCalls, 1);
    assert.equal(item.focusCalls, 0);
});

test('an invalid consumer selector must not throw after the inert and scroll lock landed', () => {
    // U8: the selector is a consumer-supplied string. A typo must degrade to the first
    // focusable — the trap has already marked the background inert and locked the scroll when
    // the selector runs, so a throw would strand the page inert with focus nowhere.
    const body = element();
    const scope = element(body);
    scope.dataset = { initialFocusSelector: '[role=' };
    const first = element(scope);
    scope.querySelectorAll = () => [first];
    scope.querySelector = () => { throw new DOMException('not a valid selector', 'SyntaxError'); };
    const documentElement = { classList: classList() };
    installDom(body, documentElement);

    activate(scope, 'scope', null, false, null, true);

    assert.equal(first.focusCalls, 1, 'the invalid selector degrades to the first focusable');
    assert.equal(documentElement.classList.contains('tm-scroll-lock'), true,
        'the scroll lock landed (the throw would have escaped after it)');
});

test('initial focus retries on the next animation frames when the first move did not land', () => {
    // UX r2 m1: a focus() on a visibility:hidden panel (anchor mid-scroll) is dropped. The stub
    // focus() never moves document.activeElement, which models the same "move did not land"
    // shape — the trap must schedule its two retries and re-run the resolution.
    const body = element();
    const scope = element(body);
    const first = element(scope);
    scope.querySelectorAll = () => [first];
    const frames = [];
    globalThis.requestAnimationFrame = (cb) => { frames.push(cb); return frames.length; };
    try {
        installDom(body);

        activate(scope, 'scope', null, false, null, true);

        assert.equal(first.focusCalls, 1, 'the immediate move runs');
        assert.equal(frames.length, 1, 'one retry frame is scheduled');

        frames.shift()(0);
        assert.equal(first.focusCalls, 2, 'the first retry re-runs the resolution');
        assert.equal(frames.length, 1, 'the second retry frame is scheduled');

        frames.shift()(0);
        assert.equal(first.focusCalls, 3);
        assert.equal(frames.length, 0, 'the two-frame retry budget is exhausted');
    }
    finally {
        delete globalThis.requestAnimationFrame;
    }
});

test('the initial-focus retry stops early once focus landed', () => {
    const body = element();
    const scope = element(body);
    const first = element(scope);
    scope.querySelectorAll = () => [first];
    first.focus = () => { document.activeElement = first; first.focusCalls++; };
    const frames = [];
    globalThis.requestAnimationFrame = (cb) => { frames.push(cb); return frames.length; };
    try {
        installDom(body);

        activate(scope, 'scope', null, false, null, true);

        assert.equal(first.focusCalls, 1);
        assert.equal(frames.length, 0, 'no retry once the move landed');
    }
    finally {
        delete globalThis.requestAnimationFrame;
    }
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
    // A host that still renders the backdrop beside the trap root marks it data-tm-backdrop. The walk
    // that inerts the page must not inert it, or a click on it never reaches the close handler. TmDrawer
    // renders its overlay inside the trap instead, so this is the fallback, not the layout.
    const body = element();
    const app = element(body);
    const page = element(app);
    const backdrop = element(app);
    backdrop.toggleAttribute('data-tm-backdrop', true);
    const scope = element(app);
    installDom(body);

    activate(scope, 'drawer', null, false, null, true);

    assert.equal(backdrop.hasAttribute('inert'), false, 'the overlay backdrop must stay clickable');
    assert.equal(page.hasAttribute('inert'), true, 'the page behind the overlay is still inert');
});

test('a toast container marked data-tm-inert-exempt stays reachable under a modal trap', () => {
    // Toast containers are non-modal status UI (pointer-events:none except the cards) pinned to
    // the top layer. Inerting them under a modal trap made a click on a toast's dismiss button
    // pass THROUGH to the backdrop and close the sheet (data loss) and silenced the role=alert
    // announcement. The walk skips data-tm-inert-exempt like it skips the backdrop.
    const body = element();
    const app = element(body);
    const page = element(app);
    const toasts = element(app);
    toasts.toggleAttribute('data-tm-inert-exempt', true);
    const scope = element(app);
    installDom(body);

    activate(scope, 'drawer', null, false, null, true);

    assert.equal(toasts.hasAttribute('inert'), false,
        'the toast container must stay reachable so its dismiss button works and role=alert is announced');
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

// ── Scroll region (review round 5) ─────────────────────────────────────────────
// The dialog scroller must be a keyboard-reachable region only while it actually overflows. A
// hardcoded tabindex="0" made it the first tabbable element of every dialog, so the trap focused
// the title block and short dialogs grew an extra tab stop.

function scroller(scrollHeight, clientHeight) {
    const attrs = new Map();
    return {
        scrollHeight,
        clientHeight,
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        hasAttribute(name) { return attrs.has(name); },
    };
}

test('an overflowing scroller becomes a tabindex=0 region named by its title', () => {
    const content = scroller(900, 400);
    syncScrollRegion(content, 'long', 'title-1');

    assert.equal(content.getAttribute('tabindex'), '0', 'a keyboard-only user cannot scroll an unfocusable scroller');
    assert.equal(content.getAttribute('role'), 'region');
    assert.equal(content.getAttribute('aria-labelledby'), 'title-1');

    stopScrollRegion('long');
});

test('a short scroller grows no tab stop and no dangling region', () => {
    const content = scroller(300, 400);
    syncScrollRegion(content, 'short', 'title-2');

    assert.equal(content.getAttribute('tabindex'), null, 'a short dialog must not grow an extra tab stop');
    assert.equal(content.getAttribute('role'), null);
    assert.equal(content.getAttribute('aria-labelledby'), null);

    stopScrollRegion('short');
});

test('an overflowing scroller without a title stays a nameless tabindex=0 region', () => {
    const content = scroller(900, 400);
    syncScrollRegion(content, 'untitled', null);

    assert.equal(content.getAttribute('tabindex'), '0');
    assert.equal(content.getAttribute('role'), null, 'a region without a name is noise for a reader');
    assert.equal(content.getAttribute('aria-labelledby'), null);

    stopScrollRegion('untitled');
});

test('an overflowing scroller without a title id is named by the aria-label fallback (F6 r2 G9)', () => {
    const content = scroller(900, 400);
    syncScrollRegion(content, 'labelled', null, 'Panels');

    assert.equal(content.getAttribute('tabindex'), '0');
    assert.equal(content.getAttribute('role'), 'region');
    assert.equal(content.getAttribute('aria-label'), 'Panels');
    assert.equal(content.getAttribute('aria-labelledby'), null);

    stopScrollRegion('labelled');
    assert.equal(content.getAttribute('aria-label'), null, 'stopping clears the name it wrote');
});

test('a title id wins over the aria-label fallback', () => {
    const content = scroller(900, 400);
    syncScrollRegion(content, 'both', 'title-9', 'Panels');

    assert.equal(content.getAttribute('aria-labelledby'), 'title-9');
    assert.equal(content.getAttribute('aria-label'), null);

    stopScrollRegion('both');
});
test('a resize re-decides the attributes and stopping clears them', () => {
    let overflows = true;
    const content = {
        get scrollHeight() { return 900; },
        get clientHeight() { return overflows ? 400 : 900; },
        setAttribute() {},
        removeAttribute() {},
        getAttribute() { return null; },
        hasAttribute() { return false; },
    };
    const observers = [];
    globalThis.ResizeObserver = class {
        constructor(callback) { observers.push(callback); }
        observe() {}
        disconnect() {}
    };

    try {
        syncScrollRegion(content, 'resize', 'title-3');
        assert.equal(observers.length, 1, 'the observer is registered once');
        overflows = false;
        observers[0]();
        assert.equal(content.getAttribute('tabindex'), null, 'shrinking below the fold removes the tab stop');
    } finally {
        delete globalThis.ResizeObserver;
    }
    stopScrollRegion('resize');
});

test('re-attaching the same id replaces the region and clears the previous element', () => {
    const first = scroller(900, 400);
    const second = scroller(900, 400);
    syncScrollRegion(first, 'replace', 'title-4');
    syncScrollRegion(second, 'replace', 'title-4');

    assert.equal(first.getAttribute('tabindex'), null, 'the replaced element loses the attributes');
    assert.equal(second.getAttribute('tabindex'), '0');

    stopScrollRegion('replace');
    assert.equal(second.getAttribute('tabindex'), null, 'stopping clears the live element too');
});

test('a null element is a no-op that still stops any previous region', () => {
    const content = scroller(900, 400);
    syncScrollRegion(content, 'null-guard', 'title-5');
    const stop = syncScrollRegion(null, 'null-guard', 'title-5');

    assert.equal(content.getAttribute('tabindex'), null, 'the null attach stopped the previous region');
    assert.doesNotThrow(() => stop());
});

// ── focusIfLost (F6 r1): the restore a host surface runs after ITS surface closed. It moves focus
// only when focus was lost (body / disconnected) or was inside the closing surface — never when the
// user or the host put focus on another live element.
function withDocument(active, body, byId = {}) {
    installDom(body);
    globalThis.document.activeElement = active;
    globalThis.document.getElementById = id => byId[id] ?? null;
}

test('focusIfLost focuses the target when focus fell to the body', () => {
    const body = element();
    const target = element(body);
    withDocument(body, body);

    assert.equal(focusIfLost(target), true);
    assert.equal(target.focusCalls, 1);
});

test('focusIfLost leaves focus alone when another live element holds it', () => {
    const body = element();
    const toolbarButton = element(body);
    toolbarButton.isConnected = true;
    const target = element(body);
    withDocument(toolbarButton, body);

    assert.equal(focusIfLost(target), false);
    assert.equal(target.focusCalls, 0, 'a focus the user placed elsewhere must never be stolen');
});

test('focusIfLost focuses when the active element is inside the closing container', () => {
    const body = element();
    const container = element(body);
    const inside = element(container);
    inside.isConnected = true;
    const target = element(body);
    withDocument(inside, body);

    assert.equal(focusIfLost(target, container), true);
    assert.equal(target.focusCalls, 1);
});

test('focusIfLost focuses when the active element left the DOM', () => {
    const body = element();
    const gone = element(body);
    gone.isConnected = false;
    const target = element(body);
    withDocument(gone, body);

    assert.equal(focusIfLost(target), true);
});

test('focusIfLost resolves ids and refuses a missing or detached target', () => {
    const body = element();
    const target = element(body);
    withDocument(body, body, { toggle: target });

    assert.equal(focusIfLost('toggle'), true);
    assert.equal(target.focusCalls, 1);
    assert.equal(focusIfLost('missing'), false);
    assert.equal(focusIfLost(null), false);
    const detached = element(body);
    detached.isConnected = false;
    assert.equal(focusIfLost(detached), false);
});

// ── F6 r2 G3: non-modal traps close on focus ownership, not registration order ──

function escapeEvent() {
    const e = { key: 'Escape', defaultPrevented: false };
    return e;
}

test('two non-modal traps: Escape with focus in the FIRST closes only the first (order-independent)', () => {
    const body = element();
    const first = element(body);
    const second = element(body);
    const insideFirst = element(first);
    installDom(body);
    const firstEscape = handler();
    const secondEscape = handler();

    activate(first, 'first', firstEscape, true, null, false);
    activate(second, 'second', secondEscape, true, null, false);
    document.activeElement = insideFirst;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(firstEscape.calls, ['HandleFocusTrapEscapeAsync'], 'the sheet that holds focus closes');
    assert.deepEqual(secondEscape.calls, [], 'the later-registered sheet that does not hold focus stays open');
});

test('two non-modal traps: Escape with focus in the LAST closes only the last', () => {
    const body = element();
    const first = element(body);
    const second = element(body);
    const insideSecond = element(second);
    installDom(body);
    const firstEscape = handler();
    const secondEscape = handler();

    activate(first, 'first', firstEscape, true, null, false);
    activate(second, 'second', secondEscape, true, null, false);
    document.activeElement = insideSecond;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(secondEscape.calls, ['HandleFocusTrapEscapeAsync']);
    assert.deepEqual(firstEscape.calls, []);
});

test('a modal trap registered AFTER a non-modal one owns Escape even when focus is in the sheet', () => {
    const body = element();
    const sheet = element(body);
    const insideSheet = element(sheet);
    const dialog = element(body);
    installDom(body);
    const sheetEscape = handler();
    const dialogEscape = handler();

    activate(sheet, 'sheet', sheetEscape, true, null, false);
    activate(dialog, 'dialog', dialogEscape, true, null, true);
    document.activeElement = insideSheet;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(dialogEscape.calls, ['HandleFocusTrapEscapeAsync'], 'the later modal wins');
    assert.deepEqual(sheetEscape.calls, [], 'the sheet behind a dialog must not close on the same key');
});

test('nested non-modal traps: the innermost one that holds focus closes, not the outer', () => {
    const body = element();
    const outer = element(body);
    const inner = element(outer);
    const insideInner = element(inner);
    installDom(body);
    const outerEscape = handler();
    const innerEscape = handler();

    activate(outer, 'outer', outerEscape, true, null, false);
    activate(inner, 'inner', innerEscape, true, null, false);
    document.activeElement = insideInner;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(innerEscape.calls, ['HandleFocusTrapEscapeAsync']);
    assert.deepEqual(outerEscape.calls, [], 'one Escape closes one layer');
});

test('a non-modal trap ignores Escape while focus is outside it (the canvas behind belongs to the page)', () => {
    const body = element();
    const sheet = element(body);
    const canvas = element(body);
    installDom(body);
    const sheetEscape = handler();

    activate(sheet, 'sheet', sheetEscape, true, null, false);
    document.activeElement = canvas;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(sheetEscape.calls, []);
});

// ── F6 r2 G4: focusWithin tells the host whether focus is inside a surface it is about to remove ──

test('focusWithin is true only while the active element is inside the container', () => {
    const body = element();
    const panel = element(body);
    const inside = element(panel);
    const outside = element(body);
    installDom(body);

    document.activeElement = inside;
    assert.equal(focusWithin(panel), true);
    document.activeElement = panel;
    assert.equal(focusWithin(panel), true, 'the container itself counts');
    document.activeElement = outside;
    assert.equal(focusWithin(panel), false);
    document.activeElement = body;
    assert.equal(focusWithin(panel), false, 'focus on the body is lost, not inside');
    assert.equal(focusWithin(null), false);
});