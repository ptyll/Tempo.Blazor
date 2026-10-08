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

test('a downward flick from the full snap steps one snap down instead of closing', () => {
    // A 1 px/ms swipe is an ordinary "back to half" gesture. Dismissing from full threw the
    // user's filters and form state away.
    const result = settle(snaps, 0.49, true, 1, 1);
    assert.equal(result.dismiss, false);
    assert.equal(result.snap, 0.5);
    assert.equal(result.index, 0);
});

test('a downward flick from the lowest snap dismisses', () => {
    // Released above the lowest-snap margin, so only the flick from the lowest snap closes it.
    const result = settle(snaps, 0.45, true, 1, 0);
    assert.equal(result.dismiss, true);
    assert.equal(result.snap, 0.5);
});

test('a very fast downward release closes the sheet from any snap', () => {
    // Above ~2 px/ms the user asked to close, whatever snap the drag started from.
    const result = settle(snaps, 0.49, true, 2.5, 1);
    assert.equal(result.dismiss, true);
});

test('an upward flick from the lowest snap steps one snap up', () => {
    const result = settle(snaps, 0.7, true, -1.5, 0);
    assert.equal(result.dismiss, false, 'an upward flick never dismisses');
    assert.equal(result.snap, 1);
    assert.equal(result.index, 1);
});

test('a slow release settles to the nearest snap whatever the start index', () => {
    const result = settle(snaps, 0.8, true, 0.1, 1);
    assert.equal(result.dismiss, false);
    assert.equal(result.snap, 1);
});

test('a one-snap sheet is not dismissed by settle — the gesture decides from the start height', () => {
    // settle used to dismiss a one-snap sheet on any release below the snap, so a 10px drag closed
    // it. The gesture now dismisses only past a quarter of the start height, or on a flick.
    assert.equal(settle([0.6], 0.3, true).dismiss, false);
    assert.equal(settle([0.6], 0.9, true).snap, 0.6);
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

test('a flick from the full snap lands on half, not on the dismiss path', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(680); // the full snap of an 800px viewport capped at 0.85
    const sink = host();
    attachGesture(grab, sheet, [0.5, 0.85], true, sink, 'full-flick');

    // The last 80ms of the drag carry 20px in 20ms, so the flick is 1 px/ms — fast enough to step,
    // far below the close-anywhere threshold. Released at 560/800 (0.7), the nearest snap is still
    // full, so only the start index can move the sheet down one snap.
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 200, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 260, timeStamp: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 300, timeStamp: 300 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 320, timeStamp: 320 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 320, timeStamp: 320 });

    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync');
    assert.equal(sink.calls[0]?.[1], 0, 'a 1 px/ms swipe from the full snap steps down to half');
    delete globalThis.window;
});

test('an upward flick from half grows the sheet to full', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'up-flick');

    // 40px up in the last 20ms is 2 px/ms upward. It must grow the sheet, not dismiss it.
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 200, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 180, timeStamp: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 140, timeStamp: 300 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 120, timeStamp: 320 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 120, timeStamp: 320 });

    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync');
    assert.equal(sink.calls[0]?.[1], 1, 'an upward flick steps one snap up');
    delete globalThis.window;
});

test('a one-snap sheet dismisses only past a quarter of its start height', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5], true, sink, 'one');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 110, timeStamp: 200 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 110, timeStamp: 400 });
    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync', '10px is not a quarter of the start height');

    sink.calls.length = 0;
    grab.dispatch('pointerdown', { button: 0, pointerId: 2, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 2, clientY: 40, timeStamp: 200 });
    grab.dispatch('pointerup', { pointerId: 2, clientY: 40, timeStamp: 400 });
    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync', 'an upward drag never dismisses a one-snap sheet');
    delete globalThis.window;
});

test('an inline sheet measures against its host, not the viewport', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(200);
    const hostBox = { getBoundingClientRect: () => ({ height: 400 }) };
    sheet.closest = () => hostBox;
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'inline', false);

    // 40px down of a 200px sheet is 0.4 of the 400px host. Of the 800px viewport that same drag is a
    // dismiss, so a snap here proves the gesture measured the host.
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 140, timeStamp: 200 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 140, timeStamp: 400 });
    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync', 'a small drag inside a short host must not dismiss');

    sink.calls.length = 0;
    grab.dispatch('pointerdown', { button: 0, pointerId: 2, clientY: 200, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 2, clientY: 80, timeStamp: 200 });
    grab.dispatch('pointerup', { pointerId: 2, clientY: 80, timeStamp: 400 });
    assert.equal(sink.calls[0]?.[1], 1, '0.8 of the host settles on the higher snap');
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

// ── SwipeToDismiss=false never dismisses (review round 5) ──────────────────────
// settle() used to close the sheet on any flick above 2 px/ms, and on a downward flick from the
// lowest snap, without consulting swipeToDismiss — attachGesture already passes `swipeToDismiss &&
// downward`, so every dismiss return inside settle must honour the flag too.

test('settle never dismisses when swipe-to-dismiss is off, whatever the velocity', () => {
    // A 1 px/ms flick from the lowest snap is a "one snap down" gesture; with dismissal off it
    // clamps to the lowest snap instead of closing.
    const slow = settle(snaps, 0.45, false, 1, 0);
    assert.equal(slow.dismiss, false, 'a downward flick from the lowest snap must not dismiss');
    assert.equal(slow.index, 0, 'a downward flick from the lowest snap clamps to index 0');
    assert.equal(slow.snap, 0.5);

    // Above ~2 px/ms used to close the sheet from any snap even with the flag off.
    const fast = settle(snaps, 0.49, false, 2.5, 1);
    assert.equal(fast.dismiss, false, 'a fast flick settles instead of dismissing');
    assert.equal(fast.index, 0, 'a fast flick with dismissal off settles at the lowest snap');
    assert.equal(fast.snap, 0.5);
});

test('SwipeToDismiss=false: a 1 px/ms downward flick from the lowest snap must not dismiss', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], false, sink, 'off-1');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 400, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 420, timeStamp: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 440, timeStamp: 220 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 440, timeStamp: 220 });

    assert.notEqual(sink.calls[0]?.[0], 'HandleSheetDismissedAsync');
    delete globalThis.window;
});

test('SwipeToDismiss=false: a 3 px/ms flick from full must not dismiss', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(800);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], false, sink, 'off-2');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 140, timeStamp: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 200, timeStamp: 220 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 200, timeStamp: 220 });

    assert.notEqual(sink.calls[0]?.[0], 'HandleSheetDismissedAsync');
    delete globalThis.window;
});

test('a net-upward drag ending in a downward flick must not dismiss', () => {
    // The drag grew the sheet first (400 -> 300 -> 380 of an 800px viewport), so its net direction
    // is upward. The last-80ms velocity sample is a fast downward flick; a decision recorded for the
    // drawer says a net-upward drag never dismisses, whatever the tail velocity says.
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'up-then-down');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 400, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 300, timeStamp: 200 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 320, timeStamp: 300 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 380, timeStamp: 320 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 380, timeStamp: 320 });

    assert.notEqual(sink.calls[0]?.[0], 'HandleSheetDismissedAsync');
    delete globalThis.window;
});
