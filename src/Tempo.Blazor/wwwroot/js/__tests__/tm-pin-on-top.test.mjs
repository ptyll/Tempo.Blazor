// Node tests of tm-pin-on-top.js — the public pin-on-top front door (plan 4ce1b994, decision
// TOP-LAYER-PIN, docs/top-layer-pin.md §4.1–§4.5): the [data-tm-pin-on-top] scan, visibility as
// the signal, the module-owned popover attribute, the observer without a feedback loop, the native
// modal <dialog> deferral, the InteractiveAuto element swap and the no-Popover-API no-op.
import test from 'node:test';
import assert from 'node:assert/strict';
import * as pins from '../tm-pin-on-top.js';
import { __resetForTests as resetRegistry } from '../tm-top-layer.js';

// ── A small fake DOM ─────────────────────────────────────────────────────────

function createDom() {
    const elements = [];
    const listeners = [];
    const paint = [];
    let observer = null;
    const frames = [];

    const doc = {
        readyState: 'complete',
        modalOpen: false,
        documentElement: { nodeName: 'HTML' },
        querySelectorAll(selector) {
            if (selector !== '[data-tm-pin-on-top]') throw new Error(`unexpected selector ${selector}`);
            return elements.filter(el => el.isConnected && el.hasAttribute('data-tm-pin-on-top'));
        },
        querySelector(selector) {
            if (selector === 'dialog:modal') return doc.modalOpen ? { tagName: 'DIALOG' } : null;
            return null;
        },
        addEventListener(type, fn, capture) { listeners.push({ type, fn, capture }); },
        removeEventListener(type, fn) {
            const at = listeners.findIndex(l => l.type === type && l.fn === fn);
            if (at >= 0) listeners.splice(at, 1);
        },
    };

    function record(target, attributeName, nodes = {}) {
        if (!observer) return;
        const filter = observer.options?.attributeFilter;
        if (attributeName && filter && !filter.includes(attributeName)) return;
        observer.queue.push({
            type: attributeName ? 'attributes' : 'childList',
            target,
            attributeName,
            addedNodes: nodes.added ?? [],
            removedNodes: nodes.removed ?? [],
        });
    }

    function element(name, { attrs = {}, display = 'block', popoverApi = true } = {}) {
        const attributes = new Map(Object.entries(attrs));
        const el = {
            name,
            nodeType: 1,
            tagName: 'DIV',
            isConnected: true,
            display,
            _open: false,
            hasAttribute: (n) => attributes.has(n),
            getAttribute: (n) => attributes.has(n) ? attributes.get(n) : null,
            setAttribute(n, v) { attributes.set(n, String(v)); record(el, n); },
            removeAttribute(n) {
                if (!attributes.has(n)) return;
                attributes.delete(n);
                if (n === 'popover') el._open = false;   // removing popover closes it
                record(el, n);
            },
            toggleAttribute(n, force) {
                const on = force ?? !attributes.has(n);
                if (on) el.setAttribute(n, ''); else el.removeAttribute(n);
                return on;
            },
            matches: (selector) => selector === ':popover-open' ? el._open
                : selector === '[data-tm-pin-on-top]' ? attributes.has('data-tm-pin-on-top') : false,
            querySelector: () => null,
            contains: (other) => other === el,
            attributes,
        };
        if (popoverApi) {
            el.showPopover = () => {
                if (!attributes.has('popover')) throw new Error('NotSupportedError: not a popover');
                el._open = true;
                paint.push(`${name}:show`);
            };
            el.hidePopover = () => { el._open = false; paint.push(`${name}:hide`); };
        }
        elements.push(el);
        return el;
    }

    // Computed display honours the UA rule [popover]:not(:popover-open) { display: none }.
    function getComputedStyle(el) {
        const display = el.hasAttribute('popover') && !el._open ? 'none' : el.display;
        return { display };
    }

    class FakeObserver {
        constructor(callback) {
            this.callback = callback;
            this.queue = [];
            this.takeRecordsCalls = 0;
            observer = this;
            FakeObserver.instances.push(this);
        }
        observe(target, options) { this.target = target; this.options = options; }
        disconnect() { this.disconnected = true; }
        takeRecords() { this.takeRecordsCalls++; const out = this.queue; this.queue = []; return out; }
        // The browser delivers the queued records in a microtask; tests deliver them explicitly.
        deliver() {
            if (this.queue.length === 0) return;
            const out = this.queue;
            this.queue = [];
            this.callback(out, this);
        }
    }
    FakeObserver.instances = [];

    const env = {
        document: doc,
        MutationObserver: FakeObserver,
        getComputedStyle,
        requestAnimationFrame: (fn) => { frames.push(fn); return frames.length; },
    };

    const dom = {
        doc, env, element, paint, frames, listeners, FakeObserver,
        documentElement: doc.documentElement,
        get observer() { return observer; },
        mutate(el, change) { change(el); },
        // Deliver queued records, then run every scheduled frame (and any records the frame
        // produced) until quiet; returns how many frames ran.
        flush() {
            let ran = 0;
            for (let guard = 0; guard < 20; guard++) {
                observer?.deliver();
                if (frames.length === 0) break;
                const batch = frames.splice(0);
                for (const fn of batch) { fn(); ran++; }
            }
            return ran;
        },
        fireClose() {
            for (const l of listeners.filter(entry => entry.type === 'close')) l.fn({ type: 'close' });
        },
        detach(el) { el.isConnected = false; record(dom.documentElement, null, { removed: [el] }); },
        attach(el) { el.isConnected = true; record(dom.documentElement, null, { added: [el] }); },
    };
    return dom;
}

function reset() {
    pins.__resetForTests();
    resetRegistry();
    delete globalThis.document;
}

function boot(dom) {
    globalThis.document = dom.doc;   // tm-top-layer.js reads the global document for dialog:modal
    pins.start(dom.env);
}

// ── Scan and visibility ──────────────────────────────────────────────────────

test('start() promotes a visible [data-tm-pin-on-top] element and owns its popover attribute', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        assert.equal(bar.getAttribute('popover'), 'manual');
        assert.equal(bar._open, true);
        assert.equal(bar.hasAttribute('data-tm-inert-exempt'), true,
            'a pinned element must stay reachable under a Tempo modal trap');
    }
    finally { reset(); }
});

test('a hidden element is registered but neither promoted nor given a popover attribute', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' }, display: 'none' });
    const flagged = dom.element('flagged', { attrs: { 'data-tm-pin-on-top': '', hidden: '' } });
    try {
        boot(dom);
        for (const el of [bar, flagged]) {
            assert.equal(el.hasAttribute('popover'), false,
                'a closed popover computes display:none — the attribute would hide the visibility signal');
            assert.equal(el._open, false);
        }
    }
    finally { reset(); }
});

test('showing and hiding through style follows the element, and hiding hands the attribute back', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' }, display: 'none' });
    try {
        boot(dom);
        bar.display = 'block';
        bar.setAttribute('style', 'display: block');   // what blazor.web.js does on an error
        dom.flush();
        assert.equal(bar._open, true);
        assert.equal(bar.getAttribute('popover'), 'manual');

        bar.display = 'none';
        bar.setAttribute('style', 'display: none');
        dom.flush();
        assert.equal(bar._open, false);
        assert.equal(bar.hasAttribute('popover'), false,
            'the hidden element is a plain element again, its own CSS decides how it looks');
    }
    finally { reset(); }
});

test('the hidden attribute and a class change are visibility signals too', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        bar.setAttribute('hidden', '');
        dom.flush();
        assert.equal(bar._open, false);

        bar.removeAttribute('hidden');
        dom.flush();
        assert.equal(bar._open, true);

        bar.display = 'none';
        bar.setAttribute('class', 'is-hidden');
        dom.flush();
        assert.equal(bar._open, false);
    }
    finally { reset(); }
});

// ── Observer discipline ─────────────────────────────────────────────────────

test('the observer watches the document subtree but never the popover or inert-exempt attribute', () => {
    const dom = createDom();
    try {
        boot(dom);
        const { target, options } = dom.observer;
        assert.equal(target, dom.doc.documentElement);
        assert.equal(options.subtree, true);
        assert.equal(options.childList, true);
        assert.equal(options.attributes, true);
        for (const name of ['style', 'class', 'hidden', 'data-tm-pin-on-top', 'data-tm-pin-priority', 'open']) {
            assert.ok(options.attributeFilter.includes(name), `${name} must be observed`);
        }
        assert.equal(options.attributeFilter.includes('popover'), false,
            'the module writes popover itself — observing it would feed every pass back into the next');
        assert.equal(options.attributeFilter.includes('data-tm-inert-exempt'), false);
    }
    finally { reset(); }
});

test('a pass never schedules another pass: own writes are discarded with takeRecords()', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' }, display: 'none' });
    try {
        boot(dom);
        bar.display = 'block';
        bar.setAttribute('style', 'display: block');
        const before = dom.observer.takeRecordsCalls;
        const frames = dom.flush();
        assert.equal(frames, 1, 'one mutation, one pass — the pass\'s own showPopover/attribute writes must not loop');
        assert.ok(dom.observer.takeRecordsCalls > before, 'the pass drops records its own writes queued');
    }
    finally { reset(); }
});

test('many mutations inside one frame coalesce into one pass', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        for (let i = 0; i < 10; i++) bar.setAttribute('class', `tick-${i}`);
        dom.observer.deliver();
        bar.setAttribute('class', 'tick-final');
        assert.equal(dom.flush(), 1);
    }
    finally { reset(); }
});

test('a mutation of an unrelated element schedules no pass', () => {
    const dom = createDom();
    dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    const other = dom.element('other');
    try {
        boot(dom);
        other.setAttribute('class', 'busy');
        assert.equal(dom.flush(), 0, 'a busy page must not pay a style read per class change elsewhere');
    }
    finally { reset(); }
});

test('start() is idempotent: one observer, one close listener', () => {
    const dom = createDom();
    try {
        boot(dom);
        const listeners = dom.listeners.length;
        pins.start(dom.env);
        pins.start(dom.env);
        assert.equal(dom.FakeObserver.instances.length, 1);
        assert.equal(dom.listeners.length, listeners, 'a repeated start() must not add a listener');
        assert.ok(dom.listeners.some(l => l.type === 'close' && l.capture === true),
            'close does not bubble: the module needs a capture listener on document');
    }
    finally { reset(); }
});

// ── Native modal <dialog> ───────────────────────────────────────────────────

test('an element shown while a native modal dialog is open stays plain and is promoted on close', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' }, display: 'none' });
    try {
        boot(dom);
        dom.doc.modalOpen = true;
        bar.display = 'block';
        bar.setAttribute('style', 'display: block');
        dom.flush();
        assert.equal(bar.hasAttribute('popover'), false,
            'everything outside the dialog is inert: a pin above it would cover it while unusable');
        assert.equal(bar._open, false);

        dom.doc.modalOpen = false;
        dom.fireClose();
        dom.flush();
        assert.equal(bar._open, true, 'the pin must not stay lost under the closed dialog');
        assert.equal(bar.getAttribute('popover'), 'manual');
    }
    finally { reset(); }
});

test('the dialog open attribute is the close fallback (dialog.open = false, removal)', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    const dialog = dom.element('dialog');
    dialog.tagName = 'DIALOG';
    try {
        dom.doc.modalOpen = true;
        boot(dom);
        assert.equal(bar._open, false);

        dom.doc.modalOpen = false;
        dialog.removeAttribute('open');   // no close event in this path
        dialog.setAttribute('open', '');
        dialog.removeAttribute('open');
        dom.flush();
        assert.equal(bar._open, true);
    }
    finally { reset(); }
});

// ── Order, swap, explicit mode, API ─────────────────────────────────────────

test('data-tm-pin-priority orders pins: the higher priority paints last (topmost)', () => {
    const dom = createDom();
    const banner = dom.element('banner', { attrs: { 'data-tm-pin-on-top': '', 'data-tm-pin-priority': '100' } });
    const strip = dom.element('strip', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        const last = dom.paint.filter(p => p.endsWith(':show')).at(-1);
        assert.equal(last, 'banner:show');
        assert.equal(strip._open, true);
    }
    finally { reset(); }
});

test('an InteractiveAuto handoff that swaps the element pins the new one and forgets the old', () => {
    const dom = createDom();
    const before = dom.element('ssr-bar', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        assert.equal(before._open, true);

        dom.detach(before);
        const after = dom.element('wasm-bar', { attrs: { 'data-tm-pin-on-top': '' } });
        dom.attach(after);
        dom.flush();
        assert.equal(after._open, true);
        assert.equal(pins.__pinnedCountForTests(), 1, 'the detached element left the registry');
    }
    finally { reset(); }
});

test('removing data-tm-pin-on-top unpins, demotes and hands back module-added attributes', () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        bar.removeAttribute('data-tm-pin-on-top');
        dom.flush();
        assert.equal(bar._open, false);
        assert.equal(bar.hasAttribute('popover'), false);
        assert.equal(bar.hasAttribute('data-tm-inert-exempt'), false);
        assert.equal(pins.__pinnedCountForTests(), 0);
    }
    finally { reset(); }
});

test('markup that already carries popover is explicit: the module never shows, hides or strips it', () => {
    const dom = createDom();
    const own = dom.element('own', { attrs: { 'data-tm-pin-on-top': '', popover: 'manual', 'data-tm-inert-exempt': '' } });
    try {
        boot(dom);
        assert.equal(own._open, false, 'the host owns showPopover for an explicit pin');
        pins.unpin(own);
        assert.equal(own.getAttribute('popover'), 'manual');
        assert.equal(own.hasAttribute('data-tm-inert-exempt'), true, 'an attribute the host wrote stays');
    }
    finally { reset(); }
});

test('pin()/unpin() pin an element without the attribute and release it completely', () => {
    const dom = createDom();
    const card = dom.element('card');
    try {
        boot(dom);
        pins.pin(card, { priority: 3 });
        assert.equal(card._open, true);

        card.display = 'none';
        card.setAttribute('class', 'gone');
        dom.flush();
        assert.equal(card._open, false, 'an API pin follows visibility exactly like an attribute pin');

        card.display = 'block';
        card.setAttribute('class', '');
        dom.flush();
        assert.equal(card._open, true);

        pins.unpin(card);
        assert.equal(card._open, false);
        assert.equal(card.hasAttribute('popover'), false);
        assert.equal(card.hasAttribute('data-tm-inert-exempt'), false);
    }
    finally { reset(); }
});

test('an element pinned by BOTH the API and the attribute stays pinned until both let go', () => {
    const dom = createDom();
    const wrapper = dom.element('wrapper', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        pins.pin(wrapper);
        pins.unpin(wrapper);
        assert.equal(wrapper._open, true, 'the attribute still asks for the pin');
        assert.equal(pins.__pinnedCountForTests(), 1);
    }
    finally { reset(); }
});

test('without the Popover API registering is a no-op: no attribute is added, nothing throws', () => {
    const dom = createDom();
    const old = dom.element('old', { attrs: { 'data-tm-pin-on-top': '' }, popoverApi: false });
    try {
        assert.doesNotThrow(() => boot(dom));
        assert.doesNotThrow(() => pins.pin(old));
        assert.equal(old.hasAttribute('popover'), false);
        assert.equal(old.hasAttribute('data-tm-inert-exempt'), false);
    }
    finally { reset(); }
});

test('pin() and unpin() tolerate null, a non-element and a call before start()', () => {
    assert.doesNotThrow(() => pins.pin(null));
    assert.doesNotThrow(() => pins.unpin(undefined));
    assert.doesNotThrow(() => pins.pin({}));
    assert.doesNotThrow(() => pins.refresh());
    reset();
});

test('a second copy of the module (script tag + IJSRuntime import) shares one observer and one pin set', async () => {
    const dom = createDom();
    const bar = dom.element('bar', { attrs: { 'data-tm-pin-on-top': '' } });
    try {
        boot(dom);
        // A real browser: the second copy auto-starts on import with the page's own globals.
        globalThis.MutationObserver = dom.env.MutationObserver;
        globalThis.getComputedStyle = dom.env.getComputedStyle;
        globalThis.requestAnimationFrame = dom.env.requestAnimationFrame;
        const copy = await import('../tm-pin-on-top.js?second-instance');
        copy.pin(bar, { priority: 5 });
        assert.equal(dom.FakeObserver.instances.length, 1, 'the second copy must not start a second observer');
        assert.equal(copy.__pinnedCountForTests(), 1, 'both copies see the same pinned element');
        copy.unpin(bar);
        assert.equal(bar._open, true, 'the attribute pin the first copy registered still holds');
    }
    finally {
        reset();
        delete globalThis.MutationObserver;
        delete globalThis.getComputedStyle;
        delete globalThis.requestAnimationFrame;
    }
});

// Code review (plan 4ce1b994 phase 2): a priority change of a pin that is ALREADY on screen used
// to wait for the next unrelated promotion. The pass re-ranks it at once.
test('a priority change of a promoted pin re-ranks it immediately', () => {
    const dom = createDom();
    const a = dom.element('a', { attrs: { 'data-tm-pin-on-top': '', 'data-tm-pin-priority': '10' } });
    const b = dom.element('b', { attrs: { 'data-tm-pin-on-top': '', 'data-tm-pin-priority': '5' } });
    try {
        boot(dom);
        assert.equal(dom.paint.filter(p => p.endsWith(':show')).at(-1), 'a:show');
        b.setAttribute('data-tm-pin-priority', '20');
        dom.flush();
        assert.equal(dom.paint.filter(p => p.endsWith(':show')).at(-1), 'b:show',
            'b now outranks a, so b must paint last');
        assert.equal(a._open, true);
        assert.equal(b._open, true);
    }
    finally { reset(); }
});
