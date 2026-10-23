// TmDocumentOpenDialog helpers (colocated ES module).
//
// Space scroll guard. The list rows and the grid cards are div[tabindex=0] — non-native focusables — so
// their Enter/Space activation is emulated in C# (Enter on keydown, Space on KEYUP, exactly where a native
// <button> clicks: docs/keyboard-activation-convention.md). The browser's default action for Space keydown on
// a focused non-form element is to scroll the scrollable region, and there is no per-key
// `@onkeydown:preventDefault` in Blazor (it is a render-time bool; a static `true` would also swallow Tab and
// trap focus). This listener is the scoped answer: it cancels the default of the Space keydown ONLY when the
// event target IS a row, a card or a tree item itself — never for a keydown that bubbles off an input, a button or a
// text field inside the dialog, and never for Tab / Enter. The same listener also cancels the default scroll of the arrow keys,
// Home and End on those three targets (shouldSuppressNavigationScroll): they move the roving focus, the browser must not also scroll.
//
// Roving focus. The folder tree, the list rows and the grid cards are ONE tab stop each (roving tabindex) and the
// arrow keys move focus inside them. Blazor owns the tabindex attributes; once it has rendered the new stop this
// module moves the DOM focus to it (focusRoving) — a tabindex alone never moves focus. getGridColumns reads how
// many cards share a grid row, which only the browser knows (the grid is auto-fill), so Up/Down can step a row.

const ITEM_SELECTOR = '.tm-dod-row, .tm-dod-card, .tm-dod-tree-node';
// Registrations are keyed by a stable id the component passes (one per dialog instance), NOT by the element: Blazor can replace the dialog
// root (the modal remounts when the presentation flips between dialog and fullscreen panel), and the element reference a later detach
// would hold is then a different node than the one the listener was added to. Without a key the element itself is the key.
const handlers = new Map();

/**
 * Whether a keydown should have its default (page scroll) cancelled.
 * @param {{ key?: string, target?: any, defaultPrevented?: boolean }} event
 * @returns {boolean}
 */
export function shouldSuppressSpaceScroll(event) {
    if (!event || event.key !== ' ') return false;
    const target = event.target;
    if (!target || typeof target.matches !== 'function') return false;
    return target.matches(ITEM_SELECTOR);
}

const NAVIGATION_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End']);

/**
 * Whether a navigation key's default (scrolling the tree / list container) should be cancelled: the arrows, Home and End
 * move FOCUS between the roving stops (Blazor + focusRoving do that), so the browser's own scroll of the scrollable
 * container would only fight it. Only when the target IS a tree item, row or card — a text field or a button
 * inside the dialog keeps its native caret / scroll behaviour.
 * @param {{ key?: string, target?: any }} event
 * @returns {boolean}
 */
export function shouldSuppressNavigationScroll(event) {
    if (!event || !NAVIGATION_KEYS.has(event.key)) return false;
    // Alt+Left/Right is the browser's back/forward, Ctrl+Home/End and Shift+arrows are the user's own gestures: leave them alone.
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
    const target = event.target;
    if (!target || typeof target.matches !== 'function') return false;
    return target.matches(ITEM_SELECTOR);
}

/**
 * Adds the keydown guard to the dialog root. Idempotent per key; a key that already guards ANOTHER (replaced) root moves to the new one.
 * @param {HTMLElement} root The dialog content root.
 * @param {string} [key] The registration key (defaults to the root element itself).
 */
export function attachSpaceGuard(root, key) {
    if (!root) return;
    const id = key ?? root;
    const existing = handlers.get(id);
    if (existing) {
        if (existing.root === root) return;
        detachSpaceGuard(id);
    }
    const handler = function (event) {
        if (shouldSuppressSpaceScroll(event) || shouldSuppressNavigationScroll(event)) event.preventDefault();
    };
    root.addEventListener('keydown', handler);
    handlers.set(id, { root, handler });
}

/** Removes the guard registered under the key (or the root element). The listener comes off the element it was ADDED to. */
export function detachSpaceGuard(keyOrRoot) {
    const entry = keyOrRoot && handlers.get(keyOrRoot);
    if (!entry) return;
    entry.root.removeEventListener('keydown', entry.handler);
    handlers.delete(keyOrRoot);
}

/**
 * The element a focus request targets, or null.
 * @param {{ querySelectorAll?: Function }} root The dialog root.
 * @param {string} kind 'tree' (a tree item, matched by data-path), 'item' (a row or card, matched by data-id), 'folder' (a mobile
 *   drill-down folder row, matched by data-path) or 'control' (a mobile head control - the back button or the search field - matched by data-dod-control).
 * @param {string} key The folder path, the document id or the control name.
 */
export function findRovingTarget(root, kind, key) {
    if (!root || typeof root.querySelectorAll !== 'function') return null;
    let selector;
    let attribute;
    if (kind === 'tree') { selector = '.tm-dod-tree-node'; attribute = 'data-path'; }
    else if (kind === 'folder') { selector = '.tm-dod-folder-row'; attribute = 'data-path'; }
    else if (kind === 'control') { selector = '[data-dod-control]'; attribute = 'data-dod-control'; }
    else { selector = '.tm-dod-row, .tm-dod-card'; attribute = 'data-id'; }
    for (const element of root.querySelectorAll(selector)) {
        if (element.getAttribute(attribute) === key) return element;
    }
    return null;
}

/** Moves DOM focus to the element Blazor just rendered (a roving stop, a drill-down folder row or a head control). The root is the dialog content root. */
export function focusRoving(root, kind, key) {
    const target = findRovingTarget(root, kind, key);
    if (target && typeof target.focus === 'function') target.focus();
}

/**
 * How many tracks the auto-fill grid currently has: the number of cards that share the first row's top edge.
 * @param {{ querySelectorAll?: Function }} root
 * @returns {number} At least 1.
 */
export function getGridColumns(root) {
    if (!root || typeof root.querySelectorAll !== 'function') return 1;
    const cards = [...root.querySelectorAll('.tm-dod-card')];
    if (cards.length === 0) return 1;
    const top = cards[0].offsetTop;
    let columns = 0;
    for (const card of cards) {
        if (card.offsetTop !== top) break;
        columns++;
    }
    return Math.max(1, columns);
}

// ── Mobile: keep the sticky footer above the on-screen keyboard ────────────────────────────────────────────────────────────
//
// The mobile layout is a fullscreen panel (TmModal MobilePresentation=Fullscreen), pinned to the LAYOUT viewport. An on-screen
// keyboard shrinks only the VISUAL viewport, so the footer (action bar + primary Open) would slide under it. The overlay is padded by
// the hidden gap (the measure the bottom sheets use) and carries a class while the keyboard is up (the footer then drops
// the Link / Copy cards, so the bar and Open keep the room). The padding is an inline style, not a CSS variable: this stylesheet
// reads only canonical tokens.

const lifts = new Map();
const liftRetries = new Map();
const LIFT_RETRY_FRAMES = 10;

/**
 * The inline bottom padding and the keyboard flag for a viewport state.
 * @param {number} layoutHeight window.innerHeight
 * @param {{ height: number, offsetTop: number }|null|undefined} viewport window.visualViewport
 * @returns {{ padding: string, open: boolean }}
 */
export function keyboardLift(layoutHeight, viewport) {
    if (!viewport) return { padding: '', open: false };
    // The part of the layout viewport the visual viewport no longer covers (the same measure tm-sheet.js keyboardOffset takes).
    const hidden = Math.max(0, layoutHeight - viewport.height - viewport.offsetTop);
    return hidden > 0 ? { padding: `${hidden}px`, open: true } : { padding: '', open: false };
}

function tryAttachLift(root, id) {
    const overlay = root.closest?.('.tm-modal-overlay');
    if (!overlay) return false;

    const apply = () => {
        const { padding, open } = keyboardLift(window.innerHeight, window.visualViewport);
        overlay.style.paddingBottom = padding;
        overlay.classList.toggle('tm-dod-keyboard', open);
    };

    apply();
    const viewport = window.visualViewport;
    viewport?.addEventListener('resize', apply);
    viewport?.addEventListener('scroll', apply);
    window.addEventListener('resize', apply);
    lifts.set(id, {
        root,
        stop: () => {
            viewport?.removeEventListener('resize', apply);
            viewport?.removeEventListener('scroll', apply);
            window.removeEventListener('resize', apply);
            // The root may be detached by now (closest() finds nothing): the overlay captured at attach time is the one that was padded.
            for (const target of new Set([overlay, root.closest?.('.tm-modal-overlay')])) {
                if (!target) continue;
                target.style.paddingBottom = '';
                target.classList.remove('tm-dod-keyboard');
            }
        }
    });
    return true;
}

function cancelLiftRetry(id) {
    const frame = liftRetries.get(id);
    if (frame === undefined) return;
    liftRetries.delete(id);
    if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(frame);
}

// The overlay can be missing for a frame (the modal mounts its overlay a render after the dialog content): retry on animation frames.
function scheduleLiftRetry(root, id) {
    if (liftRetries.has(id) || typeof requestAnimationFrame !== 'function') return;
    let frames = 0;
    const tick = () => {
        liftRetries.delete(id);
        if (lifts.has(id) || ++frames > LIFT_RETRY_FRAMES) return;
        if (!tryAttachLift(root, id)) liftRetries.set(id, requestAnimationFrame(tick));
    };
    liftRetries.set(id, requestAnimationFrame(tick));
}

/**
 * Pads the modal overlay by the hidden keyboard gap. Idempotent per key; a key that already lifts ANOTHER (replaced) root moves to the new one.
 * @param {HTMLElement} root The dialog content root (inside .tm-modal-overlay).
 * @param {string} [key] The registration key (defaults to the root element itself).
 * @returns {boolean} True when the lift is attached; false when no overlay exists yet (a retry runs on animation frames and the caller may call again).
 */
export function attachKeyboardLift(root, key) {
    if (!root) return false;
    const id = key ?? root;
    const existing = lifts.get(id);
    if (existing) {
        if (existing.root === root) return true;
        detachKeyboardLift(id);
    }
    if (tryAttachLift(root, id)) {
        cancelLiftRetry(id);
        return true;
    }
    scheduleLiftRetry(root, id);
    return false;
}

/** Removes the lift registered under the key (or the root element) with every listener it added, and cancels a pending retry. */
export function detachKeyboardLift(keyOrRoot) {
    if (!keyOrRoot) return;
    cancelLiftRetry(keyOrRoot);
    const entry = lifts.get(keyOrRoot);
    if (!entry) return;
    entry.stop();
    lifts.delete(keyOrRoot);
}