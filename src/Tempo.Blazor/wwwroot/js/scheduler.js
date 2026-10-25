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
    var ROVING_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'];

    /**
     * Whether a keydown would scroll the page instead of moving the roving stop. Space is guarded
     * only on a button or a grid cell; a modified chord is the browser's own gesture.
     * @param {{ key?: string, altKey?: boolean, ctrlKey?: boolean, metaKey?: boolean, shiftKey?: boolean, target?: any }} event
     */
    function shouldSuppressRovingScroll(event) {
        if (!event) return false;
        var key = event.key;
        var isSpace = key === ' ' || key === 'Spacebar';
        if (!isSpace && ROVING_KEYS.indexOf(key) < 0) return false;
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
        var target = event.target;
        if (!target || typeof target.closest !== 'function') return false;
        var stop = target.closest('[data-tm-roving]');
        if (!stop) return false;
        if (!isSpace) return true;
        var role = stop.getAttribute && stop.getAttribute('role');
        return role === 'button' || role === 'gridcell';
    }
    var MIN_SIZE = 10;       // px: an event is never dragged smaller than this
    var THRESHOLD = 2;       // px of movement before a mouse/pen press becomes a resize
    var LONG_PRESS_MS = 400; // touch only: a quick swipe scrolls; hold, then drag, resizes

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
                    // Touch only (the helper ignores it for mouse and pen). A finger that moves before
                    // the hold is a scroll, not a resize — Chromium's touch adjustment snaps a swipe
                    // that starts on the event body onto this handle, and threshold 2 used to commit it.
                    longPressMs: LONG_PRESS_MS,
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
        /**
         * One keydown listener per scheduler root that stops the browser scrolling the page while
         * the roving keyboard moves inside it. Only a roving stop is guarded, and a modified chord
         * is left to the browser. Losing this script only restores the scrolling.
         */
        attachScrollGuard: function (root) {
            if (!root || root.__tmSchedulerScrollGuard) return;
            var handler = function (event) {
                // Only a key whose target is inside this scheduler. A document listener would also
                // swallow the arrows of a grid the page hosts beside it.
                if (!root.contains(event.target)) return;
                if (!shouldSuppressRovingScroll(event)) return;
                event.preventDefault();
            };
            // Capture on the root: the cell stops a bubbling keydown, so a bubble listener never sees it.
            root.addEventListener('keydown', handler, true);
            // preventDefault stops the key's own scroll. Moving the DOM focus afterwards still scrolls
            // the page, so the page position is put back once the focus has landed.
            var restore = function () {
                if (window.scrollX !== root.__tmSchedulerScrollX || window.scrollY !== root.__tmSchedulerScrollY)
                    window.scrollTo(root.__tmSchedulerScrollX, root.__tmSchedulerScrollY);
            };
            var remember = function () {
                root.__tmSchedulerScrollX = window.scrollX;
                root.__tmSchedulerScrollY = window.scrollY;
            };
            root.addEventListener('keydown', remember, true);
            root.addEventListener('focusin', restore);
            root.__tmSchedulerScrollGuard = handler;
            root.__tmSchedulerScrollRestore = restore;
        },

        /** Removes the guard from the element it was added to, in the same phase. */
        detachScrollGuard: function (root) {
            if (!root || !root.__tmSchedulerScrollGuard) return;
            root.removeEventListener('keydown', root.__tmSchedulerScrollGuard, true);
            root.removeEventListener('focusin', root.__tmSchedulerScrollRestore);
            delete root.__tmSchedulerScrollGuard;
            delete root.__tmSchedulerScrollRestore;
        },

        /** The element's viewport box, so a keyboard context menu can open on the cell itself. */
        elementPoint: function (element) {
            if (!element || typeof element.getBoundingClientRect !== 'function') return [0, 0];
            var box = element.getBoundingClientRect();
            return [box.left, box.top];
        },

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
         * Move the DOM focus onto the roving stop the keyboard handler just selected. The tabindex
         * change alone leaves the focus on the element the key was pressed in. preventScroll asks the
         * browser not to jump; it ignores that for a cell below the fold, so the page scroll is put back.
         */
        focusRovingStop: function (root, selector) {
            if (!root) return;
            var stop = root.querySelector(selector);
            if (!stop || typeof stop.focus !== 'function') return;
            // The browser scrolls a newly focused cell into view even with preventScroll, which
            // jumps the page by a viewport. Remember the scroll and put it back.
            var x = window.scrollX;
            var y = window.scrollY;
            try { stop.focus({ preventScroll: true }); } catch (e) { stop.focus(); }
            if (window.scrollX !== x || window.scrollY !== y) window.scrollTo(x, y);
            // The page must not jump, but the grid's own scroller must show the slot below its sticky head.
            var scroller = typeof stop.closest === 'function' ? stop.closest('[data-tm-scroll]') : null;
            if (!scroller || typeof scroller.getBoundingClientRect !== 'function') return;
            var head = scroller.querySelector('.tm-scheduler-timegrid-head');
            var headHeight = head && typeof head.getBoundingClientRect === 'function' ? head.getBoundingClientRect().height : 0;
            var box = stop.getBoundingClientRect();
            var frame = scroller.getBoundingClientRect();
            if (box.top < frame.top + headHeight) scroller.scrollTop -= frame.top + headHeight - box.top;
            else if (box.bottom > frame.bottom) scroller.scrollTop += box.bottom - frame.bottom;
        },

        /** The slot identified by its start (and resource, when the grid has them), inside the grid. */
        slotPoint: function (root, start, resourceId) {
            if (!root || typeof root.querySelector !== 'function') return [0, 0];
            var selector = '[data-start="' + start + '"]';
            if (resourceId) selector += '[data-resource="' + resourceId + '"]';
            return this.elementPoint(root.querySelector(selector));
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
