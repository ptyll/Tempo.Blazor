// Tempo.Blazor pointer-gesture helper (ES module) — the ONE shared home for multi-pointer
// gestures (two-finger pinch/pan) and for the edge auto-scroll used while something is dragged.
// A component or a split package must never write its own; see docs/touch-interaction.md,
// section "Multi-pointer gestures".
//
//   import { attachPinch, createAutoScroller } from './pointer-gesture.js';
//   const pinch = attachPinch(element, { onStart, onChange, onEnd, onCancel });
//   pinch.detach();      // on dispose — cancels an active pinch, restores touch-action
//   const scroller = createAutoScroller(container, { onScroll });
//   // from the drag's onMove: scroller.update(info.x, info.y);  from onRelease: scroller.stop();
//
// Coexistence with pointer-drag.js (which stays the single-pointer helper): the first finger may
// already drive an attach() drag. pointer-drag ignores the second finger (its isPrimary guard), so
// the pinch cannot break the drag either — the HOST cancels the drag itself, in the pinch's
// onStart, so the drag rolls back exactly once the pinch takes over:
//
//   const drag = attach(handle, { ... });
//   const pinch = attachPinch(handle, { onStart() { drag.cancel('pinch'); } });
//
// When the drag and the pinch share an element, the drag's release on cancel also drops the
// pinch's capture of that finger. The pinch re-asserts both captures right after onStart, so the
// hand-over never rolls the pinch back. After onStart the pinch OWNS both fingers' capture: a host
// that must keep a finger elsewhere cancels the pinch from onStart (pinch.cancel('host')), and a
// failed re-assert cancels the pinch with 'lostpointercapture' when the capture had been HELD —
// the finger can no longer be tracked, so the gesture rolls back instead of committing half-seen.
// A re-assert that never had a capture (synthetic PointerEvents cannot be captured) is tolerated.
//
// WCAG 2.5.7 (Dragging Movements): a pinch is a dragging movement, so every host must ship a
// single-pointer alternative in the same change — typically zoom buttons beside the canvas.
//
// This module deliberately knows nothing about the DOM shape of its host and has no dependencies.

const WHEEL_END_DELAY = 150;   // ms of wheel silence that closes a ctrl+wheel gesture
const DEFAULT_EDGE = 36;       // px band inside the container edge that drives the auto-scroll
const DEFAULT_MAX_STEP = 28;   // px per frame at full edge power (the spreadsheet-canvas values)

/**
 * Whether the user asked the platform to reduce motion. False where it cannot be determined
 * (no matchMedia: SSR, Node) — never throws. Passed through to every callback so the host can
 * snap zoom steps instead of animating them.
 * @returns {boolean}
 */
function reducedMotion() {
    try {
        return globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches === true;
    }
    catch {
        return false;
    }
}

function positiveNumber(value, fallback) {
    return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : fallback;
}

const INERT = Object.freeze({
    detach() {},
    cancel() {},
    get active() { return false; },
});

/**
 * Two-pointer pinch/pan. Tracks the first two pointers of the allowed types on `element` by
 * pointerId; the pinch starts when the second finger lands (`onStart`) and ends when the count
 * drops below two (`onEnd`, exactly once). A third finger is ignored. Both pointers are captured
 * by id so the fingers may slide off the element mid-gesture; every exit releases by id.
 *
 * `onChange` reports `scale` relative to the gesture start (clamped to `minScale`/`maxScale`),
 * the centre of the two fingers and its pan `dx`/`dy` from the start centre. Cancel paths —
 * `pointercancel` of either finger, `lostpointercapture` of a tracked finger (a mid-pinch finger
 * whose capture the browser took back can no longer be tracked), `Escape` (document capture
 * phase, `stopPropagation`), and `detach()` — call `onCancel({ reason })` and NEVER `onEnd`.
 *
 * ctrl+wheel (trackpad pinch, `wheel: true` by default) zooms around the cursor with
 * `scale = Math.exp(-deltaY / 100)`: each tick reports `onStart` (first) + `onChange`, and `onEnd`
 * fires once 150 ms after the last tick. The wheel listener is non-passive and `preventDefault`
 * runs only while ctrl is held, so plain wheel keeps scrolling the page.
 *
 * @param {Element|null} element the element listened on
 * @param {object} [options]
 * @param {string[]} [options.pointerTypes=['touch']] pointer types that may form a pinch;
 *   an EMPTY array allows every pointer type
 * @param {boolean} [options.wheel=true] own ctrl+wheel (trackpad pinch) zoom
 * @param {number} [options.minScale=0] clamp of the cumulative scale reported
 * @param {number} [options.maxScale=Infinity] clamp of the cumulative scale reported
 * @param {string|null} [options.touchAction='none'] explicit touch-action for the element;
 *   null = do not touch; the previous value is restored on detach
 * @param {function} [options.onStart] ({ centerX, centerY, distance, reducedMotion }) — the host
 *   cancels its pointer-drag here: drag.cancel('pinch')
 * @param {function} [options.onChange] ({ scale, centerX, centerY, dx, dy, event, reducedMotion })
 * @param {function} [options.onEnd] ({ scale, centerX, centerY, dx, dy }) — commit once
 * @param {function} [options.onCancel] ({ reason }) —
 *   'escape' | 'pointercancel' | 'lostpointercapture' | 'detached'
 * @returns {{ detach: () => void, cancel: (reason?: string) => void, readonly active: boolean }}
 */
export function attachPinch(element, options = {}) {
    if (!element || typeof element.addEventListener !== 'function') return INERT;

    const pointerTypes = Array.isArray(options.pointerTypes) ? options.pointerTypes : ['touch'];
    const wheelEnabled = options.wheel !== false;
    const minScale = typeof options.minScale === 'number' && Number.isFinite(options.minScale) ? options.minScale : 0;
    const maxScale = typeof options.maxScale === 'number' && Number.isFinite(options.maxScale) ? options.maxScale : Infinity;
    const callbacks = {
        start: options.onStart, change: options.onChange, end: options.onEnd, cancel: options.onCancel,
    };
    const call = (name, payload) => typeof callbacks[name] === 'function' ? callbacks[name](payload) : undefined;
    const clampScale = value => Math.min(maxScale, Math.max(minScale, value));

    // touch-action: two fingers panning must not scroll the page, so the default claims the
    // element for the gesture; a host opting out passes touchAction: null.
    let touchActionBefore;
    let touchActionChanged = false;
    const wantedTouchAction = options.touchAction !== undefined ? options.touchAction : 'none';
    if (wantedTouchAction !== null && element.style) {
        touchActionBefore = element.style.touchAction;
        element.style.touchAction = wantedTouchAction;
        touchActionChanged = true;
    }

    const pointers = new Map();   // pointerId → { x, y } — the first two accepted fingers only
    const captured = new Set();   // pointerIds this helper captured (released by id on every exit)
    let session = null;           // active pinch: start geometry + the last reported one
    let wheelSession = null;      // pending ctrl+wheel burst: { scale, centerX, centerY, timer }
    let detached = false;
    let docKeyBound = false;
    let handingOver = false;      // true while the host's onStart runs (the drag hand-over)

    /** @returns {boolean} false when the capture could not be taken (the pointer is already gone) */
    function capture(id) {
        try {
            element.setPointerCapture?.(id);
            captured.add(id);
            return true;
        }
        catch {
            return false;   // the pointer is already gone
        }
    }

    function releaseCapture(id) {
        if (!captured.delete(id)) return;
        // The id is required: releasePointerCapture() with no argument throws in a real DOM.
        try { element.releasePointerCapture?.(id); } catch { /* already released */ }
    }

    function releaseAllCaptures() {
        for (const id of [...captured]) releaseCapture(id);
    }

    function bindEscape() {
        if (docKeyBound || typeof document === 'undefined') return;
        // Capture phase, as pointer-drag.js: a dialog listening on its own element, or an earlier
        // document listener, must not close the dialog around an active pinch before it rolled back.
        document.addEventListener?.('keydown', onKeyDown, true);
        docKeyBound = true;
    }

    function unbindEscape() {
        if (!docKeyBound) return;
        document.removeEventListener?.('keydown', onKeyDown, true);
        docKeyBound = false;
    }

    function geometry() {
        const [a, b] = [...pointers.values()];
        return {
            centerX: (a.x + b.x) / 2,
            centerY: (a.y + b.y) / 2,
            distance: Math.hypot(b.x - a.x, b.y - a.y),
        };
    }

    function beginPinch() {
        const { centerX, centerY, distance } = geometry();
        session = {
            startCenterX: centerX, startCenterY: centerY, startDistance: distance,
            centerX, centerY, scale: 1,
        };
        handingOver = true;
        try {
            call('start', { centerX, centerY, distance, reducedMotion: reducedMotion() });
        }
        finally {
            handingOver = false;
        }
        // The hand-over: onStart cancels the host's single-finger drag, and a pointer-drag
        // session attached to THIS element releases the first finger's capture by id - which is
        // the capture this pinch shares. After onStart the pinch OWNS both fingers' capture: a
        // host that must keep a finger elsewhere (e.g. a drag that survives the pinch) cancels
        // the pinch from onStart instead of relying on this re-assert. When the re-assert fails
        // for a finger whose capture we HELD (an engine that fires lostpointercapture
        // synchronously on release leaves the pointer uncapturable), the pinch can no longer
        // track it: roll back rather than commit a half-captured gesture. A failed re-assert for
        // a finger we never captured is tolerated: synthetic PointerEvents cannot be captured at
        // all, and the pinch still tracks them by their events (the pre-hand-over behaviour).
        if (session) {
            for (const id of [...pointers.keys()]) {
                const hadCapture = captured.has(id);
                if (!capture(id) && hadCapture) {
                    cancelPinch('lostpointercapture');
                    break;
                }
            }
        }
    }

    function endPinch() {
        const s = session;
        if (!s) return;
        session = null;
        call('end', {
            scale: clampScale(s.scale), centerX: s.centerX, centerY: s.centerY,
            dx: s.centerX - s.startCenterX, dy: s.centerY - s.startCenterY,
        });
    }

    /**
     * Rolls the pinch back. State and captures are cleaned up BEFORE the host callback runs, so a
     * throwing callback can never leave the helper holding a capture or the Escape listener.
     */
    function cancelPinch(reason) {
        if (!session) return;
        session = null;
        releaseAllCaptures();
        pointers.clear();
        unbindEscape();
        call('cancel', { reason });
    }

    function onKeyDown(event) {
        if (event.key === 'Escape' && session) {
            cancelPinch('escape');
            // Only an ACTIVE pinch consumes the Escape; a lone finger leaves the key to the dialog.
            event.stopPropagation?.();
        }
    }

    function onPointerDown(event) {
        if (detached) return;
        if (pointerTypes.length > 0 && !pointerTypes.includes(event.pointerType)) return;
        if (pointers.has(event.pointerId)) return;
        if (pointers.size >= 2) return;               // a third finger joins nothing
        bindEscape();
        pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        capture(event.pointerId);
        if (pointers.size === 2) beginPinch();
    }

    function onPointerMove(event) {
        const pointer = pointers.get(event.pointerId);
        if (!pointer) return;
        pointer.x = event.clientX;
        pointer.y = event.clientY;
        const s = session;
        if (!s) return;
        const { centerX, centerY, distance } = geometry();
        s.centerX = centerX;
        s.centerY = centerY;
        s.scale = s.startDistance > 0 ? distance / s.startDistance : 1;
        call('change', {
            scale: clampScale(s.scale), centerX, centerY,
            dx: centerX - s.startCenterX, dy: centerY - s.startCenterY,
            event, reducedMotion: reducedMotion(),
        });
    }

    function onPointerUp(event) {
        if (!pointers.has(event.pointerId)) return;
        const pointer = pointers.get(event.pointerId);
        pointer.x = event.clientX ?? pointer.x;
        pointer.y = event.clientY ?? pointer.y;
        if (session) {
            // The commit matches what is on screen: recompute from both fingers, then end once.
            const { centerX, centerY, distance } = geometry();
            session.centerX = centerX;
            session.centerY = centerY;
            session.scale = session.startDistance > 0 ? distance / session.startDistance : 1;
        }
        releaseCapture(event.pointerId);
        pointers.delete(event.pointerId);
        endPinch();
        if (pointers.size === 0) unbindEscape();
    }

    function onPointerCancel(event) {
        if (!pointers.has(event.pointerId)) return;
        if (session) {
            cancelPinch('pointercancel');   // releases both captures, clears the fingers, unbinds
            return;
        }
        releaseCapture(event.pointerId);
        pointers.delete(event.pointerId);
        if (pointers.size === 0) unbindEscape();
    }

    function onLostPointerCapture(event) {
        // Our own releases fire this too — by then the id is no longer tracked and we ignore it.
        if (!pointers.has(event.pointerId)) return;
        // An engine that fires lostpointercapture synchronously from releasePointerCapture does
        // so INSIDE the hand-over; beginPinch re-captures right after onStart returns.
        if (handingOver) return;
        if (session) {
            // A finger whose capture was lost mid-pinch (the element left the DOM, the browser
            // took the capture back) can no longer be tracked: roll back instead of committing
            // a gesture only half of which was seen.
            cancelPinch('lostpointercapture');
            return;
        }
        releaseCapture(event.pointerId);
        pointers.delete(event.pointerId);
        if (pointers.size === 0) unbindEscape();
    }

    function onWheel(event) {
        if (detached || !event.ctrlKey) return;   // plain wheel stays the page's scroll
        event.preventDefault?.();
        const factor = Math.exp(-event.deltaY / 100);
        const centerX = event.clientX;
        const centerY = event.clientY;
        let s = wheelSession;
        if (!s) {
            s = wheelSession = { scale: 1, centerX, centerY, timer: 0 };
            call('start', { centerX, centerY, distance: 0, reducedMotion: reducedMotion() });
        }
        s.scale *= factor;                        // the burst scale is cumulative from its start
        s.centerX = centerX;
        s.centerY = centerY;
        call('change', {
            scale: clampScale(s.scale), centerX, centerY, dx: 0, dy: 0,
            event, reducedMotion: reducedMotion(),
        });
        clearTimeout(s.timer);
        s.timer = setTimeout(finishWheel, WHEEL_END_DELAY);
    }

    function finishWheel() {
        const s = wheelSession;
        if (!s) return;
        wheelSession = null;
        clearTimeout(s.timer);
        call('end', { scale: clampScale(s.scale), centerX: s.centerX, centerY: s.centerY, dx: 0, dy: 0 });
    }

    element.addEventListener('pointerdown', onPointerDown);
    element.addEventListener('pointermove', onPointerMove);
    element.addEventListener('pointerup', onPointerUp);
    element.addEventListener('pointercancel', onPointerCancel);
    element.addEventListener('lostpointercapture', onLostPointerCapture);
    // Non-passive, or Chrome ignores preventDefault and ctrl+wheel zooms the whole browser.
    if (wheelEnabled) element.addEventListener('wheel', onWheel, { passive: false });

    return {
        get active() { return session !== null; },
        cancel(reason = 'cancelled') { cancelPinch(reason); },
        detach() {
            if (detached) return;
            detached = true;
            cancelPinch('detached');
            releaseAllCaptures();
            pointers.clear();
            unbindEscape();
            // A pending burst was already applied live through onChange: commit it once so the
            // last zoom tick is never silently lost on dispose.
            finishWheel();
            element.removeEventListener('pointerdown', onPointerDown);
            element.removeEventListener('pointermove', onPointerMove);
            element.removeEventListener('pointerup', onPointerUp);
            element.removeEventListener('pointercancel', onPointerCancel);
            element.removeEventListener('lostpointercapture', onLostPointerCapture);
            element.removeEventListener('wheel', onWheel);
            if (touchActionChanged && element.style) element.style.touchAction = touchActionBefore;
        },
    };
}

/**
 * Edge auto-scroll while something is dragged near the edge of a scroll container. A rAF loop with
 * no listeners of its own: the host feeds it the pointer from its drag's `onMove`
 * (`scroller.update(x, y)`) and stops it from `onRelease` (`scroller.stop()`).
 *
 * Ported from `spreadsheet-canvas.js` (the drag autoscroll): within `edge` px (default 36) of an
 * edge the scroll speed ramps to `maxStep` px (default 28) per frame — `round(power * maxStep)`,
 * 0 in the middle. Unlike spreadsheet-canvas, the power is CAPPED at 1 past the edge: a pointer
 * dragged beyond the edge scrolls at full `maxStep` per frame, never faster. The scroll position
 * is read back after each write, so `scrolledX`/`scrolledY` and `onScroll` carry only the APPLIED
 * delta — the browser clamps at the scroll limits, and a frame at a limit is silent.
 * `scrolledX`/`scrolledY` accumulate every applied delta since creation, so the host computes the
 * content delta under a stationary pointer as `dx + scrolledX`. The scroller is created once per
 * component and reused across gestures: `stop()` ends the loop, the next `update()` starts a new
 * one, and the accumulation continues.
 *
 * The container is read raw (`scrollLeft`/`scrollTop`): a host with RTL scroll semantics (the
 * spreadsheet, the data table) applies its own logical translation, as its migrated code does.
 *
 * @param {Element|null} container the scroll container; null = the page (`document.scrollingElement`),
 *   edge-tested against the viewport rect (`documentElement.clientWidth`/`clientHeight`), not the
 *   document box that its `getBoundingClientRect()` returns
 * @param {object} [options]
 * @param {number} [options.edge=36] width of the edge band that drives scrolling, in px
 * @param {number} [options.maxStep=28] px per frame at full edge power
 * @param {'x'|'y'|'both'} [options.axis='both'] axes the scroller may move
 * @param {function} [options.onScroll] ({ dx, dy }) after each scrolled frame, with the APPLIED
 *   delta (nothing at a scroll limit): re-hit-test the drop target — the pointer did not move,
 *   but the content under it did
 * @returns {{ update: (clientX: number, clientY: number) => void, stop: () => void,
 *             readonly scrolledX: number, readonly scrolledY: number }}
 */
export function createAutoScroller(container, options = {}) {
    const edge = positiveNumber(options.edge, DEFAULT_EDGE);
    const maxStep = positiveNumber(options.maxStep, DEFAULT_MAX_STEP);
    const axis = options.axis === 'x' || options.axis === 'y' ? options.axis : 'both';
    const onScroll = typeof options.onScroll === 'function' ? options.onScroll : null;
    const target = container
        ?? (typeof document !== 'undefined' ? document.scrollingElement ?? document.documentElement : null);
    // The page target is edge-tested against the VIEWPORT: scrollingElement.getBoundingClientRect()
    // returns the whole document box, which would put every edge far out of range.
    const isPage = !!target && typeof document !== 'undefined'
        && (target === document.scrollingElement || target === document.documentElement);

    let frame = 0;
    let pointerX = 0;
    let pointerY = 0;
    let scrolledX = 0;
    let scrolledY = 0;

    function step() {
        frame = 0;
        if (!target) return;
        const rect = isPage
            ? {
                left: 0,
                top: 0,
                right: document.documentElement?.clientWidth ?? 0,
                bottom: document.documentElement?.clientHeight ?? 0,
            }
            : target.getBoundingClientRect?.();
        if (!rect) return;
        // The power ramps 0 → 1 across the edge band; a pointer dragged PAST the edge stays at 1
        // instead of exceeding full speed.
        const leftPower = Math.min(1, Math.max(0, edge - (pointerX - rect.left)) / edge);
        const rightPower = Math.min(1, Math.max(0, edge - (rect.right - pointerX)) / edge);
        const upPower = Math.min(1, Math.max(0, edge - (pointerY - rect.top)) / edge);
        const downPower = Math.min(1, Math.max(0, edge - (rect.bottom - pointerY)) / edge);
        const dx = axis === 'y' ? 0 : Math.round((rightPower - leftPower) * maxStep);
        const dy = axis === 'x' ? 0 : Math.round((downPower - upPower) * maxStep);
        if (dx || dy) {
            const beforeLeft = target.scrollLeft;
            const beforeTop = target.scrollTop;
            if (dx) target.scrollLeft += dx;
            if (dy) target.scrollTop += dy;
            // The browser clamps at the scroll limits: only the APPLIED delta accumulates and
            // reports, so a frame at a limit is a no-op instead of a phantom dx/dy.
            const ax = target.scrollLeft - beforeLeft;
            const ay = target.scrollTop - beforeTop;
            if (ax || ay) {
                scrolledX += ax;
                scrolledY += ay;
                onScroll?.({ dx: ax, dy: ay });
            }
        }
        // Keep the loop alive until stop(): a pointer resting inside the band must keep scrolling.
        frame = requestAnimationFrame(step);
    }

    return {
        update(clientX, clientY) {
            if (!target) return;
            pointerX = clientX;
            pointerY = clientY;
            if (!frame) frame = requestAnimationFrame(step);
        },
        // Ends the loop; the next update() starts a new one — the scroller is created once per
        // component and reused across gestures, exactly like the spreadsheet start/stop pair.
        stop() {
            if (frame) {
                cancelAnimationFrame(frame);
                frame = 0;
            }
        },
        get scrolledX() { return scrolledX; },
        get scrolledY() { return scrolledY; },
    };
}
