// pdf-viewer-continuous.mjs tests — pure continuous-mode logic: visible-page
// computation, the lazy render queue order, releasing canvases outside the
// viewport under a limit, and scroll anchors that survive a zoom rebuild.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    BAND_MARGIN_RATIO,
    DEFAULT_MAX_RENDERED_PAGES,
    applyScrollAnchor,
    bandExtents,
    computeScrollAnchor,
    computeVisiblePage,
    formatPageLabel,
    orderPagesForRender,
    pageIntersectsViewport,
    placeholderSize,
    selectCanvasesToRelease,
} from '../pdf-viewer-continuous.mjs';

/** Builds page metas stacked vertically: page i sits at (i-1)*heightPx. */
function stackedPages(count, heightPx = 800) {
    return Array.from({ length: count }, (_, i) => ({
        page: i + 1,
        top: i * heightPx,
        height: heightPx
    }));
}

test('computeVisiblePage picks the page containing the viewport top edge', () => {
    const pages = stackedPages(5, 800);
    // Viewport top at 1000 → inside page 2 (800..1600).
    assert.equal(computeVisiblePage(pages, 1000, 600), 2);
    // A boundary scroll lands on the page starting at the top edge — the rule
    // scrollIntoView({block:'start'}) navigation agrees with.
    assert.equal(computeVisiblePage(pages, 1600, 600), 3);
});

test('computeVisiblePage falls back to the first page when nothing intersects', () => {
    const pages = stackedPages(3, 800);
    assert.equal(computeVisiblePage(pages, 10000, 600), 3,
        'scrolled past the end still reports the last page');
});

test('computeVisiblePage handles empty and degenerate inputs', () => {
    assert.equal(computeVisiblePage([], 0, 600), 0);
    assert.equal(computeVisiblePage([{ page: 1, top: 0, height: 0 }], 0, 600), 1);
    assert.equal(computeVisiblePage(undefined, 0, 600), 0);
});

test('pageIntersectsViewport is exclusive at the boundaries', () => {
    assert.equal(pageIntersectsViewport({ page: 1, top: 100, height: 200 }, 300, 600), false,
        'a page ending exactly at the viewport top is already scrolled past');
    assert.equal(pageIntersectsViewport({ page: 1, top: 700, height: 200 }, 0, 600), false,
        'a page starting exactly at the viewport bottom is not visible yet');
    assert.equal(pageIntersectsViewport({ page: 1, top: 500, height: 400 }, 0, 600), true);
});

test('orderPagesForRender queues visible pages first, then the rest in document order', () => {
    const pages = stackedPages(6, 800);
    // Viewport band 1600..2500 only reaches page 4 (2400..3200) among the queue;
    // pages 1, 2 and 6 are off-screen and follow in document order.
    const queue = [6, 1, 4, 2];
    assert.deepEqual(orderPagesForRender(queue, pages, 1600, 900), [4, 1, 2, 6]);
});

test('orderPagesForRender keeps document order among equally visible pages', () => {
    const pages = stackedPages(5, 800);
    assert.deepEqual(orderPagesForRender([4, 3], pages, 1600, 800), [3, 4]);
});

test('orderPagesForRender tolerates queued pages missing from the metas', () => {
    const pages = stackedPages(2, 800);
    assert.deepEqual(orderPagesForRender([9, 1], pages, 0, 800), [1, 9]);
});

test('selectCanvasesToRelease keeps the rendered set within the limit', () => {
    const pages = stackedPages(10, 800).map(m => ({ ...m, rendered: true }));
    // Viewport on page 1: pages 10, 9, ... are the farthest — release two.
    const released = selectCanvasesToRelease(pages, 0, 800, 8);
    assert.equal(released.length, 2);
    assert.deepEqual(released.sort((a, b) => a - b), [9, 10]);
});

test('selectCanvasesToRelease never releases a page intersecting the viewport', () => {
    const pages = stackedPages(10, 800).map(m => ({ ...m, rendered: true }));
    const released = selectCanvasesToRelease(pages, 0, 800, 1);
    assert.equal(released.includes(1), false, 'the visible page stays rendered');
    assert.ok(released.length > 0, 'but over-limit pages are still released');
});

test('selectCanvasesToRelease ignores unrendered pages', () => {
    const pages = stackedPages(4, 800).map(m => ({ ...m, rendered: m.page <= 3 }));
    // The viewport (0..800) protects pages 1-2; page 3 is the only releasable one.
    assert.deepEqual(selectCanvasesToRelease(pages, 0, 800, 3), []);
    assert.deepEqual(selectCanvasesToRelease(pages, 0, 800, 2), [3]);
});

test('selectCanvasesToRelease protects the margin-extended band (IO rootMargin 50% 0px)', () => {
    // The release band must match the IntersectionObserver's rootMargin '50% 0px'
    // (± 0.5 viewport height per side) — releasing a band page would repaint it
    // without a fresh observer transition.
    assert.equal(BAND_MARGIN_RATIO, 0.5);
    assert.deepEqual(bandExtents(0, 800), { top: -400, bottom: 1200 });
    const pages = stackedPages(6, 800).map(m => ({ ...m, rendered: true }));
    // Viewport 0..800 → band −400..1200: page 2 (800..1600) is INSIDE the band and
    // must survive even though it does not intersect the viewport itself.
    const released = selectCanvasesToRelease(pages, 0, 800, 4);
    assert.equal(released.includes(1), false, 'the visible page stays rendered');
    assert.equal(released.includes(2), false, 'the band page below the viewport stays rendered');
    assert.deepEqual(released.sort((a, b) => a - b), [5, 6], 'only pages beyond the band are released');
    // A page ending exactly at the band top is outside it — releasable.
    const above = [{ page: 1, top: -900, height: 800, rendered: true }, { page: 2, top: -100, height: 800, rendered: true }];
    // Band −400..1200: page 1 ends at −100 ≤ −400? No — it ends AT −100 > −400,
    // i.e. it still touches the band; page 2 (−100..700) intersects it. Both survive.
    assert.deepEqual(selectCanvasesToRelease(above, 0, 800, 0), []);
    const outside = [{ page: 1, top: -1400, height: 800, rendered: true }];
    assert.deepEqual(selectCanvasesToRelease(outside, 0, 800, 0), [1], 'a page fully above the band is released');
});

test('selectCanvasesToRelease uses the documented default limit', () => {
    assert.ok(DEFAULT_MAX_RENDERED_PAGES >= 4, 'the limit must keep a usable buffer');
    const pages = stackedPages(50, 800).map(m => ({ ...m, rendered: true }));
    const released = selectCanvasesToRelease(pages, 0, 800);
    assert.equal(pages.length - released.length, DEFAULT_MAX_RENDERED_PAGES);
});

test('computeScrollAnchor records the page containing the viewport top edge', () => {
    const pages = stackedPages(5, 800);
    // scrollTop 1000 → 200px into page 2.
    assert.deepEqual(computeScrollAnchor(pages, 1000, 600), { page: 2, offsetRatio: 0.25 });
});

test('computeScrollAnchor round-trips at the same scale', () => {
    const pages = stackedPages(5, 800);
    for (const scrollTop of [0, 123, 1600, 3999]) {
        const anchor = computeScrollAnchor(pages, scrollTop, 600);
        assert.equal(applyScrollAnchor(pages, anchor, 600), scrollTop);
    }
});

test('applyScrollAnchor preserves the content point across a zoom rebuild', () => {
    const before = stackedPages(5, 800);
    const after = stackedPages(5, 1600); // scale ×2 doubles every page box
    const anchor = computeScrollAnchor(before, 1000, 600);
    // 200px into page 2 at the old scale = 400px into page 2 at the new scale.
    assert.equal(applyScrollAnchor(after, anchor, 600), 1600 + 400);
});

test('applyScrollAnchor returns null without a usable anchor', () => {
    const pages = stackedPages(3, 800);
    assert.equal(applyScrollAnchor(pages, null, 600), null);
    assert.equal(applyScrollAnchor(pages, { page: 99, offsetRatio: 0 }, 600), null);
    assert.equal(applyScrollAnchor([], { page: 1, offsetRatio: 0 }, 600), null);
});

test('placeholderSize scales the page box and never collapses', () => {
    assert.deepEqual(placeholderSize(612, 792, 1), { width: 612, height: 792 });
    assert.deepEqual(placeholderSize(612, 792, 0.5), { width: 306, height: 396 });
    assert.deepEqual(placeholderSize(612, 792, 2), { width: 1224, height: 1584 });
    assert.equal(placeholderSize(0, 0, 1).height, 1, 'degenerate boxes clamp to 1px');
    assert.ok(placeholderSize(10, 792, 0).width >= 1, 'a zero scale still yields a visible box');
});

test('formatPageLabel substitutes {0} and {1} from the localized template', () => {
    assert.equal(formatPageLabel('Page {0} of {1}', 3, 120), 'Page 3 of 120');
    assert.equal(formatPageLabel('Stránka {0} z {1}', 3, 120), 'Stránka 3 z 120');
    assert.equal(formatPageLabel('Page {0} sur {1}', 42, 120), 'Page 42 sur 120');
    assert.equal(formatPageLabel('Stránka {0} z {1}', 1, 9), 'Stránka 1 z 9');
});

test('formatPageLabel falls back to the English template and tolerates junk', () => {
    assert.equal(formatPageLabel(undefined, 1, 2), 'Page 1 of 2');
    assert.equal(formatPageLabel('', 1, 2), 'Page 1 of 2');
    // A template without the {0} placeholder is not a page label — use the fallback.
    assert.equal(formatPageLabel('Stránky: {1}', 1, 2), 'Page 1 of 2');
});
