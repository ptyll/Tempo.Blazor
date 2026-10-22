// pdf-viewer.js M3 (FZE-9 review) selection tests — the text-selection report is
// debounced on `selectionchange` (120 ms), flushed immediately on `pointerup`,
// deduped by the pure text|page|rects key, and `clearSelection` clears the DOM
// selection and resets the dedupe key so a dismissed CTA cannot swallow the next
// selection.
import test from 'node:test';
import assert from 'node:assert/strict';
import { copyFile, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const VIEWER_JS = new URL('../pdf-viewer.js', import.meta.url);

const PDF_STUB = `export const GlobalWorkerOptions = {};
export function getDocument() { return globalThis.__tmPdfViewerTestDocRequest; };`;

const SIZE_STUB = `export function canvasSize(width, height, dpr) {
  const cap = Math.min(dpr || 1, 2);
  return { width: Math.max(1, Math.round(width * cap)), height: Math.max(1, Math.round(height * cap)), cssWidth: width, cssHeight: height, dpr: cap };
};`;

const RENDER_STUB = `export async function renderPageToCanvas() {};
export function isRenderingCancelled(err) { return err?.name === 'RenderingCancelledException'; };`;

function fakeElement(tag) {
    return {
        tagName: tag,
        nodeType: 1,
        style: {},
        dataset: {},
        attrs: {},
        children: [],
        innerHTML: '',
        isConnected: true,
        appendChild(child) { this.children.push(child); },
        insertBefore(child) { this.children.unshift(child); return child; },
        replaceChildren() { this.children.length = 0; },
        setAttribute(name, value) { this.attrs[name] = String(value); },
        getAttribute(name) { return this.attrs[name] ?? null; },
        removeAttribute(name) { delete this.attrs[name]; },
        addEventListener(type, handler) { (this.listeners ??= []).push({ type, handler }); },
        removeEventListener() { },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        contains() { return false; },
        getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 50 }; },
        scrollIntoView() { },
        scrollLeft: 0,
        scrollTop: 0,
    };
}

function fakeCanvas() {
    const canvas = fakeElement('canvas');
    canvas.offsetLeft = 0;
    canvas.offsetTop = 0;
    canvas.offsetWidth = 100;
    canvas.offsetHeight = 50;
    return canvas;
}

/** Root element whose body contains a single-page canvas, as TmPdfViewer renders it. */
function fakeRootEl(canvas) {
    const el = fakeElement('root');
    el.querySelector = (selector) => (typeof selector === 'string' && selector.includes('canvas') ? canvas : null);
    return el;
}

function fakePdfDoc(numPages) {
    return {
        numPages,
        async destroy() { },
        async getPage() {
            return {
                getViewport: () => ({ width: 100, height: 50, transform: [1, 0, 0, 1, 0, 0] }),
                getTextContent: async () => ({ items: [] }),
            };
        },
    };
}

function makeDocRequest() {
    let resolve;
    const promise = new Promise((res) => { resolve = res; });
    return { promise, resolve };
}

const flush = () => new Promise(resolve => setImmediate(resolve));
const settle = async () => { for (let i = 0; i < 5; i++) await flush(); };
const debounceWindow = () => new Promise(resolve => setTimeout(resolve, 160));

async function loadViewer(testContext) {
    const registry = {
        listeners: { added: [], removed: [] },
        loadedPages: [],
        reported: [],
        geometryReports: [],
        resizeObservers: [],
        selectionCleared: 0,
        loadErrors: [],
    };
    const docRequest = makeDocRequest();

    const originals = {
        ResizeObserver: globalThis.ResizeObserver,
        document: globalThis.document,
        window: globalThis.window,
        getSelection: globalThis.getSelection,
    };
    globalThis.ResizeObserver = class {
        constructor(callback) {
            this.callback = callback;
            this.observations = [];
            this.disconnected = false;
            registry.resizeObservers.push(this);
        }
        observe(target) { this.observations.push(target); }
        unobserve() { }
        disconnect() { this.disconnected = true; }
    };
    globalThis.window = globalThis;
    globalThis.document = {
        currentScript: { src: '' },
        addEventListener(type, handler, options) { registry.listeners.added.push({ type, handler, options }); },
        removeEventListener(type, handler) { registry.listeners.removed.push({ type, handler }); },
        createElement: () => fakeElement('div'),
    };
    globalThis.getSelection = () => globalThis.__tmPdfViewerTestSelection;

    const dir = await mkdtemp(path.join(tmpdir(), 'tm-pdf-selection-test-'));
    await mkdir(dir, { recursive: true });
    await writeFile(path.join(dir, 'package.json'), JSON.stringify({ type: 'module' }), 'utf8');
    await writeFile(path.join(dir, 'pdf.min.mjs'), PDF_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-size.mjs'), SIZE_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-render.mjs'), RENDER_STUB, 'utf8');
    // The package ships pdf-viewer-continuous.mjs as a sibling of pdf-viewer.js —
    // copy it so _ensureContinuousModule() resolves like it does in the browser.
    await copyFile(new URL('../pdf-viewer-continuous.mjs', import.meta.url),
        path.join(dir, 'pdf-viewer-continuous.mjs'));
    const viewerPath = path.join(dir, 'pdf-viewer.js');
    await copyFile(VIEWER_JS, viewerPath);

    globalThis.document.currentScript.src = pathToFileURL(viewerPath).href;
    globalThis.__tmPdfViewerTestDocRequest = docRequest;

    await import(pathToFileURL(viewerPath).href);
    const viewer = globalThis.window.tmPdfViewer;
    assert.ok(viewer, 'importing pdf-viewer.js must install window.tmPdfViewer');

    const dotNet = {
        invokeMethodAsync(name, ...args) {
            if (name === 'OnPdfLoaded') registry.loadedPages.push(args[0]);
            if (name === 'OnTextSelectionChanged') registry.reported.push(args);
            if (name === 'OnPdfLoadError') registry.loadErrors.push(args[0]);
            if (name === 'OnContinuousPageGeometry') registry.geometryReports.push(args);
            return Promise.resolve();
        },
    };

    testContext.after(async () => {
        globalThis.ResizeObserver = originals.ResizeObserver;
        globalThis.document = originals.document;
        if (originals.window === undefined) delete globalThis.window; else globalThis.window = originals.window;
        if (originals.getSelection === undefined) delete globalThis.getSelection; else globalThis.getSelection = originals.getSelection;
        delete globalThis.__tmPdfViewerTestDocRequest;
        delete globalThis.__tmPdfViewerTestSelection;
        await rm(dir, { recursive: true, force: true });
    });

    return { viewer, registry, docRequest, dotNet };
}

/** Installs a fake DOM selection: one rect over the 100x50 canvas at the given text. */
function fakeSelection(text, rect = { left: 0, top: 0, width: 50, height: 10 }) {
    const selection = {
        removeAllRangesCalled: 0,
        rangeCount: text === null ? 0 : 1,
        isCollapsed: false,
        anchorNode: { nodeType: 3 },
        toString: () => text ?? '',
        getRangeAt: () => ({ getClientRects: () => [rect] }),
        removeAllRanges() { this.removeAllRangesCalled++; },
    };
    globalThis.__tmPdfViewerTestSelection = selection;
    return selection;
}

async function loadedEnv(testContext) {
    const env = await loadViewer(testContext);
    const rootEl = fakeRootEl(fakeCanvas());
    const textLayer = fakeElement('text-layer');
    textLayer.contains = () => true;

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enableSelection(rootEl, textLayer, env.dotNet);
    await settle();
    return { env, rootEl };
}

const handlerOf = (env, type) => env.registry.listeners.added.find(l => l.type === type)?.handler;

test('the pure selection key is stable for an unchanged selection and differs otherwise', async (t) => {
    const env = await loadViewer(t);
    assert.equal(env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.02]), 'text|2|0.1,0.2,0.3,0.02');
    assert.equal(
        env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.02]),
        env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.02]),
        'the same text|page|rects must produce the same key (dedupe identity)');
    assert.notEqual(
        env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.02]),
        env.viewer._selectionKey('other', 2, [0.1, 0.2, 0.3, 0.02]),
        'a different text is a different selection');
    assert.notEqual(
        env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.02]),
        env.viewer._selectionKey('text', 3, [0.1, 0.2, 0.3, 0.02]),
        'a different page is a different selection');
    assert.notEqual(
        env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.02]),
        env.viewer._selectionKey('text', 2, [0.1, 0.2, 0.3, 0.03]),
        'different rects are a different selection');
});

test('selectionchange reports after the debounce, pointerup flushes immediately', async (t) => {
    const { env, rootEl } = await loadedEnv(t);
    fakeSelection('selected words');

    // The debounce: nothing may report synchronously or before the window elapsed.
    handlerOf(env, 'selectionchange')();
    await settle();
    assert.deepEqual(env.registry.reported, [], 'selectionchange must not report inside the debounce window');

    await debounceWindow();
    await settle();
    assert.equal(env.registry.reported.length, 1, 'the debounced report fires after ~120ms');
    assert.equal(env.registry.reported[0][0], 'selected words');
    assert.equal(env.registry.reported[0][1], 1, 'the report carries the JS-side current page');

    // The flush: pointerup reports immediately (the gesture ended).
    globalThis.__tmPdfViewerTestSelection = fakeSelection('changed words');
    handlerOf(env, 'pointerup')();
    await settle();
    assert.equal(env.registry.reported.length, 2, 'pointerup flushes without the debounce delay');
    assert.equal(env.registry.reported[1][0], 'changed words');
});

test('an unchanged selection (text|page|rects) reports nothing', async (t) => {
    const { env } = await loadedEnv(t);
    fakeSelection('same words');

    handlerOf(env, 'pointerup')();
    await settle();
    assert.equal(env.registry.reported.length, 1, 'the first report goes out');

    // The same selection again (the handlers also fire on unrelated reflows): deduped.
    handlerOf(env, 'pointerup')();
    handlerOf(env, 'selectionchange')();
    await debounceWindow();
    await settle();
    assert.equal(env.registry.reported.length, 1,
        'an unchanged text|page|rects key must not repeat the report');

    // A changed selection reports again.
    globalThis.__tmPdfViewerTestSelection = fakeSelection('same words', { left: 0, top: 5, width: 50, height: 10 });
    handlerOf(env, 'pointerup')();
    await settle();
    assert.equal(env.registry.reported.length, 2, 'different rects are a new selection');
});

test('clearSelection removes the ranges and resets the dedupe key', async (t) => {
    const { env, rootEl } = await loadedEnv(t);
    const selection = fakeSelection('quoted text');

    handlerOf(env, 'pointerup')();
    await settle();
    assert.equal(env.registry.reported.length, 1);

    env.viewer.clearSelection(rootEl);
    assert.equal(selection.removeAllRangesCalled, 1, 'clearSelection removes the DOM ranges');

    // The SAME selection reports again after the reset (the dismiss path must not
    // swallow the user's next selection).
    handlerOf(env, 'pointerup')();
    await settle();
    assert.equal(env.registry.reported.length, 2, 'clearSelection reset the report key');
});

test('destroy removes both selection listeners and the pending debounce timer', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const textLayer = fakeElement('text-layer');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;
    env.viewer.enableSelection(rootEl, textLayer, env.dotNet);
    await settle();

    assert.deepEqual(env.registry.listeners.added.map(l => l.type), ['selectionchange', 'pointerup'],
        'the debounced selectionchange and the immediate pointerup flush are separate listeners');
    assert.equal(env.registry.listeners.added[0].options.signal,
        env.registry.listeners.added[1].options.signal,
        'both selection listeners share the instance signal');

    env.viewer.destroy(rootEl);
    const removed = env.registry.listeners.removed.map(l => l.handler);
    assert.ok(removed.includes(env.registry.listeners.added[0].handler), 'destroy removes the selectionchange handler');
    assert.ok(removed.includes(env.registry.listeners.added[1].handler), 'destroy removes the pointerup handler');
});

// ══════════════════════════════════════════════════════════════════════════
// FZE-13 — continuous annotations: per-page geometry report, per-page
// selection resolution and the destroy teardown of the geometry plumbing.
// ══════════════════════════════════════════════════════════════════════════

/**
 * Continuous scroll wrap fake mirroring the production DOM contract: the
 * renderer owns a `.tm-pdf-viewer__continuous-pages` child and builds one page
 * wrap per page inside it; the Blazor overlay slots live in a sibling container.
 */
function fakeContinuousWrap() {
    const pagesEl = fakeElement('div');
    pagesEl.className = 'tm-pdf-viewer__continuous-pages';
    const container = fakeElement('container');
    container.className = 'tm-pdf-viewer__continuous-wrap';
    container.appendChild(pagesEl);
    container.clientHeight = 600;
    container.contains = () => true; // the selection anchor check passes for per-page layers
    container.querySelectorAll = (selector) => (
        typeof selector === 'string' && selector.includes('.tm-pdf-viewer__page')
            ? pagesEl.children.filter(c => String(c.className).includes('tm-pdf-viewer__page'))
            : []);
    container.querySelector = (selector) => {
        if (typeof selector === 'string' && selector.includes('.tm-pdf-viewer__continuous-pages')) return pagesEl;
        const match = typeof selector === 'string' ? selector.match(/data-page="(\d+)"/) : null;
        return match ? pagesEl.children.find(c => c.dataset?.page === match[1]) ?? null : null;
    };
    return { container, pagesEl };
}

/** Overrides document.createElement with the richer fake for the page wraps. */
function fakeCreateElement(t) {
    const originalCreate = globalThis.document.createElement;
    globalThis.document.createElement = () => fakeElement('div');
    t.after(() => {
        if (globalThis.document) globalThis.document.createElement = originalCreate;
    });
}

async function loadedContinuousEnv(t) {
    const env = await loadViewer(t);
    fakeCreateElement(t);
    const rootEl = fakeElement('root');
    env.docRequest.resolve(fakePdfDoc(10));
    await env.viewer.init(rootEl, 'https://example.com/continuous.pdf', env.dotNet);
    const { container, pagesEl } = fakeContinuousWrap();
    await env.viewer.renderAllPages(rootEl, container, 1, 0, { textLayer: true });
    await settle();
    return { env, rootEl, container, pagesEl };
}

test('renderAllPages reports the mounted band geometry to Blazor', async (t) => {
    const { env, container } = await loadedContinuousEnv(t);

    assert.equal(env.registry.geometryReports.length, 1,
        'the build sends one geometry report (deduped: identical repeats send nothing)');
    const [pages, tops, lefts, widths, heights, rotation] = env.registry.geometryReports[0];
    assert.deepEqual(pages, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
        'every page wrap sits inside the initial band (fake boxes all at the top)');
    assert.equal(tops.length, pages.length);
    assert.equal(lefts.length, pages.length);
    assert.equal(widths.length, pages.length);
    assert.equal(heights.length, pages.length);
    assert.equal(rotation, 0);
    assert.ok(container.querySelector('.tm-pdf-viewer__continuous-pages'),
        'the renderer owns its pages container inside the scroll wrap');
});

test('pageGeometry_AfterInPlaceZoom_ScalesSlots via a fresh report', async (t) => {
    const { env, rootEl, container, pagesEl } = await loadedContinuousEnv(t);

    // Simulate the in-place resize: every page box doubles (zoom 1 → 2).
    for (const [index, wrap] of pagesEl.children.entries()) {
        wrap.getBoundingClientRect = () => ({
            left: 0, top: index * 160, width: 200, height: 100,
        });
    }
    await env.viewer.updateContinuousScale(rootEl, container, 2);
    await settle();

    assert.ok(env.registry.geometryReports.length >= 2,
        'the in-place zoom sends a fresh geometry report (the boxes changed)');
    const last = env.registry.geometryReports.at(-1);
    // The doubled boxes are taller: the ±50% band (600px viewport) now covers
    // pages 1..6 only — the mount band keeps shrinking the slot set.
    assert.deepEqual(last[0], [1, 2, 3, 4, 5, 6]);
    assert.equal(last[3][0], 200, 'the report carries the SCALED slot width');
    assert.equal(last[4][0], 100, 'the report carries the SCALED slot height');
    assert.equal(last[5], 0);
});

test('continuous selection reports the anchor page with per-page normalized rects', async (t) => {
    const { env, rootEl, container, pagesEl } = await loadedContinuousEnv(t);
    env.viewer.enableSelection(rootEl, container, env.dotNet);
    await settle();

    // A selection anchored inside the page-7 wrap: one rect inside the fake page
    // box (100×50 at 0,0), one straddling its bottom edge, one on the next page.
    const wrap7 = pagesEl.children[6];
    wrap7.dataset.page = '7';
    globalThis.__tmPdfViewerTestSelection = {
        rangeCount: 1,
        isCollapsed: false,
        anchorNode: { nodeType: 3, parentElement: wrap7 },
        toString: () => 'page seven words',
        getRangeAt: () => ({
            getClientRects: () => [
                { left: 10, top: 10, width: 50, height: 10 },
                { left: 10, top: 45, width: 50, height: 10 },
                { left: 10, top: 80, width: 50, height: 10 },
            ],
        }),
        removeAllRanges() { },
    };

    handlerOf(env, 'pointerup')();
    await settle();

    assert.equal(env.registry.reported.length, 1, 'the gesture flush reports once');
    const [text, page, flat] = env.registry.reported[0];
    assert.equal(text, 'page seven words');
    assert.equal(page, 7, 'the page comes from the wrap containing the anchor node');
    assert.deepEqual(flat, [0.1, 0.2, 0.5, 0.2, 0.1, 0.9, 0.5, 0.1],
        'rects normalize against the anchor page; the cross-page rect is clamped');
});

test('continuous selection with an anchor outside any page reports nothing', async (t) => {
    const { env, rootEl, container } = await loadedContinuousEnv(t);
    env.viewer.enableSelection(rootEl, container, env.dotNet);
    await settle();

    globalThis.__tmPdfViewerTestSelection = {
        rangeCount: 1,
        isCollapsed: false,
        anchorNode: { nodeType: 3, parentElement: null },
        toString: () => 'stray words',
        getRangeAt: () => ({ getClientRects: () => [{ left: 0, top: 0, width: 10, height: 5 }] }),
        removeAllRanges() { },
    };
    handlerOf(env, 'pointerup')();
    await settle();

    assert.equal(env.registry.reported.length, 0,
        'a selection whose anchor resolves to no page is dropped');
});

test('a selection anchored inside an overlay slot reports nothing', async (t) => {
    const { env, rootEl, container } = await loadedContinuousEnv(t);
    env.viewer.enableSelection(rootEl, container, env.dotNet);
    await settle();

    // The Blazor overlay slots carry data-page too — but they live OUTSIDE the
    // renderer-owned .tm-pdf-viewer__continuous-pages, so a selection inside one
    // (a marker's number, a host overlay's text) must not report the slot's page.
    const slot = fakeElement('slot');
    slot.className = 'tm-pdf-viewer__page-overlay-slot';
    slot.dataset.page = '3';
    slot.closest = () => null; // not inside .tm-pdf-viewer__continuous-pages

    globalThis.__tmPdfViewerTestSelection = {
        rangeCount: 1,
        isCollapsed: false,
        anchorNode: { nodeType: 3, parentElement: slot },
        toString: () => 'marker number text',
        getRangeAt: () => ({ getClientRects: () => [{ left: 0, top: 0, width: 10, height: 5 }] }),
        removeAllRanges() { },
    };
    handlerOf(env, 'pointerup')();
    await settle();

    assert.equal(env.registry.reported.length, 0,
        'an overlay-slot anchor is not a page anchor');
});

test('destroy_UnregistersGeometryObservers', async (t) => {
    // Single-page: syncOverlay registers a ResizeObserver per overlay so the
    // geometry report follows canvas resizes; destroy must disconnect it.
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const overlay = fakeElement('overlay');
    env.docRequest.resolve(fakePdfDoc(3));
    await env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    await settle();
    env.viewer.syncOverlay(rootEl, overlay);
    await settle();
    const observers = env.registry.resizeObservers ?? [];
    assert.ok(observers.length >= 1, 'syncOverlay registers a geometry ResizeObserver');

    env.viewer.destroy(rootEl);
    for (const observer of observers) {
        assert.equal(observer.disconnected, true, 'destroy disconnects every geometry observer');
    }

    // Continuous (M1): renderAllPages observes the scroll CONTAINER itself, so a wrap
    // resize without a scroll (a flex sibling growing, a sheet settling) still
    // re-reports. destroy must disconnect it with the rest.
    const continuous = await loadViewer(t);
    fakeCreateElement(t);
    const contRoot = fakeElement('root');
    continuous.docRequest.resolve(fakePdfDoc(3));
    await continuous.viewer.init(contRoot, 'https://example.com/continuous.pdf', continuous.dotNet);
    const { container } = fakeContinuousWrap();
    await continuous.viewer.renderAllPages(contRoot, container, 1, 0, {});
    await settle();
    const containerObservers = continuous.registry.resizeObservers.slice();
    assert.ok(containerObservers.length >= 1,
        'renderAllPages registers the container geometry observer');
    assert.ok(containerObservers.some(o => o.observations.includes(container)),
        'the container geometry observer watches the scroll container');

    continuous.viewer.destroy(contRoot);
    for (const observer of containerObservers) {
        assert.equal(observer.disconnected, true,
            'destroy disconnects the container geometry observer too');
    }
});

test('pageGeometry reports the centered page-inner box, not the full-width wrap', async (t) => {
    const { env, rootEl, container, pagesEl } = await loadedContinuousEnv(t);

    // Production DOM (B1): each wrap is a full-width centered flex row whose inner
    // shrink-wraps the canvas — a page narrower than its container, centered. The
    // overlay slots must size to the INNER, never to the wrap's column width.
    for (const [index, wrap] of pagesEl.children.entries()) {
        const inner = fakeElement('inner');
        inner.className = 'tm-pdf-viewer__page-inner';
        wrap.querySelector = (selector) =>
            selector === ':scope > .tm-pdf-viewer__page-inner' ? inner : null;
        wrap.getBoundingClientRect = () => ({ left: 0, top: index * 60, width: 600, height: 50 });
        inner.getBoundingClientRect = () => ({ left: 200, top: index * 60, width: 200, height: 50 });
    }

    // A scale-only update ends in a fresh geometry report re-read from the DOM.
    await env.viewer.updateContinuousScale(rootEl, container, 1.5);
    await settle();

    const last = env.registry.geometryReports.at(-1);
    assert.equal(last[2][0], 200, 'the slot left is the INNER (centered) left, not the wrap left');
    assert.equal(last[3][0], 200, 'the slot width is the INNER width, not the full-width wrap');
    assert.equal(last[4][0], 50, 'the inner fills the wrap vertically, so the height is unchanged');
});

test('a container resize re-reports the geometry on the next frame', async (t) => {
    const { env, rootEl, container, pagesEl } = await loadedContinuousEnv(t);

    // Node has no rAF: run the frame callback synchronously for the test.
    const originalRaf = globalThis.requestAnimationFrame;
    globalThis.requestAnimationFrame = (fn) => { fn(); return 1; };
    t.after(() => {
        if (originalRaf === undefined) delete globalThis.requestAnimationFrame;
        else globalThis.requestAnimationFrame = originalRaf;
    });

    const observer = env.registry.resizeObservers.at(-1);
    assert.ok(observer, 'renderAllPages registered the container geometry observer');
    const reportsBefore = env.registry.geometryReports.length;

    // The container narrows: the centered page-inners shift and shrink (B1 geometry).
    for (const wrap of pagesEl.children) {
        const inner = { getBoundingClientRect: () => ({ left: 150, top: 0, width: 100, height: 50 }) };
        wrap.querySelector = (selector) =>
            selector === ':scope > .tm-pdf-viewer__page-inner' ? inner : null;
    }
    observer.callback();
    await settle();

    assert.ok(env.registry.geometryReports.length > reportsBefore,
        'the resize callback re-reports the geometry');
    const last = env.registry.geometryReports.at(-1);
    assert.equal(last[2][0], 150, 'the re-report follows the resized inner boxes');
});
