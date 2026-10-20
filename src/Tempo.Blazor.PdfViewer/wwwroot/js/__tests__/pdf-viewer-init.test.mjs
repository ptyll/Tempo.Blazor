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
        loadedPages: [],
        loadErrors: [],
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

    const originals = {
        AbortController: globalThis.AbortController,
        ResizeObserver: globalThis.ResizeObserver,
        document: globalThis.document,
        window: globalThis.window,
    };
    globalThis.AbortController = SpyAbortController;
    globalThis.ResizeObserver = SpyResizeObserver;
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
            return Promise.resolve();
        },
    };

    testContext.after(async () => {
        globalThis.AbortController = originals.AbortController;
        globalThis.ResizeObserver = originals.ResizeObserver;
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
    assert.equal(env.registry.listeners.added.length, 1);
    assert.equal(env.registry.listeners.added[0].type, 'mouseup');
    assert.equal(env.registry.listeners.added[0].options.signal, env.registry.abortControllers[0].signal,
        'the selection handler must bind to the instance signal created at init time');

    env.viewer.destroy(rootEl);
    assert.equal(env.registry.listeners.removed.length, 1, 'destroy removes the selection handler');
    assert.equal(env.registry.listeners.removed[0].handler, env.registry.listeners.added[0].handler);
    assert.equal(env.registry.resizeObservers[0].disconnected, true, 'destroy disconnects overlay observers');
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
