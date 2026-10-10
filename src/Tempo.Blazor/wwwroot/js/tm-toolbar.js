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
 * Measures a bar (the .tm-toolbar element: its children are the start/actions containers and the
 * More trigger, and the containers' children are the buttons). The containers are flattened: only
 * their children take room. Items (data-tm-toolbar-item) are measured even when collapsed - a
 * collapsed button stays in the DOM out of flow and invisible precisely so its width is known.
 * Non-item children (title, divider, custom content) are fixed width including their margins;
 * out-of-flow ones (a popover panel) take no room. The More trigger is not an item: its real
 * width is used once it is rendered.
 * @param {HTMLElement} bar the toolbar row
 * @returns {{maxVisible:number, hasPinned:boolean, signature:string}}
 */
export function computeFit(bar) {
    const style = globalThis.getComputedStyle?.(bar);
    const gap = px(style?.columnGap) || px(style?.gap);
    const items = [];
    let fixed = 0;
    let triggerWidth = DEFAULT_TRIGGER_WIDTH;
    let hasPinned = bar.dataset?.tmToolbarPinned === 'true';

    const visit = children => {
        for (const child of Array.from(children)) {
            const classes = child.classList;
            if (classes?.contains('tm-toolbar-start') || classes?.contains('tm-toolbar-actions')) {
                visit(child.children ?? []);
                continue;
            }

            if (classes?.contains('tm-toolbar-more')) {
                if (child.offsetWidth > 0) triggerWidth = child.offsetWidth;
                continue;
            }

            const data = child.dataset ?? {};
            if ('tmToolbarItem' in data) {
                if (data.pin === 'always') { hasPinned = true; continue; }
                items.push({ width: outerWidth(child), rank: Number(data.rank) || 0 });
                continue;
            }

            const childStyle = globalThis.getComputedStyle?.(child);
            const position = childStyle?.position;
            if (position === 'fixed' || position === 'absolute') continue;
            if (child.offsetWidth > 0) fixed += outerWidth(child) + gap;
        }
    };
    visit(bar.children);

    const padding = px(style?.paddingLeft) + px(style?.paddingRight);
    const available = bar.clientWidth - padding - fixed;
    const maxVisible = chooseVisibleCount(items, available, gap, triggerWidth, hasPinned);
    return { maxVisible, hasPinned, signature: `${maxVisible}|${items.length}|${hasPinned}` };
}

function outerWidth(element) {
    const style = globalThis.getComputedStyle?.(element);
    return element.offsetWidth + px(style?.marginLeft) + px(style?.marginRight);
}
const attached = new WeakMap();

function refreshTabStops(state) {
    const items = rovingItems(state.root);
    if (items.length === 0) return;
    const stop = items.find(item => item.getAttribute('tabindex') === '0') ?? items[0];
    for (const item of items) item.setAttribute('tabindex', item === stop ? '0' : '-1');
}

function isTextEntry(target) {
    const tag = String(target?.tagName ?? '').toUpperCase();
    return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target?.isContentEditable === true;
}

function measure(state) {
    state.frame = 0;
    if (state.disposed || !state.options.overflow) return;
    // The root IS the measured row; the selector exists so a host can point at an inner row.
    const bar = state.root.querySelector('[data-tm-toolbar-row]') ?? state.root;
    const fit = computeFit(bar);
    if (fit.signature === state.lastSignature) return;
    state.lastSignature = fit.signature;
    // Only the count crosses the interop boundary; C# decides which buttons leave
    // (ToolbarOverflowLayout), so the ordering rule exists once.
    try {
        state.dotNetRef?.invokeMethodAsync('OnFitChanged', fit.maxVisible)?.catch(() => {});
    } catch { /* the circuit is gone */ }
}

function schedule(state) {
    if (state.disposed || state.frame) return;
    state.frame = globalThis.requestAnimationFrame(() => {
        state.frame = 0;
        if (state.disposed) return;
        refreshTabStops(state);
        measure(state);
    });
}

/**
 * Attaches the toolbar behaviour to a .tm-toolbar element: the roving tabindex always, the fit
 * measurement when options.overflow is set. Idempotent: attaching again replaces the registration.
 * @param {HTMLElement} root the .tm-toolbar element
 * @param {{invokeMethodAsync:Function}|null} dotNetRef receives OnFitChanged(maxVisible)
 * @param {{overflow?:boolean}} [options]
 */
export function attach(root, dotNetRef, options) {
    if (!root) return;
    detach(root);

    const state = {
        root,
        dotNetRef,
        options: { overflow: Boolean(options?.overflow) },
        lastSignature: null,
        frame: 0,
        disposed: false,
        observers: [],
    };

    const onKeyDown = event => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
        if (isTextEntry(event.target)) return;
        const items = rovingItems(root);
        const current = items.indexOf(event.target);
        // Only a control of the toolbar itself roves - a stray target (the menu, a custom widget in
        // the bar) keeps its own keys.
        if (current < 0) return;
        const rtl = globalThis.getComputedStyle?.(root)?.direction === 'rtl';
        const target = nextRovingIndex(items.length, current, event.key, rtl);
        if (target < 0) return;
        event.preventDefault();
        items[target].focus();
    };
    const onFocusIn = event => {
        const items = rovingItems(root);
        if (!items.includes(event.target)) return;
        for (const item of items) item.setAttribute('tabindex', item === event.target ? '0' : '-1');
    };

    root.addEventListener('keydown', onKeyDown);
    root.addEventListener('focusin', onFocusIn);
    state.onKeyDown = onKeyDown;
    state.onFocusIn = onFocusIn;

    const schedulePass = () => schedule(state);
    if (state.options.overflow && typeof globalThis.ResizeObserver === 'function') {
        const resize = new globalThis.ResizeObserver(schedulePass);
        resize.observe(root);
        state.observers.push(resize);
    }

    if (typeof globalThis.MutationObserver === 'function') {
        // Buttons come and go (a priority change, a conditional action): the roving set and the
        // measurement follow the DOM, coalesced into one frame.
        const mutation = new globalThis.MutationObserver(schedulePass);
        mutation.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'disabled', 'data-rank', 'data-pin'] });
        state.observers.push(mutation);
    }

    attached.set(root, state);
    refreshTabStops(state);
    schedule(state);
}

/**
 * Detaches everything attach() set up on the element and returns the controls to the natural tab order.
 * @param {HTMLElement|null} root the element passed to attach
 */
export function detach(root) {
    const state = root ? attached.get(root) : null;
    if (!state) return;
    state.disposed = true;
    for (const observer of state.observers) observer.disconnect();
    if (state.frame) globalThis.cancelAnimationFrame?.(state.frame);
    state.frame = 0;
    root.removeEventListener('keydown', state.onKeyDown);
    root.removeEventListener('focusin', state.onFocusIn);
    for (const control of Array.from(root.querySelectorAll(CONTROL_SELECTOR))) control.removeAttribute('tabindex');
    attached.delete(root);
}

/** Test seam: kept for symmetry with the other modules (state is per element and weakly held). */
export function __resetForTests() {
}