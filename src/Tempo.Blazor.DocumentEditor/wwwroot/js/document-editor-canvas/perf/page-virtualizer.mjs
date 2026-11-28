const DEFAULT_PAGE_GAP = 24;
const DEFAULT_PADDING_BLOCK = 24;

export function createPageVirtualizer(options = {}) {
    let bufferPages = Math.max(0, Number(options.bufferPages ?? 1) || 0);
    let enabled = options.enabled !== false;
    let lastPlan = emptyPlan();

    // `planOptions.scale` is the zoom the pages are painted at. The viewport (scrollTop, height) is in CSS pixels while a page's height is in layout units,
    // so the pages are brought into CSS pixels first (heights times the scale; the gap between pages and the padding are CSS already) - the spacers then
    // come out in CSS pixels too, which is what the DOM needs.
    function plan(pages, viewport = {}, planOptions = {}) {
        const scale = normalizeScale(planOptions.scale);
        const normalizedPages = normalizePages(pages, options, scale);
        if (!enabled || normalizedPages.length === 0) {
            lastPlan = fullPlan(normalizedPages);
            return lastPlan;
        }

        const visibleRange = visibleIndexes(normalizedPages, viewport, bufferPages, { ...options, prepared: true });
        const visibleSet = new Set(visibleRange);
        const visiblePages = normalizedPages.filter(page => visibleSet.has(page.index));
        const firstVisible = visiblePages[0] || normalizedPages[0];
        const lastVisible = visiblePages[visiblePages.length - 1] || firstVisible;
        const totalHeight = totalDocumentHeight(normalizedPages, options);
        const topSpacerHeight = Math.max(0, firstVisible.top - DEFAULT_PADDING_BLOCK);
        const bottomSpacerHeight = Math.max(0, totalHeight - lastVisible.bottom - DEFAULT_PADDING_BLOCK);

        lastPlan = {
            enabled: true,
            pageCount: normalizedPages.length,
            visiblePageIndexes: visiblePages.map(page => page.index),
            pages: visiblePages.map(page => page.source),
            topSpacerHeight,
            bottomSpacerHeight,
            totalHeight,
            bufferPages,
            scale,
            activePageIndex: activePageOf(normalizedPages, viewport, options),
            progressive: visiblePages.length < normalizedPages.length,
        };
        return lastPlan;
    }

    function setBufferPages(value) {
        bufferPages = Math.max(0, Number(value) || 0);
        return bufferPages;
    }

    function setEnabled(value) {
        enabled = value !== false;
        return enabled;
    }

    function snapshot() {
        return { ...lastPlan, visiblePageIndexes: [...lastPlan.visiblePageIndexes] };
    }

    return {
        plan,
        setBufferPages,
        setEnabled,
        snapshot,
    };
}

export function visibleIndexes(pages, viewport = {}, bufferPages = 1, options = {}) {
    const normalized = options.prepared === true ? pages : normalizePages(pages, options, normalizeScale(options.scale));
    if (normalized.length === 0) {
        return [];
    }

    const height = Math.max(1, Number(viewport.height || viewport.viewportHeight || options.viewportHeight || 900) || 900);
    const scrollTop = Math.max(0, Number(viewport.scrollTop || viewport.viewportTop || 0) || 0);
    const averageHeight = averagePageStride(normalized, options);
    const top = Math.max(0, scrollTop - Math.max(0, bufferPages) * averageHeight);
    const bottom = scrollTop + height + Math.max(0, bufferPages) * averageHeight;
    const indexes = normalized
        .filter(page => page.bottom >= top && page.top <= bottom)
        .map(page => page.index);

    return indexes.length > 0 ? indexes : [normalized[0].index];
}

function normalizeScale(value) {
    const scale = Number(value);
    return Number.isFinite(scale) && scale > 0 ? scale : 1;
}

// The page the reader is on: the first page that still reaches below the top quarter of the view (at most 120 px). A page that is only mounted as
// overscan above the view, or whose last few pixels sit under the edge, is not "the page".
function activePageOf(normalizedPages, viewport, options) {
    const height = Math.max(1, Number(viewport.height || viewport.viewportHeight || options.viewportHeight || 900) || 900);
    const scrollTop = Math.max(0, Number(viewport.scrollTop || viewport.viewportTop || 0) || 0);
    const edge = scrollTop + Math.min(height * 0.25, 120);
    const active = normalizedPages.find(page => page.bottom > edge) || normalizedPages[normalizedPages.length - 1];
    return active.index;
}

function normalizePages(pages, options = {}, scale = 1) {
    const gap = Math.max(0, Number(options.pageGap ?? DEFAULT_PAGE_GAP) || DEFAULT_PAGE_GAP);
    const padding = Math.max(0, Number(options.paddingBlock ?? DEFAULT_PADDING_BLOCK) || DEFAULT_PADDING_BLOCK);
    let cursor = padding;
    return (Array.isArray(pages) ? pages : []).map((page, ordinal) => {
        const height = Math.max(1, Number(page?.height || options.pageHeight || 1123) || 1123) * scale;
        const top = Number.isFinite(Number(page?.y)) ? Number(page.y) * scale + padding : cursor;
        const normalized = {
            index: Number(page?.index ?? ordinal) || 0,
            top,
            bottom: top + height,
            height,
            source: page,
        };
        cursor = normalized.bottom + gap;
        return normalized;
    });
}

function fullPlan(pages) {
    return {
        enabled: false,
        pageCount: pages.length,
        visiblePageIndexes: pages.map(page => page.index),
        pages: pages.map(page => page.source),
        topSpacerHeight: 0,
        bottomSpacerHeight: 0,
        totalHeight: totalDocumentHeight(pages),
        bufferPages: 0,
        progressive: false,
    };
}

function emptyPlan() {
    return fullPlan([]);
}

function averagePageStride(pages, options = {}) {
    if (pages.length === 0) {
        return Math.max(1, Number(options.pageHeight || 1123) || 1123);
    }

    const gap = Math.max(0, Number(options.pageGap ?? DEFAULT_PAGE_GAP) || DEFAULT_PAGE_GAP);
    return pages.reduce((total, page) => total + page.height + gap, 0) / pages.length;
}

function totalDocumentHeight(pages, options = {}) {
    if (pages.length === 0) {
        return 0;
    }

    const padding = Math.max(0, Number(options.paddingBlock ?? DEFAULT_PADDING_BLOCK) || DEFAULT_PADDING_BLOCK);
    return pages[pages.length - 1].bottom + padding;
}
