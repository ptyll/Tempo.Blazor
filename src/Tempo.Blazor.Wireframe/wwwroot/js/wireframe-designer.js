// Tempo Wireframe Designer – SVG canvas engine
// Pan, zoom, drag, resize, multi-select, toolbox drop, keyboard shortcuts, context menu.
// Pointer Events end to end (mouse, finger, pen): single-pointer gestures are sessions of the shared core
// pointer-drag.js, the two-finger pinch/pan is pointer-gesture.js attachPinch, and the pure wireframe maths live
// in wireframe-gestures.mjs. See docs/touch-interaction.md - never write a second drag or pinch here.

// Resolved NOW: document.currentScript is only set while a classic script runs synchronously. The core helpers sit
// next to this package in the deployed _content layout (_content/Tempo.Blazor.Wireframe/js/ -> ../../Tempo.Blazor/js/).
var tmWdHelperUrls = (function () {
    try {
        var script = document.currentScript;
        if (script && script.src) {
            return {
                drag: new URL('../../Tempo.Blazor/js/pointer-drag.js', script.src).href,
                gesture: new URL('../../Tempo.Blazor/js/pointer-gesture.js', script.src).href,
                gestures: new URL('wireframe-gestures.mjs', script.src).href
            };
        }
    } catch (e) { /* fall through to the document-relative layout */ }
    // Absolute against the document base: a bare '_content/...' string is not a module specifier (TypeError).
    var base = '';
    try { base = document.baseURI || ''; } catch (e) { /* no document (a unit-test host) */ }
    function resolve(path) {
        try { return new URL(path, base).href; } catch (e) { return path; }
    }
    return {
        drag: resolve('_content/Tempo.Blazor/js/pointer-drag.js'),
        gesture: resolve('_content/Tempo.Blazor/js/pointer-gesture.js'),
        gestures: resolve('_content/Tempo.Blazor.Wireframe/js/wireframe-gestures.mjs')
    };
})();

window.tmWireframeDesigner = {

    instances: new Map(),

    // ── Init / Destroy ────────────────────────────────────────────────────────

    init: function (svgElement, dotNetRef, options) {
        const id = svgElement.id || 'wfd-' + Math.random().toString(36).substr(2, 9);
        svgElement.id = id;

        const opts = options || {};
        const inst = {
            svg: svgElement,
            dotNetRef: dotNetRef,
            readOnly: !!opts.readOnly,
            // Mobile viewing + light edits (G2): no resize / rotate handles, no rubber band, no connector tool / waypoints; a
            // one-finger drag on the empty canvas pans. Driven by TmWireframeDesignerCanvas.LightEditing.
            lightEditing: !!opts.lightEditing,
            gridSize: opts.gridSize || 8,
            showGrid: opts.showGrid !== false,
            canvasW: opts.canvasWidth || 1200,
            canvasH: opts.canvasHeight || 800,
            // Initial zoom (decision WIREFRAME-INITIAL-ZOOM): 'fit' | 'actual', or undefined for a standalone host that never asked
            // (the svg's own meet fit, as before). Set by TmWireframeDesignerCanvas.InitialZoom.
            initialZoom: opts.initialZoom === 'fit' || opts.initialZoom === 'actual' ? opts.initialZoom : undefined,

            // drag-element state
            isDragging: false,
            dragElIds: [],          // ids being dragged (may be multi)
            dragStartSvg: null,     // SVG point at mousedown
            dragStartPositions: {}, // id → {x,y} at drag start

            // resize state
            isResizing: false,
            resizeElId: null,
            resizeHandle: null,     // 'nw','n','ne','e','se','s','sw','w'
            resizeStartSvg: null,
            resizeStartRect: null,  // {x,y,w,h} at resize start

            // rubber-band selection
            isRubberBand: false,
            rubberStart: null,      // SVG point
            rubberRect: null,       // SVG <rect> element

            // pan state
            isPanning: false,
            panStart: null,
            viewBoxStart: null,

            // tool mode: 'select' | 'pan' | 'connector'
            toolMode: 'select',
            spaceHeld: false,

            // pointer sessions (shared helpers, attached asynchronously) and their live gesture state
            generation: 0,
            destroyed: false,
            sessions: [],
            body: null,
            gestures: null,

            // current selection
            selectedIds: new Set(),

            // scale tracker
            scale: 1.0,

            // true once the user (or fit-to-elements) moved / zoomed the view; relayout() re-fits a pristine view
            userViewChanged: false,
            layoutRect: null,           // the svg size the viewBox was last laid out for
            resizeObserver: null,

            // global configuration (merged with options)
            config: {
                gridSize: opts.gridSize || 8,
                snapToGrid: opts.gridSize > 0,
                snapToObjects: !!opts.snapToObjects,
                showRulers: false,
            },

            // format painter state
            formatPainterActive: false,

            // connector state
            isDrawingConnector: false,
            connectorFromId: null,
            connectorStartSvg: null,
            connectorPreview: null,

            // waypoint drag state
            isDraggingWaypoint: false,
            dragWaypointConnId: null,
            dragWaypointIndex: null,
            dragWaypointHandle: null,
            dragWaypointStartSvg: null,

            // connector drag state (move all waypoints)
            isDraggingConnector: false,
            dragConnectorId: null,
            dragConnectorStartSvg: null,
            dragConnectorCurrentSvg: null,
        };

        this.instances.set(id, inst);
        this._attachEvents(inst);
        return id;
    },

    destroy: function (svgElement) {
        const id = svgElement.id;
        const inst = this.instances.get(id);
        if (!inst) return;

        // Detach all DOM event listeners first (cancels a gesture in progress: the live DOM rolls back)
        inst.destroyed = true;
        this._detachEvents(inst);

        // Remove JS-injected overlay elements (selection handles, rubber-band rect, context menu)
        inst.svg.querySelectorAll('.tm-wd-selection, .tm-wd-rubber, .tm-wd-ctx-menu, .tm-wd-hold-cue').forEach(el => el.remove());

        // Cancel any pending drag / resize animation frames
        if (inst._rafId) cancelAnimationFrame(inst._rafId);

        // Release the .NET object reference so the GC can collect the Blazor component
        if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
            inst.dotNetRef.dispose();
        }

        // Null-out references to help GC
        inst.svg = null;
        inst.dotNetRef = null;
        inst.rubberRect = null;

        this.instances.delete(id);
    },

    setReadOnly: function (svgElement, readOnly) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        inst.readOnly = !!readOnly;
        // The editing gestures in flight (drag, resize, rotate, waypoint, hold) roll back and commit nothing;
        // navigation (pinch, pan tool) keeps running.
        if (inst.readOnly) {
            this._cancelEditing(inst, 'read-only');
            this._updateHandles(inst);
        }
    },

    /**
     * Switches the mobile viewing + light edits profile (the editor crossed its 768 px breakpoint). Entering it rolls back a resize /
     * rotate / waypoint gesture in flight and removes the handles; leaving it brings them back.
     */
    setLightEditing: function (svgElement, light) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        inst.lightEditing = !!light;
        if (inst.lightEditing) {
            [inst.resizeSession, inst.rotateSession, inst.waypointSession].forEach(function (session) { if (session) session.cancel('light-editing'); });
            if (inst.toolMode === 'connector') {
                this.setToolMode(svgElement, 'select');
                this._invokeQuiet(inst, 'OnToolModeChanged', 'select');   // the editor's tool row shows the real tool
            }
        }
        this._updateHandles(inst);
    },

    // ── Tool mode (select / pan) ──────────────────────────────────────────────

    setToolMode: function (svgElement, mode) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        inst.toolMode = mode;
        this._updateCursor(inst);
    },

    // ── Canvas size update (called after ResizeCanvasCommand) ─────────────────

    updateCanvasSize: function (svgElement, w, h) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        inst.canvasW = w;
        inst.canvasH = h;
        // Blazor re-renders the background rect and resets the svg viewBox to the new page size: the view is fitted again.
        inst.userViewChanged = false;
        inst.layoutRect = this._measure(inst);
    },

    setGridSize: function (svgElement, size) {
        const inst = this.instances.get(svgElement.id);
        if (inst) inst.gridSize = size;
    },

    setSnapToObjects: function (svgElement, enabled) {
        const inst = this.instances.get(svgElement.id);
        if (inst) inst.config.snapToObjects = !!enabled;
    },

    setFormatPainterActive: function (svgElement, active) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        inst.formatPainterActive = !!active;
        this._updateCursor(inst);
    },

    // ── Event attachment ──────────────────────────────────────────────────────
    //
    // Pointer work runs on the SHARED core helpers (docs/touch-interaction.md): pointer-drag.js `attach` for every
    // single-pointer gesture - mouse, finger and pen alike, the touch long-press being its `longPressMs` - and
    // pointer-gesture.js `attachPinch` for the two-finger pinch/pan. wireframe-gestures.mjs holds only the pure
    // wireframe maths (double-tap, coarse handle sizes, pinch -> viewBox, the nudge step). This file never
    // detects a pinch or a long-press of its own. The helper modules load asynchronously (dynamic import resolved
    // from this script's own URL), so the sessions attach when `inst.ready` settles; the plain listeners below
    // (wheel, contextmenu, keyboard, drop) work from the first frame.

    _loadHelpers: function () {
        if (!this._helpers) {
            this._helpers = Promise.all([
                import(tmWdHelperUrls.drag),
                import(tmWdHelperUrls.gesture),
                import(tmWdHelperUrls.gestures)
            ]).then(function (modules) {
                return { drag: modules[0], gesture: modules[1], gestures: modules[2] };
            }, (function (self) {
                return function (error) {
                    // A failed import (a CSP that blocks the module, a 404) must not stay cached: the next editor retries.
                    self._helpers = null;
                    throw error;
                };
            })(this));
        }
        return this._helpers;
    },

    _attachEvents: function (inst) {
        inst._onDblClick  = (e) => this._onDblClick(e, inst);
        inst._onWheel     = (e) => this._onWheel(e, inst);
        inst._onKeyDown   = (e) => this._onKeyDown(e, inst);
        inst._onKeyUp     = (e) => this._onKeyUp(e, inst);
        inst._onContextMenu = (e) => this._onContextMenu(e, inst);
        inst._onDragOver  = (e) => this._onDragOver(e, inst);
        inst._onDrop      = (e) => this._onDrop(e, inst);
        inst._onSvgPointerMove = (e) => this._onSvgPointerMove(e, inst);
        inst._onMiddleDown = (e) => this._onMiddleDown(e, inst);
        inst._onMiddleMove = (e) => this._onMiddleMove(e, inst);
        inst._onMiddleUp   = (e) => this._onMiddleUp(e, inst);

        inst.svg.addEventListener('dblclick',    inst._onDblClick);
        inst.svg.addEventListener('pointermove', inst._onSvgPointerMove);
        inst.svg.addEventListener('wheel',       inst._onWheel, { passive: false });
        inst.svg.addEventListener('contextmenu', inst._onContextMenu);
        inst.svg.addEventListener('dragover',    inst._onDragOver);
        inst.svg.addEventListener('drop',        inst._onDrop);
        // The middle mouse button is not a pointer-drag.js gesture (the helper only starts from the primary
        // button), so the mouse-only middle-button pan keeps its own minimal press/move/release below.
        inst.svg.addEventListener('pointerdown', inst._onMiddleDown);
        document.addEventListener('keydown',     inst._onKeyDown);
        document.addEventListener('keyup',       inst._onKeyUp);
        // Shift+F10 / the ContextMenu key also make the browser fire a native contextmenu on the focused wrapper (not the svg):
        // keep it from opening a second, native menu over ours.
        // Release-click guard of the mobile long-press sheet: the first click after the hold that opened it is swallowed, and ANY new press
        // disarms it (a real press means the user is interacting with the sheet or another control).
        inst._onReleaseClick = (e) => {
            if (!inst.holdClickArmed) return;
            if (Date.now() - (inst.lastHoldMenuAt || 0) > 5000) { inst.holdClickArmed = false; return; }
            inst.holdClickArmed = false;
            if (e.preventDefault) e.preventDefault();
            if (e.stopPropagation) e.stopPropagation();
        };
        inst._onReleasePress = () => { inst.holdClickArmed = false; };
        document.addEventListener('click', inst._onReleaseClick, true);
        document.addEventListener('pointerdown', inst._onReleasePress, true);
        inst._onDocContextMenu = (e) => {
            if (Date.now() - (inst.lastKeyMenuAt || 0) < 1000 && inst.svg && inst.svg.closest && e.target && e.target.closest
                && e.target.closest('.tm-wd-canvas-wrap') === inst.svg.closest('.tm-wd-canvas-wrap')) e.preventDefault();
        };
        document.addEventListener('contextmenu', inst._onDocContextMenu);

        inst.layoutRect = this._measure(inst);
        this._observeResize(inst);

        // The sessions of the shared helpers: attached once the modules are in.
        const generation = ++inst.generation;
        const self = this;
        inst.ready = this._loadHelpers().then(function (helpers) {
            // destroy() (or a re-init) ran while the modules were loading: attach nothing.
            if (inst.destroyed || generation !== inst.generation) return;
            self._attachSessions(inst, helpers);
        }).catch(function (error) {
            // Without the shared helpers the canvas has no pointer gestures at all: say so, loudly, instead of failing silently
            // (the keyboard, wheel and toolbar paths keep working).
            if (typeof console !== 'undefined' && console.error) {
                console.error('tmWireframeDesigner: the pointer helpers (pointer-drag.js, pointer-gesture.js, wireframe-gestures.mjs) '
                    + 'could not be loaded; canvas gestures are unavailable.', error);
            }
        });
    },

    _detachEvents: function (inst) {
        inst.svg.removeEventListener('dblclick',    inst._onDblClick);
        inst.svg.removeEventListener('pointermove', inst._onSvgPointerMove);
        inst.svg.removeEventListener('wheel',       inst._onWheel);
        inst.svg.removeEventListener('contextmenu', inst._onContextMenu);
        inst.svg.removeEventListener('dragover',    inst._onDragOver);
        inst.svg.removeEventListener('drop',        inst._onDrop);
        inst.svg.removeEventListener('pointerdown', inst._onMiddleDown);
        inst.svg.removeEventListener('pointermove', inst._onMiddleMove);
        inst.svg.removeEventListener('pointerup',   inst._onMiddleUp);
        inst.svg.removeEventListener('pointercancel', inst._onMiddleUp);
        document.removeEventListener('keydown',     inst._onKeyDown);
        document.removeEventListener('keyup',       inst._onKeyUp);
        document.removeEventListener('contextmenu', inst._onDocContextMenu);
        document.removeEventListener('click', inst._onReleaseClick, true);
        document.removeEventListener('pointerdown', inst._onReleasePress, true);

        this._cancelMiddlePan(inst);
        this._unobserveResize(inst);

        (inst.sessions || []).forEach(function (session) { session.detach(); });
        inst.sessions = [];
    },

    /**
     * What is under `target`: a connector, an element or the empty canvas - as IDS. A press selects, Blazor re-renders and
     * replaces the markup-rendered shape nodes, so a DOM node kept from the press is detached by the time a 500 ms hold or a
     * double-tap needs it (its closest() would then find nothing and the canvas menu would open instead of the element's).
     */
    _hitDescriptor: function (inst, target, event) {
        if (event) {
            inst.hitMemo = inst.hitMemo || new WeakMap();
            if (inst.hitMemo.has(event)) return inst.hitMemo.get(event);
            const resolved = this._hitDescriptor(inst, target);
            inst.hitMemo.set(event, resolved);
            return resolved;
        }
        const connHit = target && target.closest ? target.closest('.tm-wd-connector__hit') : null;
        if (connHit) {
            const connG = connHit.closest('[data-connector-id]');
            const connId = connG ? connG.getAttribute('data-connector-id') : null;
            if (connId) return { kind: 'connector', id: connId };
        }
        const elG = target && target.closest ? target.closest('[data-el-id]') : null;
        const id = elG ? elG.getAttribute('data-el-id') : null;
        return id ? { kind: 'element', id: id } : { kind: 'canvas' };
    },

    /** Presses that belong to a handle or a waypoint: the body/hold sessions must not claim them. */
    _isChromeTarget: function (target) {
        return !!(target && target.closest && target.closest(
            '[data-handle], [data-rotate-handle], .tm-wd-connector__waypoint, .tm-wd-connector__waypoint-hit'));
    },

    _focusWrap: function (inst) {
        // Focus the canvas wrapper so keyboard shortcuts (Delete, arrows, etc.) work immediately after
        // clicking away from a property panel input.
        const wrap = inst.svg.closest('.tm-wd-canvas-wrap');
        if (wrap) wrap.focus({ preventScroll: true });
    },

    _isCoarse: function (inst) {
        return !!(inst.gestures && inst.gestures.isCoarsePointer(window));
    },

    _attachSessions: function (inst, helpers) {
        const self = this;
        const drag = helpers.drag;
        const gesture = helpers.gesture;
        const gestures = helpers.gestures;
        inst.gestures = gestures;
        inst.doubleTap = gestures.createDoubleTapTracker();
        inst.sessions = [];
        const common = { touchAction: null, dragClass: null };   // CSS owns touch-action on the canvas

        // (1) Resize: a zero-latency drag on a selection handle (its visible dot or, on a coarse pointer, the larger
        // invisible hit rect). The handles are re-created while the geometry follows the pointer, so the gesture is
        // captured on the svg itself (the helper's default target), never on a handle that is about to be replaced.
        inst.resizeSession = drag.attach(inst.svg, Object.assign({}, common, {
            threshold: gestures.HANDLE_DRAG_THRESHOLD,
            suppressClick: false,
            onPress: function (info) {
                if (inst.readOnly || inst.lightEditing) return false;
                const handle = info.event.target && info.event.target.closest ? info.event.target.closest('[data-handle]') : null;
                const selG = handle && handle.closest('.tm-wd-selection');
                if (!selG) return false;
                const id = selG.getAttribute('data-sel-for');
                const rect = self._elRect(inst, id);
                if (!rect) return false;
                self._focusWrap(inst);
                inst.resize = {
                    id: id, handle: handle.getAttribute('data-handle'), rect: rect,
                    startSvg: self._svgPoint(inst, info.startX, info.startY), snapshot: self._snapshotElement(inst, id)
                };
                self._beginGesture(inst);
                return true;
            },
            onStart: function () { inst.isResizing = true; },
            onMove: function (info) { self._resizeMove(inst, info); },
            onEnd: function () {
                const id = inst.resize.id;
                const rect = self._elRect(inst, id);
                if (rect) inst.dotNetRef.invokeMethodAsync('OnElementResized', id, rect.x, rect.y, rect.w, rect.h);
            },
            onCancel: function (info) {
                self._restoreElement(inst.resize && inst.resize.snapshot);
                self._updateHandles(inst);
                self._announceCancelled(inst, info);
            },
            onRelease: function () { inst.isResizing = false; inst.resize = null; self._endGesture(inst); }
        }));
        inst.sessions.push(inst.resizeSession);

        // (2) Rotate: a drag on the rotate handle.
        inst.rotateSession = drag.attach(inst.svg, Object.assign({}, common, {
            threshold: gestures.HANDLE_DRAG_THRESHOLD,
            suppressClick: false,
            onPress: function (info) {
                if (inst.readOnly || inst.lightEditing) return false;
                const handle = info.event.target && info.event.target.closest ? info.event.target.closest('[data-rotate-handle]') : null;
                const selG = handle && handle.closest('.tm-wd-selection');
                if (!selG) return false;
                const id = selG.getAttribute('data-sel-for');
                const r = self._elRect(inst, id);
                if (!r) return false;
                self._focusWrap(inst);
                const center = { x: r.x + r.w / 2, y: r.y + r.h / 2 };
                const startSvg = self._svgPoint(inst, info.startX, info.startY);
                inst.rotate = {
                    id: id, center: center, startAngle: r.rotation || 0, snapshot: self._snapshotElement(inst, id),
                    startMouseAngle: Math.atan2(startSvg.y - center.y, startSvg.x - center.x) * 180 / Math.PI
                };
                self._beginGesture(inst);
                return true;
            },
            onStart: function () { inst.isRotating = true; },
            onMove: function (info) { self._rotateMove(inst, info); },
            onEnd: function () {
                const id = inst.rotate.id;
                const rect = self._elRect(inst, id);
                if (rect) inst.dotNetRef.invokeMethodAsync('OnElementRotated', id, rect.rotation);
            },
            onCancel: function (info) {
                self._restoreElement(inst.rotate && inst.rotate.snapshot);
                self._updateHandles(inst);
                self._announceCancelled(inst, info);
            },
            onRelease: function () { inst.isRotating = false; inst.rotate = null; self._endGesture(inst); }
        }));
        inst.sessions.push(inst.rotateSession);

        // (3) Connector waypoint drag.
        inst.waypointSession = drag.attach(inst.svg, Object.assign({}, common, {
            threshold: gestures.HANDLE_DRAG_THRESHOLD,
            suppressClick: false,
            onPress: function (info) {
                if (inst.readOnly || inst.lightEditing) return false;
                const grip = info.event.target && info.event.target.closest
                    ? info.event.target.closest('.tm-wd-connector__waypoint, .tm-wd-connector__waypoint-hit') : null;
                const connG = grip && grip.closest('[data-connector-id]');
                if (!connG) return false;
                const index = parseInt(grip.getAttribute('data-waypoint-index') || '0', 10);
                const circles = Array.from(connG.querySelectorAll('.tm-wd-connector__waypoint, .tm-wd-connector__waypoint-hit'))
                    .filter(function (c) { return parseInt(c.getAttribute('data-waypoint-index') || '0', 10) === index; });
                if (circles.length === 0) return false;
                self._focusWrap(inst);
                inst.waypoint = {
                    connId: connG.getAttribute('data-connector-id'), index: index, circles: circles,
                    startSvg: self._svgPoint(inst, info.startX, info.startY),
                    starts: circles.map(function (c) {
                        return { cx: c.getAttribute('cx'), cy: c.getAttribute('cy') };
                    })
                };
                self._beginGesture(inst);
                return true;
            },
            onStart: function () { inst.isDraggingWaypoint = true; },
            onMove: function (info) {
                const wp = inst.waypoint;
                const cur = self._svgPoint(inst, info.x, info.y);
                wp.circles.forEach(function (c, i) {
                    c.setAttribute('cx', parseFloat(wp.starts[i].cx || '0') + (cur.x - wp.startSvg.x));
                    c.setAttribute('cy', parseFloat(wp.starts[i].cy || '0') + (cur.y - wp.startSvg.y));
                });
            },
            onEnd: function () {
                const wp = inst.waypoint;
                const finalX = parseFloat(wp.circles[0].getAttribute('cx') || '0');
                const finalY = parseFloat(wp.circles[0].getAttribute('cy') || '0');
                inst.dotNetRef.invokeMethodAsync('OnConnectorWaypointDragged', wp.connId, wp.index, finalX, finalY);
            },
            onCancel: function (info) {
                const wp = inst.waypoint;
                if (!wp) return;
                wp.circles.forEach(function (c, i) {
                    c.setAttribute('cx', wp.starts[i].cx);
                    c.setAttribute('cy', wp.starts[i].cy);
                });
                self._announceCancelled(inst, info);
            },
            onRelease: function () { inst.isDraggingWaypoint = false; inst.waypoint = null; self._endGesture(inst); }
        }));
        inst.sessions.push(inst.waypointSession);

        // (4) The body gesture: press selects (like the old mousedown), the move drags an element, moves a selected
        // connector, draws a connector (connector tool), rubber-bands on the empty canvas or pans (Space / pan tool).
        // A tap commits nothing; a touch double-tap is a double-click.
        inst.bodyDrag = drag.attach(inst.svg, Object.assign({}, common, {
            threshold: gestures.DRAG_THRESHOLD,
            onPress: function (info) { return self._bodyPress(inst, info); },
            onStart: function (info) { return self._bodyStart(inst, info); },
            onMove: function (info) { self._bodyMove(inst, info); },
            onEnd: function (info) { self._bodyEnd(inst, info); },
            onCancel: function (info) { self._bodyCancel(inst, info); },
            onRelease: function (info) { self._bodyRelease(inst, info); }
        }));
        inst.sessions.push(inst.bodyDrag);

        // (5) The touch long-press (the helper's longPressMs, no detector of our own) opens the context menu, offset
        // from the finger so the lifting finger does not land on an item. The mouse keeps the native right-click.
        inst.holdSession = drag.attach(inst.svg, Object.assign({}, common, {
            pointerTypes: ['touch'],
            threshold: 0,
            longPressMs: gestures.LONG_PRESS_MS,
            suppressClick: false,
            onPress: function (info) {
                if (self._isChromeTarget(info.event.target)) return false;
                inst.holdHit = self._hitDescriptor(inst, info.event.target, info.event);
                self._showHoldCue(inst, info.startX, info.startY);
                return true;
            },
            onStart: function (info) {
                self._hideHoldCue(inst);   // the 500 ms hold completed: the menu (or nothing) replaces the cue
                // A drag that really moved owns the finger; a hold whose body gesture has not started yet rolls it
                // back (the menu replaces it) so the lift commits nothing.
                if (inst.isDragging || inst.isPanning || inst.isRubberBand || inst.isDraggingConnector
                    || inst.isDrawingConnector || inst.isResizing || inst.isRotating || inst.isDraggingWaypoint) {
                    return false;
                }
                if (inst.bodyDrag) inst.bodyDrag.cancel('long-press-menu');
                inst.lastHoldMenuAt = Date.now();
                // Light editing presents the menu as a MODAL bottom sheet that opens under the still-pressed finger: the compatibility click
                // of the lift would land on its backdrop or an entry and dismiss / run it. Arm the release-click guard (see _onReleaseClick).
                if (inst.lightEditing) inst.holdClickArmed = true;
                self._openContextMenuAt(inst, inst.holdHit, info.x, info.y, gestures.TOUCH_MENU_OFFSET);
                return false;   // the hold never becomes a drag
            },
            // Every exit (lift, drift beyond the tolerance, cancel, destroy) ends the cue.
            onRelease: function () { inst.holdHit = null; self._hideHoldCue(inst); }
        }));
        inst.sessions.push(inst.holdSession);

        // (6) Two-finger pinch + pan (pointer-gesture.js). Touch only: the mouse wheel zooms through _onWheel and a
        // trackpad pinch arrives as ctrl+wheel, so the helper's own wheel stays off. The pinch's onStart is the
        // hand-over: every single-finger gesture rolls back (never commits) so the pinch owns both fingers.
        // A pinch is never the only way: the zoom buttons beside the canvas are the single-pointer alternative.
        inst.pinch = gesture.attachPinch(inst.svg, {
            pointerTypes: ['touch'],
            wheel: false,
            touchAction: null,
            onStart: function (info) {
                self._cancelSessions(inst, 'pinch', [inst.pinch]);
                const attr = inst.svg.getAttribute('viewBox');
                const rect = inst.svg.getBoundingClientRect();
                // The viewBox rarely has the svg's aspect (preserveAspectRatio=meet letterboxes it): make that explicit
                // first (visually identical), so the fingers and the zoom read-out share ONE scale on both axes.
                const viewBox = inst.gestures.matchAspect(self._getViewBox(inst), rect);
                self._setViewBox(inst, viewBox.x, viewBox.y, viewBox.w, viewBox.h);
                inst.pinchStart = { viewBox: viewBox, attr: attr, rect: rect, center: { x: info.centerX, y: info.centerY } };
            },
            onChange: function (info) { self._pinchApply(inst, info); },
            onEnd: function (info) {
                self._pinchApply(inst, info);
                inst.pinchStart = null;
                inst.userViewChanged = true;
                const vb = self._getViewBox(inst);
                if (self._isCoarse(inst)) self._updateHandles(inst);   // the hit areas follow the zoom
                inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
                inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
                self._flushRelayout(inst);   // a resize that arrived mid-pinch was deferred
            },
            onCancel: function () {
                const start = inst.pinchStart;
                inst.pinchStart = null;
                if (start && start.attr !== null) inst.svg.setAttribute('viewBox', start.attr);
                self._flushRelayout(inst);
            }
        });
        inst.sessions.push(inst.pinch);

        // The handles drawn before the helpers arrived used the fine-pointer sizes.
        if (inst.selectedIds.size > 0) this._updateHandles(inst);
    },

    // ── Long-press cue ────────────────────────────────────────────────────────

    /**
     * While the touch hold timer is pending (press -> 500 ms) the svg carries tm-wd-canvas__svg--holding and a ring
     * (tm-wd-hold-cue) grows at the finger; CSS owns the ~500 ms animation and its reduced-motion variant. It goes away on
     * release, cancel, drift beyond the tolerance and when the menu opens.
     */
    _showHoldCue: function (inst, clientX, clientY) {
        this._hideHoldCue(inst);
        if (!inst.svg) return;
        inst.svg.classList.add('tm-wd-canvas__svg--holding');
        try {
            const pt = this._svgPoint(inst, clientX, clientY);
            const ring = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            const zoom = this._zoomNow(inst) || 1;
            ring.setAttribute('cx', pt.x);
            ring.setAttribute('cy', pt.y);
            ring.setAttribute('r', 24 / zoom);   // 24 screen px at any zoom
            ring.setAttribute('pointer-events', 'none');
            ring.classList.add('tm-wd-hold-cue');
            inst.svg.appendChild(ring);
            inst.holdCue = ring;
        } catch (err) { /* the class alone is the cue */ }
    },

    _hideHoldCue: function (inst) {
        if (inst.svg) inst.svg.classList.remove('tm-wd-canvas__svg--holding');
        if (inst.holdCue) { inst.holdCue.remove(); inst.holdCue = null; }
    },

    /** Cancels every session (except `skip`): the hand-over to the pinch and the read-only flip. */
    _cancelSessions: function (inst, reason, skip) {
        (inst.sessions || []).forEach(function (session) {
            if (!skip || skip.indexOf(session) < 0) session.cancel(reason);
        });
    },

    /** The EDITING sessions a read-only flip must roll back; navigation (pinch, pan tool) keeps running. */
    _cancelEditing: function (inst, reason) {
        [inst.bodyDrag, inst.resizeSession, inst.rotateSession, inst.waypointSession, inst.holdSession]
            .forEach(function (session) { if (session) session.cancel(reason); });
    },

    // ── Pinch ─────────────────────────────────────────────────────────────────

    _pinchApply: function (inst, info) {
        const start = inst.pinchStart;
        if (!start) return;
        const next = inst.gestures.pinchViewBox({
            startViewBox: start.viewBox, rect: start.rect,
            startCenter: start.center, center: { x: info.centerX, y: info.centerY }, scale: info.scale
        });
        this._setViewBox(inst, next.x, next.y, next.w, next.h);
        inst.scale = next.zoom;
    },

    // ── Middle mouse button pan (mouse only) ──────────────────────────────────

    // EXCEPTION to "every single-pointer gesture is a pointer-drag.js session" (docs/touch-interaction.md): the shared
    // helper only starts a drag from the PRIMARY button (button 0), so the mouse-only middle-button pan cannot be a
    // session and keeps this minimal press / move / release. It is mouse-only (touch and pen have no middle button),
    // so it needs no long-press, threshold or pinch hand-over; its cancel paths (Escape, destroy, pointercancel) all
    // run through _cancelMiddlePan / _onMiddleUp below.
    _onMiddleDown: function (e, inst) {
        if (e.button !== 1 || e.pointerType === 'touch') return;
        e.preventDefault();
        inst.middlePan = {
            pointerId: e.pointerId, start: { x: e.clientX, y: e.clientY },
            viewBox: this._getViewBox(inst), attr: inst.svg.getAttribute('viewBox')
        };
        if (inst.svg.setPointerCapture) { try { inst.svg.setPointerCapture(e.pointerId); } catch (err) { /* the pointer is gone */ } }
        inst.svg.addEventListener('pointermove', inst._onMiddleMove);
        inst.svg.addEventListener('pointerup', inst._onMiddleUp);
        inst.svg.addEventListener('pointercancel', inst._onMiddleUp);
        inst.svg.style.cursor = 'grabbing';
    },

    /**
     * Abandons a middle-button pan in progress (Escape, destroy): the viewBox goes back to where the pan started, the
     * capture and the move / up / cancel listeners are released and the cursor is reset. Nothing is sent to .NET.
     */
    _cancelMiddlePan: function (inst) {
        const pan = inst.middlePan;
        if (!pan) return false;
        inst.middlePan = null;
        if (inst.svg) {
            if (pan.attr !== null && pan.attr !== undefined) inst.svg.setAttribute('viewBox', pan.attr);
            if (inst.svg.releasePointerCapture) { try { inst.svg.releasePointerCapture(pan.pointerId); } catch (err) { /* already released */ } }
            inst.svg.removeEventListener('pointermove', inst._onMiddleMove);
            inst.svg.removeEventListener('pointerup', inst._onMiddleUp);
            inst.svg.removeEventListener('pointercancel', inst._onMiddleUp);
            this._updateCursor(inst);
        }
        this._flushRelayout(inst);   // no-op while destroying (inst.destroyed)
        return true;
    },

    _onMiddleMove: function (e, inst) {
        const pan = inst.middlePan;
        if (!pan || e.pointerId !== pan.pointerId) return;
        this._panTo(inst, pan.viewBox, pan.start, e.clientX, e.clientY);
    },

    _onMiddleUp: function (e, inst) {
        const pan = inst.middlePan;
        if (!pan || e.pointerId !== pan.pointerId) return;
        inst.middlePan = null;
        inst.svg.removeEventListener('pointermove', inst._onMiddleMove);
        inst.svg.removeEventListener('pointerup', inst._onMiddleUp);
        inst.svg.removeEventListener('pointercancel', inst._onMiddleUp);
        if (inst.svg.releasePointerCapture) { try { inst.svg.releasePointerCapture(e.pointerId); } catch (err) { /* already released */ } }
        this._updateCursor(inst);
        if (e.type === 'pointercancel') {
            if (pan.attr !== null) inst.svg.setAttribute('viewBox', pan.attr);
            this._flushRelayout(inst);
            return;
        }
        inst.userViewChanged = true;
        const vb = this._getViewBox(inst);
        inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
        this._flushRelayout(inst);
    },

    /**
     * Marks a gesture for CSS (the svg's data-wd-gesture): a side sheet that a PRESS opens reserves its room beside the canvas only
     * after the lift (see _bodyPress). When the sheet is ALREADY open and its room already reserved, the marker must not make CSS drop
     * that reservation - the svg would widen under the finger, the page would shift on screen and the drag would run in a moved
     * coordinate system (Touch_ResizeHandle(1024,768): a 360x240 button became 224x328). So the script records that the reservation was
     * in effect at the press (data-wd-reserved) and the stylesheet keeps the width while that marker is present.
     */
    _markGesture: function (inst) {
        if (!inst.svg) return;
        // Read the reservation FIRST: the moment data-wd-gesture is set the stylesheet may drop it, and a computed-style read after that
        // would see 0 (the first version of this fix did exactly that and changed nothing).
        const reserved = this._sheetReserved(inst);
        inst.svg.setAttribute('data-wd-gesture', '');
        if (reserved) inst.svg.setAttribute('data-wd-reserved', '');
    },

    /** Whether the editor's canvas region currently reserves room for an open inspector sheet (a non-zero inline-end padding). */
    _sheetReserved: function (inst) {
        const region = inst.svg && inst.svg.closest ? inst.svg.closest('.tm-wd-editor__canvas-region') : null;
        if (!region || typeof getComputedStyle !== 'function') return false;
        return parseFloat(getComputedStyle(region).paddingInlineEnd) > 0.5;
    },

    _unmarkGesture: function (inst) {
        if (!inst.svg) return;
        inst.svg.removeAttribute('data-wd-gesture');
        inst.svg.removeAttribute('data-wd-reserved');
    },

    /** Marks a handle gesture (resize / rotate / waypoint) for CSS: see _markGesture. */
    _beginGesture: function (inst) {
        this._markGesture(inst);
    },

    /** Ends a handle gesture: clears the CSS marker and runs a relayout the gesture postponed. */
    _endGesture: function (inst) {
        if (inst.svg && !inst.body) this._unmarkGesture(inst);
        this._flushRelayout(inst);
    },

    /** Moves the viewBox by the pointer travel since `start` (client px -> svg units through the start viewBox). */
    _panTo: function (inst, startViewBox, start, clientX, clientY) {
        const svgRect = inst.svg.getBoundingClientRect();
        const scaleX = startViewBox.w / svgRect.width;
        const scaleY = startViewBox.h / svgRect.height;
        const dx = (clientX - start.x) * scaleX;
        const dy = (clientY - start.y) * scaleY;
        this._setViewBox(inst, startViewBox.x - dx, startViewBox.y - dy, startViewBox.w, startViewBox.h);
    },

    // ── Coordinate helpers ────────────────────────────────────────────────────

    _svgPoint: function (inst, clientX, clientY) {
        const pt  = inst.svg.createSVGPoint();
        pt.x = clientX; pt.y = clientY;
        const ctm = inst.svg.getScreenCTM();
        if (!ctm) return { x: 0, y: 0 };
        const r = pt.matrixTransform(ctm.inverse());
        return { x: r.x, y: r.y };
    },

    _getViewBox: function (inst) {
        const vb = inst.svg.viewBox.baseVal;
        return { x: vb.x, y: vb.y, w: vb.width, h: vb.height };
    },

    _setViewBox: function (inst, x, y, w, h) {
        inst.svg.setAttribute('viewBox', x + ' ' + y + ' ' + w + ' ' + h);
    },

    _getDefaultCursor: function (inst) {
        if (inst.formatPainterActive) return 'copy';
        if (inst.spaceHeld || inst.toolMode === 'pan') return 'grab';
        return '';
    },

    _updateCursor: function (inst) {
        if (!inst.svg) return;
        // Don't override cursors during active drag/resize/pan
        if (inst.isDragging || inst.isResizing || inst.isPanning) return;
        inst.svg.style.cursor = this._getDefaultCursor(inst);
    },

    _snap: function (inst, v) {
        const g = inst.gridSize;
        return g > 0 ? Math.round(v / g) * g : v;
    },

    // ── Element lookup helpers ────────────────────────────────────────────────

    _elGroup: function (inst, id) {
        return inst.svg.querySelector('[data-el-id="' + id + '"]');
    },

    _elRect: function (inst, id) {
        const g = this._elGroup(inst, id);
        if (!g) return null;
        const t = g.getAttribute('transform') || '';
        const m = t.match(/translate\(\s*([-\d.e+]+)\s*,\s*([-\d.e+]+)\s*\)/);
        const x = m ? parseFloat(m[1]) : 0;
        const y = m ? parseFloat(m[2]) : 0;
        const dw = parseFloat(g.getAttribute('data-w') || '0');
        const dh = parseFloat(g.getAttribute('data-h') || '0');
        const rot = parseFloat(g.getAttribute('data-rotation') || '0');
        const locked = g.getAttribute('data-locked') === 'true';
        return { x, y, w: dw, h: dh, rotation: rot, locked: locked };
    },

    getElementBounds: function (svgElement, id) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return null;
        return this._elRect(inst, id);
    },

    _setElTransform: function (inst, id, x, y) {
        const g = this._elGroup(inst, id);
        if (!g) return;
        // A rotated element keeps its rotation while it moves (the move used to drop it until Blazor re-rendered).
        const rot = parseFloat(g.getAttribute('data-rotation') || '0');
        let transform = 'translate(' + x + ', ' + y + ')';
        if (rot) {
            const w = parseFloat(g.getAttribute('data-w') || '0');
            const h = parseFloat(g.getAttribute('data-h') || '0');
            transform += ' rotate(' + rot + ', ' + (w / 2) + ', ' + (h / 2) + ')';
        }
        g.setAttribute('transform', transform);
    },

    // ── Magnetic guidelines (snap to objects) ────────────────────────────────

    _computeMagneticGuides: function (inst, draggedIds, proposed) {
        const tolerance = 6;
        const all = [];
        inst.svg.querySelectorAll('[data-el-id]').forEach(g => {
            const id = g.getAttribute('data-el-id');
            if (draggedIds.includes(id)) return;
            const r = this._elRect(inst, id);
            if (!r) return;
            all.push(r);
        });

        const vEdges = [];
        const hEdges = [];

        for (const id of draggedIds) {
            const p = proposed[id];
            if (!p) continue;
            const dl = p.x;
            const dc = p.x + p.w / 2;
            const dr = p.x + p.w;
            const dt = p.y;
            const dvc = p.y + p.h / 2;
            const db = p.y + p.h;

            for (const r of all) {
                const ol = r.x;
                const oc = r.x + r.w / 2;
                const or = r.x + r.w;
                const ot = r.y;
                const ovc = r.y + r.h / 2;
                const ob = r.y + r.h;

                // Vertical guides (x-alignment)
                const vPairs = [[dl, ol], [dl, oc], [dl, or], [dc, ol], [dc, oc], [dc, or], [dr, ol], [dr, oc], [dr, or]];
                for (const [a, b] of vPairs) {
                    const diff = a - b;
                    if (Math.abs(diff) <= tolerance) {
                        vEdges.push({ diff, pos: b, from: Math.min(dt, ot), to: Math.max(db, ob) });
                    }
                }

                // Horizontal guides (y-alignment)
                const hPairs = [[dt, ot], [dt, ovc], [dt, ob], [dvc, ot], [dvc, ovc], [dvc, ob], [db, ot], [db, ovc], [db, ob]];
                for (const [a, b] of hPairs) {
                    const diff = a - b;
                    if (Math.abs(diff) <= tolerance) {
                        hEdges.push({ diff, pos: b, from: Math.min(dl, ol), to: Math.max(dr, or) });
                    }
                }
            }
        }

        // Pick the best single snap offsets
        let snapDx = 0, snapDy = 0;
        if (vEdges.length > 0) {
            vEdges.sort((a, b) => Math.abs(a.diff) - Math.abs(b.diff));
            snapDx = -vEdges[0].diff;
        }
        if (hEdges.length > 0) {
            hEdges.sort((a, b) => Math.abs(a.diff) - Math.abs(b.diff));
            snapDy = -hEdges[0].diff;
        }

        // Build guide lines (deduplicate positions)
        const vGuides = [];
        const seenV = new Set();
        for (const e of vEdges) {
            if (Math.abs(e.diff + snapDx) <= 0.5 && !seenV.has(e.pos)) {
                seenV.add(e.pos);
                vGuides.push({ x: e.pos, from: e.from, to: e.to });
            }
        }
        const hGuides = [];
        const seenH = new Set();
        for (const e of hEdges) {
            if (Math.abs(e.diff + snapDy) <= 0.5 && !seenH.has(e.pos)) {
                seenH.add(e.pos);
                hGuides.push({ y: e.pos, from: e.from, to: e.to });
            }
        }

        return { vGuides, hGuides, snapDx, snapDy };
    },

    _updateGuides: function (inst, guides) {
        // Remove old guides
        const old = inst.svg.querySelectorAll('.tm-wd-guide');
        old.forEach(el => el.remove());
        if (!guides) return;

        const ns = 'http://www.w3.org/2000/svg';
        const { vGuides, hGuides } = guides;

        for (const g of vGuides) {
            const line = document.createElementNS(ns, 'line');
            line.setAttribute('x1', g.x);
            line.setAttribute('y1', g.from);
            line.setAttribute('x2', g.x);
            line.setAttribute('y2', g.to);
            line.style.stroke = 'var(--tm-wd-guide-color)';
            line.setAttribute('stroke-width', '1');
            line.setAttribute('stroke-dasharray', '4 4');
            line.setAttribute('class', 'tm-wd-guide');
            line.setAttribute('pointer-events', 'none');
            inst.svg.appendChild(line);
        }
        for (const g of hGuides) {
            const line = document.createElementNS(ns, 'line');
            line.setAttribute('x1', g.from);
            line.setAttribute('y1', g.y);
            line.setAttribute('x2', g.to);
            line.setAttribute('y2', g.y);
            line.style.stroke = 'var(--tm-wd-guide-color)';
            line.setAttribute('stroke-width', '1');
            line.setAttribute('stroke-dasharray', '4 4');
            line.setAttribute('class', 'tm-wd-guide');
            line.setAttribute('pointer-events', 'none');
            inst.svg.appendChild(line);
        }
    },

    // ── Element snapshots (verbatim rollback of a resize / rotate / drag) ─────

    _snapshotElement: function (inst, id) {
        const g = this._elGroup(inst, id);
        if (!g) return null;
        const inner = g.querySelector('svg');
        return {
            g: g,
            transform: g.getAttribute('transform'),
            w: g.getAttribute('data-w'),
            h: g.getAttribute('data-h'),
            rotation: g.getAttribute('data-rotation'),
            inner: inner ? {
                el: inner, width: inner.getAttribute('width'), height: inner.getAttribute('height'),
                viewBox: inner.getAttribute('viewBox')
            } : null
        };
    },

    _restoreAttr: function (el, name, value) {
        if (value === null || value === undefined) el.removeAttribute(name);
        else el.setAttribute(name, value);
    },

    _restoreElement: function (snapshot) {
        if (!snapshot) return;
        this._restoreAttr(snapshot.g, 'transform', snapshot.transform);
        this._restoreAttr(snapshot.g, 'data-w', snapshot.w);
        this._restoreAttr(snapshot.g, 'data-h', snapshot.h);
        this._restoreAttr(snapshot.g, 'data-rotation', snapshot.rotation);
        if (snapshot.inner) {
            this._restoreAttr(snapshot.inner.el, 'width', snapshot.inner.width);
            this._restoreAttr(snapshot.inner.el, 'height', snapshot.inner.height);
            this._restoreAttr(snapshot.inner.el, 'viewBox', snapshot.inner.viewBox);
        }
    },

    // ── Resize / rotate (handle gestures) ─────────────────────────────────────

    _resizeMove: function (inst, info) {
        const r = inst.resize;
        const cur  = this._svgPoint(inst, info.x, info.y);
        const dx   = cur.x - r.startSvg.x;
        const dy   = cur.y - r.startSvg.y;
        const orig = r.rect;
        const h    = r.handle;
        const minW = 20, minH = 16;

        let nw = orig.w, nh = orig.h, nx = orig.x, ny = orig.y;

        if (h.includes('e'))  nw = Math.max(minW, orig.w + dx);
        if (h.includes('s'))  nh = Math.max(minH, orig.h + dy);
        if (h.includes('w')) { nw = Math.max(minW, orig.w - dx); nx = orig.x + orig.w - nw; }
        if (h.includes('n')) { nh = Math.max(minH, orig.h - dy); ny = orig.y + orig.h - nh; }

        const snappedX = this._snap(inst, nx);
        const snappedY = this._snap(inst, ny);
        const snappedW = this._snap(inst, nw);
        const snappedH = this._snap(inst, nh);

        const g = this._elGroup(inst, r.id);
        if (g) {
            g.setAttribute('transform', 'translate(' + snappedX + ', ' + snappedY + ')');
            g.setAttribute('data-w', snappedW);
            g.setAttribute('data-h', snappedH);
            // Resize the inner SVG element
            const inner = g.querySelector('svg');
            if (inner) {
                inner.setAttribute('width', snappedW);
                inner.setAttribute('height', snappedH);
                inner.setAttribute('viewBox', '0 0 ' + snappedW + ' ' + snappedH);
            }
            this._updateHandles(inst);
        }
    },

    _rotateMove: function (inst, info) {
        const rot = inst.rotate;
        const cur = this._svgPoint(inst, info.x, info.y);
        const dx = cur.x - rot.center.x;
        const dy = cur.y - rot.center.y;
        const angle = Math.atan2(dy, dx) * 180 / Math.PI;
        const delta = angle - rot.startMouseAngle;
        let newRotation = rot.startAngle + delta;
        // Snap to 15° when Shift is held
        if (info.event && info.event.shiftKey) {
            newRotation = Math.round(newRotation / 15) * 15;
        }
        const g = this._elGroup(inst, rot.id);
        if (g) {
            g.setAttribute('data-rotation', newRotation);
            const t = g.getAttribute('transform') || '';
            const tr = t.match(/translate\(\s*([-\d.e+]+)\s*,\s*([-\d.e+]+)\s*\)/);
            const tx = tr ? parseFloat(tr[1]) : 0;
            const ty = tr ? parseFloat(tr[2]) : 0;
            const r = this._elRect(inst, rot.id);
            const cx = r ? r.w / 2 : 0;
            const cy = r ? r.h / 2 : 0;
            g.setAttribute('transform',
                'translate(' + tx + ', ' + ty + ') rotate(' + newRotation + ', ' + cx + ', ' + cy + ')');
            this._updateHandles(inst);
        }
    },

    // ── Body gesture: select / drag / connector / rubber-band / pan ───────────
    //
    // ONE pointer-drag.js session. The press selects (the old mousedown), the helper's threshold turns it into a
    // drag, and the state machine of the old mouse code (drag, connector drag, connector draw, rubber-band, pan)
    // is kept as `inst.body.kind`. A tap commits nothing; Escape, pointercancel, the second finger and destroy roll
    // the live DOM back verbatim and notify .NET of nothing.

    _bodyPress: function (inst, info) {
        const e = info.event;
        const target = e.target;
        // On a phone the one-finger drag on the EMPTY canvas pans (there is no rubber band to draw): the same pan the pan tool starts.
        const hitsSomething = target && target.closest && (target.closest('[data-el-id]') || target.closest('.tm-wd-connector__hit'));
        const wantPan = inst.spaceHeld || inst.toolMode === 'pan' || (!!inst.lightEditing && !hitsSomething);
        // Read-only keeps navigation (pan tool / Space) but nothing else.
        if (inst.readOnly && !wantPan) return false;
        // Handles and waypoints belong to their own sessions.
        if (this._isChromeTarget(target)) return false;

        this._focusWrap(inst);
        const body = inst.body = {
            kind: 'none', hit: this._hitDescriptor(inst, target, e), pointerType: info.pointerType, shift: !!e.shiftKey
        };
        // CSS reads this: a side sheet that a press-selection opens reserves its room beside the canvas only AFTER the finger
        // is lifted, so the svg never resizes (and the finger's svg coordinates never shift) under a drag that has just begun.
        this._markGesture(inst);

        if (wantPan) {
            body.kind = 'pan';
            // Light editing: a TAP on the empty canvas still clears the selection (decided on release); a DRAG pans.
            body.clearOnTap = !!inst.lightEditing && !hitsSomething && !inst.spaceHeld && inst.toolMode !== 'pan';
            body.viewBox = this._getViewBox(inst);
            body.attr = inst.svg.getAttribute('viewBox');
            body.start = { x: info.startX, y: info.startY };
            return true;
        }

        // Connector hit path?
        const connHit = target && target.closest ? target.closest('.tm-wd-connector__hit') : null;
        if (connHit) {
            const connG = connHit.closest('[data-connector-id]');
            const connId = connG ? connG.getAttribute('data-connector-id') : null;
            if (connId) {
                if (body.shift) {
                    if (inst.selectedIds.has(connId)) {
                        inst.selectedIds.delete(connId);
                    } else {
                        inst.selectedIds.add(connId);
                    }
                } else if (inst.selectedIds.has(connId) && inst.toolMode === 'select' && !inst.lightEditing) {
                    // Start dragging the connector (all waypoints) once the pointer moves
                    body.kind = 'connector';
                    body.connId = connId;
                    body.startSvg = this._svgPoint(inst, info.startX, info.startY);
                    body.currentSvg = body.startSvg;
                    body.circles = Array.from(connG.querySelectorAll('.tm-wd-connector__waypoint, .tm-wd-connector__waypoint-hit'));
                    body.circleStarts = body.circles.map(function (c) {
                        return { cx: c.getAttribute('cx'), cy: c.getAttribute('cy') };
                    });
                } else {
                    inst.selectedIds = new Set([connId]);
                    inst.dotNetRef.invokeMethodAsync('OnConnectorSelectionChanged', [...inst.selectedIds]);
                }
            }
            return true;
        }

        // Element?
        const elG = target && target.closest ? target.closest('[data-el-id]') : null;
        if (elG) {
            // Locked elements cannot be selected, dragged, or resized
            if (elG.getAttribute('data-locked') === 'true') return false;

            const id = elG.getAttribute('data-el-id');
            const type = elG.getAttribute('data-type');

            // Shift+press: toggle selection
            if (body.shift) {
                if (inst.selectedIds.has(id)) {
                    inst.selectedIds.delete(id);
                } else {
                    inst.selectedIds.add(id);
                }
                this._updateHandles(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
                return true;
            }

            // Light editing: tapping an element that is ALREADY selected re-announces the selection on release, so the host can bring the
            // properties sheet back after the user closed it (a changed selection announces itself below).
            body.reselect = !!inst.lightEditing && inst.selectedIds.has(id);

            // If the pressed element is not already selected, replace the selection
            if (!inst.selectedIds.has(id)) {
                inst.selectedIds = new Set([id]);

                // If pressing a group, auto-select its children too
                if (type === '__group__') {
                    inst.svg.querySelectorAll('[data-group-id="' + id + '"]').forEach(child => {
                        const cid = child.getAttribute('data-el-id');
                        if (cid) inst.selectedIds.add(cid);
                    });
                }

                this._updateHandles(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
            }

            body.startSvg = this._svgPoint(inst, info.startX, info.startY);
            // Connector tool mode: the drag draws a connector
            if (inst.toolMode === 'connector' && !inst.lightEditing) {
                body.kind = 'draw-connector';
                body.fromId = id;
            } else {
                body.kind = 'element';
                body.id = id;
            }
            return true;
        }

        // Empty canvas: rubber-band (space / middle button / pan tool pan elsewhere)
        body.kind = 'rubber';
        body.startSvg = this._svgPoint(inst, info.startX, info.startY);
        // Clear selection (no shift)
        if (!body.shift && inst.selectedIds.size > 0) {
            inst.selectedIds.clear();
            this._updateHandles(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
        }
        return true;
    },

    _bodyStart: function (inst, info) {
        const body = inst.body;
        if (!body) return false;

        switch (body.kind) {
            case 'pan':
                inst.isPanning = true;
                inst.svg.style.cursor = 'grabbing';
                return undefined;

            case 'element': {
                inst.isDragging = true;
                inst.dragElIds = [...inst.selectedIds];
                // If any selected element is a group, add its children to the drag
                inst.dragElIds.forEach(eid => {
                    const gel = this._elGroup(inst, eid);
                    if (gel && gel.getAttribute('data-type') === '__group__') {
                        inst.svg.querySelectorAll('[data-group-id="' + eid + '"]').forEach(child => {
                            const cid = child.getAttribute('data-el-id');
                            if (cid && !inst.dragElIds.includes(cid)) {
                                inst.dragElIds.push(cid);
                            }
                        });
                    }
                });
                inst.dragStartSvg = body.startSvg;
                inst.dragStartPositions = {};
                body.snapshots = {};
                inst.dragElIds.forEach(eid => {
                    const r = this._elRect(inst, eid);
                    if (r) inst.dragStartPositions[eid] = { x: r.x, y: r.y };
                    const g = this._elGroup(inst, eid);
                    if (g) body.snapshots[eid] = g.getAttribute('transform');
                });
                // Notify Blazor of drag start so it can snapshot positions for MoveElementsCommand
                inst.dotNetRef.invokeMethodAsync('OnDragStarted', inst.dragElIds);
                inst.svg.style.cursor = 'grabbing';
                return undefined;
            }

            case 'connector':
                inst.isDraggingConnector = true;
                return undefined;

            case 'draw-connector':
                inst.isDrawingConnector = true;
                inst.connectorFromId = body.fromId;
                inst.connectorStartSvg = body.startSvg;
                inst.connectorPreview = this._createConnectorPreview(inst, body.startSvg.x, body.startSvg.y);
                return undefined;

            case 'rubber':
                inst.isRubberBand = true;
                inst.rubberStart = body.startSvg;
                inst.rubberRect = this._createRubberRect(inst, body.startSvg.x, body.startSvg.y);
                return undefined;

            default:
                return false;   // a tap on a connector / a toggled selection never becomes a drag
        }
    },

    _bodyMove: function (inst, info) {
        const body = inst.body;
        if (!body) return;

        if (body.kind === 'pan') {
            this._panTo(inst, body.viewBox, body.start, info.x, info.y);
            return;
        }

        const cur = this._svgPoint(inst, info.x, info.y);

        if (body.kind === 'element' && inst.dragElIds.length > 0) {
            const dx = cur.x - inst.dragStartSvg.x;
            const dy = cur.y - inst.dragStartSvg.y;

            // Build proposed positions (after grid snap)
            const proposed = {};
            inst.dragElIds.forEach(id => {
                const start = inst.dragStartPositions[id];
                if (!start) return;
                const nx = this._snap(inst, start.x + dx);
                const ny = this._snap(inst, start.y + dy);
                const r = this._elRect(inst, id);
                proposed[id] = { x: nx, y: ny, w: r ? r.w : 0, h: r ? r.h : 0 };
            });

            // Magnetic snap to objects
            let guides = null;
            if (inst.config.snapToObjects) {
                guides = this._computeMagneticGuides(inst, inst.dragElIds, proposed);
                if (guides) {
                    inst.dragElIds.forEach(id => {
                        if (proposed[id]) {
                            proposed[id].x += guides.snapDx;
                            proposed[id].y += guides.snapDy;
                        }
                    });
                }
            }

            inst.dragElIds.forEach(id => {
                const p = proposed[id];
                if (!p) return;
                this._setElTransform(inst, id, p.x, p.y);
            });

            this._updateGuides(inst, guides);
            this._updateHandles(inst);
            return;
        }

        if (body.kind === 'rubber' && inst.rubberRect) {
            const rx = Math.min(cur.x, inst.rubberStart.x);
            const ry = Math.min(cur.y, inst.rubberStart.y);
            const rw = Math.abs(cur.x - inst.rubberStart.x);
            const rh = Math.abs(cur.y - inst.rubberStart.y);
            inst.rubberRect.setAttribute('x', rx);
            inst.rubberRect.setAttribute('y', ry);
            inst.rubberRect.setAttribute('width', rw);
            inst.rubberRect.setAttribute('height', rh);
            return;
        }

        if (body.kind === 'connector' && body.connId) {
            const dx = cur.x - body.currentSvg.x;
            const dy = cur.y - body.currentSvg.y;
            body.currentSvg = cur;
            // Move all waypoint handles
            body.circles.forEach(circle => {
                circle.setAttribute('cx', parseFloat(circle.getAttribute('cx') || '0') + dx);
                circle.setAttribute('cy', parseFloat(circle.getAttribute('cy') || '0') + dy);
            });
            return;
        }

        if (body.kind === 'draw-connector' && inst.connectorPreview) {
            const d = 'M ' + inst.connectorStartSvg.x + ' ' + inst.connectorStartSvg.y + ' L ' + cur.x + ' ' + cur.y;
            inst.connectorPreview.setAttribute('d', d);
        }
    },

    _bodyEnd: function (inst, info) {
        const body = inst.body;
        if (!body) return;

        if (body.kind === 'pan') {
            inst.userViewChanged = true;
            const vb = this._getViewBox(inst);
            inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
            return;
        }

        if (body.kind === 'element') {
            const moves = inst.dragElIds.map(id => {
                const r = this._elRect(inst, id);
                return r ? { id, x: r.x, y: r.y } : null;
            }).filter(Boolean);

            if (moves.length === 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementMoved', moves[0].id, moves[0].x, moves[0].y);
            } else if (moves.length > 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementsMoved', moves);
            }
            return;
        }

        if (body.kind === 'rubber') {
            const rubber = inst.rubberRect;
            if (!rubber) return;
            const rx = parseFloat(rubber.getAttribute('x') || '0');
            const ry = parseFloat(rubber.getAttribute('y') || '0');
            const rw = parseFloat(rubber.getAttribute('width') || '0');
            const rh = parseFloat(rubber.getAttribute('height') || '0');

            if (rw > 4 && rh > 4) {
                // Collect all elements inside the rubber band
                const hits = [];
                inst.svg.querySelectorAll('[data-el-id]').forEach(g => {
                    const r = this._elRect(inst, g.getAttribute('data-el-id'));
                    if (!r) return;
                    if (r.x >= rx && r.y >= ry && r.x + r.w <= rx + rw && r.y + r.h <= ry + rh) {
                        hits.push(g.getAttribute('data-el-id'));
                    }
                });
                if (hits.length > 0) {
                    // If a group is selected via rubber-band, auto-select its children
                    hits.forEach(id => {
                        const gel = this._elGroup(inst, id);
                        if (gel && gel.getAttribute('data-type') === '__group__') {
                            inst.svg.querySelectorAll('[data-group-id="' + id + '"]').forEach(child => {
                                const cid = child.getAttribute('data-el-id');
                                if (cid) hits.push(cid);
                            });
                        }
                    });
                    inst.selectedIds = new Set(hits);
                    this._updateHandles(inst);
                    inst.dotNetRef.invokeMethodAsync('OnMultiSelect', hits);
                }
            }
            return;
        }

        if (body.kind === 'connector') {
            const dx = body.currentSvg.x - body.startSvg.x;
            const dy = body.currentSvg.y - body.startSvg.y;
            if (Math.abs(dx) > 0.01 || Math.abs(dy) > 0.01) {
                inst.dotNetRef.invokeMethodAsync('OnConnectorDragged', body.connId, dx, dy);
            }
            return;
        }

        if (body.kind === 'draw-connector') {
            // The pointer is captured by the svg, so the target under it is hit-tested explicitly.
            const under = document.elementFromPoint(info.x, info.y);
            const targetEl = under && under.closest ? under.closest('[data-el-id]') : null;
            const toId = targetEl ? targetEl.getAttribute('data-el-id') : null;
            if (body.fromId && toId && body.fromId !== toId) {
                inst.dotNetRef.invokeMethodAsync('OnConnectorCreated', body.fromId, toId);
            }
        }
    },

    _bodyCancel: function (inst, info) {
        const body = inst.body;
        if (!body) return;

        if (body.kind === 'pan') {
            if (body.attr !== null && body.attr !== undefined) inst.svg.setAttribute('viewBox', body.attr);
        } else if (body.kind === 'element' && body.snapshots) {
            Object.keys(body.snapshots).forEach(id => {
                const g = this._elGroup(inst, id);
                if (g) this._restoreAttr(g, 'transform', body.snapshots[id]);
            });
            this._updateHandles(inst);
            this._announceCancelled(inst, info);
            // .NET snapshotted the start positions at OnDragStarted: a cancelled drag must not leave them behind
            // for a later keyboard nudge (an empty snapshot makes the next move read the document).
            // (Not after destroy: the .NET reference is gone and the call would reject.)
            this._invokeQuiet(inst, 'OnDragStarted', []);
        } else if (body.kind === 'connector' && body.circles) {
            body.circles.forEach((circle, i) => {
                circle.setAttribute('cx', body.circleStarts[i].cx);
                circle.setAttribute('cy', body.circleStarts[i].cy);
            });
            this._announceCancelled(inst, info);
        }
    },

    /**
     * Tells .NET (a polite live region) that a move / resize / rotate / waypoint gesture was rolled back by the user
     * (Escape, pointercancel, the second finger / pinch). NOT for a normal end, and not for the programmatic roll-backs
     * (destroy, the read-only flip, the long-press hand-over): nothing the user did wrong happened there.
     */
    _announceCancelled: function (inst, info) {
        if (inst.destroyed) return;
        const reason = info && info.reason;
        if (reason === 'detached' || reason === 'read-only' || reason === 'long-press-menu') return;
        this._invokeQuiet(inst, 'OnGestureCancelled');
    },

    /** A fire-and-forget call into .NET that is skipped after destroy and never surfaces a rejection. */
    _invokeQuiet: function (inst, method) {
        if (inst.destroyed || !inst.dotNetRef) return;
        try {
            const result = inst.dotNetRef.invokeMethodAsync.apply(inst.dotNetRef, [method].concat(Array.prototype.slice.call(arguments, 2)));
            if (result && typeof result.catch === 'function') result.catch(function () { /* the component is gone */ });
        } catch (err) { /* disposed reference */ }
    },

    /** Runs on EVERY exit path (tap, end, cancel, veto): the live overlays and the flags never outlive the gesture. */
    _bodyRelease: function (inst, info) {
        const body = inst.body;
        inst.body = null;
        this._unmarkGesture(inst);

        inst.isDragging = false;
        inst.dragElIds = [];
        inst.dragStartSvg = null;
        inst.dragStartPositions = {};
        inst.isRubberBand = false;
        inst.rubberStart = null;
        if (inst.rubberRect) { inst.rubberRect.remove(); inst.rubberRect = null; }
        inst.isDrawingConnector = false;
        inst.connectorFromId = null;
        inst.connectorStartSvg = null;
        if (inst.connectorPreview) { inst.connectorPreview.remove(); inst.connectorPreview = null; }
        inst.isDraggingConnector = false;
        inst.isPanning = false;
        this._updateGuides(inst, null);
        this._updateCursor(inst);
        this._flushRelayout(inst);

        if (body && body.reselect && info.outcome === 'tap' && !inst.readOnly && inst.selectedIds.size > 0) {
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
        }

        if (body && body.clearOnTap && info.outcome === 'tap' && !inst.readOnly && inst.selectedIds.size > 0) {
            inst.selectedIds.clear();
            this._updateHandles(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
        }

        if (!body || !inst.doubleTap) return;
        // A mouse has a real dblclick; a finger or a pen double-tap is turned into the same action.
        if (info.outcome !== 'tap') { inst.doubleTap.reset(); return; }
        if (body.pointerType === 'mouse') return;
        const time = info.event && typeof info.event.timeStamp === 'number' ? info.event.timeStamp : Date.now();
        if (inst.doubleTap.tap({ x: info.x, y: info.y, time: time })) {
            inst.lastDoubleTapAt = Date.now();
            this._activateAt(inst, body.hit, info.x, info.y);
        }
    },

    // ── SVG pointer move (ruler cursor) ───────────────────────────────────────

    _onSvgPointerMove: function (e, inst) {
        // A finger has no hover position: only a mouse or a pen reports the ruler cursor.
        if (e.pointerType === 'touch') return;
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
        inst.dotNetRef.invokeMethodAsync('OnCanvasMouseMoved', svgPt.x, svgPt.y);
    },

    // ── Double click / double tap ─────────────────────────────────────────────

    _onDblClick: function (e, inst) {
        if (inst.readOnly) return;
        // The double-tap of a finger was already handled (iOS fires no dblclick, Chrome does).
        if (Date.now() - (inst.lastDoubleTapAt || 0) < 700) return;
        if (this._activateAt(inst, this._hitDescriptor(inst, e.target), e.clientX, e.clientY)) {
            e.preventDefault();
            e.stopPropagation();
        }
    },

    /** The double-click action: a double-click / double-tap on a connector path adds a waypoint. */
    _activateAt: function (inst, hit, clientX, clientY) {
        if (inst.readOnly || !hit || hit.kind !== 'connector') return false;
        const connId = hit.id;
        const connG = inst.svg.querySelector('[data-connector-id="' + connId + '"]');
        if (!connG) return false;

        const svgPt = this._svgPoint(inst, clientX, clientY);
        const path = connG.querySelector('.tm-wd-connector__path');
        if (!path) return true;

        const closest = this._closestPointOnPath(path, svgPt.x, svgPt.y);
        inst.dotNetRef.invokeMethodAsync('OnConnectorWaypointAdded', connId, closest.x, closest.y);
        return true;
    },

    // ── Closest point on SVG path helper ─────────────────────────────────────

    _closestPointOnPath: function (pathEl, x, y) {
        const len = pathEl.getTotalLength();
        if (len === 0) return { x: x, y: y };

        const step = Math.max(len / 100, 1);
        let bestDist = Infinity;
        let bestPt = { x: x, y: y };
        let bestAt = 0;

        for (let i = 0; i <= len; i += step) {
            const pt = pathEl.getPointAtLength(i);
            const dist = Math.hypot(pt.x - x, pt.y - y);
            if (dist < bestDist) {
                bestDist = dist;
                bestPt = { x: pt.x, y: pt.y };
                bestAt = i;
            }
        }

        // Fine-tune around best point
        const start = Math.max(0, bestAt - step);
        const end = Math.min(len, bestAt + step);
        for (let i = start; i <= end; i += 0.5) {
            const pt = pathEl.getPointAtLength(i);
            const dist = Math.hypot(pt.x - x, pt.y - y);
            if (dist < bestDist) {
                bestDist = dist;
                bestPt = { x: pt.x, y: pt.y };
            }
        }

        return bestPt;
    },

    // ── Wheel zoom ───────────────────────────────────────────────────────────

    _onWheel: function (e, inst) {
        e.preventDefault();
        // ctrl+wheel is a trackpad pinch (many small deltas): scale continuously; a plain wheel steps 10 %.
        const factor = e.ctrlKey ? Math.exp(e.deltaY / 100) : (e.deltaY > 0 ? 1.1 : 0.9);
        const vb = this._getViewBox(inst);
        const pt = this._svgPoint(inst, e.clientX, e.clientY);

        const newW = vb.w * factor;
        const newH = vb.h * factor;

        // Clamp 0.15x – 5x
        const svgRect = inst.svg.getBoundingClientRect();
        const newScale = svgRect.width / newW;
        if (newScale < 0.15 || newScale > 5.0) return;

        const newX = pt.x - (pt.x - vb.x) * factor;
        const newY = pt.y - (pt.y - vb.y) * factor;

        this._setViewBox(inst, newX, newY, newW, newH);
        inst.scale = newScale;
        inst.userViewChanged = true;
        this._refreshHandlesIfCoarse(inst);
        inst.dotNetRef.invokeMethodAsync('OnZoomChanged', newScale);
    },

    // ── Context menu ─────────────────────────────────────────────────────────

    _onContextMenu: function (e, inst) {
        e.preventDefault();
        // The native contextmenu of a touch long-press (Android / Windows fire it, iOS does not) must not open a
        // second menu: the shared long-press session (holdSession) already owns it. The mouse and a pen keep the
        // native right-click / press-and-hold.
        // Pointer capture retargets that contextmenu to the svg itself (the body gesture captured the finger), so by the time it
        // arrives it can no longer tell an element from the canvas: while a touch is pressed (holdHit) or just opened a menu,
        // the hold session is the ONLY opener. A mouse has no holdHit and keeps the right-click.
        // The holdHit / lastHoldMenuAt fallback is the GUARANTEE; e.pointerType alone is not reliable (it can be empty or
        // retargeted on a capture-redirected contextmenu), so it is only an extra early-out.
        if (inst.holdSession && (e.pointerType === 'touch' || inst.holdHit || Date.now() - (inst.lastHoldMenuAt || 0) < 1000)) return;
        this._openContextMenuAt(inst, this._hitDescriptor(inst, e.target), e.clientX, e.clientY, 0);
    },

    /**
     * Asks .NET to open the context menu for `hit` (a connector, an element or the empty canvas, see _hitDescriptor).
     * `offset` parks the menu beside a finger instead of under it, so the lifting finger never lands on an item.
     */
    _openContextMenuAt: function (inst, hit, clientX, clientY, offset) {
        const x = clientX + offset;
        const y = clientY + offset;

        if (hit && hit.kind === 'connector') {
            inst.dotNetRef.invokeMethodAsync('OnConnectorContextMenu', hit.id, x, y);
        } else if (hit && hit.kind === 'element') {
            inst.dotNetRef.invokeMethodAsync('OnElementContextMenu', hit.id, x, y);
        } else {
            const svgPt = this._svgPoint(inst, clientX, clientY);
            inst.dotNetRef.invokeMethodAsync('OnCanvasContextMenu', svgPt.x, svgPt.y, x, y);
        }
    },

    /** The items arrow keys can reach: a disabled button is skipped (it cannot take focus anyway). */
    _menuItems: function (menuEl) {
        return Array.from(menuEl.querySelectorAll('[role="menuitem"]')).filter(function (item) {
            return !item.disabled && item.getAttribute('aria-disabled') !== 'true';
        });
    },

    /** Roving focus inside the open menu: Arrow Up / Down (wrapping), Home / End; Tab closes it like Escape. */
    _onMenuKeyDown: function (e, inst, menuEl) {
        const items = this._menuItems(menuEl);
        if (e.key === 'Tab') {
            e.preventDefault();
            this._requestMenuClose(inst, true);
            return;
        }
        if (items.length === 0) return;
        const current = items.indexOf(document.activeElement);
        let next = -1;
        if (e.key === 'ArrowDown') next = current < 0 ? 0 : (current + 1) % items.length;
        else if (e.key === 'ArrowUp') next = current < 0 ? items.length - 1 : (current - 1 + items.length) % items.length;
        else if (e.key === 'Home') next = 0;
        else if (e.key === 'End') next = items.length - 1;
        if (next < 0) return;
        e.preventDefault();
        e.stopPropagation();
        items[next].focus();
    },

    /**
     * Asks .NET to close the menu (the editor then calls closeContextMenu). `restoreFocus` false (a pointer press elsewhere)
     * keeps the focus where that press puts it; Escape, Tab and choosing an item return it to the canvas.
     */
    _requestMenuClose: function (inst, restoreFocus) {
        inst._menuRestoreFocus = restoreFocus;
        if (inst.dotNetRef) this._invokeQuiet(inst, 'CloseContextMenu');
        this._teardownMenu(inst);
    },

    _teardownMenu: function (inst) {
        if (inst._menuDismissHandler) document.removeEventListener('pointerdown', inst._menuDismissHandler, true);
        if (inst._menuKeyHandler) document.removeEventListener('keydown', inst._menuKeyHandler);
        if (inst._menuEl && inst._menuNav) inst._menuEl.removeEventListener('keydown', inst._menuNav);
        inst._menuDismissHandler = null;
        inst._menuKeyHandler = null;
        inst._menuNav = null;
    },

    /**
     * Opens the context menu for the keyboard: at the first selected element (or connector) - the centre of its on-screen
     * box - or, with nothing selected, at the middle of the canvas as the canvas menu. Goes through the same
     * _openContextMenuAt as the right-click and the long-press.
     */
    _openContextMenuFromKeyboard: function (inst) {
        const first = [...inst.selectedIds][0];
        let hit = { kind: 'canvas' };
        let box = null;
        if (first) {
            const elG = this._elGroup(inst, first);
            const connG = elG ? null : inst.svg.querySelector('[data-connector-id="' + first + '"]');
            if (elG) { hit = { kind: 'element', id: first }; box = elG.getBoundingClientRect(); }
            else if (connG) { hit = { kind: 'connector', id: first }; box = connG.getBoundingClientRect(); }
        }
        if (!box) box = inst.svg.getBoundingClientRect();
        inst.lastKeyMenuAt = Date.now();
        const x = box.left + box.width / 2;
        const y = box.top + box.height / 2;
        this._openContextMenuAt(inst, hit, x, y, 0);
    },

    openContextMenu: function (container, menuEl) {
        if (!container || !menuEl) return;
        const inst = this.instances.get(container.id);
        if (!inst) return;
        const self = this;
        this._teardownMenu(inst);
        inst._menuEl = menuEl;
        inst._menuRestoreFocus = undefined;
        setTimeout(function () {
            if (!menuEl || inst.destroyed || !inst.svg) return;
            const rect = menuEl.getBoundingClientRect();
            const vw = window.innerWidth;
            const vh = window.innerHeight;
            const margin = 8;
            let left = parseFloat(menuEl.style.left) || 0;
            let top = parseFloat(menuEl.style.top) || 0;
            if (left + rect.width + margin > vw) {
                left = Math.max(margin, vw - rect.width - margin);
            }
            if (top + rect.height + margin > vh) {
                top = Math.max(margin, vh - rect.height - margin);
            }
            menuEl.style.left = left + 'px';
            menuEl.style.top = top + 'px';

            inst._menuDismissHandler = function (e) {
                if (e.button !== 0) return;
                if (e.target.closest('.tm-wd-editor__context-menu')) return;
                self._requestMenuClose(inst, false);
            };
            inst._menuKeyHandler = function (e) {
                if (e.key === 'Escape') self._requestMenuClose(inst, true);
            };
            inst._menuNav = function (e) { self._onMenuKeyDown(e, inst, menuEl); };
            document.addEventListener('pointerdown', inst._menuDismissHandler, true);
            document.addEventListener('keydown', inst._menuKeyHandler);
            menuEl.addEventListener('keydown', inst._menuNav);

            // Keyboard / screen-reader users land on the first enabled item (WAI-ARIA menu pattern).
            const items = self._menuItems(menuEl);
            if (items.length > 0) items[0].focus();
            else if (menuEl.focus) menuEl.focus();
        }, 0);
    },

    closeContextMenu: function (container) {
        if (!container) return;
        const inst = this.instances.get(container.id);
        if (!inst) return;
        const menuEl = inst._menuEl;
        // Focus is only given back when it was inside the menu (Escape, Tab, a chosen item), never after a press elsewhere.
        // (Safari does not focus a clicked button: the focus then sits on the body, which counts as the menu's too.)
        const active = document.activeElement;
        const hadFocus = !!(menuEl && menuEl.contains && (!active || active === document.body || menuEl.contains(active)));
        this._teardownMenu(inst);
        inst._menuEl = null;
        const restore = hadFocus && inst._menuRestoreFocus !== false;
        inst._menuRestoreFocus = undefined;
        if (restore && !inst.destroyed && inst.svg) this._focusWrap(inst);
    },
    // ── Toolbox drop ─────────────────────────────────────────────────────────

    _onDragOver: function (e, inst) {
        if (inst.readOnly) return;
        if (e.dataTransfer && e.dataTransfer.types.includes('text/plain')) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
        }
    },

    _onDrop: function (e, inst) {
        if (inst.readOnly) return;
        e.preventDefault();
        const componentType = e.dataTransfer && e.dataTransfer.getData('text/plain');
        if (!componentType) return;
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
        const x = this._snap(inst, svgPt.x);
        const y = this._snap(inst, svgPt.y);
        inst.dotNetRef.invokeMethodAsync('OnElementDropped', componentType, x, y);
    },

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    _onKeyDown: function (e, inst) {
        // Only handle if SVG or a child has focus (or SVG is inside an active editor)
        if (!inst.svg.closest(':focus-within') && document.activeElement !== document.body) return;
        // The layers and pages panels own every key typed inside them (radio roaming, Alt+Arrow reorder, F2,
        // Delete/Escape on a control), and so do the tablet overlay panels of the shell: none of them may also act on the canvas selection (nudge, delete, clear).
        if (e.target.closest?.('.tm-wd-layers, .tm-wd-pages, .tm-editor-shell__overlay')) return;

        const ids = [...inst.selectedIds];

        // Shift+F10 and the ContextMenu key open the menu for the selection (the keyboard twin of the right-click / the
        // touch long-press); typing in an input keeps its own native menu key.
        if ((e.key === 'F10' && e.shiftKey) || e.key === 'ContextMenu') {
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;
            e.preventDefault();
            this._openContextMenuFromKeyboard(inst);
            return;
        }

        if (e.key === 'Delete' || e.key === 'Backspace') {
            // Don't intercept if user is typing in an input
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
            e.preventDefault();
            if (ids.length > 0) {
                inst.selectedIds.clear();
                this._updateHandles(inst);
                inst.dotNetRef.invokeMethodAsync('OnDeleteSelected', ids);
            }
            // Also check for selected connectors
            const connIds = [...inst.selectedIds].filter(id => {
                return inst.svg.querySelector('[data-connector-id="' + id + '"]') !== null;
            });
            if (connIds.length > 0) {
                inst.selectedIds.clear();
                inst.dotNetRef.invokeMethodAsync('OnDeleteConnectors', connIds);
            }
            return;
        }

        if (e.key === 'Escape') {
            // Escape first abandons a middle-button pan in progress (the pointer-drag sessions cancel themselves).
            this._cancelMiddlePan(inst);
            e.preventDefault();
            inst.selectedIds.clear();
            this._updateHandles(inst);
            inst.dotNetRef.invokeMethodAsync('OnClearSelection');
            return;
        }

        if (e.ctrlKey || e.metaKey) {
            switch (e.key.toLowerCase()) {
                case 'z':
                    e.preventDefault();
                    if (e.shiftKey) {
                        inst.dotNetRef.invokeMethodAsync('OnRedo');
                    } else {
                        inst.dotNetRef.invokeMethodAsync('OnUndo');
                    }
                    return;
                case 'y':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnRedo');
                    return;
                case 'a':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnSelectAll');
                    return;
                case 'd':
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnDuplicate', ids);
                    return;
                case 'g':
                    e.preventDefault();
                    if (ids.length > 1) inst.dotNetRef.invokeMethodAsync('OnGroup', ids);
                    return;
            }
        }

        // Ctrl+Shift shortcuts
        if ((e.ctrlKey || e.metaKey) && e.shiftKey) {
            switch (e.key.toLowerCase()) {
                case ']':
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnBringToFront', ids);
                    return;
                case '[':
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnSendToBack', ids);
                    return;
                case 'l':
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnToggleLock', ids);
                    return;
                case 'g':
                    e.preventDefault();
                    // Ungroup: find selected groups
                    const groupIds = ids.filter(id => {
                        const g = this._elGroup(inst, id);
                        return g && g.getAttribute('data-type') === '__group__';
                    });
                    if (groupIds.length > 0) inst.dotNetRef.invokeMethodAsync('OnUngroup', groupIds);
                    return;
                case 'c':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnCopyStyle');
                    return;
                case 'v':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnPasteStyle');
                    return;
                case 's':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnPasteSize');
                    return;
            }
        }

        // Space → temporary pan mode
        if (e.code === 'Space' && !e.target.matches('input,textarea,select')) {
            e.preventDefault();
            if (!inst.spaceHeld) {
                inst.spaceHeld = true;
                if (!inst.isPanning) this._updateCursor(inst);
            }
            return;
        }

        // H → switch to pan tool, V → switch to select tool
        if (!e.ctrlKey && !e.metaKey && !e.target.matches('input,textarea,select')) {
            if (e.code === 'KeyH') {
                e.preventDefault();
                inst.toolMode = 'pan';
                this._updateCursor(inst);
                inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'pan');
                return;
            }
            if (e.code === 'KeyV') {
                e.preventDefault();
                inst.toolMode = 'select';
                this._updateCursor(inst);
                inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'select');
                return;
            }
        }

        // Arrow key nudge
        if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key)) {
            if (ids.length === 0) return;
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
            e.preventDefault();
            // The non-drag alternative to moving an element (WCAG 2.5.7): 1 unit, Shift = one grid cell.
            if (inst.readOnly) return;
            const step = inst.gestures
                ? inst.gestures.nudgeStep({ shift: e.shiftKey, gridSize: inst.gridSize })
                : (e.shiftKey ? (inst.gridSize > 0 ? inst.gridSize : 10) : 1);
            const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
            const dy = e.key === 'ArrowUp'   ? -step : e.key === 'ArrowDown'  ? step : 0;
            const movable = ids.filter(id => { const r = this._elRect(inst, id); return r && !r.locked; });
            if (movable.length === 0) return;
            movable.forEach(id => {
                const r = this._elRect(inst, id);
                this._setElTransform(inst, id, r.x + dx, r.y + dy);
            });
            this._updateHandles(inst);
            // Notify Blazor of final positions
            const moves = movable.map(id => {
                const r = this._elRect(inst, id);
                return r ? { id, x: r.x, y: r.y } : null;
            }).filter(Boolean);
            if (moves.length === 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementMoved', moves[0].id, moves[0].x, moves[0].y);
            } else if (moves.length > 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementsMoved', moves);
            }
        }
    },

    // ── Key up ───────────────────────────────────────────────────────────────

    _onKeyUp: function (e, inst) {
        if (e.code === 'Space') {
            inst.spaceHeld = false;
            if (!inst.isPanning) {
                this._updateCursor(inst);
            }
        }
    },

    // ── Selection handles ─────────────────────────────────────────────────────

    _updateHandles: function (inst) {
        // Remove all existing handle groups
        inst.svg.querySelectorAll('.tm-wd-selection').forEach(el => el.remove());
        if (inst.readOnly) return;
        // Light editing keeps the dashed selection outline (what is selected) but none of the grips: resize / rotate are tablet+.
        const showGrips = !inst.lightEditing;

        const ns = 'http://www.w3.org/2000/svg';

        inst.selectedIds.forEach(id => {
            const r = this._elRect(inst, id);
            if (!r) return;

            const selG = document.createElementNS(ns, 'g');
            selG.classList.add('tm-wd-selection');
            selG.setAttribute('data-sel-for', id);
            selG.setAttribute('pointer-events', 'none');

            // Selection border
            const border = document.createElementNS(ns, 'rect');
            border.setAttribute('x',      r.x - 1);
            border.setAttribute('y',      r.y - 1);
            border.setAttribute('width',  r.w + 2);
            border.setAttribute('height', r.h + 2);
            border.setAttribute('fill',   'none');
            border.style.stroke = 'var(--tm-wd-accent)';
            border.setAttribute('stroke-width', '1.5');
            border.setAttribute('stroke-dasharray', '4 2');
            border.setAttribute('rx', '2');
            selG.appendChild(border);

            // 8 resize handles – only if single selection and not locked
            if (showGrips && inst.selectedIds.size === 1 && !r.locked) {
                // Handle sizes: x1.5 on a coarse pointer, plus an invisible hit area of >= 24 screen px at this zoom
                // (capped so the handles of a small element do not overlap each other).
                const coarse = this._isCoarse(inst);
                const sizes = inst.gestures
                    ? inst.gestures.handleSizes({
                        coarse: coarse, zoom: this._zoomNow(inst),
                        // A hit rect never covers the body of a small element: the finger must still be able to drag it.
                        maxHit: Math.min(r.w, r.h) / 2, maxVisual: Math.max(12, Math.min(r.w, r.h) / 3)
                    })
                    : { visual: 8, hit: 8 };
                const size = { visual: sizes.visual };
                const hitSize = sizes.hit;
                const rotRadius = sizes.visual * 5 / 8;     // 5 for the 8-unit fine handle
                const rotOffset = 7 + rotRadius;            // 12 for the fine handle
                const handles = [
                    { key: 'nw', cx: r.x,           cy: r.y,           cursor: 'nw-resize' },
                    { key: 'n',  cx: r.x + r.w / 2, cy: r.y,           cursor: 'n-resize'  },
                    { key: 'ne', cx: r.x + r.w,     cy: r.y,           cursor: 'ne-resize' },
                    { key: 'e',  cx: r.x + r.w,     cy: r.y + r.h / 2, cursor: 'e-resize'  },
                    { key: 'se', cx: r.x + r.w,     cy: r.y + r.h,     cursor: 'se-resize' },
                    { key: 's',  cx: r.x + r.w / 2, cy: r.y + r.h,     cursor: 's-resize'  },
                    { key: 'sw', cx: r.x,           cy: r.y + r.h,     cursor: 'sw-resize' },
                    { key: 'w',  cx: r.x,           cy: r.y + r.h / 2, cursor: 'w-resize'  },
                ];

                handles.forEach(h => {
                    const dot = document.createElementNS(ns, 'rect');
                    dot.setAttribute('x',      h.cx - size.visual / 2);
                    dot.setAttribute('y',      h.cy - size.visual / 2);
                    dot.setAttribute('width',  size.visual);
                    dot.setAttribute('height', size.visual);
                    dot.setAttribute('rx',     2);
                    dot.style.fill = 'var(--tm-wd-handle-fill)';
                    dot.style.stroke = 'var(--tm-wd-accent)';
                    dot.setAttribute('stroke-width', '1.5');
                    dot.setAttribute('data-handle', h.key);
                    dot.setAttribute('pointer-events', 'all');
                    dot.style.cursor = h.cursor;
                    selG.appendChild(dot);
                });

                // Coarse pointer: an invisible, larger hit rect per handle (same handle key, so the resize gesture
                // reads it exactly like the dot). Painted AFTER the dots so a finger lands on it first.
                if (coarse) {
                    handles.forEach(h => {
                        const hit = document.createElementNS(ns, 'rect');
                        hit.setAttribute('x',      h.cx - hitSize / 2);
                        hit.setAttribute('y',      h.cy - hitSize / 2);
                        hit.setAttribute('width',  hitSize);
                        hit.setAttribute('height', hitSize);
                        hit.setAttribute('fill',   'transparent');
                        hit.setAttribute('stroke', 'none');
                        hit.setAttribute('data-handle', h.key);
                        hit.setAttribute('data-handle-hit', h.key);
                        hit.setAttribute('pointer-events', 'all');
                        hit.style.cursor = h.cursor;
                        selG.appendChild(hit);
                    });
                }

                // Rotation handle – circle above the element
                const rotHandle = document.createElementNS(ns, 'circle');
                rotHandle.setAttribute('cx', r.x + r.w / 2);
                rotHandle.setAttribute('cy', r.y - rotOffset);
                rotHandle.setAttribute('r', rotRadius);
                rotHandle.style.fill = 'var(--tm-wd-handle-fill)';
                rotHandle.style.stroke = 'var(--tm-wd-accent)';
                rotHandle.setAttribute('stroke-width', '1.5');
                rotHandle.setAttribute('data-rotate-handle', 'true');
                rotHandle.setAttribute('pointer-events', 'all');
                rotHandle.style.cursor = 'grab';
                selG.appendChild(rotHandle);

                if (coarse) {
                    const rotHit = document.createElementNS(ns, 'circle');
                    rotHit.setAttribute('cx', r.x + r.w / 2);
                    rotHit.setAttribute('cy', r.y - rotOffset);
                    rotHit.setAttribute('r', hitSize / 2);
                    rotHit.setAttribute('fill', 'transparent');
                    rotHit.setAttribute('stroke', 'none');
                    rotHit.setAttribute('data-rotate-handle', 'true');
                    rotHit.setAttribute('data-handle-hit', 'rotate');
                    rotHit.setAttribute('pointer-events', 'all');
                    rotHit.style.cursor = 'grab';
                    selG.appendChild(rotHit);
                }

                // Rotation handle line
                const rotLine = document.createElementNS(ns, 'line');
                rotLine.setAttribute('x1', r.x + r.w / 2);
                rotLine.setAttribute('y1', r.y);
                rotLine.setAttribute('x2', r.x + r.w / 2);
                rotLine.setAttribute('y2', r.y - 7);
                rotLine.style.stroke = 'var(--tm-wd-accent)';
                rotLine.setAttribute('stroke-width', '1');
                rotLine.setAttribute('stroke-dasharray', '2 2');
                rotLine.setAttribute('pointer-events', 'none');
                selG.appendChild(rotLine);
            }

            inst.svg.appendChild(selG);
        });
    },

    /**
     * The scale the canvas is really drawn at (screen px per svg unit; \meet\ letterboxing included), or 0 while the
     * canvas has no size yet (a hidden tab). The editor's zoom read-out starts from it, so a page wider than the
     * viewport shows its fitted percentage instead of a false 100 %.
     */
    getScale: function (svgElement) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return 0;
        const rect = inst.svg.getBoundingClientRect();
        if (!(rect.width > 0) || !(rect.height > 0)) return 0;
        const vb = this._getViewBox(inst);
        const scale = Math.min(rect.width / vb.w, rect.height / vb.h);
        inst.scale = scale;
        return scale;
    },

    /** The live zoom (screen px per svg unit), read from the svg rather than the possibly stale inst.scale. */
    _zoomNow: function (inst) {
        const vb = this._getViewBox(inst);
        const rect = inst.svg.getBoundingClientRect();
        return vb.w > 0 && vb.h > 0 && rect.width > 0 && rect.height > 0
            ? Math.min(rect.width / vb.w, rect.height / vb.h)
            : (inst.scale || 1);
    },

    /** On a coarse pointer the handle hit areas are sized in screen px, so they follow every zoom change. */
    _refreshHandlesIfCoarse: function (inst) {
        if (inst.selectedIds.size > 0 && this._isCoarse(inst)) this._updateHandles(inst);
    },

    // ── Rubber band helper ────────────────────────────────────────────────────

    _createConnectorPreview: function (inst, x, y) {
        const ns = 'http://www.w3.org/2000/svg';
        const path = document.createElementNS(ns, 'path');
        path.setAttribute('d', 'M ' + x + ' ' + y);
        path.setAttribute('fill', 'none');
        path.style.stroke = 'var(--tm-wd-accent)';
        path.setAttribute('stroke-width', '2');
        path.setAttribute('stroke-dasharray', '4 2');
        path.setAttribute('pointer-events', 'none');
        path.classList.add('tm-wd-connector-preview');
        inst.svg.appendChild(path);
        return path;
    },

    _createRubberRect: function (inst, x, y) {
        const ns   = 'http://www.w3.org/2000/svg';
        const rect = document.createElementNS(ns, 'rect');
        rect.setAttribute('x',      x);
        rect.setAttribute('y',      y);
        rect.setAttribute('width',  0);
        rect.setAttribute('height', 0);
        rect.style.fill = 'color-mix(in srgb, var(--tm-wd-accent) 8%, transparent)';
        rect.style.stroke = 'var(--tm-wd-accent)';
        rect.setAttribute('stroke-width', '1');
        rect.setAttribute('stroke-dasharray', '4 2');
        rect.setAttribute('pointer-events', 'none');
        rect.classList.add('tm-wd-rubber');
        inst.svg.appendChild(rect);
        return rect;
    },

    // ── Programmatic selection update (called from Blazor) ────────────────────

    setSelection: function (svgElement, ids) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        inst.selectedIds = new Set(ids || []);
        this._updateHandles(inst);
    },

    // ── Programmatic zoom ─────────────────────────────────────────────────────

    zoomTo: function (svgElement, scale) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        const vb  = this._getViewBox(inst);
        const cx  = vb.x + vb.w / 2;
        const cy  = vb.y + vb.h / 2;
        const svgRect = inst.svg.getBoundingClientRect();
        const newW = svgRect.width  / scale;
        const newH = svgRect.height / scale;
        this._setViewBox(inst, cx - newW / 2, cy - newH / 2, newW, newH);
        inst.scale = scale;
        inst.userViewChanged = true;
        this._refreshHandlesIfCoarse(inst);
    },

    fitToView: function (svgElement, padding) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return 1.0;
        padding = (padding != null) ? padding : 40;

        const groups = inst.svg.querySelectorAll('[data-el-id]');
        if (groups.length === 0) {
            // Fit to canvas
            const svgRect = inst.svg.getBoundingClientRect();
            this._setViewBox(inst, 0, 0, inst.canvasW, inst.canvasH);
            inst.scale = Math.min(svgRect.width / inst.canvasW, svgRect.height / inst.canvasH);
            inst.userViewChanged = false;   // the whole page fitted: a later container resize re-fits it
            return inst.scale;
        }

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        groups.forEach(g => {
            const id = g.getAttribute('data-el-id');
            const r  = this._elRect(inst, id);
            if (!r) return;
            minX = Math.min(minX, r.x);
            minY = Math.min(minY, r.y);
            maxX = Math.max(maxX, r.x + r.w);
            maxY = Math.max(maxY, r.y + r.h);
        });

        if (!isFinite(minX)) return 1.0;

        const contentW = maxX - minX + padding * 2;
        const contentH = maxY - minY + padding * 2;
        const svgRect  = inst.svg.getBoundingClientRect();
        const fitScale = Math.min(svgRect.width / contentW, svgRect.height / contentH, 2.0);
        const newW = svgRect.width  / fitScale;
        const newH = svgRect.height / fitScale;
        this._setViewBox(inst, minX - padding, minY - padding, newW, newH);
        inst.scale = fitScale;
        inst.userViewChanged = true;
        return fitScale;
    },

    // ── Initial zoom (decision WIREFRAME-INITIAL-ZOOM) ────────────────────────────

    /** The padding fit-to-view keeps around its subject (the same 40 px fitToView uses). */
    INITIAL_ZOOM_PADDING: 40,

    /**
     * Lays the view out for the given svg size: 'fit' shows the WHOLE page (plus the padding) at the largest scale that fits and
     * NEVER enlarges past 100 % (a page that fits at 100 % stays at exactly 100 %); 'actual' is 100 %. A page that does not fit an axis is pinned to its padded origin on that
     * axis, a page that is smaller is centred, so a small page is never stretched and a large one starts at its top-left corner.
     * Marks the view pristine (a later container resize lays it out again) and tells .NET the new view box.
     */
    _layoutInitial: function (inst, size, mode) {
        const pad = this.INITIAL_ZOOM_PADDING;
        const cw = inst.canvasW, ch = inst.canvasH;
        // A page that fits the canvas at 100 % (its own size, no padding) stays at exactly 100 %: the padding must not shrink it.
        const fitsActual = cw <= size.width && ch <= size.height;
        const scale = mode === 'actual' || fitsActual
            ? 1
            : Math.min(size.width / (cw + pad * 2), size.height / (ch + pad * 2), 1);
        const w = size.width / scale;
        const h = size.height / scale;
        // An axis the page overflows (actual size of a big page) starts at the padded origin; every other axis centres the page (for a
        // fitted axis that IS the padded origin).
        const x = cw > w ? -pad : cw / 2 - w / 2;
        const y = ch > h ? -pad : ch / 2 - h / 2;
        this._setViewBox(inst, x, y, w, h);
        inst.scale = scale;
        inst.userViewChanged = false;
        inst.layoutRect = size;
        return scale;
    },

    /**
     * Applies the initial zoom (first render, a swapped document, a page switch, a changed page size, a changed InitialZoom). The
     * optional <paramref name="mode"/> ('fit' | 'actual') replaces the remembered one; anything else keeps it, and a canvas that never
     * had one behaves as 'fit'. Returns the scale the canvas is now drawn at (0 while the svg has no size, e.g. a hidden tab: nothing
     * is changed and the first real size lays the view out through relayout). A zoom the user chose is NOT protected here: this is
     * the explicit "start over" for a new subject; the automatic paths (resize) never call it.
     */
    applyInitialZoom: function (svgElement, mode) {
        const inst = this.instances.get(svgElement ? svgElement.id : null);
        if (!inst || inst.destroyed || !inst.svg) return 0;
        if (mode === 'fit' || mode === 'actual') inst.initialZoom = mode;
        const size = this._measure(inst);
        if (!size) {
            // No size yet (a hidden tab): nothing can be laid out, but the NEW subject must not inherit the previous subject's user zoom or its
            // measured baseline - the first real size has to lay it out through relayout (pristine view, no remembered layout rect).
            inst.userViewChanged = false;
            inst.layoutRect = null;
            return 0;
        }
        const scale = this._layoutInitial(inst, size, inst.initialZoom || 'fit');
        this._refreshHandlesIfCoarse(inst);
        const vb = this._getViewBox(inst);
        this._invokeQuiet(inst, 'OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
        return scale;
    },
    // ── Pan to canvas origin ──────────────────────────────────────────────────

    resetView: function (svgElement) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        const svgRect = inst.svg.getBoundingClientRect();
        this._setViewBox(inst, 0, 0, svgRect.width, svgRect.height);
        inst.scale = 1.0;
        inst.userViewChanged = true;
    },

    // ── Container resize (panel open / close, window resize, device rotation) ─────

    /** The svg's pixel size, or null while it has none (a hidden tab). */
    _measure: function (inst) {
        if (!inst.svg || !inst.svg.getBoundingClientRect) return null;
        const rect = inst.svg.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 ? { width: rect.width, height: rect.height } : null;
    },

    /**
     * Re-measures the svg after its container changed size WITHOUT a Blazor re-render. A pristine view (the user has not
     * zoomed / panned yet) is re-fitted to the page; a view the user moved keeps its centre and its zoom (screen px per
     * svg unit), so the content does not jump when a side panel opens. .NET gets OnViewBoxChanged + OnZoomChanged and
     * the coarse handle hit areas follow the zoom. Returns the new scale (0 when the svg has no size or nothing changed).
     */
    relayout: function (svgElement) {
        const inst = this.instances.get(svgElement ? svgElement.id : null);
        if (!inst || inst.destroyed || !inst.svg) return 0;
        const next = this._measure(inst);
        if (!next) return 0;
        const prev = inst.layoutRect;
        if (prev && Math.abs(prev.width - next.width) < 0.5 && Math.abs(prev.height - next.height) < 0.5) return 0;
        // A gesture owns the viewBox (and the finger's coordinates) right now - selecting on press opens the tablet inspector sheet, which
        // narrows the canvas under a drag that has just started: leave the baseline alone and re-lay out when the gesture is released.
        if (this._gestureActive(inst)) { inst.relayoutDeferred = true; return 0; }
        inst.layoutRect = next;

        let scale;
        if (!inst.userViewChanged) {
            // A pristine view is laid out again the way it was first laid out: the initial zoom when the host chose one, else the meet fit.
            scale = inst.initialZoom
                ? this._layoutInitial(inst, next, inst.initialZoom)
                : (this._setViewBox(inst, 0, 0, inst.canvasW, inst.canvasH), Math.min(next.width / inst.canvasW, next.height / inst.canvasH));
        } else {
            const vb = this._getViewBox(inst);
            const base = prev || next;
            const zoom = vb.w > 0 && vb.h > 0 ? Math.min(base.width / vb.w, base.height / vb.h) : (inst.scale || 1);
            const cx = vb.x + vb.w / 2;
            const cy = vb.y + vb.h / 2;
            const w = next.width / zoom;
            const h = next.height / zoom;
            this._setViewBox(inst, cx - w / 2, cy - h / 2, w, h);
            scale = zoom;
        }
        inst.scale = scale;
        this._refreshHandlesIfCoarse(inst);
        const after = this._getViewBox(inst);
        this._invokeQuiet(inst, 'OnViewBoxChanged', after.x, after.y, after.w, after.h);
        this._invokeQuiet(inst, 'OnZoomChanged', scale);
        return scale;
    },

    _gestureActive: function (inst) {
        // inst.resize / rotate / waypoint exist from the PRESS (isResizing & co. only from the drag threshold): a relayout between the two
        // would move the viewBox under a finger that is already holding a handle.
        return !!(inst.body || inst.resize || inst.rotate || inst.waypoint || inst.isDragging || inst.isResizing || inst.isRotating || inst.isDraggingWaypoint
            || inst.isPanning || inst.isRubberBand || inst.pinchStart || inst.middlePan);
    },

    /** Runs the relayout a gesture postponed (called on every gesture exit). */
    _flushRelayout: function (inst) {
        if (!inst.relayoutDeferred || inst.destroyed || !inst.svg) return;
        inst.relayoutDeferred = false;
        this.relayout(inst.svg);
    },

    /** A ResizeObserver on the svg and its wrapper (guarded: a unit-test host or an old browser has none), one relayout per frame. */
    _observeResize: function (inst) {
        if (typeof ResizeObserver !== 'function') return;
        const self = this;
        inst.resizeObserver = new ResizeObserver(function () {
            if (inst.resizeFrame || inst.destroyed) return;
            const run = function () {
                inst.resizeFrame = 0;
                if (!inst.destroyed && inst.svg) self.relayout(inst.svg);
            };
            inst.resizeFrame = typeof requestAnimationFrame === 'function' ? requestAnimationFrame(run) : (run(), 0);
        });
        inst.resizeObserver.observe(inst.svg);
        const wrap = inst.svg.closest ? inst.svg.closest('.tm-wd-canvas-wrap') : null;
        if (wrap) inst.resizeObserver.observe(wrap);
    },

    _unobserveResize: function (inst) {
        if (inst.resizeObserver) { inst.resizeObserver.disconnect(); inst.resizeObserver = null; }
        if (inst.resizeFrame && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(inst.resizeFrame);
        inst.resizeFrame = 0;
    },

    // ── Toolbox drag init ─────────────────────────────────────────────────────

    /// <summary>
    /// Attaches a single delegated dragstart listener to a toolbox container.
    /// Items inside the container must carry data-component-type="…".
    /// </summary>
    initToolbox: function (toolboxElement) {
        if (!toolboxElement || toolboxElement._wdToolboxInit) return;
        toolboxElement._wdToolboxInit = true;
        toolboxElement.addEventListener('dragstart', function (e) {
            const item = e.target.closest('[data-component-type]');
            if (!item) return;
            const type = item.getAttribute('data-component-type');
            e.dataTransfer.setData('text/plain', type);
            e.dataTransfer.effectAllowed = 'copy';
        });
        // Space scroll guard. A toolbox item is a div[role=button][tabindex=0] (non-native), so its Space
        // activation runs on KEYUP in C# and the browser's default for Space keydown - scrolling the palette -
        // must be cancelled. Scoped on purpose (docs/keyboard-activation-convention.md): only when the
        // keydown target IS an item, so a Space typed into the search box and Tab are never touched.
        toolboxElement.addEventListener('keydown', function (e) {
            if (e.key === ' ' && e.target && e.target.classList && e.target.classList.contains('tm-wd-toolbox__item')) {
                e.preventDefault();
            }
        });
    },

    /// <summary>
    /// Scroll guard for the layers panel list. The active-layer radios are native buttons roamed with the arrow keys
    /// (handled in C#) and the layer name is a non-native div[role=button] whose Space activation runs on KEYUP, so
    /// the browser's default for those keydowns - scrolling the list - must be cancelled. Scoped on purpose
    /// (docs/keyboard-activation-convention.md): plain arrows only when the target IS a radio, Space only when the
    /// target IS a name; Alt+Arrow (reorder), Tab and every other key or target are never touched.
    /// </summary>
    initLayersList: function (listElement) {
        if (!listElement || listElement._wdLayersInit) return;
        listElement._wdLayersInit = true;
        listElement.addEventListener('keydown', function (e) {
            const target = e.target;
            if (!target || !target.classList) return;
            if (target.getAttribute('role') === 'radio'
                && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
                && (e.key === 'ArrowUp' || e.key === 'ArrowDown' || e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
                e.preventDefault();
            } else if (e.key === ' ' && target.classList.contains('tm-wd-layers__name')) {
                e.preventDefault();
            }
        });
    },
    // ── Export: bake token paints into explicit colours ───────────────────────
    // The canvas paints its page, grid and handles through var(--tm-wd-*) so dark mode and the indigo
    // theme reach it. A serialized export is standalone (the PNG/PDF rasteriser has no stylesheet and
    // no custom properties), so every node flagged data-wd-paint gets the paint the LIVE canvas computes
    // written back as an explicit presentation attribute, and the var() style is dropped. The page is
    // paper (--tm-paper), so the export is identical in every theme.
    _bakePaints: function (liveRoot, cloneRoot) {
        const live = liveRoot.querySelectorAll('[data-wd-paint]');
        const baked = cloneRoot.querySelectorAll('[data-wd-paint]');
        const count = Math.min(live.length, baked.length);
        for (let i = 0; i < count; i++) {
            const computed = window.getComputedStyle(live[i]);
            const props = (live[i].getAttribute('data-wd-paint') || '').split(/\s+/).filter(Boolean);
            props.forEach(function (prop) {
                const value = computed.getPropertyValue(prop);
                if (value) {
                    baked[i].setAttribute(prop, value);
                    baked[i].style.removeProperty(prop);
                }
            });
            if (!baked[i].getAttribute('style')) baked[i].removeAttribute('style');
            baked[i].removeAttribute('data-wd-paint');
        }
    },
    // ── Export SVG (strip UI overlays) ────────────────────────────────────────

    exportSvg: function (svgElement) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return '';
        const clone = inst.svg.cloneNode(true);
        // Bake FIRST: the live and cloned trees are index-paired, so the strip below (which removes
        // data-wd-paint nodes such as selection chrome) must not run before the pairing is used.
        this._bakePaints(inst.svg, clone);
        // Strip all interactive / UI-only layers
        clone.querySelectorAll(
            '.tm-wd-selection, .tm-wd-rubber, .tm-wd-grid-overlay, .tm-wd-ruler, .tm-wd-waypoint-handle, .tm-wd-rotate-handle, .tm-wd-resize-handle, .tm-wd-connector__waypoint, .tm-wd-connector__waypoint-hit, .tm-wd-guide, .tm-wd-connector-preview, .tm-wd-hold-cue'
        ).forEach(el => el.remove());
        // The export is the PAGE, not the live view: a fitted, panned or zoomed viewBox (InitialZoom Fit, a pinch, the padding around the page)
        // and the live sizing style (width/height:100%) must not leak into the file, or the server rasteriser would read a window onto the page.
        clone.setAttribute('viewBox', '0 0 ' + inst.canvasW + ' ' + inst.canvasH);
        clone.setAttribute('width', String(inst.canvasW));
        clone.setAttribute('height', String(inst.canvasH));
        clone.removeAttribute('style');
        clone.removeAttribute('id');
        return clone.outerHTML;
    },

    // ── Scroll / navigate to a document-space centre point ───────────────────

    scrollTo: function (svgElement, centreX, centreY) {
        const inst = this.instances.get(svgElement ? svgElement.id : null);
        if (!inst) return;
        const vb   = this._getViewBox(inst);
        const newX = centreX - vb.w / 2;
        const newY = centreY - vb.h / 2;
        this._setViewBox(inst, newX, newY, vb.w, vb.h);
        inst.userViewChanged = true;
    },

    // ── Mobile sheet ───────────────────────────────────────────────────────────

    /** The shell's sheet body is one scroller shared by every tab: a tab (or a peek) must start at its top, not at the previous tab's offset. */
    resetMobileSheetScroll: function (root) {
        if (!root || !root.querySelector) return false;
        const body = root.querySelector('.tm-editor-shell__sheet .tm-drawer__body');
        if (!body) return false;
        body.scrollTop = 0;
        return true;
    },

    // ── Mobile action bar focus (after the sheet unmounted) ────────────────────

    /** Moves the focus to the first tile of the editor's mobile action bar - only when the focus was lost (on <body> or detached). */
    focusFirstMobileAction: function (root) {
        if (!root || !root.querySelector) return false;
        const active = document.activeElement;
        const lost = !active || active === document.body || !document.contains(active);
        if (!lost) return false;
        const tile = root.querySelector('.tm-mobile-action-bar__action:not([disabled]), .tm-mobile-action-bar__more');
        if (!tile) return false;
        tile.focus({ preventScroll: true });
        return true;
    },

    /**
     * After a quick action (Delete / Lock) replaced the button that held the focus: the "More" tile of the action bar (it opens menus and
     * never inserts anything, unlike "Add"), else the canvas. Moves the focus only when it was really lost.
     */
    focusAfterMobileAction: function (root) {
        if (!root || !root.querySelector) return false;
        const active = document.activeElement;
        const lost = !active || active === document.body || !document.contains(active);
        if (!lost) return false;
        const target = root.querySelector('.tm-mobile-action-bar__more:not([disabled])') || root.querySelector('.tm-wd-canvas-wrap');
        if (!target) return false;
        target.focus({ preventScroll: true });
        return true;
    },

    // ── File download helper (used by C# export) ──────────────────────────────

    /**
     * Saves a file through a temporary Blob URL on the hidden anchor. A very long `data:` URL (a PNG/PDF export is easily a few MB
     * of base64) is silently dropped by iOS Safari and by some Chromium builds, while a Blob URL is not; the Blob path is the one
     * used, with the base64 `data:` URL kept only as the fallback for a host without Blob / URL.createObjectURL (and for the node
     * tests). iOS Safari has no real download: a link with the download attribute opens the file in a new tab (PNG/PDF/SVG/JSON are
     * all viewable there; the user saves it from the share sheet) - documented in docs/wireframe-editor.md.
     */
    downloadFile: function (anchorEl, fileName, mimeType, base64Data) {
        let url = null;
        let revoke = false;
        try {
            if (typeof Blob === 'function' && typeof URL !== 'undefined' && URL.createObjectURL && typeof atob === 'function') {
                const binary = atob(base64Data);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
                url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
                revoke = true;
            }
        } catch (err) { url = null; revoke = false; }
        if (!url) url = 'data:' + mimeType + ';base64,' + base64Data;

        anchorEl.href     = url;
        anchorEl.download = fileName;
        anchorEl.click();
        // Release the Blob URL and the anchor late: iOS Safari hands the blob to a new tab / the download manager asynchronously, so a
        // revoke a second after the click (the old delay) could kill the download before it started. 60 s is cheap (the bytes are
        // already in memory) and safe.
        setTimeout(() => {
            anchorEl.href = '';
            if (revoke) { try { URL.revokeObjectURL(url); } catch (err) { /* already gone */ } }
        }, 60000);
    },
};
