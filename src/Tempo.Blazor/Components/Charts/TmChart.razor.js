// Plot-box observer of the opt-in Responsive TmChart (plan 98c80bc3, phase 10b).
//
// The chart draws into an SVG whose viewBox is, by default, a fixed 600 x 400 that the SVG then meets into
// its box — so a wide card is not filled and the type scales with the box. With Responsive the viewBox is
// the MEASURED plot box instead, which keeps one user unit equal to one CSS pixel. CSS cannot hand a pixel
// size to the markup, so this module reports it.
//
// Two invariants:
//
// 1. .NET is told only when the ROUNDED size changed. Sub-pixel oscillation (a scrollbar appearing, a
//    fractional flex size) must not re-render the chart every frame.
// 2. A degenerate box is never reported. While the viewport is re-emulated, a tab is hidden or the chart sits
//    in a display:none ancestor, Chromium delivers transient 0-size boxes; classifying one would collapse the
//    plot to its minimum and flash the chart. The last good size stays.
//
// All per-instance state lives in `registrations`, keyed by the element, so one module serves every chart on
// the page. `release()` disconnects the observer; the component calls it from DisposeAsync.

const registrations = new WeakMap();

/**
 * Watch the plot box of one chart and report it through `dotnet.invokeMethodAsync('OnPlotResized', w, h)`.
 * Observing the same element again replaces the previous registration.
 * @param {HTMLElement | null} element the plot box
 * @param {{ invokeMethodAsync: Function }} dotnet a DotNetObjectReference of the chart
 */
export function observe(element, dotnet) {
    if (!element || typeof ResizeObserver !== 'function') return;
    release(element);

    const state = { width: null, height: null, observer: null };
    state.observer = new ResizeObserver((records) => {
        for (const record of records) {
            // A record queued before release() must not reach a disposed chart.
            if (record.target !== element || registrations.get(element) !== state) continue;
            const box = record.contentRect;
            const width = Math.round(box?.width);
            const height = Math.round(box?.height);
            if (!Number.isFinite(width) || !Number.isFinite(height) || width < 1 || height < 1) continue;
            if (width === state.width && height === state.height) continue;
            state.width = width;
            state.height = height;
            // The circuit may already be gone; a rejected invoke must not surface as an unhandled rejection.
            dotnet.invokeMethodAsync('OnPlotResized', width, height).catch(() => {});
        }
    });
    registrations.set(element, state);
    state.observer.observe(element);
}

/**
 * Stop watching an element. Idempotent; an element that was never observed is a no-op.
 * @param {HTMLElement | null} element
 */
export function release(element) {
    if (!element) return;
    const state = registrations.get(element);
    if (!state) return;
    registrations.delete(element);
    state.observer?.disconnect();
}
