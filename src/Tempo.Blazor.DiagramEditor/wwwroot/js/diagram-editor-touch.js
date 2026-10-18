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
//   2. contextMenu   600 ms hold on a node, an edge or the empty canvas opens the context menu
//                    (a press ring grows under the finger after ~150 ms, vibrate on open; the
//                    release-click guard swallows the compatibility click on the menu)
//  3a. portDrag      finger/pen drag from a port or connection point draws an edge (tablet+, R2)
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

// The helpers resolve from this module's own URL in the deployed _content layout
// (_content/Tempo.Blazor.DiagramEditor/js/ → ../../Tempo.Blazor/js/). Tests inject the pair.
const HELPER_BASE = '../../Tempo.Blazor/js/';

async function loadHelpers() {
    const drag = await import(new URL(HELPER_BASE + 'pointer-drag.js', import.meta.url).href);
    const gesture = await import(new URL(HELPER_BASE + 'pointer-gesture.js', import.meta.url).href);
    return { drag, gesture };
}

/**
 * Attaches the touch/pen gesture sessions to one canvas instance.
 *
 * @param {object} engine `window.tmDiagramEditor` (the touch* hooks and geometry internals).
 * @param {object} inst the engine instance for one canvas.
 * @param {{ drag: object, gesture: object }} [helpers] the core helper modules; omitted in the
 *   browser (loaded from the deployed layout), injected by the Node tests.
 * @returns {Promise<(function(): void & { cancelEditing: (reason?: string) => void, cancel: (reason?: string) => void })>}
 *   a detach that cancels a gesture in progress and ends every session — safe to call twice, safe
 *   before the helpers finished loading. `detach.cancelEditing(reason)` rolls back every EDITING
 *   gesture in flight (node drag, long-press, resize, rotate) while navigation keeps running — the
 *   read-only flip hook; `detach.cancel(reason)` cancels every session once.
 */
export async function attachTouch(engine, inst, helpers = null) {
    const { drag, gesture } = helpers ?? await loadHelpers();

    const sessions = [];

    // touchPortAt runs the hit-stack walk for EVERY session veto on the same press (nodeDrag,
    // contextMenu, panDrag, resizeDrag, rotateDrag): memoize the resolution per event object.
    const portAtMemo = new WeakMap();
    const portAt = (event) => {
        if (portAtMemo.has(event)) return portAtMemo.get(event);
        const resolved = engine.touchPortAt(inst, event);
        portAtMemo.set(event, resolved);
        return resolved;
    };

    // (1) Node drag: the press selects the node (like the old touchstart), the move drags the
    // whole selection — children and groups included, through the engine's own drag state.
    const nodeDrag = drag.attach(inst.container, {
        selector: NODE_SELECTOR,
        pointerTypes: TOUCH_POINTERS,
        threshold: 4,
        touchAction: null,          // CSS owns touch-action on the canvas
        onPress: (info) => {
            if (info.event?.target?.closest?.(OTHER_GESTURE_SELECTOR)) return false;
            if (portAt(info.event)) return false;
            return engine.touchPressNode(inst, info.target, info.startX, info.startY);
        },
        onStart: (info) => engine.touchBeginNodeDrag(inst, info.target, info.startX, info.startY),
        onMove: (info) => engine.touchMoveNodeDrag(inst, info.x, info.y),
        onEnd: (info) => engine.touchEndNodeDrag(inst, info.x, info.y),
        onCancel: () => engine.touchCancelNodeDrag(inst),
    });
    sessions.push(nodeDrag);

    // (2) Long-press context menu on a node, an edge or the empty canvas (R4). Touch only: a pen
    // keeps the native press-and-hold contextmenu. The hold rolls every armed gesture back.
    let menuTarget = null;
    // The long-press opens the menu AT the finger, so the compatibility click of the lifting
    // finger lands on a menu item the user never pressed — it would run that command and close
    // the menu instantly. Release-click guard (review fix): ARMED per hold — set when the menu
    // opens, disarmed on the first swallowed click or on ANY new pointerdown — and, once the
    // finger lifts, a 350 ms window swallows the compatibility click itself. Scoped to THIS
    // editor's menu: a click on another editor's menu is that editor's business.
    let menuClickArmed = false;
    let menuClickSuppressUntil = 0;
    let menuOpened = false;   // only the hold that ACTUALLY opened a menu guards its release click
    const onMenuClickGuard = (e) => {
        if (!menuClickArmed && Date.now() >= menuClickSuppressUntil) return;
        const menu = e.target?.closest?.('.tm-diagram-editor__context-menu');
        if (!menu) return;
        // Scoped to THIS editor: the click counts only when the menu lives in our editor root.
        const editorRoot = inst.container.closest?.('.tm-diagram-editor') ?? null;
        if (editorRoot && menu.closest('.tm-diagram-editor') !== editorRoot) return;
        e.preventDefault?.();
        e.stopPropagation?.();
        menuClickArmed = false;   // the first swallowed click disarms the hold
    };
    // ANY new press cancels the release-click guard entirely: a real press means the user is
    // interacting with the surface (or another control), so neither the armed flag nor the
    // lift-click suppression window may swallow the next click (review fix).
    const onAnyPointerDown = () => { menuClickArmed = false; menuClickSuppressUntil = 0; };
    if (typeof document !== 'undefined') {
        document.addEventListener('click', onMenuClickGuard, true);
        document.addEventListener('pointerdown', onAnyPointerDown, true);
    }

    // Long-press feedback (review): after ~150 ms of hold a press ring grows under the finger for
    // the remaining hold time; it is removed on every exit (open/release/cancel/move-abort) and
    // stays a static ring under prefers-reduced-motion (CSS).
    const PRESS_RING_DELAY = 150;
    let pressRing = null;
    let pressRingTimer = 0;
    const removePressRing = () => {
        if (pressRingTimer) { clearTimeout(pressRingTimer); pressRingTimer = 0; }
        pressRing?.remove?.();
        pressRing = null;
    };
    const showPressRing = (clientX, clientY) => {
        if (typeof document === 'undefined' || !document.createElement) return;
        pressRing = document.createElement('div');
        pressRing.className = 'tm-diagram-press-ring';
        pressRing.style.left = clientX + 'px';
        pressRing.style.top = clientY + 'px';
        (document.body ?? inst.container).appendChild?.(pressRing);
    };

    const contextMenu = drag.attach(inst.container, {
        pointerTypes: ['touch'],
        threshold: 0,
        longPressMs: MENU_LONG_PRESS_MS,
        touchAction: null,
        onPress: (info) => {
            const target = info.event?.target;
            if (target?.closest?.(OTHER_GESTURE_SELECTOR)) return false;
            if (portAt(info.event)) return false;
            menuTarget = engine.touchContextMenuTarget(inst, target);
            if (menuTarget === null) return false;
            pressRingTimer = setTimeout(() => showPressRing(info.startX, info.startY), PRESS_RING_DELAY);
            return true;
        },
        onStart: (info) => {
            // Hold completed: the menu opens while the finger is still down, and a drag that
            // already armed rolls back exactly once (a hold that never started a drag is a no-op).
            cancelSessions('long-press-menu', [contextMenu]);
            engine.touchCancelNodeDrag(inst);
            engine.touchCancelPan(inst);
            removePressRing();
            if (menuTarget) {
                // The origin (pointerType) rides to C#, which mirrors it as data-anchor="touch"
                // so the menu parks offset from the finger instead of under it.
                engine.touchOpenContextMenu(inst, menuTarget, info.x, info.y, info.event?.pointerType);
                menuClickArmed = true;
                menuOpened = true;
                try { globalThis.navigator?.vibrate?.(10); } catch (_e) { /* haptics unsupported */ }
            }
            return false;   // the long-press never becomes a drag
        },
        onRelease: () => {
            menuTarget = null;
            removePressRing();
            // The lift: this hold's compatibility click is about to fire — swallow it.
            menuClickArmed = false;
            if (menuOpened) menuClickSuppressUntil = Date.now() + 350;
            menuOpened = false;
        },
    });
    sessions.push(contextMenu);

    // (3a) Edge drawing from a port / connection point (tablet+, touch and pen, R2).
    let portEl = null;
    const portDrag = drag.attach(inst.container, {
        pointerTypes: TOUCH_POINTERS,
        threshold: 4,
        touchAction: null,
        onPress: (info) => { portEl = engine.touchPortAt(inst, info.event); return portEl !== null; },
        onStart: (info) => engine.touchBeginEdgeDraw(inst, portEl, info.startX, info.startY),
        onMove: (info) => engine.touchMoveEdgeDraw(inst, info.x, info.y),
        onEnd: (info) => engine.touchEndEdgeDraw(inst, info.x, info.y),
        onCancel: () => engine.touchCancelEdgeDraw(inst),
        onRelease: () => { portEl = null; },
    });
    sessions.push(portDrag);

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
            if (portAt(info.event)) return false;
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
        onPress: (info) => {
            if (inst.readOnly) return false;
            // A port hit circle paints over the handle on a coarse pointer: the port drag owns
            // the press (only ONE session may claim a pointer).
            if (portAt(info.event)) return false;
            return true;
        },
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
        onPress: (info) => {
            if (inst.readOnly) return false;
            if (portAt(info.event)) return false;   // the port drag owns the press, not the rotate
            return true;
        },
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
            engine.touchCancelEdgeDraw(inst);
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

    // Touch contact tracker for _isTouchContextMenu (diagram-editor.js): the native long-press
    // contextmenu of Android / Windows touch must not open the menu a second time. A SET of the
    // touch pointerIds currently down: a lift is only ever counted for the finger it belonged
    // to, whatever element it happened over (the lift listener lives on the DOCUMENT — a finger
    // captured by a drag session lifts off the element). A mouse/press pointer down clears the
    // set: no touch hold can be running while the mouse is pressing.
    const touchContacts = new Set();
    inst.__touchContacts = touchContacts;
    inst.__lastTouchUpAt = 0;
    const onContactDown = (e) => {
        if (e.pointerType === 'touch') touchContacts.add(e.pointerId);
        else touchContacts.clear();
    };
    const onContactUp = (e) => {
        if (e.pointerType !== 'touch') return;
        touchContacts.delete(e.pointerId);
        inst.__lastTouchUpAt = Date.now();
    };
    const onContactLost = (e) => { touchContacts.delete(e.pointerId); };
    inst.container.addEventListener('pointerdown', onContactDown, true);
    if (typeof document !== 'undefined') {
        document.addEventListener('pointerup', onContactUp, true);
        document.addEventListener('pointercancel', onContactUp, true);
        document.addEventListener('lostpointercapture', onContactLost, true);
    }

    let detached = false;
    function detach() {
        if (detached) return;
        detached = true;
        removePressRing();
        for (const session of sessions) session.detach?.();
        inst.container.removeEventListener('pointerdown', onContactDown, true);
        if (typeof document !== 'undefined') {
            document.removeEventListener('pointerup', onContactUp, true);
            document.removeEventListener('pointercancel', onContactUp, true);
            document.removeEventListener('lostpointercapture', onContactLost, true);
            document.removeEventListener('click', onMenuClickGuard, true);
            document.removeEventListener('pointerdown', onAnyPointerDown, true);
        }
    }
    // The EDITING sessions a read-only flip must roll back. A new editing session
    // MUST be added here — navigation sessions (pan, pinch) stay live.
    const editingSessions = [nodeDrag, contextMenu, portDrag, resizeDrag, rotateDrag];
    // Read-only flip: roll back every EDITING gesture in flight (their onCancel callbacks restore
    // the engine/C# state); navigation (pan, pinch) keeps running.
    detach.cancelEditing = (reason = 'read-only') => {
        for (const session of editingSessions) session.cancel?.(reason);
    };
    detach.cancel = (reason = 'cancelled') => cancelSessions(reason, []);
    return detach;
}
