// Cancels the browser's own "Save page" default for exactly Ctrl+S / Cmd+S inside one email editor.
//
// Why a module instead of `@onkeydown:preventDefault`: that directive is a RENDER-TIME boolean, so a
// static `true` would also swallow Tab and every other key (a focus trap), and there is no per-key
// variant. A named helper that preventDefaults only the save chord is the pattern the repo already
// uses for chord-scoped defaults (data-table.js attachColumnMenuKeys, TmColorGradient). The save itself
// stays in the component's own `@onkeydown`; this module only stops the browser from opening its file
// dialog on top of it.
//
// The listener is attached in the CAPTURE phase on the editor root so it runs before the browser
// decides the default action; it never stops propagation, so Blazor's delegated handler still runs.
//
// One listener per ELEMENT (a WeakMap keyed by the element), not one global slot: two editors can be on
// one page (a diff view, a master/detail layout), and disposing one of them must not disarm the other.

const attached = new WeakMap();

function isSaveChord(event) {
    if (!event) {
        return false;
    }

    if (!(event.ctrlKey || event.metaKey) || event.altKey) {
        return false;
    }

    // `event.key` is 's' (or 'S' with Shift/CapsLock); `event.code` covers layouts where the physical
    // key is S but the produced character is not (e.g. an IME or a non-Latin layout).
    return event.key === 's' || event.key === 'S' || event.code === 'KeyS';
}

/**
 * Attaches the chord-scoped `preventDefault` to an editor root. Idempotent for the same element: a
 * re-render must not stack listeners.
 * @param {Element|null|undefined} element The editor root.
 * @returns {boolean} True when the element is guarded.
 */
export function attachSaveShortcut(element) {
    if (!element) {
        return false;
    }

    if (attached.has(element)) {
        return true;
    }

    const onKeyDown = (event) => {
        if (isSaveChord(event)) {
            event.preventDefault();
        }
    };

    element.addEventListener('keydown', onKeyDown, true);
    attached.set(element, onKeyDown);
    return true;
}

/**
 * Removes THIS element's listener (editor disposal). Safe to call when nothing is attached, and it never
 * touches another editor's guard.
 * @param {Element|null|undefined} element The editor root the listener was attached to.
 * @returns {boolean} True when a listener was removed.
 */
export function detachSaveShortcut(element) {
    if (!element) {
        return false;
    }

    const onKeyDown = attached.get(element);
    if (!onKeyDown) {
        return false;
    }

    element.removeEventListener('keydown', onKeyDown, true);
    attached.delete(element);
    return true;
}

/**
 * Test hook: whether this element is currently guarded.
 * @param {Element|null|undefined} element The editor root.
 * @returns {boolean} True when a listener is attached to the element.
 */
export function isSaveShortcutAttached(element) {
    return !!element && attached.has(element);
}
