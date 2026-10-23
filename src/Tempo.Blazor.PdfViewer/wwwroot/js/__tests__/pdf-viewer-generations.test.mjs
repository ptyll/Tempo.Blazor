// pdf-viewer.js generation-counter tests (buildGen vs paintGen) — the DOM build and
// the paint loop must have SEPARATE generations: a scale update racing a pending
// build must not leave an empty DOM, the IntersectionObserver callback must survive
// a scale-only update, and an interrupted size resolve must resolve again on the
// next rebuild.
//
// Same fake-IO harness as pdf-viewer-init.test.mjs, plus the REAL
// pdf-viewer-continuous.mjs copied next to the viewer copy so the scroll-anchor
// math under test is the shipping one. Page wraps get stacked geometry through
// their data-page, so metas/anchors are deterministic.
import test from 'node:test';
import assert from 'node:assert/strict';
import { copyFile, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const VIEWER_JS = new URL('../pdf-viewer.js', import.meta.url);
const CONTINUOUS_JS = new URL('../pdf-viewer-continuous.mjs', import.meta.url);

const PDF_STUB = `export const GlobalWorkerOptions = {};
export function getDocument() { return globalThis.__tmPdfViewerTestDocRequest; };`;

const SIZE_STUB = `export function canvasSize(width, height, dpr) {
  const cap = Math.min(dpr || 1, 2);
  return { width: Math.max(1, Math.round(width * cap)), height: Math.max(1, Math.round(height * cap)), cssWidth: width, cssHeight: height, dpr: cap };
};`;

const RENDER_STUB = `export async function renderPageToCanvas(options) {
  if (options?.canvas) options.canvas.__renderCount = (options.canvas.__renderCount ?? 0) + 1;
};
export function isRenderingCancelled(err) { return err?.name === 'RenderingCancelledException'; };`;

const PAGE_H = 800;
const VIEW_H = 600;

// The container the stacked-wrap geometry reads its scrollTop from; set per test.
let currentContainer = null;
// Height of a page wrap in the fake layout; a test changes it to emulate a
// rotation's aspect swap (portrait 800 → landscape 600).
let wrapHeight = PAGE_H;

function fakeNode(tag) {
    const el = {
        tagName: tag,
        className: '',
        style: {},
        dataset: {},
        attrs: {},
        children: [],
        width: 0,
        height: 0,
        isConnected: true,
        appendChild(child) { this.children.push(child); return child; },
        setAttribute(name, value) { this.attrs[name] = String(value); },
        getAttribute(name) { return this.attrs[name] ?? null; },
        removeAttribute(name) { delete this.attrs[name]; },
        addEventListener() { },
        removeEventListener() { },
        contains() { return false; },
        getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 50 }; },
        scrollIntoView() { },
        querySelector(selector) {
            if (typeof selector === 'string' && selector.includes('canvas')) {
                return this.children.flatMap(c => c.children).find(c => c.tagName === 'canvas') ?? null;
            }
            return null;
        },
        querySelectorAll() { return []; },
    };
    Object.defineProperty(el, 'innerHTML', {
        get: () => '',
        set: () => { el.children.length = 0; },
    });
    return el;
}

/** Scrollable continuous container: fixed clientHeight, settable scrollTop.
 * Mirrors the production DOM: the renderer owns a `.tm-pdf-viewer__continuous-pages`
 * child and builds one page wrap per page inside it. */
function fakeContainer(scrollTop = 0) {
    const container = fakeNode('container');
    container.scrollTop = scrollTop;
    container.clientHeight = VIEW_H;
    const pagesEl = fakeNode('div');
    pagesEl.className = 'tm-pdf-viewer__continuous-pages';
    container.appendChild(pagesEl);
    container.querySelectorAll = (selector) => (
        typeof selector === 'string' && selector.includes('.tm-pdf-viewer__page')
            ? pagesEl.children.filter(c => c.className === 'tm-pdf-viewer__page')
            : []
    );
    container.querySelector = (selector) => {
        if (typeof selector === 'string' && selector.includes('.tm-pdf-viewer__continuous-pages')) return pagesEl;
        const match = typeof selector === 'string' ? selector.match(/data-page="(\d+)"/) : null;
        return match ? pagesEl.children.find(c => c.dataset?.page === match[1]) ?? null : null;
    };
    return container;
}

/**
 * Fake document with per-page scale-1 boxes and holdable getPage promises: pages
 * listed in `heldPages` stay pending until releaseHeld(), so a chunked size
 * resolve can be observed (and interrupted) mid-pass.
 */
function fakePdfDoc(numPages, opts = {}) {
    const getPageCalls = [];
    const resolvers = new Map();
    const heldPages = opts.heldPages ?? [];
    const sizeOf = opts.sizeOf ?? (() => ({ width: 612, height: 792 }));
    const doc = {
        numPages,
        getPageCalls,
        destroyCalls: 0,
        async getPage(n) {
            getPageCalls.push(n);
            if (heldPages.includes(n)) {
                await new Promise(resolve => resolvers.set(n, resolve));
            }
            return {
                getViewport: ({ scale = 1, rotation = 0 } = {}) => {
                    const base = sizeOf(n);
                    const swap = rotation === 90 || rotation === 270;
                    return {
                        width: (swap ? base.height : base.width) * scale,
                        height: (swap ? base.width : base.height) * scale,
                        transform: [1, 0, 0, 1, 0, 0],
                    };
                },
                getTextContent: async () => ({ items: [] }),
            };
        },
        releaseHeld() { for (const resolve of resolvers.values()) resolve(); resolvers.clear(); },
        async destroy() { this.destroyCalls += 1; },
    };
    return doc;
}

function makeDocRequest() {
    let resolve;
    const promise = new Promise(res => { resolve = res; });
    return { promise, resolve };
}

const flush = () => new Promise(resolve => setImmediate(resolve));
const settle = async () => { for (let i = 0; i < 6; i++) await flush(); };

/** Loads a fresh viewer copy whose continuous module is the real shipping one. */
async function loadViewer(testContext, doc) {
    const registry = {
        abortControllers: [],
        intersectionObservers: [],
        loadedPages: [],
        loadErrors: [],
    };
    const docRequest = makeDocRequest();

    class SpyAbortController {
        constructor() { this.signal = {}; registry.abortControllers.push(this); }
        abort() { this.aborted = true; }
    }

    class SpyIntersectionObserver {
        constructor(callback, options) {
            this.callback = callback;
            this.options = options;
            this.observations = [];
            this.disconnected = false;
            registry.intersectionObservers.push(this);
        }
        observe(el) { this.observations.push(el); }
        unobserve() { }
        disconnect() { this.disconnected = true; }
    }

    const originals = {
        AbortController: globalThis.AbortController,
        IntersectionObserver: globalThis.IntersectionObserver,
        document: globalThis.document,
        window: globalThis.window,
    };
    globalThis.AbortController = SpyAbortController;
    globalThis.IntersectionObserver = SpyIntersectionObserver;
    globalThis.window = globalThis;
    globalThis.document = {
        currentScript: { src: '' },
        addEventListener() { },
        removeEventListener() { },
        // Page wraps created by renderAllPages get stacked geometry once their
        // data-page is set; everything else keeps a flat default box.
        createElement: (tag) => {
            const el = fakeNode(tag);
            // Height captured at creation: wraps built before a rotation keep the
            // old aspect, the rebuilt ones the new one — like a real re-layout.
            const hAtCreation = wrapHeight;
            el.__h = hAtCreation;
            el.getBoundingClientRect = function () {
                const page = parseInt(this.dataset?.page, 10);
                if (Number.isFinite(page) && page >= 1 && currentContainer) {
                    const siblings = currentContainer.querySelectorAll('.tm-pdf-viewer__page');
                    const idx = siblings.indexOf(this);
                    const top = idx >= 0
                        ? siblings.slice(0, idx).reduce((sum, s) => sum + (s.__h || hAtCreation), 0)
                        : (page - 1) * hAtCreation;
                    return {
                        left: 0,
                        top: top - (currentContainer.scrollTop || 0),
                        width: 600,
                        height: this.__h || hAtCreation,
                    };
                }
                return { left: 0, top: 0, width: 100, height: 50 };
            };
            return el;
        },
    };

    const dir = await mkdtemp(path.join(tmpdir(), 'tm-pdf-viewer-gen-'));
    await writeFile(path.join(dir, 'package.json'), JSON.stringify({ type: 'module' }), 'utf8');
    await writeFile(path.join(dir, 'pdf.min.mjs'), PDF_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-size.mjs'), SIZE_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-render.mjs'), RENDER_STUB, 'utf8');
    await copyFile(CONTINUOUS_JS, path.join(dir, 'pdf-viewer-continuous.mjs'));
    const viewerPath = path.join(dir, 'pdf-viewer.js');
    await copyFile(VIEWER_JS, viewerPath);

    globalThis.document.currentScript.src = pathToFileURL(viewerPath).href;
    globalThis.__tmPdfViewerTestDocRequest = docRequest;
    await import(pathToFileURL(viewerPath).href);
    const viewer = globalThis.window.tmPdfViewer;
    assert.ok(viewer, 'importing pdf-viewer.js must install window.tmPdfViewer');

    const dotNet = {
        invokeMethodAsync() { return Promise.resolve(); },
    };

    // A root without a single-page canvas — the continuous-only host shape.
    const rootEl = fakeNode('root');
    docRequest.resolve(doc ?? fakePdfDoc(4));
    await viewer.init(rootEl, 'https://example.com/gen.pdf', dotNet);

    testContext.after(async () => {
        globalThis.AbortController = originals.AbortController;
        if (originals.IntersectionObserver === undefined) delete globalThis.IntersectionObserver; else globalThis.IntersectionObserver = originals.IntersectionObserver;
        globalThis.document = originals.document;
        if (originals.window === undefined) delete globalThis.window; else globalThis.window = originals.window;
        delete globalThis.__tmPdfViewerTestDocRequest;
        currentContainer = null;
        wrapHeight = PAGE_H;
        await rm(dir, { recursive: true, force: true });
    });

    return { viewer, registry, docRequest, dotNet, rootEl };
}

const wrapsOf = (container) => container.querySelectorAll('.tm-pdf-viewer__page');
const canvasOf = (container, page) => container
    .querySelector(`[data-page="${page}"]`)
    ?.querySelector(':scope > .tm-pdf-viewer__page-inner > canvas') ?? null;

test('a scale update while renderAllPages is still pending does not leave an empty DOM', async (t) => {
    const env = await loadViewer(t);
    const container = fakeContainer();
    currentContainer = container;

    // renderAllPages suspends at its first await (module warm-up); the scale
    // update lands while the build is still in flight.
    const build = env.viewer.renderAllPages(env.rootEl, container, 1, 0, {});
    await env.viewer.updateContinuousScale(env.rootEl, container, 2.0);
    await build;
    await settle();

    const wraps = wrapsOf(container);
    assert.equal(wraps.length, 4, 'the pending rebuild must still have produced the page wraps');
    assert.ok(wraps.every(w => w.querySelector(':scope > .tm-pdf-viewer__page-inner > canvas')),
        'every wrap carries its canvas');
});

test('the IntersectionObserver callback still enqueues pages after updateContinuousScale', async (t) => {
    const env = await loadViewer(t);
    const container = fakeContainer();
    currentContainer = container;

    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, {});
    await settle();
    const observer = env.registry.intersectionObservers[0];
    assert.ok(observer, 'the rebuild registered a page observer');

    // A plain transition: page 2 intersects and the pump paints it.
    observer.callback([{ target: container.querySelector(':scope > [data-page="2"]'), isIntersecting: true }]);
    await settle();
    assert.equal(canvasOf(container, 2)?.dataset.rendered, '1',
        'the observer paints an intersecting page before the update');

    // A scale-only update must not poison the observer of the current build.
    await env.viewer.updateContinuousScale(env.rootEl, container, 2.0);
    await settle();
    assert.equal(canvasOf(container, 1)?.dataset.rendered, '1',
        'the page visible after the resize was re-queued and painted');
    assert.equal(canvasOf(container, 2)?.dataset.rendered, undefined,
        'the off-screen page 2 stays released after the resize');

    // The observer callback keeps working after the paintGen bump.
    observer.callback([{ target: container.querySelector(':scope > [data-page="3"]'), isIntersecting: true }]);
    await settle();
    assert.equal(canvasOf(container, 3)?.dataset.rendered, '1',
        'the observer callback still enqueues pages after a scale update');
});

test('an interrupted size resolve followed by a rebuild resolves again', async (t) => {
    const doc = fakePdfDoc(20, {
        heldPages: [9, 10, 11, 12, 13, 14, 15, 16],
        sizeOf: n => ({ width: 100 + n, height: 50 + n }),
    });
    const env = await loadViewer(t, doc);
    const container = fakeContainer();
    currentContainer = container;

    // First build: chunk 1 (pages 1-8) lands, the resolve then stalls on chunk 2.
    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, {});
    assert.equal(canvasOf(container, 12)?.style.width, '612px',
        'before its size is known, page 12 carries the page-1 fallback box');

    // Rebuild while the resolve is stuck — the first pass dies at its build-gen check.
    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, {});
    await settle(); // let the fresh resolve reach its own held chunk
    doc.releaseHeld();
    await settle();

    assert.equal(canvasOf(container, 12)?.style.width, '112px',
        'the partial cache was not treated as resolved: the rebuild resolved again');
    assert.equal(canvasOf(container, 17)?.style.width, '117px',
        'the second pass also covered the final chunk');
    assert.equal(canvasOf(container, 1)?.style.width, '101px', 'page 1 keeps its own resolved size');
});

test('a first build with currentPage N restores the scroll to that page top', async (t) => {
    const env = await loadViewer(t, fakePdfDoc(6));
    const container = fakeContainer();
    currentContainer = container;

    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, { currentPage: 3 });
    await settle();

    assert.equal(container.scrollTop, 2 * PAGE_H,
        'with no anchor (empty container before the build) the scroll lands on page 3 top');
});

test('a rebuild over existing wraps keeps the scroll anchored across the rotation', async (t) => {
    const env = await loadViewer(t, fakePdfDoc(4));
    const container = fakeContainer(1000); // inside page 2 (800..1600), a quarter in
    currentContainer = container;

    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, {});
    await settle();
    assert.equal(container.scrollTop, 1000, 'sanity: the initial build kept the scroll');

    // Rotate: the pages turn landscape (the fake layout shrinks), the wraps are
    // rebuilt and the anchor must put the same content point back. The fallback
    // placeholder comes from the cached rotation-0 page box, axes swapped.
    wrapHeight = 600;
    await env.viewer.renderAllPages(env.rootEl, container, 1, 90, {});
    await settle();

    // Anchor: page 2, ratio (1000-800)/800 = 0.25 → 600 + 0.25*600 = 750.
    assert.equal(container.scrollTop, 750,
        'the rotation rebuild restored the content point against the new geometry');
    assert.equal(canvasOf(container, 1)?.style.width, '792px',
        'the rotation placeholder is the cached portrait box with swapped axes');
    assert.equal(canvasOf(container, 1)?.style.height, '612px');
});
