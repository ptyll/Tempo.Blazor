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
//
// The SCROLLER half (FZE-6) keeps the messages container pinned to the bottom ONLY while the
// user is actually reading there: `attachScroller` reports at-bottom state CHANGES back to
// TmChat (which decides whether an arriving message may scroll, or must count as unread
// behind the jump-to-new button) and preserves the visual position across a prepend of older
// messages. TmChat drives the timing: `afterUpdate(mode)` runs from OnAfterRenderAsync — the
// DOM is already updated. The scroller keeps its own PRE-UPDATE SNAPSHOT (`scrollTop`,
// `scrollHeight`, the first visible row and whether the viewport was at the bottom), which it
// refreshes on every scroll event and at the end of every `afterUpdate` — so no `beforeUpdate`
// round trip is needed: the geometry is by definition the state before the render that is now
// calling `afterUpdate`. A `pinned` flag (set by every programmatic `scrollToBottom`, cleared
// only by a user scroll upward) keeps the at-bottom belief stable across the intermediate
// positions of a smooth programmatic scroll. All DOM queries are scoped to the passed
// container — the thread panel renders the root message a second time, so `data-message-id`
// values are duplicated page-wide.

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

/**
 * Whether the user asked the OS for reduced motion — smooth scrolling must degrade to an
 * instant jump for them (`prefers-reduced-motion: reduce`). Missing `matchMedia` (old hosts,
 * the Node tests) behaves like "no preference", i.e. the smooth default applies.
 *
 * @returns {boolean} True when smooth scrolling must be skipped.
 */
function prefersReducedMotion() {
    try {
        return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    } catch {
        return false;
    }
}

/**
 * The scroll behavior for an animated scroll: `auto` under reduced motion, `smooth` otherwise.
 *
 * @returns {'auto'|'smooth'} The ScrollOptions behavior value.
 */
function scrollBehavior() {
    return prefersReducedMotion() ? 'auto' : 'smooth';
}

/**
 * Attaches the scroll behavior to the messages container (FZE-6).
 *
 * The handle is driven by TmChat around every render whose message list changed:
 *
 * - a passive `scroll` listener reports AT-BOTTOM state CHANGES (never every event) through
 *   `OnScrollStateChanged(bool)` on the DotNetObjectReference — that is what lets TmChat keep
 *   the "jump to new messages" button honest; a near-top report (`OnScrollNearTop(bool)`,
 *   also on change only) drives the automatic older-page load at the top edge (skipped while
 *   the load-older button itself holds focus — a keyboard user tabbing through it must not
 *   trigger a load). The away report is SUPPRESSED while a programmatic scroll is in flight.
 * - the scroller keeps the pre-update geometry itself (refreshed on every scroll event and at
 *   the end of every `afterUpdate`) — TmChat calls no `beforeUpdate`.
 * - `afterUpdate(mode)` applies the position work AFTER the rows changed:
 *   `'append'` scrolls to the bottom when the viewport WAS at the bottom (`pinned` or
 *   measured) and leaves the position untouched (returning `'unread'`) when the user was
 *   reading above it — TmChat then counts the new messages behind the jump button;
 *   `'prepend'` restores the reading position on the snapshot's first-visible-row ANCHOR
 *   (`scrollTop = anchor.offsetTop - anchorOffset`, which ignores height changes below the
 *   anchor) and falls back to the grown-height delta when the anchor row is gone;
 *   `'none'` only refreshes the snapshot and RE-PINS a pinned viewport whose content grew
 *   (e.g. the typing indicator appearing) — anything else is a no-op.
 *
 * @param {Element} container The `.tm-chat__messages` scroll container. All queries are scoped
 *   to it — the thread panel duplicates `data-message-id` values page-wide.
 * @param {{ invokeMethodAsync: Function }} dotNetRef A DotNetObjectReference<TmChat>.
 * @param {{ threshold?: number, topThreshold?: number }} [options] `threshold` (default 40) —
 *   the distance-from-bottom tolerance in px that still counts as "at the bottom";
 *   `topThreshold` (default 80) — the distance from the top that counts as "near the top".
 * @returns {{
 *   dispose: Function,
 *   isAtBottom: Function,
 *   scrollToBottom: Function,
 *   scrollToMessage: Function,
 *   focusMessage: Function,
 *   afterUpdate: Function,
 * }} `dispose` removes the listeners (idempotent); `isAtBottom()` reads the live state;
 *   `scrollToBottom()` / `scrollToMessage(id)` are the programmatic scrolls TmChat exposes
 *   (`scrollToMessage` returns whether the anchor was found); `focusMessage(id)` focuses a
 *   row (`tabindex="-1"`, `preventScroll` — the focus mover after a load-older prepend or a
 *   jump-button click so focus never falls to the body); `afterUpdate(mode)` is the
 *   per-render bookkeeping half.
 */
export function attachScroller(container, dotNetRef, options) {
    if (!container || typeof container.addEventListener !== 'function') {
        throw new TypeError('chat.js: a messages container element is required.');
    }
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') {
        throw new TypeError('chat.js: a DotNetObjectReference is required.');
    }

    const opts = options || {};
    const threshold = Number.isFinite(opts.threshold) && opts.threshold >= 0 ? opts.threshold : 40;
    const topThreshold = Number.isFinite(opts.topThreshold) && opts.topThreshold >= 0 ? opts.topThreshold : 80;

    let disposed = false;
    // `null` = not measured yet, so the FIRST scroll event reports the initial state.
    let lastAtBottom = null;
    let lastNearTop = null;

    // ── Pinned state ──
    // `pinned` is the scroller's belief that the viewport belongs to the bottom: set by every
    // programmatic `scrollToBottom`, cleared ONLY by a user scroll upward (a scrollTop below
    // the last programmatic target, or a wheel/touch/key gesture that scrolls up). It keeps
    // the at-bottom decision stable across the intermediate positions of a smooth programmatic
    // scroll, so two appends inside ONE animation both see "was at the bottom".
    let pinned = true;
    let scrollTarget = null;        // the scrollTop the last programmatic scroll aims at
    let programmaticScroll = false; // a smooth programmatic scroll is currently animating

    // ── Pre-update snapshot ──
    // Refreshed on every scroll event and at the end of every afterUpdate, so it always
    // describes the DOM as it was BEFORE the render that is now calling afterUpdate.
    let snapshot = null;

    function distanceFromBottom() {
        return (container.scrollHeight || 0) - (container.scrollTop || 0) - (container.clientHeight || 0);
    }

    function isAtBottom() {
        return distanceFromBottom() <= threshold;
    }

    function isNearTop() {
        return (container.scrollTop || 0) <= topThreshold;
    }

    function report(method, value) {
        // A dropped circuit must not turn a scroll event into an unhandled rejection.
        Promise.resolve(dotNetRef.invokeMethodAsync(method, value)).catch(() => { /* unreachable */ });
    }

    function messageAnchors() {
        if (typeof container.querySelectorAll !== 'function') return [];
        return container.querySelectorAll('[data-message-id]');
    }

    function findMessage(id) {
        if (!id) return null;
        for (const item of messageAnchors()) {
            if (typeof item.getAttribute === 'function' && item.getAttribute('data-message-id') === id) {
                return item;
            }
        }
        return null;
    }

    function loadOlderButton() {
        if (typeof container.querySelector !== 'function') return null;
        return container.querySelector('.tm-chat__load-older');
    }

    function focusIsOnLoadOlder() {
        const doc = container.ownerDocument;
        const active = doc && doc.activeElement;
        const button = loadOlderButton();
        return !!button && !!active && button === active;
    }

    /**
     * The first message row whose bottom edge is still at or below the container's top — the
     * message the user is currently reading — plus its offset below the viewport top.
     *
     * @param {number} scrollTop The scrollTop to measure against.
     * @returns {{ id: string, offset: number|null }|null} The anchor, if any row is visible.
     */
    function firstVisibleAnchor(scrollTop) {
        for (const item of messageAnchors()) {
            const id = typeof item.getAttribute === 'function' ? item.getAttribute('data-message-id') : null;
            if (!id) continue;
            if (typeof item.offsetTop !== 'number') return { id, offset: null }; // no geometry — the row wins
            if (item.offsetTop + (item.offsetHeight || 0) > scrollTop) {
                return { id, offset: item.offsetTop - scrollTop };
            }
        }
        return null;
    }

    function refreshSnapshot() {
        if (disposed) return;
        const scrollTop = container.scrollTop || 0;
        const anchor = firstVisibleAnchor(scrollTop);
        snapshot = {
            scrollTop,
            scrollHeight: container.scrollHeight || 0,
            wasAtBottom: pinned || isAtBottom(),
            anchorId: anchor ? anchor.id : null,
            anchorOffset: anchor ? anchor.offset : null,
        };
    }

    function clearProgrammaticScrollIfArrived() {
        if (!programmaticScroll) return;
        const top = container.scrollTop || 0;
        if (typeof scrollTarget === 'number' && Math.abs(top - scrollTarget) <= 1) {
            programmaticScroll = false; // the target is reached
        } else if (isAtBottom()) {
            programmaticScroll = false; // the browser clamped somewhere at the bottom
        }
    }

    function onScroll() {
        if (disposed) return;
        if (programmaticScroll) {
            clearProgrammaticScrollIfArrived();
        } else if (typeof scrollTarget === 'number'
            && (container.scrollTop || 0) < scrollTarget - 1
            && !isAtBottom()) {
            // A position above the last programmatic target (and away from the bottom) can
            // only be the user's own doing — release the pin.
            pinned = false;
        }
        if (!programmaticScroll && isAtBottom()) {
            // The user scrolled (back) to the bottom by hand: re-pin, so the next append
            // scrolls instead of counting as unread — a wheel-up release followed by a manual
            // scroll to the bottom must not leave the scroller unpinned.
            pinned = true;
            scrollTarget = container.scrollHeight || 0;
        }
        refreshSnapshot();

        const atBottom = isAtBottom();
        // The intermediate positions of a programmatic animation are not the user's reading
        // state — suppress the away report until the target is reached (or the flight ends).
        if (atBottom !== lastAtBottom && (atBottom || !programmaticScroll)) {
            lastAtBottom = atBottom;
            report('OnScrollStateChanged', atBottom);
        }

        // Keyboarding through the load-older button must not load a page per Tab press.
        if (!focusIsOnLoadOlder()) {
            const nearTop = isNearTop();
            if (nearTop !== lastNearTop) {
                lastNearTop = nearTop;
                report('OnScrollNearTop', nearTop);
            }
        }
    }

    function onUserScrollUp() {
        if (disposed) return;
        // The user is taking over: an in-flight programmatic scroll is abandoned and the pin
        // is released, so the next append counts as unread instead of scrolling.
        pinned = false;
        programmaticScroll = false;
    }

    function onWheel(event) {
        if (event && event.deltaY < 0) onUserScrollUp();
    }

    function onKeyDown(event) {
        const key = event && event.key;
        if (key === 'ArrowUp' || key === 'PageUp' || key === 'Home') onUserScrollUp();
    }

    function onScrollEnd() {
        programmaticScroll = false;
    }

    container.addEventListener('scroll', onScroll, { passive: true });
    container.addEventListener('scrollend', onScrollEnd);
    container.addEventListener('wheel', onWheel, { passive: true });
    container.addEventListener('touchstart', onUserScrollUp, { passive: true });
    container.addEventListener('keydown', onKeyDown);
    refreshSnapshot();

    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            container.removeEventListener('scroll', onScroll);
            container.removeEventListener('scrollend', onScrollEnd);
            container.removeEventListener('wheel', onWheel);
            container.removeEventListener('touchstart', onUserScrollUp);
            container.removeEventListener('keydown', onKeyDown);
        },
        isAtBottom,
        /**
         * @param {boolean} [instant] Skip the animation. TmChat passes true for the FIRST render
         *   and for a replaced list: an animated initial scroll would fire scroll events along the
         *   whole track — including a spurious near-top report that would auto-load history the
         *   user never asked for — and a conversation opening should land at the bottom at once.
         */
        scrollToBottom(instant) {
            if (disposed) return;
            const target = container.scrollHeight || 0;
            const behavior = instant || prefersReducedMotion() ? 'auto' : 'smooth';
            pinned = true;
            scrollTarget = target;
            if (typeof container.scrollTo === 'function') {
                // The browser clamps `top` to the real bottom; reduced motion gets an instant
                // jump instead of an animation.
                container.scrollTo({ top: target, behavior });
                programmaticScroll = behavior === 'smooth';
            } else {
                container.scrollTop = target;
            }
            refreshSnapshot();
        },
        scrollToMessage(id) {
            if (disposed) return false;
            const item = findMessage(id);
            if (!item) return false;
            if (typeof item.scrollIntoView === 'function') {
                item.scrollIntoView({ block: 'center', behavior: scrollBehavior() });
            } else if (typeof item.offsetTop === 'number') {
                container.scrollTop = item.offsetTop;
            }
            refreshSnapshot();
            return true;
        },
        focusMessage(id) {
            if (disposed) return false;
            const item = findMessage(id);
            if (!item || typeof item.focus !== 'function') return false;
            if (typeof item.setAttribute === 'function') item.setAttribute('tabindex', '-1');
            item.focus({ preventScroll: true });
            return true;
        },
        afterUpdate(mode) {
            if (disposed) return 'none';
            const before = snapshot;

            if (mode === 'prepend') {
                if (before) {
                    restoreAnchor(before);
                }
                refreshSnapshot();
                return 'preserved';
            }
            if (mode === 'append') {
                const wasAtBottom = before ? before.wasAtBottom : (pinned || isAtBottom());
                if (wasAtBottom) {
                    this.scrollToBottom();
                    return 'scrolled';
                }
                // The user is reading older messages: stay put. TmChat counts the new messages
                // and shows its jump-to-new button (the at-bottom report already went out).
                refreshSnapshot();
                return 'unread';
            }

            // 'none' (and any unknown mode): the rows did not change in a way TmChat owns —
            // keep the snapshot fresh for the NEXT change and re-pin a viewport whose content
            // grew under it (e.g. the typing indicator appearing or a draft banner) — the pin
            // belief itself OR the pre-update snapshot saying "was at the bottom" (a user who
            // scrolled back down by hand after a wheel-up release) both count.
            if (before && (pinned || before.wasAtBottom) && (container.scrollHeight || 0) > before.scrollHeight) {
                this.scrollToBottom();
            }
            refreshSnapshot();
            return 'none';
        },
    };

    /**
     * Puts the row the user was reading back where it was: the anchor row (kept by the `@key`
     * across the prepend) must sit at the same distance below the viewport top. This ignores
     * height changes BELOW the anchor — only the growth above the reading position matters.
     *
     * @param {{ scrollTop: number, scrollHeight: number, anchorId: string|null, anchorOffset: number|null }} before
     *   The pre-update snapshot.
     */
    function restoreAnchor(before) {
        const item = before.anchorId ? findMessage(before.anchorId) : null;
        if (item && typeof item.offsetTop === 'number' && typeof before.anchorOffset === 'number') {
            container.scrollTop = item.offsetTop - before.anchorOffset;
            return;
        }
        // The anchor is gone (its row was removed or never had geometry): fall back to the
        // grown-height delta — the browser kept `scrollTop` while rows were inserted ABOVE the
        // viewport, so the content the user was reading slid down by exactly the delta.
        const delta = (container.scrollHeight || 0) - before.scrollHeight;
        container.scrollTop = before.scrollTop + Math.max(delta, 0);
    }
}
