// gantt.js pointer work on the shared helpers (touch phase). The real gantt.js, the real
// pointer-drag.js and the real pointer-gesture.js run against a hand-rolled DOM: the timeline pan
// (mouse/pen), the dependency drag (every pointer), the touch long-press on bars and tree rows, the
// keyboard move of the tree and the "no HTML5 drag under a finger" guard.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const ganttUrl = pathToFileURL(path.resolve(here, '../gantt.js')).href;

// ── hand-rolled DOM ─────────────────────────────────────────────────────────────────────────

function bag() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of [...(map.get(type) ?? [])]) fn(event); return event; },
        count(type) { return (map.get(type) ?? []).length; },
        total() { let n = 0; for (const list of map.values()) n += list.length; return n; },
    };
}

const camel = name => name.replace(/^data-/, '').replace(/-([a-z])/g, (_, c) => c.toUpperCase());

/** `tag.class[attr]` / `tag.class[attr="v"]` compounds, comma separated. */
function matchesSelector(node, selector) {
    return selector.split(',').some(raw => {
        const m = /^([a-z][a-z0-9]*)?((?:\.[\w-]+|\[[^\]]+\])*)$/.exec(raw.trim());
        if (!m) return false;
        if (m[1] && node.tag !== m[1]) return false;
        const parts = m[2].match(/\.[\w-]+|\[[^\]]+\]/g) ?? [];
        return parts.every(part => {
            if (part.startsWith('.')) return node.classList.contains(part.slice(1));
            const [name, rawValue] = part.slice(1, -1).split('=');
            const value = node.getAttribute(name) ?? node.dataset[camel(name)] ?? null;
            if (value === null) return false;
            return rawValue === undefined || value === rawValue.replace(/"/g, '');
        });
    });
}

let doc;
let frames;

function el(classes = [], parent = null, rect = { left: 0, top: 0, width: 0, height: 0 }, tag = 'div') {
    const set = new Set(classes);
    const attrs = new Map();
    const node = {
        ...bag(),
        tag, parent, dataset: {}, style: {}, attrs, rect, isConnected: true,
        scrollLeft: 0, scrollTop: 0, children: [],
        captured: new Set(), released: [],
        classList: {
            add: n => set.add(n), remove: n => set.delete(n), contains: n => set.has(n),
            get list() { return [...set]; },
        },
        setAttribute(name, value) {
            attrs.set(name, String(value));
            if (name === 'class') String(value).split(/\s+/).filter(Boolean).forEach(c => set.add(c));
        },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        removeAttribute(name) { attrs.delete(name); },
        getBoundingClientRect: () => node.rect,
        setPointerCapture(id) { node.captured.add(id); },
        releasePointerCapture(id) { node.captured.delete(id); node.released.push(id); },
        appendChild(child) { child.parent = node; node.children.push(child); return child; },
        remove() { if (node.parent) node.parent.children = node.parent.children.filter(c => c !== node); node.parent = null; },
        contains(other) { for (let n = other; n; n = n.parent) if (n === node) return true; return false; },
        closest(selector) {
            for (let n = node; n; n = n.parent) if (matchesSelector(n, selector)) return n;
            return null;
        },
        querySelector(selector) {
            for (const child of node.children) {
                if (matchesSelector(child, selector)) return child;
                const found = child.querySelector(selector);
                if (found) return found;
            }
            return null;
        },
    };
    // Pointer and key events bubble from the target to the document.
    const own = node.dispatch;
    node.dispatch = (type, event = {}) => {
        own(type, event);
        (node.parent ?? doc)?.dispatch(type, event);
        return event;
    };
    if (parent) { parent.children.push(node); }
    return node;
}

/** A Gantt: timeline > body > content (bars, a milestone, empty space) + a separate tree body. */
function gantt() {
    const timeline = el(['tm-gantt__timeline'], null, { left: 0, top: 0, width: 400, height: 300, right: 400, bottom: 300 });
    const body = el(['tm-gantt__timeline-body'], timeline);
    const content = el(['tm-gantt__timeline-content'], body, { left: 100, top: 50, width: 2000, height: 600 });
    const barA = el(['tm-gantt__bar'], content, { left: 150, top: 80, width: 100, height: 20 });
    barA.dataset.taskId = 'a';
    const handle = el(['tm-gantt__dep-handle'], barA, { left: 240, top: 84, width: 12, height: 12 });
    handle.dataset.taskId = 'a';
    handle.dataset.depEnd = 'true';
    const startHandle = el(['tm-gantt__dep-handle'], barA, { left: 148, top: 84, width: 12, height: 12 });
    startHandle.dataset.taskId = 'a';
    startHandle.dataset.depEnd = 'false';
    const barB = el(['tm-gantt__bar'], content, { left: 300, top: 120, width: 100, height: 20 });
    barB.dataset.taskId = 'b';
    const milestone = el(['tm-gantt__milestone'], content, { left: 350, top: 160, width: 16, height: 16 });
    milestone.dataset.taskId = 'm';
    const empty = el(['tm-gantt__grid-row'], content, { left: 100, top: 200, width: 2000, height: 40 });
    const button = el([], barA, undefined, 'button');

    const tree = el(['tm-gantt__tree'], null);
    const treeBody = el(['tm-gantt__tree-body'], tree, { left: 0, top: 0, width: 200, height: 300, right: 200, bottom: 300 });
    const rowA = el(['tm-gantt__tree-row'], treeBody);
    rowA.dataset.taskId = 'a';
    rowA.setAttribute('draggable', 'true');
    const rowButton = el([], rowA, undefined, 'button');
    const rowCell = el(['tm-gantt__tree-cell'], rowA);
    const rowB = el(['tm-gantt__tree-row'], treeBody);
    rowB.dataset.taskId = 'b';
    rowB.setAttribute('draggable', 'true');
    return { timeline, body, content, barA, barB, milestone, handle, startHandle, empty, button, tree, treeBody, rowA, rowB, rowButton, rowCell };
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
}

let tm;

async function boot(src = ganttUrl) {
    frames = [];
    doc = {
        ...bag(), readyState: 'complete', baseURI: 'http://localhost/',
        currentScript: { src },
        contains: () => true,
        createElementNS: (ns, tag) => el([], null, undefined, tag),
        elementFromPoint: () => null,
    };
    globalThis.document = doc;
    globalThis.window = {};
    globalThis.matchMedia = undefined;
    globalThis.requestAnimationFrame = fn => { frames.push(fn); return frames.length; };
    globalThis.cancelAnimationFrame = () => {};
    await import(`../gantt.js?${Math.random()}`);
    tm = globalThis.window.tmGantt;
}

test.beforeEach(() => boot());
test.afterEach(() => {
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; }, ...over,
});

const key = (target, k, over = {}) => ({
    target, key: k, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false,
    preventDefault() { this.defaultPrevented = true; }, ...over,
});

let seq = 0;

async function mount(g = gantt(), ref = dotnet(), id = "g" + (++seq)) {
    g.id = id;
    await tm.attachTimeline(id, g.timeline, g.body, g.treeBody, ref);
    await tm.attachTree(id, g.treeBody, ref);
    return { g, ref };
}

const ghost = g => g.content.querySelector('.tm-gantt__dep-ghost');

// ── contract ────────────────────────────────────────────────────────────────────────────────

test('window.tmGantt keeps every existing function and adds the four pointer ones', () => {
    for (const name of ['getScrollLeft', 'setScrollLeft', 'getScrollTop', 'setScrollTop', 'getClientWidth',
        'linkScroll', 'unlinkScroll', 'noteOpener', 'restoreOpener',
        'attachTimeline', 'detachTimeline', 'attachTree', 'detachTree']) {
        assert.equal(typeof tm[name], 'function', name);
    }
});

test('the API returns promises and tolerates missing elements', async () => {
    assert.ok(tm.attachTimeline(null) instanceof Promise);
    await tm.attachTimeline(null, null, null, null, null);
    await tm.attachTimeline('x', null, null, null, null);
    await tm.attachTree(null, null, null);
    await tm.attachTree('x', null, null);
    await tm.detachTimeline('never-attached');
    await tm.detachTree('never-attached');
    await tm.detachTimeline(null);
    await tm.detachTree(null);
});

// ── pan ─────────────────────────────────────────────────────────────────────────────────────

test('a mouse drag on the empty timeline pans in JS (both scrollers) with zero .NET calls', async () => {
    const { g, ref } = await mount();
    g.timeline.scrollLeft = 300;
    g.body.scrollTop = 80;
    g.treeBody.scrollTop = 80;

    g.timeline.dispatch('pointerdown', ev(g.empty, { clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 150, clientY: 180 }));
    assert.equal(g.timeline.classList.contains('tm-gantt__timeline--panning'), true);
    assert.equal(g.timeline.scrollLeft, 350);
    assert.equal(g.body.scrollTop, 100);
    assert.equal(g.treeBody.scrollTop, 100);

    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 230, clientY: 230 }));
    assert.equal(g.timeline.scrollLeft, 270);
    assert.equal(g.body.scrollTop, 50);
    assert.equal(g.treeBody.scrollTop, 50);

    g.timeline.dispatch('pointerup', ev(g.empty, { clientX: 230, clientY: 230 }));
    assert.equal(g.timeline.classList.contains('tm-gantt__timeline--panning'), false);
    assert.equal(g.timeline.scrollLeft, 270, 'a finished pan keeps the position');
    assert.equal(ref.calls.length, 0, 'no interop per move or per pan');
});

test('a mouse press below the threshold is a tap and moves nothing; a pen never pans (a pen drag is a native scroll under touch-action)', async () => {
    const { g } = await mount();
    g.timeline.dispatch('pointerdown', ev(g.empty, { clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 198, clientY: 200 }));
    assert.equal(g.timeline.scrollLeft, 0);
    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 190, clientY: 200 }));
    assert.equal(g.timeline.scrollLeft, 10);
    g.timeline.dispatch('pointerup', ev(g.empty, { clientX: 190, clientY: 200 }));

    g.timeline.dispatch('pointerdown', ev(g.empty, { pointerType: 'pen', clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { pointerType: 'pen', clientX: 150, clientY: 200 }));
    assert.equal(g.timeline.scrollLeft, 10, 'unchanged: the pen is not a pan');
    g.timeline.dispatch('pointerup', ev(g.empty, { pointerType: 'pen', clientX: 150, clientY: 200 }));
});
test('a press on a bar, a milestone, a dep handle or a button does not pan', async () => {
    const { g } = await mount();
    for (const target of [g.barB, g.milestone, g.button]) {
        g.timeline.dispatch('pointerdown', ev(target, { clientX: 200, clientY: 200 }));
        g.timeline.dispatch('pointermove', ev(target, { clientX: 100, clientY: 100 }));
        g.timeline.dispatch('pointerup', ev(target, { clientX: 100, clientY: 100 }));
        assert.equal(g.timeline.scrollLeft, 0);
        assert.equal(g.timeline.classList.contains('tm-gantt__timeline--panning'), false);
    }
    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 200, clientY: 60 }));
    assert.equal(g.timeline.scrollLeft, 0, 'the dependency drag, not the pan');
    assert.equal(g.timeline.classList.contains('tm-gantt__timeline--panning'), false);
    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 200, clientY: 60 }));
});

test('a finger never pans (it scrolls natively)', async () => {
    const { g } = await mount();
    g.timeline.dispatch('pointerdown', ev(g.empty, { pointerType: 'touch', clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { pointerType: 'touch', clientX: 100, clientY: 100 }));
    assert.equal(g.timeline.scrollLeft, 0);
    assert.equal(g.body.scrollTop, 0);
    assert.equal(g.timeline.classList.contains('tm-gantt__timeline--panning'), false);
    g.timeline.dispatch('pointerup', ev(g.empty, { pointerType: 'touch', clientX: 100, clientY: 100 }));
});

test('Escape and pointercancel restore the scroll the pan started from', async () => {
    const { g, ref } = await mount();
    g.timeline.scrollLeft = 300;
    g.body.scrollTop = 40;
    g.treeBody.scrollTop = 40;

    g.timeline.dispatch('pointerdown', ev(g.empty, { clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 120, clientY: 150 }));
    assert.equal(g.timeline.scrollLeft, 380);
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });
    assert.equal(g.timeline.scrollLeft, 300);
    assert.equal(g.body.scrollTop, 40);
    assert.equal(g.treeBody.scrollTop, 40);
    assert.equal(g.timeline.classList.contains('tm-gantt__timeline--panning'), false);

    g.timeline.dispatch('pointerdown', ev(g.empty, { pointerId: 2, clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { pointerId: 2, clientX: 100, clientY: 160 }));
    assert.equal(g.timeline.scrollLeft, 400);
    g.timeline.dispatch('pointercancel', ev(g.empty, { pointerId: 2 }));
    assert.equal(g.timeline.scrollLeft, 300);
    assert.equal(g.body.scrollTop, 40);
    assert.equal(ref.calls.length, 0);
});

// ── dependency drag ─────────────────────────────────────────────────────────────────────────

test('a dependency drag shows a ghost line, marks the target, and reports ONE completion on release', async () => {
    const { g, ref } = await mount();
    doc.elementFromPoint = () => g.empty;

    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    assert.equal(ghost(g), null, 'no ghost before the threshold');
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 260, clientY: 100 }));

    const svg = ghost(g);
    assert.ok(svg, 'the ghost appears on start');
    assert.equal(svg.tag, 'svg');
    assert.match(svg.getAttribute('style'), /pointer-events:none/);
    assert.match(svg.getAttribute('style'), /z-index:6/);
    const line = svg.children[0];
    assert.equal(line.tag, 'line');
    assert.match(line.getAttribute('style'), /stroke:var\(--tm-color-primary\)/);
    assert.match(line.getAttribute('style'), /stroke-dasharray:4 3/);
    // The origin is the handle centre in content coordinates: (240+6-100, 84+6-50).
    assert.equal(line.getAttribute('x1'), '146');
    assert.equal(line.getAttribute('y1'), '40');
    assert.equal(line.getAttribute('x2'), '160');
    assert.equal(line.getAttribute('y2'), '50');

    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 350, clientY: 130 }));
    assert.equal(line.getAttribute('x2'), '250', 'the line follows the pointer');
    assert.equal(line.getAttribute('y2'), '80');
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), true);

    doc.elementFromPoint = () => g.milestone;
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 355, clientY: 165 }));
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), false, 'removed from the previous target');
    assert.equal(g.milestone.classList.contains('tm-gantt__bar--dep-target'), true);

    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 350, clientY: 130 }));

    assert.deepEqual(ref.calls, [['OnDependencyDragCompleted', 'a', true, 'b']]);
    assert.equal(ghost(g), null, 'the ghost is removed');
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), false);
    assert.equal(g.milestone.classList.contains('tm-gantt__bar--dep-target'), false);
});

test('the start handle reports fromEnd = false', async () => {
    const { g, ref } = await mount();
    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerdown', ev(g.startHandle, { clientX: 154, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.startHandle, { clientX: 300, clientY: 130 }));
    g.timeline.dispatch('pointerup', ev(g.startHandle, { clientX: 300, clientY: 130 }));
    assert.deepEqual(ref.calls, [['OnDependencyDragCompleted', 'a', false, 'b']]);
});

test('releasing on empty space, on the source bar, or with Escape reports nothing and cleans up', async () => {
    const { g, ref } = await mount();
    const start = (id = 1) => {
        g.timeline.dispatch('pointerdown', ev(g.handle, { pointerId: id, clientX: 246, clientY: 90 }));
        g.timeline.dispatch('pointermove', ev(g.handle, { pointerId: id, clientX: 330, clientY: 130 }));
    };

    doc.elementFromPoint = () => g.empty;
    start(1);
    assert.ok(ghost(g));
    g.timeline.dispatch('pointerup', ev(g.handle, { pointerId: 1, clientX: 330, clientY: 130 }));
    assert.equal(ghost(g), null);

    doc.elementFromPoint = () => g.barA;
    start(2);
    assert.equal(g.barA.classList.contains('tm-gantt__bar--dep-target'), false, 'the source is never a target');
    g.timeline.dispatch('pointerup', ev(g.handle, { pointerId: 2, clientX: 330, clientY: 130 }));
    assert.equal(ghost(g), null);

    doc.elementFromPoint = () => g.barB;
    start(3);
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), true);
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });
    assert.equal(ghost(g), null);
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), false);
    g.timeline.dispatch('pointerup', ev(g.handle, { pointerId: 3, clientX: 330, clientY: 130 }));

    start(4);
    g.timeline.dispatch('pointercancel', ev(g.handle, { pointerId: 4 }));
    assert.equal(ghost(g), null);
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), false);

    assert.equal(ref.calls.length, 0);
});

test('a tap on a handle (below the threshold) starts no ghost and reports nothing', async () => {
    const { g, ref } = await mount();
    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 246, clientY: 90 }));
    assert.equal(ghost(g), null);
    assert.equal(ref.calls.length, 0);
});

test('a handle without a task id is vetoed', async () => {
    const { g, ref } = await mount();
    delete g.handle.dataset.taskId;
    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 330, clientY: 130 }));
    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 330, clientY: 130 }));
    assert.equal(ghost(g), null);
    assert.equal(ref.calls.length, 0);
});

test('a FINGER drags a dependency the same way, immediately (no long-press)', async () => {
    const { g, ref } = await mount();
    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerdown', ev(g.handle, { pointerType: 'touch', pointerId: 5, clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { pointerType: 'touch', pointerId: 5, clientX: 330, clientY: 130 }));
    assert.ok(ghost(g));
    g.timeline.dispatch('pointerup', ev(g.handle, { pointerType: 'touch', pointerId: 5, clientX: 330, clientY: 130 }));
    assert.deepEqual(ref.calls, [['OnDependencyDragCompleted', 'a', true, 'b']]);
    assert.equal(ghost(g), null);
});

test('the edge auto-scroll moves the timeline and the ghost origin follows the scrolled handle', async () => {
    const { g, ref } = await mount();
    doc.elementFromPoint = () => g.empty;
    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 399, clientY: 90 })); // inside the right edge band
    const line = ghost(g).children[0];
    assert.equal(line.getAttribute('x1'), '146');
    assert.ok(frames.length > 0, 'the auto-scroller scheduled a frame');

    // The scroll shifts the content (and the handle) left by the applied delta. The drag runs TWO
    // scrollers (the timeline's x band plus the timeline body's y band), so the pump walks the queued
    // rAF callbacks (oldest first) until the x band's frame actually scrolls the timeline — the body's
    // y frame is a no-op in this DOM (its rect has no measurable edges). Bounded: a scroller at a
    // scroll limit reschedules forever without applying anything.
    const advance = () => {
        const before = g.timeline.scrollLeft;
        for (let pumped = 0; pumped < 10 && frames.length > 0; pumped++) {
            frames.shift()();
            const applied = g.timeline.scrollLeft - before;
            if (applied !== 0) {
                g.content.rect = { ...g.content.rect, left: g.content.rect.left - applied };
                g.handle.rect = { ...g.handle.rect, left: g.handle.rect.left - applied };
                return applied;
            }
        }
        return 0;
    };
    const applied = advance();
    assert.ok(applied > 0, 'the timeline scrolled');
    // Both the content and the handle shifted: the origin in content coordinates is unchanged and the end follows.
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 399, clientY: 90 }));
    assert.equal(line.getAttribute('x1'), '146');
    assert.equal(line.getAttribute('x2'), String(399 - g.content.rect.left));

    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 399, clientY: 90 }));
    assert.equal(ghost(g), null);
    assert.equal(ref.calls.length, 0);
});

// ── bar long-press ──────────────────────────────────────────────────────────────────────────

test('a 500ms touch hold on a bar opens the context menu once, while the finger is still down', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g, ref } = await mount();

    g.timeline.dispatch('pointerdown', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 340, clientY: 130 }));
    t.mock.timers.tick(499);
    assert.equal(ref.calls.length, 0);
    t.mock.timers.tick(1);
    assert.deepEqual(ref.calls, [['OnLongPressContextMenu', 'b', 340, 130]]);

    g.timeline.dispatch('pointermove', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 345, clientY: 135 }));
    g.timeline.dispatch('pointerup', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 345, clientY: 135 }));
    assert.equal(ref.calls.length, 1, 'move and release add nothing');
});

test('a milestone long-presses too', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g, ref } = await mount();
    g.timeline.dispatch('pointerdown', ev(g.milestone, { pointerType: 'touch', pointerId: 3, clientX: 358, clientY: 168 }));
    t.mock.timers.tick(500);
    assert.deepEqual(ref.calls, [['OnLongPressContextMenu', 'm', 358, 168]]);
});

test('a swipe before the hold, a mouse press and a pen press never open the menu', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g, ref } = await mount();

    g.timeline.dispatch('pointerdown', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 340, clientY: 130 }));
    g.timeline.dispatch('pointermove', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 380, clientY: 130 }));
    t.mock.timers.tick(600);
    g.timeline.dispatch('pointerup', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 380, clientY: 130 }));

    for (const pointerType of ['mouse', 'pen']) {
        g.timeline.dispatch('pointerdown', ev(g.barB, { pointerType, pointerId: 4, clientX: 340, clientY: 130 }));
        t.mock.timers.tick(600);
        g.timeline.dispatch('pointerup', ev(g.barB, { pointerType, pointerId: 4, clientX: 340, clientY: 130 }));
    }
    assert.equal(ref.calls.length, 0);
});

test('a touch press on a dep handle (inside its bar) is vetoed; the dependency drag owns it', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g, ref } = await mount();
    g.timeline.dispatch('pointerdown', ev(g.handle, { pointerType: 'touch', pointerId: 3, clientX: 246, clientY: 90 }));
    t.mock.timers.tick(700);
    assert.equal(ref.calls.length, 0, 'no context menu from a handle');
    g.timeline.dispatch('pointerup', ev(g.handle, { pointerType: 'touch', pointerId: 3, clientX: 246, clientY: 90 }));
});

test('a bar without a task id has no long-press menu', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g, ref } = await mount();
    delete g.barB.dataset.taskId;
    g.timeline.dispatch('pointerdown', ev(g.barB, { pointerType: 'touch', pointerId: 3, clientX: 340, clientY: 130 }));
    t.mock.timers.tick(700);
    assert.equal(ref.calls.length, 0);
});

// ── tree ────────────────────────────────────────────────────────────────────────────────────

test('a 500ms touch hold on a tree row opens the context menu; a button inside vetoes it', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g, ref } = await mount();

    g.rowB.dispatch('pointerdown', ev(g.rowB, { pointerType: 'touch', pointerId: 3, clientX: 40, clientY: 70 }));
    t.mock.timers.tick(499);
    assert.equal(ref.calls.length, 0);
    t.mock.timers.tick(1);
    assert.deepEqual(ref.calls, [['OnLongPressContextMenu', 'b', 40, 70]]);
    g.rowB.dispatch('pointerup', ev(g.rowB, { pointerType: 'touch', pointerId: 3, clientX: 40, clientY: 70 }));

    g.rowA.dispatch('pointerdown', ev(g.rowButton, { pointerType: 'touch', pointerId: 4, clientX: 10, clientY: 10 }));
    t.mock.timers.tick(700);
    g.rowA.dispatch('pointerup', ev(g.rowButton, { pointerType: 'touch', pointerId: 4, clientX: 10, clientY: 10 }));
    assert.equal(ref.calls.length, 1);

    // A swipe and a mouse press never open it.
    g.rowA.dispatch('pointerdown', ev(g.rowCell, { pointerType: 'touch', pointerId: 5, clientX: 10, clientY: 10 }));
    g.rowA.dispatch('pointermove', ev(g.rowCell, { pointerType: 'touch', pointerId: 5, clientX: 10, clientY: 60 }));
    t.mock.timers.tick(700);
    g.rowA.dispatch('pointerup', ev(g.rowCell, { pointerType: 'touch', pointerId: 5, clientX: 10, clientY: 60 }));
    g.rowA.dispatch('pointerdown', ev(g.rowCell, { pointerId: 6, clientX: 10, clientY: 10 }));
    t.mock.timers.tick(700);
    g.rowA.dispatch('pointerup', ev(g.rowCell, { pointerId: 6, clientX: 10, clientY: 10 }));
    assert.equal(ref.calls.length, 1);
});

test('Alt+ArrowUp/Down on a row reports the move; plain arrows, Alt+Left/Right and Alt+Shift report nothing', async () => {
    const { g, ref } = await mount();

    const up = g.rowB.dispatch('keydown', key(g.rowB, 'ArrowUp', { altKey: true }));
    const down = g.rowA.dispatch('keydown', key(g.rowCell, 'ArrowDown', { altKey: true }));
    assert.deepEqual(ref.calls, [['OnTreeKeyboardMove', 'b', -1], ['OnTreeKeyboardMove', 'a', 1]]);
    assert.equal(up.defaultPrevented, true, 'the helper cancels the Alt chord (browser Back/Forward)');
    assert.equal(down.defaultPrevented, true);

    const plain = g.rowA.dispatch('keydown', key(g.rowA, 'ArrowUp'));
    assert.equal(plain.defaultPrevented, undefined, 'a plain arrow stays the browser/roving key');
    const left = g.rowA.dispatch('keydown', key(g.rowA, 'ArrowLeft', { altKey: true }));
    const right = g.rowA.dispatch('keydown', key(g.rowA, 'ArrowRight', { altKey: true }));
    assert.equal(left.defaultPrevented, true, 'the helper still cancels Alt+Left');
    assert.equal(right.defaultPrevented, true);
    g.rowA.dispatch('keydown', key(g.rowA, 'ArrowUp', { altKey: true, shiftKey: true }));
    g.rowA.dispatch('keydown', key(g.rowA, 'ArrowDown', { altKey: true, ctrlKey: true }));
    assert.equal(ref.calls.length, 2);
});

test('a keyboard move outside a tree row is ignored', async () => {
    const { g, ref } = await mount();
    const stray = el([], g.treeBody);
    stray.dispatch('keydown', key(stray, 'ArrowDown', { altKey: true }));
    assert.equal(ref.calls.length, 0);
});

test('Alt+arrow inside an inline editor is left to the field (the accept option)', async () => {
    const { g, ref } = await mount();
    const input = el([], g.rowA, undefined, 'input');

    const typed = g.rowA.dispatch('keydown', key(input, 'ArrowDown', { altKey: true }));
    assert.equal(typed.defaultPrevented, undefined, 'the chord belongs to the field: not cancelled');
    assert.equal(ref.calls.length, 0, 'and the row does not move');

    const row = g.rowA.dispatch('keydown', key(g.rowCell, 'ArrowDown', { altKey: true }));
    assert.equal(row.defaultPrevented, true, 'outside the editor the row move still runs');
    assert.deepEqual(ref.calls, [['OnTreeKeyboardMove', 'a', 1]]);
});

test('attachTimeline / attachTree resolve true when attached and false with nothing to attach', async () => {
    const g = gantt();
    assert.equal(await tm.attachTimeline('ok', g.timeline, g.body, g.treeBody, dotnet()), true);
    assert.equal(await tm.attachTree('ok', g.treeBody, dotnet()), true);

    assert.equal(await tm.attachTimeline(null, g.timeline, g.body, g.treeBody, dotnet()), false);
    assert.equal(await tm.attachTimeline('no-el', null, g.body, g.treeBody, dotnet()), false);
    assert.equal(await tm.attachTree('no-el', null, dotnet()), false);
    await tm.detachTimeline('ok');
    await tm.detachTree('ok');
});

test('attachTimeline / attachTree resolve false when the pointer helpers cannot load', async (t) => {
    const warnings = [];
    t.mock.method(console, 'warn', (...args) => warnings.push(args));
    await boot('file:///definitely/not/there/gantt.js');

    const g = gantt();
    assert.equal(await tm.attachTimeline('w', g.timeline, g.body, g.treeBody, dotnet()), false, 'the caller learns the sessions are NOT registered');
    assert.equal(await tm.attachTree('w', g.treeBody, dotnet()), false);
});

test('a finger turns the row non-draggable and restores it on release / cancel; the mouse is untouched', async () => {
    const { g } = await mount();

    g.rowA.dispatch('pointerdown', ev(g.rowCell, { pointerType: 'touch', pointerId: 3, clientX: 10, clientY: 10 }));
    assert.equal(g.rowA.getAttribute('draggable'), 'false');
    assert.equal(g.rowB.getAttribute('draggable'), 'true', 'only the pressed row');
    g.rowA.dispatch('pointerup', ev(g.rowCell, { pointerType: 'touch', pointerId: 3, clientX: 10, clientY: 10 }));
    assert.equal(g.rowA.getAttribute('draggable'), 'true');

    g.rowB.dispatch('pointerdown', ev(g.rowB, { pointerType: 'touch', pointerId: 4, clientX: 10, clientY: 40 }));
    assert.equal(g.rowB.getAttribute('draggable'), 'false');
    g.rowB.dispatch('pointercancel', ev(g.rowB, { pointerType: 'touch', pointerId: 4 }));
    assert.equal(g.rowB.getAttribute('draggable'), 'true');

    g.rowB.dispatch('pointerdown', ev(g.rowB, { pointerType: 'touch', pointerId: 5, clientX: 10, clientY: 40 }));
    g.treeBody.dispatch('lostpointercapture', ev(g.rowB, { pointerType: 'touch', pointerId: 5 }));
    assert.equal(g.rowB.getAttribute('draggable'), 'true');

    for (const pointerType of ['mouse', 'pen']) {
        g.rowA.dispatch('pointerdown', ev(g.rowA, { pointerType, pointerId: 6, clientX: 10, clientY: 10 }));
        assert.equal(g.rowA.getAttribute('draggable'), 'true', pointerType);
        g.rowA.dispatch('pointerup', ev(g.rowA, { pointerType, pointerId: 6, clientX: 10, clientY: 10 }));
    }
});

test('a row without a draggable attribute gets it back removed, not "false"', async () => {
    const { g } = await mount();
    g.rowA.removeAttribute('draggable');
    g.rowA.dispatch('pointerdown', ev(g.rowA, { pointerType: 'touch', pointerId: 3, clientX: 10, clientY: 10 }));
    assert.equal(g.rowA.getAttribute('draggable'), 'false');
    g.rowA.dispatch('pointerup', ev(g.rowA, { pointerType: 'touch', pointerId: 3, clientX: 10, clientY: 10 }));
    assert.equal(g.rowA.getAttribute('draggable'), null);
});

// ── lifecycle ───────────────────────────────────────────────────────────────────────────────

test('attach twice (even concurrently) never doubles the sessions', async () => {
    const g = gantt();
    const ref = dotnet();
    await Promise.all([
        tm.attachTimeline('dup', g.timeline, g.body, g.treeBody, ref),
        tm.attachTimeline('dup', g.timeline, g.body, g.treeBody, ref),
    ]);
    const once = g.timeline.total();
    assert.ok(once > 0);
    await tm.attachTimeline('dup', g.timeline, g.body, g.treeBody, ref);
    assert.equal(g.timeline.total(), once);

    await Promise.all([tm.attachTree('dup', g.treeBody, ref), tm.attachTree('dup', g.treeBody, ref)]);
    const treeOnce = g.treeBody.total();
    assert.ok(treeOnce > 0);
    await tm.attachTree('dup', g.treeBody, ref);
    assert.equal(g.treeBody.total(), treeOnce);

    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 330, clientY: 130 }));
    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 330, clientY: 130 }));
    assert.equal(ref.calls.length, 1, 'one session, one completion');
});

test('detachTimeline removes every listener, ends a drag in flight, and is idempotent', async () => {
    const { g, ref } = await mount();
    g.timeline.scrollLeft = 100;

    doc.elementFromPoint = () => g.barB;
    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 330, clientY: 130 }));
    assert.ok(ghost(g));

    await tm.detachTimeline(g.id);
    assert.equal(ghost(g), null, 'the ghost is removed');
    assert.equal(g.barB.classList.contains('tm-gantt__bar--dep-target'), false);
    assert.equal(g.timeline.total(), 0);
    assert.equal(ref.calls.length, 0, 'a detached drag commits nothing');
    await tm.detachTimeline(g.id);

    g.timeline.dispatch('pointerdown', ev(g.empty, { clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 100, clientY: 200 }));
    assert.equal(g.timeline.scrollLeft, 100, 'no pan after detach');

    await tm.attachTimeline(g.id, g.timeline, g.body, g.treeBody, ref);
    assert.ok(g.timeline.total() > 0, 'can be attached again');
});

test('detachTimeline during a pan restores the scroll (cancel)', async () => {
    const { g } = await mount();
    g.timeline.scrollLeft = 100;
    g.timeline.dispatch('pointerdown', ev(g.empty, { clientX: 200, clientY: 200 }));
    g.timeline.dispatch('pointermove', ev(g.empty, { clientX: 100, clientY: 200 }));
    assert.equal(g.timeline.scrollLeft, 200);
    await tm.detachTimeline(g.id);
    assert.equal(g.timeline.scrollLeft, 100);
});

test('detachTree removes every listener, restores a pending draggable, and is idempotent', async () => {
    const { g } = await mount();
    g.rowA.dispatch('pointerdown', ev(g.rowA, { pointerType: 'touch', pointerId: 3, clientX: 10, clientY: 10 }));
    assert.equal(g.rowA.getAttribute('draggable'), 'false');

    await tm.detachTree(g.id);
    assert.equal(g.rowA.getAttribute('draggable'), 'true');
    assert.equal(g.treeBody.total(), 0);
    await tm.detachTree(g.id);

    g.rowA.dispatch('pointerdown', ev(g.rowA, { pointerType: 'touch', pointerId: 4, clientX: 10, clientY: 10 }));
    assert.equal(g.rowA.getAttribute('draggable'), 'true', 'no guard after detach');
});

test('two Gantts on one page are independent', async () => {
    const a = gantt();
    const b = gantt();
    const refA = dotnet();
    const refB = dotnet();
    await mount(a, refA);
    await mount(b, refB);

    doc.elementFromPoint = () => b.barB;
    b.timeline.dispatch('pointerdown', ev(b.handle, { clientX: 246, clientY: 90 }));
    b.timeline.dispatch('pointermove', ev(b.handle, { clientX: 330, clientY: 130 }));
    assert.ok(ghost(b));
    assert.equal(ghost(a), null);
    b.timeline.dispatch('pointerup', ev(b.handle, { clientX: 330, clientY: 130 }));
    assert.equal(refA.calls.length, 0);
    assert.equal(refB.calls.length, 1);

    await tm.detachTimeline(a.id);
    assert.equal(a.timeline.total(), 0);
    assert.ok(b.timeline.total() > 0, 'detaching one leaves the other attached');

    b.rowA.dispatch('keydown', key(b.rowA, 'ArrowDown', { altKey: true }));
    assert.deepEqual(refB.calls.at(-1), ['OnTreeKeyboardMove', 'a', 1]);
    assert.equal(refA.calls.length, 0);
});

test('a rejected .NET call is swallowed', async () => {
    const g = gantt();
    const ref = { invokeMethodAsync: () => Promise.reject(new Error('disposed')) };
    await mount(g, ref);
    const unhandled = [];
    const onUnhandled = e => unhandled.push(e);
    process.on('unhandledRejection', onUnhandled);
    g.rowA.dispatch('keydown', key(g.rowA, 'ArrowDown', { altKey: true }));
    await new Promise(r => setImmediate(r));
    process.off('unhandledRejection', onUnhandled);
    assert.equal(unhandled.length, 0);

    const throwing = { invokeMethodAsync() { throw new Error('sync'); } };
    const h = gantt();
    await mount(h, throwing);
    assert.doesNotThrow(() => h.rowA.dispatch('keydown', key(h.rowA, 'ArrowDown', { altKey: true })));
});

test('a helper that cannot be loaded only warns; nothing is attached and nothing throws', async (t) => {
    const warnings = [];
    t.mock.method(console, 'warn', (...args) => warnings.push(args));
    await boot('file:///definitely/not/there/gantt.js');

    const g = gantt();
    const ref = dotnet();
    await assert.doesNotReject(tm.attachTimeline('w', g.timeline, g.body, g.treeBody, ref));
    await assert.doesNotReject(tm.attachTree('w', g.treeBody, ref));
    assert.ok(warnings.length >= 1);
    assert.match(String(warnings[0][0]), /tmGantt/);
    assert.equal(g.timeline.total(), 0);
    assert.equal(g.treeBody.total(), 0);
    await assert.doesNotReject(tm.detachTimeline('w'));
    await assert.doesNotReject(tm.detachTree('w'));
});

test('a missing document.currentScript falls back to the base URI (and still never throws)', async (t) => {
    const warnings = [];
    t.mock.method(console, 'warn', (...args) => warnings.push(args));
    await boot(undefined);
    doc.currentScript = null;
    await import(`../gantt.js?${Math.random()}`);
    const g = gantt();
    await assert.doesNotReject(globalThis.window.tmGantt.attachTimeline('fb', g.timeline, g.body, g.treeBody, dotnet()));
});

// ── id-keyed registration (Blazor removes the element before DisposeAsync; the ElementReference arrives null) ──

test('detach by id works after the element left the DOM, with no element argument', async () => {
    const { g, ref } = await mount(gantt(), dotnet(), 'gone');
    g.timeline.remove();
    g.treeBody.remove();
    await tm.detachTimeline('gone');
    await tm.detachTree('gone');
    assert.equal(g.timeline.total(), 0, 'the listeners left the (detached) element');
    assert.equal(g.treeBody.total(), 0);
    await tm.detachTimeline('gone');
    await tm.detachTree('gone');
    assert.equal(ref.calls.length, 0);
});

test('attaching an id that already exists replaces the old registration (new element, new .NET reference)', async () => {
    const first = gantt();
    const refA = dotnet();
    await mount(first, refA, 'same');
    const second = gantt();
    const refB = dotnet();
    await mount(second, refB, 'same');

    assert.equal(first.timeline.total(), 0, 'the old element is released');
    assert.equal(first.treeBody.total(), 0);
    assert.ok(second.timeline.total() > 0);

    doc.elementFromPoint = () => second.barB;
    second.timeline.dispatch('pointerdown', ev(second.handle, { clientX: 246, clientY: 90 }));
    second.timeline.dispatch('pointermove', ev(second.handle, { clientX: 330, clientY: 130 }));
    second.timeline.dispatch('pointerup', ev(second.handle, { clientX: 330, clientY: 130 }));
    assert.equal(refA.calls.length, 0);
    assert.equal(refB.calls.length, 1);

    await tm.detachTimeline('same');
    assert.equal(second.timeline.total(), 0);
});

test('a dependency drag swallows the click that follows it; a tap lets the click through', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const { g } = await mount();
    doc.elementFromPoint = () => g.empty;

    g.timeline.dispatch('pointerdown', ev(g.handle, { clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointermove', ev(g.handle, { clientX: 330, clientY: 130 }));
    g.timeline.dispatch('pointerup', ev(g.handle, { clientX: 330, clientY: 130 }));
    const dragClick = g.timeline.dispatch('click', { target: g.handle, preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } });
    assert.equal(dragClick.stopped, true, 'the helper default suppressClick swallows the release click');

    t.mock.timers.tick(1);
    g.timeline.dispatch('pointerdown', ev(g.handle, { pointerId: 2, clientX: 246, clientY: 90 }));
    g.timeline.dispatch('pointerup', ev(g.handle, { pointerId: 2, clientX: 246, clientY: 90 }));
    const tapClick = g.timeline.dispatch('click', { target: g.handle, stopPropagation() { this.stopped = true; } });
    assert.equal(tapClick.stopped, undefined, 'a tap keeps its click');
});