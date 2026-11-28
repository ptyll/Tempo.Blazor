// Phase 11 (fix round): the trailing debounce between the engine's own view changes (the automatic fit during a resize) and the .NET callback.
//
// A drag of the splitter or a window resize makes the engine re-fit the page on every frame; the host only needs the zoom the page SETTLED on
// (the label, the page-fit state), so one notification per ~150 ms pause is enough and only when what the host shows has really changed.
// `read` returns the current zoom; `key` reduces it to what the host renders (percent / preset / autoFit); `notify` receives the final zoom.
export const VIEW_CHANGE_DEBOUNCE_MS = 150;

export function viewChangeKey(zoom) {
    return `${Number(zoom?.percent) || 0}|${zoom?.preset || ''}|${zoom?.autoFit || ''}`;
}

export function createViewChangeNotifier(options = {}) {
    const delay = Math.max(0, Number(options.delayMs ?? VIEW_CHANGE_DEBOUNCE_MS) || 0);
    const schedule = typeof options.setTimeout === 'function' ? options.setTimeout : globalThis.setTimeout.bind(globalThis);
    const cancel = typeof options.clearTimeout === 'function' ? options.clearTimeout : globalThis.clearTimeout.bind(globalThis);
    const read = typeof options.read === 'function' ? options.read : () => null;
    const notify = typeof options.notify === 'function' ? options.notify : () => {};
    let lastKey = options.initial === undefined ? null : viewChangeKey(options.initial);
    let timer = null;
    let destroyed = false;

    function fire() {
        timer = null;
        if (destroyed) {
            return;
        }

        const zoom = read();
        const key = viewChangeKey(zoom);
        if (key === lastKey) {
            return;
        }

        lastKey = key;
        notify(zoom);
    }

    return {
        // Called for every view change of the engine; restarts the trailing timer.
        request() {
            if (destroyed) {
                return;
            }

            if (timer !== null) {
                cancel(timer);
            }

            timer = schedule(fire, delay);
        },
        // The host already learned this state through another path (a selection sync, the mount snapshot): do not repeat it.
        markNotified(zoom) {
            lastKey = viewChangeKey(zoom);
        },
        destroy() {
            destroyed = true;
            if (timer !== null) {
                cancel(timer);
                timer = null;
            }
        },
    };
}
