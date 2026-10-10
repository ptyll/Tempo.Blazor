// ═══════════════════════════════════════════════════════════════
// Tempo.Blazor Scheduler JS Interop
// Resize of scheduler events (time grid + timeline) with a mouse, a finger or a pen.
//
// The gesture itself — Pointer Events, pointer capture by pointerId, threshold, Escape /
// pointercancel / lost-capture cancel, touch-action — is the shared core helper pointer-drag.js.
// This file is a classic script (window.TempoBlazor.Scheduler), so it loads the helper with a
// dynamic import() from the same _content folder it was served from. It only owns the scheduler
// maths: slot snapping, the live size of the event and the NotifyResizeComplete call.
// ═══════════════════════════════════════════════════════════════

(function () {
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

    var HANDLE_SELECTOR = '.tm-scheduler-event-resize-handle';
    var MIN_SIZE = 10;       // px: an event is never dragged smaller than this
    var THRESHOLD = 2;       // px of movement before a press becomes a resize

    // Namespace
    window.TempoBlazor = window.TempoBlazor || {};
    window.TempoBlazor.Scheduler = {

        _dotNetRefs: new Map(),
        _drag: null,
        _generation: 0,

        /** Resolves when the pointer helper is attached (or failed to load). */
        ready: Promise.resolve(),

        /**
         * Initialize resize handling for all schedulers on the page.
         * Safe to call repeatedly: a previous attachment is detached first.
         */
        init: function () {
            var self = this;
            this._detachDrag();
            var generation = ++this._generation;

            this.ready = import(helperUrl).then(function (helper) {
                // init/destroy was called again while the module was loading.
                if (generation !== self._generation) return;
                self._drag = helper.attach(document, {
                    selector: HANDLE_SELECTOR,
                    threshold: THRESHOLD,
                    // The handle keeps its click inert after a resize; HTML5 drag-and-drop of the
                    // event body is untouched because only the handle selector starts a gesture.
                    onPress: function (info) { return self._press(info); },
                    onMove: function (info) { self._move(info); },
                    onEnd: function () { self._end(); },
                    onCancel: function () { self._rollback(); },
                    onRelease: function () { self._release(); }
                });
            }).catch(function (err) {
                console.warn('Scheduler: pointer-drag.js could not be loaded; event resize is unavailable', err);
            });
        },

        /**
         * Register a .NET instance reference for a scheduler container element.
         * Called from C# OnAfterRenderAsync.
         */
        registerInstance: function (element, dotNetRef) {
            if (element) {
                this._dotNetRefs.set(element, dotNetRef);
            }
        },

        /**
         * Unregister a .NET instance reference.
         * Called from C# DisposeAsync.
         */
        unregisterInstance: function (element) {
            if (element) {
                this._dotNetRefs.delete(element);
            }
        },

        /**
         * A press landed on a resize handle: capture the geometry of the event and its container.
         * Returning false vetoes the gesture (nothing is touched).
         */
        _press: function (info) {
            var resizeHandle = info.target;
            if (!resizeHandle) return false;

            // Find the parent event element (could be timegrid or timeline)
            var eventElement = resizeHandle.closest('.tm-scheduler-timegrid-event, .tm-scheduler-timeline-event');
            if (!eventElement) return false;

            // Determine resize edge (left or right, top or bottom)
            var resizeEdge = resizeHandle.dataset.resizeEdge || 'right';

            // Determine if this is a timeline or timegrid event
            var isTimeline = eventElement.classList.contains('tm-scheduler-timeline-event');

            // Get the appropriate container for calculations
            var container = isTimeline
                ? resizeHandle.closest('.tm-scheduler-timeline-row-body')
                : resizeHandle.closest('.tm-scheduler-timegrid-body');
            if (!container) return false;

            // Get slot duration from data attribute or default
            var schedulerContainer = isTimeline
                ? container.closest('.tm-scheduler-timeline')
                : container.closest('.tm-scheduler-timegrid');
            var slotMinutes = parseInt(schedulerContainer && schedulerContainer.dataset.slotDuration) || 30;

            var containerRect = container.getBoundingClientRect();
            var eventRect = eventElement.getBoundingClientRect();
            var containerSize = isTimeline ? containerRect.width : containerRect.height;
            if (!(containerSize > 0)) return false;

            this._session = {
                eventElement: eventElement,
                isTimeline: isTimeline,
                resizeEdge: resizeEdge,
                slotMinutes: slotMinutes,
                eventId: eventElement.dataset.eventId,
                dotNetRef: schedulerContainer ? this._dotNetRefs.get(schedulerContainer) : null,
                originalDraggable: eventElement.draggable,
                // Blazor renders the geometry inline (percentages of the day). A cancel must put THOSE back:
                // clearing the properties would let the event collapse to its CSS min-height.
                originalStyle: {
                    left: eventElement.style.left || '',
                    top: eventElement.style.top || '',
                    width: eventElement.style.width || '',
                    height: eventElement.style.height || ''
                },
                originalSize: isTimeline ? eventRect.width : eventRect.height,
                originalLeft: isTimeline ? eventRect.left - containerRect.left : 0,
                originalTop: !isTimeline ? eventRect.top - containerRect.top : 0,
                // Minutes per pixel ratio (24 hours = 1440 minutes)
                minutesPerPixel: (24 * 60) / containerSize,
                delta: 0
            };

            // Temporarily disable HTML5 dragging on the event element while its edge is dragged.
            eventElement.draggable = false;
            eventElement.setAttribute('data-resizing', 'true');
            return true;
        },

        /** Live size of the event while the pointer moves. */
        _move: function (info) {
            var s = this._session;
            if (!s) return;

            var deltaPos = s.isTimeline ? info.dx : info.dy;
            var deltaMinutes = Math.round(deltaPos * s.minutesPerPixel / s.slotMinutes) * s.slotMinutes;
            if (deltaMinutes === s.delta) return;

            var style = s.eventElement.style;
            var newSize;
            if (s.isTimeline) {
                if (s.resizeEdge === 'left') {
                    newSize = s.originalSize - deltaPos;
                    if (newSize <= MIN_SIZE) return;
                    style.left = (s.originalLeft + deltaPos) + 'px';
                    style.width = newSize + 'px';
                } else {
                    newSize = s.originalSize + deltaPos;
                    if (newSize <= MIN_SIZE) return;
                    style.width = newSize + 'px';
                }
            } else if (s.resizeEdge === 'top') {
                newSize = s.originalSize - deltaPos;
                if (newSize <= MIN_SIZE) return;
                style.top = (s.originalTop + deltaPos) + 'px';
                style.height = newSize + 'px';
            } else {
                newSize = s.originalSize + deltaPos;
                if (newSize <= MIN_SIZE) return;
                style.height = newSize + 'px';
            }

            // Only a delta whose size passed the minimum guard can be committed.
            // deltaMinutes is negative when dragging left/up, positive when dragging right/down:
            // left edge drag-left = earlier start, right edge drag-right = later end.
            s.delta = deltaMinutes;
        },

        /** The pointer was released after a real resize: tell .NET once. */
        _end: function () {
            var s = this._session;
            if (!s) return;
            // Put the Blazor-rendered geometry back FIRST: under Blazor WebAssembly the render that applies
            // the new size runs synchronously inside invokeMethodAsync, and a later cleanup would overwrite it.
            this._restoreGeometry(s);
            if (s.eventId && s.delta !== 0) {
                this._notifyBlazorResize(s.dotNetRef, s.eventId, s.delta, s.resizeEdge);
            }
        },

        /** Escape, pointercancel, lost capture, destroy: nothing is committed. */
        _rollback: function () {
            if (this._session) this._session.delta = 0;
        },

        /** Runs on EVERY path (tap, end, cancel, veto-after-start): put the event back. */
        _release: function () {
            var s = this._session;
            this._session = null;
            if (!s) return;

            s.eventElement.draggable = s.originalDraggable;
            s.eventElement.removeAttribute('data-resizing');
            this._restoreGeometry(s);
        },

        /** Puts back the inline geometry Blazor rendered; once per session (after a commit Blazor re-renders the new one). */
        _restoreGeometry: function (s) {
            if (s.restored) return;
            s.restored = true;
            var style = s.eventElement.style;
            if (s.isTimeline) {
                style.width = s.originalStyle.width;
                if (s.resizeEdge === 'left') style.left = s.originalStyle.left;
            } else {
                style.height = s.originalStyle.height;
                if (s.resizeEdge === 'top') style.top = s.originalStyle.top;
            }
        },

        /**
         * Notify Blazor about the resize completion via instance dotNetRef
         */
        _notifyBlazorResize: function (dotNetRef, eventId, deltaMinutes, resizeEdge) {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('NotifyResizeComplete', eventId, deltaMinutes, resizeEdge)
                    .catch(function (err) {
                        console.warn('Scheduler: Failed to notify Blazor about resize', err);
                    });
            }
        },

        /**
         * Cancel an ongoing resize operation (restores the event, notifies nothing)
         */
        cancelResize: function () {
            if (this._drag) this._drag.cancel('cancelled');
        },

        _detachDrag: function () {
            var drag = this._drag;
            this._drag = null;
            if (drag) drag.detach();
        },

        /**
         * Clean up scheduler event listeners
         * Call when the scheduler component is disposed
         */
        destroy: function () {
            this._generation++;
            this._detachDrag();
            this._dotNetRefs.clear();
        }
    };

    window.TempoBlazor.Scheduler._session = null;

    // Auto-initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            window.TempoBlazor.Scheduler.init();
        });
    } else {
        // DOM already loaded
        window.TempoBlazor.Scheduler.init();
    }

})();
