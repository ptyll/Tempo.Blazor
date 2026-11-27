// The text tabs of the desktop inspector sit on ONE row that scrolls sideways when the inspector is too narrow for four labels (Properties,
// Comments, Revisions, Versions). Two jobs live here, both because CSS cannot do them:
//   * a tab activated from CODE (a review-summary button, find, Ctrl+Shift+...) must end up inside the scroller, not hidden past its edge;
//   * the row publishes `data-overflow-start` / `data-overflow-end` so the stylesheet can fade the edge that still hides tabs.
// The Blazor side calls syncTabScroller after every render of the tab bar, so an ActiveTab change from any source lands here.

const TAB_SELECTOR = '[role="tab"][aria-selected="true"]';
const EDGE_MARGIN = 8;
const attached = new WeakMap();

/** The scrollLeft that brings a tab (start / end measured from the scroller's content origin) inside the scroller, moving it as little as possible. */
export function scrollLeftForTab({ scrollLeft, clientWidth, tabStart, tabEnd }) {
    if (tabEnd - tabStart + 2 * EDGE_MARGIN >= clientWidth) {
        return Math.max(0, tabStart - EDGE_MARGIN);
    }

    if (tabStart - EDGE_MARGIN < scrollLeft) {
        return Math.max(0, tabStart - EDGE_MARGIN);
    }

    if (tabEnd + EDGE_MARGIN > scrollLeft + clientWidth) {
        return tabEnd - clientWidth + EDGE_MARGIN;
    }

    return scrollLeft;
}

/** Which sides of a scroller still hide content. */
export function overflowState({ scrollLeft, clientWidth, scrollWidth }) {
    return {
        start: scrollLeft > 1,
        end: scrollLeft + clientWidth < scrollWidth - 1,
    };
}

function publishOverflow(list) {
    const state = overflowState(list);
    list.setAttribute('data-overflow-start', state.start);
    list.setAttribute('data-overflow-end', state.end);
}

/** Scrolls the active tab of the row into view (horizontally, inside the row only) and refreshes the overflow cue. */
export function syncTabScroller(list) {
    if (!list) {
        return;
    }

    const tab = list.querySelector(TAB_SELECTOR);
    if (tab) {
        const listLeft = list.getBoundingClientRect().left;
        const tabRect = tab.getBoundingClientRect();
        const tabStart = tabRect.left - listLeft + list.scrollLeft;
        const next = scrollLeftForTab({
            scrollLeft: list.scrollLeft,
            clientWidth: list.clientWidth,
            tabStart,
            tabEnd: tabStart + tabRect.width,
        });
        if (next !== list.scrollLeft) {
            list.scrollLeft = next;
        }
    }

    publishOverflow(list);
}

export function attachTabScroller(list) {
    if (!list || attached.has(list)) {
        return;
    }

    const onScroll = () => publishOverflow(list);
    list.addEventListener('scroll', onScroll, { passive: true });
    // The inspector can be resized (or a label localized longer) without a scroll: the cue follows the width too.
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(onScroll) : null;
    observer?.observe(list);
    attached.set(list, { onScroll, observer });
}

export function detachTabScroller(list) {
    const entry = list && attached.get(list);
    if (!entry) {
        return;
    }

    list.removeEventListener('scroll', entry.onScroll);
    entry.observer?.disconnect();
    attached.delete(list);
}
