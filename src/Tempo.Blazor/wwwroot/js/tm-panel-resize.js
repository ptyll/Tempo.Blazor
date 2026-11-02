// Tempo.Blazor panel-resize helper (ES module) — the ONE resize-separator gesture and persistence
// for the components that let a user drag a docked panel wider or narrower: TmEditorShell (its
// panels) and TmAppShell (its work area). Hosts must not write their own drag (docs/touch-interaction.md).
//
//   import { attachResize, clampWidth, detachResize, loadStored, measureRegion, saveStored }
//       from './tm-panel-resize.js';
//
// What this file owns (so a host component does not have to):
//   * the width maths shared with C# — `clampWidth` is the same contract as the C#
//     `PanelResize.Clamp` (whole pixels, within [min, max], never taking the content region below
//     its minimum; both sides are pinned by the same vectors);
//   * the pointer-drag resize: the live width follows the pointer as a CSS custom property on the
//     panel, the clamped width is reported ONCE on release, and Escape / pointercancel / a lost
//     capture / a mid-drag detach put the start width back without reporting anything. The gesture
//     itself (capture by pointerId, threshold, cancel paths) is pointer-drag.js;
//   * the keydown guard that stops the ARIA separator's own keys (arrows, Home/End) from scrolling
//     the page — the resize itself runs in Blazor, so a host keeps its keyboard path in C#;
//   * best-effort localStorage persistence namespaced by a host prefix. Private mode, disabled
//     storage and SSR simply skip persistence — nothing here may throw into the render loop.
//
// A resize is never the only way to set the width: the separator is a focusable ARIA separator with
// arrows, Home/End and a double-click reset (WCAG 2.5.7).

import { attach as attachPointerDrag } from './pointer-drag.js';

const RESIZE_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'End']);
const DEFAULT_WIDTH_VAR = '--tm-panel-width';
const DEFAULT_DRAG_CLASS = 'tm-panel-resize__handle--dragging';
const DEFAULT_METHOD = 'OnResizeCommitted';

/**
 * Clamps a requested panel width: whole pixels, within [min, max], and never so wide that the
 * content region would drop below its minimum. A panel is never reversed when the region is
 * already too small — growth is refused, shrinking is always allowed.
 * @param {number} requested the width the pointer or key asked for
 * @param {number} current the width the gesture started from
 * @param {number} min smallest allowed panel width
 * @param {number} max largest allowed panel width
 * @param {number} region the content region width at the start of the gesture; NaN/undefined when unknown
 * @param {number} minRegion the content width a resize never takes away
 * @returns {number} the width in whole pixels
 */
export function clampWidth(requested, current, min, max, region, minRegion) {
    let width = Math.min(max, Math.max(min, Math.round(requested)));
    if (Number.isFinite(region) && width > current) {
        width = Math.min(width, Math.max(current, current + (region - minRegion)));
    }
    return width;
}

/**
 * The client width of the element a resize measures: the region itself, or the element the selector
 * resolves inside it. NaN when it cannot be measured (a missing element, a detached node).
 * @param {HTMLElement|null} region the element the gesture resizes against
 * @param {string|null} [selector] an optional descendant selector (an editor's canvas region)
 * @returns {number}
 */
export function regionWidth(region, selector) {
    const target = selector ? region?.querySelector?.(selector) : region;
    const width = target?.clientWidth;
    return typeof width === 'number' && Number.isFinite(width) ? width : NaN;
}

/**
 * Same as regionWidth, but -1 instead of NaN so it serialises across the interop boundary.
 * @param {HTMLElement|null} region the element the gesture resizes against
 * @param {string|null} [selector] an optional descendant selector
 * @returns {number}
 */
export function measureRegion(region, selector) {
    const width = regionWidth(region, selector);
    return Number.isNaN(width) ? -1 : width;
}

/**
 * The rendered width of an element in whole pixels, or -1 when it cannot be measured. A host's
 * keyboard path steps from this when the configured width is not a plain pixel length (rem, %).
 * @param {HTMLElement|null} element the panel
 * @returns {number}
 */
export function measureWidth(element) {
    const width = element?.getBoundingClientRect?.().width;
    return typeof width === 'number' && Number.isFinite(width) ? Math.round(width) : -1;
}

/**
 * Reads the stored layout JSON for the key, or null when nothing is stored / storage is unavailable.
 * @param {string} prefix the host's storage namespace (e.g. <c>tempo.tm-app-shell.</c>)
 * @param {string} key the host-chosen persistence key
 * @returns {string|null}
 */
export function loadStored(prefix, key) {
    try {
        return globalThis.localStorage?.getItem(`${prefix}${key}`) ?? null;
    }
    catch {
        return null;
    }
}

/**
 * Stores the layout JSON; a null payload removes the key. No-op when storage is unavailable.
 * @param {string} prefix the host's storage namespace
 * @param {string} key the host-chosen persistence key
 * @param {string|null} json the layout JSON, or null to remove the entry
 */
export function saveStored(prefix, key, json) {
    try {
        const storage = globalThis.localStorage;
        if (!storage) return;
        if (json === null || json === undefined) storage.removeItem(`${prefix}${key}`);
        else storage.setItem(`${prefix}${key}`, json);
    }
    catch {
        // Storage unavailable (private mode, quota) — persistence is best-effort only.
    }
}

const resizers = new Map();

/**
 * Attaches the pointer-drag resize to a separator. The panel width follows the pointer live (the CSS
 * custom property named by <paramref>options.widthVar</paramref>, and <c>aria-valuenow</c> on the
 * handle); on release the clamped width is reported ONCE through
 * <paramref>options.method</paramref> with <c>(side, width)</c>. Escape, pointercancel, a lost
 * pointer capture and a mid-drag detach restore the start width without reporting.
 * @param {HTMLElement} handle the separator element
 * @param {HTMLElement} panel the panel whose width is resized
 * @param {HTMLElement} region the element measured for the "never take the content below its
 *   minimum" rule (with <paramref>options.regionSelector</paramref> when the region is a descendant)
 * @param {{ invokeMethodAsync: Function }|null} dotnet the component, which receives the commit callback
 * @param {string} id registration key; attaching again with the same id replaces the previous one
 * @param {{ side: 'left'|'right', min: number, max: number, minCanvas?: number, minRegion?: number,
 *           widthVar?: string, dragClass?: string, regionSelector?: string|null, method?: string }} options
 */
export function attachResize(handle, panel, region, dotnet, id, options) {
    if (!handle || !panel) return;
    detachResize(id);

    const { side, min, max } = options;
    const minRegion = options.minRegion ?? options.minCanvas ?? 0;
    const widthVar = options.widthVar ?? DEFAULT_WIDTH_VAR;
    const regionSelector = options.regionSelector ?? null;
    const method = options.method ?? DEFAULT_METHOD;
    // The separator of a left panel sits on its right edge, so dragging right grows it; a right
    // panel's separator is on its left edge, so dragging left grows it.
    const direction = side === 'left' ? 1 : -1;

    let startWidth = 0;
    let startRegion = NaN;
    let width = 0;

    const show = (value) => {
        panel.style.setProperty(widthVar, `${value}px`);
        handle.setAttribute?.('aria-valuenow', String(value));
    };

    const drag = attachPointerDrag(handle, {
        threshold: 0,
        dragClass: options.dragClass ?? DEFAULT_DRAG_CLASS,
        // The resizer owns no click, and the separator's keyboard path lives in Blazor.
        suppressClick: false,
        onPress() {
            startWidth = Math.round(panel.getBoundingClientRect().width);
            startRegion = regionWidth(region, regionSelector);
            width = startWidth;
        },
        onMove({ dx }) {
            width = clampWidth(startWidth + direction * dx, startWidth, min, max, startRegion, minRegion);
            show(width);
        },
        onEnd() {
            const committed = width;
            if (committed !== startWidth) {
                // The circuit may already be gone: a rejected invoke must not become an unhandled rejection.
                dotnet?.invokeMethodAsync(method, side, committed)?.catch?.(() => {});
            }
        },
        // Escape, pointercancel, a lost capture, or a panel that collapses / flips layout mid-drag
        // (detach): put the live width back. Nothing is committed.
        onCancel() {
            show(startWidth);
        },
    });

    // The keys the separator handles must not also scroll the page; Blazor performs the resize.
    const onKey = (event) => {
        if (RESIZE_KEYS.has(event.key)) event.preventDefault?.();
    };
    handle.addEventListener('keydown', onKey);

    resizers.set(id, { handle, drag, onKey });
}

/** Removes the resize registered under the id. Safe to call twice or for an unknown id. */
export function detachResize(id) {
    const entry = resizers.get(id);
    if (!entry) return;
    resizers.delete(id);
    entry.drag.detach();
    entry.handle.removeEventListener('keydown', entry.onKey);
}

/**
 * Drops every registration. A host component calls this when it stops rendering separators; the
 * test suites call it between cases (a module imported by Node is shared across tests).
 */
export function resetPanelResizes() {
    for (const id of [...resizers.keys()]) detachResize(id);
}
