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
    var gestureUrl = helperUrl.replace('pointer-drag.js', 'pointer-gesture.js');

    var HANDLE_SELECTOR = '.tm-scheduler-event-resize-handle';
    // The event body. A touch long-press moves it; a mouse or a pen press is HTML5 drag-and-drop.
    // The keyboard alternative is restricted to a draggable event, so a read-only one does not swallow
    // Alt+Left/Right (the browser's Back/Forward).
    var MOVE_SELECTOR = '.tm-scheduler-timegrid-event[data-event-id][draggable="true"], .tm-scheduler-timeline-event[data-event-id][draggable="true"], .tm-scheduler-month-event[data-event-id][draggable="true"]';
    var DROP_TARGET = 'tm-scheduler-month-day--drop-target tm-scheduler-timegrid-slot--drop-target tm-scheduler-timeline-slot--drop-target';
    /** A culture-aware time. Falls back to HH:mm when Intl is missing (the node tests). */
    function clock(minute, culture) {
        var wrapped = ((minute % (24 * 60)) + 24 * 60) % (24 * 60);
        var hours = Math.floor(wrapped / 60);
        var minutes = Math.round(wrapped % 60);
        if (culture && typeof Intl !== 'undefined' && Intl.DateTimeFormat) {
            return new Intl.DateTimeFormat(culture, { hour: 'numeric', minute: '2-digit' })
                .format(new Date(2026, 0, 1, hours, minutes));
        }
        return (hours < 10 ? '0' : '') + hours + ':' + (minutes < 10 ? '0' : '') + minutes;
    }

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
    // Enter/Space that opened the title popover. A native day button focused while that key is still
    // down takes the browser's default action for it and picks the day, which closes the popover
    // the key just opened. Tracked from load so the opening keydown is not missed.
    var openingKeys = new Set();
    if (typeof window.addEventListener === 'function') {
        window.addEventListener('keydown', function (e) {
            if (e && (e.key === 'Enter' || e.key === ' ')) openingKeys.add(e.key);
        }, true);
        window.addEventListener('keyup', function (e) {
            if (e) openingKeys.delete(e.key);
        }, true);
        // A keyup that never arrives (the window lost focus mid-press) must not pin the deferral.
        window.addEventListener('blur', function () { openingKeys.clear(); });
    }

    var MIN_SIZE = 10;       // px: an event is never dragged smaller than this
    var THRESHOLD = 2;       // px of movement before a mouse/pen press becomes a resize
    var LONG_PRESS_MS = 400; // touch only: a quick swipe scrolls; hold, then drag, resizes

    // Namespace
    window.TempoBlazor = window.TempoBlazor || {};
    window.TempoBlazor.Scheduler = {

        _dotNetRefs: new Map(),
        _drag: null,
        _move: null,
        _keyboard: null,
        _generation: 0,

        /** Resolves when the pointer helper is attached (or failed to load). */
        ready: Promise.resolve(),

        /**
         * Scrolls the grid's own scroller so <paramref>minutes</paramref> sits near the top, under
         * the sticky head. The root carries data-range-start and data-range-minutes, so a clipped
         * day scrolls by the same range the resize maths use. A time outside the range is a no-op.
         */
        scrollToTime: function (root, minutes) {
            if (!root || typeof root.querySelector !== 'function') return;
            // The timeline root is itself the scroller; the time grid wraps one.
            var scroller = root.matches && root.matches('[data-tm-scroll]')
                ? root
                : root.querySelector('[data-tm-scroll]');
            if (!scroller) return;
            var start = parseInt(root.dataset.rangeStart) || 0;
            var range = parseInt(root.dataset.rangeMinutes) || 24 * 60;
            var offset = minutes - start;
            if (offset < 0 || offset > range || range <= 0) return;
            var fraction = offset / range;
            if (scroller.dataset && scroller.dataset.tmScroll === 'timeline') {
                var label = scroller.querySelector('.tm-scheduler-timeline-resource-header');
                var labelWidth = label ? label.offsetWidth : 0;
                scroller.scrollLeft = (scroller.scrollWidth - labelWidth) * fraction;
                return;
            }
            var head = root.querySelector('.tm-scheduler-timegrid-head');
            var headHeight = head ? head.offsetHeight : 0;
            scroller.scrollTop = Math.max(0, (scroller.scrollHeight - headHeight) * fraction);
        },

        /**
         * Moves focus to the selected day in the title calendar owned by <paramref>root</paramref>,
         * or to today. The panel renders in place inside the title element the caller holds, so the
         * lookup stays on that root — a document query, including <c>:popover-open</c>, throws in
         * engines that do not know the selector and is forbidden here.
         */
        focusCalendarDay: function (root) {
            if (!root || typeof root.querySelector !== 'function') return;
            var open = root.querySelector('.tm-calendar-view');
            var day = open && open.querySelector('.tm-cal-day--selected, .tm-cal-day--today');
            if (!day || typeof day.focus !== 'function') return;
            var focus = function () {
                if (day.isConnected === false) return;
                try { day.focus({ preventScroll: true }); }
                catch (e) { day.focus(); }
            };
            // Defer past the opening keydown's default action. Focusing a native day button inside
            // that action makes the browser activate it, which picks the day and closes the popover
            // the key just opened. A key that is still down waits for its release so a repeat cannot
            // do the same.
            var arm = function () {
                if (openingKeys.size === 0 || typeof window.addEventListener !== 'function') {
                    focus();
                    return;
                }
                var timer = setTimeout(finish, 1000);
                function finish() {
                    clearTimeout(timer);
                    window.removeEventListener('keyup', onUp, true);
                    focus();
                }
                function onUp(e) {
                    if (e) openingKeys.delete(e.key);
                    if (openingKeys.size > 0) return;
                    finish();
                }
                window.addEventListener('keyup', onUp, true);
            };
            setTimeout(arm, 0);
        },

        /**
         * Initialize resize handling for all schedulers on the page.
         * Safe to call repeatedly: a previous attachment is detached first.
         */
        init: function () {
            var self = this;
            this._detachDrag();
            var generation = ++this._generation;

            this.ready = Promise.all([import(helperUrl), import(gestureUrl).catch(function () { return {}; })]).then(function (modules) {
                var helper = modules[0];
                self._gesture = modules[1];
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
                    onMove: function (info) { self._resize(info); },
                    onEnd: function () { self._end(); },
                    onCancel: function () { self._rollback(); },
                    onRelease: function () { self._release(); }
                });
                // A finger holds the event body, then drags it. Mouse and pen stay on HTML5
                // drag-and-drop (pointerTypes), so the desktop behaviour is unchanged.
                self._move = helper.attach(document, {
                    selector: MOVE_SELECTOR,
                    pointerTypes: ['touch'],
                    longPressMs: LONG_PRESS_MS,
                    threshold: 4,
                    onPress: function (info) { return self._movePress(info); },
                    onStart: function (info) { self._moveStart(info); },
                    onMove: function (info) { self._moveDrag(info); },
                    onEnd: function () { self._moveEnd(); },
                    onCancel: function () { self._moveRollback(); },
                    onRelease: function () { self._moveRelease(); }
                });
                // Alt+Arrow moves, Alt+Shift+Arrow resizes. The helper cancels only that chord, so a
                // plain arrow stays the roving key and Tab is never swallowed.
                self._keyboard = helper.attachKeyboardMove(document, {
                    selector: MOVE_SELECTOR,
                    shift: true,
                    onStep: function (step) { self._keyboardStep(step); }
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
            // the page, and scroll-behavior: smooth starts that scroll after focusin and keeps going
            // after a one-shot scrollTo. Measure the page when the focus or the key happens, pin it
            // until that scroll settles, then let go so a later scroll (bringing the grid into view)
            // is kept. An unset position must not scroll the page to the top.
            var releasePin = function () {
                root.__tmSchedulerScrollHold = false;
                if (root.__tmSchedulerScrollPin && typeof window.removeEventListener === 'function')
                    window.removeEventListener('scroll', root.__tmSchedulerScrollPin);
                root.__tmSchedulerScrollPin = null;
            };
            var putBack = function () {
                if (typeof root.__tmSchedulerScrollX !== 'number') return;
                window.scrollTo({ left: root.__tmSchedulerScrollX, top: root.__tmSchedulerScrollY, behavior: 'auto' });
            };
            var watch = function () {
                if (!root.__tmSchedulerScrollHold) return;
                var moved = window.scrollX !== root.__tmSchedulerScrollX || window.scrollY !== root.__tmSchedulerScrollY;
                if (moved) {
                    root.__tmSchedulerScrollStable = 0;
                    putBack();
                } else if (++root.__tmSchedulerScrollStable >= 3) {
                    releasePin();
                    return;
                }
                if (typeof window.requestAnimationFrame === 'function')
                    window.requestAnimationFrame(watch);
            };
            var arm = function () {
                var fresh = !root.__tmSchedulerScrollHold;
                if (fresh) {
                    root.__tmSchedulerScrollX = window.scrollX;
                    root.__tmSchedulerScrollY = window.scrollY;
                    root.__tmSchedulerScrollHold = true;
                    root.__tmSchedulerScrollStable = 0;
                }
                if (!root.__tmSchedulerScrollPin && typeof window.addEventListener === 'function') {
                    root.__tmSchedulerScrollPin = function () {
                        if (!root.__tmSchedulerScrollHold) return;
                        if (window.scrollX === root.__tmSchedulerScrollX && window.scrollY === root.__tmSchedulerScrollY) return;
                        root.__tmSchedulerScrollStable = 0;
                        putBack();
                    };
                    window.addEventListener('scroll', root.__tmSchedulerScrollPin);
                }
                if (fresh && typeof window.requestAnimationFrame === 'function')
                    window.requestAnimationFrame(watch);
                clearTimeout(root.__tmSchedulerScrollHoldTimer);
                root.__tmSchedulerScrollHoldTimer = setTimeout(releasePin, 1000);
            };
            var restore = function (event) {
                // Focusing an off-screen slot scrolls the page after this handler, before any key.
                if (!root.__tmSchedulerScrollHold) {
                    var target = event && event.target;
                    if (!target || typeof target.closest !== 'function' || !target.closest('[data-tm-roving]')) return;
                    arm();
                    // Only the scroll the focus itself starts is corrected. Once a frame has passed
                    // without the page moving, a later scroll is the user and is kept.
                    root.__tmSchedulerScrollCorrect = true;
                    if (typeof window.requestAnimationFrame === 'function') {
                        window.requestAnimationFrame(function () { root.__tmSchedulerScrollCorrect = false; });
                    }
                }
                if (root.__tmSchedulerScrollCorrect) putBack();
            };
            // A wheel, a touch or a pointer press is the user taking the page. The pin must let go
            // at once, not a second later, or it undoes the scroll the user just started.
            var releaseOnUser = function () { releasePin(); };
            var remember = function (event) {
                if (!root.contains(event.target)) return;
                if (!shouldSuppressRovingScroll(event)) { releasePin(); return; }
                // A key measures the page where it is now. Releasing first lets arm() capture a
                // scroll that happened after the previous focus settled.
                releasePin();
                arm();
            };
            root.addEventListener('keydown', remember, true);
            root.addEventListener('focusin', restore);
            root.addEventListener('wheel', releaseOnUser, true);
            root.addEventListener('touchstart', releaseOnUser, true);
            root.addEventListener('pointerdown', releaseOnUser, true);
            root.__tmSchedulerScrollRelease = releaseOnUser;
            root.__tmSchedulerScrollGuard = handler;
            root.__tmSchedulerScrollRestore = restore;
            root.__tmSchedulerScrollRemember = remember;
        },

        /** Removes the guard from the element it was added to, in the same phase. */
        detachScrollGuard: function (root) {
            if (!root || !root.__tmSchedulerScrollGuard) return;
            root.removeEventListener('keydown', root.__tmSchedulerScrollGuard, true);
            root.removeEventListener('keydown', root.__tmSchedulerScrollRemember, true);
            root.removeEventListener('focusin', root.__tmSchedulerScrollRestore);
            root.removeEventListener('wheel', root.__tmSchedulerScrollRelease, true);
            root.removeEventListener('touchstart', root.__tmSchedulerScrollRelease, true);
            root.removeEventListener('pointerdown', root.__tmSchedulerScrollRelease, true);
            clearTimeout(root.__tmSchedulerScrollHoldTimer);
            if (root.__tmSchedulerScrollPin && typeof window.removeEventListener === 'function')
                window.removeEventListener('scroll', root.__tmSchedulerScrollPin);
            delete root.__tmSchedulerScrollGuard;
            delete root.__tmSchedulerScrollRestore;
            delete root.__tmSchedulerScrollRemember;
            delete root.__tmSchedulerScrollRelease;
            delete root.__tmSchedulerScrollCorrect;
            delete root.__tmSchedulerScrollHold;
            delete root.__tmSchedulerScrollHoldTimer;
            delete root.__tmSchedulerScrollPin;
            delete root.__tmSchedulerScrollStable;
        },

        /** The element's viewport box, so a keyboard context menu can open on the cell itself. */
        elementPoint: function (element) {
            if (!element || typeof element.getBoundingClientRect !== 'function') return [0, 0];
            var box = element.getBoundingClientRect();
            return [box.left, box.top];
        },

        /**
         * Wires the browser's beforeprint/afterprint to a [JSInvokable] OnPrintMediaAsync(bool) so a
         * Ctrl+P pins the desktop layout too. Additive: registerInstance's signature is unchanged.
         * One listener pair per root; a second attach replaces the previous pair.
         */
        attachPrintListener: function (root, dotNetRef) {
            if (!root || typeof window.addEventListener !== 'function') return;
            this.detachPrintListener(root);
            var before = function () { dotNetRef.invokeMethodAsync('OnPrintMediaAsync', true); };
            var after = function () { dotNetRef.invokeMethodAsync('OnPrintMediaAsync', false); };
            window.addEventListener('beforeprint', before);
            window.addEventListener('afterprint', after);
            root.__tmSchedulerPrint = { before: before, after: after };
        },

        detachPrintListener: function (root) {
            var pair = root && root.__tmSchedulerPrint;
            if (!pair || typeof window.removeEventListener !== 'function') return;
            window.removeEventListener('beforeprint', pair.before);
            window.removeEventListener('afterprint', pair.after);
            delete root.__tmSchedulerPrint;
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
            // The visible range, not a hard-coded day: a grid clipped to working hours publishes both.
            var rangeMinutes = parseInt(schedulerContainer && schedulerContainer.dataset.rangeMinutes) || 24 * 60;

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
                container: schedulerContainer,
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
                // Minutes per pixel of the VISIBLE range (1440 for a full day, less when clipped).
                minutesPerPixel: rangeMinutes / containerSize,
                rangeStart: parseInt(schedulerContainer && schedulerContainer.dataset.rangeStart) || 0,
                startMinute: Math.round((!isTimeline ? eventRect.top - containerRect.top : eventRect.left - containerRect.left) / containerSize * rangeMinutes)
                    + (parseInt(schedulerContainer && schedulerContainer.dataset.rangeStart) || 0),
                durationMinutes: Math.max(slotMinutes, Math.round((isTimeline ? eventRect.width : eventRect.height) / containerSize * rangeMinutes)),
                delta: 0
            };

            // Temporarily disable HTML5 dragging on the event element while its edge is dragged.
            eventElement.draggable = false;
            eventElement.setAttribute('data-resizing', 'true');
            return true;
        },

        /** Live size of the event while the pointer moves. */
        _resize: function (info) {
            var session = this._session;
            if (session && !session.scroller) {
                var factory = this._gesture && this._gesture.createAutoScroller;
                var scroller = session.container && typeof session.container.querySelector === 'function'
                    ? (session.isTimeline ? session.container : session.container.querySelector('.tm-scheduler-timegrid-scroll'))
                    : null;
                if (factory && scroller) session.scroller = factory(scroller, { axis: session.isTimeline ? 'x' : 'y' });
            }
            if (session && session.scroller) session.scroller.update(info.x, info.y);
            var s = this._session;
            if (!s) return;

            var deltaPos = s.isTimeline ? info.dx : info.dy;
            var deltaMinutes = Math.round(deltaPos * s.minutesPerPixel / s.slotMinutes) * s.slotMinutes;
            if (deltaMinutes === s.delta) return;

            var style = s.eventElement.style;
            var newSize;
            var startMinute = s.resizeEdge === 'top' || s.resizeEdge === 'left' ? s.startMinute + deltaMinutes : s.startMinute;
            var endMinute = s.resizeEdge === 'top' || s.resizeEdge === 'left' ? s.startMinute + s.durationMinutes : s.startMinute + s.durationMinutes + deltaMinutes;
            this._showTime(s, startMinute, endMinute - startMinute);
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
            if (s.scroller) s.scroller.stop();

            s.eventElement.draggable = s.originalDraggable;
            s.eventElement.removeAttribute('data-resizing');
            this._restoreGeometry(s);
            this._removeLabel(s.eventElement);
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

        // ── Touch move ──────────────────────────────────────────────────────
        // The finger holds the event body, then drags it. The live position is a translate (the
        // rendered geometry stays inline, so a cancel clears the translate and the event is back);
        // the commit is one NotifyMoveComplete with the day, the snapped minute and the resource
        // resolved from the element under the finger.

        /** A press on the event body. A handle or a non-draggable event vetoes it. */
        _movePress: function (info) {
            var eventElement = info.target;
            if (!eventElement) return false;
            // closest(MOVE_SELECTOR) walks up from a handle to the event, so the matched target is the
            // event either way. The finger's own target is what says "this press is a resize".
            var pressed = info.event && info.event.target;
            if (pressed && typeof pressed.closest === 'function' && pressed.closest('.tm-scheduler-event-resize-handle')) return false;
            if (eventElement.getAttribute('draggable') !== 'true') return false;

            var isMonth = eventElement.classList.contains('tm-scheduler-month-event');
            var isTimeline = eventElement.classList.contains('tm-scheduler-timeline-event');
            var container = isMonth
                ? eventElement.closest('.tm-scheduler-month')
                : isTimeline
                    ? eventElement.closest('.tm-scheduler-timeline')
                    : eventElement.closest('.tm-scheduler-timegrid');
            if (!container) return false;

            var column = isTimeline
                ? eventElement.closest('.tm-scheduler-timeline-row-body')
                : eventElement.closest('.tm-scheduler-timegrid-col, .tm-scheduler-timegrid-resource-col');
            var startCell = eventElement.closest('[data-date]');
            var startRow = eventElement.closest('[data-resource]');

            this._moveSession = {
                eventElement: eventElement,
                isMonth: isMonth,
                isTimeline: isTimeline,
                container: container,
                column: column,
                eventId: eventElement.dataset.eventId,
                dotNetRef: this._dotNetRefs.get(container) || null,
                slotMinutes: parseInt(container.dataset.slotDuration) || 30,
                rangeStart: parseInt(container.dataset.rangeStart) || 0,
                rangeMinutes: parseInt(container.dataset.rangeMinutes) || 24 * 60,
                startDate: startCell ? startCell.dataset.date : (container.dataset.date || null),
                startResource: startRow ? startRow.dataset.resource : null,
                grabOffset: this._grabOffset(eventElement, isTimeline, info),
                // Measured before any translate. A later rect already includes the preview, so
                // re-reading it would make the snap oscillate.
                originOffset: this._originOffset(eventElement, column, isTimeline),
                originalDraggable: eventElement.draggable,
                targetDate: null,
                targetMinute: null,
                targetResource: null
            };
            eventElement.draggable = false;
            return true;
        },

        _moveStart: function (info) {
            var session = this._moveSession;
            if (!session) return;
            session.eventElement.classList.add('tm-scheduler-event--moving');
            this._armContextMenu();
            var x = info && (info.x || (info.event && info.event.clientX));
            var y = info && (info.y || (info.event && info.event.clientY));
            this._showTime(session, this._minuteAt(session, session.column, x, y), this._eventDuration(session));
            this._startScroller(session);
        },

        /** The event's offset inside its column, measured before any translate. */
        _originOffset: function (eventElement, column, isTimeline) {
            if (!eventElement || !column || typeof eventElement.getBoundingClientRect !== 'function') return 0;
            var eventBox = eventElement.getBoundingClientRect();
            var columnBox = column.getBoundingClientRect();
            return isTimeline ? eventBox.left - columnBox.left : eventBox.top - columnBox.top;
        },

        /** How far into the event the finger landed, so the drop keeps the grabbed point. */
        _grabOffset: function (eventElement, isTimeline, info) {
            var box = typeof eventElement.getBoundingClientRect === 'function' ? eventElement.getBoundingClientRect() : null;
            var point = info && info.event ? info.event : info;
            if (!box || !point) return { x: 0, y: 0 };
            return { x: (point.clientX || 0) - box.left, y: (point.clientY || 0) - box.top };
        },

        /**
         * The scroll container of the view. The time-grid scroller wraps the grid, so a descendant
         * query from the grid never finds it; the walk starts at the event. A month scrolls the
         * page, so it has none.
         */
        _scrollerOf: function (session) {
            var start = session.eventElement || session.container;
            if (!start || typeof start.closest !== 'function') return null;
            if (session.isTimeline) return start.closest('.tm-scheduler-timeline');
            return start.closest('.tm-scheduler-timegrid-scroll');
        },

        _startScroller: function (session) {
            var factory = this._gesture && this._gesture.createAutoScroller;
            var container = this._scrollerOf(session);
            if (!factory || !container) return;
            var scheduler = this;
            session.scroller = factory(container, {
                axis: session.isTimeline ? 'x' : 'y',
                onScroll: function () { scheduler._moveDrag(scheduler._lastMove || { x: 0, y: 0, dx: 0, dy: 0 }); }
            });
        },

        /** The column, day cell or row under the finger. The event itself is ignored (it follows). */
        _elementUnder: function (x, y) {
            if (typeof document === 'undefined' || typeof document.elementFromPoint !== 'function') return null;
            var hit = document.elementFromPoint(x, y);
            if (!hit || typeof hit.closest !== 'function') return null;
            return hit.closest(
                '.tm-scheduler-timegrid-col, .tm-scheduler-timegrid-resource-col, .tm-scheduler-month-day, .tm-scheduler-timeline-row-body');
        },

        /** Minutes of the grabbed point, snapped to the slot, or -1 for a month. */
        _minuteAt: function (session, target, x, y) {
            if (session.isMonth || !target || typeof target.getBoundingClientRect !== 'function') return -1;
            var box = target.getBoundingClientRect();
            var size = session.isTimeline ? box.width : box.height;
            if (!(size > 0)) return -1;
            var grab = session.grabOffset || { x: 0, y: 0 };
            var along = session.isTimeline ? x - grab.x - box.left : y - grab.y - box.top;
            var offset = session.rangeStart + along / size * session.rangeMinutes;
            var slot = session.slotMinutes;
            var snapped = Math.round(offset / slot) * slot;
            var end = session.rangeStart + session.rangeMinutes;
            if (snapped < session.rangeStart) snapped = session.rangeStart;
            if (snapped > end - slot) snapped = end - slot;
            return snapped;
        },

        _moveDrag: function (info) {
            var session = this._moveSession;
            if (!session) return;
            this._lastMove = info;
            var eventElement = session.eventElement;
            if (session.scroller) session.scroller.update(info.x, info.y);

            var target = this._elementUnder(info.x, info.y);
            if (target) {
                var cell = target.closest('[data-date]');
                session.targetDate = cell ? cell.dataset.date : (session.container.dataset.date || session.startDate);
                var row = target.closest('[data-resource]');
                session.targetResource = row ? row.dataset.resource : session.startResource;
                session.targetMinute = this._minuteAt(session, session.isTimeline ? target : (session.column || target), info.x, info.y);
            }
            this._markDropTarget(session, target);
            this._snapTranslate(session, info);
            this._showTime(session, session.targetMinute, this._eventDuration(session));
            // The snap translate is not in the layout until the next frame. Placing the parked
            // label now reads the event where it was. One retry, and only while this drag lives.
            var self = this;
            if (typeof requestAnimationFrame === 'function') {
                if (session.labelFrame) cancelAnimationFrame(session.labelFrame);
                session.labelFrame = requestAnimationFrame(function () {
                    session.labelFrame = 0;
                    if (self._moveSession === session) self._showTime(session, session.targetMinute, self._eventDuration(session));
                });
            }
        },

        /** The day, column or row under the finger. The previous one loses the class. */
        _markDropTarget: function (session, target) {
            if (session.dropTarget && session.dropTarget !== target && session.dropTarget.classList) {
                DROP_TARGET.split(' ').forEach(function (name) { session.dropTarget.classList.remove(name); });
            }
            session.dropTarget = target;
            if (target && target.classList) {
                var name = session.isMonth
                    ? 'tm-scheduler-month-day--drop-target'
                    : session.isTimeline ? 'tm-scheduler-timeline-slot--drop-target' : 'tm-scheduler-timegrid-slot--drop-target';
                target.classList.add(name);
            }
        },

        /**
         * The delta the preview actually applied. A resize session has no move, so it contributes
         * nothing: the label follows the event's own box, not a delta left by an earlier drag.
         */
        _snapDelta: function (session) {
            if (!session || session.resizeEdge) return { x: 0, y: 0 };
            return session.appliedDelta || { x: 0, y: 0 };
        },

        /** The preview lands on the same snapped minute the release commits, not on the raw finger. */
        _snapTranslate: function (session, info) {
            var eventElement = session.eventElement;
            var dx = info.dx || 0;
            var dy = info.dy || 0;
            if (session.targetMinute !== null && session.targetMinute >= 0 && session.column) {
                var box = session.column.getBoundingClientRect();
                var size = session.isTimeline ? box.width : box.height;
                if (size > 0) {
                    var origin = (session.isTimeline ? box.left : box.top) + (session.originOffset || 0);
                    var snapped = (session.isTimeline ? box.left : box.top) + (session.targetMinute - session.rangeStart) / session.rangeMinutes * size;
                    var delta = snapped - origin;
                    if (session.isTimeline) dx = delta; else dy = delta;
                }
            }
            session.appliedDelta = { x: dx, y: dy };
            eventElement.style.transform = 'translate(' + dx + 'px, ' + dy + 'px)';
            return session.appliedDelta;
        },

        /** The live range label. A month has no minute axis, so it shows nothing. */
        _showTime: function (session, minute, duration) {
            var eventElement = session.eventElement;
            if (!eventElement || typeof eventElement.querySelector !== 'function') return;
            if (minute === null || minute < 0) {
                // A month has no minute axis, so a label left by an earlier drag must not follow it.
                // A timeline whose column has not resolved yet keeps the label it already shows.
                if (session.isMonth) this._removeLabel(eventElement);
                else {
                    var kept = eventElement.closest ? eventElement.closest('.tm-scheduler') : null;
                    var existing = kept && kept.querySelector ? kept.querySelector('.tm-scheduler-event-drag-time') : null;
                    if (existing) this._placeLabel(session, existing);
                }
                return;
            }
            var parked = eventElement.closest ? eventElement.closest('.tm-scheduler') : null;
            var label = (parked || eventElement).querySelector('.tm-scheduler-event-drag-time');
            if (!label) {
                label = document.createElement('span');
                label.className = 'tm-scheduler-event-drag-time';
                // Every view parks the label on the scheduler root. A row, a column and a
                // transformed event each isolate a stacking context, so a label inside the event
                // paints under a sticky head or a sticky resource column.
                var host = eventElement.closest ? eventElement.closest('.tm-scheduler') : null;
                if (!host || typeof host.appendChild !== 'function') host = eventElement;
                host.appendChild(label);
            }
            var culture = session.container && session.container.dataset ? session.container.dataset.timeCulture : null;
            label.textContent = clock(minute, culture) + ' \u2013 ' + clock(minute + duration, culture);
            // The flip needs the painted height. A label created this call has none until the
            // browser lays it out, so the placement reads it after the text is set.
            this._placeLabel(session, label);
        },

        /** The label flips below the event when the sticky head, or the scroll top, would clip it. */
        _placeLabel: function (session, label) {
            var scroller = this._scrollerOf(session);
            var box = session.eventElement.getBoundingClientRect();
            var anchor = label.parentElement;
            // The label is parked on the scheduler, so the in-event offsets do not apply. Its
            // containing block can be a positioned ancestor of the scheduler (a demo card), so the
            // anchor is the label's offset parent, not the scheduler.
            if (anchor && anchor !== session.eventElement && typeof anchor.getBoundingClientRect === 'function') {
                var origin = label.offsetParent && typeof label.offsetParent.getBoundingClientRect === 'function'
                    ? label.offsetParent : anchor;
                var rootBox = origin.getBoundingClientRect();
                label.style.position = 'absolute';
                // The event's live rect lags the snap translate by a frame, so the label follows
                // the snap: the pre-drag origin plus the same delta the event was translated by.
                var delta = this._snapDelta(session);
                var scroller = this._scrollerOf(session);
                var scrollTop = scroller ? scroller.scrollTop || 0 : 0;
                var scrollLeft = scroller ? scroller.scrollLeft || 0 : 0;
                var base = session.labelBase;
                if (!base) {
                    // The label is positioned against the scheduler, which does not scroll, while the
                    // event's rect is in the viewport. Remember the scroll at capture so a later
                    // auto-scroll, which moves the event's rect, moves the label by the same amount.
                    base = session.labelBase = {
                        top: box.top - rootBox.top,
                        left: box.left - rootBox.left,
                        scrollTop: scrollTop,
                        scrollLeft: scrollLeft
                    };
                }
                var scrolledX = scrollLeft - base.scrollLeft;
                var scrolledY = scrollTop - base.scrollTop;
                label.style.left = (base.left + delta.x - scrolledX) + 'px';
                label.style.top = (base.top + delta.y - scrolledY) + 'px';
            }
            var limit = scroller && typeof scroller.getBoundingClientRect === 'function' ? scroller.getBoundingClientRect().top : 0;
            var root = session.container;
            if (root && typeof root.querySelectorAll === 'function') {
                var heads = root.querySelectorAll('.tm-scheduler-timegrid-head, .tm-scheduler-timeline-header');
                for (var i = 0; i < heads.length; i++) {
                    var candidate = heads[i].getBoundingClientRect();
                    // A header of another scheduler, or one scrolled off the page, is not a clip.
                    if (candidate.bottom > box.top && candidate.top < box.bottom && candidate.bottom > 0 && candidate.bottom > limit) {
                        limit = candidate.bottom;
                    }
                }
            }
            var gap = 4;
            var height = label && typeof label.getBoundingClientRect === 'function'
                ? label.getBoundingClientRect().height
                : 0;
            if (height < 1 && label) height = label.offsetHeight || 0;
            if (height < 1) height = 24;
            if (box.top - height - gap < limit) label.classList.add('tm-scheduler-event-drag-time--below');
            else label.classList.remove('tm-scheduler-event-drag-time--below');
            this._keepLabelInColumn(session, label);
        },

        /** A timeline label that starts under the sticky resource column shifts right until it is readable. */
        _keepLabelInColumn: function (session, label) {
            if (!session.isTimeline || !label || typeof label.getBoundingClientRect !== 'function') return;
            var edge = 0;
            var eventBox = session.eventElement.getBoundingClientRect();
            var covers = session.container && typeof session.container.querySelectorAll === 'function'
                ? session.container.querySelectorAll('.tm-scheduler-timeline-resource-label--sticky, .tm-scheduler-timeline-resource-header')
                : [];
            for (var i = 0; i < covers.length; i++) {
                var cover = covers[i].getBoundingClientRect();
                var eventTop = eventBox.top || 0;
                var eventBottom = eventBox.bottom || (eventTop + (eventBox.height || 0));
                var coverTop = cover.top || 0;
                var coverBottom = cover.bottom || (coverTop + (cover.height || 0));
                if (coverBottom > eventTop && coverTop < eventBottom && cover.right > edge) edge = cover.right;
            }
            if (edge === 0 && session.column && typeof session.column.getBoundingClientRect === 'function') edge = session.column.getBoundingClientRect().left;
            var painted = label.getBoundingClientRect();
            if (painted.left < edge) label.style.marginLeft = (edge - painted.left + 4) + 'px';
        },

        /** A move owns the finger, so the OS context menu that a long-press raises is swallowed. */
        _armContextMenu: function () {
            if (this._contextMenu) return;
            var scheduler = this;
            this._contextMenu = function (event) {
                if (!scheduler._moveSession) return;
                if (event && typeof event.preventDefault === 'function') event.preventDefault();
            };
            document.addEventListener('contextmenu', this._contextMenu, true);
        },

        _disarmContextMenu: function () {
            if (!this._contextMenu) return;
            document.removeEventListener('contextmenu', this._contextMenu, true);
            this._contextMenu = null;
        },

        /** The event's own length in minutes, read from its box against the container. */
        _eventDuration: function (session) {
            var box = session.eventElement.getBoundingClientRect();
            var column = session.column;
            var size = column && typeof column.getBoundingClientRect === 'function'
                ? (session.isTimeline ? column.getBoundingClientRect().width : column.getBoundingClientRect().height)
                : 0;
            if (!(size > 0)) return session.slotMinutes;
            var minutes = (session.isTimeline ? box.width : box.height) / size * session.rangeMinutes;
            var slot = session.slotMinutes;
            return Math.max(slot, Math.round(minutes / slot) * slot);
        },

        _moveEnd: function () {
            var session = this._moveSession;
            if (!session || !session.dotNetRef || !session.targetDate) return;
            var generation = this._generation;
            var self = this;
            session.dotNetRef.invokeMethodAsync('NotifyMoveComplete',
                session.eventId, session.targetDate, session.targetMinute, session.targetResource)
                .catch(function (err) {
                    if (self._generation === generation)
                        console.warn('Scheduler: the move could not be committed', err);
                });
        },

        _moveRollback: function () {
            var session = this._moveSession;
            if (!session) return;
            session.eventElement.style.transform = '';
            session.targetDate = null;
        },

        _moveRelease: function () {
            var session = this._moveSession;
            this._moveSession = null;
            this._disarmContextMenu();
            if (!session) return;
            if (session.scroller) session.scroller.stop();
            this._markDropTarget(session, null);
            var eventElement = session.eventElement;
            eventElement.classList.remove('tm-scheduler-event--moving');
            eventElement.style.transform = '';
            eventElement.draggable = session.originalDraggable;
            this._lastMove = null;
            this._removeLabel(eventElement);
        },

        /** The live label is parked on the scheduler root. Both gestures remove it on release. */
        _removeLabel: function (eventElement) {
            var host = eventElement && eventElement.closest ? eventElement.closest('.tm-scheduler') : null;
            var label = host
                ? host.querySelector(':scope > .tm-scheduler-event-drag-time')
                : eventElement && eventElement.querySelector('.tm-scheduler-event-drag-time');
            if (label) label.remove();
        },

        /** Alt+Arrow moves, Alt+Shift+Arrow resizes. One step, one call; .NET owns the maths. */
        _keyboardStep: function (step) {
            var target = step.event && step.event.target;
            var eventElement = target && typeof target.closest === 'function' ? target.closest(MOVE_SELECTOR) : null;
            if (!eventElement || eventElement.getAttribute('draggable') !== 'true') return;
            var container = eventElement.closest('.tm-scheduler-timegrid, .tm-scheduler-timeline, .tm-scheduler-month');
            var dotNetRef = container ? this._dotNetRefs.get(container) : null;
            if (!dotNetRef) return;
            dotNetRef.invokeMethodAsync('NotifyKeyboardStep', eventElement.dataset.eventId, step.x, step.y, step.shift === true)
                .catch(function (err) { console.warn('Scheduler: the keyboard step could not be committed', err); });
        },

        _detachDrag: function () {
            var drag = this._drag;
            var move = this._move;
            var keyboard = this._keyboard;
            this._drag = null;
            this._move = null;
            this._keyboard = null;
            if (drag) drag.detach();
            if (move) move.detach();
            if (keyboard) keyboard.detach();
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
