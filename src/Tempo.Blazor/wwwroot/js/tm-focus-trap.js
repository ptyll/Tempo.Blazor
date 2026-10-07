// Tempo.Blazor focus trap (ES module).
//
// Keeps Tab focus within a container (TmModal / TmDialog / TmDrawer), moves initial
// focus inside on activate, and restores focus to the previously-focused element on
// deactivate. Optionally installs a DOCUMENT-level Escape listener so Esc closes the
// overlay regardless of where focus currently sits (needed by TmDrawer, whose panel
// is not always the focused element).
//
// Everything is keyed by a caller-supplied id so nested/stacked traps do not clobber
// each other's return target or listeners. Imported lazily from OnAfterRenderAsync so
// it is safe under InteractiveAuto prerendering (no interop during prerender).

const FOCUSABLE = [
    'a[href]', 'button:not([disabled])', 'textarea:not([disabled])',
    'input:not([disabled]):not([type="hidden"])', 'select:not([disabled])',
    'audio[controls]', 'video[controls]', '[contenteditable]:not([contenteditable="false"])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

// id -> { element, tabHandler, escHandler, returnTarget }
const traps = new Map();

function visibleFocusable(element) {
    return Array.from(element.querySelectorAll(FOCUSABLE))
        .filter(el => el.offsetParent !== null && !el.hasAttribute('disabled'));
}

export function activate(element, id, escapeHandler, closeOnEscape, restoreTarget, modal = true) {
    if (!element) return;

    // Deactivate any stale trap reusing this id before re-registering.
    if (traps.has(id)) {
        deactivate(id);
    }

    // A caller that names a restore target (a canvas editor handing focus back to its canvas) wins
    // over the element that happened to be focused when the trap opened.
    const returnTarget = restoreTarget || document.activeElement;

    const tabHandler = function (e) {
        if (e.key !== 'Tab' || !modal) return;
        // Only the innermost active trap cycles Tab. An outer one that also handled it would pull
        // focus out of the dialog the user is actually in.
        if (!isInnermost(id)) return;
        const list = visibleFocusable(element);
        if (list.length === 0) { e.preventDefault(); element.focus(); return; }
        const first = list[0];
        const last = list[list.length - 1];
        const active = document.activeElement;
        if (e.shiftKey && (active === first || !element.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && (active === last || !element.contains(active))) {
            e.preventDefault();
            first.focus();
        }
    };
    element.addEventListener('keydown', tabHandler);

    let escHandler = null;
    if (closeOnEscape && escapeHandler) {
        escHandler = function (e) {
            // overlay.js consumes an Escape that actually closes a floating panel via
            // preventDefault (plus stopImmediatePropagation at window capture). Honour the flag so
            // one gesture still means one layer if ordering ever lets this listener run anyway.
            if (e.key !== 'Escape' || e.defaultPrevented) return;
            // Only the topmost trap closes. A sheet behind a dialog must not close on the same key.
            if (!isTopmost(id)) return;
            escapeHandler.invokeMethodAsync('HandleFocusTrapEscapeAsync');
        };
        document.addEventListener('keydown', escHandler);
    }

    traps.set(id, { element, tabHandler, escHandler, returnTarget, modal });

    // A modal trap makes the rest of the page unreachable, not only invisible. `inert` is a DOM
    // attribute Blazor cannot keep in sync with the page around the overlay, so the module owns it.
    // The overlay's own ancestors stay reachable, otherwise the trap would inert itself. A non-modal
    // surface (an inline drawer) leaves the page usable.
    if (modal) {
        markBackgroundInert(element, true);
        lockScroll(true);
    }

    // ARIA wants initial focus INSIDE the overlay, but only when it is not already there.
    // A non-modal surface leaves focus where it is: the page behind it stays usable.
    // Activation is gated on a lazy ES-module import (FocusTrap.ActivateAsync), so it can land an
    // arbitrary amount of time after the overlay rendered — long enough for the user to have clicked
    // or typed into a field inside it. An unconditional `(list[0] || element).focus()` then STEALS
    // that focus and yanks the caret to the first focusable (typically the close button).
    //
    // `document.activeElement` is never null in practice but is `<body>` when focus is "nowhere",
    // and `<body>` is not contained by the overlay, so that case correctly still moves focus in.
    // The container itself is deliberately NOT treated as "inside": it only ever holds focus via the
    // tabindex="-1" fallback, and from there focus still belongs on the first real control.
    const active = document.activeElement;
    const alreadyInside = !!active && active !== element && element.contains(active);
    if (modal && !alreadyInside) {
        const list = visibleFocusable(element);
        const initialId = element.dataset ? element.dataset.initialFocus : null;
        const initial = initialId && document.getElementById ? document.getElementById(initialId) : null;
        (initial || list[0] || element).focus();
    }
}

/** True when no other trap is nested inside this one's element. */
export function isInnermost(id) {
    const trap = traps.get(id);
    if (!trap) return false;
    for (const [otherId, other] of traps) {
        if (otherId !== id && trap.element.contains(other.element)) return false;
    }
    return true;
}

/** True when this trap was activated last. Escape closes the most recently opened layer. */
export function isTopmost(id) {
    const ids = Array.from(traps.keys());
    return ids.length > 0 && ids[ids.length - 1] === id;
}

// The elements the module itself marked inert, so deactivation clears exactly those and never an
// `inert` a host set for its own reasons.
const inerted = new Set();

function markBackgroundInert(element, inert) {
    if (!element || !document.body) return;
    // Walk every ancestor up to body. A Blazor host is body > #app > page, and the overlay is a
    // descendant of #app, so inerting only body's children leaves the page reachable.
    for (let node = element.parentElement; node; node = node.parentElement) {
        for (const child of node.children) {
            if (child === element || child.contains(element)) continue;
            setInert(child, inert);
        }
        if (node === document.body) break;
    }
}

function setInert(child, inert) {
    if (inert) {
        if (!child.hasAttribute('inert')) {
            child.toggleAttribute('inert', true);
            inerted.add(child);
        }
    } else if (inerted.has(child)) {
        child.toggleAttribute('inert', false);
        inerted.delete(child);
    }
}

/** Drops every trap without touching the DOM. The node tests call it between cases. */
export function __resetForTests() {
    traps.clear();
    inerted.clear();
    scrollLocks.length = 0;
}

export function deactivate(id) {
    const trap = traps.get(id);
    if (!trap) return;
    traps.delete(id);

    if (trap.element && trap.tabHandler) {
        trap.element.removeEventListener('keydown', trap.tabHandler);
    }
    if (trap.escHandler) {
        document.removeEventListener('keydown', trap.escHandler);
    }
    // Only the last modal trap releases the background. A nested dialog closing must not make the
    // page reachable again while the sheet that opened it is still up.
    const stillModal = Array.from(traps.values()).some(other => other.modal);
    if (trap.modal && !stillModal) {
        markBackgroundInert(trap.element, false);
        lockScroll(false);
    }

    restoreFocus(trap);
}

const scrollLocks = [];

function lockScroll(lock) {
    const body = document.body;
    if (!body || !body.style) return;
    if (lock) {
        scrollLocks.push(body.style.overflow ?? '');
        body.style.overflow = 'hidden';
        return;
    }
    body.style.overflow = scrollLocks.pop() ?? '';
}

function restoreFocus(trap) {
    const named = trap.returnTarget;
    if (named && named.isConnected !== false && typeof named.focus === 'function') {
        try { named.focus(); return; } catch { /* disconnected or unfocusable */ }
    }
    const id = trap.element && trap.element.dataset ? trap.element.dataset.restoreTarget : null;
    const byId = id && document.getElementById ? document.getElementById(id) : null;
    const fallback = byId || document.body;
    if (fallback && typeof fallback.focus === 'function') {
        try { fallback.focus(); } catch { /* nothing to restore to */ }
    }
}
