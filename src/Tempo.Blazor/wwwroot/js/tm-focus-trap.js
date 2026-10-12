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

// The element of an inert-exempt surface (a toast card) that last took focus, or null. Two
// capture-phase document listeners keep it current: a focusin records focus entering an exempt
// surface, and a pointerdown OUTSIDE the exempt surfaces drops the record (a click on a
// non-focusable area sends focus to <body> without a focusin the record would see). The toast
// containers are EXEMPT from the trap's inert background, so their buttons take focus while a
// sheet/dialog is open — and a dismissed or expired toast takes the focused button out of the
// DOM. Screen-reader browse mode and mouse clicks leave focus wherever it was, so
// restoreTrapFocusIfLost moves focus on a timed removal ONLY while this element is recorded AND
// gone (WCAG 3.2.1 / APG: no focus moves on timed events). Installed lazily on the first
// activate; reset with the traps in the tests.
let exemptFocus = null;
let exemptFocusListenerInstalled = false;

function installExemptFocusListener() {
    if (exemptFocusListenerInstalled) return;
    if (typeof document === 'undefined' || typeof document.addEventListener !== 'function') return;
    exemptFocusListenerInstalled = true;
    document.addEventListener('focusin', (e) => {
        const target = e.target;
        exemptFocus = !!target && typeof target.closest === 'function'
            && target.closest('[data-tm-inert-exempt]') ? target : null;
    }, true);
    // A pointerdown always precedes the click that can lose focus silently: a press outside the
    // exempt surfaces means the user — not a removed toast — decided where focus went, so the
    // record is dropped. A press ON an exempt surface keeps it (the gesture may genuinely move
    // focus into the toast).
    document.addEventListener('pointerdown', (e) => { const t = e.target; if (!(t && typeof t.closest === 'function' && t.closest('[data-tm-inert-exempt]'))) exemptFocus = null; }, true);
}

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

/** Tab stops only: a focusable with a negative tabindex (a roving menu/toolbar item) is not one.
 * Cycling over -1 items made the trap's "last" a non-tab-stop, so Tab on the real last tab stop
 * fell through to the browser, which skipped the -1 items and dropped focus to <body> (the page
 * is inert) — out of the sheet (F6 follow-up f4c27b20 #1). An UNPARSEABLE tabindex value falls
 * back to a tab stop (HTML: invalid → 0), so the value is parsed as an integer, not coerced. */
function tabbable(element) {
    return visibleFocusable(element).filter(el => {
        const ti = typeof el.getAttribute === 'function' ? el.getAttribute('tabindex') : null;
        if (ti === null || ti === undefined) return true;
        const n = Number.parseInt(ti, 10);
        return Number.isNaN(n) || n >= 0;
    });
}

/** Within a same-name radio group that HAS a checked member, the unchecked radios are not Tab
 * stops: the group is one stop (the checked radio) and the arrows move within it (APG). A group
 * with no checked member keeps every radio (nothing is preselected), and unnamed radios are
 * independent stops. Checkedness is read LIVE: the checked attribute only records the input's
 * initial state, so after the user picks another radio the attribute on the first input lies —
 * the el.checked property decides; the attribute is only the fallback for a stub without it.
 * Only the modal Tab cycle applies this — arrow navigation stays native. */
function radioGroupTabStops(list) {
    const checkedByGroup = new Map();
    for (const el of list) {
        if (el.getAttribute?.('type') !== 'radio') continue;
        const name = el.getAttribute?.('name');
        if (!name || checkedByGroup.has(name)) continue;
        const isChecked = typeof el.checked === 'boolean' ? el.checked : el.hasAttribute?.('checked');
        if (isChecked) checkedByGroup.set(name, el);
    }
    if (checkedByGroup.size === 0) return list;
    return list.filter(el => {
        if (el.getAttribute?.('type') !== 'radio') return true;
        const name = el.getAttribute?.('name');
        if (!name) return true;
        const groupChecked = checkedByGroup.get(name);
        return !groupChecked || el === groupChecked;
    });
}

export function activate(element, id, escapeHandler, closeOnEscape, restoreTarget, modal = true, initialTarget = null) {
    if (!element) return;

    // The exempt-focus record must exist before any surface can ask for a recovery.
    installExemptFocusListener();

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
        const list = radioGroupTabStops(tabbable(element));
        if (list.length === 0) { e.preventDefault(); element.focus(); return; }
        const first = list[0];
        const last = list[list.length - 1];
        const active = document.activeElement;
        // Focus can sit on a -1 item the cycle list excludes (a roving item focused by the arrows
        // before the menu re-roved its tab stop). The browser's native Tab would skip the -1 items
        // and, the page being inert, drop focus to <body> — so walk the next/previous TAB STOP in
        // DOM order instead (F6 follow-up f4c27b20 #1).
        if (!list.includes(active) && element.contains(active) && active !== element
            && typeof active.compareDocumentPosition === 'function') {
            e.preventDefault();
            const following = list.filter(el => active.compareDocumentPosition(el) & 4);
            const preceding = list.filter(el => active.compareDocumentPosition(el) & 2);
            (e.shiftKey ? (preceding[preceding.length - 1] || last) : (following[0] || first)).focus();
            return;
        }
        if (e.shiftKey && (active === first || !element.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && (active === last || !element.contains(active))) {
            e.preventDefault();
            first.focus();
        }
    };
    element.addEventListener('keydown', tabHandler);

    // The element last focused INSIDE this trap. A surface outside the trap (an inert-exempt toast
    // container) can take focus and then remove it (a dismissed toast), stranding focus on <body>;
    // restoreTrapFocusIfLost hands focus back here (F6 follow-up c51ddf81 #4). The handler writes
    // through to the traps entry, so restoreTrapFocusIfLost always reads the live value.
    const focusInHandler = function (e) {
        const trap = traps.get(id);
        if (trap && e.target !== element) trap.lastFocused = e.target;
    };
    element.addEventListener('focusin', focusInHandler);

    let escHandler = null;
    if (closeOnEscape && escapeHandler) {
        escHandler = function (e) {
            // overlay.js consumes an Escape that actually closes a floating panel via
            // preventDefault (plus stopImmediatePropagation at window capture). Honour the flag so
            // one gesture still means one layer if ordering ever lets this listener run anyway.
            if (e.key !== 'Escape' || e.defaultPrevented) return;
            if (modal) {
                // The modal owns Escape unless a LATER modal registered, or a later non-modal trap
                // currently holds focus. isTopmost counted every trap, so a non-modal inline sheet
                // activated after the dialog blocked the dialog's Escape entirely (F6 follow-up
                // 08a22db5 #2). A dialog over a dialog still closes only the top one.
                if (!modalOwnsEscape(id)) return;
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

    traps.set(id, { element, tabHandler, focusInHandler, lastFocused: null, escHandler, returnTarget, restoreTarget, modal });

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

/** Registration-order query (tests/diagnostics): whether this trap was activated last. Modal
 * Escape ownership is decided by modalOwnsEscape — this no longer gates any key. */
export function isTopmost(id) {
    const ids = Array.from(traps.keys());
    return ids.length > 0 && ids[ids.length - 1] === id;
}

/**
 * Whether a MODAL trap owns Escape: no modal trap registered after it, and no later NON-modal trap
 * that CAN close (it has an escHandler) currently holds focus (an inline sheet nested in the dialog
 * closes first, one layer per key). A later non-modal trap that does not hold focus, or that holds
 * focus but registered WITHOUT an Escape handler (it could never close, so it must not deadlock the
 * dialog — review round), is skipped (F6 follow-up 08a22db5 #2) — registration order alone must not
 * deadlock the dialog's Escape.
 */
export function modalOwnsEscape(id) {
    const ids = Array.from(traps.keys());
    const at = ids.indexOf(id);
    if (at < 0) return false;
    for (let i = at + 1; i < ids.length; i++) {
        const other = traps.get(ids[i]);
        if (other.modal) return false;
        if (other.escHandler && ownsEscape(ids[i], other.element)) return false;
    }
    return true;
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
    exemptFocus = null;
    exemptFocusListenerInstalled = false;
}

export function deactivate(id) {
    const trap = traps.get(id);
    if (!trap) return;
    traps.delete(id);

    if (trap.element && trap.tabHandler) {
        trap.element.removeEventListener('keydown', trap.tabHandler);
    }
    if (trap.element && trap.focusInHandler) {
        trap.element.removeEventListener('focusin', trap.focusInHandler);
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
/**
 * Whether focus currently sits inside a surface a host is about to remove or hide. The host asks
 * BEFORE the render that unmounts it (the DOM is still the old one), then re-asserts focus on the
 * replacing control afterwards through focusIfLost.
 * @param {HTMLElement|string|null} container the surface (element or id)
 * @returns {boolean}
 */
export function focusWithin(container) {
    const region = typeof container === 'string'
        ? (document.getElementById ? document.getElementById(container) : null)
        : container;
    const active = document.activeElement;
    return !!region && !!active && typeof region.contains === 'function' && region.contains(active);
}

/**
 * Recovers focus for the topmost MODAL trap after a surface outside it (a toast) removed the
 * focused element. Acts ONLY when an inert-exempt surface actually held focus — the capture
 * listener recorded its element and that element has since left the DOM. A toast removed while
 * nothing inside it was focused (screen-reader browse mode, a mouse click elsewhere, plain Tab
 * order) must not move focus on its timer: moving focus on a timed event violates WCAG 3.2.1 /
 * APG. Within that gate, focus goes to the element last focused inside the trap, else to the
 * first TAB STOP that accepts focus (then any focusable — a roving -1 item is the last resort),
 * else the trap element itself. A focus the user placed on another live element is never stolen,
 * and a page with no modal trap keeps its lost focus — there is no reliable return target.
 * @returns {boolean} whether focus moved
 */
export function restoreTrapFocusIfLost() {
    // No recorded exempt focus, or the recorded card is still connected: nothing was lost to a
    // removed toast, so the recovery is a no-op (a timer expiry must not move focus).
    if (!exemptFocus || exemptFocus.isConnected) return false;
    exemptFocus = null;
    const active = document.activeElement;
    if (active && active !== document.body && active.isConnected !== false) return false;
    const modal = Array.from(traps.values()).filter(t => t.modal);
    if (modal.length === 0) return false;
    const top = modal[modal.length - 1];
    if (top.lastFocused && top.lastFocused.isConnected !== false
        && top.element.contains(top.lastFocused) && focusOf(top.lastFocused)) return true;
    // Tab stops first; a focusable the Tab cycle excludes (a roving -1 item) only takes over
    // when no tab stop accepts focus. The Set skips the tab stops re-tried in the fallback.
    const tried = new Set();
    for (const el of [...tabbable(top.element), ...visibleFocusable(top.element)]) {
        if (tried.has(el)) continue;
        tried.add(el);
        if (focusOf(el)) return true;
    }
    return focusOf(top.element);
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
 * @param {string|null} label the accessible name used when there is no title id (an inline sheet
 *        named only by its aria-label); a title id wins
 * @returns {() => void} stop
 */
export function syncScrollRegion(element, id, labelledBy = null, label = null) {
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
            } else if (label) {
                element.setAttribute('role', 'region');
                element.setAttribute('aria-label', label);
            }
        } else {
            element.removeAttribute('tabindex');
            element.removeAttribute('role');
            element.removeAttribute('aria-labelledby');
            element.removeAttribute('aria-label');
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
        region.element.removeAttribute('aria-label');
    }
}
