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

// CSS.escape is missing in the stub DOM the node tests run against. Attribute values only need
// the quote and the backslash escaped to stay inside the selector.
function escapeAttr(value) {
    if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') return CSS.escape(value);
    return String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function visibleFocusable(element) {
    return Array.from(element.querySelectorAll(FOCUSABLE))
        .filter(el => el.offsetParent !== null && !el.hasAttribute('disabled'));
}

export function activate(element, id, escapeHandler, closeOnEscape, restoreTarget, modal = true, initialTarget = null) {
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
            if (modal) {
                // Only the topmost modal closes. A dialog over a dialog must not close both.
                if (!isTopmost(id)) return;
            }
            else if (!ownsEscape(id, element)) {
                // A non-modal surface (an inline sheet) closes by FOCUS OWNERSHIP, not registration
                // order: a page can hold several inline sheets, and the one that holds focus is not
                // necessarily the last one that activated (F6 r2 G3).
                return;
            }
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
        // A dialog declared in page content opens inside an ancestor an outer trap already inerted.
        // That ancestor has to become reachable again, or the dialog itself is inert.
        releaseAncestors(element);
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
        const focusInitial = () => {
            const list = visibleFocusable(element);
            const initialId = element.dataset ? element.dataset.initialFocus : null;
            const byId = initialId && document.getElementById ? document.getElementById(initialId) : null;
            // A component that renders only a data-tm-id marker (no real id) is still resolvable.
            const byData = !byId && initialId && document.querySelector
                ? document.querySelector(`[data-tm-id="${escapeAttr(initialId)}"]`)
                : null;
            // A selector (data-initial-focus-selector) scopes the search to THIS trap: a menu/listbox
            // sheet wants the first menuitem/option, not the first focusable (the header Done). It
            // runs only when no explicit target or id claimed the choice, and only on traps whose
            // environment implements querySelector (the node-test stubs do not). An invalid consumer
            // selector must not throw AFTER the inert/scroll lock landed — it degrades to the first
            // focusable exactly like a selector that matches nothing.
            const selector = element.dataset ? element.dataset.initialFocusSelector : null;
            let bySelector = null;
            if (!initialTarget && !byId && !byData && selector && typeof element.querySelector === 'function') {
                try {
                    bySelector = element.querySelector(selector);
                }
                catch {
                    bySelector = null;
                }
            }
            // An element reference wins over the id: the id is the fallback for a target that is not an
            // element reference yet.
            (initialTarget || byId || byData || bySelector || list[0] || element).focus();
        };
        focusInitial();

        // UX review round 2 (m1): the move above can no-op — overlay.js parks a panel
        // visibility:hidden while its anchor is still mid-scroll (a smooth scroll-behaviour), and
        // a focus() on a hidden element is dropped. The trap would then hold focus nowhere while
        // the page is inert. Retry the same resolution on the next two animation frames: the
        // panel's first visible place() has run by then and the move lands. Stops early the
        // moment focus is inside, after deactivation, and never starts where rAF does not exist
        // (the node-test stubs).
        const landed = () => {
            const current = document.activeElement;
            return !!current && (current === element || element.contains(current));
        };
        if (typeof requestAnimationFrame === 'function' && !landed()) {
            const retry = (attemptsLeft) => {
                if (!traps.has(id)) return;
                if (landed()) return;
                focusInitial();
                if (attemptsLeft > 0) {
                    requestAnimationFrame(() => retry(attemptsLeft - 1));
                }
            };
            requestAnimationFrame(() => retry(1));
        }
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

/**
 * Whether a NON-MODAL trap owns the Escape key: focus is inside it, no modal trap registered after it
 * is active (a dialog over the editor owns Escape), and no nested trap that holds focus is more
 * specific (one Escape closes one layer — the innermost surface that contains focus).
 */
function ownsEscape(id, element) {
    const active = document.activeElement;
    if (!active || !element.contains(active)) return false;
    let seen = false;
    for (const [otherId, other] of traps) {
        if (otherId === id) { seen = true; continue; }
        if (seen && other.modal) return false;
        if (other.element !== element && element.contains(other.element) && other.element.contains(active)) return false;
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
    // data-tm-backdrop is the only contract. A class name is not recognised: a backdrop the walk
    // would otherwise inert swallows the click that should close the overlay.
    return !!element && element.hasAttribute?.('data-tm-backdrop');
}

function isInertExempt(element) {
    // Toast containers are non-modal status UI pinned to the top layer: the walk must not inert
    // them, or a click on a toast's dismiss button passes THROUGH to the backdrop and closes the
    // sheet/modal underneath (data loss), and role=alert toasts inside an inert subtree are never
    // announced (V3, F3 review round 3).
    return !!element && element.hasAttribute?.('data-tm-inert-exempt');
}

function releaseAncestors(element) {
    for (let node = element.parentElement; node; node = node.parentElement) {
        for (const marked of inertedByTrap.values()) {
            if (!marked.has(node)) continue;
            marked.delete(node);
            node.toggleAttribute('inert', false);
        }
        if (node === document.body) break;
    }
}

function markBackgroundInert(element, id) {
    if (!element || !document.body) return;
    const marked = new Set();
    // Walk every ancestor up to body. A Blazor host is body > #app > page, and the overlay is a
    // descendant of #app, so inerting only body's children leaves the page reachable.
    for (let node = element.parentElement; node; node = node.parentElement) {
        for (const child of node.children) {
            if (child === element || child.contains(element)) continue;
            if (isBackdrop(child) || isInertExempt(child)) continue;
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

/**
 * Moves focus to a host's own trigger after ITS surface closed — but only when focus is actually
 * lost: on the body, on an element that left the DOM, or inside the closing container. A focus the
 * user (or the host) placed on another live element is never stolen. The host surfaces that re-assert
 * a toggle's focus after their own re-render (TmEditorShell) use this instead of an unconditional
 * element.focus().
 * @param {HTMLElement|string|null} target the element, or its id
 * @param {HTMLElement|string|null} container the closing surface (element or id); focus inside it counts as lost
 * @returns {boolean} true when focus was moved
 */
export function focusIfLost(target, container = null) {
    const element = typeof target === 'string'
        ? (document.getElementById ? document.getElementById(target) : null)
        : target;
    if (!element || element.isConnected === false || typeof element.focus !== 'function') return false;

    const region = typeof container === 'string'
        ? (document.getElementById ? document.getElementById(container) : null)
        : container;
    const active = document.activeElement;
    const lost = !active
        || active === document.body
        || active.isConnected === false
        || (!!region && typeof region.contains === 'function' && region.contains(active));
    if (!lost) return false;
    return focusOf(element);
}
function restoreFocus(trap) {
    // A non-modal surface restores focus only if it held it. Restoring while the user is on the
    // canvas behind an inline sheet would steal that focus.
    // Skip only when focus is already on a connected element outside the sheet. A removed sheet
    // leaves activeElement on body, and that must still restore to the opener.
    const active = document.activeElement;
    if (trap.modal === false && active && active !== document.body && active.isConnected && trap.element && !trap.element.contains(active)) return;
    // Resolved at deactivation: an explicit false restores nothing; an explicit element wins; then
    // the id recorded on the root; then the opener captured at activation; then body.
    const dataset = trap.element && trap.element.dataset;
    if (dataset && dataset.restoreFocus === 'false') return;

    if (focusOf(trap.restoreTarget)) return;

    const id = dataset ? dataset.restoreTarget : null;
    const byId = id && document.getElementById ? document.getElementById(id) : null;
    const byData = !byId && id && document.querySelector
        ? document.querySelector(`[data-tm-id="${escapeAttr(id)}"]`)
        : null;
    if (focusOf(byId) || focusOf(byData)) return;

    if (focusOf(trap.returnTarget)) return;
    focusOf(document.body);
}

// id -> { element, observer }
const scrollRegions = new Map();

/**
 * Keeps an overlay's scroller a keyboard-reachable region ONLY while it actually overflows.
 * A hardcoded tabindex="0" made the content block the first tabbable element of every dialog, so
 * the trap focused the title block instead of a button or an input, and short dialogs grew an
 * extra tab stop. The attributes are owned here, not in the markup, because only a measurement
 * can decide them. The region never takes initial focus by existing — activate() picks the
 * target; keyboard users reach the region with Tab.
 * @param {HTMLElement} element the scroller
 * @param {string} id the caller's key, so a re-attach replaces the observer
 * @param {string|null} labelledBy the id of the element that names the region, when it has a title
 * @returns {() => void} stop
 */
export function syncScrollRegion(element, id, labelledBy = null) {
    stopScrollRegion(id);
    if (!element) return () => {};

    const apply = () => {
        // A 1px tolerance: sub-pixel rounding must not mint a tab stop.
        const overflows = element.scrollHeight > element.clientHeight + 1;
        if (overflows) {
            element.setAttribute('tabindex', '0');
            if (labelledBy) {
                element.setAttribute('role', 'region');
                element.setAttribute('aria-labelledby', labelledBy);
            }
        } else {
            element.removeAttribute('tabindex');
            element.removeAttribute('role');
            element.removeAttribute('aria-labelledby');
        }
    };

    apply();
    let observer = null;
    if (typeof ResizeObserver === 'function') {
        observer = new ResizeObserver(apply);
        observer.observe(element);
    }
    scrollRegions.set(id, { element, observer });
    return () => stopScrollRegion(id);
}

/** Removes the scroll-region observer and the attributes it wrote. Safe to call twice. */
export function stopScrollRegion(id) {
    const region = scrollRegions.get(id);
    if (!region) return;
    scrollRegions.delete(id);
    if (region.observer) region.observer.disconnect();
    if (region.element) {
        region.element.removeAttribute('tabindex');
        region.element.removeAttribute('role');
        region.element.removeAttribute('aria-labelledby');
    }
}
