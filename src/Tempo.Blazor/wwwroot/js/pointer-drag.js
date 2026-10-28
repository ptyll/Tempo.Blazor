// Tempo.Blazor pointer-drag helper (ES module) — the ONE shared pointer gesture for core and the
// feature packages. Components must not write their own mouse/touch drag; they attach this.
//
//   import { attach } from './pointer-drag.js';          // from another file in this folder
//   // a split-package module: import { attach } from '../../Tempo.Blazor/js/pointer-drag.js';
//   const drag = attach(handle, { threshold: 4, onStart, onMove, onEnd, onCancel });
//   drag.detach();            // on dispose — safe to call twice, cancels a drag in progress
//
// What it owns (so a component does not have to):
//   * Pointer Events with pointer capture BY pointerId (releasePointerCapture() without the id throws
//     in a real DOM and leaks the capture); only the first pointer drives a gesture, a second finger
//     can neither move nor end it;
//   * a movement threshold (a tap stays a tap) and, for touch, an optional long-press that starts the
//     drag without breaking native scrolling before it fires;
//   * every cancel path: Escape, pointercancel, lostpointercapture, a missed mouse-up, detach();
//   * touch-action guidance (set 'none' for an immediate drag handle, left alone for long-press and
//     delegated drags — put touch-action: none on the handle itself in CSS, see .tm-touch-drag-handle);
//   * the click that follows a real drag is swallowed once; a tap keeps its click;
//   * reduced motion is reported to every callback so the host can skip animation.
//
// A drag is never the only way to do something: every drag needs a keyboard alternative
// (docs/touch-interaction.md, WCAG 2.5.7). This module deliberately knows nothing about the DOM
// shape of its host — no component-specific logic belongs here.

const DEFAULT_THRESHOLD = 4;
const DEFAULT_LONG_PRESS_TOLERANCE = 8;
const DEFAULT_DRAG_CLASS = 'tm-pointer-dragging';

/**
 * Whether the user asked the platform to reduce motion. False where it cannot be determined
 * (no matchMedia: SSR, Node) — never throws.
 * @returns {boolean}
 */
export function prefersReducedMotion() {
    try {
        return globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches === true;
    }
    catch {
        return false;
    }
}

const ARROW_STEP = Object.freeze({
    ArrowLeft: { x: -1, y: 0 },
    ArrowRight: { x: 1, y: 0 },
    ArrowUp: { x: 0, y: -1 },
    ArrowDown: { x: 0, y: 1 },
});

/**
 * The keyboard alternative to a drag (WCAG 2.5.7). Cancels ONLY the Alt+Arrow chord on the focused
 * item and reports the step; a plain arrow and every other key (Tab included) keep their browser
 * behaviour. Alt+ArrowLeft/Right is the browser Back/Forward shortcut, so the cancel has to happen
 * here, in the keydown listener — a Blazor `@onkeydown:preventDefault` would swallow Tab as well.
 * A canvas editor that already nudges with plain arrows may keep doing so and not call this.
 * Pass `shift: true` to also accept Alt+Shift+Arrow (the resize alternative, e.g. a Scheduler
 * event): the step then reports `shift: true`. Without the option Alt+Shift+Arrow stays ignored
 * and unprevented, exactly like every non-arrow chord.
 * Pass `accept(event)` to veto the chord for keys another control owns (an inline editor's
 * arrows): it runs before `preventDefault`, so a `false` return leaves the key exactly as it
 * was — not prevented, not reported.
 *
 * @param {Element|null} element the focused item (or its container, with `selector`)
 * @param {{ onStep?: (step: { key: string, x: number, y: number, shift: boolean, event: Event }) => void,
 *           selector?: string, shift?: boolean, accept?: (event: Event) => boolean }} [options]
 * @returns {{ detach: () => void }}
 */
export function attachKeyboardMove(element, options = {}) {
    if (!element || typeof element.addEventListener !== 'function') return { detach() {} };

    const selector = typeof options.selector === 'string' && options.selector ? options.selector : null;
    const allowShift = options.shift === true;
    const accept = typeof options.accept === 'function' ? options.accept : null;
    const onKeyDown = (event) => {
        if (!event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.shiftKey && !allowShift) return;
        const step = ARROW_STEP[event.key];
        if (!step) return;
        if (selector) {
            const target = event.target?.closest?.(selector) ?? null;
            if (!target || !element.contains(target)) return;
        }
        if (accept && !accept(event)) return;
        event.preventDefault?.();
        options.onStep?.({ key: event.key, x: step.x, y: step.y, shift: event.shiftKey === true, event });
    };

    element.addEventListener('keydown', onKeyDown);
    let detached = false;
    return {
        detach() {
            if (detached) return;
            detached = true;
            element.removeEventListener('keydown', onKeyDown);
        },
    };
}

const INERT = Object.freeze({
    detach() {},
    cancel() {},
    get dragging() { return false; },
});

function toNumber(value, fallback) {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : fallback;
}

/**
 * Attaches a pointer drag to an element.
 *
 * The callbacks receive one info object: `{ event, pointerId, pointerType, target, startX, startY,
 * x, y, dx, dy, origin, reducedMotion }`. `dx`/`dy` are measured from the press point. `onPress`
 * and `onStart` may return `false` to veto the gesture (the capture is released; nothing else runs).
 * `onRelease({ outcome, started, ... })` runs exactly once per press that was not vetoed, with the
 * outcome 'tap' | 'end' | 'cancel' | 'aborted' — the place for cleanup that must happen on every path.
 *
 * @param {Element|null} element the element listened on (the container when `selector` is given)
 * @param {object} [options]
 * @param {number} [options.threshold=4] pixels of movement before a mouse/pen/touch drag starts
 * @param {number} [options.longPressMs=0] touch only: hold this long to start the drag; 0 disables
 * @param {number} [options.longPressTolerance=8] pixels the finger may drift while holding
 * @param {string} [options.selector] delegate: start only from descendants matching this selector
 * @param {string[]} [options.pointerTypes] restrict which pointer types may start a gesture
 * @param {'press'|'start'} [options.captureOn='press'] when to take pointer capture
 * @param {boolean} [options.cancelOnEscape=true] cancel an active gesture on Escape
 * @param {boolean} [options.suppressClick=true] swallow the click that follows a real drag
 * @param {string|null} [options.dragClass='tm-pointer-dragging'] class on the target while dragging
 * @param {string|null} [options.touchAction] explicit touch-action for the element; null = do not touch
 * @returns {{ detach: () => void, cancel: (reason?: string) => void, readonly dragging: boolean }}
 */
export function attach(element, options = {}) {
    if (!element || typeof element.addEventListener !== 'function') return INERT;

    const threshold = toNumber(options.threshold, DEFAULT_THRESHOLD);
    const longPressMs = toNumber(options.longPressMs, 0);
    const tolerance = toNumber(options.longPressTolerance, DEFAULT_LONG_PRESS_TOLERANCE);
    const selector = typeof options.selector === 'string' && options.selector ? options.selector : null;
    const pointerTypes = Array.isArray(options.pointerTypes) ? options.pointerTypes : null;
    const captureOnStart = options.captureOn === 'start';
    const cancelOnEscape = options.cancelOnEscape !== false;
    const suppressClick = options.suppressClick !== false;
    const dragClass = options.dragClass === undefined ? DEFAULT_DRAG_CLASS : options.dragClass;
    const callbacks = {
        press: options.onPress, start: options.onStart, move: options.onMove,
        end: options.onEnd, cancel: options.onCancel, release: options.onRelease,
    };

    // touch-action: an immediate drag on the element itself must stop the browser panning; a
    // long-press or delegated drag must keep scrolling, so the element is left alone.
    let touchActionBefore;
    let touchActionChanged = false;
    const wantedTouchAction = options.touchAction !== undefined
        ? options.touchAction
        : (longPressMs > 0 || selector ? null : 'none');
    if (wantedTouchAction !== null && element.style) {
        touchActionBefore = element.style.touchAction;
        element.style.touchAction = wantedTouchAction;
        touchActionChanged = true;
    }

    /** @type {null|object} */
    let session = null;
    let suppressor = null;
    let detached = false;
    let touchMoveBound = false;

    const info = (event, extra = {}) => ({
        event,
        pointerId: session?.pointerId,
        pointerType: session?.pointerType,
        target: session?.target,
        startX: session?.startX,
        startY: session?.startY,
        x: session?.x,
        y: session?.y,
        dx: session ? session.x - session.startX : 0,
        dy: session ? session.y - session.startY : 0,
        origin: session?.origin,
        reducedMotion: prefersReducedMotion(),
        ...extra,
    });

    const call = (name, payload) => {
        const callback = callbacks[name];
        return typeof callback === 'function' ? callback(payload) : undefined;
    };

    const removeDocumentListeners = () => {
        if (typeof document === 'undefined') return;
        document.removeEventListener?.('keydown', onKeyDown, true);
    };

    /**
     * Ends the active gesture. State and DOM are cleaned up BEFORE any host callback runs, so a
     * throwing callback can never leave the helper holding a capture or a document listener.
     */
    function releaseTouchMove() {
        if (!touchMoveBound || longPressMs === 0) return;
        element.removeEventListener('touchmove', onTouchMove);
        touchMoveBound = false;
    }

    function finish(outcome, event, reason) {
        const s = session;
        if (!s) return;
        const payload = info(event, { reason, outcome, started: s.started });
        session = null;

        if (s.timer !== null) clearTimeout(s.timer);
        removeDocumentListeners();
        if (s.captured) {
            // The pointer id is required: releasePointerCapture() with no argument throws in a real DOM.
            try { s.target.releasePointerCapture?.(s.pointerId); } catch { /* already released */ }
        }
        if (s.started && dragClass) s.target.classList?.remove(dragClass);
        releaseTouchMove();
        if (outcome === 'end' && s.started && suppressClick) armClickSuppression();

        try {
            if (outcome === 'end') call('end', payload);
            else if (outcome === 'cancel') call('cancel', payload);
        }
        finally {
            call('release', payload);
        }
    }

    function armClickSuppression() {
        disarmClickSuppression();
        const swallow = (clickEvent) => {
            clickEvent.preventDefault?.();
            clickEvent.stopPropagation?.();
            disarmClickSuppression();
        };
        const timer = setTimeout(disarmClickSuppression, 0);
        suppressor = { swallow, timer };
        element.addEventListener('click', swallow, true);
    }

    function disarmClickSuppression() {
        if (!suppressor) return;
        const { swallow, timer } = suppressor;
        suppressor = null;
        clearTimeout(timer);
        element.removeEventListener('click', swallow, true);
    }

    function begin(origin, event) {
        const s = session;
        if (!s || s.started) return;
        s.started = true;
        s.origin = origin;
        if ((captureOnStart || s.longPress) && !s.captured) capture(s);
        if (dragClass) s.target.classList?.add(dragClass);
        // A permanent non-passive touchmove on a delegated element (document, for the scheduler) makes
        // Chrome wait for the listener on every scroll frame. Bind it only once a long-press drag has
        // started; an immediate drag keeps it for the whole attachment, below.
        if (longPressMs > 0 && !touchMoveBound) {
            element.addEventListener('touchmove', onTouchMove, { passive: false });
            touchMoveBound = true;
        }
        if (call('start', info(event)) === false) {
            // Vetoed: undo the start without reporting a drag that never was.
            finish('aborted', event, 'vetoed');
        }
    }

    function capture(s) {
        try {
            s.target.setPointerCapture?.(s.pointerId);
            s.captured = true;
        }
        catch { /* the pointer is already gone */ }
    }

    function onKeyDown(event) {
        if (event.key === 'Escape' && session) {
            const started = session.started;
            cancelGesture('escape', event);
            // Only an ACTIVE drag consumes the Escape: a press that never became a drag leaves the
            // key for the dialog or sheet around it.
            if (started) event.stopPropagation?.();
        }
    }

    function onLostCapture(event) {
        if (!session || event.pointerId !== session.pointerId || !session.captured) return;
        cancelGesture('lostpointercapture', event);
    }

    function cancelGesture(reason, event) {
        if (!session) return;
        finish(session.started ? 'cancel' : 'aborted', event, reason);
    }

    function onPointerDown(event) {
        if (detached) return;
        // A second finger or a non-primary button can neither move nor end the gesture, so these
        // guards come before the superseded check: only a new primary pointer may cancel a stale one.
        if (event.button !== undefined && event.button !== 0) return;
        if (event.isPrimary === false) return;
        if (session) {
            // A pointer that vanished without pointerup/pointercancel left a session nothing can end.
            // A new primary pointer cancels it (never commits) and then starts its own gesture.
            if (event.pointerId === session.pointerId) return;
            cancelGesture('superseded', event);
        }
        if (pointerTypes && !pointerTypes.includes(event.pointerType)) return;

        let target = element;
        if (selector) {
            target = event.target?.closest?.(selector) ?? null;
            if (!target || !element.contains(target)) return;
        }

        const pointerType = event.pointerType ?? 'mouse';
        const longPress = longPressMs > 0 && pointerType === 'touch';
        session = {
            pointerId: event.pointerId, pointerType, target,
            startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY,
            started: false, captured: false, timer: null, origin: undefined, longPress,
        };

        if (call('press', info(event)) === false) {
            session = null;
            return;
        }
        // The callback may have detached or cancelled; re-check before touching the DOM.
        if (!session) return;

        // A pending long-press must not capture: setPointerCapture suppresses the scroll the delay
        // exists to allow. Capture happens in begin(), once the hold completes.
        if (!captureOnStart && !longPress) capture(session);
        // A long-press candidate keeps native scroll/selection decisions; everything else is a drag
        // candidate and must not start a text selection.
        if (!longPress) event.preventDefault?.();

        if (typeof document !== 'undefined') {
            // Capture phase: a dialog listening on its own element, or a document listener registered
            // earlier, would otherwise see the Escape first and close before the drag rolls back.
            if (cancelOnEscape) document.addEventListener?.('keydown', onKeyDown, true);
        }
        if (longPress) {
            const s = session;
            s.timer = setTimeout(() => {
                if (session === s) {
                    s.timer = null;
                    begin('long-press', event);
                }
            }, longPressMs);
        }
    }

    function onPointerMove(event) {
        const s = session;
        if (!s || event.pointerId !== s.pointerId) return;
        // A mouse move with no button held means the button-up was missed (released outside the
        // window with no capture): finish the gesture instead of dragging with a loose pointer.
        if (s.pointerType === 'mouse' && event.buttons === 0) {
            cancelGesture('buttons-released', event);
            return;
        }
        s.x = event.clientX;
        s.y = event.clientY;
        const distance = Math.hypot(s.x - s.startX, s.y - s.startY);

        if (!s.started) {
            if (s.longPress) {
                // Moving before the hold completes is a scroll/swipe, not a drag.
                if (distance > tolerance) finish('aborted', event, 'moved');
                return;
            }
            if (distance < threshold || (threshold === 0 && distance === 0)) return;
            begin('threshold', event);
            if (!session) return;
        }
        call('move', info(event));
    }

    function onPointerUp(event) {
        const s = session;
        if (!s || event.pointerId !== s.pointerId) return;
        s.x = event.clientX ?? s.x;
        s.y = event.clientY ?? s.y;
        finish(s.started ? 'end' : 'tap', event);
    }

    function onPointerCancel(event) {
        const s = session;
        if (!s || event.pointerId !== s.pointerId) return;
        cancelGesture('pointercancel', event);
    }

    // The browser would otherwise open its context menu on the touch long-press that starts a drag.
    function onContextMenu(event) {
        if (session) event.preventDefault?.();
    }

    // touch-action cannot change mid-gesture, but cancelling touchmove can stop a scroll that has not
    // started yet. Only an ACTIVE drag cancels it — a pending long-press must still let the page scroll.
    function onTouchMove(event) {
        if (session?.started && event.cancelable !== false) event.preventDefault?.();
    }

    element.addEventListener('pointerdown', onPointerDown);
    element.addEventListener('pointermove', onPointerMove);
    element.addEventListener('pointerup', onPointerUp);
    element.addEventListener('pointercancel', onPointerCancel);
    element.addEventListener('lostpointercapture', onLostCapture);
    element.addEventListener('contextmenu', onContextMenu);
    // Only an immediate drag needs the listener for the whole attachment. A long-press drag binds it
    // in begin() and releases it on every exit, so an idle document never blocks scrolling.
    if (longPressMs === 0) {
        element.addEventListener('touchmove', onTouchMove, { passive: false });
        touchMoveBound = true;
    }

    return {
        get dragging() { return session?.started === true; },
        cancel(reason = 'cancelled') { cancelGesture(reason); },
        detach() {
            if (detached) return;
            detached = true;
            cancelGesture('detached');
            disarmClickSuppression();
            element.removeEventListener('pointerdown', onPointerDown);
            element.removeEventListener('pointermove', onPointerMove);
            element.removeEventListener('pointerup', onPointerUp);
            element.removeEventListener('pointercancel', onPointerCancel);
            element.removeEventListener('lostpointercapture', onLostCapture);
            element.removeEventListener('contextmenu', onContextMenu);
            element.removeEventListener('touchmove', onTouchMove);
            if (touchActionChanged && element.style) element.style.touchAction = touchActionBefore;
        },
    };
}
