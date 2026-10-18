// ═══════════════════════════════════════════════════════════════════════════════
// Tempo Diagram Editor — touch/pen gesture stack (ES module, phase 6).
//
// Every touch gesture is a session of the SHARED core helpers — pointer-drag.js for the
// single-pointer gestures and pointer-gesture.js for the two-finger pinch/pan. This module
// owns only the glue: which element a session delegates from, the read-only and veto rules,
// and the hand-over from a single-finger gesture to a pinch. The engine maths live in
// diagram-editor.js (touchPressNode / touchBeginNodeDrag / touchBeginRotate / …), where the
// mouse path shares them. A second pinch or a second long-press anywhere in this package is
// forbidden — decision F7-POINTER-DRAG-PLURALS-RELATIVE-TIME, docs/touch-interaction.md.
//
//   diagram-editor.js (classic script):  inst.__touchDetach = await module.attachTouch(engine, inst)
//   destroy():                           inst.__touchDetach() — cancels a gesture in progress
//
// Sessions (in attach order):
//   1. nodeDrag      finger drag on a node — the press selects, the move drags
//   2. nodeMenu      600 ms hold on a node (longPressMs) opens the context menu and rolls
//                    the armed drag back; a finger that moves first never opens it
//   3. panDrag       drag from the empty surface pans; a tap on an edge selects the edge
//   4. resizeDrag    zero-threshold drag on a resize hit rect → OnResizeStarted/Moved/Ended
//   5. rotateDrag    zero-threshold drag on the rotate hit circle → the engine rotation session
//   6. pinch         two-finger pinch/pan; onStart cancels the single-finger sessions
// ═══════════════════════════════════════════════════════════════════════════════

const TOUCH_POINTERS = ['touch', 'pen'];
const NODE_SELECTOR = '[data-node-id]';
const RESIZE_SELECTOR = '.tm-diagram-resize-handle-hit, .tm-diagram-resize-handle';
const ROTATE_SELECTOR = '.tm-diagram-rotate-handle-hit, .tm-diagram-rotate-handle';
const MENU_LONG_PRESS_MS = 600;
const RESIZE_DIRECTIONS = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];

// Presses that belong to another gesture or to a control: never a node drag, never a pan,
// and never a long-press menu. Letting these through un-vetoed would both start a bogus drag
// and preventDefault the pointerdown, killing the control's own click.
const OTHER_GESTURE_SELECTOR = [
    RESIZE_SELECTOR,
    ROTATE_SELECTOR,
    '.tm-diagram-connect-arrow',
    '.tm-diagram-port',
    '.tm-diagram-node__collapse-btn',
    '.tm-diagram-node__inline-input',
    '.tm-diagram-node__inline-textarea',
    '.tm-diagram-edge-label-input',
    '.tm-diagram-edge-toolbar',
].join(', ');

// The helpers resolve from this module's own URL: the deployed _content layout first (two
// levels up out of the package base), then the source tree for the Node tests (wwwroot is
// part of the path there). Production always takes the first import.
async function loadHelpers() {
    const bases = ['../../Tempo.Blazor/js/', '../../../../src/Tempo.Blazor/wwwroot/js/'];
    let lastError = null;
    for (const base of bases) {
        try {
            const drag = await import(new URL(base + 'pointer-drag.js', import.meta.url).href);
            const gesture = await import(new URL(base + 'pointer-gesture.js', import.meta.url).href);
            return { drag, gesture };
        } catch (err) {
            lastError = err;
        }
    }
    throw lastError ?? new Error('pointer helpers not found');
}

/**
 * Attaches the touch/pen gesture sessions to one canvas instance.
 *
 * @param {object} engine `window.tmDiagramEditor` (the touch* hooks and geometry internals).
 * @param {object} inst the engine instance for one canvas.
 * @returns {Promise<(function(): void & { cancelEditing: (reason?: string) => void, cancel: (reason?: string) => void })>}
 *   a detach that cancels a gesture in progress and ends every session — safe to call twice, safe
 *   before the helpers finished loading. `detach.cancelEditing(reason)` rolls back every EDITING
 *   gesture in flight (node drag, long-press, resize, rotate) while navigation keeps running — the
 *   read-only flip hook; `detach.cancel(reason)` cancels every session once.
 */
export async function attachTouch(engine, inst) {
    const { drag, gesture } = await loadHelpers();

    const sessions = [];

    // (1) Node drag: the press selects the node (like the old touchstart), the move drags the
    // whole selection — children and groups included, through the engine's own drag state.
    const nodeDrag = drag.attach(inst.container, {
        selector: NODE_SELECTOR,
        pointerTypes: TOUCH_POINTERS,
        threshold: 4,
        touchAction: null,          // CSS owns touch-action on the canvas
        onPress: (info) => {
            if (info.event?.target?.closest?.(OTHER_GESTURE_SELECTOR)) return false;
            return engine.touchPressNode(inst, info.target, info.startX, info.startY);
        },
        onStart: (info) => engine.touchBeginNodeDrag(inst, info.target, info.startX, info.startY),
        onMove: (info) => engine.touchMoveNodeDrag(inst, info.x, info.y),
        onEnd: (info) => engine.touchEndNodeDrag(inst, info.x, info.y),
        onCancel: () => engine.touchCancelNodeDrag(inst),
    });
    sessions.push(nodeDrag);

    // (2) Long-press menu: the helper's longPressMs replaces the hand-rolled 600ms timer. The
    // hold opens the node context menu and rolls the armed drag back; a moving finger aborts
    // the hold, so a drag never turns into a menu.
    const nodeMenu = drag.attach(inst.container, {
        selector: NODE_SELECTOR,
        pointerTypes: ['touch'],
        threshold: 0,
        longPressMs: MENU_LONG_PRESS_MS,
        touchAction: null,
        onPress: (info) => !inst.readOnly
            && !info.event?.target?.closest?.(OTHER_GESTURE_SELECTOR),
        onStart: (info) => {
            // Hold completed: the menu opens while the finger is still down, and a drag that
            // already armed rolls back exactly once (a hold that never started a drag is a no-op).
            cancelSessions('long-press-menu', [nodeMenu]);
            engine.touchCancelNodeDrag(inst);
            engine.touchCancelPan(inst);
            const id = info.target?.getAttribute?.('data-node-id');
            if (id) {
                inst.dotNetRef.invokeMethodAsync('OnNodeContextMenu', id, info.x, info.y);
            }
            return false;   // the long-press never becomes a drag
        },
    });
    sessions.push(nodeMenu);

    // (3) Pan: a drag from the empty surface pans the canvas (navigation works read-only too);
    // a tap on an edge hit path selects the edge so a tablet can edit it. A press on the empty
    // surface clears the selection — the touch parity of the mouse rubber-band start (which
    // clears at mousedown), so a phone can deselect without a keyboard (G2 mobile light edits).
    // In read-only a finger on a node or an edge pans too, and a tap selects the node for
    // inspection (R1).
    let roPressTarget = null;   // read-only: the press target a tap inspects
    const panDrag = drag.attach(inst.container, {
        pointerTypes: TOUCH_POINTERS,
        threshold: 6,
        touchAction: null,
        onPress: (info) => {
            const target = info.event?.target;
            if (target?.closest?.(OTHER_GESTURE_SELECTOR)) return false;
            if (inst.readOnly) {
                // R1: every surface navigates in read-only; a TAP (onRelease) inspects.
                roPressTarget = target ?? null;
                return true;
            }
            if (target?.closest?.(NODE_SELECTOR)) return false;
            const edgeEl = target?.closest?.('.tm-diagram-edge-hit-path, .tm-diagram-edge-label-group');
            if (edgeEl) {
                const idEl = target.closest('[data-edge-id]') ?? edgeEl;
                engine.touchPressEdge(inst, idEl);   // selects (editable mode); never vetoes the pan
                return true;
            }
            if (inst.selectedIds.size > 0) {
                inst.selectedIds.clear();
                engine._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
            }
            return true;
        },
        onRelease: (info) => {
            const target = roPressTarget;
            roPressTarget = null;
            if (!inst.readOnly || info.outcome !== 'tap' || !target) return;
            // R1: the touch layer preventDefaults the press, so the Blazor @onclick never
            // follows a tap — edges select through the engine hook, nodes likewise.
            const edgeEl = target.closest?.('.tm-diagram-edge-hit-path, .tm-diagram-edge-label-group');
            if (edgeEl) {
                engine.selectEdgeForInspection(inst, edgeEl);
                return;
            }
            const nodeEl = target.closest?.(NODE_SELECTOR);
            if (nodeEl) engine.selectNodeForInspection(inst, nodeEl, false);
            else engine.clearSelectionForInspection(inst);
        },
        onStart: (info) => engine.touchBeginPan(inst, info.startX, info.startY),
        onMove: (info) => engine.touchMovePan(inst, info.x, info.y),
        onEnd: () => engine.touchEndPan(inst),
        onCancel: () => engine.touchCancelPan(inst),
    });
    sessions.push(panDrag);

    // (4) Resize: a zero-threshold drag on a handle hit rect. The maths stay in C#
    // (OnResizeStarted / OnResizeMoved / OnResizeEnded / OnResizeCancelled) so the mouse and
    // the finger share one resize implementation and one undo step.
    const resizeDrag = drag.attach(inst.container, {
        selector: RESIZE_SELECTOR,
        pointerTypes: TOUCH_POINTERS,
        threshold: 0,
        touchAction: null,
        onPress: () => !inst.readOnly,
        onStart: (info) => {
            const nodeId = info.target?.closest?.('g.tm-diagram-node-handles')?.getAttribute?.('data-node-id');
            const handle = info.target?.getAttribute?.('data-resize-handle')
                ?? RESIZE_DIRECTIONS.find(d => info.target?.classList?.contains('tm-diagram-resize-handle--' + d));
            if (!nodeId || !handle) return false;
            inst.dotNetRef.invokeMethodAsync('OnResizeStarted', nodeId, handle, info.startX, info.startY);
        },
        onMove: (info) => inst.dotNetRef.invokeMethodAsync('OnResizeMoved', info.x, info.y),
        onEnd: () => inst.dotNetRef.invokeMethodAsync('OnResizeEnded'),
        onCancel: () => inst.dotNetRef.invokeMethodAsync('OnResizeCancelled'),
    });
    sessions.push(resizeDrag);

    // (5) Rotate: the engine rotation session (the same maths startRotate drives with the mouse).
    const rotateDrag = drag.attach(inst.container, {
        selector: ROTATE_SELECTOR,
        pointerTypes: TOUCH_POINTERS,
        threshold: 0,
        touchAction: null,
        onPress: () => !inst.readOnly,
        onStart: (info) => {
            const nodeId = info.target?.closest?.('g.tm-diagram-node-handles')?.getAttribute?.('data-node-id');
            if (!nodeId) return false;
            engine.touchBeginRotate(inst, nodeId, info.startX, info.startY);
        },
        onMove: (info) => engine.touchMoveRotate(inst, info.x, info.y),
        onEnd: () => engine.touchEndRotate(inst),
        onCancel: () => engine.touchCancelRotate(inst),
    });
    sessions.push(rotateDrag);

    // (6) Pinch/pan: two fingers zoom around the gesture centre and pan; ctrl+wheel stays with
    // the engine's own wheel handler, so the helper's wheel is off. The pinch cancels the
    // single-finger sessions from onStart — the host hand-over from docs/touch-interaction.md.
    let pinchStartVb = null;
    const pinch = gesture.attachPinch(inst.container, {
        pointerTypes: ['touch'],
        wheel: false,
        touchAction: null,
        onStart: () => {
            cancelSessions('pinch', [pinch]);
            engine.touchCancelNodeDrag(inst);
            engine.touchCancelPan(inst);
            pinchStartVb = engine.touchPinchBegin(inst);
        },
        onChange: (info) => {
            engine.touchPinchChange(inst, pinchStartVb, info.scale, info.centerX, info.centerY, info.dx, info.dy);
        },
        onEnd: (info) => {
            engine.touchPinchEnd(inst, pinchStartVb, info.scale, info.dx, info.dy);
            pinchStartVb = null;
        },
        onCancel: () => {
            engine.touchPinchCancel(inst, pinchStartVb);
            pinchStartVb = null;
        },
    });
    sessions.push(pinch);

    function cancelSessions(reason, skip) {
        for (const session of sessions) {
            if (skip.includes(session)) continue;
            session.cancel?.(reason);
        }
    }

    let detached = false;
    function detach() {
        if (detached) return;
        detached = true;
        for (const session of sessions) session.detach?.();
    }
    // The EDITING sessions a read-only flip must roll back. A new editing session
    // (portDrag, Phase 3) MUST be added here — navigation sessions (pan, pinch) stay live.
    const editingSessions = [nodeDrag, nodeMenu, resizeDrag, rotateDrag];
    // Read-only flip: roll back every EDITING gesture in flight (their onCancel callbacks restore
    // the engine/C# state); navigation (pan, pinch) keeps running.
    detach.cancelEditing = (reason = 'read-only') => {
        for (const session of editingSessions) session.cancel?.(reason);
    };
    detach.cancel = (reason = 'cancelled') => cancelSessions(reason, []);
    return detach;
}
