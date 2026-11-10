// Tempo.Blazor.AI — the .NET-facing half of TmScheduledRuns' one-step move chord (ES module).
//
// The chord belongs to the run row: Alt+ArrowLeft/Right moves the run to the previous/next WINDOW and
// Alt+ArrowUp/Down to the previous/next DAY. Two things make it impossible to do purely in Blazor:
//
//   1. Alt+ArrowLeft / Alt+ArrowRight is ALSO the browser's Back/Forward history shortcut, and
//      `@onkeydown:preventDefault` is unconditional per element — it would swallow Tab as well.
//   2. The focus a reader actually has inside a run row is the row's MOVE MENU trigger, and its content
//      carries `@onkeydown:stopPropagation` (the fence every menu needs). Blazor therefore never dispatches
//      that key to the row, while a NATIVE listener on the surface root still sees it (browser events
//      bubble through the root before Blazor's document-level dispatch runs).
//
// So the module owns the conditional part and NOTHING else: it attaches the SHARED core helper
// `attachKeyboardMove` (from _content/Tempo.Blazor/js/pointer-drag.js — never a second keyboard
// implementation), which cancels exactly the Alt+arrow chord on the row that declares it owns the keys,
// calls `preventDefault` and never `stopPropagation`, and then reports the step here. This module resolves
// WHICH run the focused row shows and hands one `OnRunMoveStep(runId, key)` to .NET, where the same commit
// path as a drop (and its single live region) lives. No drag, no second gesture, no model in the browser.
//
// Without JavaScript every key still opens the move menu, which lists every window: the chord is the
// shortcut, never the only path.

/** The rows that own the chord. Each one also carries `data-run-id` and `aria-keyshortcuts`. */
export const ROW_SELECTOR = '[data-tm-run-move-keys]';

/** The four keys the chord owns. Every other key keeps its browser behaviour. */
export const MOVE_KEYS = Object.freeze(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);

const MOVE_KEY_SET = new Set(MOVE_KEYS);

/**
 * Which run one keyboard event moves, or null when the event is not the surface's chord. Pure, so the whole
 * decision is node-tested without a DOM: the four keys with Alt and no other modifier, on a target inside a
 * row that declares the chord.
 * @param {{altKey?: boolean, ctrlKey?: boolean, metaKey?: boolean, shiftKey?: boolean, key?: string,
 *          target?: any}} event the keydown event
 * @returns {{runId: string, key: string}|null} the run and the key, or null
 */
export function resolveMoveStep(event) {
    if (!event || typeof event.key !== 'string') return null;
    if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return null;
    if (!MOVE_KEY_SET.has(event.key)) return null;

    const target = event.target;
    if (!target || typeof target.closest !== 'function') return null;

    const row = target.closest(ROW_SELECTOR);
    const runId = row?.getAttribute?.('data-run-id');
    if (typeof runId !== 'string' || runId.length === 0) return null;

    return { runId, key: event.key };
}

let helper = null;

/**
 * Loads the shared keyboard helper. The URL is resolved against THIS module, so it works under any base path
 * a host serves the package from, and it is a dynamic import so a host without the core asset still gets the
 * menu (only the chord is missing).
 * @returns {Promise<{ attachKeyboardMove: Function }>} the helper's module namespace
 */
async function loadHelper() {
    helper ??= await import(new URL('../../Tempo.Blazor/js/pointer-drag.js', import.meta.url).href);
    return helper;
}

/**
 * Attaches the chord to one surface root. Idempotency is the caller's (one attach per rendered root), exactly
 * like the other package modules.
 * @param {Element|null} root the surface's own element
 * @param {{invokeMethodAsync: Function}|null} dotnet receives `OnRunMoveStep(runId, key)`
 * @returns {Promise<{detach: Function}|null>} the helper's handle, or null where nothing could be attached
 */
export async function attachMoveKeys(root, dotnet) {
    if (!root || typeof root.addEventListener !== 'function') return null;

    const { attachKeyboardMove } = await loadHelper();
    return attachKeyboardMove(root, {
        selector: ROW_SELECTOR,
        onStep: ({ event }) => {
            const step = resolveMoveStep(event);
            if (!step) return;
            dotnet?.invokeMethodAsync('OnRunMoveStep', step.runId, step.key)?.catch?.(() => {});
        },
    });
}
