import test from 'node:test';
import assert from 'node:assert/strict';
import { keyboardOffset, settle } from '../tm-sheet.js';

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

test('a sheet with no snap points is rejected', () => {
    assert.throws(() => settle([], 0.5, true), /snap point/);
});
