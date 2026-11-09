// Tempo.Blazor toast visibility watcher (ES module).
//
// TmToastContainer owns the auto-dismiss timers in .NET (TimeProvider), but only the browser
// knows when the document hides. While the tab is hidden the CSS progress animation is
// throttled/paused by the UA — without this bridge the .NET timer would dismiss the toast long
// before its frozen progress bar reaches zero. The module watches document.visibilitychange and
// reports every flip to Blazor, which pauses/resumes the timers with their remaining time.
//
// State lives PER WATCH INSTANCE in a Map keyed by the DotNetObjectReference (the handle): a
// re-watch of the same reference replaces the previous listener (the `_watching` flag), multiple
// containers watch independently, and unwatch(handle) detaches exactly one instance.
//
// watch() RETURNS the document's current hidden flag and, when the document is ALREADY hidden at
// watch time, reports it immediately — no visibilitychange event fires for a tab that starts
// hidden, and the timers must pause right away, not after the first flip.

const instances = new Map(); // dotNet (handle) -> { doc, handler, watching }

/**
 * Starts watching the document visibility for one caller.
 * @param {object} dotNet the DotNetObjectReference exposing OnDocumentVisibilityChanged(bool) —
 *   also the instance handle for unwatch()
 * @param {Document} [doc] the document to watch (injectable for tests)
 * @returns {boolean} the document's CURRENT hidden flag
 */
export function watch(dotNet, doc = document) {
    unwatch(dotNet);
    const state = { doc, dotNet, watching: false, handler: null };
    state.handler = () => report(state);
    doc.addEventListener('visibilitychange', state.handler);
    state.watching = true;
    instances.set(dotNet, state);
    const hidden = doc.hidden;
    if (hidden)
        report(state); // no flip event will come: PauseAll must happen NOW
    return hidden;
}

/**
 * Stops watching one instance. No-op for an unknown handle.
 * @param {object} dotNet the handle previously passed to watch()
 */
export function unwatch(dotNet) {
    const state = instances.get(dotNet);
    if (!state)
        return;
    state.watching = false;
    state.doc.removeEventListener('visibilitychange', state.handler);
    instances.delete(dotNet);
}

function report(state) {
    if (!state.watching)
        return;
    try {
        // .catch(() => {}): the circuit may be torn down — a lost report must not surface as an
        // unhandled rejection.
        Promise.resolve(state.dotNet.invokeMethodAsync('OnDocumentVisibilityChanged', state.doc.hidden))
            .catch(() => { });
    } catch {
        // invokeMethodAsync itself threw synchronously (disposed reference): nothing to report to.
    }
}
