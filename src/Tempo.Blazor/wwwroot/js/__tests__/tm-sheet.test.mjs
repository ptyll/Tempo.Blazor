import test from 'node:test';
import assert from 'node:assert/strict';
import { attachGesture, keyboardOffset, promote, raise, settle, trackViewport, pinRoot, unpinRoot } from '../tm-sheet.js';

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

function handle(options = {}) {
    const listeners = new Map();
    const captured = new Set();
    const el = {
        listeners,
        captured,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        setPointerCapture(id) { captured.add(id); },
        releasePointerCapture(id) { captured.delete(id); },
        dispatch(type, event) { listeners.get(type)?.(event); },
    };
    // The grabber variant carries the class tm-sheet.js looks up to choose captureOn 'press'.
    // Without it the stub is a header, which waits for the 6px arm before capturing.
    if (options.grabber) el.classList = { contains: (name) => name === 'tm-sheet__handle' };
    return el;
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
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientX: 0, clientY: 100, target: button });
    grab.dispatch('pointermove', { pointerId: 1, clientX: 0, clientY: 102 });
    assert.equal(sheet.inline.height ?? '', '', 'a click on the close button must not drag the sheet');

    grab.dispatch('pointermove', { pointerId: 1, clientX: 0, clientY: 112 });
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

test('the review cases: settle with dismissal off never dismisses, at any velocity or start', () => {
    // Code-review round 5 pins these two exact calls: a flick from the lowest snap, and a fast
    // flick from full, both with swipeToDismiss=false.
    for (const [released, velocity, start] of [[0.45, 1, 0.5], [0.9, 3, 1]]) {
        const result = settle(snaps, released, false, velocity, start);
        assert.equal(result.dismiss, false, `settle(snaps, ${released}, false, ${velocity}, ${start}) must not dismiss`);
    }
});

test('the net direction picks the flick branch, not the tail velocity sign', () => {
    // A net-downward drag whose tail flick points up steps DOWN from where it started (the net
    // gesture wins); the pre-round-5 code read the tail sign alone and grew the sheet instead.
    const down = settle(snaps, 0.49, true, -1, 1, true);
    assert.equal(down.dismiss, false);
    assert.equal(down.index, 0, 'a net-downward drag steps down even when the tail points up');

    // A net-upward drag with a downward tail never dismisses and never steps down.
    const up = settle(snaps, 0.475, true, 3, 0.5, false);
    assert.equal(up.dismiss, false, 'a net-upward drag never dismisses');
    assert.equal(up.index, 0, 'a net-upward drag with a downward tail stays at the lowest snap');
});

// ── 3-snap sheet, SwipeToDismiss=false (cluster-B 8.6) ─────────────────────────
// The docs used to claim that with the flag off "a fast flick settles at the lowest snap". Wrong
// for three or more snaps: a flick steps ONE snap in its direction like any flick, and the lowest
// snap is only reached because the step CLAMPS there — a fast downward flick from full lands on
// the middle snap. This pins the behaviour the docs must describe; it is a documentation test.

test('SwipeToDismiss=false on a 3-snap sheet: a fast downward flick from full steps ONE snap, not to the lowest', () => {
    const three = [0.3, 0.6, 1];

    // A 3 px/ms flick from full (release 0.9): one step down, to the middle snap — never a dismiss
    // and never straight to the lowest snap.
    const fast = settle(three, 0.9, false, 3, 1, true);
    assert.equal(fast.dismiss, false, 'with dismissal off, even a fast flick never dismisses');
    assert.equal(fast.index, 1, 'a fast flick steps ONE snap down from where it started');
    assert.equal(fast.snap, 0.6);

    // An ordinary 1 px/ms flick from the middle snap steps down to the lowest snap.
    const medium = settle(three, 0.55, false, 1, 0.6, true);
    assert.equal(medium.dismiss, false);
    assert.equal(medium.index, 0);

    // A fast flick FROM the lowest snap clamps at it — the step has nowhere further down to go.
    const fromLowest = settle(three, 0.25, false, 3, 0.3, true);
    assert.equal(fromLowest.dismiss, false, 'the flag off keeps even the fastest flick attached');
    assert.equal(fromLowest.index, 0, 'the lowest snap is the clamp, not a destination of one flick');
});

// ── Pinned-on-top registry (F3 review round 3, R3-M1) ─────────────────────────
// promote() used to leave a toast container exactly where the last push put it: any drawer,
// sheet or dialog promoted AFTER the toast painted above it. Toast containers now register as
// "pinned on top": promote()/raise() re-raises every registered pinned root that is still
// popover-open, so toasts stay above every modal surface, always.

function popoverStub(name, open = false) {
    const calls = [];
    const el = {
        name,
        calls,
        isConnected: true,
        _open: open,
        matches: (selector) => selector === ':popover-open' ? el._open : false,
        showPopover() { el._open = true; calls.push('show'); },
        hidePopover() { el._open = false; calls.push('hide'); },
    };
    return el;
}

test('promote re-raises every pinned root that is popover-open, after its own promotion', () => {
    const toast = popoverStub('toast', true);
    const sheet = popoverStub('sheet');
    pinRoot(toast);
    try {
        promote(sheet);
        assert.equal(sheet._open, true);
        assert.deepEqual(toast.calls, ['hide', 'show'],
            'the toast pushed BEFORE the sheet must be re-raised above it');
    }
    finally {
        unpinRoot(toast);
    }
});

test('promote skips pinned roots that are not popover-open (an empty demoted container)', () => {
    const toast = popoverStub('toast', false);
    const sheet = popoverStub('sheet');
    pinRoot(toast);
    try {
        promote(sheet);
        assert.deepEqual(toast.calls, [],
            'a demoted (empty) container is not open — re-raising it would show an empty popover');
    }
    finally {
        unpinRoot(toast);
    }
});

test('promote skips the root it just promoted when that root is itself pinned', () => {
    const toast = popoverStub('toast');
    pinRoot(toast);
    try {
        promote(toast);
        assert.deepEqual(toast.calls, ['show'],
            'a pinned root promoting itself must show once, not hide+show itself a second time');
    }
    finally {
        unpinRoot(toast);
    }
});

test('promote drops disconnected pinned roots from the registry', () => {
    const toast = popoverStub('toast', true);
    toast.isConnected = false;
    pinRoot(toast);
    promote(popoverStub('sheet'));
    // No hide/show on the dead root: it is unregistered instead. Re-promoting proves the set no
    // longer holds it (no calls recorded even though it reports :popover-open).
    promote(popoverStub('dialog'));
    assert.deepEqual(toast.calls, []);
    unpinRoot(toast);
});

test('raise lands the pushing pinned root on top of the other pinned roots', () => {
    const a = popoverStub('a', true);
    const b = popoverStub('b', true);
    pinRoot(a);
    pinRoot(b);
    try {
        raise(b);
        // The other pinned root re-raises first, the pusher last — the pushed toast paints topmost.
        assert.deepEqual(a.calls.at(-2), 'hide');
        assert.deepEqual(a.calls.at(-1), 'show');
        assert.deepEqual(b.calls.at(-1), 'show');
        assert.ok(b.calls.length < a.calls.length + 1,
            'the pusher is hidden and shown exactly once');
    }
    finally {
        unpinRoot(a);
        unpinRoot(b);
    }
});

test('an inline sheet accepts an explicit basis element, not a hard-coded .tm-drawer lookup', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(200);
    // The panel has NO .tm-drawer ancestor (an embedded editor shell sheet): closest stays null.
    sheet.closest = () => null;
    const basis = { getBoundingClientRect: () => ({ height: 400 }) };
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'explicit-basis', { track: false, basis });

    // 40px down of a 200px sheet is 0.4 of the 400px basis. Of the 800px viewport the same drag
    // would dismiss, so a snap here proves the explicit basis won.
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 140, timeStamp: 200 });
    grab.dispatch('pointerup', { pointerId: 1, clientY: 140, timeStamp: 400 });
    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync', 'a small drag inside the explicit basis must not dismiss');
    delete globalThis.window;
});

test('a legacy boolean track argument still works alongside the options object', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(200);
    const hostBox = { getBoundingClientRect: () => ({ height: 400 }) };
    sheet.closest = () => hostBox;
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'legacy-inline', false);

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, clientY: 140, timeStamp: 200 });
    grab.dispatch('pointerup', { button: 0, pointerId: 1, clientY: 140, timeStamp: 400 });
    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync');
    delete globalThis.window;
});

test('trackViewport writes the lengths on an explicit tracking root when given one', () => {
    const makeRoot = () => {
        const root = { props: new Map() };
        root.style = { setProperty(name, value) { root.props.set(name, value); } };
        root.classList = { contains: () => false, toggle: () => {} };
        return root;
    };
    const root = makeRoot();
    const explicit = makeRoot();
    const viewport = { height: 430, offsetTop: 12 };
    globalThis.window = {
        innerHeight: 844,
        visualViewport: viewport,
        addEventListener() {},
        removeEventListener() {},
    };
    viewport.addEventListener = () => {};
    viewport.removeEventListener = () => {};

    const stop = trackViewport(root, null, explicit);
    assert.equal(explicit.props.get('--tm-sheet-viewport'), '430px', 'the explicit root carries the lengths');
    assert.equal(root.props.size, 0, 'the sheet root itself is left alone');
    stop();
    delete globalThis.window;
});

test('attachGesture forwards an explicit viewport tracking root', () => {
    installWindow(430);
    const sheet = panel(400);
    const trackingRoot = { props: new Map() };
    trackingRoot.style = { setProperty(name, value) { trackingRoot.props.set(name, value); } };
    trackingRoot.classList = { contains: () => false, toggle: () => {} };
    sheet.closest = () => null;
    const sink = host();
    attachGesture(null, sheet, [1], false, sink, 'explicit-track-root', { viewportRoot: trackingRoot });

    assert.equal(trackingRoot.props.get('--tm-sheet-viewport'), '430px');
    assert.equal(sheet.props.size, 0, 'no class-name closest lookup ran on the panel');
    delete globalThis.window;
});

// F16 (F6 round 1): tm-sheet.js re-exports the top-layer helpers, it does not copy them. Two
// copies would mean two pinned-root registries — a toast container pinned through one module
// would never be re-raised by a promote through the other.
test('tm-sheet.js re-exports the very same helpers as tm-top-layer.js (one registry)', async () => {
    const sheet = await import('../tm-sheet.js');
    const topLayer = await import('../tm-top-layer.js');

    for (const name of ['pinRoot', 'unpinRoot', 'promote', 'demote', 'raise']) {
        assert.equal(typeof topLayer[name], 'function', `${name} lives in tm-top-layer.js`);
        assert.strictEqual(sheet[name], topLayer[name], `${name} must be the same function object, not a copy`);
    }
});

// ── F19: the gesture runs on the shared pointer-drag helper ─────────────────
// A second finger must not hijack an active drag, Escape mid-drag rolls the sheet back to the
// snap C# owns (without closing the dialog around it), a lost pointer capture cancels silently,
// and a header press never captures before the drag actually starts.

function docStub() {
    const listeners = new Map();
    return {
        addEventListener(type, fn) { listeners.set(type, [...(listeners.get(type) ?? []), fn]); },
        removeEventListener(type, fn) {
            listeners.set(type, (listeners.get(type) ?? []).filter(entry => entry !== fn));
        },
        dispatch(type, event) {
            for (const fn of [...(listeners.get(type) ?? [])]) fn(event);
            return event;
        },
    };
}

test('a second finger cannot hijack an active sheet drag', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'hijack');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, isPrimary: true, clientY: 160, timeStamp: 100 });
    assert.equal(sheet.inline.height, '340px', 'the first finger has the drag');

    // A second finger lands mid-drag: it can neither move nor end the gesture.
    grab.dispatch('pointerdown', { button: 0, pointerId: 2, isPrimary: false, clientY: 400, timeStamp: 120 });
    grab.dispatch('pointermove', { pointerId: 2, isPrimary: false, clientY: 500, timeStamp: 140 });
    assert.equal(sheet.inline.height, '340px', 'the second finger must not move the sheet');

    grab.dispatch('pointerup', { pointerId: 1, isPrimary: true, clientY: 160, timeStamp: 150 });
    assert.equal(sheet.inline.height, '', 'the first finger\'s release still settles the sheet');
    assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync',
        '340 of 800 is a small drag; it settles on the nearest snap, exactly as without the second finger');
    delete globalThis.window;
});

test('Escape during a sheet drag restores the snap and does not propagate', () => {
    installWindow(800);
    const documentStub = docStub();
    globalThis.document = documentStub;
    try {
        const grab = handle();
        const sheet = panel(400);
        const sink = host();
        attachGesture(grab, sheet, [0.5, 1], true, sink, 'escape');

        grab.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientY: 100, timeStamp: 0 });
        grab.dispatch('pointermove', { pointerId: 1, isPrimary: true, clientY: 200, timeStamp: 100 });
        assert.equal(sheet.inline.height, '300px', 'the drag is live before Escape');

        const escape = { key: 'Escape', stopPropagation() { this.propagationStopped = true; } };
        documentStub.dispatch('keydown', escape);
        assert.equal(sheet.inline.height, '', 'Escape rolls the sheet back to the snap C# owns');
        assert.equal(sheet.inline.transition, '');
        assert.equal(sink.calls.length, 0, 'a cancelled drag reports nothing');
        assert.equal(escape.propagationStopped, true,
            'the Escape is consumed by the drag; it must not close the dialog around the sheet');

        // The sheet is still draggable afterwards.
        grab.dispatch('pointerdown', { button: 0, pointerId: 2, isPrimary: true, clientY: 100, timeStamp: 0 });
        grab.dispatch('pointermove', { pointerId: 2, isPrimary: true, clientY: 112, timeStamp: 100 });
        grab.dispatch('pointerup', { pointerId: 2, isPrimary: true, clientY: 112, timeStamp: 100 });
        assert.equal(sink.calls[0]?.[0], 'HandleSheetSnappedAsync', 'a fresh drag after the cancel still works');
    }
    finally {
        delete globalThis.document;
        delete globalThis.window;
    }
});

test('lostpointercapture cancels without HandleSheetSnappedAsync', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'lost-capture');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientY: 100, timeStamp: 0 });
    grab.dispatch('pointermove', { pointerId: 1, isPrimary: true, clientY: 200, timeStamp: 100 });
    grab.dispatch('lostpointercapture', { pointerId: 1 });

    assert.equal(sheet.inline.height, '', 'the drag is rolled back');
    assert.equal(sheet.inline.transition, '');
    assert.equal(sink.calls.length, 0, 'a lost capture reports nothing, like a pointercancel');
    delete globalThis.window;
});

test('a header tap on the close button is not captured (captureOn start)', () => {
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'header-tap');

    const button = { closest: () => button };
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientY: 100, target: button, timeStamp: 0 });
    assert.equal(grab.captured.size, 0, 'a press on the header must not take pointer capture yet');
    grab.dispatch('pointerup', { pointerId: 1, isPrimary: true, clientY: 100, target: button, timeStamp: 50 });

    assert.equal(sheet.inline.height ?? '', '');
    assert.equal(sheet.inline.transition ?? '', '');
    assert.equal(sink.calls.length, 0, 'a tap never reaches the host');
    assert.equal(grab.captured.size, 0, 'a tap never captures, so the click reaches the close button');
    delete globalThis.window;
});

// ── F19 fixes: capture branch coverage, the flick, and the sideways drift ──────

test('a press on the grabber captures at once; a header press does not capture', () => {
    installWindow(800);
    const grabber = handle({ grabber: true });
    const sheet = panel(400);
    const sink = host();
    attachGesture(grabber, sheet, [0.5, 1], true, sink, 'grabber');

    grabber.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientY: 100 });
    assert.equal(grabber.captured.has(1), true,
        'the grabber captures on press, before any move — a fast drag may leave the 44px handle');

    const header = handle();
    const headerSheet = panel(400);
    attachGesture(header, headerSheet, [0.5, 1], true, host(), 'header');

    header.dispatch('pointerdown', { button: 0, pointerId: 2, isPrimary: true, clientY: 100 });
    assert.equal(header.captured.has(2), false, 'a header press waits for the drag to start');
    delete globalThis.window;
});

test('a fast flick from the half snap dismisses (the E2E FastFlick sequence)', () => {
    // SheetRound3E2ETests.FastFlick_AtHalf_Dismisses: down, +12, +70, up in ONE JS turn. The
    // browser stamps those events a fraction of a ms apart, so the 80ms sample window still holds
    // the press sample and the 70px tail collapses to the 1ms floor — a ~70 px/ms flick.
    installWindow(390);
    const grab = handle({ grabber: true });
    const sheet = panel(195); // the half snap of a 390px viewport
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'fast-flick');

    grab.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientY: 195, timeStamp: 1000 });
    assert.equal(grab.captured.has(1), true, 'the grabber captured the press before any move');
    grab.dispatch('pointermove', { pointerId: 1, isPrimary: true, clientY: 207, timeStamp: 1000.1 });
    grab.dispatch('pointermove', { pointerId: 1, isPrimary: true, clientY: 265, timeStamp: 1000.2 });
    grab.dispatch('pointerup', { pointerId: 1, isPrimary: true, clientY: 265, timeStamp: 1000.3 });

    assert.equal(sink.calls[0]?.[0], 'HandleSheetDismissedAsync',
        '125/390 is past the lowest-snap margin and the tail is a fast flick — the sheet dismisses');
    delete globalThis.window;
});

test('a sideways drift on a header button starts no drag and leaves the click alone', () => {
    // pointer-drag arms on Math.hypot, so a purely sideways move would start a drag from the
    // close button and swallow its click. The old code armed on VERTICAL movement only; the
    // gesture vetoes what a sideways drift starts on a control.
    installWindow(800);
    const grab = handle();
    const sheet = panel(400);
    const sink = host();
    attachGesture(grab, sheet, [0.5, 1], true, sink, 'header-side');

    const button = { closest: (selector) => selector.includes('button') ? button : null };
    grab.dispatch('pointerdown', { button: 0, pointerId: 1, isPrimary: true, clientX: 10, clientY: 100, target: button });
    grab.dispatch('pointermove', { pointerId: 1, isPrimary: true, clientX: 20, clientY: 100 }); // dx=10, dy=0
    grab.dispatch('pointerup', { pointerId: 1, isPrimary: true, clientX: 20, clientY: 100 });

    assert.equal(sheet.inline.height ?? '', '', 'a sideways drift must not drag the sheet');
    assert.equal(sink.calls.length, 0, 'a vetoed gesture reports nothing to the host');
    assert.equal(grab.listeners.has('click'), false, 'no click suppression may be armed');
    const click = { preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } };
    grab.dispatch('click', click);
    assert.notEqual(click.prevented, true, 'the click must reach the button');
    assert.notEqual(click.stopped, true, 'the click must not be stopped');
    delete globalThis.window;
});
