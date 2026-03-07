// ═══════════════════════════════════════════════════════════════
// Tempo.Blazor Scheduler JS Interop
// Handles resize and drag operations for scheduler events
// ═══════════════════════════════════════════════════════════════

(function () {
    'use strict';

    // Namespace
    window.TempoBlazor = window.TempoBlazor || {};
    window.TempoBlazor.Scheduler = {

        _currentResize: null,

        /**
         * Initialize resize handling for a scheduler
         * Call this once when scheduler is rendered
         */
        init: function () {
            // Remove any existing listener to avoid duplicates
            if (this._boundMouseDown) {
                document.removeEventListener('mousedown', this._boundMouseDown);
            }

            this._boundMouseDown = this._handleMouseDown.bind(this);
            document.addEventListener('mousedown', this._boundMouseDown);
        },

        /**
         * Handle mousedown on resize handles
         */
        _handleMouseDown: function (e) {
            // Check if we clicked on a resize handle
            const resizeHandle = e.target.closest('.tm-scheduler-event-resize-handle');
            if (!resizeHandle) return;

            // Prevent default to stop any other behavior (including drag start)
            e.preventDefault();
            e.stopPropagation();

            // Find the parent event element
            const eventElement = resizeHandle.closest('.tm-scheduler-timegrid-event');
            if (!eventElement) return;

            // Get the scheduler container for calculations
            const container = resizeHandle.closest('.tm-scheduler-timegrid-body');
            if (!container) return;

            // Temporarily disable dragging on the event element
            const originalDraggable = eventElement.draggable;
            eventElement.draggable = false;
            eventElement.setAttribute('data-resizing', 'true');

            // Get slot duration from data attribute or default
            const timegrid = container.closest('.tm-scheduler-timegrid');
            const slotMinutes = parseInt(timegrid?.dataset.slotDuration) || 30;

            // Get event ID from the element
            const eventId = eventElement.dataset.eventId;

            // Store original dimensions
            const containerRect = container.getBoundingClientRect();
            const eventRect = eventElement.getBoundingClientRect();
            const originalHeight = eventRect.height;
            const containerHeight = containerRect.height;
            const startY = e.clientY;

            // Start resize tracking
            this._startResizeTracking(eventElement, container, startY, slotMinutes, originalDraggable, originalHeight, containerHeight, eventId);
        },

        /**
         * Track resize movement
         */
        _startResizeTracking: function (eventElement, container, startY, slotMinutes, originalDraggable, originalHeight, containerHeight, eventId) {
            const self = this;
            let currentEndDelta = 0;
            let isResizing = true;

            // Minutes per pixel ratio (24 hours = 1440 minutes)
            const minutesPerPixel = (24 * 60) / containerHeight;

            function onMouseMove(e) {
                if (!isResizing) return;

                const deltaY = e.clientY - startY;
                const deltaMinutes = Math.round(deltaY * minutesPerPixel / slotMinutes) * slotMinutes;

                if (deltaMinutes !== currentEndDelta) {
                    currentEndDelta = deltaMinutes;

                    // Update visual height of the event element
                    const heightChange = deltaY;
                    const newHeight = originalHeight + heightChange;
                    
                    if (newHeight > 10) { // Minimum height
                        eventElement.style.height = newHeight + 'px';
                    }

                    // Store current delta for later
                    eventElement.dataset.resizeDeltaMinutes = currentEndDelta;
                }
            }

            function onMouseUp(e) {
                if (!isResizing) return;
                isResizing = false;

                // Remove event listeners
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);

                // Restore draggable on the event element
                eventElement.draggable = originalDraggable;
                eventElement.removeAttribute('data-resizing');

                // Get the final delta
                const finalDelta = parseInt(eventElement.dataset.resizeDeltaMinutes) || 0;

                // Reset height to let Blazor re-render with correct values
                eventElement.style.height = '';

                // Clean up data attributes
                delete eventElement.dataset.resizeDeltaMinutes;

                // Call Blazor to notify about the resize
                if (eventId && finalDelta !== 0) {
                    self._notifyBlazorResize(eventId, finalDelta);
                }
            }

            // Add global event listeners
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        },

        /**
         * Notify Blazor about the resize completion
         */
        _notifyBlazorResize: function (eventId, deltaMinutes) {
            // Call the static .NET method to notify about resize completion
            if (window.DotNet) {
                window.DotNet.invokeMethodAsync('Tempo.Blazor', 'NotifyResizeComplete', '', eventId, deltaMinutes)
                    .catch(function (err) {
                        console.warn('Scheduler: Failed to notify Blazor about resize', err);
                    });
            }
        },

        /**
         * Cancel ongoing resize operation
         */
        cancelResize: function () {
            // Implementation for manual cancellation if needed
        }
    };

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