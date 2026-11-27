import assert from 'node:assert/strict';
import test from 'node:test';
import { CANVAS_LAYER_KINDS, CanvasDocumentEngine, createCanvasDocumentEngine } from './entry.mjs';

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
