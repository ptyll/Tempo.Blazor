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

// ── FZE-13: per-page overlay slots ─────────────────────────────────────────
//
// Continuous annotations render as Blazor-owned overlay slots placed next to the
// JS-owned page wraps. JavaScript owns the geometry: it collects the page boxes
// relative to the scroll content, decides which pages sit inside the mount band
// (the same ±50% band the paint/release logic uses, so the lazy-render contract
// and the painted-page limit keep holding) and reports the mounted subset to
// Blazor, which diffs only those few slots. The helpers below are the pure part.

/** A page box in scroll-content coordinates: `{ page, top, left, width, height }`. */

/**
 * Builds the geometry report from DOM rects (node-tested): every wrap's client
 * box mapped into the scroll content's coordinate space — the same convention
 * {@link pageIntersectsViewport} already uses for `top`. Collapsed boxes
 * (display:none, not yet laid out) are dropped.
 * @param {{page: number, rect: {top: number, left: number, width: number, height: number}}[]|undefined} wraps
 *   One entry per page wrap, in document order.
 * @param {{top: number, left: number}} containerRect The scroll container's own client rect.
 * @param {number} scrollTop Current content offset of the container.
 * @returns {{page: number, top: number, left: number, width: number, height: number}[]}
 */
export function collectPageGeometry(wraps, containerRect, scrollTop) {
    const containerTop = containerRect?.top ?? 0;
    const containerLeft = containerRect?.left ?? 0;
    return (wraps ?? [])
        .map(wrap => ({
            page: wrap.page,
            top: wrap.rect.top - containerTop + (scrollTop || 0),
            left: wrap.rect.left - containerLeft,
            width: wrap.rect.width,
            height: wrap.rect.height,
        }))
        .filter(box => box.width > 0 && box.height > 0);
}

/**
 * The pages whose overlay slots Blazor mounts: every page box intersecting the
 * margin-extended viewport band ({@link bandExtents}, the same band
 * `selectCanvasesToRelease` protects and the IntersectionObserver watches with
 * `rootMargin: '50% 0px'`). Only these pages ever reach Blazor's diff, so a
 * 120-page document never produces 120 overlay components and the painted-page
 * limit keeps holding.
 * @param {{page: number, top: number, height: number}[]|undefined} boxes Page boxes in content coordinates.
 * @param {number} scrollTop Content offset of the viewport top edge.
 * @param {number} viewportHeight Viewport height.
 * @param {number} [marginRatio=BAND_MARGIN_RATIO] Extra viewport fraction per side.
 * @returns {number[]} Mounted page numbers, ascending.
 */
export function overlayMountedPages(boxes, scrollTop, viewportHeight, marginRatio = BAND_MARGIN_RATIO) {
    if (!Array.isArray(boxes)) return [];
    const band = bandExtents(scrollTop, viewportHeight, marginRatio);
    return boxes
        .filter(box => box.height > 0 && box.top < band.bottom && box.top + box.height > band.top)
        .map(box => box.page)
        .sort((a, b) => a - b);
}

/**
 * Resolves the page a selection belongs to by walking from the selection's
 * anchor node up to the nearest element carrying `data-page` — the continuous
 * renderer's page wrap (the per-page text layer spans are its descendants).
 * Returns 0 when the anchor lives outside any page (nothing to report).
 * @param {Node|null} anchorNode The DOM selection's `anchorNode`.
 * @returns {number} One-based page number, or 0.
 */
export function resolveSelectionPage(anchorNode) {
    let node = anchorNode;
    while (node) {
        if (node.nodeType === 1 && node.dataset && node.dataset.page !== undefined) {
            // Only the renderer-owned wraps count (the closest() check needs a real
            // DOM node — the fake harnesses carry no closest and keep the old walk).
            const page = parseInt(node.dataset.page, 10);
            if (page >= 1 && (typeof node.closest !== 'function' || node.closest('.tm-pdf-viewer__continuous-pages'))) {
                return page;
            }
        }
        node = node.parentElement ?? node.parentNode ?? null;
    }
    return 0;
}

/**
 * Normalizes client-space selection rectangles against the anchor page's wrap
 * box: each rect is clamped to the page (a selection that crosses into the next
 * page is cut at the boundary — the anchor page wins) and mapped to 0..1
 * coordinates, returning the flat `[x, y, w, h, …]` report `OnTextSelectionChanged`
 * consumes. Rects that do not intersect the page at all are dropped.
 * @param {DOMRect[]|{left:number,top:number,width:number,height:number}[]|undefined} rects Client-space rects.
 * @param {{left:number,top:number,width:number,height:number}|null} pageRect The anchor page wrap's client box.
 * @returns {number[]} Flat normalized rectangles.
 */
export function normalizeSelectionRects(rects, pageRect) {
    // getClientRects() returns a DOMRectList, not an Array — accept any iterable
    // array-like so the live selection report works unchanged.
    const items = rects == null ? [] : (Array.isArray(rects) ? rects : Array.from(rects));
    if (!pageRect || !(pageRect.width > 0) || !(pageRect.height > 0)) return [];
    const flat = [];
    for (const rect of items) {
        const left = Math.max(rect.left, pageRect.left);
        const top = Math.max(rect.top, pageRect.top);
        const right = Math.min(rect.left + rect.width, pageRect.left + pageRect.width);
        const bottom = Math.min(rect.top + rect.height, pageRect.top + pageRect.height);
        const width = right - left;
        const height = bottom - top;
        if (width < 1 || height < 1) continue; // outside the anchor page
        flat.push(
            _clampUnit((left - pageRect.left) / pageRect.width),
            _clampUnit((top - pageRect.top) / pageRect.height),
            _clampUnit(width / pageRect.width),
            _clampUnit(height / pageRect.height),
        );
    }
    return flat;
}

function _clampUnit(value) {
    if (!Number.isFinite(value)) return 0;
    return Math.min(Math.max(value, 0), 1);
}

/**
 * The scroll offset that centers a thread anchor inside the viewport: the anchor
 * point (`page.top + ratio * page.height`) lands on the viewport's middle line,
 * clamped at the document top. The browser clamps the overflow at the document
 * end, so no bottom clamp is needed here.
 * @param {{page: number, top: number, height: number}[]|undefined} pages Page metas in document order.
 * @param {number} page One-based anchor page.
 * @param {number} anchorRatio Normalized 0..1 vertical position within the page.
 * @param {number} viewportHeight Viewport height.
 * @returns {number|null} The target scrollTop, or null when the page is unknown.
 */
export function anchorScrollTop(pages, page, anchorRatio, viewportHeight) {
    if (!Array.isArray(pages) || pages.length === 0) return null;
    const meta = pages.find(candidate => candidate.page === page);
    if (!meta) return null;
    const ratio = Math.min(Math.max(Number.isFinite(anchorRatio) ? anchorRatio : 0, 0), 1);
    const anchorY = meta.top + ratio * meta.height;
    return Math.max(0, anchorY - (viewportHeight || 0) / 2);
}
