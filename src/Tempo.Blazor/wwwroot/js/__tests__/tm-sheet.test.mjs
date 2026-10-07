import test from 'node:test';
import assert from 'node:assert/strict';
import { attachGesture, keyboardOffset, settle, trackViewport } from '../tm-sheet.js';

const snaps = [0.5, 1];

test('an open keyboard reports the visible viewport and the hidden gap as lengths', () => {
    // A 844px layout viewport with the keyboard covering everything below 430px.
    const offset = keyboardOffset(844, { height: 430, offsetTop: 0 });
    assert.equal(offset.viewport, '430px');
    assert.equal(offset.keyboard, '414px');
});

test('a keyboard that also shifts the viewport counts its offset', () => {
    const offset = keyboardOffset(844, { height: 430, offsetTop: 20 });
    assert.equal(offset.keyboard, '394px');
});

test('no keyboard leaves the sheet untouched', () => {
    const offset = keyboardOffset(844, { height: 844, offsetTop: 0 });
    assert.equal(offset.viewport, '844px');
    assert.equal(offset.keyboard, '0px');
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

function panel(height) {
    const props = new Map();
    const inline = {};
    return {
        props,
        inline,
        style: {
            set height(value) { inline.height = value; },
            get height() { return inline.height ?? ''; },
            set transition(value) { inline.transition = value; },
            get transition() { return inline.transition ?? ''; },
            setProperty(name, value) { props.set(name, value); },
            removeProperty(name) { props.delete(name); },
        },
        getBoundingClientRect: () => ({ height: inline.height ? Number.parseFloat(inline.height) : height }),
        closest: () => null,
    };
}

function handle() {
    const listeners = new Map();
    return {
        listeners,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        setPointerCapture() {},
        dispatch(type, event) { listeners.get(type)?.(event); },
    };
}

function host() {
    const calls = [];
    return { calls, invokeMethodAsync(name, ...args) { calls.push([name, ...args]); return Promise.resolve(); } };
}

function installWindow(height = 800) {
    const viewport = { height, offsetTop: 0, addEventListener() {}, removeEventListener() {} };
    globalThis.window = {
        innerHeight: height,
        visualViewport: viewport,
        addEventListener() {},
        removeEventListener() {},
    };
}

test('a drag writes only an inline height and leaves the Blazor height variable alone', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    // Blazor owns --tm-sheet-height. A gesture that deletes it leaves the panel at the CSS default
    // (0.5) whenever the snap index does not change, because Blazor will not rewrite the style.
    sheet.style.setProperty('--tm-sheet-height', '1');
    attachGesture(grab, sheet, [0.5, 1], true, host(), 'sheet');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 140 });
    assert.equal(sheet.inline.height, '360px');
    assert.equal(sheet.props.get('--tm-sheet-height'), '1', 'a gesture must not rewrite the snap variable');

    sheet.getBoundingClientRect = () => ({ height: 400 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 140 });
    assert.equal(sheet.inline.height, '', 'releasing on the same snap clears the inline height');
    assert.equal(sheet.inline.transition, '');
    assert.equal(sheet.props.get('--tm-sheet-height'), '1', 'the Blazor-owned height survives a release');
    delete globalThis.window;
});

test('pointercancel and a vetoed dismiss clear the inline gesture state and keep the height variable', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    // Seeded before the gesture, the way Blazor writes PanelStyle. The gesture must not delete it:
    // a cancel leaves the snap index unchanged, so Blazor will not put the variable back.
    sheet.style.setProperty('--tm-sheet-height', '1');
    attachGesture(grab, sheet, [0.5, 1], false, sink, 'veto');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 500 });
    grab.dispatch('pointercancel', { pointerId: 1 });

    assert.equal(sheet.inline.height, '');
    assert.equal(sheet.inline.transition, '');
    assert.equal(sheet.props.get('--tm-sheet-height'), '1', 'pointercancel must not drop the Blazor-owned height');
    assert.equal(sink.calls.length, 0, 'a cancelled gesture reports nothing');

    // A vetoed dismiss (swipe-to-dismiss off, released below the lowest snap) reports the snap and
    // still must not touch the variable. Blazor rewrites it only when the index changes.
    grab.dispatch('pointerdown', { button: 0, pointerId: 2, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 2, clientY: 500, timeStamp: 200 });
    grab.dispatch('pointerup', { pointerId: 2, clientY: 500, timeStamp: 220 });
    assert.equal(sheet.props.get('--tm-sheet-height'), '1', 'a vetoed dismiss leaves the height variable alone');
    assert.equal(sink.calls.at(-1)?.[0], 'HandleSheetSnappedAsync');
    delete globalThis.window;
});

test('a release is measured before the inline height is cleared', () => {
    installWindow(800);
    const grab = handle();
    let reads = 0;
    const sheet = panel(400);
    sheet.getBoundingClientRect = () => {
        reads++;
        const dragged = Number.parseFloat(sheet.inline.height);
        // A read after resetInline sees the cleared height. The gesture must have measured before that.
        return { height: Number.isFinite(dragged) ? dragged : 0 };
    };
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'measure');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 260 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 260 });

    assert.equal(sink.calls[0][0], 'HandleSheetDismissedAsync',
        '240px of 800 is below the lowest snap by more than the margin, so it dismisses — and only because the height was read before the reset');
    delete globalThis.window;
});

test('a header ignores pointerdown on an interactive target until the finger moves', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    attachGesture(grab, sheet, [0.5, 1], true, host(), 'header');

    const button = { closest: () => button };
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100, target: button });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 102 });
    assert.equal(sheet.inline.height ?? '', '', 'a click on the close button must not drag the sheet');

    grab.dispatch('pointermove', { pointerId: 1, clientY: 112 });
    assert.equal(sheet.inline.height, '388px', 'a real drag from the header still moves the sheet');
    delete globalThis.window;
});

test('a tap on a content sheet does not dismiss it', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [], true, sink, 'tap');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 100 });

    assert.equal(sink.calls.length, 0, 'a tap never reaches the host');
    delete globalThis.window;
});

test('an upward drag on a content sheet does not dismiss it', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [], true, sink, 'up');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 120 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 120 });

    assert.equal(sink.calls.length, 0, 'growing the sheet is not a dismiss');
    delete globalThis.window;
});

test('a content sheet dismisses past a quarter of its start height', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [], true, sink, 'down');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 230 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 230 });

    assert.equal(sink.calls[0][0], 'HandleSheetDismissedAsync',
        '130px of a 400px sheet is past the quarter, so it dismisses');
    delete globalThis.window;
});

test('a flick dismisses even when pointerup lands on the last move', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'flick');

    // A real pointerup is dispatched at the last move's position, so velocity computed from lastY
    // (which onMove already overwrote) is always 0. The samples are 8ms apart and the release adds
    // no further travel: 40px / 8ms is 5 px/ms, well past the 0.5 px/ms flick.
    // Released height is 360 of 800 (0.45), which is above the lowest snap minus the 0.15 margin
    // (0.35), so only the flick can dismiss it.
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 200, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 200, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 208, timeStamp: 8 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 240, timeStamp: 16 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 240, timeStamp: 16 });

    assert.equal(sink.calls[0]?.[0], 'HandleSheetDismissedAsync',
        'a downward flick faster than 0.5 px/ms dismisses even though the release is still above the margin');
    delete globalThis.window;
});

test('a slow drag that ends on the last move does not count as a flick', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'slow');

    // 40px over 400ms is 0.1 px/ms. Released at 0.45, nearest snap is half, and it must not dismiss.
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 200, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 208, timeStamp: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 240, timeStamp: 400 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 240, timeStamp: 400 });

    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync', 'a slow drag settles to the nearest snap');
    assert.equal(sink.calls[0]?.[1], 0);
    delete globalThis.window;
});

test('a null handle still tracks the viewport', async () => {
    installWindow(430);
    const sheet = panel(400);
    const sink = host();
    attachGesture(null, sheet, [1], false, sink, 'track-only');

    assert.equal(sheet.props.get('--tm-sheet-viewport'), '430px', 'a modal sheet with no gesture still lifts for the keyboard');
    delete globalThis.window;
});
