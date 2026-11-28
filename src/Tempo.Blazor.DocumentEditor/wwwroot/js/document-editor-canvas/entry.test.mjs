import assert from 'node:assert/strict';
import test from 'node:test';
import { CANVAS_LAYER_KINDS, CanvasDocumentEngine, createCanvasDocumentEngine, scrollElementIntoViewWithin } from './entry.mjs';
import { COMPACT_ROOT_PADDING_INLINE, resolveRootPaddingInline } from './render/canvas-stack.mjs';

test('createCanvasDocumentEngine mounts a canvas-per-visible-page stack without Blazor', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        pixelRatioProvider: () => 2,
        model: {
            documentId: 'phase-1',
            body: {
                blocks: [
                    {
                        id: 'p1',
                        type: 'paragraph',
                        content: { runs: [{ text: 'Accessible paragraph' }] },
                    },
                ],
            },
        },
        ariaLabel: 'Canvas document',
    });

    const result = engine.render();
    const snapshot = engine.getSnapshot();

    assert.equal(result.ok, true);
    assert.equal(snapshot.mounted, true);
    assert.equal(snapshot.architecture.name, 'CanvasDocumentEngine');
    assert.equal(snapshot.architecture.pageSurfaceStrategy, 'canvas-per-visible-page');
    assert.deepEqual(snapshot.architecture.layerKinds, CANVAS_LAYER_KINDS);
    assert.equal(host.getAttribute('data-canvas-engine-ready'), 'true');
    assert.equal(host.getAttribute('data-canvas-engine-page-strategy'), 'canvas-per-visible-page');
    assert.equal(findAll(host, node => node.tagName === 'CANVAS').length, CANVAS_LAYER_KINDS.length);
    assert.equal(findAll(host, node => node.getAttribute('contenteditable') === 'true').length, 0);
});

test('updateOptions applies proofing word lists at runtime and re-analyzes immediately', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: {
            documentId: 'phase-7-proofing',
            body: {
                blocks: [
                    {
                        id: 'p1',
                        type: 'paragraph',
                        content: { runs: [{ text: 'Tato smlouvva byla uzavřena s chybbou.' }] },
                    },
                ],
            },
        },
    });
    engine.render();
    assert.equal(engine.getSnapshot().proofing.diagnosticCount, 0,
        'no proofing options yet — no diagnostics');

    // The async ITempoProofingProvider path pushes refreshed word lists through setOptions at
    // runtime (options.proofing), long after mount. The engine must re-analyze immediately.
    engine.updateOptions({
        proofing: {
            enabled: true,
            defaultLanguage: 'cs-CZ',
            flaggedWords: ['smlouvva', 'chybbou'],
            suggestions: { smlouvva: ['smlouva'], chybbou: ['chybou'] },
        },
    });

    const snapshot = engine.getSnapshot();
    assert.equal(snapshot.proofing.diagnosticCount, 2);
    assert.equal(snapshot.proofing.diagnostics[0].word, 'smlouvva');
    assert.deepEqual(snapshot.proofing.diagnostics[0].suggestions, ['smlouva']);
    assert.equal(snapshot.proofing.diagnostics[1].word, 'chybbou');
    assert.ok(snapshot.proofingOverlay.squiggleCount >= 1, 'squiggles must repaint after the update');

    // Turning proofing back off clears the diagnostics on the same runtime path.
    engine.updateOptions({ proofing: { enabled: false, flaggedWords: [], suggestions: {} } });
    assert.equal(engine.getSnapshot().proofing.diagnosticCount, 0);
});

test('pointer hit-testing completes a just-reset progressive layout synchronously (caret race fix)', () => {
    const bigModel = version => ({
        documentId: 'caret-race-fix',
        version,
        body: {
            blocks: Array.from({ length: 80 }, (_, index) => ({
                id: `p${index}`,
                type: 'paragraph',
                order: index + 1,
                paragraphProperties: {},
                content: {
                    type: 'paragraph',
                    runs: [{ id: `p${index}-run`, type: 'text', text: `Paragraph ${index + 1} ${'caret race filler text '.repeat(8)}`, marks: [] }],
                },
            })),
        },
    });

    const idleCallbacks = [];
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: bigModel(0),
        scheduleProgressiveIdle: callback => idleCallbacks.push(callback),
    });
    engine.render();

    // A post-mount replaceModel push resets the progressive layout — the window in which a click
    // used to hit-test against a partial selection layout and land at offset 0.
    engine.setModel(bigModel(1));
    engine.render();
    const root = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-engine-root');
    assert.equal(root.getAttribute('data-canvas-layout-complete'), 'false', 'the reset layout must be partial again');

    // The selection controller calls this seam on pointer-down: the layout must finish
    // synchronously so the hit-test resolves against the FULL geometry.
    engine.ensurePointerHitLayout();
    assert.equal(root.getAttribute('data-canvas-layout-complete'), 'true',
        'pointer-down must not hit-test against a partial layout');

    // No-op when the layout is already complete.
    engine.ensurePointerHitLayout();
    assert.equal(root.getAttribute('data-canvas-layout-complete'), 'true');
});

test('canEdit:false makes the hidden input read-only so typing cannot bypass permission gates', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        canEdit: false,
        model: { body: { blocks: [{ id: 'p1', type: 'paragraph', content: { runs: [{ text: 'Locked' }] } }] } },
    });
    engine.render();

    const input = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-hidden-input');
    assert.equal(input.readOnly, true, 'commenter/viewer permission (canEdit:false) must lock the input');

    // Runtime permission change unlocks it…
    engine.updateOptions({ canEdit: true });
    assert.equal(input.readOnly, false);

    // …and readOnly:true locks it regardless of canEdit.
    engine.updateOptions({ canEdit: true, readOnly: true });
    assert.equal(input.readOnly, true);
});

test('phase 1 canvas stack applies high-DPI backing store and paints an intentional empty page', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        pixelRatioProvider: () => 2.5,
        model: {},
    });

    engine.render();

    const page = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-page');
    const backgroundCanvas = findOne(host, node => node.getAttribute('data-canvas-layer') === 'page-background');
    const context = backgroundCanvas.getContext('2d');

    assert.equal(page.style.width, '794px');
    assert.equal(page.style.height, '1123px');
    assert.equal(backgroundCanvas.width, 1985);
    assert.equal(backgroundCanvas.height, 2808);
    assert.deepEqual(context.transforms.at(-1), [2.5, 0, 0, 2.5, 0, 0]);
    assert.ok(context.calls.some(call => call.name === 'fillRect' && call.args[2] === 794 && call.args[3] === 1123));
    assert.ok(context.calls.some(call => call.name === 'strokeRect'));
});

test('accessibility mirror and hidden input bridge are present but not browser contenteditable authorities', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: {
            body: {
                blocks: [
                    {
                        id: 'a11y-p',
                        type: 'paragraph',
                        content: {
                            runs: [
                                { text: 'Screen reader mirror text ' },
                                {
                                    id: 'a11y-math-run',
                                    type: 'math',
                                    math: {
                                        mathId: 'a11y-math',
                                        altText: 'x squared',
                                        content: {
                                            elements: [{
                                                type: 'sup',
                                                base: { elements: [{ type: 'run', text: 'x' }] },
                                                superscript: { elements: [{ type: 'run', text: '2' }] },
                                            }],
                                        },
                                    },
                                },
                            ],
                        },
                    },
                    {
                        id: 'a11y-table',
                        type: 'table',
                        content: {
                            table: {
                                rows: [{
                                    id: 'a11y-table-row',
                                    cells: [{
                                        id: 'a11y-table-cell',
                                        isHeader: false,
                                        blocks: [{
                                            id: 'a11y-table-cell-p',
                                            type: 'paragraph',
                                            content: { runs: [{ text: 'Table mirror text' }] },
                                        }],
                                    }],
                                }],
                            },
                        },
                    },
                ],
            },
        },
    });

    engine.render();

    const mirror = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-a11y-mirror');
    const input = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-hidden-input');

    assert.equal(mirror.getAttribute('role'), 'document');
    assert.equal(findOne(mirror, node => node.getAttribute('data-block-id') === 'a11y-p').textContent, 'Screen reader mirror text ');
    const math = findOne(mirror, node => node.getAttribute('data-canvas-a11y-math') === 'true');
    assert.equal(math.getAttribute('role'), 'math');
    assert.equal(math.getAttribute('aria-label'), 'x squared');
    assert.equal(math.getAttribute('data-math-id'), 'a11y-math');
    assert.equal(findOne(mirror, node => node.getAttribute('data-canvas-a11y-table') === 'true').tagName, 'TABLE');
    assert.equal(findOne(mirror, node => node.getAttribute('data-cell-id') === 'a11y-table-cell').tagName, 'TD');
    assert.equal(findOne(mirror, node => node.getAttribute('data-block-id') === 'a11y-table-cell-p').textContent, 'Table mirror text');
    assert.equal(input.tagName, 'TEXTAREA');
    assert.equal(input.getAttribute('role'), 'textbox');
    assert.equal(input.getAttribute('aria-multiline'), 'true');
    assert.equal(input.getAttribute('aria-controls'), 'document-canvas-a11y-mirror');
    assert.equal(input.getAttribute('spellcheck'), 'false');
    assert.equal(findAll(host, node => node.getAttribute('contenteditable') != null).length, 0);
});

test('command dispatcher and history stores are available from the engine snapshot boundary', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({ host, document: doc, model: {} });
    engine.commandDispatcher.register('phase1.probe', payload => ({ received: payload.value }));
    const commandResult = engine.commandDispatcher.execute('phase1.probe', { value: 42 });
    engine.history.push({
        id: 'tx1',
        kind: 'probe',
        before: { model: { version: 1 }, selection: null },
        after: { model: { version: 2 }, selection: null },
    });

    assert.equal(commandResult.handled, true);
    assert.deepEqual(commandResult.result, { received: 42 });
    assert.equal(engine.history.snapshot().canUndo, true);
    assert.equal(engine.history.undo().id, 'tx1');
    assert.equal(engine.history.snapshot().canRedo, true);
});

test('canvas offline state carries serializable collaboration and runtime data', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: {
            documentId: 'phase-20-offline',
            body: {
                blocks: [{
                    id: 'p1',
                    type: 'paragraph',
                    content: { runs: [{ text: 'Offline ready' }] },
                }],
            },
        },
    });

    engine.render();
    const state = engine.getOfflineState();
    const roundTrip = JSON.parse(JSON.stringify(state));

    assert.equal(roundTrip.schemaVersion, 1);
    assert.equal(roundTrip.engine, 'CanvasDocumentEngine');
    assert.equal(roundTrip.model.documentId, 'phase-20-offline');
    assert.equal(roundTrip.collaboration.protocolVersion, 1);
    assert.ok(Number.isFinite(roundTrip.dirtyEpoch));
    assert.ok(Number.isFinite(roundTrip.undoEpoch));
});

test('input commits publish latency immediately and coalesce canvas render to the next frame', async () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const initialModel = {
        documentId: 'phase-22-input-schedule',
        body: {
            blocks: [{
                id: 'p1',
                type: 'paragraph',
                content: { runs: [{ text: 'Before' }] },
            }],
        },
    };
    const nextModel = {
        documentId: 'phase-22-input-schedule',
        body: {
            blocks: [{
                id: 'p1',
                type: 'paragraph',
                content: { runs: [{ text: 'Before after' }] },
            }],
        },
    };
    const engine = createCanvasDocumentEngine({ host, document: doc, model: initialModel });

    engine.render();
    const root = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-engine-root');
    const renderCountBefore = Number(root.getAttribute('data-canvas-render-count') || '0');
    const commitResult = engine.commitInputChange({
        before: { model: initialModel, selection: null },
        model: nextModel,
        selection: null,
        input: { dirtyBlockIds: ['p1'] },
        result: { dirtyBlockIds: ['p1'] },
    });

    assert.equal(commitResult.scheduled, true);
    assert.equal(root.getAttribute('data-canvas-typing-latency-count'), '1');
    assert.equal(Number(root.getAttribute('data-canvas-render-count') || '0'), renderCountBefore);

    await new Promise(resolve => setTimeout(resolve, 25));

    assert.ok(Number(root.getAttribute('data-canvas-render-count') || '0') > renderCountBefore);
    assert.equal(root.getAttribute('data-canvas-recalc-first-dirty-block-index'), '0');
    assert.equal(root.getAttribute('data-canvas-input-incremental-repaint'), 'true');

    await new Promise(resolve => setTimeout(resolve, 220));

    assert.equal(engine.history.snapshot().canUndo, true);
});

test('view zoom commands rerender page surfaces without marking document dirty', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: createTextModel('Zoom command keeps the model clean.', 0),
    });

    engine.render();
    const page = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-page');
    const root = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-engine-root');
    assert.equal(page.getAttribute('data-canvas-page-css-width'), '794');

    const result = engine.execCommand('fitWidth', {
        metrics: {
            pageWidth: 794,
            pageHeight: 1123,
            viewportWidth: 650,
            viewportHeight: 900,
            paddingInline: 48,
            paddingBlock: 48,
        },
    });

    assert.equal(result.handled, true);
    assert.equal(result.result.viewChanged, true);
    assert.equal(root.getAttribute('data-canvas-zoom-preset'), 'fitWidth');
    assert.equal(root.getAttribute('data-canvas-zoom-percent'), '76');
    assert.equal(page.getAttribute('data-canvas-page-css-width'), '602');
    assert.ok(Number(page.getAttribute('data-canvas-painted-command-count') || '0') > 0);
    assert.equal(engine.getSnapshot().modelVersion, 0);
});

// ── Phase 11: engine auto-fit (reader lock, fitWidth, resize observer, destroy) ─────────────────────────────────────

function createAutoFitHarness(initialWidth) {
    const observers = [];
    class FakeResizeObserver {
        constructor(callback) {
            this.callback = callback;
            this.disconnected = false;
            observers.push(this);
        }

        observe() {}

        disconnect() {
            this.disconnected = true;
        }

        emit(width) {
            this.callback([{ contentRect: { width } }]);
        }
    }

    const frames = [];
    const view = {
        ResizeObserver: FakeResizeObserver,
        innerWidth: 1440,
        innerHeight: 900,
        requestAnimationFrame: callback => frames.push(callback),
        cancelAnimationFrame: () => {},
        getComputedStyle: node => ({
            overflowX: node?.isScroller ? 'auto' : 'visible',
            overflowY: node?.isScroller ? 'auto' : 'visible',
            paddingLeft: '0px',
            paddingRight: '0px',
        }),
    };
    const doc = createFakeDocument();
    doc.defaultView = view;
    const scroller = { isScroller: true, clientWidth: initialWidth, clientHeight: 900, scrollTop: 0, scrollHeight: 4000, getBoundingClientRect: () => ({ width: scroller.clientWidth, height: 900 }) };
    const host = doc.createElement('div');
    host.parentElement = scroller;
    return {
        doc,
        host,
        scroller,
        observers,
        frames,
        flush() {
            // animation frames of the observer AND of the engine repaint run in order, a frame may schedule another one
            for (let guard = 0; guard < 10 && frames.length > 0; guard += 1) {
                frames.splice(0, frames.length).forEach(callback => callback());
            }
        },
        resize(width) {
            scroller.clientWidth = width;
            observers[0].emit(width);
        },
    };
}

function zoomOf(engine) {
    return engine.commandRuntime.getViewState().zoom;
}

test('auto fit: a narrow column is fitted at mount and follows a width change once per frame, reporting the change', () => {
    const h = createAutoFitHarness(650);
    const reports = [];
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Auto fit keeps the page inside a narrow column.', 0),
        autoFit: 'narrow',
        onViewChanged: zoom => reports.push(zoom),
    });
    engine.render();
    engine.setAutoFitMode('narrow');

    assert.equal(zoomOf(engine).preset, 'fitWidth', 'the 650 px column fits the page at once');
    assert.ok(zoomOf(engine).scale < 1);
    assert.equal(zoomOf(engine).autoFit, 'narrow');

    h.flush();
    reports.length = 0;
    h.scroller.clientWidth = 1200;
    h.observers[0].emit(1200);
    h.observers[0].emit(1200);
    assert.equal(h.frames.length, 1, 'repeated reports within a frame schedule one fit');
    h.flush();
    assert.equal(zoomOf(engine).scale, 1, 'a column that holds the page shows it at 100 percent');
    assert.equal(zoomOf(engine).preset, 'custom');
    assert.equal(zoomOf(engine).autoFit, 'narrow');
    assert.equal(reports.length, 1, 'the host learns about the fit once');
    engine.destroy();
});

test('auto fit: a reader zoom switches the fit off, fitWidth switches it on, resetZoom restores the configured mode', () => {
    const h = createAutoFitHarness(650);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Reader zoom wins.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');

    engine.execCommand('setZoom', { percent: 120 });
    assert.equal(zoomOf(engine).autoFit, 'off');
    assert.equal(zoomOf(engine).percent, 120);

    h.resize(500);
    h.flush();
    assert.equal(zoomOf(engine).percent, 120, 'the reader zoom wins over the resize');
    assert.equal(zoomOf(engine).autoFit, 'off');

    engine.execCommand('fitWidth');
    assert.equal(zoomOf(engine).autoFit, 'always');
    assert.equal(zoomOf(engine).preset, 'fitWidth');

    h.resize(900);
    h.flush();
    assert.equal(zoomOf(engine).preset, 'fitWidth', 'the explicit fit follows the column again');
    assert.ok(zoomOf(engine).percent > 80);

    engine.execCommand('resetZoom');
    assert.equal(zoomOf(engine).autoFit, 'narrow', 'Reset zoom brings back the configured automatic mode');
    engine.destroy();
});

test('auto fit: a scroll position is kept in proportion when the scale changes', () => {
    const h = createAutoFitHarness(1200);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Reading position.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    assert.equal(zoomOf(engine).scale, 1);

    h.scroller.scrollTop = 1000;
    h.resize(600);
    h.flush();
    const scale = zoomOf(engine).scale;
    assert.ok(scale < 1);
    assert.equal(Math.round(h.scroller.scrollTop), Math.round(1000 * scale), 'the same place of the document stays at the top');
    engine.destroy();
});

test('auto fit: the reading position follows the page the reader is on, not a proportional pixel offset', () => {
    const h = createAutoFitHarness(1200);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Anchored reading position.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    const page = engine.canvasStack.pages.get('0').pageElement;
    const pageHeight = () => 1123 * zoomOf(engine).scale;
    // the page starts 24 px below the top of the scroller and scrolls with it
    page.getBoundingClientRect = () => ({ top: 24 - h.scroller.scrollTop, height: pageHeight(), bottom: 24 - h.scroller.scrollTop + pageHeight() });
    h.scroller.scrollTop = 500;
    const fraction = (500 - 24) / 1123;

    h.resize(600);
    h.flush();
    const scale = zoomOf(engine).scale;
    assert.ok(scale < 1);
    // the same fraction of the page is at the top: 24 px gutter + fraction of the SCALED page (a proportional offset would give 500 * scale)
    assert.ok(Math.abs(h.scroller.scrollTop - (24 + fraction * 1123 * scale)) < 1.5, `scrollTop ${h.scroller.scrollTop}`);
    assert.ok(Math.abs(h.scroller.scrollTop - 500 * scale) > 5, 'the page gutter is not scaled, so the anchored position differs from the proportional one');
    engine.destroy();
});

test('auto fit: the reading position is restored on the real vertical scroller when the horizontal viewport is a clipping wrapper', () => {
    const h = createAutoFitHarness(1200);
    // the observed viewport clips (overflow hidden) but does not scroll vertically; the document itself scrolls
    h.scroller.scrollHeight = 900;
    h.scroller.getBoundingClientRect = () => ({ top: 0, width: h.scroller.clientWidth, height: 900 });
    const scrollingElement = { scrollTop: 800, scrollHeight: 6000, clientHeight: 900, getBoundingClientRect: () => ({ top: 0 }) };
    h.doc.scrollingElement = scrollingElement;
    h.host.ownerDocument = h.doc;
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Window scroll.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    const page = engine.canvasStack.pages.get('0').pageElement;
    page.getBoundingClientRect = () => ({ top: 24 - scrollingElement.scrollTop, height: 1123 * zoomOf(engine).scale, bottom: 24 - scrollingElement.scrollTop + 1123 * zoomOf(engine).scale });
    scrollingElement.scrollTop = 800;

    h.resize(500);
    h.flush();
    const scale = zoomOf(engine).scale;
    assert.ok(Math.abs(scrollingElement.scrollTop - (24 + ((800 - 24) / 1123) * 1123 * scale)) < 1.5, `document scrollTop ${scrollingElement.scrollTop}`);
    engine.destroy();
});

test('auto fit: a fit at the top of the document keeps the page where it was (the lead above page one is not scrolled away)', () => {
    for (const lead of [24, 290]) {
        const h = createAutoFitHarness(1200);
        const engine = createCanvasDocumentEngine({
            host: h.host,
            document: h.doc,
            model: createTextModel('Fit at the top.', 0),
            autoFit: 'narrow',
        });
        engine.render();
        engine.setAutoFitMode('narrow');
        const page = engine.canvasStack.pages.get('0').pageElement;
        const pageHeight = () => 1123 * zoomOf(engine).scale;
        page.getBoundingClientRect = () => ({ top: lead - h.scroller.scrollTop, height: pageHeight(), bottom: lead - h.scroller.scrollTop + pageHeight() });
        h.scroller.scrollTop = 0;

        h.resize(600);
        h.flush();
        assert.ok(zoomOf(engine).scale < 1);
        assert.equal(Math.round(h.scroller.scrollTop), 0, `lead ${lead}: the page stays ${lead} px below the top of the view`);
        engine.destroy();
    }
});

test('auto fit: a fit at the top with the window as the scroller does not scroll the window', () => {
    const h = createAutoFitHarness(1200);
    h.scroller.scrollHeight = 900;
    h.scroller.getBoundingClientRect = () => ({ top: 0, width: h.scroller.clientWidth, height: 900 });
    const scrollingElement = { scrollTop: 0, scrollHeight: 6000, clientHeight: 900, getBoundingClientRect: () => ({ top: 0 }) };
    h.doc.scrollingElement = scrollingElement;
    h.host.ownerDocument = h.doc;
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Window fit at the top.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    const page = engine.canvasStack.pages.get('0').pageElement;
    const pageHeight = () => 1123 * zoomOf(engine).scale;
    page.getBoundingClientRect = () => ({ top: 290 - scrollingElement.scrollTop, height: pageHeight(), bottom: 290 - scrollingElement.scrollTop + pageHeight() });

    h.resize(500);
    h.flush();
    assert.equal(Math.round(scrollingElement.scrollTop), 0);
    engine.destroy();
});

test('auto fit: a width that grows by no more than a scrollbar keeps the fit, a larger one fits again', () => {
    const h = createAutoFitHarness(700);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Oscillation.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    const fitted = zoomOf(engine).scale;
    assert.equal(zoomOf(engine).preset, 'fitWidth');

    h.resize(715);
    h.flush();
    assert.equal(zoomOf(engine).scale, fitted, 'W + 15 px (the scrollbar that left with the shorter document) does not enlarge the page');

    h.resize(740);
    h.flush();
    assert.ok(zoomOf(engine).scale > fitted, 'W + 40 px is a real width change');
    engine.destroy();
});

test('auto fit: the observer follows a replaced scroll viewport', () => {
    const h = createAutoFitHarness(650);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Rebind.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    assert.equal(h.observers.length, 1);
    assert.equal(engine.autoFitObserver.target, h.scroller);

    const replacement = { isScroller: true, clientWidth: 650, clientHeight: 900, scrollTop: 0, scrollHeight: 4000, getBoundingClientRect: () => ({ width: replacement.clientWidth, height: 900 }) };
    h.host.parentElement = replacement;
    engine.applyAutoFit();
    assert.equal(h.observers.length, 2, 'a new observer is bound to the new scroller');
    assert.equal(h.observers[0].disconnected, true, 'the old one is released');
    assert.equal(engine.autoFitObserver.target, replacement);
    engine.destroy();
});

test('auto fit: the configured mode is seeded into the zoom at construction, so setting it at mount is no view change of its own', () => {
    const h = createAutoFitHarness(1200);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Seeded.', 0),
        autoFit: 'narrow',
    });
    assert.equal(zoomOf(engine).autoFit, 'narrow', 'before the first render');
    engine.render();
    const reports = [];
    engine.onViewChanged = zoom => reports.push(zoom);
    const renders = engine.performanceMetrics.snapshot().renderCount;
    assert.ok(renders >= 1);
    engine.setAutoFitMode('narrow');
    assert.equal(zoomOf(engine).scale, 1);
    assert.equal(reports.length, 0, 'the column holds the page: nothing changed, nothing is reported');
    assert.equal(engine.performanceMetrics.snapshot().renderCount, renders, 'and nothing is painted again');

    assert.equal(engine.canvasStack.root.getAttribute('data-canvas-zoom-auto-fit'), 'narrow');
    engine.destroy();
});

test('auto fit: a mode-only change updates the zoom-auto-fit attribute without a repaint', () => {
    const h = createAutoFitHarness(1200);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Mode only.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    const renders = engine.performanceMetrics.snapshot().renderCount;
    const reports = [];
    engine.onViewChanged = zoom => reports.push(zoom);
    engine.setAutoFitMode('off');
    assert.equal(zoomOf(engine).autoFit, 'off');
    assert.equal(engine.canvasStack.root.getAttribute('data-canvas-zoom-auto-fit'), 'off');
    assert.equal(engine.performanceMetrics.snapshot().renderCount, renders, 'the mode alone draws nothing');
    assert.equal(reports.length, 1, 'but the host still learns the mode');
    engine.destroy();
});

test('auto fit: resetZoom reads the configured mode from the engine context, not from the argument', () => {
    const h = createAutoFitHarness(650);
    const engine = createCanvasDocumentEngine({ host: h.host, document: h.doc, model: createTextModel('Reset.', 0), autoFit: 'narrow' });
    engine.render();
    engine.setAutoFitMode('narrow');
    engine.execCommand('setZoom', { percent: 150 });
    engine.execCommand('resetZoom');
    assert.equal(zoomOf(engine).autoFit, 'narrow');
    assert.equal(zoomOf(engine).preset, 'fitWidth');
    engine.destroy();
});

test('auto fit: Ctrl+0 is the Reset zoom shortcut', () => {
    const h = createAutoFitHarness(650);
    const engine = createCanvasDocumentEngine({ host: h.host, document: h.doc, model: createTextModel('Ctrl 0.', 0), autoFit: 'narrow' });
    engine.render();
    engine.setAutoFitMode('narrow');
    engine.execCommand('setZoom', { percent: 150 });
    assert.equal(zoomOf(engine).autoFit, 'off');
    let prevented = false;
    const handlers = [];
    const root = engine.canvasStack.root;
    root.addEventListener = (type, handler) => handlers.push({ type, handler });
    engine.shortcutManager.destroy();
    engine.shortcutManager.mount();
    handlers.filter(item => item.type === 'keydown').forEach(item => item.handler({ key: '0', code: 'Digit0', ctrlKey: true, preventDefault: () => { prevented = true; }, stopPropagation() {} }));
    assert.equal(prevented, true);
    assert.equal(zoomOf(engine).autoFit, 'narrow', 'Ctrl+0 returns to the automatic fit');
    engine.destroy();
});

test('auto fit: a slow 20 step resize never changes the zoom more often than the width changed', () => {
    const h = createAutoFitHarness(900);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Slow resize.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    let changes = 0;
    let last = zoomOf(engine).scale;
    for (let step = 1; step <= 20; step += 1) {
        h.resize(900 - step * 5);
        h.flush();
        const now = zoomOf(engine).scale;
        if (now !== last) {
            changes += 1;
            last = now;
        }
    }

    assert.ok(changes <= 20, `${changes} zoom changes for 20 width steps`);
    engine.destroy();
});

test('auto fit: an off mode never touches the zoom and destroy disconnects the observer', () => {
    const h = createAutoFitHarness(500);
    const engine = createCanvasDocumentEngine({ host: h.host, document: h.doc, model: createTextModel('Off.', 0) });
    engine.render();
    assert.equal(zoomOf(engine).autoFit, 'off');
    h.resize(300);
    h.flush();
    assert.equal(zoomOf(engine).percent, 100);
    assert.equal(zoomOf(engine).preset, 'custom');

    engine.destroy();
    assert.equal(h.observers[0].disconnected, true);
});

test('auto fit: the snapshot reports zoomAutoFit next to the zoom preset and percent', () => {
    const h = createAutoFitHarness(650);
    const engine = createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Snapshot.', 0),
        autoFit: 'narrow',
    });
    engine.render();
    engine.setAutoFitMode('narrow');
    const view = engine.getSnapshot().formatting.view;
    assert.equal(view.zoomAutoFit, 'narrow');
    assert.equal(view.zoomPreset, 'fitWidth');
    assert.ok(view.zoomPercent < 100);
    engine.destroy();
});

test('proofing analysis and the accessibility mirror are deferred during incremental edits (Phase 6)', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({ host, document: doc, model: createTextModel('Initial paragraph text.', 0) });

    engine.render();
    // The first render analyzes immediately so the accessibility mirror is correct on first paint.
    assert.equal(engine.lastAnalyzedModelVersion, engine.modelStore.getVersion());
    assert.equal(engine.modelAnalysisTimer, 0);
    const mirror = findOne(host, node => node.getAttribute('data-canvas-a11y-block-count') !== null);
    assert.ok(Number(mirror?.getAttribute('data-canvas-a11y-block-count') || '0') >= 1);

    // Simulate a keystroke: change the model (bumped version) and render with dirty blocks.
    const analyzedBefore = engine.lastAnalyzedModelVersion;
    engine.modelStore.setModel(createTextModel('Initial paragraph text edited.', 1));
    engine.render({ dirtyBlockIds: ['p1'] });

    // The O(document) analysis is deferred (debounced), not re-run on the edit frame.
    assert.notEqual(engine.modelStore.getVersion(), analyzedBefore);
    assert.equal(engine.lastAnalyzedModelVersion, analyzedBefore, 'analysis must NOT re-run on the edit frame');
    assert.notEqual(engine.modelAnalysisTimer, 0, 'a deferred analysis must be scheduled');

    // The deferred pass catches the analysis up to the latest model.
    engine.runModelAnalysis();
    assert.equal(engine.lastAnalyzedModelVersion, engine.modelStore.getVersion());

    engine.clearModelAnalysisTimer();
    engine.destroy();
});

test('replacing the model (import/load) refreshes analysis immediately, not on the typing debounce', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({ host, document: doc, model: createTextModel('First document.', 0) });
    engine.render();

    engine.setModel(createTextModel('A different imported document.', 0));
    engine.render();
    assert.equal(engine.lastAnalyzedModelVersion, engine.modelStore.getVersion(), 'a model replacement analyzes immediately');
    assert.equal(engine.modelAnalysisTimer, 0, 'no deferred timer for an immediate analysis');

    engine.destroy();
});

test('selection layout enriches existing connector drawing blocks with endpoint handles', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: createConnectorSelectionModel(),
    });

    engine.render();
    const root = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-engine-root');
    const selectionLayout = engine.getSnapshot().render.selectionLayout;
    const connectorBlock = selectionLayout.blocks.find(block => block.objectId === 'selection-connector');

    assert.equal(connectorBlock.object.kind, 'connector');
    assert.equal(connectorBlock.connector.points.length, 4);
    assert.equal(connectorBlock.object.connector.points.length, 4);

    engine.selectionController.setSelection({
        anchor: { blockId: connectorBlock.blockId, offset: 0 },
        focus: { blockId: connectorBlock.blockId, offset: 0 },
        object: {
            objectId: connectorBlock.objectId,
            blockId: connectorBlock.blockId,
            runId: connectorBlock.runId,
            pageIndex: connectorBlock.pageIndex,
            rect: connectorBlock.rect,
            width: connectorBlock.rect.width,
            height: connectorBlock.rect.height,
            kind: connectorBlock.object.kind,
            connector: connectorBlock.connector,
        },
    });

    assert.equal(root.getAttribute('data-canvas-object-id'), 'selection-connector');
    assert.equal(root.getAttribute('data-canvas-object-connector-handle-count'), '2');
    assert.equal(findAll(host, node => node.getAttribute('data-testid') === 'document-canvas-object-connector-handle-start').length, 1);
    assert.equal(findAll(host, node => node.getAttribute('data-testid') === 'document-canvas-object-connector-handle-end').length, 1);
});

test('autocorrect side effects keep raw typed text as the undo snapshot', async () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const initialModel = createTextModel('Dash: ', 0);
    const firstDashModel = createTextModel('Dash: -', 1);
    const correctedModel = createTextModel('Dash: —', 2);
    const rawTypedModel = createTextModel('Dash: --', 2);
    const engine = createCanvasDocumentEngine({ host, document: doc, model: initialModel });

    engine.render();
    engine.commitInputChange({
        before: { model: initialModel, selection: collapsedTextSelection(6) },
        model: firstDashModel,
        selection: collapsedTextSelection(7),
        edit: { type: 'insertText', text: '-', source: 'insertText' },
        input: { revision: 1, dirtyBlockIds: ['p1'] },
        result: { changed: true, operation: 'insertText', dirtyBlockIds: ['p1'] },
    });
    engine.commitInputChange({
        before: { model: rawTypedModel, selection: collapsedTextSelection(8) },
        model: correctedModel,
        selection: collapsedTextSelection(7),
        edit: { type: 'insertText', text: '-', source: 'insertText' },
        input: { revision: 2, dirtyBlockIds: ['p1'] },
        result: { changed: true, operation: 'emDash', autoCorrect: true, dirtyBlockIds: ['p1'] },
    });

    await new Promise(resolve => setTimeout(resolve, 220));

    const transaction = engine.history.undo();
    assert.equal(textFromModel(transaction.before.model), 'Dash: --');
    assert.equal(textFromModel(transaction.after.model), 'Dash: —');
});

test('annotation overlays skip the per-render rebuild while the document has no annotations (N6.2)', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({ host, document: doc, model: createTextModel('No annotations here', 0) });

    let commentUpdates = 0;
    let revisionUpdates = 0;
    const commentUpdate = engine.commentOverlay.update.bind(engine.commentOverlay);
    const revisionUpdate = engine.revisionOverlay.update.bind(engine.revisionOverlay);
    engine.commentOverlay.update = (...args) => { commentUpdates += 1; return commentUpdate(...args); };
    engine.revisionOverlay.update = (...args) => { revisionUpdates += 1; return revisionUpdate(...args); };

    engine.render();
    assert.equal(commentUpdates, 1, 'the first render establishes the (empty) overlay state');
    engine.render();
    engine.render();
    assert.equal(commentUpdates, 1, 'renders with no comments must not rebuild the overlay');
    assert.equal(revisionUpdates, 1, 'renders with no revisions must not rebuild the overlay');

    // A model WITH comments must update on every render (marker geometry follows the layout).
    const withComment = createTextModel('No annotations here', 1);
    withComment.comments = [{ id: 'c1', text: 'note', anchor: { blockId: 'p1', startOffset: 0, endOffset: 2 } }];
    engine.setModel(withComment);
    const updatesAfterSet = commentUpdates;
    engine.render();
    assert.ok(commentUpdates > updatesAfterSet, 'a present comment keeps the per-render update');
});

test('print preview snapshot is skipped on deferred renders and rebuilt on demand (N6.3)', () => {
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({ host, document: doc, model: createTextModel('Preview me', 0) });

    engine.render();
    const settled = engine.getPrintPreviewSnapshot();
    assert.ok(settled, 'a settled render computes the snapshot');

    engine.render({ deferPaintDiagnostics: true });
    assert.equal(engine.printPreviewStale, true, 'a deferred render marks the snapshot stale');
    const rebuilt = engine.getPrintPreviewSnapshot();
    assert.ok(rebuilt, 'on-demand access rebuilds the snapshot');
    assert.equal(engine.printPreviewStale, false, 'the rebuild clears the stale flag');
    assert.strictEqual(engine.getPrintPreviewSnapshot(), rebuilt, 'repeated access reuses the rebuilt snapshot');
});

test('progressive first layout budgets the mount render and completes on idle continuations (N11)', () => {
    const bigModel = () => ({
        documentId: 'n11-progressive-entry',
        body: {
            blocks: Array.from({ length: 80 }, (_, index) => ({
                id: `p${index}`,
                type: 'paragraph',
                order: index + 1,
                paragraphProperties: {},
                content: {
                    type: 'paragraph',
                    runs: [{ id: `p${index}-run`, type: 'text', text: `Paragraph ${index + 1} ${'progressive layout filler text '.repeat(8)}`, marks: [] }],
                },
            })),
        },
    });

    // N11.7 rollback flag: progressiveFirstLayout:false keeps the old single-pass behaviour.
    const baselineDoc = createFakeDocument();
    const baselineHost = baselineDoc.createElement('div');
    const baseline = createCanvasDocumentEngine({
        host: baselineHost,
        document: baselineDoc,
        model: bigModel(),
        progressiveFirstLayout: false,
    });
    baseline.render();
    const baselineRoot = findOne(baselineHost, node => node.getAttribute('data-testid') === 'document-canvas-engine-root');
    const fullPageCount = Number(baselineRoot.getAttribute('data-canvas-page-count'));
    assert.ok(fullPageCount > 3, `fixture must span several pages (got ${fullPageCount})`);
    assert.equal(baselineRoot.getAttribute('data-canvas-layout-complete'), 'true',
        'a non-progressive render must report a complete layout');

    // Default (flag on): the mount render lays out only the first pages within the budget...
    const idleCallbacks = [];
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: bigModel(),
        scheduleProgressiveIdle: callback => idleCallbacks.push(callback),
    });
    engine.render();
    const root = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-engine-root');
    assert.equal(root.getAttribute('data-canvas-layout-complete'), 'false', 'the mount render must be partial');
    const firstPageCount = Number(root.getAttribute('data-canvas-page-count'));
    assert.ok(firstPageCount < fullPageCount, 'the budgeted mount must not lay out every page');
    assert.ok(Number(root.getAttribute('data-canvas-estimated-page-count')) > firstPageCount,
        'the estimated total page count must extend beyond the laid pages');
    assert.ok(idleCallbacks.length > 0, 'a continuation must be scheduled');

    // N11.3: the bottom spacer estimates the unlaid tail so the scrollbar approximates the document.
    const bottomSpacer = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-virtual-bottom-spacer');
    assert.ok(Number(bottomSpacer.getAttribute('data-canvas-spacer-height')) > 0,
        'the bottom spacer must reserve estimated height for the unlaid tail');

    // ...and idle continuations finish the layout with the same page count as the baseline.
    let guard = 0;
    while (idleCallbacks.length > 0 && guard < 100) {
        idleCallbacks.shift()();
        guard += 1;
    }
    assert.equal(root.getAttribute('data-canvas-layout-complete'), 'true', 'continuations must complete the layout');
    assert.equal(Number(root.getAttribute('data-canvas-page-count')), fullPageCount,
        'the progressive result must reach the full page count');

    // Tile-cache-skipped repaints during the continuations must not zero the painted-command
    // diagnostics of the already-painted first page (E2E contract: "page is not blank").
    const firstPage = findOne(host, node => node.getAttribute('data-testid') === 'document-canvas-page');
    assert.ok(Number(firstPage.getAttribute('data-canvas-painted-command-count')) > 0,
        'the first page must keep its painted-command count across skipped repaints');
});

// Fáze 20 (code review N11.2): an EDIT during the progressive first layout replaces the model
// reference, so the resume token no longer matches — but the re-layout budget must never fall
// below the pages already laid out. Before the fix it clamped back to initialLayoutPageBudget
// (≤8 pages): typing on page 30 of a large document collapsed data-canvas-page-count 30→8,
// clipped the scroll range and hid the caret page until idle chunks caught up again.
test('an edit during the progressive first layout does not shrink the laid page range (Fáze 20)', () => {
    const buildModel = (firstText) => ({
        documentId: 'phase20-progressive-edit',
        body: {
            blocks: Array.from({ length: 240 }, (_, index) => ({
                id: `p${index}`,
                type: 'paragraph',
                order: index + 1,
                paragraphProperties: {},
                content: {
                    type: 'paragraph',
                    runs: [{
                        id: `p${index}-run`,
                        type: 'text',
                        text: index === 0 ? firstText : `Paragraph ${index + 1} ${'progressive edit filler text '.repeat(8)}`,
                        marks: [],
                    }],
                },
            })),
        },
    });

    const idleCallbacks = [];
    const doc = createFakeDocument();
    const host = doc.createElement('div');
    const engine = createCanvasDocumentEngine({
        host,
        document: doc,
        model: buildModel('Original first paragraph'),
        scheduleProgressiveIdle: callback => idleCallbacks.push(callback),
    });
    engine.render();

    // Extend the progressive layout well past the 8-page mount clamp.
    let guard = 0;
    while (engine.canvasStack.getLayoutState().laidPages < 12 && idleCallbacks.length > 0 && guard < 20) {
        idleCallbacks.shift()();
        guard += 1;
    }
    const laidBefore = engine.canvasStack.getLayoutState().laidPages;
    assert.ok(laidBefore >= 12, `fixture must lay out past the mount clamp before the edit (got ${laidBefore})`);
    assert.equal(engine.canvasStack.getLayoutState().complete, false, 'the layout must still be progressive');

    // Simulate the input path: a copy-on-write edit swaps the model REFERENCE without resetting
    // the progressive state (commitInputChange uses modelStore.setModel({normalize:false})).
    engine.modelStore.setModel(buildModel('Edited first paragraph'), { normalize: false });
    engine.render();

    const laidAfter = engine.canvasStack.getLayoutState().laidPages;
    assert.ok(laidAfter >= laidBefore,
        `the re-layout after an edit must keep at least the already-laid range (before ${laidBefore}, after ${laidAfter})`);
});

function createFakeDocument() {
    return {
        createElement(tagName) {
            const normalized = String(tagName).toUpperCase();
            return normalized === 'CANVAS'
                ? new FakeCanvasElement(this)
                : new FakeElement(this, normalized);
        },
    };
}

function createTextModel(text, version = 0) {
    return {
        documentId: 'entry-autocorrect',
        version,
        body: {
            blocks: [{
                id: 'p1',
                sectionId: 'section-1',
                type: 'paragraph',
                order: 10,
                paragraphProperties: {},
                content: {
                    type: 'paragraph',
                    runs: [{ id: 'p1-run', type: 'text', text, marks: [] }],
                },
            }],
        },
        sections: [{ id: 'section-1', blocks: [] }],
    };
}

function createConnectorSelectionModel() {
    return {
        documentId: 'entry-connector-selection',
        body: {
            blocks: [
                drawingBlock('source-shape-block', 'selection-source-shape', 1, 120, 72, {
                    preset: 'rectangle',
                    fill: { color: '#dbeafe', opacity: 1 },
                    stroke: { color: '#2563eb', width: 2 },
                }, { x: 96, y: 120 }),
                drawingBlock('target-shape-block', 'selection-target-shape', 1, 120, 72, {
                    preset: 'ellipse',
                    fill: { color: '#fef3c7', opacity: 1 },
                    stroke: { color: '#d97706', width: 2 },
                }, { x: 420, y: 170 }),
                drawingBlock('connector-block', 'selection-connector', 4, 300, 84, {
                    preset: 'bentConnector',
                    fill: { type: 'none', color: '#ffffff' },
                    stroke: { color: '#0f766e', width: 2, endArrow: 'triangle' },
                    routing: 'elbow',
                    startConnection: { objectId: 'selection-source-shape', site: 'right' },
                    endConnection: { objectId: 'selection-target-shape', site: 'left' },
                }, { x: 216, y: 156 }),
            ],
        },
        sections: [{ id: 'section-1', blocks: [] }],
    };
}

function drawingBlock(blockId, objectId, kind, width, height, shape, position) {
    return {
        id: blockId,
        sectionId: 'section-1',
        type: 'paragraph',
        order: 10,
        paragraphProperties: {},
        content: {
            type: 'paragraph',
            runs: [{
                id: `${objectId}-run`,
                type: 'drawing',
                drawing: {
                    objectId,
                    kind,
                    size: { width, height },
                    naturalSize: { width, height },
                    layout: {
                        kind: 1,
                        anchor: { blockId: blockId, offset: 0 },
                        position: { x: Number(position?.x || 0) || 0, y: Number(position?.y || 0) || 0 },
                        wrap: { mode: 6 },
                        transform: { width, height, lockAspectRatio: false },
                        stacking: { zIndex: kind },
                    },
                    shape,
                },
            }],
        },
    };
}

function collapsedTextSelection(offset) {
    return {
        anchor: { blockId: 'p1', offset },
        focus: { blockId: 'p1', offset },
    };
}

function textFromModel(model) {
    return (model?.body?.blocks?.[0]?.content?.runs || []).map(run => run.text || '').join('');
}

class FakeElement {
    constructor(ownerDocument, tagName) {
        this.ownerDocument = ownerDocument;
        this.tagName = tagName;
        this.children = [];
        this.attributes = new Map();
        this.style = {};
        this.parentNode = null;
        this.textContent = '';
        this.className = '';
    }

    appendChild(child) {
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    append(...children) {
        for (const child of children) {
            this.appendChild(child);
        }
    }

    removeChild(child) {
        this.children = this.children.filter(item => item !== child);
        child.parentNode = null;
        return child;
    }

    replaceChildren(...children) {
        for (const child of this.children) {
            child.parentNode = null;
        }

        this.children = [];
        for (const child of children) {
            this.appendChild(child);
        }
    }

    setAttribute(name, value) {
        this.attributes.set(String(name), String(value));
    }

    getAttribute(name) {
        return this.attributes.has(String(name)) ? this.attributes.get(String(name)) : null;
    }

    removeAttribute(name) {
        this.attributes.delete(String(name));
    }

    addEventListener() {
    }

    removeEventListener() {
    }

    focus() {
        this.focused = true;
    }
}

class FakeCanvasElement extends FakeElement {
    constructor(ownerDocument) {
        super(ownerDocument, 'CANVAS');
        this.width = 0;
        this.height = 0;
        this.context = new FakeCanvasContext();
    }

    getContext(type) {
        assert.equal(type, '2d');
        return this.context;
    }
}

class FakeCanvasContext {
    constructor() {
        this.calls = [];
        this.transforms = [];
    }

    setTransform(...args) {
        this.transforms.push(args);
        this.calls.push({ name: 'setTransform', args });
    }

    clearRect(...args) {
        this.calls.push({ name: 'clearRect', args });
    }

    fillRect(...args) {
        this.calls.push({ name: 'fillRect', args });
    }

    fillText(...args) {
        this.calls.push({ name: 'fillText', args });
    }

    strokeRect(...args) {
        this.calls.push({ name: 'strokeRect', args });
    }

    save(...args) {
        this.calls.push({ name: 'save', args });
    }

    restore(...args) {
        this.calls.push({ name: 'restore', args });
    }

    beginPath(...args) {
        this.calls.push({ name: 'beginPath', args });
    }

    moveTo(...args) {
        this.calls.push({ name: 'moveTo', args });
    }

    lineTo(...args) {
        this.calls.push({ name: 'lineTo', args });
    }

    stroke(...args) {
        this.calls.push({ name: 'stroke', args });
    }

    setLineDash(...args) {
        this.calls.push({ name: 'setLineDash', args });
    }
}

function findOne(root, predicate) {
    const result = findAll(root, predicate)[0];
    assert.ok(result, 'Expected a matching fake DOM node.');
    return result;
}

function findAll(root, predicate) {
    const results = [];
    visit(root);
    return results;

    function visit(node) {
        if (predicate(node)) {
            results.push(node);
        }

        for (const child of node.children || []) {
            visit(child);
        }
    }
}

test('scrollMatchRectIntoView scrolls a transient probe at the match rectangle into the middle, then removes it', () => {
    const calls = [];
    const probes = [];
    const pageElement = {
        getAttribute: name => (name === 'data-canvas-page-zoom-scale' ? '2' : null),
        appendChild: node => { probes.push(node); },
    };
    const fake = {
        document: {
            createElement: () => {
                const node = {
                    style: {},
                    setAttribute() {},
                    scrollIntoView: options => calls.push(['scroll', options, node.style.cssText]),
                    remove: () => calls.push(['remove']),
                };
                return node;
            },
        },
        canvasStack: { pages: new Map([['3', { pageElement }]]) },
    };

    CanvasDocumentEngine.prototype.scrollMatchRectIntoView.call(fake, { pageIndex: 3, x: 10, y: 400, width: 50, height: 20 });

    assert.equal(probes.length, 1, 'a probe was placed inside the page');
    assert.equal(calls[0][0], 'scroll');
    assert.deepEqual(calls[0][1], { block: 'center', inline: 'nearest' });
    assert.match(calls[0][2], /left:20px;top:800px;/, 'layout units are multiplied by the page zoom');
    assert.equal(calls[1][0], 'remove', 'the probe never stays in the DOM');

    // Nothing to scroll to: no page, no rectangle - and no throw.
    assert.doesNotThrow(() => CanvasDocumentEngine.prototype.scrollMatchRectIntoView.call(fake, null));
    assert.doesNotThrow(() => CanvasDocumentEngine.prototype.scrollMatchRectIntoView.call(fake, { pageIndex: 9, x: 0, y: 0, height: 1 }));
});

test('the page column of a tablet and a phone is padded by a hairline, a desktop keeps the view mode padding', () => {
    const inLayout = layout => ({ closest: selector => (selector === '[data-layout]' ? { getAttribute: () => layout } : null) });

    assert.equal(resolveRootPaddingInline(inLayout('desktop'), 24), 24);
    assert.equal(resolveRootPaddingInline(inLayout('tablet'), 24), COMPACT_ROOT_PADDING_INLINE);
    assert.equal(resolveRootPaddingInline(inLayout('mobile'), 24), COMPACT_ROOT_PADDING_INLINE);
    // A narrower view-mode padding is never widened, an element outside any editor and a throwing probe fall back to the view mode's own.
    assert.equal(resolveRootPaddingInline(inLayout('mobile'), 2), 2);
    assert.equal(resolveRootPaddingInline({ closest: () => null }, 24), 24);
    assert.equal(resolveRootPaddingInline({ closest: () => { throw new Error('detached'); } }, 24), 24);
});

test('getFitPaddingInline adds the mount padding to the root padding in every layout (a desktop never below 48)', () => {
    const host = layout => ({ closest: () => ({ getAttribute: () => layout }) });
    const withStyle = (layout, left, right) => {
        const previous = globalThis.getComputedStyle;
        globalThis.getComputedStyle = () => ({ paddingLeft: `${left}px`, paddingRight: `${right}px` });
        try {
            return CanvasDocumentEngine.prototype.getFitPaddingInline.call({ host: host(layout) });
        } finally {
            globalThis.getComputedStyle = previous;
        }
    };

    // The mount's own padding counts on a desktop too: the page column is root padding + mount padding wide (16 px a side in the demo), and a
    // fit that ignored it left the document column 32 px wider than its viewport.
    assert.equal(withStyle('desktop', 8, 8), 8 + 8 + 48);
    assert.equal(withStyle('desktop', 16, 16), 32 + 48);
    assert.equal(withStyle('desktop', 0, 0), 48);
    assert.equal(withStyle('mobile', 8, 8), 8 + 8 + 2 * COMPACT_ROOT_PADDING_INLINE);
    assert.equal(withStyle('tablet', 8, 8), 8 + 8 + 2 * COMPACT_ROOT_PADDING_INLINE);
});

// ── Phase 12: the viewport-bounded frame - the document region is the scroller (data-scroll-viewport) ─────────────────

function markAsBoundedScroller(h, { top = 100, clientHeight = 600 } = {}) {
    h.scroller.getAttribute = name => (name === 'data-scroll-viewport' ? 'true' : null);
    h.scroller.clientHeight = clientHeight;
    h.scroller.scrollHeight = 9000;
    h.scroller.getBoundingClientRect = () => ({ top, height: clientHeight, width: h.scroller.clientWidth });
}

function createBoundedEngine(h, options = {}) {
    h.doc.defaultView.addEventListener = (type, listener, opts) => { (h.viewListeners ||= []).push({ type, listener, opts, added: true }); };
    h.doc.defaultView.removeEventListener = (type, listener, opts) => { (h.viewListeners ||= []).push({ type, listener, opts, added: false }); };
    h.doc.documentElement = { scrollTop: 0, scrollHeight: 900, clientHeight: 900, getBoundingClientRect: () => ({ top: 0 }) };
    h.doc.scrollingElement = h.doc.documentElement;
    h.host.ownerDocument = h.doc;
    return createCanvasDocumentEngine({
        host: h.host,
        document: h.doc,
        model: createTextModel('Bounded frame.', 0),
        autoFit: 'off',
        ...options,
    });
}

test('bounded frame: readViewport measures the marked scroller, not the window', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h, { top: 100, clientHeight: 600 });
    const engine = createBoundedEngine(h);
    engine.render();
    // the engine root is 2400 px above the top edge of the scroller's visible area: the reader is 2400 px down the document
    engine.canvasStack.root.getBoundingClientRect = () => ({ top: 100 - 2400 });

    const viewport = engine.readViewport();

    assert.equal(viewport.scrollTop, 2400);
    assert.equal(viewport.height, 600, 'the height is the scroller\'s own, not window.innerHeight (900)');
    engine.destroy();
});

test('bounded frame: without a marked scroller readViewport keeps the window measurements', () => {
    const h = createAutoFitHarness(1200);
    const engine = createBoundedEngine(h);
    engine.render();
    engine.canvasStack.root.getBoundingClientRect = () => ({ top: -700 });

    const viewport = engine.readViewport();

    assert.equal(viewport.scrollTop, 700);
    assert.equal(viewport.height, 900, 'window.innerHeight - the page is the scroller');
    engine.destroy();
});

test('bounded frame: an explicit viewport still wins over the measured one', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h);
    const engine = createBoundedEngine(h);
    engine.render();
    engine.viewport = { scrollTop: 50, height: 321 };

    const viewport = engine.readViewport();

    assert.equal(viewport.scrollTop, 50);
    assert.equal(viewport.height, 321);
    engine.destroy();
});

test('bounded frame: the scroll viewport the fit measures is the marked scroller', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h);
    const engine = createBoundedEngine(h);
    engine.render();

    const metrics = engine.getViewportMetrics();

    assert.equal(metrics.viewportHeight, 600, 'fit-page fits the bounded frame, not the window');
    assert.equal(metrics.viewportWidth, 1200);
    engine.destroy();
});

test('bounded frame: a scroll of the marked scroller repaints (captured on the window), the scroll of anything else does not', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h);
    h.scroller.contains = node => node === h.host;
    const engine = createBoundedEngine(h);
    engine.render();
    let scrolls = 0;
    engine.handleScroll = () => { scrolls += 1; };
    const captured = h.viewListeners.filter(entry => entry.type === 'scroll' && entry.added && entry.opts?.capture === true);
    assert.equal(captured.length, 1, 'ONE capture-phase listener: scroll does not bubble, a scroller is found by its event target');

    captured[0].listener({ target: h.scroller });
    assert.equal(scrolls, 1);
    captured[0].listener({ target: { getAttribute: () => null, contains: () => false } });
    captured[0].listener({ target: h.doc });
    assert.equal(scrolls, 1, 'a scrolled inspector panel or the page itself is not the document scroller');

    engine.destroy();
    const removed = h.viewListeners.filter(entry => entry.type === 'scroll' && entry.added === false && entry.opts?.capture === true);
    assert.equal(removed.length, 1, 'the capture listener is removed with the same capture flag');
    assert.equal(removed[0].listener, captured[0].listener);
});

test('bounded frame: scrollToPage of an unmounted page scrolls the marked scroller by the scaled page stride (never the window)', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h, { top: 100, clientHeight: 600 });
    const engine = createBoundedEngine(h);
    engine.render();
    let windowScrolls = 0;
    h.doc.defaultView.scrollTo = () => { windowScrolls += 1; };
    // a twelve page document at half scale, page 10 not mounted; the engine root sits 40 px below the scroller's top edge
    engine.lastRender = { displayList: { pages: Array.from({ length: 12 }, () => ({ height: 1000 })) }, view: { zoomScale: 0.5 } };
    engine.canvasStack.pages.delete('10');
    engine.canvasStack.root.getBoundingClientRect = () => ({ top: 100 + 40 - h.scroller.scrollTop });
    h.scroller.scrollTop = 0;

    const result = engine.scrollToPage(10);

    assert.deepEqual({ scrolled: result.scrolled, mounted: result.mounted, pageIndex: result.pageIndex }, { scrolled: true, mounted: false, pageIndex: 10 });
    assert.equal(windowScrolls, 0, 'the window was not touched');
    assert.equal(h.scroller.scrollTop, 40 + 24 + 10 * (1000 * 0.5 + 24), 'root offset + padding + the stride of the SCALED pages');
    engine.destroy();
});

test('bounded frame: the reading position of a fit is kept on the marked scroller even when it does not overflow yet', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h, { top: 0, clientHeight: 600 });
    h.scroller.scrollHeight = 600; // a short document: nothing to scroll, but the marker still names the scroller
    const engine = createBoundedEngine(h, { autoFit: 'narrow' });
    engine.render();
    h.doc.documentElement.scrollTop = 123;

    const scroller = engine.resolveReadingScroller();

    assert.equal(scroller.element, h.scroller);
    assert.equal(scroller.isDocument, false);
    engine.destroy();
});

// ── Phase 12 fix round: scrolling inside the marked scroller only, the settle loop yields to the reader, the reading position survives a frame change ──

function fakeScrollTarget(rect) {
    const target = { scrolled: [], getBoundingClientRect: () => rect(), scrollIntoView: options => target.scrolled.push(options) };
    return target;
}

test('scrollElementIntoViewWithin scrolls only the marked scroller (the page and every other ancestor keep their position)', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h, { top: 100, clientHeight: 600 });
    h.scroller.scrollLeft = 0;
    h.scroller.clientWidth = 1200;
    h.scroller.scrollTop = 1000;
    h.scroller.getBoundingClientRect = () => ({ top: 100, left: 0, height: 600, width: 1200 });
    const target = fakeScrollTarget(() => ({ top: 900, bottom: 940, left: 10, right: 60, height: 40, width: 50 }));

    scrollElementIntoViewWithin(h.host, target, { block: 'start', inline: 'nearest' });
    assert.equal(h.scroller.scrollTop, 1000 + (900 - 100), 'the element top is brought to the top edge of the scroller');
    assert.equal(target.scrolled.length, 0, 'the native scrollIntoView (which scrolls every ancestor, the window included) is not used');

    h.scroller.scrollTop = 1000;
    scrollElementIntoViewWithin(h.host, target, { block: 'center', inline: 'nearest' });
    assert.equal(h.scroller.scrollTop, 1000 + (920 - (100 + 300)), 'center puts the middle of the element in the middle of the scroller');

    h.scroller.scrollTop = 1000;
    const inView = fakeScrollTarget(() => ({ top: 200, bottom: 240, left: 10, right: 60, height: 40, width: 50 }));
    scrollElementIntoViewWithin(h.host, inView, { block: 'nearest', inline: 'nearest' });
    assert.equal(h.scroller.scrollTop, 1000, 'nearest leaves an element that is already visible alone');
    const below = fakeScrollTarget(() => ({ top: 760, bottom: 800, left: 10, right: 60, height: 40, width: 50 }));
    scrollElementIntoViewWithin(h.host, below, { block: 'nearest', inline: 'nearest' });
    assert.equal(h.scroller.scrollTop, 1000 + (800 - 700), 'nearest brings an element below the view just into it');
});

test('scrollElementIntoViewWithin falls back to the native scrollIntoView without a marked scroller', () => {
    const h = createAutoFitHarness(1200);
    const target = fakeScrollTarget(() => ({ top: 0, bottom: 10, left: 0, right: 10, height: 10, width: 10 }));

    scrollElementIntoViewWithin(h.host, target, { block: 'center', inline: 'nearest' });

    assert.deepEqual(target.scrolled, [{ block: 'center', inline: 'nearest' }]);
});

test('bounded frame: the settle loop of scrollToPage yields to a reader who scrolled in between', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h, { top: 100, clientHeight: 600 });
    h.scroller.getBoundingClientRect = () => ({ top: 100, left: 0, height: 600, width: 1200 });
    const engine = createBoundedEngine(h);
    engine.render();
    engine.lastRender = { displayList: { pages: Array.from({ length: 12 }, () => ({ height: 1000 })) }, view: { zoomScale: 0.5 } };
    engine.canvasStack.root.getBoundingClientRect = () => ({ top: 140 - h.scroller.scrollTop });
    engine.canvasStack.pages.delete('10');
    h.scroller.scrollTop = 0;

    engine.scrollToPage(10);
    const expected = h.scroller.scrollTop;
    // the repaint mounts the page; meanwhile the reader scrolls elsewhere
    const page = { getBoundingClientRect: () => ({ top: 5000, bottom: 5500, left: 0, right: 10, height: 500, width: 10 }), scrollIntoView: () => { throw new Error('native'); } };
    engine.canvasStack.pages.set('10', { pageElement: page });
    h.scroller.scrollTop = expected + 700;
    h.flush();

    assert.equal(h.scroller.scrollTop, expected + 700, 'the reader\'s own scroll is not yanked back to the page');
    engine.destroy();
});

test('bounded frame: the reading position survives a change of the frame (fullscreen, a new Height)', () => {
    const h = createAutoFitHarness(1200);
    markAsBoundedScroller(h, { top: 100, clientHeight: 600 });
    let viewTop = 100;
    h.scroller.getBoundingClientRect = () => ({ top: viewTop, left: 0, height: 600, width: 1200 });
    const engine = createBoundedEngine(h);
    engine.render();
    const pageTop = 2400; // the page starts 2400 px into the scroller's content
    const page = engine.canvasStack.pages.get('0').pageElement;
    page.getBoundingClientRect = () => ({ top: viewTop + pageTop - h.scroller.scrollTop, height: 1123, bottom: viewTop + pageTop - h.scroller.scrollTop + 1123 });
    h.scroller.scrollTop = pageTop + 300;

    assert.equal(engine.captureReadingPosition(), true);
    // the frame changes: another scroller box, the scroll position is gone
    viewTop = 0;
    h.scroller.scrollTop = 0;
    assert.equal(engine.restoreReadingPosition(), true);
    h.flush();

    assert.ok(Math.abs(h.scroller.scrollTop - (pageTop + 300)) < 2, `scrollTop ${h.scroller.scrollTop}`);
    assert.equal(engine.restoreReadingPosition(), false, 'a restore is one-shot');
    engine.destroy();
});
