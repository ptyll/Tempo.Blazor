// Tempo.Blazor toolbar module (ES module) - the generalised toolbar overflow mechanism of the core
// TmToolbar (F4). It generalises the DocumentEditor's toolbar-overflow.mjs (which measures which
// ribbon commands are scrolled out of view): the core toolbar measures how many of its buttons FIT
// and reports that single number; C# (ToolbarOverflowLayout, over the shared ActionOverflowLayout
// partition) decides WHICH buttons leave, so the ordering rule exists once. The DocumentEditor
// migrates onto this module in its own plan (DE-1.2).
//
// This file holds the pure maths (testable without a DOM), the bar measurement and the DOM wiring:
//   - fit/collapse: ResizeObserver + MutationObserver, rAF-coalesced, signature dedup - .NET is
//     called only when the answer changes, so there is no render loop;
//   - roving tabindex + ArrowLeft/Right/Home/End (ArrowRight/Left swap in an RTL toolbar).

const DEFAULT_TRIGGER_WIDTH = 44; // 2.75rem = --tm-touch-target; the CSS pins the More trigger to it.
const ITEM = '[data-tm-toolbar-item]';

/**
 * The order buttons leave the bar: lowest rank first, ties - the LATER button first. This is the
 * rule of ActionOverflowLayout.Partition (C#); the C# side stays the single decision-maker for
 * which buttons overflow, this exists so the JS can count how many must go.
 * @param {number[]} ranks overflow rank per button, in bar order
 * @returns {number[]} button indexes in leaving order
 */
export function collapseOrder(ranks) {
    return ranks.map((rank, index) => ({ rank, index }))
        .sort((a, b) => a.rank - b.rank || b.index - a.index)
        .map(entry => entry.index);
}

/**
 * How many of the (non-pinned) buttons stay on the bar.
 * @param {{width:number, rank:number}[]} items the collapsible buttons in bar order
 * @param {number} available room for the buttons: the bar width minus the fixed children
 * @param {number} gap the flex gap
 * @param {number} triggerWidth the More trigger width
 * @param {boolean} hasPinned whether an OverflowOnly button forces the trigger to exist
 * @returns {number} the count of buttons kept; 0 when not even one fits
 */
export function chooseVisibleCount(items, available, gap, triggerWidth, hasPinned) {
    if (items.length === 0) return 0;
    const order = collapseOrder(items.map(item => item.rank));
    for (let removed = 0; removed < items.length; removed++) {
        const gone = new Set(order.slice(0, removed));
        const kept = items.filter((_, index) => !gone.has(index));
        let width = kept.reduce((sum, item) => sum + item.width, 0) + gap * (kept.length - 1);
        if (removed > 0 || hasPinned) width += gap + triggerWidth;
        if (width <= available) return kept.length;
    }

    return 0;
}

/**
 * The roving-tabindex target for a key.
 * @param {number} count how many roving controls there are
 * @param {number} current the index of the focused control, -1 when none
 * @param {string} key the pressed key
 * @param {boolean} rtl whether the toolbar is right-to-left
 * @returns {number} the target index, or -1 when the key moves nothing
 */
export function nextRovingIndex(count, current, key, rtl) {
    if (!(count > 0)) return -1;
    const forward = rtl ? 'ArrowLeft' : 'ArrowRight';
    const backward = rtl ? 'ArrowRight' : 'ArrowLeft';
    if (key === forward) return current < 0 ? 0 : (current + 1) % count;
    if (key === backward) return current < 0 ? count - 1 : (current - 1 + count) % count;
    if (key === 'Home') return 0;
    if (key === 'End') return count - 1;
    return -1;
}

const CONTROL_SELECTOR = 'button.tm-toolbar-btn, button.tm-toolbar-more, [data-tm-toolbar-control]';
// Controls that are not reachable: a collapsed bar copy (visibility:hidden) or anything inside the
// open More menu / its panel (the menu has its own keyboard, tm-menu-nav.js).
const EXCLUDED_SELECTOR = '.tm-toolbar-item--collapsed, [role="menu"], .tm-overlay-panel';

function isDisabled(control) {
    return control.disabled === true || control.getAttribute?.('aria-disabled') === 'true';
}

/**
 * The controls the arrow keys visit: enabled, on the bar, not inside the More menu.
 * @param {ParentNode} root the toolbar body
 */
export function rovingItems(root) {
    return Array.from(root.querySelectorAll(CONTROL_SELECTOR))
        .filter(control => !isDisabled(control) && !control.closest(EXCLUDED_SELECTOR));
}

function px(value) {
    const parsed = parseFloat(value);
    return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Measures a bar (the element whose direct children are the toolbar items).
 * @param {HTMLElement} bar the .tm-toolbar-start element
 * @returns {{maxVisible:number, hasPinned:boolean, signature:string}}
 */
export function computeFit(bar) {
    const style = globalThis.getComputedStyle?.(bar);
    const gap = px(style?.columnGap) || px(style?.gap);
    const items = [];
    let fixed = 0;
    let hasPinned = bar.dataset?.tmToolbarPinned === 'true';
    for (const child of Array.from(bar.children)) {
        const classes = child.classList;
        if (classes?.contains('tm-toolbar-more') || classes?.contains('tm-overlay-panel')) continue;
        const data = child.dataset ?? {};
        if ('tmToolbarItem' in data) {
            if (data.pin === 'always') { hasPinned = true; continue; }
            items.push({ width: child.offsetWidth, rank: Number(data.rank) || 0 });
        } else if (child.offsetWidth > 0) {
            fixed += child.offsetWidth + gap;
        }
    }

    const available = bar.clientWidth - fixed;
    const maxVisible = chooseVisibleCount(items, available, gap, DEFAULT_TRIGGER_WIDTH, hasPinned);
    return { maxVisible, hasPinned, signature: `${maxVisible}|${items.length}|${hasPinned}` };
}

/** Test seam: forgets every attached toolbar. */
export function __resetForTests() {
}
