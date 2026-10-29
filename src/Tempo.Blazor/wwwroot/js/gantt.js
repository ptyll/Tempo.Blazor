// Tempo.Blazor Gantt JS interop (classic script, window.tmGantt).
//
// Scroll helpers, the workload scroll link and the history drawer focus return, plus the pointer
// work of the touch phase: timeline pan (mouse only - a finger or a pen scrolls natively),
// dependency drag (all pointers), touch long-press context menus on bars and tree rows, and the
// keyboard move alternative of the tree. The gestures themselves are the shared core helpers
// pointer-drag.js (attach, attachKeyboardMove) and pointer-gesture.js (createAutoScroller), loaded
// by a dynamic import() resolved from the URL this script was served from (document.currentScript
// is only set while the classic script evaluates).
// Never a second drag/pinch implementation here - see docs/touch-interaction.md.

(function () {
    'use strict';

    // Resolved NOW: document.currentScript is only set while the classic script runs synchronously.
    var dragUrl = null;
    var gestureUrl = null;
    try {
        if (typeof document !== 'undefined') {
            var script = document.currentScript;
            if (script && script.src) {
                dragUrl = new URL('pointer-drag.js', script.src).href;
                gestureUrl = new URL('pointer-gesture.js', script.src).href;
            } else if (document.baseURI) {
                dragUrl = new URL('_content/Tempo.Blazor/js/pointer-drag.js', document.baseURI).href;
                gestureUrl = new URL('_content/Tempo.Blazor/js/pointer-gesture.js', document.baseURI).href;
            }
        }
    } catch (e) {
        dragUrl = null;
        gestureUrl = null;
    }

    var helpersPromise = null;

    /** The registered scroll groups of tmGantt.linkScrollGroup, keyed by the caller-supplied id. */
    var groupLinks = new Map();

    /** True when both lists hold the same members (identity, order-insensitive). */
    function sameMembers(a, b) {
        if (a.length !== b.length) return false;
        return a.every(function (el) { return b.indexOf(el) !== -1; });
    }

    /** Loads the shared helpers once; resolves null (after a console warning) when they cannot load. */
    function loadHelpers() {
        if (!helpersPromise) {
            helpersPromise = Promise.resolve()
                .then(function () {
                    if (!dragUrl || !gestureUrl) throw new Error('the helper URL could not be resolved');
                    return Promise.all([import(dragUrl), import(gestureUrl)]);
                })
                .then(function (modules) {
                    var drag = modules[0];
                    var gesture = modules[1];
                    if (typeof drag.attach !== 'function' || typeof drag.attachKeyboardMove !== 'function'
                        || typeof gesture.createAutoScroller !== 'function') {
                        throw new Error('the helper modules do not export the expected functions');
                    }
                    return {
                        attach: drag.attach,
                        attachKeyboardMove: drag.attachKeyboardMove,
                        createAutoScroller: gesture.createAutoScroller
                    };
                })
                .catch(function (error) {
                    helpersPromise = null; // a later call may retry
                    console.warn('tmGantt: the pointer helpers (pointer-drag.js / pointer-gesture.js) could not be loaded; touch and pointer gestures are unavailable.', error);
                    return null;
                });
        }
        return helpersPromise;
    }

    var NON_PAN_TARGETS = '.tm-gantt__bar, .tm-gantt__milestone, .tm-gantt__dep-handle, button, a, input, select, textarea';
    var BAR_SELECTOR = '.tm-gantt__bar, .tm-gantt__milestone';
    var BAR_LONG_PRESS = '.tm-gantt__bar[data-task-id], .tm-gantt__milestone[data-task-id]';
    var DEP_HANDLE = '.tm-gantt__dep-handle';
    var ROW_SELECTOR = '.tm-gantt__tree-row';
    var ROW_LONG_PRESS = '.tm-gantt__tree-row[data-task-id]';
    var TARGET_CLASS = 'tm-gantt__bar--dep-target';
    var SVG_NS = 'http://www.w3.org/2000/svg';

    // Per element state (several Gantts on a page never share one).
    // Keyed by a caller-supplied id, NOT by the element: Blazor removes the element before DisposeAsync runs,
    // so a stale ElementReference arrives as null on detach.
    var timelineStates = new Map();
    var treeStates = new Map();

    function closestOf(target, selector) {
        return target && typeof target.closest === 'function' ? target.closest(selector) : null;
    }

    /** Calls a .NET method; a rejection (disposed reference, circuit gone) is ignored. */
    function notify(state, method) {
        var args = Array.prototype.slice.call(arguments, 2);
        try {
            var dotNet = state.dotNet;
            if (!dotNet || typeof dotNet.invokeMethodAsync !== 'function') return;
            var result = dotNet.invokeMethodAsync.apply(dotNet, [method].concat(args));
            if (result && typeof result.catch === 'function') result.catch(function () { });
        } catch (e) { /* ignore */ }
    }

    function warn(what, error) {
        console.warn('tmGantt: ' + what + ' failed.', error);
    }

    function detachAll(sessions) {
        var list = sessions.splice(0, sessions.length);
        list.forEach(function (session) {
            try { session.detach(); } catch (e) { /* already gone */ }
        });
    }

    // ── Timeline ────────────────────────────────────────────────────────────────────────────────

    function contentOf(state) {
        var content = state.body && typeof state.body.querySelector === 'function'
            ? state.body.querySelector('.tm-gantt__timeline-content') : null;
        if (!content && typeof state.timeline.querySelector === 'function') {
            content = state.timeline.querySelector('.tm-gantt__timeline-content');
        }
        return content;
    }

    /** Pan: the mouse drags the empty timeline; a finger or a pen scrolls natively (CSS touch-action: pan-x pan-y). */
    function attachPan(h, state) {
        var timeline = state.timeline;
        var start = null;
        return h.attach(timeline, {
            // Mouse only: a pen drag on the timeline would be a native scroll (touch-action applies to the pen too), which cancels the session.
            pointerTypes: ['mouse'],
            threshold: 4,
            touchAction: null,
            dragClass: 'tm-gantt__timeline--panning',
            onPress: function (info) {
                if (closestOf(info.event && info.event.target, NON_PAN_TARGETS)) return false;
                start = {
                    left: timeline.scrollLeft,
                    top: state.body ? state.body.scrollTop : 0
                };
                return undefined;
            },
            onMove: function (info) {
                if (!start) return;
                timeline.scrollLeft = start.left - info.dx;
                var top = start.top - info.dy;
                if (state.body) state.body.scrollTop = top;
                if (state.treeBody) state.treeBody.scrollTop = top;
            },
            onCancel: function () {
                if (!start) return;
                timeline.scrollLeft = start.left;
                if (state.body) state.body.scrollTop = start.top;
                if (state.treeBody) state.treeBody.scrollTop = start.top;
            },
            onRelease: function () { start = null; }
        });
    }

    /** Dependency drag from a handle to another bar; one OnDependencyDragCompleted call on release. */
    function attachDependencyDrag(h, state) {
        var timeline = state.timeline;
        var drag = null;

        function targetAt(x, y, fromId) {
            if (typeof document === 'undefined' || typeof document.elementFromPoint !== 'function') return null;
            var hit = document.elementFromPoint(x, y);
            var bar = closestOf(hit, BAR_SELECTOR);
            if (!bar || !bar.dataset || !bar.dataset.taskId || bar.dataset.taskId === fromId) return null;
            return bar;
        }

        function markTarget(current, bar) {
            if (current.target === bar) return;
            if (current.target && current.target.classList) current.target.classList.remove(TARGET_CLASS);
            current.target = bar;
            if (bar && bar.classList) bar.classList.add(TARGET_CLASS);
        }

        function redraw(current) {
            if (!current.line || !current.content) return;
            var rect = current.content.getBoundingClientRect();
            // The content rect is viewport-relative, so the scroll is already included; the handle moves with
            // the content, so its centre is re-measured every time (an auto-scroll shifts the origin).
            if (current.handle && current.handle.isConnected !== false) {
                var hr = current.handle.getBoundingClientRect();
                current.ox = hr.left + hr.width / 2 - rect.left;
                current.oy = hr.top + hr.height / 2 - rect.top;
            }
            current.line.setAttribute('x1', String(current.ox));
            current.line.setAttribute('y1', String(current.oy));
            current.line.setAttribute('x2', String(current.x - rect.left));
            current.line.setAttribute('y2', String(current.y - rect.top));
        }

        var scroller = h.createAutoScroller(timeline, {
            axis: 'x',
            onScroll: function () {
                if (!drag) return;
                redraw(drag);
                markTarget(drag, targetAt(drag.x, drag.y, drag.fromId));
            }
        });

        function cleanup() {
            var current = drag;
            drag = null;
            try { scroller.stop(); } catch (e) { /* ignore */ }
            if (!current) return;
            if (current.target && current.target.classList) current.target.classList.remove(TARGET_CLASS);
            if (current.ghost && typeof current.ghost.remove === 'function') current.ghost.remove();
        }

        var session = h.attach(timeline, {
            selector: DEP_HANDLE,
            threshold: 2,
            touchAction: null,
            onPress: function (info) {
                var handle = info.target;
                var fromId = handle && handle.dataset ? handle.dataset.taskId : null;
                if (!fromId) return false;
                drag = {
                    handle: handle,
                    fromId: fromId,
                    fromEnd: handle.dataset.depEnd === 'true',
                    x: info.startX, y: info.startY,
                    ox: info.startX, oy: info.startY,
                    content: null, ghost: null, line: null, target: null
                };
                return undefined;
            },
            onStart: function (info) {
                if (!drag) return;
                drag.x = info.x;
                drag.y = info.y;
                var content = contentOf(state);
                if (!content || typeof document === 'undefined' || typeof document.createElementNS !== 'function') return;
                var svg = document.createElementNS(SVG_NS, 'svg');
                svg.setAttribute('class', 'tm-gantt__dep-ghost');
                svg.setAttribute('style', 'position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;overflow:visible;z-index:6');
                var line = document.createElementNS(SVG_NS, 'line');
                line.setAttribute('style', 'stroke:var(--tm-color-primary);stroke-width:2;stroke-dasharray:4 3');
                svg.appendChild(line);
                content.appendChild(svg);
                drag.content = content;
                drag.ghost = svg;
                drag.line = line;
                redraw(drag);
            },
            onMove: function (info) {
                if (!drag) return;
                drag.x = info.x;
                drag.y = info.y;
                redraw(drag);
                markTarget(drag, targetAt(info.x, info.y, drag.fromId));
                scroller.update(info.x, info.y);
            },
            onEnd: function (info) {
                if (!drag) return;
                var bar = targetAt(info.x, info.y, drag.fromId);
                if (bar) notify(state, 'OnDependencyDragCompleted', drag.fromId, drag.fromEnd, bar.dataset.taskId);
            },
            onRelease: cleanup
        });

        return {
            detach: function () {
                session.detach();
                cleanup();
            }
        };
    }

    /** Touch long-press on a bar or milestone opens the context menu while the finger is still down. */
    function attachBarLongPress(h, state) {
        return h.attach(state.timeline, {
            selector: BAR_LONG_PRESS,
            pointerTypes: ['touch'],
            longPressMs: 500,
            threshold: 4,
            touchAction: null,
            suppressClick: true,
            onPress: function (info) {
                if (closestOf(info.event && info.event.target, DEP_HANDLE)) return false;
                return undefined;
            },
            onStart: function (info) {
                var id = info.target && info.target.dataset ? info.target.dataset.taskId : null;
                if (id) notify(state, 'OnLongPressContextMenu', id, info.startX, info.startY);
            }
        });
    }

    function teardown(states, id, state) {
        state.disposed = true;
        detachAll(state.sessions);
        if (states.get(id) === state) states.delete(id);
    }

    // ── Tree ────────────────────────────────────────────────────────────────────────────────────

    /**
     * A finger must not start HTML5 drag-and-drop on a draggable tree row (a native long-press would fight the
     * long-press menu): the row is made non-draggable for that finger and restored when it lifts.
     */
    function attachNoTouchDnd(state) {
        var treeBody = state.treeBody;
        var pending = new Map();

        function restore(pointerId) {
            var entry = pending.get(pointerId);
            if (!entry) return;
            pending.delete(pointerId);
            // The row may have been shared by two fingers: only the last one puts the value back.
            if (entry.row.__tmGanttDndHolds > 1) { entry.row.__tmGanttDndHolds -= 1; return; }
            delete entry.row.__tmGanttDndHolds;
            if (entry.value === null) entry.row.removeAttribute('draggable');
            else entry.row.setAttribute('draggable', entry.value);
        }

        function onDown(event) {
            if (event.pointerType !== 'touch') return;
            var row = closestOf(event.target, ROW_SELECTOR);
            if (!row || typeof row.setAttribute !== 'function') return;
            restore(event.pointerId);
            var held = row.__tmGanttDndHolds || 0;
            var value = held > 0 ? row.__tmGanttDndValue : (row.getAttribute('draggable'));
            if (held === 0) row.__tmGanttDndValue = value;
            row.__tmGanttDndHolds = held + 1;
            pending.set(event.pointerId, { row: row, value: row.__tmGanttDndValue });
            row.setAttribute('draggable', 'false');
        }

        function onUp(event) { restore(event.pointerId); }

        treeBody.addEventListener('pointerdown', onDown, true);
        treeBody.addEventListener('pointerup', onUp, true);
        treeBody.addEventListener('pointercancel', onUp, true);
        treeBody.addEventListener('lostpointercapture', onUp, true);

        return {
            detach: function () {
                treeBody.removeEventListener('pointerdown', onDown, true);
                treeBody.removeEventListener('pointerup', onUp, true);
                treeBody.removeEventListener('pointercancel', onUp, true);
                treeBody.removeEventListener('lostpointercapture', onUp, true);
                Array.from(pending.keys()).forEach(restore);
            }
        };
    }


    window.tmGantt = {
        getScrollLeft: function (el) { return el ? el.scrollLeft : 0; },
        setScrollLeft: function (el, v) { if (el) el.scrollTo({ left: v }); },
        getScrollTop: function (el) { return el ? el.scrollTop : 0; },
        setScrollTop: function (el, v) { if (el) el.scrollTop = v; },
        // Width of the element the caller owns (an ElementReference) - replaces the former eval() +
        // global querySelector, which broke under a CSP without unsafe-eval and measured the wrong
        // Gantt when several were on the page (portfolio).
        getClientWidth: function (el) { return el ? el.clientWidth : 0; },

        // Docked workload panel: keeps its horizontal scroll and the timeline's in step. Both axes show the same
        // date range, so the position is mapped as a RATIO of the scrollable distance (the widths differ). Runs
        // entirely in the browser - a Blazor Server circuit must not pay a round trip per scroll event. The peer is
        // found from the workload element itself (closest .tm-gantt), so several Gantts on a page never cross.
        // Idempotent and safe to call on every render: returns true while the workload is linked to the CURRENT
        // timeline (an existing link to that same element is kept; a link to a replaced element is released and
        // remade), false when there is nothing to link to.
        linkScroll: function (workload) {
            if (!workload) return false;
            const root = workload.closest('.tm-gantt');
            const timeline = root ? root.querySelector('.tm-gantt__timeline') : null;
            const existing = workload.__tmGanttLink;
            if (existing && timeline && existing.timeline === timeline) return true;
            window.tmGantt.unlinkScroll(workload);
            if (!timeline) return false;

            // The value a programmatic write produced: the scroll event it causes must not drive the peer back.
            const written = new WeakMap();
            const ratio = function (el) {
                const max = el.scrollWidth - el.clientWidth;
                return max > 0 ? el.scrollLeft / max : 0;
            };
            const follow = function (source, target) {
                return function () {
                    if (written.get(source) === source.scrollLeft) {
                        written.delete(source);
                        return;
                    }
                    const max = target.scrollWidth - target.clientWidth;
                    target.scrollLeft = ratio(source) * Math.max(max, 0);
                    written.set(target, target.scrollLeft);
                };
            };

            const toTimeline = follow(workload, timeline);
            const toWorkload = follow(timeline, workload);
            workload.addEventListener('scroll', toTimeline, { passive: true });
            timeline.addEventListener('scroll', toWorkload, { passive: true });
            workload.__tmGanttLink = { timeline: timeline, toTimeline: toTimeline, toWorkload: toWorkload };
            toWorkload();
            return true;
        },

        unlinkScroll: function (workload) {
            const link = workload && workload.__tmGanttLink;
            if (!link) return;
            workload.removeEventListener('scroll', link.toTimeline);
            link.timeline.removeEventListener('scroll', link.toWorkload);
            delete workload.__tmGanttLink;
        },

        // Shared portfolio axis (phase 11): links the horizontal scroll of a GROUP of elements (the sticky
        // axis header plus every project card's timeline). All members show the same date range. Equal
        // content widths map PIXEL-FOR-PIXEL, clamped to the target's own range (the client widths differ:
        // the header's scroller is not as wide as a card's timeline); members whose content width differs
        // fall back to the RATIO of the scrollable distance, exactly like linkScroll. One source event fans
        // out to every other member in the browser, with no interop per scroll tick. Idempotent per groupId:
        // a call with the same member set (identity) keeps the existing group; a changed set is rebuilt.
        // Returns true while the group is registered, false when there is nothing to link (fewer than two
        // elements).
        linkScrollGroup: function (groupId, elements) {
            if (!groupId || !elements) return false;
            const list = Array.prototype.filter.call(elements, function (el) { return !!el; });
            if (list.length < 2) {
                window.tmGantt.unlinkScrollGroup(groupId);
                return false;
            }

            const existing = groupLinks.get(groupId);
            if (existing && sameMembers(existing.elements, list)) return true;
            window.tmGantt.unlinkScrollGroup(groupId);

            // The value a programmatic write produced: the echo scroll event it causes is swallowed once.
            const written = new WeakMap();
            const ratio = function (el) {
                const max = el.scrollWidth - el.clientWidth;
                return max > 0 ? el.scrollLeft / max : 0;
            };
            const mapTo = function (source, target) {
                const max = Math.max(target.scrollWidth - target.clientWidth, 0);
                if (target.scrollWidth === source.scrollWidth) {
                    return Math.min(source.scrollLeft, max);
                }
                return ratio(source) * max;
            };
            // A write that does not move the target fires no scroll event, so it must leave no echo
            // marker: a stale marker would swallow the user's real scroll to that position later.
            const write = function (target, value) {
                const previous = target.scrollLeft;
                target.scrollLeft = value;
                if (target.scrollLeft !== previous) written.set(target, target.scrollLeft);
            };
            const handlers = new Map();
            list.forEach(function (source) {
                const handler = function () {
                    if (written.get(source) === source.scrollLeft) {
                        written.delete(source);
                        return;
                    }
                    list.forEach(function (target) {
                        if (target === source) return;
                        write(target, mapTo(source, target));
                    });
                };
                source.addEventListener('scroll', handler, { passive: true });
                handlers.set(source, handler);
            });
            groupLinks.set(groupId, { elements: list, handlers: handlers });

            // Initial equalization (like linkScroll): members registered late converge on the first
            // member's position, so a group linked after a scroll does not desync. Marked like every
            // write, so the echo of the initial writes does not fan back out.
            const first = list[0];
            list.forEach(function (target) {
                if (target === first) return;
                write(target, mapTo(first, target));
            });

            return true;
        },

        unlinkScrollGroup: function (groupId) {
            const link = groupLinks.get(groupId);
            if (!link) return;
            link.handlers.forEach(function (handler, source) {
                source.removeEventListener('scroll', handler);
            });
            groupLinks.delete(groupId);
        },

        // History drawer focus return: the drawer notes the element that had focus when it opened (the History button
        // of the toolbar) and hands focus back to it when it closes. The note lives on the drawer element, so several
        // Gantts never share one; a body / in-drawer / detached opener is ignored (nothing sensible to return to).
        noteOpener: function (drawer) {
            if (!drawer) return;
            const doc = drawer.ownerDocument;
            const active = doc ? doc.activeElement : null;
            if (!active || active === doc.body || drawer.contains(active)) {
                delete drawer.__tmGanttOpener;
                return;
            }
            drawer.__tmGanttOpener = active;
        },

        restoreOpener: function (drawer) {
            if (!drawer) return false;
            const opener = drawer.__tmGanttOpener;
            delete drawer.__tmGanttOpener;
            if (!opener || opener.isConnected === false || typeof opener.focus !== 'function') return false;
            opener.focus();
            return true;
        },

        // Pointer sessions of the timeline: mouse pan, dependency drag, touch long-press on bars.
        // `timeline` = .tm-gantt__timeline, `body` = .tm-gantt__timeline-body (vertical scroller with the relative
        // .tm-gantt__timeline-content), `treeBody` = .tm-gantt__tree-body (kept in step vertically), `dotNet` = a
        // DotNetObjectReference. id is a caller-supplied string key (the element may already be gone when the
        // component disposes). Attaching an id that exists first detaches the old registration; never throws to .NET.
        // Resolves true when every session attached, false when there was nothing to attach or the
        // pointer helpers are unavailable (the caller then knows the sessions are NOT registered).
        attachTimeline: function (id, timeline, body, treeBody, dotNet) {
            if (id === null || id === undefined) return Promise.resolve(false);
            const existing = timelineStates.get(id);
            if (existing) teardown(timelineStates, id, existing);
            if (!timeline) return Promise.resolve(false);
            const state = {
                timeline: timeline, body: body, treeBody: treeBody, dotNet: dotNet,
                sessions: [], disposed: false, ready: null
            };
            timelineStates.set(id, state);
            state.ready = loadHelpers().then(function (h) {
                if (state.disposed) return false;
                if (!h) {
                    teardown(timelineStates, id, state);
                    return false;
                }
                try {
                    state.sessions.push(attachPan(h, state));
                    state.sessions.push(attachDependencyDrag(h, state));
                    state.sessions.push(attachBarLongPress(h, state));
                    return true;
                } catch (e) {
                    warn('attachTimeline', e);
                    teardown(timelineStates, id, state);
                    return false;
                }
            });
            return state.ready;
        },

        detachTimeline: function (id) {
            const state = id === null || id === undefined ? null : timelineStates.get(id);
            if (state) teardown(timelineStates, id, state);
            return Promise.resolve();
        },

        // Pointer sessions of the task tree: touch long-press context menu, the Alt+Up/Down keyboard move
        // (WCAG 2.5.7 alternative to the tree drag-and-drop) and the "no HTML5 drag-and-drop under a finger" guard.
        // Resolves true when every session attached, false when there was nothing to attach or the
        // pointer helpers are unavailable (the caller then knows the sessions are NOT registered).
        attachTree: function (id, treeBody, dotNet) {
            if (id === null || id === undefined) return Promise.resolve(false);
            const existing = treeStates.get(id);
            if (existing) teardown(treeStates, id, existing);
            if (!treeBody) return Promise.resolve(false);
            const state = { treeBody: treeBody, dotNet: dotNet, sessions: [], disposed: false, ready: null };
            treeStates.set(id, state);
            state.ready = loadHelpers().then(function (h) {
                if (state.disposed) return false;
                if (!h) {
                    teardown(treeStates, id, state);
                    return false;
                }
                try {
                    state.sessions.push(attachNoTouchDnd(state));
                    state.sessions.push(h.attach(treeBody, {
                        selector: ROW_LONG_PRESS,
                        pointerTypes: ['touch'],
                        longPressMs: 500,
                        threshold: 4,
                        touchAction: null,
                        onPress: function (info) {
                            if (closestOf(info.event && info.event.target, 'button, input, select, textarea, a')) return false;
                            return undefined;
                        },
                        onStart: function (info) {
                            var id = info.target && info.target.dataset ? info.target.dataset.taskId : null;
                            if (id) notify(state, 'OnLongPressContextMenu', id, info.startX, info.startY);
                        }
                    }));
                    state.sessions.push(h.attachKeyboardMove(treeBody, {
                        selector: ROW_SELECTOR,
                        // Alt+arrow inside an inline editor belongs to the field, not to the row: the
                        // shared helper vetoes the chord before it would cancel the browser's key.
                        accept: function (event) {
                            return !(event.target && event.target.closest && event.target.closest('input, select, textarea'));
                        },
                        onStep: function (step) {
                            if (step.y === 0 || step.shift) return;
                            var row = closestOf(step.event && step.event.target, ROW_SELECTOR);
                            var id = row && row.dataset ? row.dataset.taskId : null;
                            if (id) notify(state, 'OnTreeKeyboardMove', id, step.y);
                        }
                    }));
                    return true;
                } catch (e) {
                    warn('attachTree', e);
                    teardown(treeStates, id, state);
                    return false;
                }
            });
            return state.ready;
        },

        detachTree: function (id) {
            const state = id === null || id === undefined ? null : treeStates.get(id);
            if (state) teardown(treeStates, id, state);
            return Promise.resolve();
        }
    };
})();
