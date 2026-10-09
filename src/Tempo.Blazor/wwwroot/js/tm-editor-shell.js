// Tempo.Blazor editor shell (ES module).
//
// Two jobs for TmEditorShell, both optional and best-effort:
//   1. the panel resize separator — the pointer drag lives here (pointer capture, live width, one
//      commit on release); the keyboard path lives in Blazor and shares clampWidth's rules with
//      EditorShellResize.Clamp (the same vectors are asserted on both sides);
//   2. width persistence under a host-chosen localStorage key. Private mode, disabled storage and SSR
//      simply skip persistence — nothing here may throw into the render loop.

const STORAGE_PREFIX = 'tempo.tm-editor-shell.';
const RESIZE_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'End']);
const WIDTH_VAR = '--tm-editor-shell-panel-width';

/**
 * Clamps a requested panel width: whole pixels, within [min, max], and never so wide that the canvas
 * would drop below its minimum. A panel is never reversed when the canvas is already too small —
 * growth is refused, shrinking is always allowed.
 * @param {number} requested the width the pointer or key asked for
 * @param {number} current the width the gesture started from
 * @param {number} min smallest allowed panel width
 * @param {number} max largest allowed panel width
 * @param {number} canvas the canvas width at the start of the gesture; NaN/undefined when unknown
 * @param {number} minCanvas the canvas width a resize never takes away
 * @returns {number} the width in whole pixels
 */
export function clampWidth(requested, current, min, max, canvas, minCanvas) {
    let width = Math.min(max, Math.max(min, Math.round(requested)));
    if (Number.isFinite(canvas) && width > current) {
        width = Math.min(width, Math.max(current, current + (canvas - minCanvas)));
    }
    return width;
}

/**
 * The width of the canvas region of a shell main element, or NaN when it cannot be measured.
 * @param {HTMLElement|null} main the `.tm-editor-shell__main` element
 */
export function canvasWidth(main) {
    const canvas = main?.querySelector?.('[data-region="canvas"]');
    const width = canvas?.clientWidth;
    return typeof width === 'number' && Number.isFinite(width) ? width : NaN;
}

/** Same as canvasWidth, but -1 instead of NaN so it serialises across the interop boundary. */
export function measureCanvas(main) {
    const width = canvasWidth(main);
    return Number.isNaN(width) ? -1 : width;
}

/**
 * The rendered width of an element in whole pixels, or -1 when it cannot be measured. The keyboard
 * path steps from this when the host width is not a plain pixel length (rem, %).
 * @param {HTMLElement|null} element the panel
 * @returns {number}
 */
export function measureWidth(element) {
    const width = element?.getBoundingClientRect?.().width;
    return typeof width === 'number' && Number.isFinite(width) ? Math.round(width) : -1;
}

/**
 * Reads the stored panel widths JSON, or null when nothing is stored / storage is unavailable.
 * @param {string} key the host-chosen persistence key
 * @returns {string|null}
 */
export function loadPanelWidths(key) {
    try {
        return globalThis.localStorage?.getItem(`${STORAGE_PREFIX}${key}`) ?? null;
    }
    catch {
        return null;
    }
}

/**
 * Stores the panel widths JSON; a null payload removes the key. No-op when storage is unavailable.
 * @param {string} key the host-chosen persistence key
 * @param {string|null} json the widths JSON, or null to remove the entry
 */
export function savePanelWidths(key, json) {
    try {
        const storage = globalThis.localStorage;
        if (!storage) return;
        if (json === null || json === undefined) storage.removeItem(`${STORAGE_PREFIX}${key}`);
        else storage.setItem(`${STORAGE_PREFIX}${key}`, json);
    }
    catch {
        // Storage unavailable (private mode, quota) — persistence is best-effort only.
    }
}

const resizers = new Map();

/**
 * Attaches the pointer-drag resize to a separator. The panel width follows the pointer live (a CSS
 * custom property on the panel); on release the clamped width is reported ONCE. Escape and
 * pointercancel restore the start width without reporting.
 * @param {HTMLElement} handle the separator element
 * @param {HTMLElement} panel the aside whose width is resized
 * @param {HTMLElement} main the shell main element (its canvas region is measured)
 * @param {{ invokeMethodAsync: Function }|null} dotnet the shell, which receives OnResizeCommitted
 * @param {string} id registration key; attaching again with the same id replaces the previous one
 * @param {{ side: 'left'|'right', min: number, max: number, minCanvas: number }} options
 */
export function attachResize(handle, panel, main, dotnet, id, options) {
    if (!handle || !panel) return;
    detachResize(id);

    const { side, min, max, minCanvas } = options;
    // The separator of a left panel sits on its right edge, so dragging right grows it; a right
    // panel's separator is on its left edge, so dragging left grows it.
    const direction = side === 'left' ? 1 : -1;

    let dragging = false;
    let pointerId = null;
    let startX = 0;
    let startWidth = 0;
    let startCanvas = NaN;
    let width = 0;

    const show = (value) => {
        panel.style.setProperty(WIDTH_VAR, `${value}px`);
        handle.setAttribute('aria-valuenow', String(value));
    };

    const finish = () => {
        const captured = pointerId;
        dragging = false;
        pointerId = null;
        handle.classList?.remove('tm-editor-shell__resizer--dragging');
        // The pointer id is required: releasePointerCapture() with no argument throws in a real DOM,
        // and the swallowed error left the capture held until the pointer went up.
        try { if (captured !== null) handle.releasePointerCapture?.(captured); } catch { /* already released */ }
        if (typeof document !== 'undefined') document.removeEventListener?.('keydown', onEscape);
    };

    const onEscape = (event) => {
        if (event.key !== 'Escape' || !dragging) return;
        show(startWidth);
        finish();
    };

    const onDown = (event) => {
        if (event.button !== undefined && event.button !== 0) return;
        event.preventDefault?.();
        dragging = true;
        pointerId = event.pointerId;
        startX = event.clientX;
        startWidth = Math.round(panel.getBoundingClientRect().width);
        startCanvas = canvasWidth(main);
        width = startWidth;
        try { handle.setPointerCapture?.(event.pointerId); } catch { /* the pointer is gone */ }
        handle.classList?.add('tm-editor-shell__resizer--dragging');
        if (typeof document !== 'undefined') document.addEventListener?.('keydown', onEscape);
    };

    const onMove = (event) => {
        if (!dragging || event.pointerId !== pointerId) return;
        width = clampWidth(startWidth + direction * (event.clientX - startX), startWidth, min, max, startCanvas, minCanvas);
        show(width);
    };

    const onUp = (event) => {
        if (!dragging || event.pointerId !== pointerId) return;
        const committed = width;
        finish();
        if (committed !== startWidth) {
            // The circuit may already be gone: a rejected invoke must not become an unhandled rejection.
            dotnet?.invokeMethodAsync('OnResizeCommitted', side, committed)?.catch?.(() => {});
        }
    };

    const onCancel = (event) => {
        if (!dragging || event.pointerId !== pointerId) return;
        show(startWidth);
        finish();
    };

    // The keys the separator handles must not also scroll the page; Blazor performs the resize.
    const onKey = (event) => {
        if (RESIZE_KEYS.has(event.key)) event.preventDefault?.();
    };

    // A panel that collapses, hides or flips layout mid-drag: put the live width back and let go.
    const abort = () => {
        if (!dragging) return;
        show(startWidth);
        finish();
    };

    handle.addEventListener('pointerdown', onDown);
    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onUp);
    handle.addEventListener('pointercancel', onCancel);
    handle.addEventListener('keydown', onKey);
    resizers.set(id, { handle, onDown, onMove, onUp, onCancel, onKey, onEscape, abort });
}

/** Removes the resize registered under the id. Safe to call twice or for an unknown id. */
export function detachResize(id) {
    const entry = resizers.get(id);
    if (!entry) return;
    resizers.delete(id);
    entry.abort?.();
    entry.handle.removeEventListener('pointerdown', entry.onDown);
    entry.handle.removeEventListener('pointermove', entry.onMove);
    entry.handle.removeEventListener('pointerup', entry.onUp);
    entry.handle.removeEventListener('pointercancel', entry.onCancel);
    entry.handle.removeEventListener('keydown', entry.onKey);
    if (typeof document !== 'undefined') document.removeEventListener?.('keydown', entry.onEscape);
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...resizers.keys()]) detachResize(id);
}