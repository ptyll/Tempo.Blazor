// Tempo Wireframe Designer – SVG canvas engine
// Pan, zoom, drag, resize, multi-select, toolbox drop, keyboard shortcuts, context menu
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
            gridSize: opts.gridSize || 8,
            showGrid: opts.showGrid !== false,
            canvasW: opts.canvasWidth || 1200,
            canvasH: opts.canvasHeight || 800,

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

            // current selection
            selectedIds: new Set(),

            // scale tracker
            scale: 1.0,

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

        // Detach all DOM event listeners first
        this._detachEvents(inst);

        // Remove JS-injected overlay elements (selection handles, rubber-band rect, context menu)
        inst.svg.querySelectorAll('.tm-wd-selection, .tm-wd-rubber, .tm-wd-ctx-menu').forEach(el => el.remove());

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
        if (inst) inst.readOnly = !!readOnly;
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
        // Blazor re-renders the background rect — just update stored size for fitToView
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

    _attachEvents: function (inst) {
        inst._onMouseDown = (e) => this._onMouseDown(e, inst);
        inst._onMouseMove = (e) => this._onMouseMove(e, inst);
        inst._onMouseUp   = (e) => this._onMouseUp(e, inst);
        inst._onDblClick  = (e) => this._onDblClick(e, inst);
        inst._onWheel     = (e) => this._onWheel(e, inst);
        inst._onKeyDown   = (e) => this._onKeyDown(e, inst);
        inst._onKeyUp     = (e) => this._onKeyUp(e, inst);
        inst._onContextMenu = (e) => this._onContextMenu(e, inst);
        inst._onDragOver  = (e) => this._onDragOver(e, inst);
        inst._onDrop      = (e) => this._onDrop(e, inst);
        inst._onSvgMouseMove = (e) => this._onSvgMouseMove(e, inst);

        inst.svg.addEventListener('mousedown',   inst._onMouseDown);
        inst.svg.addEventListener('dblclick',    inst._onDblClick);
        inst.svg.addEventListener('mousemove',   inst._onSvgMouseMove);
        document.addEventListener('mousemove',   inst._onMouseMove);
        document.addEventListener('mouseup',     inst._onMouseUp);
        inst.svg.addEventListener('wheel',       inst._onWheel, { passive: false });
        inst.svg.addEventListener('contextmenu', inst._onContextMenu);
        inst.svg.addEventListener('dragover',    inst._onDragOver);
        inst.svg.addEventListener('drop',        inst._onDrop);
        document.addEventListener('keydown',     inst._onKeyDown);
        document.addEventListener('keyup',       inst._onKeyUp);
    },

    _detachEvents: function (inst) {
        inst.svg.removeEventListener('mousedown',   inst._onMouseDown);
        inst.svg.removeEventListener('dblclick',    inst._onDblClick);
        inst.svg.removeEventListener('mousemove',   inst._onSvgMouseMove);
        document.removeEventListener('mousemove',   inst._onMouseMove);
        document.removeEventListener('mouseup',     inst._onMouseUp);
        inst.svg.removeEventListener('wheel',       inst._onWheel);
        inst.svg.removeEventListener('contextmenu', inst._onContextMenu);
        inst.svg.removeEventListener('dragover',    inst._onDragOver);
        inst.svg.removeEventListener('drop',        inst._onDrop);
        document.removeEventListener('keydown',     inst._onKeyDown);
        document.removeEventListener('keyup',       inst._onKeyUp);
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
        if (g) g.setAttribute('transform', 'translate(' + x + ', ' + y + ')');
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

    // ── Mouse down ───────────────────────────────────────────────────────────

    _onMouseDown: function (e, inst) {
        if (inst.readOnly && e.button !== 1) return;

        // Focus canvas wrapper so keyboard shortcuts (Delete, arrows, etc.)
        // work immediately after clicking away from property panel inputs.
        const wrap = inst.svg.closest('.tm-wd-canvas-wrap');
        if (wrap) wrap.focus({ preventScroll: true });

        // Middle mouse button OR Space held OR pan mode → pan canvas
        const wantPan = e.button === 1 || inst.spaceHeld || inst.toolMode === 'pan';
        if (wantPan) {
            e.preventDefault();
            inst.isPanning   = true;
            inst.panStart    = { x: e.clientX, y: e.clientY };
            inst.viewBoxStart = this._getViewBox(inst);
            inst.svg.style.cursor = 'grabbing';
            return;
        }

        if (e.button !== 0) return;

        // Resize handle?
        const handle = e.target.closest('[data-handle]');
        if (handle) {
            e.preventDefault();
            e.stopPropagation();
            // Handles live inside .tm-wd-selection which carries data-sel-for (not data-el-id)
            const selG = handle.closest('.tm-wd-selection');
            if (!selG) return;
            inst.isResizing   = true;
            inst.resizeElId   = selG.getAttribute('data-sel-for');
            inst.resizeHandle = handle.getAttribute('data-handle');
            inst.resizeStartSvg  = this._svgPoint(inst, e.clientX, e.clientY);
            inst.resizeStartRect = this._elRect(inst, inst.resizeElId);
            return;
        }

        // Waypoint handle?
        const wpHandle = e.target.closest('.tm-wd-connector__waypoint');
        if (wpHandle) {
            e.preventDefault();
            e.stopPropagation();
            const connG = wpHandle.closest('[data-connector-id]');
            if (!connG) return;
            inst.isDraggingWaypoint = true;
            inst.dragWaypointConnId = connG.getAttribute('data-connector-id');
            inst.dragWaypointIndex = parseInt(wpHandle.getAttribute('data-waypoint-index') || '0', 10);
            inst.dragWaypointHandle = wpHandle;
            inst.dragWaypointStartSvg = this._svgPoint(inst, e.clientX, e.clientY);
            return;
        }

        // Rotate handle?
        const rotHandle = e.target.closest('[data-rotate-handle]');
        if (rotHandle) {
            e.preventDefault();
            e.stopPropagation();
            const selG = rotHandle.closest('.tm-wd-selection');
            if (!selG) return;
            const id = selG.getAttribute('data-sel-for');
            const r = this._elRect(inst, id);
            if (!r) return;
            inst.isRotating = true;
            inst.rotateElId = id;
            inst.rotateCenter = { x: r.x + r.w / 2, y: r.y + r.h / 2 };
            inst.rotateStartAngle = r.rotation || 0;
            inst.rotateStartSvg = this._svgPoint(inst, e.clientX, e.clientY);
            // Calculate initial angle from center to mouse
            const dx = inst.rotateStartSvg.x - inst.rotateCenter.x;
            const dy = inst.rotateStartSvg.y - inst.rotateCenter.y;
            inst.rotateStartMouseAngle = Math.atan2(dy, dx) * 180 / Math.PI;
            return;
        }

        // Connector hit path?
        const connHit = e.target.closest('.tm-wd-connector__hit');
        if (connHit) {
            e.preventDefault();
            e.stopPropagation();
            const connG = connHit.closest('[data-connector-id]');
            const connId = connG ? connG.getAttribute('data-connector-id') : null;
            if (connId) {
                if (e.shiftKey) {
                    if (inst.selectedIds.has(connId)) {
                        inst.selectedIds.delete(connId);
                    } else {
                        inst.selectedIds.add(connId);
                    }
                } else if (inst.selectedIds.has(connId) && inst.toolMode === 'select' && !inst.readOnly) {
                    // Start dragging the connector (all waypoints)
                    inst.isDraggingConnector = true;
                    inst.dragConnectorId = connId;
                    inst.dragConnectorStartSvg = this._svgPoint(inst, e.clientX, e.clientY);
                    inst.dragConnectorCurrentSvg = inst.dragConnectorStartSvg;
                } else {
                    inst.selectedIds = new Set([connId]);
                    inst.dotNetRef.invokeMethodAsync('OnConnectorSelectionChanged', [...inst.selectedIds]);
                }
            }
            return;
        }

        // Element?
        const elG = e.target.closest('[data-el-id]');
        if (elG) {
            // Locked elements cannot be selected, dragged, or resized
            if (elG.getAttribute('data-locked') === 'true') return;

            e.preventDefault();
            const id = elG.getAttribute('data-el-id');
            const type = elG.getAttribute('data-type');

            // Shift+click: toggle selection
            if (e.shiftKey) {
                if (inst.selectedIds.has(id)) {
                    inst.selectedIds.delete(id);
                } else {
                    inst.selectedIds.add(id);
                }
                this._updateHandles(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
                return;
            }

            // If clicked element not already selected, replace selection
            if (!inst.selectedIds.has(id)) {
                inst.selectedIds = new Set([id]);

                // If clicking a group, auto-select its children too
                if (type === '__group__') {
                    inst.svg.querySelectorAll('[data-group-id="' + id + '"]').forEach(child => {
                        const cid = child.getAttribute('data-el-id');
                        if (cid) inst.selectedIds.add(cid);
                    });
                }

                this._updateHandles(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [...inst.selectedIds]);
            }

            // Connector tool mode: start drawing a connector
            if (inst.toolMode === 'connector') {
                inst.isDrawingConnector = true;
                inst.connectorFromId = id;
                inst.connectorStartSvg = this._svgPoint(inst, e.clientX, e.clientY);
                inst.connectorPreview = this._createConnectorPreview(inst, inst.connectorStartSvg.x, inst.connectorStartSvg.y);
                return;
            }

            // Begin drag – include group children
            inst.isDragging = true;
            inst.dragElIds = [...inst.selectedIds];
            // If any selected element is a group, add its children to drag
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
            inst.dragStartSvg = this._svgPoint(inst, e.clientX, e.clientY);
            inst.dragStartPositions = {};
            inst.dragElIds.forEach(eid => {
                const r = this._elRect(inst, eid);
                if (r) inst.dragStartPositions[eid] = { x: r.x, y: r.y };
            });
            // Notify Blazor of drag start so it can snapshot positions for MoveElementsCommand
            inst.dotNetRef.invokeMethodAsync('OnDragStarted', inst.dragElIds);
            inst.svg.style.cursor = 'grabbing';
            return;
        }

        // Empty canvas: rubber-band OR pan (space / middle button handled elsewhere)
        e.preventDefault();
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
        inst.isRubberBand = true;
        inst.rubberStart  = svgPt;
        inst.rubberRect   = this._createRubberRect(inst, svgPt.x, svgPt.y);

        // Clear selection (no shift)
        if (!e.shiftKey && inst.selectedIds.size > 0) {
            inst.selectedIds.clear();
            this._updateHandles(inst);
            inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', []);
        }
    },

    // ── Mouse move ───────────────────────────────────────────────────────────

    _onMouseMove: function (e, inst) {
        if (inst.isResizing) {
            const cur  = this._svgPoint(inst, e.clientX, e.clientY);
            const dx   = cur.x - inst.resizeStartSvg.x;
            const dy   = cur.y - inst.resizeStartSvg.y;
            const orig = inst.resizeStartRect;
            const h    = inst.resizeHandle;
            const minW = 20, minH = 16;

            let { x, y, w, ww: _ww, h: _h } = { x: orig.x, y: orig.y, w: orig.w, h: orig.h };
            let nw = orig.w, nh = orig.h, nx = orig.x, ny = orig.y;

            if (h.includes('e'))  nw = Math.max(minW, orig.w + dx);
            if (h.includes('s'))  nh = Math.max(minH, orig.h + dy);
            if (h.includes('w')) { nw = Math.max(minW, orig.w - dx); nx = orig.x + orig.w - nw; }
            if (h.includes('n')) { nh = Math.max(minH, orig.h - dy); ny = orig.y + orig.h - nh; }

            const snappedX = this._snap(inst, nx);
            const snappedY = this._snap(inst, ny);
            const snappedW = this._snap(inst, nw);
            const snappedH = this._snap(inst, nh);

            const g = this._elGroup(inst, inst.resizeElId);
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
            return;
        }

        if (inst.isRotating && inst.rotateCenter) {
            const cur = this._svgPoint(inst, e.clientX, e.clientY);
            const dx = cur.x - inst.rotateCenter.x;
            const dy = cur.y - inst.rotateCenter.y;
            let angle = Math.atan2(dy, dx) * 180 / Math.PI;
            let delta = angle - inst.rotateStartMouseAngle;
            let newRotation = inst.rotateStartAngle + delta;
            // Snap to 15° when Shift is held
            if (e.shiftKey) {
                newRotation = Math.round(newRotation / 15) * 15;
            }
            const g = this._elGroup(inst, inst.rotateElId);
            if (g) {
                g.setAttribute('data-rotation', newRotation);
                const t = g.getAttribute('transform') || '';
                const tr = t.match(/translate\(\s*([-\d.e+]+)\s*,\s*([-\d.e+]+)\s*\)/);
                const tx = tr ? parseFloat(tr[1]) : 0;
                const ty = tr ? parseFloat(tr[2]) : 0;
                const r = this._elRect(inst, inst.rotateElId);
                const cx = r ? r.w / 2 : 0;
                const cy = r ? r.h / 2 : 0;
                g.setAttribute('transform',
                    'translate(' + tx + ', ' + ty + ') rotate(' + newRotation + ', ' + cx + ', ' + cy + ')');
                this._updateHandles(inst);
            }
            return;
        }

        if (inst.isDragging && inst.dragElIds.length > 0) {
            const cur = this._svgPoint(inst, e.clientX, e.clientY);
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

        if (inst.isRubberBand && inst.rubberRect) {
            const cur = this._svgPoint(inst, e.clientX, e.clientY);
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

        if (inst.isDraggingWaypoint && inst.dragWaypointHandle) {
            const cur = this._svgPoint(inst, e.clientX, e.clientY);
            const dx = cur.x - inst.dragWaypointStartSvg.x;
            const dy = cur.y - inst.dragWaypointStartSvg.y;
            const circle = inst.dragWaypointHandle;
            const newCx = parseFloat(circle.getAttribute('cx') || '0') + dx;
            const newCy = parseFloat(circle.getAttribute('cy') || '0') + dy;
            circle.setAttribute('cx', newCx);
            circle.setAttribute('cy', newCy);
            inst.dragWaypointStartSvg = cur;
            return;
        }

        if (inst.isDraggingConnector && inst.dragConnectorId) {
            const cur = this._svgPoint(inst, e.clientX, e.clientY);
            const dx = cur.x - inst.dragConnectorCurrentSvg.x;
            const dy = cur.y - inst.dragConnectorCurrentSvg.y;
            inst.dragConnectorCurrentSvg = cur;

            // Move all waypoint handles
            const connG = inst.svg.querySelector('[data-connector-id="' + inst.dragConnectorId + '"]');
            if (connG) {
                connG.querySelectorAll('.tm-wd-connector__waypoint').forEach(circle => {
                    const cx = parseFloat(circle.getAttribute('cx') || '0') + dx;
                    const cy = parseFloat(circle.getAttribute('cy') || '0') + dy;
                    circle.setAttribute('cx', cx);
                    circle.setAttribute('cy', cy);
                });
            }
            return;
        }

        if (inst.isDrawingConnector && inst.connectorPreview) {
            const cur = this._svgPoint(inst, e.clientX, e.clientY);
            const d = 'M ' + inst.connectorStartSvg.x + ' ' + inst.connectorStartSvg.y + ' L ' + cur.x + ' ' + cur.y;
            inst.connectorPreview.setAttribute('d', d);
            return;
        }

        if (inst.isPanning && inst.panStart) {
            const vb = inst.viewBoxStart;
            const svgRect = inst.svg.getBoundingClientRect();
            const scaleX = vb.w / svgRect.width;
            const scaleY = vb.h / svgRect.height;
            const dx = (e.clientX - inst.panStart.x) * scaleX;
            const dy = (e.clientY - inst.panStart.y) * scaleY;
            this._setViewBox(inst, vb.x - dx, vb.y - dy, vb.w, vb.h);
        }
    },

    // ── Mouse up ─────────────────────────────────────────────────────────────

    _onMouseUp: function (e, inst) {
        if (inst.isResizing) {
            const id   = inst.resizeElId;
            const rect = this._elRect(inst, id);
            inst.isResizing   = false;
            inst.resizeElId   = null;
            inst.resizeHandle = null;
            inst.resizeStartSvg  = null;
            inst.resizeStartRect = null;
            if (rect) {
                inst.dotNetRef.invokeMethodAsync('OnElementResized', id, rect.x, rect.y, rect.w, rect.h);
            }
            return;
        }

        if (inst.isRotating) {
            const id = inst.rotateElId;
            const rect = this._elRect(inst, id);
            inst.isRotating = false;
            inst.rotateElId = null;
            inst.rotateCenter = null;
            inst.rotateStartAngle = 0;
            inst.rotateStartMouseAngle = 0;
            inst.rotateStartSvg = null;
            if (rect) {
                inst.dotNetRef.invokeMethodAsync('OnElementRotated', id, rect.rotation);
            }
            return;
        }

        if (inst.isDragging) {
            const moves = inst.dragElIds.map(id => {
                const r = this._elRect(inst, id);
                return r ? { id, x: r.x, y: r.y } : null;
            }).filter(Boolean);

            this._updateGuides(inst, null);
            inst.isDragging = false;
            inst.dragElIds  = [];
            inst.dragStartSvg = null;
            inst.dragStartPositions = {};
            this._updateCursor(inst);

            if (moves.length === 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementMoved', moves[0].id, moves[0].x, moves[0].y);
            } else if (moves.length > 1) {
                inst.dotNetRef.invokeMethodAsync('OnElementsMoved', moves);
            }
            return;
        }

        if (inst.isRubberBand) {
            const rubber = inst.rubberRect;
            inst.isRubberBand = false;
            inst.rubberStart  = null;
            inst.rubberRect   = null;

            if (rubber) {
                const rx = parseFloat(rubber.getAttribute('x') || '0');
                const ry = parseFloat(rubber.getAttribute('y') || '0');
                const rw = parseFloat(rubber.getAttribute('width') || '0');
                const rh = parseFloat(rubber.getAttribute('height') || '0');
                rubber.remove();

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
            }
            return;
        }

        if (inst.isDraggingWaypoint) {
            const connId = inst.dragWaypointConnId;
            const circle = inst.dragWaypointHandle;
            const finalX = parseFloat(circle.getAttribute('cx') || '0');
            const finalY = parseFloat(circle.getAttribute('cy') || '0');
            inst.isDraggingWaypoint = false;
            inst.dragWaypointConnId = null;
            inst.dragWaypointIndex = null;
            inst.dragWaypointHandle = null;
            inst.dragWaypointStartSvg = null;
            if (connId !== null) {
                inst.dotNetRef.invokeMethodAsync('OnConnectorWaypointDragged', connId, inst.dragWaypointIndex, finalX, finalY);
            }
            return;
        }

        if (inst.isDraggingConnector) {
            const connId = inst.dragConnectorId;
            const start = inst.dragConnectorStartSvg;
            const end = inst.dragConnectorCurrentSvg;
            inst.isDraggingConnector = false;
            inst.dragConnectorId = null;
            inst.dragConnectorStartSvg = null;
            inst.dragConnectorCurrentSvg = null;
            if (connId !== null && start !== null && end !== null) {
                const dx = end.x - start.x;
                const dy = end.y - start.y;
                if (Math.abs(dx) > 0.01 || Math.abs(dy) > 0.01) {
                    inst.dotNetRef.invokeMethodAsync('OnConnectorDragged', connId, dx, dy);
                }
            }
            return;
        }

        if (inst.isDrawingConnector) {
            const fromId = inst.connectorFromId;
            const preview = inst.connectorPreview;
            inst.isDrawingConnector = false;
            inst.connectorFromId = null;
            inst.connectorStartSvg = null;
            inst.connectorPreview = null;
            if (preview) preview.remove();

            // Find target element under mouse
            const targetEl = e.target.closest('[data-el-id]');
            const toId = targetEl ? targetEl.getAttribute('data-el-id') : null;
            if (fromId && toId && fromId !== toId) {
                inst.dotNetRef.invokeMethodAsync('OnConnectorCreated', fromId, toId);
            }
            return;
        }

        if (inst.isPanning) {
            inst.isPanning    = false;
            inst.panStart     = null;
            inst.viewBoxStart = null;
            this._updateCursor(inst);
            const vb = this._getViewBox(inst);
            inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
        }
    },

    // ── Double click ─────────────────────────────────────────────────────────

    _onDblClick: function (e, inst) {
        if (inst.readOnly) return;

        // Double-click on connector path → add waypoint
        const connHit = e.target.closest('.tm-wd-connector__hit');
        if (connHit) {
            e.preventDefault();
            e.stopPropagation();
            const connG = connHit.closest('[data-connector-id]');
            const connId = connG ? connG.getAttribute('data-connector-id') : null;
            if (!connId) return;

            const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
            const path = connG.querySelector('.tm-wd-connector__path');
            if (!path) return;

            const closest = this._closestPointOnPath(path, svgPt.x, svgPt.y);
            inst.dotNetRef.invokeMethodAsync('OnConnectorWaypointAdded', connId, closest.x, closest.y);
            return;
        }
    },

    // ── SVG mouse move (for ruler cursor) ────────────────────────────────────

    _onSvgMouseMove: function (e, inst) {
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
        inst.dotNetRef.invokeMethodAsync('OnCanvasMouseMoved', svgPt.x, svgPt.y);
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
        const factor = e.deltaY > 0 ? 1.1 : 0.9;
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
        inst.dotNetRef.invokeMethodAsync('OnZoomChanged', newScale);
    },

    // ── Context menu ─────────────────────────────────────────────────────────

    _onContextMenu: function (e, inst) {
        e.preventDefault();
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);

        // Connector hit path?
        const connHit = e.target.closest('.tm-wd-connector__hit');
        if (connHit) {
            const connG = connHit.closest('[data-connector-id]');
            const connId = connG ? connG.getAttribute('data-connector-id') : null;
            if (connId) {
                inst.dotNetRef.invokeMethodAsync('OnConnectorContextMenu', connId, e.clientX, e.clientY);
                return;
            }
        }

        const elG   = e.target.closest('[data-el-id]');
        const id    = elG ? elG.getAttribute('data-el-id') : null;

        if (id) {
            inst.dotNetRef.invokeMethodAsync('OnElementContextMenu', id, e.clientX, e.clientY);
        } else {
            inst.dotNetRef.invokeMethodAsync('OnCanvasContextMenu', svgPt.x, svgPt.y, e.clientX, e.clientY);
        }
    },

    openContextMenu: function (container, menuEl) {
        if (!container || !menuEl) return;
        const inst = this.instances.get(container.id);
        if (!inst) return;
        if (inst._menuDismissHandler) {
            document.removeEventListener('mousedown', inst._menuDismissHandler);
            document.removeEventListener('keydown', inst._menuKeyHandler);
            inst._menuDismissHandler = null;
            inst._menuKeyHandler = null;
        }
        setTimeout(function () {
            if (!menuEl) return;
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
                if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
                document.removeEventListener('mousedown', inst._menuDismissHandler);
                document.removeEventListener('keydown', inst._menuKeyHandler);
                inst._menuDismissHandler = null;
                inst._menuKeyHandler = null;
            };
            inst._menuKeyHandler = function (e) {
                if (e.key === 'Escape') {
                    if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('CloseContextMenu');
                    document.removeEventListener('mousedown', inst._menuDismissHandler);
                    document.removeEventListener('keydown', inst._menuKeyHandler);
                    inst._menuDismissHandler = null;
                    inst._menuKeyHandler = null;
                }
            };
            document.addEventListener('mousedown', inst._menuDismissHandler);
            document.addEventListener('keydown', inst._menuKeyHandler);
        }, 0);
    },

    closeContextMenu: function (container) {
        if (!container) return;
        const inst = this.instances.get(container.id);
        if (!inst) return;
        if (inst._menuDismissHandler) {
            document.removeEventListener('mousedown', inst._menuDismissHandler);
            document.removeEventListener('keydown', inst._menuKeyHandler);
            inst._menuDismissHandler = null;
            inst._menuKeyHandler = null;
        }
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
        // The layers panel owns every key typed inside it (radio roaming, Alt+Arrow reorder, Delete/Escape on a
        // control): none of them may also act on the canvas selection (nudge, delete, clear).
        if (e.target.closest?.('.tm-wd-layers')) return;

        const ids = [...inst.selectedIds];

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
            const step = e.shiftKey ? 10 : 1;
            const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
            const dy = e.key === 'ArrowUp'   ? -step : e.key === 'ArrowDown'  ? step : 0;
            ids.forEach(id => {
                const r = this._elRect(inst, id);
                if (!r) return;
                this._setElTransform(inst, id, r.x + dx, r.y + dy);
            });
            this._updateHandles(inst);
            // Notify Blazor of final positions
            const moves = ids.map(id => {
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
            if (inst.selectedIds.size === 1 && !r.locked) {
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
                    dot.setAttribute('x',      h.cx - 4);
                    dot.setAttribute('y',      h.cy - 4);
                    dot.setAttribute('width',  8);
                    dot.setAttribute('height', 8);
                    dot.setAttribute('rx',     2);
                    dot.style.fill = 'var(--tm-wd-handle-fill)';
                    dot.style.stroke = 'var(--tm-wd-accent)';
                    dot.setAttribute('stroke-width', '1.5');
                    dot.setAttribute('data-handle', h.key);
                    dot.setAttribute('pointer-events', 'all');
                    dot.style.cursor = h.cursor;
                    selG.appendChild(dot);
                });

                // Rotation handle – circle above the element
                const rotHandle = document.createElementNS(ns, 'circle');
                rotHandle.setAttribute('cx', r.x + r.w / 2);
                rotHandle.setAttribute('cy', r.y - 12);
                rotHandle.setAttribute('r', 5);
                rotHandle.style.fill = 'var(--tm-wd-handle-fill)';
                rotHandle.style.stroke = 'var(--tm-wd-accent)';
                rotHandle.setAttribute('stroke-width', '1.5');
                rotHandle.setAttribute('data-rotate-handle', 'true');
                rotHandle.setAttribute('pointer-events', 'all');
                rotHandle.style.cursor = 'grab';
                selG.appendChild(rotHandle);

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
        return fitScale;
    },

    // ── Pan to canvas origin ──────────────────────────────────────────────────

    resetView: function (svgElement) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        const svgRect = inst.svg.getBoundingClientRect();
        this._setViewBox(inst, 0, 0, svgRect.width, svgRect.height);
        inst.scale = 1.0;
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
            '.tm-wd-selection, .tm-wd-rubber, .tm-wd-grid-overlay, .tm-wd-ruler, .tm-wd-waypoint-handle, .tm-wd-rotate-handle, .tm-wd-resize-handle, .tm-wd-connector__waypoint, .tm-wd-guide, .tm-wd-connector-preview'
        ).forEach(el => el.remove());
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
    },

    // ── File download helper (used by C# export) ──────────────────────────────

    downloadFile: function (anchorEl, fileName, mimeType, base64Data) {
        const dataUrl = 'data:' + mimeType + ';base64,' + base64Data;
        anchorEl.href     = dataUrl;
        anchorEl.download = fileName;
        anchorEl.click();
        // Clean up to avoid stale blob URLs
        setTimeout(() => { anchorEl.href = ''; }, 1000);
    },
};
