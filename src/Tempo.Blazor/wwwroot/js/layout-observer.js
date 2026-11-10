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

/**
 * Whether the primary pointer is coarse (a finger). A missing or throwing matchMedia is a fine
 * pointer — the same answer static render and node tests get before any browser signal arrives.
 * @param {((query: string) => { matches?: boolean }) | undefined} matchMedia
 * @returns {boolean}
 */
export function readPointer(matchMedia) {
    try {
        return matchMedia?.('(pointer: coarse)')?.matches === true;
    } catch {
        return false;
    }
}

function pointerQuery() {
    if (typeof globalThis.matchMedia !== 'function') return null;
    try {
        return globalThis.matchMedia('(pointer: coarse)');
    } catch {
        return null;
    }
}

function reportPointer(entry, coarse) {
    if (coarse === entry.pointer) return;
    entry.pointer = coarse;
    entry.dotnet.invokeMethodAsync('OnPointerKindChanged', coarse).catch(() => {});
}

/** Subscribe to the coarse-pointer query. A change listener only — never pointermove. */
function attachPointer(entry) {
    const query = pointerQuery();
    if (!query) return;
    entry.pointerQuery = query;
    const coarse = readPointer(() => query);
    entry.pointer = coarse;
    entry.onPointer = () => reportPointer(entry, readPointer(() => query));
    if (typeof query.addEventListener === 'function') query.addEventListener('change', entry.onPointer);
    else if (typeof query.addListener === 'function') query.addListener(entry.onPointer);
    // A fine pointer is the .NET default, so only a coarse match is worth a render.
    if (coarse) entry.dotnet.invokeMethodAsync('OnPointerKindChanged', true).catch(() => {});
}

function detachPointer(entry) {
    const query = entry.pointerQuery;
    if (!query || !entry.onPointer) return;
    if (typeof query.removeEventListener === 'function') query.removeEventListener('change', entry.onPointer);
    else if (typeof query.removeListener === 'function') query.removeListener(entry.onPointer);
    entry.onPointer = null;
    entry.pointerQuery = null;
}

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
    if (mode !== entry.mode) {
        entry.mode = mode;
        // The circuit may already be gone. A rejected invoke must not surface as an unhandled rejection.
        entry.dotnet.invokeMethodAsync('OnLayoutModeChanged', mode).catch(() => {});
    }

    // An opt-in raw-width report for owners that derive geometry from the container (a measured
    // column count): a resize INSIDE one mode can still change the derived number, so this fires on
    // every delivered width, not only on a mode change. Rounded to whole pixels so sub-pixel
    // oscillation (scrollbar appear/disappear) cannot re-render the owner each frame.
    if (entry.reportWidth) {
        const rounded = Math.round(widthPx);
        if (rounded !== entry.width) {
            entry.width = rounded;
            entry.dotnet.invokeMethodAsync('OnContainerWidthChanged', rounded).catch(() => {});
        }
    }
}

function sharedObserver() {
    if (shared) return shared;
    if (typeof ResizeObserver !== 'function') return null;
    shared = new ResizeObserver(records => {
        for (const record of records) {
            const entry = tracked.get(record.target);
            if (!entry) continue;
            // A viewport-scope entry classifies the window: its own content box can stay at the
            // previous band across a viewport flip and would report mobile over the resize listener.
            const width = entry.measureWindow && typeof window !== 'undefined'
                ? window.innerWidth
                : record.contentRect?.width;
            // Ignore degenerate widths: while the viewport is re-emulated (Playwright fullPage
            // capture, print preview, a hidden→shown transition) Chromium reports transient 0-size
            // content boxes. Classifying one would flip the whole shell to mobile — the editor
            // canvas remounts and every open piece of UI state is lost. Keep the last good mode.
            if (typeof width !== 'number' || width < 1) continue;
            report(entry, width);
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
 * @param {{ initialWidth?: number, breakpoints: { sm: number, md: number, lg: number }, reportWidth?: boolean }} options The TmBreakpoints values, a width already measured by the caller, and whether every resize should also report the raw width through <c>OnContainerWidthChanged</c>.
 */
export function observe(element, dotnet, id, options) {
    if (!element) {
        return Promise.reject(new Error('layout-observer: a root element is required.'));
    }
    disconnect(id);

    const entry = {
        element, dotnet, id, mode: null, width: null, breakpoints: options?.breakpoints,
        // A viewport-scope observer classifies the window, not its own box: a fixed probe (or a
        // root whose box the ResizeObserver does not re-deliver) keeps the last mode across a
        // Playwright 390 → 1440 flip, and every menu pinned to that scope stays a clipped sheet.
        measureWindow: options?.measureWindow === true,
        // Opt-in raw-width report; false keeps every existing consumer on the mode-only contract.
        reportWidth: options?.reportWidth === true,
    };
    tracked.set(element, entry);
    attachPointer(entry);

    // ResizeObserver fires an initial observation, so there is no clientWidth fallback: that box
    // differs from the content box the observer reports. initialWidth is only the caller's own
    // measurement, used by tests.
    sharedObserver()?.observe(element);
    if (typeof options?.initialWidth === 'number' && Number.isFinite(options.initialWidth)) {
        report(entry, options.initialWidth);
    }
    // A Playwright viewport flip (390 → 1440) resizes the window. A fixed probe whose box the
    // ResizeObserver does not re-deliver keeps the last mode, so the shell stays mobile and a
    // later desktop menu is clipped. Read the live width on the window resize itself.
    entry.onResize = () => {
        // window.innerWidth is updated before the resize event; the element's own box may still be
        // the previous band until the next frame, which is exactly the box that stuck the mode.
        const live = entry.measureWindow && typeof window !== 'undefined'
            ? window.innerWidth
            : element.getBoundingClientRect?.().width;
        if (typeof live === 'number' && live >= 1) report(entry, live);
    };
    if (typeof window !== 'undefined') window.addEventListener('resize', entry.onResize);
    return Promise.resolve();
}

/** Release the observer registered under <paramref>id</paramref>. A missing id is a no-op. */
export function disconnect(id) {
    for (const [element, entry] of tracked) {
        if (entry.id !== id) continue;
        tracked.delete(element);
        shared?.unobserve(element);
        detachPointer(entry);
        if (entry.onResize && typeof window !== 'undefined') window.removeEventListener('resize', entry.onResize);
    }
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const element of [...tracked.keys()]) {
        const entry = tracked.get(element);
        if (entry) detachPointer(entry);
        shared?.unobserve(element);
        tracked.delete(element);
    }
    shared?.disconnect();
    shared = null;
}
