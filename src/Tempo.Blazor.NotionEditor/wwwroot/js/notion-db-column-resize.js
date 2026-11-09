// Column resize for the Notion database table. The live width is written to the header cell
// directly, so a pointermove never re-renders the Blazor component (that re-render is what made
// the drag unusable on Blazor Server). Blazor learns the width once, on pointerup, through
// OnColumnResized. Alt+Arrow commits on a short debounce (or keyup/blur), not per repeat.
//
// The gesture is the shared pointer-drag helper, delegated from the table root so a field that
// appears later (or a ReadOnly flip that mounts handles after first render) still resizes.
// This file does not implement a second drag.

const MIN_WIDTH = 60;
const MAX_WIDTH = 800;
const KEY_STEP = 16;
const KEY_COMMIT_MS = 300;

let helperPromise = null;

function helperUrl() {
    // Same-folder resolution from this module's own URL, then one `_content` sibling over into
    // the core package. A package-local copy of pointer-drag.js is forbidden.
    const here = new URL(import.meta.url);
    const sibling = new URL(here.href.replace('/Tempo.Blazor.NotionEditor/', '/Tempo.Blazor/'));
    sibling.pathname = sibling.pathname.replace(/[^/]+$/, 'pointer-drag.js');
    return sibling.href;
}

function loadHelper() {
    if (!helperPromise) {
        const loader = globalThis.__tmNotionResizeImport ?? ((url) => import(url));
        helperPromise = Promise.resolve(loader(helperUrl())).catch((err) => {
            helperPromise = null;
            console.error('notion-db-column-resize: pointer-drag.js failed to load', err);
            throw err;
        });
    }
    return helperPromise;
}

function clamp(width) {
    return Math.max(MIN_WIDTH, Math.min(MAX_WIDTH, Math.round(width)));
}

/**
 * @param {HTMLElement|null} root the table wrapper
 * @param {{ invokeMethodAsync: (name: string, ...args: unknown[]) => Promise<unknown> }} dotNetRef
 * @returns {Promise<{ detach: () => void }>}
 */
export async function attachColumnResize(root, dotNetRef) {
    if (!root) return { detach() {} };

    const { attach, attachKeyboardMove } = await loadHelper();

    const pending = new Map();

    const headerOf = (handle) => handle?.closest?.('th') ?? null;

    const apply = (handle, width) => {
        const header = headerOf(handle);
        if (!header) return null;
        const next = clamp(width);
        header.style.width = `${next}px`;
        header.style.minWidth = `${next}px`;
        handle.setAttribute('aria-valuenow', String(next));
        handle.setAttribute('aria-valuemin', String(MIN_WIDTH));
        handle.setAttribute('aria-valuemax', String(MAX_WIDTH));
        return next;
    };

    const commit = (handle, width) => {
        const fieldId = handle?.getAttribute?.('data-resize-field');
        if (!fieldId) return;
        const next = apply(handle, width);
        if (next == null) return;
        dotNetRef?.invokeMethodAsync?.('OnColumnResized', fieldId, next)?.catch?.((err) => {
            console.error('notion-db-column-resize: OnColumnResized failed', err);
        });
    };

    const flush = (handle) => {
        const entry = pending.get(handle);
        if (!entry) return;
        clearTimeout(entry.timer);
        pending.delete(handle);
        commit(handle, entry.width);
    };

    const schedule = (handle, width) => {
        const next = apply(handle, width);
        if (next == null) return;
        const prev = pending.get(handle);
        if (prev) clearTimeout(prev.timer);
        const timer = setTimeout(() => flush(handle), KEY_COMMIT_MS);
        pending.set(handle, { width: next, timer });
    };

    const drag = attach(root, {
        selector: '[data-resize-field]',
        threshold: 2,
        onStart: (info) => {
            const header = headerOf(info.target);
            if (!header) return false;
            header.dataset.resizeStart = String(header.getBoundingClientRect().width);
            root.classList.add('tm-dbt--resizing');
        },
        onMove: (info) => {
            const header = headerOf(info.target);
            if (!header) return;
            const start = Number(header.dataset.resizeStart) || header.getBoundingClientRect().width;
            apply(info.target, start + info.dx);
        },
        onEnd: (info) => {
            root.classList.remove('tm-dbt--resizing');
            const header = headerOf(info.target);
            const start = Number(header?.dataset.resizeStart) || header?.getBoundingClientRect().width || MIN_WIDTH;
            commit(info.target, start + info.dx);
        },
        onCancel: () => root.classList.remove('tm-dbt--resizing'),
    });

    const keys = attachKeyboardMove(root, {
        selector: '[data-resize-field]',
        onStep: (step) => {
            if (!step.x) return;
            const handle = step.event?.target?.closest?.('[data-resize-field]');
            if (!handle) return;
            const header = headerOf(handle);
            if (!header) return;
            const current = Number(handle.getAttribute('aria-valuenow')) || header.getBoundingClientRect().width;
            schedule(handle, current + step.x * KEY_STEP);
        },
    });

    const handleFromEvent = (event) => {
        const target = event.target;
        if (target?.getAttribute?.('data-resize-field')) return target;
        return target?.closest?.('[data-resize-field]') ?? null;
    };
    const onKeyUp = (event) => {
        const handle = handleFromEvent(event);
        if (handle) flush(handle);
    };
    const onBlur = (event) => {
        const handle = handleFromEvent(event);
        if (handle) flush(handle);
    };
    root.addEventListener('keyup', onKeyUp);
    root.addEventListener('focusout', onBlur);

    return {
        detach() {
            for (const handle of [...pending.keys()]) flush(handle);
            drag.detach();
            keys.detach();
            root.removeEventListener('keyup', onKeyUp);
            root.removeEventListener('focusout', onBlur);
            root.classList.remove('tm-dbt--resizing');
        },
    };
}
