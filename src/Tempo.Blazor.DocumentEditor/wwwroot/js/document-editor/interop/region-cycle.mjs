// F6 / Shift+F6 region cycle of TmDocumentEditor (redesign 146c7e1b, DE-1.1).
//
// The editor chrome is five landmarks - header, ribbon, navigation, document, inspector - and F6 moves the focus
// from one to the next (Shift+F6 back), wrapping around. The browser's own F6 jumps to the address bar, and the
// editor's root keydown handler runs in Blazor (a server round trip on Blazor Server), so a key that must cancel the
// default has to be handled synchronously here. Regions are marked in the markup with data-focus-region; a region that
// does not exist (no header, no left column on a tablet container) or is not displayed is skipped.
//
// The ribbon keyboard mode (Alt / F10, data-ribbon-keyboard-mode) is Blazor state and is NOT touched: F6 only moves focus.

export const REGION_ORDER = ['header', 'ribbon', 'nav', 'doc', 'inspector'];

// Ordered per region: the FIRST selector that has a displayed match wins, so the preferred control is never shadowed by an
// earlier plain button in document order (a single comma-joined selector would return the first match in DOM order).
const FOCUS_TARGETS = {
    header: ['button:not([disabled])', 'a[href]', 'input:not([disabled])', '[tabindex="0"]'],
    // The selected tab first: a ribbon is one tab stop.
    ribbon: ['[role="tab"][aria-selected="true"]', '[role="tab"]', 'button:not([disabled])'],
    nav: ['button:not([disabled])', 'a[href]', '[tabindex="0"]'],
    // The canvas engine owns a hidden textarea that receives the typing and the caret.
    doc: ['.tm-document-canvas-engine-host__input', 'textarea:not([disabled])', '[contenteditable="true"]', 'button:not([disabled])'],
    inspector: ['[role="tab"][aria-selected="true"]', '[role="tab"]', 'button:not([disabled])', 'input:not([disabled])', 'select:not([disabled])'],
};

// How long the "you are here" ring of the region F6 entered stays, unless a pointer action clears it earlier.
export const REGION_FOCUS_MS = 1500;
const REGION_FOCUS_ATTRIBUTE = 'data-region-focus';

const attached = new WeakMap();

function isDisplayed(element) {
    if (!element) return false;
    if (typeof element.getClientRects === 'function') return element.getClientRects().length > 0;
    return true;
}

/** The marked regions of the editor, in cycle order, skipping the ones that are absent or not displayed. */
export function resolveRegions(root) {
    const found = new Map();
    for (const element of root.querySelectorAll('[data-focus-region]')) {
        const name = element.getAttribute('data-focus-region');
        if (REGION_ORDER.includes(name) && !found.has(name) && isDisplayed(element)) {
            found.set(name, element);
        }
    }
    return REGION_ORDER.filter(name => found.has(name)).map(name => ({ name, element: found.get(name) }));
}

/** Index of the region that contains <code>active</code>, or -1 when the focus is outside every region. */
export function currentRegionIndex(regions, active) {
    if (!active) return -1;
    return regions.findIndex(region => region.element === active || region.element.contains(active));
}

/** The next index of a wrap-around cycle. From outside every region (-1), forward starts at the first and backward at the last. */
export function nextRegionIndex(current, count, backwards) {
    if (count <= 0) return -1;
    if (current < 0) return backwards ? count - 1 : 0;
    return backwards ? (current - 1 + count) % count : (current + 1) % count;
}

function firstDisplayed(region) {
    for (const selector of FOCUS_TARGETS[region.name] ?? []) {
        for (const candidate of region.element.querySelectorAll(selector)) {
            if (typeof candidate.focus === 'function' && isDisplayed(candidate)) return candidate;
        }
    }
    return null;
}

const regionFocusTimers = new WeakMap();

/** Marks the region F6 just entered (a visible ring) for a moment; a pointer action or the next F6 clears it. */
export function markRegionFocus(region, setTimeoutFn = globalThis.setTimeout, clearTimeoutFn = globalThis.clearTimeout) {
    region.element.setAttribute?.(REGION_FOCUS_ATTRIBUTE, 'true');
    const previous = regionFocusTimers.get(region.element);
    if (previous) clearTimeoutFn?.(previous);
    if (typeof setTimeoutFn === 'function') {
        regionFocusTimers.set(region.element, setTimeoutFn(() => region.element.removeAttribute?.(REGION_FOCUS_ATTRIBUTE), REGION_FOCUS_MS));
    }
}

/** Removes every region ring under <code>root</code>. */
export function clearRegionFocus(root) {
    if (!root?.querySelectorAll) return;
    for (const element of root.querySelectorAll(`[${REGION_FOCUS_ATTRIBUTE}]`)) element.removeAttribute?.(REGION_FOCUS_ATTRIBUTE);
}

/** Moves the focus into a region; returns true when something took it. */
export function focusRegion(region) {
    const target = firstDisplayed(region);
    if (target) {
        target.focus({ preventScroll: false });
        return true;
    }
    if (typeof region.element.focus === 'function') {
        region.element.focus();
        return true;
    }
    return false;
}

/** The key handler: exported so a node test can drive it without a DOM. */
export function handleRegionCycleKey(event, root, doc = globalThis.document) {
    if (event.key !== 'F6' || event.ctrlKey || event.altKey || event.metaKey) return false;

    // A modal surface owns F6 while it is up.
    const target = event.target;
    if (target && typeof target.closest === 'function' && target.closest('[aria-modal="true"]')) return false;

    const regions = resolveRegions(root);
    if (regions.length === 0) return false;

    const current = currentRegionIndex(regions, doc?.activeElement ?? target);
    const next = nextRegionIndex(current, regions.length, event.shiftKey === true);
    event.preventDefault();
    event.stopPropagation();
    clearRegionFocus(root);
    const moved = focusRegion(regions[next]);
    if (moved) markRegionFocus({ ...regions[next], root });
    return moved;
}

/**
 * Publishes the height of the sticky chrome above the workspace (header + ribbon) as --tm-document-ribbon-block-size on the
 * root, so the sticky navigation column parks BELOW the ribbon instead of sliding under it. Re-measured when the ribbon or the
 * root resizes; the property is only written when the rounded value changes.
 */
export function observeRibbonSize(root, ResizeObserverCtor = globalThis.ResizeObserver) {
    let last = -1;
    const measure = () => {
        const ribbon = root.querySelector?.('[data-focus-region="ribbon"]');
        const height = ribbon ? Math.round(ribbon.getBoundingClientRect?.().height ?? 0) : 0;
        if (height === last) return;
        last = height;
        root.style?.setProperty?.('--tm-document-ribbon-block-size', `${height}px`);
    };
    measure();
    if (typeof ResizeObserverCtor !== 'function') return () => {};
    const observer = new ResizeObserverCtor(measure);
    observer.observe(root);
    const ribbon = root.querySelector?.('[data-focus-region="ribbon"]');
    if (ribbon) observer.observe(ribbon);
    return () => {
        observer.disconnect?.();
        root.style?.removeProperty?.('--tm-document-ribbon-block-size');
    };
}

/** Registers the capture-phase F6 handler on the editor root. Idempotent per root. */
export function attachRegionCycle(root) {
    if (!root || attached.has(root)) return;
    const handler = event => { handleRegionCycleKey(event, root); };
    const clearRing = () => clearRegionFocus(root);
    root.addEventListener('keydown', handler, true);
    root.addEventListener('pointerdown', clearRing, true);
    attached.set(root, { handler, clearRing, stopRibbon: observeRibbonSize(root) });
}

export function detachRegionCycle(root) {
    const state = attached.get(root);
    if (!state) return;
    root.removeEventListener('keydown', state.handler, true);
    root.removeEventListener('pointerdown', state.clearRing, true);
    state.stopRibbon?.();
    clearRegionFocus(root);
    attached.delete(root);
}
