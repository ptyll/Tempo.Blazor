import test from 'node:test';
import assert from 'node:assert/strict';
import { anchorIntersectsViewport, resolvePlacement, positionFloating, readViewport } from '../overlay.js';

// Viewport used by all tests unless overridden.
const VIEW = { viewWidth: 1280, viewHeight: 800 };
const PANEL = { width: 200, height: 150 };

function anchor(over = {}) {
    return { top: 100, left: 100, right: 220, bottom: 140, width: 120, height: 40, ...over };
}

function opts(over = {}) {
    return { placement: 'bottom', align: 'start', offset: 4, margin: 8, ...VIEW, ...over };
}

test('bottom + start places below the anchor, left-aligned', () => {
    const r = resolvePlacement(anchor(), PANEL, opts());
    assert.equal(r.side, 'bottom');
    assert.equal(r.y, 144);       // anchor.bottom + offset
    assert.equal(r.x, 100);       // anchor.left
});

test('top + start places above the anchor when it fits', () => {
    // Anchor near the bottom leaves real room above — no flip.
    const a = anchor({ top: 700, bottom: 740 });
    const r = resolvePlacement(a, PANEL, opts({ placement: 'top' }));
    assert.equal(r.side, 'top');
    assert.equal(r.y, 700 - 4 - 150); // anchor.top - offset - height
    assert.equal(r.x, 100);
});

test('align end right-aligns the panel to the anchor', () => {
    const r = resolvePlacement(anchor(), PANEL, opts({ align: 'end' }));
    assert.equal(r.x, 220 - 200); // anchor.right - panel.width
});

test('align center centers the panel on the anchor', () => {
    const r = resolvePlacement(anchor(), PANEL, opts({ align: 'center' }));
    assert.equal(r.x, 100 + (120 - 200) / 2); // 60
});

test('flip: no room below flips to top', () => {
    // Anchor near the viewport bottom: only 60px below, plenty above.
    const a = anchor({ top: 700, bottom: 740 });
    const r = resolvePlacement(a, PANEL, opts());
    assert.equal(r.side, 'top');
    assert.equal(r.y, 700 - 4 - 150);
});

test('flip: no room above flips a top placement to bottom', () => {
    const a = anchor({ top: 60, bottom: 100 });
    const r = resolvePlacement(a, PANEL, opts({ placement: 'top' }));
    assert.equal(r.side, 'bottom');
    assert.equal(r.y, 104);
});

test('flip does not trigger when the opposite side is equally cramped', () => {
    // Panel taller than both sides -> preferred side wins.
    const a = anchor({ top: 300, bottom: 340 });
    const big = { width: 200, height: 700 };
    const r = resolvePlacement(a, big, opts());
    assert.equal(r.side, 'bottom');
});

test('flip: false keeps the preferred side even without room', () => {
    const a = anchor({ top: 700, bottom: 740 });
    const r = resolvePlacement(a, PANEL, opts({ flip: false }));
    assert.equal(r.side, 'bottom');
});

test('shift: panel wider than the space to the right clamps into the viewport', () => {
    const a = anchor({ left: 1200, right: 1240, width: 40 });
    const r = resolvePlacement(a, PANEL, opts());
    // 200-wide panel from x=1200 would end at 1400 > 1280-8; clamped to 1280-8-200.
    assert.equal(r.x, 1280 - 8 - 200);
});

test('shift: keeps the margin at the left edge', () => {
    const a = anchor({ left: -150, right: -30, width: 120 });
    const r = resolvePlacement(a, PANEL, opts());
    assert.equal(r.x, 8); // margin
});

test('shift: false leaves an off-viewport x alone', () => {
    const a = anchor({ left: 1200, right: 1240, width: 40 });
    const r = resolvePlacement(a, PANEL, opts({ shift: false }));
    assert.equal(r.x, 1200);
});

test('main-axis safety clamp: a panel taller than the viewport stays inside', () => {
    const a = anchor({ top: 400, bottom: 440 });
    const huge = { width: 200, height: 2000 };
    const r = resolvePlacement(a, huge, opts());
    // 2000-tall panel cannot fit anywhere; clamped to the top margin.
    assert.equal(r.y, 8);
});

test('right placement sits right of the anchor, start-aligned on the vertical axis', () => {
    const r = resolvePlacement(anchor(), PANEL, opts({ placement: 'right' }));
    assert.equal(r.side, 'right');
    assert.equal(r.x, 224);       // anchor.right + offset
    assert.equal(r.y, 100);       // anchor.top
});

test('right flips to left when the right side lacks room', () => {
    const a = anchor({ left: 1100, right: 1220, width: 120 });
    const r = resolvePlacement(a, PANEL, opts({ placement: 'right' }));
    assert.equal(r.side, 'left');
    assert.equal(r.x, 1100 - 4 - 200);
});

test('left placement with end align bottoms the panel to the anchor bottom', () => {
    // Far enough right that a 200-wide panel fits on the left — no flip.
    const a = anchor({ left: 400, right: 520, top: 400, bottom: 440 });
    const r = resolvePlacement(a, PANEL, opts({ placement: 'left', align: 'end' }));
    assert.equal(r.side, 'left');
    assert.equal(r.x, 400 - 4 - 200);
    assert.equal(r.y, 440 - 150); // anchor.bottom - height
});

test('margin is respected in room math', () => {
    // 140px below the anchor: fits a 150-high panel only with margin 0+offset.
    const a = anchor({ top: 600, bottom: 640 });
    const flipped = resolvePlacement(a, PANEL, opts({ margin: 8 }));
    assert.equal(flipped.side, 'top');
    const kept = resolvePlacement(a, PANEL, opts({ margin: 0, offset: 0 }));
    assert.equal(kept.side, 'bottom');
});

// ── anchorIntersectsViewport (place() hides the panel when this goes false) ──

test('anchor fully inside the viewport intersects', () => {
    assert.equal(anchorIntersectsViewport(anchor(), VIEW.viewWidth, VIEW.viewHeight), true);
});

test('anchor scrolled above the viewport does not intersect', () => {
    const a = anchor({ top: -200, bottom: -160 });
    assert.equal(anchorIntersectsViewport(a, VIEW.viewWidth, VIEW.viewHeight), false);
});

test('anchor scrolled below the viewport does not intersect', () => {
    const a = anchor({ top: 820, bottom: 860 });
    assert.equal(anchorIntersectsViewport(a, VIEW.viewWidth, VIEW.viewHeight), false);
});

test('anchor fully left/right of the viewport does not intersect', () => {
    const left = anchor({ left: -300, right: -180 });
    assert.equal(anchorIntersectsViewport(left, VIEW.viewWidth, VIEW.viewHeight), false);
    const right = anchor({ left: 1300, right: 1420 });
    assert.equal(anchorIntersectsViewport(right, VIEW.viewWidth, VIEW.viewHeight), false);
});

test('anchor clipped to a viewport edge still intersects (strict >)', () => {
    // One pixel still on screen → panel stays visible.
    const a = anchor({ top: -39, bottom: 1 });
    assert.equal(anchorIntersectsViewport(a, VIEW.viewWidth, VIEW.viewHeight), true);
});

test('anchor with a zeroed rect (display:none) does not intersect', () => {
    const a = anchor({ top: 0, bottom: 0, left: 0, right: 0 });
    assert.equal(anchorIntersectsViewport(a, VIEW.viewWidth, VIEW.viewHeight), false);
});

// ── readViewport: the visible viewport (visualViewport) drives placement, so an on-screen
//    keyboard shrinking the visible area flips/clamps panels into view. ────────────────────

function stubWindow(over = {}) {
    return { innerWidth: 1280, innerHeight: 800, visualViewport: null, ...over };
}

test('readViewport: falls back to the layout viewport when visualViewport is missing', () => {
    const view = readViewport(stubWindow());
    assert.deepEqual(view, { width: 1280, height: 800, left: 0, top: 0 });
});

test('readViewport: prefers visualViewport when present (on-screen keyboard shrinks it)', () => {
    const win = stubWindow({
        innerWidth: 390,
        innerHeight: 844,
        visualViewport: { width: 390, height: 420, offsetLeft: 0, offsetTop: -180 },
    });
    // Height = the visible part above the keyboard; top = the panned layout offset.
    assert.deepEqual(readViewport(win), { width: 390, height: 420, left: 0, top: -180 });
});

test('readViewport: ignores a degenerate visualViewport (1x1 metrics-emulation screenshot)', () => {
    const win = stubWindow({
        innerWidth: 390,
        innerHeight: 844,
        visualViewport: { width: 1, height: 1, offsetLeft: 0, offsetTop: 0 },
    });
    assert.deepEqual(readViewport(win), { width: 390, height: 844, left: 0, top: 0 });
});

// ── positionFloating: measure + resolve + write, driven through stub elements so the whole
//    pipeline (flip, clamp, visualViewport) is exercised without a DOM. ────────────────────

function stubPanel({ width = 200, height = 150 } = {}) {
    return {
        style: {},
        offsetWidth: width,
        offsetHeight: height,
        previousElementSibling: null,
        attributes: {},
        setAttribute(name, value) { this.attributes[name] = value; },
        getAttribute(name) { return this.attributes[name]; },
    };
}

function stubAnchor(rect) {
    return { isConnected: true, getBoundingClientRect: () => rect };
}

function floatOpts(over = {}) {
    return {
        anchor: stubAnchor({ top: 100, left: 100, right: 220, bottom: 140, width: 120, height: 40 }),
        placement: 'bottom',
        align: 'start',
        anchorGap: 4,
        margin: 8,
        flip: true,
        clamp: true,
        matchAnchorWidth: false,
        ...over,
    };
}

test('positionFloating: places below the anchor and writes the placement attribute', () => {
    const panel = stubPanel();
    const result = positionFloating(panel, floatOpts(), stubWindow());
    assert.equal(result.side, 'bottom');
    assert.equal(panel.style.top, '144px');
    assert.equal(panel.style.left, '100px');
    assert.equal(panel.getAttribute('data-tm-placement'), 'bottom');
});

test('positionFloating: flips to top when the visible space below the anchor is too small', () => {
    const panel = stubPanel();
    // Phone with the keyboard open: visible height 420, anchor at 380-424.
    const anchorEl = stubAnchor({ top: 380, left: 20, right: 160, bottom: 424, width: 140, height: 44 });
    const win = stubWindow({
        innerWidth: 390,
        innerHeight: 844,
        visualViewport: { width: 390, height: 420, offsetLeft: 0, offsetTop: -180 },
    });
    const result = positionFloating(panel, floatOpts({ anchor: anchorEl }), win);
    assert.equal(result.side, 'top');
    // Above the anchor, lifted by the panned layout offset.
    assert.equal(panel.style.top, `${380 - 4 - 150 - 180}px`);
});

test('positionFloating: clamps the panel inside the visible viewport (shift)', () => {
    const panel = stubPanel({ width: 200, height: 150 });
    const anchorEl = stubAnchor({ top: 100, left: 340, right: 380, bottom: 140, width: 40, height: 40 });
    const win = stubWindow({ innerWidth: 390, innerHeight: 844 });
    const result = positionFloating(panel, floatOpts({ anchor: anchorEl }), win);
    assert.equal(panel.style.left, `${390 - 8 - 200}px`);
});

test('positionFloating: matchAnchorWidth stretches the panel to the anchor before measuring', () => {
    const panel = stubPanel();
    positionFloating(panel, floatOpts({ matchAnchorWidth: true }), stubWindow());
    assert.equal(panel.style.width, '120px');
    const unstyled = stubPanel();
    positionFloating(unstyled, floatOpts({ matchAnchorWidth: false }), stubWindow());
    assert.equal(unstyled.style.width, '');
});

test('positionFloating: subtracts a fallback containing-block origin (no Popover API)', () => {
    const panel = stubPanel();
    const result = positionFloating(panel, floatOpts({ originLeft: 50, originTop: 30 }), stubWindow());
    assert.equal(panel.style.left, `${100 - 50}px`);
    assert.equal(panel.style.top, `${144 - 30}px`);
    assert.equal(result.side, 'bottom');
});
