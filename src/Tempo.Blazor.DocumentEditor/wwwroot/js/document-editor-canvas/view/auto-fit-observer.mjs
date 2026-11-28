// Phase 11: the ResizeObserver behind the engine's automatic fit-width.
//
// It watches the real SCROLL VIEWPORT of the document column (never the content-sized mount: the mount grows with the page, so measuring it
// would feed back into the fit) and reports a ROUNDED width change once per animation frame, so a drag of the splitter or a window resize
// asks for one fit per frame instead of one per observer notification. A height-only change is not a change. Destroy disconnects the
// observer and cancels the pending frame. Without a scroll viewport or without ResizeObserver the observer is an inert no-op.
// `target` exposes the element that is observed, so the owner can notice that the scroll viewport was REPLACED (a host that re-parents the
// mount, a layout that swaps the scroller) and bind a new observer instead of watching a detached node.
export function createAutoFitObserver(options = {}) {
    const view = options.view || globalThis;
    const ResizeObserverCtor = options.ResizeObserver !== undefined
        ? options.ResizeObserver
        : (view.ResizeObserver || globalThis.ResizeObserver);
    const onResize = typeof options.onResize === 'function' ? options.onResize : () => {};
    const target = typeof options.resolveTarget === 'function' ? options.resolveTarget() : null;
    const noop = { target: null, destroy() {} };
    if (!target || typeof ResizeObserverCtor !== 'function') {
        return noop;
    }

    const request = (view.requestAnimationFrame || globalThis.requestAnimationFrame || (callback => setTimeout(callback, 16))).bind(view);
    const cancel = (view.cancelAnimationFrame || globalThis.cancelAnimationFrame || clearTimeout).bind(view);
    let lastWidth = null;
    let pendingWidth = null;
    let frame = 0;
    let destroyed = false;

    const observer = new ResizeObserverCtor(entries => {
        if (destroyed || !entries || entries.length === 0) {
            return;
        }

        const rect = entries[entries.length - 1]?.contentRect;
        const width = Math.round(Number(rect?.width ?? target.clientWidth) || 0);
        if (width === lastWidth) {
            return;
        }

        lastWidth = width;
        pendingWidth = width;
        if (frame) {
            return;
        }

        frame = request(() => {
            frame = 0;
            if (destroyed) {
                return;
            }

            const reported = pendingWidth;
            pendingWidth = null;
            onResize(reported);
        });
    });
    observer.observe(target);

    return {
        target,
        destroy() {
            if (destroyed) {
                return;
            }

            destroyed = true;
            if (frame) {
                cancel(frame);
                frame = 0;
            }

            observer.disconnect();
        },
    };
}
