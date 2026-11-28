import assert from 'node:assert/strict';
import test from 'node:test';
import { createPageVirtualizer } from '../page-virtualizer.mjs';

// Phase 12 (fix round): the viewport the virtualizer reads is in CSS pixels, the pages it plans are in layout units; at a zoom below 1 (the fit of a
// phone / a tablet) the two must be brought into one unit, or a scroll position maps to a page far below the one the reader is on.
const PAGE = 1123;
const pagesOf = count => Array.from({ length: count }, (_, index) => ({ index, width: 794, height: PAGE }));

test('at zoom 0.38 the planned pages and spacers are in CSS pixels, so a scroll position maps to the page the reader is on', () => {
    const scale = 0.38;
    const stride = PAGE * scale + 24;
    const virtualizer = createPageVirtualizer({ bufferPages: 1, pageGap: 24, paddingBlock: 24 });

    // the engine root is 6 page strides (+ the 24 px padding) above the top edge of the scroller: the reader is on page index 6
    const plan = virtualizer.plan(pagesOf(79), { scrollTop: 6 * stride, height: 560 }, { scale });

    assert.ok(plan.visiblePageIndexes.includes(6), `page 6 is mounted (${plan.visiblePageIndexes})`);
    assert.ok(!plan.visiblePageIndexes.includes(11), 'a page five strides further down is not');
    assert.ok(Math.abs(plan.topSpacerHeight - (plan.visiblePageIndexes[0] * stride)) < 1, `top spacer ${plan.topSpacerHeight} is the CSS height of the pages above`);
    const total = 24 + 79 * PAGE * scale + 78 * 24 + 24;
    assert.ok(Math.abs(plan.totalHeight - total) < 1, `total ${plan.totalHeight} vs ${total}`);
    const last = plan.visiblePageIndexes[plan.visiblePageIndexes.length - 1];
    assert.ok(Math.abs(plan.bottomSpacerHeight - (78 - last) * stride) < 1, `bottom spacer ${plan.bottomSpacerHeight} is the CSS height of the pages below`);
});

test('without a scale the plan is exactly what it was (scale 1)', () => {
    const virtualizer = createPageVirtualizer({ bufferPages: 1 });
    const a = virtualizer.plan(pagesOf(10), { scrollTop: 1123 * 4, height: 900 });
    const b = virtualizer.plan(pagesOf(10), { scrollTop: 1123 * 4, height: 900 }, { scale: 1 });
    assert.deepEqual(b.visiblePageIndexes, a.visiblePageIndexes);
    assert.equal(b.topSpacerHeight, a.topSpacerHeight);
    assert.equal(b.bottomSpacerHeight, a.bottomSpacerHeight);
});

test('the active page is the first page that reaches below the top quarter of the view, never an overscan page', () => {
    const virtualizer = createPageVirtualizer({ bufferPages: 1 });
    const stride = PAGE + 24;
    // page 9 starts 24 px below the top edge: page 8 ended at the edge, the reader is on page 9 although 8 is mounted as overscan
    const plan = virtualizer.plan(pagesOf(79), { scrollTop: 9 * stride, height: 560 });
    assert.ok(plan.visiblePageIndexes.includes(8), 'page 8 is mounted as the overscan page');
    assert.equal(plan.activePageIndex, 9);

    // halfway through page 9 it is still page 9; near its end the next page takes over
    assert.equal(virtualizer.plan(pagesOf(79), { scrollTop: 9 * stride + 600, height: 560 }).activePageIndex, 9);
    assert.equal(virtualizer.plan(pagesOf(79), { scrollTop: 10 * stride - 60, height: 560 }).activePageIndex, 10);
    assert.equal(virtualizer.plan(pagesOf(79), { scrollTop: 0, height: 560 }).activePageIndex, 0);
});

test('the active page follows the scale and clamps to the last page past the end', () => {
    const scale = 0.5;
    const stride = PAGE * scale + 24;
    const virtualizer = createPageVirtualizer({ bufferPages: 1 });
    assert.equal(virtualizer.plan(pagesOf(20), { scrollTop: 7 * stride, height: 500 }, { scale }).activePageIndex, 7);
    assert.equal(virtualizer.plan(pagesOf(20), { scrollTop: 999999, height: 500 }, { scale }).activePageIndex, 19);
});
