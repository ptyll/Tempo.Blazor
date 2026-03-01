// Tempo Workflow Designer - SVG drag, pan, zoom, transition creation
window.tmWorkflowDesigner = {
    instances: new Map(),

    init: function (svgElement, dotNetRef, readOnly) {
        const id = svgElement.id || 'wf-' + Math.random().toString(36).substr(2, 9);
        svgElement.id = id;

        const instance = {
            svg: svgElement,
            dotNetRef: dotNetRef,
            readOnly: !!readOnly,
            // drag state node
            isDragging: false,
            draggedStateId: null,
            dragStartPoint: null,
            dragStartNodePos: null,
            // pan
            isPanning: false,
            panStart: null,
            viewBoxStart: null,
            // zoom
            scale: 1.0,
            // transition creation
            isCreatingTransition: false,
            transitionFromId: null,
            tempLine: null
        };

        this.instances.set(id, instance);
        this._attachEvents(instance);
        return id;
    },

    destroy: function (svgElement) {
        const id = svgElement.id;
        const instance = this.instances.get(id);
        if (instance) {
            this._detachEvents(instance);
            this.instances.delete(id);
        }
    },

    setReadOnly: function (svgElement, readOnly) {
        const instance = this.instances.get(svgElement.id);
        if (instance) instance.readOnly = !!readOnly;
    },

    // ── Event attachment ──

    _attachEvents: function (inst) {
        inst._onMouseDown = (e) => this._onMouseDown(e, inst);
        inst._onMouseMove = (e) => this._onMouseMove(e, inst);
        inst._onMouseUp = (e) => this._onMouseUp(e, inst);
        inst._onWheel = (e) => this._onWheel(e, inst);
        inst._onContextMenu = (e) => this._onContextMenu(e, inst);

        inst.svg.addEventListener('mousedown', inst._onMouseDown);
        document.addEventListener('mousemove', inst._onMouseMove);
        document.addEventListener('mouseup', inst._onMouseUp);
        inst.svg.addEventListener('wheel', inst._onWheel, { passive: false });
        inst.svg.addEventListener('contextmenu', inst._onContextMenu);
    },

    _detachEvents: function (inst) {
        inst.svg.removeEventListener('mousedown', inst._onMouseDown);
        document.removeEventListener('mousemove', inst._onMouseMove);
        document.removeEventListener('mouseup', inst._onMouseUp);
        inst.svg.removeEventListener('wheel', inst._onWheel);
        inst.svg.removeEventListener('contextmenu', inst._onContextMenu);
    },

    // ── Coordinate helpers ──

    _svgPoint: function (inst, clientX, clientY) {
        const pt = inst.svg.createSVGPoint();
        pt.x = clientX;
        pt.y = clientY;
        const ctm = inst.svg.getScreenCTM();
        if (!ctm) return { x: 0, y: 0 };
        const svgPt = pt.matrixTransform(ctm.inverse());
        return { x: svgPt.x, y: svgPt.y };
    },

    _getViewBox: function (inst) {
        const vb = inst.svg.viewBox.baseVal;
        return { x: vb.x, y: vb.y, w: vb.width, h: vb.height };
    },

    _setViewBox: function (inst, x, y, w, h) {
        inst.svg.setAttribute('viewBox', x + ' ' + y + ' ' + w + ' ' + h);
    },

    // ── Mouse down ──

    _onMouseDown: function (e, inst) {
        if (e.button !== 0) return; // left button only

        // Check if clicked on a connection port
        const port = e.target.closest('[data-port]');
        if (port && !inst.readOnly) {
            e.preventDefault();
            e.stopPropagation();
            const stateId = port.getAttribute('data-port');
            inst.isCreatingTransition = true;
            inst.transitionFromId = stateId;
            inst.dragStartPoint = this._svgPoint(inst, e.clientX, e.clientY);
            this._createTempLine(inst);
            return;
        }

        // Check if clicked on a state node
        const stateG = e.target.closest('[data-state-id]');
        if (stateG && !inst.readOnly) {
            e.preventDefault();
            inst.isDragging = true;
            inst.draggedStateId = stateG.getAttribute('data-state-id');
            inst.dragStartPoint = this._svgPoint(inst, e.clientX, e.clientY);

            // Parse current transform to get initial node position
            const transform = stateG.getAttribute('transform') || '';
            const match = transform.match(/translate\(\s*([\d.e+-]+)\s*,\s*([\d.e+-]+)\s*\)/);
            inst.dragStartNodePos = match
                ? { x: parseFloat(match[1]), y: parseFloat(match[2]) }
                : { x: 0, y: 0 };

            inst.svg.style.cursor = 'grabbing';
            return;
        }

        // Otherwise: pan canvas
        if (!inst.isCreatingTransition) {
            e.preventDefault();
            inst.isPanning = true;
            inst.panStart = { x: e.clientX, y: e.clientY };
            inst.viewBoxStart = this._getViewBox(inst);
            inst.svg.style.cursor = 'move';
        }
    },

    // ── Mouse move ──

    _onMouseMove: function (e, inst) {
        if (inst.isDragging && inst.draggedStateId) {
            const current = this._svgPoint(inst, e.clientX, e.clientY);
            const dx = current.x - inst.dragStartPoint.x;
            const dy = current.y - inst.dragStartPoint.y;
            const newX = inst.dragStartNodePos.x + dx;
            const newY = inst.dragStartNodePos.y + dy;

            // Live preview: move the SVG group directly
            const stateG = inst.svg.querySelector('[data-state-id="' + inst.draggedStateId + '"]');
            if (stateG) {
                stateG.setAttribute('transform', 'translate(' + newX + ', ' + newY + ')');
            }
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
            return;
        }

        if (inst.isCreatingTransition && inst.tempLine) {
            const current = this._svgPoint(inst, e.clientX, e.clientY);
            inst.tempLine.setAttribute('x2', current.x);
            inst.tempLine.setAttribute('y2', current.y);
            return;
        }
    },

    // ── Mouse up ──

    _onMouseUp: function (e, inst) {
        if (inst.isDragging && inst.draggedStateId) {
            const current = this._svgPoint(inst, e.clientX, e.clientY);
            const dx = current.x - inst.dragStartPoint.x;
            const dy = current.y - inst.dragStartPoint.y;
            const newX = inst.dragStartNodePos.x + dx;
            const newY = inst.dragStartNodePos.y + dy;

            const stateId = inst.draggedStateId;
            inst.isDragging = false;
            inst.draggedStateId = null;
            inst.dragStartPoint = null;
            inst.dragStartNodePos = null;
            inst.svg.style.cursor = '';

            // Notify Blazor
            inst.dotNetRef.invokeMethodAsync('OnNodeDragged', stateId, newX, newY);
            return;
        }

        if (inst.isPanning) {
            inst.isPanning = false;
            inst.panStart = null;
            inst.viewBoxStart = null;
            inst.svg.style.cursor = '';

            // Notify Blazor of final viewBox
            const vb = this._getViewBox(inst);
            inst.dotNetRef.invokeMethodAsync('OnViewBoxChanged', vb.x, vb.y, vb.w, vb.h);
            return;
        }

        if (inst.isCreatingTransition) {
            // Check if dropped on a state
            const targetState = e.target.closest('[data-state-id]');
            const toId = targetState ? targetState.getAttribute('data-state-id') : null;

            this._removeTempLine(inst);
            const fromId = inst.transitionFromId;
            inst.isCreatingTransition = false;
            inst.transitionFromId = null;

            if (toId && toId !== fromId) {
                inst.dotNetRef.invokeMethodAsync('OnTransitionCreated', fromId, toId);
            }
            return;
        }
    },

    // ── Wheel zoom ──

    _onWheel: function (e, inst) {
        e.preventDefault();
        const delta = e.deltaY > 0 ? 1.1 : 0.9;
        const vb = this._getViewBox(inst);

        // Zoom toward cursor
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
        const newW = vb.w * delta;
        const newH = vb.h * delta;

        // Clamp scale (0.25x to 4x of original)
        const origW = 800;
        const scale = origW / newW;
        if (scale < 0.25 || scale > 4.0) return;

        const newX = svgPt.x - (svgPt.x - vb.x) * delta;
        const newY = svgPt.y - (svgPt.y - vb.y) * delta;

        this._setViewBox(inst, newX, newY, newW, newH);
        inst.scale = scale;

        inst.dotNetRef.invokeMethodAsync('OnZoomChanged', scale);
    },

    // ── Context menu ──

    _onContextMenu: function (e, inst) {
        if (inst.readOnly) return;
        e.preventDefault();
        const svgPt = this._svgPoint(inst, e.clientX, e.clientY);
        const stateG = e.target.closest('[data-state-id]');
        const stateId = stateG ? stateG.getAttribute('data-state-id') : null;
        const transitionG = stateId ? null : e.target.closest('[data-transition-id]');
        const transitionId = transitionG ? transitionG.getAttribute('data-transition-id') : null;
        // Send offset relative to the canvas container (parent of SVG)
        var container = inst.svg.parentElement;
        var rect = container ? container.getBoundingClientRect() : inst.svg.getBoundingClientRect();
        var offsetX = e.clientX - rect.left;
        var offsetY = e.clientY - rect.top;
        inst.dotNetRef.invokeMethodAsync('OnContextMenu', svgPt.x, svgPt.y, offsetX, offsetY, stateId, transitionId);
    },

    // ── Temp line for transition creation ──

    _createTempLine: function (inst) {
        const ns = 'http://www.w3.org/2000/svg';
        const line = document.createElementNS(ns, 'line');
        line.setAttribute('x1', inst.dragStartPoint.x);
        line.setAttribute('y1', inst.dragStartPoint.y);
        line.setAttribute('x2', inst.dragStartPoint.x);
        line.setAttribute('y2', inst.dragStartPoint.y);
        line.setAttribute('stroke', '#3b82f6');
        line.setAttribute('stroke-width', '2');
        line.setAttribute('stroke-dasharray', '6 3');
        line.setAttribute('pointer-events', 'none');
        line.classList.add('tm-wf-temp-line');
        inst.svg.appendChild(line);
        inst.tempLine = line;
    },

    _removeTempLine: function (inst) {
        if (inst.tempLine && inst.tempLine.parentNode) {
            inst.tempLine.parentNode.removeChild(inst.tempLine);
        }
        inst.tempLine = null;
    },

    // ── Programmatic zoom ──

    zoomTo: function (svgElement, scale) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return;
        const vb = this._getViewBox(inst);
        const cx = vb.x + vb.w / 2;
        const cy = vb.y + vb.h / 2;
        const newW = 800 / scale;
        const newH = 500 / scale;
        this._setViewBox(inst, cx - newW / 2, cy - newH / 2, newW, newH);
        inst.scale = scale;
    },

    fitToView: function (svgElement, padding) {
        const inst = this.instances.get(svgElement.id);
        if (!inst) return 1.0;
        padding = padding || 40;

        // Find bounding box of all state nodes
        const nodes = inst.svg.querySelectorAll('[data-state-id]');
        if (nodes.length === 0) return 1.0;

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        nodes.forEach(function (g) {
            const t = g.getAttribute('transform') || '';
            const m = t.match(/translate\(\s*([\d.e+-]+)\s*,\s*([\d.e+-]+)\s*\)/);
            if (m) {
                const x = parseFloat(m[1]);
                const y = parseFloat(m[2]);
                minX = Math.min(minX, x);
                minY = Math.min(minY, y);
                maxX = Math.max(maxX, x + 140); // NodeW
                maxY = Math.max(maxY, y + 50);  // NodeH
            }
        });

        if (!isFinite(minX)) return 1.0;

        const contentW = maxX - minX + padding * 2;
        const contentH = maxY - minY + padding * 2;
        const svgRect = inst.svg.getBoundingClientRect();
        const scaleX = svgRect.width / contentW;
        const scaleY = svgRect.height / contentH;
        const fitScale = Math.min(scaleX, scaleY, 2.0);

        const newW = svgRect.width / fitScale;
        const newH = svgRect.height / fitScale;
        this._setViewBox(inst, minX - padding, minY - padding, newW, newH);
        inst.scale = fitScale;
        return fitScale;
    }
};
