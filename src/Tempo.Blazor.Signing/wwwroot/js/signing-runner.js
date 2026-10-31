/**
 * signing-runner.js — the TmSigningFormRunner's scroll + pointer helpers (Signing redesign phase 5).
 *
 * Imported by the package's Blazor interop wrapper as an ES module (dynamic `import()`, like
 * `pdf-template-designer.js`) — NO <script> tag is needed in the host page. The pure helpers
 * (scroll targeting, behaviour, pointer classification) are exported for the node lane
 * (`npm run test:signing-runner`); the DOM glue reads the runner's own BEM classes.
 */

/** Extra breathing room between the field's bottom edge and the sheet's top edge, in px. */
const SHEET_GAP_PX = 16;

/**
 * Whether the primary input is a coarse pointer (a finger). `matchMedia` is injectable so the
 * node lane can stub it; a runtime without matchMedia (SSR, tests) reports a fine pointer.
 *
 * @param {((query: string) => { matches: boolean }) | undefined} [matchMedia]
 * @returns {boolean}
 */
export function isCoarsePointer(matchMedia) {
    const query = matchMedia ?? (typeof globalThis !== 'undefined' ? globalThis.matchMedia : undefined);
    try {
        return query?.('(pointer: coarse)')?.matches === true;
    } catch {
        return false;
    }
}

/**
 * Whether the user asked for reduced motion. The scroll animation honours it with
 * `behavior: 'auto'` (an instant jump) instead of the smooth default.
 *
 * @param {((query: string) => { matches: boolean }) | undefined} [matchMedia]
 * @returns {boolean}
 */
export function prefersReducedMotion(matchMedia) {
    const query = matchMedia ?? (typeof globalThis !== 'undefined' ? globalThis.matchMedia : undefined);
    try {
        return query?.('(prefers-reduced-motion: reduce)')?.matches === true;
    } catch {
        return false;
    }
}

/**
 * The scroll behaviour for a programmatic scroll: `auto` (no animation) under reduced motion,
 * `smooth` otherwise.
 *
 * @param {boolean} reducedMotion
 * @returns {'auto' | 'smooth'}
 */
export function scrollBehavior(reducedMotion) {
    return reducedMotion ? 'auto' : 'smooth';
}

/**
 * The absolute scroll position that puts the field in the scroller's usable space.
 *
 * The usable space is the scroller's own viewport, cut at the sheet's top edge minus the gap
 * (a sticky bottom sheet covers the lower part of the space; when there is no sheet the whole
 * scroller viewport is usable). The field is CENTRED in that space, clamped so its bottom never
 * sits below the sheet top (the gap) and its top never above the scroller viewport — one number,
 * applied by a SINGLE `scrollTo` (the old centre-then-correct pair produced two jumps).
 *
 * @param {{
 *   top: number, bottom: number,
 * }} fieldRect viewport-space field rect (getBoundingClientRect)
 * @param {number} scrollerViewportTop viewport-space top edge of the scroller's visible area
 * @param {number} scrollerViewportHeight visible height of the scroller's viewport
 * @param {number} scrollTop the scroller's CURRENT scroll offset (window.scrollY / el.scrollTop)
 * @param {number | null | undefined} sheetTop viewport-space top edge of the sticky sheet
 *        (`null` — no sheet; the whole viewport is usable)
 * @param {number} [gap] breathing room below the sheet's top edge (px)
 * @returns {number} the target absolute scroll position, clamped to the scroller's range
 */
export function scrollTargetFor(fieldRect, scrollerViewportTop, scrollerViewportHeight, scrollTop, sheetTop, gap = SHEET_GAP_PX) {
    const spaceTop = scrollerViewportTop;
    const spaceBottom = sheetTop === null || sheetTop === undefined
        ? scrollerViewportTop + scrollerViewportHeight
        : Math.min(scrollerViewportTop + scrollerViewportHeight, sheetTop - gap);

    // delta = target - scrollTop (positive scrolls down). Constraints, in delta space:
    //   field bottom must not sink below spaceBottom → delta >= fieldRect.bottom - spaceBottom
    //   field top must not rise above spaceTop      → delta <= fieldRect.top    - spaceTop
    const deltaMin = fieldRect.bottom - spaceBottom;
    const deltaMax = fieldRect.top - spaceTop;
    const deltaCentre = (fieldRect.top + fieldRect.bottom) / 2 - (spaceTop + spaceBottom) / 2;
    const delta = Math.min(Math.max(deltaCentre, deltaMin), deltaMax);

    return scrollTop + delta;
}

/**
 * Clamps an absolute scroll target to a scroller's range. The window clamps at 0 (browsers own
 * the overscroll); an element scroller clamps at `0 … scrollHeight - clientHeight`.
 *
 * @param {number} target absolute scroll position
 * @param {number} maxScroll the scroller's maximum scroll offset
 * @returns {number}
 */
export function clampScrollTop(target, maxScroll) {
    const ceiling = Math.max(0, maxScroll);
    return Math.min(Math.max(0, target), ceiling);
}

/**
 * Scrolls the document overlay of `fieldUuid` into view with ONE `scrollTo`: the field ends up
 * centred in the space above the sticky mobile sheet (or centred in the scroller viewport when
 * the sheet is absent), so a landing never produces the centre-then-correct double jump. The
 * scroller is the field's nearest scrollable ancestor (a `overflow-y: auto/scroll` element that
 * actually overflows), falling back to the window. Reduced motion scrolls instantly.
 *
 * @param {string} fieldUuid the signing field's stable uuid (`data-field-uuid` on the overlay)
 * @param {{
 *   document?: Document,
 *   window?: Window,
 *   sheetSelector?: string,
 *   gap?: number,
 * }} [options] injectable environment for the node lane
 */
export function scrollToField(fieldUuid, options = {}) {
    const doc = options.document ?? (typeof document !== 'undefined' ? document : null);
    const win = options.window ?? (typeof window !== 'undefined' ? window : null);
    if (!doc || !win || !fieldUuid) {
        return;
    }

    let field = null;
    try {
        field = doc.querySelector(`.tm-signing-field[data-field-uuid="${cssEscape(fieldUuid)}"]`);
    } catch {
        return;
    }

    if (!field || typeof field.getBoundingClientRect !== 'function') {
        return;
    }

    const scroller = nearestScrollable(field, doc) ?? win;
    const sheet = doc.querySelector(options.sheetSelector ?? '.tm-signing-form-runner__steps');
    const behavior = scrollBehavior(prefersReducedMotion(win.matchMedia));

    scrollToTarget(scroller, scroller === win, field, rectOf(sheet), behavior, options.gap);
}

/**
 * Performs the single scroll for `field` inside `scroller` (the window or an element).
 *
 * @param {Window | Element} scroller
 * @param {boolean} isWindow whether `scroller` is the window (viewport metrics vs element metrics)
 * @param {Element} field
 * @param {{ top: number, left: number, right: number } | null} sheetRect viewport-space rect of
 *        the sheet (null = no sheet; the top only cuts the space when the rect horizontally
 *        overlaps the field and sits below the scroller viewport top)
 * @param {'auto' | 'smooth'} behavior
 * @param {number | undefined} gapOverride
 */
function scrollToTarget(scroller, isWindow, field, sheetRect, behavior, gapOverride) {
    const fieldRect = field.getBoundingClientRect();
    let viewportTop;
    let viewportHeight;
    let scrollTop;
    let maxScroll;
    if (isWindow) {
        viewportTop = 0;
        viewportHeight = typeof scroller.innerHeight === 'number' ? scroller.innerHeight : fieldRect.bottom + 1;
        scrollTop = typeof scroller.scrollY === 'number' ? scroller.scrollY : 0;
        const documentHeight = scroller.document?.documentElement?.scrollHeight ?? 0;
        maxScroll = Math.max(0, documentHeight - viewportHeight);
    } else {
        const rect = scroller.getBoundingClientRect();
        viewportTop = rect.top;
        viewportHeight = rect.height;
        scrollTop = scroller.scrollTop;
        maxScroll = scroller.scrollHeight - scroller.clientHeight;
    }

    // The sheet cuts the usable space only when it actually COVERS the field (phase 5 review):
    // a docked desktop rail sits BESIDE the document column (no horizontal overlap) and must not
    // lift the field, and a sheet scrolled past (its top at or above the scroller viewport top)
    // covers nothing the scroller can show. Everything else — the sticky mobile sheet over the
    // document — is the real ceiling.
    const sheetTop = sheetRect
        && typeof sheetRect.top === 'number'
        && typeof sheetRect.left === 'number'
        && typeof sheetRect.right === 'number'
        && fieldRect.left < sheetRect.right && fieldRect.right > sheetRect.left
        && sheetRect.top > viewportTop
        ? sheetRect.top
        : null;
    const target = clampScrollTop(
        scrollTargetFor(fieldRect, viewportTop, viewportHeight, scrollTop, sheetTop, gapOverride),
        maxScroll);

    if (typeof scroller.scrollTo === 'function') {
        scroller.scrollTo({ top: target, behavior });
    }
}

/**
 * The field's nearest scrollable ancestor: the closest element with a vertical overflow that
 * actually overflows (`overflow-y: auto/scroll` and `scrollHeight > clientHeight`). `null` —
 * the window scrolls. `doc` carries the `defaultView` whose `getComputedStyle` reads the
 * overflow styles (injectable for the node lane).
 *
 * @param {Element} element
 * @param {Document | null | undefined} [doc]
 * @returns {Element | null}
 */
export function nearestScrollable(element, doc) {
    const view = doc?.defaultView
        ?? (typeof document !== 'undefined' ? document.defaultView : null);
    let current = element.parentElement;
    while (current) {
        const style = styleOf(current, view);
        const overflowY = style?.overflowY;
        if ((overflowY === 'auto' || overflowY === 'scroll') && current.scrollHeight > current.clientHeight) {
            return current;
        }

        current = current.parentElement;
    }

    return null;
}

/**
 * @param {Element} element
 * @param {Window | null} view
 * @returns {{ overflowY?: string } | null}
 */
function styleOf(element, view) {
    try {
        const style = view?.getComputedStyle?.(element)
            ?? (typeof getComputedStyle === 'function' ? getComputedStyle(element) : null);
        return style ?? null;
    } catch {
        return null;
    }
}

/**
 * Reveals the first element matching `selector` (Signing redesign phase 6 review fix: after a
 * host-accepted send the runner reveals the phone step's OTP ROW — the input-only focus scroll
 * leaves the Verify action and the resend countdown clipped below the mobile sheet's visible
 * area). The element's nearest scrollable ancestor scrolls so the element's bottom sits at the
 * scrollport's usable bottom — UNDER the sticky action bar when one overlays it (a bare
 * `scrollIntoView({ block: 'nearest' })` no-ops once the focus scroll already showed the row's
 * top, and aligning to the scrollport bottom would hide the row behind that bar). The delta is
 * applied with ONE `scrollTop` assignment; an instant jump honours reduced motion for free.
 *
 * @param {string} selector CSS selector of the element to reveal
 * @param {{
 *   document?: Document,
 *   stickySelector?: string,
 * }} [options] injectable environment for the node lane; `stickySelector` names the sticky bar
 *   that covers the scrollport's bottom edge (resolved INSIDE the element's own runner scope, so
 *   a page with several runners measures its own bar)
 * @returns {boolean} whether a matching element was found and scrolled
 */
export function scrollIntoView(selector, options = {}) {
    const doc = options.document ?? (typeof document !== 'undefined' ? document : null);
    if (!doc || !selector) {
        return false;
    }

    let element = null;
    try {
        element = doc.querySelector(selector);
    } catch {
        // A malformed selector must disable the reveal, never throw into the circuit.
        return false;
    }

    if (!element) {
        return false;
    }

    const scroller = nearestScrollable(element, doc);
    if (!scroller) {
        // No scrollable ancestor actually overflows (the desktop panel with the row fully
        // visible): the browser-level fallback keeps the plain semantics.
        if (typeof element.scrollIntoView === 'function') {
            element.scrollIntoView({ block: 'nearest' });
            return true;
        }

        return false;
    }

    const scrollerRect = rectOf(scroller);
    const elementRect = rectOf(element);
    if (!scrollerRect || !elementRect) {
        return false;
    }

    // The scroller's own bottom PADDING sits below the sticky bar (the panel pads by a space
    // step), so the usable bottom ends at the CONTENT-box edge, not the border-box edge.
    const view = doc.defaultView;
    let padding = 0;
    try {
        padding = parseFloat(styleOf(scroller, view)?.paddingBottom ?? '0') || 0;
    } catch {
        padding = 0;
    }

    const contentBottom = scrollerRect.bottom - padding;
    let usableBottom = contentBottom;
    const scope = element.closest('.tm-signing-form-runner__step-panel')
        ?? element.closest('.tm-signing-form-runner__steps');
    const bar = scope?.querySelector(options.stickySelector ?? '.tm-signing-form-runner__actions') ?? null;
    const barRect = rectOf(bar);
    if (barRect && barRect.bottom >= contentBottom - 2) {
        // The sticky bar rides on the scrollport's bottom edge (±2px rounding): the usable space
        // ends at its top edge.
        usableBottom = barRect.top;
    }

    if (elementRect.bottom > usableBottom) {
        scroller.scrollTop += elementRect.bottom - usableBottom;
    } else if (elementRect.top < scrollerRect.top) {
        scroller.scrollTop -= scrollerRect.top - elementRect.top;
    }

    return true;
}

/**
 * Scrolls the field list's button for `fieldUuid` into view and focuses it (the More menu's
 * "Field list" entry hands focus back to the list's current row). `scrollIntoView` first, so the
 * focus never lands off-screen inside the sheet's own scroll area.
 *
 * @param {string} fieldUuid
 * @param {{
 *   document?: Document,
 *   window?: Window,
 *   buttonSelector?: string,
 * }} [options] injectable environment for the node lane
 */
export function focusFieldButton(fieldUuid, options = {}) {
    const doc = options.document ?? (typeof document !== 'undefined' ? document : null);
    if (!doc || !fieldUuid) {
        return;
    }

    let button = null;
    try {
        button = doc.querySelector(
            `${options.buttonSelector ?? '.tm-signing-form-runner__field-button'}[data-field-uuid="${cssEscape(fieldUuid)}"]`);
    } catch {
        return;
    }

    if (!button || typeof button.focus !== 'function') {
        return;
    }

    if (typeof button.scrollIntoView === 'function') {
        button.scrollIntoView({ block: 'nearest' });
    }

    button.focus();
}

/** @param {Element | null | undefined} element */
function rectOf(element) {
    return element && typeof element.getBoundingClientRect === 'function'
        ? element.getBoundingClientRect()
        : null;
}

/**
 * Minimal attribute-value escaping for the selector lookup. `CSS.escape` exists in every browser;
 * the fallback keeps a malformed uuid from throwing instead of just failing to match.
 *
 * @param {string} value
 * @returns {string}
 */
function cssEscape(value) {
    if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
        return CSS.escape(value);
    }

    return String(value).replace(/["\\\]]/g, '\\$&');
}
