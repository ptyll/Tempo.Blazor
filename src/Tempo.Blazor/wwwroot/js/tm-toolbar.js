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
const COLLAPSED_SELECTOR = '.tm-toolbar-item--collapsed';
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

const DIVIDER = 'tm-toolbar-divider';
const COLLAPSED = 'tm-toolbar-item--collapsed';
const REDUNDANT = 'data-tm-divider-redundant';

function isFlattened(child) {
    const classes = child.classList;
    if (classes?.contains('tm-toolbar-start') || classes?.contains('tm-toolbar-actions')) return true;
    // A group wrapper (data-tm-toolbar-group, role="group" or ANY wrapper that holds items) is a layout box
    // only: its children take the room. Registration is by the cascade at any depth, so measurement must
    // see items at any depth too - otherwise a wrapped group looks like one fixed block with no items and
    // the toolbar collapses everything into More forever.
    if ('tmToolbarGroup' in (child.dataset ?? {})) return true;
    return typeof child.querySelector === 'function' && child.querySelector(ITEM) !== null;
}

/**
 * Walks the bar in DOM order, flattening the start/actions containers and group wrappers.
 * @param {Iterable<Element>} children the children of the bar
 * @param {(child: Element, wrapper: boolean) => void} visit called for every leaf, and for every flattened wrapper (wrapper=true)
 */
function walk(children, visit) {
    for (const child of Array.from(children)) {
        if (isFlattened(child)) {
            visit(child, true);
            walk(child.children ?? [], visit);
        } else {
            visit(child, false);
        }
    }
}

/**
 * Measures a bar (the .tm-toolbar element). The start/actions containers and any group wrapper are
 * flattened: only their children take room (a wrapper's own padding and border count as fixed width).
 * Items (data-tm-toolbar-item) are measured even when collapsed - a collapsed button stays in the DOM
 * out of flow and invisible precisely so its width is known. Non-item children (title, divider, custom
 * content) are fixed width including their margins; out-of-flow ones (a popover panel) take no room. The
 * More trigger is not an item: its real width is used once it is rendered.
 * @param {HTMLElement} bar the toolbar row
 * @returns {{maxVisible:number, hasPinned:boolean, signature:string|null, ids:string[], unmeasured:boolean}}
 *   `ids` are the item ids in DOM order (the order of record, reported to .NET); `unmeasured` is true when
 *   nothing could be measured (no items, or a bar with no width) - the caller must NOT report a fit then.
 */
export function computeFit(bar) {
    const style = globalThis.getComputedStyle?.(bar);
    const gap = px(style?.columnGap) || px(style?.gap);
    const items = [];
    const ids = [];
    let fixed = 0;
    let triggerWidth = DEFAULT_TRIGGER_WIDTH;
    let hasPinned = bar.dataset?.tmToolbarPinned === 'true';

    walk(bar.children, (child, wrapper) => {
        const classes = child.classList;
        if (wrapper) {
            // start/actions carry no box of their own; a group wrapper may have padding/border.
            const wrapperStyle = child.computed ?? globalThis.getComputedStyle?.(child);
            fixed += px(wrapperStyle?.paddingLeft) + px(wrapperStyle?.paddingRight)
                + px(wrapperStyle?.borderLeftWidth) + px(wrapperStyle?.borderRightWidth);
            return;
        }

        if (classes?.contains('tm-toolbar-more')) {
            if (child.offsetWidth > 0) triggerWidth = child.offsetWidth;
            return;
        }

        const data = child.dataset ?? {};
        if ('tmToolbarItem' in data) {
            ids.push(String(data.tmToolbarItem));
            if (data.pin === 'always') { hasPinned = true; return; }
            // Pinned (data-pin=never): never collapses, so it is plain fixed width on the bar.
            if (data.pin === 'never') { fixed += outerWidth(child) + gap; return; }
            items.push({ width: outerWidth(child), rank: Number(data.rank) || 0 });
            return;
        }

        const childStyle = globalThis.getComputedStyle?.(child);
        const position = childStyle?.position;
        if (position === 'fixed' || position === 'absolute') return;
        if (child.offsetWidth > 0) fixed += outerWidth(child) + gap;
        else if (classes?.contains('tm-toolbar-divider') && childStyle?.display !== 'none') {
            // A divider squeezed to 0px by a flex-shrinking row still takes its declared width and margins once it is
            // laid out again; counting 0 here over-commits the bar (the next control overlaps the pinned one).
            fixed += px(childStyle?.width) + px(childStyle?.marginLeft) + px(childStyle?.marginRight) + gap;
        }
    });

    const padding = px(style?.paddingLeft) + px(style?.paddingRight);
    // Nothing measurable (no item in the DOM, or a bar that is not laid out - display:none, detached): a
    // "0 fit" answer would collapse every button into More and never recover. Report nothing instead.
    if (ids.length === 0 || !(bar.clientWidth > 0)) {
        return { maxVisible: 0, hasPinned, signature: null, ids, unmeasured: true };
    }

    const available = bar.clientWidth - padding - fixed;
    const maxVisible = chooseVisibleCount(items, available, gap, triggerWidth, hasPinned);
    return { maxVisible, hasPinned, signature: `${maxVisible}|${items.length}|${hasPinned}|${ids.join(',')}`, ids, unmeasured: false };
}

function outerWidth(element) {
    const style = globalThis.getComputedStyle?.(element);
    return element.offsetWidth + px(style?.marginLeft) + px(style?.marginRight);
}

/**
 * Marks the dividers that separate nothing with data-tm-divider-redundant (CSS hides them with
 * visibility, so the measured width stays stable): a divider that is first/last among the visible
 * content, or directly followed by another divider. The attribute is NOT observed (no mutation loop).
 * @param {HTMLElement} bar the toolbar row
 */
export function markRedundantDividers(bar) {
    const flow = [];
    walk(bar.children, (child, wrapper) => {
        if (wrapper) return;
        const classes = child.classList;
        if (classes?.contains('tm-toolbar-more')) return;
        if (classes?.contains(DIVIDER)) { flow.push({ el: child, divider: true }); return; }
        if (classes?.contains(COLLAPSED)) return;
        const position = globalThis.getComputedStyle?.(child)?.position;
        if (position === 'fixed' || position === 'absolute') return;
        if (child.offsetWidth > 0) flow.push({ el: child, divider: false });
    });

    let seenContent = false;
    for (let i = 0; i < flow.length; i++) {
        const entry = flow[i];
        if (!entry.divider) { seenContent = true; continue; }
        const next = flow[i + 1];
        const redundant = !seenContent || !next || next.divider;
        if (redundant) {
            if (!entry.el.hasAttribute(REDUNDANT)) entry.el.setAttribute(REDUNDANT, 'true');
        } else if (entry.el.hasAttribute(REDUNDANT)) {
            entry.el.removeAttribute(REDUNDANT);
        }
    }
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
    if (fit.unmeasured) return;
    markRedundantDividers(bar);
    if (fit.signature === state.lastSignature) return;
    state.lastSignature = fit.signature;
    // The count and the DOM order of the items cross the interop boundary; C# decides which buttons leave
    // (ToolbarOverflowLayout, the shared partition) from that order, so the ordering rule exists once.
    try {
        state.dotNetRef?.invokeMethodAsync('OnFitChanged', fit.maxVisible, fit.ids)?.catch(() => {});
    } catch { /* the circuit is gone */ }
}

/**
 * Focus never drops to <body> because the toolbar re-laid itself out: when the control that had focus
 * collapsed into More (inert + hidden) or its menu item unmounted with the closing menu, hand focus to the More
 * trigger, else to the last roving control. A focus that is still valid, or went elsewhere on purpose, is
 * left alone.
 */
function restoreLostFocus(state) {
    const last = state.lastFocus;
    if (!last) return;
    const doc = globalThis.document;
    const active = doc?.activeElement;
    if (active && active !== doc.body && active !== doc.documentElement) {
        // Focus is somewhere real: keep following it while it is inside the bar (the next pass may collapse it),
        // forget it once it left the toolbar on purpose.
        state.lastFocus = state.root.contains?.(active) === true ? active : null;
        return;
    }
    const unreachable = last.isConnected === false || Boolean(last.closest?.(COLLAPSED_SELECTOR));
    state.lastFocus = unreachable ? state.lastFocus : null;
    if (!unreachable) return;
    state.lastFocus = null;
    const trigger = state.root.querySelector?.('button.tm-toolbar-more');
    const target = trigger && !trigger.disabled ? trigger : (state.lastRoving?.isConnected !== false && !state.lastRoving?.closest?.(EXCLUDED_SELECTOR) ? state.lastRoving : rovingItems(state.root)[0]);
    target?.focus?.();
}

function schedule(state) {
    if (state.disposed || state.frame) return;
    state.frame = globalThis.requestAnimationFrame(() => {
        state.frame = 0;
        if (state.disposed) return;
        // The element left the document without a detach (a host that removed it, a lost circuit): let go of
        // the observers and listeners instead of measuring a corpse every frame.
        if (state.root.isConnected === false) { detach(state.root); return; }
        restoreLostFocus(state);
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
        state.lastFocus = event.target;
        const items = rovingItems(root);
        if (!items.includes(event.target)) return;
        state.lastRoving = event.target;
        for (const item of items) item.setAttribute('tabindex', item === event.target ? '0' : '-1');
    };
    const onFocusOut = event => {
        // Focus moved to another element on purpose: never pull it back. (A null relatedTarget is a window blur
        // or a focus fixup - the pass decides.)
        if (event.relatedTarget) state.lastFocus = null;
    };

    root.addEventListener('keydown', onKeyDown);
    root.addEventListener('focusin', onFocusIn);
    root.addEventListener('focusout', onFocusOut);
    state.onKeyDown = onKeyDown;
    state.onFocusIn = onFocusIn;
    state.onFocusOut = onFocusOut;

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
        // data-tm-divider-redundant is deliberately NOT in the filter: the measurement sets it (no loop).
        mutation.observe(root, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class', 'disabled', 'data-rank', 'data-pin'] });
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
    root.removeEventListener('focusout', state.onFocusOut);
    for (const control of Array.from(root.querySelectorAll(CONTROL_SELECTOR))) control.removeAttribute('tabindex');
    attached.delete(root);
}

/** Test seam: kept for symmetry with the other modules (state is per element and weakly held). */
export function __resetForTests() {
}