/**
 * Tempo.Blazor.PdfViewer — PDF.js render-task bookkeeping (node-tested via
 * npm run test:pdf-viewer).
 *
 * PDF.js v5's `page.render()` returns a RenderTask object that is NOT
 * thenable — the promise lives on `task.promise`. A canvas can only host one
 * in-flight render, so a new render for the same canvas must cancel the
 * previous task first; the discarded render's promise rejects with a
 * `RenderingCancelledException`, which callers must ignore instead of logging.
 *
 * The helpers are pure (no DOM/global access) so the node test suite can drive
 * them with fake page/canvas objects: `renderPageToCanvas` only touches the
 * passed-in canvas, page and a precomputed size.
 */

const RENDER_CANCELLED_NAME = 'RenderingCancelledException';

/**
 * Detects a PDF.js "rendering was cancelled" rejection, which surfaces when a
 * superseded RenderTask is cancelled. Checks the exception name and message so
 * it works across PDF.js builds (some do not subclass Error).
 * @param {unknown} error The caught value.
 * @returns {boolean} Whether the error is a render cancellation.
 */
export function isRenderingCancelled(error) {
    if (!error) return false;
    const name = typeof error.name === 'string' ? error.name : '';
    const message = typeof error.message === 'string' ? error.message : '';
    return (
        name === RENDER_CANCELLED_NAME ||
        message.includes(RENDER_CANCELLED_NAME) ||
        message.includes('Rendering was cancelled')
    );
}

/**
 * Cancels and forgets the render task currently tracked for a canvas.
 * @param {Map<object, {promise: Promise, cancel: Function}>|undefined} tasks Per-root canvas→task map.
 * @param {object} canvas The canvas whose render is superseded.
 * @returns {boolean} Whether a pending task was cancelled.
 */
export function cancelPendingRenderTask(tasks, canvas) {
    if (!(tasks instanceof Map)) return false;
    const pending = tasks.get(canvas);
    if (!pending) return false;
    tasks.delete(canvas);
    try {
        pending.cancel();
    } catch {
        // A task that already settled may throw from cancel(); nothing left to do.
    }
    return true;
}

/**
 * Tracks a freshly started RenderTask for a canvas and drops the entry once
 * the task settles, so the map never pins finished tasks. Rejections are
 * swallowed here only for bookkeeping — the awaiting caller still observes them.
 * @param {Map<object, {promise: Promise, cancel: Function}>|undefined} tasks Per-root canvas→task map.
 * @param {object} canvas The canvas being rendered to.
 * @param {{promise?: Promise, cancel?: Function}|undefined} task The RenderTask returned by page.render().
 */
export function trackRenderTask(tasks, canvas, task) {
    if (!(tasks instanceof Map) || !task) return;
    tasks.set(canvas, task);
    const promise = task.promise;
    if (promise && typeof promise.finally === 'function') {
        promise.catch(() => { }).finally(() => {
            if (tasks.get(canvas) === task) tasks.delete(canvas);
        });
    }
}

function applyCanvasSize(canvas, size, setCssSize) {
    canvas.width = size.width;
    canvas.height = size.height;
    if (setCssSize && canvas.style) {
        canvas.style.width = size.cssWidth + 'px';
        canvas.style.height = size.cssHeight + 'px';
    }
}

function buildRenderParams(canvas, viewport, size) {
    const params = { canvasContext: canvas.getContext('2d'), viewport };
    if (size.dpr !== 1) {
        params.transform = [size.dpr, 0, 0, size.dpr, 0, 0];
    }
    return params;
}

/**
 * Renders one PDF.js page into a canvas, cancelling any render still in flight
 * for that canvas and tracking the new RenderTask until it settles.
 * @param {object} options
 * @param {object} options.canvas Target canvas element (or a fake in tests).
 * @param {{render: Function}} options.page PDF.js page proxy with render().
 * @param {{width: number, height: number}} options.viewport PDF.js viewport.
 * @param {{width: number, height: number, cssWidth: number, cssHeight: number, dpr: number}} options.size Precomputed canvas size (pdf-viewer-size.mjs / fallback).
 * @param {boolean} [options.setCssSize=true] Whether to also set canvas.style width/height (thumbnails keep a fluid CSS width).
 * @param {Map<object, {promise: Promise, cancel: Function}>} [options.tasks] Per-root canvas→task map.
 * @returns {Promise<{promise: Promise, cancel: Function}>} The started RenderTask.
 */
export async function renderPageToCanvas({ canvas, page, viewport, size, setCssSize = true, tasks = new Map() }) {
    if (!canvas) throw new Error('renderPageToCanvas: canvas is required');
    if (!page || typeof page.render !== 'function') {
        throw new Error('renderPageToCanvas: page.render() is required');
    }
    if (!size) throw new Error('renderPageToCanvas: size is required');

    cancelPendingRenderTask(tasks, canvas);
    applyCanvasSize(canvas, size, setCssSize);
    const task = page.render(buildRenderParams(canvas, viewport, size));
    trackRenderTask(tasks, canvas, task);
    await task.promise;
    return task;
}
