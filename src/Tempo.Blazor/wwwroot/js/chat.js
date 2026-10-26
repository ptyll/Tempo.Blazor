// Composer behavior for TmChat: Enter/Shift+Enter submit, the IME (composition) guard and
// auto-resize of the textarea height.
//
// WHY the guard lives here: `KeyboardEventArgs.IsComposing` exists only on .NET 9+ — a net8
// Blazor app cannot see that a keydown belongs to an IME composition session (dead keys,
// Japanese/Chinese/Korean input, …) and would submit half-composed text. The browser reports
// it on the native KeyboardEvent (`isComposing`, or the legacy `keyCode === 229`), so the
// guard belongs in the keydown listener. TmChat keeps its C# `@onkeydown` handler as the
// fallback for the window between the render and the module attach; once attached, it stops
// submitting from C# so Enter activates exactly once.
//
// NO module-level state: every call closes over its own element, options and listeners, so
// several TmChat instances (and a host's custom composer through ChatComposerContext
// .AttachAsync) can live on the same page and dispose independently. A rejected
// `invokeMethodAsync` (circuit dropped mid-flight) is swallowed — the composer outlives the
// response and the user must not lose their text to an unhandled rejection.

/**
 * Attaches the composer behavior to one textarea.
 *
 * @param {HTMLTextAreaElement} textarea The composer input (or any element receiving keydown).
 * @param {{ invokeMethodAsync: Function }} dotNetRef A DotNetObjectReference<TmChat>.
 * @param {{ enterToSend?: boolean, maxRows?: number, submitMethod?: string }} [options]
 *   `enterToSend` (default true) — when false, Enter inserts a native new line and only the
 *   send button (or Ctrl/⌘+Enter) submits; `maxRows` (default 0 = off) — grow the textarea up
 *   to this many rows, never beyond the CSS max-height; `submitMethod` (default
 *   'OnComposerSubmit') — the [JSInvokable] method to call, so the thread reply and
 *   message-edit inputs can route to their own handlers.
 * @returns {{ dispose: Function, resize: Function }} `dispose` removes the listeners and the
 *   resize observer (idempotent); `resize` re-measures the height (used by TmChat after a
 *   programmatic clear, which — unlike typing — raises no `input` event).
 */
export function attachComposer(textarea, dotNetRef, options) {
    if (!textarea || typeof textarea.addEventListener !== 'function') {
        throw new TypeError('chat.js: a composer element is required.');
    }
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') {
        throw new TypeError('chat.js: a DotNetObjectReference is required.');
    }

    const opts = options || {};
    const enterToSend = opts.enterToSend !== false;
    const submitMethod = typeof opts.submitMethod === 'string' && opts.submitMethod
        ? opts.submitMethod
        : 'OnComposerSubmit';
    const maxRows = Number.isFinite(opts.maxRows) && opts.maxRows > 0 ? opts.maxRows : 0;

    let disposed = false;
    let resizeObserver = null;

    function onKeyDown(event) {
        if (event.key !== 'Enter') return;
        // IME guard: a keydown inside a composition session (or its legacy keyCode 229) must
        // keep the default behavior so the composition can finish.
        if (event.isComposing || event.keyCode === 229) return;
        if (enterToSend) {
            // Modifier chords are not a submit: Shift+Enter is the documented "new line", and
            // Alt/Ctrl/Meta+Enter stay native for browser and host shortcuts.
            if (event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) return;
        } else {
            // With enterToSend=false Enter inserts a native new line — but Ctrl/⌘+Enter
            // (without Shift/Alt, outside a composition) still submits, so keyboard users
            // keep a send path next to the send button. Shift/Alt stay native.
            if (event.shiftKey || event.altKey || !(event.ctrlKey || event.metaKey)) return;
        }

        if (typeof event.preventDefault === 'function') event.preventDefault();
        Promise.resolve(dotNetRef.invokeMethodAsync(submitMethod))
            .then(() => scheduleResize())
            .catch(() => { /* the circuit is gone — the text stays, nothing to report */ });
    }

    function scheduleResize() {
        if (disposed || !maxRows) return;
        if (typeof requestAnimationFrame === 'function') {
            requestAnimationFrame(() => resize());
        } else {
            resize();
        }
    }

    function resize() {
        if (disposed || !maxRows) return;
        const style = readStyle();
        if (!style) return;

        // Resetting the height first lets `scrollHeight` report the natural content height even
        // when the textarea currently sits taller (e.g. after a row was removed) — without the
        // reset it would never shrink back.
        textarea.style.height = 'auto';

        const lineHeight = positive(style.lineHeight) || positive(style.fontSize) * 1.5 || 21;
        const paddingY = positive(style.paddingTop) + positive(style.paddingBottom);
        const borderY = positive(style.borderTopWidth) + positive(style.borderBottomWidth);
        const rowCap = maxRows * lineHeight + paddingY + (style.boxSizing === 'border-box' ? borderY : 0);
        const cap = Math.min(rowCap, positive(style.maxHeight) || rowCap);

        // `scrollHeight` already includes the padding; under border-box the total height also
        // has to carry the borders, under content-box the set height excludes both.
        const measured = textarea.scrollHeight + (style.boxSizing === 'border-box' ? borderY : 0);
        const next = Math.min(measured, cap);
        textarea.style.height = `${next}px`;
        textarea.style.overflowY = measured > cap ? 'auto' : '';
    }

    function readStyle() {
        try {
            if (typeof getComputedStyle !== 'function') return null;
            return getComputedStyle(textarea);
        } catch {
            return null;
        }
    }

    function onInput() {
        resize();
    }

    textarea.addEventListener('keydown', onKeyDown);
    if (maxRows) {
        textarea.addEventListener('input', onInput);
        scheduleResize();

        // The container around the textarea can change width without an `input` event (a
        // sidebar collapsing, a layout switch, …) — the wrapped rows then need a re-measure.
        // Feature-guarded: the ResizeObserver may not exist in old hosts and the guard keeps
        // the Node tests (no ResizeObserver) on the input-event path.
        if (typeof ResizeObserver === 'function') {
            let lastWidth = null;
            resizeObserver = new ResizeObserver((entries) => {
                if (disposed) return;
                for (const entry of entries) {
                    const width = entry && entry.contentRect ? entry.contentRect.width : null;
                    if (width === null) continue;
                    if (width !== lastWidth) {
                        lastWidth = width;
                        scheduleResize();
                    }
                }
            });
            resizeObserver.observe(textarea);
        }
    }

    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            textarea.removeEventListener('keydown', onKeyDown);
            if (maxRows) textarea.removeEventListener('input', onInput);
            if (resizeObserver) {
                resizeObserver.disconnect();
                resizeObserver = null;
            }
        },
        resize: scheduleResize,
    };
}

/**
 * Focuses an element — the refocus target TmChat uses after a successful send when a custom
 * composer registered its input through <c>ChatComposerContext.AttachAsync</c>.
 *
 * @param {Element} element The element to focus; missing/unfocusable values are ignored.
 */
export function focus(element) {
    if (element && typeof element.focus === 'function') {
        element.focus();
    }
}

/**
 * Focuses an element unless it already has focus — the refocus path used after a send. Stealing
 * focus the element already owns would close the on-screen keyboard on touch devices and reset
 * IME state, so an already-focused element is left untouched.
 *
 * @param {Element} element The element to focus; missing/unfocusable values are ignored.
 */
export function focusIfNeeded(element) {
    if (!element || typeof element.focus !== 'function') return;
    const active = element.ownerDocument && element.ownerDocument.activeElement;
    if (active === element) return;
    element.focus();
}

/** @param {string} value A CSS length like "21px" or "1.5" — 0 when not parseable. */
function positive(value) {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
}
