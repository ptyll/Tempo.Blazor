// Tempo.Blazor.AI — conversation scroll interop.
//
// TmAiConversation owns the transcript in Blazor; the browser owns the geometry. This module is the only
// piece that reads a scroll position, and it answers exactly two questions and performs one action:
//
//   attach(root, content, dotNetRef)  -> start reporting the transcript's scroll state (once per root)
//   detach(root)                      -> stop reporting and release the observers
//   isNearBottom(root)                -> whether the reader is close enough to the bottom to follow content
//   scrollToBottom(root, smooth)      -> follow the newest turn (smooth only when motion is allowed)
//
// Two things are reported back to .NET:
//   OnScrollStateAsync(bool)      when the reader crosses the near-bottom band themselves
//   OnContentResizedAsync(bool)   when the transcript's CONTENT changes size (a streamed token, an image
//                                 that finished loading, an expanded panel) — a scroll handler cannot see
//                                 that, and it is exactly the case where a detached reader must not be
//                                 dragged to the bottom.
//
// The resize report carries the position the reader had BEFORE this growth, not the one measured after it:
// a reader sitting at the bottom is no longer "near the bottom" the instant a token makes the content
// taller, so a fresh measurement would answer "detached" and pop a "new messages" pill at the very reader
// who is watching the answer arrive. The tracked state is updated by the reader's own scroll crossings and
// by this module's own scrollToBottom, which is exactly the state the next growth should react to.
//
// Following the newest turn is the ONE scroll this module performs itself, and a smooth one fires frames
// that are still far from the bottom (it starts where the reader was, while the content already grew). Those
// frames are not a reader crossing: reported as one, they would tell .NET to stop following content that is
// still arriving. `scrollToBottom` therefore marks the scroll as its own and stays silent until it lands —
// released by `scrollend`, by the landing frame, or by a fallback timer — while the first real gesture
// (wheel, touch, pointer, key) releases the mark immediately, so a reader who takes over is never ignored.
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
        entry.lastNear = nearBottom(root);
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

    if (typeof ResizeObserver === 'function' && content) {
        // Reports `entry.lastNear` — the position before the growth — and not a fresh `nearBottom(root)`:
        // the growth itself is what pushed the reader out of the bottom band, and a component that hears
        // "detached" from a reader who never scrolled would offer them the "new messages" pill for the
        // answer they are watching.
        entry.observer = new ResizeObserver(() => report(dotNetRef, 'OnContentResizedAsync', entry.lastNear));
        entry.observer.observe(content);
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
