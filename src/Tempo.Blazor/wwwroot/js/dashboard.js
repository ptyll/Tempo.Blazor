// Tempo Dashboard - Drag & Drop and Resize functionality
window.tempoDashboard = {
    dashboards: new Map(),

    init: function (element, dotNetRef) {
        const id = element.id || Math.random().toString(36).substr(2, 9);
        element.id = id;

        const dashboard = {
            element: element,
            dotNetRef: dotNetRef,
            isDragging: false,
            isResizing: false,
            dragStartX: 0,
            dragStartY: 0,
            initialX: 0,
            initialY: 0,
            draggedWidget: null,
            resizeWidget: null,
            resizeDirection: null,
            initialWidth: 0,
            initialHeight: 0,
            gridColumns: 12,
            rowHeight: 60,
            gap: 16
        };

        this.dashboards.set(id, dashboard);
        this.attachEvents(dashboard);

        return id;
    },

    attachEvents: function (dashboard) {
        const grid = dashboard.element.querySelector('.tm-dashboard-grid');
        if (!grid) return;

        dashboard._grid = grid;

        // Store bound handlers for cleanup
        dashboard._onDragStart = (e) => this.onDragStart(e, dashboard);
        dashboard._onDragOver = (e) => this.onDragOver(e, dashboard);
        dashboard._onDrop = (e) => this.onDrop(e, dashboard);
        dashboard._onDragEnd = (e) => this.onDragEnd(e, dashboard);
        dashboard._onMouseDown = (e) => this.onMouseDown(e, dashboard);
        dashboard._onMouseMove = (e) => this.onMouseMove(e, dashboard);
        dashboard._onMouseUp = (e) => this.onMouseUp(e, dashboard);
        dashboard._onDragEnter = (e) => e.preventDefault();
        dashboard._onDragLeave = (e) => e.preventDefault();

        // Drag events
        grid.addEventListener('dragstart', dashboard._onDragStart);
        grid.addEventListener('dragover', dashboard._onDragOver);
        grid.addEventListener('drop', dashboard._onDrop);
        grid.addEventListener('dragend', dashboard._onDragEnd);

        // Mouse events for resize
        grid.addEventListener('mousedown', dashboard._onMouseDown);
        document.addEventListener('mousemove', dashboard._onMouseMove);
        document.addEventListener('mouseup', dashboard._onMouseUp);

        // Prevent default drag behaviors
        grid.addEventListener('dragenter', dashboard._onDragEnter);
        grid.addEventListener('dragleave', dashboard._onDragLeave);
    },

    onDragStart: function (e, dashboard) {
        const widget = e.target.closest('.tm-widget');
        if (!widget) return;

        const handle = e.target.closest('.tm-widget-drag-handle, .tm-widget-header');
        if (!handle) {
            e.preventDefault();
            return;
        }

        dashboard.isDragging = true;
        dashboard.draggedWidget = widget;
        dashboard.dragStartX = e.clientX;
        dashboard.dragStartY = e.clientY;

        // Get initial grid position
        const rect = widget.getBoundingClientRect();
        const gridRect = dashboard.element.querySelector('.tm-dashboard-grid').getBoundingClientRect();

        dashboard.initialX = rect.left - gridRect.left;
        dashboard.initialY = rect.top - gridRect.top;

        widget.classList.add('tm-widget--dragging');
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', widget.dataset.instanceId);
    },

    onDragOver: function (e, dashboard) {
        if (!dashboard.isDragging) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        // Calculate drop position
        const grid = dashboard.element.querySelector('.tm-dashboard-grid');
        const gridRect = grid.getBoundingClientRect();

        const x = e.clientX - gridRect.left;
        const y = e.clientY - gridRect.top;

        // Snap to grid
        const colWidth = (gridRect.width - (dashboard.gridColumns - 1) * dashboard.gap) / dashboard.gridColumns;
        const targetCol = Math.max(0, Math.min(dashboard.gridColumns - 1, Math.round(x / (colWidth + dashboard.gap))));
        const targetRow = Math.max(0, Math.round(y / (dashboard.rowHeight + dashboard.gap)));

        // Update preview
        this.updateDropPreview(dashboard, targetCol, targetRow);
    },

    onDrop: function (e, dashboard) {
        if (!dashboard.isDragging) return;
        e.preventDefault();

        const grid = dashboard.element.querySelector('.tm-dashboard-grid');
        const gridRect = grid.getBoundingClientRect();

        const x = e.clientX - gridRect.left;
        const y = e.clientY - gridRect.top;

        // Snap to grid
        const colWidth = (gridRect.width - (dashboard.gridColumns - 1) * dashboard.gap) / dashboard.gridColumns;
        const targetCol = Math.max(0, Math.min(dashboard.gridColumns - 1, Math.round(x / (colWidth + dashboard.gap))));
        const targetRow = Math.max(0, Math.round(y / (dashboard.rowHeight + dashboard.gap)));

        // Notify Blazor
        if (dashboard.dotNetRef && dashboard.draggedWidget) {
            const instanceId = dashboard.draggedWidget.dataset.instanceId;
            dashboard.dotNetRef.invokeMethodAsync('OnGridPositionChanged', instanceId, targetCol, targetRow);
        }

        this.clearDropPreview(dashboard);
    },

    onDragEnd: function (e, dashboard) {
        if (dashboard.draggedWidget) {
            dashboard.draggedWidget.classList.remove('tm-widget--dragging');
        }
        dashboard.isDragging = false;
        dashboard.draggedWidget = null;
        this.clearDropPreview(dashboard);
    },

    onMouseDown: function (e, dashboard) {
        const handle = e.target.closest('.tm-widget-resize-handle');
        if (!handle) return;

        const widget = handle.closest('.tm-widget');
        if (!widget) return;

        e.preventDefault();
        e.stopPropagation();

        dashboard.isResizing = true;
        dashboard.resizeWidget = widget;
        dashboard.resizeDirection = this.getResizeDirection(handle);
        dashboard.dragStartX = e.clientX;
        dashboard.dragStartY = e.clientY;

        // Get initial size
        const rect = widget.getBoundingClientRect();
        const gridRect = dashboard.element.querySelector('.tm-dashboard-grid').getBoundingClientRect();

        dashboard.initialWidth = rect.width;
        dashboard.initialHeight = rect.height;
        dashboard.initialX = rect.left - gridRect.left;
        dashboard.initialY = rect.top - gridRect.top;

        widget.classList.add('tm-widget--resizing');
    },

    onMouseMove: function (e, dashboard) {
        if (!dashboard.isResizing || !dashboard.resizeWidget) return;

        const deltaX = e.clientX - dashboard.dragStartX;
        const deltaY = e.clientY - dashboard.dragStartY;

        let newWidth = dashboard.initialWidth;
        let newHeight = dashboard.initialHeight;
        let newX = dashboard.initialX;
        let newY = dashboard.initialY;

        // Calculate new dimensions based on resize direction
        switch (dashboard.resizeDirection) {
            case 'e':
                newWidth = dashboard.initialWidth + deltaX;
                break;
            case 'w':
                newWidth = dashboard.initialWidth - deltaX;
                newX = dashboard.initialX + deltaX;
                break;
            case 's':
                newHeight = dashboard.initialHeight + deltaY;
                break;
            case 'n':
                newHeight = dashboard.initialHeight - deltaY;
                newY = dashboard.initialY + deltaY;
                break;
            case 'se':
                newWidth = dashboard.initialWidth + deltaX;
                newHeight = dashboard.initialHeight + deltaY;
                break;
            case 'sw':
                newWidth = dashboard.initialWidth - deltaX;
                newX = dashboard.initialX + deltaX;
                newHeight = dashboard.initialHeight + deltaY;
                break;
            case 'ne':
                newWidth = dashboard.initialWidth + deltaX;
                newHeight = dashboard.initialHeight - deltaY;
                newY = dashboard.initialY + deltaY;
                break;
            case 'nw':
                newWidth = dashboard.initialWidth - deltaX;
                newX = dashboard.initialX + deltaX;
                newHeight = dashboard.initialHeight - deltaY;
                newY = dashboard.initialY + deltaY;
                break;
        }

        // Apply constraints
        newWidth = Math.max(120, newWidth); // Min width
        newHeight = Math.max(80, newHeight); // Min height

        // Update widget visual
        dashboard.resizeWidget.style.width = newWidth + 'px';
        dashboard.resizeWidget.style.height = newHeight + 'px';
    },

    onMouseUp: function (e, dashboard) {
        if (!dashboard.isResizing || !dashboard.resizeWidget) return;

        const widget = dashboard.resizeWidget;
        const rect = widget.getBoundingClientRect();
        const gridRect = dashboard.element.querySelector('.tm-dashboard-grid').getBoundingClientRect();

        // Calculate grid units
        const colWidth = (gridRect.width - (dashboard.gridColumns - 1) * dashboard.gap) / dashboard.gridColumns;
        const widthInCols = Math.round(rect.width / (colWidth + dashboard.gap));
        const heightInRows = Math.round(rect.height / (dashboard.rowHeight + dashboard.gap));

        // Notify Blazor
        if (dashboard.dotNetRef) {
            const instanceId = widget.dataset.instanceId;
            dashboard.dotNetRef.invokeMethodAsync('OnWidgetResized', instanceId,
                Math.max(2, Math.min(dashboard.gridColumns, widthInCols)),
                Math.max(2, heightInRows));
        }

        // Reset styles
        widget.style.width = '';
        widget.style.height = '';
        widget.classList.remove('tm-widget--resizing');

        dashboard.isResizing = false;
        dashboard.resizeWidget = null;
        dashboard.resizeDirection = null;
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
        let preview = dashboard.element.querySelector('.tm-widget-drop-preview');
        if (!preview) {
            preview = document.createElement('div');
            preview.className = 'tm-widget-drop-preview';
            dashboard.element.querySelector('.tm-dashboard-grid').appendChild(preview);
        }

        if (dashboard.draggedWidget) {
            const width = parseInt(dashboard.draggedWidget.style.gridColumnEnd?.split(' ')[1] || 4);
            const height = parseInt(dashboard.draggedWidget.style.gridRowEnd?.split(' ')[1] || 4);

            preview.style.gridColumn = `${col + 1} / span ${width}`;
            preview.style.gridRow = `${row + 1} / span ${height}`;
        }
    },

    clearDropPreview: function (dashboard) {
        const preview = dashboard.element.querySelector('.tm-widget-drop-preview');
        if (preview) {
            preview.remove();
        }
    },

    destroy: function (element) {
        const id = element.id;
        const dashboard = this.dashboards.get(id);
        if (dashboard) {
            // Remove all event listeners
            if (dashboard._grid) {
                dashboard._grid.removeEventListener('dragstart', dashboard._onDragStart);
                dashboard._grid.removeEventListener('dragover', dashboard._onDragOver);
                dashboard._grid.removeEventListener('drop', dashboard._onDrop);
                dashboard._grid.removeEventListener('dragend', dashboard._onDragEnd);
                dashboard._grid.removeEventListener('mousedown', dashboard._onMouseDown);
                dashboard._grid.removeEventListener('dragenter', dashboard._onDragEnter);
                dashboard._grid.removeEventListener('dragleave', dashboard._onDragLeave);
            }
            document.removeEventListener('mousemove', dashboard._onMouseMove);
            document.removeEventListener('mouseup', dashboard._onMouseUp);

            this.dashboards.delete(id);
        }
    }
};
