// pdf-viewer.js paint-ownership tests — the thumbnails and the continuous pages are DOM built by
// JS, so their paint (colors, shadows, radii, hover/active states) must come from scoped CSS
// classes instead of inline styles: inline styles beat every stylesheet rule, carry hex/alias
// fallbacks the token audit cannot see, and ignore the indigo theme and dark mode.
//
// Same harness as pdf-viewer-init.test.mjs: pdf-viewer.js is copied into a temp dir next to
// stubs of its three dynamic imports, so each test gets a fresh window.tmPdfViewer.
import test from 'node:test';
import assert from 'node:assert/strict';
import { copyFile, mkdtemp, rm, writeFile } from 'node:fs/promises';
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

const RENDER_STUB = `export async function renderPageToCanvas(options) {
  if (options?.canvas) options.canvas.__renderCount = (options.canvas.__renderCount ?? 0) + 1;
};
export function isRenderingCancelled(err) { return err?.name === 'RenderingCancelledException'; };`;

function fakeElement(tag) {
    const classes = new Set();
    return {
        tagName: tag,
        style: {},
        dataset: {},
        attrs: {},
        children: [],
        innerHTML: '',
        className: '',
        setAttribute(name, value) { this.attrs[name] = String(value); },
        getAttribute(name) { return this.attrs[name] ?? null; },
        removeAttribute(name) { delete this.attrs[name]; },
        classList: {
            add(...names) { names.forEach(name => classes.add(name)); },
            remove(...names) { names.forEach(name => classes.delete(name)); },
            toggle(name, force) {
                const next = force === undefined ? !classes.has(name) : Boolean(force);
                if (next) { classes.add(name); } else { classes.delete(name); }
                return next;
            },
            contains: name => classes.has(name),
        },
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

/** A root whose `.tm-pdf-thumbnail` query returns the given wrappers (like the live sidebar). */
function fakeRootEl(thumbnails) {
    const el = fakeElement('root');
    el.querySelectorAll = selector =>
        selector === '.tm-pdf-thumbnail' ? thumbnails : [];
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
    let reject;
    const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}

const flush = () => new Promise(resolve => setImmediate(resolve));

async function loadViewer(testContext) {
    const registry = { loadedPages: [], loadErrors: [] };
    const docRequest = makeDocRequest();

    class SpyAbortController {
        constructor() { this.signal = {}; }
        abort() { this.aborted = true; }
    }

    const originals = {
        AbortController: globalThis.AbortController,
        ResizeObserver: globalThis.ResizeObserver,
        document: globalThis.document,
        window: globalThis.window,
    };
    globalThis.AbortController = SpyAbortController;
    globalThis.ResizeObserver = class { observe() { } unobserve() { } disconnect() { } };
    globalThis.window = globalThis;
    globalThis.document = {
        currentScript: { src: '' },
        addEventListener() { },
        removeEventListener() { },
        createElement: tag => fakeElement(tag),
    };

    const dir = await mkdtemp(path.join(tmpdir(), 'tm-pdf-viewer-classes-'));
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
        invokeMethodAsync() { return Promise.resolve(); },
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

async function loadedViewer(testContext) {
    const env = await loadViewer(testContext);
    const rootEl = fakeElement('root');
    const initPromise = env.viewer.init(rootEl, 'https://example.com/classes.pdf', env.dotNet);
    await flush();
    env.docRequest.resolve(fakePdfDoc(3));
    await initPromise;
    return { ...env, rootEl };
}

/** No paint may ride on inline styles: they beat every stylesheet rule and ignore the themes. */
function assertNoInlinePaint(el, label) {
    for (const property of ['background', 'backgroundColor', 'color', 'boxShadow', 'borderRadius']) {
        assert.equal(
            el.style[property],
            undefined,
            `${label} must not carry an inline ${property} — paint belongs to the scoped CSS classes`);
    }
}

test('thumbnails carry the tm-pdf-thumbnail class and no inline paint', async (t) => {
    const env = await loadedViewer(t);
    const container = fakeElement('div');
    await env.viewer.renderThumbnails(env.rootEl, container, 0.3, env.dotNet);

    assert.equal(container.children.length, 3, 'one wrapper per page');
    for (const wrap of container.children) {
        assert.equal(wrap.className, 'tm-pdf-thumbnail');
        assert.equal(wrap.dataset.page !== undefined, true, 'the wrapper keeps its page anchor');
        assertNoInlinePaint(wrap, 'thumbnail wrapper');

        const [canvas, label] = wrap.children;
        assert.equal(canvas.tagName, 'canvas');
        assertNoInlinePaint(canvas, 'thumbnail canvas');

        assert.equal(label.className, 'tm-pdf-thumbnail__label');
        assertNoInlinePaint(label, 'thumbnail page label');
    }
});

test('highlightThumbnail toggles the active class instead of an inline background', async (t) => {
    const env = await loadedViewer(t);
    const container = fakeElement('div');
    await env.viewer.renderThumbnails(env.rootEl, container, 0.3, env.dotNet);

    // The live sidebar is found through querySelectorAll on the root.
    const sidebar = fakeRootEl(container.children);
    env.viewer.highlightThumbnail(sidebar, 2);

    for (const wrap of container.children) {
        const isActive = wrap.dataset.page === '2';
        assert.equal(
            wrap.classList.contains('tm-pdf-thumbnail--active'),
            isActive,
            `page ${wrap.dataset.page} active state must be the tm-pdf-thumbnail--active class`);
        assertNoInlinePaint(wrap, 'thumbnail wrapper');
    }
});

test('continuous pages carry the tm-pdf-viewer__page class and no inline paint', async (t) => {
    const env = await loadedViewer(t);
    const container = fakeElement('div');
    await env.viewer.renderAllPages(env.rootEl, container, 1, 0);

    // The wraps live in the renderer-owned pages container (a single child of the
    // scroll wrap — the Blazor overlay slots are a sibling container).
    const pagesContainer = container.children[0];
    assert.equal(pagesContainer.className, 'tm-pdf-viewer__continuous-pages');
    assert.equal(pagesContainer.children.length, 3, 'one page wrap per page');
    for (const [index, wrap] of pagesContainer.children.entries()) {
        assert.equal(wrap.className, 'tm-pdf-viewer__page');
        assertNoInlinePaint(wrap, 'continuous page wrap');
        // A11y: a named page group; the canvas mirrors the label as an image (no
        // text layer in this render).
        assert.equal(wrap.getAttribute('role'), 'group');
        assert.equal(wrap.getAttribute('aria-label'), `Page ${index + 1} of 3`);
        assert.equal(wrap.getAttribute('aria-current'), null,
            'aria-current lands on the visible page only (kept in sync by the scroll tracker)');

        // The inner div is the positioning context for the per-page text/search
        // layers; the canvas is its first child and stays paint-free.
        const inner = wrap.children[0];
        assert.equal(inner.tagName, 'div');
        assert.equal(inner.className, 'tm-pdf-viewer__page-inner');
        assertNoInlinePaint(inner, 'continuous page inner');

        const canvas = inner.children[0];
        assert.equal(canvas.tagName, 'canvas');
        assertNoInlinePaint(canvas, 'continuous page canvas');
        assert.equal(canvas.getAttribute('role'), 'img', 'the canvas is announced as an image of the page');
        assert.equal(canvas.getAttribute('aria-label'), `Page ${index + 1} of 3`);
    }
});
