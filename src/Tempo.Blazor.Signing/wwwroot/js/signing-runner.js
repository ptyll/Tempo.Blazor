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
 * @returns {Promise<void> | undefined} a promise when the scroll waits for the sheet's height
 *          transition; otherwise the scroll is synchronous
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

    // The drawer PANEL is the sheet that covers the field. The drawer root is a sticky 85dvh
    // frame whose top sits above the viewport, so measuring the root would not lift the field.
    const panel = doc.querySelector(options.sheetSelector ?? '.tm-signing-form-runner__steps .tm-drawer__panel');
    const view = doc.defaultView ?? win;
    const reduced = prefersReducedMotion(win.matchMedia ?? view?.matchMedia);
    const behavior = scrollBehavior(reduced);
    const perform = () => {
        // A panel whose top is above the scrollport has already left its sticky range. Bring it
        // back to the scrollport bottom before measuring, or the field scroll treats a covering
        // sheet as "no ceiling" and leaves the field under it.
        alignSheetCell(panel, doc, win);
        const activeScroller = scrollerForField(field, panel, doc, win);
        scrollToTarget(activeScroller, activeScroller === win, field, rectOf(panel), behavior, options.gap, panel);
    };

    // A height transition (the drawer snap) would report a mid-flight panel. Wait for it, unless
    // reduced motion already collapsed the duration — then the snap target is the ceiling.
    if (!reduced && heightTransitionPending(panel, view)) {
        return waitForHeightTransition(panel, win).then(perform);
    }

    perform();
}

/**
 * A panel that has scrolled above the scrollport (sticky released) still covers the field. Scroll
 * the nearest scroller so the panel's bottom sits on the scrollport bottom again — the sticky rest
 * position — before the field is measured against it.
 *
 * @param {Element | null | undefined} panel
 * @param {Document | null | undefined} doc
 * @param {Window} win
 */
/**
 * Puts the documents cell at the top of its scrollport so the anchored sheet (absolute, bottom of
 * that cell) sits on the viewport and the page band above it is on screen. A stub panel with no
 * host is left alone.
 *
 * @param {Element | null | undefined} panel
 * @param {Document | null | undefined} doc
 * @param {Window} win
 */
function alignSheetCell(panel, doc, win) {
    const cell = panel?.closest?.('.tm-signing-form-runner__documents');
    const host = panel?.closest?.('.tm-signing-form-runner__sheet-host');
    if (!cell || !host || typeof cell.getBoundingClientRect !== 'function') {
        restoreCoveringSheet(panel, doc, win);
        return;
    }

    const frameClipped = styleOf(cell, doc?.defaultView)?.overflowY === 'hidden';
    if (frameClipped) {
        return;
    }

    const scroller = nearestScrollable(cell, doc) ?? win;
    const viewportTop = scroller === win ? 0 : scroller.getBoundingClientRect().top;
    const delta = cell.getBoundingClientRect().top - viewportTop;
    if (Math.abs(delta) < 2) {
        return;
    }

    scrollBy(scroller, scroller === win, win, delta);
}

/**
 * @param {Window | Element} scroller
 * @param {boolean} isWindow
 * @param {Window} win
 * @param {number} delta
 */
function scrollBy(scroller, isWindow, win, delta) {
    if (isWindow && typeof win.scrollBy === 'function') {
        win.scrollBy({ top: delta, behavior: 'auto' });
        return;
    }

    if (typeof scroller.scrollBy === 'function') {
        scroller.scrollBy({ top: delta, behavior: 'auto' });
        return;
    }

    if (typeof scroller.scrollTop === 'number') {
        scroller.scrollTop += delta;
    }
}

function restoreCoveringSheet(panel, doc, win) {
    const rect = rectOf(panel);
    if (!rect || typeof rect.bottom !== 'number') {
        return;
    }

    const host = panel?.closest?.('.tm-signing-form-runner__sheet-host') ?? panel;
    const scroller = nearestScrollable(host, doc) ?? win;
    const isWindow = scroller === win;
    const viewportBottom = isWindow
        ? (typeof win.innerHeight === 'number' ? win.innerHeight : 0)
        : (scroller.getBoundingClientRect().top + scroller.getBoundingClientRect().height);
    const viewportTop = isWindow ? 0 : scroller.getBoundingClientRect().top;
    // Only a panel that covers this scrollport (top above it, bottom still inside) needs restoring.
    // A sheet that has scrolled entirely past does not cover the field.
    if (rect.top > viewportTop || rect.bottom <= viewportTop) {
        return;
    }

    const delta = rect.bottom - viewportBottom;
    if (delta >= -1) {
        return;
    }

    if (isWindow && typeof win.scrollBy === 'function') {
        win.scrollBy({ top: delta, behavior: 'auto' });
        return;
    }

    if (typeof scroller.scrollBy === 'function') {
        scroller.scrollBy({ top: delta, behavior: 'auto' });
        return;
    }

    if (typeof scroller.scrollTop === 'number') {
        scroller.scrollTop += delta;
    }
}

/**
 * Whether `panel` is currently animating `height` (a TmDrawer snap). A missing duration, `0s`,
 * or a transition that does not include height is not pending. Reduced motion is the caller's
 * concern — this only reports what the computed style says.
 *
 * @param {Element | null | undefined} panel
 * @param {Window | { getComputedStyle?: Function } | null | undefined} view
 * @returns {boolean}
 */
export function heightTransitionPending(panel, view) {
    if (!panel || typeof panel.addEventListener !== 'function') {
        return false;
    }

    const style = styleOf(panel, view);
    const duration = style?.transitionDuration;
    if (!duration || duration === '0s' || duration === '0ms') {
        return false;
    }

    const property = style?.transitionProperty || 'all';
    return property === 'all' || property.split(',').some(part => part.trim() === 'height');
}

/**
 * Resolves on the panel's height `transitionend`, or after a short timeout so a transition that
 * never fires cannot pin the scroll. Reduced-motion callers must not wait — they already skip this.
 *
 * @param {Element} panel
 * @param {Window | { setTimeout?: Function, clearTimeout?: Function }} win
 * @returns {Promise<void>}
 */
export function waitForHeightTransition(panel, win) {
    return new Promise(resolve => {
        let settled = false;
        let timer;
        const schedule = win?.setTimeout ?? ((fn, ms) => setTimeout(fn, ms));
        const clear = win?.clearTimeout ?? ((id) => clearTimeout(id));
        const finish = () => {
            if (settled) {
                return;
            }

            settled = true;
            panel.removeEventListener?.('transitionend', onEnd);
            if (timer !== undefined) {
                clear(timer);
            }

            resolve();
        };
        const onEnd = (event) => {
            if (event?.target === panel && (event.propertyName === 'height' || event.propertyName === 'max-height')) {
                finish();
            }
        };
        panel.addEventListener?.('transitionend', onEnd);
        timer = schedule(finish, 400);
    });
}

/**
 * The visible drawer panel's top. A measured top at or above the scroller is the 85dvh host (or a
 * panel the sticky range has already released), not the ceiling: the settled snap is
 * `fraction × host height` up from the host bottom, or from the scrollport bottom when the host
 * has scrolled away. A panel whose height is still the previous snap uses that same target so the
 * scroll does not land under the sheet the transition is opening.
 *
 * @param {Element | null | undefined} panel
 * @param {{ top: number, left?: number, right?: number, bottom?: number, height?: number } | null | undefined} measured
 * @param {number} viewportTop
 * @param {number} viewportBottom
 * @returns {{ top: number, left?: number, right?: number, bottom?: number } | null | undefined}
 */
export function resolveSheetCeiling(panel, measured, viewportTop, viewportBottom) {
    if (!measured || typeof measured.top !== 'number') {
        return measured ?? null;
    }

    const settledHeight = settledPanelHeight(panel);
    if (!(settledHeight > 0)) {
        return measured;
    }

    const measuredHeight = typeof measured.height === 'number'
        ? measured.height
        : (typeof measured.bottom === 'number' ? measured.bottom - measured.top : 0);
    const midSnap = measuredHeight > 0 && Math.abs(measuredHeight - settledHeight) > 2;
    const aboveViewport = measured.top <= viewportTop;
    if (!midSnap && !aboveViewport) {
        return measured;
    }

    const hostBottom = panel?.closest?.('.tm-signing-form-runner__sheet-host')?.getBoundingClientRect?.().bottom;
    let top = typeof hostBottom === 'number' && hostBottom > viewportTop
        ? hostBottom - settledHeight
        : viewportBottom - settledHeight;
    if (top <= viewportTop) {
        top = viewportBottom - settledHeight;
    }

    if (top <= viewportTop) {
        return measured;
    }

    return {
        top,
        left: measured.left,
        right: measured.right,
        bottom: top + settledHeight,
    };
}

/**
 * Settled panel height: the drawer's snap fraction times the sticky host height. `0` when the
 * panel is not a snapping signing sheet (a docked rail, a node-lane stub).
 *
 * @param {Element | null | undefined} panel
 * @returns {number}
 */
function settledPanelHeight(panel) {
    if (!panel || typeof panel.closest !== 'function') {
        return 0;
    }

    const drawer = panel.closest('.tm-drawer') ?? panel.parentElement;
    const snaps = String(drawer?.getAttribute?.('data-snap-points') ?? '')
        .split(',')
        .map(part => Number(part.trim()))
        .filter(value => Number.isFinite(value) && value > 0);
    const index = Number(drawer?.getAttribute?.('data-snap-index'));
    const fraction = snaps[index];
    if (!(fraction > 0)) {
        return 0;
    }

    const hostHeight = panel.closest('.tm-signing-form-runner__sheet-host')?.getBoundingClientRect?.().height;
    return hostHeight > 0 ? fraction * hostHeight : 0;
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
 * @param {Element | null | undefined} [panel] the drawer panel, so a host-top or mid-snap rect can
 *        be replaced by the settled snap ceiling
 */
function scrollToTarget(scroller, isWindow, field, sheetRect, behavior, gapOverride, panel) {
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

    sheetRect = resolveSheetCeiling(panel, sheetRect, viewportTop, viewportTop + viewportHeight) ?? sheetRect;

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
 * The scroller that moves the field against the sheet. A document overlay sits beside the sticky
 * sheet, so a nested overflow (the page viewer) must not steal the scroll — the sheet would stay
 * put and the field would remain under it. A step input inside the panel keeps its own scroller.
 *
 * @param {Element} field
 * @param {Element | null | undefined} panel
 * @param {Document | null | undefined} doc
 * @param {Window} win
 * @returns {Window | Element}
 */
function scrollerForField(field, panel, doc, win) {
    // A step input lives inside the panel: its own overflow is the right scroller. The panel
    // itself is not that case — restoring the sheet must move the scrollport it sticks to.
    if (panel && field !== panel && typeof panel.contains === 'function' && panel.contains(field)) {
        return nearestScrollable(field, doc) ?? win;
    }

    // Document overlay. Snap 1/2 keeps the pages wrapper as the stack (the per-page document is
    // not a flex scroller). When that wrapper itself overflows it is the scroller; otherwise the
    // window moves the field against the sticky sheet. A host without the wrapper keeps the
    // nearest overflowing ancestor.
    const pages = field.closest?.('.tm-signing-form-runner__pages');
    if (pages) {
        const overflowY = styleOf(pages, doc?.defaultView)?.overflowY;
        if ((overflowY === 'auto' || overflowY === 'scroll') && pages.scrollHeight > pages.clientHeight + 1) {
            return pages;
        }

        // A clipped documents frame hides a field the window cannot reveal. Scroll that frame's
        // pages wrapper anyway so the field stays inside it.
        const frame = pages.closest?.('.tm-signing-form-runner__documents');
        const frameOverflow = styleOf(frame, doc?.defaultView)?.overflowY;
        if (frame && (frameOverflow === 'hidden' || frameOverflow === 'clip' || frameOverflow === 'auto')) {
            return pages;
        }

        return win;
    }

    return nearestScrollable(field, doc) ?? win;
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
 * Focuses the first element matching `selector` in the whole document (phase 7 review: an editor
 * whose remove/move action rebuilds the rows must move the focus somewhere useful — the next row's
 * action — instead of dropping it to <body>). Returns whether the element really owns the focus
 * afterwards, so the caller can fall back to another target; a malformed selector must disable the
 * move, never throw into the circuit.
 *
 * The implementation lives in the editors' neutral module `signing-editor.js` (shared with
 * TmRecipientRoleEditor / TmConditionBuilder / TmFormulaBuilder through `SigningEditorInterop`);
 * the re-export below keeps this module's public surface (the node lane imports it from here).
 *
 * @param {string} selector the CSS selector of the element to focus
 * @param {{
 *   document?: Document,
 * }} [options] injectable environment for the node lane
 * @returns {boolean} whether the focus landed on the element
 */
export { focusElement } from './signing-editor.js';

/**
 * Attaches the shared two-finger pinch (pointer-gesture.js `attachPinch`) to the viewer viewport.
 * The commit is an ABSOLUTE scale (`baseScale * gesture.scale`), clamped by the caller. There is
 * no second pinch implementation: the helper module is injected (node tests) or loaded from the
 * core static-asset sibling.
 *
 * @param {Element} element the viewport that owns the page
 * @param {{
 *   getScale: () => number,
 *   onCommit: (scale: number) => void,
 *   attachPinch?: Function,
 * }} options
 * @returns {{ detach: () => void }}
 */
export function attachViewerPinch(element, options = {}) {
    const attach = options.attachPinch;
    if (!element || typeof attach !== 'function' || typeof options.onCommit !== 'function') {
        return { detach() { } };
    }

    let base = 1;
    const pinch = attach(element, {
        pointerTypes: ['touch'],
        wheel: true,
        touchAction: null,
        captureSinglePointer: false,
        onStart() {
            const current = Number(options.getScale?.());
            base = Number.isFinite(current) && current > 0 ? current : 1;
        },
        onEnd({ scale }) {
            const factor = Number.isFinite(scale) && scale > 0 ? scale : 1;
            options.onCommit(base * factor);
        },
    });

    return {
        detach() {
            pinch?.detach?.();
        },
    };
}

/**
 * Loads the core pinch helper and attaches it. Best-effort: a missing module leaves zoom buttons
 * working and never throws into the circuit.
 *
 * @param {Element} element the viewport that owns the page
 * @param {DotNetObject} dotNetRef receives <c>OnViewerPinchCommit</c>
 * @returns {Promise<{ detach: () => void }>} the gesture handle; call <c>detach</c> before dropping it
 */
export async function enableViewerPinch(element, dotNetRef) {
    if (!element) {
        return { detach() { } };
    }

    try {
        const helper = await import(new URL('../../Tempo.Blazor/js/pointer-gesture.js', import.meta.url).href);
        return attachViewerPinch(element, {
            attachPinch: helper.attachPinch,
            getScale() {
                const page = element.querySelector?.('.tm-document-page-viewer__page') ?? element;
                const raw = getComputedStyle(page).getPropertyValue('--tm-document-page-scale');
                const parsed = Number.parseFloat(raw);
                return Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
            },
            onCommit(scale) {
                dotNetRef?.invokeMethodAsync?.('OnViewerPinchCommit', scale)?.catch?.(() => { });
            },
        });
    } catch {
        return { detach() { } };
    }
}

/**
 * Scrolls the field list's button for `fieldUuid` into view and focuses it (the More menu's
 * "Field list" entry hands focus back to the list's current row). `scrollIntoView` first, so the
 * focus never lands off-screen inside the sheet's own scroll area.
 *
 * @param {string} fieldUuid
 * @param {{
 *   document?: Document,
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
