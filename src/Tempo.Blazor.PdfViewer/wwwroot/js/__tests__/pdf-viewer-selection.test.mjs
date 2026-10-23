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
        style: {},
        dataset: {},
        children: [],
        innerHTML: '',
        isConnected: true,
        appendChild(child) { this.children.push(child); },
        addEventListener() { },
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
    globalThis.ResizeObserver = class { observe() { } unobserve() { } disconnect() { } };
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
