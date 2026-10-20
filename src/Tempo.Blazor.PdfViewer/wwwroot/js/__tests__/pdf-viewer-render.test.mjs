// pdf-viewer-render.mjs tests — PDF.js v5 RenderTask bookkeeping: page.render()
// returns a non-thenable {promise, cancel} task, a canvas hosts one render at a
// time (the previous task is cancelled before the next starts) and cancelled
// renders surface as RenderingCancelledException that callers must ignore.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    cancelPendingRenderTask,
    isRenderingCancelled,
    renderPageToCanvas,
    trackRenderTask,
} from '../pdf-viewer-render.mjs';
import { canvasSize } from '../pdf-viewer-size.mjs';

/** The shape PDF.js rejects a cancelled render with (BaseException carries name/message). */
function cancellationError() {
    const err = new Error('Rendering was cancelled');
    err.name = 'RenderingCancelledException';
    return err;
}

/**
 * A fake PDF.js v5 page: render() returns a {promise, cancel} RenderTask that
 * is NOT thenable and stays pending until the test resolves/rejects it.
 */
function fakePage() {
    return {
        renderCalls: 0,
        lastParams: null,
        lastTask: null,
        render(renderParams) {
            this.renderCalls += 1;
            this.lastParams = renderParams;
            let cancelled = false;
            const task = {
                get isCancelled() { return cancelled; },
                cancel() { cancelled = true; },
                promise: null
            };
            task.promise = new Promise((resolve, reject) => {
                task.resolve = resolve;
                task.reject = reject;
            });
            this.lastTask = task;
            return task;
        }
    };
}

function fakeCanvas() {
    const ctx = {};
    return { style: {}, width: 0, height: 0, getContext: () => ctx };
}

/** Starts a render and resolves its fake task, simulating PDF.js finishing the draw. */
async function renderAndResolve(options) {
    const pending = renderPageToCanvas(options);
    options.page?.lastTask?.resolve();
    return pending;
}

test('renderPageToCanvas awaits task.promise (PDF.js v5 RenderTask is not thenable)', async () => {
    const page = fakePage();
    const canvas = fakeCanvas();

    let settled = false;
    const pending = renderPageToCanvas({
        canvas, page, viewport: { width: 612, height: 792 }, size: canvasSize(612, 792, 1)
    }).then(() => { settled = true; }, () => { settled = true; });

    assert.equal(page.renderCalls, 1, 'page.render() must have been called synchronously');
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(settled, false, 'the render must stay pending until task.promise settles');

    // Resolve the in-flight task the way PDF.js does after the last draw call.
    const task = page.lastTask;
    assert.ok(task, 'page.render() returned a task to await');
    task.resolve();
    await pending;
});

test('renderPageToCanvas sizes the backing store by DPR and passes the transform', async () => {
    const page = fakePage();
    const canvas = fakeCanvas();

    await renderAndResolve({
        canvas, page, viewport: { width: 612, height: 792 }, size: canvasSize(612, 792, 2)
    });

    assert.equal(page.lastParams.canvasContext, canvas.getContext('2d'));
    assert.equal(page.lastParams.viewport.width, 612);
    assert.deepEqual(page.lastParams.transform, [2, 0, 0, 2, 0, 0]);
    assert.equal(canvas.width, 1224, 'backing store is the viewport × dpr');
    assert.equal(canvas.height, 1584);
    assert.equal(canvas.style.width, '612px', 'CSS size stays at the viewport size');
    assert.equal(canvas.style.height, '792px');
});

test('renderPageToCanvas omits the transform at dpr 1', async () => {
    const page = fakePage();
    await renderAndResolve({
        canvas: fakeCanvas(), page, viewport: { width: 612, height: 792 }, size: canvasSize(612, 792, 1)
    });
    assert.equal(page.lastParams.transform, undefined);
});

test('renderPageToCanvas with setCssSize:false keeps the fluid CSS width (thumbnails)', async () => {
    const page = fakePage();
    const canvas = fakeCanvas();

    await renderAndResolve({
        canvas, page, viewport: { width: 100, height: 100 }, size: canvasSize(100, 100, 2), setCssSize: false
    });

    assert.equal(canvas.width, 200);
    assert.equal(canvas.height, 200);
    assert.equal(canvas.style.width, undefined, 'thumbnails keep their fluid CSS width');
    assert.equal(canvas.style.height, undefined);
});

test('a new render for the same canvas cancels the previous in-flight task', async () => {
    const page = fakePage();
    const canvas = fakeCanvas();
    const tasks = new Map();

    const first = renderPageToCanvas({
        canvas, page, viewport: { width: 100, height: 100 }, size: canvasSize(100, 100, 1), tasks
    });
    const firstTask = tasks.get(canvas);
    assert.ok(firstTask, 'the first render is tracked per canvas');

    await renderAndResolve({
        canvas, page, viewport: { width: 200, height: 200 }, size: canvasSize(200, 200, 1), tasks
    });

    assert.equal(page.renderCalls, 2);
    assert.equal(firstTask.isCancelled, true, 'the superseded task must be cancelled');
    assert.notEqual(tasks.get(canvas), firstTask, 'only the current task stays tracked');

    // The superseded caller observes the cancel exactly like PDF.js reports it.
    const rejection = first.catch(err => err);
    firstTask.reject(cancellationError());
    assert.equal(isRenderingCancelled(await rejection), true);
});

test('rendering to a different canvas does not cancel the other canvas render', async () => {
    const page = fakePage();
    const tasks = new Map();
    const canvasA = fakeCanvas();
    const canvasB = fakeCanvas();

    const first = renderPageToCanvas({ canvas: canvasA, page, viewport: { width: 100, height: 100 }, size: canvasSize(100, 100, 1), tasks });
    await renderAndResolve({ canvas: canvasB, page, viewport: { width: 100, height: 100 }, size: canvasSize(100, 100, 1), tasks });

    assert.equal(page.renderCalls, 2);
    assert.ok(tasks.get(canvasA), 'the other canvas keeps its in-flight task');
    assert.equal(tasks.get(canvasB), undefined, 'the second render already settled and was dropped');

    tasks.get(canvasA).resolve();
    await first;
});

test('renderPageToCanvas rejects when the task rejects and forgets the entry', async () => {
    const page = fakePage();
    const canvas = fakeCanvas();
    const tasks = new Map();

    const pending = renderPageToCanvas({
        canvas, page, viewport: { width: 100, height: 100 }, size: canvasSize(100, 100, 1), tasks
    });
    const task = tasks.get(canvas);

    const rejection = assert.rejects(() => pending, err => isRenderingCancelled(err));
    task.reject(cancellationError());
    await rejection;

    await new Promise(resolve => setImmediate(resolve));
    assert.equal(tasks.has(canvas), false, 'a settled task must not pin the map');
});

test('renderPageToCanvas validates its inputs', async () => {
    await assert.rejects(() => renderPageToCanvas({ page: fakePage(), viewport: {}, size: canvasSize(1, 1, 1) }));
    await assert.rejects(() => renderPageToCanvas({ canvas: fakeCanvas(), viewport: {}, size: canvasSize(1, 1, 1) }));
    await assert.rejects(() => renderPageToCanvas({ canvas: fakeCanvas(), page: fakePage(), viewport: {} }));
});

test('cancelPendingRenderTask is a no-op without a pending task or a non-map', () => {
    const canvas = fakeCanvas();
    assert.equal(cancelPendingRenderTask(new Map(), canvas), false);
    assert.equal(cancelPendingRenderTask(undefined, canvas), false);

    let cancelled = false;
    const tasks = new Map([[canvas, { promise: Promise.resolve(), cancel: () => { cancelled = true; } }]]);
    assert.equal(cancelPendingRenderTask(tasks, canvas), true);
    assert.equal(cancelled, true);
    assert.equal(tasks.has(canvas), false, 'the cancelled task is forgotten');
});

test('cancelPendingRenderTask survives a cancel() that throws on a settled task', () => {
    const canvas = fakeCanvas();
    const tasks = new Map([[canvas, { promise: Promise.resolve(), cancel: () => { throw new Error('already finished'); } }]]);
    assert.equal(cancelPendingRenderTask(tasks, canvas), true);
    assert.equal(tasks.has(canvas), false);
});

test('isRenderingCancelled recognises PDF.js cancellation shapes', () => {
    assert.equal(isRenderingCancelled({ name: 'RenderingCancelledException', message: 'render cancelled' }), true);
    assert.equal(isRenderingCancelled(cancellationError()), true);
    assert.equal(isRenderingCancelled({ message: 'RenderingCancelledException: cancelled' }), true);
    assert.equal(isRenderingCancelled(undefined), false);
    assert.equal(isRenderingCancelled(null), false);
    assert.equal(isRenderingCancelled(new Error('boom')), false);
    assert.equal(isRenderingCancelled('RenderingCancelledException'), false, 'plain strings are not cancellations');
});

test('trackRenderTask drops the entry once the task settles', async () => {
    const canvas = fakeCanvas();
    const tasks = new Map();
    let reject;
    const task = { promise: new Promise((_, r) => { reject = r; }), cancel: () => { } };

    trackRenderTask(tasks, canvas, task);
    assert.equal(tasks.get(canvas), task);

    reject(cancellationError());
    await new Promise(resolve => setImmediate(resolve));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(tasks.has(canvas), false, 'settled tasks must not pin the map');
});

test('trackRenderTask ignores missing maps and tasks', () => {
    assert.doesNotThrow(() => trackRenderTask(undefined, fakeCanvas(), { promise: Promise.resolve() }));
    assert.doesNotThrow(() => trackRenderTask(new Map(), fakeCanvas(), undefined));
});
