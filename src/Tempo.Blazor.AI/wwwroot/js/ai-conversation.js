// Tempo.Blazor.AI — conversation scroll interop.
//
// TmAiConversation owns the transcript in Blazor; the browser owns the geometry. This module is the only
// piece that reads a scroll position, and it answers exactly two questions and performs one action:
//
//   attach(root, content, dotNetRef)  -> start reporting the transcript's scroll state (once per root)
//   detach(root)                      -> stop reporting and release the observers
//   isNearBottom(root)                -> whether the reader is close enough to the bottom to follow content
//   scrollToBottom(root, smooth)      -> follow the newest turn (smooth only when motion is allowed)
//   scrollToMessage(root, id, smooth) -> bring the turn with data-message-id="id" into view
//
// Two things are reported back to .NET:
//   OnScrollStateAsync(bool)      when the reader crosses the near-bottom band themselves, or when one of
//                                 the module's own landing resyncs lands on a position .NET does not know
//                                 (the `scrollToMessage` jump, see below)
//   OnContentResizedAsync(bool)   when the transcript's geometry changes size — the CONTENT (a streamed
//                                 token, an image that finished loading, an expanded panel) or the LOG BOX
//                                 ITSELF (a host resized the panel; height changes only) — a scroll handler
//                                 cannot see either, and it is exactly the case where a detached reader
//                                 must not be dragged to the bottom.
//
// The resize report carries the position the reader had BEFORE this growth, not the one measured after it:
// a reader sitting at the bottom is no longer "near the bottom" the instant a token makes the content
// taller, so a fresh measurement would answer "detached" and pop a "new messages" pill at the very reader
// who is watching the answer arrive. The tracked state is updated by the reader's own scroll crossings,
// by this module's own scrollToBottom, and by the detached baseline `scrollToMessage` sets for itself.
//
// Following the newest turn is the ONE scroll this module performs itself, and a smooth one fires frames
// that are still far from the bottom (it starts where the reader was, while the content already grew). Those
// frames are not a reader crossing: reported as one, they would tell .NET to stop following content that is
// still arriving. `scrollToBottom` therefore marks the scroll as its own and stays silent until it lands —
// released by `scrollend`, by the landing frame, or by a fallback timer — while the first real gesture
// (wheel, touch, pointer, key) releases the mark immediately, so a reader who takes over is never ignored.
// `scrollToMessage` shares the mark, but detaches the baseline BEFORE its animation starts: its landing
// (usually away from the bottom, occasionally right at it) is the honest state, and the release resync
// reports it when it differs from the baseline .NET was left with.
//
// The module is a plain ES module (no eval, no inline handler), imported through IJSObjectReference, so a
// host Content-Security-Policy needs no 'unsafe-inline' for it. Every entry point is null-safe: a component
// that lost its root element (a circuit that went away mid-render) gets a quiet false, never a throw.
/**
 * How close to the bottom still counts as "at the bottom", in CSS pixels. A little slack is deliberate:
 * sub-pixel layout rounding otherwise leaves the scroller a few pixels short of the end after every
 * scroll, and the reader would be treated as detached while visibly at the bottom.
 */
const NEAR_BOTTOM_THRESHOLD = 48;

/**
 * How long the module's own scroll stays marked, in milliseconds, when the engine has no `scrollend` event
 * to release it: long enough for a smooth scroll to land (browsers take 300–500 ms), short enough that a
 * missed release cannot keep suppressing the reader for long. Every gesture that starts a scroll releases
 * the mark on its own, so this only covers a scroll nobody gestured for.
 */
const PROGRAMMATIC_RELEASE_MS = 500;

/**
 * Events that mean the READER took the scroll over. They clear the mark before the resulting `scroll`
 * events arrive, so a gesture during the module's own animation is still reported as a crossing.
 */
const INTERRUPT_EVENTS = ['wheel', 'touchstart', 'pointerdown', 'keydown'];

/** Roots this module is currently attached to. */
const attached = new WeakMap();

function prefersReducedMotion() {
    try {
        return globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches === true;
    } catch {
        return false;
    }
}

function nearBottom(root) {
    if (!root) {
        return true;
    }

    return root.scrollHeight - root.scrollTop - root.clientHeight <= NEAR_BOTTOM_THRESHOLD;
}

function report(dotNetRef, method, value) {
    try {
        dotNetRef?.invokeMethodAsync(method, value)?.catch(() => {
            // The circuit is gone; there is nobody left to update.
        });
    } catch {
        // Same case, synchronously.
    }
}

/**
 * Marks the scroll the module is about to start as its own. A previous mark is released first, so a
 * streaming re-follow cannot stack `scrollend` listeners or fallback timers.
 */
function beginProgrammatic(entry, root) {
    endProgrammatic(entry, root, false);

    entry.programmatic = true;
    entry.scrollEnd = () => endProgrammatic(entry, root, true);
    root.addEventListener?.('scrollend', entry.scrollEnd, { once: true });
    entry.releaseTimer = setTimeout(() => endProgrammatic(entry, root, true), PROGRAMMATIC_RELEASE_MS);
}

/**
 * Releases the mark. `resync` makes the measured position the new baseline, which is what the end of the
 * module's own scroll wants: neither the middle of the animation nor its landing is a crossing to report.
 * A reader gesture passes `false`, keeping `lastNear` where the reader left it, so the scroll they cause is
 * still reported as a crossing.
 *
 * When the resync lands on a position DIFFERENT from the baseline .NET last knew, the resync itself is
 * reported: the measured position is the honest state of record and a differing one means .NET's follow
 * state (`_stickToBottom`) is wrong — the case that matters is `scrollToMessage`, which detaches the
 * baseline BEFORE its animation starts and whose landing (usually away from the bottom, sometimes right at
 * it) must reach .NET once it settles.
 */
function endProgrammatic(entry, root, resync) {
    entry.programmatic = false;

    if (entry.scrollEnd) {
        root?.removeEventListener?.('scrollend', entry.scrollEnd);
        entry.scrollEnd = null;
    }

    if (entry.releaseTimer !== null) {
        clearTimeout(entry.releaseTimer);
        entry.releaseTimer = null;
    }

    if (resync) {
        const near = nearBottom(root);
        const changed = near !== entry.lastNear;
        entry.lastNear = near;
        if (changed) {
            report(entry.dotNetRef, 'OnScrollStateAsync', near);
        }
    }
}

/**
 * Starts tracking `root`. Re-attaching the same root first detaches the previous session, so a component
 * that re-renders cannot stack listeners or observers.
 */
export function attach(root, content, dotNetRef) {
    if (!root) {
        return false;
    }

    detach(root);

    const entry = {
        dotNetRef,
        onScroll: null,
        onGesture: null,
        observer: null,
        lastNear: nearBottom(root),
        boxHeight: null,
        programmatic: false,
        scrollEnd: null,
        releaseTimer: null,
    };

    entry.onScroll = () => {
        const near = nearBottom(root);
        if (entry.programmatic) {
            if (near) {
                // The module's own scroll landed: resume reporting from the real position, without
                // inventing a crossing for the animation that just ended.
                endProgrammatic(entry, root, true);
            }

            // Every frame of that animation is the module's own motion, not the reader's.
            return;
        }

        if (near === entry.lastNear) {
            // Only a CROSSING is news: a scroll fires dozens of times per gesture and every event that
            // reported the unchanged state would be a Blazor round-trip.
            return;
        }

        entry.lastNear = near;
        report(dotNetRef, 'OnScrollStateAsync', near);
    };

    // A reader taking over the scroll during the module's own animation: the mark must be gone BEFORE the
    // scroll events of their gesture arrive, or their crossing would be swallowed along with the animation.
    entry.onGesture = () => endProgrammatic(entry, root, false);

    root.addEventListener('scroll', entry.onScroll, { passive: true });
    for (const type of INTERRUPT_EVENTS) {
        root.addEventListener(type, entry.onGesture, { passive: true });
    }

    if (typeof ResizeObserver === 'function') {
        // ONE observer for both surfaces the transcript's geometry lives on: the CONTENT (a streamed
        // token, an image that finished loading, an expanded panel) and the LOG BOX ITSELF (the host
        // resized the side panel, a layout split moved, the host's own height binding changed). The BOX
        // half filters on the box's own HEIGHT: a width-only resize (a scrollbar appearing, a panel
        // dragging wider) reflows the same number of lines and must not disturb the reader's position.
        // When the box SHRINKS, `scrollTop` stays where it is while the bottom moves away — a scroll
        // handler cannot see that either — so a bottom-riding reader is re-aligned by the report: it
        // carries `entry.lastNear` (the position BEFORE the change), .NET follows the new bottom, and the
        // jump that re-asserts it runs without animation. A DETACHED reader is never dragged and never
        // pill-ed: the report carries their position, not the new one. The report carries the position
        // BEFORE the change for the same reason the content report does: the change itself is what pushed
        // a bottom-riding reader out of the band, and a fresh measurement would answer "detached" for the
        // reader who is watching the answer arrive.
        entry.observer = new ResizeObserver((observed) => {
            const contentChanged = content !== null && content !== undefined
                && observed.some((item) => item.target === content);
            const box = observed.find((item) => item.target === root);
            let boxHeightChanged = false;
            if (box && box.contentRect) {
                const height = box.contentRect.height;
                boxHeightChanged = entry.boxHeight === null
                    || Math.abs(height - entry.boxHeight) > 0.5;
                entry.boxHeight = height;
            }

            if (!contentChanged && !boxHeightChanged) {
                return;
            }

            report(dotNetRef, 'OnContentResizedAsync', entry.lastNear);
        });
        if (content) {
            entry.observer.observe(content);
        }

        entry.observer.observe(root);
    }

    attached.set(root, entry);
    return true;
}

/** Stops tracking `root`; safe to call when nothing is attached. */
export function detach(root) {
    const entry = attached.get(root);
    if (!entry) {
        return false;
    }

    if (entry.onScroll) {
        root.removeEventListener('scroll', entry.onScroll);
    }

    if (entry.onGesture) {
        for (const type of INTERRUPT_EVENTS) {
            root.removeEventListener(type, entry.onGesture);
        }
    }

    endProgrammatic(entry, root, false);

    entry.observer?.disconnect();
    attached.delete(root);
    return true;
}

/** Whether the transcript is within the bottom tolerance. */
export function isNearBottom(root) {
    return nearBottom(root);
}

/**
 * Scrolls the transcript to its end. `smooth` is honoured only when the reader's system does not ask for
 * reduced motion — a programmatic smooth scroll is exactly the kind of animation that preference forbids.
 */
export function scrollToBottom(root, smooth) {
    if (!root) {
        return false;
    }

    const entry = attached.get(root);
    if (entry) {
        // The scroll below is the module's own motion. Mark it BEFORE it starts, so not one frame of the
        // animation can be mistaken for the reader detaching from a transcript that did grow.
        beginProgrammatic(entry, root);

        // The reader is at the bottom as of this action: the next growth must be followed, not pill-ed.
        entry.lastNear = true;
    }

    const behavior = smooth === true && !prefersReducedMotion() ? 'smooth' : 'auto';
    try {
        root.scrollTo({ top: root.scrollHeight, behavior });
    } catch {
        // A browser without scroll options still gets the jump.
        root.scrollTop = root.scrollHeight;
    }

    return true;
}

/**
 * Escapes an id for an attribute-selector string — inside a quoted CSS string only a backslash and the
 * quote itself need it. `CSS.escape` would also work, but it escapes for IDENTIFIERS and is not available
 * in every engine this module is node-tested in.
 */
function attributeValueEscape(value) {
    return value.replace(/["\\]/g, '\\$&');
}

/**
 * The log's `padding-top` (in CSS pixels), which sits between the root's border edge and the first
 * scrollable pixel: `scrollToMessage` measures the target against the border edge, so the padding has to
 * come off or the turn lands lower than the top by exactly that much. Engines without `getComputedStyle`
 * (the node tests) get 0 and the border-edge measurement stands.
 */
function paddingBoxTop(root) {
    try {
        const padding = globalThis.getComputedStyle?.(root)?.paddingTop;
        return Number.parseFloat(padding ?? '') || 0;
    } catch {
        return 0;
    }
}

/**
 * Brings the turn with `data-message-id="messageId"` into view. The scroll moves ONLY the transcript —
 * the target's offset is measured against the root itself (minus the log's `padding-top`), never through
 * `scrollIntoView`, which would also scroll the page the component sits on. Like `scrollToBottom`, the
 * scroll is marked as the module's own (its frames are not reported as a reader crossing) — but the
 * baseline is detached BEFORE the animation starts (`lastNear = false`: .NET unsticks its follow for a
 * target that is not the newest turn), and the mark's RELEASE re-syncs `lastNear` to wherever the turn
 * actually left the reader and REPORTS it when that differs from the detached baseline — the honest
 * position for the next growth. Returns whether a matching turn was found; a turn outside the render
 * window (or an empty id) scrolls nothing.
 */
export function scrollToMessage(root, messageId, smooth) {
    if (!root || typeof messageId !== 'string' || messageId.length === 0) {
        return false;
    }

    let target = null;
    try {
        target = root.querySelector('[data-message-id="' + attributeValueEscape(messageId) + '"]');
    } catch {
        return false;
    }

    if (!target) {
        return false;
    }

    const entry = attached.get(root);
    if (entry) {
        beginProgrammatic(entry, root);
        // The jump is the module's own motion, but its DESTINATION is not the bottom: from the moment the
        // animation starts, "detached" is the state both halves of the package work from, and the landing
        // resync reports the truth if it differs (a turn near the bottom reports `true` again).
        entry.lastNear = false;
    }

    const rootTop = root.getBoundingClientRect().top;
    const targetTop = target.getBoundingClientRect().top;
    const top = root.scrollTop + (targetTop - rootTop) - paddingBoxTop(root);
    const behavior = smooth === true && !prefersReducedMotion() ? 'smooth' : 'auto';
    try {
        root.scrollTo({ top, behavior });
    } catch {
        // A browser without scroll options still gets the jump.
        root.scrollTop = top;
    }

    return true;
}
