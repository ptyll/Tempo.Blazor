// ═══════════════════════════════════════════════════════════════
// Tempo.Blazor Dashboard JS interop — widget move & resize.
//
// The gesture — Pointer Events, pointer capture by pointerId, threshold, Escape /
// pointercancel cancel — is the shared core helper pointer-drag.js (Fáze 5 of the dashboard
// redesign): HTML5 drag-and-drop and mouse events are gone, so a mouse, a finger and a pen
// all work. touch-action: none lives on the handles in _dashboard.css, so the page keeps
// scrolling everywhere else. This classic script only owns the grid maths: the pointer-to-cell
// snap, the resize clamp (from the widget's data-min-*/data-max-* attributes the component
// renders out of the WidgetDefinition; the authoritative clamp stays in C#), the drop preview
// and the two commits through the unchanged JSInvokable contract
// (OnGridPositionChanged / OnWidgetResized). The keyboard alternative remains the grab mode
// on the drag-handle button (Blazor side, Fáze 4).
// ═══════════════════════════════════════════════════════════════

(function () {
    'use strict';

    // Resolved NOW: document.currentScript is only set while the classic script runs synchronously.
    var helperUrl = (function () {
        try {
            var script = document.currentScript;
            if (script && script.src) return new URL('pointer-drag.js', script.src).href;
        } catch (e) { /* fall through to the conventional core location */ }
        return '_content/Tempo.Blazor/js/pointer-drag.js';
    })();

    var DRAG_HANDLE_SELECTOR = '.tm-widget-drag-handle';
    var RESIZE_HANDLE_SELECTOR = '.tm-widget-resize-handle';
    var PREVIEW_SELECTOR = '.tm-widget-drop-preview';
    var MOVE_THRESHOLD_PX = 4; // a tap on the handle stays the keyboard-grab toggle click

    /** Cell width in px for the current metrics (columns of `col` px separated by `gap`). */
    function columnStep(rect, m) {
        return (rect.width - (m.columns - 1) * m.gap) / m.columns + m.gap;
    }

    /**
     * Snaps a pointer position to the hovered grid cell (0-based, clamped into the grid).
     * Exported on window.tempoDashboard for the Node unit tests.
     */
    function cellFromPointer(rect, pointerX, pointerY, m) {
        var stepX = columnStep(rect, m);
        var stepY = m.rowHeight + m.gap;
        var col = Math.round((pointerX - rect.left) / stepX);
        var row = Math.round((pointerY - rect.top) / stepY);
        return {
            col: Math.max(0, Math.min(m.columns - 1, col)),
            row: Math.max(0, row),
        };
    }

    /**
     * Converts a dragged pixel size into a cell count, clamped by the widget's data-* min/max
     * (grid cells, rendered from the WidgetDefinition; max 0 = unlimited). `ceiling` is the hard
     * axis bound — the column count for widths, 0 (unlimited) for heights, which may exceed the
     * column count. Exported for the Node unit tests.
     */
    function cellsFromSize(sizePx, stepPx, min, max, ceiling) {
        var cells = Math.max(1, Math.round(sizePx / stepPx));
        cells = Math.max(min, cells);
        if (max > 0) cells = Math.min(max, cells);
        return ceiling > 0 ? Math.min(ceiling, cells) : cells;
    }

    function num(value, fallback) {
        var parsed = parseInt(value, 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
    }

    /** The metrics object the pure maths helpers expect (columns/rowHeight/gap). */
    function metricsOf(dashboard) {
        return { columns: dashboard.gridColumns, rowHeight: dashboard.rowHeight, gap: dashboard.gap };
    }

    window.tempoDashboard = {
        dashboards: new Map(),

        /** Grid metrics in px cells — replaced by readGridMetrics from the CSS custom properties. */
        cellFromPointer: cellFromPointer,
        cellsFromSize: cellsFromSize,

        init: function (element, dotNetRef) {
            var id = element.id || Math.random().toString(36).slice(2, 11);
            element.id = id;

            var dashboard = {
                element: element,
                dotNetRef: dotNetRef,
                gridColumns: 12,
                rowHeight: 60,
                gap: 16,
                _grid: null,
                _drags: [],
                _session: null,
                destroyed: false,
                ready: null,
            };

            this.dashboards.set(id, dashboard);
            this._attach(dashboard);

            return id;
        },

        /** Resolves when the pointer helper is attached (or failed to load). */
        _attach: function (dashboard) {
            var grid = dashboard.element.querySelector('.tm-dashboard-grid');
            if (!grid) return;

            dashboard._grid = grid;
            this.readGridMetrics(dashboard);

            var self = this;
            dashboard.ready = import(helperUrl).then(function (helper) {
                // destroy() won the race against the module load.
                if (dashboard.destroyed) return;

                // Move: from the drag handle, after a 4px threshold so a tap keeps its click
                // (the keyboard grab toggle). touch-action is left to the CSS on the handle.
                dashboard._drags.push(helper.attach(grid, {
                    selector: DRAG_HANDLE_SELECTOR,
                    threshold: MOVE_THRESHOLD_PX,
                    dragClass: null,
                    onPress: function (info) { return self._pressMove(dashboard, info); },
                    onStart: function (info) { self._startMove(dashboard, info); },
                    onMove: function (info) { self._previewMove(dashboard, info); },
                    onEnd: function (info) { self._endMove(dashboard, info); },
                    onCancel: function () { /* nothing committed */ },
                    onRelease: function () { self._releaseMove(dashboard); },
                }));

                // Resize: every press on a resize handle is a resize (threshold 0).
                dashboard._drags.push(helper.attach(grid, {
                    selector: RESIZE_HANDLE_SELECTOR,
                    threshold: 0,
                    dragClass: null,
                    onPress: function (info) { return self._pressResize(dashboard, info); },
                    onMove: function (info) { self._previewResize(dashboard, info); },
                    onEnd: function () { self._endResize(dashboard); },
                    onCancel: function () { /* nothing committed */ },
                    onRelease: function () { self._releaseResize(dashboard); },
                }));
            }).catch(function (err) {
                console.warn('Dashboard: pointer-drag.js could not be loaded; widget drag/resize is unavailable', err);
            });
        },

        /**
         * The grid publishes its own metrics as custom properties. Reading them at init and at
         * the start of a gesture keeps the math on the columns, row height and gap the
         * stylesheet renders now.
         */
        readGridMetrics: function (dashboard) {
            var grid = dashboard._grid || dashboard.element.querySelector('.tm-dashboard-grid');
            if (!grid) return;
            var gridStyle = getComputedStyle(grid);
            var columns = parseFloat(gridStyle.getPropertyValue('--grid-columns'));
            var rowHeight = parseFloat(gridStyle.getPropertyValue('--row-height'));
            var gap = parseFloat(gridStyle.getPropertyValue('--grid-gap'));
            if (Number.isFinite(columns) && columns > 0) dashboard.gridColumns = columns;
            if (Number.isFinite(rowHeight) && rowHeight > 0) dashboard.rowHeight = rowHeight;
            if (Number.isFinite(gap) && gap >= 0) dashboard.gap = gap;
        },

        // ── move ─────────────────────────────────────────────────────────────────────────────

        _pressMove: function (dashboard, info) {
            var widget = info.target && info.target.closest('.tm-widget');
            if (!widget || !widget.dataset.instanceId) return false;

            this.readGridMetrics(dashboard);
            dashboard._session = {
                mode: 'move',
                widget: widget,
                instanceId: widget.dataset.instanceId,
            };
            return true;
        },

        _startMove: function (dashboard, info) {
            var s = dashboard._session;
            if (!s) return;
            s.widget.classList.add('tm-widget--dragging');
            this._previewAt(dashboard, info.x, info.y);
        },

        _previewMove: function (dashboard, info) {
            if (!dashboard._session) return;
            this._previewAt(dashboard, info.x, info.y);
        },

        _previewAt: function (dashboard, pointerX, pointerY) {
            var s = dashboard._session;
            var grid = dashboard._grid;
            if (!s || !grid) return;
            var cell = cellFromPointer(grid.getBoundingClientRect(), pointerX, pointerY, metricsOf(dashboard));
            this.updateDropPreview(dashboard, cell.col, cell.row);
        },

        _endMove: function (dashboard, info) {
            var s = dashboard._session;
            var grid = dashboard._grid;
            if (!s || !grid) return;

            var cell = cellFromPointer(grid.getBoundingClientRect(), info.x, info.y, metricsOf(dashboard));
            if (dashboard.dotNetRef) {
                dashboard.dotNetRef.invokeMethodAsync('OnGridPositionChanged', s.instanceId, cell.col, cell.row)
                    .catch(function (err) {
                        console.warn('Dashboard: failed to notify Blazor about the drop', err);
                    });
            }
        },

        _releaseMove: function (dashboard) {
            var s = dashboard._session;
            if (!s || s.mode !== 'move') return;
            s.widget.classList.remove('tm-widget--dragging');
            this.clearDropPreview(dashboard);
            dashboard._session = null;
        },

        // ── resize ───────────────────────────────────────────────────────────────────────────

        _pressResize: function (dashboard, info) {
            var handle = info.target;
            var widget = handle && handle.closest('.tm-widget');
            if (!widget || !widget.dataset.instanceId) return false;

            var grid = dashboard._grid;
            if (!grid) return false;

            this.readGridMetrics(dashboard);
            var rect = widget.getBoundingClientRect();
            var gridRect = grid.getBoundingClientRect();
            var stepX = columnStep(gridRect, metricsOf(dashboard));
            var stepY = dashboard.rowHeight + dashboard.gap;

            dashboard._session = {
                mode: 'resize',
                widget: widget,
                instanceId: widget.dataset.instanceId,
                direction: this.getResizeDirection(handle),
                gridRect: gridRect,
                stepX: stepX,
                stepY: stepY,
                startWidth: rect.width,
                startHeight: rect.height,
                minWidth: num(widget.dataset.minW, 1),
                maxWidth: num(widget.dataset.maxW, 0), // 0 = unlimited: the grid is the ceiling
                minHeight: num(widget.dataset.minH, 1),
                maxHeight: num(widget.dataset.maxH, 0),
                // The rendered cell values: the untouched axis of a direction-only resize commits
                // exactly what Blazor rendered, never a pixel-recomputed approximation of it.
                startColumns: parseInt(widget.style.getPropertyValue('--tm-w-span'), 10) ||
                    Math.max(1, Math.round(rect.width / stepX)),
                startRows: parseInt(widget.style.getPropertyValue('--tm-w-rows'), 10) ||
                    Math.max(1, Math.round(rect.height / stepY)),
            };
            widget.classList.add('tm-widget--resizing');
            return true;
        },

        /** Live size of the widget while the pointer moves, painted in whole cells. */
        _previewResize: function (dashboard, info) {
            var s = dashboard._session;
            if (!s || s.mode !== 'resize') return;

            var widthPx = s.startWidth;
            var heightPx = s.startHeight;
            if (s.direction.indexOf('e') !== -1) widthPx = s.startWidth + (info.x - info.startX);
            if (s.direction.indexOf('w') !== -1) widthPx = s.startWidth - (info.x - info.startX);
            if (s.direction.indexOf('s') !== -1) heightPx = s.startHeight + (info.y - info.startY);
            if (s.direction.indexOf('n') !== -1) heightPx = s.startHeight - (info.y - info.startY);

            // Paint whole cells so the live size matches what the commit will ask Blazor for.
            // Each axis only follows the pointer when a handle on that axis is dragged; the other
            // keeps its rendered value, so an e/w resize never changes the row count (and an
            // unlimited maxH is not capped at the column count — ceiling 0 for rows).
            var touchesX = s.direction.indexOf('e') !== -1 || s.direction.indexOf('w') !== -1;
            var touchesY = s.direction.indexOf('n') !== -1 || s.direction.indexOf('s') !== -1;
            var columns = touchesX
                ? cellsFromSize(widthPx, s.stepX, s.minWidth, s.maxWidth, metricsOf(dashboard).columns)
                : s.startColumns;
            var rows = touchesY
                ? cellsFromSize(heightPx, s.stepY, s.minHeight, s.maxHeight, 0)
                : s.startRows;
            s.columns = columns;
            s.rows = rows;

            var style = s.widget.style;
            style.width = (columns * (s.stepX - dashboard.gap) + (columns - 1) * dashboard.gap) + 'px';
            style.height = (rows * s.stepY - dashboard.gap) + 'px';
        },

        _endResize: function (dashboard) {
            var s = dashboard._session;
            if (!s || s.mode !== 'resize') return;

            // Restore the Blazor-rendered geometry FIRST: under Blazor WebAssembly the render
            // that applies the committed size runs inside invokeMethodAsync.
            if (s.columns !== undefined && dashboard.dotNetRef) {
                var width = s.columns;
                var height = s.rows;
                dashboard.dotNetRef.invokeMethodAsync('OnWidgetResized', s.instanceId, width, height)
                    .catch(function (err) {
                        console.warn('Dashboard: failed to notify Blazor about the resize', err);
                    });
            }
        },

        _releaseResize: function (dashboard) {
            var s = dashboard._session;
            if (!s || s.mode !== 'resize') return;
            s.widget.classList.remove('tm-widget--resizing');
            s.widget.style.width = '';
            s.widget.style.height = '';
            dashboard._session = null;
        },

        getResizeDirection: function (handle) {
            if (handle.classList.contains('tm-widget-resize-n')) return 'n';
            if (handle.classList.contains('tm-widget-resize-s')) return 's';
            if (handle.classList.contains('tm-widget-resize-e')) return 'e';
            if (handle.classList.contains('tm-widget-resize-w')) return 'w';
            if (handle.classList.contains('tm-widget-resize-ne')) return 'ne';
            if (handle.classList.contains('tm-widget-resize-nw')) return 'nw';
            if (handle.classList.contains('tm-widget-resize-se')) return 'se';
            if (handle.classList.contains('tm-widget-resize-sw')) return 'sw';
            return 'se';
        },

        updateDropPreview: function (dashboard, col, row) {
            var grid = dashboard._grid;
            if (!grid) return;
            var preview = grid.querySelector(PREVIEW_SELECTOR);
            if (!preview) {
                preview = document.createElement('div');
                preview.className = 'tm-widget-drop-preview';
                grid.appendChild(preview);
            }

            if (dashboard._session) {
                var width = parseInt(dashboard._session.widget.style.getPropertyValue('--tm-w-span')) || 4;
                var height = parseInt(dashboard._session.widget.style.getPropertyValue('--tm-w-rows')) || 4;
                // A wide widget dropped near the right edge must not overflow the grid
                // (the C# side clamps the commit the same way in OnGridPositionChanged).
                col = Math.max(0, Math.min(col, metricsOf(dashboard).columns - width));

                preview.style.setProperty('grid-column', (col + 1) + ' / span ' + width);
                preview.style.setProperty('grid-row', (row + 1) + ' / span ' + height);
            }
        },

        clearDropPreview: function (dashboard) {
            var grid = dashboard._grid;
            if (!grid) return;
            var preview = grid.querySelector(PREVIEW_SELECTOR);
            if (preview) {
                preview.remove();
            }
        },

        // F4: a freshly added widget may land below the fold (mobile 390 px) — bring it into view
        // so the brief highlight is actually seen. Respects prefers-reduced-motion.
        scrollWidgetIntoView: function (element, instanceId) {
            var widget = element.querySelector('[data-instance-id="' + instanceId + '"]');
            if (!widget) {
                return;
            }
            var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            widget.scrollIntoView({ block: 'nearest', behavior: reduceMotion ? 'auto' : 'smooth' });
        },

        destroy: function (element) {
            var id = element.id;
            var dashboard = this.dashboards.get(id);
            if (!dashboard) return;

            dashboard.destroyed = true;
            for (var i = 0; i < dashboard._drags.length; i++) {
                dashboard._drags[i].detach();
            }
            dashboard._drags = [];
            if (dashboard._session) {
                if (dashboard._session.mode === 'move') {
                    dashboard._session.widget.classList.remove('tm-widget--dragging');
                    this.clearDropPreview(dashboard);
                } else {
                    dashboard._session.widget.classList.remove('tm-widget--resizing');
                    dashboard._session.widget.style.width = '';
                    dashboard._session.widget.style.height = '';
                }
                dashboard._session = null;
            }
            this.dashboards.delete(id);
        }
    };
})();
