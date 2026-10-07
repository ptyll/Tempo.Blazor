import test from 'node:test';
import assert from 'node:assert/strict';
import { keyboardOffset, settle, trackViewport } from '../tm-sheet.js';

const snaps = [0.5, 1];

test('an open keyboard reports the visible viewport and the hidden gap', () => {
    // A 844px layout viewport with the keyboard covering everything below 430px.
    const offset = keyboardOffset(844, { height: 430, offsetTop: 0 });
    assert.equal(offset.viewport, 430);
    assert.equal(offset.keyboard, 414);
});

test('a keyboard that also shifts the viewport counts its offset', () => {
    const offset = keyboardOffset(844, { height: 430, offsetTop: 20 });
    assert.equal(offset.keyboard, 394);
});

test('no keyboard leaves the sheet untouched', () => {
    const offset = keyboardOffset(844, { height: 844, offsetTop: 0 });
    assert.equal(offset.viewport, 844);
    assert.equal(offset.keyboard, 0);
});

test('a release below the lowest snap dismisses when swipe-to-dismiss is on', () => {
    const result = settle(snaps, 0.2, true);
    assert.equal(result.dismiss, true);
    assert.equal(result.snap, 0.5);
});

test('a release below the lowest snap snaps back when swipe-to-dismiss is off', () => {
    const result = settle(snaps, 0.1, false);
    assert.equal(result.dismiss, false);
    assert.equal(result.snap, 0.5);
});

test('a release between snaps settles to the nearest one', () => {
    assert.equal(settle(snaps, 0.7, true).snap, 0.5);
    assert.equal(settle(snaps, 0.8, true).snap, 1);
    assert.equal(settle(snaps, 0.7, true).dismiss, false);
});

test('a release exactly on a snap stays there', () => {
    const result = settle(snaps, 0.5, true);
    assert.equal(result.dismiss, false);
    assert.equal(result.snap, 0.5);
});

test('a release above the tallest snap clamps to it', () => {
    const result = settle(snaps, 1.4, true);
    assert.equal(result.dismiss, false);
    assert.equal(result.snap, 1);
});

test('a sheet with one snap dismisses only below it', () => {
    assert.equal(settle([0.6], 0.3, true).dismiss, true);
    assert.equal(settle([0.6], 0.9, true).dismiss, false);
});

test('a sheet with no snap points is content height, not an error', () => {
    // A short menu has nothing to snap to. It sizes to its content under the max-height cap.
    const result = settle([], 0.4, true);
    assert.equal(result.dismiss, false);
    assert.equal(result.snap, null);
});

test('trackViewport writes lengths, so the sheet is usable before the module runs', () => {
    const root = {
        props: new Map(),
        style: {
            setProperty(name, value) { root.props.set(name, value); },
        },
    };
    const viewport = { height: 430, offsetTop: 12 };
    globalThis.window = {
        innerHeight: 844,
        visualViewport: viewport,
        addEventListener() {},
        removeEventListener() {},
    };
    viewport.addEventListener = () => {};
    viewport.removeEventListener = () => {};

    const stop = trackViewport(root);
    assert.equal(root.props.get('--tm-sheet-viewport'), '430px', 'the viewport variable is a length, not a bare number');
    assert.equal(root.props.get('--tm-sheet-keyboard'), '402px');
    stop();
    delete globalThis.window;
});
