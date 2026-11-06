// Tempo.Blazor sortable list (ES module) — the pointer path of TmSortableList.
//
//   import { attachSortable, detachSortable } from './tm-sortable.js';
//   attachSortable(root, dotnet, id, 'handle' | 'whole-item', { group, listId });
//   detachSortable(id);          // on dispose / when the list turns read-only
//
// Composition, never a second drag: the gesture comes from the shared pointer-drag.js helper (pointer
// capture by pointerId, threshold, the touch long-press, Escape / pointercancel / lostpointercapture
// rollback, click swallowing) and the edge scroll from pointer-gesture.js createAutoScroller. This module
// owns only the LIST-specific part: which child is the dragged row, where it would land, how the siblings
// shift out of the way, and the ONE commit reported to .NET on release.
//
// GROUPS. Two lists that pass the same non-empty `group` form one drag group: a row dragged out of one of
// them follows the pointer into another member and is reported as a TRANSFER (OnSortableTransferCommitted)
// instead of a reorder. Membership comes from this module's own registry, never from a DOM query, so a
// group cannot pick up a list that belongs to another component instance.
//
// Rules that keep it honest:
//   * Blazor is told exactly ONCE per gesture (on release). Nothing here writes component state on move —
//     the dragged row and its siblings move with inline transforms only, so a 200-row list does not
//     re-render on every pointer event.
//   * Cancel paths restore every inline style VERBATIM and report nothing, so a rolled-back drag can
//     never reorder (or transfer) anything.
//   * A committed drop reports the SOURCE indexes of the row and its landing slot. The component owns the
//     list (it never mutates the host's Items by itself), so the module never assumes the DOM order it
//     saw will be the order after the commit.
//   * Nothing here is a keyboard path: TmSortableList implements that in Blazor, and every pointer gesture
//     therefore has a tap alternative (the per-row move menu, WCAG 2.5.7). The ONE keyboard touch is the
//     page-scroll guard of a grabbed row (shouldPreventGrabbedKey): it stops the default action of the
//     reorder keys on the row itself, never Tab and never a key aimed at a control inside the row.

import { attach } from './pointer-drag.js';
import { createAutoScroller } from './pointer-gesture.js';

const ITEM_SELECTOR = '[data-tm-sortable-item]';
const HANDLE_SELECTOR = '[data-tm-sortable-handle]';
const DRAGGING_CLASS = 'tm-sortable-list__item--dragging';
const DROP_TARGET_CLASS = 'tm-sortable-list--drop-target';
const DRAG_ORIGIN_ATTR = 'data-sortable-source-index';
const GRABBED_ATTR = 'data-grabbed';

/** Touch presses on a whole row must be held this long before they become a drag (ms). */
const WHOLE_ITEM_LONG_PRESS_MS = 400;

/**
 * The keys a GRABBED row owns on keydown. Tab is deliberately absent: the browser must still move the
 * focus out of the row, or the grab becomes the keyboard trap WCAG 2.1.2 forbids — so Tab cancels the
 * grab in Blazor and its default action is left alone.
 */
const GRABBED_KEYS = new Set(['ArrowUp', 'ArrowDown', 'Home', 'End', 'Enter', ' ', 'Spacebar', 'Escape']);

/**
 * Whether a keydown must have its default action (page scroll, button activation) stopped, because it
 * targets a GRABBED row. Pure and exported so the decision is node-tested without a DOM.
 * A key that originates INSIDE the row's content (an input, a link in ItemTemplate) belongs to that
 * control: its event target is the control, not the row, so it is never swallowed.
 * @param {{key?: string, target?: any}} event the keydown event
 * @returns {boolean} true when the caller should call preventDefault()
 */
export function shouldPreventGrabbedKey(event) {
    if (!event?.key || !GRABBED_KEYS.has(event.key)) return false;
    const target = event.target;
    if (!target || typeof target.closest !== 'function') return false;
    const row = target.closest(ITEM_SELECTOR);
    return row === target && row.getAttribute(GRABBED_ATTR) === 'true';
}

/** The gesture options of a handle mode: a dedicated handle drags immediately, a whole row needs the hold. */
export function optionsFor(handleMode) {
    const whole = handleMode === 'whole-item';
    return {
        gripSelector: whole ? ITEM_SELECTOR : HANDLE_SELECTOR,
        longPressMs: whole ? WHOLE_ITEM_LONG_PRESS_MS : 0,
    };
}

/**
 * Where a row dragged out of `fromIndex` would land, given the current row boxes.
 * A pure function so the whole decision is testable without a DOM: the caller passes the row boxes in
 * DOM order and the pointer's viewport Y, and receives the target index (0..boxes.length-1).
 * @param {{top: number, height: number}[]} boxes the row boxes in DOM order
 * @param {number} pointerY the pointer's viewport Y
 * @param {number} fromIndex the index the drag started from
 * @returns {number} the index the row belongs at
 */
export function resolveTargetIndex(boxes, pointerY, fromIndex) {
    if (!Array.isArray(boxes) || boxes.length === 0) return Math.max(0, fromIndex);
    // The pointer is compared with the CENTRE of every other row: crossing a centre is what makes the
    // rows trade places, which is the convention every file manager uses.
    let target = fromIndex;
    for (let index = 0; index < boxes.length; index++) {
        const box = boxes[index];
        const centre = box.top + box.height / 2;
        if (index < fromIndex && pointerY < centre) {
            target = Math.min(target, index);
        }
        else if (index > fromIndex && pointerY > centre) {
            target = Math.max(target, index);
        }
    }
    return Math.min(Math.max(0, target), boxes.length - 1);
}

/**
 * The INSERTION slot a row dropped into ANOTHER list belongs at, given that list's row boxes and the
 * pointer's viewport Y. Unlike a reorder there is no row to trade places with: the slot is the first
 * row whose centre the pointer has not yet passed, and `boxes.length` means "after the last row".
 * Pure and exported for the same reason as resolveTargetIndex.
 * @param {{top: number, height: number}[]} boxes the target rows in DOM order
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
 * Which of the group's lists the pointer is over. Pure: the caller passes one viewport rect per list in
 * registration order and the pointer position, and gets the index of the first containing rect or -1
 * when the pointer is over none of them (in that case the caller keeps the list it had, so a pointer in
 * the gap between two columns never invents a target).
 * @param {{top: number, left: number, right: number, bottom: number}[]} rects the list rects
 * @param {number} x the pointer's viewport X
 * @param {number} y the pointer's viewport Y
 * @returns {number} the index of the containing list, or -1
 */
export function resolveContainerIndex(rects, x, y) {
    if (!Array.isArray(rects) || rects.length === 0) return -1;
    for (let index = 0; index < rects.length; index++) {
        const rect = rects[index];
        if (!rect) continue;
        if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom) return index;
    }
    return -1;
}

const registrations = new Map();

function readBoxes(root, grip) {
    const rows = [...root.querySelectorAll(ITEM_SELECTOR)];
    return rows.map((row) => {
        const rect = row.getBoundingClientRect();
        return {
            row,
            grip,
            top: rect.top,
            height: rect.height,
            sourceIndex: Number.parseInt(row.getAttribute(DRAG_ORIGIN_ATTR) ?? '-1', 10),
        };
    });
}

/**
 * The row gap of one list, measured ONCE per gesture from two adjacent rows. A list with fewer than two
 * rows has no gap to measure; the value is never re-measured mid-drag because the rects then include this
 * module's own sibling transforms.
 * @param {{top: number, height: number}[]} boxes the rows in DOM order
 * @returns {number} the gap in px
 */
function gapOf(boxes) {
    if (!Array.isArray(boxes) || boxes.length < 2) return 0;
    const first = boxes[0];
    const second = boxes[1];
    return Math.max(0, Math.round(second.top - (first.top + first.height)));
}

function clearInlineStyles(entries) {
    for (const entry of entries) {
        const row = entry?.row;
        if (!row?.style) continue;
        row.style.transform = '';
        row.style.zIndex = '';
        row.classList?.remove(DRAGGING_CLASS);
    }
}

/**
 * Attaches the pointer drag to a sortable list root. Attaching again with the same id replaces the
 * previous registration, so a component that changes its handle mode never leaves a stale session.
 * @param {Element|null} root the role="list" element
 * @param {{invokeMethodAsync: Function}|null} dotnet receives OnSortableReorderCommitted(from, to) and,
 *        inside a group, OnSortableTransferCommitted(toListId, from, insertIndex)
 * @param {string} id registration key (one per component instance)
 * @param {string} handleMode 'handle' or 'whole-item'
 * @param {{group?: string, listId?: string}|null} [options] cross-list membership: lists that pass the
 *        same non-empty group accept each other's rows
 */
export function attachSortable(root, dotnet, id, handleMode, options = null) {
    if (!root || typeof root.querySelectorAll !== 'function') return;
    detachSortable(id);

    const group = typeof options?.group === 'string' ? options.group : '';
    const listId = typeof options?.listId === 'string' ? options.listId : id;
    const { gripSelector, longPressMs } = optionsFor(handleMode);
    let boxes = [];
    let active = null;
    const scroller = createAutoScroller(root, {
        // Auto-scrolling moves the rows under a stationary pointer, so every base box shifts UP by the
        // applied delta and the landing slot has to be resolved again. The boxes are NOT re-measured:
        // a live rect would include this module's own sibling transforms and corrupt the decision
        // (measured: a drag that should land two rows down landed one row down).
        onScroll: ({ dy }) => {
            if (!active) return;
            active.scrolled += dy;
            for (const box of boxes) box.top -= dy;
            render();
        },
    });

    /**
     * Every list of this drag's group, in visual order (left, then top), so the container index is stable
     * and the source list is always one of them. Membership comes from the registry: a DOM query would
     * pick up a list rendered by another component instance with the same group name.
     * @returns {{root: any, listId: string, rect: any, boxes: any[]|null, isSource: boolean}[]}
     */
    function containers() {
        const members = [];
        if (group) {
            for (const entry of registrations.values()) {
                if (entry.group !== group || !entry.root) continue;
                members.push(entry);
            }
        }

        if (!members.some(entry => entry.root === root)) {
            members.push({ root, listId });
        }

        const list = members.map(entry => {
            const rect = entry.root.getBoundingClientRect?.() ?? null;
            return {
                root: entry.root,
                listId: entry.listId ?? id,
                rect,
                boxes: entry.root === root && boxes.length > 0 ? boxes : null,
                isSource: entry.root === root,
            };
        });

        list.sort((left, right) => {
            const a = left.rect ?? { left: 0, top: 0 };
            const b = right.rect ?? { left: 0, top: 0 };
            return a.left - b.left || a.top - b.top;
        });

        return list;
    }

    /** Reads the rows of a foreign list once, the first time the pointer reaches it. */
    function ensureBoxes(index) {
        const container = active?.containers?.[index];
        if (!container || container.boxes) return;
        container.boxes = readBoxes(container.root, active.grip);
    }

    function paint(entry, shift) {
        if (!entry?.row?.style) return;
        entry.row.style.transform = shift === 0 ? '' : `translateY(${shift}px)`;
        entry.row.style.zIndex = '';
        active.touched.push(entry);
    }

    function render() {
        if (!active) return;

        // Every box touched by the previous frame loses its inline styles first: the decision below is
        // always made against the PRE-drag geometry, never against this module's own output.
        for (const entry of active.touched) {
            if (!entry?.row?.style) continue;
            entry.row.style.transform = '';
            entry.row.style.zIndex = '';
            entry.row.classList?.remove(DRAGGING_CLASS);
        }
        active.touched = [];

        const container = active.containers[active.containerIndex] ?? active.containers[0];
        const foreign = container ? container.root !== root : false;
        // A foreign list is resolved from ITS OWN rows only. Falling back to the SOURCE boxes would borrow a
        // row count the target does not have — and an empty target (a board column with no cards yet) is a
        // perfectly valid drop target: its one honest slot is 0, the "before the first row" position.
        const targetBoxes = foreign ? container?.boxes ?? [] : boxes;

        const dragged = boxes[active.from];
        if (dragged?.row?.style) {
            // Inside one list the row only travels vertically; across lists it follows the pointer in
            // both axes, because the row's DOM node stays in the source list (Blazor owns the DOM).
            dragged.row.style.transform = foreign
                ? `translate(${active.x - active.startX}px, ${active.y - active.startY + active.scrolled}px)`
                : `translateY(${active.y - active.startY + active.scrolled}px)`;
            dragged.row.style.zIndex = '2';
            dragged.row.classList?.add(DRAGGING_CLASS);
            active.touched.push(dragged);
        }

        const step = (dragged?.height ?? 0) + gapOf(foreign ? targetBoxes : boxes);

        if (!foreign) {
            const target = resolveTargetIndex(boxes, active.y, active.from);
            active.to = target;
            for (let index = 0; index < boxes.length; index++) {
                if (index === active.from) continue;
                let shift = 0;
                if (active.from < target && index > active.from && index <= target) shift = -step;
                else if (active.from > target && index >= target && index < active.from) shift = step;
                paint(boxes[index], shift);
            }
        }
        else {
            // The source list closes the hole the lifted row left behind…
            for (let index = 0; index < boxes.length; index++) {
                if (index === active.from) continue;
                paint(boxes[index], index > active.from ? -step : 0);
            }

            // …and the target list opens the slot the row would land in. An EMPTY list has exactly one slot:
            // 0, so the row becomes its first row instead of borrowing the source list's indexes.
            const insert = targetBoxes.length === 0 ? 0 : resolveInsertIndex(targetBoxes, active.y);
            active.to = insert;
            for (let index = 0; index < targetBoxes.length; index++) {
                paint(targetBoxes[index], index >= insert ? step : 0);
            }
        }

        highlight(foreign ? container : null);
    }

    /** Marks the list the row would land in, so a pointer drag says where it is about to drop. */
    function highlight(container) {
        const previous = active?.highlighted;
        if (previous === container) return;
        previous?.root?.classList?.remove(DROP_TARGET_CLASS);
        container?.root?.classList?.add(DROP_TARGET_CLASS);
        if (active) active.highlighted = container;
    }

    /** Clears every painted state of the gesture, including the drop-target mark. */
    function reset() {
        if (!active) return;
        highlight(null);
        for (const entry of active.touched) {
            if (!entry?.row?.style) continue;
            entry.row.style.transform = '';
            entry.row.style.zIndex = '';
            entry.row.classList?.remove(DRAGGING_CLASS);
        }
        active.touched = [];
    }

    const drag = attach(root, {
        selector: gripSelector,
        threshold: 4,
        longPressMs,
        // The element is the whole list, so it must keep its native scrolling for touch and wheel;
        // only the dedicated handle claims touch-action, and it does so in CSS.
        touchAction: null,
        dragClass: null,
        onPress(info) {
            const parsed = info?.target?.closest?.(ITEM_SELECTOR) ?? null;
            if (!parsed || root.dataset?.disabled === 'true') return false;
            const grip = info.target?.closest?.(gripSelector) ?? null;
            if (!grip || !root.contains(parsed)) return false;
            boxes = readBoxes(root, grip);
            const fromIndex = boxes.findIndex((box) => box.row === parsed);
            if (fromIndex < 0 || boxes.length < 2) return false;
            const list = containers();
            const sourceIndex = Math.max(0, list.findIndex(entry => entry.root === root));
            const container = list[sourceIndex];
            if (container) container.boxes = boxes;
            active = {
                from: fromIndex,
                to: fromIndex,
                grip,
                x: info.startX,
                y: info.startY,
                startX: info.startX,
                startY: info.startY,
                scrolled: 0,
                containers: list,
                containerIndex: sourceIndex,
                highlighted: null,
                touched: [],
            };
            return true;
        },
        onMove(info) {
            if (!active) return;
            active.x = info.x;
            active.y = info.y;
            if (active.containers.length > 1) {
                const rects = active.containers.map(entry => entry.rect);
                const hit = resolveContainerIndex(rects, info.x, info.y);
                if (hit >= 0 && hit !== active.containerIndex) {
                    active.containerIndex = hit;
                    ensureBoxes(hit);
                }
            }
            render();
            scroller.update(info.x, info.y);
        },
        onEnd() {
            if (!active) return;
            const container = active.containers[active.containerIndex] ?? active.containers[0];
            const foreign = container ? container.root !== root : false;
            const from = active.from;
            const to = active.to;
            finish();
            if (foreign) {
                // The circuit may already be gone: a rejected invoke must not become an unhandled rejection.
                dotnet?.invokeMethodAsync('OnSortableTransferCommitted', container.listId, from, to)
                    ?.catch?.(() => {});
            }
            else if (from !== to) {
                dotnet?.invokeMethodAsync('OnSortableReorderCommitted', from, to)?.catch?.(() => {});
            }
        },
        onCancel() {
            finish();
        },
        onRelease() {
            scroller.stop();
        },
    });

    function finish() {
        reset();
        clearInlineStyles(boxes);
        active = null;
        boxes = [];
        scroller.stop();
    }

    // The page-scroll guard of a grabbed row. It lives here (not in a render-time
    // @onkeydown:preventDefault) because the decision needs the KEY: Tab must keep its default action.
    const onKeyDown = (event) => {
        if (shouldPreventGrabbedKey(event)) event.preventDefault();
    };
    root.addEventListener?.('keydown', onKeyDown);

    registrations.set(id, { root, drag, scroller, onKeyDown, group, listId });
}

/** Removes the drag registered under the id. Safe to call twice or for an unknown id. */
export function detachSortable(id) {
    const entry = registrations.get(id);
    if (!entry) return;
    registrations.delete(id);
    entry.drag.detach();
    entry.scroller.stop();
    entry.root?.removeEventListener?.('keydown', entry.onKeyDown);
    entry.root?.classList?.remove(DROP_TARGET_CLASS);
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...registrations.keys()]) detachSortable(id);
}
