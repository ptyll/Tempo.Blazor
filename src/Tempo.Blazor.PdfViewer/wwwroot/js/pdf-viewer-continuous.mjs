/**
 * Tempo.Blazor.PdfViewer — pure continuous-mode logic (node-tested via
 * npm run test:pdf-viewer).
 *
 * The continuous renderer in pdf-viewer.js builds one placeholder wrap per page,
 * lazily paints the pages an IntersectionObserver reports (one render at a time,
 * visibility-ordered), releases canvases far from the viewport under a limit and
 * keeps the current page reported to Blazor while the user scrolls. The DOM reads
 * stay in pdf-viewer.js; every decision below is pure so the node suite can drive
 * it with plain number arrays: a page meta is `{ page, top, height }` where `top`
 * is the offset of the page box inside the scrolled content and the viewport is
 * the band `[scrollTop, scrollTop + viewportHeight]` in the same coordinates.
 */

/**
 * How many painted page canvases may live at once. A painted A4 page at DPR 2
 * costs tens of megabytes of backing store, so pages scrolled far out of the
 * viewport are released (the placeholder box keeps the layout stable) and are
 * re-rendered on demand when they scroll back.
 */
export const DEFAULT_MAX_RENDERED_PAGES = 12;

/**
 * The extra viewport fraction the paint/release band extends beyond the visible
 * area on each side. This mirrors the IntersectionObserver rootMargin
 * `'50% 0px'` used by the renderer: pages inside the band are treated as
 * "about to be visible" — they are neither released nor released-then-repainted
 * (a page released while still inside the band would immediately re-enter it
 * without a fresh observer transition).
 */
export const BAND_MARGIN_RATIO = 0.5;

/**
 * The margin-extended viewport band used for release decisions and re-queueing.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height.
 * @param {number} [marginRatio=BAND_MARGIN_RATIO] Extra viewport fraction per side.
 * @returns {{top: number, bottom: number}} Content-coordinate band extents.
 */
export function bandExtents(scrollTop, viewportHeight, marginRatio = BAND_MARGIN_RATIO) {
    const margin = (viewportHeight || 0) * marginRatio;
    return { top: scrollTop - margin, bottom: scrollTop + (viewportHeight || 0) + margin };
}

/**
 * The fraction of the page height the viewport top edge sits at, for the anchor
 * page — clamped into a sane range even for odd geometries (negative gaps).
 */
function clampRatio(value) {
    if (!Number.isFinite(value)) return 0;
    return Math.min(Math.max(value, -1), 1);
}

/**
 * Whether a page box intersects the viewport band.
 * @param {{page: number, top: number, height: number}} meta Page box.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height.
 * @returns {boolean}
 */
export function pageIntersectsViewport(meta, scrollTop, viewportHeight) {
    if (!meta || !(meta.height > 0)) return false;
    const top = meta.top;
    const bottom = meta.top + meta.height;
    const viewTop = scrollTop;
    const viewBottom = scrollTop + viewportHeight;
    // Half-open interval: a page that ends exactly at the view top (or starts
    // exactly at the view bottom) is not worth painting.
    return top < viewBottom && bottom > viewTop;
}

/**
 * Picks the page the user is currently viewing: the page containing the
 * viewport's top edge — the same rule `scrollIntoView({ block: 'start' })`
 * navigation agrees with, so an explicit jump to page N is never re-reported
 * as a different page. When the scroll is past the end of the document, the
 * last page is reported.
 * @param {{page: number, top: number, height: number}[]|undefined} pages Page metas in document order.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height (unused by the top-edge rule; kept for
 *   signature symmetry with the other helpers).
 * @returns {number} 1-based page number, or 0 when there is nothing to report.
 */
export function computeVisiblePage(pages, scrollTop, viewportHeight) {
    if (!Array.isArray(pages) || pages.length === 0) return 0;
    const containing = pages.find(meta => meta && meta.top + meta.height > scrollTop);
    return (containing ?? pages[pages.length - 1]).page;
}

/**
 * Orders the lazy render queue: pages intersecting the viewport are painted
 * first (document order — the paint loop is serial, so this is the priority),
 * then everything else in document order. Queued pages missing from the metas
 * (a stale queue entry after a rebuild) keep their relative order at the end.
 * @param {number[]} queue Page numbers waiting to be painted.
 * @param {{page: number, top: number, height: number}[]|undefined} pages Page metas in document order.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height.
 * @returns {number[]} Queue page numbers, visibility-ordered.
 */
export function orderPagesForRender(queue, pages, scrollTop, viewportHeight) {
    const metas = new Map((pages ?? []).map(meta => [meta.page, meta]));
    const known = [];
    const unknown = [];
    for (const page of queue ?? []) {
        const meta = metas.get(page);
        if (meta) known.push(meta);
        else unknown.push(page);
    }
    known.sort((a, b) => a.top - b.top);
    const visible = known.filter(meta => pageIntersectsViewport(meta, scrollTop, viewportHeight));
    const rest = known.filter(meta => !pageIntersectsViewport(meta, scrollTop, viewportHeight));
    return [...visible, ...rest].map(meta => meta.page).concat(unknown);
}

/**
 * Selects the painted pages whose canvases should be released to honor the
 * limit: while more than `maxRendered` pages hold painted canvases, the one
 * farthest from the viewport band is released. Pages intersecting the
 * margin-extended band ({@link BAND_MARGIN_RATIO}, the same band the
 * IntersectionObserver's rootMargin `'50% 0px'` watches) are never released —
 * releasing a band page would repaint it without a fresh observer transition.
 * @param {{page: number, top: number, height: number, rendered: boolean}[]} pages Page metas with their render state.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height.
 * @param {number} [maxRendered=DEFAULT_MAX_RENDERED_PAGES] Painted-page limit.
 * @returns {number[]} Page numbers to release, farthest first.
 */
export function selectCanvasesToRelease(pages, scrollTop, viewportHeight, maxRendered = DEFAULT_MAX_RENDERED_PAGES) {
    if (!Array.isArray(pages) || !(maxRendered >= 0)) return [];
    const band = bandExtents(scrollTop, viewportHeight);
    const distanceFromViewport = meta => {
        const bottom = meta.top + meta.height;
        if (meta.top >= band.bottom) return meta.top - band.bottom;
        if (bottom <= band.top) return band.top - bottom;
        return 0; // intersects the margin-extended band
    };
    const released = [];
    let rendered = pages.filter(meta => meta.rendered);
    while (rendered.length > maxRendered) {
        let candidate = null;
        let candidateDistance = -1;
        for (const meta of rendered) {
            const distance = distanceFromViewport(meta);
            if (distance <= 0) continue; // visible pages are never released
            if (distance > candidateDistance) {
                candidate = meta;
                candidateDistance = distance;
            }
        }
        if (!candidate) break; // only visible pages remain
        released.push(candidate.page);
        rendered = rendered.filter(meta => meta !== candidate);
    }
    return released;
}

/**
 * Records the scroll position as an anchor relative to the page containing the
 * viewport top edge, so a rebuild at a different scale can restore the same
 * content point (the placeholder boxes change size, the ratio does not).
 * @param {{page: number, top: number, height: number}[]|undefined} pages Page metas in document order.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height (unused for the anchor itself, kept for symmetry).
 * @returns {{page: number, offsetRatio: number}|null} The anchor, or null when there are no page metas.
 */
export function computeScrollAnchor(pages, scrollTop, viewportHeight) {
    if (!Array.isArray(pages) || pages.length === 0) return null;
    const anchorPage = pages.find(meta => meta.top + meta.height > scrollTop) ?? pages[pages.length - 1];
    return {
        page: anchorPage.page,
        offsetRatio: clampRatio((scrollTop - anchorPage.top) / (anchorPage.height || 1))
    };
}

/**
 * Restores an anchor recorded by {@link computeScrollAnchor} against the rebuilt
 * page boxes.
 * @param {{page: number, top: number, height: number}[]|undefined} pages New page metas in document order.
 * @param {{page: number, offsetRatio: number}|null} anchor The recorded anchor.
 * @param {number} viewportHeight Viewport height (unused; kept for symmetry with compute).
 * @returns {number|null} The restored scrollTop, or null when the anchor cannot be honored.
 */
export function applyScrollAnchor(pages, anchor, viewportHeight) {
    if (!Array.isArray(pages) || pages.length === 0 || !anchor) return null;
    const target = pages.find(meta => meta.page === anchor.page);
    if (!target) return null;
    return target.top + clampRatio(anchor.offsetRatio) * target.height;
}

/**
 * Placeholder box for a not-yet-painted page: the page box at the current scale.
 * Rounded and clamped so the layout never collapses between paints.
 * @param {number} pageWidth Page width at scale 1.
 * @param {number} pageHeight Page height at scale 1.
 * @param {number} scale Current zoom scale.
 * @returns {{width: number, height: number}} CSS pixel box.
 */
export function placeholderSize(pageWidth, pageHeight, scale) {
    const s = Number.isFinite(scale) && scale > 0 ? scale : 1;
    return {
        width: Math.max(1, Math.round((pageWidth || 0) * s)),
        height: Math.max(1, Math.round((pageHeight || 0) * s))
    };
}

/**
 * Formats the localized "Page {0} of {1}" template for a page wrap's
 * aria-label (node-tested). Falls back to the English template when the host
 * passes nothing usable, and never throws on a malformed template.
 * @param {string|undefined} template Localized template with {0}/{1} placeholders.
 * @param {number} page One-based page number.
 * @param {number} total Total page count.
 * @returns {string} Formatted label.
 */
export function formatPageLabel(template, page, total) {
    const t = typeof template === 'string' && template.includes('{0}') ? template : 'Page {0} of {1}';
    return t.replace('{0}', String(page)).replace('{1}', String(total));
}
