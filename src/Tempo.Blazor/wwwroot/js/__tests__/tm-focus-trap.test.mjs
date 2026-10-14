// Focus-trap tests with hand-rolled DOM stubs, the same approach as overlay-lifecycle.test.mjs:
// no jsdom. The stubs only implement what the module touches.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    activate, deactivate, ensureLoaded, isInnermost, isTopmost, modalOwnsEscape,
    restoreTrapFocusIfLost, syncScrollRegion, stopScrollRegion, focusIfLost, focusWithin,
    noteReturnTarget, __resetForTests,
} from '../tm-focus-trap.js';

test.beforeEach(() => __resetForTests());

/** Marks a stub element focusable for the module's FOCUSABLE lookups: the stub closest() below
 * understands only bare attribute selectors, so the complex FOCUSABLE selector query (used by the
 * pointerdown record and the focus history) would otherwise match nothing. The override answers
 * only the focusability query and forwards everything else. */
function focusable(el) {
    const original = el.closest.bind(el);
    el.closest = (selector) => (
        typeof selector === 'string' && selector.includes('button:not([disabled])')
            ? el
            : original(selector));
    return el;
}

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
        isConnected: true,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        contains(other) {
            for (let node = other; node; node = node.parent) if (node === el) return true;
            return false;
        },
        querySelectorAll() { return []; },
        // Attribute-presence selectors only — the module asks closest('[data-tm-inert-exempt]').
        closest(selector) {
            const attr = /^\[\s*([^\s~|^$*=]+)\s*\]$/.exec(selector)?.[1];
            if (!attr) return null;
            for (let node = el; node; node = node.parent) {
                if (node.hasAttribute(attr)) return node;
            }
            return null;
        },
        hasAttribute(name) { return el.attributes.has(name); },
        toggleAttribute(name, force) {
            const on = force === undefined ? !el.attributes.has(name) : force;
            if (on) el.attributes.add(name); else el.attributes.delete(name);
            return on;
        },
        get children() { return el._children; },
        set children(value) { el._children = value; },
        dataset: {},
        focus() {
            el.focusCalls++;
            // The real DOM moves document.activeElement with every focus() — the restore guard
            // reads it to decide whether the close still owns the focus. A stub that kept
            // activeElement parked would make every deactivate look like "focus already went
            // elsewhere" and silently mask the guard.
            if (globalThis.document) globalThis.document.activeElement = el;
        },
        dispatch(type, event) { listeners.get(type)?.(event); },
    };
    if (parent) parent.children.push(el);
    return el;
}

/** An inert-exempt toast card in the DOM: the container plus a dismiss button inside it. */
function toastCard(parent) {
    const container = element(parent);
    container.toggleAttribute('data-tm-inert-exempt', true);
    const dismiss = element(container);
    return { container, dismiss };
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
    // element's focus() moves document.activeElement (like the real DOM), so a dropped move is
    // modelled explicitly: focus is counted but activeElement stays put — the trap must schedule
    // its two retries and re-run the resolution.
    const body = element();
    const scope = element(body);
    const first = element(scope);
    first.focus = () => { first.focusCalls++; };
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

// ── Return-target hygiene ─────────────────────────────────────────
// The trap's own capture runs when the LAZY import completes, so an app that focuses a field
// inside the overlay during the open lands BEFORE activate(): the capture then names an element
// that dies with the surface, and the close focused that dying element (the focus fell to <body>
// the moment Blazor removed it). FocusTrap therefore notes the opener BEFORE the surface renders
// (noteReturnTarget), that note wins over the late capture, and a capture that lies inside the
// surface is never recorded. Deactivation must also never steal a focus the app already placed
// on a live element outside the overlay (a post-close action button focused before the render).

test('a field focused inside the overlay before activation never becomes the return target', () => {
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const trigger = element(body);
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    // The app focused the field while the lazy import was still in flight; a note that raced and
    // lost reads the same poisoned element.
    document.activeElement = field;
    noteReturnTarget('scope');
    activate(dialog, 'scope', null, false, null, true);

    deactivate('scope');

    assert.equal(field.focusCalls, 0, 'the poisoned capture must not focus the dying overlay');
    assert.equal(body.focusCalls, 1, 'without a valid opener the close lands on the body deterministically');
});

test('a noted opener beats the app focus that landed inside the overlay during the import', () => {
    const body = element();
    const trigger = element(body);
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    // FocusTrap notes the opener BEFORE the overlay renders — the trigger is still focused.
    noteReturnTarget('scope');
    // The app focused its field while the import was in flight; the late capture lies inside.
    document.activeElement = field;
    activate(dialog, 'scope', null, false, null, true);

    deactivate('scope');

    assert.equal(field.focusCalls, 0, 'the dying field is never focused on close');
    assert.equal(trigger.focusCalls, 1, 'focus returns to the opener, not to the body');
});

test('the last pointerdown rescues a poisoned capture for a pointer-opened surface', () => {
    // No note (the note rides the same lazy import and lands poisoned too): the user CLICKED the
    // trigger — the last document pointerdown names it — while the capture and the activeElement
    // both name the field the app focused inside the overlay.
    const body = element();
    const trigger = focusable(element(body));
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    activate(dialog, 'first', null, false, null, true);   // installs the pointerdown listener
    deactivate('first');
    // The warm-up open already exercised the restore path on this shared opener; the assertions
    // below must see only what THIS open's rescue does.
    trigger.focusCalls = 0;
    document.dispatch('pointerdown', { target: trigger }); // the user clicks the open trigger
    document.activeElement = field;                        // the app focuses its field inside
    activate(dialog, 'scope', null, false, null, true);

    deactivate('scope');

    assert.equal(field.focusCalls, 0, 'the dying field is never focused on close');
    assert.equal(trigger.focusCalls, 1, 'the engaged element is the opener the restore returns to');
});

test('a keyboard-opened surface is rescued by the focus history, and a stale press never wins', () => {
    // Enter on a focused trigger: there IS no pointerdown for this open — and a press from an
    // earlier interaction must not survive the keyboard gesture anyway. The focusin the trigger's
    // own focus produced is what names it.
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const trigger = focusable(element(body));
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    activate(dialog, 'first', null, false, null, true);   // installs the listeners
    deactivate('first');
    trigger.focusCalls = 0;

    document.dispatch('pointerdown', { target: trigger }); // an earlier pointer interaction...
    document.dispatch('focusin', { target: trigger });     // ...the trigger takes focus (focusin seen)
    document.dispatch('keydown', { key: 'Enter' });        // Enter opens — clears the press record
    document.activeElement = field;                        // the app focuses its field inside
    activate(dialog, 'scope', null, false, null, true);

    deactivate('scope');

    assert.equal(field.focusCalls, 0, 'the dying field is never focused on close');
    assert.equal(trigger.focusCalls, 1, 'the focus history names the keyboard-opened opener');
});

test('a pointerdown with no focusable ancestor records nothing (Safari body press)', () => {
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const plain = element(body);           // a div: no focusable ancestor at all
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = body;

    activate(dialog, 'first', null, false, null, true);
    deactivate('first');
    body.focusCalls = 0;               // the warm-up close already fell back to the body
    document.dispatch('pointerdown', { target: plain });   // closest(FOCUSABLE) → null
    document.activeElement = field;
    activate(dialog, 'scope', null, false, null, true);

    deactivate('scope');

    assert.equal(field.focusCalls, 0);
    assert.equal(body.focusCalls, 1, 'a dead pointerdown candidate is never the opener — body fallback');
});

test('a pointerdown older than the staleness window is not the opener', () => {
    const realNow = Date.now;
    try {
        let now = 1_000_000;
        Date.now = () => now;
        const body = element();
        body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
        const trigger = focusable(element(body));
        trigger.focus = () => { trigger.focusCalls++; };
        const dialog = element(body);
        const field = element(dialog);
        field.focus = () => { field.focusCalls++; };
        installDom(body);
        document.activeElement = trigger;

        activate(dialog, 'first', null, false, null, true);
        deactivate('first');
        trigger.focusCalls = 0;
        document.dispatch('pointerdown', { target: trigger });
        now += 3000;                                       // the press is three seconds old
        document.activeElement = field;
        activate(dialog, 'scope', null, false, null, true);

        deactivate('scope');

        assert.equal(field.focusCalls, 0);
        assert.equal(body.focusCalls, 1, 'a stale press is dropped — body fallback');
        assert.equal(trigger.focusCalls, 0);
    }
    finally {
        Date.now = realNow;
    }
});

test('a pointerdown inside the closing overlay is never the opener', () => {
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const dialog = element(body);
    const close = element(dialog);   // the user's last press was the close button, INSIDE the overlay
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = body;

    // The press is dispatched BEFORE the activation (the real click-to-close sequence: press the
    // X, then the close render deactivates) with a poisoned activeElement — the press on the
    // surface itself must not win the opener vote over the outside check.
    document.dispatch('pointerdown', { target: close });
    document.activeElement = field;
    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');

    assert.equal(close.focusCalls, 0, 'the close button press must not become the return target');
    assert.equal(field.focusCalls, 0, 'the dying field is never focused on close');
    assert.equal(body.focusCalls, 1, 'no candidate outside the surface — the close lands on the body');
});

test('a noted opener that is disconnected while the surface is open is never focused', () => {
    const body = element();
    body.focus = () => { body.focusCalls = (body.focusCalls ?? 0) + 1; };
    const trigger = focusable(element(body));
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    noteReturnTarget('scope');
    document.activeElement = field;
    activate(dialog, 'scope', null, false, null, true);
    trigger.isConnected = false;               // a re-render replaced the trigger while open
    deactivate('scope');

    assert.equal(trigger.focusCalls, 0, 'a disconnected opener is never focused');
    assert.equal(body.focusCalls, 1, 'the close falls back to the body deterministically');
});

test('ensureLoaded installs the records before the FIRST activation of a session', () => {
    // Cold start: the owner warms the module up while still closed, so the first open already has
    // the pointerdown record — previously the first activation loaded the module and lost the
    // opener record with it.
    const body = element();
    const trigger = focusable(element(body));
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    ensureLoaded();                                        // the owner's first OnAfterRenderAsync
    document.dispatch('pointerdown', { target: trigger });
    document.activeElement = field;                        // the app autofocus races the import
    activate(dialog, 'scope', null, false, null, true);

    deactivate('scope');

    assert.equal(field.focusCalls, 0);
    assert.equal(trigger.focusCalls, 1, 'the very first open of a session restores its opener too');
});

test('a note sent without an activate never leaks into the next open', () => {
    const body = element();
    const stale = element(body);
    stale.focus = () => { stale.focusCalls++; };
    const fresh = focusable(element(body));
    fresh.focus = () => { fresh.focusCalls++; };
    const dialog = element(body);
    const field = element(dialog);
    field.focus = () => { field.focusCalls++; };
    installDom(body);
    document.activeElement = stale;

    // Open cancelled before the trap ever activated (a close that beat the promote gate): the
    // note reached the module, but no trap is registered under the id.
    noteReturnTarget('scope');
    deactivate('scope');

    // The next open of the SAME id must not inherit the stale note.
    document.activeElement = fresh;
    noteReturnTarget('scope');
    document.activeElement = field;
    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');

    assert.equal(stale.focusCalls, 0, 'the cancelled open must not leave a note behind');
    assert.equal(fresh.focusCalls, 1, 'the next open restores ITS OWN opener');
    assert.equal(field.focusCalls, 0);
});

test('the first note of an open wins, later notes never overwrite it, and it is consumed once', () => {
    const body = element();
    const first = element(body);
    first.focus = () => { first.focusCalls++; };
    const second = element(body);
    second.focus = () => { second.focusCalls++; };
    const dialog = element(body);
    installDom(body);
    document.activeElement = first;

    // The owner's early note (its creation render, before the surface mounted) is the trustworthy
    // capture; a LATER note for the same open — a gated scope re-noting at its Active flip, by
    // which time the app's autofocus may already sit inside the surface — must not overwrite it.
    noteReturnTarget('scope');
    document.activeElement = second;
    noteReturnTarget('scope');
    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');

    assert.equal(second.focusCalls, 0, 'the late note must not replace the early opener');
    assert.equal(first.focusCalls, 1, 'the early opener wins');

    // A second activate on the same id must not resurrect the consumed note: the capture is its own.
    document.activeElement = first;
    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');
    assert.equal(first.focusCalls, 2, 'the second open captures the opener it saw at activation');
    assert.equal(second.focusCalls, 0);

    // And a FRESH note of a NEW open is honoured: the consumed entry left the slot empty.
    document.activeElement = second;
    noteReturnTarget('scope');
    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');
    assert.equal(second.focusCalls, 1, 'the fresh open notes its own opener');
    assert.equal(first.focusCalls, 2);
});

test('an explicit restore target inside the closing surface is skipped, not focused', () => {
    const body = element();
    const dialog = element(body);
    const inside = element(dialog);
    inside.focus = () => { inside.focusCalls++; };
    const trigger = element(body);
    trigger.focus = () => { trigger.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    activate(dialog, 'scope', null, false, inside, true);
    deactivate('scope');

    assert.equal(inside.focusCalls, 0, 'a target that dies with the overlay is never focused');
    assert.equal(trigger.focusCalls, 1, 'the opener is the fallback');
});

test('deactivation never steals a focus the app placed outside the overlay', () => {
    const body = element();
    const trigger = element(body);
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    const create = element(body);
    create.focus = () => { create.focusCalls++; };
    installDom(body);
    document.activeElement = trigger;

    activate(dialog, 'scope', null, false, null, true);
    // The OK handler settled focus on its post-close action BEFORE the close render deactivated
    // the trap. The restore must not yank it back to the opener.
    document.activeElement = create;
    deactivate('scope');

    assert.equal(create.focusCalls, 0, 'the app focus is not touched');
    assert.equal(trigger.focusCalls, 0, 'the opener must not be re-focused over the app decision');
});

test('a non-modal close keeps its restore-only-when-held contract', () => {
    const body = element();
    const drawer = element(body);
    const behind = element(body);
    behind.focus = () => { behind.focusCalls++; };
    installDom(body);
    document.activeElement = behind;

    activate(drawer, 'inline', null, false, null, false);
    deactivate('inline');

    assert.equal(behind.focusCalls, 0, 'the canvas focus behind the sheet is not stolen');
});

test('deactivation still restores the opener when the overlay holds the focus', () => {
    const body = element();
    const trigger = element(body);
    trigger.focus = () => { trigger.focusCalls++; };
    const dialog = element(body);
    installDom(body);
    document.activeElement = trigger;

    activate(dialog, 'scope', null, false, null, true);
    deactivate('scope');

    assert.equal(trigger.focusCalls, 1, 'a close that still owns the focus returns it to the opener');
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

// ── The region never takes initial focus (F3 follow-up, 26e1afd5 #4 / cluster-B 8.4) ──
// The scroller precedes its own content in DOM order, so once an overflow makes it the first
// focusable, the fallback "first focusable" would always resolve to it (a Custom TmDialog's
// content then never takes focus). focusInitial must skip the marked region.

test('an overflowing scroll region carries the data-tm-scroll-region marker', () => {
    const content = scroller(900, 400);
    syncScrollRegion(content, 'marked', 'title-7');

    assert.equal(content.hasAttribute('data-tm-scroll-region'), true,
        'the marker is the contract the initial-focus fallback skips');
    stopScrollRegion('marked');
    assert.equal(content.hasAttribute('data-tm-scroll-region'), false, 'stopping clears the marker with the region');
});

test('initial focus skips an overflowing scroll region and lands on the first control after it', () => {
    const body = element();
    const dialog = element(body);
    const content = element(dialog);
    // While it overflows, syncScrollRegion makes the scroller tabindex=0 and marks it.
    content.toggleAttribute('tabindex', true);
    content.toggleAttribute('data-tm-scroll-region', true);
    const button = element(dialog);
    dialog.querySelectorAll = () => [content, button];

    installDom(body);
    activate(dialog, 'scope', null, false, null, true);

    assert.equal(button.focusCalls, 1, 'the first control after the region takes initial focus');
    assert.equal(content.focusCalls, 0, 'the overflowing scroll region never takes initial focus');
});

test('initial focus falls back to the region itself when it is the only focusable', () => {
    const body = element();
    const dialog = element(body);
    const content = element(dialog);
    content.toggleAttribute('tabindex', true);
    content.toggleAttribute('data-tm-scroll-region', true);
    dialog.querySelectorAll = () => [content];

    installDom(body);
    activate(dialog, 'scope', null, false, null, true);

    assert.equal(content.focusCalls, 1, 'still "first focusable" — the skip must not strand focus nowhere');
});

// A control INSIDE the marked region that only scrolling would reveal must not take initial focus
// either: focusing it scrolls the dialog start out of view. The region itself (tabindex=0,
// role=region, named) takes the focus instead (WAI-ARIA APG) — a control already visible inside
// the region still wins.
test('a first control inside a scroll region but below the fold focuses the region instead', () => {
    const body = element();
    const dialog = element(body);
    const content = element(dialog);
    content.toggleAttribute('tabindex', true);
    content.toggleAttribute('data-tm-scroll-region', true);
    const button = element(content);
    // Geometry: the region shows the band 100..500, the control sits at 900 — only reachable by
    // scrolling the region, so focusing it would pull the dialog start out of view.
    content.getBoundingClientRect = () => ({ top: 100, bottom: 500 });
    button.getBoundingClientRect = () => ({ top: 900, bottom: 940 });
    dialog.querySelectorAll = () => [content, button];

    installDom(body);
    activate(dialog, 'scope', null, false, null, true);

    assert.equal(content.focusCalls, 1, 'the named region keeps the dialog start in view');
    assert.equal(button.focusCalls, 0, 'the below-fold control never takes initial focus');
});

test('a first control inside a scroll region that is already visible keeps the focus', () => {
    const body = element();
    const dialog = element(body);
    const content = element(dialog);
    content.toggleAttribute('tabindex', true);
    content.toggleAttribute('data-tm-scroll-region', true);
    const button = element(content);
    content.getBoundingClientRect = () => ({ top: 100, bottom: 500 });
    button.getBoundingClientRect = () => ({ top: 120, bottom: 160 });
    dialog.querySelectorAll = () => [content, button];

    installDom(body);
    activate(dialog, 'scope', null, false, null, true);

    assert.equal(button.focusCalls, 1, 'a visible control still wins over the region');
    assert.equal(content.focusCalls, 0);
});

test('restoreTrapFocusIfLost skips the scroll region in its fallback too', () => {
    const body = element();
    const dialog = element(body);
    const content = element(dialog);
    content.toggleAttribute('tabindex', true);
    content.toggleAttribute('data-tm-scroll-region', true);
    const button = element(dialog);
    dialog.querySelectorAll = () => [content, button];

    installDom(body);
    activate(dialog, 'dialog', null, false, null, true);
    // The element the trap last focused left the DOM with a re-render, so the recovery resolves a
    // fresh target from the trap's content: the marked region is skipped, the control is not.
    const temporary = element(dialog);
    dialog.dispatch('focusin', { target: temporary });
    temporary.isConnected = false;

    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    dismiss.isConnected = false;
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), true);
    assert.equal(button.focusCalls, 2, 'the fallback lands on the first control, not on the scroll region');
    assert.equal(content.focusCalls, 0, 'the scroll region never takes focus on a recovery either');
});

// ── Scroll region keeps itself current (cluster-B 8.3) ─────────────────────────
// The ResizeObserver alone only fires when the SCROLLER's box changes. Content growing inside a
// max-height-capped scroller (rows appended, a late title rendered, text edited) never resizes the
// scroller, so the region must also watch mutations and re-decide.

test('a mutation inside the scroller re-decides the region', () => {
    let overflows = false;
    const attrs = new Map();
    const content = {
        get scrollHeight() { return 900; },
        get clientHeight() { return overflows ? 400 : 900; },
        children: [],
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        hasAttribute(name) { return attrs.has(name); },
    };
    const mutationCallbacks = [];
    const observeCalls = [];
    globalThis.MutationObserver = class {
        constructor(callback) { mutationCallbacks.push(callback); }
        observe(target, options) { observeCalls.push({ target, options }); }
        disconnect() {}
    };

    try {
        syncScrollRegion(content, 'mutate', 'title-8');
        assert.equal(mutationCallbacks.length, 1, 'a mutation observer watches the scroller');
        assert.deepEqual(observeCalls[0].options, { childList: true },
            'the mutation watch stays SHALLOW — childList only, never subtree/characterData');
        assert.equal(observeCalls[0].target, content, 'the scroller itself is the observed node');
        assert.equal(content.getAttribute('tabindex'), null, 'initially short: no tab stop');

        overflows = true;               // rows appended inside the capped scroller
        mutationCallbacks[0]();
        assert.equal(content.getAttribute('tabindex'), '0',
            'content growing into the overflow must re-decide the region');
    } finally {
        delete globalThis.MutationObserver;
    }
    stopScrollRegion('mutate');
});

test('a childList record re-observes the scroller and its children; other records do not', () => {
    const attrs = new Map();
    const child = {};
    const content = {
        scrollHeight: 900,
        clientHeight: 400,
        children: [child],
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        hasAttribute(name) { return attrs.has(name); },
    };
    let mutationCallback = null;
    globalThis.MutationObserver = class {
        constructor(callback) { mutationCallback = callback; }
        observe() {}
        disconnect() {}
    };
    const observed = [];
    globalThis.ResizeObserver = class {
        constructor() {}
        observe(target) { observed.push(target); }
        disconnect() { observed.push('disconnect'); }
    };

    try {
        syncScrollRegion(content, 'reobserve', 'title-12');
        // reobserveChildren disconnects before (re)observing, so the initial call records one too.
        assert.deepEqual(observed, ['disconnect', content, child],
            'initially the scroller and its children are observed');

        mutationCallback([{ type: 'characterData' }]);
        assert.equal(observed.length, 3, 'a non-childList record does not re-observe');

        mutationCallback([{ type: 'childList' }, { type: 'childList' }]);
        assert.deepEqual(observed, ['disconnect', content, child, 'disconnect', content, child],
            'a childList record re-observes the scroller with its (added) children, once per callback');
    } finally {
        delete globalThis.MutationObserver;
        delete globalThis.ResizeObserver;
    }
    stopScrollRegion('reobserve');
});

test('mutation bursts coalesce into one overflow decision per frame', () => {
    let overflows = false;
    const attrs = new Map();
    const content = {
        get scrollHeight() { return 900; },
        get clientHeight() { return overflows ? 400 : 900; },
        children: [],
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        hasAttribute(name) { return attrs.has(name); },
    };
    let mutationCallback = null;
    const frames = [];
    globalThis.MutationObserver = class {
        constructor(callback) { mutationCallback = callback; }
        observe() {}
        disconnect() {}
    };
    globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };

    try {
        syncScrollRegion(content, 'coalesce', 'title-11');
        assert.equal(content.getAttribute('tabindex'), null, 'initially short: no tab stop');

        overflows = true;
        mutationCallback([]);           // a framework diff touching many nodes…
        mutationCallback([]);           // …three bursts inside the same frame…
        mutationCallback([]);           // …must still decide only once
        assert.equal(frames.length, 1, 'one pending frame carries the whole burst');

        frames[0]();
        assert.equal(content.getAttribute('tabindex'), '0', 'the coalesced decision ran with the frame');

        const before = frames.length;
        mutationCallback([]);
        assert.equal(frames.length, before + 1, 'a burst after the frame ran schedules a fresh frame');
    } finally {
        delete globalThis.MutationObserver;
        delete globalThis.requestAnimationFrame;
    }
    stopScrollRegion('coalesce');
});

test('stopScrollRegion cancels the pending coalesced decision', () => {
    const attrs = new Map();
    const content = {
        scrollHeight: 900,
        clientHeight: 400,
        children: [],
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        hasAttribute(name) { return attrs.has(name); },
    };
    let mutationCallback = null;
    const frames = [];
    const cancelled = [];
    globalThis.MutationObserver = class {
        constructor(callback) { mutationCallback = callback; }
        observe() {}
        disconnect() {}
    };
    globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
    globalThis.cancelAnimationFrame = (handle) => { cancelled.push(handle); };

    try {
        syncScrollRegion(content, 'cancel-apply', 'title-13');
        assert.equal(content.getAttribute('tabindex'), '0');
        mutationCallback([{ type: 'childList' }]);      // schedules the coalesced decision
        assert.equal(frames.length, 1, 'the decision is pending');
        stopScrollRegion('cancel-apply');
        assert.deepEqual(cancelled, [1], 'the pending frame is cancelled with the region');
        frames[0]();                                     // a runner that fires the frame anyway
        assert.equal(content.getAttribute('tabindex'), null,
            'the cancelled decision never re-applies the attributes');
    } finally {
        delete globalThis.MutationObserver;
        delete globalThis.requestAnimationFrame;
        delete globalThis.cancelAnimationFrame;
    }
});

test('stopScrollRegion disconnects the mutation observer', () => {
    const content = scroller(900, 400);
    let disconnects = 0;
    globalThis.MutationObserver = class {
        constructor() {}
        observe() {}
        disconnect() { disconnects++; }
    };

    try {
        syncScrollRegion(content, 'mutate-stop', 'title-10');
        stopScrollRegion('mutate-stop');
        assert.equal(disconnects, 1, 'stopping must disconnect the mutation observer with the resize observer');
    } finally {
        delete globalThis.MutationObserver;
    }
});

test('updateScrollRegionLabel rewires the region name after attach', async () => {
    const { updateScrollRegionLabel } = await import('../tm-focus-trap.js');
    const content = scroller(900, 400);
    syncScrollRegion(content, 'label-update', 'title-1');
    assert.equal(content.getAttribute('aria-labelledby'), 'title-1');

    updateScrollRegionLabel('label-update', 'title-2', null);
    assert.equal(content.getAttribute('aria-labelledby'), 'title-2',
        'a title that renders (or changes) after the attach re-points the region name');

    updateScrollRegionLabel('label-update', null, null);
    assert.equal(content.getAttribute('aria-labelledby'), null, 'a removed title drops the idref');
    assert.equal(content.getAttribute('role'), null, 'an unnamed region drops role with the name');
    assert.equal(content.getAttribute('tabindex'), '0', 'the region itself is unaffected');

    stopScrollRegion('label-update');
    updateScrollRegionLabel('label-update', 'gone', null);
    assert.equal(content.getAttribute('tabindex'), null,
        'an unknown id is a no-op — the stopped region stays stopped');
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

// ── F6 follow-up f4c27b20 #1: Tab cycles over TAB STOPS only ──────────────────
// A roving menu/toolbar item carries tabindex="-1": it is reachable by the arrows, never by Tab.
// The trap's cycle list counted every focusable, so the last entry was a -1 item: Tab on the real
// last tab stop was "not last", the browser's native Tab skipped the -1 items, found nothing
// tabbable left (the page is inert) and focus dropped to <body> — out of the sheet.

function menuSheet() {
    // A bottom-sheet menu shape: the header Done button plus three roving menu items.
    const body = element();
    const sheet = element(body);
    const done = element(sheet);
    const item0 = element(sheet);
    const item1 = element(sheet);
    const item2 = element(sheet);
    // done is a plain button (no tabindex attribute); the roving items carry 0 / -1 / -1.
    item0.getAttribute = n => (n === 'tabindex' ? '0' : null);
    item1.getAttribute = n => (n === 'tabindex' ? '-1' : null);
    item2.getAttribute = n => (n === 'tabindex' ? '-1' : null);
    sheet.querySelectorAll = () => [done, item0, item1, item2];
    return { body, sheet, done, item0, item1, item2 };
}

test('Tab from the last TAB STOP wraps to the first: tabindex=-1 roving items are not tab stops', () => {
    const { body, sheet, done, item0 } = menuSheet();
    installDom(body);
    activate(sheet, 'menu-sheet', null, false, null, true);

    // Initial focus moved to the first focusable (Done); reset so the assertions below see only
    // what the Tab handling does. Focus sits on the roving group's only tab stop.
    done.focusCalls = 0;
    document.activeElement = item0;

    const tab = { key: 'Tab', shiftKey: false, preventDefault() { tab.prevented = true; } };
    sheet.dispatch('keydown', tab);

    assert.equal(tab.prevented, true, 'Tab on the last TAB STOP must cycle inside the sheet');
    assert.equal(done.focusCalls, 1, 'the cycle wraps to the first TAB STOP, not into a -1 item');
});

test('Shift+Tab from the first tab stop wraps to the last TAB STOP, not to a -1 item', () => {
    const { body, sheet, done, item0, item1, item2 } = menuSheet();
    installDom(body);
    activate(sheet, 'menu-sheet', null, false, null, true);

    done.focusCalls = 0;
    item1.focusCalls = 0;
    item2.focusCalls = 0;
    document.activeElement = done;

    const shiftTab = { key: 'Tab', shiftKey: true, preventDefault() { shiftTab.prevented = true; } };
    sheet.dispatch('keydown', shiftTab);

    assert.equal(shiftTab.prevented, true);
    assert.equal(item0.focusCalls, 1, 'the wrap lands on the roving tab stop (the last TAB STOP)');
    assert.equal(item1.focusCalls, 0, 'a -1 item is never the wrap target');
    assert.equal(item2.focusCalls, 0, 'a -1 item is never the wrap target');
});

test('Tab from a focused -1 item moves to the next tab stop in DOM order', () => {
    // Hardening: focus can sit on a -1 item before the menu's focusin re-roved the tab stop
    // (focus moved by the arrows while the keydown races the re-render). Tab must preventDefault
    // (the browser would jump to <body>) and walk the next TAB STOP after the focused item.
    const body = element();
    const sheet = element(body);
    const first = element(sheet);
    const neg = element(sheet);
    const second = element(sheet);
    first.getAttribute = n => (n === 'tabindex' ? '0' : null);
    neg.getAttribute = n => (n === 'tabindex' ? '-1' : null);
    second.getAttribute = n => (n === 'tabindex' ? '0' : null);
    sheet.querySelectorAll = () => [first, neg, second];
    // Node.DOCUMENT_POSITION_PRECEDING = 2, FOLLOWING = 4: neg sits between first and second.
    neg.compareDocumentPosition = (other) => (other === second ? 4 : 2);
    installDom(body);
    activate(sheet, 'menu-sheet', null, false, null, true);

    first.focusCalls = 0;
    second.focusCalls = 0;
    document.activeElement = neg;

    const tab = { key: 'Tab', shiftKey: false, preventDefault() { tab.prevented = true; } };
    sheet.dispatch('keydown', tab);

    assert.equal(tab.prevented, true, 'Tab from a -1 item must be handled, or focus escapes to <body>');
    assert.equal(second.focusCalls, 1, 'the next tab stop AFTER the focused item takes focus');
    assert.equal(first.focusCalls, 0);
});

test('an invalid tabindex value is still a tab stop', () => {
    // Review fix: `Number(ti) >= 0` excluded a NON-NUMERIC tabindex ("auto", garbage) from the
    // cycle — Number('auto') is NaN and NaN >= 0 is false. HTML falls back to 0 for an invalid
    // value: it is a tab stop. Only a parsed NEGATIVE integer is excluded.
    const body = element();
    const sheet = element(body);
    const done = element(sheet);
    const weird = element(sheet);
    weird.getAttribute = n => (n === 'tabindex' ? 'auto' : null);
    sheet.querySelectorAll = () => [done, weird];
    installDom(body);
    activate(sheet, 'invalid-ti', null, false, null, true);

    done.focusCalls = 0;
    document.activeElement = done;
    const shiftTab = { key: 'Tab', shiftKey: true, preventDefault() { shiftTab.prevented = true; } };
    sheet.dispatch('keydown', shiftTab);

    assert.equal(shiftTab.prevented, true);
    assert.equal(weird.focusCalls, 1, 'Shift+Tab wraps to the LAST tab stop — the invalid value is one');
});

test('Tab skips unchecked radios of a same-name group that has a checked member', () => {
    // Review (non-blocking): a radio group with a checked member is ONE tab stop — the checked
    // radio — and the arrows move within it (APG). The cycle must not land on the unchecked ones.
    const body = element();
    const form = element(body);
    const done = element(form);
    const checked = element(form);
    const uncheckedA = element(form);
    const uncheckedB = element(form);
    const after = element(form);
    const radio = (el) => {
        el.getAttribute = n => (n === 'type' ? 'radio' : n === 'name' ? 'color' : null);
    };
    radio(checked);
    radio(uncheckedA);
    radio(uncheckedB);
    checked.checked = true;
    // DOCUMENT_POSITION_PRECEDING = 2 / FOLLOWING = 4: uncheckedA sits between the checked radio
    // and uncheckedB.
    uncheckedA.compareDocumentPosition = (other) => (other === done || other === checked ? 2 : 4);
    form.querySelectorAll = () => [done, checked, uncheckedA, uncheckedB, after];
    installDom(body);
    activate(form, 'radio-form', null, false, null, true);

    done.focusCalls = 0;
    checked.focusCalls = 0;
    after.focusCalls = 0;

    // The user clicked onto an UNCHECKED radio (a click places focus there). It is not a tab stop
    // of the group, so Shift+Tab walks the previous TAB STOP in DOM order — the checked member —
    // never another unchecked one.
    document.activeElement = uncheckedA;
    const shiftTab = { key: 'Tab', shiftKey: true, preventDefault() { shiftTab.prevented = true; } };
    form.dispatch('keydown', shiftTab);
    assert.equal(shiftTab.prevented, true);
    assert.equal(checked.focusCalls, 1, "the checked member is the group's only tab stop");
    assert.equal(uncheckedA.focusCalls, 0, 'an unchecked radio of a checked group is skipped');
    assert.equal(uncheckedB.focusCalls, 0, 'an unchecked radio of a checked group is skipped');

    // Tab from the control after the group wraps to the first stop — the cycle list excluded the
    // unchecked radios, so nothing can land on them.
    document.activeElement = after;
    const tab = { key: 'Tab', shiftKey: false, preventDefault() { tab.prevented = true; } };
    form.dispatch('keydown', tab);
    assert.equal(tab.prevented, true);
    assert.equal(done.focusCalls, 1, 'the cycle wraps to the first tab stop');
    assert.equal(uncheckedA.focusCalls, 0);
    assert.equal(uncheckedB.focusCalls, 0);
});

test('Tab from a focused unchecked radio walks to the next tab stop after it', () => {
    // Focus CAN sit on an unchecked radio (a click places it there). It is not in the cycle
    // list, so Tab walks the next TAB STOP in DOM order — the control after the group.
    const body = element();
    const form = element(body);
    const done = element(form);
    const checked = element(form);
    const unchecked = element(form);
    const after = element(form);
    const radio = (el) => {
        el.getAttribute = n => (n === 'type' ? 'radio' : n === 'name' ? 'color' : null);
    };
    radio(checked);
    radio(unchecked);
    checked.checked = true;
    unchecked.compareDocumentPosition = (other) => (other === after ? 4 : 2);
    form.querySelectorAll = () => [done, checked, unchecked, after];
    installDom(body);
    activate(form, 'radio-form-2', null, false, null, true);

    done.focusCalls = 0;
    after.focusCalls = 0;
    document.activeElement = unchecked;

    const tab = { key: 'Tab', shiftKey: false, preventDefault() { tab.prevented = true; } };
    form.dispatch('keydown', tab);

    assert.equal(tab.prevented, true, 'Tab must be handled, or the browser lands on <body>');
    assert.equal(after.focusCalls, 1, 'the next tab stop after the unchecked radio takes focus');
    assert.equal(done.focusCalls, 0);
});

test('Tab lands on the radio that IS checked now, not the one with a stale checked attribute', () => {
    // Review fix: the checked ATTRIBUTE only records the input's INITIAL state — after the user
    // picks another radio the attribute on the first input lies, and only the live el.checked
    // property tells which member is the group's one tab stop.
    const body = element();
    const form = element(body);
    const done = element(form);
    const stale = element(form);
    const fresh = element(form);
    const radio = (el) => {
        el.getAttribute = n => (n === 'type' ? 'radio' : n === 'name' ? 'color' : null);
    };
    radio(stale);
    radio(fresh);
    // The first input still carries the initial checked ATTRIBUTE, but the user has since
    // selected the second one: the live property is the truth.
    stale.toggleAttribute('checked', true);
    stale.checked = false;
    fresh.checked = true;
    form.querySelectorAll = () => [done, stale, fresh];
    installDom(body);
    activate(form, 'radio-live', null, false, null, true);

    // Shift+Tab from the first control wraps to the group's ONE tab stop — the radio that is
    // checked NOW.
    done.focusCalls = 0;
    stale.focusCalls = 0;
    document.activeElement = done;
    const shiftTab = { key: 'Tab', shiftKey: true, preventDefault() { shiftTab.prevented = true; } };
    form.dispatch('keydown', shiftTab);

    assert.equal(shiftTab.prevented, true);
    assert.equal(fresh.focusCalls, 1, "the radio that is checked NOW is the group's only tab stop");
    assert.equal(stale.focusCalls, 0, 'a stale checked attribute must not make the first input the stop');
});

// ── F6 follow-up 08a22db5 #2: a modal trap's Escape gate skips non-modal traps ─
// isTopmost counts every trap: a non-modal inline sheet activated AFTER a dialog made the dialog
// not-topmost, so Escape closed NOTHING — the dialog was stuck until the sheet closed.

test('a non-modal trap activated AFTER an open modal does not block the modal\'s Escape', () => {
    const body = element();
    const dialog = element(body);
    const insideDialog = element(dialog);
    const sheet = element(body);
    installDom(body);
    const dialogEscape = handler();
    const sheetEscape = handler();

    activate(dialog, 'dialog', dialogEscape, true, null, true);
    activate(sheet, 'sheet', sheetEscape, true, null, false);
    document.activeElement = insideDialog;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(dialogEscape.calls, ['HandleFocusTrapEscapeAsync'],
        'the modal owns Escape even though a non-modal trap registered later');
    assert.deepEqual(sheetEscape.calls, [], 'the later non-modal sheet must not close on the same key');
    assert.equal(modalOwnsEscape('dialog'), true, 'modalOwnsEscape reports the modal ownership directly');
});

test('an inline sheet nested in a modal dialog that holds focus closes alone', () => {
    const body = element();
    const dialog = element(body);
    const sheet = element(dialog);
    const insideSheet = element(sheet);
    installDom(body);
    const dialogEscape = handler();
    const sheetEscape = handler();

    activate(dialog, 'dialog', dialogEscape, true, null, true);
    activate(sheet, 'sheet', sheetEscape, true, null, false);
    document.activeElement = insideSheet;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(sheetEscape.calls, ['HandleFocusTrapEscapeAsync'], 'the nested sheet holding focus closes first');
    assert.deepEqual(dialogEscape.calls, [], 'the dialog must not close on the same Escape');
    assert.equal(modalOwnsEscape('dialog'), false, 'the dialog does not own Escape while its nested sheet holds focus');
});

test('two modals plus a later non-modal: only the second modal closes', () => {
    const body = element();
    const dialog1 = element(body);
    const dialog2 = element(body);
    const insideDialog2 = element(dialog2);
    const sheet = element(body);
    installDom(body);
    const firstEscape = handler();
    const secondEscape = handler();
    const sheetEscape = handler();

    activate(dialog1, 'd1', firstEscape, true, null, true);
    activate(dialog2, 'd2', secondEscape, true, null, true);
    activate(sheet, 'sheet', sheetEscape, true, null, false);
    document.activeElement = insideDialog2;

    document.dispatch('keydown', escapeEvent());

    assert.deepEqual(secondEscape.calls, ['HandleFocusTrapEscapeAsync'], 'the topmost MODAL closes');
    assert.deepEqual(firstEscape.calls, [], 'the modal below it stays open');
    assert.deepEqual(sheetEscape.calls, [], 'the non-modal sheet that does not hold focus stays open');
});

test('modalOwnsEscape is false for an unknown id', () => {
    const body = element();
    const dialog = element(body);
    installDom(body);
    activate(dialog, 'dialog', null, false, null, true);

    assert.equal(modalOwnsEscape('nope'), false);
});

test('a later non-modal trap that CANNOT close on Escape does not block the modal\'s', () => {
    // Review fix: the modal gate asked ownsEscape() of every later non-modal trap — one that
    // holds focus but registered WITHOUT an Escape handler (closeOnEscape=false) would still
    // deadlock the dialog forever, because nothing can ever close it. Only a later trap that
    // CAN close (it has an escHandler) and currently holds focus blocks the modal.
    const body = element();
    const dialog = element(body);
    const insideDialog = element(dialog);
    const sheet = element(body);
    const insideSheet = element(sheet);
    installDom(body);
    const dialogEscape = handler();

    activate(dialog, 'dialog', dialogEscape, true, null, true);
    activate(sheet, 'sheet', null, false, null, false);
    document.activeElement = insideSheet;

    assert.equal(modalOwnsEscape('dialog'), true,
        'a focus-holding sheet with no escHandler must not deadlock the dialog');
    document.dispatch('keydown', escapeEvent());
    assert.deepEqual(dialogEscape.calls, ['HandleFocusTrapEscapeAsync']);
});

// ── F6 follow-up c51ddf81 #4 + review: a toast dismissed under a modal trap recovers lost focus ──
// The toast container is inert-exempt status UI on the top layer: its dismiss button can take
// focus while a sheet/dialog is open, and dismissing removes the button — focus fell to <body>
// and the next Tab landed on the sheet's drag handle. The trap remembers the last element
// focused INSIDE it and can hand focus back when the toast asks. REVIEW: a timer expiry must
// move focus only when a toast actually HELD it — a capture-phase document focusin listener
// records the last focus inside a [data-tm-inert-exempt] element, and the recovery acts only
// while that element is GONE (screen-reader browse mode and mouse clicks leave focus where it
// was; moving focus on a timed event violates WCAG 3.2.1 / APG).

test('restoreTrapFocusIfLost returns focus to the element last focused inside the topmost modal trap', () => {
    const body = element();
    const dialog = element(body);
    // The first focusable is deliberately NOT the element the user was on: the fallback must not
    // mask a broken lastFocused record.
    const other = element(dialog);
    const input = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [other, input];

    activate(dialog, 'dialog', null, false, null, true);
    // The initial move landed on `other`; the user then moved to the input (a real browser fires
    // focusin for both moves).
    dialog.dispatch('focusin', { target: input });

    // The user then Tabbed to a toast's dismiss button (the container is inert-exempt) and the
    // toast was dismissed: the button left the DOM and focus fell to the body.
    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    dismiss.isConnected = false;
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), true);
    assert.equal(input.focusCalls, 1, 'focus returns to the element last focused inside the trap');
    assert.equal(other.focusCalls, 1, 'the fallback (first focusable) ran only as the initial move');
});

test('restoreTrapFocusIfLost does not move focus on a timed removal when no toast held focus', () => {
    // (a) Body focused, lastFocused set, NO exempt focus: the expiry ask is a no-op. Focus that
    // never entered a toast (screen-reader browse mode, a mouse click, plain Tab order) must not
    // be yanked anywhere by a timed event.
    const body = element();
    const dialog = element(body);
    const other = element(dialog);
    const input = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [other, input];

    activate(dialog, 'dialog', null, false, null, true);
    dialog.dispatch('focusin', { target: input });
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), false, 'no inert-exempt focus was recorded — nothing to recover');
    assert.equal(other.focusCalls, 1, 'the initial move only — the timed removal must not move focus');
    assert.equal(input.focusCalls, 0, 'no recovery, nothing else moves');
});

test('restoreTrapFocusIfLost recovers when the toast that held focus was removed', () => {
    // (b) Focus WAS in a toast which got removed: the recorded exempt element is gone and the
    // focus fell to the body — the trap hands focus back inside.
    const body = element();
    const dialog = element(body);
    const input = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [input];

    activate(dialog, 'dialog', null, false, null, true);
    dialog.dispatch('focusin', { target: input });

    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    dismiss.isConnected = false;
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), true);
    assert.equal(input.focusCalls, 2, 'focus returns inside the trap after the toast took it away');
});

test('restoreTrapFocusIfLost never steals focus from a live element', () => {
    const body = element();
    const dialog = element(body);
    const input = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [input];

    activate(dialog, 'dialog', null, false, null, true);
    dialog.dispatch('focusin', { target: input });

    const live = element(body);
    document.activeElement = live;

    assert.equal(restoreTrapFocusIfLost(), false);
    assert.equal(input.focusCalls, 1, 'no extra focus move — the live focus is not stolen');
});

test('restoreTrapFocusIfLost skips the recovery while the exempt element is still connected', () => {
    // The toast card is still in the DOM (a different card expired): the recorded exempt focus
    // is live, so nothing is lost and nothing may move.
    const body = element();
    const dialog = element(body);
    const input = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [input];

    activate(dialog, 'dialog', null, false, null, true);
    dialog.dispatch('focusin', { target: input });

    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), false);
    assert.equal(input.focusCalls, 1);
});

test('restoreTrapFocusIfLost falls back to the first focusable when the last-focused element left the DOM', () => {
    const body = element();
    const dialog = element(body);
    const gone = element(dialog);
    const live = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [gone, live];

    activate(dialog, 'dialog', null, false, null, true);
    dialog.dispatch('focusin', { target: gone });
    // The user reached a toast's dismiss button; dismissing the toast removed it (the recorded
    // exempt focus), and the element the trap last focused was already gone too.
    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    gone.isConnected = false;
    dismiss.isConnected = false;
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), true);
    assert.equal(live.focusCalls, 1, 'focus lands on the first focusable that accepts it');
});

test('restoreTrapFocusIfLost prefers TAB STOPS in the fallback and only then any focusable', () => {
    // Review fix: the fallback used to iterate visibleFocusable, whose FIRST entry can be a
    // roving tabindex="-1" item — focus belongs on the first TAB STOP when one accepts it.
    const body = element();
    const dialog = element(body);
    const rejecting = element(dialog);
    const roving = element(dialog);
    rejecting.getAttribute = () => null;
    roving.getAttribute = n => (n === 'tabindex' ? '-1' : null);
    installDom(body);
    dialog.querySelectorAll = () => [rejecting, roving];

    activate(dialog, 'dialog', null, false, null, true);
    assert.equal(rejecting.focusCalls, 1, 'the initial move landed on the first tab stop');
    // From now on the first tab stop refuses focus (a real browser drops a focus() on a hidden
    // control instead of throwing — modelled as a throwing focus()).
    rejecting.focus = () => { rejecting.focusCalls++; throw new Error('unfocusable'); };
    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    dismiss.isConnected = false;
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), true);
    assert.equal(rejecting.focusCalls, 2, 'the first tab stop is tried first and refuses focus');
    assert.equal(roving.focusCalls, 1, 'the -1 item only takes over when no tab stop accepts focus');
});

test('restoreTrapFocusIfLost is a no-op without a modal trap', () => {
    const body = element();
    const sheet = element(body);
    const inside = element(sheet);
    installDom(body);
    sheet.querySelectorAll = () => [inside];

    activate(sheet, 'sheet', null, false, null, false);
    sheet.dispatch('focusin', { target: inside });
    // Even with a removed toast that held focus there is no MODAL trap to recover for.
    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    dismiss.isConnected = false;
    document.activeElement = body;

    assert.equal(restoreTrapFocusIfLost(), false, 'a non-modal trap alone never reclaims focus');
    assert.equal(inside.focusCalls, 0);
});

test('a pointerdown outside the exempt surfaces clears the recorded exempt focus', () => {
    // The user focused a toast's dismiss button, then clicked a NON-focusable area of the sheet:
    // focus fell to <body> and NO focusin followed (the body is not a focus target the focusin
    // record sees). The stale record would later sell that plain click as "a toast removed while
    // it held focus" and yank focus on the toast's timer — the pointerdown that landed OUTSIDE
    // the exempt surfaces must invalidate the record first.
    const body = element();
    const dialog = element(body);
    const input = element(dialog);
    installDom(body);
    dialog.querySelectorAll = () => [input];

    activate(dialog, 'dialog', null, false, null, true);
    dialog.dispatch('focusin', { target: input });

    const { dismiss } = toastCard(body);
    document.dispatch('focusin', { target: dismiss });
    // The click on the sheet body: only the pointerdown is delivered — focus dropped to <body>
    // without a focusin.
    document.dispatch('pointerdown', { target: dialog });
    document.activeElement = body;
    // Only afterwards the toast is removed (its own timer, not the click, dismissed it).
    dismiss.isConnected = false;

    assert.equal(restoreTrapFocusIfLost(), false,
        'the click already explained the lost focus — the timed removal must not move it');
    assert.equal(input.focusCalls, 1, 'the initial move only — the stale record must not recover');
});