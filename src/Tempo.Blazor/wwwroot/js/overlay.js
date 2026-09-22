// Shared floating-layer engine for TmOverlayPanel.
//
// Panels are rendered with `popover="manual"`: while closed they are display:none, and
// showPopover() puts them into the browser TOP LAYER — above every z-index stacking context and,
// crucially, outside every ancestor's overflow/transform clipping. A dropdown inside a modal body
// or a scrolling form column can no longer be clipped or scrolled away from its anchor.
//
// The top layer is also what makes fixed positioning exact here: a top-layer element's containing
// block is always the viewport, so getBoundingClientRect() coordinates map 1:1 onto top/left.
//
// FALLBACK — where the Popover API is missing, the panel is still position:fixed but an ancestor
// with transform/filter/perspective/contain/will-change becomes its containing block instead of
// the viewport (.tm-modal is exactly that — it animates in with transform: scale(...)). The
// fallback path therefore walks ancestors, subtracts that box's origin, and measures usable space
// against that box, mirroring what TmUserPicker.razor.js has always done.
//
// A module imported from the same URL is shared by every panel on the page, so all per-panel
// state lives in `tracked` and the scroll/resize/dismiss listeners are bound once for all of
// them. `tracked` is keyed by a STRING the component owns, not by the element: Blazor resolves an
// ElementReference through a document query, and by the time a closing panel is released its
// element is already detached — the query returns null, so an element-keyed map could never be
// cleaned up.
//
// resolvePlacement is pure math (rects in, position out) so the flip/shift rules are exercised by
// Node tests without a DOM — see wwwroot/js/__tests__/overlay.test.mjs.

const OPEN_CLASS = 'tm-overlay-panel--open';
const PLACEMENT_ATTR = 'data-tm-placement';
const FALLBACK_ATTR = 'data-tm-overlay-fallback';

// Elements the browser itself moves focus to on mousedown (mirrors tm-focus-trap.js's list).
// An outside pointerdown landing anywhere else leaves focus nowhere — or on a node the closing
// panel is about to destroy — so the anchor takes it back instead.
const FOCUSABLE = [
    'a[href]', 'button:not([disabled])', 'textarea:not([disabled])',
    'input:not([disabled]):not([type="hidden"])', 'select:not([disabled])',
    'audio[controls]', 'video[controls]', '[contenteditable]:not([contenteditable="false"])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

const tracked = new Map();
let listenersBound = false;
let resizeObserver = null;
let frame = 0;

const supportsPopover = typeof HTMLElement !== 'undefined'
    && typeof HTMLElement.prototype.showPopover === 'function';

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), Math.max(min, max));
}

const OPPOSITE = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' };

// ── Pure placement math ─────────────────────────────────────────────────────
// anchor: {top,left,right,bottom,width,height} viewport-space rect.
// size:   {width,height} measured panel size.
// options:{placement,align,offset,margin,flip,shift,viewWidth,viewHeight}
// Returns {x,y,side} — viewport-space coordinates and the resolved (post-flip) side.
export function resolvePlacement(anchor, size, options) {
    const placement = options.placement ?? 'bottom';
    const align = options.align ?? 'start';
    const offset = options.offset ?? 4;
    const margin = options.margin ?? 8;
    const flip = options.flip !== false;
    const shift = options.shift !== false;
    const viewW = options.viewWidth;
    const viewH = options.viewHeight;

    const room = {
        top: anchor.top - margin - offset,
        bottom: viewH - anchor.bottom - margin - offset,
        left: anchor.left - margin - offset,
        right: viewW - anchor.right - margin - offset,
    };

    let side = placement;
    if (flip) {
        const needMain = (side === 'top' || side === 'bottom') ? size.height : size.width;
        const opposite = OPPOSITE[side];
        // Flip only when the preferred side lacks room AND the opposite side is strictly better:
        // an equally cramped opposite side must not flip the panel away from where the anchor is.
        if (room[side] < needMain && room[opposite] > room[side]) {
            side = opposite;
        }
    }

    let x;
    let y;
    if (side === 'bottom' || side === 'top') {
        y = side === 'bottom' ? anchor.bottom + offset : anchor.top - offset - size.height;
        x = align === 'end' ? anchor.right - size.width
            : align === 'center' ? anchor.left + (anchor.width - size.width) / 2
                : anchor.left;
        if (shift) {
            x = clamp(x, margin, viewW - margin - size.width);
            // Safety clamp on the main axis too: a panel taller than the viewport must not escape
            // at the top. When the panel fits, this is a no-op (the resolved y already lies inside).
            y = clamp(y, margin, viewH - margin - size.height);
        }
    } else {
        x = side === 'right' ? anchor.right + offset : anchor.left - offset - size.width;
        y = align === 'end' ? anchor.bottom - size.height
            : align === 'center' ? anchor.top + (anchor.height - size.height) / 2
                : anchor.top;
        if (shift) {
            y = clamp(y, margin, viewH - margin - size.height);
            x = clamp(x, margin, viewW - margin - size.width);
        }
    }

    return { x, y, side };
}

// ── Pure visibility math ────────────────────────────────────────────────────
// True while any part of the anchor rect intersects the viewport. Exported so the Node tests
// can pin the edges; place() hides the panel when this goes false — a clamped panel hanging
// off an anchor that scrolled away reads as a detached artifact, not a positioned one.
export function anchorIntersectsViewport(anchor, viewWidth, viewHeight) {
    return anchor.bottom > 0
        && anchor.top < viewHeight
        && anchor.right > 0
        && anchor.left < viewWidth;
}

// ── Fallback containing-block walk (no Popover API) ─────────────────────────
function establishesContainingBlock(cs) {
    return (
        (cs.transform && cs.transform !== 'none') ||
        (cs.perspective && cs.perspective !== 'none') ||
        (cs.filter && cs.filter !== 'none') ||
        (cs.backdropFilter && cs.backdropFilter !== 'none') ||
        /transform|perspective|filter/.test(cs.willChange || '') ||
        /paint|layout|strict|content/.test(cs.contain || '')
    );
}

// The box a fixed descendant is positioned against: its PADDING box, hence the border correction.
// Null means the viewport.
function containingBlockOf(element) {
    let node = element.parentElement;
    while (node) {
        const cs = getComputedStyle(node);
        if (establishesContainingBlock(cs)) {
            const rect = node.getBoundingClientRect();
            return {
                top: rect.top + (parseFloat(cs.borderTopWidth) || 0),
                left: rect.left + (parseFloat(cs.borderLeftWidth) || 0),
                bottom: rect.bottom - (parseFloat(cs.borderBottomWidth) || 0),
                right: rect.right - (parseFloat(cs.borderRightWidth) || 0),
            };
        }
        node = node.parentElement;
    }
    return null;
}

// ── Placement driver ────────────────────────────────────────────────────────
function resolveAnchor(entry) {
    const { panel, anchor } = entry;
    if (anchor && anchor.isConnected) {
        return anchor;
    }
    // No (or stale) explicit anchor: the trigger-then-panel sibling layout is the convention of
    // every built-in consumer, so the previous element sibling is the natural anchor.
    const sibling = panel.previousElementSibling;
    return sibling ?? null;
}

function place(entry) {
    const { panel, options } = entry;
    if (!panel.isConnected) {
        release(entry.key);
        return;
    }

    const anchorEl = resolveAnchor(entry);
    if (!anchorEl) {
        // No live anchor to measure against — keep the panel where it is; the next render pass
        // (or close()) resolves it.
        return;
    }

    const anchorRect = anchorEl.getBoundingClientRect();

    // Measure the LAYOUT box, not getBoundingClientRect: a transform on the panel's open
    // animation scales the rect (e.g. .tm-popover__body grows from scale(0.95)) and place()
    // runs at t≈0 — the mis-measurement would then stick for the panel's whole open life.
    // offsetWidth/offsetHeight ignore transforms. translate-only openers (tm-fade-in) never
    // touched the measurement, but they don't mind the switch either.
    const panelWidth = panel.offsetWidth;
    const panelHeight = panel.offsetHeight;

    // matchAnchorWidth must feed the ALIGN math too, not just the inline width written below:
    // with align 'end'/'center' the x coordinate is width-dependent, so resolving against the
    // panel's natural width and only then overriding width would land the panel off by the
    // (naturalWidth − anchorWidth) delta.
    const effectiveWidth = options.matchAnchorWidth ? anchorRect.width : panelWidth;

    const viewW = window.innerWidth;
    const viewH = window.innerHeight;

    // Anchor scrolled fully out of the viewport: park the panel invisible rather than clamp it
    // against an edge — it reappears on the next pass once the anchor is back. ('' restores the
    // stylesheet value; hidePanel's cssText reset already covers the closed path.)
    panel.style.visibility = anchorIntersectsViewport(anchorRect, viewW, viewH) ? '' : 'hidden';

    // Top layer: containing block is the viewport. Fallback: nearest transformed-ish ancestor.
    const block = entry.usesPopover ? null : containingBlockOf(panel);
    const originTop = block ? block.top : 0;
    const originLeft = block ? block.left : 0;

    const result = resolvePlacement(anchorRect, { width: effectiveWidth, height: panelHeight }, {
        placement: options.placement,
        align: options.align,
        offset: options.offset,
        margin: options.margin,
        flip: options.flip,
        shift: options.shift,
        viewWidth: viewW,
        viewHeight: viewH,
    });

    panel.style.left = `${Math.round(result.x - originLeft)}px`;
    panel.style.top = `${Math.round(result.y - originTop)}px`;
    panel.setAttribute(PLACEMENT_ATTR, result.side);

    if (options.matchAnchorWidth) {
        panel.style.width = `${anchorRect.width}px`;
    } else {
        // The option may have flipped off while the panel is open (update()) — the inline width
        // written by an earlier pass would otherwise survive as a stale override of the CSS class.
        panel.style.width = '';
    }

    if (options.constrainHeight && (result.side === 'bottom' || result.side === 'top')) {
        const room = result.side === 'bottom'
            ? (block ? Math.min(block.bottom, window.innerHeight) : window.innerHeight) - anchorRect.bottom - options.offset - options.margin
            : anchorRect.top - (block ? Math.max(block.top, 0) : 0) - options.offset - options.margin;
        const cssCap = parseFloat(getComputedStyle(panel).maxHeight);
        const cap = Number.isNaN(cssCap) ? room : Math.min(room, cssCap);
        panel.style.maxHeight = `${Math.max(cap, 0)}px`;
        panel.style.overflowY = 'auto';
    } else {
        // Same staleness class as width: constrainHeight toggled off (or a left/right side, where
        // height capping does not apply) must not keep the previous pass's inline cap.
        panel.style.maxHeight = '';
        panel.style.overflowY = '';
    }
}

function placeAll() {
    frame = 0;
    for (const entry of [...tracked.values()]) {
        place(entry);
    }
}

function schedule() {
    if (frame) {
        return;
    }
    frame = requestAnimationFrame(placeAll);
}

// ── Dismissal ───────────────────────────────────────────────────────────────

// The release half of a consumed Escape must not reach host overlays: TmModal, TmDialog and
// TmKeyboardShortcutsHelp close on keyUP, so the same physical key that just shut the panel
// would otherwise shut its host a beat later. One suppressor covers a held/repeated Escape —
// it eats only the first Escape keyup it sees and disarms on window blur (a keyup lost to an
// alt-tab mid-press must not swallow the NEXT, unrelated Escape).
let escapeKeyUpSuppressor = null;

function suppressEscapeKeyUp() {
    if (escapeKeyUpSuppressor) {
        return;
    }
    const disarm = () => {
        window.removeEventListener('keyup', onKeyUp, { capture: true });
        window.removeEventListener('blur', disarm);
        clearTimeout(timeoutId);
        escapeKeyUpSuppressor = null;
    };
    const onKeyUp = e => {
        if (e.key !== 'Escape') {
            return; // a different key released mid-hold — keep waiting for ours
        }
        disarm();
        e.stopImmediatePropagation();
    };
    // A keyup the browser swallows natively (fullscreen/PiP exit consuming the physical Escape
    // without ever firing window blur) must not leave this armed forever — it would eat the
    // NEXT, unrelated Escape's keyup. 1000ms comfortably covers a real press-then-release of the
    // SAME key (the only pair this suppressor exists to eat) without surviving to the next one
    // (N163, review 2026-09-22).
    const timeoutId = setTimeout(disarm, 1000);
    escapeKeyUpSuppressor = onKeyUp;
    window.addEventListener('keyup', onKeyUp, { capture: true });
    window.addEventListener('blur', disarm, { once: true });
}

// Outside pointerdown: give focus back to the anchor ONLY when the pointerdown target cannot
// take focus itself. A click into a field/button earns its focus through the browser's own
// mousedown — pulling it back to the anchor would steal it (B1). For dead-space clicks the
// browser drops focus to <body> after our capture-phase run, so the restore is deferred past
// the mousedown and then applies only when focus truly went nowhere (or is still sitting
// inside the doomed panel).
function maybeRestoreAnchorFocus(entry, target) {
    if (target instanceof Element && target.closest(FOCUSABLE)) {
        return;
    }
    const anchorEl = resolveAnchor(entry);
    if (!anchorEl || typeof anchorEl.focus !== 'function') {
        return;
    }
    setTimeout(() => {
        const active = document.activeElement;
        const nowhere = !active
            || active === document.body
            || active === document.documentElement
            || !active.isConnected
            || entry.panel.contains(active);
        if (nowhere && anchorEl.isConnected) {
            anchorEl.focus({ preventScroll: true });
        }
    }, 0);
}

// Exported so the lifecycle unit tests can drive the veto path directly.
export function dismiss(entry, reason) {
    if (entry.dismissed) {
        return false;
    }
    entry.dismissed = true;
    if (reason === 'escape') {
        // Keyboard dismissal returns focus to the trigger: the panel node is about to be
        // removed by the .NET re-render and focus would otherwise drop to <body>.
        const anchorEl = resolveAnchor(entry);
        if (anchorEl && typeof anchorEl.focus === 'function') {
            anchorEl.focus({ preventScroll: true });
        }
    }
    // .NET flips IsOpen, which re-renders and runs close(key) — the panel element is hidden there,
    // so a slow or swallowed callback can never leave a ghost panel tracked forever. The callback
    // answers whether the dismissal was ACCEPTED: a consumer that vetoes it (controlled IsOpen
    // stays true) resolves false and the entry must be re-armed, or every later Escape is
    // silently dropped by the dismissed===true short-circuit above (N167, review 2026-09-22).
    Promise.resolve(entry.dotNetRef.invokeMethodAsync('NotifyDismissedAsync', reason))
        .then(accepted => {
            if (!accepted) {
                entry.dismissed = false;
            }
        })
        .catch(() => { entry.dismissed = false; });
    return true;
}

function onKeyDown(e) {
    if (e.key !== 'Escape') {
        return;
    }
    // Topmost = most recently opened eligible panel. Dismissed-but-still-tracked entries (the
    // .NET re-render hasn't caught up) are skipped so a quick second Escape peels the NEXT layer
    // instead of leaking through to the host under a still-visible panel.
    for (const entry of [...tracked.values()].reverse()) {
        if (entry.options.closeOnEscape && !entry.dismissed) {
            if (dismiss(entry, 'escape')) {
                // One gesture = one layer: the keydown is consumed (preventDefault marks it for
                // document-level handlers that honour the flag — tm-focus-trap's drawer escape —
                // and stops every remaining listener including element-level ones), and the
                // matching keyup is intercepted below for keyup-driven hosts (TmModal/TmDialog).
                e.preventDefault();
                e.stopImmediatePropagation();
                suppressEscapeKeyUp();
            }
            return;
        }
    }
}

function onPointerDown(e) {
    const target = e.target;
    let topmostDismissed = null;
    for (const entry of [...tracked.values()].reverse()) {
        if (!entry.options.closeOnOutsidePointerDown) {
            continue;
        }
        if (entry.panel.contains(target)) {
            continue;
        }
        // The anchor is exempt so a trigger's own click handler toggles instead of racing
        // close-then-reopen.
        const anchorEl = resolveAnchor(entry);
        if (anchorEl && typeof anchorEl.contains === 'function' && anchorEl.contains(target)) {
            continue;
        }
        if (dismiss(entry, 'outside')) {
            topmostDismissed ??= entry;
        }
    }
    if (topmostDismissed) {
        maybeRestoreAnchorFocus(topmostDismissed, target);
    }
}

function bindListeners() {
    if (listenersBound) {
        return;
    }
    // Capture phase: a scroll inside .tm-modal-body does not bubble to the window.
    window.addEventListener('scroll', schedule, { passive: true, capture: true });
    window.addEventListener('resize', schedule, { passive: true });
    window.addEventListener('keydown', onKeyDown, { capture: true });
    document.addEventListener('pointerdown', onPointerDown, { capture: true });
    listenersBound = true;
}

function unbindListeners() {
    if (!listenersBound || tracked.size > 0) {
        return;
    }
    window.removeEventListener('scroll', schedule, { capture: true });
    window.removeEventListener('resize', schedule);
    window.removeEventListener('keydown', onKeyDown, { capture: true });
    document.removeEventListener('pointerdown', onPointerDown, { capture: true });
    listenersBound = false;
}

// A panel that keeps its coordinates while its CONTENT changes size (async results arriving,
// accordion expanding) is silently misplaced; watching it keeps the anchor edge glued.
function ensureResizeObserver() {
    if (resizeObserver || typeof ResizeObserver === 'undefined') {
        return;
    }
    resizeObserver = new ResizeObserver(schedule);
}

function normalizeOptions(options) {
    return {
        placement: options?.placement ?? 'bottom',
        align: options?.align ?? 'start',
        offset: options?.offset ?? 4,
        margin: options?.margin ?? 8,
        flip: options?.flip !== false,
        shift: options?.shift !== false,
        matchAnchorWidth: options?.matchAnchorWidth === true,
        constrainHeight: options?.constrainHeight === true,
        closeOnEscape: options?.closeOnEscape !== false,
        closeOnOutsidePointerDown: options?.closeOnOutsidePointerDown !== false,
    };
}

function showPanel(entry) {
    const { panel } = entry;
    if (entry.usesPopover) {
        try {
            if (!panel.matches(':popover-open')) {
                panel.showPopover();
            }
        }
        catch {
            // InvalidStateError — element not connected / not a popover any more; the entry is
            // released by the next place() pass which checks isConnected.
        }
    }
    panel.classList.add(OPEN_CLASS);
    // Marker for tests/diagnostics: fallback mode = position:fixed + containing-block math instead
    // of the browser top layer.
    if (entry.usesPopover) {
        panel.removeAttribute(FALLBACK_ATTR);
    } else {
        panel.setAttribute(FALLBACK_ATTR, 'true');
    }
}

function hidePanel(entry) {
    const { panel } = entry;
    if (entry.usesPopover) {
        try {
            if (panel.matches(':popover-open')) {
                panel.hidePopover();
            }
        }
        catch {
            // Same guard as showPanel.
        }
    }
    panel.classList.remove(OPEN_CLASS);
    panel.style.cssText = '';
    panel.removeAttribute(PLACEMENT_ATTR);
    panel.removeAttribute(FALLBACK_ATTR);
}

// ── Public API (called from TmOverlayPanel) ─────────────────────────────────
export function open(key, panel, anchor, dotNetRef, options) {
    if (!key || !panel) {
        return;
    }
    const existing = tracked.get(key);
    if (existing) {
        // An orphaned entry being reopened carries a NEW panel element (Blazor's @if deleted the
        // old div and rendered a fresh one): stop observing the dead node and start observing the
        // live one, or size changes never re-place the panel and the dead node is held forever
        // (N164, review 2026-09-22).
        if (existing.panel !== panel) {
            resizeObserver?.unobserve(existing.panel);
            resizeObserver?.observe(panel);
        }
        existing.panel = panel;
        existing.anchor = anchor;
        existing.dotNetRef = dotNetRef;
        existing.options = normalizeOptions(options);
        existing.dismissed = false;
        // Restore stacking order: a reopened entry counts as freshly opened, not stuck wherever
        // it was first inserted — onKeyDown walks [...tracked.values()].reverse() (N164).
        tracked.delete(key);
        tracked.set(key, existing);
        showPanel(existing);
        place(existing);
        return;
    }

    const entry = {
        key,
        panel,
        anchor,
        dotNetRef,
        options: normalizeOptions(options),
        usesPopover: supportsPopover,
        dismissed: false,
    };
    tracked.set(key, entry);
    bindListeners();
    ensureResizeObserver();
    resizeObserver?.observe(panel);
    showPanel(entry);
    place(entry);
}

export function update(key, anchor, options) {
    const entry = tracked.get(key);
    if (!entry) {
        return;
    }
    entry.anchor = anchor ?? entry.anchor;
    entry.options = normalizeOptions(options);
    place(entry);
}

export function close(key) {
    release(key);
}

function release(key) {
    const entry = tracked.get(key);
    if (!entry) {
        return;
    }
    tracked.delete(key);
    resizeObserver?.unobserve(entry.panel);
    if (entry.panel.isConnected) {
        hidePanel(entry);
    }
    unbindListeners();
}
