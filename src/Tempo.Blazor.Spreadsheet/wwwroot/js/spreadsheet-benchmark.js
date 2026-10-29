window.tmSpreadsheetBenchmark = (() => {
    const frame = () => new Promise(resolve => requestAnimationFrame(resolve));

    const waitForStableFrame = async () => {
        const start = performance.now();
        await frame();
        await frame();
        return performance.now() - start;
    };

    const measureScroll = async (grid, durationMs) => {
        if (!grid) {
            return { fps: 0, frames: 0, durationMs: 0, maxFrameMs: 0, scrollTop: 0, scrollLeft: 0 };
        }

        const maxTop = Math.max(0, grid.scrollHeight - grid.clientHeight);
        const maxLeft = Math.max(0, grid.scrollWidth - grid.clientWidth);
        const startTop = grid.scrollTop;
        const startLeft = grid.scrollLeft;
        const targetTop = maxTop > 0 ? Math.min(maxTop, startTop + Math.max(240, grid.clientHeight)) : startTop;
        const targetLeft = maxLeft > 0 ? Math.min(maxLeft, startLeft + Math.max(320, grid.clientWidth / 2)) : startLeft;

        let frames = 0;
        let maxFrameMs = 0;
        let previousTime = performance.now();
        const startTime = previousTime;

        await new Promise(resolve => {
            const step = now => {
                frames++;
                maxFrameMs = Math.max(maxFrameMs, now - previousTime);
                previousTime = now;

                const progress = Math.min(1, (now - startTime) / durationMs);
                grid.scrollTop = startTop + (targetTop - startTop) * progress;
                grid.scrollLeft = startLeft + (targetLeft - startLeft) * progress;

                if (progress < 1) {
                    requestAnimationFrame(step);
                } else {
                    resolve();
                }
            };

            requestAnimationFrame(step);
        });

        const measuredDurationMs = performance.now() - startTime;
        return {
            fps: measuredDurationMs > 0 ? frames * 1000 / measuredDurationMs : 0,
            frames,
            durationMs: measuredDurationMs,
            maxFrameMs,
            scrollTop: grid.scrollTop,
            scrollLeft: grid.scrollLeft
        };
    };

    const debugMetrics = grid => {
        if (!grid || !window.tmSpreadsheetCanvas?.getDebugMetrics) {
            return {};
        }

        const metrics = window.tmSpreadsheetCanvas.getDebugMetrics(grid) || {};
        return {
            ...metrics,
            dotNetCallbacksByMethod: { ...(metrics.dotNetCallbacksByMethod || {}) },
            hotPathDotNetCallbacksByMethod: { ...(metrics.hotPathDotNetCallbacksByMethod || {}) }
        };
    };

    const diffCounterMap = (before, after) => {
        const result = {};
        const keys = new Set([
            ...Object.keys(before || {}),
            ...Object.keys(after || {})
        ]);

        for (const key of keys) {
            const value = (after?.[key] || 0) - (before?.[key] || 0);
            if (value) result[key] = value;
        }

        return result;
    };

    const diffDebugMetrics = (before, after) => ({
        nativeScrollEvents: (after.nativeScrollEventCount || 0) - (before.nativeScrollEventCount || 0),
        dotNetCallbackCount: (after.dotNetCallbackCount || 0) - (before.dotNetCallbackCount || 0),
        hotPathDotNetCallbackCount: (after.hotPathDotNetCallbackCount || 0) - (before.hotPathDotNetCallbackCount || 0),
        dotNetCallbacksByMethod: diffCounterMap(before.dotNetCallbacksByMethod, after.dotNetCallbacksByMethod),
        hotPathDotNetCallbacksByMethod: diffCounterMap(before.hotPathDotNetCallbacksByMethod, after.hotPathDotNetCallbacksByMethod),
        lastDotNetCallbackMethod: after.lastDotNetCallbackMethod || "",
        blazorFrameCount: (after.blazorFrameCount || 0) - (before.blazorFrameCount || 0),
        hotPathBlazorFrameCount: (after.hotPathBlazorFrameCount || 0) - (before.hotPathBlazorFrameCount || 0),
        viewportCallbackCount: (after.viewportCallbackCount || 0) - (before.viewportCallbackCount || 0),
        selectionCallbackCount: (after.selectionCallbackCount || 0) - (before.selectionCallbackCount || 0),
        scrollToCount: (after.scrollToCount || 0) - (before.scrollToCount || 0),
        keyboardScrollToCount: (after.keyboardScrollToCount || 0) - (before.keyboardScrollToCount || 0),
        logicalKeyboardScrollCount: (after.logicalKeyboardScrollCount || 0) - (before.logicalKeyboardScrollCount || 0),
        logicalWheelScrollCount: (after.logicalWheelScrollCount || 0) - (before.logicalWheelScrollCount || 0),
        logicalPointerScrollCount: (after.logicalPointerScrollCount || 0) - (before.logicalPointerScrollCount || 0),
        wheelEventCount: (after.wheelEventCount || 0) - (before.wheelEventCount || 0),
        wheelPreventedCount: (after.wheelPreventedCount || 0) - (before.wheelPreventedCount || 0),
        dragAutoscrollFrames: (after.dragAutoscrollFrames || 0) - (before.dragAutoscrollFrames || 0),
        resizePointerMoveCount: (after.resizePointerMoveCount || 0) - (before.resizePointerMoveCount || 0),
        resizePreviewFrameCount: (after.resizePreviewFrameCount || 0) - (before.resizePreviewFrameCount || 0),
        resizePaintFrameCount: (after.resizePaintFrameCount || 0) - (before.resizePaintFrameCount || 0),
        resizeDotNetCallbackCount: (after.resizeDotNetCallbackCount || 0) - (before.resizeDotNetCallbackCount || 0),
        resizeBlazorFrameCount: (after.resizeBlazorFrameCount || 0) - (before.resizeBlazorFrameCount || 0),
        paintFrameCount: (after.paintFrameCount || 0) - (before.paintFrameCount || 0),
        contentPaintFrameCount: (after.contentPaintFrameCount || 0) - (before.contentPaintFrameCount || 0),
        selectionPaintFrameCount: (after.selectionPaintFrameCount || 0) - (before.selectionPaintFrameCount || 0),
        ensureCellVisibleMs: (after.ensureCellVisibleTotalMs || 0) - (before.ensureCellVisibleTotalMs || 0),
        ensureCellVisibleCount: (after.ensureCellVisibleCount || 0) - (before.ensureCellVisibleCount || 0),
        drawCellsMs: (after.drawCellsTotalMs || 0) - (before.drawCellsTotalMs || 0),
        drawCellContentMs: (after.drawCellContentTotalMs || 0) - (before.drawCellContentTotalMs || 0),
        contextSaveClipRestoreCount: (after.contextSaveClipRestoreTotalCount || 0) - (before.contextSaveClipRestoreTotalCount || 0)
    });

    const dispatchArrow = (grid, key) => {
        grid.dispatchEvent(new KeyboardEvent("keydown", {
            key,
            code: key,
            bubbles: true,
            cancelable: true
        }));
    };

    const settleInteraction = async () => {
        await frame();
        await frame();
    };

    const waitForBenchmarkIdle = async grid => {
        await settleInteraction();
        for (let i = 0; i < 20; i++) {
            const state = grid?.__tmSpreadsheetCanvas;
            if (!state) return;
            const hasPendingTimer = !!state.viewportTimer
                || !!state.nativeScrollSyncTimer
                || !!state.selectionSyncTimer
                || !!state.commandLogTimer;
            const hasPendingFrame = !!state.paintFrame
                || !!state.syncFrame
                || !!state.selectionSyncFrame
                || !!state.postPaintDebouncedViewportFrame
                || !!state.dragAutoscrollFrame;
            const hasInFlight = !!state.viewportInFlight
                || !!state.selectionInFlight
                || !!state.commandLogInFlight
                || !!state.commandLogPending
                || ((state.commandLog || []).length > 0);
            if (!hasPendingTimer && !hasPendingFrame && !hasInFlight) return;
            await frame();
        }
    };

    const resetGridViewport = async grid => {
        if (!grid) return;
        grid.scrollTop = 0;
        grid.scrollLeft = 0;
        grid.focus();
        await settleInteraction();
    };

    const dispatchViewportClick = async (grid, x, y) => {
        const rect = grid.getBoundingClientRect();
        grid.dispatchEvent(new MouseEvent("click", {
            clientX: Math.max(rect.left + 1, Math.min(rect.right - 1, rect.left + x)),
            clientY: Math.max(rect.top + 1, Math.min(rect.bottom - 1, rect.top + y)),
            bubbles: true,
            cancelable: true
        }));
        await settleInteraction();
    };

    const dispatchViewportClickNow = (grid, x, y) => {
        const rect = grid.getBoundingClientRect();
        grid.dispatchEvent(new MouseEvent("click", {
            clientX: Math.max(rect.left + 1, Math.min(rect.right - 1, rect.left + x)),
            clientY: Math.max(rect.top + 1, Math.min(rect.bottom - 1, rect.top + y)),
            bubbles: true,
            cancelable: true
        }));
    };

    const prepareTopLeftCell = grid => dispatchViewportClick(grid, 72, 48);

    const prepareBottomEdgeCell = grid => dispatchViewportClick(grid, 72, grid.clientHeight - 32);

    const prepareTopEdgeCellAfterScroll = async grid => {
        const maxTop = Math.max(0, grid.scrollHeight - grid.clientHeight);
        grid.scrollTop = Math.min(maxTop, Math.max(240, grid.clientHeight));
        grid.dispatchEvent(new Event("scroll", { bubbles: true }));
        await settleInteraction();
        await dispatchViewportClick(grid, 72, 48);
    };

    const prepareRightEdgeCell = grid => dispatchViewportClick(grid, grid.clientWidth - 32, 48);

    const measureKeyNavigation = async (grid, key, count, options) => {
        if (!grid) {
            return { durationMs: 0, totalDurationMs: 0, count: 0, debug: diffDebugMetrics({}, {}) };
        }

        const settings = options || {};
        if (settings.reset !== false) {
            await resetGridViewport(grid);
        }

        grid.focus();
        if (typeof settings.prepare === "function") {
            await settings.prepare(grid);
            grid.focus();
        }
        await waitForBenchmarkIdle(grid);

        const before = debugMetrics(grid);
        const start = performance.now();
        for (let i = 0; i < count; i++) {
            dispatchArrow(grid, key);

            if ((i + 1) % 5 === 0) {
                await frame();
            }
        }

        await settleInteraction();
        const totalDurationMs = performance.now() - start;
        return {
            durationMs: count > 0 ? totalDurationMs / count : 0,
            totalDurationMs,
            count,
            debug: diffDebugMetrics(before, debugMetrics(grid)),
            scrollTop: grid.scrollTop,
            scrollLeft: grid.scrollLeft
        };
    };

    const measureKeyboardScenarios = async (grid, settings) => {
        if (!grid) {
            return {};
        }

        const noScrollCount = settings.keyboardNoScrollCount || 8;
        const edgeCount = settings.keyboardEdgeCount || 80;
        const rightCount = settings.keyboardRightEdgeCount || 45;

        const noScrollDown = await measureKeyNavigation(grid, "ArrowDown", noScrollCount, { reset: true, prepare: prepareTopLeftCell });
        const edgeDown = await measureKeyNavigation(grid, "ArrowDown", edgeCount, { reset: true, prepare: prepareBottomEdgeCell });
        const edgeUp = await measureKeyNavigation(grid, "ArrowUp", edgeCount, { reset: true, prepare: prepareTopEdgeCellAfterScroll });
        const edgeRight = await measureKeyNavigation(grid, "ArrowRight", rightCount, { reset: true, prepare: prepareRightEdgeCell });
        const keyboardDebug = [noScrollDown, edgeDown, edgeUp, edgeRight].reduce((total, item) => ({
            nativeScrollEvents: total.nativeScrollEvents + (item.debug?.nativeScrollEvents || 0),
            scrollToCount: total.scrollToCount + (item.debug?.scrollToCount || 0),
            keyboardScrollToCount: total.keyboardScrollToCount + (item.debug?.keyboardScrollToCount || 0),
            logicalKeyboardScrollCount: total.logicalKeyboardScrollCount + (item.debug?.logicalKeyboardScrollCount || 0),
            ensureCellVisibleMs: total.ensureCellVisibleMs + (item.debug?.ensureCellVisibleMs || 0),
            ensureCellVisibleCount: total.ensureCellVisibleCount + (item.debug?.ensureCellVisibleCount || 0)
        }), {
            nativeScrollEvents: 0,
            scrollToCount: 0,
            keyboardScrollToCount: 0,
            logicalKeyboardScrollCount: 0,
            ensureCellVisibleMs: 0,
            ensureCellVisibleCount: 0
        });

        return {
            noScrollDown,
            edgeDown,
            edgeUp,
            edgeRight,
            keyboardDebug
        };
    };

    const measureWheelScroll = async (grid, durationMs) => {
        if (!grid) {
            return { fps: 0, frames: 0, durationMs: 0, debug: diffDebugMetrics({}, {}) };
        }

        await resetGridViewport(grid);
        const before = debugMetrics(grid);
        const start = performance.now();
        let frames = 0;
        let maxFrameMs = 0;
        let previous = start;
        await new Promise(resolve => {
            const step = now => {
                frames++;
                maxFrameMs = Math.max(maxFrameMs, now - previous);
                previous = now;

                const defaultAllowed = grid.dispatchEvent(new WheelEvent("wheel", {
                    deltaY: 96,
                    deltaX: 24,
                    bubbles: true,
                    cancelable: true
                }));

                if (defaultAllowed) {
                    const topBefore = grid.scrollTop;
                    const leftBefore = grid.scrollLeft;
                    grid.scrollTop = Math.min(grid.scrollHeight - grid.clientHeight, grid.scrollTop + 96);
                    grid.scrollLeft = Math.min(grid.scrollWidth - grid.clientWidth, grid.scrollLeft + 24);
                    if (grid.scrollTop !== topBefore || grid.scrollLeft !== leftBefore) {
                        grid.dispatchEvent(new Event("scroll", { bubbles: true }));
                    }
                }

                if (now - start < durationMs) {
                    requestAnimationFrame(step);
                } else {
                    resolve();
                }
            };
            requestAnimationFrame(step);
        });

        await settleInteraction();
        const totalDurationMs = performance.now() - start;
        return {
            fps: totalDurationMs > 0 ? frames * 1000 / totalDurationMs : 0,
            frames,
            durationMs: totalDurationMs,
            maxFrameMs,
            scrollTop: grid.scrollTop,
            scrollLeft: grid.scrollLeft,
            debug: diffDebugMetrics(before, debugMetrics(grid)),
            finalDebug: debugMetrics(grid)
        };
    };

    const measureDragAutoscroll = async (grid, durationMs) => {
        if (!grid) {
            return { durationMs: 0, dragAutoscrollFrames: 0, debug: diffDebugMetrics({}, {}) };
        }

        await resetGridViewport(grid);
        const rect = grid.getBoundingClientRect();
        const startX = Math.min(rect.right - 80, rect.left + 140);
        const startY = Math.min(rect.bottom - 80, rect.top + 90);
        const dragX = Math.min(rect.right - 8, startX + 40);
        const dragY = rect.bottom + 36;
        const before = debugMetrics(grid);
        const started = performance.now();
        const pointerId = 77;

        const PointerCtor = window.PointerEvent || window.MouseEvent;
        grid.dispatchEvent(new PointerCtor("pointerdown", {
            pointerId,
            pointerType: "mouse",
            clientX: startX,
            clientY: startY,
            bubbles: true,
            cancelable: true
        }));

        grid.dispatchEvent(new PointerCtor("pointermove", {
            pointerId,
            pointerType: "mouse",
            clientX: startX + 10,
            clientY: startY + 10,
            bubbles: true,
            cancelable: true
        }));

        while (performance.now() - started < durationMs) {
            grid.dispatchEvent(new PointerCtor("pointermove", {
                pointerId,
                pointerType: "mouse",
                clientX: dragX,
                clientY: dragY,
                bubbles: true,
                cancelable: true
            }));
            await frame();
        }

        grid.dispatchEvent(new PointerCtor("pointerup", {
            pointerId,
            pointerType: "mouse",
            clientX: dragX,
            clientY: dragY,
            bubbles: true,
            cancelable: true
        }));

        await settleInteraction();
        const after = debugMetrics(grid);
        return {
            durationMs: performance.now() - started,
            dragAutoscrollFrames: (after.dragAutoscrollFrames || 0) - (before.dragAutoscrollFrames || 0),
            scrollTop: grid.scrollTop,
            scrollLeft: grid.scrollLeft,
            debug: diffDebugMetrics(before, after)
        };
    };

    const buildPasteText = (rows, columns) => {
        const lines = [];
        for (let row = 0; row < rows; row++) {
            const values = [];
            for (let col = 0; col < columns; col++) {
                values.push(`Paste ${row + 1}:${col + 1}`);
            }
            lines.push(values.join("\t"));
        }
        return lines.join("\n");
    };

    const measureCanvasPaste = async (grid, rows, columns) => {
        if (!grid || !window.tmSpreadsheetCanvas?.applyClipboardText) {
            return { durationMs: 0, debug: diffDebugMetrics({}, {}) };
        }

        await resetGridViewport(grid);
        await prepareTopLeftCell(grid);
        await waitForBenchmarkIdle(grid);
        const before = debugMetrics(grid);
        const started = performance.now();
        window.tmSpreadsheetCanvas.applyClipboardText(grid, buildPasteText(rows, columns));
        await waitForBenchmarkIdle(grid);
        return {
            durationMs: performance.now() - started,
            debug: diffDebugMetrics(before, debugMetrics(grid))
        };
    };

    const getResizeHandlePoint = (grid, kind) => {
        const state = grid?.__tmSpreadsheetCanvas;
        const model = state?.model;
        if (!state || !model) return null;
        const rect = grid.getBoundingClientRect();
        const rowHeaderWidth = model.RowHeaderWidth ?? model.rowHeaderWidth ?? 40;
        const columnHeaderHeight = model.ColumnHeaderHeight ?? model.columnHeaderHeight ?? 20;

        if (kind === "column") {
            const columns = model.Columns || model.columns || [];
            const column = columns[1] || columns[0];
            if (!column) return null;
            const left = column.Left ?? column.left ?? 0;
            const width = column.Width ?? column.width ?? 64;
            const x = rect.left + rowHeaderWidth + left + width - 1;
            const y = rect.top + Math.max(6, columnHeaderHeight / 2);
            return { startX: x, startY: y, endX: x + 52, endY: y, index: column.Index ?? column.index ?? 0 };
        }

        const rows = model.Rows || model.rows || [];
        const row = rows[2] || rows[1] || rows[0];
        if (!row) return null;
        const top = row.Top ?? row.top ?? 0;
        const height = row.Height ?? row.height ?? 20;
        const x = rect.left + Math.max(8, rowHeaderWidth / 2);
        const y = rect.top + columnHeaderHeight + top + height - 1;
        return { startX: x, startY: y, endX: x, endY: y + 24, index: row.Index ?? row.index ?? 0 };
    };

    const measureResizeDrag = async (grid, kind) => {
        if (!grid) {
            return { durationMs: 0, pointerMoves: 0, debug: diffDebugMetrics({}, {}) };
        }

        await resetGridViewport(grid);
        await waitForBenchmarkIdle(grid);
        const handle = getResizeHandlePoint(grid, kind);
        if (!handle) {
            return { durationMs: 0, pointerMoves: 0, debug: diffDebugMetrics({}, {}) };
        }

        const before = debugMetrics(grid);
        const pointerId = kind === "column" ? 61 : 62;
        const moveCount = 8;
        const started = performance.now();

        grid.dispatchEvent(new PointerEvent("pointerdown", {
            clientX: handle.startX,
            clientY: handle.startY,
            button: 0,
            buttons: 1,
            pointerId,
            pointerType: "mouse",
            bubbles: true,
            cancelable: true
        }));

        for (let i = 1; i <= moveCount; i++) {
            grid.dispatchEvent(new PointerEvent("pointermove", {
                clientX: handle.startX + (handle.endX - handle.startX) * i / moveCount,
                clientY: handle.startY + (handle.endY - handle.startY) * i / moveCount,
                button: 0,
                buttons: 1,
                pointerId,
                pointerType: "mouse",
                bubbles: true,
                cancelable: true
            }));
            await frame();
        }

        grid.dispatchEvent(new PointerEvent("pointerup", {
            clientX: handle.endX,
            clientY: handle.endY,
            button: 0,
            buttons: 0,
            pointerId,
            pointerType: "mouse",
            bubbles: true,
            cancelable: true
        }));

        await waitForBenchmarkIdle(grid);
        return {
            durationMs: performance.now() - started,
            pointerMoves: moveCount,
            debug: diffDebugMetrics(before, debugMetrics(grid))
        };
    };

    const readModelFlag = (model, name, fallback) => {
        if (!model) return fallback;
        const camel = name.charAt(0).toLowerCase() + name.slice(1);
        return model[camel] ?? model[name] ?? fallback;
    };

    const writeModelFlag = (model, name, value) => {
        if (!model) return;
        const camel = name.charAt(0).toLowerCase() + name.slice(1);
        if (camel in model || !(name in model)) model[camel] = value;
        if (name in model) model[name] = value;
    };

    const measureOneInteraction = async (grid, action) => {
        await waitForBenchmarkIdle(grid);
        const before = debugMetrics(grid);
        const started = performance.now();
        await action();
        await frame();
        const firstFrameMs = performance.now() - started;
        const firstFrameDebug = diffDebugMetrics(before, debugMetrics(grid));
        await waitForBenchmarkIdle(grid);
        const settledMs = performance.now() - started;
        const settledDebug = diffDebugMetrics(before, debugMetrics(grid));
        return { firstFrameMs, settledMs, firstFrameDebug, settledDebug };
    };

    const emptyPhase12Interaction = () => {
        const emptyDebug = diffDebugMetrics({}, {});
        return { firstFrameMs: 0, settledMs: 0, firstFrameDebug: emptyDebug, settledDebug: emptyDebug };
    };

    const measurePhase12Latencies = async grid => {
        if (!grid) {
            return {
                arrowDownViewport: emptyPhase12Interaction(),
                arrowDownScrollEdge: emptyPhase12Interaction(),
                normalCellClick: emptyPhase12Interaction(),
                formulaCellClick: emptyPhase12Interaction(),
                typingCharacter: emptyPhase12Interaction(),
                formulaCommit: emptyPhase12Interaction()
            };
        }

        await resetGridViewport(grid);
        await prepareTopLeftCell(grid);
        const arrowDownViewport = await measureOneInteraction(grid, async () => {
            dispatchArrow(grid, "ArrowDown");
        });

        await resetGridViewport(grid);
        await prepareBottomEdgeCell(grid);
        const arrowDownScrollEdge = await measureOneInteraction(grid, async () => {
            dispatchArrow(grid, "ArrowDown");
        });

        await resetGridViewport(grid);
        const normalCellClick = await measureOneInteraction(grid, async () => {
            dispatchViewportClickNow(grid, 128, 72);
        });

        await resetGridViewport(grid);
        await prepareTopLeftCell(grid);
        grid.dispatchEvent(new KeyboardEvent("keydown", {
            key: "=",
            code: "Equal",
            bubbles: true,
            cancelable: true
        }));
        await frame();
        const formulaCellClick = await measureOneInteraction(grid, async () => {
            dispatchViewportClickNow(grid, 160, 92);
        });

        await resetGridViewport(grid);
        await prepareTopLeftCell(grid);
        grid.dispatchEvent(new KeyboardEvent("keydown", {
            key: "x",
            code: "KeyX",
            bubbles: true,
            cancelable: true
        }));
        await frame();
        const typingCharacter = await measureOneInteraction(grid, async () => {
            const editor = grid.querySelector(".tm-spreadsheet-canvas-grid__editor");
            if (editor) {
                editor.value += "y";
                editor.dispatchEvent(new InputEvent("input", { bubbles: true, data: "y", inputType: "insertText" }));
            }
        });

        await resetGridViewport(grid);
        await prepareTopLeftCell(grid);
        grid.dispatchEvent(new KeyboardEvent("keydown", {
            key: "=",
            code: "Equal",
            bubbles: true,
            cancelable: true
        }));
        await frame();
        const formulaCommit = await measureOneInteraction(grid, async () => {
            const editor = grid.querySelector(".tm-spreadsheet-canvas-grid__editor");
            if (editor) {
                editor.value = "=B2";
                editor.dispatchEvent(new InputEvent("input", { bubbles: true, data: "2", inputType: "insertText" }));
                editor.dispatchEvent(new KeyboardEvent("keydown", {
                    key: "Enter",
                    code: "Enter",
                    bubbles: true,
                    cancelable: true
                }));
            }
        });

        return {
            arrowDownViewport,
            arrowDownScrollEdge,
            normalCellClick,
            formulaCellClick,
            typingCharacter,
            formulaCommit
        };
    };

    const averageMetric = values => {
        const numeric = (values || []).filter(value => Number.isFinite(value));
        if (numeric.length === 0) return 0;
        return numeric.reduce((sum, value) => sum + value, 0) / numeric.length;
    };

    const getMemory = () => {
        if (performance.memory && typeof performance.memory.usedJSHeapSize === "number") {
            return performance.memory.usedJSHeapSize;
        }

        return null;
    };

    return {
        run: async (selector, options) => {
            const settings = options || {};
            const grid = document.querySelector(selector);
            const stableFrameMs = await waitForStableFrame();
            const scroll = await measureScroll(grid, settings.scrollDurationMs || 1200);
            const keyboard = await measureKeyboardScenarios(grid, settings);
            const wheel = await measureWheelScroll(grid, settings.wheelDurationMs || 900);
            const drag = await measureDragAutoscroll(grid, settings.dragAutoscrollDurationMs || 700);
            const columnResize = await measureResizeDrag(grid, "column");
            const rowResize = await measureResizeDrag(grid, "row");
            const paste = await measureCanvasPaste(grid, settings.pasteRows || 100, settings.pasteColumns || 20);
            const phase12 = grid?.classList?.contains("tm-spreadsheet-canvas-grid")
                ? await measurePhase12Latencies(grid)
                : {
                    arrowDownViewport: emptyPhase12Interaction(),
                    arrowDownScrollEdge: emptyPhase12Interaction(),
                    normalCellClick: emptyPhase12Interaction(),
                    formulaCellClick: emptyPhase12Interaction(),
                    typingCharacter: emptyPhase12Interaction(),
                    formulaCommit: emptyPhase12Interaction()
                };
            const finalDebug = debugMetrics(grid);
            const interactionDotNetCallbacksPerInteraction = averageMetric([
                phase12.arrowDownViewport?.firstFrameDebug?.dotNetCallbackCount || 0,
                phase12.arrowDownScrollEdge?.firstFrameDebug?.dotNetCallbackCount || 0,
                phase12.normalCellClick?.firstFrameDebug?.dotNetCallbackCount || 0,
                phase12.formulaCellClick?.firstFrameDebug?.dotNetCallbackCount || 0,
                phase12.typingCharacter?.firstFrameDebug?.dotNetCallbackCount || 0,
                phase12.formulaCommit?.firstFrameDebug?.dotNetCallbackCount || 0
            ]);
            const interactionHotPathDotNetCallbacksPerInteraction = averageMetric([
                phase12.arrowDownViewport?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                phase12.arrowDownScrollEdge?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                phase12.normalCellClick?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                phase12.formulaCellClick?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                phase12.typingCharacter?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                phase12.formulaCommit?.firstFrameDebug?.hotPathDotNetCallbackCount || 0
            ]);

            return {
                stableFrameMs,
                scrollFps: scroll.fps,
                scrollFrames: scroll.frames,
                scrollDurationMs: scroll.durationMs,
                maxFrameMs: scroll.maxFrameMs,
                scrollTop: scroll.scrollTop,
                scrollLeft: scroll.scrollLeft,
                keyNavigationMs: keyboard.edgeDown?.durationMs || 0,
                keyNavigationTotalMs: keyboard.edgeDown?.totalDurationMs || 0,
                keyNavigationCount: keyboard.edgeDown?.count || 0,
                keyboardNoScrollMs: keyboard.noScrollDown?.durationMs || 0,
                keyboardNoScrollTotalMs: keyboard.noScrollDown?.totalDurationMs || 0,
                keyboardScrollDownMs: keyboard.edgeDown?.durationMs || 0,
                keyboardScrollDownTotalMs: keyboard.edgeDown?.totalDurationMs || 0,
                keyboardScrollUpMs: keyboard.edgeUp?.durationMs || 0,
                keyboardScrollUpTotalMs: keyboard.edgeUp?.totalDurationMs || 0,
                keyboardScrollRightMs: keyboard.edgeRight?.durationMs || 0,
                keyboardScrollRightTotalMs: keyboard.edgeRight?.totalDurationMs || 0,
                keyboardNativeScrollEvents: keyboard.keyboardDebug?.nativeScrollEvents || 0,
                keyboardScrollToCount: keyboard.keyboardDebug?.keyboardScrollToCount || 0,
                keyboardLogicalScrollCount: keyboard.keyboardDebug?.logicalKeyboardScrollCount || 0,
                keyboardEnsureCellVisibleMs: keyboard.keyboardDebug?.ensureCellVisibleMs || 0,
                keyboardEnsureCellVisibleCount: keyboard.keyboardDebug?.ensureCellVisibleCount || 0,
                wheelScrollFps: wheel.fps,
                wheelScrollMs: wheel.durationMs,
                wheelNativeScrollEvents: wheel.debug?.nativeScrollEvents || 0,
                wheelEventCount: wheel.debug?.wheelEventCount || 0,
                wheelPreventedCount: wheel.debug?.wheelPreventedCount || 0,
                wheelLogicalScrollCount: wheel.debug?.logicalWheelScrollCount || 0,
                wheelViewportCallbackCount: wheel.debug?.viewportCallbackCount || 0,
                wheelScrollToCount: wheel.debug?.scrollToCount || 0,
                wheelPaintFrameCount: wheel.debug?.paintFrameCount || 0,
                wheelContentPaintFrameCount: wheel.debug?.contentPaintFrameCount || 0,
                dragAutoscrollMs: drag.durationMs,
                dragAutoscrollFrames: drag.dragAutoscrollFrames || 0,
                dragLogicalScrollCount: drag.debug?.logicalPointerScrollCount || 0,
                dragViewportCallbackCount: drag.debug?.viewportCallbackCount || 0,
                dragSelectionCallbackCount: drag.debug?.selectionCallbackCount || 0,
                dragScrollToCount: drag.debug?.scrollToCount || 0,
                dragBlazorFrameCount: drag.debug?.blazorFrameCount || 0,
                dragHotPathBlazorFrameCount: drag.debug?.hotPathBlazorFrameCount || 0,
                columnResizeDragMs: columnResize.durationMs,
                columnResizePointerMoveCount: columnResize.pointerMoves || 0,
                columnResizeDotNetCallbackCount: columnResize.debug?.resizeDotNetCallbackCount || 0,
                columnResizeBlazorFrameCount: columnResize.debug?.resizeBlazorFrameCount || 0,
                columnResizePaintFrameCount: columnResize.debug?.resizePaintFrameCount || 0,
                rowResizeDragMs: rowResize.durationMs,
                rowResizePointerMoveCount: rowResize.pointerMoves || 0,
                rowResizeDotNetCallbackCount: rowResize.debug?.resizeDotNetCallbackCount || 0,
                rowResizeBlazorFrameCount: rowResize.debug?.resizeBlazorFrameCount || 0,
                rowResizePaintFrameCount: rowResize.debug?.resizePaintFrameCount || 0,
                pasteDurationMs: paste.durationMs || 0,
                pasteDotNetCallbackCount: paste.debug?.dotNetCallbackCount || 0,
                pasteHotPathDotNetCallbackCount: paste.debug?.hotPathDotNetCallbackCount || 0,
                pastePaintFrameCount: paste.debug?.paintFrameCount || 0,
                pasteContentPaintFrameCount: paste.debug?.contentPaintFrameCount || 0,
                singleArrowInViewportMs: phase12.arrowDownViewport?.firstFrameMs || 0,
                singleArrowInViewportSettledMs: phase12.arrowDownViewport?.settledMs || 0,
                singleArrowInViewportDotNetCallbackCount: phase12.arrowDownViewport?.firstFrameDebug?.dotNetCallbackCount || 0,
                singleArrowInViewportHotPathDotNetCallbackCount: phase12.arrowDownViewport?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                singleArrowInViewportBlazorFrameCount: phase12.arrowDownViewport?.firstFrameDebug?.blazorFrameCount || 0,
                singleArrowInViewportSelectionPaintFrameCount: phase12.arrowDownViewport?.firstFrameDebug?.selectionPaintFrameCount || 0,
                singleArrowInViewportContentPaintFrameCount: phase12.arrowDownViewport?.firstFrameDebug?.contentPaintFrameCount || 0,
                singleArrowScrollEdgeMs: phase12.arrowDownScrollEdge?.firstFrameMs || 0,
                singleArrowScrollEdgeSettledMs: phase12.arrowDownScrollEdge?.settledMs || 0,
                singleArrowScrollEdgeDotNetCallbackCount: phase12.arrowDownScrollEdge?.firstFrameDebug?.dotNetCallbackCount || 0,
                singleArrowScrollEdgeHotPathDotNetCallbackCount: phase12.arrowDownScrollEdge?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                singleArrowScrollEdgeBlazorFrameCount: phase12.arrowDownScrollEdge?.firstFrameDebug?.blazorFrameCount || 0,
                formulaCellClickLatencyMs: phase12.formulaCellClick?.firstFrameMs || 0,
                formulaCellClickDotNetCallbackCount: phase12.formulaCellClick?.firstFrameDebug?.dotNetCallbackCount || 0,
                formulaCellClickHotPathDotNetCallbackCount: phase12.formulaCellClick?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                formulaCellClickBlazorFrameCount: phase12.formulaCellClick?.firstFrameDebug?.blazorFrameCount || 0,
                typingLatencyMs: phase12.typingCharacter?.firstFrameMs || 0,
                typingDotNetCallbackCount: phase12.typingCharacter?.firstFrameDebug?.dotNetCallbackCount || 0,
                typingHotPathDotNetCallbackCount: phase12.typingCharacter?.firstFrameDebug?.hotPathDotNetCallbackCount || 0,
                typingBlazorFrameCount: phase12.typingCharacter?.firstFrameDebug?.blazorFrameCount || 0,
                formulaCommitLatencyMs: phase12.formulaCommit?.firstFrameMs || 0,
                formulaCommitDotNetCallbackCount: phase12.formulaCommit?.firstFrameDebug?.dotNetCallbackCount || 0,
                interactionDotNetCallbacksPerInteraction,
                interactionHotPathDotNetCallbacksPerInteraction,
                debugNativeScrollEvents: finalDebug.nativeScrollEventCount || 0,
                debugScrollToCount: finalDebug.scrollToCount || 0,
                debugKeyboardScrollToCount: finalDebug.keyboardScrollToCount || 0,
                debugEnsureCellVisibleMs: finalDebug.ensureCellVisibleTotalMs || 0,
                debugDrawCellsMs: finalDebug.drawCellsTotalMs || 0,
                debugDrawCellContentMs: finalDebug.drawCellContentTotalMs || 0,
                debugContextSaveClipRestoreCount: finalDebug.contextSaveClipRestoreTotalCount || 0,
                debugContextSaveMs: finalDebug.contextSaveTotalMs || 0,
                debugContextClipMs: finalDebug.contextClipTotalMs || 0,
                debugContextRestoreMs: finalDebug.contextRestoreTotalMs || 0,
                debugFastCellPathCount: finalDebug.fastCellPathCount || 0,
                debugSlowCellPathCount: finalDebug.slowCellPathCount || 0,
                debugUnclippedTextCount: finalDebug.unclippedTextCount || 0,
                debugClippedTextCount: finalDebug.clippedTextCount || 0,
                debugContextStateSetCount: finalDebug.contextStateSetCount || 0,
                debugContextStateSkipCount: finalDebug.contextStateSkipCount || 0,
                debugDotNetCallbackCount: finalDebug.dotNetCallbackCount || 0,
                debugHotPathDotNetCallbackCount: finalDebug.hotPathDotNetCallbackCount || 0,
                debugBlazorFrameCount: finalDebug.blazorFrameCount || 0,
                debugHotPathBlazorFrameCount: finalDebug.hotPathBlazorFrameCount || 0,
                wheelBlazorFrameCount: wheel.debug?.blazorFrameCount || 0,
                wheelHotPathBlazorFrameCount: wheel.debug?.hotPathBlazorFrameCount || 0,
                usedJsHeapSize: getMemory()
            };
        },

        runPhase12LatencyProbe: async selector => {
            const grid = document.querySelector(selector);
            return measurePhase12Latencies(grid);
        },

        /**
         * The SELECTION-DRAG probe of the phase-7b A/B (redesign phase 7b, remaining task 12bc2afb):
         * N synthetic mouse selections (press on a cell, two moves across the row, release) with the
         * grid's own pointer handlers, timed in-page. Synthetic PointerEvents keep Playwright's
         * per-action round trip — which is far bigger than the canvas work — out of the number, and the
         * probe works against BOTH arms: the engine's `getCellCenter` when the build has it, the model
         * offsets (with the frame's gutters) when the arm is an older canvas script.
         * <para>
         * `changedCells` proves the probe actually moved the selection; a zero means the arm did not
         * react and the caller must not treat the duration as a measurement.
         * </para>
         */
        runSelectionDragProbe: async (selector, options) => {
            const settings = options || {};
            const grid = document.querySelector(selector);
            if (!grid) return { durationMs: 0, steps: 0, changedCells: 0, usable: false };

            const readAny = (object, camel, pascal, fallback) =>
                (object?.[camel] ?? object?.[pascal] ?? fallback);
            const activeRef = () => String(readAny(grid.__tmSpreadsheetCanvas?.model, "activeCellRef", "ActiveCellRef", ""));
            const cellCenter = (row, col) => {
                const engine = window.tmSpreadsheetCanvas;
                if (typeof engine?.getCellCenter === "function") {
                    const point = engine.getCellCenter(grid, row, col);
                    if (point && point.x >= 0) return point;
                }
                const state = grid.__tmSpreadsheetCanvas;
                const model = state?.model || {};
                const cell = state?.sheetState?.cellStore?.cells?.get(`${row}:${col}`);
                if (!cell) return null;
                const rowHeaderWidth = Number(readAny(model, "rowHeaderWidth", "RowHeaderWidth", 40));
                const columnHeaderHeight = Number(readAny(model, "columnHeaderHeight", "ColumnHeaderHeight", 20));
                return {
                    x: rowHeaderWidth + Number(readAny(cell, "left", "Left", 0))
                        - Number(readAny(model, "scrollLeft", "ScrollLeft", 0))
                        + Number(readAny(cell, "width", "Width", 0)) / 2,
                    y: columnHeaderHeight + Number(readAny(cell, "top", "Top", 0))
                        - Number(readAny(model, "scrollTop", "ScrollTop", 0))
                        + Number(readAny(cell, "height", "Height", 0)) / 2
                };
            };

            const dispatch = (target, type, clientX, clientY, buttons) => {
                target.dispatchEvent(new PointerEvent(type, {
                    bubbles: true,
                    cancelable: true,
                    composed: true,
                    pointerId: 1,
                    pointerType: "mouse",
                    isPrimary: true,
                    button: 0,
                    buttons,
                    clientX,
                    clientY
                }));
            };

            const steps = Math.max(1, Number(settings.steps) || 8);
            const startRow = Number(settings.startRow ?? 1);
            const startCol = Number(settings.startCol ?? 2);
            const endCol = Number(settings.endCol ?? 5);
            const settleFrames = Math.max(1, Number(settings.settleFrames) || 2);
            const rect = grid.getBoundingClientRect();
            const before = debugMetrics(grid);
            const start = performance.now();
            let changedCells = 0;
            let previous = activeRef();

            for (let index = 0; index < steps; index++) {
                const from = cellCenter(startRow + index, startCol);
                const to = cellCenter(startRow + index, endCol);
                if (!from || !to) continue;
                const target = document.elementFromPoint(rect.left + from.x, rect.top + from.y) || grid;
                dispatch(target, "pointerdown", rect.left + from.x, rect.top + from.y, 1);
                dispatch(target, "pointermove", rect.left + (from.x + to.x) / 2, rect.top + from.y, 1);
                dispatch(target, "pointermove", rect.left + to.x, rect.top + (from.y + to.y) / 2, 1);
                dispatch(target, "pointerup", rect.left + to.x, rect.top + (from.y + to.y) / 2, 0);
                for (let frameIndex = 0; frameIndex < settleFrames; frameIndex++) await frame();
                const current = activeRef();
                if (current && current !== previous) {
                    changedCells += 1;
                    previous = current;
                }
            }

            await frame();
            const durationMs = performance.now() - start;
            const after = debugMetrics(grid);
            const delta = name => (after[name] || 0) - (before[name] || 0);
            return {
                durationMs,
                steps,
                changedCells,
                usable: changedCells > 0,
                selectionPaintFrameCount: delta("selectionPaintFrameCount"),
                headerLayerPaintCount: delta("headerLayerPaintCount"),
                contentLayerPaintCount: delta("contentLayerPaintCount"),
                redrawCount: delta("redrawCount"),
                lastHeaderLayerMs: Number(after.lastHeaderLayerMs || 0),
                lastSelectionLayerMs: Number(after.lastSelectionLayerMs || 0)
            };
        }
    };
})();
