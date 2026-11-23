// Phase 8b: applyInitialZoom must wait until the svg size has settled (two consecutive matching
// getBoundingClientRect readings, cap 10 frames / 500 ms) before laying the view out. A user zoom
// or destroy during that wait cancels the pending layout. These tests drive a queued rAF (not the
// immediate stub in wireframe-designer-pointer.test.mjs) so the settle loop is observable.
import test, { mock } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const selfUrl = pathToFileURL(path.resolve(here, '../wireframe-designer.js')).href;
const coreJs = path.resolve(here, '../../../../Tempo.Blazor/wwwroot/js');
const dragUrl = pathToFileURL(path.join(coreJs, 'pointer-drag.js')).href;
const gestureUrl = pathToFileURL(path.join(coreJs, 'pointer-gesture.js')).href;
const gesturesUrl = pathToFileURL(path.resolve(here, '../wireframe-gestures.mjs')).href;

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

function matchesOne(el, part) {
    part = part.trim();
    if (part.startsWith('.')) return el.classList.contains(part.slice(1));
    const attr = part.match(/^\[([\w-]+)(?:="([^"]*)")?\]$/);
    if (attr) {
        const value = el.attrs.get(attr[1]);
        return attr[2] === undefined ? value !== undefined : value === attr[2];
    }
    return el.tag === part;
}
const matches = (el, selector) => selector.split(',').some(part => matchesOne(el, part));

function node(tag, attrs = {}, parent = null) {
    const classes = new Set();
    const n = {
        ...bag(), tag, attrs: new Map(), children: [], parent, style: {}, captured: new Set(), released: [],
        classList: {
            add: (...c) => c.forEach(x => classes.add(x)),
            remove: (...c) => c.forEach(x => classes.delete(x)),
            contains: c => classes.has(c),
        },
        setAttribute(name, value) { n.attrs.set(name, String(value)); if (name === 'class') String(value).split(/\s+/).forEach(c => c && classes.add(c)); },
        getAttribute(name) { return n.attrs.has(name) ? n.attrs.get(name) : null; },
        hasAttribute(name) { return n.attrs.has(name); },
        removeAttribute(name) { n.attrs.delete(name); },
        appendChild(child) { child.parent = n; n.children.push(child); return child; },
        remove() { if (n.parent) n.parent.children = n.parent.children.filter(c => c !== n); n.parent = null; },
        setPointerCapture(id) { n.captured.add(id); },
        releasePointerCapture(id) { n.captured.delete(id); n.released.push(id); },
        closest(selector) { for (let p = n; p; p = p.parent) if (matches(p, selector)) return p; return null; },
        contains(el) { for (let p = el; p; p = p.parent) if (p === n) return true; return false; },
        querySelectorAll(selector) {
            const out = [];
            const walk = el => { for (const c of el.children) { if (matches(c, selector)) out.push(c); walk(c); } };
            walk(n);
            return out;
        },
        querySelector(selector) { return n.querySelectorAll(selector)[0] ?? null; },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 }),
        focus() { n.focused = true; },
    };
    for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
    const own = n.dispatch;
    n.dispatch = (type, event = {}) => {
        own(type, event);
        if (n.parent) n.parent.dispatch(type, event);
        return event;
    };
    if (parent) parent.children.push(n);
    return n;
}

function svgNode() {
    const svg = node('svg');
    svg.viewBox = { baseVal: { x: 0, y: 0, width: 800, height: 600 } };
    const setAttribute = svg.setAttribute;
    svg.setAttribute = (name, value) => {
        setAttribute(name, value);
        if (name === 'viewBox') {
            const [x, y, w, h] = String(value).split(/\s+/).map(Number);
            Object.assign(svg.viewBox.baseVal, { x, y, width: w, height: h });
        }
    };
    svg.id = 'wd-initial-zoom';
    svg.setAttribute('viewBox', '0 0 800 600');
    svg.createSVGPoint = () => ({
        x: 0, y: 0,
        matrixTransform(m) { return { x: this.x * m.a + this.y * m.c + m.e, y: this.x * m.b + this.y * m.d + m.f }; },
    });
    svg.getScreenCTM = () => {
        const vb = svg.viewBox.baseVal;
        const s = 800 / vb.width;
        return { a: s, b: 0, c: 0, d: s, e: -vb.x * s, f: -vb.y * s, inverse() {
            const inv = { a: 1 / s, b: 0, c: 0, d: 1 / s, e: vb.x, f: vb.y };
            inv.inverse = () => this;
            return inv;
        } };
    };
    svg.closest = (selector) => (matches(svg, selector) ? svg : (svg.parent ? svg.parent.closest(selector) : null));
    return svg;
}

function dotnet() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); },
        named: name => calls.filter(c => c[0] === name),
    };
}

let doc;
let engine;
let helpers;
let rafQueue;
let rafId;
let cancelled;
let nowMs;

function installQueuedRaf() {
    rafQueue = [];
    rafId = 0;
    cancelled = new Set();
    nowMs = 1_000;
    globalThis.Date.now = () => nowMs;
    globalThis.requestAnimationFrame = fn => {
        const id = ++rafId;
        rafQueue.push({ id, fn });
        return id;
    };
    globalThis.cancelAnimationFrame = id => { cancelled.add(id); };
}

function flushRaf(max = 20) {
    let n = 0;
    while (rafQueue.length && n < max) {
        const batch = rafQueue;
        rafQueue = [];
        for (const item of batch) {
            if (cancelled.has(item.id)) continue;
            item.fn(nowMs);
        }
        n++;
    }
    return n;
}

async function boot() {
    installQueuedRaf();
    doc = {
        ...bag(),
        readyState: 'complete', baseURI: 'https://example.test/app/', currentScript: { src: selfUrl },
        contains: () => true, activeElement: null, body: node('body'),
        documentElement: { clientWidth: 800, clientHeight: 600 },
        scrollingElement: { scrollLeft: 0, scrollTop: 0 },
        elementFromPoint: () => null,
        createElementNS: (_ns, tag) => node(tag),
    };
    globalThis.document = doc;
    globalThis.window = { matchMedia: () => ({ matches: false }) };
    globalThis.matchMedia = globalThis.window.matchMedia;
    await import(`${selfUrl}?${Math.random()}`);
    engine = globalThis.window.tmWireframeDesigner;
    helpers = {
        drag: await import(dragUrl),
        gesture: await import(gestureUrl),
        gestures: await import(gesturesUrl),
    };
    engine._loadHelpers = () => Promise.resolve(helpers);
    return engine;
}

async function canvas({ initialZoom = 'fit', canvasWidth = 1440, canvasHeight = 900 } = {}) {
    await boot();
    const svg = svgNode();
    const wrap = node('div', { class: 'tm-wd-canvas-wrap' });
    wrap.children.push(svg); svg.parent = wrap;
    const ref = dotnet();
    const id = engine.init(svg, ref, { readOnly: false, gridSize: 8, showGrid: true, canvasWidth, canvasHeight, initialZoom });
    await engine.instances.get(id).ready;
    return { svg, wrap, ref, id, inst: () => engine.instances.get(id) };
}

function resizeSvg(svg, width, height) {
    svg.getBoundingClientRect = () => ({ left: 0, top: 0, width, height });
}

function sequenceRect(svg, frames) {
    let i = 0;
    svg.getBoundingClientRect = () => {
        const f = frames[Math.min(i, frames.length - 1)];
        i++;
        return { left: 0, top: 0, width: f.w, height: f.h };
    };
    return () => i;
}

const near = (actual, expected, message) =>
    assert.ok(Math.abs(actual - expected) < 1e-6, `${message ?? ''} expected ${expected}, got ${actual}`);

test.afterEach(() => {
    mock.timers.reset();
    engine?.instances.forEach((_v, id) => engine.destroy({ id }));
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.matchMedia;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
    delete globalThis.Date.now;
});

test('applyInitialZoom returns 0 immediately and lays the view out from the settled size after inflated frames', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    sequenceRect(c.svg, [
        { w: 720, h: 4000 },
        { w: 720, h: 3500 },
        { w: 720, h: 3000 },
        { w: 720, h: 450 },
        { w: 720, h: 450 },
    ]);

    const returned = engine.applyInitialZoom(c.svg, 'fit');
    assert.equal(returned, 0, 'layout is not synchronous; .NET must not treat the return as the real scale');
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0, 'no .NET callback until settle');
    assert.equal(c.ref.named('OnZoomChanged').length, 0);

    flushRaf();

    const expected = Math.min(720 / 1520, 450 / 980);
    near(c.inst().scale, expected, 'scale from the REAL svg size, not the inflated frames');
    const vb = c.svg.viewBox.baseVal;
    near(vb.width * c.inst().scale, 720, 'viewBox spans the settled svg width');
    near(vb.height * c.inst().scale, 450, 'and its height');
    assert.equal(c.ref.named('OnViewBoxChanged').length, 1, 'rulers / minimap learn the view once');
    assert.equal(c.ref.named('OnZoomChanged').length, 1, 'the real scale is reported once via OnZoomChanged');
    assert.equal(c.ref.named('OnZoomChanged')[0][1], c.inst().scale);
    assert.equal(c.inst().userViewChanged, false);
    assert.equal(c.inst().pendingInitialLayout, null);
});

test('oscillating sizes fall back to the last measure after 10 frames (or 500 ms)', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    let i = 0;
    c.svg.getBoundingClientRect = () => ({ left: 0, top: 0, width: 720, height: 400 + (i++ % 2) * 20 });

    assert.equal(engine.applyInitialZoom(c.svg), 0);
    flushRaf(12);

    const lastHeight = 400 + ((i - 1) % 2) * 20;
    const expected = Math.min(720 / 1520, lastHeight / 980);
    near(c.inst().scale, expected, 'the cap uses the last measured size');
    assert.equal(c.ref.named('OnZoomChanged').length, 1);
    assert.equal(c.inst().pendingInitialLayout, null);
});

test('user zoom during pending cancels the layout: the user view is never overwritten', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    sequenceRect(c.svg, [
        { w: 720, h: 4000 },
        { w: 720, h: 3500 },
        { w: 720, h: 450 },
        { w: 720, h: 450 },
    ]);

    assert.equal(engine.applyInitialZoom(c.svg), 0);
    flushRaf(1);                              // still inflated, not settled
    engine.zoomTo(c.svg, 2);
    assert.equal(c.inst().userViewChanged, true);
    const zoomed = c.svg.getAttribute('viewBox');

    flushRaf();
    assert.equal(c.svg.getAttribute('viewBox'), zoomed, 'pending Fit never overwrites the user zoom');
    assert.equal(c.inst().scale, 2);
    assert.equal(c.inst().pendingInitialLayout, null);
});

test('destroy during pending cancels rAF: no .NET callback, a second destroy is a no-op', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    sequenceRect(c.svg, [
        { w: 720, h: 4000 },
        { w: 720, h: 450 },
        { w: 720, h: 450 },
    ]);

    assert.equal(engine.applyInitialZoom(c.svg), 0);
    engine.destroy(c.svg);
    assert.ok(cancelled.size > 0, 'destroy cancels the pending rAF (a later tick must not run)');
    flushRaf();
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0, 'a destroyed instance never reports a view');
    assert.equal(c.ref.named('OnZoomChanged').length, 0);
    engine.destroy(c.svg);                    // idempotent
});

test('a hidden tab (no size) reports 0; the first real size lays the view out through relayout without the 0.5 px threshold', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    resizeSvg(c.svg, 0, 0);
    assert.equal(engine.applyInitialZoom(c.svg), 0);
    assert.equal(c.svg.getAttribute('viewBox'), '0 0 800 600', 'untouched while hidden');
    assert.equal(c.inst().userViewChanged, false);
    assert.equal(c.inst().layoutRect, null);

    resizeSvg(c.svg, 720, 450);
    const scale = engine.relayout(c.svg);
    near(scale, Math.min(720 / 1520, 450 / 980), 'the first real size Fits even if it later matches a stale 0.5 px window');
    assert.equal(c.ref.named('OnZoomChanged').length, 1);
});

test('ActualSize after settle is 100 % (same result as today, later measurement)', async () => {
    const c = await canvas({ initialZoom: 'actual', canvasWidth: 1440, canvasHeight: 900 });
    sequenceRect(c.svg, [
        { w: 600, h: 2000 },
        { w: 600, h: 400 },
        { w: 600, h: 400 },
    ]);

    assert.equal(engine.applyInitialZoom(c.svg, 'actual'), 0);
    flushRaf();
    assert.equal(c.inst().scale, 1);
    const vb = c.svg.viewBox.baseVal;
    near(vb.width, 600);
    near(vb.height, 400);
    near(vb.x, -40);
    near(vb.y, -40);
    assert.equal(c.ref.named('OnZoomChanged').length, 1);
    assert.equal(c.ref.named('OnZoomChanged')[0][1], 1);
});

test('relayout during pending skips the 0.5 px threshold and does not stamp a blocking layoutRect', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    let n = 0;
    c.svg.getBoundingClientRect = () => {
        n++;
        // Inflated first measures that do NOT match consecutively (otherwise two readings would
        // settle on the inflated size). relayout's 0.5 px threshold would still freeze 4000.2
        // if it stamped layoutRect; the real size then differs by many pixels.
        if (n === 1) return { left: 0, top: 0, width: 720, height: 4000.2 }; // apply
        if (n === 2) return { left: 0, top: 0, width: 720, height: 4000.2 }; // relayout mid-settle
        if (n === 3) return { left: 0, top: 0, width: 720, height: 3500 };   // first tick, still inflated
        return { left: 0, top: 0, width: 720, height: 450 };
    };

    assert.equal(engine.applyInitialZoom(c.svg), 0);
    engine.relayout(c.svg);                   // ResizeObserver mid-settle
    assert.equal(c.inst().layoutRect, null, 'relayout during pending must not stamp a blocking layoutRect');
    assert.equal(c.ref.named('OnZoomChanged').length, 0, 'relayout must not commit mid-settle');
    flushRaf();
    near(c.inst().scale, Math.min(720 / 1520, 450 / 980), 'settle wins over a threshold-blocked inflated rect');
});

test('two consecutive matching sizes (the apply measure plus one frame) settle without waiting for a third reading', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    sequenceRect(c.svg, [
        { w: 720, h: 450 },
        { w: 720, h: 450 },
    ]);

    assert.equal(engine.applyInitialZoom(c.svg), 0);
    const ticks = flushRaf();
    assert.ok(ticks <= 1, `settled after the first matching frame, not a third reading (ticks=${ticks})`);
    near(c.inst().scale, Math.min(720 / 1520, 450 / 980));
    assert.equal(c.ref.named('OnZoomChanged').length, 1);
    assert.equal(c.inst().pendingInitialLayout, null);
});

test('a size that disappears mid-settle (hidden tab) does not commit a stale layoutRect; later relayout fits', async () => {
    const c = await canvas({ initialZoom: 'fit', canvasWidth: 1440, canvasHeight: 900 });
    let n = 0;
    c.svg.getBoundingClientRect = () => {
        n++;
        if (n === 1) return { left: 0, top: 0, width: 720, height: 4000 };
        return { left: 0, top: 0, width: 0, height: 0 };
    };
    const originalVb = c.svg.getAttribute('viewBox');

    assert.equal(engine.applyInitialZoom(c.svg), 0);
    flushRaf(12);

    assert.equal(c.svg.getAttribute('viewBox'), originalVb, 'no viewBox change when the size is gone at timeout');
    assert.equal(c.inst().layoutRect, null, 'must not stamp a stale lastSize that would freeze relayout');
    assert.equal(c.inst().userViewChanged, false);
    assert.equal(c.inst().pendingInitialLayout, null);
    assert.equal(c.ref.named('OnViewBoxChanged').length, 0);
    assert.equal(c.ref.named('OnZoomChanged').length, 0);

    resizeSvg(c.svg, 720, 450);
    const scale = engine.relayout(c.svg);
    near(scale, Math.min(720 / 1520, 450 / 980), 'the first real size Fits through relayout');
    assert.equal(c.ref.named('OnZoomChanged').length, 1);
});
