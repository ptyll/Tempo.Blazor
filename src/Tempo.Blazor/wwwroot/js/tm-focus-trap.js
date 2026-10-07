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

    // The opener is whatever held focus when the trap opened. An explicit restore target and a named
    // id both outrank it; they are resolved at deactivation, because the target may not exist yet.
    const returnTarget = document.activeElement;

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

    traps.set(id, { element, tabHandler, escHandler, returnTarget, restoreTarget, modal });

    // A modal trap makes the rest of the page unreachable, not only invisible. `inert` is a DOM
    // attribute Blazor cannot keep in sync with the page around the overlay, so the module owns it.
    // The overlay's own ancestors stay reachable, otherwise the trap would inert itself. A non-modal
    // surface (an inline drawer) leaves the page usable.
    if (modal) {
        markBackgroundInert(element, id);
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

// id -> Set of elements THAT trap marked inert. A trap never clears an element another trap (or the
// host) marked, and it never marks an element that was already inert when it activated.
const inertedByTrap = new Map();

function isBackdrop(element) {
    // A backdrop rendered as a sibling of the trap root (TmDrawer's overlay) still belongs to the
    // overlay. Inerting it swallows the click that should close the sheet.
    return !!element && (
        element.hasAttribute?.('data-tm-backdrop')
        || element.dataset?.tmBackdrop !== undefined
        || element.classList?.contains('tm-drawer__overlay')
        || element.classList?.contains('tm-command-palette-backdrop')
    );
}

function markBackgroundInert(element, id) {
    if (!element || !document.body) return;
    const marked = new Set();
    // Walk every ancestor up to body. A Blazor host is body > #app > page, and the overlay is a
    // descendant of #app, so inerting only body's children leaves the page reachable.
    for (let node = element.parentElement; node; node = node.parentElement) {
        for (const child of node.children) {
            if (child === element || child.contains(element)) continue;
            if (isBackdrop(child)) continue;
            // Another open trap, or a host that set inert for its own reasons, owns this element.
            if (child.hasAttribute('inert')) continue;
            child.toggleAttribute('inert', true);
            marked.add(child);
        }
        if (node === document.body) break;
    }
    inertedByTrap.set(id, marked);
}

function releaseBackground(id) {
    const marked = inertedByTrap.get(id);
    inertedByTrap.delete(id);
    if (!marked) return;
    for (const child of marked) {
        let stillOwned = false;
        for (const others of inertedByTrap.values()) {
            if (others.has(child)) { stillOwned = true; break; }
        }
        if (!stillOwned) child.toggleAttribute('inert', false);
    }
}

/** Drops every trap without touching the DOM. The node tests call it between cases. */
export function __resetForTests() {
    traps.clear();
    inertedByTrap.clear();
    scrollLockCount = 0;
    scrollLockWasPresent = false;
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
    if (trap.modal) {
        releaseBackground(id);
        lockScroll(false);
        // The trap that remains is now topmost. Re-apply its inert set so a nested dialog that
        // marked the drawer's own content does not leave that content unreachable.
        const remaining = Array.from(traps.entries()).filter(([, other]) => other.modal);
        if (remaining.length > 0) {
            const [topId, top] = remaining[remaining.length - 1];
            releaseBackground(topId);
            markBackgroundInert(top.element, topId);
        }
    }

    restoreFocus(trap);
}

// A ref count, not a stack of snapshots. Two modal traps add the class once and the last one to
// close removes it, unless the host already had it.
let scrollLockCount = 0;
let scrollLockWasPresent = false;

function lockScroll(lock) {
    const root = document.documentElement;
    if (!root || !root.classList) return;
    if (lock) {
        if (scrollLockCount === 0) scrollLockWasPresent = root.classList.contains('tm-scroll-lock');
        scrollLockCount++;
        root.classList.add('tm-scroll-lock');
        return;
    }
    scrollLockCount = Math.max(0, scrollLockCount - 1);
    if (scrollLockCount === 0 && !scrollLockWasPresent) root.classList.remove('tm-scroll-lock');
}

function focusOf(target) {
    if (!target || target.isConnected === false || typeof target.focus !== 'function') return false;
    try { target.focus(); return true; } catch { return false; }
}

function restoreFocus(trap) {
    // Resolved at deactivation: an explicit false restores nothing; an explicit element wins; then
    // the id recorded on the root; then the opener captured at activation; then body.
    const dataset = trap.element && trap.element.dataset;
    if (dataset && dataset.restoreFocus === 'false') return;

    if (focusOf(trap.restoreTarget)) return;

    const id = dataset ? dataset.restoreTarget : null;
    const byId = id && document.getElementById ? document.getElementById(id) : null;
    if (focusOf(byId)) return;

    if (focusOf(trap.returnTarget)) return;
    focusOf(document.body);
}
