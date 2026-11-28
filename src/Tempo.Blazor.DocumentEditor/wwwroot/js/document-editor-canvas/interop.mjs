import { createCanvasDocumentEngine } from './entry.mjs';
import { isDeliberateSelectionNotification } from './selection-cadence.mjs';
import { buildFormattingState } from './format-state.mjs';
import { createViewChangeNotifier } from './view/view-change-notifier.mjs';
import { selectionToRestoreAfterModelRefresh } from './selection/selection-restore.mjs';
import { extractAnnotations, countModelWords } from './annotations-state.mjs';
import { clampExportScale, renderDisplayListPageToCanvas } from './render/page-image-export.mjs';
import { collectRedactedRunIds, translateDisplayListToLayoutSnapshot } from './render/layout-snapshot-export.mjs';
import { findSigningFieldAtSelection } from './controls/signing-field-selection.mjs';
import { findContentControlAtSelection } from './controls/content-control-selection.mjs';
import { extractSigningFields } from './controls/signing-field-areas.mjs';

const instances = new Map();
let nextInstanceId = 1;

// Debounce window for JS->.NET notifications (change + selection). Each invokeMethodAsync blocks the
// single WASM thread, which on a HUMAN typing cadence (~150-250 ms/key) lands BETWEEN keystrokes and makes
// glyphs appear in batches instead of one-per-key. This window is deliberately wider than typical typing
// gaps so that during continuous typing/selection NO .NET call fires at all — the canvas paints every key
// unblocked — and .NET only catches up (toolbar, dirty, reconcile) once the user actually pauses.
const NET_NOTIFY_DEBOUNCE_MS = 400;

export function createInteropBridge(engine) {
    if (!engine || typeof engine.getSnapshot !== 'function') {
        throw new Error('CanvasDocumentEngine interop bridge requires an engine instance.');
    }

    return {
        ready() {
            return engine.getSnapshot().mounted;
        },
        snapshot() {
            return engine.getSnapshot();
        },
        focus() {
            return engine.focusInput();
        },
        destroy() {
            engine.destroy();
        },
    };
}

export function mount(hostElement, mountElement, modelJson, optionsJson, dotNetRef) {
    const host = resolveElement(hostElement, 'Canvas engine host element is required.');
    const mountHost = resolveElement(mountElement, 'Canvas engine mount element is required.');
    const model = parseJson(modelJson, {});
    const options = parseJson(optionsJson, {});
    const handle = `canvas-document-engine-${nextInstanceId++}`;

    mountHost.replaceChildren();

    let state = null;
    const engine = createCanvasDocumentEngine({
        host: mountHost,
        document: mountHost.ownerDocument,
        model,
        pageSettings: options.pageSettings,
        ariaLabel: options.ariaLabel,
        inputAriaLabel: options.inputAriaLabel,
        inputId: options.inputId,
        accessibility: options.accessibility,
        contentControlRenderMode: options.contentControlRenderMode,
        signingRoles: options.signingRoles,
        uploadImage: file => uploadClipboardImage(dotNetRef, model.documentId, file),
        proofing: options.proofing,
        author: options.author,
        readOnly: options.readOnly,
        canEdit: options.canEdit,
        trackChanges: options.trackChanges,
        reviewDisplayMode: options.trackChanges?.reviewDisplayMode || options.reviewDisplayMode,
        autoFit: options.autoFit,
        // Phase 11: the engine re-fits the page on a resize of the document column on its own; the host learns the new zoom here.
        // The report is debounced (trailing, ~150 ms) and sent only when the percent / preset / autoFit really changed: a drag of the splitter
        // re-fits the page every frame, the host needs the zoom it settled on.
        onViewChanged: () => state?.viewNotifier?.request(),
        onContextMenu: payload => notifyDotNet(dotNetRef, 'OnCanvasContextMenuRequested', payload),
        onSelectionChange: payload => {
            if (!state) {
                return;
            }
            // Carry the toolbar UI snapshot ONLY on a deliberate (range/object) selection — the only case
            // where the pressed-state must update promptly, and it is low-frequency. Building it for every
            // collapsed caret move (typing, arrow navigation, the selection reset emitted while a
            // ReplaceDocumentAsync runs) would add per-event marshalling for no UI benefit and destabilises
            // heavy flows. Collapsed selections fall through to the debounced toolbar-sync as before.
            if (payload && typeof payload === 'object' && isDeliberateSelectionNotification(payload)) {
                payload.uiState = buildUiState(state);
                state.viewNotifier?.markNotified(engine.commandRuntime.getViewState().zoom);
            }
            notifySelectionChanged(state, payload);
        },
        onCommandPaletteRequested: () => notifyDotNet(dotNetRef, 'OnCanvasCommandPaletteRequested', {}),
        onRibbonFocusRequested: () => notifyDotNet(dotNetRef, 'OnCanvasRibbonFocusRequested', {}),
        onVersionsPanelRequested: () => notifyDotNet(dotNetRef, 'OnCanvasVersionsPanelRequested', {}),
        onAnnotationSelected: payload => notifyDotNet(dotNetRef, 'OnCanvasAnnotationSelected', payload),
        onModelChanged: () => {
            if (!state) {
                return;
            }

            state.dirty = isDirtyForState(state);
            notifyChanged(state);
        },
    });
    const result = engine.render();
    // Fit the page to the column before the host learns the engine is ready (the second paint only happens when the page does not fit).
    engine.setAutoFitMode(engine.autoFitMode);
    state = {
        engine,
        host,
        mountHost,
        dotNetRef,
        handle,
        dirty: false,
        savedVersion: engine.getSnapshot().modelVersion,
        options,
        viewNotifier: createViewChangeNotifier({
            read: () => engine.commandRuntime.getViewState().zoom,
            notify: () => notifyDotNet(dotNetRef, 'OnCanvasViewChanged', { uiState: buildUiState(state) }),
            // The ready snapshot below already carries the zoom the engine chose at mount.
            initial: engine.commandRuntime.getViewState().zoom,
            setTimeout: (callback, delay) => (mountHost.ownerDocument?.defaultView || globalThis).setTimeout(callback, delay),
            clearTimeout: timer => (mountHost.ownerDocument?.defaultView || globalThis).clearTimeout(timer),
        }),
    };
    instances.set(handle, state);
    setReadyAttributes(state, true);
    notifyDotNet(dotNetRef, 'OnCanvasEngineReady', buildStateSnapshot(state, result));
    return handle;
}

export function setOptions(handle, optionsJson) {
    const state = getInstance(handle);
    const options = parseJson(optionsJson, {});
    state.options = {
        ...(state.options || {}),
        ...options,
    };
    state.engine.updateOptions(options);
    return JSON.stringify({ ok: true });
}

export function dispose(handle) {
    const state = instances.get(handle);
    if (!state) {
        return;
    }

    {
        const view = state.document?.defaultView || globalThis;
        const clear = view.clearTimeout || clearTimeout;
        if (state.changeNotifyTimer) { clear(state.changeNotifyTimer); state.changeNotifyTimer = 0; }
        if (state.selectionNotifyTimer) { clear(state.selectionNotifyTimer); state.selectionNotifyTimer = 0; }
    }

    state.viewNotifier?.destroy();
    state.engine.destroy();
    setReadyAttributes(state, false);
    instances.delete(handle);
}

export function getModelJson(handle) {
    const state = getInstance(handle);
    // Phase N2: read the model directly — getSnapshot() would also run the O(document)
    // queryCommandState (outline + bookmarks walk) just to throw everything but `model` away.
    const model = typeof state.engine.getModel === 'function'
        ? state.engine.getModel()
        : state.engine.getSnapshot().model;
    // Diagnostic counter: full-document pulls should happen only on save/export/compare, never on
    // the settled-typing path (asserted by the typing E2E).
    state.modelJsonRequestCount = (state.modelJsonRequestCount || 0) + 1;
    state.host.setAttribute('data-canvas-model-json-request-count', String(state.modelJsonRequestCount));
    return JSON.stringify(model);
}

export function getSnapshotJson(handle) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.getSnapshot());
}

export function setModel(handle, modelJson) {
    const state = getInstance(handle);
    const model = parseJson(modelJson, {});
    state.engine.setModel(model).render();
    state.dirty = true;
    notifyChanged(state);
}

// `options.preserveSelection` is sent by the host ONLY for the mirror refresh that follows a panel edit; every other replacement (accept /
// reject, template, import, version preview / restore, a document switch) starts from the engine's own selection.
export function replaceModel(handle, modelJson, options = {}) {
    const state = getInstance(handle);
    const model = parseJson(modelJson, {});
    const previousModel = state.engine.getModel?.() ?? null;
    const previousSelection = state.engine.selectionController?.getSelection?.() ?? null;
    state.engine.setModel(model).render();
    // The host pushes the same document back after a panel edit (the C# mirror refresh): the caret must not jump to the start of it.
    const restore = selectionToRestoreAfterModelRefresh(previousModel, previousSelection, state.engine.getModel?.() ?? model, state.engine.selectionController?.getSelection?.() ?? null, options ?? {});
    if (restore) {
        // A just-reset progressive layout would resolve (and re-resolve, on its idle continuation) the restored caret against a partial
        // layout: finish the layout first, then put the caret back.
        state.engine.ensurePointerHitLayout?.();
        state.engine.selectionController.setSelection(restore);
    }

    state.savedVersion = state.engine.getSnapshot().modelVersion;
    state.dirty = false;
}

export function applyRemoteOperationBatch(handle, batchJson) {
    const state = getInstance(handle);
    const batch = parseJson(batchJson, {});
    const result = state.engine.applyRemoteOperationBatch(batch);
    state.dirty = isDirtyForState(state);
    state.host.setAttribute('data-canvas-collaboration-applied-count', String((result.appliedOperationIds || []).length));
    state.host.setAttribute('data-canvas-collaboration-failed-count', String((result.failedOperationIds || []).length));
    notifyChanged(state);
    return JSON.stringify(result);
}

export function applyRemoteCursor(handle, cursorJson) {
    const state = getInstance(handle);
    const cursor = parseJson(cursorJson, {});
    const result = state.engine.applyRemoteCursor(cursor);
    state.host.setAttribute('data-canvas-presence-count', String(result.cursorCount || 0));
    return JSON.stringify(result);
}

export function applyRemoteCursors(handle, cursorsJson) {
    const state = getInstance(handle);
    const cursors = parseJson(cursorsJson, []);
    const result = state.engine.applyRemoteCursors(cursors);
    state.host.setAttribute('data-canvas-presence-count', String(result.cursorCount || 0));
    return JSON.stringify(result);
}

export function getCollaborationStateJson(handle) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.getSnapshot().collaboration || {});
}

// B3: light pull of just the comment + revision lists out of the live engine model (no full-document marshal),
// so the C# comment rail / revision panel can read them without depending on the debounced document mirror.
export function getAnnotationsJson(handle) {
    const state = getInstance(handle);
    const model = typeof state.engine.modelStore?.getModel === 'function'
        ? state.engine.modelStore.getModel()
        : state.engine.getSnapshot().model;
    const result = extractAnnotations(model);
    // B6: also carry the live word + page count so the status bar stays current without the C# document mirror
    // (no per-edit full marshal). pageCount comes from the already-computed layout; wordCount from the model.
    result.wordCount = countModelWords(model);
    result.pageCount = Array.isArray(state.engine.lastLayout?.pages) ? state.engine.lastLayout.pages.length : 0;
    // Perf plan N11.5: consumers asserting final page/word counts must wait for layoutComplete —
    // during the progressive first layout the counts only cover the laid-out prefix.
    result.layoutComplete = state.engine.progressiveLayout?.complete !== false;
    return JSON.stringify(result);
}

// B1: hand the engine's pending local operation batches to the host (C#) for relay, clearing them from the
// op-log. The host forwards each batch verbatim to collaborators (dumb pipe) — no C# document diff needed.
export function takeLocalOperationBatchesJson(handle) {
    const state = getInstance(handle);
    const log = state.engine?.operationLog;
    if (!log || typeof log.takeLocalBatches !== 'function') {
        return '[]';
    }

    return JSON.stringify(log.takeLocalBatches());
}

export function getOfflineStateJson(handle) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.getOfflineState());
}

export function isDirty(handle) {
    const state = getInstance(handle);
    return state.dirty || engineModelVersion(state) !== state.savedVersion;
}

export function markSaved(handle) {
    const state = getInstance(handle);
    state.savedVersion = state.engine.getSnapshot().modelVersion;
    state.dirty = false;
}

export function focus(handle) {
    const state = getInstance(handle);
    return state.engine.focusInput();
}

export function execCommand(handle, commandId, argumentJson = null) {
    const state = getInstance(handle);
    const argument = parseJson(argumentJson, null);
    const result = state.engine.execCommand(commandId, argument) || {};
    state.dirty = isDirtyForState(state);
    notifyChanged(state);
    // Bundle the primitives-only UI snapshot (formatting pressed-state, dirty, undo availability, page count)
    // INTO the command response so the .NET toolbar can update from this single round-trip instead of firing
    // a follow-up batch of interop pulls (getFormattingStateJson/getUndoStateJson/isDirty/...). Cheap: it is
    // the same O(selection) readback the pull did. (Perf phase 2.2.)
    if (result && typeof result === 'object') {
        result.uiState = buildUiState(state);
    }

    return JSON.stringify(result);
}

export function setTrackChangesEnabled(handle, enabled, authorJson = null) {
    const state = getInstance(handle);
    const author = parseJson(authorJson, null);
    const result = state.engine.setTrackChangesEnabled(enabled === true, author);
    return JSON.stringify({ enabled: result });
}

export function setReviewDisplayMode(handle, mode) {
    const state = getInstance(handle);
    const result = state.engine.setReviewDisplayMode(mode);
    return JSON.stringify({ reviewDisplayMode: result });
}

export function selectComment(handle, commentId) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.selectComment(commentId) || {});
}

export function selectRevision(handle, revisionId) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.selectRevision(revisionId) || {});
}

// Programmatic object selection (test/automation seam) — selects an image/drawing by id without a synthetic
// pointer click, then the normal selection push surfaces the object mini toolbar.
export function selectObject(handle, objectId) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.selectObjectById(objectId) || {});
}

// Programmatic text-range selection (test/automation seam) — selects blockId[start..end] without a synthetic
// drag, then the normal selection push surfaces the mini toolbar (e.g. inside a table cell).
export function selectTextRange(handle, blockId, startOffset, endOffset) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.selectTextRange(blockId, startOffset, endOffset) || {});
}

// B6: enter/exit header-footer editing programmatically (ribbon "Edit header/footer" + "Close" buttons).
export function editHeaderFooter(handle, type) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.editHeaderFooter(type) || {});
}

export function closeHeaderFooter(handle) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.closeHeaderFooter() || {});
}

// Diagnostic seam: the last mini toolbar payload the engine pushed (for tests to inspect placement/visibility).
export function getLastMiniToolbarPayloadJson(handle) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.lastMiniToolbarPayload || null);
}

// B11/B12: programmatic clipboard operations for the context menu (no clipboard event to hook). Copy/cut write
// the selected fragment to the system clipboard; paste reads it via the async Clipboard API. Async — the
// caller awaits the JSON result ({ handled, operation, reason? }).
export async function copySelection(handle) {
    const state = getInstance(handle);
    return JSON.stringify((await state.engine.clipboardController.copyToSystemClipboard()) || {});
}

export async function cutSelection(handle) {
    const state = getInstance(handle);
    return JSON.stringify((await state.engine.clipboardController.cutToSystemClipboard()) || {});
}

export async function pasteFromSystemClipboard(handle) {
    const state = getInstance(handle);
    return JSON.stringify((await state.engine.clipboardController.pasteFromSystemClipboard()) || {});
}

export function captureCommentAnchorJson(handle) {
    const state = getInstance(handle);
    const selection = state.engine.getSnapshot().selection || {};
    return JSON.stringify({
        type: selection.isCollapsed === false ? 'textRange' : 'block',
        blockId: selection.anchor?.blockId || selection.focus?.blockId || '',
        startOffset: Number(selection.anchor?.offset || 0) || 0,
        endOffset: Number(selection.focus?.offset || selection.anchor?.offset || 0) || 0,
    });
}

export function queryCommand(handle, commandId = null) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.queryCommand(commandId));
}

function readFormattingState(state) {
    // queryCommandState({ includeNavigation: false }) inspects only the current selection (no outline /
    // bookmark walk over the whole document) — far cheaper than the full engine snapshot.
    const formatting = (typeof state.engine.commandRuntime?.queryCommandState === 'function'
        ? state.engine.commandRuntime.queryCommandState({ includeNavigation: false, formattingOnly: true })
        : state.engine.getSnapshot().formatting) || {};
    return buildFormattingState(formatting);
}

function readUndoState(state) {
    // history.snapshot() only reads the undo/redo stack lengths — cheap, unlike the full engine snapshot.
    const history = (typeof state.engine.history?.snapshot === 'function'
        ? state.engine.history.snapshot()
        : state.engine.getSnapshot().history) || {};
    return {
        canUndo: (history.undoDepth || 0) > 0,
        canRedo: (history.redoDepth || 0) > 0,
    };
}

// Small, primitives-only UI snapshot pushed to .NET alongside the change + deliberate-selection events so the
// toolbar pressed-state / dirty / undo availability / page count update WITHOUT a round-trip of interop pulls
// (B2). Everything here is O(selection) or O(1): formatting reads the current selection only, undo reads stack
// depths, pageCount reads the already-computed layout. Word count stays C#-side for now (it is O(document) and
// not latency-critical; it rides the debounced reconcile — revisit when the mirror is removed in B6).
function buildUiState(state) {
    const undo = readUndoState(state);
    return {
        formatting: readFormattingState(state),
        isDirty: isDirtyForState(state),
        canUndo: undo.canUndo,
        canRedo: undo.canRedo,
        pageCount: Array.isArray(state.engine.lastLayout?.pages) ? state.engine.lastLayout.pages.length : 0,
        modelVersion: engineModelVersion(state),
    };
}

export function getFormattingStateJson(handle) {
    const state = getInstance(handle);
    return JSON.stringify(readFormattingState(state));
}

export function getPrintPreviewStateJson(handle) {
    const state = getInstance(handle);
    // N6.3: read the (lazily rebuilt) snapshot directly — the full engine snapshot would also run
    // the O(document) queryCommandState just to be discarded.
    // Fáze 23 (code review N11): a routine state POLL must not force the full synchronous layout
    // during the progressive first-layout window (that negated N11 and blocked the UI thread on a
    // whole-document layout). Only an ACTIVE print preview needs every page — and its activation
    // render already completes the layout (render() skips the page budget when the preview is on).
    const previewActive = state.engine.commandRuntime?.getViewState?.()?.printPreview?.active === true;
    const printPreview = typeof state.engine.getPrintPreviewSnapshot === 'function'
        ? state.engine.getPrintPreviewSnapshot({ allowPartial: previewActive !== true })
        : state.engine.getSnapshot().printPreview;
    return JSON.stringify({
        ...(printPreview || {}),
        dialog: state.engine.lastPrintDialog || null,
    });
}

export function getUndoStateJson(handle) {
    const state = getInstance(handle);
    return JSON.stringify(readUndoState(state));
}

/**
 * The caret (or the selection's bounding rect) in viewport coordinates: the page-local rect the selection controller reports
 * plus the page element's client rect. Null when the page is not mounted or the selection has no rect.
 */
export function selectionViewportRect(engine, selection) {
    const rect = selection?.boundingRect || selection?.caretRect;
    const pageIndex = selection?.boundingRect ? selection.boundingRect.pageIndex : selection?.pageIndex;
    if (!rect) {
        return null;
    }

    const page = engine?.canvasStack?.pages?.get?.(String(pageIndex ?? 0));
    const bounds = page?.pageElement?.getBoundingClientRect?.();
    if (!bounds) {
        return null;
    }

    // The page-local rect is in unscaled page units; the page is painted at data-canvas-page-zoom-scale, so the viewport rect scales it
    // (the same model the mini toolbar and the presence overlay use) - without it the popover drifts away from the field when zoomed.
    const scale = Math.max(0.01, Number(page?.pageElement?.getAttribute?.('data-canvas-page-zoom-scale') || 1) || 1);
    return {
        left: bounds.left + (Number(rect.x || 0) || 0) * scale,
        top: bounds.top + (Number(rect.y || 0) || 0) * scale,
        width: Math.max(1, (Number(rect.width || 0) || 0) * scale),
        height: Math.max(1, (Number(rect.height || 0) || 0) * scale),
    };
}

export function getSelectionStateJson(handle) {
    const state = getInstance(handle);
    // Phase N2: the selection pull runs after every settled edit (debounced toolbar sync), so it must
    // stay O(selection) — the full snapshot would recompute the O(document) navigation state each time.
    const selection = (typeof state.engine.getSelectionState === 'function'
        ? state.engine.getSelectionState()
        : state.engine.getSnapshot().selection) || {};
    const model = typeof state.engine.getModel === 'function'
        ? state.engine.getModel()
        : state.engine.getSnapshot().model;
    const signingField = findSigningFieldAtSelection(model, selection);
    // The content control under the caret rides the same payload (O(focused block)), replacing the
    // full-document marshal the C# popover sync used to perform.
    const contentControl = findContentControlAtSelection(model, selection);
    return JSON.stringify({
        isCollapsed: selection.isCollapsed !== false,
        signingFieldSelected: signingField != null,
        signingField: signingField || null,
        contentControlSelected: contentControl != null,
        contentControl: contentControl || null,
        // Phase 5 (DE-1.6): where the field is in the VIEWPORT, so the Blazor field popover anchors to it (the same
        // coordinate model the mini toolbar uses). Only computed for a field - the pull stays O(selection).
        fieldAnchor: (signingField != null || contentControl != null) ? selectionViewportRect(state.engine, selection) : null,
        pageIndex: Number(selection.pageIndex || 0) || 0,
        anchorBlockId: selection.anchor?.blockId || '',
        anchorOffset: Number(selection.anchor?.offset || 0) || 0,
        focusBlockId: selection.focus?.blockId || '',
        focusOffset: Number(selection.focus?.offset || 0) || 0,
        selectionRectCount: Number(selection.selectionRectCount || 0) || 0,
        region: selection.region || 'Body',
        headerFooterScope: selection.headerFooterScope || '',
        inTable: selection.table?.inTable === true,
        tableId: selection.table?.tableId || '',
        cellId: selection.table?.cellId || '',
        rowIndex: Number(selection.table?.rowIndex || 0) || 0,
        cellIndex: Number(selection.table?.cellIndex || 0) || 0,
        objectSelected: selection.object != null,
        objectId: selection.object?.objectId || '',
        objectBlockId: selection.object?.blockId || '',
        objectRunId: selection.object?.runId || '',
        objectWrapMode: selection.object?.wrapMode || '',
    });
}

export function getDiagnosticsJson(handle) {
    const state = getInstance(handle);
    const snapshot = state.engine.getSnapshot();
    return JSON.stringify({
        architectureName: snapshot.architecture?.name || 'CanvasDocumentEngine',
        pageSurfaceStrategy: snapshot.architecture?.pageSurfaceStrategy || 'canvas-per-visible-page',
        pageCount: snapshot.render?.pageCount || snapshot.layout?.pages?.length || 0,
        selectionRectCount: snapshot.selection?.selectionRectCount || 0,
        proofingDiagnosticCount: snapshot.proofing?.diagnosticCount || 0,
        proofingSquiggleCount: snapshot.proofingOverlay?.squiggleCount || 0,
    });
}

export function getRuntimeDebugSnapshotJson(handle) {
    const state = getInstance(handle);
    const snapshot = state.engine.getSnapshot();
    const layoutPages = Array.isArray(snapshot.layout?.pages) ? snapshot.layout.pages : [];
    const displayPages = Array.isArray(snapshot.render?.pages) ? snapshot.render.pages : [];
    const displayListCommandCount = displayPages.reduce(
        (total, page) => total + (Array.isArray(page?.displayList) ? page.displayList.length : 0),
        0);
    return JSON.stringify({
        architecture: snapshot.architecture || {
            name: 'CanvasDocumentEngine',
            pageSurfaceStrategy: 'canvas-per-visible-page',
        },
        model: snapshot.model || null,
        modelVersion: snapshot.modelVersion || 0,
        dirty: isDirtyForState(state),
        savedVersion: state.savedVersion,
        layout: {
            pageCount: layoutPages.length,
            pages: layoutPages,
            blocks: Array.isArray(snapshot.layout?.blocks) ? snapshot.layout.blocks : [],
        },
        render: {
            pageCount: snapshot.render?.pageCount || displayPages.length,
            displayListCommandCount,
            selectionLayout: snapshot.render?.selectionLayout || null,
        },
        selection: snapshot.selection || null,
        formatting: snapshot.formatting || null,
        undo: snapshot.undo || null,
        search: snapshot.search || null,
        printPreview: snapshot.printPreview || null,
        proofing: snapshot.proofing || null,
        proofingOverlay: snapshot.proofingOverlay || null,
        comments: snapshot.comments || null,
        revisions: snapshot.revisions || null,
        restrictedEditing: snapshot.restrictedEditing || null,
        collaboration: snapshot.collaboration || null,
        presence: snapshot.presence || null,
    });
}

export function getSearchStateJson(handle) {
    const state = getInstance(handle);
    const search = state.engine.getSnapshot().search || {};
    return JSON.stringify(search);
}

export function getNavigationStateJson(handle) {
    const state = getInstance(handle);
    const formatting = state.engine.getSnapshot().formatting || {};
    return JSON.stringify(formatting.navigation || { outline: [], bookmarks: [] });
}

// B9: page metrics for the side-panel navigator + status bar. Total pages come from the laid-out document;
// the active page is the topmost currently-visible (virtualized) page.
export function getPageMetricsJson(handle) {
    const state = getInstance(handle);
    const snapshot = state.engine.getSnapshot();
    // The TOTAL page count is the render's allRenderPages (= displayList.pages = data-canvas-page-count), NOT
    // the pre-pagination layout.pages.
    const renderPages = Array.isArray(snapshot.render?.displayList?.pages) ? snapshot.render.displayList.pages : [];
    const total = Number(snapshot.render?.pageCount ?? renderPages.length) || renderPages.length;
    const visible = Array.isArray(snapshot.render?.virtualization?.visiblePageIndexes)
        ? snapshot.render.virtualization.visiblePageIndexes.map(Number)
        : [];
    const activePageIndex = visible.length ? Math.max(0, Math.min(Math.max(0, total - 1), visible[0])) : 0;
    const indexOf = (page, ordinal) => Number(page?.index ?? ordinal);
    const pages = total > 0
        ? Array.from({ length: total }, (_, ordinal) => {
            const pageIndex = renderPages.length > ordinal ? (indexOf(renderPages[ordinal], ordinal) || ordinal) : ordinal;
            return {
                pageIndex,
                pageNumber: pageIndex + 1,
                label: '',
                isVirtual: visible.length ? !visible.includes(pageIndex) : false,
            };
        })
        : [];
    return JSON.stringify({
        totalPages: total,
        renderedPages: visible.length || total,
        virtualizedPages: Math.max(0, total - (visible.length || total)),
        activePageIndex,
        pages,
        // Perf plan N11.5: totalPages only covers the laid prefix until the progressive layout completes.
        layoutComplete: state.engine.progressiveLayout?.complete !== false,
    });
}

// Signing bridge (plan S2): derive the signing fields (with their multi-page areas) from the engine's
// current display list. Areas are computed from the layout, never stored — a body field has one area,
// a header/footer field one per page it renders on (all sharing one field uuid).
export function getSigningFieldsJson(handle) {
    const state = getInstance(handle);
    const displayList = state.engine.getSnapshot().render?.displayList;
    return JSON.stringify(extractSigningFields(displayList || {}));
}

// Signing bridge (plan S1): flatten the document into one opaque bitmap per page so the editor's
// output can be used directly as a signing-template page. Reuses the engine's CURRENT display list
// (the exact layout the editor produced), so the image is faithful and every page is exported —
// including pages the editor never mounted under virtualization.
export function exportPageImages(handle, optionsJson) {
    const state = getInstance(handle);
    const options = parseJson(optionsJson, {});
    const displayList = state.engine.getSnapshot().render?.displayList;
    const pages = Array.isArray(displayList?.pages) ? displayList.pages : [];
    const images = pages.map((_, index) => exportPageDescriptor(state, displayList, index, options));
    return JSON.stringify(images);
}

// Print/PDF export (schema v1): translate the engine's CURRENT display list — the exact commands
// the editor painted, laid out with the browser's real font metrics — into the page-indexed print
// snapshot the server PDF renderer consumes. WYSIWYG parity comes from reusing the live layout.
export function getLayoutSnapshotJson(handle) {
    const state = getInstance(handle);
    const snapshot = state.engine.getSnapshot();
    const displayList = snapshot.render?.displayList;
    return JSON.stringify(translateDisplayListToLayoutSnapshot(displayList || {}, {
        // Redline printing: tracked changes style the PDF exactly like the canvas markup view.
        revisions: snapshot.model?.revisions,
        reviewDisplayMode: state.engine.reviewDisplayMode,
        // Redaction: destroy redacted characters in the print snapshot (never just overlay them).
        redactedRunIds: [...collectRedactedRunIds(snapshot.model)],
    }));
}

// Single-page variant so the host can paginate the export and avoid very large interop strings.
export function exportPageImage(handle, pageIndex, optionsJson) {
    const state = getInstance(handle);
    const options = parseJson(optionsJson, {});
    const displayList = state.engine.getSnapshot().render?.displayList;
    return JSON.stringify(exportPageDescriptor(state, displayList, Number(pageIndex) || 0, options));
}

function exportPageDescriptor(state, displayList, pageIndex, options) {
    const doc = state.mountHost?.ownerDocument || globalThis.document;
    const format = String(options.format || 'png').toLowerCase() === 'jpeg' ? 'jpeg' : 'png';
    const mime = format === 'jpeg' ? 'image/jpeg' : 'image/png';
    const { canvas, width, height, scale } = renderDisplayListPageToCanvas(displayList || {}, pageIndex, {
        scale: clampExportScale(options.scale),
        createCanvas: (backingWidth, backingHeight) => {
            const element = doc.createElement('canvas');
            element.width = backingWidth;
            element.height = backingHeight;
            return element;
        },
    });
    const dataUrl = typeof canvas.toDataURL === 'function'
        ? (format === 'jpeg' ? canvas.toDataURL(mime, clampJpegQuality(options.quality)) : canvas.toDataURL(mime))
        : '';
    return { pageIndex, width, height, scale, dataUrl };
}

function clampJpegQuality(quality) {
    const value = Number(quality);
    if (!Number.isFinite(value)) {
        return 0.92;
    }

    return Math.min(1, Math.max(0.1, value));
}

// B9: scroll a page into view from the navigator (works for virtualized/unmounted pages too).
export function scrollToPage(handle, pageIndex) {
    const state = getInstance(handle);
    return JSON.stringify(state.engine.scrollToPage(pageIndex) || {});
}

export function getClipboardDebugSnapshotJson(handle) {
    const state = getInstance(handle);
    const debug = state.engine.getSnapshot().clipboard?.debug || {};
    return JSON.stringify(debug);
}

export function on(handle, eventName) {
    const state = getInstance(handle);
    return JSON.stringify({
        eventName,
        ready: true,
        dirty: isDirty(handle),
        modelVersion: state.engine.getSnapshot().modelVersion,
    });
}

function getInstance(handle) {
    const state = instances.get(handle);
    if (!state) {
        throw new Error(`Canvas document engine instance '${handle}' does not exist.`);
    }

    return state;
}

function resolveElement(element, message) {
    if (!element || typeof element.appendChild !== 'function') {
        throw new Error(message);
    }

    return element;
}

function parseJson(json, fallback) {
    if (!json || typeof json !== 'string') {
        return fallback;
    }

    return JSON.parse(json);
}

function setReadyAttributes(state, ready) {
    const value = ready ? 'true' : 'false';
    state.host.setAttribute('data-canvas-engine-ready', value);
    state.mountHost.setAttribute('data-canvas-engine-ready', value);
    state.host.setAttribute('data-canvas-engine-interop', 'mounted');
    state.host.setAttribute('data-canvas-engine-handle', state.handle || '');
    state.host.setAttribute('data-canvas-engine-dirty', String(isDirtyForState(state)));
}

function notifyChanged(state) {
    // The dirty attribute is published synchronously (cheap, used by tests/host).
    state.host.setAttribute('data-canvas-engine-dirty', String(isDirtyForState(state)));

    // Debounce the JS->.NET change notification. On the single-threaded WASM runtime, invoking .NET per
    // keystroke blocks the thread with interop marshalling + async machinery BETWEEN keystrokes, which
    // throttles how fast the canvas can process the next key. The canvas has already painted this edit;
    // .NET only needs to learn about it once typing settles (it then runs its own debounced toolbar sync +
    // document reconcile). This keeps continuous typing at canvas speed regardless of .NET overhead.
    const view = state.document?.defaultView || globalThis;
    const clear = view.clearTimeout || clearTimeout;
    const set = view.setTimeout || setTimeout;
    if (state.changeNotifyTimer) {
        clear(state.changeNotifyTimer);
    }

    state.changeNotifyTimer = set(() => {
        state.changeNotifyTimer = 0;
        notifyDotNet(state.dotNetRef, 'OnCanvasEngineChanged', {
            isDirty: isDirtyForState(state),
            modelVersion: engineModelVersion(state),
        });
    }, NET_NOTIFY_DEBOUNCE_MS);
}

// Selection changes split into two cadences:
//   * A DELIBERATE selection (a placed range or an object selection -> isVisible, or any non-collapsed
//     selection) is low-frequency and drives the floating mini toolbar + toolbar pressed-state. The user is
//     waiting on it, and pointer features (ctrl+click a link, right-click a misspelling) are positioned
//     against it, so it must reach .NET PROMPTLY. Debouncing it regressed those interactions.
//   * A COLLAPSED caret fires on every keystroke during typing and on every arrow-key move. Notifying .NET
//     per key blocks the single WASM thread BETWEEN keystrokes and batches the glyphs, so it is debounced;
//     the toolbar/mini-toolbar simply catch up once the caret settles. The latest payload wins.
function notifySelectionChanged(state, payload) {
    const view = state.document?.defaultView || globalThis;
    const clear = view.clearTimeout || clearTimeout;
    const set = view.setTimeout || setTimeout;

    if (isDeliberateSelectionNotification(payload)) {
        // Cancel any pending collapsed-caret notification so a stale "selection hidden" cannot land after the
        // deliberate selection we are about to publish, then notify immediately.
        if (state.selectionNotifyTimer) {
            clear(state.selectionNotifyTimer);
            state.selectionNotifyTimer = 0;
        }
        state.pendingSelectionPayload = null;
        notifyDotNet(state.dotNetRef, 'OnCanvasMiniToolbarChanged', payload);
        return;
    }

    state.pendingSelectionPayload = payload;
    if (state.selectionNotifyTimer) {
        clear(state.selectionNotifyTimer);
    }

    state.selectionNotifyTimer = set(() => {
        state.selectionNotifyTimer = 0;
        notifyDotNet(state.dotNetRef, 'OnCanvasMiniToolbarChanged', state.pendingSelectionPayload);
    }, NET_NOTIFY_DEBOUNCE_MS);
}

function notifyDotNet(dotNetRef, methodName, payload) {
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') {
        return;
    }

    dotNetRef.invokeMethodAsync(methodName, JSON.stringify(payload));
}

function buildStateSnapshot(state, result) {
    const snapshot = state.engine.getSnapshot();
    return {
        ready: true,
        isDirty: isDirtyForState(state),
        modelVersion: snapshot.modelVersion,
        architectureName: result?.architecture?.name || snapshot.architecture?.name || 'CanvasDocumentEngine',
        pageSurfaceStrategy: result?.architecture?.pageSurfaceStrategy || snapshot.architecture?.pageSurfaceStrategy || 'canvas-per-visible-page',
        pageCount: snapshot.layout?.pages?.length || 0,
    };
}

async function uploadClipboardImage(dotNetRef, documentId, file) {
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function' || !file) {
        return { success: false, errorMessage: 'Image provider is not available.' };
    }

    const streamReference = globalThis.DotNet?.createJSStreamReference
        ? globalThis.DotNet.createJSStreamReference(file)
        : null;
    if (!streamReference) {
        return { success: false, errorMessage: 'Browser stream reference is not available.' };
    }

    return await dotNetRef.invokeMethodAsync(
        'UploadCanvasClipboardImage',
        {
            documentId: documentId || '',
            fileName: file.name || 'clipboard-image.png',
            contentType: file.type || 'image/png',
            sizeBytes: Number(file.size || 0) || 0,
        },
        streamReference);
}

// Cheap model version read. getSnapshot() assembles the whole document + ~15 subsystem snapshots, so it
// must NOT be used on per-keystroke paths (dirty/version checks) — that froze typing on large documents.
function engineModelVersion(state) {
    const store = state.engine.modelStore;
    if (store && typeof store.getVersion === 'function') {
        return store.getVersion();
    }

    return state.engine.getSnapshot().modelVersion;
}

function isDirtyForState(state) {
    return state.dirty || engineModelVersion(state) !== state.savedVersion;
}
