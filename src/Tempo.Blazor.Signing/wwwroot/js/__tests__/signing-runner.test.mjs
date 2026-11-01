import test from 'node:test';
import assert from 'node:assert/strict';
import {
    clampScrollTop,
    focusFieldButton,
    isCoarsePointer,
    nearestScrollable,
    prefersReducedMotion,
    scrollBehavior,
    scrollIntoView,
    resolveSheetCeiling,
    scrollTargetFor,
    scrollToField,
    attachViewerPinch
} from '../signing-runner.js';

const matchMedia = (matchesByQuery) => (query) => ({ matches: matchesByQuery[query] ?? false });

test('viewer pinch commits the shared helper scale against the current scale', () => {
    const listeners = new Map();
    const element = {
        addEventListener(type, handler) { listeners.set(type, handler); },
        removeEventListener() { },
        style: {},
    };
    let committed = 0;
    const session = attachViewerPinch(element, {
        getScale: () => 1.25,
        onCommit: scale => { committed = scale; },
        attachPinch(_element, options) {
            options.onStart();
            options.onEnd({ scale: 1.2 });
            return { detach() { } };
        },
    });

    assert.equal(committed, 1.5);
    assert.equal(typeof session.detach, 'function');
});

test('viewer pinch without the shared helper is a no-op', () => {
    const session = attachViewerPinch({ addEventListener() { } }, { onCommit() { throw new Error('must not commit'); } });
    session.detach();
});

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
        getBoundingClientRect() { return fieldRect; },
        closest(selector) {
            const className = selector.startsWith('.') ? selector.slice(1) : selector;
            let current = this;
            while (current) {
                if (current.classList?.includes(className)) {
                    return current;
                }

                current = current.parentElement;
            }

            return null;
        }
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
        closest(selector) {
            const className = selector.startsWith('.') ? selector.slice(1) : selector;
            let current = this;
            while (current) {
                if (current.classList?.includes(className)) {
                    return current;
                }

                current = current.parentElement;
            }

            return null;
        },
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
            if (selector === '.tm-signing-form-runner__steps .tm-drawer__panel') return sheet;
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

test('a multi-page runner scrolls the pages wrapper, not the per-page document', () => {
    const field = fakeField(rect(700, 760));
    const page = fakeScroller({ top: 80, height: 200, scrollTop: 0, scrollHeight: 900, clientHeight: 200, overflowY: 'auto' });
    page.classList = ['tm-signing-form-runner__document'];
    const pages = fakeScroller({ top: 0, height: 500, scrollTop: 0, scrollHeight: 1800, clientHeight: 500, overflowY: 'auto' });
    pages.classList = ['tm-signing-form-runner__pages'];
    page.parentElement = pages;
    field.parentElement = page;
    pages.scrollHeight = 1800;
    pages.clientHeight = 500;
    const sheet = {
        getBoundingClientRect: () => ({ top: 400, bottom: 844, left: 0, right: 390 }),
        contains: () => false,
    };
    const win = fakeWindow({ innerHeight: 844, scrollY: 0, documentHeight: 4000 });

    scrollToField('sig-1', { document: fakeDocument(field, sheet), window: win });

    assert.equal(page.calls.length, 0, 'the per-page document is not the snap scroller');
    assert.equal(pages.calls.length, 1, 'the pages wrapper moves the field above the panel');
    assert.equal(win.calls.length, 0);
});

test('a pages wrapper that does not overflow lets the window move the field', () => {
    const field = fakeField(rect(700, 760));
    const pages = fakeScroller({ top: 0, height: 1800, scrollTop: 0, scrollHeight: 1800, clientHeight: 1800, overflowY: 'visible' });
    pages.classList = ['tm-signing-form-runner__pages'];
    pages.scrollHeight = 1800;
    pages.clientHeight = 1800;
    field.parentElement = pages;
    const sheet = {
        getBoundingClientRect: () => ({ top: 400, bottom: 844, left: 0, right: 390 }),
        contains: () => false,
    };
    const win = fakeWindow({ innerHeight: 844, scrollY: 0, documentHeight: 4000 });

    scrollToField('sig-1', { document: fakeDocument(field, sheet), window: win });

    assert.equal(pages.calls.length, 0, 'a non-overflowing pages wrapper is not the scroller');
    assert.equal(win.calls.length, 1, 'the window lifts the field above the sticky sheet');
});

test('a clipped documents frame scrolls the pages wrapper even when it does not overflow yet', () => {
    const field = fakeField(rect(700, 760));
    const pages = fakeScroller({ top: 0, height: 500, scrollTop: 0, scrollHeight: 500, clientHeight: 500, overflowY: 'auto' });
    pages.classList = ['tm-signing-form-runner__pages'];
    pages.scrollHeight = 500;
    pages.clientHeight = 500;
    const frame = fakeScroller({ top: 0, height: 844, scrollTop: 0, scrollHeight: 844, clientHeight: 844, overflowY: 'hidden' });
    frame.classList = ['tm-signing-form-runner__documents'];
    pages.parentElement = frame;
    field.parentElement = pages;
    const sheet = {
        getBoundingClientRect: () => ({ top: 400, bottom: 844, left: 0, right: 390 }),
        contains: () => false,
    };
    const win = fakeWindow({ innerHeight: 844, scrollY: 0, documentHeight: 4000 });

    scrollToField('sig-1', { document: fakeDocument(field, sheet), window: win });

    assert.equal(win.calls.length, 0, 'the window must not move a field the clipped frame hides');
    assert.equal(pages.calls.length, 1, 'the pages wrapper is the scroller inside a clipped frame');
});

test('a nested document overflow scrolls the field against an anchored sheet', () => {
    const field = fakeField(rect(700, 760));
    const inner = fakeScroller({ top: 0, height: 400, scrollTop: 0, scrollHeight: 900, clientHeight: 400, overflowY: 'auto' });
    field.parentElement = inner;
    const sheet = {
        getBoundingClientRect: () => ({ top: 139, bottom: 844, left: 0, right: 390 }),
        contains: () => false,
    };
    const win = fakeWindow({ innerHeight: 844, scrollY: 0, documentHeight: 4000 });

    scrollToField('sig-1', { document: fakeDocument(field, sheet), window: win });

    assert.equal(win.calls.length, 0, 'the window must not move the anchored sheet with the field');
    assert.equal(inner.calls.length, 1);
    assert.ok(inner.calls[0].top > 0, 'the field is lifted inside the page scroller');
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

test('a host top above the viewport is not the ceiling — the snap panel is', () => {
    const host = {
        getBoundingClientRect: () => ({ top: -200, bottom: 517, height: 717, left: 0, right: 390 }),
    };
    const drawer = {
        getAttribute(name) {
            if (name === 'data-snap-points') return '0.28,0.55,1';
            if (name === 'data-snap-index') return '1';
            return null;
        },
    };
    const panel = {
        closest(selector) {
            if (selector === '.tm-signing-form-runner__sheet-host') return host;
            if (selector === '.tm-drawer') return drawer;
            return null;
        },
        getBoundingClientRect: () => ({ top: -200, bottom: 517, height: 717, left: 0, right: 390 }),
    };
    const ceiling = resolveSheetCeiling(panel, panel.getBoundingClientRect(), 0, 844);
    // Snap 0.55 of the 717px host, anchored to the host bottom still in view.
    assert.ok(Math.abs(ceiling.top - (517 - 0.55 * 717)) < 0.5, `ceiling ${ceiling.top}`);
    assert.ok(ceiling.top > 0, 'the ceiling stays inside the viewport');
});

test('scrollToField lifts the field above a scrolled-away host using the snap panel', () => {
    const field = fakeField(rect(700, 760));
    const host = {
        getBoundingClientRect: () => ({ top: -200, bottom: 517, height: 717, left: 0, right: 390 }),
    };
    const drawer = {
        getAttribute(name) {
            if (name === 'data-snap-points') return '0.28,0.55,1';
            if (name === 'data-snap-index') return '1';
            return null;
        },
    };
    const panel = {
        closest(selector) {
            if (selector === '.tm-signing-form-runner__sheet-host') return host;
            if (selector === '.tm-drawer') return drawer;
            return null;
        },
        getBoundingClientRect: () => ({ top: -200, bottom: 517, height: 717, left: 0, right: 390 }),
    };
    const win = fakeWindow({ innerHeight: 844, scrollY: 200, documentHeight: 4000 });
    scrollToField('sig-1', { document: fakeDocument(field, panel), window: win });
    // No-sheet centre would land at 508. The snap ceiling must scroll further so the field clears it.
    assert.equal(win.calls.length, 1);
    assert.ok(win.calls[0].top > 508, `expected a lift above the snap panel, got ${win.calls[0].top}`);
});

test('scrollToField waits for a height transition, and reduced motion does not', async () => {
    const field = fakeField(rect(700, 760));
    const listeners = {};
    const panel = {
        getBoundingClientRect: () => ({ top: 500, bottom: 844, height: 344, left: 0, right: 390 }),
        closest: () => null,
        addEventListener(type, fn) { listeners[type] = fn; },
        removeEventListener(type) { delete listeners[type]; },
        computed: { overflowY: 'visible', transitionDuration: '0.2s', transitionProperty: 'height' },
    };
    const win = fakeWindow({ innerHeight: 844, scrollY: 0, documentHeight: 3000 });
    win.setTimeout = (fn) => { win.pending = fn; return 7; };
    win.clearTimeout = () => { win.cleared = true; };
    const pending = scrollToField('sig-1', { document: fakeDocument(field, panel), window: win });
    assert.equal(win.calls.length, 0, 'does not scroll while the snap is in flight');
    listeners.transitionend({ target: panel, propertyName: 'height' });
    await pending;
    assert.equal(win.calls.length, 1);
    assert.equal(win.cleared, true, 'the fallback timer is cleared');

    const reduced = fakeWindow({ innerHeight: 844, scrollY: 0, documentHeight: 3000 });
    reduced.matchMedia = (query) => ({ matches: query.includes('reduce') });
    scrollToField('sig-1', { document: fakeDocument(field, panel), window: reduced });
    assert.equal(reduced.calls.length, 1, 'reduced motion scrolls immediately');
});

test('scrollToField without a uuid does nothing', () => {
    const win = fakeWindow();
    scrollToField('', { document: fakeDocument(fakeField(rect(0, 1)), null), window: win });

    assert.equal(win.calls.length, 0);
});

// ── scrollIntoView (the phone step's OTP-row reveal, phase 6 review fix) ──

/** The OTP row inside a scrollable panel: the parent chain carries the overflowing scroller and
    `closest` resolves the panel scope whose sticky action bar covers the scrollport bottom. */
function fakeOtpRow({ rowRect, scrollerRect, barRect, stickyOverflow = true } = {}) {
    const scroller = {
        scrollTop: 100,
        scrollHeight: 2000,
        clientHeight: 600,
        computed: { overflowY: 'auto' },
        parentElement: null,
        getBoundingClientRect: () => scrollerRect
    };
    const scope = {
        querySelector(selector) {
            assert.equal(selector, '.tm-signing-form-runner__actions');
            return barRect === undefined ? null : { getBoundingClientRect: () => barRect };
        }
    };
    const row = {
        parentElement: scroller,
        closest(selector) {
            assert.equal(selector, '.tm-signing-form-runner__step-panel');
            return scope;
        },
        getBoundingClientRect: () => rowRect,
        scrollIntoView() { throw new Error('the sticky-aware reveal must scroll the ancestor, not the row'); }
    };
    const doc = {
        defaultView: { getComputedStyle: (el) => el.computed ?? { overflowY: 'visible' } },
        querySelector: () => row
    };
    return { row, scroller, stickyOverflow, doc };
}

test('scrollIntoView lifts the row bottom above the sticky action bar', () => {
    // The scrollport ends at 700; the sticky bar covers 630-700; the row's bottom (760) sits
    // below the bar top — the scroller must scroll DOWN by 760 - 630 = 130.
    const { row, scroller, doc } = fakeOtpRow({
        rowRect: { top: 640, bottom: 760 },
        scrollerRect: { top: 100, bottom: 700 },
        barRect: { top: 630, bottom: 700 }
    });

    const revealed = scrollIntoView('.tm-signing-phone-step__otp-row', { document: doc });

    assert.equal(revealed, true);
    assert.equal(scroller.scrollTop, 100 + 130);
});

test('scrollIntoView does not scroll when the row already fits above the bar', () => {
    const { scroller, doc } = fakeOtpRow({
        rowRect: { top: 500, bottom: 620 },
        scrollerRect: { top: 100, bottom: 700 },
        barRect: { top: 630, bottom: 700 }
    });

    scrollIntoView('.tm-signing-phone-step__otp-row', { document: doc });

    assert.equal(scroller.scrollTop, 100, 'a fully visible row never scrolls');
});

test('scrollIntoView lifts the row TOP when it starts above the scrollport', () => {
    // Row top (80) above the scrollport top (100): the scroller scrolls UP by 20.
    const { scroller, doc } = fakeOtpRow({
        rowRect: { top: 80, bottom: 180 },
        scrollerRect: { top: 100, bottom: 700 },
        barRect: { top: 630, bottom: 700 }
    });

    scrollIntoView('.tm-signing-phone-step__otp-row', { document: doc });

    assert.equal(scroller.scrollTop, 100 - 20);
});

test('scrollIntoView without a sticky bar aligns the row bottom to the scrollport', () => {
    const { scroller, doc } = fakeOtpRow({
        rowRect: { top: 640, bottom: 760 },
        scrollerRect: { top: 100, bottom: 700 },
        barRect: undefined
    });

    scrollIntoView('.tm-signing-phone-step__otp-row', { document: doc });

    assert.equal(scroller.scrollTop, 100 + (760 - 700), 'the plain scrollport bottom is the usable bottom');
});

test('scrollIntoView honours the scroller bottom padding when measuring the bar', () => {
    // The panel pads its bottom by 16: the bar bottom (684) sits at the CONTENT edge (700 - 16)
    // and must still count as the sticky bar (±2px) — the usable bottom is its top (610).
    const scroller = {
        scrollTop: 100,
        scrollHeight: 2000,
        clientHeight: 600,
        computed: { overflowY: 'auto', paddingBottom: '16px' },
        parentElement: null,
        getBoundingClientRect: () => ({ top: 100, bottom: 700 })
    };
    const scope = {
        querySelector: () => ({ getBoundingClientRect: () => ({ top: 610, bottom: 684 }) })
    };
    const row = {
        parentElement: scroller,
        closest: () => scope,
        getBoundingClientRect: () => ({ top: 640, bottom: 760 })
    };
    const doc = {
        defaultView: { getComputedStyle: (el) => el.computed ?? { overflowY: 'visible' } },
        querySelector: () => row
    };

    scrollIntoView('.tm-signing-phone-step__otp-row', { document: doc });

    assert.equal(scroller.scrollTop, 100 + (760 - 610), 'the usable bottom ends above the padded sticky bar');
});

test('scrollIntoView falls back to scrollIntoView without an overflowing ancestor', () => {
    const calls = [];
    const row = {
        parentElement: null,
        getBoundingClientRect: () => ({ top: 0, bottom: 10 }),
        closest: () => null,
        scrollIntoView(options) { calls.push(options); }
    };
    const doc = {
        defaultView: { getComputedStyle: (el) => el.computed ?? { overflowY: 'visible' } },
        querySelector: () => row
    };

    const revealed = scrollIntoView('.tm-signing-phone-step__otp-row', { document: doc });

    assert.equal(revealed, true);
    assert.deepEqual(calls, [{ block: 'nearest' }]);
});

test('scrollIntoView without a match reports false and does nothing', () => {
    const doc = { querySelector: () => null, defaultView: { getComputedStyle: () => ({ overflowY: 'visible' }) } };
    assert.equal(scrollIntoView('.missing', { document: doc }), false);
});

test('scrollIntoView with an empty selector does nothing', () => {
    assert.doesNotThrow(() => scrollIntoView('', { document: { querySelector: () => { throw new Error('never'); } } }));
    assert.equal(scrollIntoView('', { document: { querySelector: () => { throw new Error('never'); } } }), false);
});

test('scrollIntoView with a malformed selector reports false instead of throwing', () => {
    assert.equal(scrollIntoView('###', { document: { querySelector: () => { throw new Error('bad selector'); } } }), false);
});

test('scrollIntoView without a document does nothing', () => {
    assert.equal(scrollIntoView('.tm-signing-phone-step__otp-row', { document: null }), false);
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
