// tm-toast-swipe.js tests with hand-rolled DOM stubs (same approach as tm-toast.test.mjs): no
// jsdom. The real swipe module and the real shared pointer-drag.js run against the stub —
// covering what bUnit cannot reach: the 40 % distance dismiss, the velocity dismiss, the rollback
// below the threshold, the pointercancel/Escape rollback, the vertical-intent veto, the
// touch/pen-only pointer filter, reduced-motion snapping and clean detach.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachSwipe, detachSwipe } from '../tm-toast-swipe.js';

const INTERACTIVE = 'button,a,input,select,textarea,[role="button"]';

function makeCard(id, width = 380) {
    const listeners = new Map();
    const classes = new Set(['tm-toast']);
    return {
        nodeType: 1,
        isConnected: true,
        dataset: { toastId: id },
        offsetWidth: width,
        style: {},
        captured: [],
        setPointerCapture(pid) { this.captured.push(pid); },
        releasePointerCapture() { },
        classes,
        classList: {
            add: (c) => classes.add(c),
            remove: (c) => classes.delete(c),
            contains: (c) => classes.has(c),
        },
        closest(selector) { return selector === '.tm-toast' ? this : null; },
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type, fn) { listeners.delete(type); },
        transitionEnd() { listeners.get('transitionend')?.({}); },
    };
}

/** A dismiss-button child: closest() answers the interactive selector with itself. */
function makeButton(card) {
    return {
        nodeType: 1,
        isConnected: true,
        captured: [],
        closest(selector) {
            if (selector === INTERACTIVE) return this;
            return selector === '.tm-toast' ? card : null;
        },
        setPointerCapture(id) { this.captured.push(id); },
        releasePointerCapture() { },
    };
}

function makeContainer() {
    const listeners = new Map();
    const cards = [];
    return {
        cards,
        listeners,
        style: {},
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type, fn) { listeners.delete(type); },
        contains(node) { return cards.includes(node); },
        appendCard(card) { cards.push(card); },
        fire(type, event) { listeners.get(type)?.(event); },
    };
}

function dotnetStub() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(method, ...args) {
            calls.push({ method, args });
            return Promise.resolve();
        },
    };
}

/** A pointer event the shared helper accepts: primary, button 0, on a card inside the container. */
function pointer(container, card, type, { pointerId = 1, pointerType = 'touch', x = 0, y = 0, timeStamp = 0, target } = {}) {
    return {
        pointerId, pointerType, clientX: x, clientY: y,
        button: 0, buttons: 1, isPrimary: true, timeStamp,
        target: target ?? card,
        prevented: false,
        preventDefault() { this.prevented = true; },
        stopPropagation() { },
    };
}

/** One horizontal drag from x=0 to x=toX between the two time stamps, touch by default. */
function drag(container, card, { toX, start = 0, end = 100, pointerType = 'touch', steps = 4 } = {}) {
    container.fire('pointerdown', pointer(container, card, 'pointerdown', { pointerType, x: 0, y: 0, timeStamp: start }));
    for (let i = 1; i <= steps; i++) {
        container.fire('pointermove', pointer(container, card, 'pointermove', {
            pointerType, x: (toX * i) / steps, y: 0, timeStamp: start + ((end - start) * i) / steps,
        }));
    }
    container.fire('pointerup', pointer(container, card, 'pointerup', { pointerType, x: toX, y: 0, timeStamp: end }));
}

test('drag past 40 % of the width dismisses through DismissToast', () => {
    const container = makeContainer();
    const card = makeCard('abc', 380); // 40 % = 152px
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    drag(container, card, { toX: 200, start: 0, end: 400 });

    assert.deepEqual(dotNet.calls, [{ method: 'DismissToast', args: ['abc'] }]);
    assert.equal(card.style.transform, 'translateX(200px)', 'the dragged transform stays until Blazor removes the card');
    assert.equal(card.classes.has('tm-toast--swipe-return'), false, 'a dismissed card does not roll back');
});

test('drag past -40 % of the width dismisses too (either direction)', () => {
    const container = makeContainer();
    const card = makeCard('left', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    drag(container, card, { toX: -200, start: 0, end: 400 });

    assert.deepEqual(dotNet.calls, [{ method: 'DismissToast', args: ['left'] }]);
});

test('a fast flick dismisses even below the distance threshold', () => {
    const container = makeContainer();
    const card = makeCard('flick', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    drag(container, card, { toX: 60, start: 0, end: 50 }); // 60px / 50ms = 1.2 px/ms > 0.5

    assert.deepEqual(dotNet.calls, [{ method: 'DismissToast', args: ['flick'] }],
        'velocity is the deliberate-flick rule next to the distance rule');
});

test('a slow short drag rolls the card back through the return class', () => {
    const container = makeContainer();
    const card = makeCard('short', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    drag(container, card, { toX: 80, start: 0, end: 800 }); // 80px / 800ms = 0.1 px/ms

    assert.deepEqual(dotNet.calls, [], 'below both thresholds: nothing is dismissed');
    assert.equal(card.style.transform, '', 'the inline transform is cleared');
    assert.equal(card.style.opacity, '');
    assert.equal(card.classes.has('tm-toast--swipe-return'), true, 'the rollback animates through the shared class');

    card.transitionEnd();
    assert.equal(card.classes.has('tm-toast--swipe-return'), false, 'transitionend drops the class so later drags are unpinned');
});

test('pointercancel rolls the card back without dismissing (browser took the gesture)', () => {
    const container = makeContainer();
    const card = makeCard('cancelled', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    container.fire('pointerdown', pointer(container, card, 'pointerdown', { x: 0, y: 0, timeStamp: 0 }));
    container.fire('pointermove', pointer(container, card, 'pointermove', { x: 190, y: 0, timeStamp: 100 }));
    assert.equal(card.style.transform, 'translateX(190px)');
    container.fire('pointercancel', pointer(container, card, 'pointercancel', { x: 190, y: 0, timeStamp: 100 }));

    assert.deepEqual(dotNet.calls, [], 'a cancelled gesture never dismisses');
    assert.equal(card.style.transform, '', 'the card snaps back to its rest position');
    assert.equal(card.style.opacity, '');
});

test('a mostly-vertical press is vetoed: no drag, no transform, no dismiss', () => {
    const container = makeContainer();
    const card = makeCard('scroll', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    container.fire('pointerdown', pointer(container, card, 'pointerdown', { x: 0, y: 0, timeStamp: 0 }));
    container.fire('pointermove', pointer(container, card, 'pointermove', { x: 10, y: 120, timeStamp: 100 }));
    container.fire('pointerup', pointer(container, card, 'pointerup', { x: 12, y: 140, timeStamp: 100 }));

    assert.deepEqual(dotNet.calls, []);
    assert.equal(card.style.transform, undefined, 'vertical intent is a scroll — the card never moves');
});

test('mouse drags are ignored: swipe is touch/pen only', () => {
    const container = makeContainer();
    const card = makeCard('mouse', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    drag(container, card, { toX: 200, start: 0, end: 400, pointerType: 'mouse' });

    assert.deepEqual(dotNet.calls, []);
    assert.equal(card.style.transform, undefined, 'a mouse keeps text selection and the buttons');
});

test('reduced motion snaps back without the return class', () => {
    const container = makeContainer();
    const card = makeCard('rm', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet, { reducedMotion: () => true });

    drag(container, card, { toX: 80, start: 0, end: 800 });

    assert.deepEqual(dotNet.calls, []);
    assert.equal(card.style.transform, '');
    assert.equal(card.classes.has('tm-toast--swipe-return'), false,
        'the rollback animation is skipped entirely under reduced motion');
});

test('detach mid-drag rolls back and silences every listener', () => {
    const container = makeContainer();
    const card = makeCard('gone', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    const handle = attachSwipe(container, dotNet);

    container.fire('pointerdown', pointer(container, card, 'pointerdown', { x: 0, y: 0, timeStamp: 0 }));
    container.fire('pointermove', pointer(container, card, 'pointermove', { x: 190, y: 0, timeStamp: 100 }));
    handle.detach();

    assert.equal(card.style.transform, '', 'a detached container never leaves a card hanging mid-drag');
    container.fire('pointerup', pointer(container, card, 'pointerup', { x: 190, y: 0, timeStamp: 100 }));
    assert.deepEqual(dotNet.calls, [], 'no dismissal fires after detach');
});

test('repeated attach replaces the wiring; detachSwipe is a clean no-op twice', () => {
    const container = makeContainer();
    const card = makeCard('x', 380);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);
    attachSwipe(container, dotNet); // idempotent replace

    drag(container, card, { toX: 200, start: 0, end: 400 });
    assert.equal(dotNet.calls.length, 1, 'exactly one wiring is live after a re-attach');

    detachSwipe(container);
    detachSwipe(container);

    drag(container, card, { toX: 200, start: 0, end: 400 });
    assert.equal(dotNet.calls.length, 1, 'a detached container dismisses nothing');
});

test('a press that starts on an interactive child (dismiss button) is vetoed: no capture, no dismiss', () => {
    const container = makeContainer();
    const card = makeCard('btn', 380);
    const button = makeButton(card);
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    container.fire('pointerdown', pointer(container, card, 'pointerdown', { x: 0, y: 0, timeStamp: 0, target: button }));
    container.fire('pointermove', pointer(container, card, 'pointermove', { x: 200, y: 0, timeStamp: 400, target: button }));
    container.fire('pointerup', pointer(container, card, 'pointerup', { x: 200, y: 0, timeStamp: 400, target: button }));

    assert.deepEqual(dotNet.calls, [], 'a swipe starting on the dismiss button must not dismiss');
    assert.equal(card.captured.length + button.captured.length, 0, 'the press never captures the pointer');
    assert.equal(card.style.transform, undefined, 'the card never moves');
});

test('tiny fast jitter does not dismiss: velocity counts only past max(24px, 15% of width)', () => {
    const container = makeContainer();
    const card = makeCard('jitter', 380); // 15 % = 57px; the flick floor is 57px
    container.appendCard(card);
    const dotNet = dotnetStub();
    attachSwipe(container, dotNet);

    // 10px in 5ms = 2px/ms — far above 0.5 px/ms, but under the 57px distance floor.
    drag(container, card, { toX: 10, start: 0, end: 5 });

    assert.deepEqual(dotNet.calls, [],
        'a tiny fast jitter is not a deliberate flick — velocity only counts past the distance floor');
});

test('pan-y stays on the container so vertical scrolling is untouched', () => {
    const container = makeContainer();
    container.appendCard(makeCard('y', 380));
    attachSwipe(container, dotnetStub());

    assert.equal(container.style.touchAction, 'pan-y',
        'the shared helper sets touch-action: pan-y — horizontal swipes fire pointer events, vertical pans scroll natively');
});
