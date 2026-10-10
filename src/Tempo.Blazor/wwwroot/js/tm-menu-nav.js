// Tempo.Blazor menu keyboard module (ES module) - the WAI-ARIA menu pattern for every
// role="menu" surface in the library (TmDropdown, TmSplitButton, TmContextMenu, the F5 action-bar
// "More" menu and the F4 toolbar overflow menu).
//
// TmOverlayPanel is the single owner of the menu role, so it attaches this module to a Role="menu"
// panel once per open (the popover element, or the sheet's content wrapper). Behaviour:
//   - ArrowDown / ArrowUp rove focus through the ENABLED items and wrap;
//   - Home / End jump to the first / last enabled item;
//   - typeahead: printable characters focus the next item whose label starts with the typed prefix
//     (repeating one character cycles the matches), diacritics and case are ignored (cs/fr labels);
//   - roving tabindex: once focus is inside the menu the focused item is the only tabbable one, so
//     Tab leaves the menu instead of walking every entry.
// Disabled (`disabled` / `aria-disabled="true"`) and hidden items are skipped everywhere. A key
// pressed inside a text-entry element (a filter input in the menu) is never intercepted. Nothing
// here activates an item - Enter/Space stay with the native <button> (one activation per press,
// docs/keyboard-activation-convention.md) - and Escape stays with overlay.js / the sheet trap.

const ITEM_SELECTOR = '[role="menuitem"], [role="menuitemcheckbox"], [role="menuitemradio"]';
const TYPEAHEAD_RESET_MS = 700;

let attached = new WeakMap();
// trigger element -> teardown of the Tab-out listeners currently on it. A menu that unmounts is never detached,
// so a re-open on the same trigger must replace the previous registration instead of stacking another one.
let anchored = new WeakMap();

/**
 * The index ArrowDown/ArrowUp/Home/End move to.
 * @param {number} count how many enabled items there are
 * @param {number} current the index of the focused item, -1 when none
 * @param {string} key the pressed key
 * @returns {number} the target index, or -1 when the key moves nothing
 */
export function nextIndex(count, current, key) {
    if (!(count > 0)) return -1;
    switch (key) {
        case 'ArrowDown': return current < 0 ? 0 : (current + 1) % count;
        case 'ArrowUp': return current < 0 ? count - 1 : (current - 1 + count) % count;
        case 'Home': return 0;
        case 'End': return count - 1;
        default: return -1;
    }
}

function fold(text) {
    return String(text ?? '').normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase();
}

/**
 * The index of the next label that starts with the typed prefix. A single character starts the
 * search AFTER the current item (so repeating it cycles); a longer prefix starts AT the current item
 * (refining a prefix keeps a match that already has focus). Wraps; -1 when nothing matches.
 * @param {string[]} labels the enabled items' labels in DOM order
 * @param {number} current the index of the focused item, -1 when none
 * @param {string} typed the typed prefix
 */
export function typeaheadMatch(labels, current, typed) {
    const needle = fold(typed);
    if (!needle || labels.length === 0) return -1;
    const start = needle.length === 1 ? current + 1 : Math.max(current, 0);
    for (let step = 0; step < labels.length; step++) {
        const index = (((start + step) % labels.length) + labels.length) % labels.length;
        if (fold(labels[index].trim()).startsWith(needle)) return index;
    }
    return -1;
}

function isEnabled(item) {
    if (item.disabled) return false;
    if (item.getAttribute?.('aria-disabled') === 'true') return false;
    if (item.hidden || item.hasAttribute?.('hidden')) return false;
    return true;
}

/**
 * The focusable menu items of a menu element in DOM order.
 * @param {ParentNode} menu the role=menu element
 */
export function enabledItems(menu) {
    return Array.from(menu.querySelectorAll(ITEM_SELECTOR)).filter(isEnabled);
}

function isTextEntry(target) {
    if (!target) return false;
    const tag = String(target.tagName ?? '').toUpperCase();
    return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable === true;
}

function currentIndex(items, target) {
    let index = items.indexOf(target);
    if (index < 0 && typeof target?.closest === 'function') {
        const owner = target.closest(ITEM_SELECTOR);
        if (owner) index = items.indexOf(owner);
    }
    return index;
}

function rove(menu, focused) {
    const items = Array.from(menu.querySelectorAll(ITEM_SELECTOR));
    const owner = items.includes(focused) ? focused
        : (typeof focused?.closest === 'function' ? focused.closest(ITEM_SELECTOR) : null);
    if (!owner) return;
    for (const item of items) {
        item.setAttribute('tabindex', item === owner ? '0' : '-1');
    }
}

/**
 * Attaches the menu keyboard to a role=menu element. Idempotent per element.
 * @param {HTMLElement|null} menu the menu element (popover root or sheet content wrapper)
 * @param {{invokeMethodAsync:Function}|null} [dotNetRef] the popover panel (TmOverlayPanel); when given, a Tab that
 *   moves focus out of BOTH the menu and its trigger dismisses the popover (NotifyDismissedAsync), so aria-expanded
 *   never stays true behind a closed-over page. Omitted for the sheet, whose own trap/Done owns focus.
 * @param {HTMLElement|null} [anchor] the trigger element of the popover
 */
export function attach(menu, dotNetRef = null, anchor = null) {
    if (!menu || attached.has(menu)) return;

    const state = { typed: '', typedAt: 0 };

    const onKeyDown = event => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
        if (isTextEntry(event.target)) return;

        const items = enabledItems(menu);
        if (items.length === 0) return;
        const current = currentIndex(items, event.target);

        if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Home' || event.key === 'End') {
            const target = nextIndex(items.length, current, event.key);
            if (target >= 0) {
                event.preventDefault();
                items[target].focus();
            }
            return;
        }

        // Typeahead: one printable character that is not the activation key.
        if (typeof event.key !== 'string' || event.key.length !== 1 || event.key === ' ') return;

        const now = Date.now();
        state.typed = now - state.typedAt > TYPEAHEAD_RESET_MS ? event.key : state.typed + event.key;
        state.typedAt = now;
        const needle = [...state.typed].every(character => character === state.typed[0]) ? state.typed[0] : state.typed;
        const match = typeaheadMatch(items.map(item => item.textContent ?? ''), current, needle);
        if (match >= 0) {
            event.preventDefault();
            items[match].focus();
        }
    };

    const onFocusIn = event => rove(menu, event.target);

    menu.addEventListener('keydown', onKeyDown);
    menu.addEventListener('focusin', onFocusIn);
    const registration = { onKeyDown, onFocusIn, anchor: null };
    attached.set(menu, registration);

    if (dotNetRef && typeof dotNetRef.invokeMethodAsync === 'function') {
        // Only a TAB-driven focus loss dismisses: a KeepMenuOpen item that opens a confirm dialog moves focus out
        // programmatically and must keep the menu (docs/overlays.md, rule 8).
        let tabbing = false;
        let timer = 0;
        const armTab = event => {
            if (event.key !== 'Tab') return;
            tabbing = true;
            clearTimeout(timer);
            timer = setTimeout(() => { tabbing = false; }, 0);
        };
        const owns = element => Boolean(element) && (menu.contains?.(element) === true || element === menu
            || (anchor && (anchor === element || anchor.contains?.(element) === true)));
        const teardown = () => {
            clearTimeout(timer);
            menu.removeEventListener('keydown', armTab, true);
            menu.removeEventListener('focusout', onFocusOut);
            if (anchor?.removeEventListener) {
                anchor.removeEventListener('keydown', armTab, true);
                anchor.removeEventListener('focusout', onFocusOut);
                if (anchored.get(anchor) === teardown) anchored.delete(anchor);
            }
        };
        const onFocusOut = event => {
            // The menu this registration belongs to is gone (the popover closed): never dismiss a later open.
            if (menu.isConnected === false) { teardown(); return; }
            const wasTab = tabbing;
            tabbing = false;
            if (!wasTab || !event.relatedTarget || owns(event.relatedTarget)) return;
            try { Promise.resolve(dotNetRef.invokeMethodAsync('NotifyDismissedAsync', 'focus-out')).catch(() => {}); } catch { /* the circuit is gone */ }
        };
        menu.addEventListener('keydown', armTab, true);
        menu.addEventListener('focusout', onFocusOut);
        registration.armTab = armTab;
        registration.onFocusOut = onFocusOut;
        registration.teardown = teardown;
        if (anchor?.addEventListener) {
            // One registration per trigger: drop the previous open's listeners before adding this open's.
            anchored.get(anchor)?.();
            anchor.addEventListener('keydown', armTab, true);
            anchor.addEventListener('focusout', onFocusOut);
            anchored.set(anchor, teardown);
            registration.anchor = anchor;
        }
    }
}

/**
 * Removes the menu keyboard from an element. A menu that unmounts needs no detach (the element and
 * its listeners are collected together); this exists for a host that keeps the element.
 * @param {HTMLElement|null} menu the element passed to attach
 */
export function detach(menu) {
    const registration = menu ? attached.get(menu) : null;
    if (!registration) return;
    menu.removeEventListener('keydown', registration.onKeyDown);
    menu.removeEventListener('focusin', registration.onFocusIn);
    registration.teardown?.();
    attached.delete(menu);
}

/**
 * Whether the element already carries the menu keyboard.
 * @param {HTMLElement|null} menu the element passed to attach
 */
export function isAttached(menu) {
    return Boolean(menu) && attached.has(menu);
}

/**
 * Moves focus to the first enabled menu item inside a host element (the dropdown wrapper: the popover
 * stays in its DOM, so does the sheet). Returns whether something was focused.
 * @param {ParentNode|null} root the element that contains the open role=menu
 */
export function focusFirst(root) {
    const menu = root?.querySelector?.('[role="menu"]');
    if (!menu) return false;
    const first = enabledItems(menu)[0];
    if (!first) return false;
    first.focus();
    return true;
}

/** Test seam: forgets every registration. */
export function __resetForTests() {
    attached = new WeakMap();
    anchored = new WeakMap();
}
