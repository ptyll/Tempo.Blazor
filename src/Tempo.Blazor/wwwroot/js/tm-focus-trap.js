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
//
// Because activation is gated on the lazy import, activate() can run long after the
// overlay rendered — long enough for the app to have focused a field inside it.
// noteReturnTarget lets the owner record the opener BEFORE the surface renders; it
// outranks the late capture, and a capture that lies inside the surface is never
// used. A capture-phase focus-history listener and the last document-level pointerdown
// rescue the cases where both the note and the capture name an inside-element (a
// keyboard-opened surface was never pressed; the history names its trigger anyway).
// Deactivate restores only while the surface still holds the focus (or focus fell to
// <body>): a focus the app placed on a live element outside is never stolen. Owners
// pre-import the module with ensureLoaded() on their first render, so even the first
// trap of a session opens with the listeners (and the note) already in place.

const FOCUSABLE = [
    'a[href]', 'button:not([disabled])', 'textarea:not([disabled])',
    'input:not([disabled]):not([type="hidden"])', 'select:not([disabled])',
    'audio[controls]', 'video[controls]', '[contenteditable]:not([contenteditable="false"])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

// id -> { element, tabHandler, escHandler, returnTarget }
const traps = new Map();

// id -> element an owner noted as the opener (noteReturnTarget). The capture activate() would
// take itself runs only after the lazy ES-module import completed — an app that focuses a field
// inside the overlay during the open then gets that field captured as the "opener", and the close
// would focus a dying element. A gated owner notes in its FIRST parameter-set phase (the mount
// render, before any content can autofocus), so the note still names the trigger. activate()
// consumes the note, so every open captures its own opener; deactivate drops it, even when no
// trap was ever registered (a close that beat the promote gate).
const openers = new Map();

/**
 * Notes the element that should receive focus when the trap identified by `id` deactivates —
 * the OPENER. The focus-trap interop calls this in the owning scope's FIRST parameter-set
 * phase — the mount render, BEFORE the render that presents the surface is applied (ahead of
 * the render batch; its own interop message on Blazor Server) — so the trigger is still the
 * focused element there. A LATER note for the same id never overwrites it: the earliest capture
 * of an open is the trustworthy one, and a scope can note again only after a deactivate cleared
 * the entry. activate() prefers the note over its own (late) capture, consumes it, and falls
 * back to the focus history and the last pointerdown when it names an inside-element.
 * @param {string} id the caller's trap id (the same key activate/deactivate use)
 */
export function noteReturnTarget(id) {
    if (!id) return;
    if (openers.has(id)) return;
    openers.set(id, document.activeElement ?? null);
}

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

// The target of the last pointerdown anywhere in the document, or null. The opener capture can be
// POISONED: activate() runs after the lazy import, by which time the app may have focused a field
// inside the overlay (its own OnAfterRender autofocus, or a focus that raced the import) — and a
// late note reads the same poisoned state. The last pointerdown is the element the user actually
// ENGAGED to open the surface, and at capture time it still lies OUTSIDE it, so activate() uses it
// when the note and the capture name a dying inside-element. Hardened three ways: only a press
// with a FOCUSABLE ancestor is recorded (focus() on a bare div or on <body> is a no-op — Safari
// proves it with body — so a dead candidate must never join the vote); the record EXPIRES (~2 s),
// because a press that old no longer says where the user is; and any non-modifier KEYDOWN clears
// it, because a keyboard gesture between the press and the activation retargeted the user. A
// pointerdown on the surface itself (the close button, a footer action) is filtered by the same
// outside check at vote time. Installed by ensureLoaded()/the first activate; reset with the
// traps in the tests.
let lastPointerDown = null;
let lastPointerDownAt = 0;
let pointerDownListenerInstalled = false;

// How long a pointerdown stays a trustworthy opener candidate. Two seconds is far longer than any
// real open gesture, and short enough that a stale press cannot beat fresher signals.
const POINTER_DOWN_TTL_MS = 2000;

// Keys that do not themselves move the user's attention.
const MODIFIER_KEYS = new Set(['Shift', 'Control', 'Alt', 'Meta']);

function installPointerDownListener() {
    if (pointerDownListenerInstalled) return;
    if (typeof document === 'undefined' || typeof document.addEventListener !== 'function') return;
    pointerDownListenerInstalled = true;
    document.addEventListener('pointerdown', (e) => {
        const t = e && e.target ? e.target : null;
        if (!t || typeof t.closest !== 'function') { lastPointerDown = null; lastPointerDownAt = 0; return; }
        // The press can land on a label span or an svg line inside the real control; the opener
        // restore needs the focusable control itself, so resolve the nearest focusable ancestor
        // (closest() includes the target itself when it already matches). NO focusable ancestor
        // (a plain div, <body>) records NOTHING — restoring to such an element is a silent no-op.
        lastPointerDown = t.closest(FOCUSABLE);
        lastPointerDownAt = lastPointerDown ? Date.now() : 0;
    }, true);
    document.addEventListener('keydown', (e) => {
        // A key press (Enter on a trigger, Tab, anything that is not a bare modifier) means the
        // pointerdown record no longer describes the user's attention.
        if (e && MODIFIER_KEYS.has(e.key)) return;
        lastPointerDown = null;
        lastPointerDownAt = 0;
    }, true);
}

// The most recently focused focusable elements, most recent first. Belt-and-braces behind the
// owner's note: activate() consults the history when its capture lies inside the surface, and the
// most recent entry OUTSIDE it names the opener even for a KEYBOARD-opened surface (Enter on a
// trigger was never a pointerdown, but the trigger took focus and the focusin was seen). Capture
// phase, so the record exists before the target's own handlers run. Installed by ensureLoaded()
// and the first activate; reset with the traps in the tests.
let focusHistory = [];
let focusHistoryListenerInstalled = false;
const FOCUS_HISTORY_LIMIT = 12;

function installFocusHistoryListener() {
    if (focusHistoryListenerInstalled) return;
    if (typeof document === 'undefined' || typeof document.addEventListener !== 'function') return;
    focusHistoryListenerInstalled = true;
    document.addEventListener('focusin', (e) => {
        const t = e && e.target;
        if (!t || typeof t.closest !== 'function' || !t.closest(FOCUSABLE)) return;
        focusHistory = [t, ...focusHistory.filter(el => el !== t)].slice(0, FOCUS_HISTORY_LIMIT);
    }, true);
}

/**
 * Eager warm-up for the FIRST trap of a session. Imports already happened (this module only runs
 * once loaded); what this installs is the document-level listener state — the pointerdown record,
 * the focus history, the inert-exempt record — so the first activation opens with the same
 * evidence every later one has. Without it the first open of a browser session had no opener
 * record at all and its close landed on <body>. Owners call this from their first
 * OnAfterRenderAsync while still closed. Idempotent.
 */
export function ensureLoaded() {
    installExemptFocusListener();
    installPointerDownListener();
    installFocusHistoryListener();
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

/** Whether `candidate` may name the opener of `element`'s surface: it must be a live element
 * OUTSIDE the surface, and never <body> — a focus() on the body is a no-op (Safari proves it),
 * and focusing an inside-element on close would hand focus to a dying element. */
function isOpenerCandidate(candidate, element) {
    return !!candidate
        && candidate !== element
        && candidate !== document.body
        && candidate.isConnected !== false
        && !(typeof element.contains === 'function' && element.contains(candidate));
}

export function activate(element, id, escapeHandler, closeOnEscape, restoreTarget, modal = true, initialTarget = null) {
    if (!element) return;

    // The exempt-focus record must exist before any surface can ask for a recovery. The pointerdown
    // and focus-history records have to exist before THIS open can consult them.
    installExemptFocusListener();
    installPointerDownListener();
    installFocusHistoryListener();

    // The opener is whatever held focus when the trap opened. An explicit restore target and a named
    // id both outrank it; they are resolved at deactivation, because the target may not exist yet.
    // The candidates, most trustworthy first — the first one that lies OUTSIDE this element wins,
    // because focusing an inside-element on close would hand focus to a dying element:
    //   1. the owner's note (noteReturnTarget) — captured in the surface's mount parameter-set
    //      phase, before the render was applied and before any content autofocus ran;
    //   2. document.activeElement — right after the lazy import, so an app focus that landed inside
    //      the overlay during the open has usually already poisoned it;
    //   3. the most recent focus-history entry outside the surface — rescues the poisoned capture
    //      for KEYBOARD-opened surfaces, which no pointerdown can name;
    //   4. the last pointerdown — the element the user actually engaged, still fresh (a stale press
    //      is dropped) — rescues the poisoned capture for pointer-opened surfaces.
    // The note is consumed BEFORE the stale-trap deactivate below, which clears the map entry.
    const noted = openers.has(id) ? openers.get(id) : null;
    openers.delete(id);
    const historyOpener = focusHistory.find(candidate => isOpenerCandidate(candidate, element)) ?? null;
    const pointerOpener = lastPointerDown !== null
        && Date.now() - lastPointerDownAt <= POINTER_DOWN_TTL_MS ? lastPointerDown : null;
    const openerCandidates = [noted, document.activeElement, historyOpener, pointerOpener];
    const returnTarget = openerCandidates.find(candidate => isOpenerCandidate(candidate, element)) ?? null;

    // Deactivate any stale trap reusing this id before re-registering.
    if (traps.has(id)) {
        deactivate(id);
    }

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
            // First focusable that is not a marked scroll region (8.4): an overflowing scroller
            // precedes its own content, so unmarked it would always win this fallback and a
            // Custom dialog's real controls would never take focus. A control INSIDE the region
            // that only scrolling would reveal is demoted to the region itself — focusing it
            // would scroll the dialog start out of view.
            const first = firstFocusableNotScrollRegion(list);
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
            (initialTarget || byId || byData || bySelector || first || element).focus();
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
    openers.clear();
    inertedByTrap.clear();
    scrollLockCount = 0;
    scrollLockWasPresent = false;
    exemptFocus = null;
    exemptFocusListenerInstalled = false;
    lastPointerDown = null;
    lastPointerDownAt = 0;
    pointerDownListenerInstalled = false;
    focusHistory = [];
    focusHistoryListenerInstalled = false;
}

export function deactivate(id) {
    // A note whose activate never ran (a close that beat the promote gate) must not leak into a
    // later trap reusing this id — and the drop must happen even when NO trap is registered,
    // because the note reached the module before its trap ever activated.
    openers.delete(id);

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
    // when no tab stop accepts focus. The Set skips the tab stops re-tried in the fallback. A
    // marked scroll region is demoted behind every real focusable (8.4) — it is a last resort,
    // never the recovery target while a control can take the focus.
    const candidates = [...tabbable(top.element), ...visibleFocusable(top.element)];
    const ordered = [
        ...candidates.filter(el => !(el.hasAttribute && el.hasAttribute('data-tm-scroll-region'))),
        ...candidates.filter(el => el.hasAttribute && el.hasAttribute('data-tm-scroll-region')),
    ];
    const tried = new Set();
    for (const el of ordered) {
        if (tried.has(el)) continue;
        tried.add(el);
        if (focusOf(el)) return true;
    }
    return focusOf(top.element);
}

/** Whether `target` may take the focus on close: it must still be in the DOM and must lie OUTSIDE
 * the closing surface — a target inside it dies with the overlay, and focusing it hands focus to
 * an element the render is about to remove (it then falls to <body> uncontrolled). */
function restorable(target, element) {
    if (!target || target.isConnected === false || typeof target.focus !== 'function') return false;
    return !(element && typeof element.contains === 'function' && element.contains(target));
}

function restoreFocus(trap) {
    // Focus that already sits on a CONNECTED element outside this surface holds: the app (or the
    // user) decided where it belongs — a modal close used to yank it back to the opener even after
    // the app's OK handler had settled it on its post-close action button. A close only restores
    // while its own surface still holds the focus (the common case — the trap moved it in and the
    // render that removes the surface has not run yet) or nothing does (focus fell to <body>).
    const active = document.activeElement;
    if (active && active !== document.body && active.isConnected !== false
        && trap.element && typeof trap.element.contains === 'function'
        && !trap.element.contains(active)) {
        return;
    }
    // Resolved at deactivation: an explicit false restores nothing; an explicit element wins; then
    // the id recorded on the root; then the opener captured at activation; then body. Every
    // candidate passes restorable() — a disconnected or inside-the-surface target is skipped.
    const dataset = trap.element && trap.element.dataset;
    if (dataset && dataset.restoreFocus === 'false') return;

    if (restorable(trap.restoreTarget, trap.element) && focusOf(trap.restoreTarget)) return;

    const id = dataset ? dataset.restoreTarget : null;
    const byId = id && document.getElementById ? document.getElementById(id) : null;
    const byData = !byId && id && document.querySelector
        ? document.querySelector(`[data-tm-id="${escapeAttr(id)}"]`)
        : null;
    if ((restorable(byId, trap.element) && focusOf(byId))
        || (restorable(byData, trap.element) && focusOf(byData))) return;

    if (restorable(trap.returnTarget, trap.element) && focusOf(trap.returnTarget)) return;
    focusOf(document.body);
}

// id -> { element, labelledBy, label, observer, mutations, apply, applyScheduled, applyHandle, stopped }
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

    // The name lives in the map entry (not in closure locals) so updateScrollRegionLabel can
    // re-point it after the title rendered or changed (cluster-B 8.3). The apply* fields carry
    // the coalescing state: one pending overflow decision per frame, cancellable at stop.
    const region = {
        element, labelledBy, label, observer: null, mutations: null, apply: null,
        applyScheduled: false, applyHandle: null, stopped: false,
    };

    region.apply = () => {
        // A 1px tolerance: sub-pixel rounding must not mint a tab stop.
        const overflows = element.scrollHeight > element.clientHeight + 1;
        if (overflows) {
            element.setAttribute('tabindex', '0');
            // The marker the initial-focus fallback skips (8.4): the scroller precedes its own
            // content in DOM order, so unmarked it would always win the "first focusable".
            element.setAttribute('data-tm-scroll-region', '');
            if (region.labelledBy) {
                element.setAttribute('role', 'region');
                element.setAttribute('aria-labelledby', region.labelledBy);
                element.removeAttribute('aria-label');
            } else if (region.label) {
                element.setAttribute('role', 'region');
                element.setAttribute('aria-label', region.label);
                element.removeAttribute('aria-labelledby');
            } else {
                element.removeAttribute('role');
                element.removeAttribute('aria-labelledby');
                element.removeAttribute('aria-label');
            }
        } else {
            element.removeAttribute('tabindex');
            element.removeAttribute('data-tm-scroll-region');
            element.removeAttribute('role');
            element.removeAttribute('aria-labelledby');
            element.removeAttribute('aria-label');
        }
    };

    region.apply();

    const reobserveChildren = () => {
        if (!region.observer) return;
        region.observer.disconnect();
        region.observer.observe(element);
        // Content that grows INSIDE the scroller does not resize the scroller itself when the
        // scroller is height-capped — the children must be observed too (cluster-B 8.3).
        for (const child of element.children ?? []) region.observer.observe(child);
    };
    if (typeof ResizeObserver === 'function') {
        region.observer = new ResizeObserver(() => region.apply());
        reobserveChildren();
    }
    // One pending overflow decision at a time: a framework diff can touch dozens of nodes and
    // fire the observer once per node — the bursts share a single requestAnimationFrame instead
    // of re-measuring per record. Environments without rAF (the node-test stubs) apply
    // synchronously instead of never applying.
    const scheduleApply = () => {
        if (region.applyScheduled) return;
        if (typeof requestAnimationFrame !== 'function') { region.apply(); return; }
        region.applyScheduled = true;
        region.applyHandle = requestAnimationFrame(() => {
            region.applyScheduled = false;
            region.applyHandle = null;
            if (!region.stopped) region.apply();
        });
    };
    // A mutation (rows appended, a title rendered late) also re-decides the region: the
    // ResizeObserver only fires on box size changes, which a capped scroller never produces.
    // The watch stays SHALLOW — childList only. subtree+characterData fired for every text node
    // the framework touches, which re-measured far too often; ADDED children are picked up by
    // re-observing them (below) instead of walking the whole subtree.
    if (typeof MutationObserver === 'function') {
        region.mutations = new MutationObserver((records) => {
            let childList = false;
            for (const record of records ?? []) {
                if (record && record.type === 'childList') { childList = true; break; }
            }
            if (childList) reobserveChildren();
            scheduleApply();
        });
        region.mutations.observe(element, { childList: true });
    }

    scrollRegions.set(id, region);
    return () => stopScrollRegion(id);
}

/**
 * Re-points the region's accessible name after the title rendered or changed (cluster-B 8.3): a
 * `TmModal`/`TmDialog`/`TmDrawer` title that appears (or changes) after the attach rewires the
 * region's aria wiring and re-applies it, so the region never keeps a stale or missing name.
 * Unknown ids are a no-op.
 * @param {string} id the caller's key used at syncScrollRegion
 * @param {string|null} labelledBy the new naming element id, when the surface has a title
 * @param {string|null} label the new accessible name when there is no title id
 */
export function updateScrollRegionLabel(id, labelledBy = null, label = null) {
    const region = scrollRegions.get(id);
    if (!region) return;
    region.labelledBy = labelledBy;
    region.label = label;
    if (region.apply) region.apply();
}

/** Removes the scroll-region observer and the attributes it wrote. Safe to call twice. */
export function stopScrollRegion(id) {
    const region = scrollRegions.get(id);
    if (!region) return;
    scrollRegions.delete(id);
    // Cancel the pending coalesced decision first: a frame that still fires must not re-apply
    // the attributes the removal below just cleared (the stopped guard covers runners without
    // a cancellable rAF).
    region.stopped = true;
    if (region.applyScheduled && region.applyHandle !== null && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(region.applyHandle);
    }
    region.applyScheduled = false;
    region.applyHandle = null;
    if (region.observer) region.observer.disconnect();
    if (region.mutations) region.mutations.disconnect();
    if (region.element) {
        region.element.removeAttribute('tabindex');
        region.element.removeAttribute('data-tm-scroll-region');
        region.element.removeAttribute('role');
        region.element.removeAttribute('aria-labelledby');
        region.element.removeAttribute('aria-label');
    }
}

/**
 * The first focusable that is NOT a marked scroll region (8.4), falling back to the plain first
 * focusable when every entry is one. Still "first focusable" — just never the scroller that
 * precedes its own content in DOM order and would otherwise always win the fallback.
 *
 * A chosen control that lies INSIDE a marked region but is NOT visible without scrolling (its rect
 * lies outside the region's rect — typically far below the fold) is demoted to the region itself:
 * focusing the control would scroll the dialog start out of view, while the region (tabindex=0,
 * role=region, named) keeps the title in view (WAI-ARIA APG). A candidate outside any marked
 * region, or already visible inside one, keeps the focus.
 */
function firstFocusableNotScrollRegion(list) {
    const chosen = list.find(el => !(el.hasAttribute && el.hasAttribute('data-tm-scroll-region'))) || list[0];
    if (!chosen || typeof chosen.closest !== 'function') return chosen;
    const region = chosen.closest('[data-tm-scroll-region]');
    if (!region || region === chosen || rectVisibleWithin(chosen, region)) return chosen;
    return region;
}

/** Whether the element's rect lies within the region's rect, i.e. focus can reach it without
 * scrolling. Environments without geometry (the node-test stubs) keep the candidate. */
function rectVisibleWithin(element, region) {
    if (typeof element.getBoundingClientRect !== 'function'
        || typeof region.getBoundingClientRect !== 'function') return true;
    const rect = element.getBoundingClientRect();
    const regionRect = region.getBoundingClientRect();
    // 1px tolerance, the same sub-pixel guard the scroll region itself uses.
    return rect.top >= regionRect.top - 1 && rect.bottom <= regionRect.bottom + 1;
}
