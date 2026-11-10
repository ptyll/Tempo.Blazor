// tm-autonomy-dial.js tests — the scroll guard of TmAutonomyDial.
//
// The component owns the keyboard MODEL (which grade an arrow reaches); the module owns only the one thing Blazor
// cannot express: telling the browser not to scroll the page with the six keys that belong to a focused
// `role="slider"`. These tests pin the contract that matters: exactly those six keys are swallowed, every other key
// keeps its browser behaviour, the event still reaches Blazor (no stopPropagation), the guard is installed once per
// element and a detach leaves nothing behind.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    attachDialKeys,
    DIAL_KEYS,
    detachDialKeys,
    handleKeyDown,
    shouldPreventScroll,
} from '../tm-autonomy-dial.js';

/** A stand-in for the slider element that records the listeners a module installs. */
function fakeSlider() {
    const state = { listeners: [], removed: [] };

    return {
        state,
        addEventListener(type, handler) {
            state.listeners.push({ type, handler });
        },
        removeEventListener(type, handler) {
            state.removed.push({ type, handler });
            state.listeners = state.listeners.filter(entry => entry.handler !== handler);
        },
    };
}

/** A keyboard event whose default action and propagation can be observed. */
function fakeKeyEvent(key) {
    const event = {
        key,
        prevented: 0,
        stopped: 0,
        preventDefault() { event.prevented += 1; },
        stopPropagation() { event.stopped += 1; },
    };

    return event;
}

test('DIAL_KEYS is the four arrows plus the two ends, and nothing else', () => {
    assert.deepEqual([...DIAL_KEYS], ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End']);
});

test('shouldPreventScroll swallows the keys the dial owns', () => {
    for (const key of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End']) {
        assert.equal(shouldPreventScroll(key), true, `${key} belongs to the slider`);
    }
});

test('shouldPreventScroll leaves every other key to the browser', () => {
    for (const key of ['Tab', 'Enter', ' ', 'Escape', 'PageUp', 'PageDown', 'a', 'Shift', '']) {
        assert.equal(shouldPreventScroll(key), false, `${key} must keep its browser behaviour`);
    }

    assert.equal(shouldPreventScroll(undefined), false);
    assert.equal(shouldPreventScroll(null), false);
    assert.equal(shouldPreventScroll(7), false, 'a non-string key is not a key');
});

test('handleKeyDown preventDefaults an owned key exactly once', () => {
    const event = fakeKeyEvent('ArrowDown');

    assert.equal(handleKeyDown(event), true);
    assert.equal(event.prevented, 1);
    assert.equal(event.stopped, 0, 'Blazor still has to receive the event: the guard never stops propagation');
});

test('handleKeyDown leaves a foreign key completely alone', () => {
    const event = fakeKeyEvent('Tab');

    assert.equal(handleKeyDown(event), false);
    assert.equal(event.prevented, 0, 'Tab must still move the focus');
    assert.equal(event.stopped, 0);
});

test('handleKeyDown survives an event without preventDefault', () => {
    assert.equal(handleKeyDown({ key: 'Home' }), true);
    assert.equal(handleKeyDown({}), false);
    assert.equal(handleKeyDown(null), false);
});

test('attachDialKeys installs ONE keydown listener, idempotently', () => {
    const slider = fakeSlider();

    assert.equal(attachDialKeys(slider), true);
    assert.equal(slider.state.listeners.length, 1);
    assert.equal(slider.state.listeners[0].type, 'keydown');

    assert.equal(attachDialKeys(slider), false, 'a second attach for the same element adds no listener');
    assert.equal(slider.state.listeners.length, 1, 'two handlers would call preventDefault twice');
});

test('the installed listener is the guarded handler', () => {
    const slider = fakeSlider();
    attachDialKeys(slider);

    const event = fakeKeyEvent('End');
    slider.state.listeners[0].handler(event);

    assert.equal(event.prevented, 1, 'the listener the element carries is the guard itself');
    assert.equal(event.stopped, 0);
});

test('detachDialKeys removes the listener and is safe to repeat', () => {
    const slider = fakeSlider();
    attachDialKeys(slider);

    assert.equal(detachDialKeys(slider), true);
    assert.equal(slider.state.listeners.length, 0);
    assert.equal(slider.state.removed.length, 1);
    assert.equal(slider.state.removed[0].type, 'keydown');

    assert.equal(detachDialKeys(slider), false, 'nothing is left to remove');
    assert.equal(slider.state.removed.length, 1);
});

test('a re-attach after a detach works again', () => {
    const slider = fakeSlider();
    attachDialKeys(slider);
    detachDialKeys(slider);

    assert.equal(attachDialKeys(slider), true, 'a new slider element (or a re-render after the guard was released) arms again');
    assert.equal(slider.state.listeners.length, 1);
});

test('a missing element or one without listeners is a no-op, never a throw', () => {
    assert.equal(attachDialKeys(null), false);
    assert.equal(attachDialKeys(undefined), false);
    assert.equal(attachDialKeys({}), false, 'prerender hands the module no real element');
    assert.equal(detachDialKeys(null), false);
    assert.equal(detachDialKeys({}), false);
});
