// TmGantt window virtualization maths (Fáze 12) — a pure module with no DOM and no dependencies, so the
// node tests (wwwroot/js/__tests__/gantt-virtual.test.mjs) can drive it directly. gantt.js owns the DOM
// (the attachVirtualScroll session); this file only answers the questions:
//   - which rows does the viewport show (first/last, overscan included)?
//   - did the window move far enough to wake .NET (the overscan/2 hysteresis)?
//   - where must the scroll be for a given row (the keyboard's scrollToRow)?

/**
 * The visible window for a scroll position. `first` and `last` are row indexes into the visible set;
 * `last` is exclusive and clamped to `rowCount`. The window is anchored on the REAL pixel range the
 * viewport shows — `firstVisible = floor(top / rowHeight)`, `lastVisible = ceil((top + clientHeight) /
 * rowHeight)` — so the partially visible edge rows stay painted, with the overscan added on BOTH sides,
 * so a single scroll step never shows an unpainted edge.
 *
 * @param {number} scrollTop current vertical scroll of the tree body
 * @param {number} clientHeight visible height of the tree body (the viewport of the window)
 * @param {number} rowCount total visible rows
 * @param {number} rowHeight the one row height the tree and the timeline share
 * @param {number} overscan extra rows rendered above and below the window
 * @returns {{first: number, last: number}}
 */
export function computeWindow(scrollTop, clientHeight, rowCount, rowHeight, overscan) {
    if (!(rowCount > 0) || !(rowHeight > 0)) return { first: 0, last: 0 };
    const top = scrollTop > 0 ? scrollTop : 0;
    const viewport = clientHeight > 0 ? clientHeight : 0;
    const over = overscan > 0 ? overscan : 0;
    const firstVisible = Math.floor(top / rowHeight);
    const lastVisible = Math.ceil((top + viewport) / rowHeight);
    let first = Math.max(0, firstVisible - over);
    if (first > rowCount - 1) first = rowCount - 1; // an overscroll past the end keeps the last row painted
    const last = Math.min(rowCount, Math.max(first, lastVisible + over));
    return { first, last };
}

/**
 * Whether a window shift is worth a .NET render. The hysteresis is overscan/2 rows: a scroll that moves
 * the window less than that re-renders nothing (the overscan absorbs it). The first measurement always
 * reports — the prerender window (the first few rows) must be corrected whatever it takes.
 *
 * @param {{first: number, last: number}|null} previous the last window .NET was told about (null = never)
 * @param {{first: number, last: number}} next the freshly computed window
 * @param {number} overscan
 * @returns {boolean}
 */
export function shouldNotify(previous, next, overscan) {
    if (!previous) return true;
    if (previous.first === next.first && previous.last === next.last) return false;
    const threshold = Math.max(1, Math.floor((overscan > 0 ? overscan : 0) / 2));
    return Math.abs(next.first - previous.first) >= threshold
        || Math.abs(next.last - previous.last) >= threshold;
}

/**
 * The scrollTop that brings `index` into the viewport. `align` is 'start' (the row at the top edge —
 * Home), 'end' (the row at the bottom edge — End) or 'nearest' (scroll only when the row is outside).
 * Never negative.
 *
 * @param {number} index row to align
 * @param {number} rowHeight
 * @param {number} clientHeight viewport height
 * @param {string} align 'start' | 'end' | 'nearest'
 * @param {number} scrollTop the current scroll position ('nearest' keeps a fully visible row where it is)
 * @returns {number}
 */
export function alignScrollTop(index, rowHeight, clientHeight, align, scrollTop) {
    if (!(rowHeight > 0)) return 0;
    const top = index * rowHeight;
    const bottom = top + rowHeight;
    const viewport = clientHeight > 0 ? clientHeight : 0;
    const current = scrollTop > 0 ? scrollTop : 0;
    if (align === 'start') return top;
    if (align === 'end') return Math.max(0, bottom - viewport);
    // nearest: a fully visible row stays where it is.
    if (top >= current + viewport) return top;                    // fully below the viewport
    if (bottom > current + viewport) return Math.max(0, bottom - viewport); // straddles the bottom edge
    if (bottom <= current) return top;                            // fully above the viewport
    return current; // already visible
}
