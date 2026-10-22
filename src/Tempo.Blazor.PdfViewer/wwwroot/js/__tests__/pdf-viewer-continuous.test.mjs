// pdf-viewer-continuous.mjs tests — pure continuous-mode logic: visible-page
// computation, the lazy render queue order, releasing canvases outside the
// viewport under a limit, scroll anchors that survive a zoom rebuild, the
// per-page overlay geometry/band report (FZE-13) and the per-page selection
// normalization (FZE-13).
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    BAND_MARGIN_RATIO,
    DEFAULT_MAX_RENDERED_PAGES,
    anchorScrollTop,
    applyScrollAnchor,
    bandExtents,
    collectPageGeometry,
    computeScrollAnchor,
    computeVisiblePage,
    formatPageLabel,
    normalizeSelectionRects,
    orderPagesForRender,
    overlayMountedPages,
    pageIntersectsViewport,
    placeholderSize,
    resolveSelectionPage,
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

// ══════════════════════════════════════════════════════════════════════════
// FZE-13 — per-page overlay slots: geometry report, mount band, per-page
// selection normalization and thread-anchor scrolling.
// ══════════════════════════════════════════════════════════════════════════

/** Page box in client coordinates for `collectPageGeometry` input. */
function wrapAt(page, clientTop, clientLeft, width, height) {
    return { page, rect: { top: clientTop, left: clientLeft, width, height } };
}

test('pageGeometry_ReportsOffsetsRelativeToScrollContent', () => {
    // Container border box at client (40, 100); the scroll content is offset by
    // scrollTop 500 — a wrap at client top 300 sits at content 700.
    const boxes = collectPageGeometry(
        [wrapAt(1, 300, 60, 612, 792), wrapAt(2, 1100, 60, 612, 792)],
        { top: 100, left: 40 },
        500);
    assert.deepEqual(boxes, [
        { page: 1, top: 700, left: 20, width: 612, height: 792 },
        { page: 2, top: 1500, left: 20, width: 612, height: 792 },
    ]);
});

test('pageGeometry_ReportsOffsetsRelativeToScrollContent_toleratesJunk', () => {
    assert.deepEqual(collectPageGeometry(undefined, { top: 0, left: 0 }, 0), []);
    assert.deepEqual(collectPageGeometry([wrapAt(1, 0, 0, 0, 0)], { top: 0, left: 0 }, 0), [],
        'collapsed boxes (hidden wrap) are dropped, never reported');
});

test('pageGeometry_AfterInPlaceZoom_ScalesSlots', () => {
    // Zoom 1 → 2 doubles every box; the geometry report must carry the SCALED
    // boxes so the Blazor slots resize with the page (in-place resize path).
    const before = collectPageGeometry([wrapAt(3, 100, 10, 612, 792)], { top: 0, left: 0 }, 100);
    const after = collectPageGeometry([wrapAt(3, 300, 5, 1224, 1584)], { top: 0, left: 0 }, 300);
    assert.equal(before[0].width, 612);
    assert.equal(after[0].width, 1224, 'the in-place zoom doubles the reported slot width');
    assert.equal(after[0].height, 1584, 'the in-place zoom doubles the reported slot height');
    assert.equal(after[0].top, 600, 'the slot top tracks the reflowed content point');
});

test('overlayBand_MountsOnlyPagesNearViewport', () => {
    const boxes = stackedPages(10, 800).map(m => ({ ...m, left: 0, width: 612 }));
    // Viewport 1600..2400 (page 3); the ±50% band extends it to 1200..2800 —
    // pages 2..4 intersect it, everything else stays unmounted.
    const mounted = overlayMountedPages(boxes, 1600, 800);
    assert.deepEqual(mounted, [2, 3, 4]);
    // The default margin mirrors the IntersectionObserver rootMargin '50% 0px'.
    assert.deepEqual(overlayMountedPages(boxes, 0, 800), [1, 2],
        'at the top of the document the band covers pages 1..2 only');
    assert.deepEqual(overlayMountedPages(boxes, 800 * 9, 800), [9, 10],
        'scrolled past the end, the band covers the last pages only');
    assert.deepEqual(overlayMountedPages([], 0, 800), []);
    assert.deepEqual(overlayMountedPages(boxes, 1600, 800, 0), [3],
        'a zero margin mounts only the intersecting page');
    assert.equal(overlayMountedPages(boxes, 1600, 800).length <= 6, true,
        'the band never mounts more pages than the paint limit would hold');
});

test('resolveSelectionPage_FindsWrapOfAnchorNode', () => {
    // The selection anchor is a text node inside the per-page text layer; the walk
    // climbs to the page wrap carrying data-page.
    const wrap = { nodeType: 1, dataset: { page: '7' }, parentElement: null };
    const textLayer = { nodeType: 1, dataset: {}, parentElement: wrap };
    const span = { nodeType: 1, dataset: {}, parentElement: textLayer };
    const textNode = { nodeType: 3, parentElement: span };
    assert.equal(resolveSelectionPage(textNode), 7);
    assert.equal(resolveSelectionPage(span), 7, 'element anchors resolve through the same walk');
    // The canvas inside the wrap also carries data-page — the walk stops at the
    // NEAREST data-page ancestor, which is the wrap for text-layer nodes.
    assert.equal(resolveSelectionPage({ nodeType: 3, parentElement: null }), 0,
        'an anchor outside any page resolves to no page');
    assert.equal(resolveSelectionPage(null), 0);
    assert.equal(resolveSelectionPage({ nodeType: 1, dataset: { page: '0' } }), 0,
        'a degenerate data-page attribute resolves to no page');
});

test('normalizeSelectionRects_PerPageWrap_ReturnsZeroToOneRects', () => {
    // Page wrap at client (50, 100), 612×792.
    const pageRect = { left: 50, top: 100, width: 612, height: 792 };
    const rects = [
        { left: 100, top: 200, width: 122, height: 20 },   // fully inside
        { left: 600, top: 300, width: 200, height: 20 },   // straddles the right edge
        { left: 0, top: 0, width: 10, height: 10 },        // fully outside → dropped
    ];
    const flat = normalizeSelectionRects(rects, pageRect);
    assert.deepEqual(flat, [
        50 / 612, 100 / 792, 122 / 612, 20 / 792,          // inside rect normalized 0..1
        550 / 612, 200 / 792, (612 - 550) / 612, 20 / 792, // straddling rect clamped to the page
    ], 'rects are normalized against the page wrap and clamped to its box');
    assert.deepEqual(normalizeSelectionRects([], pageRect), []);
    assert.deepEqual(normalizeSelectionRects(undefined, pageRect), []);
    assert.deepEqual(normalizeSelectionRects(rects, null), [],
        'no page box means nothing can be normalized');
});

test('normalizeSelectionRects accepts a DOMRectList (array-like, not an Array)', () => {
    // range.getClientRects() hands the report a live DOMRectList — the normalizer
    // must not require a real Array (the live-selection regression this pins).
    const pageRect = { left: 0, top: 0, width: 100, height: 50 };
    const domRectList = {
        0: { left: 10, top: 10, width: 50, height: 10 },
        1: { left: 10, top: 80, width: 50, height: 10 },
        length: 2,
        [Symbol.iterator]: Array.prototype[Symbol.iterator],
    };
    assert.deepEqual(normalizeSelectionRects(domRectList, pageRect), [0.1, 0.2, 0.5, 0.2],
        'the inside rect normalizes; the one below the page is dropped');
});

test('selectionAcrossTwoPages_ClampsToAnchorPage', () => {    // A selection spanning the boundary of page 2 (content 800..1600) and page 3:
    // the anchor page's wrap ends at client top 900 (page 2 at scrollTop 700 with
    // 800px pages); the rects on page 3 must be dropped, the straddling one clamped.
    const pageRect = { left: 0, top: 100, width: 612, height: 800 }; // page 2 wrap
    const rects = [
        { left: 0, top: 400, width: 612, height: 30 },   // mid page 2 → kept
        { left: 0, top: 890, width: 612, height: 30 },   // straddles the bottom edge
        { left: 0, top: 950, width: 612, height: 30 },   // fully on page 3 → dropped
    ];
    const flat = normalizeSelectionRects(rects, pageRect);
    assert.equal(flat.length, 8, 'exactly two rects survive (one clamped at the boundary)');
    const [, , , , x2, y2, w2, h2] = flat;
    assert.ok(y2 + h2 <= 1.0001, 'the clamped rect never leaves the anchor page');
    assert.equal(y2 + h2, 1, 'the straddling rect is clamped to the page bottom');
    assert.equal(x2, 0);
});

test('scrollToAnchor_CentersAnchorYWithinPage', () => {
    const pages = stackedPages(10, 800);
    // Thread anchor at 30% of page 5 (content 3200..4000 → anchorY 3440):
    // the viewport centers the anchor point.
    assert.equal(anchorScrollTop(pages, 5, 0.3, 800), 3440 - 400);
    assert.equal(anchorScrollTop(pages, 1, 0, 800), 0,
        'clamped at the document top');
    assert.equal(anchorScrollTop(pages, 10, 1, 800), 8000 - 400,
        'the last page anchor still centers (the browser clamps the overflow)');
    assert.equal(anchorScrollTop(pages, 99, 0.5, 800), null, 'unknown page → no scroll');
    assert.equal(anchorScrollTop([], 1, 0.5, 800), null);
    assert.equal(anchorScrollTop(pages, 5, 1.7, 800), 4000 - 400,
        'an out-of-range ratio clamps into the page box');
});
