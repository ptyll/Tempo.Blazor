// Tempo.Blazor toast swipe-to-dismiss (ES module) — the gesture runs on the ONE shared
// pointer-drag helper; this module is only the toast-specific policy on top of it.
//
//   import { attachSwipe, detachSwipe } from './tm-toast-swipe.js';
//   attachSwipe(container, dotNet);   // delegated: every .tm-toast card inside the container
//   detachSwipe(container);           // on dispose
//
// Policy:
//   * horizontal drag on a card (touch/pen only — a mouse keeps text selection and the buttons)
//     follows the finger via the inline transform, with the card fading as it travels;
//   * releasing past 40 % of the card width, or flicking faster than 0.5 px/ms, dismisses the
//     card through the same DismissToast entry point as Escape and the close button (the
//     ToastService owns the state — JS only asks);
//   * anything else — a short drag, pointercancel (the browser took the gesture, e.g. a scroll),
//     Escape, detach — rolls the card back to its rest position;
//   * a press that starts on an interactive child (the close button, an action, a link or a
//     field) is vetoed in onPress and never even captures the pointer — captureOn 'start' keeps
//     taps and clicks on those children fully native;
//   * the rollback animates through the tm-toast--swipe-return class EXCEPT under reduced motion
//     (then it snaps back instantly);
//   * a mostly-vertical press is vetoed before the drag starts, so pan-y scrolling is untouched
//     and the swipe is never the only way out: the close button and Escape always work (WCAG 2.5.7).
//
// The card's own slide-in animation uses fill-mode backwards exactly so the inline transform is
// not pinned by a forwards fill for the card's whole life.

import { attach } from './pointer-drag.js';

const instances = new Map(); // container -> { drag }

const DEFAULT_DISMISS_RATIO = 0.4;   // of the card width
const DEFAULT_MIN_VELOCITY = 0.5;    // px per ms — a deliberate flick
const FLICK_MIN_FRACTION = 0.15;     // of the card width — below this a flick never counts
const FLICK_MIN_PX = 24;             // absolute floor for narrow cards
const VELOCITY_WINDOW_MS = 100;      // the flick speed reads from the last ~100ms of moves
const INTERACTIVE_SELECTOR = 'button,a,input,select,textarea,[role="button"]';
const RETURN_CLASS = 'tm-toast--swipe-return';

/**
 * Wires swipe-to-dismiss for one toast container. Repeated calls replace the previous wiring.
 * @param {HTMLElement} container the .tm-toast-container root
 * @param {object} dotNet the DotNetObjectReference exposing DismissToast(string id)
 * @param {object} [options] test seams
 * @param {Function} [options.getWidth] card width in px (defaults to offsetWidth; 0 disables the distance rule)
 * @param {number} [options.dismissRatio=0.4] fraction of the width that dismisses on release
 * @param {number} [options.minVelocity=0.5] px/ms that dismisses on release
 * @param {Function} [options.reducedMotion] override the reduced-motion probe
 * @returns {{ detach: () => void }}
 */
export function attachSwipe(container, dotNet, options = {}) {
    detachSwipe(container);

    const getWidth = typeof options.getWidth === 'function' ? options.getWidth
        : (el) => (typeof el.offsetWidth === 'number' ? el.offsetWidth : 0);
    const dismissRatio = typeof options.dismissRatio === 'number' ? options.dismissRatio : DEFAULT_DISMISS_RATIO;
    const minVelocity = typeof options.minVelocity === 'number' ? options.minVelocity : DEFAULT_MIN_VELOCITY;
    const reducedMotion = typeof options.reducedMotion === 'function' ? options.reducedMotion : null;

    let card = null;
    let moves = []; // recent { x, stamp } samples — the flick speed reads the last ~100ms window

    const stamp = (info) => {
        const t = info.event?.timeStamp;
        return typeof t === 'number' ? t : 0;
    };

    const paint = (target, dx) => {
        const width = getWidth(target);
        const ratio = width > 0 ? Math.min(1, Math.abs(dx) / width) : 0;
        target.style.transform = `translateX(${dx}px)`;
        target.style.opacity = String(Math.max(0, 1 - ratio * 0.6));
    };

    const clearPaint = (target) => {
        target.style.transform = '';
        target.style.opacity = '';
    };

    /** Snaps the card back with nothing but a style clear — the reduced-motion and cancel path. */
    const snapBack = (target) => {
        clearPaint(target);
        target.classList?.remove(RETURN_CLASS);
    };

    /** Animates the card back through the shared return class (skipped under reduced motion). */
    const rollBack = (target) => {
        if (reducedMotion?.() === true) {
            snapBack(target);
            return;
        }
        clearPaint(target);
        target.classList?.add(RETURN_CLASS);
        const done = () => {
            target.classList?.remove(RETURN_CLASS);
            target.removeEventListener?.('transitionend', done);
        };
        target.addEventListener?.('transitionend', done);
        // A layout-less host never fires transitionend: drop the class after one transition tick.
        setTimeout(done, 300);
    };

    const drag = attach(container, {
        selector: '.tm-toast',
        pointerTypes: ['touch', 'pen'],
        threshold: 8,
        captureOn: 'start',     // the press never captures — a tap on a child keeps its native click
        touchAction: 'pan-y',   // vertical scroll stays native; a horizontal drag fires pointer events
        dragClass: null,        // the inline transform is the drag visual, not a class
        onPress(info) {
            // A press on an interactive child (close button, action, link, field) is that child's
            // own gesture — veto before capture so the click and focus stay fully native.
            if (info.event?.target?.closest?.(INTERACTIVE_SELECTOR))
                return false;
            return undefined;
        },
        onStart(info) {
            // A mostly-vertical press is a scroll, not a swipe: veto before the drag commits (the
            // pan-y touch-action then hands the gesture to the browser without ever capturing).
            if (Math.abs(info.dy) > Math.abs(info.dx))
                return false;
            card = info.target;
            moves = [{ x: info.startX, stamp: stamp(info) }];
            // A leftover return transition from a previous rollback must not fight this drag.
            card.classList?.remove(RETURN_CLASS);
            return undefined;
        },
        onMove(info) {
            if (!card)
                return;
            moves.push({ x: info.x, stamp: stamp(info) });
            paint(card, info.dx);
        },
        onEnd(info) {
            settle(info, false);
        },
        onCancel() {
            // pointercancel / Escape / detach: the browser or the user took the gesture — the
            // card returns to rest and nothing is dismissed.
            const target = card;
            card = null;
            if (target)
                snapBack(target);
        },
    });

    function settle(info, cancelled) {
        const target = card;
        const samples = moves;
        card = null;
        moves = [];
        if (!target || cancelled)
            return;
        const width = getWidth(target);
        const distanceHit = width > 0 && Math.abs(info.dx) >= dismissRatio * width;
        // The flick rule: velocity reads the last ~100ms of moves, and it only counts once the
        // drag has covered at least max(24px, 15% of the card) — a tiny fast jitter (a shaky
        // finger, a bumped table) is not a deliberate flick.
        const last = samples[samples.length - 1];
        const flickFloor = width > 0 ? Math.max(FLICK_MIN_PX, FLICK_MIN_FRACTION * width) : FLICK_MIN_PX;
        let velocityHit = false;
        if (last && Math.abs(info.dx) >= flickFloor) {
            let first = samples[0];
            for (let i = samples.length - 1; i >= 0; i--) {
                if (last.stamp - samples[i].stamp <= VELOCITY_WINDOW_MS)
                    first = samples[i];
                else
                    break;
            }
            const elapsed = Math.max(1, last.stamp - first.stamp);
            velocityHit = Math.abs((last.x - first.x) / elapsed) >= minVelocity;
        }
        if (!distanceHit && !velocityHit) {
            rollBack(target);
            return;
        }
        const id = target.dataset?.toastId;
        if (id === undefined || id === null)
            return;
        try {
            // .catch(() => {}): the circuit may be torn down — a lost ask must not surface as an
            // unhandled rejection. The dragged transform stays on the card until Blazor removes it.
            Promise.resolve(dotNet.invokeMethodAsync('DismissToast', id)).catch(() => { });
        } catch {
            // invokeMethodAsync itself threw synchronously (disposed reference).
        }
    }

    const handle = {
        detach() {
            if (card) {
                const target = card;
                card = null;
                snapBack(target);
            }
            drag.detach();
        },
    };
    instances.set(container, handle);
    return handle;
}

/**
 * Removes the swipe wiring. No-op when the container is not attached. A drag in progress rolls
 * back without dismissing.
 * @param {HTMLElement} container the container previously passed to attachSwipe()
 */
export function detachSwipe(container) {
    instances.get(container)?.detach();
    instances.delete(container);
}
