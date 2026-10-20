/**
 * Tempo.Blazor — TmDataTable JS interop.
 *
 * Column resize drag on the shared pointer-drag helper: the gesture (Pointer Events, pointer
 * capture by pointerId, threshold, Escape / pointercancel / lost-capture cancel, touch-action)
 * lives in pointer-drag.js; this file owns only the table maths. The width is measured from the
 * <th> on press (never the 160 default), reported live to .NET (OnColumnResized) and committed
 * exactly once on release (OnColumnResizeCommitted). Cancel paths — Escape, pointercancel, a lost
 * capture, a mid-drag detach — report OnColumnResizeCancelled: the C# side keeps the snapshot of
 * the width the column had before the gesture and restores or removes it itself, so a cancelled
 * drag on a widthless (auto) column is rendered widthless again, never persisting anything.
 *
 * This file is a classic script (window.tmDataTable), so it loads the helper with a dynamic
 * import() resolved from the URL it was served from (a bare '_content/…' specifier is not a
 * module specifier and a root-absolute URL misses <base href> and MapStaticAssets fingerprints).
 */
window.tmDataTable = (function () {
    'use strict';

    // Resolved NOW: document.currentScript is only set while the classic script runs synchronously.
    var helperUrl = (function () {
        try {
            var script = document.currentScript;
            if (script && script.src) return new URL('pointer-drag.js', script.src).href;
            return new URL('_content/Tempo.Blazor/js/pointer-drag.js', document.baseURI).href;
        } catch (e) {
            return '_content/Tempo.Blazor/js/pointer-drag.js';
        }
    })();

    // The writing direction probe is the SHARED one (tm-rtl.js — the same single directionOf the
    // lightbox and the splitter read). This file is a classic script, so the module loads the way
    // pointer-drag.js does: by URL, once, at startup. Until the import lands (or wherever it
    // fails, e.g. SSR) directionOf answers with the same computed-style probe inline, so the
    // export is callable from the first synchronous line after script evaluation.
    var rtlUrl = (function () {
        try {
            var script = document.currentScript;
            if (script && script.src) return new URL('tm-rtl.js', script.src).href;
            return new URL('_content/Tempo.Blazor/js/tm-rtl.js', document.baseURI).href;
        } catch (e) {
            return '_content/Tempo.Blazor/js/tm-rtl.js';
        }
    })();
    var sharedRtl = null;
    import(rtlUrl).then(function (m) { sharedRtl = m; }).catch(function () { /* inline probe stays */ });

    var HANDLE_SELECTOR = '.tm-col-resize-handle';
    // Keys the resize handle answers (Blazor performs the resize); the browser's default for them
    // on a focused handle is scrolling the table, which a width change must not also do.
    var RESIZE_KEYS = ['ArrowLeft', 'ArrowRight', 'Home', 'End'];

    /**
     * The writing direction of an element, read live from its computed style. The horizontal drag
     * delta inverts in RTL, so the table has to ask the DOM (a `dir` attribute or CSS can flip it
     * at any level above the table). Delegates to the shared tm-rtl.js probe once its import has
     * landed; the inline branch is the identical fallback for the window before that (and for
     * hosts where the import failed). Falls back to 'ltr' wherever the answer cannot be
     * determined (Node, SSR).
     */
    function directionOf(element) {
        if (sharedRtl) return sharedRtl.directionOf(element);
        try {
            if (typeof getComputedStyle !== 'function') return 'ltr';
            return getComputedStyle(element)?.direction === 'rtl' ? 'rtl' : 'ltr';
        } catch (e) {
            return 'ltr';
        }
    }

    /**
     * The real rendered width of one column's <th>, measured live through its resize handle.
     * The keyboard resize starts from this (the 160 default is only a fallback), so a column
     * that never carried an explicit width still steps from where it actually is.
     * @returns {number|null} the th's bounding-box width, or null when it cannot be measured.
     */
    function measureColumn(root, columnKey) {
        try {
            if (!root || !root.querySelector || columnKey == null) return null;
            var handle = root.querySelector('[data-col-key="' + columnKey + '"]');
            var th = handle && handle.closest ? handle.closest('th') : null;
            var rect = th && th.getBoundingClientRect ? th.getBoundingClientRect() : null;
            return rect && rect.width ? rect.width : null;
        } catch (e) {
            return null;
        }
    }

    /**
     * The legacy drag entry point. Kept for hosts that still call it; superseded by
     * attachColumnResize, which measures the real start width and rolls cancellations back.
     * @deprecated
     */
    function startColumnResize(dotNetRef, columnKey, startX, startWidth, minWidth) {
        if (!dotNetRef) return;
        const min = minWidth || 60;
        let lastWidth = Math.round(startWidth);
        let frame = 0;

        function onMove(e) {
            const clientX = (e.touches && e.touches.length) ? e.touches[0].clientX : e.clientX;
            const width = Math.max(min, Math.round(startWidth + (clientX - startX)));
            if (width === lastWidth) return;
            lastWidth = width;
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = 0;
                dotNetRef.invokeMethodAsync('OnColumnResized', columnKey, width).catch(() => { });
            });
        }

        function onUp() {
            document.removeEventListener('pointermove', onMove);
            document.removeEventListener('pointerup', onUp);
            document.removeEventListener('pointercancel', onUp);
            document.body.style.userSelect = '';
            document.body.style.cursor = '';
            if (frame) {
                cancelAnimationFrame(frame);
                frame = 0;
            }
            // Flush the final width before committing so a release within one frame of the last
            // movement still persists the exact width the user left.
            dotNetRef.invokeMethodAsync('OnColumnResized', columnKey, lastWidth)
                .then(() => dotNetRef.invokeMethodAsync('OnColumnResizeCommitted'))
                .catch(() => { });
        }

        document.body.style.userSelect = 'none';
        document.body.style.cursor = 'col-resize';
        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp);
        document.addEventListener('pointercancel', onUp);
    }

    // root (the <table>) -> { drag, onKey }; a WeakMap so a removed table is collectable.
    const attached = new WeakMap();
    // Roots whose import() is still in flight; a detach during the load kills the attach.
    const pending = new WeakSet();

    /**
     * Attaches the delegated column-resize drag to a table. A press that starts on a
     * .tm-col-resize-handle measures its <th>, the width follows the pointer live through
     * OnColumnResized (rAF-throttled, no persistence), and the release flushes the final width and
     * commits ONCE through OnColumnResizeCommitted. Escape, pointercancel and a lost capture
     * report OnColumnResizeCancelled — the C# snapshot restores the pre-gesture width — and
     * commit nothing. Arrows,
     * Home and End on a focused handle are prevented here (they would scroll the table); Blazor
     * performs the keyboard resize. A second attach on the same root replaces the first.
     */
    function attachColumnResize(root, dotNetRef) {
        if (!root || !dotNetRef) return Promise.resolve();
        detachColumnResize(root);
        pending.add(root);
        return import(helperUrl).then(function (helper) {
            // detachColumnResize was called while the helper was loading.
            if (!pending.delete(root)) return;

            var state = null;

            var drag = helper.attach(root, {
                selector: HANDLE_SELECTOR,
                threshold: 0,
                // The handle owns no click of its own; the Blazor @onclick barrier keeps a tap
                // from sorting the column.
                suppressClick: false,
                // CSS owns touch-action: none on the handle (.tm-col-resize-handle) — a delegated
                // drag must leave the table scrollable everywhere else.
                touchAction: null,
                onPress(info) {
                    var handle = info.target;
                    var th = handle.closest && handle.closest('th');
                    var rect = th && th.getBoundingClientRect ? th.getBoundingClientRect() : null;
                    if (!rect || !rect.width) return false;
                    state = {
                        key: handle.dataset.colKey || handle.getAttribute('data-col-key') || '',
                        start: Math.max(0, Math.round(rect.width)),
                        min: parseInt(handle.dataset.minWidth, 10) || 60,
                        last: 0,
                        frame: 0,
                        rtl: directionOf(handle) === 'rtl',
                    };
                    state.last = state.start;
                    return true;
                },
                onMove(info) {
                    var delta = state.rtl ? -info.dx : info.dx;
                    var width = Math.max(state.min, Math.round(state.start + delta));
                    if (width === state.last) return;
                    state.last = width;
                    if (state.frame) return;
                    state.frame = requestAnimationFrame(function () {
                        state.frame = 0;
                        dotNetRef.invokeMethodAsync('OnColumnResized', state.key, state.last).catch(function () { });
                    });
                },
                onEnd() {
                    if (state.frame) {
                        cancelAnimationFrame(state.frame);
                        state.frame = 0;
                    }
                    // Flush the final width before committing so a release within one frame of the
                    // last movement still persists the exact width the user left.
                    dotNetRef.invokeMethodAsync('OnColumnResized', state.key, state.last)
                        .then(function () { return dotNetRef.invokeMethodAsync('OnColumnResizeCommitted'); })
                        .catch(function () { });
                },
                onCancel() {
                    if (state.frame) {
                        cancelAnimationFrame(state.frame);
                        state.frame = 0;
                    }
                    // The C# side keeps its own snapshot of the width the column had before the
                    // FIRST OnColumnResized of this gesture and restores (or removes) it here —
                    // a cancelled drag must not even transiently leave the start width behind as
                    // a persisted-looking value. NO commit, NO OnColumnResized call.
                    dotNetRef.invokeMethodAsync('OnColumnResizeCancelled').catch(function () { });
                },
                onRelease() {
                    state = null;
                },
            });

            var onKey = function (event) {
                var target = event.target;
                if (!target || !target.closest || !target.closest(HANDLE_SELECTOR)) return;
                if (RESIZE_KEYS.indexOf(event.key) !== -1) event.preventDefault();
            };
            root.addEventListener('keydown', onKey);

            attached.set(root, { drag: drag, onKey: onKey });
        }).catch(function (err) {
            pending.delete(root);
            console.error('tmDataTable.attachColumnResize', err);
        });
    }

    /** Removes the resize attached to a root. Safe to call twice or for an unknown root. */
    function detachColumnResize(root) {
        pending.delete(root);
        var entry = attached.get(root);
        if (!entry) return;
        attached.delete(root);
        entry.drag.detach();
        root.removeEventListener('keydown', entry.onKey);
    }

    function downloadFile(fileName, contentType, base64) {
        try {
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName || 'export';
            document.body.appendChild(a);
            a.click();
            a.remove();
            // Keep the blob URL alive long enough for slow consumers (e.g. automated
            // download capture) — revoking too early cancels an in-flight download.
            setTimeout(() => URL.revokeObjectURL(url), 60000);
        } catch (err) {
            console.error('tmDataTable.downloadFile', err);
        }
    }

    function setIndeterminate(el, value) {
        if (el) el.indeterminate = !!value;
    }

    // Navigation keys whose browser default (scroll the page / the cell) must not run while a
    // grid cell owns the key. Blazor cannot make preventDefault depend on which key was pressed,
    // so the decision lives here. Ctrl/Alt/Meta combinations stay with the browser.
    var GRID_NAV_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown', ' '];
    var gridKeyRoots = new Map();

    function isGridEditTarget(target) {
        if (!target || !target.closest) return false;
        return !!target.closest('input, textarea, select, [contenteditable="true"]');
    }

    /**
     * Cancels the page-scroll default of grid navigation keys on a grid cell. A key that lands
     * inside an editor (inline edit, a filter, the checkbox's own control) is left alone — that
     * control owns Space and the arrows.
     */
    function attachGridKeys(table) {
        if (!table || !table.addEventListener || gridKeyRoots.has(table)) return;
        var onKey = function (event) {
            var target = event.target;
            if (!target || !target.closest) return;
            if (!target.closest('td[role="gridcell"], th[role="columnheader"], td[role="rowheader"]')) return;
            if (event.ctrlKey || event.altKey || event.metaKey) return;
            if (isGridEditTarget(target)) return;
            if (GRID_NAV_KEYS.indexOf(event.key) === -1) return;
            event.preventDefault();
        };
        table.addEventListener('keydown', onKey);
        gridKeyRoots.set(table, onKey);
    }

    // Arrow keys that scroll the page when a composite widget (a radiogroup) owns them.
    // Tab is deliberately absent: a blanket preventDefault on the container would trap focus.
    var ARROW_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'];
    var arrowKeyRoots = new Map();

    /**
     * Cancels the page-scroll default of arrow keys on a composite widget. Only a key whose target
     * matches `selector` (or sits inside one) is cancelled, and only the four arrows — Tab, Enter
     * and Space keep their browser behaviour. Attaching twice to the same element is a no-op.
     */
    function attachArrowKeyPrevent(el, selector) {
        if (!el || !el.addEventListener || arrowKeyRoots.has(el)) return;
        var onKey = function (event) {
            if (event.ctrlKey || event.altKey || event.metaKey) return;
            if (ARROW_KEYS.indexOf(event.key) === -1) return;
            var target = event.target;
            if (selector && target && typeof target.closest === 'function' && !target.closest(selector)) return;
            event.preventDefault();
        };
        el.addEventListener('keydown', onKey);
        arrowKeyRoots.set(el, onKey);
    }

    function detachGridKeys(table) {
        var onKey = gridKeyRoots.get(table);
        if (!onKey) return;
        gridKeyRoots.delete(table);
        table.removeEventListener('keydown', onKey);
    }

    /**
     * Brings a not-yet-rendered virtualized row into the scroller so the next render can paint it.
     * The estimate is the same item size Virtualize uses; the real cell is focused afterwards.
     */
    function scrollGridToIndex(table, index, itemSize) {
        if (!table || !table.closest) return;
        var scroller = table.closest('.tm-data-table-scroll') || table.parentElement;
        if (!scroller) return;
        var top = Math.max(0, Number(index) * Number(itemSize || 0));
        try { scroller.scrollTo({ top: top }); } catch (e) { scroller.scrollTop = top; }
    }

    /**
     * Moves DOM focus onto the roving grid cell Blazor just marked tabindex=0, then scrolls it
     * into the nearest edge of its scroller. A group header only renders column 0, so a cursor
     * that kept its previous column falls back to that row's cell before the empty-table header.
     * A cell that is not in the DOM (a virtualized row that has not rendered) is a no-op.
     */
    function focusGridCell(table, row, col) {
        if (!table || !table.querySelector) return;
        var key = Number(row) + ':' + Number(col);
        var cell = table.querySelector('[data-grid-cell="' + key + '"]');
        if (!cell || typeof cell.focus !== 'function') {
            cell = table.querySelector('[data-grid-cell^="' + Number(row) + ':"]');
        }
        if (!cell || typeof cell.focus !== 'function') {
            cell = table.querySelector('th[role="columnheader"][tabindex="0"]');
        }
        if (cell && typeof cell.focus === 'function') {
            try { cell.focus({ preventScroll: true }); } catch (e) { cell.focus(); }
            try { cell.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) { /* no scroller */ }
        }
    }

    /**
     * Hands focus to the first item of the column menu Blazor just opened. The menu lives in the
     * top layer (TmOverlayPanel), so it is looked up by id on the document, not inside the table.
     * Arrow / Home / End navigation is owned by tm-menu-nav.js, which TmOverlayPanel attaches to
     * every role="menu" panel — this only moves the initial focus. Sort items are menuitemradio;
     * every other item is menuitem, so the selector matches both.
     */
    function focusColumnMenu(menuId) {
        var menu = document.getElementById(menuId);
        if (!menu) return;
        var item = menu.querySelector('[role^="menuitem"]:not([disabled])');
        if (item && typeof item.focus === 'function') {
            try { item.focus({ preventScroll: true }); } catch (e) { item.focus(); }
        }
    }

    /**
     * Returns focus to the header of the column whose menu just closed. The kebab itself is
     * tabindex=-1 (reached with M), so the stop is the header's sort button, else the header.
     */
    function focusHeader(tableEl, key) {
        if (!tableEl || !tableEl.querySelector || key == null) return;
        var trigger = tableEl.querySelector('.tm-data-table-col-menu[data-column-key="' + cssEscape(key) + '"]');
        var header = trigger && trigger.closest ? trigger.closest('th') : null;
        if (!header) {
            header = tableEl.querySelector('th[data-column-key="' + cssEscape(key) + '"]');
        }
        if (!header) return;
        var target = header.querySelector('.tm-th-sort') || header;
        if (typeof target.focus === 'function') {
            try { target.focus({ preventScroll: true }); } catch (e) { target.focus(); }
        }
    }

    function cssEscape(value) {
        if (window.CSS && typeof window.CSS.escape === 'function') return window.CSS.escape(String(value));
        return String(value).replace(/["\\]/g, '\\$&');
    }

    /**
     * Moves focus onto the filter control the column menu's "Filter" item names. The control is
     * rendered by Blazor before this runs, so a missing element means the column is not filterable.
     */
    function focusColumnFilter(inputId) {
        var input = document.getElementById(inputId);
        if (input && typeof input.focus === 'function') {
            try { input.focus({ preventScroll: true }); } catch (e) { input.focus(); }
            try { input.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) { /* no scroller */ }
        }
    }

    // wrapper element -> { observer, timer }. A WeakMap so a removed table is collectable.
    var pinObservers = new WeakMap();

    /**
     * Recomputes every pinned column's sticky offset from the first header row only, and writes it
     * once onto the wrapper as `--tm-pin-left-<index>` / `--tm-pin-right-<index>`. Body cells used
     * to share the running total, so each row landed at the sum of every pinned header; writing the
     * property on the cell itself was also thrown away by the next Blazor render, which rebuilds
     * the inline style. The component reads the wrapper property back (`var(--tm-pin-left-N, …)`),
     * so a re-render keeps the measured value and the C# offset stays the fallback. Debounced so a
     * resize storm measures once.
     */
    function recomputePinnedOffsets(wrapper) {
        if (!wrapper || !wrapper.querySelectorAll || !wrapper.style) return;
        var headerRow = wrapper.querySelector('thead tr');
        if (!headerRow) return;
        var headers = Array.from(headerRow.children || []).filter(function (cell) {
            return cell && (cell.tagName === 'TH' || cell.tagName === 'th');
        });
        var left = 0;
        headers.forEach(function (header, index) {
            if (!header.classList || !header.classList.contains('tm-col-pinned-left')) return;
            wrapper.style.setProperty('--tm-pin-left-' + index, left + 'px');
            left += Math.round(header.getBoundingClientRect().width);
        });
        var right = 0;
        for (var i = headers.length - 1; i >= 0; i--) {
            var header = headers[i];
            if (!header.classList || !header.classList.contains('tm-col-pinned-right')) continue;
            wrapper.style.setProperty('--tm-pin-right-' + i, right + 'px');
            right += Math.round(header.getBoundingClientRect().width);
        }
    }

    /**
     * Watches the table wrapper and recomputes the pinned offsets 100ms after the last resize.
     * Observing twice is a no-op; dispose detaches the observer.
     */
    function observePinnedOffsets(wrapper) {
        if (!wrapper || pinObservers.has(wrapper) || typeof ResizeObserver !== 'function') return;
        var state = { timer: 0 };
        state.observer = new ResizeObserver(function () {
            clearTimeout(state.timer);
            state.timer = setTimeout(function () { recomputePinnedOffsets(wrapper); }, 100);
        });
        state.observer.observe(wrapper);
        pinObservers.set(wrapper, state);
    }

    /** Detaches the pinned-offset observer. Called from the component's dispose. */
    function disposePinnedOffsets(wrapper) {
        var state = pinObservers.get(wrapper);
        if (!state) return;
        clearTimeout(state.timer);
        state.observer.disconnect();
        pinObservers.delete(wrapper);
    }

    return { startColumnResize, attachColumnResize, detachColumnResize, directionOf, measureColumn, downloadFile, setIndeterminate, focusGridCell, attachGridKeys, detachGridKeys, scrollGridToIndex, attachArrowKeyPrevent, focusColumnMenu, focusColumnFilter, focusHeader, observePinnedOffsets, disposePinnedOffsets };
})();
