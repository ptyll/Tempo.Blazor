// Tempo.Blazor sortable list (ES module) — the pointer path of TmSortableList.
//
//   import { attachSortable, detachSortable } from './tm-sortable.js';
//   attachSortable(root, dotnet, id, 'handle' | 'whole-item');
//   detachSortable(id);          // on dispose / when the list turns read-only
//
// Composition, never a second drag: the gesture comes from the shared pointer-drag.js helper (pointer
// capture by pointerId, threshold, the touch long-press, Escape / pointercancel / lostpointercapture
// rollback, click swallowing) and the edge scroll from pointer-gesture.js createAutoScroller. This module
// owns only the LIST-specific part: which child is the dragged row, where it would land, how the siblings
// shift out of the way, and the ONE commit reported to .NET on release.
//
// Rules that keep it honest:
//   * Blazor is told exactly ONCE per gesture (on release). Nothing here writes component state on move —
//     the dragged row and its siblings move with inline transforms only, so a 200-row list does not
//     re-render on every pointer event.
//   * Cancel paths restore every inline style VERBATIM and report nothing, so a rolled-back drag can
//     never reorder the host's list.
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
 * @param {{invokeMethodAsync: Function}|null} dotnet receives OnSortableReorderCommitted(from, to)
 * @param {string} id registration key (one per component instance)
 * @param {string} handleMode 'handle' or 'whole-item'
 */
export function attachSortable(root, dotnet, id, handleMode) {
    if (!root || typeof root.querySelectorAll !== 'function') return;
    detachSortable(id);

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
            resolveAndApply();
        },
    });

    function resolveAndApply() {
        if (!active) return;
        const target = resolveTargetIndex(boxes, active.y, active.from);
        active.to = target;
        applyOffsets(target);
    }

    function applyOffsets(target) {
        if (!active) return;

        const rowHeight = boxes[active.from]?.height ?? 0;
        const step = rowHeight + rowGap();
        for (let index = 0; index < boxes.length; index++) {
            const entry = boxes[index];
            if (!entry.row?.style) continue;
            if (index === active.from) {
                // The row follows the pointer from the exact point it was grabbed at, plus whatever the
                // edge auto-scroll moved the list by (the pointer did not move, the content did).
                entry.row.style.transform = `translateY(${active.y - active.startY + active.scrolled}px)`;
                entry.row.style.zIndex = '2';
                entry.row.classList?.add(DRAGGING_CLASS);
                continue;
            }
            let shift = 0;
            if (active.from < target && index > active.from && index <= target) shift = -step;
            else if (active.from > target && index >= target && index < active.from) shift = step;
            entry.row.style.transform = shift === 0 ? '' : `translateY(${shift}px)`;
        }
    }

    function rowGap() {
        // Measure once per gesture from two adjacent rows; a list with one row has no gap to measure.
        if (boxes.length < 2) return 0;
        const first = boxes[0];
        const second = boxes[1];
        return Math.max(0, Math.round(second.top - (first.top + first.height)));
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
            active = {
                from: fromIndex,
                to: fromIndex,
                grip,
                x: info.startX,
                y: info.startY,
                startY: info.startY,
                scrolled: 0,
            };
            return true;
        },
        onMove(info) {
            if (!active) return;
            active.x = info.x;
            active.y = info.y;
            resolveAndApply();
            scroller.update(info.x, info.y);
        },
        onEnd() {
            if (!active) return;
            const from = active.from;
            const to = active.to;
            finish();
            if (from !== to) {
                // The circuit may already be gone: a rejected invoke must not become an unhandled rejection.
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

    registrations.set(id, { root, drag, scroller, onKeyDown });
}

/** Removes the drag registered under the id. Safe to call twice or for an unknown id. */
export function detachSortable(id) {
    const entry = registrations.get(id);
    if (!entry) return;
    registrations.delete(id);
    entry.drag.detach();
    entry.scroller.stop();
    entry.root?.removeEventListener?.('keydown', entry.onKeyDown);
}

/** Drop every registration. Test-only: a module imported by Node is shared across tests. */
export function __resetForTests() {
    for (const id of [...registrations.keys()]) detachSortable(id);
}
