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

            // Find the parent event element (could be timegrid or timeline)
            const eventElement = resizeHandle.closest('.tm-scheduler-timegrid-event, .tm-scheduler-timeline-event');
            if (!eventElement) return;

            // Determine resize edge (left or right)
            const resizeEdge = resizeHandle.dataset.resizeEdge || 'right';

            // Determine if this is a timeline or timegrid event
            const isTimeline = eventElement.classList.contains('tm-scheduler-timeline-event');

            // Get the appropriate container for calculations
            const container = isTimeline
                ? resizeHandle.closest('.tm-scheduler-timeline-row-body')
                : resizeHandle.closest('.tm-scheduler-timegrid-body');

            if (!container) return;

            // Temporarily disable dragging on the event element
            const originalDraggable = eventElement.draggable;
            eventElement.draggable = false;
            eventElement.setAttribute('data-resizing', 'true');

            // Get slot duration from data attribute or default
            const schedulerContainer = isTimeline
                ? container.closest('.tm-scheduler-timeline')
                : container.closest('.tm-scheduler-timegrid');
            const slotMinutes = parseInt(schedulerContainer?.dataset.slotDuration) || 30;

            // Get event ID from the element
            const eventId = eventElement.dataset.eventId;

            // Store original dimensions
            const containerRect = container.getBoundingClientRect();
            const eventRect = eventElement.getBoundingClientRect();
            const originalSize = isTimeline ? eventRect.width : eventRect.height;
            const containerSize = isTimeline ? containerRect.width : containerRect.height;
            const startPos = isTimeline ? e.clientX : e.clientY;

            // For timeline left resize, we need the original left position
            const originalLeft = isTimeline ? eventRect.left - containerRect.left : 0;

            // Start resize tracking
            this._startResizeTracking(eventElement, container, startPos, slotMinutes, originalDraggable, 
                originalSize, containerSize, eventId, isTimeline, resizeEdge, originalLeft);
        },

        /**
         * Track resize movement
         */
        _startResizeTracking: function (eventElement, container, startPos, slotMinutes, originalDraggable, 
            originalSize, containerSize, eventId, isTimeline, resizeEdge, originalLeft) {
            const self = this;
            let currentDelta = 0;
            let isResizing = true;

            // Minutes per pixel ratio (24 hours = 1440 minutes)
            const minutesPerPixel = (24 * 60) / containerSize;

            function onMouseMove(e) {
                if (!isResizing) return;

                const currentPos = isTimeline ? e.clientX : e.clientY;
                const deltaPos = currentPos - startPos;
                const deltaMinutes = Math.round(deltaPos * minutesPerPixel / slotMinutes) * slotMinutes;

                if (deltaMinutes !== currentDelta) {
                    currentDelta = deltaMinutes;

                    if (isTimeline) {
                        if (resizeEdge === 'left') {
                            // Left resize: adjust left position and width
                            const newLeft = originalLeft + deltaPos;
                            const newWidth = originalSize - deltaPos;
                            
                            if (newWidth > 10) { // Minimum width
                                eventElement.style.left = newLeft + 'px';
                                eventElement.style.width = newWidth + 'px';
                            }
                        } else {
                            // Right resize: adjust width only
                            const newWidth = originalSize + deltaPos;
                            
                            if (newWidth > 10) { // Minimum width
                                eventElement.style.width = newWidth + 'px';
                            }
                        }
                    } else {
                        // TimeGrid resize (always bottom/right resize)
                        const newSize = originalSize + deltaPos;
                        
                        if (newSize > 10) { // Minimum size
                            eventElement.style.height = newSize + 'px';
                        }
                    }

                    // Store current delta for later
                    // For left resize: invert delta because dragging right reduces width from left
                    // For right resize: use delta directly
                    eventElement.dataset.resizeDeltaMinutes = resizeEdge === 'left' ? -currentDelta : currentDelta;
                    eventElement.dataset.resizeEdge = resizeEdge;
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

                // Reset styles to let Blazor re-render with correct values
                if (isTimeline) {
                    eventElement.style.width = '';
                    if (resizeEdge === 'left') {
                        eventElement.style.left = '';
                    }
                } else {
                    eventElement.style.height = '';
                }

                // Clean up data attributes
                delete eventElement.dataset.resizeDeltaMinutes;
                delete eventElement.dataset.resizeEdge;

                // Call Blazor to notify about the resize
                if (eventId && finalDelta !== 0) {
                    self._notifyBlazorResize(eventId, finalDelta, isTimeline, resizeEdge);
                }
            }

            // Add global event listeners
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        },

        /**
         * Notify Blazor about the resize completion
         */
        _notifyBlazorResize: function (eventId, deltaMinutes, isTimeline, resizeEdge) {
            // Call the appropriate static .NET method based on scheduler type
            if (window.DotNet) {
                const methodName = isTimeline ? 'NotifyTimelineResizeComplete' : 'NotifyResizeComplete';
                // Pass the resize edge as additional parameter for timeline
                if (isTimeline) {
                    window.DotNet.invokeMethodAsync('Tempo.Blazor', methodName, '', eventId, deltaMinutes, resizeEdge)
                        .catch(function (err) {
                            console.warn('Scheduler: Failed to notify Blazor about resize', err);
                        });
                } else {
                    window.DotNet.invokeMethodAsync('Tempo.Blazor', methodName, '', eventId, deltaMinutes)
                        .catch(function (err) {
                            console.warn('Scheduler: Failed to notify Blazor about resize', err);
                        });
                }
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