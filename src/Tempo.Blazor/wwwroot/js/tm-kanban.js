// Tempo.Blazor kanban board (ES module) — the TOUCH path of TmKanbanBoard.
//
//   import { attachKanbanBoard, detachKanbanBoard } from './tm-kanban.js';
//   attachKanbanBoard(root, dotnet, id);
//   detachKanbanBoard(id);       // on dispose / when the board turns Disabled
//
// Composition, never a second drag: the gesture comes from the shared pointer-drag.js helper —
// pointer capture by pointerId, the 400 ms touch long-press (a tap and a page scroll keep working;
// moving beyond the tolerance before the hold completes is a scroll, not a drag), Escape /
// pointercancel / lostpointercapture / a second finger rollback, the context-menu and click
// swallowing — and the edge auto-scroll from pointer-gesture.js createAutoScroller (as tm-sortable.js
// and gantt.js already do). This module owns only the BOARD-specific part: which card is dragged,
// which (column, lane) zone the pointer hovers, where the insertion line sits, and the ONE commit (or
// the cancel announcement) reported to .NET on release.
//
// Rules that keep it honest (mirroring tm-sortable.js):
//   * Blazor is told exactly ONCE per gesture (on release, or on cancel). Nothing here writes
//     component state on move — the dragged card, the hovered zone and the insertion line are painted
//     with inline styles and classes only, so a board of hundreds of cards never re-renders on a
//     pointer event and the captured card's DOM node stays alive for the whole gesture.
//   * Cancel paths restore every inline style and report the cancel, so a rolled-back drag can never
//     move a card — and the reader HEARS that it rolled back.
//   * The report carries (column, lane, index) triples, never item payloads: the board resolves the
//     item from the host's unchanged list, so the touch drop is the same KanbanMoveEvent an HTML5
//     drop reports. The SOURCE triple is double-checked at release against the card's STABLE key
//     (its index in the host's full list): a host refresh that moved the list under the gesture
//     cancels the drop instead of moving the wrong card. The TARGET reports the FULL-LIST index of
//     the card before which the drop lands (the hovered card's own data-tm-kanban-index, -1 =
//     append), so a VirtualizeCards cell — whose DOM holds only the visible window — reads the same
//     slot as a fully-rendered one. Lane encoding: "" / absent = classic layout, TmKanbanBoard.
//     NoSwimlaneId ("__tm_kanban_no_swimlane__") = the "no value" lane.
//   * Nothing here is a keyboard path: TmKanbanBoard implements that in Blazor, and the pointer
//     gesture has the tap alternative (the per-card "Move…" menu, WCAG 2.5.7).

import { attach } from './pointer-drag.js';
import { createAutoScroller } from './pointer-gesture.js';

const CARD_SELECTOR = '[data-tm-kanban-card]';
const ZONE_SELECTOR = '[data-tm-kanban-column]';
const CARDS_CONTAINER_SELECTOR = '.tm-kanban__cards';
const SLOT_CLASS = 'tm-kanban__lane-slot';
const COLLAPSED_CLASS = 'tm-kanban__column--collapsed';
const SLOT_DROP_TARGET_CLASS = 'tm-kanban__lane-slot--drop-target';
const COLUMN_DROP_TARGET_CLASS = 'tm-kanban__column--drop-target';
const CELL_TOUCH_TARGET_CLASS = 'tm-kanban__cell--touch-target';
const ROOT_DRAGGING_CLASS = 'tm-kanban--dragging';
const CARD_DRAGGING_CLASS = 'tm-kanban__card--touch-dragging';
const INDICATOR_CLASS = 'tm-kanban__touch-indicator';

/** Touch presses on a card must be held this long before they become a drag (ms). */
export const TOUCH_LONG_PRESS_MS = 400;

/** How long the release report may take before the paint is restored anyway (ms). */
export const REPORT_TIMEOUT_MS = 1000;

/**
 * Which zone rect contains the pointer. Pure: the caller passes the zone boxes in DOM order and the
 * pointer's viewport position, and gets the index of the first containing box or -1 (the caller then
 * keeps the zone it had, so a pointer in the gap between two columns never invents a target).
 * @param {{top: number, left: number, right: number, bottom: number}[]} rects the zone boxes
 * @param {number} x the pointer's viewport X
 * @param {number} y the pointer's viewport Y
 * @returns {number} the index of the containing zone, or -1
 */
export function resolveZoneIndex(rects, x, y) {
    if (!Array.isArray(rects) || rects.length === 0) return -1;
    for (let index = 0; index < rects.length; index++) {
        const rect = rects[index];
        if (!rect) continue;
        if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom) return index;
    }
    return -1;
}

/**
 * The insertion slot a dropped card belongs at, given the OTHER cards' boxes of the target cell and
 * the pointer's viewport Y. The midpoint rule (the convention every file manager uses): the card is
 * inserted before the first other card whose centre the pointer has not passed; the box count means
 * "after the last card". The dragged card is EXCLUDED from the boxes — it travels with the pointer
 * and must not count itself into the decision.
 * @param {{top: number, height: number}[]} boxes the other cards' boxes in DOM order
 * @param {number} pointerY the pointer's viewport Y
 * @returns {number} the insertion index in 0..boxes.length
 */
export function resolveInsertIndex(boxes, pointerY) {
    if (!Array.isArray(boxes) || boxes.length === 0) return 0;
    for (let index = 0; index < boxes.length; index++) {
        const box = boxes[index];
        if (pointerY < box.top + box.height / 2) return index;
    }
    return boxes.length;
}

/**
 * Where the insertion line sits for one target zone. Pure: given the zone box, the other cards'
 * boxes and the resolved insertion index, returns the line's viewport rectangle — the top edge of
 * the card the drop would insert before (inset by the card row gap look), the zone's bottom edge
 * when appending. A zone WITHOUT cards (a collapsed strip, an empty or collapsed cell) has no line:
 * the zone outline is the whole feedback, exactly like the HTML5 path.
 * @param {{top: number, left: number, right: number, bottom: number}} zoneRect the zone box
 * @param {{top: number, height: number}[]} boxes the other cards' boxes in DOM order
 * @param {number} insertIndex the resolved insertion index in 0..boxes.length
 * @returns {{top: number, left: number, width: number}|null} the line position, or null when the zone carries no line
 */
export function resolveIndicatorPosition(zoneRect, boxes, insertIndex) {
    if (!zoneRect || !Array.isArray(boxes) || boxes.length === 0) return null;
    const first = boxes[0];
    const last = boxes[boxes.length - 1];
    const left = first.left;
    const right = first.right ?? zoneRect.right;
    if (insertIndex >= boxes.length) {
        return { top: last.top + last.height, left, width: Math.max(0, right - left) };
    }
    return { top: boxes[insertIndex].top, left, width: Math.max(0, right - left) };
}

const registrations = new Map();

/**
 * Attaches the touch drag to a kanban board root. Attaching again with the same id replaces the
 * previous registration, so a component that flips its Disabled state never leaves a stale session.
 * @param {Element|null} root the .tm-kanban element
 * @param {{invokeMethodAsync: Function}|null} dotnet receives OnKanbanTouchDrop(fromColumn, fromLane,
 *        fromIndex, fromKey, toColumn, toLane, beforeIndex) and
 *        OnKanbanTouchDragCancelled(fromColumn, fromLane, fromIndex, fromKey)
 * @param {string} id registration key (one per component instance)
 */
export function attachKanbanBoard(root, dotnet, id) {
    if (!root || typeof root.querySelectorAll !== 'function') return;
    detachKanbanBoard(id);

    /**
     * @type {null|{card: Element, ghost: Element|null, startRect: {left: number, top: number, width: number},
     *        key: number, fromColumn: string, fromLane: string|null, fromIndex: number}}
     */
    let active = null;
    let indicator = null;
    // The last pointer type that touched the board: HTML5 dragstart must stay the MOUSE path, so a
    // dragstart that follows a touch press (or fires while a touch session owns the gesture) is
    // cancelled before the card's draggable="true" starts a native drag.
    let lastPointerType = 'mouse';
    // The last known pointer position — fed to the auto-scrollers and re-used by their onScroll to
    // re-run the zone/indicator hit test (the content moved under a stationary pointer).
    let pointerX = 0;
    let pointerY = 0;

    const invoke = (name, ...args) => {
        // The circuit may already be gone (dispose races the cancel report): a rejected invoke —
        // or a synchronously thrown disposed proxy — must never become an unhandled rejection.
        try {
            dotnet?.invokeMethodAsync(name, ...args)?.catch?.(() => {});
        }
        catch { /* the .NET reference is already disposed */ }
    };

    /**
     * The release report, AWAITED: the ghost stays under the finger until the commit (or the cancel)
     * settles on the .NET side, so on Blazor Server the card does not visibly snap back between the
     * release and the server's round trip. A timeout bounds the wait — a lost circuit must not hold
     * the paint forever.
     * @param {string} name the invokable name
     * @param {...*} args the report arguments
     * @returns {Promise<void>} settles when .NET answered or the timeout elapsed
     */
    const report = (name, ...args) => {
        let pending;
        try {
            pending = dotnet?.invokeMethodAsync?.(name, ...args);
        }
        catch { /* the .NET reference is already disposed */ }
        if (!pending || typeof pending.then !== 'function') return Promise.resolve();
        const timeout = new Promise((resolve) => {
            const timer = setTimeout(resolve, REPORT_TIMEOUT_MS);
            timer?.unref?.();
        });
        return Promise.race([pending.catch?.(() => {}) ?? pending, timeout]);
    };

    /**
     * Lifts the card: the source stays IN THE FLOW, invisibly (its siblings never jump), and a
     * fixed-position ghost rides the pointer. A plain inline transform cannot do this — the card's
     * own `.tm-kanban__cards` container clips at its bounds (overflow-y: auto computes overflow-x
     * to auto), so a card translated out of its column would vanish mid-drag. The ghost is
     * pointer-events: none, inert and anonymous (its subtree's id / data-testid stripped), so the
     * zone hit-test sees straight through it and no test or label can address the clone.
     */
    const liftCard = () => {
        if (!active || active.ghost || typeof document === 'undefined') return;
        const rect = active.card.getBoundingClientRect?.();
        if (!rect) return;
        const ghost = active.card.cloneNode?.(true);
        if (!ghost) return;
        ghost.classList?.add(CARD_DRAGGING_CLASS);
        ghost.setAttribute?.('aria-hidden', 'true');
        ghost.inert = true;
        ghost.removeAttribute?.('id');
        ghost.removeAttribute?.('data-testid');
        ghost.querySelectorAll?.('[id], [data-testid]')
            ?.forEach?.((node) => {
                node.removeAttribute?.('id');
                node.removeAttribute?.('data-testid');
            });
        if (ghost.style) {
            ghost.style.position = 'fixed';
            ghost.style.left = `${rect.left}px`;
            ghost.style.top = `${rect.top}px`;
            ghost.style.width = `${rect.width}px`;
            ghost.style.margin = '0';
            ghost.style.pointerEvents = 'none';
            // The z-index is the stylesheet's job (.tm-kanban__card--touch-dragging reads the
            // --tm-z-* token) — the clone would not see a --tm-kanban-* knob anyway, appended to
            // <body> it inherits nothing from the board root.
        }

        document.body?.appendChild?.(ghost);
        active.startRect = { left: rect.left, top: rect.top, width: rect.width };
        active.ghost = ghost;
        active.card.style.visibility = 'hidden';
    };

    /**
     * Restores the paint of one session. The release path calls this AFTER the report settles (via
     * settle), when onRelease has already cleared `active` — so the session is passed explicitly.
     * @param {{ghost: Element|null, card: Element}} session the gesture to clean up
     * @param {boolean} full also clear the board-level paint (root class, zone highlights, indicator)
     */
    const restore = (session, full) => {
        session?.ghost?.remove?.();
        if (session?.card?.style) session.card.style.visibility = '';
        if (!full) return;
        root.classList?.remove(ROOT_DRAGGING_CLASS);
        root.querySelectorAll?.(`.${SLOT_DROP_TARGET_CLASS}, .${COLUMN_DROP_TARGET_CLASS}, .${CELL_TOUCH_TARGET_CLASS}`)
            .forEach((zone) => zone.classList.remove(
                SLOT_DROP_TARGET_CLASS, COLUMN_DROP_TARGET_CLASS, CELL_TOUCH_TARGET_CLASS));
        indicator?.remove?.();
        indicator = null;
    };

    /** Clears every painted state of the gesture — ghost, classes, the indicator, the scrollers. */
    const reset = () => {
        restore(active, true);
        if (active) active.ghost = null;
        stopScrollers();
    };

    const ensureIndicator = () => {
        if (indicator?.isConnected) return indicator;
        indicator = document.createElement('div');
        indicator.className = INDICATOR_CLASS;
        indicator.setAttribute('aria-hidden', 'true');
        document.body?.appendChild(indicator);
        return indicator;
    };

    const dropIndicator = () => {
        indicator?.remove?.();
        indicator = null;
    };

    const zoneBoxes = (zone) => {
        const rect = zone.getBoundingClientRect?.();
        if (!rect) return null;
        return { top: rect.top, left: rect.left, right: rect.right, bottom: rect.bottom };
    };

    /** The OTHER cards of a zone as ELEMENTS, in DOM order — the dragged card excluded. Keeping the
     *  elements (not only their boxes) is what lets onEnd read the hovered card's data-tm-kanban-
     *  index: the FULL-LIST index inside its cell, identical for a fully-rendered cell and for a
     *  <Virtualize>d window that renders only part of the list. */
    const otherCards = (zone) => [...zone.querySelectorAll(CARD_SELECTOR)]
        .filter((card) => card !== active?.card);

    const cardBoxes = (zone) => otherCards(zone).map((card) => {
        const rect = card.getBoundingClientRect();
        return { top: rect.top, height: rect.height, left: rect.left, right: rect.right };
    });

    const highlightClassFor = (zone) => {
        if (zone.classList?.contains(SLOT_CLASS)) return SLOT_DROP_TARGET_CLASS;
        if (zone.classList?.contains(COLLAPSED_CLASS)) return COLUMN_DROP_TARGET_CLASS;
        return CELL_TOUCH_TARGET_CLASS;
    };

    const paint = () => {
        if (!active) return;
        root.classList?.add(ROOT_DRAGGING_CLASS);
        liftCard();
    };

    /** The zone under the pointer (capture routes every event to the card, so hit-test by point).
     *  The source card is already visibility:hidden and the ghost pointer-events:none, so the
     *  lookup sees the zone straight through the drag. */
    const zoneAt = (x, y) => {
        const hit = document.elementFromPoint?.(x, y);
        const zone = hit?.closest?.(ZONE_SELECTOR) ?? null;
        return zone && root.contains(zone) ? zone : null;
    };

    // Edge auto-scroll, as tm-sortable.js / gantt.js compose it: the board root scrolls
    // HORIZONTALLY (overflow-x: auto) and the hovered cell's card list scrolls VERTICALLY
    // (overflow-y: auto) — the vertical scroller swaps when the hovered zone changes.
    const rootScroller = createAutoScroller(root, { axis: 'x', onScroll: hitTest });
    let zoneScroller = null;
    let zoneScrollerFor = null;

    function stopScrollers() {
        try { rootScroller?.stop?.(); } catch { /* ignore */ }
        try { zoneScroller?.stop?.(); } catch { /* ignore */ }
        zoneScroller = null;
        zoneScrollerFor = null;
    }

    /** The hovered cell's card list owns the VERTICAL edge scroll; it swaps on every zone change. */
    const ensureZoneScroller = (zone) => {
        const cards = zone?.querySelector?.(CARDS_CONTAINER_SELECTOR) ?? null;
        if (cards === zoneScrollerFor) return;
        try { zoneScroller?.stop?.(); } catch { /* ignore */ }
        zoneScroller = cards ? createAutoScroller(cards, { axis: 'y', onScroll: hitTest }) : null;
        zoneScrollerFor = cards;
        // The fresh scroller adopts the current pointer — a horizontal scrolled frame can swap the
        // hovered column WITHOUT a pointermove, so onMove alone cannot feed it.
        zoneScroller?.update?.(pointerX, pointerY);
    };

    /**
     * The zone/indicator paint for the CURRENT pointer position. Runs from onMove AND from the
     * scrollers' onScroll: an auto-scroll moves the content under a stationary pointer, so the
     * landing zone and the insertion line are re-resolved after every scrolled frame. It never
     * feeds the scrollers — they hold the pointer from onMove and re-arm their own loop.
     */
    function hitTest() {
        if (!active) return;
        const zone = zoneAt(pointerX, pointerY);
        ensureZoneScroller(zone);

        root.querySelectorAll?.(`.${SLOT_DROP_TARGET_CLASS}, .${COLUMN_DROP_TARGET_CLASS}, .${CELL_TOUCH_TARGET_CLASS}`)
            .forEach((marked) => {
                if (marked !== zone) marked.classList.remove(
                    SLOT_DROP_TARGET_CLASS, COLUMN_DROP_TARGET_CLASS, CELL_TOUCH_TARGET_CLASS);
            });

        if (!zone) {
            dropIndicator();
            return;
        }

        zone.classList?.add(highlightClassFor(zone));

        const boxes = cardBoxes(zone);
        const insertIndex = resolveInsertIndex(boxes, pointerY);
        const position = resolveIndicatorPosition(zoneBoxes(zone), boxes, insertIndex);
        if (position && position.width > 0) {
            const line = ensureIndicator();
            line.style.top = `${position.top - 1}px`;
            line.style.left = `${position.left}px`;
            line.style.width = `${position.width}px`;
            line.style.display = '';
        } else {
            dropIndicator();
        }
    }

    /**
     * The release cleanup: the ghost stays until the commit (or the cancel) report settles on the
     * .NET side — on Blazor Server the invoke resolves only after the move applied and re-rendered,
     * and resetting earlier would snap the card back for the length of the round trip. onRelease has
     * already cleared `active` by then, so the session arrives captured; if a NEW gesture started
     * during the wait it owns the board paint and only this session's ghost/visibility are touched.
     * @param {object} source the settled session
     */
    const settle = (source) => {
        const ownsBoard = !active;
        restore(source, ownsBoard);
        if (ownsBoard) stopScrollers();
    };

    const drag = attach(root, {
        selector: CARD_SELECTOR,
        pointerTypes: ['touch'],
        threshold: 4,
        longPressMs: TOUCH_LONG_PRESS_MS,
        // The card keeps native scrolling until the hold completes — touch-action stays untouched.
        touchAction: null,
        // The module paints its own classes; the shared drag class would fight Blazor's class diff.
        dragClass: null,
        onPress(info) {
            const card = info.target?.closest?.(CARD_SELECTOR) ?? null;
            const zone = card?.closest?.(ZONE_SELECTOR) ?? null;
            if (!card || !zone || !root.contains(zone)) return false;
            const fromIndex = Number.parseInt(card.getAttribute('data-tm-kanban-index') ?? '-1', 10);
            if (fromIndex < 0) return false;
            // The STABLE identity: the card's index in the host's full list. The cell index can lie
            // after a host refresh; the key lets the board verify the source at release.
            const key = Number.parseInt(card.getAttribute('data-tm-kanban-key') ?? '-1', 10);
            const lane = zone.getAttribute('data-tm-kanban-lane');
            // No paint here: a press is a long-press CANDIDATE (a tap or a scroll must stay untouched),
            // so the card only lifts in onStart, once the hold completed.
            active = {
                card,
                key,
                fromColumn: zone.getAttribute('data-tm-kanban-column') ?? '',
                fromLane: lane ? lane : null,
                fromIndex,
            };
            return true;
        },
        onStart() {
            paint();
        },
        onMove(info) {
            if (!active) return;
            pointerX = info.x;
            pointerY = info.y;
            paint();
            if (active.ghost?.style && active.startRect) {
                active.ghost.style.left = `${active.startRect.left + info.dx}px`;
                active.ghost.style.top = `${active.startRect.top + info.dy}px`;
            }

            hitTest();
            // Feed the edge auto-scrollers: the root scrolls horizontally, the hovered cell's card
            // list vertically (swapped inside hitTest). Both keep looping until stop() in reset().
            rootScroller?.update?.(pointerX, pointerY);
            zoneScroller?.update?.(pointerX, pointerY);
        },
        async onEnd(info) {
            if (!active) return;
            const source = active;
            const zone = zoneAt(info.x, info.y);
            if (!zone) {
                await report('OnKanbanTouchDragCancelled',
                    source.fromColumn, source.fromLane, source.fromIndex, source.key);
                settle(source);
                return;
            }
            const lane = zone.getAttribute('data-tm-kanban-lane');
            const toColumn = zone.getAttribute('data-tm-kanban-column') ?? '';
            const toLane = lane ? lane : null;
            // The FULL-LIST index of the card before which the drop lands (its own data-tm-kanban-
            // index), -1 = append. Resolving the anchor by identity instead of by visible-window
            // position keeps a VirtualizeCards cell honest — its DOM renders only the window.
            const others = otherCards(zone);
            const insertIndex = resolveInsertIndex(
                others.map((card) => {
                    const rect = card.getBoundingClientRect();
                    return { top: rect.top, height: rect.height };
                }),
                info.y);
            const beforeIndex = insertIndex < others.length
                ? Number.parseInt(others[insertIndex].getAttribute('data-tm-kanban-index') ?? '-1', 10)
                : -1;
            await report('OnKanbanTouchDrop',
                source.fromColumn, source.fromLane, source.fromIndex, source.key,
                toColumn, toLane, beforeIndex);
            settle(source);
        },
        onCancel() {
            if (!active) return;
            const source = active;
            reset();
            invoke('OnKanbanTouchDragCancelled',
                source.fromColumn, source.fromLane, source.fromIndex, source.key);
        },
        onRelease() {
            active = null;
        },
    });

    // HTML5 dragstart stays the MOUSE path: on a touch press (or while a touch session owns the
    // gesture) the card's draggable="true" would otherwise surface a native drag — blocked here, in
    // the capture phase, before the card's own dragstart handler runs. Both listeners leave every
    // other pointer (and the mouse drag) untouched.
    const onRootPointerDown = (event) => {
        lastPointerType = event.pointerType ?? 'mouse';
    };
    const onRootDragStart = (event) => {
        if (lastPointerType === 'touch' || active) {
            event.preventDefault?.();
            event.stopPropagation?.();
        }
    };
    root.addEventListener?.('pointerdown', onRootPointerDown, true);
    root.addEventListener?.('dragstart', onRootDragStart, true);

    registrations.set(id, { root, drag, onRootPointerDown, onRootDragStart });
}

/** Removes the registration under the id. Safe to call twice or for an unknown id. */
export function detachKanbanBoard(id) {
    const entry = registrations.get(id);
    if (!entry) return;
    registrations.delete(id);
    entry.drag?.cancel?.('detached');
    entry.drag?.detach?.();
    entry.root?.removeEventListener?.('pointerdown', entry.onRootPointerDown, true);
    entry.root?.removeEventListener?.('dragstart', entry.onRootDragStart, true);
    entry.root?.classList?.remove(ROOT_DRAGGING_CLASS);
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...registrations.keys()]) detachKanbanBoard(id);
}
