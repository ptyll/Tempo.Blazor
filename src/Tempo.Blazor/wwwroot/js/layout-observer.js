// Shared container-width observer for TmLayoutObserver.
//
// A component imported into a host page cannot trust the viewport: a 390px dashboard inside a
// 1440px page is mobile. ResizeObserver reports the component root's own inline size, and .NET is
// called only when the resolved mode changes — a resize inside the same mode must not re-render.
//
// The mode boundaries are the TmBreakpoints contract (Mobile < 640, Tablet < 1024, else Desktop).
// 768 and 1280 are named CSS steps, not mode boundaries. classifyWidth is pure so the boundaries
// are exercised without a DOM; see wwwroot/js/__tests__/layout-observer.test.mjs.
//
// Registrations are keyed by a string the component owns. Blazor detaches the element before a
// dispose callback runs, so an element-keyed map could never be released.

const tracked = new Map();

/** @param {number} widthPx */
export function classifyWidth(widthPx) {
    if (!Number.isFinite(widthPx) || widthPx < 0) {
        throw new Error(`A container width must be a finite, non-negative number of CSS pixels (got ${widthPx}).`);
    }
    if (widthPx < 640) return 'mobile';
    if (widthPx < 1024) return 'tablet';
    return 'desktop';
}

function report(entry, widthPx) {
    const mode = classifyWidth(widthPx);
    entry.element.dataset.layout = mode;
    if (mode === entry.mode) return;
    entry.mode = mode;
    entry.dotnet.invokeMethodAsync('OnLayoutModeChanged', mode);
}

/**
 * Watch <paramref>element</paramref> and report its layout mode through
 * <c>dotnet.invokeMethodAsync('OnLayoutModeChanged', mode)</c>.
 * @param {HTMLElement} element The component root.
 * @param {{ invokeMethodAsync: Function }} dotnet A DotNetObjectReference.
 * @param {string} id The registration key. Re-observing the same id releases the previous observer.
 * @param {{ initialWidth?: number }} [options] A width already measured by the caller, so the first mode does not wait for the observer's first frame.
 */
export function observe(element, dotnet, id, options) {
    if (!element) {
        return Promise.reject(new Error('layout-observer: a root element is required.'));
    }
    if (tracked.has(id)) {
        disconnect(id);
    }

    const entry = { element, dotnet, mode: null, observer: null };
    tracked.set(id, entry);

    if (typeof ResizeObserver === 'function') {
        entry.observer = new ResizeObserver(records => {
            const current = tracked.get(id);
            if (current !== entry) return;
            const width = records[0]?.contentRect?.width;
            if (typeof width === 'number') report(entry, width);
        });
        entry.observer.observe(element);
    }

    const initial = options?.initialWidth ?? element.clientWidth;
    if (typeof initial === 'number' && Number.isFinite(initial)) {
        report(entry, initial);
    }
    return Promise.resolve();
}

/** Release the observer registered under <paramref>id</paramref>. A missing id is a no-op. */
export function disconnect(id) {
    const entry = tracked.get(id);
    if (!entry) return;
    tracked.delete(id);
    entry.observer?.disconnect();
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...tracked.keys()]) disconnect(id);
}
