import test from 'node:test';
import assert from 'node:assert/strict';
import {
    clampScrollTop,
    focusFieldButton,
    isCoarsePointer,
    nearestScrollable,
    prefersReducedMotion,
    scrollBehavior,
    scrollTargetFor,
    scrollToField
} from '../signing-runner.js';

const matchMedia = (matchesByQuery) => (query) => ({ matches: matchesByQuery[query] ?? false });

// ── isCoarsePointer ────────────────────────────────────────────

test('a coarse pointer is reported through matchMedia', () => {
    assert.equal(
        isCoarsePointer(matchMedia({ '(pointer: coarse)': true })),
        true);
});

test('a fine pointer is not coarse', () => {
    assert.equal(
        isCoarsePointer(matchMedia({ '(pointer: coarse)': false, '(pointer: fine)': true })),
        false);
});

test('a runtime without matchMedia reports a fine pointer', () => {
    assert.equal(isCoarsePointer(undefined), false);
});

// ── reduced motion ─────────────────────────────────────────────

test('prefers-reduced-motion: reduce is honoured', () => {
    assert.equal(
        prefersReducedMotion(matchMedia({ '(prefers-reduced-motion: reduce)': true })),
        true);
});

test('the default animation preference scrolls smoothly', () => {
    assert.equal(
        prefersReducedMotion(matchMedia({ '(prefers-reduced-motion: reduce)': false })),
        false);
    assert.equal(scrollBehavior(false), 'smooth');
});

test('reduced motion scrolls instantly (behavior: auto)', () => {
    assert.equal(scrollBehavior(true), 'auto');
});

// ── scrollTargetFor (the single-scroll math) ───────────────────

// Field rects also carry the horizontal extent the sheet-overlap guard reads (left/right only
// matter to scrollToField; scrollTargetFor sees top/bottom).
const rect = (top, bottom, left = 0, right = 400) => ({ top, bottom, left, right });

test('the field is centred in the space above the sheet', () => {
    // Viewport 844 tall, the sticky sheet starts at 500: usable space is 0…484 (500 − 16 gap).
    // The field at 700–760 sits under the sheet; centring puts the field centre (730) at the
    // space centre (242): delta +488 from scroll 0 → target 488 (scroll DOWN lifts the field).
    assert.equal(scrollTargetFor(rect(700, 760), 0, 844, 0, 500), 488);
});

test('a field above the viewport is centred back into the usable space', () => {
    // Field scrolled past (top −60, bottom 0) at scroll 900: the centred delta (−272) satisfies
    // both bounds (top lands at 212 ≥ spaceTop, bottom 272 ≤ spaceBottom), so the centre wins.
    assert.equal(scrollTargetFor(rect(-60, 0), 0, 844, 900, 500), 628);
});

test('a field under the sheet is lifted so its centre sits in the usable space', () => {
    // Space 0…484, field 460–520 (bottom 36 under the usable floor): the centre delta (248)
    // is inside the clamp band [36, 460], so the centre wins: 100 + 248.
    assert.equal(scrollTargetFor(rect(460, 520), 0, 844, 100, 500), 348);
});

test('a taller-than-space field pins its top to the viewport top', () => {
    // deltaMax (top at viewport top) < deltaMin → the smaller clamp wins: top stays visible.
    assert.equal(scrollTargetFor(rect(-100, 900), 0, 844, 200, 500), 100);
});

test('no sheet centres the field in the whole scroller viewport (desktop left column)', () => {
    assert.equal(scrollTargetFor(rect(300, 360), 0, 900, 50, null), 50 - 120);
});

test('clampScrollTop keeps the target inside the scroller range', () => {
    assert.equal(clampScrollTop(-20, 600), 0);
    assert.equal(clampScrollTop(900, 600), 600);
    assert.equal(clampScrollTop(300, 600), 300);
    assert.equal(clampScrollTop(300, -5), 0, 'a degenerate scroller has no range');
});

// ── nearestScrollable ──────────────────────────────────────────

test('nearestScrollable returns null with no parent chain', () => {
    assert.equal(nearestScrollable({ parentElement: null }), null);
});

test('nearestScrollable returns null for an orphan element', () => {
    assert.equal(nearestScrollable({}), null);
});

// ── scrollToField ──────────────────────────────────────────────

function fakeField(fieldRect) {
    return {
        rect: fieldRect,
        getBoundingClientRect() { return fieldRect; }
    };
}

function fakeScroller({ top = 0, height = 800, scrollTop = 0, scrollHeight = 2000, clientHeight = 800, overflowY = 'visible' } = {}) {
    const calls = [];
    return {
        calls,
        top,
        height,
        scrollTop,
        scrollHeight,
        clientHeight,
        computed: { overflowY },
        getBoundingClientRect() { return { top, height }; },
        scrollTo(options) { calls.push(options); }
    };
}

function fakeWindow({ innerHeight = 844, scrollY = 0, documentHeight = 2000 } = {}) {
    const calls = [];
    return {
        calls,
        innerHeight,
        scrollY,
        document: { documentElement: { scrollHeight: documentHeight } },
        matchMedia: (query) => ({ matches: false }),
        scrollTo(options) { calls.push(options); }
    };
}

/** A document whose querySelector resolves the field / sheet and whose defaultView reads the
    stubbed `computed` overflow style (the node lane's `getComputedStyle` stand-in). */
function fakeDocument(field, sheet, scrollerElement = null) {
    return {
        defaultView: { getComputedStyle: (el) => el.computed ?? { overflowY: 'visible' } },
        querySelector(selector) {
            if (selector.startsWith('.tm-signing-field')) return field;
            if (selector === '.tm-signing-form-runner__steps') return sheet;
            return scrollerElement;
        }
    };
}

test('scrollToField centres the field above the sheet with ONE window scroll (smooth)', () => {
    const field = fakeField(rect(700, 760));
    const sheet = { getBoundingClientRect: () => ({ top: 500, bottom: 844, left: 0, right: 400 }) };
    const win = fakeWindow({ innerHeight: 844, scrollY: 200, documentHeight: 3000 });

    scrollToField('sig-1', { document: fakeDocument(field, sheet), window: win });

    // Usable space 0…484; centre 242; field centre 730 → delta +488 → target 688 (200 + 488).
    assert.deepEqual(win.calls, [{ top: 688, behavior: 'smooth' }], 'exactly one scroll');
});

test('a field beside the docked desktop rail is not lifted', () => {
    // Desktop 1440: the rail occupies 700…1000 with its top at 50, the field column 0…600 —
    // no horizontal overlap, so the rail must NOT cut the usable space: the field is centred
    // exactly as with no sheet (the old guard lifted the field by the rail's top and pushed it
    // under the document column's own fold).
    const field = fakeField(rect(300, 360, 0, 600));
    const rail = { getBoundingClientRect: () => ({ top: 50, bottom: 900, left: 700, right: 1000 }) };
    const win = fakeWindow({ innerHeight: 900, scrollY: 200, documentHeight: 3000 });

    scrollToField('sig-1', { document: fakeDocument(field, rail), window: win });

    // No-sheet centre: space 0…900, centre 450; field centre 330 → delta −120 → target 80.
    assert.deepEqual(win.calls, [{ top: 80, behavior: 'smooth' }]);
});

test('a sheet scrolled past the scroller viewport top does not cut the space', () => {
    // The sheet's top sits ABOVE the scroller viewport top (element scrolled past it): it covers
    // nothing the scroller can show, so the whole viewport is usable.
    const field = fakeField(rect(300, 360));
    const sheet = { getBoundingClientRect: () => ({ top: -20, bottom: 400, left: 0, right: 400 }) };
    const win = fakeWindow();
    const scroller = fakeScroller({ top: 100, height: 500, scrollTop: 100, scrollHeight: 2000, clientHeight: 500, overflowY: 'auto' });

    const doc = fakeDocument({ ...field, parentElement: scroller }, sheet);

    scrollToField('sig-1', { document: doc, window: win });

    // Space 100…600 (viewport, no sheet cut), centre 350; field centre 330 → delta −20 →
    // target 80 (100 − 20).
    assert.deepEqual(scroller.calls, [{ top: 80, behavior: 'smooth' }]);
});

test('scrollToField scrolls a NON-WINDOW scroller (the sheet-internal ancestor)', () => {
    const field = fakeField(rect(600, 660));
    const sheet = { getBoundingClientRect: () => ({ top: 500, left: 0, right: 400 }) };
    const win = fakeWindow();
    const scroller = fakeScroller({ top: 0, height: 800, scrollTop: 100, scrollHeight: 2500, clientHeight: 800, overflowY: 'auto' });

    const doc = fakeDocument({ ...field, parentElement: scroller }, sheet);

    scrollToField('sig-1', { document: doc, window: win });

    assert.equal(win.calls.length, 0, 'the window never scrolls when an ancestor overflows');
    // Usable space 0…484, centre 242; field centre 630 → delta +388 → target 488 (100 + 388).
    assert.deepEqual(scroller.calls, [{ top: 488, behavior: 'smooth' }]);
});

test('scrollToField keeps the element target inside its scroll range', () => {
    const field = fakeField(rect(600, 660));
    const win = fakeWindow();
    const scroller = fakeScroller({ top: 0, height: 500, scrollTop: 100, scrollHeight: 700, clientHeight: 500, overflowY: 'scroll' });

    const doc = fakeDocument({ ...field, parentElement: scroller }, null);

    scrollToField('sig-1', { document: doc, window: win });

    // No sheet: space 0…500, centre 250; field centre 630 → delta +380 → target 480, clamped
    // to the scroller's own range (scrollHeight − clientHeight = 200).
    assert.deepEqual(scroller.calls, [{ top: 200, behavior: 'smooth' }]);
});

test('scrollToField honours reduced motion with behavior auto', () => {
    const field = fakeField(rect(700, 760));
    const sheet = { getBoundingClientRect: () => ({ top: 500, left: 0, right: 400 }) };
    const win = fakeWindow({ scrollY: 200, documentHeight: 3000 });
    win.matchMedia = (query) => ({ matches: query === '(prefers-reduced-motion: reduce)' });

    scrollToField('sig-1', { document: fakeDocument(field, sheet), window: win });

    assert.equal(win.calls[0].behavior, 'auto');
});

test('scrollToField without a matching field does nothing', () => {
    const win = fakeWindow();
    scrollToField('missing', { document: fakeDocument(null, null), window: win });

    assert.equal(win.calls.length, 0);
});

test('scrollToField without a uuid does nothing', () => {
    const win = fakeWindow();
    scrollToField('', { document: fakeDocument(fakeField(rect(0, 1)), null), window: win });

    assert.equal(win.calls.length, 0);
});

// ── focusFieldButton ───────────────────────────────────────────

test('focusFieldButton scrolls the row into view and focuses it', () => {
    const calls = [];
    const button = {
        focus() { calls.push('focus'); },
        scrollIntoView(options) { calls.push(options); }
    };
    const doc = {
        querySelector(selector) {
            assert.match(selector, /tm-signing-form-runner__field-button/);
            assert.match(selector, /field-3/);
            return button;
        }
    };

    focusFieldButton('field-3', { document: doc });

    assert.deepEqual(calls, [{ block: 'nearest' }, 'focus']);
});

test('focusFieldButton without a match does nothing', () => {
    const doc = { querySelector: () => null };
    assert.doesNotThrow(() => focusFieldButton('missing', { document: doc }));
});

test('focusFieldButton without a uuid does nothing', () => {
    assert.doesNotThrow(() => focusFieldButton('', { document: { querySelector: () => { throw new Error('never'); } } }));
});
