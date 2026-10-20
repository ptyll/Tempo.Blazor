// ═══════════════════════════════════════════════════════════════
// Tempo Workflow Designer JS interop — node drag, transition creation,
// pan, pinch zoom, auto-scroll, keyboard move.
//
// Every gesture lives on the shared core helpers: pointer-drag.js (the three
// single-pointer sessions) and pointer-gesture.js (the two-finger pinch and
// the ctrl+wheel zoom, plus the edge auto-scroller). This file is a classic
// script (window.tmWorkflowDesigner), so it loads the helpers with a dynamic
// import() resolved from its own URL. Only the workflow maths lives here:
// the viewBox, the grid, and the OnNodeDragged / OnTransitionCreated /
// OnViewBoxChanged / OnZoomChanged / OnNodeKeyboardMoved / OnNodeKeyboardDeleted
// calls into .NET. docs/touch-interaction.md is the contract.
// ═══════════════════════════════════════════════════════════════

(function () {
    'use strict';

    // Resolved NOW: document.currentScript is only set while the classic script runs synchronously.
    var helperUrls = (function () {
        try {
            var script = document.currentScript;
            if (script && script.src) {
                return {
                    drag: new URL('pointer-drag.js', script.src).href,
                    gesture: new URL('pointer-gesture.js', script.src).href,
                    menuNav: new URL('tm-menu-nav.js', script.src).href
                };
            }
            return {
                drag: new URL('_content/Tempo.Blazor/js/pointer-drag.js', document.baseURI).href,
                gesture: new URL('_content/Tempo.Blazor/js/pointer-gesture.js', document.baseURI).href,
                menuNav: new URL('_content/Tempo.Blazor/js/tm-menu-nav.js', document.baseURI).href
            };
        } catch (e) {
            return {
                drag: '_content/Tempo.Blazor/js/pointer-drag.js',
                gesture: '_content/Tempo.Blazor/js/pointer-gesture.js',
                menuNav: '_content/Tempo.Blazor/js/tm-menu-nav.js'
            };
        }
    })();

    var PORT_SELECTOR = '[data-port]';
    var STATE_SELECTOR = '[data-state-id]';
    var STATE_AND_PORT_SELECTOR = '[data-state-id],[data-port]';
    var NODE_DRAG_THRESHOLD = 4;  // px before a press becomes a drag; below it a tap keeps its click
    var PORT_DRAG_THRESHOLD = 2;  // the port has no click of its own, so the arm is short
    var ZOOM_MIN = 0.25;          // absolute zoom limits, as fractions of the 800-wide base viewBox
    var ZOOM_MAX = 4.0;
    var BASE_W = 800;             // the viewBox width that reads as scale 1.0

    function parseTranslate(g) {
        var t = (g && g.getAttribute && g.getAttribute('transform')) || '';
        var m = t.match(/translate\(\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*\)/);
        return m ? { x: parseFloat(m[1]), y: parseFloat(m[2]) } : { x: 0, y: 0 };
    }

    window.tmWorkflowDesigner = {
        instances: new Map(),

        init: function (svgElement, dotNetRef, readOnly, options) {
            options = options || {};
            var id = svgElement.id || ('wf-' + Math.random().toString(36).slice(2, 11));
            svgElement.id = id;

            var inst = {
                svg: svgElement,
                dotNetRef: dotNetRef,
                readOnly: !!readOnly,
                gridSize: typeof options.gridSize === 'number' && options.gridSize > 0 ? options.gridSize : 20,
                // the helper sessions (created by the async _attachEvents)
                portDrag: null,
                nodeDrag: null,
                panDrag: null,
                pinch: null,
                keyboard: null,
                scroller: null,
                // per-gesture state
                transitionFromId: null,
                tempLine: null,
                draggedStateId: null,
                dragStartSvg: null,
                dragStartNodePos: null,
                dragStartTransform: null,
                lastPointer: null,
                panStartClient: null,
                viewBoxStart: null,
                viewBoxStartAttr: null,
                pinchStart: null,
                generation: 0
            };

            this.instances.set(id, inst);
            this._attachEvents(inst);
            return id;
        },

        destroy: function (svgElement) {
            var inst = this.instances.get(svgElement.id);
            if (!inst) return;
            inst.generation++;               // a destroy during the helper import wins
            if (inst.portDrag) inst.portDrag.detach();
            if (inst.nodeDrag) inst.nodeDrag.detach();
            if (inst.panDrag) inst.panDrag.detach();
            if (inst.pinch) inst.pinch.detach();
            if (inst.keyboard) inst.keyboard.detach();
            if (inst.scroller) inst.scroller.stop();
            if (inst._onKeyDown) inst.svg.removeEventListener('keydown', inst._onKeyDown);
            if (inst._onContextMenu) inst.svg.removeEventListener('contextmenu', inst._onContextMenu);
            this._removeTempLine(inst);
            this.instances.delete(svgElement.id);
        },

        setReadOnly: function (svgElement, readOnly) {
            var inst = this.instances.get(svgElement.id);
            if (inst) inst.readOnly = !!readOnly;
        },

        /**
         * Focuses a state node (the roving-focus move after Blazor re-rendered the tabindex).
         * Elements with tabindex="-1" accept programmatic focus, so this always lands.
         */
        focusState: function (svgElement, stateId) {
            var g = svgElement.querySelector('[data-state-id="' + stateId + '"]');
            if (g && typeof g.focus === 'function') g.focus();
        },

        // ── helper attachment ──

        _attachEvents: function (inst) {
            var self = this;
            var generation = ++inst.generation;

            inst.ready = Promise.all([
                import(helperUrls.drag),
                import(helperUrls.gesture)
            ]).then(function (modules) {
                // init/destroy was called again while the modules were loading.
                if (generation !== inst.generation) return;
                var drag = modules[0];
                var gesture = modules[1];

                // (1) Transition creation: a press on a connection port draws the temp line, the
                // release drops it on whatever state sits under the pointer.
                inst.portDrag = drag.attach(inst.svg, {
                    selector: PORT_SELECTOR,
                    threshold: PORT_DRAG_THRESHOLD,
                    onPress: function () { return !inst.readOnly; },
                    onStart: function (info) {
                        inst.transitionFromId = info.target.getAttribute('data-port');
                        inst.dragStartSvg = self._svgPoint(inst, info.startX, info.startY);
                        self._createTempLine(inst);
                    },
                    onMove: function (info) {
                        if (!inst.tempLine) return;
                        var p = self._svgPoint(inst, info.x, info.y);
                        inst.tempLine.setAttribute('x2', p.x);
                        inst.tempLine.setAttribute('y2', p.y);
                    },
                    onEnd: function (info) {
                        // The pointer was captured by the port: hit-test the REAL drop point.
                        var hit = (typeof document !== 'undefined' && document.elementFromPoint)
                            ? document.elementFromPoint(info.x, info.y) : null;
                        var stateG = (hit && hit.closest) ? hit.closest(STATE_SELECTOR) : null;
                        var toId = stateG ? stateG.getAttribute('data-state-id') : null;
                        var fromId = inst.transitionFromId;
                        inst.transitionFromId = null;
                        self._removeTempLine(inst);
                        if (toId && toId !== fromId) {
                            inst.dotNetRef.invokeMethodAsync('OnTransitionCreated', fromId, toId);
                        }
                    },
                    onCancel: function () {
                        inst.transitionFromId = null;
                        self._removeTempLine(inst);
                    }
                });

                // (2) Node drag: touch starts immediately (no long-press — a canvas owns its
                // surface, as in the diagram editor). The pinch takes over via onStart below.
                inst.nodeDrag = drag.attach(inst.svg, {
                    selector: STATE_SELECTOR,
                    threshold: NODE_DRAG_THRESHOLD,
                    onPress: function (info) {
                        if (inst.readOnly) return false;
                        // The port is a child of the state group: the port session owns that press.
                        var hit = info.event && info.event.target;
                        if (hit && hit.closest && hit.closest(PORT_SELECTOR)) return false;
                        return true;
                    },
                    onStart: function (info) {
                        var g = info.target;
                        inst.draggedStateId = g.getAttribute('data-state-id');
                        inst.dragStartSvg = self._svgPoint(inst, info.startX, info.startY);
                        // VERBATIM snapshot: a cancelled drag puts the exact attribute back.
                        inst.dragStartTransform = g.getAttribute('transform');
                        inst.dragStartNodePos = parseTranslate(g);
                    },
                    onMove: function (info) {
                        inst.lastPointer = { x: info.x, y: info.y };
                        if (inst.scroller) inst.scroller.update(info.x, info.y);
                        self._moveDraggedNode(inst, info.x, info.y);
                    },
                    onEnd: function (info) {
                        var p = self._svgPoint(inst, info.x, info.y);
                        var x = inst.dragStartNodePos.x + (p.x - inst.dragStartSvg.x);
                        var y = inst.dragStartNodePos.y + (p.y - inst.dragStartSvg.y);
                        var stateId = inst.draggedStateId;
                        inst.draggedStateId = null;
                        inst.dragStartSvg = null;
                        inst.dragStartNodePos = null;
                        inst.dragStartTransform = null;
                        inst.dotNetRef.invokeMethodAsync('OnNodeDragged', stateId, x, y);
                    },
                    onCancel: function () {
                        // A cancelled drag persists nothing: the start transform goes back VERBATIM
                        // (before .NET hears about anything, so a synchronous re-render never fights it).
                        if (inst.draggedStateId) {
                            var g = self._stateEl(inst, inst.draggedStateId);
                            if (g) {
                                if (inst.dragStartTransform === null || inst.dragStartTransform === undefined) {
                                    g.removeAttribute('transform');
                                } else {
                                    g.setAttribute('transform', inst.dragStartTransform);
                                }
                            }
                        }
                        inst.draggedStateId = null;
                        inst.dragStartSvg = null;
                        inst.dragStartNodePos = null;
                        inst.dragStartTransform = null;
                    },
                    onRelease: function () {
                        if (inst.scroller) inst.scroller.stop();
                        inst.lastPointer = null;
                    }
                });

                // (3) Pan: the empty canvas. touch-action is owned by CSS (the svg root), so the
                // helper leaves the element alone.
                inst.panDrag = drag.attach(inst.svg, {
                    threshold: NODE_DRAG_THRESHOLD,
                    touchAction: null,
                    onPress: function (info) {
                        var hit = info.event && info.event.target;
                        if (hit && hit.closest && hit.closest(STATE_AND_PORT_SELECTOR)) return false;
                        return true;
                    },
                    onStart: function (info) {
                        inst.panStartClient = { x: info.startX, y: info.startY };
                        inst.viewBoxStart = self._getViewBox(inst);
                        inst.viewBoxStartAttr = inst.svg.getAttribute('viewBox');
                    },
                    onMove: function (info) {
                        var vb = inst.viewBoxStart;
                        var rect = inst.svg.getBoundingClientRect();
                        if (!rect.width || !rect.height) return;
                        var dx = (info.x - inst.panStartClient.x) * (vb.w / rect.width);
                        var dy = (info.y - inst.panStartClient.y) * (vb.h / rect.height);
                        self._setViewBox(inst, vb.x - dx, vb.y - dy, vb.w, vb.h);
                    },
                    onEnd: function () {
                        var vb = self._getViewBox(inst);
                        inst.panStartClient = null;
                        inst.viewBoxStart = null;
                        inst.viewBoxStartAttr = null;
                        inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
                    },
                    onCancel: function () {
                        // Verbatim restore: the pre-gesture ATTRIBUTE (absent before = absent again).
                        if (inst.viewBoxStartAttr === null || inst.viewBoxStartAttr === undefined) {
                            inst.svg.removeAttribute('viewBox');
                        } else if (inst.svg.getAttribute('viewBox') !== inst.viewBoxStartAttr) {
                            inst.svg.setAttribute('viewBox', inst.viewBoxStartAttr);
                        }
                        inst.panStartClient = null;
                        inst.viewBoxStart = null;
                        inst.viewBoxStartAttr = null;
                    }
                });

                // (4) Pinch zoom + ctrl+wheel (pointer-gesture.js). A pinch is never the only way:
                // the canvas overlay zoom buttons and the toolbox zoom are the single-pointer paths.
                inst.pinch = gesture.attachPinch(inst.svg, {
                    pointerTypes: ['touch'],
                    wheel: true,
                    touchAction: null,   // CSS owns touch-action on the canvas
                    onStart: function () {
                        // The host hands over: the single-finger gestures roll back (never commit).
                        if (inst.nodeDrag) inst.nodeDrag.cancel('pinch');
                        if (inst.panDrag) inst.panDrag.cancel('pinch');
                        if (inst.portDrag) inst.portDrag.cancel('pinch');
                        inst.pinchStart = self._getViewBox(inst);
                    },
                    onChange: function (info) {
                        self._applyZoom(inst, info.scale, info.centerX, info.centerY);
                    },
                    onEnd: function (info) {
                        self._applyZoom(inst, info.scale, info.centerX, info.centerY);
                        inst.pinchStart = null;
                        var vb = self._getViewBox(inst);
                        // The viewBox first: OnZoomChanged re-renders, and that render must see
                        // the committed viewBox fields, not the pre-gesture ones.
                        inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
                        inst.dotNetRef.invokeMethodAsync('OnZoomChanged', BASE_W / vb.w);
                    },
                    onCancel: function () {
                        if (!inst.pinchStart) return;
                        self._setViewBox(inst, inst.pinchStart.x, inst.pinchStart.y, inst.pinchStart.w, inst.pinchStart.h);
                        inst.pinchStart = null;
                    }
                });

                // (5) Edge auto-scroll: a node dragged against the viewport edge scrolls the page
                // (the canvas itself never scrolls); onScroll re-positions the node under the pointer.
                if (gesture.createAutoScroller) {
                    inst.scroller = gesture.createAutoScroller(null, {
                        onScroll: function () {
                            if (inst.draggedStateId && inst.lastPointer) {
                                self._moveDraggedNode(inst, inst.lastPointer.x, inst.lastPointer.y);
                            }
                        }
                    });
                }

                // (6) Keyboard move (WCAG 2.5.7): Alt+arrows on a focused state move it one grid
                // step through the SAME commit path as the drag.
                inst.keyboard = drag.attachKeyboardMove(inst.svg, {
                    selector: STATE_SELECTOR,
                    onStep: function (step) {
                        if (inst.readOnly) return;
                        var g = step.event && step.event.target && step.event.target.closest
                            ? step.event.target.closest(STATE_SELECTOR) : null;
                        if (!g) return;
                        var stateId = g.getAttribute('data-state-id');
                        var pos = parseTranslate(g);
                        var x = pos.x + step.x * inst.gridSize;
                        var y = pos.y + step.y * inst.gridSize;
                        g.setAttribute('transform', 'translate(' + x + ', ' + y + ')');
                        inst.dotNetRef.invokeMethodAsync('OnNodeKeyboardMoved', stateId, x, y);
                    }
                });

                // Delegated state keys: Delete removes the focused state (the context-menu command
                // is the pointer path), and the keys the roving focus uses are cancelled here, in
                // JS — the arrow scroll, Space (Blazor activates on keyup; the keydown default is
                // only the page scroll) and the browser's own Shift+F10 menu (a Blazor
                // `:preventDefault` would swallow Tab as well).
                inst._onKeyDown = function (e) {
                    var target = e.target;
                    var g = target && target.closest ? target.closest(STATE_SELECTOR) : null;
                    if (!g) return;
                    if (!e.altKey && (e.key === 'ArrowLeft' || e.key === 'ArrowRight'
                        || e.key === 'ArrowUp' || e.key === 'ArrowDown')) {
                        e.preventDefault && e.preventDefault();
                        return;
                    }
                    if (!e.altKey && e.key === ' ') {
                        e.preventDefault && e.preventDefault();
                        return;
                    }
                    if ((e.key === 'F10' && e.shiftKey) || e.key === 'ContextMenu') {
                        e.preventDefault && e.preventDefault();
                        return;
                    }
                    if (e.key !== 'Delete' || inst.readOnly) return;
                    e.preventDefault && e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnNodeKeyboardDeleted', g.getAttribute('data-state-id'));
                };
                inst.svg.addEventListener('keydown', inst._onKeyDown);

                // (7) The context menu: the pointer route to the same menu the keyboard opens with
                // Shift+F10. Read-only leaves it to the browser; an open menu cancels the gestures
                // in flight, exactly like the pinch's hand-over.
                inst._onContextMenu = function (e) {
                    if (inst.readOnly) return;
                    e.preventDefault && e.preventDefault();
                    if (inst.nodeDrag) inst.nodeDrag.cancel('contextmenu');
                    if (inst.panDrag) inst.panDrag.cancel('contextmenu');
                    if (inst.portDrag) inst.portDrag.cancel('contextmenu');
                    var p = self._svgPoint(inst, e.clientX, e.clientY);
                    var target = e.target && e.target.closest ? e.target : null;
                    var stateG = target ? target.closest(STATE_SELECTOR) : null;
                    var transitionG = stateG ? null : (target ? target.closest('[data-transition-id]') : null);
                    var host = inst.svg.parentElement;
                    var hostRect = (host && host.getBoundingClientRect)
                        ? host.getBoundingClientRect() : { left: 0, top: 0 };
                    inst.dotNetRef.invokeMethodAsync('OnContextMenu',
                        p.x, p.y,
                        e.clientX - hostRect.left, e.clientY - hostRect.top,
                        stateG ? stateG.getAttribute('data-state-id') : null,
                        transitionG ? transitionG.getAttribute('data-transition-id') : null);
                };
                inst.svg.addEventListener('contextmenu', inst._onContextMenu);
            }).catch(function (err) {
                console.warn('tmWorkflowDesigner: pointer helpers could not be loaded; drag/zoom are unavailable', err);
            });
        },

        // ── geometry helpers ──

        _svgPoint: function (inst, clientX, clientY) {
            var pt = inst.svg.createSVGPoint();
            pt.x = clientX;
            pt.y = clientY;
            var ctm = inst.svg.getScreenCTM();
            if (!ctm) return { x: 0, y: 0 };
            var svgPt = pt.matrixTransform(ctm.inverse());
            return { x: svgPt.x, y: svgPt.y };
        },

        _getViewBox: function (inst) {
            var vb = inst.svg.viewBox.baseVal;
            return { x: vb.x, y: vb.y, w: vb.width, h: vb.height };
        },

        _setViewBox: function (inst, x, y, w, h) {
            inst.svg.setAttribute('viewBox', x + ' ' + y + ' ' + w + ' ' + h);
        },

        _moveDraggedNode: function (inst, clientX, clientY) {
            if (!inst.draggedStateId || !inst.dragStartSvg || !inst.dragStartNodePos) return;
            var p = this._svgPoint(inst, clientX, clientY);
            var x = inst.dragStartNodePos.x + (p.x - inst.dragStartSvg.x);
            var y = inst.dragStartNodePos.y + (p.y - inst.dragStartSvg.y);
            var g = this._stateEl(inst, inst.draggedStateId);
            if (g) g.setAttribute('transform', 'translate(' + x + ', ' + y + ')');
        },

        _stateEl: function (inst, stateId) {
            return inst.svg.querySelector('[data-state-id="' + stateId + '"]');
        },

        /**
         * Zooms by `scale` (relative to the gesture start) keeping the viewBox point under the
         * client point stationary, clamped to the absolute zoom limits. All maths run in viewBox
         * space so a mid-gesture re-read of the (already zoomed) CTM cannot drift.
         */
        _applyZoom: function (inst, scale, clientX, clientY) {
            var start = inst.pinchStart;
            if (!start || !scale || !isFinite(scale) || scale <= 0) return;
            var rect = inst.svg.getBoundingClientRect();
            if (!rect.width || !rect.height) return;
            var newW = Math.min(BASE_W / ZOOM_MIN, Math.max(BASE_W / ZOOM_MAX, start.w / scale));
            var newH = start.h * (newW / start.w);
            var px = clientX - rect.left;
            var py = clientY - rect.top;
            var svgX = start.x + px * (start.w / rect.width);
            var svgY = start.y + py * (start.h / rect.height);
            this._setViewBox(inst, svgX - px * (newW / rect.width), svgY - py * (newH / rect.height), newW, newH);
        },

        // ── temp transition line ──

        _createTempLine: function (inst) {
            var ns = 'http://www.w3.org/2000/svg';
            var p = inst.dragStartSvg || { x: 0, y: 0 };
            var line = document.createElementNS(ns, 'line');
            line.setAttribute('x1', p.x);
            line.setAttribute('y1', p.y);
            line.setAttribute('x2', p.x);
            line.setAttribute('y2', p.y);
            // Colour and dash live in CSS (a token, forced-colors safe) — never an inline hex.
            line.setAttribute('class', 'tm-wf-temp-line');
            line.setAttribute('pointer-events', 'none');
            inst.svg.appendChild(line);
            inst.tempLine = line;
        },

        _removeTempLine: function (inst) {
            if (inst.tempLine && inst.tempLine.parentNode) {
                inst.tempLine.parentNode.removeChild(inst.tempLine);
            }
            inst.tempLine = null;
        },

        // ── programmatic zoom ──
        // Both helpers report the new geometry back: .NET assigns its _viewBox fields from the
        // result BEFORE StateHasChanged, so the Blazor-rendered viewBox attribute and the JS one
        // can never disagree.

        zoomTo: function (svgElement, scale) {
            var inst = this.instances.get(svgElement.id);
            if (!inst) return null;
            scale = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, scale));
            var vb = this._getViewBox(inst);
            var cx = vb.x + vb.w / 2;
            var cy = vb.y + vb.h / 2;
            var newW = BASE_W / scale;
            var newH = 500 / scale;
            var x = cx - newW / 2;
            var y = cy - newH / 2;
            this._setViewBox(inst, x, y, newW, newH);
            return [x, y, newW, newH];
        },

        fitToView: function (svgElement, padding) {
            var inst = this.instances.get(svgElement.id);
            if (!inst) return null;
            padding = padding || 40;

            var nodes = inst.svg.querySelectorAll(STATE_SELECTOR);
            if (nodes.length === 0) {
                var current = this._getViewBox(inst);
                return { scale: BASE_W / current.w, x: current.x, y: current.y, w: current.w, h: current.h };
            }

            var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
            nodes.forEach(function (g) {
                var pos = parseTranslate(g);
                minX = Math.min(minX, pos.x);
                minY = Math.min(minY, pos.y);
                maxX = Math.max(maxX, pos.x + 140); // NodeW
                maxY = Math.max(maxY, pos.y + 50);  // NodeH
            });

            if (!isFinite(minX)) {
                var fallback = this._getViewBox(inst);
                return { scale: BASE_W / fallback.w, x: fallback.x, y: fallback.y, w: fallback.w, h: fallback.h };
            }

            var contentW = maxX - minX + padding * 2;
            var contentH = maxY - minY + padding * 2;
            var svgRect = inst.svg.getBoundingClientRect();
            var scaleX = svgRect.width / contentW;
            var scaleY = svgRect.height / contentH;
            var fitScale = Math.min(scaleX, scaleY, 2.0);

            var newW = svgRect.width / fitScale;
            var newH = svgRect.height / fitScale;
            var x = minX - padding;
            var y = minY - padding;
            this._setViewBox(inst, x, y, newW, newH);
            return { scale: fitScale, x: x, y: y, w: newW, h: newH };
        },

        // ── context menu / roving focus helpers ──

        /**
         * Focuses the first item of the just-opened context menu and hands the arrow/Home/End
         * roving to the shared menu keyboard module (tm-menu-nav.js). The menu element is the
         * Blazor-rendered role="menu" div; the focus move is synchronous, the module attaches
         * behind it.
         */
        focusMenu: function (menuElement) {
            if (!menuElement) return;
            var first = menuElement.querySelector('.tm-wf-context-menu__item');
            if (first && typeof first.focus === 'function') first.focus();
            import(helperUrls.menuNav).then(function (nav) {
                if (typeof nav.attach === 'function') nav.attach(menuElement);
            }).catch(function () { /* the arrows keep the browser default (no menu roving) */ });
        },

        /**
         * The last-resort focus target when a keyboard delete removed the last state: the zoom
         * buttons (single-pointer chrome that survives an empty canvas), else the svg itself.
         */
        focusZoom: function (svgElement) {
            var host = svgElement.parentElement;
            var btn = (host && host.querySelector) ? host.querySelector('.tm-wf-zoom-overlay__btn') : null;
            if (btn && typeof btn.focus === 'function') {
                btn.focus();
                return;
            }
            if (typeof svgElement.focus === 'function') svgElement.focus();
        },

        /**
         * Where the KEYBOARD-opened context menu belongs: the state's real bounding rect
         * (measured, so zoom and pan are already in) minus the canvas container rect — the
         * coordinate space the absolutely-positioned menu lives in.
         */
        stateMenuPoint: function (svgElement, stateId) {
            var g = svgElement.querySelector
                ? svgElement.querySelector('[data-state-id="' + stateId + '"]') : null;
            var host = svgElement.parentElement;
            if (!g || !g.getBoundingClientRect || !host || !host.getBoundingClientRect) return null;
            var r = g.getBoundingClientRect();
            var h = host.getBoundingClientRect();
            var bottom = (typeof r.bottom === 'number') ? r.bottom : (r.top + r.height);
            return [r.left - h.left, bottom - h.top];
        }
    };
})();
