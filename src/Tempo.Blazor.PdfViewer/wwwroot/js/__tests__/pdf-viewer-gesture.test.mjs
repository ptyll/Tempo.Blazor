// pdf-viewer.js FZE-9 touch interaction tests — the two-finger pinch/pan zoom MUST come from
// the core sibling module pointer-gesture.js (docs/touch-interaction.md, "Consuming from a
// split package"): pdf-viewer.js resolves it by walking up from its own script directory, so
// the harness arranges the temp dir as the two static-web-asset packages sit side by side
// (`<tmp>/Tempo.Blazor.PdfViewer/js/pdf-viewer.js` + `<tmp>/Tempo.Blazor/js/pointer-gesture.js`).
// The touch text selection reports on selectionchange + pointerup, and the text layer keeps
// the text invisible (color: transparent) so the native selection highlight paints at full
// strength under a finger.
import test from 'node:test';
import assert from 'node:assert/strict';
import { copyFile, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const VIEWER_JS = new URL('../pdf-viewer.js', import.meta.url);

const PDF_STUB = `export const GlobalWorkerOptions = {};
export const Util = { transform: () => [1, 0, 0, 1, 0, 0] };
export function getDocument() { return globalThis.__tmPdfViewerTestDocRequest; };
// Minimal TextLayer mirror with the same DOM contract as PDF.js v5: a span per text
// item and a <br> for every hasEOL item (the class positions and sizes them itself).
export class TextLayer {
    constructor({ textContentSource, container, viewport }) {
        this.textContentSource = textContentSource;
        this.container = container;
        this.viewport = viewport;
    }
    async render() {
        const el = this.container;
        el.__textLayerContract = { scale: this.viewport.scale, spans: [], brs: 0 };
        for (const item of this.textContentSource.items) {
            if (item.str !== '') {
                const span = { textContent: item.str };
                el.children.push(span);
                el.__textLayerContract.spans.push(item.str);
            }
            if (item.hasEOL) {
                el.children.push({ br: true });
                el.__textLayerContract.brs++;
            }
        }
    }
};`;

const SIZE_STUB = `export function canvasSize(width, height, dpr) {
  const cap = Math.min(dpr || 1, 2);
  return { width: Math.max(1, Math.round(width * cap)), height: Math.max(1, Math.round(height * cap)), cssWidth: width, cssHeight: height, dpr: cap };
};`;

const RENDER_STUB = `export async function renderPageToCanvas(options) {
  if (options?.canvas) options.canvas.__renderCount = (options.canvas.__renderCount ?? 0) + 1;
};
export function isRenderingCancelled(err) { return err?.name === 'RenderingCancelledException'; };`;

// The stub of the CORE pinch module: records every attach call and hands the test the
// handle so it can drive onStart/onChange/onEnd by hand.
const GESTURE_STUB = `export const attachCalls = [];
export function attachPinch(element, options = {}) {
  const handle = {
    element, options, detached: false,
    detach() { this.detached = true; },
    cancel() { },
    get active() { return false; },
  };
  attachCalls.push(handle);
  globalThis.__tmPdfViewerPinchHandles = attachCalls;
  return handle;
};`;

function fakeElement(tag) {
    // The style object doubles as the custom-property store: setProperty assigns it.
    const style = {
        setProperty(name, value) { this[name] = value; },
    };
    return {
        tagName: tag,
        style,
        dataset: {},
        children: [],
        innerHTML: '',
        isConnected: true,
        listeners: [],
        appendChild(child) { this.children.push(child); },
        addEventListener(type, handler, options) { this.listeners.push({ type, handler, options }); },
        removeEventListener(type, handler) { this.listeners = this.listeners.filter(l => l.handler !== handler); },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        contains() { return false; },
        getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 50 }; },
        scrollIntoView() { },
        scrollLeft: 0,
        scrollTop: 0,
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
        async destroy() { },
        async getPage() {
            return {
                userUnit: 1,
                getViewport: () => ({ width: 100, height: 50, scale: 1, transform: [1, 0, 0, 1, 0, 0] }),
                getTextContent: async () => ({
                    items: [
                        { str: 'First line of body text', transform: [10, 0, 0, 10, 4, 12], fontName: 'g_d0_f1', hasEOL: true },
                        { str: 'Second line of body text', transform: [10, 0, 0, 10, 4, 24], fontName: 'g_d0_f1' },
                    ],
                }),
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

async function loadViewer(testContext) {
    const registry = {
        listeners: { added: [], removed: [] },
        resizeObservers: [],
        loadedPages: [],
        pinched: [],
        loadErrors: [],
    };
    const docRequest = makeDocRequest();

    const originals = {
        ResizeObserver: globalThis.ResizeObserver,
        document: globalThis.document,
        window: globalThis.window,
    };
    globalThis.ResizeObserver = class { observe() { } unobserve() { } disconnect() { } };
    globalThis.window = globalThis;
    globalThis.document = {
        currentScript: { src: '' },
        addEventListener(type, handler, options) { registry.listeners.added.push({ type, handler, options }); },
        removeEventListener(type, handler) { registry.listeners.removed.push({ type, handler }); },
        createElement: () => fakeElement('div'),
    };

    // The split-package layout: pdf-viewer.js ships in Tempo.Blazor.PdfViewer/js and resolves
    // the core sibling from ../../Tempo.Blazor/js/.
    const dir = await mkdtemp(path.join(tmpdir(), 'tm-pdf-gesture-test-'));
    const viewerDir = path.join(dir, 'Tempo.Blazor.PdfViewer', 'js');
    const coreDir = path.join(dir, 'Tempo.Blazor', 'js');
    await mkdir(viewerDir, { recursive: true });
    await mkdir(coreDir, { recursive: true });
    await writeFile(path.join(viewerDir, 'package.json'), JSON.stringify({ type: 'module' }), 'utf8');
    await writeFile(path.join(viewerDir, 'pdf.min.mjs'), PDF_STUB, 'utf8');
    await writeFile(path.join(viewerDir, 'pdf-viewer-size.mjs'), SIZE_STUB, 'utf8');
    await writeFile(path.join(viewerDir, 'pdf-viewer-render.mjs'), RENDER_STUB, 'utf8');
    await writeFile(path.join(coreDir, 'pointer-gesture.js'), GESTURE_STUB, 'utf8');
    const viewerPath = path.join(viewerDir, 'pdf-viewer.js');
    await copyFile(VIEWER_JS, viewerPath);

    globalThis.document.currentScript.src = pathToFileURL(viewerPath).href;
    globalThis.__tmPdfViewerTestDocRequest = docRequest;

    await import(pathToFileURL(viewerPath).href);
    const viewer = globalThis.window.tmPdfViewer;
    assert.ok(viewer, 'importing pdf-viewer.js must install window.tmPdfViewer');

    const dotNet = {
        invokeMethodAsync(name, ...args) {
            if (name === 'OnPdfLoaded') registry.loadedPages.push(args[0]);
            if (name === 'OnPinchZoomEnded') registry.pinched.push(args[0]);
            if (name === 'OnPdfLoadError') registry.loadErrors.push(args[0]);
            return Promise.resolve();
        },
    };

    testContext.after(async () => {
        globalThis.ResizeObserver = originals.ResizeObserver;
        globalThis.document = originals.document;
        if (originals.window === undefined) delete globalThis.window; else globalThis.window = originals.window;
        delete globalThis.__tmPdfViewerTestDocRequest;
        delete globalThis.__tmPdfViewerPinchHandles;
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

test('pinch zoom attaches to the scroll wrap through the core sibling module', async (t) => {
    const env = await loadViewer(t);
    const canvas = fakeCanvas();
    const rootEl = fakeRootEl(canvas);
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();

    const handles = globalThis.__tmPdfViewerPinchHandles;
    assert.ok(handles && handles.length === 1, 'the pinch is attached exactly once');
    assert.equal(handles[0].element, wrap, 'the pinch listens on the scroll wrap');
    assert.deepEqual(handles[0].options.pointerTypes, ['touch'],
        'mouse/pen cannot pinch — ctrl+wheel covers the trackpads');
    assert.equal(handles[0].options.touchAction, null,
        'CSS owns touch-action: pan-x pan-y keeps the one-finger scroll and blocks the native page pinch');
    assert.equal(handles[0].options.captureSinglePointer, false,
        'the annotator\u2019s ink surface is a descendant listener — a lone finger must stay with it');
    assert.equal(wrap.style.touchAction, undefined, 'the helper must not write an inline touch-action');
});

test('the live gesture transforms the wrap around the finger centre and the commit re-renders', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();
    const pinch = globalThis.__tmPdfViewerPinchHandles[0];

    pinch.options.onStart({ centerX: 30, centerY: 20, distance: 100 });
    pinch.options.onChange({ scale: 2, dx: 5, dy: 10 });
    assert.equal(wrap.style.transform, 'translate(5px, 10px) scale(2)',
        'the live gesture is a CSS transform');
    assert.equal(wrap.style.transformOrigin, '30px 20px',
        'the zoom happens around the fingers\u2019 start centre');

    pinch.options.onEnd({ scale: 8, dx: 5, dy: 10 });
    assert.equal(wrap.style.transform, '', 'the commit clears the live transform (the re-render replaces it)');
    assert.equal(wrap.style.transformOrigin, '');
    assert.equal(wrap.scrollLeft, -5, 'the two-finger pan becomes scroll');
    assert.equal(wrap.scrollTop, -10);

    await settle();
    assert.deepEqual(env.registry.pinched, [5],
        'OnPinchZoomEnded receives the ABSOLUTE scale clamped to the documented bounds');
});

test('an out-of-range pinch commits at the bounds, a cancelled one commits nothing', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();
    const pinch = globalThis.__tmPdfViewerPinchHandles[0];

    pinch.options.onStart({ centerX: 0, centerY: 0, distance: 100 });
    pinch.options.onChange({ scale: 0.01, dx: 0, dy: 0 });
    pinch.options.onEnd({ scale: 0.01, dx: 0, dy: 0 });
    await settle();
    assert.deepEqual(env.registry.pinched, [0.1], 'the absolute scale is clamped to the minimum');

    pinch.options.onStart({ centerX: 0, centerY: 0, distance: 10 });
    pinch.options.onChange({ scale: 2, dx: 3, dy: 0 });
    pinch.options.onCancel({ reason: 'pointercancel' });
    assert.equal(wrap.style.transform, '', 'a cancelled gesture rolls the transform back');
    await settle();
    assert.deepEqual(env.registry.pinched, [0.1], 'a cancelled gesture never commits');
});

test('destroy detaches the pinch and a re-bind hands over to the fresh wrap', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();

    // A view-mode swap re-created the wrap: the new element takes over.
    const freshWrap = fakeElement('wrap2');
    env.viewer.enablePinchZoom(rootEl, freshWrap, env.dotNet);
    await settle();

    let handles = globalThis.__tmPdfViewerPinchHandles;
    assert.equal(handles.length, 2);
    assert.equal(handles[0].detached, true, 'the old wrap\u2019s pinch is detached');
    assert.equal(handles[1].element, freshWrap);
    assert.equal(handles[1].detached, false);

    env.viewer.destroy(rootEl);
    assert.equal(handles[1].detached, true, 'destroy detaches the live pinch');
});

test('enablePinchZoom no-ops before the document is loaded', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/pending.pdf', env.dotNet);
    await flush();

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();
    assert.equal(globalThis.__tmPdfViewerPinchHandles, undefined,
        'no pinch may attach against a pending (canvas-less) state');
});

test('the text layer is built by the PDF.js TextLayer with the v5 scale-factor contract', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const textLayer = fakeElement('text-layer');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    await env.viewer.renderTextLayer(rootEl, textLayer, 1, 1, 0);

    // PDF.js v5 sizes the layer and its spans through the --total-scale-factor custom
    // property — the container must carry it or the layer collapses to zero.
    assert.equal(textLayer.style['--scale-factor'], '1',
        'the layer container carries the PDF.js v5 --scale-factor variable');
    assert.equal(textLayer.style['--user-unit'], '1', 'the page user unit feeds the total scale');
    assert.equal(textLayer.style['--total-scale-factor'], 'calc(var(--scale-factor) * var(--user-unit))',
        'the total scale factor resolves through the user unit');
    const contract = textLayer.__textLayerContract;
    assert.ok(contract, 'the layer is rendered by the TextLayer class (not hand-rolled divs)');
    assert.equal(contract.scale, 1, 'the TextLayer receives the render viewport');
});

test('a multi-line selection reads with a line break between the lines', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const textLayer = fakeElement('text-layer');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    await env.viewer.renderTextLayer(rootEl, textLayer, 1, 1, 0);

    // The browser walks the selected range in DOM order: span text plus '\n' per <br>.
    // The old hand-rolled layer had no line breaks at all — the lines ran together
    // ("First line…Second line…") and the boxes sat on the baseline.
    const selectionText = textLayer.children.map(c => (c.br ? '\n' : c.textContent)).join('');
    assert.ok(selectionText.includes('\n'),
        `the two text items must be separated by the hasEOL line break (got '${selectionText}')`);
    assert.equal(selectionText, 'First line of body text\nSecond line of body text',
        'the selection text keeps each line intact');
});

test('M6: a non-passive touchmove listener on the wrap blocks the native two-finger zoom', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();

    const entry = wrap.listeners.find(l => l.type === 'touchmove');
    assert.ok(entry, 'enablePinchZoom binds a touchmove guard on the scroll wrap');
    assert.equal(entry.options.passive, false, 'the guard must be non-passive to allow preventDefault');
    assert.ok(entry.options.signal instanceof AbortSignal,
        'the guard binds to the instance abort signal');

    let prevented = 0;
    entry.handler({ touches: [1, 2], preventDefault: () => { prevented++; } });
    assert.equal(prevented, 1, 'two fingers: the native page zoom is prevented');
    entry.handler({ touches: [1], preventDefault: () => { prevented++; } });
    assert.equal(prevented, 1, 'a lone finger keeps the native scroll (no preventDefault)');

    env.viewer.destroy(rootEl);
    assert.equal(entry.options.signal.aborted, true,
        'the guard dies with the instance abort signal');
});

test('M5: the pinch commit anchors the scroll so the fingers keep their content point', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', env.dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, env.dotNet);
    await settle();
    const pinch = globalThis.__tmPdfViewerPinchHandles[0];

    // The fingers sit 30/20px into the wrap; the content under them is at
    // scroll (10, 5). Doubling the scale doubles the distance from the origin.
    pinch.options.onStart({ centerX: 30, centerY: 20, distance: 100 });
    pinch.options.onEnd({ scale: 2, dx: 0, dy: 0 });
    await settle();

    wrap.scrollLeft = 10;
    wrap.scrollTop = 5;
    await env.viewer.renderPage(rootEl, 1, 2, 0);
    await settle();

    assert.equal(wrap.scrollLeft, (10 + 30) * 2 - 30, 'the horizontal anchor math (scroll+origin)*ratio-origin');
    assert.equal(wrap.scrollTop, (5 + 20) * 2 - 20, 'the vertical anchor math');

    // The anchor clears after applying: a further render must not re-apply it.
    wrap.scrollLeft = 0;
    wrap.scrollTop = 0;
    await env.viewer.renderPage(rootEl, 1, 2, 0);
    await settle();
    assert.equal(wrap.scrollLeft, 0, 'the pinch anchor is one-shot');
    assert.equal(wrap.scrollTop, 0);
});

test('M2: the pinch start reports OnPinchStarted to the host', async (t) => {
    const env = await loadViewer(t);
    const rootEl = fakeRootEl(fakeCanvas());
    const wrap = fakeElement('wrap');
    const started = [];

    const dotNet = {
        ...env.dotNet,
        invokeMethodAsync(name, ...args) {
            if (name === 'OnPinchStarted') started.push(args);
            return env.dotNet.invokeMethodAsync(name, ...args);
        },
    };

    const initPromise = env.viewer.init(rootEl, 'https://example.com/doc.pdf', dotNet);
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;

    env.viewer.enablePinchZoom(rootEl, wrap, dotNet);
    await settle();
    const pinch = globalThis.__tmPdfViewerPinchHandles[0];

    pinch.options.onStart({ centerX: 10, centerY: 10, distance: 100 });
    await settle();
    assert.equal(started.length, 1,
        'the pinch’s onStart notifies the host so it can discard a live ink stroke');
});
