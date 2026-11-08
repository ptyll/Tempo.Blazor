// Tempo.Blazor toast container behaviors (ES module).
//
// Browser-owned behaviors of TmToastContainer that cannot live in Blazor event handlers:
//
// 1. ESCAPE — a keydown listener on the container dismisses the toast card holding focus. It
//    sets `e.tmEscapeHandled = true` (the shared flag capture-phase consumers like the diagram
//    editor consult before acting on Escape), then preventDefault + stopPropagation, and reports
//    the card id to Blazor (the ToastService owns the state — JS only asks).
//
// 2. FOCUS RETURN — focusin remembers the element focus came FROM (relatedTarget). When a card
//    leaves the DOM (dismiss, expire, Undo), focus that was inside it falls back to <body>; the
//    observer returns it to the remembered element while that element is still connected.
//
// 3. Alt+T HOTKEY — a document-level keydown focuses the NEWEST action button (e.g. Undo) on any
//    visible toast, so the action is reachable without tabbing through the page. Documented on
//    the action button through `aria-keyshortcuts="Alt+T"`. Skipped while typing in a field.
//
// attach() is idempotent per container; detach() removes every listener and the observer.

const instances = new Map(); // container -> state

function isEditable(target) {
    return !!target
        && typeof target.closest === 'function'
        && !!target.closest('input, textarea, select, [contenteditable="true"]');
}

/**
 * Wires the container behaviors. Repeated attach() calls on the same container replace the
 * previous wiring.
 * @param {HTMLElement} container the .tm-toast-container root
 * @param {object} dotNet the DotNetObjectReference exposing DismissToast(string id)
 * @param {Document} [doc] owner document (injectable for tests)
 * @param {Function} [ObserverImpl] MutationObserver implementation (injectable for tests)
 */
export function attach(container, dotNet, doc = container.ownerDocument ?? document,
    ObserverImpl = globalThis.MutationObserver) {
    detach(container, doc);

    const state = { relatedTarget: null };

    state.onKeyDown = (e) => {
        if (e.key !== 'Escape')
            return;
        const card = e.target?.closest?.('.tm-toast');
        if (!card || !container.contains(card))
            return;
        e.preventDefault();
        e.stopPropagation();
        e.tmEscapeHandled = true;
        try {
            Promise.resolve(dotNet.invokeMethodAsync('DismissToast', card.dataset.toastId))
                .catch(() => { });
        } catch {
            // disposed reference: the toast is going away anyway
        }
    };

    state.onFocusIn = (e) => {
        // The element focus came FROM: the place to return to when a card takes the focus away
        // with it on removal.
        state.relatedTarget = e.relatedTarget ?? null;
    };

    state.onDocKeyDown = (e) => {
        if (!e.altKey || (e.key !== 't' && e.key !== 'T') || isEditable(e.target))
            return;
        const actions = container.querySelectorAll('.tm-toast-action');
        if (!actions.length)
            return;
        e.preventDefault();
        actions[actions.length - 1].focus();
    };

    container.addEventListener('keydown', state.onKeyDown);
    container.addEventListener('focusin', state.onFocusIn);
    doc.addEventListener('keydown', state.onDocKeyDown);

    if (typeof ObserverImpl === 'function') {
        state.observer = new ObserverImpl((mutations) => {
            const removed = mutations.some(m => Array.from(m.removedNodes ?? [])
                .some(n => n.nodeType === 1
                    && (n.classList?.contains('tm-toast') || typeof n.querySelector?.('.tm-toast') === 'object')));
            if (!removed)
                return;
            const active = doc.activeElement;
            if (active && active !== doc.body && container.contains(active))
                return; // focus already landed somewhere sensible inside the container
            const target = state.relatedTarget;
            state.relatedTarget = null;
            if (target && target.isConnected) {
                try { target.focus(); } catch { /* detached mid-flight */ }
            }
        });
        state.observer.observe(container, { childList: true });
    }

    instances.set(container, state);
}

/**
 * Removes every listener and the observer. No-op when the container is not attached.
 * @param {HTMLElement} container the container previously passed to attach()
 * @param {Document} [doc] owner document (injectable for tests)
 */
export function detach(container, doc = container.ownerDocument ?? document) {
    const state = instances.get(container);
    if (!state)
        return;
    container.removeEventListener('keydown', state.onKeyDown);
    container.removeEventListener('focusin', state.onFocusIn);
    doc.removeEventListener('keydown', state.onDocKeyDown);
    state.observer?.disconnect();
    instances.delete(container);
}
