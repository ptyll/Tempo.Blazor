// Tempo.Blazor editor shell (ES module).
//
// Two jobs for TmEditorShell, both optional and best-effort:
//   1. the panel resize separator — the gesture, the width maths and the persistence live in the
//      SHARED helper (`tm-panel-resize.js` over `pointer-drag.js`), which TmAppShell's work area
//      uses too. This file only pins the editor shell's own knobs: the CSS width variable, the
//      drag class, the canvas-region selector and the storage prefix. The keyboard path lives in
//      Blazor and shares clampWidth's rules with EditorShellResize.Clamp (the same vectors are
//      asserted on both sides);
//   2. width persistence under a host-chosen localStorage key (the shared helper's storage, under
//      the editor shell's own prefix). Private mode, disabled storage and SSR simply skip
//      persistence — nothing here may throw into the render loop.

import {
    attachResize as attachPanelResize,
    clampWidth,
    detachResize as detachPanelResize,
    loadStored,
    measureWidth,
    measureRegion,
    regionWidth,
    resetPanelResizes,
    saveStored,
} from './tm-panel-resize.js';

const STORAGE_PREFIX = 'tempo.tm-editor-shell.';
const DRAGGING_CLASS = 'tm-editor-shell__resizer--dragging';
const CANVAS_SELECTOR = '[data-region="canvas"]';
const WIDTH_VAR = '--tm-editor-shell-panel-width';
const COMMIT_METHOD = 'OnResizeCommitted';

// The width maths are the shared helper's: re-exported so the editor shell's public module surface
// (and every consumer/test of it) keeps working unchanged.
export { clampWidth, measureWidth };

/**
 * The width of the canvas region of a shell main element, or NaN when it cannot be measured.
 * @param {HTMLElement|null} main the `.tm-editor-shell__main` element
 */
export function canvasWidth(main) {
    return regionWidth(main, CANVAS_SELECTOR);
}

/** Same as canvasWidth, but -1 instead of NaN so it serialises across the interop boundary. */
export function measureCanvas(main) {
    return measureRegion(main, CANVAS_SELECTOR);
}

/**
 * Scrolls the mobile panels sheet back to its top. The sheet body is ONE scroller shared by every tab, so a tab switch would
 * otherwise start at the previous tab's offset. Returns whether a sheet body was found.
 * @param {HTMLElement|null} stage the `.tm-editor-shell__stage` element that hosts the sheet
 * @returns {boolean}
 */
export function resetSheetScroll(stage) {
    const body = stage?.querySelector?.('.tm-editor-shell__sheet .tm-drawer__body');
    if (!body) return false;
    body.scrollTop = 0;
    return true;
}

/**
 * Reads the stored panel widths JSON, or null when nothing is stored / storage is unavailable.
 * @param {string} key the host-chosen persistence key
 * @returns {string|null}
 */
export function loadPanelWidths(key) {
    return loadStored(STORAGE_PREFIX, key);
}

/**
 * Stores the panel widths JSON; a null payload removes the key. No-op when storage is unavailable.
 * @param {string} key the host-chosen persistence key
 * @param {string|null} json the widths JSON, or null to remove the entry
 */
export function savePanelWidths(key, json) {
    saveStored(STORAGE_PREFIX, key, json);
}

/**
 * Attaches the pointer-drag resize to a separator, clamped to the panel's min/max width and to the
 * canvas's minimum. Thin wrapper over the shared helper: the editor shell's canvas region selector,
 * width variable, drag class and commit method name are pinned here.
 * @param {HTMLElement} handle the separator element
 * @param {HTMLElement} panel the aside whose width is resized
 * @param {HTMLElement} main the shell main element (its canvas region is measured)
 * @param {{ invokeMethodAsync: Function }|null} dotnet the shell, which receives OnResizeCommitted
 * @param {string} id registration key; attaching again with the same id replaces the previous one
 * @param {{ side: 'left'|'right', min: number, max: number, minCanvas: number }} options
 */
export function attachResize(handle, panel, main, dotnet, id, options) {
    attachPanelResize(handle, panel, main, dotnet, id, {
        ...options,
        widthVar: WIDTH_VAR,
        dragClass: DRAGGING_CLASS,
        regionSelector: CANVAS_SELECTOR,
        method: COMMIT_METHOD,
    });
}

/** Removes the resize registered under the id. Safe to call twice or for an unknown id. */
export function detachResize(id) {
    detachPanelResize(id);
}

const canvasObservers = new Map();

/**
 * Watches the canvas region of a shell main element with a ResizeObserver. A container or window
 * resize can change the canvas width without a Blazor render (no OnAfterRender runs), which would
 * leave the tablet fit stale; the rAF-debounced observer reports the change through
 * OnCanvasResized so the fit recomputes. One observer per element: observing the same main again
 * replaces the first. No-op where ResizeObserver is unavailable (the fit then follows renders).
 * @param {HTMLElement} main the shell main element (its canvas region is watched)
 * @param {{ invokeMethodAsync: Function }|null} dotnet the shell, which receives OnCanvasResized
 */
export function observeCanvas(main, dotnet) {
    if (!main) return;
    disconnectCanvas(main);
    if (typeof ResizeObserver === 'undefined') return;
    const canvas = main.querySelector?.(CANVAS_SELECTOR) ?? main;
    let frame = 0;
    const observer = new ResizeObserver(() => {
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            // The circuit may already be gone: a rejected invoke must not become an unhandled rejection.
            dotnet?.invokeMethodAsync('OnCanvasResized')?.catch?.(() => {});
        });
    });
    observer.observe(canvas);
    canvasObservers.set(main, {
        observer,
        cancel() { if (frame) { cancelAnimationFrame(frame); frame = 0; } },
    });
}

/** Stops the canvas watch for one main element. Safe to call twice or for an unknown element. */
export function disconnectCanvas(main) {
    const entry = canvasObservers.get(main);
    if (!entry) return;
    canvasObservers.delete(main);
    entry.cancel();
    entry.observer.disconnect();
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    resetPanelResizes();
    for (const main of [...canvasObservers.keys()]) disconnectCanvas(main);
}
