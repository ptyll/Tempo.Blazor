// pdf-viewer.js init/destroy lifecycle tests — the pending-load entry registered by
// init() must be flipped to loaded IN PLACE (the abort signal created at init time
// stays the instance signal), enableSelection/syncOverlay must no-op while the
// document is still downloading, and destroy() during a pending load must abort the
// controller and let the stale download be discarded silently.
//
// pdf-viewer.js dynamically imports pdf.min.mjs / pdf-viewer-size.mjs /
// pdf-viewer-render.mjs from its own directory, so each test copies the real
// pdf-viewer.js into a temp dir next to tiny stubs of those three modules and imports
// the copy — giving every test a fresh window.tmPdfViewer with controllable downloads.
import test from 'node:test';
import assert from 'node:assert/strict';
import { copyFile, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const VIEWER_JS = new URL('../pdf-viewer.js', import.meta.url);

// getDocument() hands back whatever the test last staged, so a download can be kept
// in flight while the assertions run.
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

// Mirrors the real pdf-viewer-filesize.mjs contract: reads the length off the open
// document's getDownloadInfo() — the promise a test can keep pending indefinitely.
const FILESIZE_STUB = `export async function documentLength(pdfDoc) {
  if (!pdfDoc || typeof pdfDoc.getDownloadInfo !== 'function') return null;
  const info = await pdfDoc.getDownloadInfo();
  return Number.isFinite(info?.length) && info.length >= 0 ? Math.round(info.length) : null;
};`;

function fakeElement(tag) {
    return {
        tagName: tag,
        style: {},
        dataset: {},
        children: [],
        innerHTML: '',
        appendChild(child) { this.children.push(child); },
        addEventListener() { },
        removeEventListener() { },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        contains() { return false; },
        getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 50 }; },
        scrollIntoView() { },
    };
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
        destroyCalls: 0,
        async destroy() { this.destroyCalls += 1; },
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
    let reject;
    const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}

const flush = () => new Promise(resolve => setImmediate(resolve));

async function loadViewer(testContext) {
    const registry = {
        abortControllers: [],
        listeners: { added: [], removed: [] },
        resizeObservers: [],
        intersectionObservers: [],
        loadedPages: [],
        loadErrors: [],
        documentLengths: [],
    };
    const docRequest = makeDocRequest();

    class SpyAbortController {
        constructor() {
            this.signal = {};
            registry.abortControllers.push(this);
        }

        abort() { this.aborted = true; }
    }

    class SpyResizeObserver {
        constructor(callback) {
            this.callback = callback;
            this.observations = [];
            registry.resizeObservers.push(this);
        }

        observe(el) { this.observations.push(el); }
        unobserve() { }
        disconnect() { this.disconnected = true; }
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
        ResizeObserver: globalThis.ResizeObserver,
        IntersectionObserver: globalThis.IntersectionObserver,
        document: globalThis.document,
        window: globalThis.window,
    };
    globalThis.AbortController = SpyAbortController;
    globalThis.ResizeObserver = SpyResizeObserver;
    globalThis.IntersectionObserver = SpyIntersectionObserver;
    globalThis.window = globalThis;
    globalThis.document = {
        currentScript: { src: '' },
        addEventListener(type, handler, options) { registry.listeners.added.push({ type, handler, options }); },
        removeEventListener(type, handler) { registry.listeners.removed.push({ type, handler }); },
        createElement: () => fakeElement('div'),
    };

    // Fresh temp dir per test: a copy of the real pdf-viewer.js plus the three stubbed
    // dynamic imports it resolves next to itself.
    const dir = await mkdtemp(path.join(tmpdir(), 'tm-pdf-viewer-test-'));
    await writeFile(path.join(dir, 'package.json'), JSON.stringify({ type: 'module' }), 'utf8');
    await writeFile(path.join(dir, 'pdf.min.mjs'), PDF_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-size.mjs'), SIZE_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-render.mjs'), RENDER_STUB, 'utf8');
    await writeFile(path.join(dir, 'pdf-viewer-filesize.mjs'), FILESIZE_STUB, 'utf8');
    const viewerPath = path.join(dir, 'pdf-viewer.js');
    await copyFile(VIEWER_JS, viewerPath);

    // currentScript is captured while the module evaluates — point it at the copy so the
    // dynamic imports land on the stubs.
    globalThis.document.currentScript.src = pathToFileURL(viewerPath).href;
    globalThis.__tmPdfViewerTestDocRequest = docRequest;

    await import(pathToFileURL(viewerPath).href);
    const viewer = globalThis.window.tmPdfViewer;
    assert.ok(viewer, 'importing pdf-viewer.js must install window.tmPdfViewer');

    const dotNet = {
        invokeMethodAsync(name, ...args) {
            if (name === 'OnPdfLoaded') registry.loadedPages.push(args[0]);
            if (name === 'OnPdfLoadError') registry.loadErrors.push(args[0]);
            if (name === 'OnDocumentLengthKnown') registry.documentLengths.push(args[0]);
            return Promise.resolve();
        },
    };

    testContext.after(async () => {
        globalThis.AbortController = originals.AbortController;
        globalThis.ResizeObserver = originals.ResizeObserver;
        if (originals.IntersectionObserver === undefined) delete globalThis.IntersectionObserver; else globalThis.IntersectionObserver = originals.IntersectionObserver;
        globalThis.document = originals.document;
        if (originals.window === undefined) delete globalThis.window; else globalThis.window = originals.window;
        delete globalThis.__tmPdfViewerTestDocRequest;
        await rm(dir, { recursive: true, force: true });
    });

    return { viewer, registry, docRequest, dotNet };
}

function fakeCanvas() {
    const canvas = fakeElement('canvas');
    canvas.offsetLeft = 0;
    canvas.offsetTop = 0;
    canvas.offsetWidth = 100;
    canvas.offsetHeight = 50;
    return canvas;
}

test('enableSelection and syncOverlay no-op while pending, then apply to the loaded entry in place', async (t) => {
    const env = await loadViewer(t);
    const canvas = fakeCanvas();
    const rootEl = fakeRootEl(canvas);
    const doc = fakePdfDoc(3);

    // The download is held open until the assertions had their chance to run
    // against the pending entry.
    const initPromise = env.viewer.init(rootEl, 'https://example.com/pending.pdf', env.dotNet);
    await flush();

    const overlay = fakeElement('overlay');
    env.viewer.syncOverlay(rootEl, overlay);
    assert.equal(overlay.style.left, undefined, 'overlay must not be positioned while the document is pending');
    assert.equal(env.registry.resizeObservers.length, 0, 'no ResizeObserver may observe a canvas before the document exists');

    const textLayer = fakeElement('text-layer');
    env.viewer.enableSelection(rootEl, textLayer, env.dotNet);
    assert.equal(env.registry.listeners.added.length, 0, 'no document-level selection handler while the document is pending');

    // Finish the download: the pending entry flips to loaded IN PLACE.
    env.docRequest.resolve(doc);
    await initPromise;

    assert.deepEqual(env.registry.loadedPages, [3], 'OnPdfLoaded reports the page count once the download completes');
    assert.equal(env.registry.abortControllers.length, 1,
        'loading must not rebuild the state: the abort controller created at init time survives');
    assert.equal(env.viewer.getTotalPages(rootEl), 3);

    // After the load the deferred one-shot setup applies — and it binds to the SAME
    // instance signal, which only the in-place update guarantees.
    env.viewer.syncOverlay(rootEl, overlay);
    assert.equal(overlay.style.left, '0px', 'overlay is aligned once the document is loaded');
    assert.equal(env.registry.resizeObservers.length, 1);
    assert.equal(env.registry.resizeObservers[0].observations[0], canvas);

    env.viewer.enableSelection(rootEl, textLayer, env.dotNet);
    assert.equal(env.registry.listeners.added.length, 2,
        'touch selection binds both the selectionchange and the pointerup listeners');
    assert.deepEqual(env.registry.listeners.added.map(l => l.type), ['selectionchange', 'pointerup'],
        'FZE-9: touch selection reports on selectionchange + pointerup (not mouseup)');
    assert.equal(env.registry.listeners.added[0].options.signal, env.registry.abortControllers[0].signal,
        'the selection handler must bind to the instance signal created at init time');
    assert.equal(env.registry.listeners.added[1].options.signal, env.registry.abortControllers[0].signal,
        'both selection listeners share the instance signal');

    env.viewer.destroy(rootEl);
    assert.equal(env.registry.listeners.removed.length, 2, 'destroy removes the selection handlers');
    assert.equal(env.registry.listeners.removed[0].handler, env.registry.listeners.added[0].handler);
    assert.equal(env.registry.listeners.removed[1].handler, env.registry.listeners.added[1].handler);
    assert.equal(env.registry.resizeObservers[0].disconnected, true, 'destroy disconnects overlay observers');
});

// ── FZE-11: the file-size lookup never blocks init ────────────────────────────

test('init resolves while getDownloadInfo is still pending and reports the length later', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const doc = fakePdfDoc(3);
    let resolveLength;
    // getDownloadInfo stays pending until the test resolves it: a BLOCKING lookup
    // would hold init (and the first paint) hostage until then.
    doc.getDownloadInfo = () => new Promise(resolve => { resolveLength = resolve; });

    const initPromise = env.viewer.init(rootEl, 'https://example.com/slow-size.pdf', env.dotNet);
    env.docRequest.resolve(doc);
    await Promise.race([
        initPromise,
        new Promise((_, reject) => setTimeout(() => reject(new Error('init awaited the file-size lookup')), 2000)),
    ]);

    assert.deepEqual(env.registry.loadedPages, [3], 'OnPdfLoaded fired before the size was known');
    assert.deepEqual(env.registry.documentLengths, [], 'no length is reported while getDownloadInfo is pending');

    // Let the background module import settle so documentLength() has called
    // getDownloadInfo() (the executor assigns resolveLength).
    await flush();
    assert.equal(typeof resolveLength, 'function', 'the background lookup started');

    resolveLength({ length: 123456 });
    await flush();
    assert.deepEqual(env.registry.documentLengths, [123456],
        'the background lookup reports the length once getDownloadInfo settles');
});

test('destroy() during a pending load aborts the entry and the stale download is discarded', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const staleDoc = fakePdfDoc(4);
    const freshDoc = fakePdfDoc(7);

    const staleInit = env.viewer.init(rootEl, 'https://example.com/stale.pdf', env.dotNet);
    await flush();
    assert.equal(env.registry.abortControllers.length, 1);

    // Destroy while the download is still in flight.
    env.viewer.destroy(rootEl);
    assert.equal(env.registry.abortControllers[0].aborted, true,
        'the pending-branch destroy must abort the state controller');
    assert.equal(env.registry.resizeObservers.length, 0);

    // A newer init for the same root loads its own document and reports it.
    const freshRequest = makeDocRequest();
    globalThis.__tmPdfViewerTestDocRequest = freshRequest;
    const freshInit = env.viewer.init(rootEl, 'https://example.com/fresh.pdf', env.dotNet);
    await flush();
    freshRequest.resolve(freshDoc);
    await freshInit;
    assert.deepEqual(env.registry.loadedPages, [7], 'only the newest generation reports OnPdfLoaded');

    // The stale download finishing afterwards stays quiet and closes its document.
    env.docRequest.resolve(staleDoc);
    await staleInit;
    assert.deepEqual(env.registry.loadedPages, [7], 'the superseded load must not report OnPdfLoaded');
    assert.deepEqual(env.registry.loadErrors, [], 'the superseded load must not report OnPdfLoadError');
    assert.equal(staleDoc.destroyCalls, 1, 'the superseded document is destroyed');
});

// ── Continuous DOM lifecycle (fake IntersectionObserver + scrollable container) ──

/**
 * A DOM node shaped like the nodes pdf-viewer.js builds for the continuous mode:
 * class/attribute plumbing, a page-inner → canvas hierarchy for the lookups and an
 * innerHTML setter that really clears the children (a rebuild replaces the wraps).
 */
function fakeDomEl(tag) {
    const el = {
        tagName: tag,
        className: '',
        style: {},
        dataset: {},
        attrs: {},
        children: [],
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

/** The continuous wrap: a scrollable container the renderer fills with page wraps. */
function fakeContinuousContainer() {
    const container = fakeDomEl('container');
    container.scrollTop = 0;
    container.clientHeight = 600;
    container.addEventListener = () => { };
    container.querySelectorAll = (selector) => (
        typeof selector === 'string' && selector.includes('.tm-pdf-viewer__page')
            ? container.children.filter(c => c.className === 'tm-pdf-viewer__page')
            : []
    );
    container.querySelector = (selector) => {
        const match = typeof selector === 'string' ? selector.match(/\[data-page="(\d+)"\]/) : null;
        return match ? container.children.find(c => c.dataset?.page === match[1]) ?? null : null;
    };
    return container;
}

async function loadContinuousViewer(testContext) {
    const env = await loadViewer(testContext);
    // renderAllPages creates the wraps through document.createElement — swap in the
    // richer fake (loadViewer's own after-hook restores the original document).
    const originalCreate = globalThis.document.createElement;
    globalThis.document.createElement = () => fakeDomEl('div');
    testContext.after(() => {
        // loadViewer's after-hook may already have restored the original document
        // (undefined in the node env) — guard so the restore never throws.
        if (globalThis.document) globalThis.document.createElement = originalCreate;
    });

    // A root without a single-page canvas: init() skips the page-1 render, exactly
    // like a viewer that starts (or switches) directly in continuous mode.
    const rootEl = fakeElement('root');
    env.docRequest.resolve(fakePdfDoc(4));
    await env.viewer.init(rootEl, 'https://example.com/continuous.pdf', env.dotNet);
    return { ...env, rootEl };
}

test('renderAllPages twice disconnects the first observer and aborts the first scroll signal', async (t) => {
    const env = await loadContinuousViewer(t);
    const container = fakeContinuousContainer();

    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, { textLayer: true });
    await flush();
    assert.equal(env.registry.intersectionObservers.length, 1, 'the first rebuild registers one IntersectionObserver');
    const firstObserver = env.registry.intersectionObservers[0];
    assert.equal(firstObserver.disconnected, false);
    assert.equal(firstObserver.observations.length, 4, 'every page wrap is observed');
    // init state controller (0) + scroll controller #1 (1).
    assert.equal(env.registry.abortControllers.length, 2);

    await env.viewer.renderAllPages(env.rootEl, container, 1.5, 0, { textLayer: true });
    await flush();
    assert.equal(env.registry.intersectionObservers.length, 2, 'the rebuild registers a fresh observer');
    assert.equal(firstObserver.disconnected, true, 'the first observer is disconnected by the rebuild');
    assert.equal(env.registry.abortControllers[1].aborted, true, 'the first scroll tracker signal is aborted');
    assert.ok(!env.registry.abortControllers[2].aborted, 'the fresh scroll tracker is still live');
});

test('destroy disconnects the page observer and aborts the scroll controller', async (t) => {
    const env = await loadContinuousViewer(t);
    const container = fakeContinuousContainer();

    await env.viewer.renderAllPages(env.rootEl, container, 1, 0, {});
    await flush();

    env.viewer.destroy(env.rootEl);
    assert.equal(env.registry.intersectionObservers[0].disconnected, true,
        'destroy disconnects the per-wrap IntersectionObserver');
    assert.equal(env.registry.abortControllers[1].aborted, true,
        'destroy aborts the scroll tracker signal');
});
