// Tempo Diagram Editor – Hybrid SVG + HTML canvas engine
// Pan, zoom, drag, touch, multi-select, keyboard shortcuts
window.tmDiagramEditor = {

    instances: new Map(),

    // ── MathJax support ──────────────────────────────────────────────────────

    mathJaxReady: false,

    loadMathJax: function () {
        if (this.mathJaxReady) return Promise.resolve();
        if (typeof window.MathJax === 'undefined') return Promise.resolve();
        const self = this;
        return new Promise(function (resolve) {
            const check = function () {
                if (window.MathJax && window.MathJax.typesetPromise) {
                    self.mathJaxReady = true;
                    resolve();
                } else {
                    setTimeout(check, 100);
                }
            };
            check();
        });
    },

    typesetMath: function (container, nodeId) {
        const self = this;
        return this.loadMathJax().then(function () {
            const inst = self.instances.get(container.id);
            if (!inst || !inst.dotNetRef) return;

            const selector = nodeId
                ? '[data-node-id="' + nodeId + '"] .tm-diagram-math'
                : '.tm-diagram-math';
            const elements = container.querySelectorAll(selector);
            if (elements.length === 0) return;

            return window.MathJax.typesetPromise(Array.from(elements)).then(function () {
                elements.forEach(function (el) {
                    const svg = el.querySelector('svg');
                    if (svg) {
                        const nid = el.closest('[data-node-id]')?.getAttribute('data-node-id');
                        if (nid) {
                            const serializer = new XMLSerializer();
                            const svgString = serializer.serializeToString(svg);
                            inst.dotNetRef.invokeMethodAsync('OnMathSvgCached', nid, svgString);
                        }
                    }
                });
            });
        });
    },

    // ── Init / Destroy ────────────────────────────────────────────────────────

    init: function (container, dotNetRef, options) {
        const id = container.id || 'tmd-' + Math.random().toString(36).substr(2, 9);
        // A Blazor re-render can call init on a container whose previous instance is still
        // registered (no dispose between): tear it down first so no listener or touch session
        // survives onto the fresh instance.
        if (this.instances.has(id)) this.destroy(container);
        container.id = id;

        const svg = container.querySelector('.tm-diagram-canvas__svg');
        if (!svg) throw new Error('TmDiagramCanvas requires an SVG element with class tm-diagram-canvas__svg');

        // F2 — 4-pane SVG structure (see planning/DIAGRAM_UNIFIED_SVG_PLAN.md §5.1).
        // The panes are rendered by Blazor as direct <g> children of the SVG and
        // live inside the same coordinate system, so no per-pane transform is
        // needed.
        const bgPane = svg.querySelector('.tm-diagram-bg-pane');
        const scenePane = svg.querySelector('.tm-diagram-scene-pane');
        const overlayPane = svg.querySelector('.tm-diagram-overlay-pane');
        const decoratorPane = svg.querySelector('.tm-diagram-decorator-pane');

        const opts = options || {};
        const inst = {
            container: container,
            svg: svg,
            bgPane: bgPane,
            scenePane: scenePane,
            overlayPane: overlayPane,
            decoratorPane: decoratorPane,
            dotNetRef: dotNetRef,

            readOnly: !!opts.readOnly,
            gridSize: opts.gridSize || 8,
            showGrid: opts.showGrid !== false,
            canvasW: opts.canvasWidth || 3000,
            canvasH: opts.canvasHeight || 2000,
            activeGroupId: opts.activeGroupId || null,

            // drag node state
            isDragging: false,
            dragNodeIds: [],
            dragStartScreen: null,
            dragStartPositions: {},

            // pan state
            isPanning: false,
            panStartScreen: null,
            viewBoxStart: null,

            // rubber-band selection
            isRubberBand: false,
            rubberStartDoc: null,
            rubberEl: null,

            // two-finger pinch/pan (pointer-gesture.js drives it; the flag lets the chrome
            // defer viewport moves that would fight the gesture)
            isPinching: false,

            // tool mode
            toolMode: 'select',
            spaceHeld: false,

            // selection
            selectedIds: new Set(),

            // scale tracker
            scale: 1.0,

                // edge drawing
                isDrawingEdge: false,
                drawSource: null,
                drawTempPath: null,

                // pending free-line draw (empty canvas mousedown in edge mode
                // that has not yet escalated to an active drag). Cleared on
                // escalation, mouseup, ESC, or right-click.
                isPendingEdgeDraw: false,
                pendingEdgeStart: null,

                // polyline draw (phase 2 — click-to-click). Active only when
                // the user clicked (no drag) on empty canvas while in edge
                // mode, or when a second empty-canvas click is made while
                // isDrawingEdge is already true. Each entry in
                // polylinePoints is an intermediate waypoint in document
                // coords: {x, y}. The source terminal is stored in
                // inst.drawSource (same shape as phase 1).
                isDrawingPolyline: false,
                polylinePoints: [],
                polylineCommittedAt: 0,

            // jetty drag
            isDraggingJetty: false,
            dragJettyEdgeId: null,
            dragJettyType: null,
            dragJettySide: null,
            dragJettyNodeId: null,
            dragJettyStartDoc: null,

            // whole-edge drag
            isDraggingWholeEdge: false,
            dragWholeEdgeId: null,
            dragWholeEdgeStartDoc: null,
            dragWholeEdgeDeltaDoc: { x: 0, y: 0 },

            // magnetic guidelines
            guideLines: [],
        };

        this.instances.set(id, inst);
        this._attachEvents(inst);
        // Phase 6: the touch/pen gesture stack (node drag, pan, long-press menu, pinch/pan,
        // resize/rotate sessions) lives in diagram-editor-touch.js over the shared core helpers
        // pointer-drag.js / pointer-gesture.js — a second pinch or long-press here is forbidden
        // (decision F7-POINTER-DRAG-PLURALS-RELATIVE-TIME).
        this._loadTouchModule(inst);

        // Auto-fit the content into the visible canvas on first initialization.
        // Without this, the default viewBox (e.g. 0 0 3000 2000) is much larger
        // than the container, making the effective scale much smaller than 1.0
        // (so edge strokes render sub-pixel thin and appear invisible), while
        // the Blazor toolbar still displays "100 %". Auto-fit keeps the initial
        // rendering aligned with the reported zoom level.
        if (opts.autoFit !== false) {
            try { this.fitToView(inst.container, (opts.padding != null) ? opts.padding : 40); }
            catch (_e) { /* fitToView already falls back gracefully */ }
        }

        // Report both viewport and the real scale so Blazor's zoom label is
        // correct from the very first render (init previously only sent the
        // viewport and left the scale at its Blazor-side default of 1.0).
        const vb = this._getViewBox(inst);
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x, vb.y, vb.w, vb.h);
            dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
        }
        return id;
    },

    destroy: function (container) {
        const id = container.id;
        const inst = this.instances.get(id);
        if (!inst) return;
        inst.__destroyed = true;   // a touch module still importing must not attach onto this canvas

        if (inst.__touchDetach) {
            try { inst.__touchDetach(); } catch (_e) { /* the sessions are already gone */ }
            inst.__touchDetach = null;
        }
        this._cancelRotateSession(inst);   // before inst.svg = null: the rollback needs the geometry
        this._detachEvents(inst);
        if (inst.rubberEl) inst.rubberEl.remove();
        this._clearSelectionOutlines(inst);

        if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
            inst.dotNetRef.dispose();
        }

        inst.svg = null;
        inst.bgPane = null;
        inst.scenePane = null;
        inst.overlayPane = null;
        inst.decoratorPane = null;
        inst.scenePane = null;
        inst.dotNetRef = null;
        this.instances.delete(id);
    },

    // ── Tool mode ─────────────────────────────────────────────────────────────

    setToolMode: function (container, mode) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        this._applyToolMode(inst, mode);
    },

    // Central tool-mode assignment helper. Keeps `inst.toolMode`, the
    // container cursor, and the `--edge-mode` / `--pan-mode` CSS classes
    // consistent so styling hooks (e.g. node pointer-cursor in edge mode)
    // stay in sync regardless of which code path flipped the mode.
    _applyToolMode: function (inst, mode) {
        if (!inst || !inst.container) return;
        inst.toolMode = mode;
        inst.container.style.cursor = mode === 'pan' ? 'grab' : (mode === 'edge' ? 'crosshair' : '');
        inst.container.classList.toggle('tm-diagram-canvas--edge-mode', mode === 'edge');
        inst.container.classList.toggle('tm-diagram-canvas--pan-mode', mode === 'pan');
    },

    // ── Canvas size update ────────────────────────────────────────────────────

    updateCanvasSize: function (container, w, h) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        inst.canvasW = w;
        inst.canvasH = h;
    },

    // ── Event attachment ──────────────────────────────────────────────────────
    // The mouse path stays here (characterization parity, phase 6); every touch/pen gesture is
    // the shared-helper session stack in diagram-editor-touch.js — no touchstart handlers.

    // Resolved NOW: document.currentScript is only set while this classic script runs. When it
    // is null (type="module", a loader or an injected script) find our own <script> tag; the last
    // resort resolves the deployed path against document.baseURI so <base href> is honoured and
    // the result is always an absolute URL (a bare '_content/...' string is not a module
    // specifier: import() would throw TypeError).
    _touchModuleUrl: (function () {
        const TOUCH = 'diagram-editor-touch.js';
        try {
            const script = document.currentScript;
            if (script && script.src) return new URL(TOUCH, script.src).href;
        } catch (_e) { /* fall through */ }
        try {
            const tags = document.querySelectorAll ? document.querySelectorAll('script[src]') : [];
            for (let i = tags.length - 1; i >= 0; i--) {
                const src = tags[i].src || '';
                // diagram-editor.js or a fingerprinted diagram-editor.<hash>.js, never -touch.js
                if (/\/diagram-editor(\.[A-Za-z0-9_-]+)?\.js(?:[?#]|$)/.test(src)) return new URL(TOUCH, src).href;
            }
        } catch (_e) { /* fall through */ }
        try {
            return new URL('_content/Tempo.Blazor.DiagramEditor/js/' + TOUCH, document.baseURI).href;
        } catch (_e) {
            return '/_content/Tempo.Blazor.DiagramEditor/js/' + TOUCH;
        }
    })(),

    // Test seam: the Node tests replace this to avoid a real dynamic import.
    _importTouchModule: function (url) { return import(url); },

    _loadTouchModule: function (inst) {
        if (inst.__touchDetach) return Promise.resolve();
        if (inst.__touchLoading) return inst.__touchLoading;
        const self = this;
        // Alive = not destroyed and still THE instance for its container id (a re-init replaces it).
        const alive = function () {
            return !inst.__destroyed && !!inst.container && self.instances.get(inst.container.id) === inst;
        };
        inst.__touchLoading = Promise.resolve()
            .then(function () { return self._importTouchModule(self._touchModuleUrl); })
            .then(function (module) {
                if (!alive()) return null;
                return module.attachTouch(self, inst);     // returned: a rejection reaches .catch
            })
            .then(function (detach) {
                if (typeof detach !== 'function') return;
                if (!alive() || inst.__touchDetach) { detach(); return; }
                inst.__touchDetach = detach;
            })
            .catch(function (err) {
                console.warn('tmDiagramEditor: the touch module could not be loaded; touch gestures are unavailable', err);
            })
            .finally(function () { inst.__touchLoading = null; });
        return inst.__touchLoading;
    },

    _attachEvents: function (inst) {
        inst._onMouseDown = (e) => this._onMouseDown(e, inst);
        inst._onMouseMove = (e) => this._onMouseMove(e, inst);
        inst._onMouseUp = (e) => this._onMouseUp(e, inst);
        inst._onWheel = (e) => this._onWheel(e, inst);
        inst._onKeyDown = (e) => this._onKeyDown(e, inst);
        inst._onKeyUp = (e) => this._onKeyUp(e, inst);
        inst._onDrop = (e) => this._onDrop(e, inst);
        inst._onDragOver = (e) => { e.preventDefault(); };
        inst._onContextMenu = (e) => this._onContextMenu(e, inst);
        inst._onDblClick = (e) => this._onDblClick(e, inst);

        inst._onSearchEscape = (e) => this._onSearchEscape(e, inst);
        inst.container.addEventListener('mousedown', inst._onMouseDown);
        inst.container.addEventListener('wheel', inst._onWheel, { passive: false });
        inst.container.addEventListener('drop', inst._onDrop);
        inst.container.addEventListener('dragover', inst._onDragOver);
        inst.container.addEventListener('contextmenu', inst._onContextMenu);
        inst.container.addEventListener('dblclick', inst._onDblClick);
        document.addEventListener('mousemove', inst._onMouseMove, true);
        document.addEventListener('mouseup', inst._onMouseUp, true);
        // Both keydown listeners run in the CAPTURE phase: the core tm-focus-trap module honors
        // Escape on a document BUBBLE keydown listener, so a bubble-phase consumer would race it
        // (registration order would decide the winner). Capture always runs before any bubble
        // listener; a consumed Escape is marked with preventDefault + tmEscapeHandled so the trap
        // (and any later consumer) skips it. See _onSearchEscape / _onKeyDown.
        document.addEventListener('keydown', inst._onSearchEscape, true);
        document.addEventListener('keydown', inst._onKeyDown, true);
        document.addEventListener('keyup', inst._onKeyUp);
    },

    _detachEvents: function (inst) {
        inst.container.removeEventListener('mousedown', inst._onMouseDown);
        inst.container.removeEventListener('wheel', inst._onWheel);
        inst.container.removeEventListener('drop', inst._onDrop);
        inst.container.removeEventListener('dragover', inst._onDragOver);
        inst.container.removeEventListener('contextmenu', inst._onContextMenu);
        if (inst._onDblClick) inst.container.removeEventListener('dblclick', inst._onDblClick);
        document.removeEventListener('mousemove', inst._onMouseMove, true);
        document.removeEventListener('mouseup', inst._onMouseUp, true);
        if (inst._onSearchEscape) {
            document.removeEventListener('keydown', inst._onSearchEscape, true);
            inst._onSearchEscape = null;
        }
        document.removeEventListener('keydown', inst._onKeyDown, true);
        document.removeEventListener('keyup', inst._onKeyUp);
    },

    // ── Order-independent Escape consumers (capture phase) ───────────────────
    // The host page may layer this editor inside an overlay (a TmModal dialog) whose focus trap
    // honors Escape on a document BUBBLE keydown listener. Any consumer registered in the bubble
    // phase would be order-dependent against it, so the editor's Escape consumers run in the
    // CAPTURE phase and mark an Escape they actually handle (preventDefault + tmEscapeHandled) —
    // the trap and any later consumer skip a handled one. An Escape NOBODY here consumes must stay
    // unmarked so it keeps bubbling and closes the host overlay (draw.io ordering: an empty canvas
    // Escape belongs to the dialog).

    // Escape closes the TOPMOST layer only — one press closes ONE layer (Fáze 10, confirmed
    // ordering: context menu > "⋯" view menu > search panel > inline edit > gesture/selection >
    // the host dialog). While the custom context menu is open it owns the Escape through its own
    // capture handler (this consumer must not close a surface under it); with the menu gone the
    // view menu outranks the search panel, and an Escape typed into an inline edit cancels the
    // edit without closing a host overlay. All consumers only fire while the focus is inside the
    // editor root, so an Escape typed elsewhere (another surface of the host page) is not stolen.
    _onSearchEscape: function (e, inst) {
        if (!inst.container || !inst.container.closest || !inst.dotNetRef) return;
        if (!e || e.key !== 'Escape' || e.defaultPrevented || e.tmEscapeHandled) return;
        // The open context menu is the topmost layer: its _menuKeyHandler (capture, registered on
        // open) consumes the Escape — skipping here is what makes the MENU close before the
        // search panel even though this listener was registered first.
        if (inst._menuOpen) return;
        const root = inst.container.closest('.tm-diagram-editor');
        if (!root) return;
        if (!root.contains(document.activeElement)) return;

        const viewMenu = root.querySelector('.tm-diagram-editor__view-menu[data-open]');
        if (viewMenu) {
            e.preventDefault();
            e.tmEscapeHandled = true;
            inst.dotNetRef.invokeMethodAsync('OnViewMenuEscape');
            return;
        }

        const panel = root.querySelector('.tm-diagram-search-panel');
        if (panel) {
            e.preventDefault();
            e.tmEscapeHandled = true;
            inst.dotNetRef.invokeMethodAsync('OnSearchEscape');
            return;
        }

        // Fáze 10: an Escape typed into an INLINE EDIT of the canvas (the edge-label foreignObject
        // input or a node inline editor input/textarea) belongs to the editor: the edit CANCELS
        // (value reverted, nothing pushed to the undo stack) and the Escape is consumed so a host
        // overlay (the open-editor dialog) does not close under the caret. The owning component is
        // notified — a node inline editor through its registered TmDiagramStencilShape, the canvas
        // through OnInlineEditEscape (which also covers the edge-label input). Reaching this
        // branch means no menu, no view menu and no panel consumed the key.
        const active = document.activeElement;
        if (active && typeof active.closest === 'function'
            && inst.container.contains(active)
            && active.closest('.tm-diagram-node__inline-input, .tm-diagram-node__inline-textarea, .tm-diagram-edge-label-input')) {
            e.preventDefault();
            e.tmEscapeHandled = true;
            const nodeGroup = active.closest('g.tm-diagram-node[data-node-id]');
            const shapeRef = nodeGroup && window.tmDiagramStencilShape
                ? window.tmDiagramStencilShape.getRef(nodeGroup.getAttribute('data-node-id'))
                : null;
            if (shapeRef && typeof shapeRef.invokeMethodAsync === 'function') {
                shapeRef.invokeMethodAsync('CancelActiveEditFromJs');
                // The node inline editor unmounts with the cancel, so the focus would drop to
                // body: return it to the canvas container (the canvas side mirrors this for the
                // edge-label path through FocusCanvasAsync).
                inst.container.focus({ preventScroll: true });
            }
            inst.dotNetRef.invokeMethodAsync('OnInlineEditEscape');
        }
    },

    _onDrop: function (e, inst) {
        e.preventDefault();
        if (inst.readOnly) return;
        const stencilId = window.__tmDiagramDragStencil;
        if (!stencilId) return;

        const docPt = this._screenToDoc(inst, e.clientX, e.clientY);

        inst.dotNetRef.invokeMethodAsync('OnDropFromToolbox', stencilId, docPt.x, docPt.y);
        window.__tmDiagramDragStencil = null;
    },

    _onContextMenu: function (e, inst) {
        if (inst.readOnly || !inst.dotNetRef) return;

        e.preventDefault();

        // R4: every touch long-press (node, edge, empty canvas) is owned by the contextMenu
        // session (600 ms, diagram-editor-touch.js). Android Chrome and Windows touch also fire a
        // native contextmenu for the same hold — swallow it BEFORE the close-on-menu branch, or
        // the touch-synthesized event ON the just-opened menu would close it again.
        if (this._isTouchContextMenu(e, inst)) return;

        // If clicking on the custom context menu itself, close it and prevent native menu
        if (e.target.closest('.tm-diagram-editor__context-menu')) {
            inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
            return;
        }

        this._openContextMenuFor(inst, this._contextMenuTarget(e.target), e.clientX, e.clientY);
    },

    // Shared by the right-click and the touch long-press: what the press is on.
    _contextMenuTarget: function (target) {
        if (!target || !target.closest) return { kind: 'canvas' };
        const labelGroupEl = target.closest('.tm-diagram-edge-label-group');
        if (labelGroupEl) {
            const labelHandle = labelGroupEl.querySelector('[data-label-handle]');
            const edgeId = (labelHandle && labelHandle.getAttribute('data-edge-id')) || labelGroupEl.getAttribute('data-edge-id');
            if (edgeId) return { kind: 'edge', id: edgeId };
        }
        const tableCellEl = target.closest('.tm-diagram-node__table-cell');
        const nodeEl = target.closest('.tm-diagram-node');
        if (tableCellEl && nodeEl) {
            const id = nodeEl.getAttribute('data-node-id');
            const row = parseInt(tableCellEl.getAttribute('data-row'), 10);
            const col = parseInt(tableCellEl.getAttribute('data-col'), 10);
            return (id && !isNaN(row) && !isNaN(col)) ? { kind: 'cell', id, row, col } : null;
        }
        if (nodeEl) { const id = nodeEl.getAttribute('data-node-id'); return id ? { kind: 'node', id } : null; }
        const edgeEl = target.closest('.tm-diagram-edge-hit-path');
        if (edgeEl) { const id = edgeEl.getAttribute('data-edge-id'); return id ? { kind: 'edge', id } : null; }
        return { kind: 'canvas' };
    },

    _openContextMenuFor: function (inst, t, clientX, clientY, origin) {
        if (!t || !inst.dotNetRef) return;
        const src = origin || 'mouse';
        if (t.kind === 'cell') inst.dotNetRef.invokeMethodAsync('OnTableCellContextMenu', t.id, t.row, t.col, clientX, clientY, src);
        else if (t.kind === 'node') inst.dotNetRef.invokeMethodAsync('OnNodeContextMenu', t.id, clientX, clientY, src);
        else if (t.kind === 'edge') inst.dotNetRef.invokeMethodAsync('OnEdgeContextMenu', t.id, clientX, clientY, src);
        else {
            const docPt = this._screenToDoc(inst, clientX, clientY);
            inst.dotNetRef.invokeMethodAsync('OnCanvasContextMenu', docPt.x, docPt.y, clientX, clientY, src);
        }
    },

    _isTouchContextMenu: function (e, inst) {
        if (!inst.__touchDetach) return false;   // no touch stack on this canvas: a contextmenu is a mouse right-click
        if (e.pointerType === 'touch') return true;                                    // contextmenu is a PointerEvent in Chromium/Firefox
        if (inst.__touchContacts && inst.__touchContacts.size > 0) return true;        // fired during the hold
        return inst.__lastTouchUpAt > 0 && Date.now() - inst.__lastTouchUpAt < 800;     // Windows: fired on lift
    },

    // Touch hooks for the long-press session (R4).
    touchContextMenuTarget: function (inst, target) {
        if (inst.readOnly || !target || !target.closest || !target.closest('.tm-diagram-canvas__svg')) return null;
        return this._contextMenuTarget(target);
    },
    // `origin` is the pointer type that opened the menu ('touch'); C# mirrors it as
    // data-anchor="touch" so openContextMenu can offset the menu away from the finger.
    touchOpenContextMenu: function (inst, t, clientX, clientY, origin) { this._openContextMenuFor(inst, t, clientX, clientY, origin); },

    // ── Coordinate helpers ────────────────────────────────────────────────────

    _getViewBox: function (inst) {
        const vb = inst.svg.viewBox.baseVal;
        return { x: vb.x, y: vb.y, w: vb.width, h: vb.height };
    },

    _setViewBox: function (inst, x, y, w, h) {
        inst.svg.setAttribute('viewBox', x + ' ' + y + ' ' + w + ' ' + h);
        // After F2 the HTML nodes live inside a <foreignObject> in the scene
        // pane, so they share the SVG's viewBox. inst.scale tracks the effective
        // screen-to-doc ratio (still derived from the SVG's rendered width) for
        // callers that want "zoom level" independent of getScreenCTM.
        inst.scale = Math.max(inst.svg.getBoundingClientRect().width, 1) / w;
        // Keep the Blazor-side `_viewBox` string in sync so any subsequent
        // parent re-render (which would otherwise reset the viewBox attribute
        // from stale Blazor state) preserves the current view. Without this,
        // changes like fitToView/zoomTo would be lost whenever OnParametersSet
        // re-computes _viewBox from the document dimensions.
        if (inst.dotNetRef)
            inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', x, y, w, h);
    },

    // Zoom limits shared by wheel, pinch, zoomTo and fitToView. C# mirror: DiagramZoomLimits.cs
    // (the zoom buttons step between them); DiagramZoomLimitsTests keeps both equal.
    MIN_SCALE: 0.15,
    MAX_SCALE: 5.0,
    // The fit ceiling: zoomToRect and fitToView never fit content LARGER than this on screen.
    FIT_MAX_SCALE: 2.0,
    _clampScale: function (s) { return Math.min(this.MAX_SCALE, Math.max(this.MIN_SCALE, s)); },

    _snap: function (inst, v) {
        const g = inst.gridSize;
        return g > 0 ? Math.round(v / g) * g : v;
    },

    _screenToDoc: function (inst, clientX, clientY) {
        const pt = inst.svg.createSVGPoint();
        pt.x = clientX;
        pt.y = clientY;
        const ctm = inst.svg.getScreenCTM();
        if (!ctm) return { x: 0, y: 0 };
        const r = pt.matrixTransform(ctm.inverse());
        return { x: r.x, y: r.y };
    },

    // ── Node lookup helpers ───────────────────────────────────────────────────

    _nodeEl: function (inst, id) {
        if (!inst.scenePane) return null;
        return inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + id + '"]');
    },

    // Post-F3 node position/rotation live in the SVG `transform` attribute on
    // the `<g class="tm-diagram-node">`. The stored format is:
    //   translate(X,Y) rotate(θ cx cy)
    // where (cx, cy) is the node centre (W/2, H/2) so the rotation pivot
    // stays at the node's centre regardless of X/Y. The parsers below cover
    // both F3's canonical format and any legacy single-part transforms.
    _parseNodeTransform: function (el) {
        const t = el ? (el.getAttribute('transform') || '') : '';
        const tm = t.match(/translate\(\s*([-\d.e+]+)\s*[,\s]\s*([-\d.e+]+)\s*\)/);
        const rm = t.match(/rotate\(\s*([-\d.e+]+)(?:\s*[,\s]\s*([-\d.e+]+)\s*[,\s]\s*([-\d.e+]+))?\s*\)/);
        return {
            x: tm ? parseFloat(tm[1]) : 0,
            y: tm ? parseFloat(tm[2]) : 0,
            rot: rm ? parseFloat(rm[1]) : 0,
            cx: (rm && rm[2] !== undefined) ? parseFloat(rm[2]) : null,
            cy: (rm && rm[3] !== undefined) ? parseFloat(rm[3]) : null,
        };
    },

    _buildNodeTransform: function (x, y, rot, w, h) {
        let t = 'translate(' + x + ',' + y + ')';
        if (rot) {
            const cx = (w != null ? w : 0) / 2;
            const cy = (h != null ? h : 0) / 2;
            t += ' rotate(' + rot + ' ' + cx + ' ' + cy + ')';
        }
        return t;
    },

    _nodeRect: function (inst, id) {
        const el = this._nodeEl(inst, id);
        if (!el) return null;
        const p = this._parseNodeTransform(el);
        const dw = parseFloat(el.getAttribute('data-w') || '0');
        const dh = parseFloat(el.getAttribute('data-h') || '0');
        return { x: p.x, y: p.y, w: dw, h: dh };
    },

    _getNodeRotation: function (el) {
        return this._parseNodeTransform(el).rot;
    },

    _setNodeTranslate: function (inst, id, x, y) {
        const el = this._nodeEl(inst, id);
        if (!el) return;
        const rot = this._getNodeRotation(el);
        const w = parseFloat(el.getAttribute('data-w') || '0');
        const h = parseFloat(el.getAttribute('data-h') || '0');
        el.setAttribute('transform', this._buildNodeTransform(x, y, rot, w, h));
    },

    _isNodeInActiveGroup: function (inst, nodeEl) {
        if (!nodeEl) return false;
        const parentGroupId = nodeEl.getAttribute('data-parent-group-id') || null;
        if (!inst.activeGroupId) return !parentGroupId;
        return parentGroupId === inst.activeGroupId;
    },

    _clampChildPosition: function (inst, childId, x, y) {
        const childEl = this._nodeEl(inst, childId);
        const parentId = childEl ? childEl.getAttribute('data-parent-id') : null;
        if (!parentId || inst.dragNodeIds.includes(parentId)) return { x, y };
        const parentRect = this._nodeRect(inst, parentId);
        const childRect = this._nodeRect(inst, childId);
        if (!parentRect || !childRect) return { x, y };
        const padding = 4;
        const minX = parentRect.x + padding;
        const minY = parentRect.y + padding;
        const maxX = parentRect.x + parentRect.w - childRect.w - padding;
        const maxY = parentRect.y + parentRect.h - childRect.h - padding;
        let cx = Math.max(minX, Math.min(x, maxX));
        let cy = Math.max(minY, Math.min(y, maxY));

        // Also clamp to parent group bounds
        const groupId = childEl ? childEl.getAttribute('data-parent-group-id') : null;
        if (groupId) {
            const groupRect = this._nodeRect(inst, groupId);
            if (groupRect && childRect && !inst.dragNodeIds.includes(groupId)) {
                const gMinX = groupRect.x + padding;
                const gMinY = groupRect.y + padding;
                const gMaxX = groupRect.x + groupRect.w - childRect.w - padding;
                const gMaxY = groupRect.y + groupRect.h - childRect.h - padding;
                cx = Math.max(gMinX, Math.min(cx, gMaxX));
                cy = Math.max(gMinY, Math.min(cy, gMaxY));
            }
        }
        return { x: cx, y: cy };
    },

    _includeGroupNodes: function (inst, ids) {
        const allIds = new Set(ids);
        const groupIds = new Set();
        ids.forEach(nid => {
            const el = this._nodeEl(inst, nid);
            if (!el) return;
            // If selected node is a group container, find its children by parent-group-id
            if (el.getAttribute('data-stencil-id') === 'general.group') {
                groupIds.add(nid);
            }
            // Also check if the node itself is a member of a group
            const gid = el.getAttribute('data-group-id');
            if (gid) groupIds.add(gid);
        });
        if (groupIds.size > 0 && inst.scenePane) {
            inst.scenePane.querySelectorAll('g.tm-diagram-node[data-node-id]').forEach(el => {
                const nid = el.getAttribute('data-node-id');
                if (!nid || allIds.has(nid)) return;
                const parentGroupId = el.getAttribute('data-parent-group-id') || null;
                const groupId = el.getAttribute('data-group-id') || null;
                // Include if this node belongs to any selected group
                if (parentGroupId && groupIds.has(parentGroupId)) {
                    allIds.add(nid);
                }
                // Also include by legacy group-id matching
                else if (groupId && groupIds.has(groupId)) {
                    const pgid = el.getAttribute('data-parent-group-id') || null;
                    const isVisible = !inst.activeGroupId
                        ? !pgid || pgid === groupId
                        : pgid === inst.activeGroupId;
                    if (isVisible) allIds.add(nid);
                }
            });
        }
        return [...allIds];
    },

    // ── Group bounds ─────────────────────────────────────────────────────────
    //
    // F6: No JS imperative group-bounds (no scenePane.appendChild div/rect).
    // Razor emits <rect class="tm-diagram-group-bounds"> in .tm-diagram-bg-pane
    // from Document.Nodes — model → view. Bounds update when Blazor re-renders
    // after drag/resize commit; during live drag they stay at last committed
    // geometry (less visual noise than a constantly resizing dashed frame).

    // ── Hit-path helper (HTML overlay may block SVG hit-path, use elementFromPoint fallback)
    _findHitPath: function (e) {
        let el = e.target.closest('.tm-diagram-edge-hit-path');
        if (el) return el;
        if (typeof document.elementsFromPoint === 'function') {
            const all = document.elementsFromPoint(e.clientX, e.clientY);
            for (let i = 0; i < all.length; i++) {
                if (all[i].classList && all[i].classList.contains('tm-diagram-edge-hit-path')) {
                    return all[i];
                }
            }
        }
        return null;
    },

    // First element carrying `className` at the press: the raw target first, then the hit stack
    // (edges paint over nodes, so a port / connection point under an edge corridor is not the
    // event target). `stack` is computed once per mousedown by the caller. A search for a port
    // or a connection point also accepts the finger-sized `.tm-diagram-port-hit` circle rendered
    // over them (it carries the same data-port-id / data-cp-* attributes).
    _firstWithClass: function (target, stack, className) {
        const hitAlias = className === 'tm-diagram-port' || className === 'tm-diagram-connection-point';
        const direct = target && target.closest ? target.closest('.' + className) : null;
        if (direct) return direct;
        if (hitAlias && target && target.closest) {
            const hit = target.closest('.tm-diagram-port-hit');
            if (hit) return hit;
        }
        for (let i = 0; i < stack.length; i++) {
            const el = stack[i];
            if (el.classList && (el.classList.contains(className) || (hitAlias && el.classList.contains('tm-diagram-port-hit')))) return el;
        }
        return null;
    },

    // ── Mouse down ───────────────────────────────────────────────────────────

    _onMouseDown: function (e, inst) {
        // Read-only (R1): the left button inspects (selects a node, clears on the empty surface);
        // middle button, Space+drag and the Pan tool still pan below. Edges select through the
        // Blazor @onclick (OnEdgeClicked), so a press on an edge is left alone here.
        if (inst.readOnly && e.button !== 1 && !inst.spaceHeld && inst.toolMode !== 'pan') {
            if (e.button !== 0) return;
            // Read-only keeps the canvas focusable (keyboard panning, Escape): the writable
            // path below focuses it, so the inspection branch must do the same.
            inst.container.focus({ preventScroll: true });
            if (e.target.closest('.tm-diagram-edge-hit-path, .tm-diagram-edge-label-group')) return;
            const roNodeEl = e.target.closest('[data-node-id]');
            if (roNodeEl) this.selectNodeForInspection(inst, roNodeEl, !!(e.ctrlKey || e.metaKey));
            else this.clearSelectionForInspection(inst);
            return;
        }

        // Ignore when interacting with inline edit inputs or edge toolbars
        if (e.target.closest('.tm-diagram-node__inline-input, .tm-diagram-node__inline-textarea, .tm-diagram-edge-label-input, .tm-diagram-edge-toolbar')) {
            return;
        }

        inst.container.focus({ preventScroll: true });

        // Middle mouse OR Space held OR pan mode => pan
        const wantPan = e.button === 1 || inst.spaceHeld || inst.toolMode === 'pan';
        if (wantPan) {
            e.preventDefault();
            inst.isPanning = true;
            inst.panStartScreen = { x: e.clientX, y: e.clientY };
            inst.viewBoxStart = this._getViewBox(inst);
            inst.container.style.cursor = 'grabbing';
            return;
        }

        const isRightClick = e.button === 2;
        if (e.button !== 0 && !isRightClick) return;

        // Any right-click aborts a pending free-line draw (mirrors the
        // classic "right-click to cancel drawing" UX from draw.io / Visio).
        if (isRightClick && inst.isPendingEdgeDraw) {
            inst.isPendingEdgeDraw = false;
            inst.pendingEdgeStart = null;
        }

        // Polyline draw in progress: intercept before any other handler so
        // clicks only add waypoints / commit the edge. Right-click discards
        // the whole draft.
        if (inst.isDrawingPolyline) {
            if (isRightClick) {
                e.preventDefault();
                e.stopPropagation();
                this._cancelEdgeDraw(inst);
                return;
            }
            if (e.button === 0) {
                e.preventDefault();
                e.stopPropagation();
                this._onPolylineMouseDown(e, inst);
                return;
            }
        }

        // Right-click on edge or label -> select edge
        if (isRightClick) {
            const edgeEl = this._findHitPath(e);
            if (edgeEl) {
                const edgeId = edgeEl.getAttribute('data-edge-id');
                if (edgeId) {
                    if (e.ctrlKey || e.metaKey) {
                        if (inst.selectedIds.has(edgeId)) inst.selectedIds.delete(edgeId);
                        else inst.selectedIds.add(edgeId);
                    } else {
                        inst.selectedIds = new Set([edgeId]);
                        this._updateSelection(inst);
                    }
                    inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
                    return;
                }
            }
            const labelGroupEl = e.target.closest('.tm-diagram-edge-label-group');
            if (labelGroupEl) {
                const labelHandle = labelGroupEl.querySelector('[data-label-handle]');
                const edgeId = labelHandle ? labelHandle.getAttribute('data-edge-id') : null;
                if (edgeId) {
                    inst.selectedIds = new Set([edgeId]);
                    this._updateSelection(inst);
                    inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [edgeId]);
                    return;
                }
            }
        }

        // Edge label handle clicked?
        const labelHitEl = e.target.closest('[data-label-handle="true"]');
        if (labelHitEl && !inst.readOnly) {
            e.preventDefault();
            e.stopPropagation();
            inst.isDraggingEdgeLabel = true;
            inst.dragEdgeLabelId = labelHitEl.getAttribute('data-edge-id');
            inst.dragEdgeLabelStart = { x: e.clientX, y: e.clientY };
            const g = labelHitEl.closest('.tm-diagram-edge-label-group');
            if (g) {
                inst.dragEdgeLabelStartT = parseFloat(g.getAttribute('data-label-t')) || 0.5;
                inst.dragEdgeLabelStartOx = parseFloat(g.getAttribute('data-label-ox')) || 0;
                inst.dragEdgeLabelStartOy = parseFloat(g.getAttribute('data-label-oy')) || 0;
            }
            return;
        }

        // Edge virtual bend clicked?
        const virtualBendEl = e.target.closest('.tm-diagram-edge-virtual-bend');
        if (virtualBendEl && !inst.readOnly) {
            e.preventDefault();
            e.stopPropagation();
            const edgeId = virtualBendEl.getAttribute('data-edge-id');
            const segmentIndex = parseInt(virtualBendEl.getAttribute('data-segment-index'), 10);
            const pt = this._screenToDoc(inst, e.clientX, e.clientY);
            (async () => {
                const newIndex = await inst.dotNetRef.invokeMethodAsync('OnVirtualBendInsert', edgeId, segmentIndex, pt.x, pt.y);
                inst.isDraggingWaypoint = true;
                inst.dragWaypointEdgeId = edgeId;
                inst.dragWaypointIndex = newIndex;
                inst.dragWaypointStartScreen = { x: e.clientX, y: e.clientY };
                inst.dragWaypointStartDoc = pt;
            })();
            return;
        }

        // Edge waypoint / jetty handle clicked?
        const handleEl = e.target.closest('.tm-diagram-edge-handle');
        if (handleEl && !inst.readOnly) {
            const isWaypoint = handleEl.getAttribute('data-waypoint') === 'true';
            if (isWaypoint) {
                e.preventDefault();
                e.stopPropagation();
                inst.isDraggingWaypoint = true;
                inst.dragWaypointEdgeId = handleEl.getAttribute('data-edge-id');
                inst.dragWaypointIndex = parseInt(handleEl.getAttribute('data-handle-index'), 10) - 1;
                inst.dragWaypointStartScreen = { x: e.clientX, y: e.clientY };
                const pt = this._screenToDoc(inst, e.clientX, e.clientY);
                inst.dragWaypointStartDoc = pt;
                return;
            }
            const jettyType = handleEl.getAttribute('data-jetty');
            if (jettyType) {
                const jettyNodeId = handleEl.getAttribute('data-node-id');
                const jettyNodeEl = jettyNodeId ? this._nodeEl(inst, jettyNodeId) : null;
                if (jettyNodeEl && jettyNodeEl.getAttribute('data-locked') === 'true') return;
                e.preventDefault();
                e.stopPropagation();
                inst.isDraggingJetty = true;
                inst.dragJettyEdgeId = handleEl.getAttribute('data-edge-id');
                inst.dragJettyType = jettyType;
                inst.dragJettySide = handleEl.getAttribute('data-jetty-side');
                inst.dragJettyNodeId = jettyNodeId;
                const pt = this._screenToDoc(inst, e.clientX, e.clientY);
                inst.dragJettyStartDoc = pt;
                return;
            }
            const danglingType = handleEl.getAttribute('data-dangling');
            if (danglingType) {
                e.preventDefault();
                e.stopPropagation();
                inst.isDraggingDangling = true;
                inst.dragDanglingEdgeId = handleEl.getAttribute('data-edge-id');
                inst.dragDanglingType = danglingType;
                const pt = this._screenToDoc(inst, e.clientX, e.clientY);
                inst.dragDanglingStartDoc = pt;
                inst.dragDanglingStartScreen = { x: e.clientX, y: e.clientY };
                return;
            }
        }

        // Segment handle clicked? (orthogonal edge segment drag)
        const segmentHandleEl = e.target.closest('.tm-diagram-edge-segment-handle');
        if (segmentHandleEl && !inst.readOnly) {
            e.preventDefault();
            e.stopPropagation();
            const edgeId = segmentHandleEl.getAttribute('data-edge-id');
            const segmentIndex = parseInt(segmentHandleEl.getAttribute('data-segment-index'), 10);
            const handles = Array.from(inst.svg.querySelectorAll('circle.tm-diagram-edge-handle[data-edge-id="' + edgeId + '"]'))
                .sort(function (a, b) {
                    return parseInt(a.getAttribute('data-handle-index'), 10) - parseInt(b.getAttribute('data-handle-index'), 10);
                });
            if (handles.length >= segmentIndex + 2) {
                const x1 = parseFloat(handles[segmentIndex].getAttribute('cx'));
                const y1 = parseFloat(handles[segmentIndex].getAttribute('cy'));
                const x2 = parseFloat(handles[segmentIndex + 1].getAttribute('cx'));
                const y2 = parseFloat(handles[segmentIndex + 1].getAttribute('cy'));
                const isVertical = Math.abs(x1 - x2) < 0.5;
                const isHorizontal = Math.abs(y1 - y2) < 0.5;
                inst.isDraggingSegment = true;
                inst.dragSegmentEdgeId = edgeId;
                inst.dragSegmentIndex = segmentIndex;
                inst.dragSegmentIsVertical = isVertical;
                inst.dragSegmentIsHorizontal = isHorizontal;
                inst.dragSegmentStartScreen = { x: e.clientX, y: e.clientY };
                inst.dragSegmentHandlePositions = handles.map(function (h) {
                    return {
                        x: parseFloat(h.getAttribute('cx')),
                        y: parseFloat(h.getAttribute('cy'))
                    };
                });
                inst.container.style.cursor = isVertical ? 'col-resize' : 'row-resize';
            }
            return;
        }

        // Left-click on edge hit-path? -> start whole-edge drag. A port sitting under the
        // pointer wins over the edge corridor, though: edges paint over nodes, so the fat
        // invisible hit path is the top element at a port's position and the raw target is
        // the hit path, not the port (phase 8 regression — pressing a port that carries an
        // attached edge started a whole-edge drag and edge drawing became impossible).
        const pointerStack = (typeof document.elementsFromPoint === 'function') ? document.elementsFromPoint(e.clientX, e.clientY) : [];
        if (!isRightClick && !inst.readOnly) {
            const edgeHitEl = this._findHitPath(e);
            var portUnderPointer = !!edgeHitEl && !!(this._firstWithClass(e.target, pointerStack, 'tm-diagram-port')
                || this._firstWithClass(e.target, pointerStack, 'tm-diagram-connection-point'));
            if (edgeHitEl && !portUnderPointer) {
                const edgeId = edgeHitEl.getAttribute('data-edge-id');
                if (edgeId) {
                    if (!inst.selectedIds.has(edgeId)) {
                        inst.selectedIds = new Set([edgeId]);
                        this._updateSelection(inst);
                        inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
                    }
                    e.preventDefault();
                    e.stopPropagation();
                    inst.isDraggingWholeEdge = true;
                    inst.dragWholeEdgeId = edgeId;
                    const pt = this._screenToDoc(inst, e.clientX, e.clientY);
                    inst.dragWholeEdgeStartDoc = pt;
                    inst.dragWholeEdgeDeltaDoc = { x: 0, y: 0 };
                    // Cache edge endpoint positions for guideline snapping
                    const pathEl = inst.svg.querySelector('.tm-diagram-edge-hit-path[data-edge-id="' + edgeId + '"]');
                    if (pathEl) {
                        const len = pathEl.getTotalLength();
                        const p1 = pathEl.getPointAtLength(0);
                        const p2 = pathEl.getPointAtLength(len);
                        inst.dragWholeEdgeStartPts = { source: { x: p1.x, y: p1.y }, target: { x: p2.x, y: p2.y } };
                    } else {
                        inst.dragWholeEdgeStartPts = null;
                    }
                    inst.container.style.cursor = 'grabbing';
                    return;
                }
            }
        }

        // Port clicked? -> start edge drawing (skip if node is locked). The port may sit
        // UNDER an edge hit path (edges paint over nodes and the hit path becomes the event
        // target), so when the raw target is not a port, resolve the element stack under
        // the pointer (phase 8, _firstWithClass). Occlusion: the stack is only consulted when
        // the raw target is the port itself or an edge corridor that paints over it — a press on
        // another control (a resize/rotate handle hit, a node body, …) is that control's
        // gesture, and a port buried deeper in the stack under it is COVERED, not pressed.
        var rawHit = e.target && e.target.closest ? e.target : null;
        var stackAllowed = !!rawHit && (!!(rawHit.closest('.tm-diagram-port, .tm-diagram-port-hit, .tm-diagram-connection-point'))
            || !!(rawHit.closest('.tm-diagram-edge-hit-path, .tm-diagram-edge-label-group')));
        var portEl = stackAllowed ? this._firstWithClass(e.target, pointerStack, 'tm-diagram-port') : null;
        // A `.tm-diagram-port-hit` circle may stand in for a CONNECTION POINT (data-cp-*): only a
        // real port carries data-port-id — let the connection-point branch handle the rest.
        if (portEl && portEl.getAttribute('data-port-id') && !inst.readOnly) {
            const nodeEl = portEl.closest('[data-node-id]');
            if (nodeEl && nodeEl.getAttribute('data-locked') === 'true') return;
            e.preventDefault();
            e.stopPropagation();
            if (nodeEl) {
                this._startEdgeDraw(inst, nodeEl.getAttribute('data-node-id'), portEl.getAttribute('data-port-id'), e.clientX, e.clientY);
            }
            return;
        }

        // Connection point clicked? -> start edge drawing with fixed constraint
        const cpEl = stackAllowed ? this._firstWithClass(e.target, pointerStack, 'tm-diagram-connection-point') : null;
        if (cpEl && !inst.readOnly) {
            const nodeEl = cpEl.closest('[data-node-id]');
            if (nodeEl && nodeEl.getAttribute('data-locked') === 'true') return;
            e.preventDefault();
            e.stopPropagation();
            if (nodeEl) {
                const rx = parseFloat(cpEl.getAttribute('data-cp-rx'));
                const ry = parseFloat(cpEl.getAttribute('data-cp-ry'));
                const perimeter = cpEl.getAttribute('data-cp-perimeter') === 'true';
                this._startEdgeDraw(inst, nodeEl.getAttribute('data-node-id'), null, e.clientX, e.clientY, null, 0.5, rx, ry, perimeter);
            }
            return;
        }

        // Resize / rotate / connect clicked? -> let Blazor handle it (do NOT stopPropagation,
        // otherwise Blazor's document-level event delegation never receives the event). The -hit
        // rects (phase 6) carry the gesture for every pointer type.
        if (e.target.closest('.tm-diagram-resize-handle, .tm-diagram-rotate-handle, .tm-diagram-resize-handle-hit, .tm-diagram-rotate-handle-hit, .tm-diagram-connect-arrow')) {
            return;
        }

        // Link clicked? (on a node with data-link)
        const linkEl = e.target.closest('[data-link]');
        if (linkEl && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
            const link = linkEl.getAttribute('data-link');
            const nodeId = linkEl.getAttribute('data-node-id');
            if (link && nodeId && inst.dotNetRef) {
                e.preventDefault();
                e.stopPropagation();
                inst.dotNetRef.invokeMethodAsync('OnNodeLinkClicked', nodeId, link);
                return;
            }
        }

        // Node clicked?
        const nodeEl = e.target.closest('[data-node-id]');
        if (nodeEl && this._isNodeInActiveGroup(inst, nodeEl)) {
            e.preventDefault();
            const id = nodeEl.getAttribute('data-node-id');
            const isLocked = nodeEl.getAttribute('data-locked') === 'true';

            // ── Drill-in for table cells (draw.io pattern) ───────────────────
            // If the mousedown landed on a <td> of an ALREADY-selected table and
            // we're not in a special tool mode, enter a "pending cell click"
            // state. On mouseup without meaningful movement this becomes a cell
            // sub-select; on meaningful movement it escalates to a normal node
            // drag so large tables remain movable.
            if (!isRightClick && !inst.readOnly && inst.toolMode !== 'edge' && inst.selectedIds.has(id)) {
                const cellEl = e.target.closest('.tm-diagram-node__table-cell');
                if (cellEl) {
                    const row = parseInt(cellEl.getAttribute('data-row'), 10);
                    const col = parseInt(cellEl.getAttribute('data-col'), 10);
                    if (!isNaN(row) && !isNaN(col)) {
                        inst.pendingTableCellClick = {
                            nodeId: id,
                            row: row,
                            col: col,
                            ctrlKey: !!(e.ctrlKey || e.metaKey),
                            startScreenX: e.clientX,
                            startScreenY: e.clientY
                        };
                        return;
                    }
                }
            }

            if (e.ctrlKey || e.metaKey) {
                if (inst.selectedIds.has(id)) inst.selectedIds.delete(id);
                else inst.selectedIds.add(id);
                this._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
                return;
            }

            if (inst.toolMode === 'edge' && !inst.readOnly) {
                const rect = this._nodeRect(inst, id);
                if (rect) {
                    const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
                    const dx = docPt.x - (rect.x + rect.w / 2);
                    const dy = docPt.y - (rect.y + rect.h / 2);
                    const adx = Math.abs(dx);
                    const ady = Math.abs(dy);
                    let side = null;
                    let offset = 0.5;
                    if (rect.w > 0 && rect.h > 0 && (adx > 0 || ady > 0)) {
                        if (adx / rect.w > ady / rect.h) {
                            side = dx > 0 ? 'right' : 'left';
                            offset = Math.max(0, Math.min(1, (docPt.y - rect.y) / rect.h));
                        } else {
                            side = dy > 0 ? 'bottom' : 'top';
                            offset = Math.max(0, Math.min(1, (docPt.x - rect.x) / rect.w));
                        }
                    }
                    this._startEdgeDraw(inst, id, null, e.clientX, e.clientY, side, offset);
                }
                e.preventDefault();
                return;
            }

            if (!inst.selectedIds.has(id)) {
                inst.selectedIds = new Set([id]);
                this._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
            }

            if (isLocked || inst.readOnly) return;
            if (isRightClick) return;

            inst.isDragging = true;
            inst.dragNodeIds = [...inst.selectedIds];

            // Include children of selected nodes
            const allIds = new Set(inst.dragNodeIds);
            inst.dragNodeIds.forEach(nid => {
                if (inst.scenePane) {
                    inst.scenePane.querySelectorAll('[data-parent-id="' + nid + '"]').forEach(childEl => {
                        const cid = childEl.getAttribute('data-node-id');
                        if (cid && this._isNodeInActiveGroup(inst, childEl)) allIds.add(cid);
                    });
                }
            });
            inst.dragNodeIds = [...allIds];

            // Include group nodes
            inst.dragNodeIds = this._includeGroupNodes(inst, inst.dragNodeIds);

            inst.dragStartScreen = { x: e.clientX, y: e.clientY };
            inst.dragStartPositions = {};
            inst.dragNodeIds.forEach(nid => {
                const r = this._nodeRect(inst, nid);
                if (r) inst.dragStartPositions[nid] = { x: r.x, y: r.y, w: r.w, h: r.h };
            });
            inst.dotNetRef.invokeMethodAsync('OnDragStarted', inst.dragNodeIds);
            inst.container.style.cursor = 'grabbing';
            return;
        }

        // Edge tool on empty canvas: start a *pending* free-line draw. The
        // actual `_startEdgeDraw` is deferred until the pointer moves past
        // DRAG_ESCALATE_PX (see _onMouseMove) so a simple click on empty
        // canvas still behaves as "cancel / back to select" like before.
        if (inst.toolMode === 'edge' && !isRightClick) {
            e.preventDefault();
            const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
            inst.isPendingEdgeDraw = true;
            inst.pendingEdgeStart = {
                clientX: e.clientX,
                clientY: e.clientY,
                docX: docPt.x,
                docY: docPt.y
            };
            return;
        }
        if (inst.toolMode === 'edge') {
            // Right-click in edge mode: revert to select.
            this._applyToolMode(inst, 'select');
            if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'select');
            return;
        }

        // Empty canvas: rubber-band selection (or clear selection on right-click)
        if (isRightClick) {
            if (inst.selectedIds.size > 0) {
                inst.selectedIds.clear();
                this._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
            }
            return;
        }
        e.preventDefault();
        const rect = inst.container.getBoundingClientRect();
        const screenPt = { x: e.clientX - rect.left, y: e.clientY - rect.top };
        inst.isRubberBand = true;
        inst.rubberStartScreen = screenPt;
        inst.rubberEl = this._createRubberEl(inst, screenPt.x, screenPt.y);

        if (!e.shiftKey && inst.selectedIds.size > 0) {
            inst.selectedIds.clear();
            this._updateSelection(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
        }
    },

    // ── Mouse move ───────────────────────────────────────────────────────────

    _onMouseMove: function (e, inst) {
        // Escalate a pending table-cell click into a node drag once the pointer
        // has moved past a small threshold (so a tiny jitter does not cancel
        // the sub-select while a deliberate drag still moves the whole table).
        if (inst.pendingTableCellClick) {
            const p = inst.pendingTableCellClick;
            const dx = Math.abs(e.clientX - p.startScreenX);
            const dy = Math.abs(e.clientY - p.startScreenY);
            const DRAG_ESCALATE_PX = 4;
            if (dx > DRAG_ESCALATE_PX || dy > DRAG_ESCALATE_PX) {
                inst.pendingTableCellClick = null;
                this._beginNodeDragFromCell(inst, p.nodeId, p.startScreenX, p.startScreenY);
                // fall through so the current delta is also applied this frame
            } else {
                return;
            }
        }

        // Escalate a pending free-line draw into an actual edge draw once the
        // pointer has moved past DRAG_ESCALATE_PX. This keeps a plain click
        // on empty canvas (no movement) as a no-op (and mouseup will then
        // revert to select mode).
        if (inst.isPendingEdgeDraw && inst.pendingEdgeStart) {
            const p = inst.pendingEdgeStart;
            const dx = Math.abs(e.clientX - p.clientX);
            const dy = Math.abs(e.clientY - p.clientY);
            const DRAG_ESCALATE_PX = 4;
            if (dx > DRAG_ESCALATE_PX || dy > DRAG_ESCALATE_PX) {
                inst.isPendingEdgeDraw = false;
                inst.pendingEdgeStart = null;
                // Start the draw anchored at the original mousedown point so
                // the temp path does not visually "jump" from the cursor.
                this._startEdgeDraw(inst, null, null, p.clientX, p.clientY);
                this._updateEdgeDraw(inst, e.clientX, e.clientY);
                return;
            }
        }

        if (inst.isDrawingEdge) {
            // Remember the latest cursor position so Enter can commit at it.
            inst.lastPointerClient = { x: e.clientX, y: e.clientY };
            this._updateEdgeDraw(inst, e.clientX, e.clientY);
            return;
        }

        if (inst.isDraggingWaypoint) {
            let pt = this._screenToDoc(inst, e.clientX, e.clientY);
            if (inst.gridSize > 0 && !e.altKey) {
                pt = {
                    x: Math.round(pt.x / inst.gridSize) * inst.gridSize,
                    y: Math.round(pt.y / inst.gridSize) * inst.gridSize
                };
            }
            const guides = this._computeSnapGuidesForPoint(inst, pt.x, pt.y);
            if (guides.x) pt.x += guides.x.delta;
            if (guides.y) pt.y += guides.y.delta;
            this._drawGuideLines(inst, guides);
            this._updateWaypointVisuals(inst, inst.dragWaypointEdgeId, inst.dragWaypointIndex, pt.x, pt.y);
            return;
        }

        if (inst.isDraggingSegment) {
            const dxScreen = e.clientX - inst.dragSegmentStartScreen.x;
            const dyScreen = e.clientY - inst.dragSegmentStartScreen.y;
            const dxDoc = dxScreen / inst.scale;
            const dyDoc = dyScreen / inst.scale;

            const positions = inst.dragSegmentHandlePositions.map(function (p) { return { x: p.x, y: p.y }; });
            const idx = inst.dragSegmentIndex;

            if (inst.dragSegmentIsVertical) {
                positions[idx].x += dxDoc;
                positions[idx + 1].x += dxDoc;
            } else if (inst.dragSegmentIsHorizontal) {
                positions[idx].y += dyDoc;
                positions[idx + 1].y += dyDoc;
            }

            const edgeId = inst.dragSegmentEdgeId;
            const handles = Array.from(inst.svg.querySelectorAll('circle.tm-diagram-edge-handle[data-edge-id="' + edgeId + '"]'))
                .sort(function (a, b) {
                    return parseInt(a.getAttribute('data-handle-index'), 10) - parseInt(b.getAttribute('data-handle-index'), 10);
                });
            for (let i = 0; i < handles.length && i < positions.length; i++) {
                handles[i].setAttribute('cx', positions[i].x);
                handles[i].setAttribute('cy', positions[i].y);
            }

            // Also move segment handles
            const segHandles = Array.from(inst.svg.querySelectorAll('circle.tm-diagram-edge-segment-handle[data-edge-id="' + edgeId + '"]'))
                .sort(function (a, b) {
                    return parseInt(a.getAttribute('data-segment-index'), 10) - parseInt(b.getAttribute('data-segment-index'), 10);
                });
            for (let i = 0; i < segHandles.length && i < positions.length - 1; i++) {
                const mx = (positions[i].x + positions[i + 1].x) / 2;
                const my = (positions[i].y + positions[i + 1].y) / 2;
                segHandles[i].setAttribute('cx', mx);
                segHandles[i].setAttribute('cy', my);
            }

            let d = 'M ' + positions[0].x + ' ' + positions[0].y;
            for (let i = 1; i < positions.length; i++) {
                d += ' L ' + positions[i].x + ' ' + positions[i].y;
            }
            const hitPath = inst.svg.querySelector('path.tm-diagram-edge-hit-path[data-edge-id="' + edgeId + '"]');
            const visPath = inst.svg.querySelector('path.tm-diagram-edge-path[data-edge-id="' + edgeId + '"]');
            if (hitPath) hitPath.setAttribute('d', d);
            if (visPath) visPath.setAttribute('d', d);

            return;
        }

        if (inst.isDraggingDangling) {
            let pt = this._screenToDoc(inst, e.clientX, e.clientY);
            if (inst.gridSize > 0 && !e.altKey) {
                pt = {
                    x: Math.round(pt.x / inst.gridSize) * inst.gridSize,
                    y: Math.round(pt.y / inst.gridSize) * inst.gridSize
                };
            }
            const guides = this._computeSnapGuidesForPoint(inst, pt.x, pt.y);
            if (guides.x) pt.x += guides.x.delta;
            if (guides.y) pt.y += guides.y.delta;
            this._drawGuideLines(inst, guides);
            this._updateDanglingVisuals(inst, inst.dragDanglingEdgeId, inst.dragDanglingType, pt.x, pt.y);

            // Detect hover over node for reconnect (use elementsFromPoint because the dangling SVG handle blocks elementFromPoint)
            const els = document.elementsFromPoint(e.clientX, e.clientY);
            let nodeEl = null;
            for (let i = 0; i < els.length; i++) {
                const candidate = els[i].closest('[data-node-id]');
                if (candidate) {
                    nodeEl = candidate;
                    break;
                }
            }
            if (nodeEl) {
                const hoverNodeId = nodeEl.getAttribute('data-node-id');
                if (inst.dragDanglingHoverNodeId !== hoverNodeId) {
                    // Clear previous highlight and timer
                    if (inst.dragDanglingHoverNodeId) {
                        const prev = inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + inst.dragDanglingHoverNodeId + '"]');
                        if (prev) {
                            prev.classList.remove('tm-diagram-node--drop-target');
                            prev.classList.remove('tm-diagram-node--outline-connect');
                        }
                    }
                    if (inst.danglingOutlineTimer) {
                        clearTimeout(inst.danglingOutlineTimer);
                        inst.danglingOutlineTimer = null;
                    }
                    inst.danglingOutlineConnect = false;
                    inst.dragDanglingHoverNodeId = hoverNodeId;
                    nodeEl.classList.add('tm-diagram-node--drop-target');
                    // Start 2s timer for outline connect
                    inst.danglingOutlineTimer = setTimeout(function () {
                        if (inst.dragDanglingHoverNodeId === hoverNodeId) {
                            const stillNode = inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + hoverNodeId + '"]');
                            if (stillNode) stillNode.classList.add('tm-diagram-node--outline-connect');
                            inst.danglingOutlineConnect = true;
                        }
                    }, 2000);
                }
            } else if (inst.dragDanglingHoverNodeId) {
                const prev = inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + inst.dragDanglingHoverNodeId + '"]');
                if (prev) {
                    prev.classList.remove('tm-diagram-node--drop-target');
                    prev.classList.remove('tm-diagram-node--outline-connect');
                }
                if (inst.danglingOutlineTimer) {
                    clearTimeout(inst.danglingOutlineTimer);
                    inst.danglingOutlineTimer = null;
                }
                inst.danglingOutlineConnect = false;
                inst.dragDanglingHoverNodeId = null;
            }
            return;
        }

        if (inst.isDraggingJetty && inst.dragJettyEdgeId) {
            const pt = this._screenToDoc(inst, e.clientX, e.clientY);
            const nodeRect = this._nodeRect(inst, inst.dragJettyNodeId);
            if (nodeRect) {
                const side = inst.dragJettySide;
                let newSpacing = 0;
                if (side === 'left') newSpacing = nodeRect.x - pt.x;
                else if (side === 'right') newSpacing = pt.x - (nodeRect.x + nodeRect.w);
                else if (side === 'top') newSpacing = nodeRect.y - pt.y;
                else if (side === 'bottom') newSpacing = pt.y - (nodeRect.y + nodeRect.h);
                newSpacing = Math.max(0, Math.round(newSpacing));
                const sourceSpacing = inst.dragJettyType === 'source' ? newSpacing : null;
                const targetSpacing = inst.dragJettyType === 'target' ? newSpacing : null;
                inst.dotNetRef.invokeMethodAsync('OnEdgeSpacingChanged', inst.dragJettyEdgeId, sourceSpacing, targetSpacing);
            }
            return;
        }

        if (inst.isDraggingEdgeLabel && inst.dragEdgeLabelId) {
            const edgeId = inst.dragEdgeLabelId;
            const g = inst.svg.querySelector('g.tm-diagram-edge-label-group[data-edge-id="' + edgeId + '"]');
            if (g) {
                const dxScreen = e.clientX - inst.dragEdgeLabelStart.x;
                const dyScreen = e.clientY - inst.dragEdgeLabelStart.y;
                const dxDoc = dxScreen / inst.scale;
                const dyDoc = dyScreen / inst.scale;
                const newOx = (inst.dragEdgeLabelStartOx || 0) + dxDoc;
                const newOy = (inst.dragEdgeLabelStartOy || 0) + dyDoc;
                g.setAttribute('transform', 'translate(' + newOx + ' ' + newOy + ')');
            }
            return;
        }

        if (inst.isDraggingWholeEdge && inst.dragWholeEdgeId) {
            e.preventDefault();
            e.stopPropagation();

            let pt = this._screenToDoc(inst, e.clientX, e.clientY);
            if (inst.gridSize > 0 && !e.altKey) {
                pt = {
                    x: Math.round(pt.x / inst.gridSize) * inst.gridSize,
                    y: Math.round(pt.y / inst.gridSize) * inst.gridSize
                };
            }
            // Compute delta
            let dx = pt.x - inst.dragWholeEdgeStartDoc.x;
            let dy = pt.y - inst.dragWholeEdgeStartDoc.y;
            // Snap guidelines using bounding box of both endpoints (like node-drag snapping)
            if (inst.dragWholeEdgeStartPts) {
                const sPt = { x: inst.dragWholeEdgeStartPts.source.x + dx, y: inst.dragWholeEdgeStartPts.source.y + dy };
                const tPt = { x: inst.dragWholeEdgeStartPts.target.x + dx, y: inst.dragWholeEdgeStartPts.target.y + dy };
                const minX = Math.min(sPt.x, tPt.x);
                const minY = Math.min(sPt.y, tPt.y);
                const maxX = Math.max(sPt.x, tPt.x);
                const maxY = Math.max(sPt.y, tPt.y);
                const fakeId = '__edge__';
                const fakeStart = { x: minX, y: minY, w: Math.max(1, maxX - minX), h: Math.max(1, maxY - minY) };
                const guides = this._computeSnapGuides(inst, [fakeId], { [fakeId]: fakeStart }, 0, 0);
                if (guides.x) { dx += guides.x.delta; pt.x += guides.x.delta; }
                if (guides.y) { dy += guides.y.delta; pt.y += guides.y.delta; }
                this._drawGuideLines(inst, guides);
            } else {
                this._drawGuideLines(inst, { x: null, y: null });
            }
            inst.dragWholeEdgeDeltaDoc = { x: dx, y: dy };
            const group = inst.svg.querySelector('g.tm-diagram-edge-group[data-edge-id="' + inst.dragWholeEdgeId + '"]');
            if (group) {
                group.setAttribute('transform', 'translate(' + dx + ' ' + dy + ')');
            }
            return;
        }

        if (inst.isDragging && inst.dragNodeIds.length > 0) {
            e.preventDefault();
            e.stopPropagation();
            const dxScreen = e.clientX - inst.dragStartScreen.x;
            const dyScreen = e.clientY - inst.dragStartScreen.y;

            let dxDoc = dxScreen / inst.scale;
            let dyDoc = dyScreen / inst.scale;

            const guides = this._computeSnapGuides(inst, inst.dragNodeIds, inst.dragStartPositions, dxDoc, dyDoc);
            if (guides.x !== null) dxDoc += guides.x.delta;
            if (guides.y !== null) dyDoc += guides.y.delta;
            this._drawGuideLines(inst, guides);

            inst.dragNodeIds.forEach(id => {
                const start = inst.dragStartPositions[id];
                if (!start) return;
                let nx = start.x + dxDoc;
                let ny = start.y + dyDoc;
                const clamped = this._clampChildPosition(inst, id, nx, ny);
                this._setNodeTranslate(inst, id, clamped.x, clamped.y);
            });
            this._updateSelectionTransforms(inst);
            return;
        }

        if (inst.isRubberBand && inst.rubberEl) {
            const rect = inst.container.getBoundingClientRect();
            const cur = { x: e.clientX - rect.left, y: e.clientY - rect.top };
            const rx = Math.min(cur.x, inst.rubberStartScreen.x);
            const ry = Math.min(cur.y, inst.rubberStartScreen.y);
            const rw = Math.abs(cur.x - inst.rubberStartScreen.x);
            const rh = Math.abs(cur.y - inst.rubberStartScreen.y);
            inst.rubberEl.style.left = rx + 'px';
            inst.rubberEl.style.top = ry + 'px';
            inst.rubberEl.style.width = rw + 'px';
            inst.rubberEl.style.height = rh + 'px';
            return;
        }

        if (inst.isPanning && inst.panStartScreen) {
            const vb = inst.viewBoxStart;
            const dxScreen = e.clientX - inst.panStartScreen.x;
            const dyScreen = e.clientY - inst.panStartScreen.y;
            const dxDoc = dxScreen / inst.scale;
            const dyDoc = dyScreen / inst.scale;
            this._setViewBox(inst, vb.x - dxDoc, vb.y - dyDoc, vb.w, vb.h);
        }

        // Ruler cursor (skip during drag/pan/rubber-band to avoid Blazor re-render fighting with JS direct DOM updates)
        if (inst.dotNetRef && !inst.isDragging && !inst.isPanning && !inst.isRubberBand && !inst.isDrawingEdge && !inst.isDraggingWaypoint && !inst.isDraggingJetty && !inst.isDraggingEdgeLabel && !inst.isDraggingWholeEdge) {
            const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
            inst.dotNetRef.invokeMethodAsync('OnRulerCursorMoved', docPt.x, docPt.y);
        }

        // Connection hover icons — disabled, ports now appear on hover via CSS
        // if (!inst.readOnly && !inst.isDrawingEdge && !inst.isDragging && !inst.isDraggingWaypoint && !inst.isDraggingJetty && !inst.isDraggingEdgeLabel && !inst.isPanning && !inst.isRubberBand) {
        //     this._updateConnectHoverIcons(inst, e.clientX, e.clientY);
        // }
    },

    // ── Drill-in drag escalation ─────────────────────────────────────────────

    /// Initialises a normal node drag starting from the screen position captured
    /// when the user first pressed mouse down on a cell of an already-selected
    /// table. Mirrors the drag-setup block from _onMouseDown.
    _beginNodeDragFromCell: function (inst, nodeId, startScreenX, startScreenY) {
        if (inst.readOnly) return;
        const nodeEl = inst.scenePane ? inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + nodeId + '"]') : null;
        if (nodeEl && nodeEl.getAttribute('data-locked') === 'true') return;
        if (!inst.selectedIds.has(nodeId)) return;

        inst.isDragging = true;
        inst.dragNodeIds = [...inst.selectedIds];

        const allIds = new Set(inst.dragNodeIds);
        inst.dragNodeIds.forEach(nid => {
            if (inst.scenePane) {
                inst.scenePane.querySelectorAll('[data-parent-id="' + nid + '"]').forEach(childEl => {
                    const cid = childEl.getAttribute('data-node-id');
                    if (cid && this._isNodeInActiveGroup(inst, childEl)) allIds.add(cid);
                });
            }
        });
        inst.dragNodeIds = [...allIds];
        inst.dragNodeIds = this._includeGroupNodes(inst, inst.dragNodeIds);

        inst.dragStartScreen = { x: startScreenX, y: startScreenY };
        inst.dragStartPositions = {};
        inst.dragNodeIds.forEach(nid => {
            const r = this._nodeRect(inst, nid);
            if (r) inst.dragStartPositions[nid] = { x: r.x, y: r.y, w: r.w, h: r.h };
        });
        inst.dotNetRef.invokeMethodAsync('OnDragStarted', inst.dragNodeIds);
        inst.container.style.cursor = 'grabbing';
    },

    // ── Double-click on table cells (start edit) ─────────────────────────────

    _onDblClick: function (e, inst) {
        if (inst.readOnly) return;

        // Finish a polyline draft on double-click over empty canvas. Both
        // mousedowns of the dblclick pair already appended a waypoint at ~
        // this position — pop up to two colocated waypoints so the commit
        // does not leave redundant zero-length segments, then commit with
        // the cursor as a floating target terminal.
        if (inst.isDrawingPolyline) {
            e.preventDefault();
            e.stopPropagation();
            const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
            const threshDoc = 12 / (inst.scale || 1);
            let popped = 0;
            while (inst.polylinePoints.length > 0 && popped < 2) {
                const last = inst.polylinePoints[inst.polylinePoints.length - 1];
                if (Math.abs(last.x - docPt.x) < threshDoc && Math.abs(last.y - docPt.y) < threshDoc) {
                    inst.polylinePoints.pop();
                    popped++;
                } else {
                    break;
                }
            }
            this._commitPolyline(inst, { kind: 'floating', x: docPt.x, y: docPt.y });
            return;
        }

        // Swallow the native dblclick that immediately follows a polyline
        // commit — otherwise the table-cell / edge-label dbl-click handlers
        // on the same element would fire and e.g. open an inline editor.
        if (inst.polylineCommittedAt && (Date.now() - inst.polylineCommittedAt) < 500) {
            e.preventDefault();
            e.stopPropagation();
            return;
        }
        // Don't fight with an already-active inline editor.
        const tag = e.target && e.target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA') return;

        const cellEl = e.target.closest ? e.target.closest('.tm-diagram-node__table-cell') : null;
        if (!cellEl) return;

        const nodeEl = cellEl.closest('[data-node-id]');
        if (!nodeEl || !this._isNodeInActiveGroup(inst, nodeEl)) return;
        if (nodeEl.getAttribute('data-locked') === 'true') return;

        const nodeId = nodeEl.getAttribute('data-node-id');
        const row = parseInt(cellEl.getAttribute('data-row'), 10);
        const col = parseInt(cellEl.getAttribute('data-col'), 10);
        if (isNaN(row) || isNaN(col)) return;

        e.preventDefault();
        e.stopPropagation();

        const ref = window.tmDiagramStencilShape && window.tmDiagramStencilShape.getRef(nodeId);
        if (!ref) return;
        ref.invokeMethodAsync('StartTableCellEditFromJs', row, col);
    },

    // ── Mouse up ─────────────────────────────────────────────────────────────

    // The edge-draw commit shared by the mouse path (_onMouseUp) and the touch path
    // (touchEndEdgeDraw, J12a). One commit path, one JsOnEdgeCreated shape.
    _commitEdgeDraw: function (inst, clientX, clientY) {
        // When the source is floating (free-line from empty canvas) pass
        // the doc-space source point so the backend can create an edge
        // with SourcePoint instead of a source node/port. Floating
        // coordinates are grid-snapped when `gridSize > 0` so free
        // endpoints line up with the grid just like node positions do.
        const floatingSource = !inst.drawSource.nodeId;
        const srcPtX = floatingSource ? this._snap(inst, inst.drawSource.x) : null;
        const srcPtY = floatingSource ? this._snap(inst, inst.drawSource.y) : null;

        // Edge-to-edge connection takes priority
        if (inst.drawHoverEdgeId && inst.drawHoverEdgeT !== null) {
            inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                inst.drawSource.nodeId, inst.drawSource.portId,
                null, null,
                inst.drawSource.side, inst.drawSource.offset, null, 0.5,
                inst.drawHoverEdgeId, inst.drawHoverEdgeT,
                inst.drawSource.constraintRx, inst.drawSource.constraintRy, inst.drawSource.constraintPerimeter,
                null, null, null,
                null, null,
                srcPtX, srcPtY,
                null);
            this._cancelEdgeDraw(inst);
            return;
        }
        // Use elementFromPoint because e.target may hit SVG grid/path instead of HTML port overlay
        const hitEl = document.elementFromPoint(clientX, clientY);
        let portEl = hitEl ? hitEl.closest('.tm-diagram-port') : null;
        // Phase 3.2 smart-snap to port — if the cursor isn't directly on
        // a port but _is_ over a node, pull the target onto the closest
        // port within the same threshold used by `_updateEdgeDraw`
        // (15 / scale in doc units). This mirrors the preview behavior
        // so what the user sees during drag is what they get on release.
        let smartSnapNodeEl = null;
        if (!portEl) {
            const nodeUnderCursor = hitEl ? hitEl.closest('[data-node-id]') : null;
            if (nodeUnderCursor) {
                const nId = nodeUnderCursor.getAttribute('data-node-id');
                const upDoc = this._screenToDoc(inst, clientX, clientY);
                const nearest = this._findNearestPortOnNode(inst, nId, upDoc.x, upDoc.y, 15 / Math.max(inst.scale, 0.01));
                if (nearest && nearest.portEl) {
                    portEl = nearest.portEl;
                    smartSnapNodeEl = nodeUnderCursor;
                }
            }
        }
        if (portEl) {
            const nodeEl = portEl.closest('[data-node-id]') || smartSnapNodeEl;
            if (nodeEl) {
                const targetNodeId = nodeEl.getAttribute('data-node-id');
                const targetPortId = portEl.getAttribute('data-port-id');
                console.log('[EdgeDraw] Port connect -> nodeId=' + targetNodeId + ' portId=' + targetPortId);
                // Allow self-connections (loop edges)
                inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                    inst.drawSource.nodeId, inst.drawSource.portId,
                    targetNodeId, targetPortId,
                    inst.drawSource.side, inst.drawSource.offset, null, 0.5,
                    null, 0.5,
                    inst.drawSource.constraintRx, inst.drawSource.constraintRy, inst.drawSource.constraintPerimeter,
                    null, null, null,
                    null, null,
                    srcPtX, srcPtY,
                    null);
                this._cancelEdgeDraw(inst);
                return;
            }
        }
        const cpEl = hitEl ? hitEl.closest('.tm-diagram-connection-point') : null;
        if (cpEl) {
            const nodeEl = cpEl.closest('[data-node-id]');
            if (nodeEl) {
                const targetNodeId = nodeEl.getAttribute('data-node-id');
                const targetRx = parseFloat(cpEl.getAttribute('data-cp-rx'));
                const targetRy = parseFloat(cpEl.getAttribute('data-cp-ry'));
                const targetPerimeter = cpEl.getAttribute('data-cp-perimeter') === 'true';

                inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                    inst.drawSource.nodeId, inst.drawSource.portId,
                    targetNodeId, null,
                    inst.drawSource.side, inst.drawSource.offset, null, 0.5,
                    null, 0.5,
                    inst.drawSource.constraintRx, inst.drawSource.constraintRy, inst.drawSource.constraintPerimeter,
                    targetRx, targetRy, targetPerimeter,
                    null, null,
                    srcPtX, srcPtY,
                    null);
                this._cancelEdgeDraw(inst);
                return;
            }
        }
        // Dangling edge: drop on empty canvas
        const docPt = this._screenToDoc(inst, clientX, clientY);
        const tgtX = this._snap(inst, docPt.x);
        const tgtY = this._snap(inst, docPt.y);
        console.log('[EdgeDraw] Dangling -> pt=' + tgtX + ',' + tgtY);
        inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
            inst.drawSource.nodeId, inst.drawSource.portId,
            null, null,
            inst.drawSource.side, inst.drawSource.offset, null, 0.5,
            null, 0.5,
            inst.drawSource.constraintRx, inst.drawSource.constraintRy, inst.drawSource.constraintPerimeter,
            null, null, null,
            tgtX, tgtY,
            srcPtX, srcPtY,
            null);
        this._cancelEdgeDraw(inst);
    },

    _onMouseUp: function (e, inst) {
        // Pending free-line draw that never escalated (user just *clicked*
        // on empty canvas in edge mode — no drag). In phase 2 this promotes
        // to a click-to-click polyline draw anchored at that point. The
        // gesture is ended by ESC, right-click, Enter, double-click on
        // empty canvas, or a mousedown on a node / port / edge.
        if (inst.isPendingEdgeDraw) {
            const p = inst.pendingEdgeStart;
            inst.isPendingEdgeDraw = false;
            inst.pendingEdgeStart = null;
            if (p && inst.toolMode === 'edge') {
                this._startPolylineDraw(inst, p.clientX, p.clientY);
            }
            return;
        }

        // Mouseup while already drawing a polyline is a no-op: the polyline
        // gesture is driven by mousedown (add waypoint / commit on node) and
        // keyboard (Enter / ESC) rather than by mouseup.
        if (inst.isDrawingPolyline) {
            return;
        }

        // Pending table-cell click that never escalated into a drag → fire the
        // cell sub-select up to Blazor. Ctrl held = multi-select toggle.
        if (inst.pendingTableCellClick) {
            const p = inst.pendingTableCellClick;
            inst.pendingTableCellClick = null;
            if (inst.dotNetRef) {
                inst.dotNetRef.invokeMethodAsync('OnTableCellLeftClick', p.nodeId, p.row, p.col, !!p.ctrlKey);
            }
            return;
        }

        if (inst.isDrawingEdge) {
            this._commitEdgeDraw(inst, e.clientX, e.clientY);
            return;
        }

        if (inst.isDraggingWaypoint) {
            const pt = this._screenToDoc(inst, e.clientX, e.clientY);
            inst.dotNetRef.invokeMethodAsync('OnEdgeWaypointMoved',
                inst.dragWaypointEdgeId, inst.dragWaypointIndex, pt.x, pt.y);
            inst.isDraggingWaypoint = false;
            inst.dragWaypointEdgeId = null;
            inst.dragWaypointIndex = null;
            inst.dragWaypointStartScreen = null;
            inst.dragWaypointStartDoc = null;
            this._clearGuideLines(inst);
            return;
        }

        if (inst.isDraggingSegment) {
            const edgeId = inst.dragSegmentEdgeId;
            const handles = Array.from(inst.svg.querySelectorAll('circle.tm-diagram-edge-handle[data-edge-id="' + edgeId + '"]'))
                .sort(function (a, b) {
                    return parseInt(a.getAttribute('data-handle-index'), 10) - parseInt(b.getAttribute('data-handle-index'), 10);
                });
            const positions = [];
            for (let i = 0; i < handles.length; i++) {
                positions.push({
                    x: parseFloat(handles[i].getAttribute('cx')),
                    y: parseFloat(handles[i].getAttribute('cy'))
                });
            }
            // Extract waypoints (exclude first and last which are terminals)
            const waypoints = [];
            for (let i = 1; i < positions.length - 1; i++) {
                waypoints.push({ x: positions[i].x, y: positions[i].y });
            }
            // Simplify collinear orthogonal waypoints
            const simplified = this._simplifyEdgeWaypoints(waypoints);
            inst.dotNetRef.invokeMethodAsync('OnEdgeSegmentDragged', edgeId, simplified);
            inst.isDraggingSegment = false;
            inst.dragSegmentEdgeId = null;
            inst.dragSegmentIndex = null;
            inst.dragSegmentIsVertical = false;
            inst.dragSegmentIsHorizontal = false;
            inst.dragSegmentStartScreen = null;
            inst.dragSegmentHandlePositions = null;
            inst.container.style.cursor = inst.toolMode === 'pan' ? 'grab' : '';
            return;
        }

        if (inst.isDraggingDangling) {
            if (inst.dragDanglingHoverNodeId) {
                const nodeEl = inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + inst.dragDanglingHoverNodeId + '"]');
                if (nodeEl) {
                    nodeEl.classList.remove('tm-diagram-node--drop-target');
                    nodeEl.classList.remove('tm-diagram-node--outline-connect');
                }
                if (inst.danglingOutlineConnect) {
                    // Outline connect: compute constraint from drop position relative to node bounds
                    const rect = this._nodeRect(inst, inst.dragDanglingHoverNodeId);
                    const pt = this._screenToDoc(inst, e.clientX, e.clientY);
                    let relX = rect.w > 0 ? Math.max(0, Math.min(1, (pt.x - rect.x) / rect.w)) : 0.5;
                    let relY = rect.h > 0 ? Math.max(0, Math.min(1, (pt.y - rect.y) / rect.h)) : 0.5;
                    inst.dotNetRef.invokeMethodAsync('OnEdgeTerminalOutlineConnected', inst.dragDanglingEdgeId, inst.dragDanglingType, inst.dragDanglingHoverNodeId, relX, relY);
                } else {
                    // Find closest port under cursor (elementsFromPoint because dangling SVG handle blocks elementFromPoint)
                    const els = document.elementsFromPoint(e.clientX, e.clientY);
                    let portEl = null;
                    for (let i = 0; i < els.length; i++) {
                        const candidate = els[i].closest('.tm-diagram-port');
                        if (candidate) {
                            portEl = candidate;
                            break;
                        }
                    }
                    const portId = portEl ? portEl.getAttribute('data-port-id') : null;
                    inst.dotNetRef.invokeMethodAsync('OnEdgeTerminalReconnected', inst.dragDanglingEdgeId, inst.dragDanglingType, inst.dragDanglingHoverNodeId, portId);
                }
                inst.dragDanglingHoverNodeId = null;
            } else {
                const pt = this._screenToDoc(inst, e.clientX, e.clientY);
                inst.dotNetRef.invokeMethodAsync('OnDanglingTerminalMoved', inst.dragDanglingEdgeId, inst.dragDanglingType, pt.x, pt.y);
            }
            if (inst.danglingOutlineTimer) {
                clearTimeout(inst.danglingOutlineTimer);
                inst.danglingOutlineTimer = null;
            }
            inst.danglingOutlineConnect = false;
            inst.isDraggingDangling = false;
            inst.dragDanglingEdgeId = null;
            inst.dragDanglingType = null;
            inst.dragDanglingStartDoc = null;
            inst.dragDanglingStartScreen = null;
            this._clearGuideLines(inst);
            return;
        }

        if (inst.isDraggingJetty) {
            inst.isDraggingJetty = false;
            inst.dragJettyEdgeId = null;
            inst.dragJettyType = null;
            inst.dragJettySide = null;
            inst.dragJettyNodeId = null;
            inst.dragJettyStartDoc = null;
            this._clearGuideLines(inst);
            return;
        }

        if (inst.isDraggingEdgeLabel) {
            const edgeId = inst.dragEdgeLabelId;
            if (edgeId) {
                const g = inst.svg.querySelector('g.tm-diagram-edge-label-group[data-edge-id="' + edgeId + '"]');
                const pathEl = inst.svg.querySelector('.tm-diagram-edge-path[data-edge-id="' + edgeId + '"]') || inst.svg.querySelector('path[data-edge-id="' + edgeId + '"]');
                if (g && pathEl) {
                    const dxScreen = e.clientX - inst.dragEdgeLabelStart.x;
                    const dyScreen = e.clientY - inst.dragEdgeLabelStart.y;
                    const dxDoc = dxScreen / inst.scale;
                    const dyDoc = dyScreen / inst.scale;
                    const newOx = (inst.dragEdgeLabelStartOx || 0) + dxDoc;
                    const newOy = (inst.dragEdgeLabelStartOy || 0) + dyDoc;

                    // Compute new T from current absolute label position
                    const len = pathEl.getTotalLength();
                    const startT = inst.dragEdgeLabelStartT || 0.5;
                    const basePt = pathEl.getPointAtLength(startT * len);
                    const labelX = basePt.x + newOx;
                    const labelY = basePt.y + newOy;

                    let bestT = 0;
                    let bestDist = Infinity;
                    const samples = 50;
                    for (let i = 0; i <= samples; i++) {
                        const t = i / samples;
                        const p = pathEl.getPointAtLength(t * len);
                        const dx = p.x - labelX;
                        const dy = p.y - labelY;
                        const d = dx * dx + dy * dy;
                        if (d < bestDist) {
                            bestDist = d;
                            bestT = t;
                        }
                    }
                    const closest = pathEl.getPointAtLength(bestT * len);
                    const finalOx = labelX - closest.x;
                    const finalOy = labelY - closest.y;

                    inst.dotNetRef.invokeMethodAsync('OnEdgeLabelMoved', edgeId, bestT, finalOx, finalOy);
                }
            }
            inst.isDraggingEdgeLabel = false;
            inst.dragEdgeLabelId = null;
            inst.dragEdgeLabelStart = null;
            inst.dragEdgeLabelStartT = null;
            inst.dragEdgeLabelStartOx = null;
            inst.dragEdgeLabelStartOy = null;
            return;
        }

        if (inst.isDraggingWholeEdge) {
            const edgeId = inst.dragWholeEdgeId;
            const dx = inst.dragWholeEdgeDeltaDoc.x;
            const dy = inst.dragWholeEdgeDeltaDoc.y;
            const group = inst.svg.querySelector('g.tm-diagram-edge-group[data-edge-id="' + edgeId + '"]');
            if (group) {
                group.removeAttribute('transform');
            }
            if (edgeId && (dx !== 0 || dy !== 0)) {
                inst.dotNetRef.invokeMethodAsync('OnWholeEdgeDragged', edgeId, dx, dy);
            }
            inst.isDraggingWholeEdge = false;
            inst.dragWholeEdgeId = null;
            inst.dragWholeEdgeStartDoc = null;
            inst.dragWholeEdgeDeltaDoc = { x: 0, y: 0 };
            inst.container.style.cursor = inst.toolMode === 'pan' ? 'grab' : '';
            this._clearGuideLines(inst);
            return;
        }

        if (inst.isDragging) {
            const moves = inst.dragNodeIds.map(id => {
                const r = this._nodeRect(inst, id);
                if (!r) return null;
                const sx = this._snap(inst, r.x);
                const sy = this._snap(inst, r.y);
                this._setNodeTranslate(inst, id, sx, sy);
                return { id, x: sx, y: sy };
            }).filter(Boolean);
            inst.isDragging = false;
            inst.dragNodeIds = [];
            inst.dragStartScreen = null;
            inst.dragStartPositions = {};
            inst.container.style.cursor = inst.toolMode === 'pan' ? 'grab' : '';
            this._clearGuideLines(inst);

            if (moves.length === 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementMoved', moves[0].id, moves[0].x, moves[0].y);
            } else if (moves.length > 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementsMoved', moves);
            }
            return;
        }

        if (inst.isRubberBand) {
            const rubber = inst.rubberEl;
            inst.isRubberBand = false;
            inst.rubberStartScreen = null;
            inst.rubberEl = null;

            if (rubber) {
                const rect = inst.container.getBoundingClientRect();
                const leftScreen = parseFloat(rubber.style.left);
                const topScreen = parseFloat(rubber.style.top);
                const widthScreen = parseFloat(rubber.style.width);
                const heightScreen = parseFloat(rubber.style.height);
                rubber.remove();

                if (widthScreen > 4 && heightScreen > 4) {
                    const docTopLeft = this._screenToDoc(inst, rect.left + leftScreen, rect.top + topScreen);
                    const docBottomRight = this._screenToDoc(inst, rect.left + leftScreen + widthScreen, rect.top + topScreen + heightScreen);
                    const left = Math.min(docTopLeft.x, docBottomRight.x);
                    const top = Math.min(docTopLeft.y, docBottomRight.y);
                    const width = Math.abs(docBottomRight.x - docTopLeft.x);
                    const height = Math.abs(docBottomRight.y - docTopLeft.y);

                    const hits = [];
                    if (inst.scenePane) {
                        inst.scenePane.querySelectorAll('g.tm-diagram-node[data-node-id]').forEach(el => {
                            if (!this._isNodeInActiveGroup(inst, el)) return;
                            const id = el.getAttribute('data-node-id');
                            const r = this._nodeRect(inst, id);
                            if (!r) return;
                            if (r.x >= left && r.y >= top && r.x + r.w <= left + width && r.y + r.h <= top + height) {
                                hits.push(id);
                            }
                        });
                    }
                    // Rubber-band edge selection: sample points along each edge path
                    inst.svg.querySelectorAll('path.tm-diagram-edge-hit-path[data-edge-id]').forEach(function (pathEl) {
                        const edgeId = pathEl.getAttribute('data-edge-id');
                        if (!edgeId) return;
                        var pathLen = pathEl.getTotalLength();
                        if (pathLen === 0) return;
                        var samples = Math.max(3, Math.floor(pathLen / 8));
                        for (var s = 0; s <= samples; s++) {
                            var samplePt = pathEl.getPointAtLength(pathLen * s / samples);
                            if (samplePt.x >= left && samplePt.x <= left + width && samplePt.y >= top && samplePt.y <= top + height) {
                                hits.push(edgeId);
                                break;
                            }
                        }
                    });
                    if (hits.length > 0) {
                        inst.selectedIds = new Set(hits);
                        this._updateSelection(inst);
                        inst.dotNetRef.invokeMethodAsync('OnMultiSelect', hits);
                    }
                }
            }
            return;
        }

        if (inst.isPanning) {
            inst.isPanning = false;
            inst.panStartScreen = null;
            inst.viewBoxStart = null;
            inst.container.style.cursor = (inst.spaceHeld || inst.toolMode === 'pan') ? 'grab' : '';
            const vb = this._getViewBox(inst);
            inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x, vb.y, vb.w, vb.h);
        }
    },

    // ── Wheel zoom ───────────────────────────────────────────────────────────

    _onWheel: function (e, inst) {
        if (e.target.closest('.tm-diagram-node__inline-input, .tm-diagram-node__inline-textarea, .tm-diagram-edge-label-input')) {
            return;
        }
        e.preventDefault();
        const factor = e.deltaY > 0 ? 1.1 : 0.9;
        const vb = this._getViewBox(inst);
        const pt = this._screenToDoc(inst, e.clientX, e.clientY);

        const svgRect = inst.svg.getBoundingClientRect();
        const curScale = svgRect.width / vb.w;
        // Outside the range (a restored viewBox the limits no longer cover): only a tick that
        // moves TOWARD the range applies, capped at the boundary — never a jump past it.
        let newScale = curScale / factor;
        if (curScale < this.MIN_SCALE) {
            if (newScale <= curScale) return;
            newScale = Math.min(newScale, this.MIN_SCALE);
        }
        else if (curScale > this.MAX_SCALE) {
            if (newScale >= curScale) return;
            newScale = Math.max(newScale, this.MAX_SCALE);
        }
        else {
            newScale = this._clampScale(newScale);
            if (Math.abs(newScale - curScale) < 1e-9) return;   // already at the limit
        }
        const f = curScale / newScale;                      // effective factor after the clamp
        const newW = vb.w * f;
        const newH = vb.h * f;
        const newX = pt.x - (pt.x - vb.x) * f;
        const newY = pt.y - (pt.y - vb.y) * f;

        this._setViewBox(inst, newX, newY, newW, newH);
        inst.dotNetRef.invokeMethodAsync('OnZoomChanged', newScale);
        inst.dotNetRef.invokeMethodAsync('OnViewportChanged', newX, newY, newW, newH);
    },

    // ── Touch/pen gesture hooks (diagram-editor-touch.js) ────────────────────
    // The sessions (core pointer-drag.js / pointer-gesture.js) drive the SAME inst state as the
    // mouse path — selection, node drag, pan, rotation — so each gesture keeps one implementation.
    // There is deliberately no second pinch and no second long-press here (decision
    // F7-POINTER-DRAG-PLURALS-RELATIVE-TIME).

    /** R1: read-only inspection — selects a node (mouse click or finger tap) without arming a
     *  drag, resize, rotate or connect. `additive` toggles it (Ctrl/Cmd+click). */
    selectNodeForInspection: function (inst, nodeEl, additive) {
        const id = nodeEl ? nodeEl.getAttribute('data-node-id') : null;
        if (!id || !this._isNodeInActiveGroup(inst, nodeEl)) return false;
        if (additive) {
            if (inst.selectedIds.has(id)) inst.selectedIds.delete(id);
            else inst.selectedIds.add(id);
        } else {
            if (inst.selectedIds.size === 1 && inst.selectedIds.has(id)) return true;
            inst.selectedIds = new Set([id]);
        }
        this._updateSelection(inst);
        inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
        return true;
    },

    /** R1: read-only inspection — a finger tap on an edge selects it (the touch layer prevents
     *  the default, so the Blazor @onclick never fires for taps; the mouse keeps using it). */
    selectEdgeForInspection: function (inst, el) {
        const idEl = el && el.closest ? el.closest('[data-edge-id]') : null;
        const id = (idEl ?? el)?.getAttribute?.('data-edge-id');
        if (!id) return false;
        if (!(inst.selectedIds.size === 1 && inst.selectedIds.has(id))) {
            inst.selectedIds = new Set([id]);
            this._updateSelection(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
        }
        return true;
    },

    /** R1: read-only inspection — a click/tap on the empty surface clears the selection. */
    clearSelectionForInspection: function (inst) {
        if (inst.selectedIds.size === 0) return;
        inst.selectedIds.clear();
        this._updateSelection(inst);
        inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
    },

    touchPressNode: function (inst, nodeEl, clientX, clientY) {
        if (inst.readOnly) return false;
        const id = nodeEl.getAttribute('data-node-id');
        if (!id) return false;
        if (!inst.selectedIds.has(id)) {
            inst.selectedIds = new Set([id]);
            this._updateSelection(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
        }
        inst.container.focus({ preventScroll: true });
        return nodeEl.getAttribute('data-locked') !== 'true';
    },

    touchPressEdge: function (inst, edgeEl) {
        if (inst.readOnly) return false;
        const edgeId = edgeEl.getAttribute('data-edge-id');
        if (!edgeId) return false;
        if (!inst.selectedIds.has(edgeId)) {
            inst.selectedIds = new Set([edgeId]);
            this._updateSelection(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [edgeId]);
        }
        return true;
    },

    touchBeginNodeDrag: function (inst, nodeEl, clientX, clientY) {
        if (inst.readOnly || inst.isDragging) return;
        const id = nodeEl ? nodeEl.getAttribute('data-node-id') : null;
        if (!id || nodeEl.getAttribute('data-locked') === 'true') return;
        if (!inst.selectedIds.has(id)) return;   // the press must have selected it first

        inst.isDragging = true;
        inst.dragNodeIds = [...inst.selectedIds];

        // Include children of selected nodes
        const allIds = new Set(inst.dragNodeIds);
        inst.dragNodeIds.forEach(nid => {
            if (inst.scenePane) {
                inst.scenePane.querySelectorAll('[data-parent-id="' + nid + '"]').forEach(childEl => {
                    const cid = childEl.getAttribute('data-node-id');
                    if (cid && this._isNodeInActiveGroup(inst, childEl)) allIds.add(cid);
                });
            }
        });
        inst.dragNodeIds = [...allIds];

        // Include group nodes
        inst.dragNodeIds = this._includeGroupNodes(inst, inst.dragNodeIds);

        inst.dragStartScreen = { x: clientX, y: clientY };
        inst.dragStartPositions = {};
        inst.dragNodeIds.forEach(nid => {
            const r = this._nodeRect(inst, nid);
            if (r) inst.dragStartPositions[nid] = { x: r.x, y: r.y, w: r.w, h: r.h };
        });
        inst.dotNetRef.invokeMethodAsync('OnDragStarted', inst.dragNodeIds);
    },

    touchMoveNodeDrag: function (inst, clientX, clientY) {
        if (!inst.isDragging || inst.dragNodeIds.length === 0 || !inst.dragStartScreen) return;
        const dxScreen = clientX - inst.dragStartScreen.x;
        const dyScreen = clientY - inst.dragStartScreen.y;
        let dxDoc = dxScreen / inst.scale;
        let dyDoc = dyScreen / inst.scale;

        const guides = this._computeSnapGuides(inst, inst.dragNodeIds, inst.dragStartPositions, dxDoc, dyDoc);
        if (guides.x !== null) dxDoc += guides.x.delta;
        if (guides.y !== null) dyDoc += guides.y.delta;
        this._drawGuideLines(inst, guides);

        inst.dragNodeIds.forEach(id => {
            const start = inst.dragStartPositions[id];
            if (!start) return;
            const nx = start.x + dxDoc;
            const ny = start.y + dyDoc;
            const clamped = this._clampChildPosition(inst, id, nx, ny);
            this._setNodeTranslate(inst, id, clamped.x, clamped.y);
        });
        this._updateSelectionTransforms(inst);
    },

    touchEndNodeDrag: function (inst, clientX, clientY) {
        if (!inst.isDragging) return;
        const moves = inst.dragNodeIds.map(id => {
            const r = this._nodeRect(inst, id);
            if (!r) return null;
            const sx = this._snap(inst, r.x);
            const sy = this._snap(inst, r.y);
            this._setNodeTranslate(inst, id, sx, sy);
            return { id, x: sx, y: sy };
        }).filter(Boolean);

        inst.isDragging = false;
        inst.dragNodeIds = [];
        inst.dragStartScreen = null;
        inst.dragStartPositions = {};
        this._clearGuideLines(inst);

        if (moves.length === 1) {
            inst.dotNetRef.invokeMethodAsync('OnElementMoved', moves[0].id, moves[0].x, moves[0].y);
        } else if (moves.length > 1) {
            inst.dotNetRef.invokeMethodAsync('OnElementsMoved', moves);
        }
    },

    touchCancelNodeDrag: function (inst) { this._cancelNodeDrag(inst); },

    // Shared node-drag rollback (mouse path, touch path, read-only flip, pinch hand-over):
    // every dragged node returns to its start position, the guides drop, and C# learns the
    // drag died so ShouldRender() resumes (OnDragStarted latched it off — J1b).
    _cancelNodeDrag: function (inst) {
        if (!inst.isDragging) return;
        // Roll the gesture back: every dragged node returns to its start position.
        inst.dragNodeIds.forEach(id => {
            const start = inst.dragStartPositions[id];
            if (!start) return;
            this._setNodeTranslate(inst, id, start.x, start.y);
        });
        inst.isDragging = false;
        inst.dragNodeIds = [];
        inst.dragStartScreen = null;
        inst.dragStartPositions = {};
        this._clearGuideLines(inst);
        this._updateSelectionTransforms(inst);
        // C# stopped rendering at OnDragStarted (ShouldRender => !_isJsDragging): release it.
        if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('OnDragCancelled');
    },

    // A touch gesture starting while the context menu is open dismisses it (the finger is
    // clearly doing something else). The _menuOpen flag is kept by openContextMenu/closeContextMenu.
    _closeMenuIfOpen: function (inst) {
        if (inst._menuOpen && inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
    },

    touchBeginPan: function (inst, clientX, clientY) {
        this._closeMenuIfOpen(inst);
        if (inst.isPanning) return;
        inst.isPanning = true;
        inst.panStartScreen = { x: clientX, y: clientY };
        inst.viewBoxStart = this._getViewBox(inst);
    },

    touchMovePan: function (inst, clientX, clientY) {
        if (!inst.isPanning || !inst.panStartScreen || !inst.viewBoxStart) return;
        const vb = inst.viewBoxStart;
        const dxDoc = (clientX - inst.panStartScreen.x) / inst.scale;
        const dyDoc = (clientY - inst.panStartScreen.y) / inst.scale;
        this._setViewBox(inst, vb.x - dxDoc, vb.y - dyDoc, vb.w, vb.h);
    },

    touchEndPan: function (inst) {
        if (!inst.isPanning) return;
        inst.isPanning = false;
        inst.panStartScreen = null;
        inst.viewBoxStart = null;
        const vb = this._getViewBox(inst);
        inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
        inst.dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x, vb.y, vb.w, vb.h);
    },

    touchCancelPan: function (inst) {
        if (!inst.isPanning) return;
        if (inst.viewBoxStart) {
            const vb = inst.viewBoxStart;
            this._setViewBox(inst, vb.x, vb.y, vb.w, vb.h);
        }
        inst.isPanning = false;
        inst.panStartScreen = null;
        inst.viewBoxStart = null;
    },

    // A port / connection point under the finger (the raw target first, then the hit stack —
    // an edge corridor paints over it, see _firstWithClass, J6). The invisible
    // `.tm-diagram-port-hit` circles rendered for the selected node's ports count as ports too:
    // they are the finger-sized press target a coarse pointer actually hits.
    touchPortAt: function (inst, event) {
        if (inst.readOnly || !event) return null;
        const stack = typeof document.elementsFromPoint === 'function' ? document.elementsFromPoint(event.clientX, event.clientY) : [];
        const el = this._firstWithClass(event.target, stack, 'tm-diagram-port')
            || this._firstWithClass(event.target, stack, 'tm-diagram-connection-point');
        if (!el) return null;
        // Occlusion: the port/cp only wins when the raw press target is the port itself (or its
        // finger-sized hit circle) or an edge corridor that paints over it (J6). A press on
        // another control (a resize/rotate handle hit, a node body, …) is that control's gesture
        // — a port buried deeper in the hit stack under it is COVERED, not pressed.
        const raw = event.target;
        const rawIsPort = !!(raw && raw.closest && raw.closest('.tm-diagram-port, .tm-diagram-port-hit, .tm-diagram-connection-point'));
        const rawIsEdge = !!(raw && raw.closest && raw.closest('.tm-diagram-edge-hit-path, .tm-diagram-edge-label-group'));
        if (!rawIsPort && !rawIsEdge) return null;
        const nodeEl = el.closest('[data-node-id]');
        if (!nodeEl || nodeEl.getAttribute('data-locked') === 'true') return null;
        // G2: edge drawing is tablet+ (the editor mirrors its layout on the root).
        const root = inst.container.closest('[data-editor-layout]');
        if (root && root.getAttribute('data-editor-layout') === 'mobile') return null;
        return el;
    },
    touchBeginEdgeDraw: function (inst, el, clientX, clientY) {
        this._closeMenuIfOpen(inst);
        const nodeEl = el.closest('[data-node-id]');
        const nodeId = nodeEl.getAttribute('data-node-id');
        // A `.tm-diagram-port-hit` circle carries EITHER data-port-id (a port) or the data-cp-*
        // attributes (a connection point) — decide by the attribute, not the class.
        if (el.getAttribute('data-port-id')) {
            this._startEdgeDraw(inst, nodeId, el.getAttribute('data-port-id'), clientX, clientY);
        } else {
            this._startEdgeDraw(inst, nodeId, null, clientX, clientY, null, 0.5,
                parseFloat(el.getAttribute('data-cp-rx')), parseFloat(el.getAttribute('data-cp-ry')),
                el.getAttribute('data-cp-perimeter') === 'true');
        }
    },
    touchMoveEdgeDraw: function (inst, clientX, clientY) { this._updateEdgeDraw(inst, clientX, clientY); },
    touchEndEdgeDraw: function (inst, clientX, clientY) { if (inst.isDrawingEdge) this._commitEdgeDraw(inst, clientX, clientY); },
    touchCancelEdgeDraw: function (inst) { if (inst.isDrawingEdge) this._cancelEdgeDraw(inst); },

    // ── Two-finger pinch/pan (pointer-gesture.js reports the geometry) ───────

    touchPinchBegin: function (inst) {
        this._closeMenuIfOpen(inst);
        inst.isPinching = true;
        return this._getViewBox(inst);
    },

    touchPinchChange: function (inst, startVb, scale, centerX, centerY, dx, dy) {
        if (!startVb || !isFinite(scale) || scale <= 0) return;
        const rect = inst.svg.getBoundingClientRect();
        if (!rect.width || !rect.height) return;
        // Clamp instead of rejecting: at a limit the pinch sits exactly at the limit and the
        // two-finger pan keeps working (a rejected frame froze both).
        const startScale = rect.width / startVb.w;
        const effective = this._clampScale(startScale * scale) / startScale;
        const newW = startVb.w / effective;
        const newH = startVb.h / effective;

        // The viewport point under the gesture's START centre stays under the centre, plus the
        // two-finger pan in new-scale document units.
        const px = (centerX - dx) - rect.left;
        const py = (centerY - dy) - rect.top;
        const docX = startVb.x + px * (startVb.w / rect.width);
        const docY = startVb.y + py * (startVb.h / rect.height);
        const panX = dx * (newW / rect.width);
        const panY = dy * (newH / rect.height);
        this._setViewBox(inst, docX - px * (newW / rect.width) - panX, docY - py * (newH / rect.height) - panY, newW, newH);
    },

    touchPinchEnd: function (inst, startVb, scale, dx, dy) {
        inst.isPinching = false;
        const vb = this._getViewBox(inst);
        inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
        inst.dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x, vb.y, vb.w, vb.h);
        inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
    },

    touchPinchCancel: function (inst, startVb) {
        inst.isPinching = false;
        if (!startVb) return;
        this._setViewBox(inst, startVb.x, startVb.y, startVb.w, startVb.h);
    },

    // ── Rotation session (shared by the mouse path and the touch session) ────

    _beginRotateSession: function (inst, nodeId, clientX, clientY, initialRotation, snap) {
        if (inst.isRotating) return;
        const r = this._nodeRect(inst, nodeId);
        if (!r) return;

        inst.isRotating = true;
        inst.rotateNodeId = nodeId;
        inst.rotateCenterDoc = { x: r.x + r.w / 2, y: r.y + r.h / 2 };
        inst.rotateStartNodeRotation = initialRotation;
        inst.rotateSnap = snap || 0;
        inst.rotateGroupMembers = null;

        const startPt = this._screenToDoc(inst, clientX, clientY);
        inst.rotateStartAngle = Math.atan2(startPt.y - inst.rotateCenterDoc.y, startPt.x - inst.rotateCenterDoc.x) * 180 / Math.PI;

        // If rotating a group, cache member start rotations for real-time updates
        const nodeEl = this._nodeEl(inst, nodeId);
        if (nodeEl && nodeEl.getAttribute('data-stencil-id') === 'general.group' && inst.scenePane) {
            inst.rotateGroupMembers = [];
            inst.scenePane.querySelectorAll('g.tm-diagram-node[data-node-id]').forEach(el => {
                if (el.getAttribute('data-parent-group-id') === nodeId) {
                    const memberId = el.getAttribute('data-node-id');
                    const memberRot = this._getNodeRotation(el);
                    inst.rotateGroupMembers.push({ id: memberId, startRotation: memberRot });
                }
            });
        }
    },

    _moveRotateSession: function (inst, clientX, clientY) {
        if (!inst.isRotating) return;
        const pt = this._screenToDoc(inst, clientX, clientY);
        const angle = Math.atan2(pt.y - inst.rotateCenterDoc.y, pt.x - inst.rotateCenterDoc.x) * 180 / Math.PI;
        let delta = angle - inst.rotateStartAngle;
        let rot = inst.rotateStartNodeRotation + delta;
        if (inst.rotateSnap > 0) {
            rot = Math.round(rot / inst.rotateSnap) * inst.rotateSnap;
        }
        this._applyNodeRotation(inst, inst.rotateNodeId, rot);
        if (inst.rotateGroupMembers) {
            const groupDelta = rot - inst.rotateStartNodeRotation;
            inst.rotateGroupMembers.forEach(m => {
                this._applyNodeRotation(inst, m.id, m.startRotation + groupDelta);
            });
        }
    },

    _endRotateSession: function (inst) {
        if (!inst.isRotating) return;
        inst.isRotating = false;
        const nodeId = inst.rotateNodeId;
        const nodeEl = this._nodeEl(inst, nodeId);
        const rot = nodeEl ? this._getNodeRotation(nodeEl) : inst.rotateStartNodeRotation;
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnRotateEnded', nodeId, rot);
        }
        inst.rotateNodeId = null;
        inst.rotateGroupMembers = null;
    },

    _cancelRotateSession: function (inst) {
        if (inst.__rotateMouseDetach) inst.__rotateMouseDetach();   // never leak the mouse listeners
        if (!inst.isRotating) return;
        inst.isRotating = false;
        if (inst.rotateNodeId) {
            this._applyNodeRotation(inst, inst.rotateNodeId, inst.rotateStartNodeRotation);
            if (inst.rotateGroupMembers) {
                inst.rotateGroupMembers.forEach(m => {
                    this._applyNodeRotation(inst, m.id, m.startRotation);
                });
            }
        }
        inst.rotateNodeId = null;
        inst.rotateGroupMembers = null;
    },

    startRotate: function (container, nodeId, clientX, clientY, initialRotation, snap) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst || inst.readOnly || inst.isRotating) return;   // re-entry: a second press mid-rotation is ignored
        this._beginRotateSession(inst, nodeId, clientX, clientY, initialRotation, snap);
        if (!inst.isRotating) return;

        const self = this;
        const detach = function () {
            document.removeEventListener('mousemove', move);
            document.removeEventListener('mouseup', up);
            if (inst.__rotateMouseDetach === detach) inst.__rotateMouseDetach = null;
        };
        const move = function (e) {
            if (!inst.isRotating) { detach(); return; }
            e.preventDefault();
            self._moveRotateSession(inst, e.clientX, e.clientY);
        };
        const up = function () {
            detach();                       // ALWAYS first: a cancelled session must not leak listeners
            self._endRotateSession(inst);   // no-op when the session was cancelled
        };
        inst.__rotateMouseDetach = detach;
        document.addEventListener('mousemove', move);
        document.addEventListener('mouseup', up);
    },

    touchBeginRotate: function (inst, nodeId, clientX, clientY) {
        const nodeEl = this._nodeEl(inst, nodeId);
        if (!nodeEl) return;
        this._beginRotateSession(inst, nodeId, clientX, clientY, this._getNodeRotation(nodeEl), 0);
    },

    touchMoveRotate: function (inst, clientX, clientY) {
        this._moveRotateSession(inst, clientX, clientY);
    },

    touchEndRotate: function (inst) {
        this._endRotateSession(inst);
    },

    touchCancelRotate: function (inst) {
        this._cancelRotateSession(inst);
    },

    // ── Viewport re-measure (phase 6) ────────────────────────────────────────
    // After the editor chrome changed around the canvas (a docked panel expanded, collapsed or
    // was resized), the SVG box changed size without any view change: re-derive the scale and
    // report the viewport and zoom — no Blazor re-render of the canvas.

    relayout: function (container) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst || !inst.svg) return;
        const vb = this._getViewBox(inst);
        const rect = inst.svg.getBoundingClientRect();
        if (rect.width > 0 && vb.w > 0) inst.scale = rect.width / vb.w;
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x, vb.y, vb.w, vb.h);
            inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
        }
    },

    // ── Edge drawing helpers ─────────────────────────────────────────────────

    _startEdgeDraw: function (inst, nodeId, portId, clientX, clientY, side, offset, constraintRx, constraintRy, constraintPerimeter) {
        inst.isDrawingEdge = true;
        inst.container.classList.add('tm-diagram-canvas--edge-drawing');
        const docPt = this._screenToDoc(inst, clientX, clientY);
        inst.drawSource = {
            nodeId: nodeId,
            portId: portId,
            side: side || null,
            offset: offset || 0.5,
            x: docPt.x,
            y: docPt.y,
            edgeId: null,
            constraintRx: constraintRx !== undefined ? constraintRx : null,
            constraintRy: constraintRy !== undefined ? constraintRy : null,
            constraintPerimeter: constraintPerimeter !== undefined ? constraintPerimeter : null
        };

        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('class', 'tm-diagram-edge-draw-path');
        path.setAttribute('fill', 'none');
        path.setAttribute('stroke', '#3b82f6');
        path.setAttribute('stroke-width', '2');
        path.setAttribute('marker-end', 'url(#arrow-default)');
        path.setAttribute('pointer-events', 'none');
        inst.svg.appendChild(path);
        inst.drawTempPath = path;

        // Highlight source port
        const portEl = inst.scenePane?.querySelector('.tm-diagram-port[data-port-id="' + portId + '"]');
        if (portEl) portEl.classList.add('tm-diagram-port--active');

        // Highlight source connection point
        if (inst.drawSource.constraintRx !== null) {
            const cpSelector = '.tm-diagram-connection-point[data-cp-rx="' + inst.drawSource.constraintRx + '"][data-cp-ry="' + inst.drawSource.constraintRy + '"]'; 
            const cpEls = nodeId ? inst.scenePane?.querySelectorAll('[data-node-id="' + nodeId + '"] ' + cpSelector) : null;
            if (cpEls && cpEls.length > 0) cpEls[0].classList.add('tm-diagram-connection-point--active');
        }
    },

    _updateEdgeDraw: function (inst, clientX, clientY) {
        if (!inst.drawTempPath || !inst.drawSource) return;
        const docPt = this._screenToDoc(inst, clientX, clientY);
        let endX = docPt.x;
        let endY = docPt.y;

        // Magnet snapping: find nearest node perimeter point
        const el = document.elementFromPoint(clientX, clientY);
        const nodeEl = el ? el.closest('[data-node-id]') : null;
        const hoverNodeId = nodeEl ? nodeEl.getAttribute('data-node-id') : null;
        if (nodeEl) {
            const rect = this._nodeRect(inst, hoverNodeId);
            if (rect) {
                // Auto-snap to nearest port when cursor is close enough
                // (smart-target threshold). Falls back to perimeter snap
                // otherwise. Distance is measured in document units so
                // the 15px screen threshold scales with zoom.
                const snapToPort = this._findNearestPortOnNode(inst, hoverNodeId, docPt.x, docPt.y, 15 / Math.max(inst.scale, 0.01));
                if (snapToPort) {
                    endX = snapToPort.x;
                    endY = snapToPort.y;
                } else {
                    const snap = this._snapToNodePerimeter(rect, docPt.x, docPt.y);
                    endX = snap.x;
                    endY = snap.y;
                }
            }
        }

        // Drop-target highlight — mark the node the cursor is over so the
        // user gets draw.io-style feedback of where the edge will attach.
        // Don't highlight the source node of a node-anchored draw.
        const prevDrop = inst.__dropTargetNodeId || null;
        const isOwnSource = hoverNodeId && inst.drawSource && hoverNodeId === inst.drawSource.nodeId;
        const nextDrop = (hoverNodeId && !isOwnSource) ? hoverNodeId : null;
        if (prevDrop && prevDrop !== nextDrop) {
            inst.scenePane?.querySelectorAll('.tm-diagram-node--drop-target').forEach(n => n.classList.remove('tm-diagram-node--drop-target'));
        }
        if (nextDrop && nextDrop !== prevDrop) {
            const target = inst.scenePane?.querySelector('g.tm-diagram-node[data-node-id="' + nextDrop + '"]');
            if (target) target.classList.add('tm-diagram-node--drop-target');
        }
        inst.__dropTargetNodeId = nextDrop;

        const d = this._buildDrawPathD(inst, endX, endY);
        inst.drawTempPath.setAttribute('d', d);

        // Port snapping highlight. Prefer the explicitly hovered port
        // (cursor is directly on the port circle) but also highlight the
        // near-miss port that `_findNearestPortOnNode` just snapped the
        // endpoint to, so the user sees the same visual affordance whether
        // they aim precisely or just near enough.
        let portEl = el ? el.closest('.tm-diagram-port') : null;
        if (!portEl && hoverNodeId) {
            const nearest = this._findNearestPortOnNode(inst, hoverNodeId, docPt.x, docPt.y, 15 / Math.max(inst.scale, 0.01));
            if (nearest && nearest.portEl) portEl = nearest.portEl;
        }
        inst.scenePane?.querySelectorAll('.tm-diagram-port.tm-diagram-port--target').forEach(p => p.classList.remove('tm-diagram-port--target'));
        if (portEl) {
            const nid = portEl.closest('[data-node-id]')?.getAttribute('data-node-id');
            const pid = portEl.getAttribute('data-port-id');
            if (nid !== inst.drawSource.nodeId || pid !== inst.drawSource.portId) {
                portEl.classList.add('tm-diagram-port--target');
            }
        }

        // Edge-to-edge: detect hover over existing edge
        let hitEl = el ? el.closest('.tm-diagram-edge-hit-path') : null;
        if (!hitEl && el) {
            const edgeGroup = el.closest('.tm-diagram-edge-group');
            if (edgeGroup) {
                hitEl = edgeGroup.querySelector('.tm-diagram-edge-hit-path');
            }
        }
        // Fallback: if elementFromPoint didn't find an edge (e.g. label group or foreignObject on top),
        // check all edges by geometric distance
        if (!hitEl) {
            const toleranceDoc = 20 / inst.scale;
            let bestDist = Infinity;
            let bestPath = null;
            const allPaths = inst.svg.querySelectorAll('.tm-diagram-edge-hit-path');
            for (let i = 0; i < allPaths.length; i++) {
                const path = allPaths[i];
                const eid = path.getAttribute('data-edge-id');
                if (!eid || eid === inst.drawSource.edgeId) continue;
                const closest = this._findClosestPointOnEdge(path, docPt.x, docPt.y);
                if (closest && closest.dist < bestDist) {
                    bestDist = closest.dist;
                    bestPath = path;
                }
            }
            if (bestPath && bestDist < toleranceDoc) {
                hitEl = bestPath;
            }
        }
        if (hitEl) {
            const edgeId = hitEl.getAttribute('data-edge-id');
            if (edgeId && edgeId !== inst.drawSource.edgeId) {
                const closest = this._findClosestPointOnEdge(hitEl, docPt.x, docPt.y);
                if (closest && closest.dist < 20 / inst.scale) { // 20px screen tolerance
                    inst.drawHoverEdgeId = edgeId;
                    inst.drawHoverEdgeT = closest.t;
                    endX = closest.x;
                    endY = closest.y;
                    this._highlightEdgeTarget(inst, edgeId);
                    const d2 = this._buildDrawPathD(inst, endX, endY);
                    inst.drawTempPath.setAttribute('d', d2);
                    return;
                }
            }
        }
        inst.drawHoverEdgeId = null;
        inst.drawHoverEdgeT = null;
        this._highlightEdgeTarget(inst, null);

        // Nothing snapped — apply grid snap to the free floating endpoint
        // so the preview matches what the final edge will actually use.
        if (!hoverNodeId && inst.gridSize > 0) {
            const gx = this._snap(inst, endX);
            const gy = this._snap(inst, endY);
            if (gx !== endX || gy !== endY) {
                const d3 = this._buildDrawPathD(inst, gx, gy);
                inst.drawTempPath.setAttribute('d', d3);
            }
        }
    },

    // Find the port on `nodeId` whose center is closest to the given doc
    // point (x, y). Returns `{ x, y, portEl, portId, distDoc }` if the
    // distance is within `maxDistDoc` document units, otherwise null.
    // Used for smart auto-snap during edge draw (Phase 3.2): the draw
    // endpoint and port highlight both snap even when the cursor is
    // slightly off the port circle.
    _findNearestPortOnNode: function (inst, nodeId, x, y, maxDistDoc) {
        if (!nodeId || !inst.scenePane) return null;
        const nodeEl = inst.scenePane.querySelector('g.tm-diagram-node[data-node-id="' + nodeId + '"]');
        if (!nodeEl) return null;
        const portEls = nodeEl.querySelectorAll('.tm-diagram-port[data-port-id]');
        if (!portEls || portEls.length === 0) return null;
        // Map every port's screen-space center through the same _screenToDoc
        // transform used for the cursor. In the unified SVG canvas (F2+)
        // there is only one coordinate system (viewBox), so drift is
        // impossible; the comment is kept as historical context.
        let best = null;
        let bestD2 = Infinity;
        const maxD2 = maxDistDoc * maxDistDoc;
        for (let i = 0; i < portEls.length; i++) {
            const p = portEls[i];
            const pr = p.getBoundingClientRect();
            const pcScreen = { x: pr.left + pr.width / 2, y: pr.top + pr.height / 2 };
            const pcDoc = this._screenToDoc(inst, pcScreen.x, pcScreen.y);
            const dx = pcDoc.x - x;
            const dy = pcDoc.y - y;
            const d2 = dx * dx + dy * dy;
            if (d2 < bestD2 && d2 <= maxD2) {
                bestD2 = d2;
                best = { x: pcDoc.x, y: pcDoc.y, portEl: p, portId: p.getAttribute('data-port-id'), distDoc: Math.sqrt(d2) };
            }
        }
        return best;
    },

    _snapToNodePerimeter: function (rect, x, y) {
        // Find closest point on rectangle perimeter
        let cx = Math.max(rect.x, Math.min(x, rect.x + rect.w));
        let cy = Math.max(rect.y, Math.min(y, rect.y + rect.h));
        const dx = cx - x;
        const dy = cy - y;
        // If inside, project to nearest edge
        if (dx === 0 && dy === 0) {
            const dl = x - rect.x;
            const dr = rect.x + rect.w - x;
            const dt = y - rect.y;
            const db = rect.y + rect.h - y;
            const min = Math.min(dl, dr, dt, db);
            if (min === dl) return { x: rect.x, y: y };
            if (min === dr) return { x: rect.x + rect.w, y: y };
            if (min === dt) return { x: x, y: rect.y };
            return { x: x, y: rect.y + rect.h };
        }
        return { x: cx, y: cy };
    },

    _findClosestPointOnEdge: function (pathEl, x, y) {
        if (!pathEl) return null;
        try {
            const len = pathEl.getTotalLength();
            if (len === 0) return null;
            let bestT = 0;
            let bestDist = Infinity;
            const samples = 50;
            for (let i = 0; i <= samples; i++) {
                const t = i / samples;
                const pt = pathEl.getPointAtLength(t * len);
                const dx = pt.x - x;
                const dy = pt.y - y;
                const d = dx * dx + dy * dy;
                if (d < bestDist) {
                    bestDist = d;
                    bestT = t;
                }
            }
            // Refine with binary search around bestT
            let left = Math.max(0, bestT - 1 / samples);
            let right = Math.min(1, bestT + 1 / samples);
            for (let i = 0; i < 8; i++) {
                const m1 = left + (right - left) * 0.33;
                const m2 = left + (right - left) * 0.67;
                const p1 = pathEl.getPointAtLength(m1 * len);
                const p2 = pathEl.getPointAtLength(m2 * len);
                const d1 = (p1.x - x) * (p1.x - x) + (p1.y - y) * (p1.y - y);
                const d2 = (p2.x - x) * (p2.x - x) + (p2.y - y) * (p2.y - y);
                if (d1 < d2) {
                    right = m2;
                    if (d1 < bestDist) { bestDist = d1; bestT = m1; }
                } else {
                    left = m1;
                    if (d2 < bestDist) { bestDist = d2; bestT = m2; }
                }
            }
            const bestPt = pathEl.getPointAtLength(bestT * len);
            return { x: bestPt.x, y: bestPt.y, t: bestT, dist: Math.sqrt(bestDist) };
        } catch (e) {
            return null;
        }
    },

    _highlightEdgeTarget: function (inst, edgeId) {
        inst.svg?.querySelectorAll('.tm-diagram-edge--target').forEach(el => el.classList.remove('tm-diagram-edge--target'));
        if (edgeId) {
            const g = inst.svg?.querySelector('g.tm-diagram-edge-group[data-edge-id="' + edgeId + '"]');
            if (g) g.classList.add('tm-diagram-edge--target');
        }
    },

    _updateConnectHoverIcons: function (inst, clientX, clientY) {
        if (inst.isDrawingEdge || inst.isDragging || inst.isDraggingWaypoint || inst.isDraggingJetty || inst.isDraggingEdgeLabel || inst.isDraggingWholeEdge || inst.isPanning || inst.isRubberBand) {
            this._hideConnectHoverIcons(inst);
            return;
        }
        const el = document.elementFromPoint(clientX, clientY);
        const nodeEl = el ? el.closest('[data-node-id]') : null;
        if (!nodeEl || !this._isNodeInActiveGroup(inst, nodeEl)) {
            this._hideConnectHoverIcons(inst);
            return;
        }
        if (nodeEl.getAttribute('data-locked') === 'true') {
            this._hideConnectHoverIcons(inst);
            return;
        }
        const nodeId = nodeEl.getAttribute('data-node-id');
        const rect = this._nodeRect(inst, nodeId);
        if (!rect) {
            this._hideConnectHoverIcons(inst);
            return;
        }
        const docPt = this._screenToDoc(inst, clientX, clientY);
        const dx = Math.max(rect.x - docPt.x, 0, docPt.x - (rect.x + rect.w));
        const dy = Math.max(rect.y - docPt.y, 0, docPt.y - (rect.y + rect.h));
        const distPx = Math.max(dx, dy) * inst.scale;
        if (distPx > 15) {
            this._hideConnectHoverIcons(inst);
            return;
        }
        this._showConnectHoverIcons(inst, nodeId, rect, nodeEl);
    },

    _showConnectHoverIcons: function (inst, nodeId, rect, nodeEl) {
        if (inst.hoverConnectNodeId === nodeId) return;
        this._hideConnectHoverIcons(inst);
        inst.hoverConnectNodeId = nodeId;
        const dirs = [
            { css: 'n', side: 'top' },
            { css: 'e', side: 'right' },
            { css: 's', side: 'bottom' },
            { css: 'w', side: 'left' },
        ];
        dirs.forEach(d => {
            const btn = document.createElement('div');
            btn.className = 'tm-diagram-hover-connect tm-diagram-hover-connect--' + d.css;
            btn.setAttribute('data-hover-side', d.side);
            btn.style.cssText = 'position:absolute;width:16px;height:16px;border-radius:50%;background:transparent;border:1px solid #94a3b3;display:flex;align-items:center;justify-content:center;cursor:crosshair;z-index:50;pointer-events:all;';
            if (d.css === 'n') { btn.style.top = '-8px'; btn.style.left = 'calc(50% - 8px)'; }
            else if (d.css === 'e') { btn.style.top = 'calc(50% - 8px)'; btn.style.right = '-8px'; }
            else if (d.css === 's') { btn.style.bottom = '-8px'; btn.style.left = 'calc(50% - 8px)'; }
            else if (d.css === 'w') { btn.style.top = 'calc(50% - 8px)'; btn.style.left = '-8px'; }
            btn.innerHTML = '<span style="font-size:10px;color:#94a3b3;transform:rotate(' + (d.css === 'n' ? '-90deg' : d.css === 's' ? '90deg' : d.css === 'w' ? '180deg' : '0deg') + ')">→</span>';
            btn.onmousedown = (e) => {
                e.preventDefault();
                e.stopPropagation();
                const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
                let offset = 0.5;
                if (d.side === 'top' || d.side === 'bottom') {
                    offset = Math.max(0, Math.min(1, (docPt.x - rect.x) / rect.w));
                } else {
                    offset = Math.max(0, Math.min(1, (docPt.y - rect.y) / rect.h));
                }
                this._startEdgeDraw(inst, nodeId, null, e.clientX, e.clientY, d.side, offset);
            };
            nodeEl.appendChild(btn);
            if (!inst.hoverConnectEls) inst.hoverConnectEls = [];
            inst.hoverConnectEls.push(btn);
        });
    },

    _hideConnectHoverIcons: function (inst) {
        if (inst.hoverConnectEls) {
            inst.hoverConnectEls.forEach(el => { if (el.parentNode) el.parentNode.removeChild(el); });
            inst.hoverConnectEls = null;
        }
        inst.hoverConnectNodeId = null;
    },

    _cancelEdgeDraw: function (inst) {
        inst.isDrawingEdge = false;
        inst.isDrawingPolyline = false;
        inst.polylinePoints = [];
        inst.container.classList.remove('tm-diagram-canvas--edge-drawing');
        if (inst.drawTempPath) {
            inst.drawTempPath.remove();
            inst.drawTempPath = null;
        }
        if (inst.drawSource) {
            const portEl = inst.scenePane?.querySelector('.tm-diagram-port[data-port-id="' + inst.drawSource.portId + '"]');
            if (portEl) portEl.classList.remove('tm-diagram-port--active');
            // Remove connection point active highlight
            inst.scenePane?.querySelectorAll('.tm-diagram-connection-point--active').forEach(cp => cp.classList.remove('tm-diagram-connection-point--active'));
            inst.drawSource = null;
        }
        inst.drawHoverEdgeId = null;
        inst.drawHoverEdgeT = null;
        this._highlightEdgeTarget(inst, null);
        inst.scenePane?.querySelectorAll('.tm-diagram-port.tm-diagram-port--target').forEach(p => p.classList.remove('tm-diagram-port--target'));
        inst.scenePane?.querySelectorAll('.tm-diagram-node--drop-target').forEach(n => n.classList.remove('tm-diagram-node--drop-target'));
        inst.__dropTargetNodeId = null;

        // Return to select mode after using edge tool
        if (inst.toolMode === 'edge') {
            this._applyToolMode(inst, 'select');
            if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'select');
        }
    },

    // ── Polyline draw (phase 2) ──────────────────────────────────────────────

    // Build the temp path's `d` attribute, routing through all committed
    // polyline waypoints when one is in progress. `endX/endY` is the live
    // cursor-side endpoint (or the snapped target endpoint).
    _buildDrawPathD: function (inst, endX, endY) {
        let d = 'M ' + inst.drawSource.x + ' ' + inst.drawSource.y;
        if (inst.isDrawingPolyline && inst.polylinePoints && inst.polylinePoints.length) {
            for (let i = 0; i < inst.polylinePoints.length; i++) {
                const p = inst.polylinePoints[i];
                d += ' L ' + p.x + ' ' + p.y;
            }
        }
        d += ' L ' + endX + ' ' + endY;
        return d;
    },

    // Begin a click-to-click polyline draft at the given screen point. The
    // source is always floating (empty canvas). Subsequent left-clicks in
    // `_onPolylineMouseDown` append waypoints or commit the edge.
    _startPolylineDraw: function (inst, clientX, clientY) {
        inst.isDrawingEdge = true;
        inst.isDrawingPolyline = true;
        inst.polylinePoints = [];
        inst.container.classList.add('tm-diagram-canvas--edge-drawing');

        const docPt = this._screenToDoc(inst, clientX, clientY);
        inst.drawSource = {
            nodeId: null, portId: null,
            side: null, offset: 0.5,
            x: docPt.x, y: docPt.y,
            edgeId: null,
            constraintRx: null, constraintRy: null, constraintPerimeter: null
        };

        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('class', 'tm-diagram-edge-draw-path');
        path.setAttribute('fill', 'none');
        path.setAttribute('stroke', '#3b82f6');
        path.setAttribute('stroke-width', '2');
        path.setAttribute('marker-end', 'url(#arrow-default)');
        path.setAttribute('pointer-events', 'none');
        path.setAttribute('stroke-dasharray', '6 4');
        inst.svg.appendChild(path);
        inst.drawTempPath = path;

        // Seed the preview path with source → source (zero-length) so it is
        // visible before the first mousemove event.
        inst.drawTempPath.setAttribute('d', 'M ' + docPt.x + ' ' + docPt.y + ' L ' + docPt.x + ' ' + docPt.y);
    },

    // Handle a left mousedown while a polyline draft is active. Commits the
    // edge if the click landed on a port / connection-point / edge; otherwise
    // appends a waypoint. Double-click finish is handled by `_onDblClick`
    // (the real `dblclick` DOM event) rather than `mousedown.detail` because
    // the latter also fires on sequential clicks that happen within the OS
    // double-click interval, which would commit a polyline prematurely.
    _onPolylineMouseDown: function (e, inst) {
        const hitEl = document.elementFromPoint(e.clientX, e.clientY);

        // Port → attach target to node + port
        const portEl = hitEl ? hitEl.closest('.tm-diagram-port') : null;
        if (portEl) {
            const nodeEl = portEl.closest('[data-node-id]');
            if (nodeEl) {
                this._commitPolyline(inst, {
                    kind: 'port',
                    nodeId: nodeEl.getAttribute('data-node-id'),
                    portId: portEl.getAttribute('data-port-id')
                });
                return;
            }
        }

        // Connection point → attach target via constraint
        const cpEl = hitEl ? hitEl.closest('.tm-diagram-connection-point') : null;
        if (cpEl) {
            const nodeEl = cpEl.closest('[data-node-id]');
            if (nodeEl) {
                this._commitPolyline(inst, {
                    kind: 'cp',
                    nodeId: nodeEl.getAttribute('data-node-id'),
                    rx: parseFloat(cpEl.getAttribute('data-cp-rx')),
                    ry: parseFloat(cpEl.getAttribute('data-cp-ry')),
                    perimeter: cpEl.getAttribute('data-cp-perimeter') === 'true'
                });
                return;
            }
        }

        // Node body (no port) → attach to node without port
        const nodeEl = hitEl ? hitEl.closest('[data-node-id]') : null;
        if (nodeEl) {
            this._commitPolyline(inst, {
                kind: 'node',
                nodeId: nodeEl.getAttribute('data-node-id')
            });
            return;
        }

        // Edge hit → attach to edge midpoint
        const edgeHit = hitEl ? hitEl.closest('.tm-diagram-edge-hit-path') : null;
        if (edgeHit && edgeHit.getAttribute('data-edge-id')) {
            const edgeId = edgeHit.getAttribute('data-edge-id');
            const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
            const closest = this._findClosestPointOnEdge(edgeHit, docPt.x, docPt.y);
            this._commitPolyline(inst, {
                kind: 'edge',
                edgeId: edgeId,
                t: closest ? closest.t : 0.5
            });
            return;
        }

        // Empty canvas → append waypoint and update preview.
        const docPt = this._screenToDoc(inst, e.clientX, e.clientY);
        inst.polylinePoints.push({ x: docPt.x, y: docPt.y });
        this._updateEdgeDraw(inst, e.clientX, e.clientY);
    },

    // Flatten polyline waypoints and dispatch JsOnEdgeCreated with the
    // appropriate target terminal descriptor.
    _commitPolyline: function (inst, target) {
        if (!inst.dotNetRef) { this._cancelEdgeDraw(inst); return; }

        const waypointsXY = [];
        for (let i = 0; i < inst.polylinePoints.length; i++) {
            waypointsXY.push(this._snap(inst, inst.polylinePoints[i].x));
            waypointsXY.push(this._snap(inst, inst.polylinePoints[i].y));
        }

        // Source is always floating for a polyline draft started from empty
        // canvas; forward its doc-space coordinates, grid-snapped so free
        // endpoints align with the grid when one is active.
        const srcPtX = this._snap(inst, inst.drawSource.x);
        const srcPtY = this._snap(inst, inst.drawSource.y);

        if (target.kind === 'floating') {
            inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                null, null,
                null, null,
                null, 0.5, null, 0.5,
                null, 0.5,
                null, null, null,
                null, null, null,
                this._snap(inst, target.x), this._snap(inst, target.y),
                srcPtX, srcPtY,
                waypointsXY);
        } else if (target.kind === 'port') {
            inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                null, null,
                target.nodeId, target.portId,
                null, 0.5, null, 0.5,
                null, 0.5,
                null, null, null,
                null, null, null,
                null, null,
                srcPtX, srcPtY,
                waypointsXY);
        } else if (target.kind === 'node') {
            inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                null, null,
                target.nodeId, null,
                null, 0.5, null, 0.5,
                null, 0.5,
                null, null, null,
                null, null, null,
                null, null,
                srcPtX, srcPtY,
                waypointsXY);
        } else if (target.kind === 'cp') {
            inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                null, null,
                target.nodeId, null,
                null, 0.5, null, 0.5,
                null, 0.5,
                null, null, null,
                target.rx, target.ry, target.perimeter,
                null, null,
                srcPtX, srcPtY,
                waypointsXY);
        } else if (target.kind === 'edge') {
            inst.dotNetRef.invokeMethodAsync('JsOnEdgeCreated',
                null, null,
                null, null,
                null, 0.5, null, 0.5,
                target.edgeId, target.t,
                null, null, null,
                null, null, null,
                null, null,
                srcPtX, srcPtY,
                waypointsXY);
        }

        inst.polylineCommittedAt = Date.now();
        this._cancelEdgeDraw(inst);
    },

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    _onKeyDown: function (e, inst) {
        // The document.body fallback does not apply when this editor sits inside an INERT
        // subtree (the host overlay's background — e.g. the Modeling preview behind the open
        // open-editor dialog): the dialog's editor owns the keys, this canvas is inert.
        if (!inst.container.contains(document.activeElement)
            && (document.activeElement !== document.body || inst.container.closest('[inert]'))) return;

        const ids = [...inst.selectedIds];

        if (e.key === 'Delete' || e.key === 'Backspace') {
            if (inst.readOnly) return;
            if (ids.length === 0) return;
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
            e.preventDefault();
            inst.selectedIds.clear();
            this._updateSelection(inst);
            inst.dotNetRef.invokeMethodAsync('OnDeleteSelected', ids);
            return;
        }

        // Enter commits the current polyline draft, using the last known
        // cursor position as a floating target terminal.
        if (e.key === 'Enter' && inst.isDrawingPolyline) {
            e.preventDefault();
            const last = inst.lastPointerClient;
            if (last) {
                const docPt = this._screenToDoc(inst, last.x, last.y);
                this._commitPolyline(inst, { kind: 'floating', x: docPt.x, y: docPt.y });
            } else {
                this._cancelEdgeDraw(inst);
            }
            return;
        }

        // Escape consumes ONLY what this editor actually handles (a running gesture/session or a
        // non-empty selection). The preventDefault lives inside each consuming branch: an Escape
        // with nothing to consume stays unmarked, keeps bubbling and closes a host overlay (a
        // TmModal dialog) through its own document-level listener — the capture registration plus
        // _onSearchEscape above make the ordering against that listener deterministic.
        if (e.key === 'Escape') {
            // A capture-phase consumer that ran BEFORE this handler (e.g. _onSearchEscape —
            // inline edit / search panel / view menu / context menu) already owns this Escape:
            // skip entirely so e.g. a non-empty selection is not ALSO cleared on the same
            // keypress while an inline editor cancels.
            if (e.defaultPrevented || e.tmEscapeHandled) return;
            if (inst.isRotating) {
                e.preventDefault();
                this._cancelRotateSession(inst);
                return;
            }
            if (inst.isPendingEdgeDraw) {
                e.preventDefault();
                inst.isPendingEdgeDraw = false;
                inst.pendingEdgeStart = null;
                if (inst.toolMode === 'edge') {
                    this._applyToolMode(inst, 'select');
                    if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'select');
                }
                return;
            }
            if (inst.isDrawingEdge) {
                e.preventDefault();
                this._cancelEdgeDraw(inst);
                return;
            }
            if (inst.isDraggingWaypoint && inst.dotNetRef) {
                e.preventDefault();
                inst.isDraggingWaypoint = false;
                inst.dragWaypointEdgeId = null;
                inst.dragWaypointIndex = null;
                inst.dragWaypointStartScreen = null;
                inst.dragWaypointStartDoc = null;
                inst.dotNetRef.invokeMethodAsync('OnCancelEdgeEdit');
                return;
            }
            if (inst.isDraggingWholeEdge) {
                e.preventDefault();
                const edgeId = inst.dragWholeEdgeId;
                const group = inst.svg.querySelector('g.tm-diagram-edge-group[data-edge-id="' + edgeId + '"]');
                if (group) {
                    group.removeAttribute('transform');
                }
                inst.isDraggingWholeEdge = false;
                inst.dragWholeEdgeId = null;
                inst.dragWholeEdgeStartDoc = null;
                inst.dragWholeEdgeDeltaDoc = { x: 0, y: 0 };
                inst.container.style.cursor = inst.toolMode === 'pan' ? 'grab' : '';
                return;
            }
            if (ids.length > 0) {
                e.preventDefault();
                // A consumed Escape: the selection clear is this editor's action, so a host
                // overlay must not also close on the same keypress (final review).
                e.tmEscapeHandled = true;
                inst.selectedIds.clear();
                this._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnClearSelection');
            }
            return;
        }

        if (e.ctrlKey || e.metaKey) {
            switch (e.key.toLowerCase()) {
                case 'z':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync(e.shiftKey ? 'OnRedo' : 'OnUndo');
                    return;
                case 'y':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnRedo');
                    return;
                case 'a':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnSelectAll');
                    return;
                case 'd':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnDuplicate', ids);
                    return;
                case 'c':
                    e.preventDefault();
                    if (e.shiftKey) {
                        if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnCopyStyle');
                    } else {
                        if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnCopy', ids);
                    }
                    return;
                case 'v':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    if (e.shiftKey) {
                        inst.dotNetRef.invokeMethodAsync('OnPasteStyle');
                    } else {
                        inst.dotNetRef.invokeMethodAsync('OnPaste');
                    }
                    return;
                case 'f':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnShowSearch');
                    return;
                case 'h':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnShowReplace');
                    return;
                case 'b':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnToggleBold');
                    return;
                case 'i':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnToggleItalic');
                    return;
                case 'u':
                    if (inst.readOnly) return;
                    e.preventDefault();
                    if (ids.length > 0) inst.dotNetRef.invokeMethodAsync('OnToggleUnderline');
                    return;
                case 'home':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnNavigateToCorner', 'top-left');
                    return;
                case 'end':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnNavigateToCorner', 'bottom-right');
                    return;
                case 'pageup':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnSwitchPage', -1);
                    return;
                case 'pagedown':
                    e.preventDefault();
                    inst.dotNetRef.invokeMethodAsync('OnSwitchPage', 1);
                    return;
                case 'g':
                    if (e.shiftKey) {
                        if (inst.readOnly) return;
                        e.preventDefault();
                        if (ids.length === 1) {
                            const selId = ids[0];
                            const el = this._nodeEl(inst, selId);
                            if (el && el.getAttribute('data-stencil-id') === 'general.group') {
                                inst.dotNetRef.invokeMethodAsync('OnEnterGroup', selId);
                                return;
                            }
                        }
                        if (ids.length > 1) inst.dotNetRef.invokeMethodAsync('OnGroupSelected');
                    }
                    return;
                case 'f':
                    if (e.shiftKey) {
                        e.preventDefault();
                        inst.dotNetRef.invokeMethodAsync('OnExitGroup');
                    }
                    return;
                case 'l':
                    if (e.shiftKey) {
                        if (inst.readOnly) return;
                        e.preventDefault();
                        inst.dotNetRef.invokeMethodAsync('OnLockSelected');
                    }
                    return;
            }
        }

        // Quick-insert stencils (A,S,D,F,R). Read-only skips THIS block only — an early
        // return here would also kill the H/V tool shortcuts that stay live in read-only.
        if (!e.ctrlKey && !e.metaKey && !inst.readOnly && !e.target.matches('input,textarea,select')) {
            const stencilMap = {
                'KeyA': 'general.text',
                'KeyS': 'general.sticky-note',
                'KeyD': 'general.rectangle',
                'KeyF': 'general.ellipse',
                'KeyR': 'general.rhombus'
            };
            if (stencilMap[e.code]) {
                e.preventDefault();
                inst.dotNetRef.invokeMethodAsync('OnQuickInsert', stencilMap[e.code]);
                return;
            }
        }

        // Space -> temporary pan mode
        if (e.code === 'Space' && !e.target.matches('input,textarea,select')) {
            e.preventDefault();
            if (!inst.spaceHeld) {
                inst.spaceHeld = true;
                if (!inst.isPanning) inst.container.style.cursor = 'grab';
            }
            return;
        }

        // Tool-mode shortcuts: H → pan, V → select, L/C/E → edge.
        // `E` was added in Phase 3 to match draw.io's edge shortcut; `L`
        // and `C` are kept for backwards compatibility.
        // R1: read-only keeps H and V live (navigation/inspection tools); the edge
        // shortcuts are edit-only and stay blocked.
        if (!e.ctrlKey && !e.metaKey && !e.target.matches('input,textarea,select')) {
            if (e.code === 'KeyH') {
                e.preventDefault();
                this._applyToolMode(inst, 'pan');
                inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'pan');
                return;
            }
            if (e.code === 'KeyV') {
                e.preventDefault();
                this._applyToolMode(inst, 'select');
                inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'select');
                return;
            }
            if (e.code === 'KeyL' || e.code === 'KeyC' || e.code === 'KeyE') {
                if (inst.readOnly) return;
                e.preventDefault();
                this._applyToolMode(inst, 'edge');
                inst.dotNetRef.invokeMethodAsync('OnToolModeChanged', 'edge');
                return;
            }
        }

        // Arrow nudge
        if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) {
            if (ids.length === 0) return;
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
            if (inst.readOnly) { e.preventDefault(); return; }
            e.preventDefault();
            const step = e.shiftKey ? 10 : 1;
            const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
            const dy = e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0;

            // Filter to active group, skip locked nodes, and include group nodes
            let nudgeIds = ids.filter(id => {
                const el = this._nodeEl(inst, id);
                if (!el || el.getAttribute('data-locked') === 'true') return false;
                return this._isNodeInActiveGroup(inst, el);
            });
            nudgeIds = this._includeGroupNodes(inst, nudgeIds);

            nudgeIds.forEach(id => {
                const r = this._nodeRect(inst, id);
                if (!r) return;
                const clamped = this._clampChildPosition(inst, id, r.x + dx, r.y + dy);
                this._setNodeTranslate(inst, id, clamped.x, clamped.y);
            });
            this._updateSelection(inst);

            const moves = nudgeIds.map(id => {
                const r = this._nodeRect(inst, id);
                return r ? { id, x: r.x, y: r.y } : null;
            }).filter(Boolean);

            if (moves.length === 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementMoved', moves[0].id, moves[0].x, moves[0].y);
            } else if (moves.length > 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementsMoved', moves);
            }
        }
    },

    _onKeyUp: function (e, inst) {
        if (e.code === 'Space') {
            inst.spaceHeld = false;
            if (!inst.isPanning) {
                inst.container.style.cursor = inst.toolMode === 'pan' ? 'grab' : '';
            }
        }
    },

    // ── Selection outlines ───────────────────────────────────────────────────

    _updateSelection: function (inst) {
        this._clearSelectionOutlines(inst);
        if (!inst.scenePane) return;

        // Sync the .tm-diagram-node--selected modifier on the <g> so the CSS
        // visibility rules for ports / connection points fire on selected nodes.
        inst.scenePane.querySelectorAll('.tm-diagram-node--selected').forEach(function (el) {
            el.classList.remove('tm-diagram-node--selected');
        });

        if (!inst.overlayPane) return;
        const SVG_NS = 'http://www.w3.org/2000/svg';
        inst.selectedIds.forEach(id => {
            const nodeEl = this._nodeEl(inst, id);
            if (nodeEl) nodeEl.classList.add('tm-diagram-node--selected');
            const r = this._nodeRect(inst, id);
            if (!r) return;
            const rot = this._getNodeRotation(nodeEl);
            // Selection outline sits in the overlay-pane as an SVG <rect> with
            // the same translate+rotate as the node, sized 8 px larger so it
            // visually frames the shape rather than overlapping it.
            const rect = document.createElementNS(SVG_NS, 'rect');
            rect.setAttribute('class', 'tm-diagram-selection-outline');
            rect.setAttribute('x', '-4');
            rect.setAttribute('y', '-4');
            rect.setAttribute('width', String(r.w + 8));
            rect.setAttribute('height', String(r.h + 8));
            rect.setAttribute('fill', 'none');
            rect.setAttribute('pointer-events', 'none');
            rect.setAttribute('transform', this._buildNodeTransform(r.x, r.y, rot, r.w, r.h));
            rect.setAttribute('data-sel-for', id);
            inst.overlayPane.appendChild(rect);
        });
    },

    _updateSelectionTransforms: function (inst) {
        if (!inst.overlayPane && !inst.decoratorPane) return;
        inst.selectedIds.forEach(id => {
            const r = this._nodeRect(inst, id);
            if (!r) return;
            const nodeEl = this._nodeEl(inst, id);
            const rot = this._getNodeRotation(nodeEl);
            const tr = this._buildNodeTransform(r.x, r.y, rot, r.w, r.h);

            if (inst.overlayPane) {
                const outline = inst.overlayPane.querySelector('[data-sel-for="' + id + '"]');
                if (outline) outline.setAttribute('transform', tr);
            }
            // F5: keep the decorator-pane per-node groups (transform handles, the finger-sized
            // port hit circles and the quick-connect arrows — every group Blazor renders with
            // data-node-id for this node) in sync with the live node during JS-driven drags.
            // Blazor re-renders them after the drag commits, but during the drag we must update
            // the DOM directly otherwise the handles would lag behind the node. The generic
            // [data-node-id] selector covers whichever groups exist (a mobile host renders no
            // handles group, a read-only host renders none at all) without a per-class lookup.
            if (inst.decoratorPane) {
                inst.decoratorPane.querySelectorAll('[data-node-id="' + id + '"]').forEach(el => {
                    el.setAttribute('transform', tr);
                });
            }
        });
    },

    _clearSelectionOutlines: function (inst) {
        if (inst.overlayPane) {
            inst.overlayPane.querySelectorAll('.tm-diagram-selection-outline').forEach(el => el.remove());
        }
    },

    _updateWaypointVisuals: function (inst, edgeId, waypointIndex, x, y) {
        const svg = inst.svg;
        if (!svg) return;
        const handleIndex = waypointIndex + 1;
        const handle = svg.querySelector('circle.tm-diagram-edge-handle[data-edge-id="' + edgeId + '"][data-handle-index="' + handleIndex + '"]');
        if (handle) {
            handle.setAttribute('cx', x);
            handle.setAttribute('cy', y);
        }
        const hitPath = svg.querySelector('path.tm-diagram-edge-hit-path[data-edge-id="' + edgeId + '"]');
        const visPath = svg.querySelector('path.tm-diagram-edge-path[data-edge-id="' + edgeId + '"]');
        const handles = Array.from(svg.querySelectorAll('circle.tm-diagram-edge-handle[data-edge-id="' + edgeId + '"]'))
            .sort(function (a, b) {
                return parseInt(a.getAttribute('data-handle-index'), 10) - parseInt(b.getAttribute('data-handle-index'), 10);
            });
        if (handles.length > 1) {
            let d = 'M ' + handles[0].getAttribute('cx') + ' ' + handles[0].getAttribute('cy');
            for (let i = 1; i < handles.length; i++) {
                d += ' L ' + handles[i].getAttribute('cx') + ' ' + handles[i].getAttribute('cy');
            }
            if (hitPath) hitPath.setAttribute('d', d);
            if (visPath) visPath.setAttribute('d', d);
        }
    },

    _updateDanglingVisuals: function (inst, edgeId, type, x, y) {
        const svg = inst.svg;
        if (!svg) return;
        const handle = svg.querySelector('rect.tm-diagram-edge-handle--dangling[data-edge-id="' + edgeId + '"][data-dangling="' + type + '"]');
        if (handle) {
            handle.setAttribute('x', x - 4);
            handle.setAttribute('y', y - 4);
        }
        const group = svg.querySelector('g.tm-diagram-edge-group[data-edge-id="' + edgeId + '"]');
        const hitPath = group ? group.querySelector('path.tm-diagram-edge-hit-path') : null;
        const visPath = group ? group.querySelector('path.tm-diagram-edge-path') : null;
        if (!hitPath || !visPath) return;
        const d = hitPath.getAttribute('d');
        if (!d) return;
        if (type === 'source') {
            const newD = d.replace(/^M\s+[\d\-.]+\s+[\d\-.]+/, 'M ' + x + ' ' + y);
            hitPath.setAttribute('d', newD);
            visPath.setAttribute('d', newD);
        } else {
            const newD = d.replace(/L\s+[\d\-.]+\s+[\d\-.]+$/, 'L ' + x + ' ' + y);
            hitPath.setAttribute('d', newD);
            visPath.setAttribute('d', newD);
        }
    },

    // ── Rubber band helper ───────────────────────────────────────────────────

    _createRubberEl: function (inst, x, y) {
        const el = document.createElement('div');
        el.className = 'tm-diagram-rubber';
        el.style.position = 'absolute';
        el.style.left = x + 'px';
        el.style.top = y + 'px';
        el.style.width = '0';
        el.style.height = '0';
        el.style.pointerEvents = 'none';
        el.style.zIndex = '10';
        inst.container.appendChild(el);
        return el;
    },

    // ── Obstacle-avoiding orthogonal router ─────────────────────────────────

    _simplifyWaypoints: function (waypoints) {
        if (!waypoints || waypoints.length < 3) return waypoints;
        const result = [waypoints[0]];
        for (let i = 1; i < waypoints.length - 1; i++) {
            const prev = result[result.length - 1];
            const curr = waypoints[i];
            const next = waypoints[i + 1];
            if ((prev[0] === curr[0] && curr[0] === next[0]) || (prev[1] === curr[1] && curr[1] === next[1])) {
                continue;
            }
            result.push(curr);
        }
        result.push(waypoints[waypoints.length - 1]);
        return result;
    },

    _simplifyEdgeWaypoints: function (waypoints) {
        if (!waypoints || waypoints.length < 3) return waypoints;
        const result = [waypoints[0]];
        for (let i = 1; i < waypoints.length - 1; i++) {
            const prev = result[result.length - 1];
            const curr = waypoints[i];
            const next = waypoints[i + 1];
            if ((Math.abs(prev.x - curr.x) < 0.5 && Math.abs(curr.x - next.x) < 0.5) ||
                (Math.abs(prev.y - curr.y) < 0.5 && Math.abs(curr.y - next.y) < 0.5)) {
                continue;
            }
            result.push(curr);
        }
        result.push(waypoints[waypoints.length - 1]);
        return result;
    },

    _computeObstacleAvoidingWaypoints: function (sx1, sy1, sx2, sy2, obstacles) {
        if (!obstacles || obstacles.length === 0) return null;

        const margin = 12;
        const expanded = obstacles.map(function (o) {
            return {
                x: o.x - margin,
                y: o.y - margin,
                w: o.w + margin * 2,
                h: o.h + margin * 2
            };
        });

        const xSet = new Set();
        const ySet = new Set();
        xSet.add(sx1);
        xSet.add(sx2);
        ySet.add(sy1);
        ySet.add(sy2);

        for (let i = 0; i < expanded.length; i++) {
            const o = expanded[i];
            xSet.add(o.x);
            xSet.add(o.x + o.w);
            ySet.add(o.y);
            ySet.add(o.y + o.h);
        }

        let xs = Array.from(xSet).sort(function (a, b) { return a - b; });
        let ys = Array.from(ySet).sort(function (a, b) { return a - b; });

        // Add midpoints between adjacent coordinates to ensure narrow channels are represented
        const augmentedXs = [];
        for (let i = 0; i < xs.length; i++) {
            augmentedXs.push(xs[i]);
            if (i < xs.length - 1 && xs[i + 1] - xs[i] > 1) {
                augmentedXs.push((xs[i] + xs[i + 1]) / 2);
            }
        }
        const augmentedYs = [];
        for (let i = 0; i < ys.length; i++) {
            augmentedYs.push(ys[i]);
            if (i < ys.length - 1 && ys[i + 1] - ys[i] > 1) {
                augmentedYs.push((ys[i] + ys[i + 1]) / 2);
            }
        }

        xs = augmentedXs.sort(function (a, b) { return a - b; });
        ys = augmentedYs.sort(function (a, b) { return a - b; });

        const xToIdx = {};
        const yToIdx = {};
        for (let i = 0; i < xs.length; i++) xToIdx[xs[i]] = i;
        for (let i = 0; i < ys.length; i++) yToIdx[ys[i]] = i;

        const startX = xToIdx[sx1];
        const startY = yToIdx[sy1];
        const endX = xToIdx[sx2];
        const endY = yToIdx[sy2];

        function intersectsObstacle(x1, y1, x2, y2) {
            const minX = Math.min(x1, x2);
            const maxX = Math.max(x1, x2);
            const minY = Math.min(y1, y2);
            const maxY = Math.max(y1, y2);
            for (let i = 0; i < expanded.length; i++) {
                const o = expanded[i];
                const ox1 = o.x, oy1 = o.y, ox2 = o.x + o.w, oy2 = o.y + o.h;
                if (maxX <= ox1 || minX >= ox2 || maxY <= oy1 || minY >= oy2) continue;
                if (y1 === y2) {
                    if (y1 > oy1 && y1 < oy2 && maxX > ox1 && minX < ox2) return true;
                } else if (x1 === x2) {
                    if (x1 > ox1 && x1 < ox2 && maxY > oy1 && minY < oy2) return true;
                }
            }
            return false;
        }

        // A* with turn penalty
        const open = [];
        const closed = new Set();
        const gScore = {};
        const fScore = {};
        const cameFrom = {};

        function makeKey(x, y) { return x + ',' + y; }

        function pushOpen(k, f) {
            open.push({ k: k, f: f });
            open.sort(function (a, b) { return a.f - b.f; });
        }

        function popOpen() {
            return open.shift().k;
        }

        const sKey = makeKey(startX, startY);
        gScore[sKey] = 0;
        fScore[sKey] = Math.abs(sx2 - sx1) + Math.abs(sy2 - sy1);
        pushOpen(sKey, fScore[sKey]);

        while (open.length > 0) {
            const current = popOpen();
            if (current === makeKey(endX, endY)) {
                const path = [];
                let c = current;
                while (c) {
                    const parts = c.split(',');
                    const cx = parseInt(parts[0], 10);
                    const cy = parseInt(parts[1], 10);
                    path.push([xs[cx], ys[cy]]);
                    c = cameFrom[c];
                }
                path.reverse();
                return this._simplifyWaypoints(path);
            }

            closed.add(current);

            const parts = current.split(',');
            const cx = parseInt(parts[0], 10);
            const cy = parseInt(parts[1], 10);
            const cxVal = xs[cx];
            const cyVal = ys[cy];

            const neighbors = [];
            if (cx > 0) neighbors.push([cx - 1, cy, xs[cx - 1], cyVal]);
            if (cx < xs.length - 1) neighbors.push([cx + 1, cy, xs[cx + 1], cyVal]);
            if (cy > 0) neighbors.push([cx, cy - 1, cxVal, ys[cy - 1]]);
            if (cy < ys.length - 1) neighbors.push([cx, cy + 1, cxVal, ys[cy + 1]]);

            for (let i = 0; i < neighbors.length; i++) {
                const n = neighbors[i];
                const nx = n[0], ny = n[1], nxVal = n[2], nyVal = n[3];
                const nKey = makeKey(nx, ny);
                if (closed.has(nKey)) continue;
                if (intersectsObstacle(cxVal, cyVal, nxVal, nyVal)) continue;

                const moveDist = Math.abs(nxVal - cxVal) + Math.abs(nyVal - cyVal);
                let turnPenalty = 0;
                if (cameFrom[current]) {
                    const cparts = cameFrom[current].split(',');
                    const px = parseInt(cparts[0], 10);
                    const py = parseInt(cparts[1], 10);
                    const dx1 = cx - px;
                    const dy1 = cy - py;
                    const dx2 = nx - cx;
                    const dy2 = ny - cy;
                    if (dx1 !== dx2 || dy1 !== dy2) turnPenalty = 40;
                }

                const tentativeG = gScore[current] + moveDist + turnPenalty;
                if (!(nKey in gScore) || tentativeG < gScore[nKey]) {
                    cameFrom[nKey] = current;
                    gScore[nKey] = tentativeG;
                    fScore[nKey] = tentativeG + Math.abs(nxVal - sx2) + Math.abs(nyVal - sy2);
                    pushOpen(nKey, fScore[nKey]);
                }
            }
        }

        return null;
    },

    // ── Orthogonal router ────────────────────────────────────────────────────

    _computeOrthogonalWaypoints: function (x1, y1, side1, x2, y2, side2, routing, sourceSpacing, targetSpacing, srcBounds, tgtBounds, obstacles) {
        routing = (routing || 'orthogonal').toLowerCase();
        sourceSpacing = sourceSpacing || 0;
        targetSpacing = targetSpacing || 0;

        let s1 = (side1 || '').toLowerCase();
        let s2 = (side2 || '').toLowerCase();

        // Swimlane-aware routing: force entry direction based on swimlane orientation
        if (srcBounds && srcBounds.isSwimlane) {
            if (srcBounds.isHorizontal) {
                // Force left/right exit for horizontal swimlane
                if (s1 !== 'left' && s1 !== 'right') s1 = x1 < x2 ? 'right' : 'left';
            } else {
                // Force top/bottom exit for vertical swimlane
                if (s1 !== 'top' && s1 !== 'bottom') s1 = y1 < y2 ? 'bottom' : 'top';
            }
        }
        if (tgtBounds && tgtBounds.isSwimlane) {
            if (tgtBounds.isHorizontal) {
                // Force left/right entry for horizontal swimlane
                if (s2 !== 'left' && s2 !== 'right') s2 = x1 < x2 ? 'left' : 'right';
            } else {
                // Force top/bottom entry for vertical swimlane
                if (s2 !== 'top' && s2 !== 'bottom') s2 = y1 < y2 ? 'top' : 'bottom';
            }
        }

        const dx1 = s1 === 'left' ? -1 : s1 === 'right' ? 1 : 0;
        const dy1 = s1 === 'top' ? -1 : s1 === 'bottom' ? 1 : 0;
        const dx2 = s2 === 'left' ? -1 : s2 === 'right' ? 1 : 0;
        const dy2 = s2 === 'top' ? -1 : s2 === 'bottom' ? 1 : 0;

        // Apply spacing
        const sx1 = x1 + dx1 * sourceSpacing;
        const sy1 = y1 + dy1 * sourceSpacing;
        const sx2 = x2 + dx2 * targetSpacing;
        const sy2 = y2 + dy2 * targetSpacing;

        if (routing === 'elbow') {
            var orient = (arguments[12] || 'auto').toLowerCase();
            if (orient === 'horizontal') {
                if (dx1 !== 0 && dx2 !== 0) {
                    const midX = (sx1 + sx2) / 2;
                    return [[midX, sy1], [midX, sy2]];
                }
                return [[sx2, sy1]];
            }
            if (orient === 'vertical') {
                if (dy1 !== 0 && dy2 !== 0) {
                    const midY = (sy1 + sy2) / 2;
                    return [[sx1, midY], [sx2, midY]];
                }
                return [[sx1, sy2]];
            }
            // auto
            if (dx1 !== 0 && dx2 !== 0) {
                const midX = (sx1 + sx2) / 2;
                return [[midX, sy1], [midX, sy2]];
            }
            if (dy1 !== 0 && dy2 !== 0) {
                const midY = (sy1 + sy2) / 2;
                return [[sx1, midY], [sx2, midY]];
            }
            if (dx1 !== 0 && dy2 !== 0) {
                return [[sx2, sy1]];
            }
            if (dy1 !== 0 && dx2 !== 0) {
                return [[sx1, sy2]];
            }
            return [[sx1, sy2]];
        }

        if (routing === 'segment') {
            if (dx1 !== 0 && dx2 !== 0) {
                const midX = (sx1 + sx2) / 2;
                return [[midX, sy1], [midX, sy2]];
            }
            if (dy1 !== 0 && dy2 !== 0) {
                const midY = (sy1 + sy2) / 2;
                return [[sx1, midY], [sx2, midY]];
            }
            if (dx1 !== 0 && dy2 !== 0) {
                return [[sx2, sy1]];
            }
            if (dy1 !== 0 && dx2 !== 0) {
                return [[sx1, sy2]];
            }
            return [[sx1, sy2]];
        }

        if (routing === 'isometric') {
            const dx = sx2 - sx1;
            const dy = sy2 - sy1;
            const absDx = Math.abs(dx);
            const absDy = Math.abs(dy);
            if (absDx > absDy) {
                // Horizontal arms with diagonal connector
                return [[sx1 + dx * 0.3, sy1], [sx1 + dx * 0.7, sy2]];
            } else {
                // Vertical arms with diagonal connector
                return [[sx1, sy1 + dy * 0.3], [sx2, sy1 + dy * 0.7]];
            }
        }

        if (routing === 'entityrelation') {
            const arm = 30;
            const midY = (sy1 + sy2) / 2;
            if (dx1 > 0) {
                return [[sx1 + arm, sy1], [sx1 + arm, midY], [sx2 - arm, midY], [sx2 - arm, sy2]];
            } else {
                return [[sx1 - arm, sy1], [sx1 - arm, midY], [sx2 + arm, midY], [sx2 + arm, sy2]];
            }
        }

        // orthogonal (default) and rounded
        if (routing === 'orthogonal' || routing === 'rounded') {
            const obstaclePath = this._computeObstacleAvoidingWaypoints(sx1, sy1, sx2, sy2, obstacles);
            if (obstaclePath && obstaclePath.length >= 2) {
                // obstaclePath includes start and end; we need intermediate waypoints only
                return obstaclePath.slice(1, obstaclePath.length - 1);
            }
        }

        // Simple fallback for orthogonal, rounded, and any other routing
        if (dx1 !== 0 && dx2 !== 0) {
            const midX = (sx1 + sx2) / 2;
            return [[midX, sy1], [midX, sy2]];
        }
        if (dy1 !== 0 && dy2 !== 0) {
            const midY = (sy1 + sy2) / 2;
            return [[sx1, midY], [sx2, midY]];
        }
        if (dx1 !== 0 && dy2 !== 0) {
            return [[sx2, sy1]];
        }
        if (dy1 !== 0 && dx2 !== 0) {
            return [[sx1, sy2]];
        }
        const midX = (sx1 + sx2) / 2;
        return [[midX, sy1], [midX, sy2]];
    },

    // ── Programmatic API ─────────────────────────────────────────────────────

    setSelection: function (container, ids) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        inst.selectedIds = new Set(ids || []);
        this._updateSelection(inst);
    },

    setActiveGroupId: function (container, activeGroupId) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        inst.activeGroupId = activeGroupId || null;
    },

    setRulerUnit: function (container, unit) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        inst.rulerUnit = unit || 'px';
    },

    setPageScale: function (container, scale) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        inst.pageScale = scale || 1.0;
    },

    // Mirror a host-side read-only change into the instance. This must run on
    // EVERY read-only flip (not only at init): otherwise `_onKeyDown` keeps
    // mutating the document and drags keep moving DOM nodes after the host
    // locked the editor. Any gesture caught mid-flight is released here so a
    // read-only switch never leaves a half-finished drag or edge draw.
    setReadOnly: function (container, readOnly) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        inst.readOnly = !!readOnly;

        if (inst.readOnly) {
            // Touch/pen editing sessions (node drag, long-press, resize, rotate): their onCancel
            // callbacks roll back through the engine hooks and OnResizeCancelled.
            if (inst.__touchDetach && typeof inst.__touchDetach.cancelEditing === 'function') {
                try { inst.__touchDetach.cancelEditing('read-only'); } catch (_e) { /* best effort */ }
            }
            // Mouse or touch rotation: restore the start angle (the mouse listener leak is J8, phase 3).
            this._cancelRotateSession(inst);
        }

        if (inst.isPendingEdgeDraw) {
            inst.isPendingEdgeDraw = false;
            inst.pendingEdgeStart = null;
        }
        if (inst.isDrawingEdge) {
            this._cancelEdgeDraw(inst);
        }
        if (inst.isDraggingWholeEdge) {
            const group = inst.svg.querySelector('g.tm-diagram-edge-group[data-edge-id="' + inst.dragWholeEdgeId + '"]');
            if (group) group.removeAttribute('transform');
            inst.isDraggingWholeEdge = false;
            inst.dragWholeEdgeId = null;
            inst.dragWholeEdgeStartDoc = null;
            inst.dragWholeEdgeDeltaDoc = { x: 0, y: 0 };
            this._clearGuideLines(inst);
        }
        if (inst.isDraggingWaypoint || inst.isDraggingSegment || inst.isDraggingJetty ||
            inst.isDraggingEdgeLabel || inst.isDraggingDangling) {
            inst.isDraggingWaypoint = false;
            inst.dragWaypointEdgeId = null;
            inst.dragWaypointIndex = null;
            inst.dragWaypointStartScreen = null;
            inst.dragWaypointStartDoc = null;
            inst.isDraggingSegment = false;
            inst.isDraggingJetty = false;
            inst.dragJettyEdgeId = null;
            inst.isDraggingEdgeLabel = false;
            inst.dragEdgeLabelId = null;
            inst.isDraggingDangling = false;
            // Same rollback path as Escape: rolls back the virtual-bend
            // transaction when one is in flight, no-op otherwise.
            if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('OnCancelEdgeEdit');
        }
        this._cancelNodeDrag(inst);
        if (inst.isRubberBand) {
            inst.isRubberBand = false;
            inst.rubberStartDoc = null;
            if (inst.rubberEl) {
                inst.rubberEl.remove();
                inst.rubberEl = null;
            }
        }
        if (inst.isPanning) {
            inst.isPanning = false;
            inst.panStartScreen = null;
            inst.viewBoxStart = null;
        }
        inst.container.style.cursor = inst.toolMode === 'pan' ? 'grab' : '';
    },

    zoomTo: function (container, scale) {
        const inst = this.instances.get(container.id);
        if (!inst) return;
        scale = this._clampScale(scale);
        const vb = this._getViewBox(inst);
        const cx = vb.x + vb.w / 2;
        const cy = vb.y + vb.h / 2;
        const svgRect = inst.svg.getBoundingClientRect();
        const newW = svgRect.width / scale;
        const newH = svgRect.height / scale;
        const nx = cx - newW / 2;
        const ny = cy - newH / 2;
        this._setViewBox(inst, nx, ny, newW, newH);
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', nx, ny, newW, newH);
            inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
        }
    },

    // Fáze 9: `mode` is additive — 'width' scales by the container width ONLY (a taller-than-box
    // diagram stays scrollable instead of shrinking to the height); the default (undefined/null)
    // keeps today's page fit (both axes). The two-argument signature stays valid.
    fitToView: function (container, padding, mode) {
        const inst = this.instances.get(container.id);
        if (!inst) return 1.0;
        padding = (padding != null) ? padding : 40;

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        let hasNodes = false;

        if (inst.scenePane) {
            inst.scenePane.querySelectorAll('g.tm-diagram-node[data-node-id]').forEach(el => {
                const id = el.getAttribute('data-node-id');
                const r = this._nodeRect(inst, id);
                if (!r) return;
                hasNodes = true;
                minX = Math.min(minX, r.x);
                minY = Math.min(minY, r.y);
                maxX = Math.max(maxX, r.x + r.w);
                maxY = Math.max(maxY, r.y + r.h);
            });
        }

        if (!hasNodes) {
            const svgRect = inst.svg.getBoundingClientRect();
            // The empty-document branch obeys the same limits: the raw canvas size can resolve
            // outside [MIN_SCALE, MAX_SCALE] (a small canvas on a big screen zooms past the max).
            const emptyScale = this._clampScale(svgRect.width / inst.canvasW);
            const newW = svgRect.width / emptyScale;
            const newH = svgRect.height / emptyScale;
            this._setViewBox(inst, 0, 0, newW, newH);
            const settled = inst.scale;
            if (inst.dotNetRef) {
                inst.dotNetRef.invokeMethodAsync('OnViewportChanged', 0, 0, newW, newH);
                inst.dotNetRef.invokeMethodAsync('OnZoomChanged', settled);
            }
            return settled;
        }

        const contentW = maxX - minX + padding * 2;
        const contentH = maxY - minY + padding * 2;
        const svgRect = inst.svg.getBoundingClientRect();
        let fitScale;
        let newW, newH, nx, ny;
        if (mode === 'width') {
            // Width mode: the CONTAINER WIDTH over the CONTENT WIDTH decides the scale — the
            // height is ignored, so a tall diagram overflows (scrolls) instead of shrinking.
            // Short content is TOP-ALIGNED in the visible band (the band's top edge pins the
            // content; a centered placement would let it drift when the band resizes).
            fitScale = this._clampScale(Math.min(svgRect.width / contentW, this.FIT_MAX_SCALE));
        } else {
            fitScale = this._clampScale(Math.min(svgRect.width / contentW, svgRect.height / contentH, this.FIT_MAX_SCALE));
        }
        newW = svgRect.width / fitScale;
        newH = svgRect.height / fitScale;
        nx = minX - padding;
        ny = minY - padding;
        this._setViewBox(inst, nx, ny, newW, newH);
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', nx, ny, newW, newH);
            inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
        }
        return fitScale;
    },

    // ── Fixed-position placement (context menu, table inserter) ─────────────
    // The box a position:fixed descendant of `host` resolves against, in viewport coordinates:
    // the viewport, or an ancestor that establishes a containing block for fixed descendants
    // (layout/paint containment, container-type in some engines, transform, filter). MEASURED
    // with an inset:0 probe, never assumed: its rect IS the containing block's padding box.
    _fixedContainingBlock: function (host) {
        const viewport = { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
        if (!host || typeof document === 'undefined' || !document.createElement) return viewport;
        const probe = document.createElement('div');
        probe.setAttribute('aria-hidden', 'true');
        probe.style.cssText = 'position:fixed;left:0;top:0;right:0;bottom:0;margin:0;border:0;padding:0;visibility:hidden;pointer-events:none;';
        host.appendChild(probe);
        const r = probe.getBoundingClientRect();
        probe.remove();
        if (!(r.width > 0 && r.height > 0)) return viewport;
        return { left: r.left, top: r.top, width: r.width, height: r.height };
    },

    // Clamps a w×h box anchored at the VIEWPORT point (x, y) into the visible part of the
    // containing block `cb` (cb ∩ viewport, minus `margin`); returns the cb-LOCAL left/top to
    // write into style, plus the available height of that visible region.
    _placeFixed: function (cb, x, y, w, h, margin) {
        const minX = Math.max(cb.left, 0) + margin;
        const minY = Math.max(cb.top, 0) + margin;
        const maxX = Math.min(cb.left + cb.width, window.innerWidth) - margin;
        const maxY = Math.min(cb.top + cb.height, window.innerHeight) - margin;
        const vx = Math.max(minX, Math.min(x, maxX - w));
        const vy = Math.max(minY, Math.min(y, maxY - h));
        return { left: vx - cb.left, top: vy - cb.top, availHeight: Math.max(0, maxY - minY) };
    },

    openContextMenu: function (container, menuEl) {
        if (!container || !menuEl) return;
        const inst = this.instances.get(container.id);
        if (!inst) return;
        const self = this;
        // Clean up any existing dismiss handlers first
        self._teardownMenu(inst);
        // Flag the menu as open SYNCHRONOUSLY: an Escape typed between this call and the deferred
        // placement (setTimeout 0) must already see `_menuOpen` in _onSearchEscape, otherwise the
        // capture consumer would close a surface UNDER the still-opening menu.
        inst._menuOpen = true;
        setTimeout(function () {
            if (!menuEl) return;
            if (!inst._menuOpen) return; // closed again before the deferred placement ran
            const margin = 8;
            // VIEWPORT coordinates from C# (data-screen-x/y); the style is OUR output and may
            // already be translated by an earlier call — never read it back.
            const readCoord = function (attr, styleProp) {
                const v = parseFloat(menuEl.getAttribute(attr));
                return Number.isFinite(v) ? v : (parseFloat(menuEl.style[styleProp]) || 0);
            };
            let x = readCoord('data-screen-x', 'left');
            let y = readCoord('data-screen-y', 'top');
            // The containing block the FIXED menu resolves against, MEASURED with the probe from
            // the menu's own parent (its real ancestor chain); the editor root is the fallback
            // for hosts that render the menu elsewhere.
            const cb = self._fixedContainingBlock(menuEl.parentElement || container.closest('.tm-diagram-editor'));
            menuEl.style.maxHeight = '';
            let rect = menuEl.getBoundingClientRect();
            const avail = Math.max(160, self._placeFixed(cb, 0, 0, 0, 0, margin).availHeight);
            if (rect.height > avail) {
                menuEl.style.maxHeight = avail + 'px';
                rect = menuEl.getBoundingClientRect();
            }
            if (menuEl.getAttribute('data-anchor') === 'touch') {
                // Opened by a finger long-press: park the finger-sized menu ABOVE the finger so
                // the release never lands on an item (flip below when there is no room).
                const fingerY = y;
                y = fingerY - rect.height - 16;
                if (y < Math.max(cb.top, 0) + margin) y = fingerY + 16;
                x = x + 16;
            }
            const pos = self._placeFixed(cb, x, y, rect.width, rect.height, margin);
            menuEl.style.left = pos.left + 'px';
            menuEl.style.top = pos.top + 'px';

            // (_menuOpen was already set synchronously before this deferred placement.)

            // A11y: roving focus over the native menuitem buttons — focus the first ENABLED item
            // after placement, ArrowUp/Down/Home/End move (no Enter/Space emulation: the items are
            // native <button>s and activate themselves), Escape closes in the key handler below.
            const menuItems = function () {
                const all = menuEl.querySelectorAll ? [...menuEl.querySelectorAll('[role="menuitem"]')] : [];
                return all.filter(it => !it.disabled && it.getAttribute('aria-disabled') !== 'true');
            };
            const items = menuItems();
            items.forEach((it) => { it.tabIndex = -1; });
            if (items.length > 0 && typeof items[0].focus === 'function') {
                items[0].tabIndex = 0;
                items[0].focus({ preventScroll: true });
            }

            inst._menuDismissHandler = function (e) {
                if (e.button !== undefined && e.button !== 0) return; // only a primary press outside
                if (e.target.closest('.tm-diagram-editor__context-menu')) return;
                if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
                self._teardownMenu(inst);
            };
            inst._menuKeyHandler = function (e) {
                if (e.key === 'Escape') {
                    // One gesture closes ONE layer: an Escape an earlier capture consumer already
                    // handled (the search panel, the view menu) is not re-consumed here.
                    if (e.defaultPrevented || e.tmEscapeHandled) return;
                    // Capture-phase consumers (see _onSearchEscape): the open menu owns this
                    // Escape — mark it so the focus trap (and the canvas handler) never see it.
                    e.preventDefault();
                    e.tmEscapeHandled = true;
                    if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
                    self._teardownMenu(inst);
                    return;
                }
                if (e.key === 'Tab') {
                    // Tab closes the menu and lets the default Tab proceed: focus leaves the
                    // closed menu and follows the document's natural tab order (a11y review fix).
                    if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
                    self._teardownMenu(inst);
                    return;
                }
                const step = { ArrowDown: 1, ArrowUp: -1 };
                if (!(e.key in step) && e.key !== 'Home' && e.key !== 'End') return;
                const enabled = menuItems();
                if (enabled.length === 0) return;
                const current = enabled.indexOf(typeof document !== 'undefined' ? document.activeElement : null);
                let next;
                if (e.key === 'Home') next = 0;
                else if (e.key === 'End') next = enabled.length - 1;
                else if (current < 0) next = step[e.key] > 0 ? 0 : enabled.length - 1;
                else next = (current + step[e.key] + enabled.length) % enabled.length;
                enabled.forEach((it) => { it.tabIndex = -1; });
                enabled[next].tabIndex = 0;
                enabled[next].focus({ preventScroll: true });
                e.preventDefault?.();
            };
            // pointerdown in the CAPTURE phase, not mousedown: a touch tap on the canvas must
            // dismiss the menu too, before the pan / edge-draw sessions claim the press.
            document.addEventListener('pointerdown', inst._menuDismissHandler, true);
            // Escape must beat the bubble-phase focus trap (order-independent consumers):
            // the key handler runs in the CAPTURE phase too.
            document.addEventListener('keydown', inst._menuKeyHandler, true);
        }, 0);
    },

    // Removes the dismissal/key listeners, marks the menu closed and returns the focus to the
    // canvas when the menu owned it.
    _teardownMenu: function (inst) {
        if (inst._menuDismissHandler) {
            document.removeEventListener('pointerdown', inst._menuDismissHandler, true);
            inst._menuDismissHandler = null;
        }
        if (inst._menuKeyHandler) {
            document.removeEventListener('keydown', inst._menuKeyHandler, true);
            inst._menuKeyHandler = null;
        }
        inst._menuOpen = false;
        const active = typeof document !== 'undefined' ? document.activeElement : null;
        if (active && active.closest && active.closest('.tm-diagram-editor__context-menu')
            && inst.container && typeof inst.container.focus === 'function') {
            inst.container.focus({ preventScroll: true });
        }
    },

    closeContextMenu: function (container) {
        if (!container) return;
        const inst = this.instances.get(container.id);
        if (!inst) return;
        this._teardownMenu(inst);
    },

    scrollTo: function (container, centreX, centreY) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst) return;
        const vb = this._getViewBox(inst);
        const newX = centreX - vb.w / 2;
        const newY = centreY - vb.h / 2;
        this._setViewBox(inst, newX, newY, vb.w, vb.h);
        if (inst.dotNetRef)
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', newX, newY, vb.w, vb.h);
    },

    zoomToRect: function (container, x, y, w, h, padding) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst || !inst.svg) return;
        padding = (padding != null) ? padding : 40;
        const svgRect = inst.svg.getBoundingClientRect();
        const contentW = w + padding * 2;
        const contentH = h + padding * 2;
        const scale = this._clampScale(Math.min(svgRect.width / contentW, svgRect.height / contentH, this.FIT_MAX_SCALE));
        const newW = svgRect.width / scale;
        const newH = svgRect.height / scale;
        const nx = x + w / 2 - newW / 2;
        const ny = y + h / 2 - newH / 2;
        this._setViewBox(inst, nx, ny, newW, newH);
        if (inst.dotNetRef)
            inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
        if (inst.dotNetRef)
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', nx, ny, newW, newH);
    },

    addPageViewMargin: function (container, margin) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst || !inst.svg) return 1.0;
        const vb = this._getViewBox(inst);
        this._setViewBox(inst, vb.x - margin, vb.y - margin, vb.w + margin * 2, vb.h + margin * 2);
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x - margin, vb.y - margin, vb.w + margin * 2, vb.h + margin * 2);
            // The margin GROWS the viewBox, so the scale shrank — report the FINAL zoom (Fáze 9
            // review: a fit followed by the margin must not leave a pre-margin zoom label).
            inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
        }
        return inst.scale;
    },

    // ── Toolbox drag init ─────────────────────────────────────────────────────

    initToolbox: function (toolboxElement) {
        if (!toolboxElement || toolboxElement._tmdToolboxInit) return;
        toolboxElement._tmdToolboxInit = true;
        toolboxElement.addEventListener('dragstart', function (e) {
            const item = e.target.closest('[data-stencil-id]');
            if (!item) return;
            const type = item.getAttribute('data-stencil-id');
            window.__tmDiagramDragStencil = type;
            e.dataTransfer.setData('text/plain', type);
            e.dataTransfer.effectAllowed = 'copy';
        });
    },

    updateSelectionOutlines: function (container) {
        const inst = this.instances.get(container.id);
        if (inst) this._updateSelection(inst);
    },

    // Live resize (mouse C# drag or the touch/pen session): the finger-sized port hit circles
    // and the quick-connect arrows keep their last-rendered positions while the node geometry
    // changes under them, so the canvas carries `tm-diagram-canvas--resizing` for the gesture's
    // duration and the CSS hides `.tm-diagram-port-hits` and `.tm-diagram-connect-arrows`.
    // C# sets true in TryBeginResize and false on commit/rollback
    // (Blazor re-renders the hits and arrows at the new geometry afterwards).
    setResizing: function (container, resizing) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst || !inst.container) return;
        inst.container.classList.toggle('tm-diagram-canvas--resizing', !!resizing);
    },

    // Scrolls the element's nearest scrollable ancestor just enough to make it visible —
    // the companion to ElementReference.FocusAsync(preventScroll: true), so roving focus
    // (the connect dialog's arrow-key grid) never jolts the page scroll position.
    scrollNearest: function (el) {
        if (el && el.scrollIntoView) el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    },

    getDragStencilId: function () {
        return window.__tmDiagramDragStencil ?? null;
    },

    _applyNodeRotation: function (inst, nodeId, angle) {
        const el = this._nodeEl(inst, nodeId);
        if (!el) return;
        const p = this._parseNodeTransform(el);
        const w = parseFloat(el.getAttribute('data-w') || '0');
        const h = parseFloat(el.getAttribute('data-h') || '0');
        const tr = this._buildNodeTransform(p.x, p.y, angle, w, h);
        el.setAttribute('transform', tr);
        // F5: also rotate the decorator-pane groups (handles, port hits, connect arrows) so
        // every affordance tracks the live rotation. The generic [data-node-id] selector
        // covers whichever per-node groups Blazor rendered.
        if (inst.decoratorPane) {
            inst.decoratorPane.querySelectorAll('[data-node-id="' + nodeId + '"]').forEach(handle => {
                handle.setAttribute('transform', tr);
            });
        }
        this._updateSelection(inst);
    },

    setNodeRotation: function (container, nodeId, angle) {
        const inst = this.instances.get(container ? container.id : null);
        if (!inst) return;
        this._applyNodeRotation(inst, nodeId, angle);
    },

    computeOrthogonalWaypoints: function (x1, y1, side1, x2, y2, side2, routing, sourceSpacing, targetSpacing, srcBounds, tgtBounds, obstacles) {
        return this._computeOrthogonalWaypoints(x1, y1, side1, x2, y2, side2, routing, sourceSpacing, targetSpacing, srcBounds, tgtBounds, obstacles);
    },

    screenToDoc: function (container, clientX, clientY) {
        const inst = this.instances.get(container.id);
        if (!inst) return { x: clientX, y: clientY };
        return this._screenToDoc(inst, clientX, clientY);
    },

    // ── Mobile sheet-visible band centre (tap-insert above the peek sheet) ────
    // The mobile panels sheet is a non-modal bottom drawer whose PANEL covers the lower
    // band of the canvas, so a tap-insert at the viewport centre can land under it.
    // The sheet ROOT spans the whole stage (it is click-through), so only the
    // .tm-drawer__panel marks the covered band.
    // The canvas client band not covered by the open mobile sheet panel:
    // { inst, rect, top, bottom } or null (no instance / zero-size svg / sheet covers everything).
    _visibleBand: function (root) {
        if (!root) return null;
        const canvasEl = root.querySelector('.tm-diagram-canvas');
        const inst = canvasEl ? this.instances.get(canvasEl.id) : null;
        if (!inst || !inst.svg) return null;
        const rect = inst.svg.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return null;
        let bottom = rect.bottom;
        const panel = root.querySelector('.tm-editor-shell__sheet .tm-drawer__panel');
        if (panel) {
            const pr = panel.getBoundingClientRect();
            if (pr.height > 0 && pr.top < bottom && pr.top > rect.top) bottom = pr.top;
        }
        if (bottom <= rect.top) return null;
        return { inst, rect, top: rect.top, bottom };
    },

    // [x, y] in document coordinates — the C# side reads double[] (TryGetVisibleBandCentreAsync).
    visibleBandCentre: function (root) {
        const b = this._visibleBand(root);
        if (!b) return null;
        const p = this._screenToDoc(b.inst, b.rect.left + b.rect.width / 2, (b.top + b.bottom) / 2);
        return [p.x, p.y];
    },

    // UX review (mobile sheet focus): moves focus to the first tile of the host's mobile action
    // bar — the sheet's close button unmounts with the sheet, and the bar is the one reopen
    // path (the shell's own panels toggle is hidden while a bar renders). Resolves false when
    // no bar exists.
    focusFirstAction: function (root) {
        if (!root) return false;
        const el = root.querySelector('.tm-mobile-action-bar__action');
        if (!el) return false;
        el.focus({ preventScroll: true });
        return true;
    },

    // Pans (never zooms) so the doc-space rect lies inside the band above the mobile sheet with
    // `margin` screen px of air. UX review: (1) a live gesture owns the viewBox — the pan is
    // refused while the user drags/pans/rubber-bands/pinches; (2) the sheet panel's snap
    // animation is AWAITED (getAnimations().finished) so the measured band is the settled one —
    // when animations cannot be observed, the panel top is computed from the stage bottom minus
    // the panel's target height instead; (3) a ~500 ms fallback resolves false so the C# await
    // never hangs on a stalled animation.
    revealRectAboveSheet: function (root, x, y, w, h, margin) {
        const self = this;
        return new Promise(function (resolve) {
            const early = self._visibleBand(root);
            if (!early) { resolve(false); return; }
            if (early.inst.isDragging || early.inst.isPanning || early.inst.isRubberBand || early.inst.isPinching) {
                resolve(false); return;
            }

            var settled = false;
            var timer = null;
            var finish = function (ok) {
                if (settled) return;
                settled = true;
                if (timer) clearTimeout(timer);
                resolve(ok);
            };
            timer = setTimeout(function () { finish(false); }, 500);

            var pan = function () {
                requestAnimationFrame(function () { requestAnimationFrame(function () {
                    // The ~500 ms fallback may have won the race while the animation promise was
                    // still pending — a late pan must never touch the viewBox after resolving.
                    if (settled) return;
                    const b = self._visibleBand(root);
                    if (!b) { finish(false); return; }
                    // The wait gave a gesture time to start (a pinch during the snap animation):
                    // a live gesture owns the viewBox, exactly like the early guard above.
                    if (b.inst.isDragging || b.inst.isPanning || b.inst.isRubberBand || b.inst.isPinching) {
                        finish(false); return;
                    }
                    const s = b.inst.scale || 1;
                    const vb = self._getViewBox(b.inst);
                    const m = (margin ?? 16) / s;
                    // The settled band: the panel top either measured after the animation ended,
                    // or computed from the stage bottom and the panel's target height.
                    let bottom = b.bottom;
                    const panel = root.querySelector('.tm-editor-shell__sheet .tm-drawer__panel');
                    if (panel && panel.offsetHeight != null) {
                        const stage = panel.closest ? panel.closest('.tm-editor-shell__stage') : null;
                        const sr = stage ? stage.getBoundingClientRect() : null;
                        if (sr && panel.offsetHeight > 0) {
                            bottom = Math.min(b.rect.bottom, sr.bottom - panel.offsetHeight);
                        }
                    }
                    if (bottom <= b.rect.top) { finish(false); return; }
                    const bandTop = vb.y + (b.top - b.rect.top) / s;
                    const bandBottom = vb.y + (bottom - b.rect.top) / s;
                    const bandLeft = vb.x, bandRight = vb.x + vb.w;
                    let dx = 0, dy = 0;
                    if (h + 2 * m > bandBottom - bandTop || y - m < bandTop) dy = (y - m) - bandTop;
                    else if (y + h + m > bandBottom) dy = (y + h + m) - bandBottom;
                    if (w + 2 * m > bandRight - bandLeft || x - m < bandLeft) dx = (x - m) - bandLeft;
                    else if (x + w + m > bandRight) dx = (x + w + m) - bandRight;
                    if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) { finish(false); return; }
                    self._setViewBox(b.inst, vb.x + dx, vb.y + dy, vb.w, vb.h);
                    if (b.inst.dotNetRef)
                        b.inst.dotNetRef.invokeMethodAsync('OnViewportChanged', vb.x + dx, vb.y + dy, vb.w, vb.h);
                    finish(true);
                }); });
            };

            const panel = root.querySelector('.tm-editor-shell__sheet .tm-drawer__panel');
            const anims = panel && panel.getAnimations ? panel.getAnimations() : [];
            if (anims && anims.length) {
                // The snap transition owns the panel geometry: measure after it settles. A
                // rejected/aborted animation still proceeds (the fallback timer bounds the wait).
                Promise.all(anims.map(function (a) { return a.finished; })).then(pan, pan);
            }
            else {
                pan();
            }
        });
    },

    // ── Table inserter anchor (UX review phase 4) ─────────────────────────────
    // The picker renders at the editor root as a FIXED popover (never inside a toolbar item,
    // which collapses inert into "More"), so Blazor needs the viewport point to pin it to: the
    // Insert trigger while the group sits on the bar, otherwise the toolbar's "More" trigger
    // that now owns the group's commands. Returns null when nothing is measurable (the caller
    // keeps its previous position).
    tableInserterAnchor: function (root) {
        if (!root) return null;
        const insertTrigger = root.querySelector("[data-tm-group='insert'] .tm-dropdown-trigger");
        const onBar = insertTrigger !== null
            && insertTrigger.closest('.tm-toolbar-item--collapsed') === null;
        const anchorEl = onBar ? insertTrigger : root.querySelector('.tm-toolbar-more');
        if (!anchorEl) return null;
        const rect = anchorEl.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) return null;
        // The grid popover is ~200×230; keep it inside the visible containing block. Probe from
        // the popover's own parent (its real ancestor chain) — the editor root is the fallback.
        const width = 208, height = 240, margin = 8, gap = 6;
        const cbHost = root.parentElement || root;
        const pos = this._placeFixed(this._fixedContainingBlock(cbHost), rect.left, rect.bottom + gap, width, height, margin);
        return [pos.left, pos.top];
    },

    // ── Magnetic guidelines ────────────────────────────────────────────────

    _computeSnapGuides: function (inst, dragNodeIds, dragStartPositions, dxDoc, dyDoc) {
        const threshold = 8 / inst.scale; // 8 screen px
        const guides = { x: null, y: null, distances: { x: null, y: null } };
        if (!inst.scenePane) return guides;

        const draggedRects = dragNodeIds.map(id => {
            const start = dragStartPositions[id];
            if (!start) return null;
            const w = start.w ?? 0;
            const h = start.h ?? 0;
            return {
                id,
                left: start.x + dxDoc,
                right: start.x + dxDoc + w,
                top: start.y + dyDoc,
                bottom: start.y + dyDoc + h,
                centerX: start.x + dxDoc + w / 2,
                centerY: start.y + dyDoc + h / 2,
            };
        }).filter(Boolean);

        const allRects = [];
        inst.scenePane.querySelectorAll('.tm-diagram-node').forEach(el => {
            const id = el.getAttribute('data-node-id');
            if (!id || dragNodeIds.includes(id)) return;
            const r = this._nodeRect(inst, id);
            if (!r) return;
            allRects.push({
                id,
                left: r.x,
                right: r.x + r.w,
                top: r.y,
                bottom: r.y + r.h,
                centerX: r.x + r.w / 2,
                centerY: r.y + r.h / 2,
            });
        });

        function findBest(dragged, targets, propNames) {
            let best = null;
            for (const d of dragged) {
                for (const t of targets) {
                    for (const dp of propNames) {
                        for (const tp of propNames) {
                            const delta = t[tp] - d[dp];
                            if (Math.abs(delta) <= threshold) {
                                if (!best || Math.abs(delta) < Math.abs(best.delta)) {
                                    best = { delta, dProp: dp, tProp: tp, dVal: d[dp], tVal: t[tp] };
                                }
                            }
                        }
                    }
                }
            }
            return best;
        }

        const xBest = findBest(draggedRects, allRects, ['left', 'right', 'centerX']);
        const yBest = findBest(draggedRects, allRects, ['top', 'bottom', 'centerY']);

        if (xBest) guides.x = xBest;
        if (yBest) guides.y = yBest;

        // Distance guides
        if (draggedRects.length > 0) {
            const dragBounds = {
                left: Math.min(...draggedRects.map(r => r.left)),
                right: Math.max(...draggedRects.map(r => r.right)),
                top: Math.min(...draggedRects.map(r => r.top)),
                bottom: Math.max(...draggedRects.map(r => r.bottom)),
            };
            const distThreshold = 500; // document px max distance to show label

            // X axis distances
            let leftNeighbor = null;
            let rightNeighbor = null;
            let minLeftGap = Infinity;
            let minRightGap = Infinity;
            for (const t of allRects) {
                const gapLeft = dragBounds.left - t.right;
                const gapRight = t.left - dragBounds.right;
                if (gapLeft >= 0 && gapLeft < minLeftGap) { minLeftGap = gapLeft; leftNeighbor = t; }
                if (gapRight >= 0 && gapRight < minRightGap) { minRightGap = gapRight; rightNeighbor = t; }
            }
            if (leftNeighbor && minLeftGap <= distThreshold) {
                const consistent = allRects.some(t => t !== leftNeighbor && Math.abs((dragBounds.left - t.right) - minLeftGap) < 1);
                guides.distances.x = { value: minLeftGap, from: leftNeighbor.right, to: dragBounds.left, side: 'left', consistent };
            } else if (rightNeighbor && minRightGap <= distThreshold) {
                const consistent = allRects.some(t => t !== rightNeighbor && Math.abs((t.left - dragBounds.right) - minRightGap) < 1);
                guides.distances.x = { value: minRightGap, from: dragBounds.right, to: rightNeighbor.left, side: 'right', consistent };
            }

            // Y axis distances
            let topNeighbor = null;
            let bottomNeighbor = null;
            let minTopGap = Infinity;
            let minBottomGap = Infinity;
            for (const t of allRects) {
                const gapTop = dragBounds.top - t.bottom;
                const gapBottom = t.top - dragBounds.bottom;
                if (gapTop >= 0 && gapTop < minTopGap) { minTopGap = gapTop; topNeighbor = t; }
                if (gapBottom >= 0 && gapBottom < minBottomGap) { minBottomGap = gapBottom; bottomNeighbor = t; }
            }
            if (topNeighbor && minTopGap <= distThreshold) {
                const consistent = allRects.some(t => t !== topNeighbor && Math.abs((dragBounds.top - t.bottom) - minTopGap) < 1);
                guides.distances.y = { value: minTopGap, from: topNeighbor.bottom, to: dragBounds.top, side: 'top', consistent };
            } else if (bottomNeighbor && minBottomGap <= distThreshold) {
                const consistent = allRects.some(t => t !== bottomNeighbor && Math.abs((t.top - dragBounds.bottom) - minBottomGap) < 1);
                guides.distances.y = { value: minBottomGap, from: dragBounds.bottom, to: bottomNeighbor.top, side: 'bottom', consistent };
            }
        }

        return guides;
    },

    _formatDistance: function (inst, px) {
        const unit = (inst.rulerUnit || 'px').toLowerCase();
        const scale = inst.pageScale || 1.0;
        const dpi = 96.0;
        let factor = 1.0;
        switch (unit) {
            case 'pt': factor = 72.0 / dpi; break;
            case 'in': factor = 1.0 / dpi; break;
            case 'mm': factor = 25.4 / dpi; break;
            case 'm': factor = 0.0254 / dpi; break;
        }
        const val = px * factor / scale;
        let s = val.toFixed(2);
        if (s.endsWith('.00')) s = s.slice(0, -3);
        else if (s.endsWith('0')) s = s.slice(0, -1);
        if (s === '-0') s = '0';
        return s + ' ' + unit;
    },

    _computeSnapGuidesForPoint: function (inst, x, y) {
        const threshold = 8 / inst.scale;
        const guides = { x: null, y: null, distances: { x: null, y: null } };
        if (!inst.scenePane) return guides;

        let xBest = null;
        let yBest = null;
        const self = this;

        inst.scenePane.querySelectorAll('.tm-diagram-node').forEach(function (el) {
            const id = el.getAttribute('data-node-id');
            if (!id) return;
            const r = self._nodeRect(inst, id);
            if (!r) return;

            const xTargets = [
                { val: r.left, prop: 'left' },
                { val: r.right, prop: 'right' },
                { val: r.left + r.w / 2, prop: 'centerX' }
            ];
            for (let i = 0; i < xTargets.length; i++) {
                const t = xTargets[i];
                const delta = t.val - x;
                if (Math.abs(delta) <= threshold) {
                    if (!xBest || Math.abs(delta) < Math.abs(xBest.delta)) {
                        xBest = { delta: delta, dProp: 'centerX', tProp: t.prop, dVal: x, tVal: t.val };
                    }
                }
            }

            const yTargets = [
                { val: r.top, prop: 'top' },
                { val: r.bottom, prop: 'bottom' },
                { val: r.top + r.h / 2, prop: 'centerY' }
            ];
            for (let i = 0; i < yTargets.length; i++) {
                const t = yTargets[i];
                const delta = t.val - y;
                if (Math.abs(delta) <= threshold) {
                    if (!yBest || Math.abs(delta) < Math.abs(yBest.delta)) {
                        yBest = { delta: delta, dProp: 'centerY', tProp: t.prop, dVal: y, tVal: t.val };
                    }
                }
            }
        });

        if (xBest) guides.x = xBest;
        if (yBest) guides.y = yBest;
        return guides;
    },

    _drawGuideLines: function (inst, guides) {
        this._clearGuideLines(inst);
        if (!inst.svg) return;
        const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        g.id = 'tm-diagram-snap-guides';
        g.setAttribute('pointer-events', 'none');

        const vb = this._getViewBox(inst);
        const color = '#3b82f6'; // blue-500

        if (guides.x !== null) {
            const x = guides.x.tVal;
            const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
            line.setAttribute('x1', x);
            line.setAttribute('y1', vb.y);
            line.setAttribute('x2', x);
            line.setAttribute('y2', vb.y + vb.h);
            line.setAttribute('stroke', color);
            line.setAttribute('stroke-width', 1 / inst.scale);
            line.setAttribute('stroke-dasharray', (4 / inst.scale) + ',' + (4 / inst.scale));
            g.appendChild(line);
        }
        if (guides.y !== null) {
            const y = guides.y.tVal;
            const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
            line.setAttribute('x1', vb.x);
            line.setAttribute('y1', y);
            line.setAttribute('x2', vb.x + vb.w);
            line.setAttribute('y2', y);
            line.setAttribute('stroke', color);
            line.setAttribute('stroke-width', 1 / inst.scale);
            line.setAttribute('stroke-dasharray', (4 / inst.scale) + ',' + (4 / inst.scale));
            g.appendChild(line);
        }

        // Distance guide lines & labels
        if (guides.distances) {
            if (guides.distances.x) {
                const d = guides.distances.x;
                const midY = vb.y + vb.h / 2;
                const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                line.setAttribute('x1', d.from);
                line.setAttribute('y1', midY);
                line.setAttribute('x2', d.to);
                line.setAttribute('y2', midY);
                line.setAttribute('stroke', '#10b981'); // emerald-500
                line.setAttribute('stroke-width', 1 / inst.scale);
                line.setAttribute('marker-start', 'url(#arrow-emerald)');
                line.setAttribute('marker-end', 'url(#arrow-emerald)');
                g.appendChild(line);
                const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                text.setAttribute('x', (d.from + d.to) / 2);
                text.setAttribute('y', midY - (4 / inst.scale));
                text.setAttribute('text-anchor', 'middle');
                text.setAttribute('class', 'tm-diagram-distance-label');
                text.setAttribute('font-size', (11 / inst.scale));
                text.textContent = this._formatDistance(inst, d.value);
                g.appendChild(text);
            }
            if (guides.distances.y) {
                const d = guides.distances.y;
                const midX = vb.x + vb.w / 2;
                const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                line.setAttribute('x1', midX);
                line.setAttribute('y1', d.from);
                line.setAttribute('x2', midX);
                line.setAttribute('y2', d.to);
                line.setAttribute('stroke', '#10b981');
                line.setAttribute('stroke-width', 1 / inst.scale);
                line.setAttribute('marker-start', 'url(#arrow-emerald)');
                line.setAttribute('marker-end', 'url(#arrow-emerald)');
                g.appendChild(line);
                const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                text.setAttribute('x', midX + (4 / inst.scale));
                text.setAttribute('y', (d.from + d.to) / 2);
                text.setAttribute('text-anchor', 'start');
                text.setAttribute('dominant-baseline', 'middle');
                text.setAttribute('class', 'tm-diagram-distance-label');
                text.setAttribute('font-size', (11 / inst.scale));
                text.textContent = this._formatDistance(inst, d.value);
                g.appendChild(text);
            }
        }

        if (g.childNodes.length > 0) {
            inst.svg.appendChild(g);
        }
    },

    _clearGuideLines: function (inst) {
        if (!inst.svg) return;
        const g = inst.svg.getElementById('tm-diagram-snap-guides');
        if (g) g.remove();
    },

    // ── Layout (dagre) ─────────────────────────────────────────────────────

    runDagreLayout: function (container, nodes, edges, direction) {
        if (typeof dagre === 'undefined') {
            console.warn('dagre not loaded');
            return null;
        }
        const inst = this.instances.get(container.id);
        if (!inst) return null;

        const g = new dagre.graphlib.Graph().setGraph({
            rankdir: direction || 'TB',
            ranksep: 80,
            nodesep: 40,
            marginx: 20,
            marginy: 20,
        }).setDefaultEdgeLabel(function() { return {}; });

        nodes.forEach(function(n) {
            g.setNode(n.id, { width: n.width, height: n.height });
        });

        edges.forEach(function(e) {
            g.setEdge(e.source, e.target);
        });

        dagre.layout(g);

        const result = [];
        g.nodes().forEach(function(v) {
            const node = g.node(v);
            result.push({ id: v, x: node.x - node.width / 2, y: node.y - node.height / 2 });
        });

        return result;
    },

    // ── Tree Layout (d3-hierarchy) ───────────────────────────────────────────

    runTreeLayout: function (container, nodes, edges, direction) {
        if (typeof d3 === 'undefined' || !d3.tree) {
            console.warn('d3-hierarchy not loaded');
            return null;
        }
        const nodeMap = new Map(nodes.map(n => [n.id, n]));
        const inDegree = new Map();
        nodes.forEach(n => inDegree.set(n.id, 0));
        edges.forEach(e => {
            if (inDegree.has(e.target)) inDegree.set(e.target, inDegree.get(e.target) + 1);
        });

        const roots = nodes.filter(n => inDegree.get(n.id) === 0).map(n => n.id);

        let hierarchyData;
        if (roots.length === 0) {
            // Cycle: pick arbitrary root
            hierarchyData = { name: nodes[0].id, children: this._buildTreeChildren(nodes[0].id, nodeMap, edges, new Set()) };
        } else if (roots.length === 1) {
            hierarchyData = { name: roots[0], children: this._buildTreeChildren(roots[0], nodeMap, edges, new Set()) };
        } else {
            const children = roots.map(r => ({ name: r, children: this._buildTreeChildren(r, nodeMap, edges, new Set()) }));
            hierarchyData = { name: '__virtual_root__', children: children };
        }

        const root = d3.hierarchy(hierarchyData);
        const nodeCount = nodes.length;
        const avgSize = Math.max(60, nodes.reduce((s, n) => s + n.width + n.height, 0) / (nodeCount * 2));
        const sizeW = nodeCount * avgSize * 1.5;
        const sizeH = nodeCount * avgSize * 1.2;

        const isHorizontal = direction === 'LR' || direction === 'RL';
        const treeLayout = d3.tree().size(isHorizontal ? [sizeH, sizeW] : [sizeW, sizeH]);
        treeLayout(root);

        const result = [];
        root.descendants().forEach(d => {
            if (d.data.name === '__virtual_root__') return;
            const n = nodeMap.get(d.data.name);
            if (!n) return;
            let x = d.x;
            let y = d.y;
            if (direction === 'BT') {
                y = sizeH - y;
            } else if (direction === 'RL') {
                x = sizeW - x;
            }
            result.push({ id: d.data.name, x: x - n.width / 2, y: y - n.height / 2 });
        });

        return result;
    },

    _buildTreeChildren: function (parentId, nodeMap, edges, visited) {
        visited.add(parentId);
        const children = [];
        edges.forEach(e => {
            if (e.source === parentId && !visited.has(e.target)) {
                children.push({
                    name: e.target,
                    children: this._buildTreeChildren(e.target, nodeMap, edges, visited)
                });
            }
        });
        return children;
    },

    // ── Force-directed Layout (d3-force) ─────────────────────────────────────

    runForceLayout: function (container, nodes, edges, options) {
        if (typeof d3 === 'undefined' || !d3.forceSimulation) {
            console.warn('d3-force not loaded');
            return null;
        }
        const opts = options || {};
        const width = opts.width || 800;
        const height = opts.height || 600;
        const linkDistance = opts.linkDistance || 100;
        const chargeStrength = opts.chargeStrength || -300;
        const collideRadius = opts.collideRadius || 40;
        const ticks = opts.ticks || 300;

        const nodeMap = new Map();
        const simNodes = nodes.map(n => {
            const sn = { id: n.id, x: width / 2 + (Math.random() - 0.5) * 50, y: height / 2 + (Math.random() - 0.5) * 50, width: n.width, height: n.height };
            nodeMap.set(n.id, sn);
            return sn;
        });

        const simLinks = edges.map(e => ({
            source: nodeMap.get(e.source),
            target: nodeMap.get(e.target)
        })).filter(l => l.source && l.target);

        const simulation = d3.forceSimulation(simNodes)
            .force('link', d3.forceLink(simLinks).id(d => d.id).distance(linkDistance))
            .force('charge', d3.forceManyBody().strength(chargeStrength))
            .force('center', d3.forceCenter(width / 2, height / 2))
            .force('collide', d3.forceCollide().radius(d => collideRadius))
            .stop();

        for (let i = 0; i < ticks; i++) {
            simulation.tick();
        }

        const result = [];
        simNodes.forEach(n => {
            const original = nodes.find(x => x.id === n.id);
            const w = original ? original.width : 0;
            const h = original ? original.height : 0;
            const x = Number.isFinite(n.x) ? n.x : width / 2;
            const y = Number.isFinite(n.y) ? n.y : height / 2;
            result.push({ id: n.id, x: x - w / 2, y: y - h / 2 });
        });

        return result;
    },

    // ── Circle Layout ────────────────────────────────────────────────────────

    runCircleLayout: function (container, nodes, options) {
        const opts = options || {};
        const maxSize = Math.max(60, ...nodes.map(n => Math.max(n.width, n.height)));
        const radius = opts.radius || (maxSize * nodes.length / (2 * Math.PI) + maxSize);
        const centerX = opts.centerX || radius + maxSize;
        const centerY = opts.centerY || radius + maxSize;
        const startAngle = opts.startAngle || 0;

        const result = [];
        const count = nodes.length;
        nodes.forEach((n, i) => {
            const angle = startAngle + (2 * Math.PI * i / count);
            const x = centerX + radius * Math.cos(angle);
            const y = centerY + radius * Math.sin(angle);
            result.push({ id: n.id, x: x - n.width / 2, y: y - n.height / 2 });
        });

        return result;
    },

    // ── Grid / Matrix Layout ─────────────────────────────────────────────────

    runGridLayout: function (container, nodes, options) {
        const opts = options || {};
        const cellWidth = opts.cellWidth || 180;
        const cellHeight = opts.cellHeight || 120;
        const padding = opts.padding || 20;
        const columns = opts.columns || Math.ceil(Math.sqrt(nodes.length));

        const result = [];
        nodes.forEach((n, i) => {
            const col = n.gridColumn != null ? n.gridColumn : (i % columns);
            const row = n.gridRow != null ? n.gridRow : Math.floor(i / columns);
            const x = padding + col * (cellWidth + padding);
            const y = padding + row * (cellHeight + padding);
            result.push({ id: n.id, x: x, y: y });
        });

        return result;
    },

    // ── Export ───────────────────────────────────────────────────────────────

    exportViewportAsSvg: function (container) {
        const inst = this.instances.get(container.id);
        if (!inst || !inst.svg) return null;
        const clone = inst.svg.cloneNode(true);
        // Remove interactive / transient layers that should not appear in export
        clone.querySelector('.tm-diagram-decorator-pane')?.remove();
        clone.querySelector('.tm-diagram-overlay-pane')?.remove();
        // Remove any temporary draw path
        clone.querySelectorAll('.tm-diagram-edge-draw-path').forEach(function (el) { el.remove(); });

        // Inline the computed paints of the token-driven surfaces: the serialized SVG is
        // standalone and no longer sees the theme stylesheet, so var()-driven fills/colors
        // would fall back to black in the exported file. Copy what the LIVE canvas computes
        // (including any host override of --tm-diagram-paper / --tm-diagram-node-ink) onto
        // the matching clone elements as inline styles.
        const PAINT_SOURCES = [
            { selector: '.tm-diagram-page', props: ['fill', 'stroke'] },
            { selector: '.tm-diagram-page-backdrop', props: ['fill'] },
            { selector: '.tm-diagram-edge-label__text', props: ['color', 'background-color'] }
        ];
        PAINT_SOURCES.forEach(function (rule) {
            const live = inst.svg.querySelectorAll(rule.selector);
            const clones = clone.querySelectorAll(rule.selector);
            for (let i = 0; i < live.length && i < clones.length; i++) {
                const computed = window.getComputedStyle(live[i]);
                rule.props.forEach(function (prop) {
                    const value = computed.getPropertyValue(prop);
                    if (value) clones[i].style.setProperty(prop, value);
                });
            }
        });

        // The canvas-scoped custom properties must travel too: node bodies, table cells and
        // labels inside the serialized tree reference var(--tm-diagram-node-ink) /
        // var(--tm-diagram-paper), and the standalone export has no stylesheet to resolve them.
        // Setting the computed values on the clone ROOT re-points every var() in the tree.
        const canvasComputed = window.getComputedStyle(inst.svg);
        ['--tm-diagram-node-ink', '--tm-diagram-paper'].forEach(function (prop) {
            const value = canvasComputed.getPropertyValue(prop);
            if (value) clone.style.setProperty(prop, value);
        });

        // Wrap every foreignObject in <switch> with a <rect> fallback so viewers
        // that do not support foreignObject still render a visible placeholder.
        // (draw.io/mxGraph pattern — see planning/DIAGRAM_UNIFIED_SVG_PLAN.md §11.5)
        const SVG_NS = 'http://www.w3.org/2000/svg';
        clone.querySelectorAll('foreignObject').forEach(function (fo) {
            var parent = fo.parentNode;
            if (!parent) return;
            var sw = document.createElementNS(SVG_NS, 'switch');
            fo.setAttribute('requiredFeatures', 'http://www.w3.org/TR/SVG11/feature#Extensibility');
            var fallback = document.createElementNS(SVG_NS, 'rect');
            fallback.setAttribute('x', fo.getAttribute('x') || '0');
            fallback.setAttribute('y', fo.getAttribute('y') || '0');
            fallback.setAttribute('width', fo.getAttribute('width') || '0');
            fallback.setAttribute('height', fo.getAttribute('height') || '0');
            fallback.setAttribute('fill', '#f9fafb');
            fallback.setAttribute('stroke', '#d1d5db');
            fallback.setAttribute('stroke-width', '1');
            parent.insertBefore(sw, fo);
            sw.appendChild(fo);
            sw.appendChild(fallback);
        });
        // Ensure the clone has explicit xmlns for standalone SVG serialization
        if (!clone.getAttribute('xmlns')) {
            clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        }
        return new XMLSerializer().serializeToString(clone);
    },
};

// ── TmDiagramArrowSelect helpers ─────────────────────────────────────────────

window.tmDiagramArrowSelect = {
    _refs: new Map(),

    init: function (menuId, dotNetRef, focusedIndex) {
        const menu = document.getElementById(menuId);
        if (!menu) return;

        // Clean up any previous instance for this menu
        this.destroy(menuId);

        // Move focus into the menu so key events target it
        menu.focus();

        // Capture-phase keydown handler so we intercept ArrowUp/ArrowDown
        // BEFORE they bubble up and scroll the parent panel
        const handler = function (e) {
            switch (e.key) {
                case 'ArrowDown':
                case 'ArrowUp':
                    e.preventDefault();
                    e.stopPropagation();
                    if (e.key === 'ArrowDown')
                        dotNetRef.invokeMethodAsync('OnArrowDown');
                    else
                        dotNetRef.invokeMethodAsync('OnArrowUp');
                    break;
                case 'Enter':
                    e.preventDefault();
                    e.stopPropagation();
                    dotNetRef.invokeMethodAsync('OnEnter');
                    break;
                case 'Escape':
                    e.preventDefault();
                    e.stopPropagation();
                    dotNetRef.invokeMethodAsync('OnEscape');
                    break;
            }
        };

        menu.addEventListener('keydown', handler, true);
        this._refs.set(menuId, { handler: handler, dotNetRef: dotNetRef });

        // Auto-scroll to currently selected value
        const selected = menu.children[focusedIndex];
        if (selected) {
            selected.scrollIntoView({ block: 'nearest' });
        }
    },

    destroy: function (menuId) {
        const menu = document.getElementById(menuId);
        const entry = this._refs.get(menuId);
        if (menu && entry) {
            menu.removeEventListener('keydown', entry.handler, true);
        }
        this._refs.delete(menuId);
    },

    scrollToOption: function (menuId, index) {
        const menu = document.getElementById(menuId);
        if (!menu || index < 0) return;
        const child = menu.children[index];
        if (!child) return;
        child.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
};

// ── Per-node DotNetRef registry for table cells ───────────────────────────
//
// Each TmDiagramStencilShape registers its DotNetObjectReference keyed by
// the diagram Node.Id. The canvas-level dblclick handler (installed in
// _attachEvents) looks up the shape by nodeId and calls StartTableCellEditFromJs
// directly, bypassing Blazor's @ondblclick — which is unreliable on Blazor
// Server because the SignalR round-trip for the preceding click re-renders
// the component and can eat the native dblclick.
window.tmDiagramStencilShape = {
    _refs: new Map(),

    register: function (nodeId, dotNetRef) {
        if (!nodeId || !dotNetRef) return;
        this._refs.set(nodeId, dotNetRef);
    },

    unregister: function (nodeId) {
        if (!nodeId) return;
        this._refs.delete(nodeId);
    },

    getRef: function (nodeId) {
        return this._refs.get(nodeId);
    }
};

// ── TmDiagramMinimap pointer capture ────────────────────────────────────────
//
// The minimap's viewport drag captures the pointer so the gesture survives a
// finger/mouse slipping off the svg mid-drag (the old pointerleave bail-out).
window.tmDiagramMinimap = {
    capturePointer: function (svgElement, pointerId) {
        if (!svgElement || typeof svgElement.setPointerCapture !== 'function' || pointerId == null) return;
        try { svgElement.setPointerCapture(pointerId); } catch { /* capture is an enhancement */ }
    },

    releasePointer: function (svgElement, pointerId) {
        if (!svgElement || typeof svgElement.releasePointerCapture !== 'function' || pointerId == null) return;
        try {
            if (typeof svgElement.hasPointerCapture !== 'function' || svgElement.hasPointerCapture(pointerId)) {
                svgElement.releasePointerCapture(pointerId);
            }
        } catch { /* already released */ }
    }
};
