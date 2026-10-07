// Shared container-width observer for TmLayoutObserver.
//
// A component imported into a host page cannot trust the viewport: a 390px dashboard inside a
// 1440px page is mobile. One ResizeObserver watches every registered root, and .NET is called only
// when the resolved mode changes — a resize inside the same mode must not re-render.
//
// The mode boundaries come from the caller (TmBreakpoints). This file has no breakpoint literals:
// a literal here would drift from TmBreakpoints.Classify. Blazor owns data-layout; this script
// never writes it.
//
// Registrations are keyed by the element. Blazor detaches the element before a dispose callback
// runs, so the id the component owns is what disconnect() takes.

const tracked = new Map();
let shared = null;

/** @param {number} widthPx @param {{ sm: number, lg: number }} breakpoints */
export function classifyWidth(widthPx, breakpoints) {
    if (!Number.isFinite(widthPx) || widthPx < 0) {
        throw new Error(`A container width must be a finite, non-negative number of CSS pixels (got ${widthPx}).`);
    }
    if (!breakpoints || !Number.isFinite(breakpoints.sm) || !Number.isFinite(breakpoints.lg)) {
        throw new Error('layout-observer: breakpoints { sm, lg } are required.');
    }
    if (widthPx < breakpoints.sm) return 'mobile';
    if (widthPx < breakpoints.lg) return 'tablet';
    return 'desktop';
}

function report(entry, widthPx) {
    const mode = classifyWidth(widthPx, entry.breakpoints);
    if (mode === entry.mode) return;
    entry.mode = mode;
    // The circuit may already be gone. A rejected invoke must not surface as an unhandled rejection.
    entry.dotnet.invokeMethodAsync('OnLayoutModeChanged', mode).catch(() => {});
}

function sharedObserver() {
    if (shared) return shared;
    if (typeof ResizeObserver !== 'function') return null;
    shared = new ResizeObserver(records => {
        for (const record of records) {
            const entry = tracked.get(record.target);
            if (!entry) continue;
            const width = record.contentRect?.width;
            if (typeof width === 'number') report(entry, width);
        }
    });
    return shared;
}

/**
 * Watch <paramref>element</paramref> and report its layout mode through
 * <c>dotnet.invokeMethodAsync('OnLayoutModeChanged', mode)</c>.
 * @param {HTMLElement} element The component root.
 * @param {{ invokeMethodAsync: Function }} dotnet A DotNetObjectReference.
 * @param {string} id The registration key. Re-observing the same id releases the previous observer.
 * @param {{ initialWidth?: number, breakpoints: { sm: number, md: number, lg: number } }} options The TmBreakpoints values, and a width already measured by the caller.
 */
export function observe(element, dotnet, id, options) {
    if (!element) {
        return Promise.reject(new Error('layout-observer: a root element is required.'));
    }
    disconnect(id);

    const entry = { element, dotnet, id, mode: null, breakpoints: options?.breakpoints };
    tracked.set(element, entry);

    // ResizeObserver fires an initial observation, so a clientWidth report here would measure a
    // different box than the one the observer reports.
    sharedObserver()?.observe(element);
    return Promise.resolve();
}

/** Release the observer registered under <paramref>id</paramref>. A missing id is a no-op. */
export function disconnect(id) {
    for (const [element, entry] of tracked) {
        if (entry.id !== id) continue;
        tracked.delete(element);
        shared?.unobserve(element);
    }
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const element of [...tracked.keys()]) {
        shared?.unobserve(element);
        tracked.delete(element);
    }
    shared?.disconnect();
    shared = null;
}
