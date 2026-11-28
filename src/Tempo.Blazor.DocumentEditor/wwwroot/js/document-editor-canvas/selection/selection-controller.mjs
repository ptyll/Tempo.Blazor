import { blockMaxOffset, caretRect, moveCaretByKey } from '../../document-editor/core-engine/caret.mjs';
import { caretStopAt, collectCaretStops, hitTestPoint } from '../../document-editor/core-engine/hit-test.mjs';
import { createCompositionUnderlineElement, createSelectionRectElement, selectionRectsForRange } from '../../document-editor/core-engine/selection-overlay.mjs';
import { wordRangeAt } from '../../document-editor/layout/grapheme.mjs';
import { createCanvasRunText, orderedCanvasBlocks } from '../layout/canvas-text-style.mjs';
import { classifyPointerGesture, normalizePointerPoint, shouldBeginDrag } from './pointer-gestures.mjs';
import { cellRangeFromSelection, findTableCellByBlockId } from '../tables/table-selection.mjs';
import { hitTestTableCell } from '../tables/table-layout.mjs';
import {
    OBJECT_CONNECTOR_END_HANDLE_NAME,
    OBJECT_CONNECTOR_START_HANDLE_NAME,
    OBJECT_ROTATE_HANDLE_NAME,
    cursorForObjectHandle,
    imageObjectAtPoint,
    isObjectConnectorEndpointHandle,
    moveRect,
    objectInteractionHandleRects,
    objectResizeHandleAt,
    rotationFromPointer,
} from '../objects/object-handles.mjs';
import { snapObjectMoveRect, snapObjectResizeRect } from '../objects/image-move-snap.mjs';
import {
    collectMathSlots,
    mathCaretRectForSlot,
    mathSlotAtPoint,
    mathSlotRectForSlot,
} from '../math/math-caret.mjs';

const SELECTION_FILL = 'rgba(37, 99, 235, 0.28)';
const TABLE_RANGE_FILL = 'rgba(37, 99, 235, 0.16)';
const TABLE_RANGE_STROKE = 'rgba(29, 78, 216, 0.62)';
const TABLE_RESIZE_STROKE = 'rgba(29, 78, 216, 0.9)';
const OBJECT_HANDLE_STROKE = '#1d4ed8';
const OBJECT_HANDLE_FILL = '#ffffff';
const OBJECT_SELECTION_STROKE = 'rgba(29, 78, 216, 0.95)';
const CARET_FILL = '#1d4ed8';
const COMPOSITION_UNDERLINE_FILL = 'rgba(37, 99, 235, 0.92)';
const CARET_WIDTH = 2;
const TABLE_RESIZE_HIT_WIDTH = 5;
const CARET_STYLE_ID = 'tm-document-canvas-caret-style';

export function createCanvasSelectionController(options = {}) {
    const doc = options.document || globalThis.document;
    const canvasStack = options.canvasStack;
    const inputBridge = options.inputBridge;
    const openLinkAtPosition = typeof options.openLinkAtPosition === 'function' ? options.openLinkAtPosition : null;
    const executeCommand = typeof options.executeCommand === 'function' ? options.executeCommand : null;
    const ensureHitTestLayout = typeof options.ensureHitTestLayout === 'function' ? options.ensureHitTestLayout : null;
    const onSelectionChanged = typeof options.onSelectionChanged === 'function' ? options.onSelectionChanged : null;
    if (!doc || typeof doc.createElement !== 'function') {
        throw new Error('Canvas selection controller requires a DOM-like document.');
    }

    let layout = null;
    let model = null;
    let selection = null;
    let objectSelection = null;
    // WHERE an object selection comes from, stamped by the deliberate paths ('pointer' = a click on the object that did not drag,
    // 'keyboard' = Tab-cycling) and consumed by the NEXT selection notification. Every other notification (a re-layout, a drag /
    // resize / rotate preview, the host's state re-render) reports 'layout'. The host opens its inspector only for a new object or a
    // deliberate selection, so a closed inspector is not popped back by a re-report of the same object.
    let objectSelectionSource = null;
    let compositionRange = null;
    let pointerState = null;
    let selectionRevision = 0;
    let unsubscribeInput = null;
    let hoverCursorScheduled = false;
    let lastHoverEvent = null;

    const root = canvasStack?.root || null;
    const onMouseDown = event => handlePointerDown(event);
    const onMouseMove = event => handlePointerMove(event);
    const onMouseUp = event => handlePointerUp(event);
    const onKeyDown = event => handleKeyDown(event);

    function mount() {
        ensureCaretStyle(doc);
        root?.addEventListener?.('mousedown', onMouseDown);
        doc.addEventListener?.('mousemove', onMouseMove);
        doc.addEventListener?.('mouseup', onMouseUp);
        inputBridge?.input?.addEventListener?.('keydown', onKeyDown);
        unsubscribeInput = inputBridge?.subscribe?.(() => {}) || null;
        return api;
    }

    function update(nextLayout, nextModel) {
        layout = normalizeSelectionLayout(nextLayout);
        model = nextModel || {};
        if (!selection || !positionExists(layout, selection.focus)) {
            // Prefer the first BODY position: header text sits highest on the page, so a plain
            // (pageIndex, y, x) sort would open every document with the caret inside the header.
            const first = firstTextPosition(layout, model);
            selection = first ? createSelection(first, first) : null;
        }
        if (objectSelection) {
            objectSelection = withTextBoxState(resolveObjectSelection(layout, objectSelection) || objectSelection, objectSelection?.textBox);
        }

        renderOverlay();
        return api;
    }

    function handlePointerDown(event) {
        if (event?.button === 2) {
            return;
        }

        // Caret race fix: a just-reset progressive layout (post-mount replaceModel push) must be
        // completed synchronously before hit-testing, or the click resolves to offset 0. The
        // callback re-enters update() with the finished selection layout.
        ensureHitTestLayout?.();

        if (!layout) {
            return;
        }

        const pageElement = findPageElement(event.target);
        const point = normalizePointerPoint(event, pageElement);
        const objectHandle = point ? objectResizeHandleAt(layout, objectSelection, point.pageIndex, point.x, point.y) : null;
        const objectHit = point ? imageObjectAtPoint(layout, point.pageIndex, point.x, point.y) : null;
        const resizeHandle = point ? tableResizeHandleAt(layout, point.pageIndex, point.x, point.y) : null;
        const mathHit = point ? hitTestMathSlot(layout, point.pageIndex, point.x, point.y) : null;
        const hit = point ? hitTestOnPage(layout, point.pageIndex, point.x, point.y) : null;
        root?.setAttribute?.('data-canvas-pointer-page-index', String(point?.pageIndex ?? ''));
        root?.setAttribute?.('data-canvas-pointer-x', String(point?.x ?? ''));
        root?.setAttribute?.('data-canvas-pointer-y', String(point?.y ?? ''));
        root?.setAttribute?.('data-canvas-pointer-object-id', objectHandle?.objectId || objectHit?.objectId || '');
        root?.setAttribute?.('data-canvas-pointer-hit-block-id', hit?.blockId || mathHit?.blockId || objectHit?.blockId || objectHandle?.blockId || '');
        root?.setAttribute?.('data-canvas-pointer-math-id', mathHit?.mathId || '');
        root?.setAttribute?.('data-canvas-pointer-math-slot-name', mathHit?.slotName || '');
        if (!hit && !mathHit && !resizeHandle && !objectHit && !objectHandle) {
            return;
        }

        if (hit && (event.ctrlKey || event.metaKey) && openLinkAtPosition?.(hit)) {
            event.preventDefault?.();
            return;
        }

        event.preventDefault?.();
        inputBridge?.focus?.();
        if (!objectHandle && objectHit && event.detail >= 2 && isTextEditableObject(objectHit)) {
            const target = withTextBoxState(objectHit, objectHit.textBox);
            const offset = textBoxOffsetAtPoint(target, point.x, point.y);
            objectSelection = {
                ...target,
                textBox: {
                    ...(target.textBox || {}),
                    active: true,
                    offset,
                    selectionAnchorOffset: offset,
                    selectionFocusOffset: offset,
                    selecting: false,
                },
            };
            selection = createSelection(
                { blockId: target.blockId, offset: 0 },
                { blockId: target.blockId, offset: 0 },
                objectSelection);
            executeCommand?.('activateTextBoxEdit', {
                ...objectCommandTarget(target),
                offset,
                source: 'pointerDoubleClick',
            });
            pointerState = null;
            renderOverlay();
            return;
        }

        if (objectHandle || objectHit) {
            const target = withTextBoxState(objectHandle || objectHit, objectSelection?.textBox);
            objectSelection = target;
            selection = createSelection({ blockId: target.blockId, offset: 0 }, { blockId: target.blockId, offset: 0 }, target);
            const endpointHandle = isObjectConnectorEndpointHandle(objectHandle?.handle);
            pointerState = {
                active: true,
                mode: endpointHandle
                    ? 'object-connector-endpoint'
                    : objectHandle?.handle === OBJECT_ROTATE_HANDLE_NAME
                        ? 'object-rotate'
                        : (objectHandle ? 'object-resize' : 'object-move'),
                dragging: objectHandle ? true : false,
                startPoint: point,
                object: target,
                startRect: { ...(target.rect || {}) },
                previewRect: { ...(target.rect || {}) },
                startRotation: Number(target.rotation || 0) || 0,
                previewRotation: Number(target.rotation || 0) || 0,
                handle: objectHandle?.handle || null,
            };
            applyDragCursor(pointerState.mode, pointerState.handle);
            renderOverlay();
            return;
        }

        if (mathHit && !resizeHandle && !objectHit && !objectHandle) {
            event.preventDefault?.();
            inputBridge?.focus?.();
            objectSelection = null;
            const handled = executeCommand?.('activateMathSlot', {
                mathId: mathHit.mathId,
                runId: mathHit.runId,
                blockId: mathHit.blockId,
                slotPath: mathHit.slotPath,
                slotName: mathHit.slotName,
                offset: mathHit.offset,
                source: 'pointer',
            });
            if (!handled) {
                setSelection(createSelection(
                    { blockId: mathHit.blockId, offset: mathHit.end },
                    { blockId: mathHit.blockId, offset: mathHit.end },
                    null,
                    {
                        math: {
                            mathId: mathHit.mathId,
                            runId: mathHit.runId,
                            slotPath: mathHit.slotPath,
                            slotName: mathHit.slotName,
                            offset: mathHit.offset,
                        },
                    }));
            }

            pointerState = null;
            return;
        }

        if (resizeHandle) {
            const resizeSelection = hit ? createSelection(hit, hit) : selection;
            if (resizeSelection) {
                setSelection(resizeSelection);
            }

            pointerState = {
                active: true,
                mode: 'table-column-resize',
                dragging: true,
                startPoint: point,
                resize: resizeHandle,
                width: resizeHandle.width,
            };
            root?.setAttribute?.('data-canvas-table-resize-active', 'true');
            root?.setAttribute?.('data-canvas-table-resize-cell-id', resizeHandle.cellId || '');
            root?.setAttribute?.('data-canvas-table-resize-column-index', String(resizeHandle.columnIndex));
            renderResizePreview(resizeHandle.pageIndex, resizeHandle.previewX, resizeHandle.tableRect);
            return;
        }

        // B6: a header/footer is entered ONLY by a double-click in its band (Word parity). A single click in
        // the band while editing the body is ignored so the body caret is not disturbed; clicking the body
        // while editing a header/footer falls through normally and exits back to the body.
        const caretInHeaderFooter = selection?.focus
            ? editableRegionForBlock(model, selection.focus.blockId).kind === 'headerFooter'
            : false;
        const hitInHeaderFooter = editableRegionForBlock(model, hit.blockId).kind === 'headerFooter';
        if (hitInHeaderFooter && !caretInHeaderFooter && (Number(event.detail) || 0) < 2) {
            return;
        }

        const gesture = classifyPointerGesture(event, { hasAnchor: !!selection?.anchor });
        let nextSelection;
        if (gesture === 'word') {
            nextSelection = selectWordAt(hit);
        } else if (gesture === 'paragraph') {
            nextSelection = selectParagraphAt(hit);
        } else if (gesture === 'extend' && selection?.anchor) {
            nextSelection = createSelection(selection.anchor, hit);
        } else {
            nextSelection = createSelection(hit, hit);
        }

        setSelection(nextSelection);
        pointerState = {
            active: true,
            dragging: false,
            startPoint: point,
            anchor: clonePosition(selection.anchor),
        };
    }

    function handlePointerMove(event) {
        if (!pointerState?.active) {
            // No active drag: give hover affordance over a selected object's handles/body (and table borders).
            updateHoverCursor(event);
            return;
        }

        if (!layout) {
            return;
        }

        const pageElement = findPageElement(event.target) || pageElementForIndex(pointerState.startPoint?.pageIndex);
        const point = normalizePointerPoint(event, pageElement);
        if (pointerState.mode === 'object-move'
            || pointerState.mode === 'object-resize'
            || pointerState.mode === 'object-rotate'
            || pointerState.mode === 'object-connector-endpoint') {
            if (!point) {
                return;
            }

            event.preventDefault?.();
            pointerState.dragging = true;
            if (pointerState.mode === 'object-connector-endpoint') {
                pointerState.previewEndpoint = {
                    x: Number(point.x || 0) || 0,
                    y: Number(point.y || 0) || 0,
                };
                objectSelection = {
                    ...pointerState.object,
                    connector: previewConnectorWithEndpoint(pointerState.object?.connector, pointerState.handle, pointerState.previewEndpoint),
                };
                root?.setAttribute?.('data-canvas-object-dragging', 'true');
                root?.setAttribute?.('data-canvas-connector-endpoint-dragging', 'true');
                root?.setAttribute?.('data-canvas-connector-endpoint-handle', pointerState.handle || '');
                root?.setAttribute?.('data-canvas-connector-endpoint-preview-x', String(Math.round(pointerState.previewEndpoint.x * 100) / 100));
                root?.setAttribute?.('data-canvas-connector-endpoint-preview-y', String(Math.round(pointerState.previewEndpoint.y * 100) / 100));
                renderOverlay();
                return;
            }

            if (pointerState.mode === 'object-rotate') {
                pointerState.previewRotation = rotationFromPointer(
                    pointerState.startRect,
                    pointerState.startPoint,
                    point,
                    pointerState.startRotation,
                    event.shiftKey === true);
                objectSelection = {
                    ...pointerState.object,
                    rect: pointerState.previewRect,
                    width: pointerState.previewRect.width,
                    height: pointerState.previewRect.height,
                    rotation: pointerState.previewRotation,
                };
                root?.setAttribute?.('data-canvas-object-dragging', 'true');
                root?.setAttribute?.('data-canvas-object-rotation-preview', String(pointerState.previewRotation));
                renderOverlay();
                return;
            }

            const dx = point.x - (Number(pointerState.startPoint?.x || 0) || 0);
            const dy = point.y - (Number(pointerState.startPoint?.y || 0) || 0);
            const snapContext = objectSnapContext(pointerState, event);
            const snap = pointerState.mode === 'object-resize'
                ? snapObjectResizeRect(pointerState.startRect, pointerState.handle, dx, dy, event.shiftKey !== true, snapContext)
                : snapObjectMoveRect(moveRect(pointerState.startRect, dx, dy), snapContext);
            pointerState.previewRect = snap.rect;
            pointerState.snap = snap;
            objectSelection = {
                ...pointerState.object,
                rect: pointerState.previewRect,
                width: pointerState.previewRect.width,
                height: pointerState.previewRect.height,
            };
            syncObjectSnapAttributes(snap);
            root?.setAttribute?.('data-canvas-object-dragging', 'true');
            renderOverlay();
            return;
        }

        if (pointerState.mode === 'table-column-resize') {
            if (!point) {
                return;
            }

            event.preventDefault?.();
            const resize = pointerState.resize;
            const width = Math.max(32, Math.min(720, (Number(resize.width || 0) || 0) + point.x - (Number(pointerState.startPoint?.x || 0) || 0)));
            pointerState.width = width;
            const previewX = (Number(resize.left || 0) || 0) + width;
            root?.setAttribute?.('data-canvas-table-resize-width', String(Math.round(width)));
            renderResizePreview(resize.pageIndex, previewX, resize.tableRect);
            return;
        }

        if (!point || !shouldBeginDrag(pointerState.startPoint, point, pointerState.dragging ? 1 : 3)) {
            syncResizeCursor(point);
            return;
        }

        const hit = hitTestOnPage(layout, point.pageIndex, point.x, point.y);
        if (!hit) {
            return;
        }

        event.preventDefault?.();
        pointerState.dragging = true;
        setSelection(createSelection(pointerState.anchor, hit));
    }

    function syncResizeCursor(point) {
        if (!root || !point) {
            return;
        }

        root.style.cursor = tableResizeHandleAt(layout, point.pageIndex, point.x, point.y) ? 'col-resize' : '';
    }

    // Hover-cursor feedback, throttled to one hit-test per animation frame so rapid mousemove does not re-run
    // the object/table hit-tests dozens of times per frame.
    function updateHoverCursor(event) {
        lastHoverEvent = event;
        if (hoverCursorScheduled) {
            return;
        }

        hoverCursorScheduled = true;
        const view = root?.ownerDocument?.defaultView || doc?.defaultView || globalThis;
        const raf = typeof view.requestAnimationFrame === 'function'
            ? view.requestAnimationFrame.bind(view)
            : (callback => (view.setTimeout || setTimeout)(callback, 16));
        raf(() => {
            hoverCursorScheduled = false;
            applyHoverCursor(lastHoverEvent);
        });
    }

    function applyHoverCursor(event) {
        if (!root?.style || !layout || !event) {
            return;
        }

        const pageElement = findPageElement(event.target);
        const point = normalizePointerPoint(event, pageElement);
        if (!point) {
            root.style.cursor = '';
            return;
        }

        // A selected object: resize/rotate/connector cursor over its handles, move over its body.
        if (objectSelection) {
            const handle = objectResizeHandleAt(layout, objectSelection, point.pageIndex, point.x, point.y);
            if (handle?.handle) {
                root.style.cursor = cursorForObjectHandle(handle.handle, Number(objectSelection.rotation || 0) || 0);
                return;
            }

            const objectHit = imageObjectAtPoint(layout, point.pageIndex, point.x, point.y);
            if (objectHit && String(objectHit.objectId || '') === String(objectSelection.objectId || '')) {
                root.style.cursor = 'move';
                return;
            }
        }

        if (tableResizeHandleAt(layout, point.pageIndex, point.x, point.y)) {
            root.style.cursor = 'col-resize';
            return;
        }

        root.style.cursor = '';
    }

    // Cursor shown WHILE an object drag is in progress (resize direction / grabbing for move + rotate).
    function applyDragCursor(mode, handle) {
        if (!root?.style) {
            return;
        }

        if (mode === 'object-resize' && handle) {
            root.style.cursor = cursorForObjectHandle(handle, Number(pointerState?.object?.rotation || 0) || 0);
        } else if (mode === 'object-rotate' || mode === 'object-move') {
            root.style.cursor = 'grabbing';
        } else if (mode === 'object-connector-endpoint') {
            root.style.cursor = 'crosshair';
        }
    }

    function handlePointerUp(event) {
        if (!pointerState?.active) {
            return;
        }

        event.preventDefault?.();
        if (pointerState.mode === 'table-column-resize') {
            const resize = pointerState.resize || {};
            const width = Math.max(32, Math.min(720, Number(pointerState.width || resize.width || 0) || 0));
            clearResizePreview();
            root?.setAttribute?.('data-canvas-table-resize-active', 'false');
            root?.setAttribute?.('data-canvas-table-resize-width', String(Math.round(width)));
            executeCommand?.('resizeTableColumn', {
                tableId: resize.tableId || '',
                cellId: resize.cellId || '',
                columnIndex: resize.columnIndex,
                width,
            });
        }

        if ((pointerState.mode === 'object-move' || pointerState.mode === 'object-resize') && pointerState.dragging) {
            const rect = pointerState.previewRect || pointerState.startRect || {};
            const startRect = pointerState.startRect || rect;
            syncObjectSnapLastAttributes(pointerState.snap || null);
            // Move the object by the VISUAL drag delta (page coords) instead of re-deriving an absolute
            // body-relative offset. The resolved position is frameOrigin + storedOffset for every reference
            // frame (paragraph/page/margin), so nudging the stored offset by the drag delta lands the object
            // exactly under the pointer regardless of frame. The old body-relative offset only worked for
            // body/page-relative objects and pushed paragraph-anchored objects down by (paragraphFlowY − bodyY).
            // dx/dy are sent only when non-zero so a single-axis drag/resize never materialises a spurious 0 on
            // the untouched axis (which would pin an alignment-positioned object to the frame origin).
            const dx = (Number(rect.x || 0) || 0) - (Number(startRect.x || 0) || 0);
            const dy = (Number(rect.y || 0) || 0) - (Number(startRect.y || 0) || 0);
            const payload = {
                objectId: pointerState.object?.objectId || '',
                blockId: pointerState.object?.blockId || '',
                runId: pointerState.object?.runId || '',
                width: Math.max(24, Number(rect.width || 0) || 24),
                height: Math.max(24, Number(rect.height || 0) || 24),
            };
            if (Math.abs(dx) > 0.001) {
                payload.dx = dx;
            }

            if (Math.abs(dy) > 0.001) {
                payload.dy = dy;
            }

            executeCommand?.('updateImageLayout', payload);
        }

        if (pointerState.mode === 'object-connector-endpoint' && pointerState.dragging) {
            const body = pageBodyForIndex(pointerState.object?.pageIndex || 0);
            const endpoint = pointerState.previewEndpoint || pointerState.startPoint || {};
            const connector = pointerState.object?.connector || {};
            executeCommand?.('updateConnectorEndpoint', {
                objectId: pointerState.object?.objectId || '',
                blockId: pointerState.object?.blockId || '',
                runId: pointerState.object?.runId || '',
                endpoint: pointerState.handle === OBJECT_CONNECTOR_START_HANDLE_NAME ? 'start' : 'end',
                pageX: Number(endpoint.x || 0) || 0,
                pageY: Number(endpoint.y || 0) || 0,
                bodyX: Number(body.x || 0) || 0,
                bodyY: Number(body.y || 0) || 0,
                currentStartX: Number(connector.start?.x ?? connector.points?.[0]?.x ?? pointerState.startRect?.x ?? 0) || 0,
                currentStartY: Number(connector.start?.y ?? connector.points?.[0]?.y ?? pointerState.startRect?.y ?? 0) || 0,
                currentEndX: Number(connector.end?.x ?? connector.points?.at?.(-1)?.x ?? ((Number(pointerState.startRect?.x || 0) || 0) + (Number(pointerState.startRect?.width || 0) || 0))) || 0,
                currentEndY: Number(connector.end?.y ?? connector.points?.at?.(-1)?.y ?? ((Number(pointerState.startRect?.y || 0) || 0) + (Number(pointerState.startRect?.height || 0) || 0) / 2)) || 0,
            });
        }

        if (pointerState.mode === 'object-rotate' && pointerState.dragging) {
            executeCommand?.('updateImageLayout', {
                objectId: pointerState.object?.objectId || '',
                blockId: pointerState.object?.blockId || '',
                runId: pointerState.object?.runId || '',
                rotation: Number(pointerState.previewRotation ?? pointerState.startRotation ?? 0) || 0,
            });
        }

        // A release on an object that never moved is a click ON the object: the reader's decision to look at it.
        const deliberateObjectClick = pointerState.mode === 'object-move' && pointerState.dragging !== true && !!objectSelection;
        pointerState = null;
        root?.setAttribute?.('data-canvas-object-dragging', 'false');
        root?.setAttribute?.('data-canvas-connector-endpoint-dragging', 'false');
        root?.removeAttribute?.('data-canvas-object-rotation-preview');
        root?.removeAttribute?.('data-canvas-connector-endpoint-handle');
        root?.removeAttribute?.('data-canvas-connector-endpoint-preview-x');
        root?.removeAttribute?.('data-canvas-connector-endpoint-preview-y');
        syncObjectSnapAttributes(null);
        if (root?.style) {
            root.style.cursor = '';
        }

        if (deliberateObjectClick) {
            objectSelectionSource = 'pointer';
            renderOverlay();
        }
    }

    // B6: while a header/footer is being edited, dim the page body and frame the active slot with a dashed
    // border + label (Word parity) so it is clear which region the caret is editing.
    function renderHeaderFooterEditOverlay() {
        if (!selection?.focus) {
            return;
        }

        const region = editableRegionForBlock(model, selection.focus.blockId);
        if (region.kind !== 'headerFooter') {
            return;
        }

        const caret = caretRectForPosition(layout, selection.focus);
        if (!caret?.rect) {
            return;
        }

        const pageIndex = Number(caret.pageIndex || 0) || 0;
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const scale = pageScale(pageIndex);
        const page = canvasStack.pages.get(String(pageIndex));
        const body = page?.layout?.body || null;
        const pageWidth = Number(page?.layout?.width || 0) || (body ? body.x * 2 + body.width : caret.rect.x + 200);
        const bandX = body ? body.x : Math.max(0, caret.rect.x - 8);
        const bandWidth = body ? body.width : Math.max(120, pageWidth - 2 * bandX);

        // Dim the body content (the area the caret is NOT editing).
        if (body) {
            const dim = doc.createElement('div');
            dim.setAttribute('data-canvas-hf-dim', 'true');
            dim.setAttribute('aria-hidden', 'true');
            dim.style.position = 'absolute';
            assignScaledRectStyle(dim, body, scale);
            dim.style.background = 'rgba(100, 116, 139, 0.16)';
            dim.style.pointerEvents = 'none';
            overlay.appendChild(dim);
        }

        // Dashed frame around the active header/footer slot line.
        const band = {
            x: bandX,
            y: Math.max(0, (Number(caret.rect.y) || 0) - 4),
            width: bandWidth,
            height: Math.max(18, Number(caret.rect.height) || 16) + 8,
        };
        const frame = doc.createElement('div');
        frame.setAttribute('data-canvas-hf-frame', region.region);
        frame.setAttribute('aria-hidden', 'true');
        frame.style.position = 'absolute';
        assignScaledRectStyle(frame, band, scale);
        frame.style.border = '1px dashed rgba(37, 99, 235, 0.85)';
        frame.style.borderRadius = '2px';
        frame.style.pointerEvents = 'none';
        overlay.appendChild(frame);

        // Slot label badge above the frame.
        const label = doc.createElement('div');
        label.setAttribute('data-testid', 'document-canvas-hf-label');
        label.setAttribute('data-canvas-hf-region', region.region);
        label.setAttribute('aria-hidden', 'true');
        label.style.position = 'absolute';
        label.style.left = `${band.x * scale}px`;
        label.style.top = `${Math.max(0, band.y - 15) * scale}px`;
        label.style.font = '600 10px Aptos, Arial, sans-serif';
        label.style.letterSpacing = '0.04em';
        label.style.textTransform = 'uppercase';
        label.style.color = '#2563eb';
        label.style.background = 'rgba(255, 255, 255, 0.92)';
        label.style.padding = '1px 5px';
        label.style.borderRadius = '3px';
        label.style.pointerEvents = 'none';
        label.textContent = region.region === 'Footer' ? 'Footer' : 'Header';
        overlay.appendChild(label);
    }

    // B6: place the caret at the start of the first body block (exit header/footer editing).
    function exitHeaderFooterToBody() {
        const block = (model?.body?.blocks || [])[0];
        if (!block?.id) {
            return false;
        }

        const pos = { blockId: String(block.id), offset: 0 };
        setSelection(createSelection(pos, pos));
        inputBridge?.focus?.();
        return true;
    }

    // B6: place the caret in the first header/footer block of the given type ('Header' | 'Footer').
    function enterHeaderFooter(type) {
        const target = normalizeHeaderFooterType(type);
        const headerFooter = (Array.isArray(model?.headersFooters) ? model.headersFooters : [])
            .find(item => normalizeHeaderFooterType(item?.type ?? item?.Type) === target);
        const block = (headerFooter?.blocks || headerFooter?.Blocks || [])[0];
        if (!block?.id && !block?.Id) {
            return false;
        }

        const pos = { blockId: String(block.id || block.Id), offset: 0 };
        setSelection(createSelection(pos, pos));
        inputBridge?.focus?.();
        return true;
    }

    function handleKeyDown(event) {
        if (!layout || !selection) {
            return;
        }

        if (objectSelection && handleObjectKeyDown(event)) {
            return;
        }

        if (selection?.math && handleMathKeyDown(event)) {
            return;
        }

        // B6: Escape exits header/footer editing back to the body (Word parity).
        if (event.key === 'Escape'
            && selection?.focus
            && editableRegionForBlock(model, selection.focus.blockId).kind === 'headerFooter'
            && exitHeaderFooterToBody()) {
            event.preventDefault?.();
            return;
        }

        if (!isNavigationKey(event.key)) {
            return;
        }

        event.preventDefault?.();
        const focus = selection.focus;
        const next = moveByKeyboard(focus, event);
        const anchor = event.shiftKey ? selection.anchor : next;
        setSelection(createSelection(anchor, next));
    }

    function handleObjectKeyDown(event) {
        if (objectSelection?.textBox?.active === true && handleTextBoxKeyDown(event)) {
            return true;
        }

        if (event.key === 'Escape') {
            event.preventDefault?.();
            objectSelection = null;
            selection = createSelection(selection.focus, selection.focus);
            renderOverlay();
            return true;
        }

        if (event.key === 'Delete' || event.key === 'Backspace') {
            event.preventDefault?.();
            executeCommand?.('deleteObject', objectCommandTarget(objectSelection));
            return true;
        }

        if (event.key === 'Tab') {
            if (!cycleObjectSelection(event.shiftKey === true)) {
                return false;
            }

            event.preventDefault?.();
            return true;
        }

        if (!isObjectArrowKey(event.key)) {
            return false;
        }

        event.preventDefault?.();
        const step = event.shiftKey === true ? 10 : 1;
        const delta = objectArrowDelta(event.key, step);
        if (event.altKey === true) {
            const width = Math.max(24, Number(objectSelection.width || objectSelection.rect?.width || 24) + delta.x);
            const height = Math.max(24, Number(objectSelection.height || objectSelection.rect?.height || 24) + delta.y);
            executeCommand?.('updateImageLayout', {
                ...objectCommandTarget(objectSelection),
                width,
                height,
            });
            return true;
        }

        executeCommand?.('updateImageLayout', {
            ...objectCommandTarget(objectSelection),
            dx: delta.x,
            dy: delta.y,
        });
        return true;
    }

    function handleTextBoxKeyDown(event) {
        if (event.key === 'Escape') {
            consumeKeyboardEvent(event);
            executeCommand?.('exitTextBoxEdit', {
                ...objectCommandTarget(objectSelection),
                source: 'keyboardEscape',
            });
            objectSelection = withTextBoxState(objectSelection, {
                ...(objectSelection.textBox || {}),
                active: false,
                selecting: false,
            });
            selection = createSelection(selection.anchor, selection.focus, objectSelection);
            renderOverlay();
            return true;
        }

        if ((event.ctrlKey === true || event.metaKey === true) && String(event.key || '').toLowerCase() === 'a') {
            consumeKeyboardEvent(event);
            const textLength = textBoxTextLengthForObject(objectSelection);
            setTextBoxOffset(textLength, 0, textLength);
            return true;
        }

        if (event.key === 'Home' || event.key === 'End') {
            consumeKeyboardEvent(event);
            const line = textBoxLineForOffset(objectSelection, objectSelection.textBox?.offset || 0);
            const offset = event.ctrlKey === true || event.metaKey === true
                ? (event.key === 'Home' ? 0 : textBoxTextLengthForObject(objectSelection))
                : (event.key === 'Home' ? Number(line?.start || 0) || 0 : Number(line?.end ?? textBoxTextLengthForObject(objectSelection)) || 0);
            setTextBoxOffset(offset, event.shiftKey === true ? null : offset);
            return true;
        }

        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
            consumeKeyboardEvent(event);
            const delta = event.key === 'ArrowLeft' ? -1 : 1;
            const offset = clampTextBoxOffset((Number(objectSelection.textBox?.offset || 0) || 0) + delta, objectSelection);
            setTextBoxOffset(offset, event.shiftKey === true ? null : offset);
            return true;
        }

        if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
            consumeKeyboardEvent(event);
            const offset = textBoxVerticalNavigationOffset(objectSelection, event.key === 'ArrowUp' ? -1 : 1);
            setTextBoxOffset(offset, event.shiftKey === true ? null : offset);
            return true;
        }

        return false;
    }

    function handleMathKeyDown(event) {
        if (!selection?.math) {
            return false;
        }

        if (event.key === 'Escape') {
            consumeKeyboardEvent(event);
            setSelection(createSelection(selection.focus, selection.focus));
            return true;
        }

        if (event.key === 'Tab') {
            consumeKeyboardEvent(event);
            executeCommand?.('moveMathSlot', {
                direction: event.shiftKey === true ? 'previous' : 'next',
                source: 'keyboardTab',
            });
            return true;
        }

        if (event.key === 'Home' || event.key === 'End') {
            consumeKeyboardEvent(event);
            const slot = activeMathSlot(selection.math);
            const offset = event.key === 'Home' ? 0 : Math.max(0, Number(slot?.textLength || 0) || 0);
            executeCommand?.('activateMathSlot', mathCommandPayload(selection.math, { offset, source: 'keyboardNavigation' }));
            return true;
        }

        if (event.key === 'ArrowRight' || event.key === 'ArrowDown' || event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
            consumeKeyboardEvent(event);
            if (event.shiftKey === true) {
                adjustMathSlotSelection(event.key);
                return true;
            }

            executeCommand?.('moveMathSlot', {
                direction: event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? 'previous' : 'next',
                source: 'keyboardArrow',
            });
            return true;
        }

        return false;
    }

    function cycleObjectSelection(reverse) {
        const objects = canvasObjectBlocks();
        if (objects.length === 0) {
            objectSelection = null;
            renderOverlay();
            return false;
        }

        const currentIndex = objects.findIndex(block =>
            String(block?.objectId || block?.object?.objectId || '') === String(objectSelection?.objectId || '')
            || String(block?.runId || '') === String(objectSelection?.runId || '')
            || String(block?.blockId || '') === String(objectSelection?.blockId || ''));
        const nextIndex = currentIndex < 0
            ? (reverse ? objects.length - 1 : 0)
            : (reverse ? (currentIndex - 1 + objects.length) % objects.length : (currentIndex + 1) % objects.length);
        const nextObject = objectSelectionFromBlock(objects[nextIndex]);
        objectSelection = nextObject;
        selection = createSelection(
            { blockId: nextObject.blockId, offset: 0 },
            { blockId: nextObject.blockId, offset: 0 },
            nextObject);
        objectSelectionSource = 'keyboard';
        renderOverlay();
        return true;
    }

    function canvasObjectBlocks() {
        return (layout?.blocks || [])
            .filter(block => block?.type === 'image' && block?.rect)
            .sort((left, right) =>
                (Number(left.pageIndex || 0) - Number(right.pageIndex || 0))
                || ((Number(left.rect?.y || 0) || 0) - (Number(right.rect?.y || 0) || 0))
                || ((Number(left.rect?.x || 0) || 0) - (Number(right.rect?.x || 0) || 0))
                || ((Number(left.sequence || 0) || 0) - (Number(right.sequence || 0) || 0)));
    }

    function objectSelectionFromBlock(block) {
        const rect = block?.rect || {};
        return withTextBoxState({
            objectId: String(block?.objectId || block?.object?.objectId || ''),
            blockId: String(block?.blockId || ''),
            runId: String(block?.runId || ''),
            role: String(block?.role || block?.object?.role || 'imageBlock'),
            pageIndex: Number(block?.pageIndex || 0) || 0,
            rect: { ...rect },
            width: Math.max(1, Number(rect.width || 0) || 1),
            height: Math.max(1, Number(rect.height || 0) || 1),
            rotation: Number(block?.object?.rotation ?? block?.rotation ?? 0) || 0,
            flipHorizontal: block?.object?.flipHorizontal === true || block?.flipHorizontal === true,
            flipVertical: block?.object?.flipVertical === true || block?.flipVertical === true,
            wrapMode: block?.object?.wrapMode || 'Inline',
            altText: block?.object?.altText || '',
            caption: block?.object?.caption || '',
            kind: String(block?.object?.kind || block?.kind || ''),
            zIndex: Number(block?.object?.zIndex ?? block?.zIndex ?? 0) || 0,
            connector: cloneConnector(block?.connector || block?.object?.connector || null),
        }, block?.object?.textBox || null);
    }

    function objectCommandTarget(object) {
        return {
            objectId: String(object?.objectId || ''),
            blockId: String(object?.blockId || ''),
            runId: String(object?.runId || ''),
        };
    }

    function objectArrowDelta(key, step) {
        if (key === 'ArrowLeft') return { x: -step, y: 0 };
        if (key === 'ArrowRight') return { x: step, y: 0 };
        if (key === 'ArrowUp') return { x: 0, y: -step };
        if (key === 'ArrowDown') return { x: 0, y: step };
        return { x: 0, y: 0 };
    }

    function adjustMathSlotSelection(key) {
        const math = selection?.math;
        const slot = activeMathSlot(math);
        const textLength = Math.max(0, Number(slot?.textLength || 0) || 0);
        const currentOffset = Math.max(0, Math.min(textLength, Number(math?.offset || 0) || 0));
        const anchorOffset = Number.isFinite(Number(math?.selectionAnchorOffset))
            ? Math.max(0, Math.min(textLength, Number(math.selectionAnchorOffset)))
            : currentOffset;
        const delta = key === 'ArrowLeft' || key === 'ArrowUp' ? -1 : 1;
        const focusOffset = Math.max(0, Math.min(textLength, currentOffset + delta));
        setSelection({
            ...selection,
            math: {
                ...math,
                offset: focusOffset,
                selectionAnchorOffset: anchorOffset,
                selectionFocusOffset: focusOffset,
                selecting: anchorOffset !== focusOffset,
            },
        });
    }

    function activeMathSlot(math) {
        const equation = mathEquationForSelection(math);
        if (!equation) {
            return null;
        }

        const path = normalizeMathPath(math?.slotPath || math?.SlotPath || []);
        return collectMathSlots(equation.mathLayout, { includeRoot: true })
            .find(slot => sameMathPath(slot.path, path)) || null;
    }

    function mathCommandPayload(math, extra = {}) {
        return {
            mathId: math?.mathId || math?.MathId || '',
            runId: math?.runId || math?.RunId || '',
            slotPath: normalizeMathPath(math?.slotPath || math?.SlotPath || []),
            slotName: math?.slotName || math?.SlotName || '',
            ...extra,
        };
    }

    function consumeKeyboardEvent(event) {
        event.preventDefault?.();
        event.stopImmediatePropagation?.();
        event.stopPropagation?.();
    }

    function moveByKeyboard(position, event) {
        if ((event.ctrlKey || event.altKey || event.metaKey) && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
            return moveWordPosition(model, position, event.key);
        }

        return moveCaretByKey(layout, position, event.key, {
            text: blockText(model, position.blockId),
            pageLines: Math.max(8, linesPerPage(layout)),
        });
    }

    function setSelection(nextSelection, options = {}) {
        selection = normalizeSelection(nextSelection);
        objectSelection = nextSelection?.object || null;
        if (options.render !== false) {
            renderOverlay();
        }
    }

    function renderOverlay() {
        // One-shot: the stamp belongs to THIS render, whether or not it ends in a notification.
        const source = objectSelectionSource || 'layout';
        objectSelectionSource = null;
        if (!canvasStack?.pages) {
            return;
        }

        clearOverlayCanvases();
        clearDomOverlay();
        if (!layout || !selection) {
            return;
        }

        const mathVisual = selection?.math ? mathSelectionVisual(selection.math) : null;
        const textBoxVisual = objectSelection?.textBox?.active === true ? textBoxSelectionVisual(objectSelection) : null;
        const rects = mathVisual ? [] : selectionRectsForRange(layout, selection.anchor, selection.focus);
        for (const item of rects) {
            paintSelectionRect(item.pageIndex, item.rect);
            appendSelectionRect(item.pageIndex, item.rect);
        }

        const cellRects = tableCellRectsForSelectionRange(layout, model, selection);
        for (const item of cellRects) {
            paintTableRangeRect(item.pageIndex, item.rect);
            appendTableRangeRect(item.pageIndex, item.rect, item.cell);
        }

        if (isCollapsed(selection) && !mathVisual) {
            const rect = caretRectForPosition(layout, selection.focus);
            if (rect && !objectSelection) {
                paintCaret(rect.pageIndex, rect.rect);
                appendCaret(rect.pageIndex, rect.rect);
            }
        }

        renderHeaderFooterEditOverlay();

        if (mathVisual) {
            for (const item of mathVisual.selectionRects) {
                paintSelectionRect(item.pageIndex, item.rect);
                appendMathSelectionRect(item.pageIndex, item.rect, mathVisual);
            }

            paintCaret(mathVisual.pageIndex, mathVisual.caretRect);
            appendMathCaret(mathVisual.pageIndex, mathVisual.caretRect, mathVisual);
        }

        if (objectSelection?.rect) {
            paintObjectSelection(objectSelection.pageIndex || 0, objectSelection, { handles: !textBoxVisual });
            appendObjectSelection(objectSelection.pageIndex || 0, objectSelection, { handles: !textBoxVisual });
            appendObjectSnapGuides(objectSelection.pageIndex || 0, pointerState?.snap || null);
        }

        if (textBoxVisual) {
            for (const item of textBoxVisual.selectionRects) {
                paintSelectionRect(item.pageIndex, item.rect);
                appendTextBoxSelectionRect(item.pageIndex, item.rect, textBoxVisual);
            }

            paintCaret(textBoxVisual.pageIndex, textBoxVisual.caretRect);
            appendTextBoxCaret(textBoxVisual.pageIndex, textBoxVisual.caretRect, textBoxVisual);
        }

        if (compositionRange && !isCollapsed(compositionRange)) {
            const compositionRects = selectionRectsForRange(layout, compositionRange.anchor, compositionRange.focus);
            for (const item of compositionRects) {
                paintCompositionUnderline(item.pageIndex, item.rect);
                appendCompositionUnderline(item.pageIndex, item.rect);
            }
        }

        selectionRevision += 1;
        root?.setAttribute?.('data-canvas-selection-revision', String(selectionRevision));
        root?.setAttribute?.('data-canvas-selection-collapsed', String(isCollapsed(selection)));
        root?.setAttribute?.('data-canvas-selection-anchor-block-id', selection.anchor.blockId);
        root?.setAttribute?.('data-canvas-selection-anchor-offset', String(selection.anchor.offset));
        root?.setAttribute?.('data-canvas-selection-focus-block-id', selection.focus.blockId);
        root?.setAttribute?.('data-canvas-selection-focus-offset', String(selection.focus.offset));
        root?.setAttribute?.('data-canvas-object-selected', String(!!objectSelection));
        root?.setAttribute?.('data-canvas-math-slot-active', String(!!mathVisual));
        root?.setAttribute?.('data-canvas-math-selection-active', String(!!mathVisual?.selectionRects?.length));
        root?.setAttribute?.('data-canvas-textbox-editing', String(!!textBoxVisual));
        root?.setAttribute?.('data-canvas-textbox-selection-active', String(!!textBoxVisual?.selectionRects?.length));
        if (textBoxVisual) {
            root?.setAttribute?.('data-canvas-textbox-object-id', textBoxVisual.objectId || '');
            root?.setAttribute?.('data-canvas-textbox-offset', String(textBoxVisual.offset));
            root?.setAttribute?.('data-canvas-textbox-text-length', String(textBoxVisual.textLength));
            root?.setAttribute?.('data-canvas-textbox-line-count', String(textBoxVisual.lines.length));
        } else {
            root?.removeAttribute?.('data-canvas-textbox-object-id');
            root?.removeAttribute?.('data-canvas-textbox-offset');
            root?.removeAttribute?.('data-canvas-textbox-text-length');
            root?.removeAttribute?.('data-canvas-textbox-line-count');
        }
        if (mathVisual) {
            root?.setAttribute?.('data-canvas-math-id', mathVisual.mathId || '');
            root?.setAttribute?.('data-canvas-math-run-id', mathVisual.runId || '');
            root?.setAttribute?.('data-canvas-math-slot-name', mathVisual.slotName || '');
            root?.setAttribute?.('data-canvas-math-slot-path', JSON.stringify(mathVisual.slotPath || []));
            root?.setAttribute?.('data-canvas-math-slot-offset', String(mathVisual.offset));
            root?.setAttribute?.('data-canvas-math-slot-text-length', String(mathVisual.textLength));
        } else {
            root?.removeAttribute?.('data-canvas-math-id');
            root?.removeAttribute?.('data-canvas-math-run-id');
            root?.removeAttribute?.('data-canvas-math-slot-name');
            root?.removeAttribute?.('data-canvas-math-slot-path');
            root?.removeAttribute?.('data-canvas-math-slot-offset');
            root?.removeAttribute?.('data-canvas-math-slot-text-length');
        }
        if (objectSelection) {
            root?.setAttribute?.('data-canvas-object-id', objectSelection.objectId || '');
            root?.setAttribute?.('data-canvas-object-block-id', objectSelection.blockId || '');
            root?.setAttribute?.('data-canvas-object-run-id', objectSelection.runId || '');
            root?.setAttribute?.('data-canvas-object-wrap-mode', objectSelection.wrapMode || '');
            root?.setAttribute?.('data-canvas-object-rotation', String(Number(objectSelection.rotation || 0) || 0));
            const handles = objectInteractionHandleRects(objectSelection);
            // handle-count is the count of directional RESIZE handles (8) — the rotate + connector handles are
            // reported separately — so it stays 8 for an image/shape regardless of the rotate handle.
            const resizeHandleCount = handles.filter(item =>
                item.name !== OBJECT_ROTATE_HANDLE_NAME && !isObjectConnectorEndpointHandle(item.name)).length;
            root?.setAttribute?.('data-canvas-object-handle-count', String(resizeHandleCount));
            root?.setAttribute?.('data-canvas-object-connector-handle-count', String(handles.filter(item => isObjectConnectorEndpointHandle(item.name)).length));
            root?.setAttribute?.('data-canvas-object-alt-warning', String(!objectSelection.altText));
        } else {
            root?.removeAttribute?.('data-canvas-object-id');
            root?.removeAttribute?.('data-canvas-object-block-id');
            root?.removeAttribute?.('data-canvas-object-run-id');
            root?.removeAttribute?.('data-canvas-object-wrap-mode');
            root?.removeAttribute?.('data-canvas-object-rotation');
            root?.removeAttribute?.('data-canvas-object-handle-count');
            root?.removeAttribute?.('data-canvas-object-connector-handle-count');
            root?.removeAttribute?.('data-canvas-object-alt-warning');
        }
        const tableCell = findTableCellByBlockId(model, selection.focus.blockId);
        const tableRange = cellRects.length > 0 ? cellRangeFromSelection(model, selection) : null;
        root?.setAttribute?.('data-canvas-selection-in-table', String(!!tableCell));
        root?.setAttribute?.('data-canvas-selection-table-cell-range-count', String(cellRects.length));
        if (tableCell) {
            root?.setAttribute?.('data-canvas-selection-table-id', tableCell.tableBlock.id || '');
            root?.setAttribute?.('data-canvas-selection-cell-id', tableCell.cell.id || '');
            root?.setAttribute?.('data-canvas-selection-row-index', String(tableCell.rowIndex));
            root?.setAttribute?.('data-canvas-selection-cell-index', String(tableCell.cellIndex));
            if (tableRange) {
                root?.setAttribute?.('data-canvas-selection-table-range', `${tableRange.startRow}:${tableRange.startCell}-${tableRange.endRow}:${tableRange.endCell}`);
            } else {
                root?.removeAttribute?.('data-canvas-selection-table-range');
            }
        } else {
            root?.removeAttribute?.('data-canvas-selection-table-id');
            root?.removeAttribute?.('data-canvas-selection-cell-id');
            root?.removeAttribute?.('data-canvas-selection-row-index');
            root?.removeAttribute?.('data-canvas-selection-cell-index');
            root?.removeAttribute?.('data-canvas-selection-table-range');
        }
        const editRegion = editableRegionForBlock(model, selection.focus.blockId);
        root?.setAttribute?.('data-canvas-header-footer-editing', String(editRegion.kind === 'headerFooter'));
        root?.setAttribute?.('data-canvas-note-editing', String(editRegion.kind === 'note'));
        if (editRegion.kind === 'headerFooter') {
            root?.setAttribute?.('data-canvas-header-footer-edit-region', editRegion.region || '');
            root?.setAttribute?.('data-canvas-header-footer-edit-scope', editRegion.scope || '');
        } else {
            root?.removeAttribute?.('data-canvas-header-footer-edit-region');
            root?.removeAttribute?.('data-canvas-header-footer-edit-scope');
        }
        root?.setAttribute?.('data-canvas-composition-active', String(!!compositionRange && !isCollapsed(compositionRange)));
        onSelectionChanged?.({ ...api.getState(), objectSelectionSource: source });
    }

    function clearOverlayCanvases() {
        for (const page of canvasStack.pages.values()) {
            const canvas = page.layers.get('selection-caret');
            const context = canvas?.getContext?.('2d');
            if (context) {
                context.clearRect(0, 0, page.layout.width, page.layout.height);
            }
        }
    }

    function appendObjectSnapGuides(pageIndex, snap) {
        if (!snap?.snapped) {
            return;
        }

        const overlay = ensureDomOverlay(pageIndex);
        const page = canvasStack.pages.get(String(pageIndex));
        if (!overlay || !page) {
            return;
        }

        const body = pageBodyForIndex(pageIndex);
        const scale = pageScale(pageIndex);
        if (snap.x) {
            const guide = doc.createElement('div');
            guide.className = 'tm-document-canvas-object-snap-guide';
            guide.setAttribute('data-testid', 'document-canvas-object-snap-guide-x');
            guide.setAttribute('data-canvas-object-snap-guide', 'x');
            guide.setAttribute('data-snap-guide-type', snap.x.guideType || '');
            guide.setAttribute('aria-hidden', 'true');
            guide.style.position = 'absolute';
            guide.style.left = `${Number(snap.x.guide || 0) * scale}px`;
            guide.style.top = `${(Number(body?.y || 0) || 0) * scale}px`;
            guide.style.width = '0';
            guide.style.height = `${Math.max(1, Number(body?.height || page.layout?.height || 1) || 1) * scale}px`;
            guide.style.borderLeft = `1px dashed ${OBJECT_SELECTION_STROKE}`;
            guide.style.pointerEvents = 'none';
            guide.style.opacity = '0.78';
            overlay.appendChild(guide);
        }

        if (snap.y) {
            const guide = doc.createElement('div');
            guide.className = 'tm-document-canvas-object-snap-guide';
            guide.setAttribute('data-testid', 'document-canvas-object-snap-guide-y');
            guide.setAttribute('data-canvas-object-snap-guide', 'y');
            guide.setAttribute('data-snap-guide-type', snap.y.guideType || '');
            guide.setAttribute('aria-hidden', 'true');
            guide.style.position = 'absolute';
            guide.style.left = `${(Number(body?.x || 0) || 0) * scale}px`;
            guide.style.top = `${Number(snap.y.guide || 0) * scale}px`;
            guide.style.width = `${Math.max(1, Number(body?.width || page.layout?.width || 1) || 1) * scale}px`;
            guide.style.height = '0';
            guide.style.borderTop = `1px dashed ${OBJECT_SELECTION_STROKE}`;
            guide.style.pointerEvents = 'none';
            guide.style.opacity = '0.78';
            overlay.appendChild(guide);
        }
    }

    function clearDomOverlay() {
        for (const page of canvasStack.pages.values()) {
            let overlay = findDomOverlay(page.pageElement);
            if (overlay) {
                overlay.replaceChildren?.();
                if (!overlay.replaceChildren) {
                    while (overlay.children?.length) {
                        overlay.removeChild(overlay.children[0]);
                    }
                }
            }
        }
    }

    function paintSelectionRect(pageIndex, rect) {
        const context = overlayContext(pageIndex);
        if (!context) {
            return;
        }

        context.save?.();
        context.fillStyle = SELECTION_FILL;
        context.fillRect(rect.x, rect.y, Math.max(1, rect.width), Math.max(1, rect.height));
        context.restore?.();
    }

    function paintCaret(pageIndex, rect) {
        const context = overlayContext(pageIndex);
        if (!context) {
            return;
        }

        context.save?.();
        context.fillStyle = CARET_FILL;
        context.fillRect(rect.x, rect.y, CARET_WIDTH, Math.max(1, rect.height));
        context.restore?.();
    }

    function paintCompositionUnderline(pageIndex, rect) {
        const context = overlayContext(pageIndex);
        if (!context) {
            return;
        }

        const y = (Number(rect.y) || 0) + Math.max(1, Number(rect.height) || 16) - 2;
        context.save?.();
        context.fillStyle = COMPOSITION_UNDERLINE_FILL;
        context.fillRect(Number(rect.x) || 0, y, Math.max(1, Number(rect.width) || 1), 2);
        context.restore?.();
    }

    function paintTableRangeRect(pageIndex, rect) {
        const context = overlayContext(pageIndex);
        if (!context) {
            return;
        }

        context.save?.();
        context.fillStyle = TABLE_RANGE_FILL;
        context.strokeStyle = TABLE_RANGE_STROKE;
        context.lineWidth = 1;
        context.fillRect(rect.x, rect.y, Math.max(1, rect.width), Math.max(1, rect.height));
        context.strokeRect(rect.x + 0.5, rect.y + 0.5, Math.max(1, rect.width) - 1, Math.max(1, rect.height) - 1);
        context.restore?.();
    }

    function paintObjectSelection(pageIndex, object, options = {}) {
        const context = overlayContext(pageIndex);
        if (!context) {
            return;
        }

        const rect = object?.rect || {};
        const rotation = Number(object?.rotation || 0) || 0;
        context.save?.();
        rotateOverlayContext(context, rect, rotation);
        context.strokeStyle = OBJECT_SELECTION_STROKE;
        context.lineWidth = 1.5;
        context.strokeRect(rect.x, rect.y, rect.width, rect.height);
        if (options.handles !== false) {
            for (const handle of objectInteractionHandleRects(object)) {
                context.fillStyle = OBJECT_HANDLE_FILL;
                context.strokeStyle = OBJECT_HANDLE_STROKE;
                context.lineWidth = 1;
                context.fillRect(handle.rect.x, handle.rect.y, handle.rect.width, handle.rect.height);
                context.strokeRect(handle.rect.x, handle.rect.y, handle.rect.width, handle.rect.height);
            }
        }

        context.restore?.();
    }

    function appendCaret(pageIndex, rect) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const caret = doc.createElement('div');
        caret.className = 'tm-document-canvas-caret';
        caret.setAttribute('data-testid', 'document-canvas-caret');
        caret.setAttribute('data-canvas-caret', 'true');
        caret.setAttribute('aria-hidden', 'true');
        caret.style.position = 'absolute';
        assignScaledRectStyle(caret, rect, pageScale(pageIndex), CARET_WIDTH);
        caret.style.background = CARET_FILL;
        caret.style.pointerEvents = 'none';
        caret.style.animation = 'tm-document-canvas-caret-blink 1.06s step-end infinite';
        overlay.appendChild(caret);
    }

    function appendMathCaret(pageIndex, rect, visual) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const caret = doc.createElement('div');
        caret.className = 'tm-document-canvas-caret tm-document-canvas-math-caret';
        caret.setAttribute('data-testid', 'document-canvas-math-caret');
        caret.setAttribute('data-canvas-math-caret', 'true');
        caret.setAttribute('data-math-id', visual?.mathId || '');
        caret.setAttribute('data-math-slot-name', visual?.slotName || '');
        caret.setAttribute('aria-hidden', 'true');
        caret.style.position = 'absolute';
        assignScaledRectStyle(caret, rect, pageScale(pageIndex), CARET_WIDTH);
        caret.style.background = CARET_FILL;
        caret.style.pointerEvents = 'none';
        caret.style.animation = 'tm-document-canvas-caret-blink 1.06s step-end infinite';
        overlay.appendChild(caret);
    }

    function appendTextBoxCaret(pageIndex, rect, visual) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const caret = doc.createElement('div');
        caret.className = 'tm-document-canvas-caret tm-document-canvas-textbox-caret';
        caret.setAttribute('data-testid', 'document-canvas-textbox-caret');
        caret.setAttribute('data-canvas-textbox-caret', 'true');
        caret.setAttribute('data-object-id', visual?.objectId || '');
        caret.setAttribute('data-textbox-offset', String(visual?.offset ?? 0));
        caret.setAttribute('aria-hidden', 'true');
        caret.style.position = 'absolute';
        assignScaledRectStyle(caret, rect, pageScale(pageIndex), CARET_WIDTH);
        caret.style.background = CARET_FILL;
        caret.style.pointerEvents = 'none';
        caret.style.animation = 'tm-document-canvas-caret-blink 1.06s step-end infinite';
        overlay.appendChild(caret);
    }

    function appendSelectionRect(pageIndex, rect) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const element = createSelectionRectElement({ doc, rect });
        element.setAttribute('data-testid', 'document-canvas-selection-rect');
        element.setAttribute('data-canvas-selection-rect', 'true');
        element.className = `${element.className} tm-document-canvas-selection-rect`;
        assignScaledRectStyle(element, rect, pageScale(pageIndex), 1);
        overlay.appendChild(element);
    }

    function appendMathSelectionRect(pageIndex, rect, visual) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const element = createSelectionRectElement({ doc, rect });
        element.setAttribute('data-testid', 'document-canvas-math-selection-rect');
        element.setAttribute('data-canvas-math-selection-rect', 'true');
        element.setAttribute('data-math-id', visual?.mathId || '');
        element.setAttribute('data-math-slot-name', visual?.slotName || '');
        element.className = `${element.className} tm-document-canvas-selection-rect tm-document-canvas-math-selection-rect`;
        assignScaledRectStyle(element, rect, pageScale(pageIndex), 1);
        overlay.appendChild(element);
    }

    function appendTextBoxSelectionRect(pageIndex, rect, visual) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const element = createSelectionRectElement({ doc, rect });
        element.setAttribute('data-testid', 'document-canvas-textbox-selection-rect');
        element.setAttribute('data-canvas-textbox-selection-rect', 'true');
        element.setAttribute('data-object-id', visual?.objectId || '');
        element.className = `${element.className} tm-document-canvas-selection-rect tm-document-canvas-textbox-selection-rect`;
        assignScaledRectStyle(element, rect, pageScale(pageIndex), 1);
        overlay.appendChild(element);
    }

    function appendTableRangeRect(pageIndex, rect, cell) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const element = doc.createElement('div');
        element.className = 'tm-document-canvas-table-cell-selection-rect';
        element.setAttribute('data-testid', 'document-canvas-table-cell-selection-rect');
        element.setAttribute('data-canvas-table-cell-selection', 'true');
        element.setAttribute('data-table-id', cell.tableId || '');
        element.setAttribute('data-cell-id', cell.cellId || '');
        element.setAttribute('aria-hidden', 'true');
        element.style.position = 'absolute';
        assignScaledRectStyle(element, rect, pageScale(pageIndex), 1);
        element.style.background = TABLE_RANGE_FILL;
        element.style.outline = `1px solid ${TABLE_RANGE_STROKE}`;
        element.style.pointerEvents = 'none';
        overlay.appendChild(element);
    }

    function appendObjectSelection(pageIndex, object, options = {}) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const scale = pageScale(pageIndex);
        const rect = object.rect || {};
        const rotation = Number(object.rotation || 0) || 0;
        // Outline AND handles live in ONE container that rotates about the object centre, so the handles sit on
        // the rotated frame (not a second, un-rotated set beside it). The canvas-layer paintObjectSelection
        // rotates the same way, so both overlays agree.
        const container = doc.createElement('div');
        container.setAttribute('aria-hidden', 'true');
        container.style.position = 'absolute';
        container.style.left = '0';
        container.style.top = '0';
        container.style.width = '100%';
        container.style.height = '100%';
        container.style.pointerEvents = 'none';
        if (Math.abs(rotation) > 0.001) {
            const centerX = (Number(rect.x || 0) || 0) + (Number(rect.width || 0) || 0) / 2;
            const centerY = (Number(rect.y || 0) || 0) + (Number(rect.height || 0) || 0) / 2;
            container.style.transformOrigin = `${centerX * scale}px ${centerY * scale}px`;
            container.style.transform = `rotate(${rotation}deg)`;
        }

        overlay.appendChild(container);

        const outline = doc.createElement('div');
        outline.className = 'tm-document-canvas-object-selection';
        outline.setAttribute('data-testid', 'document-canvas-object-selection');
        outline.setAttribute('data-canvas-object-selection', 'true');
        outline.setAttribute('data-object-id', object.objectId || '');
        outline.setAttribute('aria-hidden', 'true');
        outline.style.position = 'absolute';
        assignScaledRectStyle(outline, object.rect, scale, 1);
        outline.style.outline = `1.5px solid ${OBJECT_SELECTION_STROKE}`;
        outline.style.pointerEvents = 'none';
        container.appendChild(outline);

        if (options.handles === false) {
            return;
        }

        for (const handle of objectInteractionHandleRects(object)) {
            const element = doc.createElement('div');
            const isRotate = handle.name === OBJECT_ROTATE_HANDLE_NAME;
            const isConnectorEndpoint = isObjectConnectorEndpointHandle(handle.name);
            element.className = isConnectorEndpoint
                ? 'tm-document-canvas-object-connector-handle'
                : isRotate
                    ? 'tm-document-canvas-object-rotate-handle'
                    : 'tm-document-canvas-object-resize-handle';
            element.setAttribute('data-testid', isConnectorEndpoint
                ? (handle.name === OBJECT_CONNECTOR_START_HANDLE_NAME
                    ? 'document-canvas-object-connector-handle-start'
                    : 'document-canvas-object-connector-handle-end')
                : isRotate
                    ? 'document-canvas-object-rotate-handle'
                    : `document-canvas-object-resize-handle-${handle.name}`);
            if (isConnectorEndpoint) {
                element.setAttribute('data-canvas-object-connector-handle', handle.name);
            } else if (isRotate) {
                // The rotate handle is NOT a resize handle; labelling it data-canvas-object-resize-handle made
                // the resize-handle count read 9 instead of 8 (the long-standing Phase 15 selection mismatch).
                element.setAttribute('data-canvas-object-rotate-handle', handle.name);
            } else {
                element.setAttribute('data-canvas-object-resize-handle', handle.name);
            }
            element.setAttribute('data-object-id', object.objectId || '');
            element.setAttribute('aria-hidden', 'true');
            element.style.position = 'absolute';
            assignScaledRectStyle(element, handle.rect, scale, 1);
            element.style.background = OBJECT_HANDLE_FILL;
            element.style.border = `1px solid ${OBJECT_HANDLE_STROKE}`;
            element.style.borderRadius = isRotate || isConnectorEndpoint ? '999px' : '0';
            element.style.boxSizing = 'border-box';
            element.style.pointerEvents = 'none';
            container.appendChild(element);
        }
    }

    function appendCompositionUnderline(pageIndex, rect) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        const element = createCompositionUnderlineElement({ doc, rect });
        element.setAttribute('data-testid', 'document-canvas-composition-underline');
        element.setAttribute('data-canvas-composition-underline', 'true');
        element.className = `${element.className} tm-document-canvas-composition-underline`;
        assignScaledRectStyle(element, rect, pageScale(pageIndex), 1);
        overlay.appendChild(element);
    }

    function renderResizePreview(pageIndex, x, tableRect) {
        const overlay = ensureDomOverlay(pageIndex);
        if (!overlay) {
            return;
        }

        clearResizePreview();
        const preview = doc.createElement('div');
        preview.className = 'tm-document-canvas-table-resize-preview';
        preview.setAttribute('data-testid', 'document-canvas-table-resize-preview');
        preview.setAttribute('data-canvas-table-resize-preview', 'true');
        preview.setAttribute('aria-hidden', 'true');
        preview.style.position = 'absolute';
        const scale = pageScale(pageIndex);
        preview.style.left = `${(Number(x) || 0) * scale}px`;
        preview.style.top = `${(Number(tableRect?.y || 0) || 0) * scale}px`;
        preview.style.width = '0';
        preview.style.height = `${Math.max(1, Number(tableRect?.height || 0) || 1) * scale}px`;
        preview.style.borderLeft = `2px solid ${TABLE_RESIZE_STROKE}`;
        preview.style.pointerEvents = 'none';
        overlay.appendChild(preview);
    }

    function clearResizePreview() {
        for (const page of canvasStack.pages.values()) {
            const overlays = typeof page.pageElement.querySelectorAll === 'function'
                ? Array.from(page.pageElement.querySelectorAll('[data-canvas-table-resize-preview="true"]'))
                : findDescendants(page.pageElement, node => node.getAttribute?.('data-canvas-table-resize-preview') === 'true');
            for (const overlay of overlays) {
                overlay.parentNode?.removeChild?.(overlay);
            }
        }
    }

    function ensureDomOverlay(pageIndex) {
        const page = canvasStack.pages.get(String(pageIndex));
        if (!page) {
            return null;
        }

        let overlay = findDomOverlay(page.pageElement);
        if (!overlay) {
            overlay = doc.createElement('div');
            overlay.className = 'tm-document-canvas-selection-overlay';
            overlay.setAttribute('data-testid', 'document-canvas-selection-overlay');
            overlay.setAttribute('aria-hidden', 'true');
            overlay.style.position = 'absolute';
            overlay.style.inset = '0';
            overlay.style.pointerEvents = 'none';
            page.pageElement.appendChild(overlay);
        }

        return overlay;
    }

    function pageScale(pageIndex) {
        const page = canvasStack.pages.get(String(pageIndex));
        return Math.max(0.01, Number(page?.pageElement?.getAttribute?.('data-canvas-page-zoom-scale') || 1) || 1);
    }

    function assignScaledRectStyle(element, rect, scale = 1, minSize = 0) {
        const zoomScale = Math.max(0.01, Number(scale) || 1);
        element.style.left = `${(Number(rect?.x) || 0) * zoomScale}px`;
        element.style.top = `${(Number(rect?.y) || 0) * zoomScale}px`;
        const width = minSize > 0 && !('width' in (rect || {})) ? minSize : Math.max(minSize, Number(rect?.width) || minSize);
        const height = Math.max(1, Number(rect?.height) || 16);
        element.style.width = `${width * zoomScale}px`;
        element.style.height = `${height * zoomScale}px`;
    }

    function rotateOverlayContext(context, rect, rotation) {
        const degrees = Number(rotation || 0) || 0;
        if (Math.abs(degrees) < 0.001) {
            return;
        }

        const centerX = (Number(rect?.x || 0) || 0) + Math.max(1, Number(rect?.width || 0) || 1) / 2;
        const centerY = (Number(rect?.y || 0) || 0) + Math.max(1, Number(rect?.height || 0) || 1) / 2;
        context.translate?.(centerX, centerY);
        context.rotate?.(degrees * Math.PI / 180);
        context.translate?.(-centerX, -centerY);
    }

    function overlayContext(pageIndex) {
        const page = canvasStack.pages.get(String(pageIndex));
        return page?.layers.get('selection-caret')?.getContext?.('2d') || null;
    }

    function findDomOverlay(pageElement) {
        if (typeof pageElement?.querySelector === 'function') {
            return pageElement.querySelector('[data-testid="document-canvas-selection-overlay"]');
        }

        return findDescendant(pageElement, node => node.getAttribute?.('data-testid') === 'document-canvas-selection-overlay');
    }

    function findPageElement(target) {
        if (target?.closest) {
            return target.closest('[data-testid="document-canvas-page"]');
        }

        let node = target;
        while (node) {
            if (node.getAttribute?.('data-testid') === 'document-canvas-page') {
                return node;
            }

            node = node.parentNode;
        }

        return null;
    }

    function pageElementForIndex(pageIndex) {
        return canvasStack.pages.get(String(pageIndex))?.pageElement || null;
    }

    function pageBodyForIndex(pageIndex) {
        return (layout?.pages || []).find(page => Number(page.index || 0) === Number(pageIndex || 0))?.body
            || canvasStack.pages.get(String(pageIndex))?.layout?.body
            || { x: 0, y: 0 };
    }

    function objectSnapContext(state, event) {
        const pageIndex = Number(state?.object?.pageIndex ?? state?.startPoint?.pageIndex ?? 0) || 0;
        return {
            layout,
            pageIndex,
            objectId: state?.object?.objectId || '',
            body: pageBodyForIndex(pageIndex),
            enabled: event?.altKey !== true,
        };
    }

    function syncObjectSnapAttributes(snap) {
        const active = snap?.snapped === true;
        root?.setAttribute?.('data-canvas-object-snap-active', String(active));
        root?.setAttribute?.('data-canvas-object-snap-x', active && snap?.x ? String(Math.round(snap.x.guide)) : '');
        root?.setAttribute?.('data-canvas-object-snap-y', active && snap?.y ? String(Math.round(snap.y.guide)) : '');
        root?.setAttribute?.('data-canvas-object-snap-x-type', active && snap?.x ? snap.x.guideType || '' : '');
        root?.setAttribute?.('data-canvas-object-snap-y-type', active && snap?.y ? snap.y.guideType || '' : '');
        root?.setAttribute?.('data-canvas-object-snap-x-edge', active && snap?.x ? snap.x.edge || '' : '');
        root?.setAttribute?.('data-canvas-object-snap-y-edge', active && snap?.y ? snap.y.edge || '' : '');
    }

    function syncObjectSnapLastAttributes(snap) {
        const active = snap?.snapped === true;
        root?.setAttribute?.('data-canvas-object-snap-last-active', String(active));
        root?.setAttribute?.('data-canvas-object-snap-last-x', active && snap?.x ? String(Math.round(snap.x.guide)) : '');
        root?.setAttribute?.('data-canvas-object-snap-last-y', active && snap?.y ? String(Math.round(snap.y.guide)) : '');
        root?.setAttribute?.('data-canvas-object-snap-last-x-type', active && snap?.x ? snap.x.guideType || '' : '');
        root?.setAttribute?.('data-canvas-object-snap-last-y-type', active && snap?.y ? snap.y.guideType || '' : '');
        root?.setAttribute?.('data-canvas-object-snap-last-x-edge', active && snap?.x ? snap.x.edge || '' : '');
        root?.setAttribute?.('data-canvas-object-snap-last-y-edge', active && snap?.y ? snap.y.edge || '' : '');
    }

    function withTextBoxState(object, sourceTextBox = null) {
        if (!object || !isTextEditableObject(object)) {
            return object;
        }

        const source = sourceTextBox || object.textBox || {};
        const textLength = textBoxTextLengthForObject({ ...object, textBox: source });
        const offset = clampTextBoxOffset(source.offset ?? source.Offset ?? textLength, { ...object, textBox: source });
        const anchor = Number.isFinite(Number(source.selectionAnchorOffset ?? source.SelectionAnchorOffset))
            ? clampTextBoxOffset(source.selectionAnchorOffset ?? source.SelectionAnchorOffset, { ...object, textBox: source })
            : offset;
        const focus = Number.isFinite(Number(source.selectionFocusOffset ?? source.SelectionFocusOffset))
            ? clampTextBoxOffset(source.selectionFocusOffset ?? source.SelectionFocusOffset, { ...object, textBox: source })
            : offset;
        return {
            ...object,
            textBox: {
                active: source.active === true || source.Active === true,
                offset,
                selectionAnchorOffset: anchor,
                selectionFocusOffset: focus,
                selecting: source.selecting === true || source.Selecting === true || anchor !== focus,
                textLength,
                text: String(source.text ?? source.Text ?? ''),
                alignment: String(source.alignment ?? source.Alignment ?? ''),
            },
        };
    }

    function isTextEditableObject(object) {
        const kind = String(object?.kind || object?.object?.kind || '')
            .replace(/[\s_-]/g, '')
            .toLowerCase();
        return kind === 'textbox' || kind === 'shape';
    }

    function textBoxLinesForObject(object) {
        const objectId = String(object?.objectId || '');
        if (!objectId) {
            return [];
        }

        return (layout?.drawingText || [])
            .filter(line => String(line.objectId || '') === objectId)
            .sort((left, right) =>
                (Number(left.pageIndex || 0) - Number(right.pageIndex || 0))
                || (Number(left.start || 0) - Number(right.start || 0))
                || (Number(left.sequence || 0) - Number(right.sequence || 0)));
    }

    function textBoxTextLengthForObject(object) {
        const lines = textBoxLinesForObject(object);
        const lineLength = lines.length > 0
            ? Math.max(...lines.map(line => Number(line.end || 0) || 0))
            : 0;
        const rawStateLength = Number(object?.textBox?.textLength ?? object?.textBox?.TextLength);
        const stateLength = Number.isFinite(rawStateLength) ? rawStateLength : 0;
        const textLength = String(object?.textBox?.text ?? object?.textBox?.Text ?? '').length;
        return Math.max(0, stateLength, textLength, lineLength);
    }

    function clampTextBoxOffset(offset, object) {
        const length = textBoxTextLengthForObject(object);
        return Math.max(0, Math.min(length, Number(offset ?? length) || 0));
    }

    function setTextBoxOffset(offset, anchorOverride = null, focusOverride = offset) {
        const textLength = textBoxTextLengthForObject(objectSelection);
        const safeFocus = Math.max(0, Math.min(textLength, Number(focusOverride ?? offset) || 0));
        const current = objectSelection?.textBox || {};
        const currentAnchor = Number.isFinite(Number(current.selectionAnchorOffset))
            ? Math.max(0, Math.min(textLength, Number(current.selectionAnchorOffset)))
            : safeFocus;
        const safeAnchor = anchorOverride == null
            ? currentAnchor
            : Math.max(0, Math.min(textLength, Number(anchorOverride) || 0));
        objectSelection = {
            ...objectSelection,
            textBox: {
                ...current,
                active: true,
                offset: safeFocus,
                selectionAnchorOffset: safeAnchor,
                selectionFocusOffset: safeFocus,
                selecting: safeAnchor !== safeFocus,
                textLength,
            },
        };
        selection = createSelection(selection.anchor, selection.focus, objectSelection);
        renderOverlay();
    }

    function textBoxSelectionVisual(object) {
        const normalized = withTextBoxState(object, object?.textBox);
        const textBox = normalized?.textBox || {};
        const lines = textBoxLinesForObject(normalized);
        const textLength = textBoxTextLengthForObject(normalized);
        const offset = clampTextBoxOffset(textBox.offset ?? textLength, normalized);
        const anchor = clampTextBoxOffset(textBox.selectionAnchorOffset ?? offset, normalized);
        const focus = clampTextBoxOffset(textBox.selectionFocusOffset ?? offset, normalized);
        return {
            objectId: normalized.objectId || '',
            pageIndex: Number(normalized.pageIndex || 0) || 0,
            offset,
            textLength,
            lines,
            caretRect: textBoxCaretRect(normalized, offset),
            selectionRects: anchor !== focus || textBox.selecting === true
                ? textBoxSelectionRects(normalized, anchor, focus)
                : [],
        };
    }

    function textBoxLineForOffset(object, offset) {
        const lines = textBoxLinesForObject(object);
        if (!lines.length) {
            return null;
        }

        const safeOffset = clampTextBoxOffset(offset, object);
        return lines.find(line => safeOffset >= Number(line.start || 0) && safeOffset <= Number(line.end || 0))
            || lines.find(line => safeOffset <= Number(line.end || 0))
            || lines.at(-1)
            || null;
    }

    function textBoxCaretRect(object, offset) {
        const line = textBoxLineForOffset(object, offset);
        if (!line) {
            const rect = object?.rect || {};
            return {
                x: (Number(rect.x || 0) || 0) + 8,
                y: (Number(rect.y || 0) || 0) + 8,
                width: CARET_WIDTH,
                height: 18,
            };
        }

        const textLength = Math.max(0, Number(line.end || 0) - Number(line.start || 0));
        const localOffset = Math.max(0, Math.min(textLength, Number(offset || 0) - Number(line.start || 0)));
        const textWidth = estimatedTextLineWidth(line);
        const charWidth = textLength > 0 ? textWidth / textLength : Math.max(5, Number(line.style?.fontSize || 14) * 0.52);
        return {
            x: textBoxLineStartX(line) + charWidth * localOffset,
            y: Number(line.y || 0) || 0,
            width: CARET_WIDTH,
            height: Math.max(1, Number(line.height || 0) || Number(line.style?.fontSize || 14) * 1.25),
        };
    }

    function textBoxSelectionRects(object, anchorOffset, focusOffset) {
        const start = Math.min(clampTextBoxOffset(anchorOffset, object), clampTextBoxOffset(focusOffset, object));
        const end = Math.max(clampTextBoxOffset(anchorOffset, object), clampTextBoxOffset(focusOffset, object));
        if (start === end) {
            return [];
        }

        const rects = [];
        for (const line of textBoxLinesForObject(object)) {
            const lineStart = Number(line.start || 0) || 0;
            const lineEnd = Number(line.end || lineStart) || lineStart;
            const from = Math.max(start, lineStart);
            const to = Math.min(end, lineEnd);
            if (to <= from) {
                continue;
            }

            const length = Math.max(1, lineEnd - lineStart);
            const textWidth = estimatedTextLineWidth(line);
            const charWidth = textWidth / length;
            const x = textBoxLineStartX(line) + (from - lineStart) * charWidth;
            rects.push({
                pageIndex: Number(line.pageIndex || 0) || 0,
                rect: {
                    x,
                    y: Number(line.y || 0) || 0,
                    width: Math.max(1, (to - from) * charWidth),
                    height: Math.max(1, Number(line.height || 0) || Number(line.style?.fontSize || 14) * 1.25),
                },
            });
        }

        return rects;
    }

    function textBoxOffsetAtPoint(object, x, y) {
        const lines = textBoxLinesForObject(object);
        if (!lines.length) {
            return textBoxTextLengthForObject(object);
        }

        const samePage = lines.filter(line => Number(line.pageIndex || 0) === Number(object?.pageIndex || 0));
        const candidates = samePage.length ? samePage : lines;
        let best = candidates[0];
        let bestDistance = Infinity;
        for (const line of candidates) {
            const top = Number(line.y || 0) || 0;
            const bottom = top + Math.max(1, Number(line.height || 0) || 1);
            const distance = y < top ? top - y : y > bottom ? y - bottom : 0;
            if (distance < bestDistance) {
                best = line;
                bestDistance = distance;
            }
        }

        const lineStart = Number(best.start || 0) || 0;
        const lineEnd = Number(best.end || lineStart) || lineStart;
        const length = Math.max(0, lineEnd - lineStart);
        if (length === 0) {
            return lineStart;
        }

        const startX = textBoxLineStartX(best);
        const charWidth = estimatedTextLineWidth(best) / Math.max(1, length);
        const local = Math.max(0, Math.min(length, Math.round(((Number(x || 0) || 0) - startX) / Math.max(1, charWidth))));
        return clampTextBoxOffset(lineStart + local, object);
    }

    function textBoxVerticalNavigationOffset(object, direction) {
        const lines = textBoxLinesForObject(object);
        if (!lines.length) {
            return clampTextBoxOffset(object?.textBox?.offset || 0, object);
        }

        const currentLine = textBoxLineForOffset(object, object?.textBox?.offset || 0) || lines[0];
        const currentIndex = lines.indexOf(currentLine);
        const nextLine = lines[Math.max(0, Math.min(lines.length - 1, currentIndex + direction))] || currentLine;
        const caret = textBoxCaretRect(object, object?.textBox?.offset || 0);
        return textBoxOffsetAtLineX(object, nextLine, caret.x);
    }

    function textBoxOffsetAtLineX(object, line, x) {
        const lineStart = Number(line?.start || 0) || 0;
        const lineEnd = Number(line?.end || lineStart) || lineStart;
        const length = Math.max(0, lineEnd - lineStart);
        if (length === 0) {
            return lineStart;
        }

        const startX = textBoxLineStartX(line);
        const charWidth = estimatedTextLineWidth(line) / Math.max(1, length);
        return clampTextBoxOffset(lineStart + Math.max(0, Math.min(length, Math.round(((Number(x || 0) || 0) - startX) / Math.max(1, charWidth)))), object);
    }

    function textBoxLineStartX(line) {
        const x = Number(line?.x || 0) || 0;
        const width = Math.max(1, Number(line?.width || 0) || 1);
        const textWidth = estimatedTextLineWidth(line);
        const align = String(line?.align || '').replace(/[\s_-]/g, '').toLowerCase();
        if (align === 'center') {
            return x + Math.max(0, (width - textWidth) / 2);
        }

        if (align === 'right' || align === 'end') {
            return x + Math.max(0, width - textWidth);
        }

        return x;
    }

    function estimatedTextLineWidth(line) {
        const text = String(line?.text || '');
        if (!text) {
            return Math.max(1, Number(line?.style?.fontSize || 14) * 0.52);
        }

        const fontSize = Math.max(6, Number(line?.style?.fontSize || 14) || 14);
        return Math.max(1, Math.min(Number(line?.width || 1) || 1, text.length * fontSize * 0.52));
    }

    function mathSelectionVisual(math) {
        const equation = mathEquationForSelection(math);
        if (!equation?.mathLayout) {
            return null;
        }

        const slotPath = normalizeMathPath(math?.slotPath || math?.SlotPath || []);
        const slot = collectMathSlots(equation.mathLayout, { includeRoot: true })
            .find(item => sameMathPath(item.path, slotPath)) || null;
        const textLength = Math.max(0, Number(slot?.textLength || 0) || 0);
        const offset = Math.max(0, Math.min(textLength, Number(math?.offset ?? math?.Offset ?? textLength) || 0));
        const localCaret = mathCaretRectForSlot(equation.mathLayout, slotPath, offset);
        const caretRect = translateMathRect(equation, localCaret, CARET_WIDTH);
        const selectionRects = math?.structuralRange === true
            ? mathStructuralSelectionRects(equation, math.selectedSlotPaths)
            : math?.selecting === true
                ? mathSelectionRectsForSlot(equation, slotPath, textLength, math.selectionAnchorOffset, math.selectionFocusOffset)
                : [];
        return {
            pageIndex: Number(equation.pageIndex || 0) || 0,
            mathId: equation.mathId || math?.mathId || '',
            runId: equation.runId || math?.runId || '',
            slotPath,
            slotName: slot?.slotName || math?.slotName || '',
            offset,
            textLength,
            caretRect,
            selectionRects,
        };
    }

    function mathEquationForSelection(math) {
        const mathId = String(math?.mathId || math?.MathId || '');
        const runId = String(math?.runId || math?.RunId || '');
        return (layout?.mathEquations || []).find(equation =>
            (mathId && String(equation.mathId || '') === mathId)
            || (runId && String(equation.runId || '') === runId)) || null;
    }

    function mathSelectionRectsForSlot(equation, slotPath, textLength, anchorOffset, focusOffset) {
        const length = Math.max(1, Number(textLength || 0) || 1);
        const start = Math.max(0, Math.min(length, Number(anchorOffset || 0) || 0));
        const end = Math.max(0, Math.min(length, Number(focusOffset || 0) || 0));
        if (start === end) {
            return [];
        }

        const local = mathSlotRectForSlot(equation.mathLayout, slotPath);
        const left = Math.min(start, end) / length;
        const right = Math.max(start, end) / length;
        return [{
            pageIndex: Number(equation.pageIndex || 0) || 0,
            rect: translateMathRect(equation, {
                x: local.x + local.width * left,
                y: local.y,
                width: Math.max(1, local.width * (right - left)),
                height: local.height,
            }, 1),
        }];
    }

    function mathStructuralSelectionRects(equation, selectedSlotPaths) {
        const paths = Array.isArray(selectedSlotPaths) ? selectedSlotPaths : [];
        return paths
            .map(path => normalizeMathPath(path))
            .filter(path => path.length > 0)
            .map(path => ({
                pageIndex: Number(equation.pageIndex || 0) || 0,
                rect: translateMathRect(equation, mathSlotRectForSlot(equation.mathLayout, path), 1),
            }));
    }

    function translateMathRect(equation, rect, minWidth = 1) {
        return {
            x: (Number(equation.x || equation.rect?.x || 0) || 0) + (Number(rect?.x || 0) || 0),
            y: (Number(equation.y || equation.rect?.y || 0) || 0) + (Number(rect?.y || 0) || 0),
            width: Math.max(minWidth, Number(rect?.width || 0) || minWidth),
            height: Math.max(1, Number(rect?.height || 0) || 12),
        };
    }

    function selectWordAt(position) {
        const text = blockText(model, position.blockId);
        const range = wordRangeAt(text, position.offset);
        return createSelection({ blockId: position.blockId, offset: range.start }, { blockId: position.blockId, offset: range.end });
    }

    function selectParagraphAt(position) {
        return createSelection(
            { blockId: position.blockId, offset: 0 },
            { blockId: position.blockId, offset: blockMaxOffset(layout, position.blockId) });
    }

    function destroy() {
        root?.removeEventListener?.('mousedown', onMouseDown);
        doc.removeEventListener?.('mousemove', onMouseMove);
        doc.removeEventListener?.('mouseup', onMouseUp);
        inputBridge?.input?.removeEventListener?.('keydown', onKeyDown);
        unsubscribeInput?.();
        clearOverlayCanvases();
        clearDomOverlay();
        selection = null;
        objectSelection = null;
        compositionRange = null;
        pointerState = null;
    }

    const api = {
        mount,
        update,
        destroy,
        setSelection,
        enterHeaderFooter,
        exitHeaderFooterToBody,
        setCompositionRange(range) {
            compositionRange = range ? normalizeSelection(range) : null;
            renderOverlay();
        },
        getSelection() {
            if (!selection) {
                return null;
            }

            return objectSelection
                ? createSelection(selection.anchor, selection.focus, objectSelection)
                : normalizeSelection(selection);
        },
        getState() {
            const collapsed = !selection || isCollapsed(selection);
            const mathVisual = selection?.math ? mathSelectionVisual(selection.math) : null;
            const caret = selection ? (mathVisual ? { pageIndex: mathVisual.pageIndex, rect: mathVisual.caretRect } : caretRectForPosition(layout, selection.focus)) : null;
            const rects = selection
                ? (mathVisual?.selectionRects?.length ? mathVisual.selectionRects : (mathVisual ? [] : selectionRectsForRange(layout, selection.anchor, selection.focus)))
                : [];
            const boundingRect = boundingRectForSelectionRects(rects);
            const tableCell = selection ? findTableCellByBlockId(model, selection.focus.blockId) : null;
            // B6: the editable region of the caret (body / header / footer) so the host can light up the
            // Header & Footer contextual tab + enable field commands when editing a header/footer.
            const editRegion = selection ? editableRegionForBlock(model, selection.focus.blockId) : { kind: 'body' };
            return {
                isCollapsed: collapsed,
                anchor: selection?.anchor || null,
                focus: selection?.focus || null,
                region: editRegion.kind === 'headerFooter' ? editRegion.region : 'Body',
                headerFooterScope: editRegion.kind === 'headerFooter' ? (editRegion.scope || '') : '',
                pageIndex: caret?.pageIndex ?? rects[0]?.pageIndex ?? 0,
                caretRect: caret?.rect || null,
                selectionRectCount: rects.length,
                selectionRects: rects.map(item => ({
                    pageIndex: Number(item.pageIndex || 0) || 0,
                    rect: item.rect,
                })),
                boundingRect,
                table: tableCell ? {
                    inTable: true,
                    tableId: tableCell.tableBlock.id || '',
                    cellId: tableCell.cell.id || '',
                    rowIndex: tableCell.rowIndex,
                    cellIndex: tableCell.cellIndex,
                } : { inTable: false },
                object: objectSelection ? { ...objectSelection } : null,
                math: mathVisual ? {
                    active: true,
                    mathId: mathVisual.mathId,
                    runId: mathVisual.runId,
                    slotPath: mathVisual.slotPath,
                    slotName: mathVisual.slotName,
                    offset: mathVisual.offset,
                    textLength: mathVisual.textLength,
                    selectionActive: mathVisual.selectionRects.length > 0,
                    structuralRange: selection?.math?.structuralRange === true,
                    selectedSlotPaths: Array.isArray(selection?.math?.selectedSlotPaths)
                        ? selection.math.selectedSlotPaths.map(path => Array.isArray(path) ? path.slice() : [])
                        : [],
                    structuralPath: Array.isArray(selection?.math?.structuralPath) ? selection.math.structuralPath.slice() : [],
                } : { active: false },
                textBox: objectSelection?.textBox ? {
                    active: objectSelection.textBox.active === true,
                    objectId: objectSelection.objectId || '',
                    offset: Number(objectSelection.textBox.offset || 0) || 0,
                    textLength: textBoxTextLengthForObject(objectSelection),
                    selectionActive: objectSelection.textBox.selecting === true,
                } : { active: false },
                revision: selectionRevision,
                compositionRange,
            };
        },
        hitTestPoint(pageIndex, x, y) {
            return layout ? hitTestOnPage(layout, pageIndex, x, y) : null;
        },
        tableResizeHandleAt(pageIndex, x, y) {
            return layout ? tableResizeHandleAt(layout, pageIndex, x, y) : null;
        },
        caretRect(position) {
            return layout ? caretRectForPosition(layout, position) : null;
        },
    };

    return api;
}

export function tableCellRectsForSelectionRange(layout, model, selection) {
    const anchor = findTableCellByBlockId(model, selection?.anchor?.blockId);
    const focus = findTableCellByBlockId(model, selection?.focus?.blockId);
    if (!anchor || !focus || anchor.tableBlock.id !== focus.tableBlock.id || anchor.cell.id === focus.cell.id) {
        return [];
    }

    const range = cellRangeFromSelection(model, selection);
    if (!range) {
        return [];
    }

    const tableLayout = (layout?.blocks || []).find(block =>
        block?.type === 'table'
        && String(block?.table?.tableId || block?.blockId || block?.id || '') === String(range.table.tableBlock.id || ''));
    const cells = tableLayout?.table?.cells || [];
    return cells
        .filter(cell =>
            Number(cell.rowIndex || 0) >= range.startRow
            && Number(cell.rowIndex || 0) <= range.endRow
            && Number(cell.cellIndex ?? cell.columnIndex ?? 0) >= range.startCell
            && Number(cell.cellIndex ?? cell.columnIndex ?? 0) <= range.endCell)
        .map(cell => ({
            pageIndex: Number(cell.pageIndex || tableLayout.pageIndex || 0) || 0,
            rect: cellRect(cell),
            cell,
        }));
}

export function tableResizeHandleAt(layout, pageIndex, x, y) {
    const tableBlocks = (layout?.blocks || [])
        .filter(block => block?.type === 'table' && block?.table);
    for (const tableBlock of tableBlocks) {
        const cells = tableBlock.table?.cells || [];
        for (const cell of cells) {
            if (Number(cell.pageIndex || tableBlock.pageIndex || 0) !== Number(pageIndex || 0)) {
                continue;
            }

            if ((Number(cell.columnSpan || 1) || 1) !== 1 || cell.merge?.isOrigin === false) {
                continue;
            }

            const rect = cellRect(cell);
            const left = rect.x;
            const top = rect.y;
            const width = rect.width;
            const height = rect.height;
            const right = left + width;
            if (x >= right - TABLE_RESIZE_HIT_WIDTH && x <= right + TABLE_RESIZE_HIT_WIDTH && y >= top && y <= top + height) {
                return {
                    pageIndex: Number(pageIndex || 0) || 0,
                    tableId: cell.tableId || tableBlock.table?.tableId || '',
                    cellId: cell.cellId || '',
                    columnIndex: Number(cell.columnIndex ?? cell.cellIndex ?? 0) || 0,
                    left,
                    width,
                    previewX: right,
                    tableRect: tableBlock.rect || { x: left, y: top, width, height },
                };
            }
        }
    }

    return null;
}

function cellRect(cell) {
    const source = cell?.rect && typeof cell.rect === 'object' ? cell.rect : cell;
    return {
        x: Number(source?.x || 0) || 0,
        y: Number(source?.y || 0) || 0,
        width: Math.max(1, Number(source?.width || 0) || 1),
        height: Math.max(1, Number(source?.height || 0) || 1),
    };
}

function resolveObjectSelection(layout, selection) {
    const objectId = String(selection?.objectId || '');
    const blockId = String(selection?.blockId || '');
    const runId = String(selection?.runId || '');
    const block = (layout?.blocks || []).find(candidate =>
        candidate?.type === 'image'
        && ((objectId && String(candidate.objectId || candidate.object?.objectId || '') === objectId)
            || (runId && String(candidate.runId || '') === runId)
            || (blockId && String(candidate.blockId || '') === blockId)));
    if (!block) {
        return null;
    }

    return {
        ...selection,
        objectId: String(block.objectId || block.object?.objectId || selection.objectId || ''),
        blockId: String(block.blockId || selection.blockId || ''),
        runId: String(block.runId || selection.runId || ''),
        role: String(block.role || block.object?.role || selection.role || 'imageBlock'),
        pageIndex: Number(block.pageIndex || 0) || 0,
        rect: { ...(block.rect || {}) },
        width: Math.max(1, Number(block.rect?.width || selection.width || 1) || 1),
        height: Math.max(1, Number(block.rect?.height || selection.height || 1) || 1),
        rotation: Number(block.object?.rotation ?? selection.rotation ?? 0) || 0,
        flipHorizontal: block.object?.flipHorizontal === true || selection.flipHorizontal === true,
        flipVertical: block.object?.flipVertical === true || selection.flipVertical === true,
        wrapMode: block.object?.wrapMode || selection.wrapMode || 'Inline',
        altText: block.object?.altText ?? selection.altText ?? '',
        caption: block.object?.caption ?? selection.caption ?? '',
        kind: String(block.object?.kind || selection.kind || ''),
        zIndex: Number(block.object?.zIndex ?? selection.zIndex ?? 0) || 0,
        connector: cloneConnector(block.connector || block.object?.connector || selection.connector || null),
    };
}

function previewConnectorWithEndpoint(connector, handle, point) {
    const next = cloneConnector(connector || {}) || { routing: '', points: [] };
    const endpoint = {
        x: Number(point?.x || 0) || 0,
        y: Number(point?.y || 0) || 0,
    };
    if (handle === OBJECT_CONNECTOR_START_HANDLE_NAME) {
        next.start = endpoint;
    } else if (handle === OBJECT_CONNECTOR_END_HANDLE_NAME) {
        next.end = endpoint;
    }

    const start = next.start || next.points?.[0];
    const end = next.end || next.points?.at?.(-1);
    if (start && end) {
        next.points = buildPreviewConnectorPoints(start, end, next.routing);
    }

    return next;
}

function buildPreviewConnectorPoints(start, end, routing) {
    const normalized = String(routing || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'elbow' || normalized === 'orthogonal') {
        const startX = Number(start.x || 0) || 0;
        const startY = Number(start.y || 0) || 0;
        const endX = Number(end.x || 0) || 0;
        const endY = Number(end.y || 0) || 0;
        const midX = startX + (endX - startX) / 2;
        return [
            { x: startX, y: startY },
            { x: midX, y: startY },
            { x: midX, y: endY },
            { x: endX, y: endY },
        ];
    }

    return [
        { x: Number(start.x || 0) || 0, y: Number(start.y || 0) || 0 },
        { x: Number(end.x || 0) || 0, y: Number(end.y || 0) || 0 },
    ];
}

function cloneConnector(connector) {
    if (!connector || typeof connector !== 'object') {
        return null;
    }

    return {
        routing: String(connector.routing || ''),
        start: clonePoint(connector.start),
        end: clonePoint(connector.end),
        points: Array.isArray(connector.points) ? connector.points.map(clonePoint).filter(Boolean) : [],
        startConnection: connector.startConnection ? { ...connector.startConnection } : null,
        endConnection: connector.endConnection ? { ...connector.endConnection } : null,
    };
}

function clonePoint(point) {
    const x = Number(point?.x ?? point?.X);
    const y = Number(point?.y ?? point?.Y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
        return null;
    }

    return { x, y };
}

export function normalizeSelectionLayout(layout) {
    const normalized = layout || { blocks: [] };
    normalized.caretStops = collectCaretStops(normalized);
    return normalized;
}

export function hitTestOnPage(layout, pageIndex, x, y) {
    const tableHit = hitTestTableCellPosition(layout, pageIndex, x, y);
    if (tableHit) {
        return tableHit;
    }

    const pageLayout = filterLayoutToPage(layout, pageIndex);
    return hitTestPoint(pageLayout, x, y);
}

export function hitTestMathSlot(layout, pageIndex, x, y) {
    const equations = (layout?.mathEquations || [])
        .filter(equation => Number(equation?.pageIndex || 0) === Number(pageIndex || 0))
        .slice()
        .sort((left, right) => (Number(right.sequence || 0) - Number(left.sequence || 0)));
    for (const equation of equations) {
        const rect = equation.rect || equation;
        const localX = (Number(x || 0) || 0) - (Number(rect.x || 0) || 0);
        const localY = (Number(y || 0) || 0) - (Number(rect.y || 0) || 0);
        if (localX < -4
            || localY < -4
            || localX > Math.max(1, Number(rect.width || 0) || 1) + 4
            || localY > Math.max(1, Number(rect.height || 0) || 1) + 4) {
            continue;
        }

        const slot = mathSlotAtPoint(equation.mathLayout, localX, localY, { hitSlop: 4 });
        if (!slot) {
            continue;
        }

        return {
            pageIndex: Number(equation.pageIndex || 0) || 0,
            blockId: String(equation.blockId || ''),
            runId: String(equation.runId || ''),
            mathId: String(equation.mathId || equation.mathLayout?.mathId || ''),
            start: Number(equation.start || 0) || 0,
            end: Number(equation.end || equation.start || 0) || 0,
            slotPath: normalizeMathPath(slot.path),
            slotName: slot.slotName || '',
            offset: Math.max(0, Number(slot.offset || 0) || 0),
            rect: {
                x: (Number(rect.x || 0) || 0) + (Number(slot.rect?.x || 0) || 0),
                y: (Number(rect.y || 0) || 0) + (Number(slot.rect?.y || 0) || 0),
                width: Math.max(1, Number(slot.rect?.width || 0) || 1),
                height: Math.max(1, Number(slot.rect?.height || 0) || 1),
            },
        };
    }

    return null;
}

export function caretRectForPosition(layout, position) {
    const stop = caretStopAt(layout, position);
    if (!stop) {
        return null;
    }

    return {
        pageIndex: Number(stop.pageIndex || 0) || 0,
        rect: stop.rect,
    };
}

export function blockText(model, blockId) {
    const block = findBlockDeep(model, blockId);
    const runs = Array.isArray(block?.content?.runs) ? block.content.runs : [];
    return runs.map(run => createCanvasRunText(run)).join('');
}

export function moveWordPosition(model, position, key) {
    const text = blockText(model, position?.blockId);
    const offset = Number(position?.offset || 0) || 0;
    if (!text) {
        return clonePosition(position);
    }

    if (key === 'ArrowLeft') {
        const range = wordRangeAt(text, Math.max(0, offset - 1));
        return { blockId: position.blockId, offset: range.start };
    }

    const range = wordRangeAt(text, Math.min(text.length, offset + 1));
    const nextOffset = range.end > offset ? range.end : Math.min(text.length, offset + 1);
    return { blockId: position.blockId, offset: nextOffset };
}

function filterLayoutToPage(layout, pageIndex) {
    return {
        ...layout,
        blocks: (layout?.blocks || []).map(block => ({
            ...block,
            lines: (block.lines || []).filter(line => Number(line.pageIndex || 0) === Number(pageIndex || 0)),
            segments: (block.segments || []).filter(segment => Number(segment.pageIndex || 0) === Number(pageIndex || 0)),
            caretStops: (block.caretStops || []).filter(stop => Number(stop.pageIndex || 0) === Number(pageIndex || 0)),
        })).filter(block => block.caretStops.length > 0),
    };
}

function hitTestTableCellPosition(layout, pageIndex, x, y) {
    const tableBlocks = (layout?.blocks || [])
        .filter(block => block?.type === 'table' && block?.table);
    for (const tableBlock of tableBlocks) {
        const cell = hitTestTableCell(tableBlock, pageIndex, x, y);
        if (!cell) {
            continue;
        }

        const blockIds = new Set((cell.blockIds || []).map(String).filter(Boolean));
        if (blockIds.size === 0) {
            for (const block of layout?.blocks || []) {
                if (block?.cell?.tableId === cell.tableId && block?.cell?.cellId === cell.cellId && block.blockId) {
                    blockIds.add(String(block.blockId));
                }
            }
        }

        const cellStops = collectCaretStops(layout)
            .filter(stop => blockIds.has(String(stop.blockId || '')) && Number(stop.pageIndex || 0) === Number(pageIndex || 0));
        const best = nearestCaretStop(cellStops, x, y);
        if (best) {
            return {
                blockId: best.blockId,
                offset: Number(best.offset || 0) || 0,
                lineId: best.lineId || null,
                pageIndex: Number(best.pageIndex || 0) || 0,
            };
        }
    }

    return null;
}

function nearestCaretStop(stops, x, y) {
    if (!Array.isArray(stops) || stops.length === 0) {
        return null;
    }

    let best = null;
    let bestScore = Infinity;
    for (const stop of stops) {
        const rect = stop.rect || {};
        const stopX = Number(rect.x || 0) || 0;
        const stopY = Number(rect.y || 0) || 0;
        const stopHeight = Math.max(1, Number(rect.height || 1) || 1);
        const vertical = y < stopY ? stopY - y : y > stopY + stopHeight ? y - stopY - stopHeight : 0;
        const horizontal = Math.abs(stopX - x);
        const score = vertical * 4096 + horizontal;
        if (score < bestScore) {
            best = stop;
            bestScore = score;
        }
    }

    return best;
}

function firstTextPosition(layout, model = null) {
    const stops = collectCaretStops(layout);
    if (!stops.length) {
        return null;
    }

    const sorted = stops
        .slice()
        .sort((left, right) =>
            (Number(left.pageIndex || 0) - Number(right.pageIndex || 0))
            || (Number(left.rect?.y || 0) - Number(right.rect?.y || 0))
            || (Number(left.rect?.x || 0) - Number(right.rect?.x || 0)));
    // Header/footer (and note) text paints above/below the body, so the visually-first stop can be
    // chrome rather than content. The caret must default into the body; fall back to the first stop
    // only for documents with no body text at all.
    const first = (model ? sorted.find(stop => editableRegionForBlock(model, stop.blockId).kind === 'body') : null) || sorted[0];
    return { blockId: first.blockId, offset: Number(first.offset || 0) || 0 };
}

function positionExists(layout, position) {
    return !!(position && caretStopAt(layout, position));
}

function findBlockDeep(model, blockId) {
    const id = String(blockId || '');
    const stack = allEditableBlocks(model).slice().reverse();
    while (stack.length > 0) {
        const block = stack.pop();
        if (String(block?.id || '') === id) {
            return block;
        }

        const rows = block?.content?.table?.rows;
        if (Array.isArray(rows)) {
            for (let rowIndex = rows.length - 1; rowIndex >= 0; rowIndex -= 1) {
                const cells = rows[rowIndex]?.cells || [];
                for (let cellIndex = cells.length - 1; cellIndex >= 0; cellIndex -= 1) {
                    for (const nested of [...(cells[cellIndex]?.blocks || [])].reverse()) {
                        stack.push(nested);
                    }
                }
            }
        }
    }

    return null;
}

function allEditableBlocks(model) {
    const blocks = orderedCanvasBlocks(model);
    for (const headerFooter of Array.isArray(model?.headersFooters) ? model.headersFooters : []) {
        blocks.push(...(Array.isArray(headerFooter?.blocks) ? headerFooter.blocks : []));
    }

    for (const note of Array.isArray(model?.notes) ? model.notes : []) {
        blocks.push(...(Array.isArray(note?.blocks) ? note.blocks : []));
    }

    return blocks;
}

export function editableRegionForBlock(model, blockId) {
    const target = String(blockId || '');
    for (const headerFooter of Array.isArray(model?.headersFooters) ? model.headersFooters : []) {
        if ((headerFooter?.blocks || []).some(block => String(block?.id || '') === target)) {
            return {
                kind: 'headerFooter',
                region: normalizeHeaderFooterType(headerFooter?.type ?? headerFooter?.Type),
                scope: normalizeHeaderFooterScope(headerFooter?.scope ?? headerFooter?.Scope),
            };
        }
    }

    for (const note of Array.isArray(model?.notes) ? model.notes : []) {
        if ((note?.blocks || []).some(block => String(block?.id || '') === target)) {
            return { kind: 'note' };
        }
    }

    return { kind: 'body' };
}

function normalizeHeaderFooterType(value) {
    if (typeof value === 'number') {
        return value === 1 ? 'Footer' : 'Header';
    }

    return String(value || '').toLowerCase() === 'footer' ? 'Footer' : 'Header';
}

function normalizeHeaderFooterScope(value) {
    if (typeof value === 'number') {
        if (value === 1) {
            return 'FirstPage';
        }

        if (value === 2) {
            return 'EvenPages';
        }

        if (value === 3) {
            return 'OddPages';
        }

        return 'Primary';
    }

    const normalized = String(value || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'firstpage') {
        return 'FirstPage';
    }

    if (normalized === 'evenpages') {
        return 'EvenPages';
    }

    if (normalized === 'oddpages') {
        return 'OddPages';
    }

    return 'Primary';
}

function createSelection(anchor, focus, object = null, extras = null) {
    const math = extras?.math || extras?.Math || null;
    return {
        anchor: clonePosition(anchor),
        focus: clonePosition(focus),
        ...(object ? { object: { ...object } } : {}),
        ...(math ? { math: normalizeMathSelection(math) } : {}),
    };
}

function normalizeSelection(selection) {
    return createSelection(selection?.anchor, selection?.focus, selection?.object || null, {
        math: selection?.math || selection?.Math || null,
    });
}

function clonePosition(position) {
    const cloned = {
        blockId: String(position?.blockId || ''),
        offset: Math.max(0, Number(position?.offset || 0) || 0),
    };
    // Preserve the lineId at a soft-wrap boundary so the rendered caret resolves to the intended visual line
    // (a boundary offset is shared by the previous line's end and the next line's start — B1). Carried by
    // clicks (hitTestPoint) and keyboard moves (moveCaretByKey Home/End/Up/Down).
    if (position?.lineId) {
        cloned.lineId = position.lineId;
    }

    return cloned;
}

function normalizeMathPath(value) {
    if (Array.isArray(value)) {
        return value.map(segment => numericOrString(segment));
    }

    if (typeof value === 'string') {
        const trimmed = value.trim();
        if (!trimmed) {
            return [];
        }

        try {
            const parsed = JSON.parse(trimmed);
            if (Array.isArray(parsed)) {
                return parsed.map(segment => numericOrString(segment));
            }
        } catch {
            return trimmed.split(/[./]/).filter(Boolean).map(segment => numericOrString(segment));
        }
    }

    return [];
}

function sameMathPath(left, right) {
    const a = normalizeMathPath(left);
    const b = normalizeMathPath(right);
    return a.length === b.length && a.every((segment, index) => segment === b[index]);
}

function numericOrString(value) {
    if (typeof value === 'number') {
        return Math.max(0, Math.trunc(value));
    }

    const text = String(value ?? '');
    return /^\d+$/.test(text) ? Number(text) : text;
}

function isCollapsed(selection) {
    return selection?.anchor?.blockId === selection?.focus?.blockId
        && Number(selection?.anchor?.offset || 0) === Number(selection?.focus?.offset || 0);
}

function normalizeMathSelection(math) {
    const slotPath = normalizeMathPath(math?.slotPath || math?.SlotPath || []);
    const offset = Math.max(0, Number(math?.offset ?? math?.Offset ?? 0) || 0);
    return {
        mathId: String(math?.mathId || math?.MathId || ''),
        runId: String(math?.runId || math?.RunId || ''),
        slotPath,
        slotName: String(math?.slotName || math?.SlotName || ''),
        offset,
        selectionAnchorOffset: Number.isFinite(Number(math?.selectionAnchorOffset ?? math?.SelectionAnchorOffset))
            ? Math.max(0, Number(math?.selectionAnchorOffset ?? math?.SelectionAnchorOffset))
            : offset,
        selectionFocusOffset: Number.isFinite(Number(math?.selectionFocusOffset ?? math?.SelectionFocusOffset))
            ? Math.max(0, Number(math?.selectionFocusOffset ?? math?.SelectionFocusOffset))
            : offset,
        selecting: math?.selecting === true || math?.Selecting === true,
    };
}

function boundingRectForSelectionRects(rects) {
    if (!Array.isArray(rects) || rects.length === 0) {
        return null;
    }

    const pageIndex = Number(rects[0]?.pageIndex || 0) || 0;
    const samePage = rects.filter(item => Number(item?.pageIndex || 0) === pageIndex && item?.rect);
    if (!samePage.length) {
        return null;
    }

    const left = Math.min(...samePage.map(item => Number(item.rect.x || 0) || 0));
    const top = Math.min(...samePage.map(item => Number(item.rect.y || 0) || 0));
    const right = Math.max(...samePage.map(item => (Number(item.rect.x || 0) || 0) + Math.max(1, Number(item.rect.width || 0) || 0)));
    const bottom = Math.max(...samePage.map(item => (Number(item.rect.y || 0) || 0) + Math.max(1, Number(item.rect.height || 0) || 0)));
    return {
        pageIndex,
        x: left,
        y: top,
        width: Math.max(1, right - left),
        height: Math.max(1, bottom - top),
    };
}

function isNavigationKey(key) {
    return key === 'ArrowLeft'
        || key === 'ArrowRight'
        || key === 'ArrowUp'
        || key === 'ArrowDown'
        || key === 'Home'
        || key === 'End'
        || key === 'PageUp'
        || key === 'PageDown';
}

function isObjectArrowKey(key) {
    return key === 'ArrowLeft'
        || key === 'ArrowRight'
        || key === 'ArrowUp'
        || key === 'ArrowDown';
}

function linesPerPage(layout) {
    const pages = new Map();
    for (const stop of collectCaretStops(layout)) {
        const key = Number(stop.pageIndex || 0) || 0;
        pages.set(key, (pages.get(key) || new Set()).add(stop.lineId));
    }

    const counts = Array.from(pages.values()).map(set => set.size);
    return counts.length ? Math.max(...counts) : 12;
}

function findDescendant(rootElement, predicate) {
    const children = Array.isArray(rootElement?.children) ? rootElement.children : Array.from(rootElement?.children || []);
    for (const child of children) {
        if (predicate(child)) {
            return child;
        }

        const nested = findDescendant(child, predicate);
        if (nested) {
            return nested;
        }
    }

    return null;
}

function findDescendants(rootElement, predicate, results = []) {
    const children = Array.isArray(rootElement?.children) ? rootElement.children : Array.from(rootElement?.children || []);
    for (const child of children) {
        if (predicate(child)) {
            results.push(child);
        }

        findDescendants(child, predicate, results);
    }

    return results;
}

function ensureCaretStyle(doc) {
    if (!doc?.head || typeof doc.getElementById !== 'function' || doc.getElementById(CARET_STYLE_ID)) {
        return;
    }

    const style = doc.createElement('style');
    style.id = CARET_STYLE_ID;
    style.textContent = '@keyframes tm-document-canvas-caret-blink{0%,49%{opacity:1}50%,100%{opacity:0}}'
        + '@media (forced-colors:active){.tm-document-canvas-caret{background:CanvasText!important;forced-color-adjust:none}'
        + '.tm-document-canvas-selection-rect{background:Highlight!important;forced-color-adjust:none}}';
    doc.head.appendChild(style);
}
