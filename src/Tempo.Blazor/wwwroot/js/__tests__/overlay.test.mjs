import test from 'node:test';
import assert from 'node:assert/strict';
import { anchorIntersectsViewport, resolvePlacement } from '../overlay.js';

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
