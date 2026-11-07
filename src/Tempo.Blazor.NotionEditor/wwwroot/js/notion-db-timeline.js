// Timeline bar drag and resize for the Notion database. The live left/width is written to the
// bar element, so a pointermove never re-renders the Blazor component (that re-render is what
// made the old mousemove overlay unusable on Blazor Server). Blazor learns the new dates once,
// on pointerup, through OnBarCommitted.
//
// The gesture is the shared pointer-drag helper, delegated from the timeline root. This file
// does not implement a second drag. A touch drag starts on a long-press so a swipe can scroll.

const LONG_PRESS_MS = 500;

let helperPromise = null;

function helperUrl() {
    const here = new URL(import.meta.url);
    const sibling = new URL(here.href.replace('/Tempo.Blazor.NotionEditor/', '/Tempo.Blazor/'));
    sibling.pathname = sibling.pathname.replace(/[^/]+$/, 'pointer-drag.js');
    return sibling.href;
}

function loadHelper() {
    if (!helperPromise) {
        const loader = globalThis.__tmNotionTimelineImport ?? ((url) => import(url));
        helperPromise = Promise.resolve(loader(helperUrl())).catch((err) => {
            helperPromise = null;
            console.error('notion-db-timeline: pointer-drag.js failed to load', err);
            throw err;
        });
    }
    return helperPromise;
}

/**
 * @param {HTMLElement|null} root the timeline root
 * @param {{ invokeMethodAsync: (name: string, ...args: unknown[]) => Promise<unknown> }} dotNetRef
 * @returns {Promise<{ detach: () => void }>}
 */
export async function attachTimeline(root, dotNetRef) {
    if (!root) return { detach() {} };

    const { attach, attachKeyboardMove } = await loadHelper();

    const pxPerDay = () => Number(root.dataset.pxPerDay) || 1;

    const drag = attach(root, {
        selector: '[data-timeline-bar], [data-timeline-resize]',
        threshold: 4,
        longPressMs: LONG_PRESS_MS,
        onStart: (info) => {
            const bar = info.target?.closest?.('[data-timeline-bar]');
            if (!bar || root.dataset.readonly === 'true') return false;
            bar.dataset.dragStartLeft = bar.style.left || '0px';
            bar.dataset.dragStartWidth = bar.style.width || '0px';
            // Captured once: a later pointermove can retarget away from the resize handle.
            bar.dataset.dragMode = info.target?.matches?.('[data-timeline-resize]') ? 'resize' : 'move';
            root.classList.add('tm-dbtl--dragging');
        },
        onMove: (info) => {
            const bar = info.target?.closest?.('[data-timeline-bar]');
            if (!bar) return;
            const resizing = bar.dataset.dragMode === 'resize';
            const startLeft = parseFloat(bar.dataset.dragStartLeft) || 0;
            const startWidth = parseFloat(bar.dataset.dragStartWidth) || 0;
            if (resizing) {
                bar.style.width = `${Math.max(8, startWidth + info.dx)}px`;
            }
            else {
                bar.style.left = `${startLeft + info.dx}px`;
            }
        },
        onEnd: (info) => {
            root.classList.remove('tm-dbtl--dragging');
            const bar = info.target?.closest?.('[data-timeline-bar]');
            if (!bar) return;
            const resizing = bar.dataset.dragMode === 'resize';
            // Restore before the callback: Blazor re-renders from the committed dates, and a
            // leftover inline width would fight that render if the callback is slow.
            if (bar.dataset.dragStartLeft) bar.style.left = bar.dataset.dragStartLeft;
            if (bar.dataset.dragStartWidth) bar.style.width = bar.dataset.dragStartWidth;
            const recordId = bar.getAttribute('data-record-id');
            if (!recordId) return;
            const days = Math.round(info.dx / pxPerDay());
            dotNetRef?.invokeMethodAsync?.('OnBarCommitted', recordId, days, resizing)?.catch?.((err) => {
                console.error('notion-db-timeline: OnBarCommitted failed', err);
            });
        },
        onCancel: (info) => {
            root.classList.remove('tm-dbtl--dragging');
            const bar = info?.target?.closest?.('[data-timeline-bar]');
            if (!bar) return;
            if (bar.dataset.dragStartLeft) bar.style.left = bar.dataset.dragStartLeft;
            if (bar.dataset.dragStartWidth) bar.style.width = bar.dataset.dragStartWidth;
        },
    });

    // Alt+Arrow moves, Alt+Shift+Arrow resizes (WCAG 2.5.7). shift:true opts the helper into
    // the Shift chord; a step with shift===true is the resize.
    const keys = attachKeyboardMove(root, {
        selector: '[data-timeline-bar]',
        shift: true,
        onStep: (step) => {
            if (!step.x) return;
            const bar = step.event?.target?.closest?.('[data-timeline-bar]');
            const recordId = bar?.getAttribute?.('data-record-id');
            if (!recordId) return;
            dotNetRef?.invokeMethodAsync?.('OnBarCommitted', recordId, step.x, step.shift === true)?.catch?.(() => {});
        },
    });

    return {
        detach() {
            drag.detach();
            keys.detach();
            root.classList.remove('tm-dbtl--dragging');
        },
    };
}
